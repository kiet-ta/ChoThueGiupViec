using System.Reflection;
using CommonService.Application.Common.Models;
using CommonService.Application.Features.Payouts;
using CommonService.Application.Features.Payouts.Dtos;
using CommonService.Application.Features.Payouts.Services;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using CommonService.WebAPI.Controllers.Payouts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CommonService.Tests.Payouts;

/// <summary>BE-M6-04: build, read and confirm of the monthly payout batch (contract payouts.md 2.1, 2.2, 2.4) with an in-memory repository.</summary>
public class PayoutBatchServiceTests
{
    // 2026-11-10 11:00 Asia/Ho_Chi_Minh: October 2026 is finished, November is not.
    private static readonly DateTime Now = new(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc);
    private const int AdminId = 3;

    internal sealed class TestClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => local.AddHours(-7);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(utcNow));
    }

    private sealed class RecordingPublisher : IPublisher
    {
        public List<PayoutBatchClosed> Events { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (notification is PayoutBatchClosed e) Events.Add(e);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Publish((object)notification, cancellationToken);
    }

    private sealed class StubPenalties : IPayoutPenaltySource
    {
        public Dictionary<PayeeKey, decimal> Decided { get; } = [];
        public DateTime? AskedThrough { get; private set; }

        public Task<IReadOnlyDictionary<PayeeKey, decimal>> GetDecidedAsync(DateTime throughUtc, CancellationToken cancellationToken = default)
        {
            AskedThrough = throughUtc;
            return Task.FromResult<IReadOnlyDictionary<PayeeKey, decimal>>(Decided);
        }
    }

    private sealed class RollbackTrackingUnitOfWork : IUnitOfWork
    {
        public int Committed { get; private set; }
        public int RolledBack { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await action();
                Committed++;
                return result;
            }
            catch
            {
                RolledBack++;
                throw;
            }
        }
    }

    internal sealed class MemoryPayouts : IPayoutRepository
    {
        public List<PayoutBatch> Batches { get; } = [];
        public Dictionary<int, List<NewPayoutItem>> Stored { get; } = [];
        public List<PayoutAssignmentRow> Payable { get; } = [];
        public Dictionary<PayeeKey, decimal> Applied { get; } = [];
        public Dictionary<PayeeKey, string> Banks { get; } = [];
        public Dictionary<int, string> ItemStatus { get; } = [];
        public bool LockWorks { get; set; } = true;
        public bool ClaimLost { get; set; }
        public int Replaces { get; private set; }
        public (DateTime From, DateTime To, int BatchId)? AskedRange { get; private set; }
        public string? AskedAppliedBefore { get; private set; }
        public Action? BeforeClose { get; set; }
        public (int PageSize, PayeeType? Type)? LastItems { get; private set; }
        private int _next = 1;

        public Task<bool> TryLockPeriodAsync(string periodMonth, CancellationToken cancellationToken = default) => Task.FromResult(LockWorks);

        public Task<PayoutBatch?> FindByPeriodAsync(string periodMonth, CancellationToken cancellationToken = default) =>
            Task.FromResult(Batches.FirstOrDefault(b => b.PeriodMonth == periodMonth));

        public Task<PayoutBatch?> GetBatchAsync(int batchId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Batches.FirstOrDefault(b => b.BatchId == batchId));

        public Task<PayoutBatch> AddDraftAsync(string periodMonth, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var batch = new PayoutBatch { BatchId = _next++, PeriodMonth = periodMonth, BatchStatus = "DRAFT", CreatedAt = nowUtc };
            Batches.Add(batch);
            return Task.FromResult(batch);
        }

        public Task<IReadOnlyList<PayoutAssignmentRow>> GetPayableAsync(DateTime fromUtc, DateTime toUtc, int batchId, CancellationToken cancellationToken = default)
        {
            AskedRange = (fromUtc, toUtc, batchId);
            return Task.FromResult<IReadOnlyList<PayoutAssignmentRow>>(Payable.ToList());
        }

        public Task<IReadOnlyDictionary<PayeeKey, decimal>> GetAppliedPenaltiesBeforeAsync(string periodMonth, CancellationToken cancellationToken = default)
        {
            AskedAppliedBefore = periodMonth;
            return Task.FromResult<IReadOnlyDictionary<PayeeKey, decimal>>(Applied);
        }

        public Task<IReadOnlyDictionary<PayeeKey, string>> GetBankAccountsAsync(IReadOnlyCollection<PayeeKey> payees, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<PayeeKey, string>>(Banks);

        public Task ReplaceItemsAsync(int batchId, IReadOnlyList<NewPayoutItem> items, decimal totalAmount, CancellationToken cancellationToken = default)
        {
            Replaces++;
            if (ClaimLost) throw new PayoutClaimLostException(1);
            Stored[batchId] = items.ToList();
            Batches.First(b => b.BatchId == batchId).TotalAmount = totalAmount;
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<PayoutBatch> Items, int Total)> ListBatchesAsync(int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var all = Batches.OrderByDescending(b => b.PeriodMonth).ToList();
            return Task.FromResult<(IReadOnlyList<PayoutBatch>, int)>((all.Skip((page - 1) * pageSize).Take(pageSize).ToList(), all.Count));
        }

        public Task<IReadOnlyDictionary<int, int>> CountItemsAsync(IReadOnlyCollection<int> batchIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, int>>(batchIds.Where(Stored.ContainsKey).ToDictionary(id => id, id => Stored[id].Count));

        public Task<(IReadOnlyList<PayoutItemView> Items, int Total)> GetItemsAsync(int batchId, PayeeType? payeeType, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            LastItems = (pageSize, payeeType);
            var items = Stored.GetValueOrDefault(batchId) ?? [];
            var matching = items.Where(i => payeeType is null || i.PayeeType == payeeType).ToList();
            var views = matching.Skip((page - 1) * pageSize).Take(pageSize).Select((n, index) => new PayoutItemView(
                new PayoutItem
                {
                    ItemId = index + 1,
                    BatchId = batchId,
                    WorkerId = n.WorkerId,
                    AgencyId = n.AgencyId,
                    PayeeType = n.PayeeType,
                    JobCount = n.JobCount,
                    GrossAmount = n.GrossAmount,
                    CommissionAmount = n.CommissionAmount,
                    PenaltyAmount = n.PenaltyAmount,
                    NetAmount = n.NetAmount,
                    BankAccountNo = n.BankAccountNo,
                    ItemStatus = ItemStatus.GetValueOrDefault(batchId) ?? "PENDING",
                },
                n.PayeeType == PayeeType.Freelancer ? $"Worker {n.WorkerId}" : $"Agency {n.AgencyId}", "ACB")).ToList();
            return Task.FromResult<(IReadOnlyList<PayoutItemView>, int)>((views, matching.Count));
        }

        public Dictionary<PayeeKey, (string Name, string? Bank)> Names { get; } = [];
        public List<PayoutDetailRow> Details { get; } = [];

        public Task<IReadOnlyList<PayoutItemView>> GetAllItemsAsync(int batchId, PayeeType payeeType, CancellationToken cancellationToken = default)
        {
            var items = (Stored.GetValueOrDefault(batchId) ?? []).Where(i => i.PayeeType == payeeType).Select((n, index) =>
            {
                var key = new PayeeKey(n.PayeeType, n.PayeeType == PayeeType.Freelancer ? n.WorkerId!.Value : n.AgencyId!.Value);
                var name = Names.GetValueOrDefault(key, ($"Payee {key.Id}", "ACB"));
                return new PayoutItemView(
                    new PayoutItem
                    {
                        ItemId = index + 1,
                        BatchId = batchId,
                        WorkerId = n.WorkerId,
                        AgencyId = n.AgencyId,
                        PayeeType = n.PayeeType,
                        JobCount = n.JobCount,
                        GrossAmount = n.GrossAmount,
                        CommissionAmount = n.CommissionAmount,
                        PenaltyAmount = n.PenaltyAmount,
                        NetAmount = n.NetAmount,
                        BankAccountNo = n.BankAccountNo,
                        ItemStatus = "PENDING",
                    },
                    name.Item1, name.Item2);
            }).ToList();
            return Task.FromResult<IReadOnlyList<PayoutItemView>>(items);
        }

        public Task<IReadOnlyList<PayoutDetailRow>> GetAgencyDetailAsync(int batchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PayoutDetailRow>>(Details.ToList());

        public Task SetExportUrlAsync(int batchId, string? url, CancellationToken cancellationToken = default)
        {
            Batches.First(b => b.BatchId == batchId).ExportFileUrl = url;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetPayeesWithoutBankAsync(int batchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>((Stored.GetValueOrDefault(batchId) ?? [])
                .Where(i => i.BankAccountNo.Length == 0)
                .Select(n => n.PayeeType == PayeeType.Freelancer ? $"Worker {n.WorkerId}" : $"Agency {n.AgencyId}").ToList());

        public Task<bool> TryCloseAsync(int batchId, int adminId, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            BeforeClose?.Invoke();
            var batch = Batches.First(b => b.BatchId == batchId);
            if (batch.BatchStatus != "DRAFT") return Task.FromResult(false);
            batch.BatchStatus = "CLOSED";
            batch.ConfirmedBy = adminId;
            batch.ConfirmedAt = nowUtc;
            ItemStatus[batchId] = "TRANSFERRED";
            return Task.FromResult(true);
        }
    }

    private sealed class Harness
    {
        public MemoryPayouts Repo { get; } = new();
        public StubPenalties Penalties { get; } = new();
        public FakeAuditLog Audit { get; } = new();
        public RollbackTrackingUnitOfWork Uow { get; } = new();
        public RecordingPublisher Publisher { get; } = new();

        public PayoutBatchService Service() => new(Repo, Penalties, Audit, Uow, new TestClock(Now), Publisher);

        public Harness WithPayable(params PayoutAssignmentRow[] rows)
        {
            Repo.Payable.AddRange(rows);
            return this;
        }

        public static PayoutAssignmentRow Done(long id, int worker, decimal gross, decimal rate = 0.200m, int? agency = null) =>
            new(id, worker, agency, JobAssignmentStatus.Completed, gross, rate, null);
    }

    // ---- build: validation ----------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-13")]
    [InlineData("2026-1")]
    [InlineData("10/2026")]
    public async Task A_period_that_is_not_YYYY_MM_is_a_400(string? period)
    {
        var h = new Harness();

        var result = await h.Service().BuildAsync(period);

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("periodMonth", result.ValidationErrors!.Keys);
        Assert.Equal(0, h.Repo.Replaces);
    }

    [Theory]
    [InlineData("2026-11")] // the month that is still running (today is 2026-11-10 in Asia/Ho_Chi_Minh)
    [InlineData("2026-12")]
    [InlineData("2030-01")]
    public async Task A_month_that_is_not_over_cannot_be_built(string period)
    {
        var h = new Harness();

        var result = await h.Service().BuildAsync(period);

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("periodMonth", result.ValidationErrors!.Keys);
        Assert.Empty(h.Repo.Batches);
    }

    [Fact]
    public async Task The_month_is_decided_by_the_Asia_Ho_Chi_Minh_calendar_not_UTC()
    {
        // 2026-10-31 20:00 UTC is already 2026-11-01 03:00 in Ho Chi Minh: October is over there, but the UTC date is still October.
        var h = new Harness();
        var lateOctoberUtc = new DateTime(2026, 10, 31, 20, 0, 0, DateTimeKind.Utc);
        var service = new PayoutBatchService(h.Repo, h.Penalties, h.Audit, h.Uow, new TestClock(lateOctoberUtc), h.Publisher);

        var october = await service.BuildAsync("2026-10");
        var september = await service.BuildAsync("2026-09");

        Assert.Equal(201, october.StatusCode);
        Assert.Equal(201, september.StatusCode);
    }

    [Fact]
    public async Task The_assignments_are_read_for_the_month_in_Ho_Chi_Minh_time_converted_to_UTC()
    {
        var h = new Harness();

        await h.Service().BuildAsync("2026-10");

        var range = h.Repo.AskedRange!.Value;
        Assert.Equal(new DateTime(2026, 9, 30, 17, 0, 0), range.From); // 2026-10-01 00:00 +07:00
        Assert.Equal(new DateTime(2026, 10, 31, 17, 0, 0), range.To); // 2026-11-01 00:00 +07:00
        Assert.Equal(1, range.BatchId);
        Assert.Equal(range.To, h.Penalties.AskedThrough); // deductions decided before the month end
        Assert.Equal("2026-10", h.Repo.AskedAppliedBefore);
    }

    // ---- build: effects -------------------------------------------------------------------------

    [Fact]
    public async Task A_new_month_is_a_201_and_stores_one_item_per_payee_with_the_bank_account_and_the_total()
    {
        var h = new Harness().WithPayable(
            Harness.Done(1, 5, 260000m), Harness.Done(2, 5, 100000m), Harness.Done(3, 8, 260000m, 0.15m, agency: 3));
        h.Repo.Banks[new PayeeKey(PayeeType.Freelancer, 5)] = "111222";
        h.Repo.Banks[new PayeeKey(PayeeType.Agency, 3)] = "999000";

        var result = await h.Service().BuildAsync(" 2026-10 ");

        Assert.True(result.Success);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal("2026-10", result.Data!.PeriodMonth);
        Assert.Equal("DRAFT", result.Data.BatchStatus);
        Assert.Equal(2, result.Data.ItemCount);
        Assert.Equal(288000m + 221000m, result.Data.TotalAmount);
        Assert.Equal(Now, result.Data.CreatedAt);

        var items = h.Repo.Stored[result.Data.BatchId];
        Assert.Equal(PayeeType.Freelancer, items[0].PayeeType);
        Assert.Equal(5, items[0].WorkerId);
        Assert.Null(items[0].AgencyId);
        Assert.Equal("111222", items[0].BankAccountNo);
        Assert.Equal([1L, 2L], items[0].AssignmentIds.ToArray());
        Assert.Equal(PayeeType.Agency, items[1].PayeeType);
        Assert.Null(items[1].WorkerId);
        Assert.Equal(3, items[1].AgencyId);
        Assert.Equal("999000", items[1].BankAccountNo);
        Assert.Equal(1, h.Uow.Committed);
    }

    [Fact]
    public async Task A_payee_without_bank_data_is_still_listed_with_an_empty_account()
    {
        var h = new Harness().WithPayable(Harness.Done(1, 5, 260000m));

        var result = await h.Service().BuildAsync("2026-10");

        Assert.Equal(string.Empty, h.Repo.Stored[result.Data!.BatchId][0].BankAccountNo);
    }

    [Fact]
    public async Task Building_the_same_month_again_rebuilds_the_DRAFT_and_answers_200_with_the_same_batch_id()
    {
        var h = new Harness().WithPayable(Harness.Done(1, 5, 260000m));
        var first = await h.Service().BuildAsync("2026-10");

        h.Repo.Payable.Add(Harness.Done(2, 5, 100000m)); // a late assignment appeared
        var second = await h.Service().BuildAsync("2026-10");

        Assert.Equal(201, first.StatusCode);
        Assert.Equal(200, second.StatusCode);
        Assert.Equal(first.Data!.BatchId, second.Data!.BatchId);
        Assert.Single(h.Repo.Batches);
        Assert.Equal(2, h.Repo.Replaces);
        Assert.Equal(208000m + 80000m, second.Data.TotalAmount);
        Assert.Equal(first.Data.BatchId, h.Repo.AskedRange!.Value.BatchId); // its own assignments count again
    }

    [Fact]
    public async Task A_CLOSED_batch_is_a_409_and_is_never_changed()
    {
        var h = new Harness().WithPayable(Harness.Done(1, 5, 260000m));
        var built = await h.Service().BuildAsync("2026-10");
        await h.Service().ConfirmAsync(AdminId, built.Data!.BatchId);
        h.Repo.Payable.Add(Harness.Done(2, 5, 100000m));

        var result = await h.Service().BuildAsync("2026-10");

        Assert.Equal(409, result.StatusCode);
        Assert.Equal(1, h.Repo.Replaces);
        Assert.Equal(208000m, h.Repo.Batches[0].TotalAmount);
    }

    [Fact]
    public async Task The_pending_penalties_decided_and_already_applied_reach_the_calculation()
    {
        var h = new Harness().WithPayable(Harness.Done(1, 5, 260000m));
        h.Penalties.Decided[new PayeeKey(PayeeType.Freelancer, 5)] = 80000m;
        h.Repo.Applied[new PayeeKey(PayeeType.Freelancer, 5)] = 30000m;

        var result = await h.Service().BuildAsync("2026-10");

        var item = Assert.Single(h.Repo.Stored[result.Data!.BatchId]);
        Assert.Equal(50000m, item.PenaltyAmount);
        Assert.Equal(158000m, item.NetAmount);
        Assert.Equal(158000m, result.Data.TotalAmount);
    }

    [Fact]
    public async Task A_month_without_assignments_builds_an_empty_batch()
    {
        var result = await new Harness().Service().BuildAsync("2026-10");

        Assert.Equal(201, result.StatusCode);
        Assert.Equal(0, result.Data!.ItemCount);
        Assert.Equal(0m, result.Data.TotalAmount);
    }

    [Fact]
    public async Task When_the_month_lock_cannot_be_taken_the_answer_is_a_409_and_nothing_is_stored()
    {
        var h = new Harness().WithPayable(Harness.Done(1, 5, 260000m));
        h.Repo.LockWorks = false;

        var result = await h.Service().BuildAsync("2026-10");

        Assert.Equal(409, result.StatusCode);
        Assert.Empty(h.Repo.Batches);
        Assert.Equal(1, h.Uow.RolledBack);
    }

    [Fact]
    public async Task When_an_assignment_was_paid_by_another_batch_meanwhile_the_build_is_a_409_and_undone()
    {
        var h = new Harness().WithPayable(Harness.Done(1, 5, 260000m));
        h.Repo.ClaimLost = true;

        var result = await h.Service().BuildAsync("2026-10");

        Assert.Equal(409, result.StatusCode);
        Assert.Equal(1, h.Uow.RolledBack);
        Assert.Equal(0, h.Uow.Committed);
    }

    // ---- read -----------------------------------------------------------------------------------

    [Fact]
    public async Task The_list_is_newest_month_first_with_the_item_counts_and_validates_paging()
    {
        var h = new Harness().WithPayable(Harness.Done(1, 5, 260000m));
        await h.Service().BuildAsync("2026-09");
        await h.Service().BuildAsync("2026-10");

        var result = await h.Service().ListAsync(null, null);

        Assert.Equal(["2026-10", "2026-09"], result.Data!.Items.Select(b => b.PeriodMonth).ToArray());
        Assert.Equal(1, result.Data.Items[0].ItemCount);
        Assert.Equal((1, 20, 2), (result.Data.Page, result.Data.PageSize, result.Data.Total));

        Assert.Equal(400, (await h.Service().ListAsync("0", null)).StatusCode);
        Assert.Equal(400, (await h.Service().ListAsync(null, "101")).StatusCode);
        Assert.Equal(400, (await h.Service().ListAsync("x", null)).StatusCode);
    }

    [Fact]
    public async Task The_detail_has_the_items_a_payee_type_filter_and_a_warning_for_each_payee_without_a_bank_account()
    {
        var h = new Harness().WithPayable(Harness.Done(1, 5, 260000m), Harness.Done(2, 6, 260000m), Harness.Done(3, 8, 100000m, 0.15m, agency: 3));
        h.Repo.Banks[new PayeeKey(PayeeType.Freelancer, 5)] = "111";
        h.Repo.Banks[new PayeeKey(PayeeType.Agency, 3)] = "333";
        var built = await h.Service().BuildAsync("2026-10");

        var all = await h.Service().GetAsync(built.Data!.BatchId, null, null, null);
        var agencyOnly = await h.Service().GetAsync(built.Data.BatchId, "agency", null, null);

        Assert.Equal(3, all.Data!.Total);
        Assert.Equal(3, all.Data.Batch.ItemCount);
        Assert.Equal(["Worker 6 has no bank account number."], all.Data.Warnings.ToArray());
        Assert.Equal("Worker 5", all.Data.Items[0].PayeeName);
        Assert.Equal("FREELANCER", all.Data.Items[0].PayeeType);
        Assert.Equal("ACB", all.Data.Items[0].BankName);
        var agency = Assert.Single(agencyOnly.Data!.Items);
        Assert.Equal("AGENCY", agency.PayeeType);
        Assert.Equal(PayeeType.Agency, h.Repo.LastItems!.Value.Type);
    }

    [Theory]
    [InlineData("WORKER", null, null, "payeeType")]
    [InlineData(null, "0", null, "page")]
    [InlineData(null, null, "101", "pageSize")]
    public async Task Bad_detail_query_values_are_a_400(string? type, string? page, string? pageSize, string field)
    {
        var h = new Harness();
        var built = await h.Service().BuildAsync("2026-10");

        var result = await h.Service().GetAsync(built.Data!.BatchId, type, page, pageSize);

        Assert.Equal(400, result.StatusCode);
        Assert.Contains(field, result.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task A_missing_batch_is_a_404()
    {
        Assert.Equal(404, (await new Harness().Service().GetAsync(99, null, null, null)).StatusCode);
        Assert.Equal(404, (await new Harness().Service().ConfirmAsync(AdminId, 99)).StatusCode);
    }

    // ---- confirm --------------------------------------------------------------------------------

    [Fact]
    public async Task Confirming_closes_the_batch_marks_the_items_audits_once_and_publishes_PayoutBatchClosed()
    {
        var h = new Harness().WithPayable(Harness.Done(1, 5, 260000m), Harness.Done(2, 6, 100000m));
        var built = await h.Service().BuildAsync("2026-10");

        var result = await h.Service().ConfirmAsync(AdminId, built.Data!.BatchId);

        Assert.True(result.Success);
        Assert.Equal("CLOSED", result.Data!.BatchStatus);
        Assert.Equal(AdminId, result.Data.ConfirmedBy);
        Assert.Equal(Now, result.Data.ConfirmedAt);
        Assert.Equal(2, result.Data.ItemCount);

        var detail = await h.Service().GetAsync(built.Data.BatchId, null, null, null);
        Assert.All(detail.Data!.Items, i => Assert.Equal("TRANSFERRED", i.ItemStatus));

        var row = Assert.Single(h.Audit.Entries);
        Assert.Equal(AuditActorType.Admin, row.ActorType);
        Assert.Equal(AdminId, row.AdminId);
        Assert.Equal("PAYOUT_BATCH", row.EntityType);
        Assert.Equal(built.Data.BatchId.ToString(), row.EntityId);
        Assert.Equal("batch_status", row.FieldName);
        Assert.Equal("DRAFT", row.OldValue);
        Assert.Equal("CLOSED", row.NewValue);
        Assert.Equal("Disbursement confirmed for 2026-10: 2 items, total 288000", row.Reason);

        var published = Assert.Single(h.Publisher.Events);
        Assert.Equal(built.Data.BatchId, published.BatchId);
        Assert.Equal("2026-10", published.PeriodMonth);
        Assert.Equal(288000m, published.TotalAmount);
        Assert.Equal(2, published.ItemCount);
        Assert.Equal(Now, published.ClosedAtUtc);
    }

    [Fact]
    public async Task A_second_confirm_is_a_409_and_changes_nothing()
    {
        var h = new Harness().WithPayable(Harness.Done(1, 5, 260000m));
        var built = await h.Service().BuildAsync("2026-10");
        await h.Service().ConfirmAsync(AdminId, built.Data!.BatchId);

        var second = await h.Service().ConfirmAsync(AdminId + 1, built.Data.BatchId);

        Assert.Equal(409, second.StatusCode);
        Assert.Equal(AdminId, h.Repo.Batches[0].ConfirmedBy);
        Assert.Single(h.Audit.Entries);
        Assert.Single(h.Publisher.Events);
    }

    [Fact]
    public async Task An_empty_batch_cannot_be_confirmed()
    {
        var h = new Harness();
        var built = await h.Service().BuildAsync("2026-10");

        var result = await h.Service().ConfirmAsync(AdminId, built.Data!.BatchId);

        Assert.Equal(409, result.StatusCode);
        Assert.Equal("DRAFT", h.Repo.Batches[0].BatchStatus);
        Assert.Empty(h.Publisher.Events);
    }

    [Fact]
    public async Task A_batch_whose_month_is_not_over_cannot_be_confirmed()
    {
        // The month was over when the batch was built; here the clock of the confirming call is earlier than the end of the month.
        var h = new Harness().WithPayable(Harness.Done(1, 5, 260000m));
        var built = await h.Service().BuildAsync("2026-10");
        var early = new PayoutBatchService(h.Repo, h.Penalties, h.Audit, h.Uow, new TestClock(new DateTime(2026, 10, 20, 4, 0, 0, DateTimeKind.Utc)), h.Publisher);

        var result = await early.ConfirmAsync(AdminId, built.Data!.BatchId);

        Assert.Equal(409, result.StatusCode);
        Assert.Contains("not over", result.ErrorMessage);
    }

    [Fact]
    public async Task Losing_the_close_to_another_admin_is_a_409_with_no_audit_or_event()
    {
        var h = new Harness().WithPayable(Harness.Done(1, 5, 260000m));
        var built = await h.Service().BuildAsync("2026-10");
        h.Repo.BeforeClose = () => h.Repo.Batches[0].BatchStatus = "CLOSED"; // the other admin got there between our read and the claim

        var result = await h.Service().ConfirmAsync(AdminId, built.Data!.BatchId);

        Assert.Equal(409, result.StatusCode);
        Assert.Empty(h.Audit.Entries);
        Assert.Empty(h.Publisher.Events);
    }

    // ---- controller -----------------------------------------------------------------------------

    private sealed class FakeUser(int? id) : ICurrentUser
    {
        public bool IsAuthenticated => id is not null;
        public int? UserId => id;
        public UserRole? Role => null;
    }

    private sealed class StubBatches(int status) : IPayoutBatchService
    {
        public int? ConfirmedBy { get; private set; }
        public string? BuiltPeriod { get; private set; }

        private PayoutResult<T> Answer<T>(T data) => status switch
        {
            200 => PayoutResult<T>.Ok(data),
            201 => PayoutResult<T>.Created(data),
            400 => PayoutResult<T>.ValidationError(new Dictionary<string, string[]> { ["periodMonth"] = ["bad"] }),
            404 => PayoutResult<T>.NotFound(),
            _ => PayoutResult<T>.Conflict("closed"),
        };

        public Task<PayoutResult<PayoutBatchDto>> BuildAsync(string? periodMonth, CancellationToken cancellationToken = default)
        {
            BuiltPeriod = periodMonth;
            return Task.FromResult(Answer(new PayoutBatchDto()));
        }

        public Task<PayoutResult<PayoutBatchPageDto>> ListAsync(string? page, string? pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(Answer(new PayoutBatchPageDto()));

        public Task<PayoutResult<PayoutBatchDetailDto>> GetAsync(int batchId, string? payeeType, string? page, string? pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(Answer(new PayoutBatchDetailDto()));

        public Task<PayoutResult<PayoutBatchDto>> ConfirmAsync(int adminId, int batchId, CancellationToken cancellationToken = default)
        {
            ConfirmedBy = adminId;
            return Task.FromResult(Answer(new PayoutBatchDto()));
        }
    }

    [Fact]
    public void The_controller_is_admin_only_with_the_contract_route_and_actions()
    {
        var type = typeof(AdminPayoutBatchesController);
        Assert.Equal("AdminOnly", type.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal("api/admin/payout-batches", type.GetCustomAttribute<RouteAttribute>()!.Template);

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.All(methods, m => Assert.Null(m.GetCustomAttribute<AllowAnonymousAttribute>()));
        var routes = methods
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(a => $"{a.HttpMethods.Single()} {a.Template}".Trim()))
            .Order().ToArray();
        Assert.Equal(["GET", "GET {batchId:int}", "POST", "POST {batchId:int}/confirm"], routes);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(400)]
    [InlineData(409)]
    public async Task Build_passes_the_service_status_through_and_keeps_the_errors_map(int status)
    {
        var service = new StubBatches(status);
        var controller = new AdminPayoutBatchesController(service, new FakeUser(7));

        var result = Assert.IsType<ObjectResult>(await controller.Build(new BuildPayoutBatchRequestDto { PeriodMonth = "2026-10" }, default));

        Assert.Equal(status, result.StatusCode);
        Assert.Equal("2026-10", service.BuiltPeriod);
        if (status == 400)
        {
            Assert.Contains("errors", Assert.IsType<ApiResponse<object>>(result.Value).Data!.GetType().GetProperties().Select(p => p.Name));
        }
    }

    [Theory]
    [InlineData(200)]
    [InlineData(404)]
    [InlineData(409)]
    public async Task Confirm_takes_the_admin_id_from_the_token_and_maps_the_status(int status)
    {
        var service = new StubBatches(status);

        var result = Assert.IsType<ObjectResult>(await new AdminPayoutBatchesController(service, new FakeUser(7)).Confirm(5, default));

        Assert.Equal(status, result.StatusCode);
        Assert.Equal(7, service.ConfirmedBy);
    }

    [Fact]
    public async Task Without_a_user_id_confirm_is_a_401_and_a_missing_build_body_is_a_400_before_the_service_is_called()
    {
        var service = new StubBatches(200);
        Assert.IsType<UnauthorizedObjectResult>(await new AdminPayoutBatchesController(service, new FakeUser(null)).Confirm(5, default));
        Assert.IsType<BadRequestObjectResult>(await new AdminPayoutBatchesController(service, new FakeUser(7)).Build(null!, default));
        Assert.Null(service.ConfirmedBy);
        Assert.Null(service.BuiltPeriod);
    }
}

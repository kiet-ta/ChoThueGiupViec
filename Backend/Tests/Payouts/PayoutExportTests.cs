using System.Reflection;
using CommonService.Application.Common.Models;
using CommonService.Application.Features.Payouts;
using CommonService.Application.Features.Payouts.Services;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Admin;
using CommonService.Infrastructure.Modules.Disputes;
using CommonService.Infrastructure.Modules.Payouts;
using CommonService.Infrastructure.Persistence;
using CommonService.WebAPI.Controllers.Payouts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Tests.Payouts;

/// <summary>BE-M6-05: the three bank transfer files of a batch (decision Q18, contract payouts.md 2.3 and question P5).</summary>
public class PayoutExportTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc);

    private sealed class Rig
    {
        public PayoutBatchServiceTests.MemoryPayouts Repo { get; } = new();
        public FakeFileStorage Storage { get; } = new();
        public int BatchId { get; private set; }

        public PayoutExportService Service() => new(Repo, Storage, new PayoutBatchServiceTests.TestClock(Now));

        /// <summary>A batch of 2026-10 with the given payable rows, built through the real batch service.</summary>
        public async Task<Rig> BuildAsync(params PayoutAssignmentRow[] rows)
        {
            Repo.Payable.AddRange(rows);
            var batches = new PayoutBatchService(Repo, new NoPenalties(), new FakeAuditLog(), new NoopUnitOfWork(), new PayoutBatchServiceTests.TestClock(Now), new NullPublisher());
            BatchId = (await batches.BuildAsync("2026-10")).Data!.BatchId;
            return this;
        }
    }

    private sealed class NoPenalties : IPayoutPenaltySource
    {
        public Task<IReadOnlyDictionary<PayeeKey, decimal>> GetDecidedAsync(DateTime throughUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<PayeeKey, decimal>>(new Dictionary<PayeeKey, decimal>());
    }

    private sealed class NoopUnitOfWork : CommonService.Application.Interfaces.IRepositories.IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default) => action();
    }

    private sealed class NullPublisher : MediatR.IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : MediatR.INotification => Task.CompletedTask;
    }

    private static PayoutAssignmentRow Done(long id, int worker, decimal gross, decimal rate = 0.200m, int? agency = null) =>
        new(id, worker, agency, JobAssignmentStatus.Completed, gross, rate, null);

    // ---- validation -----------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("csv")]
    [InlineData("agency")]
    public async Task A_missing_or_unknown_type_is_a_400_and_nothing_is_stored(string? type)
    {
        var rig = await new Rig().BuildAsync(Done(1, 5, 260000m));

        var result = await rig.Service().ExportAsync(rig.BatchId, type);

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("type", result.ValidationErrors!.Keys);
        Assert.Equal(0, rig.Storage.Count);
    }

    [Fact]
    public async Task A_missing_batch_is_a_404()
    {
        var result = await new Rig().Service().ExportAsync(99, "freelancer");

        Assert.Equal(404, result.StatusCode);
    }

    // ---- the three files ------------------------------------------------------------------------

    [Fact]
    public async Task The_freelancer_file_has_the_Q18_columns_one_row_per_payee_with_net_above_zero_and_the_account_as_text()
    {
        var rig = new Rig();
        rig.Repo.Names[new PayeeKey(PayeeType.Freelancer, 5)] = ("Nguyễn Văn A", "ACB");
        rig.Repo.Names[new PayeeKey(PayeeType.Freelancer, 6)] = ("Trần Thị B", null);
        rig.Repo.Names[new PayeeKey(PayeeType.Freelancer, 7)] = ("Lê C", "VCB");
        rig.Repo.Banks[new PayeeKey(PayeeType.Freelancer, 5)] = "0012345678";
        rig.Repo.Banks[new PayeeKey(PayeeType.Freelancer, 7)] = "999";
        await rig.BuildAsync(Done(1, 5, 260000m), Done(2, 6, 100000m), Done(3, 7, 260000m, agency: null));
        rig.Repo.Stored[rig.BatchId][2] = rig.Repo.Stored[rig.BatchId][2] with { NetAmount = 0m }; // a penalty took the whole month

        var result = await rig.Service().ExportAsync(rig.BatchId, "freelancer");

        Assert.True(result.Success);
        var file = result.Data!;
        Assert.Equal("payout-2026-10-freelancer.xlsx", file.FileName);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);

        var table = XlsxTestReader.Table(file.Content, 5);
        Assert.Equal(["full_name", "bank_name", "bank_account_no", "net_amount", "transfer_note"], table[0]);
        Assert.Equal(3, table.Count); // header + payee 5 + payee 6; payee 7 has nothing to transfer
        Assert.Equal(["Nguyễn Văn A", "ACB", "0012345678", "208000", "Thanh toan ChoThueGiupViec 2026-10"], table[1]);
        Assert.Equal(["Trần Thị B", null, "", "80000", "Thanh toan ChoThueGiupViec 2026-10"], table[2]); // no bank: still listed (P4)
        Assert.Equal("inlineStr", XlsxTestReader.Rows(file.Content)[1][2].Type); // the account number is text
        Assert.Equal("2", XlsxTestReader.Rows(file.Content)[1][3].Style); // the amount is a number
        Assert.Equal("Freelancer", XlsxTestReader.SheetName(file.Content));
    }

    [Fact]
    public async Task The_freelancer_file_leaves_out_the_agencies_and_the_summary_leaves_out_the_freelancers()
    {
        var rig = new Rig();
        rig.Repo.Names[new PayeeKey(PayeeType.Agency, 3)] = ("Agency Co", "VCB");
        rig.Repo.Banks[new PayeeKey(PayeeType.Agency, 3)] = "777";
        await rig.BuildAsync(Done(1, 5, 260000m), Done(2, 8, 200000m, 0.15m, agency: 3), Done(3, 9, 100000m, 0.15m, agency: 3));

        var freelancer = XlsxTestReader.Table((await rig.Service().ExportAsync(rig.BatchId, "freelancer")).Data!.Content, 5);
        var summary = XlsxTestReader.Table((await rig.Service().ExportAsync(rig.BatchId, "agency-summary")).Data!.Content, 9);

        Assert.Equal(2, freelancer.Count); // header + the one freelancer
        Assert.Equal(2, summary.Count); // header + the one agency
        Assert.Equal(
            ["agency_name", "bank_name", "bank_account_no", "job_count", "gross_amount", "commission_amount", "penalty_amount", "net_amount", "transfer_note"],
            summary[0]);
        Assert.Equal(["Agency Co", "VCB", "777", "2", "300000", "45000", "0", "255000", "Thanh toan ChoThueGiupViec 2026-10"], summary[1]);
    }

    [Fact]
    public async Task The_detail_file_has_one_row_per_assignment_with_the_commission_recomputed_the_absence_fee_without_commission_and_local_time()
    {
        var rig = new Rig();
        await rig.BuildAsync(Done(1, 8, 260000m, 0.15m, agency: 3));
        rig.Repo.Details.AddRange(
        [
            new PayoutDetailRow("Agency Co", 11, 111, "Worker One", JobAssignmentStatus.Completed, 260000m, 0.150m, null, new DateTime(2026, 10, 5, 7, 30, 0, DateTimeKind.Utc)),
            new PayoutDetailRow("Agency Co", 12, 112, "Worker Two", JobAssignmentStatus.Absent, 260000m, 0.150m, 104000m, new DateTime(2026, 10, 6, 17, 30, 0, DateTimeKind.Utc)),
        ]);

        var result = await rig.Service().ExportAsync(rig.BatchId, "Agency-Detail");

        Assert.Equal("payout-2026-10-agency-detail.xlsx", result.Data!.FileName);
        var table = XlsxTestReader.Table(result.Data.Content, 8);
        Assert.Equal(["agency_name", "assignment_id", "order_id", "completed_at", "worker_name", "gross_amount", "commission_amount", "net_amount"], table[0]);
        Assert.Equal(["Agency Co", "11", "111", "2026-10-05T14:30:00+07:00", "Worker One", "260000", "39000", "221000"], table[1]);
        // the absence fee: paid in full, no commission, dated 2026-10-07 00:30 in Ho Chi Minh
        Assert.Equal(["Agency Co", "12", "112", "2026-10-07T00:30:00+07:00", "Worker Two", "104000", "0", "104000"], table[2]);
        Assert.Equal("inlineStr", XlsxTestReader.Rows(result.Data.Content)[1][3].Type); // the date is text, not an Excel serial
        Assert.Null(XlsxTestReader.Rows(result.Data.Content)[1][1].Type); // the id is a plain number
    }

    [Fact]
    public async Task A_batch_without_payees_gives_a_file_with_only_the_header()
    {
        var rig = await new Rig().BuildAsync();

        foreach (var type in PayoutExportService.Types)
        {
            var result = await rig.Service().ExportAsync(rig.BatchId, type);

            Assert.True(result.Success);
            Assert.Single(XlsxTestReader.Rows(result.Data!.Content));
        }
    }

    [Fact]
    public async Task Names_with_special_characters_survive_into_the_file()
    {
        var rig = new Rig();
        rig.Repo.Names[new PayeeKey(PayeeType.Freelancer, 5)] = ("Anh & \"Em\" <Co>", "ACB");
        await rig.BuildAsync(Done(1, 5, 260000m));

        var table = XlsxTestReader.Table((await rig.Service().ExportAsync(rig.BatchId, "freelancer")).Data!.Content, 5);

        Assert.Equal("Anh & \"Em\" <Co>", table[1][0]);
    }

    // ---- storage --------------------------------------------------------------------------------

    [Fact]
    public async Task The_file_is_stored_through_IFileStorage_and_its_url_is_written_to_the_batch()
    {
        var rig = await new Rig().BuildAsync(Done(1, 5, 260000m));

        var result = await rig.Service().ExportAsync(rig.BatchId, "freelancer");

        Assert.Equal(1, rig.Storage.Count);
        var url = rig.Repo.Batches[0].ExportFileUrl!;
        Assert.StartsWith("/files/payouts/", url);
        Assert.EndsWith("payout-2026-10-freelancer.xlsx", url);
        var stored = await rig.Storage.OpenReadAsync(url["/files/".Length..]);
        Assert.NotNull(stored);
        using var copy = new MemoryStream();
        await stored!.CopyToAsync(copy);
        Assert.Equal(result.Data!.Content, copy.ToArray()); // what was stored is what was downloaded
    }

    [Fact]
    public async Task A_new_export_replaces_the_previously_stored_file_so_files_do_not_pile_up()
    {
        var rig = await new Rig().BuildAsync(Done(1, 5, 260000m));

        await rig.Service().ExportAsync(rig.BatchId, "freelancer");
        var first = rig.Repo.Batches[0].ExportFileUrl!;
        await rig.Service().ExportAsync(rig.BatchId, "agency-summary");
        var second = rig.Repo.Batches[0].ExportFileUrl!;

        Assert.NotEqual(first, second);
        Assert.EndsWith("payout-2026-10-agency-summary.xlsx", second);
        Assert.Equal(1, rig.Storage.Count);
        Assert.Null(await rig.Storage.OpenReadAsync(first["/files/".Length..]));
    }

    [Fact]
    public async Task A_rejected_type_neither_stores_a_file_nor_touches_the_url()
    {
        var rig = await new Rig().BuildAsync(Done(1, 5, 260000m));

        await rig.Service().ExportAsync(rig.BatchId, "nope");

        Assert.Null(rig.Repo.Batches[0].ExportFileUrl);
        Assert.Equal(0, rig.Storage.Count);
    }

    // ---- controller -----------------------------------------------------------------------------

    private sealed class StubExports(int status) : IPayoutExportService
    {
        public (int Batch, string? Type)? LastCall { get; private set; }

        public Task<PayoutResult<PayoutExportFile>> ExportAsync(int batchId, string? type, CancellationToken cancellationToken = default)
        {
            LastCall = (batchId, type);
            return Task.FromResult(status switch
            {
                200 => PayoutResult<PayoutExportFile>.Ok(new PayoutExportFile([1, 2, 3], "payout-2026-10-freelancer.xlsx", XlsxWriter.ContentType)),
                400 => PayoutResult<PayoutExportFile>.ValidationError(new Dictionary<string, string[]> { ["type"] = ["bad"] }),
                _ => PayoutResult<PayoutExportFile>.NotFound(),
            });
        }
    }

    [Fact]
    public void The_controller_is_admin_only_with_the_contract_route_and_one_GET()
    {
        var type = typeof(AdminPayoutExportController);
        Assert.Equal("AdminOnly", type.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal("api/admin/payout-batches/{batchId:int}/export", type.GetCustomAttribute<RouteAttribute>()!.Template);

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.All(methods, m => Assert.Null(m.GetCustomAttribute<AllowAnonymousAttribute>()));
        var verbs = methods.SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(a => a.HttpMethods.Single())).ToArray();
        Assert.Equal(["GET"], verbs);
    }

    [Fact]
    public async Task A_success_is_the_file_itself_as_an_attachment_not_the_JSON_envelope()
    {
        var stub = new StubExports(200);

        var result = Assert.IsType<FileContentResult>(await new AdminPayoutExportController(stub).Export(5, "freelancer", default));

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.ContentType);
        Assert.Equal("payout-2026-10-freelancer.xlsx", result.FileDownloadName); // makes Content-Disposition: attachment
        Assert.Equal([1, 2, 3], result.FileContents);
        Assert.Equal((5, "freelancer"), stub.LastCall);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    public async Task Errors_keep_the_JSON_envelope(int status)
    {
        var result = Assert.IsType<ObjectResult>(await new AdminPayoutExportController(new StubExports(status)).Export(5, "x", default));

        Assert.Equal(status, result.StatusCode);
        var body = Assert.IsType<ApiResponse<object>>(result.Value);
        Assert.False(body.Success);
        if (status == 400) Assert.Contains("errors", body.Data!.GetType().GetProperties().Select(p => p.Name));
    }

    // ---- SQL Server -----------------------------------------------------------------------------

    [Fact]
    public async Task Real_database_the_three_files_of_a_real_batch_are_readable_and_the_export_url_is_stored()
    {
        if (!PayoutBatchDatabaseTests.IsSqlServerAvailable()) return;

        var seed = await PayoutBatchDatabaseTests.SeedBaseAsync(withAgency: true);
        const string period = "2092-04";
        try
        {
            var f = await PayoutBatchDatabaseTests.AddWorkerAsync(seed, "Freelancer Ơ", "0123456");
            var s = await PayoutBatchDatabaseTests.AddWorkerAsync(seed, "Agency Staff S", "555", agencyStaff: true);
            await PayoutBatchDatabaseTests.AddAssignmentAsync(seed, f, PayoutBatchDatabaseTests.Utc(2092, 4, 10, 3));
            await PayoutBatchDatabaseTests.AddAssignmentAsync(seed, s, PayoutBatchDatabaseTests.Utc(2092, 4, 11, 3), gross: 300000m, rate: 0.150m, agencyId: seed.AgencyId);
            await PayoutBatchDatabaseTests.AddAssignmentAsync(seed, s, PayoutBatchDatabaseTests.Utc(2092, 4, 12, 3), agencyId: seed.AgencyId, absenceFee: 104000m, rate: 0.150m);

            var clock = new PayoutBatchServiceTests.TestClock(new DateTime(2099, 6, 1, 4, 0, 0, DateTimeKind.Utc));
            var storage = new FakeFileStorage();
            int batchId;
            await using (var db = new AppDbContext(PayoutBatchDatabaseTests.Options()))
            {
                batchId = (await PayoutBatchDatabaseTests.Real(db).BuildAsync(period)).Data!.BatchId;
            }

            byte[] freelancerFile, summaryFile, detailFile;
            await using (var db = new AppDbContext(PayoutBatchDatabaseTests.Options()))
            {
                var service = new PayoutExportService(new EfPayoutRepository(db), storage, clock);
                freelancerFile = (await service.ExportAsync(batchId, "freelancer")).Data!.Content;
                summaryFile = (await service.ExportAsync(batchId, "agency-summary")).Data!.Content;
                detailFile = (await service.ExportAsync(batchId, "agency-detail")).Data!.Content;
            }

            var freelancer = XlsxTestReader.Table(freelancerFile, 5);
            Assert.Equal(2, freelancer.Count);
            Assert.Equal(["Freelancer Ơ", "ACB", "0123456", "208000", "Thanh toan ChoThueGiupViec 2092-04"], freelancer[1]);

            var summary = XlsxTestReader.Table(summaryFile, 9);
            Assert.Equal(2, summary.Count);
            Assert.Equal(["Payout Agency Co", "VCB", "777888", "2", "404000", "45000", "0", "359000", "Thanh toan ChoThueGiupViec 2092-04"], summary[1]);

            var detail = XlsxTestReader.Table(detailFile, 8);
            Assert.Equal(3, detail.Count); // header + the completed job + the absence fee
            Assert.Equal("Payout Agency Co", detail[1][0]);
            Assert.Equal("300000", detail[1][5]);
            Assert.Equal("45000", detail[1][6]);
            Assert.Equal("255000", detail[1][7]);
            Assert.Equal("104000", detail[2][5]);
            Assert.Equal("0", detail[2][6]); // no commission on the absence fee
            Assert.Equal("2092-04-12T10:00:00+07:00", detail[2][3]); // the fee is dated by updated_at 03:00Z = 10:00 local
            Assert.Equal("2092-04-11T10:00:00+07:00", detail[1][3]);

            await using var verify = new AppDbContext(PayoutBatchDatabaseTests.Options());
            var batch = await verify.PayoutBatches.AsNoTracking().SingleAsync(b => b.BatchId == batchId);
            Assert.EndsWith("payout-2092-04-agency-detail.xlsx", batch.ExportFileUrl);
            Assert.Equal(1, storage.Count); // only the latest export is kept
        }
        finally
        {
            await PayoutBatchDatabaseTests.CleanAsync(seed, period);
        }
    }
}

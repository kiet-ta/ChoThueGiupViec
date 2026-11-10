using System.Globalization;
using CommonService.Application.Features.Ledger;
using CommonService.Application.Features.Payouts.Dtos;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CommonService.Application.Features.Payouts.Services;

public class PayoutResult<T>
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IDictionary<string, string[]>? ValidationErrors { get; init; }
    public T? Data { get; init; }

    public static PayoutResult<T> Ok(T data) => new() { Success = true, StatusCode = 200, Data = data };

    public static PayoutResult<T> Created(T data) => new() { Success = true, StatusCode = 201, Data = data };

    public static PayoutResult<T> ValidationError(IDictionary<string, string[]> errors) => new()
    {
        StatusCode = 400,
        ErrorMessage = "Validation failed",
        ValidationErrors = errors,
    };

    public static PayoutResult<T> NotFound() => new() { StatusCode = 404, ErrorMessage = "Not found." };

    public static PayoutResult<T> Conflict(string message) => new() { StatusCode = 409, ErrorMessage = message };
}

public interface IPayoutBatchService
{
    /// <summary>
    /// Settles a finished month from the ledger (decision D28, contract ledger.md section 7): one item per payee, a SETTLEMENT entry for
    /// each positive net, and the batch CLOSED by the system. 201 when this call closed the month, 200 when it was closed before (a
    /// closed month never changes and a re-run does nothing); 409 when a payee could not be settled (the next run continues).
    /// <paramref name="allowUnfinishedMonth"/> is for the Development demo only: it settles a month that is not over yet, as if it ended now.
    /// </summary>
    Task<PayoutResult<PayoutBatchDto>> SettleAsync(string? periodMonth, bool allowUnfinishedMonth = false, CancellationToken cancellationToken = default);

    Task<PayoutResult<PayoutBatchPageDto>> ListAsync(string? page, string? pageSize, CancellationToken cancellationToken = default);

    Task<PayoutResult<PayoutBatchDetailDto>> GetAsync(
        int batchId, string? payeeType, string? page, string? pageSize, CancellationToken cancellationToken = default);
}

/// <summary>
/// The monthly settlement (decisions Q10, Q11, G-1, G-2, G-3, D28). The month is computed from the ledger and closed by the system; nothing
/// here calls a bank (G-1): a payee's money leaves the wallet as a SETTLEMENT entry, nobody confirms it. Each payee is settled in its own
/// unit of work (wallet lock, item, SETTLEMENT entry), so a failure leaves the others done and a later run continues; a payee that
/// already has an item is skipped, which makes a re-run safe.
/// </summary>
public class PayoutBatchService(
    IPayoutRepository payouts,
    ILedgerQuery ledger,
    ILedgerWriter writer,
    IAuditLog audit,
    IUnitOfWork unitOfWork,
    IClock clock,
    IPublisher publisher,
    ILogger<PayoutBatchService> logger) : IPayoutBatchService
{
    /// <summary>Thrown inside the unit of work to undo it; never leaves this class.</summary>
    private class SettleAbortedException(string message) : Exception(message);

    public async Task<PayoutResult<PayoutBatchDto>> SettleAsync(string? periodMonth, bool allowUnfinishedMonth = false, CancellationToken cancellationToken = default)
    {
        var period = periodMonth?.Trim();
        if (!PayoutConstants.TryParsePeriod(period, out var year, out var month))
        {
            return PayoutResult<PayoutBatchDto>.ValidationError(new Dictionary<string, string[]>
            {
                ["periodMonth"] = ["periodMonth must be a month in the form YYYY-MM."],
            });
        }

        if (!allowUnfinishedMonth && !IsFinished(year, month))
        {
            return PayoutResult<PayoutBatchDto>.ValidationError(new Dictionary<string, string[]>
            {
                ["periodMonth"] = ["Only a finished month can be settled."],
            });
        }

        // The month is read in Asia/Ho_Chi_Minh (decision G-3) and queried in UTC.
        var monthStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var fromUtc = DateTime.SpecifyKind(clock.ToUtc(monthStart), DateTimeKind.Utc); // the database layer rejects any other Kind
        var toUtc = DateTime.SpecifyKind(clock.ToUtc(monthStart.AddMonths(1)), DateTimeKind.Utc);
        var now = clock.UtcNow;

        try
        {
            var batch = await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                if (!await payouts.TryLockPeriodAsync(period!, cancellationToken)) throw new SettleAbortedException("Another run of this month is starting.");
                return await payouts.FindByPeriodAsync(period!, cancellationToken) ?? await payouts.AddDraftAsync(period!, now, cancellationToken);
            }, cancellationToken);

            if (batch.BatchStatus == PayoutConstants.BatchClosed)
            {
                var closedCount = (await payouts.CountItemsAsync([batch.BatchId], cancellationToken)).GetValueOrDefault(batch.BatchId);
                return PayoutResult<PayoutBatchDto>.Ok(ToDto(batch, closedCount));
            }

            var failed = 0;
            foreach (var party in await ledger.GetSettlementPartiesAsync(toUtc, cancellationToken))
            {
                try
                {
                    await SettlePartyAsync(batch.BatchId, period!, party.Type, party.Id, fromUtc, toUtc, now, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    logger.LogError(ex, "Settlement {Period}: could not settle {PartyType} {PartyId}; the next run continues.", period, party.Type, party.Id);
                }
            }

            if (failed > 0)
            {
                return PayoutResult<PayoutBatchDto>.Conflict($"{failed} payee(s) could not be settled; the month stays open and the next run continues.");
            }

            var total = await payouts.SumNetAsync(batch.BatchId, cancellationToken);
            var count = (await payouts.CountItemsAsync([batch.BatchId], cancellationToken)).GetValueOrDefault(batch.BatchId);
            var closed = await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                if (!await payouts.TryCloseAsync(batch.BatchId, total, now, cancellationToken)) return false; // another run closed it first

                await audit.WriteAsync(
                    new AuditEntry(AuditActorType.System, null, PayoutConstants.AuditEntityType, batch.BatchId.ToString(CultureInfo.InvariantCulture),
                        PayoutConstants.AuditFieldName, PayoutConstants.BatchDraft, PayoutConstants.BatchClosed,
                        string.Create(CultureInfo.InvariantCulture, $"Settled {period} from the ledger: {count} items, total {total:0}")),
                    cancellationToken);
                return true;
            }, cancellationToken);

            var stored = (await payouts.GetBatchAsync(batch.BatchId, cancellationToken))!;
            if (!closed) return PayoutResult<PayoutBatchDto>.Ok(ToDto(stored, count));

            await publisher.Publish(new PayoutBatchClosed(stored.BatchId, stored.PeriodMonth, stored.TotalAmount, count, now), cancellationToken);
            return PayoutResult<PayoutBatchDto>.Created(ToDto(stored, count));
        }
        catch (SettleAbortedException ex)
        {
            return PayoutResult<PayoutBatchDto>.Conflict(ex.Message);
        }
    }

    /// <summary>One payee: under its wallet lock, the item, then the SETTLEMENT entry computed under the ledger lock, then the figures.</summary>
    private async Task SettlePartyAsync(
        int batchId, string period, LedgerPartyType type, int id, DateTime fromUtc, DateTime toUtc, DateTime now, CancellationToken cancellationToken)
    {
        var payee = new PayeeKey(type == LedgerPartyType.Worker ? PayeeType.Freelancer : PayeeType.Agency, id);

        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await payouts.LockPayeeAsync(payee, cancellationToken);
            if (await payouts.FindItemIdAsync(batchId, payee, cancellationToken) is not null) return false; // settled by an earlier run

            if (!(await ComputeAsync(type, id, fromUtc, toUtc, cancellationToken)).HasActivity) return false;

            var banks = await payouts.GetBankAccountsAsync([payee], cancellationToken);
            var bank = banks.GetValueOrDefault(payee, string.Empty);
            var itemId = await payouts.AddItemAsync(batchId, payee, bank, now, cancellationToken);

            // The figures are read again under the ledger lock, so no entry can slip in between the net and the SETTLEMENT entry.
            SettlementFigures figures = null!;
            await writer.AppendAsync(async _ =>
            {
                figures = await ComputeAsync(type, id, fromUtc, toUtc, cancellationToken);
                IReadOnlyList<NewLedgerEntry> none = [];
                return figures.Net > 0 ? [LedgerRules.Settlement(type, id, figures.Net, itemId, period)] : none;
            }, cancellationToken);

            await payouts.CompleteItemAsync(itemId, new NewPayoutItem(
                payee.Type, payee.Type == PayeeType.Freelancer ? id : null, payee.Type == PayeeType.Agency ? id : null,
                figures.JobCount, figures.Gross, figures.Commission, figures.Penalty, figures.Advance, figures.CarryIn, figures.Net,
                bank, figures.AssignmentIds), cancellationToken);
            return true;
        }, cancellationToken);
    }

    private async Task<SettlementFigures> ComputeAsync(LedgerPartyType type, int id, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        var entries = await ledger.GetPartyEntriesAsync(type, id, toUtc, cancellationToken);
        var earned = entries.Where(e => e.CreatedAt >= fromUtc && e.ToPartyType == type && e.ToPartyId == id
            && e.EntryType is LedgerEntryType.JobEarning or LedgerEntryType.ExtensionEarning).ToList();
        var commissions = new List<LedgerEntry>();
        foreach (var group in earned.GroupBy(e => e.RefType))
        {
            commissions.AddRange(await ledger.GetCommissionsAsync(group.Key, group.Select(e => e.RefId).Distinct().ToList(), cancellationToken));
        }

        var held = type == LedgerPartyType.Worker ? await ledger.GetHeldAsync(id, cancellationToken) : 0m;
        return PayoutSettlementCalculator.Compute(type, id, entries, commissions, fromUtc, toUtc, held);
    }

    public async Task<PayoutResult<PayoutBatchPageDto>> ListAsync(string? page, string? pageSize, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        var pageNumber = ParseInt(page, 1, 1, int.MaxValue, "page", "Page must be an integer of at least 1.", errors);
        var size = ParseInt(pageSize, PayoutConstants.DefaultPageSize, 1, PayoutConstants.MaxPageSize, "pageSize",
            $"Page size must be an integer between 1 and {PayoutConstants.MaxPageSize}.", errors);
        if (errors.Count > 0) return PayoutResult<PayoutBatchPageDto>.ValidationError(errors);

        var (batches, total) = await payouts.ListBatchesAsync(pageNumber, size, cancellationToken);
        var counts = await payouts.CountItemsAsync(batches.Select(b => b.BatchId).ToList(), cancellationToken);
        return PayoutResult<PayoutBatchPageDto>.Ok(new PayoutBatchPageDto
        {
            Items = batches.Select(b => ToDto(b, counts.GetValueOrDefault(b.BatchId))).ToList(),
            Page = pageNumber,
            PageSize = size,
            Total = total,
        });
    }

    public async Task<PayoutResult<PayoutBatchDetailDto>> GetAsync(
        int batchId, string? payeeType, string? page, string? pageSize, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        PayeeType? filter = null;
        if (!string.IsNullOrWhiteSpace(payeeType))
        {
            var text = payeeType.Trim();
            if (string.Equals(text, PayoutConstants.PayeeFreelancer, StringComparison.OrdinalIgnoreCase)) filter = PayeeType.Freelancer;
            else if (string.Equals(text, PayoutConstants.PayeeAgency, StringComparison.OrdinalIgnoreCase)) filter = PayeeType.Agency;
            else errors["payeeType"] = ["payeeType must be FREELANCER or AGENCY."];
        }

        var pageNumber = ParseInt(page, 1, 1, int.MaxValue, "page", "Page must be an integer of at least 1.", errors);
        var size = ParseInt(pageSize, PayoutConstants.DefaultPageSize, 1, PayoutConstants.MaxPageSize, "pageSize",
            $"Page size must be an integer between 1 and {PayoutConstants.MaxPageSize}.", errors);
        if (errors.Count > 0) return PayoutResult<PayoutBatchDetailDto>.ValidationError(errors);

        var batch = await payouts.GetBatchAsync(batchId, cancellationToken);
        if (batch is null) return PayoutResult<PayoutBatchDetailDto>.NotFound();

        var (items, total) = await payouts.GetItemsAsync(batchId, filter, pageNumber, size, cancellationToken);
        var counts = await payouts.CountItemsAsync([batchId], cancellationToken);
        var without = await payouts.GetPayeesWithoutBankAsync(batchId, cancellationToken);

        return PayoutResult<PayoutBatchDetailDto>.Ok(new PayoutBatchDetailDto
        {
            Batch = ToDto(batch, counts.GetValueOrDefault(batchId)),
            Items = items.Select(ToDto).ToList(),
            Page = pageNumber,
            PageSize = size,
            Total = total,
            Warnings = without.Select(name => $"{name} has no bank account number.").ToList(),
        });
    }

    /// <summary>True once the Asia/Ho_Chi_Minh calendar has left the month (question P2).</summary>
    private bool IsFinished(int year, int month)
    {
        var today = clock.LocalToday;
        return (year, month).CompareTo((today.Year, today.Month)) < 0;
    }

    private static PayoutBatchDto ToDto(PayoutBatch b, int itemCount) => new()
    {
        BatchId = b.BatchId,
        PeriodMonth = b.PeriodMonth,
        BatchStatus = b.BatchStatus,
        TotalAmount = b.TotalAmount,
        ItemCount = itemCount,
        ConfirmedBy = b.ConfirmedBy,
        ConfirmedAt = b.ConfirmedAt is { } at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null,
        CreatedAt = DateTime.SpecifyKind(b.CreatedAt, DateTimeKind.Utc),
    };

    private static PayoutItemDto ToDto(PayoutItemView view) => new()
    {
        ItemId = view.Item.ItemId,
        PayeeType = DbEnum.ToDb(view.Item.PayeeType),
        WorkerId = view.Item.WorkerId,
        AgencyId = view.Item.AgencyId,
        PayeeName = view.PayeeName,
        JobCount = view.Item.JobCount,
        GrossAmount = view.Item.GrossAmount,
        CommissionAmount = view.Item.CommissionAmount,
        PenaltyAmount = view.Item.PenaltyAmount,
        AdvanceAmount = view.Item.AdvanceAmount,
        CarryInAmount = view.Item.CarryInAmount,
        NetAmount = view.Item.NetAmount,
        BankName = view.BankName,
        BankAccountNo = view.Item.BankAccountNo,
        ItemStatus = view.Item.ItemStatus,
    };

    private static int ParseInt(string? raw, int fallback, int min, int max, string field, string message, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (int.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max)
        {
            return value;
        }

        errors[field] = [message];
        return fallback;
    }
}

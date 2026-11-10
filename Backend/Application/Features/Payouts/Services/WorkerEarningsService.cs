using System.Globalization;
using CommonService.Application.Features.Payouts.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Payouts.Services;

public interface IWorkerEarningsService
{
    /// <summary>The freelancer's income of a month (<c>YYYY-MM</c>, default the current month); 403 for agency staff (contract payouts.md 2.5).</summary>
    Task<PayoutResult<WorkerEarningsDto>> GetEarningsAsync(int workerId, string? month, CancellationToken cancellationToken = default);

    /// <summary>The worker's own items of CLOSED batches, newest month first.</summary>
    Task<PayoutResult<WorkerPayoutHistoryPageDto>> GetPayoutsAsync(int workerId, string? page, string? pageSize, CancellationToken cancellationToken = default);
}

/// <summary>
/// The income screens of the freelancer (BE-M6-07, decision Q11: 80 % net), read from the ledger since decision D28. The month is computed
/// with the same <see cref="PayoutSettlementCalculator"/> as the monthly settlement, so the screen is right before the month is settled
/// (it shows what would be paid if the month ended now) and, once the month's batch is CLOSED, the stored item is the answer: the screen
/// equals the money that was settled, including the advance (withdrawals) and the carry-in from earlier months.
/// </summary>
public class WorkerEarningsService(IWorkerEarningsRepository earnings, ILedgerQuery ledger, IClock clock) : IWorkerEarningsService
{
    public const string NotBuilt = "NOT_BUILT";
    public const string Pending = "PENDING";
    public const string Transferred = "TRANSFERRED";

    public async Task<PayoutResult<WorkerEarningsDto>> GetEarningsAsync(int workerId, string? month, CancellationToken cancellationToken = default)
    {
        var type = await earnings.GetWorkerTypeAsync(workerId, cancellationToken);
        if (type is null) return PayoutResult<WorkerEarningsDto>.NotFound();

        // The agency, not the worker, is paid for an agency staff member (PRD 4.4 step 3).
        if (type != WorkerType.Freelancer)
        {
            return new PayoutResult<WorkerEarningsDto> { StatusCode = 403, ErrorMessage = "Only freelancers have monthly earnings; the agency is paid for its staff." };
        }

        var today = clock.LocalToday;
        int year, monthNumber;
        if (string.IsNullOrWhiteSpace(month))
        {
            (year, monthNumber) = (today.Year, today.Month);
        }
        else if (!PayoutConstants.TryParsePeriod(month.Trim(), out year, out monthNumber))
        {
            return Invalid("month must be a month in the form YYYY-MM.");
        }

        if ((year, monthNumber).CompareTo((today.Year, today.Month)) > 0) return Invalid("A future month has no earnings yet.");

        var period = PayoutConstants.FormatPeriod(year, monthNumber);
        var monthStart = new DateTime(year, monthNumber, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var fromUtc = DateTime.SpecifyKind(clock.ToUtc(monthStart), DateTimeKind.Utc); // queried in UTC, read in Asia/Ho_Chi_Minh (G-3)
        var toUtc = DateTime.SpecifyKind(clock.ToUtc(monthStart.AddMonths(1)), DateTimeKind.Utc);

        var entries = await ledger.GetPartyEntriesAsync(LedgerPartyType.Worker, workerId, toUtc, cancellationToken);
        var commissions = await CommissionsOfAsync(entries, fromUtc, cancellationToken);
        var held = await ledger.GetHeldAsync(workerId, cancellationToken);
        var figures = PayoutSettlementCalculator.Compute(LedgerPartyType.Worker, workerId, entries, commissions, fromUtc, toUtc, held);
        var state = await earnings.GetBatchStateAsync(workerId, period, cancellationToken);

        var jobs = JobsOf(entries, commissions, workerId, fromUtc);

        int jobCount;
        decimal gross, commission, penalty, advance, carryIn, net;
        if (state is { BatchStatus: PayoutConstants.BatchClosed, Item: { } item })
        {
            // A closed batch is immutable: what was settled is the answer, whatever the ledger says now.
            (jobCount, gross, commission, penalty, advance, carryIn, net) =
                (item.JobCount, item.GrossAmount, item.CommissionAmount, item.PenaltyAmount, item.AdvanceAmount, item.CarryInAmount, item.NetAmount);
        }
        else
        {
            (jobCount, gross, commission, penalty, advance, carryIn, net) =
                (figures.JobCount, figures.Gross, figures.Commission, figures.Penalty, figures.Advance, figures.CarryIn, figures.Net);
        }

        return PayoutResult<WorkerEarningsDto>.Ok(new WorkerEarningsDto
        {
            PeriodMonth = period,
            JobCount = jobCount,
            GrossAmount = gross,
            CommissionAmount = commission,
            PenaltyAmount = penalty,
            AdvanceAmount = advance,
            CarryInAmount = carryIn,
            NetAmount = net,
            PayoutStatus = state is null ? NotBuilt : state.BatchStatus == PayoutConstants.BatchClosed ? Transferred : Pending,
            Jobs = jobs,
        });
    }

    public async Task<PayoutResult<WorkerPayoutHistoryPageDto>> GetPayoutsAsync(
        int workerId, string? page, string? pageSize, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        var pageNumber = ParseInt(page, 1, 1, int.MaxValue, "page", "Page must be an integer of at least 1.", errors);
        var size = ParseInt(pageSize, PayoutConstants.DefaultPageSize, 1, PayoutConstants.MaxPageSize, "pageSize",
            $"Page size must be an integer between 1 and {PayoutConstants.MaxPageSize}.", errors);
        if (errors.Count > 0) return PayoutResult<WorkerPayoutHistoryPageDto>.ValidationError(errors);

        var (rows, total) = await earnings.GetClosedItemsAsync(workerId, pageNumber, size, cancellationToken);
        return PayoutResult<WorkerPayoutHistoryPageDto>.Ok(new WorkerPayoutHistoryPageDto
        {
            Items = rows.Select(r => new WorkerPayoutHistoryItemDto
            {
                BatchId = r.BatchId,
                PeriodMonth = r.PeriodMonth,
                AdvanceAmount = r.AdvanceAmount,
                CarryInAmount = r.CarryInAmount,
                NetAmount = r.NetAmount,
                ItemStatus = r.ItemStatus,
                TransferredAt = r.TransferredAt is { } at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null,
            }).ToList(),
            Page = pageNumber,
            PageSize = size,
            Total = total,
        });
    }

    /// <summary>The platform's commission entries of the earnings the worker got in the month.</summary>
    private async Task<IReadOnlyList<LedgerEntry>> CommissionsOfAsync(IReadOnlyList<LedgerEntry> entries, DateTime fromUtc, CancellationToken cancellationToken)
    {
        var commissions = new List<LedgerEntry>();
        var earned = entries.Where(e => e.CreatedAt >= fromUtc && e.ToPartyType == LedgerPartyType.Worker
            && e.EntryType is LedgerEntryType.JobEarning or LedgerEntryType.ExtensionEarning);
        foreach (var group in earned.GroupBy(e => e.RefType))
        {
            commissions.AddRange(await ledger.GetCommissionsAsync(group.Key, group.Select(e => e.RefId).Distinct().ToList(), cancellationToken));
        }

        return commissions;
    }

    /// <summary>
    /// One line per job (or approved absence fee) of the month, oldest first: the earning of the worker and the commission of the same
    /// assignment. Extensions are inside the month's totals but are not jobs of their own.
    /// </summary>
    private static List<WorkerEarningJobDto> JobsOf(IReadOnlyList<LedgerEntry> entries, IReadOnlyList<LedgerEntry> commissions, int workerId, DateTime fromUtc) =>
        entries
            .Where(e => e.CreatedAt >= fromUtc && e.ToPartyType == LedgerPartyType.Worker && e.ToPartyId == workerId && e.RefType == LedgerRefType.Assignment
                && e.EntryType is LedgerEntryType.JobEarning or LedgerEntryType.AbsenceFee)
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.RefId)
            .Select(e =>
            {
                var absence = e.EntryType == LedgerEntryType.AbsenceFee;
                var commission = absence ? 0m : commissions.Where(c => c.RefType == e.RefType && c.RefId == e.RefId).Sum(c => c.Amount);
                return new WorkerEarningJobDto
                {
                    AssignmentId = e.RefId,
                    OrderId = e.OrderId ?? 0,
                    CompletedAt = DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc),
                    GrossAmount = e.Amount + commission,
                    CommissionAmount = commission,
                    NetAmount = e.Amount,
                    AbsenceFee = absence,
                };
            }).ToList();

    private static PayoutResult<WorkerEarningsDto> Invalid(string message) =>
        PayoutResult<WorkerEarningsDto>.ValidationError(new Dictionary<string, string[]> { ["month"] = [message] });

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

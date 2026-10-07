using System.Globalization;
using CommonService.Application.Features.Payouts.Dtos;
using CommonService.Application.Interfaces.Ports;
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
/// The income screens of the freelancer (BE-M6-07, decision Q11: 80 % net). The month is computed with the same <see cref="PayoutCalculator"/>
/// as the payout batch, so the screen is available before the batch exists; once the month's batch is CLOSED the stored item is the
/// answer, so the screen equals the money that was transferred.
/// </summary>
public sealed class WorkerEarningsService(IWorkerEarningsRepository earnings, IPayoutPenaltySource penalties, IClock clock) : IWorkerEarningsService
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

        var rows = await earnings.GetEarningsAsync(workerId, fromUtc, toUtc, cancellationToken);
        var state = await earnings.GetBatchStateAsync(workerId, period, cancellationToken);

        var assignments = rows.Select(r => new PayoutAssignmentRow(r.AssignmentId, workerId, null, r.Status, r.GrossAmount, r.CommissionRate, r.AbsenceFeeAmount)).ToList();
        var jobs = rows.Zip(assignments, (row, a) =>
        {
            var amounts = PayoutCalculator.AmountsOf(a);
            return new WorkerEarningJobDto
            {
                AssignmentId = row.AssignmentId,
                OrderId = row.OrderId,
                CompletedAt = DateTime.SpecifyKind(row.DateUtc, DateTimeKind.Utc),
                GrossAmount = amounts.Gross,
                CommissionAmount = amounts.Commission,
                NetAmount = amounts.Payable,
                AbsenceFee = row.Status == JobAssignmentStatus.Absent,
            };
        }).Where(j => j.GrossAmount > 0).ToList();

        int jobCount;
        decimal gross, commission, penalty, net;
        if (state is { BatchStatus: PayoutConstants.BatchClosed, Item: { } item })
        {
            // A closed batch is immutable: what was paid is the answer, whatever the data says now.
            (jobCount, gross, commission, penalty, net) = (item.JobCount, item.GrossAmount, item.CommissionAmount, item.PenaltyAmount, item.NetAmount);
        }
        else
        {
            var key = new PayeeKey(PayeeType.Freelancer, workerId);
            var decided = await penalties.GetDecidedAsync(toUtc, cancellationToken);
            var applied = await earnings.GetAppliedPenaltyBeforeAsync(workerId, period, cancellationToken);
            var line = PayoutCalculator.Build(
                assignments,
                new Dictionary<PayeeKey, decimal> { [key] = decided.GetValueOrDefault(key) },
                new Dictionary<PayeeKey, decimal> { [key] = applied }).SingleOrDefault();
            (jobCount, gross, commission, penalty, net) = line is null ? (0, 0m, 0m, 0m, 0m) : (line.JobCount, line.Gross, line.Commission, line.Penalty, line.Net);
        }

        return PayoutResult<WorkerEarningsDto>.Ok(new WorkerEarningsDto
        {
            PeriodMonth = period,
            JobCount = jobCount,
            GrossAmount = gross,
            CommissionAmount = commission,
            PenaltyAmount = penalty,
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
                NetAmount = r.NetAmount,
                ItemStatus = r.ItemStatus,
                TransferredAt = r.TransferredAt is { } at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null,
            }).ToList(),
            Page = pageNumber,
            PageSize = size,
            Total = total,
        });
    }

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

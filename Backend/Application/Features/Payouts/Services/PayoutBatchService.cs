using System.Globalization;
using CommonService.Application.Features.Payouts.Dtos;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;

namespace CommonService.Application.Features.Payouts.Services;

public sealed class PayoutResult<T>
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
    /// <summary>Builds (201) or rebuilds the DRAFT (200) batch of a finished month (contract payouts.md 2.1).</summary>
    Task<PayoutResult<PayoutBatchDto>> BuildAsync(string? periodMonth, CancellationToken cancellationToken = default);

    Task<PayoutResult<PayoutBatchPageDto>> ListAsync(string? page, string? pageSize, CancellationToken cancellationToken = default);

    Task<PayoutResult<PayoutBatchDetailDto>> GetAsync(
        int batchId, string? payeeType, string? page, string? pageSize, CancellationToken cancellationToken = default);

    /// <summary>Records that the transfers were made: CLOSED, items TRANSFERRED, <see cref="PayoutBatchClosed"/> (contract 2.4).</summary>
    Task<PayoutResult<PayoutBatchDto>> ConfirmAsync(int adminId, int batchId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The monthly payout batch (BE-M6-04, spec 4.4 step 3; decisions Q10, Q11, G-1, G-2, G-3). Nothing here calls a bank (G-1): confirming
/// only records that the Admin made the transfers from the exported file.
/// </summary>
public sealed class PayoutBatchService(
    IPayoutRepository payouts,
    IPayoutPenaltySource penalties,
    IAuditLog audit,
    IUnitOfWork unitOfWork,
    IClock clock,
    IPublisher publisher) : IPayoutBatchService
{
    /// <summary>Thrown inside the unit of work to undo it; never leaves this class.</summary>
    private sealed class BuildAbortedException(string message) : Exception(message);

    public async Task<PayoutResult<PayoutBatchDto>> BuildAsync(string? periodMonth, CancellationToken cancellationToken = default)
    {
        var period = periodMonth?.Trim();
        if (!PayoutConstants.TryParsePeriod(period, out var year, out var month))
        {
            return PayoutResult<PayoutBatchDto>.ValidationError(new Dictionary<string, string[]>
            {
                ["periodMonth"] = ["periodMonth must be a month in the form YYYY-MM."],
            });
        }

        if (!IsFinished(year, month))
        {
            return PayoutResult<PayoutBatchDto>.ValidationError(new Dictionary<string, string[]>
            {
                ["periodMonth"] = ["Only a finished month can be built."],
            });
        }

        // The month is read in Asia/Ho_Chi_Minh (decision G-3) and queried in UTC.
        var monthStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var fromUtc = DateTime.SpecifyKind(clock.ToUtc(monthStart), DateTimeKind.Utc); // the database layer rejects any other Kind
        var toUtc = DateTime.SpecifyKind(clock.ToUtc(monthStart.AddMonths(1)), DateTimeKind.Utc);
        var now = clock.UtcNow;

        try
        {
            var (created, batchId, lines) = await unitOfWork.ExecuteInTransactionAsync<(bool Created, int BatchId, IReadOnlyList<PayoutLine>? Lines)>(async () =>
            {
                if (!await payouts.TryLockPeriodAsync(period!, cancellationToken)) throw new BuildAbortedException("Another build of this month is running.");

                var batch = await payouts.FindByPeriodAsync(period!, cancellationToken);
                if (batch is { BatchStatus: PayoutConstants.BatchClosed }) return (false, batch.BatchId, null);

                var isNew = batch is null;
                batch ??= await payouts.AddDraftAsync(period!, now, cancellationToken);

                var rows = await payouts.GetPayableAsync(fromUtc, toUtc, batch.BatchId, cancellationToken);
                var decided = await penalties.GetDecidedAsync(toUtc, cancellationToken);
                var applied = await payouts.GetAppliedPenaltiesBeforeAsync(period!, cancellationToken);
                var computed = PayoutCalculator.Build(rows, decided, applied);

                var banks = await payouts.GetBankAccountsAsync(computed.Select(l => l.Payee).ToList(), cancellationToken);
                var items = computed.Select(l => new NewPayoutItem(
                    l.Payee.Type,
                    l.Payee.Type == PayeeType.Freelancer ? l.Payee.Id : null,
                    l.Payee.Type == PayeeType.Agency ? l.Payee.Id : null,
                    l.JobCount, l.Gross, l.Commission, l.Penalty, l.Net,
                    banks.GetValueOrDefault(l.Payee, string.Empty),
                    l.AssignmentIds)).ToList();

                await payouts.ReplaceItemsAsync(batch.BatchId, items, computed.Sum(l => l.Net), cancellationToken);
                return (isNew, batch.BatchId, computed);
            }, cancellationToken);

            if (lines is null) return PayoutResult<PayoutBatchDto>.Conflict("The batch of this month is closed and cannot change.");

            var stored = (await payouts.GetBatchAsync(batchId, cancellationToken))!;
            var dto = ToDto(stored, lines.Count);
            return created ? PayoutResult<PayoutBatchDto>.Created(dto) : PayoutResult<PayoutBatchDto>.Ok(dto);
        }
        catch (BuildAbortedException ex)
        {
            return PayoutResult<PayoutBatchDto>.Conflict(ex.Message);
        }
        catch (PayoutClaimLostException)
        {
            return PayoutResult<PayoutBatchDto>.Conflict("An assignment was paid by another batch while this one was built; try again.");
        }
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

    public async Task<PayoutResult<PayoutBatchDto>> ConfirmAsync(int adminId, int batchId, CancellationToken cancellationToken = default)
    {
        var batch = await payouts.GetBatchAsync(batchId, cancellationToken);
        if (batch is null) return PayoutResult<PayoutBatchDto>.NotFound();
        if (batch.BatchStatus != PayoutConstants.BatchDraft) return PayoutResult<PayoutBatchDto>.Conflict("The batch is already closed.");

        var count = (await payouts.CountItemsAsync([batchId], cancellationToken)).GetValueOrDefault(batchId);
        if (count == 0) return PayoutResult<PayoutBatchDto>.Conflict("The batch has no items to disburse.");
        if (PayoutConstants.TryParsePeriod(batch.PeriodMonth, out var year, out var month) && !IsFinished(year, month))
        {
            return PayoutResult<PayoutBatchDto>.Conflict("The month of this batch is not over yet.");
        }

        var now = clock.UtcNow;
        var closed = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await payouts.TryCloseAsync(batchId, adminId, now, cancellationToken)) return false; // closed between our read and now

            await audit.WriteAsync(
                new AuditEntry(AuditActorType.Admin, adminId, PayoutConstants.AuditEntityType, batchId.ToString(CultureInfo.InvariantCulture),
                    PayoutConstants.AuditFieldName, PayoutConstants.BatchDraft, PayoutConstants.BatchClosed,
                    string.Create(CultureInfo.InvariantCulture, $"Disbursement confirmed for {batch.PeriodMonth}: {count} items, total {batch.TotalAmount:0}")),
                cancellationToken);
            return true;
        }, cancellationToken);

        if (!closed) return PayoutResult<PayoutBatchDto>.Conflict("The batch is already closed.");

        await publisher.Publish(new PayoutBatchClosed(batch.BatchId, batch.PeriodMonth, batch.TotalAmount, count, now), cancellationToken);

        var stored = (await payouts.GetBatchAsync(batchId, cancellationToken))!;
        return PayoutResult<PayoutBatchDto>.Ok(ToDto(stored, count));
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

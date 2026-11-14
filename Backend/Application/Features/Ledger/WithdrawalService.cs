using CommonService.Application.Exceptions;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.ValueObjects;

namespace CommonService.Application.Features.Ledger;

/// <summary>Codes of the 409 answers of the withdrawal flow (contract ledger.md section 6).</summary>
public static class WithdrawalErrorCodes
{
    public const string Pending = "WITHDRAWAL_PENDING";
    public const string NotPending = "WITHDRAWAL_NOT_PENDING";
    public const string Insufficient = "WITHDRAWAL_INSUFFICIENT";

    /// <summary>The previous month is not settled yet for this worker (decision D31): no withdrawal is created or approved meanwhile.</summary>
    public const string Settling = "WITHDRAWAL_SETTLING";
}

/// <summary>
/// Decision D31 (LEDGER-30): the monthly settlement pays the balance as it stood at 00:00 on the 1st (Asia/Ho_Chi_Minh) but runs later
/// (from 00:05, hourly), so a withdrawal written in between would be paid twice. Withdrawals of a worker are therefore paused until the
/// previous month is settled for that worker: its batch is CLOSED or already holds the worker's item. The check runs under the worker's
/// lock, the same lock the settlement of that worker takes.
/// </summary>
public static class WithdrawalSettlementGate
{
    /// <summary>The month (<c>yyyy-MM</c>) that must be settled before the worker may withdraw: the month before the current Vietnam month.</summary>
    public static string PreviousPeriod(IClock clock)
    {
        var local = clock.ToLocal(clock.UtcNow);
        var previous = new DateTime(local.Year, local.Month, 1).AddMonths(-1);
        return Payouts.PayoutConstants.FormatPeriod(previous.Year, previous.Month);
    }

    public static async Task<bool> IsPausedAsync(IWithdrawalRepository repository, IClock clock, int workerId, CancellationToken cancellationToken) =>
        !await repository.IsSettledForAsync(workerId, PreviousPeriod(clock), cancellationToken);

    public static BusinessRuleViolationException Conflict() =>
        new("Đang tất toán lương tháng trước. Bạn có thể rút tiền lại ngay khi tất toán xong.", WithdrawalErrorCodes.Settling);
}

public record WithdrawalDto(int WithdrawalId, decimal Amount, string Status, DateTime RequestedAt, DateTime? ReviewedAt, string? RejectReason);

public record WithdrawalPageDto(IReadOnlyList<WithdrawalDto> Items, int Page, int PageSize, int Total);

public record PendingWithdrawalDto(int WithdrawalId, decimal Amount, string Status, DateTime RequestedAt);

/// <summary>The worker's wallet: <c>Available</c> is what can be withdrawn now (24 hours after completion, no open dispute, minus held).</summary>
/// <remarks><c>WithdrawalsPaused</c> is true while the previous month is not settled for the worker (decision D31): no request can be sent or approved.</remarks>
public record WalletDto(decimal Balance, decimal Available, decimal Held, PendingWithdrawalDto? PendingWithdrawal, DateTime? NextAvailableAt, bool WithdrawalsPaused = false);

public record WithdrawalWorker(WorkerType Type, string? BankAccountNo, string? BankName);

/// <summary>A request as the Admin sees it, with the worker's name and phone number (read-only join).</summary>
public record AdminWithdrawalRow(WithdrawalRequest Request, string WorkerName, string WorkerPhone);

public interface IWithdrawalRepository
{
    Task<WithdrawalWorker?> GetWorkerAsync(int workerId, CancellationToken cancellationToken = default);

    /// <summary>Runs the action in a transaction that holds the application lock <c>ledger-party:WORKER:{id}</c>, so the creation, cancel and approval of one worker never interleave.</summary>
    Task<T> ExecuteLockedAsync<T>(int workerId, Func<Task<T>> action, CancellationToken cancellationToken = default);

    Task<WithdrawalRequest?> GetPendingAsync(int workerId, CancellationToken cancellationToken = default);

    /// <summary>The worker's own request; null for a missing or foreign one.</summary>
    Task<WithdrawalRequest?> GetAsync(int workerId, int withdrawalId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<WithdrawalRequest> Items, int Total)> ListAsync(int workerId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Inserts the request; false when the filtered unique index (one PENDING request per worker) rejected it.</summary>
    Task<bool> TryAddAsync(WithdrawalRequest request, CancellationToken cancellationToken = default);

    /// <summary>One conditional update PENDING to CANCELLED; false when the request is no longer pending.</summary>
    Task<bool> TryCancelAsync(int workerId, int withdrawalId, CancellationToken cancellationToken = default);

    /// <summary>Any worker's request by id (the Admin's view); null when it does not exist.</summary>
    Task<WithdrawalRequest?> GetByIdAsync(int withdrawalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The requests of every worker, optionally of one status. Without a <paramref name="sort"/> the pending queue is oldest first and the
    /// others newest first; with one the order is applied before paging, ties by the request id in the same direction.
    /// </summary>
    Task<(IReadOnlyList<AdminWithdrawalRow> Items, int Total)> ListForAdminAsync(
        WithdrawalStatus? status, int page, int pageSize, AdminWithdrawalSort? sort, CancellationToken cancellationToken = default);

    /// <summary>One conditional update PENDING to PAID with the ledger entry and the reviewer; false when no longer pending.</summary>
    Task<bool> TryApproveAsync(int withdrawalId, long entryId, int adminId, DateTime atUtc, CancellationToken cancellationToken = default);

    /// <summary>One conditional update PENDING to REJECTED with the reason and the reviewer; false when no longer pending.</summary>
    Task<bool> TryRejectAsync(int withdrawalId, string reason, int adminId, DateTime atUtc, CancellationToken cancellationToken = default);

    /// <summary>Flushes what is tracked (the audit row) inside the current transaction.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the month <paramref name="periodMonth"/> (<c>yyyy-MM</c>) is settled for the worker: its PAYOUT_BATCH is CLOSED or already holds
    /// the worker's PAYOUT_ITEM (read only; decision D31).
    /// </summary>
    Task<bool> IsSettledForAsync(int workerId, string periodMonth, CancellationToken cancellationToken = default);
}

public interface IWithdrawalService
{
    Task<WalletDto> GetWalletAsync(int workerId, CancellationToken cancellationToken = default);
    Task<WithdrawalDto> CreateAsync(int workerId, decimal amount, CancellationToken cancellationToken = default);
    Task<WithdrawalPageDto> ListAsync(int workerId, int? page, int? pageSize, CancellationToken cancellationToken = default);
    Task<WithdrawalDto> CancelAsync(int workerId, int withdrawalId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The freelancer's side of the advance (decision E): at most one PENDING request, never more than the withdrawable amount, only a
/// whole number of VND. The approval by the Admin is a separate service. Business answers are exceptions mapped by the error middleware
/// (400 field errors, 403, 404, 409 with a <c>code</c>).
/// </summary>
public class WithdrawalService(IWithdrawalRepository repository, ILedgerBalanceService balances, IClock clock) : IWithdrawalService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public async Task<WalletDto> GetWalletAsync(int workerId, CancellationToken cancellationToken = default)
    {
        await RequireFreelancerAsync(workerId, cancellationToken);
        var balance = await balances.GetAsync(new LedgerParty(LedgerPartyType.Worker, workerId), cancellationToken);
        var pending = await repository.GetPendingAsync(workerId, cancellationToken);
        var paused = await WithdrawalSettlementGate.IsPausedAsync(repository, clock, workerId, cancellationToken);
        return new WalletDto(
            balance.Balance, balance.Available ?? 0m, balance.Held,
            pending is null ? null : new PendingWithdrawalDto(pending.WithdrawalId, pending.Amount, DbEnum.ToDb(pending.RequestStatus), pending.RequestedAt),
            balance.NextAvailableAt, paused);
    }

    public async Task<WithdrawalDto> CreateAsync(int workerId, decimal amount, CancellationToken cancellationToken = default)
    {
        var worker = await RequireFreelancerAsync(workerId, cancellationToken);
        if (amount <= 0) throw new ValidationException("amount", "Số tiền phải là số nguyên lớn hơn 0 và không vượt quá số có thể rút.");
        if (amount != Vnd.Round(amount)) throw new ValidationException("amount", "Số tiền phải là số nguyên lớn hơn 0 và không vượt quá số có thể rút.");

        return await repository.ExecuteLockedAsync(workerId, async () =>
        {
            if (await repository.GetPendingAsync(workerId, cancellationToken) is not null) throw PendingConflict();
            if (await WithdrawalSettlementGate.IsPausedAsync(repository, clock, workerId, cancellationToken)) throw WithdrawalSettlementGate.Conflict();

            var balance = await balances.GetAsync(new LedgerParty(LedgerPartyType.Worker, workerId), cancellationToken);
            if (amount > (balance.Available ?? 0m))
            {
                throw new ValidationException("amount", $"Số tiền phải là số nguyên lớn hơn 0 và không vượt quá số có thể rút ({balance.Available ?? 0m:0} đồng).");
            }

            var request = new WithdrawalRequest
            {
                WorkerId = workerId,
                Amount = amount,
                RequestStatus = WithdrawalStatus.Pending,
                BankAccountNo = worker.BankAccountNo,
                BankName = worker.BankName,
                RequestedAt = clock.UtcNow,
            };
            if (!await repository.TryAddAsync(request, cancellationToken)) throw PendingConflict();
            return ToDto(request);
        }, cancellationToken);
    }

    public async Task<WithdrawalPageDto> ListAsync(int workerId, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        await RequireFreelancerAsync(workerId, cancellationToken);
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (page is < 1) errors["page"] = ["page must be at least 1."];
        if (pageSize is < 1 or > MaxPageSize) errors["pageSize"] = [$"pageSize must be between 1 and {MaxPageSize}."];
        if (errors.Count > 0) throw new ValidationException(errors);

        var (items, total) = await repository.ListAsync(workerId, page ?? 1, pageSize ?? DefaultPageSize, cancellationToken);
        return new WithdrawalPageDto(items.Select(ToDto).ToList(), page ?? 1, pageSize ?? DefaultPageSize, total);
    }

    public async Task<WithdrawalDto> CancelAsync(int workerId, int withdrawalId, CancellationToken cancellationToken = default)
    {
        await RequireFreelancerAsync(workerId, cancellationToken);
        return await repository.ExecuteLockedAsync(workerId, async () =>
        {
            var request = await repository.GetAsync(workerId, withdrawalId, cancellationToken)
                ?? throw new NotFoundException("Withdrawal request", withdrawalId);
            if (!await repository.TryCancelAsync(workerId, withdrawalId, cancellationToken))
            {
                throw new BusinessRuleViolationException("Yêu cầu không còn ở trạng thái chờ duyệt.", WithdrawalErrorCodes.NotPending);
            }

            request.RequestStatus = WithdrawalStatus.Cancelled;
            return ToDto(request);
        }, cancellationToken);
    }

    /// <summary>Only a freelancer can withdraw: an agency's staff is paid through the agency (403).</summary>
    private async Task<WithdrawalWorker> RequireFreelancerAsync(int workerId, CancellationToken cancellationToken)
    {
        var worker = await repository.GetWorkerAsync(workerId, cancellationToken) ?? throw new NotFoundException("Worker", workerId);
        if (worker.Type != WorkerType.Freelancer) throw new ForbiddenAccessException("Thợ thuộc Agency được trả lương qua Agency, không rút tiền trên app.");
        return worker;
    }

    private static BusinessRuleViolationException PendingConflict() =>
        new("Bạn đang có một yêu cầu rút tiền chờ duyệt. Mỗi lần chỉ được một yêu cầu.", WithdrawalErrorCodes.Pending);

    private static WithdrawalDto ToDto(WithdrawalRequest r) =>
        new(r.WithdrawalId, r.Amount, DbEnum.ToDb(r.RequestStatus), r.RequestedAt, r.ReviewedAt, r.RejectReason);
}

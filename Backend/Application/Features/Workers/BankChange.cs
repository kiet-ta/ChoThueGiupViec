using CommonService.Application.Features.Workers.Dtos;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Workers;

/// <summary>Codes of the 409 answers of the payout account flow (contract workers.md, bank section; LEDGER-28).</summary>
public static class BankChangeErrorCodes
{
    /// <summary><c>PUT /workers/me/bank</c> on a worker who already has an account: a change is a request.</summary>
    public const string RequiresRequest = "BANK_CHANGE_REQUIRES_REQUEST";

    /// <summary>A request to change needs an account to change: the first one is set with the PUT.</summary>
    public const string NotSet = "BANK_NOT_SET";

    public const string Pending = "BANK_CHANGE_PENDING";
    public const string Locked = "BANK_CHANGE_LOCKED";
    public const string NotPending = "BANK_CHANGE_NOT_PENDING";
}

/// <summary>Topics of the messages to the worker about a request.</summary>
public static class BankChangeNotificationTopics
{
    public const string Approved = "bank-change.approved";
    public const string Rejected = "bank-change.rejected";
}

/// <summary>The worker's row the flow needs (read-only).</summary>
public record BankWorker(WorkerType Type, string? BankName, string? BankAccountNo)
{
    public bool IsSet => !string.IsNullOrEmpty(BankName) && !string.IsNullOrEmpty(BankAccountNo);
}

/// <summary>A request as the Admin sees it, with the worker's name and phone number (read-only join).</summary>
public record BankChangeRow(WorkerBankChangeRequest Request, string WorkerName, string WorkerPhone);

public interface IWorkerBankChangeRepository
{
    Task<BankWorker?> GetWorkerAsync(int workerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the action in a transaction that holds the application lock <c>ledger-party:WORKER:{id}</c> (the same one the withdrawals use), so a
    /// request, an approval and a withdrawal of one worker never interleave.
    /// </summary>
    Task<T> ExecuteLockedAsync<T>(int workerId, Func<Task<T>> action, CancellationToken cancellationToken = default);

    /// <summary>The worker's latest request of any status (by the time sent); null when there is none.</summary>
    Task<WorkerBankChangeRequest?> GetLatestAsync(int workerId, CancellationToken cancellationToken = default);

    Task<WorkerBankChangeRequest?> GetPendingAsync(int workerId, CancellationToken cancellationToken = default);

    /// <summary>Inserts the request; false when the filtered unique index (one PENDING request per worker) rejected it.</summary>
    Task<bool> TryAddAsync(WorkerBankChangeRequest request, CancellationToken cancellationToken = default);

    /// <summary>One conditional update that sets the account only while the worker has none; false when there already is one.</summary>
    Task<bool> TrySetFirstAccountAsync(int workerId, string bankName, string bankAccountNo, DateTime atUtc, CancellationToken cancellationToken = default);

    Task<WorkerBankChangeRequest?> GetByIdAsync(int requestId, CancellationToken cancellationToken = default);

    /// <summary>One request with the worker's name and phone (the Admin's view); null when it does not exist.</summary>
    Task<BankChangeRow?> GetRowAsync(int requestId, CancellationToken cancellationToken = default);

    /// <summary>The requests of every worker, optionally of one status: the pending queue oldest first, the others newest first.</summary>
    Task<(IReadOnlyList<BankChangeRow> Items, int Total)> ListForAdminAsync(
        BankChangeStatus? status, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>PENDING to APPROVED and the new account written on the worker; false when the request is no longer pending. Call it inside the lock.</summary>
    Task<bool> TryApproveAsync(int requestId, int adminId, DateTime atUtc, CancellationToken cancellationToken = default);

    /// <summary>PENDING to REJECTED with the reason; false when the request is no longer pending. The account does not change.</summary>
    Task<bool> TryRejectAsync(int requestId, string reason, int adminId, DateTime atUtc, CancellationToken cancellationToken = default);

    /// <summary>Flushes what is tracked (the audit row) inside the current transaction.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}

public record BankChangePendingDto(int RequestId, string BankName, string BankAccountNo, DateTime RequestedAt);

public record BankChangeLastDto(string Status, string? RejectReason, DateTime RequestedAt, DateTime? ReviewedAt);

/// <summary>Body of <c>POST /api/v1/workers/me/bank-change-requests</c>: both fields are required.</summary>
public record CreateBankChangeRequest(string? BankName, string? BankAccountNo);

public record AdminBankChangeDto(
    int RequestId, int WorkerId, string WorkerName, string WorkerPhone,
    string? PreviousBankName, string? PreviousBankAccountNo, string BankName, string BankAccountNo,
    string Status, DateTime RequestedAt, DateTime? ReviewedAt, string? RejectReason);

public record AdminBankChangePageDto(IReadOnlyList<AdminBankChangeDto> Items, int Page, int PageSize, int Total);

/// <summary>Body of a rejection: the reason, 1-255 characters.</summary>
public record RejectBankChangeRequest(string? Reason);

public interface IWorkerBankService
{
    /// <summary>The worker's account and what they can do about it: <c>canRequestChange</c>, the lock date, the pending and the latest request.</summary>
    Task<WorkerBankResponse> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets the FIRST account (no approval, no lock). 409 <c>BANK_CHANGE_REQUIRES_REQUEST</c> when there already is one.</summary>
    Task<WorkerBankResponse> SetFirstAsync(UpdateWorkerBankRequest? request, CancellationToken cancellationToken = default);

    /// <summary>Sends a request to change the account: 409 while one is pending or while the 15-day lock of the latest request lasts.</summary>
    Task<WorkerBankResponse> RequestChangeAsync(CreateBankChangeRequest? request, CancellationToken cancellationToken = default);
}

public interface IAdminBankChangeService
{
    Task<AdminBankChangePageDto> ListAsync(string? status, int? page, int? pageSize, CancellationToken cancellationToken = default);
    Task<AdminBankChangeDto> ApproveAsync(int adminId, int requestId, CancellationToken cancellationToken = default);
    Task<AdminBankChangeDto> RejectAsync(int adminId, int requestId, string? reason, CancellationToken cancellationToken = default);
}

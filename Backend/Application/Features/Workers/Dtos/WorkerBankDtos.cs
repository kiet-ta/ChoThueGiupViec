namespace CommonService.Application.Features.Workers.Dtos;

/// <summary>
/// The account the platform transfers the worker's monthly payout to (<c>WORKER.bank_name</c> / <c>bank_account_no</c>) and what the worker can do about
/// it (LEDGER-28): <c>CanRequestChange</c> is true only with an account, no pending request and no lock; <c>ChangeLockedUntil</c> is the end of the
/// 15-day lock that starts when a request is SENT (null when there is none); <c>LastRequest</c> is the latest request of any status.
/// </summary>
public record WorkerBankResponse(
    string BankName,
    string BankAccountNo,
    bool IsSet,
    bool CanRequestChange = false,
    DateTime? ChangeLockedUntil = null,
    BankChangePendingDto? PendingRequest = null,
    BankChangeLastDto? LastRequest = null);

/// <summary>Body of <c>PUT /api/v1/workers/me/bank</c> (the first account only): both fields are required.</summary>
public record UpdateWorkerBankRequest(string? BankName, string? BankAccountNo);

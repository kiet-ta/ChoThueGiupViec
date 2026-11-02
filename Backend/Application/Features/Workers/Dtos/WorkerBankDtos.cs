namespace CommonService.Application.Features.Workers.Dtos;

/// <summary>The account the platform transfers the worker's monthly payout to (<c>WORKER.bank_name</c> / <c>bank_account_no</c>).</summary>
public record WorkerBankResponse(string BankName, string BankAccountNo, bool IsSet);

/// <summary>Body of <c>PUT /api/workers/me/bank</c>: both fields are required.</summary>
public record UpdateWorkerBankRequest(string? BankName, string? BankAccountNo);

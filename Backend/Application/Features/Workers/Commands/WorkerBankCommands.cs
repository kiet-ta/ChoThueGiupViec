using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Interfaces.Ports;
using MediatR;

namespace CommonService.Application.Features.Workers.Commands;

/// <summary>The account the platform transfers the worker's monthly payout to (<c>WORKER.bank_name</c> / <c>bank_account_no</c>).</summary>
public sealed record WorkerBankResponse(string BankName, string BankAccountNo, bool IsSet);

/// <summary>Body of <c>PUT /api/workers/me/bank</c>: both fields are required.</summary>
public sealed record UpdateWorkerBankRequest(string? BankName, string? BankAccountNo);

public sealed record GetWorkerBankQuery : IRequest<ApiResponse<WorkerBankResponse>>;

public sealed record UpdateWorkerBankCommand(UpdateWorkerBankRequest? Request) : IRequest<ApiResponse<WorkerBankResponse>>;

/// <summary>
/// Field rules of the payout account of a worker (ECON-FLOW-FIX, decisions D19): the same rules as the agency's account (a name of at most
/// 100 characters, 6 to 30 digits with spaces ignored), kept here so Workers does not depend on the Agencies module.
/// </summary>
public static class WorkerBankValidator
{
    public const int BankNameMaxLength = 100;
    public const int BankAccountNoMaxLength = 30;
    public const int BankAccountNoMinLength = 6;

    public static (string BankName, string BankAccountNo) Validate(string? bankName, string? bankAccountNo)
    {
        var name = (bankName ?? string.Empty).Trim();
        var account = new string((bankAccountNo ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (name.Length == 0)
        {
            errors["bankName"] = ["Bank name is required."];
        }
        else if (name.Length > BankNameMaxLength)
        {
            errors["bankName"] = [$"Bank name must be at most {BankNameMaxLength} characters."];
        }

        if (account.Length == 0)
        {
            errors["bankAccountNo"] = ["Bank account number is required."];
        }
        else if (account.Length < BankAccountNoMinLength || account.Length > BankAccountNoMaxLength || !account.All(char.IsAsciiDigit))
        {
            errors["bankAccountNo"] = [$"Bank account number must be {BankAccountNoMinLength} to {BankAccountNoMaxLength} digits."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return (name, account);
    }
}

public sealed class GetWorkerBankQueryHandler(IWorkerRepository workerRepository, ICurrentUser currentUser)
    : IRequestHandler<GetWorkerBankQuery, ApiResponse<WorkerBankResponse>>
{
    public async Task<ApiResponse<WorkerBankResponse>> Handle(GetWorkerBankQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        var worker = await workerRepository.GetByIdAsync(workerId, cancellationToken)
            ?? throw new NotFoundException("Worker profile not found.");
        var name = worker.BankName ?? string.Empty;
        var account = worker.BankAccountNo ?? string.Empty;
        return ApiResponse<WorkerBankResponse>.Ok(new WorkerBankResponse(name, account, name.Length > 0 && account.Length > 0));
    }
}

public sealed class UpdateWorkerBankCommandHandler(IWorkerRepository workerRepository, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<UpdateWorkerBankCommand, ApiResponse<WorkerBankResponse>>
{
    public async Task<ApiResponse<WorkerBankResponse>> Handle(UpdateWorkerBankCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        var request = command.Request ?? throw new ValidationException("request", "Request body is required.");
        var (name, account) = WorkerBankValidator.Validate(request.BankName, request.BankAccountNo);

        var worker = await workerRepository.GetByIdAsync(workerId, cancellationToken)
            ?? throw new NotFoundException("Worker profile not found.");
        worker.BankName = name;
        worker.BankAccountNo = account;
        worker.UpdatedAt = clock.UtcNow;
        workerRepository.Update(worker);
        await workerRepository.SaveChangesAsync(cancellationToken);

        return ApiResponse<WorkerBankResponse>.Ok(new WorkerBankResponse(name, account, true), "Bank account saved.");
    }
}

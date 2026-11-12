using System.Globalization;
using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Workers.Services;

/// <summary>
/// The freelancer's payout account (LEDGER-28, contract workers.md bank section). A worker has ONE account. The first one is set at once;
/// changing it is a request that an Admin approves, and after a request is SENT no new one can be sent for
/// <see cref="WorkerRules.BankChangeLockDays"/> days (15), whatever the Admin decides. A withdrawal request copies the account when it is made, so
/// nothing already in motion changes.
/// </summary>
public class WorkerBankService(
    IWorkerBankChangeRepository repository,
    ICurrentUser currentUser,
    IClock clock,
    IOptions<BusinessRules> rules) : IWorkerBankService
{
    private int WorkerId => currentUser.UserId ?? throw new ForbiddenAccessException("Authenticated worker user ID is required.");

    public Task<WorkerBankResponse> GetAsync(CancellationToken cancellationToken = default) => ReadAsync(WorkerId, cancellationToken);

    public async Task<WorkerBankResponse> SetFirstAsync(UpdateWorkerBankRequest? request, CancellationToken cancellationToken = default)
    {
        var workerId = WorkerId;
        var body = request ?? throw new ValidationException("request", "Request body is required.");
        var (name, account) = WorkerBankValidator.Validate(body.BankName, body.BankAccountNo);

        await repository.ExecuteLockedAsync(workerId, async () =>
        {
            var worker = await repository.GetWorkerAsync(workerId, cancellationToken) ?? throw new NotFoundException("Worker profile not found.");
            if (worker.IsSet || !await repository.TrySetFirstAccountAsync(workerId, name, account, clock.UtcNow, cancellationToken))
            {
                throw new BusinessRuleViolationException(
                    "Bạn đã có tài khoản nhận tiền. Muốn đổi, hãy gửi đơn đổi tài khoản để Admin duyệt.", BankChangeErrorCodes.RequiresRequest);
            }

            return true;
        }, cancellationToken);

        return await ReadAsync(workerId, cancellationToken);
    }

    public async Task<WorkerBankResponse> RequestChangeAsync(CreateBankChangeRequest? request, CancellationToken cancellationToken = default)
    {
        var workerId = WorkerId;
        var body = request ?? throw new ValidationException("request", "Request body is required.");
        var (name, account) = WorkerBankValidator.Validate(body.BankName, body.BankAccountNo);

        await repository.ExecuteLockedAsync(workerId, async () =>
        {
            var worker = await repository.GetWorkerAsync(workerId, cancellationToken) ?? throw new NotFoundException("Worker profile not found.");
            if (!worker.IsSet)
            {
                throw new BusinessRuleViolationException("Bạn chưa có tài khoản nhận tiền: hãy thêm tài khoản, không cần gửi đơn.", BankChangeErrorCodes.NotSet);
            }

            if (string.Equals(worker.BankAccountNo, account, StringComparison.Ordinal) && string.Equals(worker.BankName, name, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException("bankAccountNo", "Tài khoản mới phải khác tài khoản hiện tại.");
            }

            if (await repository.GetPendingAsync(workerId, cancellationToken) is not null)
            {
                throw new BusinessRuleViolationException("Bạn đã có một đơn đổi tài khoản đang chờ Admin duyệt.", BankChangeErrorCodes.Pending);
            }

            var now = clock.UtcNow;
            var lockedUntil = LockedUntil(await repository.GetLatestAsync(workerId, cancellationToken), now);
            if (lockedUntil is not null)
            {
                throw new BusinessRuleViolationException(
                    $"Bạn đã gửi đơn đổi tài khoản gần đây. Có thể gửi đơn mới từ ngày {clock.ToLocal(lockedUntil.Value).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.",
                    BankChangeErrorCodes.Locked);
            }

            var added = await repository.TryAddAsync(new WorkerBankChangeRequest
            {
                WorkerId = workerId,
                BankName = name,
                BankAccountNo = account,
                PreviousBankName = worker.BankName,
                PreviousBankAccountNo = worker.BankAccountNo,
                RequestStatus = BankChangeStatus.Pending,
                RequestedAt = now,
            }, cancellationToken);
            if (!added)
            {
                throw new BusinessRuleViolationException("Bạn đã có một đơn đổi tài khoản đang chờ Admin duyệt.", BankChangeErrorCodes.Pending);
            }

            return true;
        }, cancellationToken);

        return await ReadAsync(workerId, cancellationToken);
    }

    /// <summary>The end of the lock of the latest request, or null when the worker may send a request (none yet, or the days have passed).</summary>
    private DateTime? LockedUntil(WorkerBankChangeRequest? latest, DateTime nowUtc)
    {
        if (latest is null) return null;
        var until = latest.RequestedAt.AddDays(rules.Value.Worker.BankChangeLockDays);
        return until > nowUtc ? until : null;
    }

    private async Task<WorkerBankResponse> ReadAsync(int workerId, CancellationToken cancellationToken)
    {
        var worker = await repository.GetWorkerAsync(workerId, cancellationToken) ?? throw new NotFoundException("Worker profile not found.");
        var pending = await repository.GetPendingAsync(workerId, cancellationToken);
        var latest = await repository.GetLatestAsync(workerId, cancellationToken);
        var lockedUntil = LockedUntil(latest, clock.UtcNow);
        return new WorkerBankResponse(
            worker.BankName ?? string.Empty,
            worker.BankAccountNo ?? string.Empty,
            worker.IsSet,
            CanRequestChange: worker.IsSet && pending is null && lockedUntil is null,
            ChangeLockedUntil: lockedUntil,
            PendingRequest: pending is null ? null : new BankChangePendingDto(pending.RequestId, pending.BankName, pending.BankAccountNo, pending.RequestedAt),
            LastRequest: latest is null ? null : new BankChangeLastDto(DbEnum.ToDb(latest.RequestStatus), latest.RejectReason, latest.RequestedAt, latest.ReviewedAt));
    }
}

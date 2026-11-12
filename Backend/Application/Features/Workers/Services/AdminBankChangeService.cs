using System.Globalization;
using CommonService.Application.Exceptions;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CommonService.Application.Features.Workers.Services;

/// <summary>
/// The Admin's decision on a request to change a worker's payout account (LEDGER-28). Under the worker's lock, in one transaction: an approval
/// writes the new account on the worker and the status APPROVED, a rejection only the status and the reason; a request that is not pending any
/// more answers 409 <c>BANK_CHANGE_NOT_PENDING</c>. Both write an audit row and tell the worker after the commit (best effort).
/// </summary>
public class AdminBankChangeService(
    IWorkerBankChangeRepository repository,
    IAuditLog audit,
    INotificationService notifications,
    IClock clock,
    ILogger<AdminBankChangeService> logger) : IAdminBankChangeService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const int MaxReasonLength = 255;
    private const string AuditEntityType = "WORKER_BANK_CHANGE";
    private const string AuditField = "bank_account_no";

    public async Task<AdminBankChangePageDto> ListAsync(string? status, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        BankChangeStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            parsed = Enum.GetValues<BankChangeStatus>().Cast<BankChangeStatus?>()
                .FirstOrDefault(s => string.Equals(DbEnum.ToDb(s!.Value), status.Trim(), StringComparison.OrdinalIgnoreCase));
            if (parsed is null) errors["status"] = ["status must be PENDING, APPROVED or REJECTED."];
        }

        if (page is < 1) errors["page"] = ["page must be at least 1."];
        if (pageSize is < 1 or > MaxPageSize) errors["pageSize"] = [$"pageSize must be between 1 and {MaxPageSize}."];
        if (errors.Count > 0) throw new ValidationException(errors);

        var (rows, total) = await repository.ListForAdminAsync(parsed, page ?? 1, pageSize ?? DefaultPageSize, cancellationToken);
        return new AdminBankChangePageDto(rows.Select(ToDto).ToList(), page ?? 1, pageSize ?? DefaultPageSize, total);
    }

    public async Task<AdminBankChangeDto> ApproveAsync(int adminId, int requestId, CancellationToken cancellationToken = default)
    {
        var first = await repository.GetByIdAsync(requestId, cancellationToken) ?? throw new NotFoundException("Bank change request", requestId);

        var request = await repository.ExecuteLockedAsync(first.WorkerId, async () =>
        {
            // Read again under the lock: another Admin may have decided it while this call waited.
            var current = await repository.GetByIdAsync(requestId, cancellationToken) ?? throw new NotFoundException("Bank change request", requestId);
            if (current.RequestStatus != BankChangeStatus.Pending) throw NotPending();

            var now = clock.UtcNow;
            if (!await repository.TryApproveAsync(requestId, adminId, now, cancellationToken)) throw NotPending();

            await audit.WriteAsync(
                new AuditEntry(AuditActorType.Admin, adminId, AuditEntityType, requestId.ToString(CultureInfo.InvariantCulture), AuditField,
                    Describe(current.PreviousBankName, current.PreviousBankAccountNo), Describe(current.BankName, current.BankAccountNo),
                    $"Approved the change of the payout account of worker {current.WorkerId}."), cancellationToken);
            await repository.SaveAsync(cancellationToken);

            current.RequestStatus = BankChangeStatus.Approved;
            current.ReviewedBy = adminId;
            current.ReviewedAt = now;
            return current;
        }, cancellationToken);

        await NotifyAsync(
            request, BankChangeNotificationTopics.Approved, "Đơn đổi tài khoản nhận tiền đã được duyệt",
            $"Tài khoản nhận tiền của bạn giờ là {request.BankName} - {Mask(request.BankAccountNo)}.", cancellationToken);
        return await ToDtoAsync(request, cancellationToken);
    }

    public async Task<AdminBankChangeDto> RejectAsync(int adminId, int requestId, string? reason, CancellationToken cancellationToken = default)
    {
        var text = reason?.Trim() ?? string.Empty;
        if (text.Length == 0) throw new ValidationException("reason", "Hãy nhập lý do từ chối.");
        if (text.Length > MaxReasonLength) throw new ValidationException("reason", $"Lý do tối đa {MaxReasonLength} ký tự.");

        var first = await repository.GetByIdAsync(requestId, cancellationToken) ?? throw new NotFoundException("Bank change request", requestId);

        var request = await repository.ExecuteLockedAsync(first.WorkerId, async () =>
        {
            var current = await repository.GetByIdAsync(requestId, cancellationToken) ?? throw new NotFoundException("Bank change request", requestId);
            if (current.RequestStatus != BankChangeStatus.Pending) throw NotPending();

            var now = clock.UtcNow;
            if (!await repository.TryRejectAsync(requestId, text, adminId, now, cancellationToken)) throw NotPending();

            await audit.WriteAsync(
                new AuditEntry(AuditActorType.Admin, adminId, AuditEntityType, requestId.ToString(CultureInfo.InvariantCulture), AuditField,
                    Describe(current.PreviousBankName, current.PreviousBankAccountNo), Describe(current.PreviousBankName, current.PreviousBankAccountNo), text), cancellationToken);
            await repository.SaveAsync(cancellationToken);

            current.RequestStatus = BankChangeStatus.Rejected;
            current.RejectReason = text;
            current.ReviewedBy = adminId;
            current.ReviewedAt = now;
            return current;
        }, cancellationToken);

        await NotifyAsync(
            request, BankChangeNotificationTopics.Rejected, "Đơn đổi tài khoản nhận tiền bị từ chối",
            $"Đơn đổi tài khoản của bạn bị từ chối: {text}", cancellationToken);
        return await ToDtoAsync(request, cancellationToken);
    }

    private static string Describe(string? name, string? account) =>
        string.IsNullOrEmpty(account) ? "(none)" : $"{name} {Mask(account)}";

    /// <summary>Only the last 4 digits go to the audit log and to a push message.</summary>
    private static string Mask(string account) => account.Length <= 4 ? account : $"****{account[^4..]}";

    private static BusinessRuleViolationException NotPending() =>
        new("Đơn không còn ở trạng thái chờ duyệt.", BankChangeErrorCodes.NotPending);

    /// <summary>Best effort: the decision is committed, a failing push never undoes it.</summary>
    private async Task NotifyAsync(WorkerBankChangeRequest request, string topic, string title, string body, CancellationToken cancellationToken)
    {
        try
        {
            var data = new Dictionary<string, string>
            {
                ["requestId"] = request.RequestId.ToString(CultureInfo.InvariantCulture),
                ["status"] = DbEnum.ToDb(request.RequestStatus),
            };
            await notifications.SendAsync(new NotificationMessage(UserRole.Worker, request.WorkerId, topic, title, body, data), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not notify worker {WorkerId} of the decision on bank change request {RequestId}", request.WorkerId, request.RequestId);
        }
    }

    private static AdminBankChangeDto ToDto(BankChangeRow row) => ToDto(row.Request, row.WorkerName, row.WorkerPhone);

    private static AdminBankChangeDto ToDto(WorkerBankChangeRequest r, string workerName, string workerPhone) =>
        new(r.RequestId, r.WorkerId, workerName, workerPhone, r.PreviousBankName, r.PreviousBankAccountNo, r.BankName, r.BankAccountNo,
            DbEnum.ToDb(r.RequestStatus), r.RequestedAt, r.ReviewedAt, r.RejectReason);

    /// <summary>The decided request with the worker's name and phone (one read of the row the Admin acted on).</summary>
    private async Task<AdminBankChangeDto> ToDtoAsync(WorkerBankChangeRequest r, CancellationToken cancellationToken)
    {
        var row = await repository.GetRowAsync(r.RequestId, cancellationToken);
        return ToDto(r, row?.WorkerName ?? string.Empty, row?.WorkerPhone ?? string.Empty);
    }
}

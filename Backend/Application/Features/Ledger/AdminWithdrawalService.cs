using System.Globalization;
using CommonService.Application.Exceptions;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CommonService.Application.Features.Ledger;

public record AdminWithdrawalDto(
    int WithdrawalId, int WorkerId, string WorkerName, string WorkerPhone, decimal Amount, string Status, string? BankAccountNo, string? BankName,
    DateTime RequestedAt, int? ReviewedBy, DateTime? ReviewedAt, string? RejectReason, long? EntryId);

public record AdminWithdrawalPageDto(IReadOnlyList<AdminWithdrawalDto> Items, int Page, int PageSize, int Total);

/// <summary>The column of the Admin's request list: the worker's name, the amount, the bank account, the time it was sent or the status.</summary>
public enum AdminWithdrawalSortKey
{
    Worker,
    Amount,
    Bank,
    RequestedAt,
    Status,
}

public record AdminWithdrawalSort(AdminWithdrawalSortKey Key, bool Descending);

public interface IAdminWithdrawalService
{
    /// <summary><c>sort</c> is worker, amount, bank, requestedAt or status and <c>dir</c> asc or desc (default desc); a <c>dir</c> alone orders by the time sent.</summary>
    Task<AdminWithdrawalPageDto> ListAsync(string? status, int? page, int? pageSize, string? sort = null, string? dir = null, CancellationToken cancellationToken = default);
    Task<WithdrawalDto> ApproveAsync(int adminId, int withdrawalId, CancellationToken cancellationToken = default);
    Task<WithdrawalDto> RejectAsync(int adminId, int withdrawalId, string? reason, CancellationToken cancellationToken = default);
}

/// <summary>Topics of the withdrawal messages to the worker.</summary>
public static class WithdrawalNotificationTopics
{
    public const string Approved = "withdrawal.approved";
    public const string Rejected = "withdrawal.rejected";
}

/// <summary>
/// The Admin's decision on an advance request (decision E, contract ledger.md section 6). Under the worker's lock, in one transaction:
/// the withdrawable amount is recomputed (the request's own held amount counts again), the WITHDRAWAL entry is written and the request
/// becomes PAID; two approvals at once write one entry. A rejection writes no entry. Both are audited and the worker is told after the
/// commit (best effort).
/// </summary>
public class AdminWithdrawalService(
    IWithdrawalRepository repository,
    ILedgerBalanceService balances,
    ILedgerWriter writer,
    IAuditLog audit,
    INotificationService notifications,
    IClock clock,
    ILogger<AdminWithdrawalService> logger) : IAdminWithdrawalService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const int MaxReasonLength = 255;
    private const string AuditEntityType = "WITHDRAWAL_REQUEST";
    private const string AuditStatusField = "request_status";

    public async Task<AdminWithdrawalPageDto> ListAsync(string? status, int? page, int? pageSize, string? sort = null, string? dir = null, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        WithdrawalStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            parsed = Enum.GetValues<WithdrawalStatus>().Cast<WithdrawalStatus?>()
                .FirstOrDefault(s => string.Equals(DbEnum.ToDb(s!.Value), status.Trim(), StringComparison.OrdinalIgnoreCase));
            if (parsed is null) errors["status"] = ["status must be PENDING, PAID, REJECTED or CANCELLED."];
        }

        if (page is < 1) errors["page"] = ["page must be at least 1."];
        if (pageSize is < 1 or > MaxPageSize) errors["pageSize"] = [$"pageSize must be between 1 and {MaxPageSize}."];
        var order = ParseSort(sort, dir, errors);
        if (errors.Count > 0) throw new ValidationException(errors);

        var (rows, total) = await repository.ListForAdminAsync(parsed, page ?? 1, pageSize ?? DefaultPageSize, order, cancellationToken);
        return new AdminWithdrawalPageDto(rows.Select(ToAdminDto).ToList(), page ?? 1, pageSize ?? DefaultPageSize, total);
    }

    private static AdminWithdrawalSort? ParseSort(string? sort, string? dir, Dictionary<string, string[]> errors)
    {
        var descending = true;
        var hasDir = !string.IsNullOrWhiteSpace(dir);
        if (hasDir)
        {
            switch (dir!.Trim().ToLowerInvariant())
            {
                case "asc": descending = false; break;
                case "desc": descending = true; break;
                default: errors["dir"] = ["dir must be asc or desc."]; hasDir = false; break;
            }
        }

        if (string.IsNullOrWhiteSpace(sort)) return hasDir ? new AdminWithdrawalSort(AdminWithdrawalSortKey.RequestedAt, descending) : null;

        AdminWithdrawalSortKey? key = sort.Trim().ToLowerInvariant() switch
        {
            "worker" => AdminWithdrawalSortKey.Worker,
            "amount" => AdminWithdrawalSortKey.Amount,
            "bank" => AdminWithdrawalSortKey.Bank,
            "requestedat" => AdminWithdrawalSortKey.RequestedAt,
            "status" => AdminWithdrawalSortKey.Status,
            _ => null,
        };
        if (key is null)
        {
            errors["sort"] = ["sort must be one of: worker, amount, bank, requestedAt, status."];
            return null;
        }

        return new AdminWithdrawalSort(key.Value, descending);
    }

    public async Task<WithdrawalDto> ApproveAsync(int adminId, int withdrawalId, CancellationToken cancellationToken = default)
    {
        var first = await repository.GetByIdAsync(withdrawalId, cancellationToken) ?? throw new NotFoundException("Withdrawal request", withdrawalId);

        var result = await repository.ExecuteLockedAsync(first.WorkerId, async () =>
        {
            // Read again under the lock: another admin may have decided it, or the worker cancelled, while this call waited.
            var request = await repository.GetByIdAsync(withdrawalId, cancellationToken) ?? throw new NotFoundException("Withdrawal request", withdrawalId);
            if (request.RequestStatus != WithdrawalStatus.Pending) throw NotPending();

            // Decision D31: until the previous month is settled for this worker, an approval would be paid again by the settlement.
            if (await WithdrawalSettlementGate.IsPausedAsync(repository, clock, request.WorkerId, cancellationToken))
            {
                throw new BusinessRuleViolationException(
                    "Đang tất toán lương tháng trước của thợ này. Hãy duyệt lại sau khi tất toán xong.", WithdrawalErrorCodes.Settling);
            }

            // The request holds its own amount, so what counts is the withdrawable amount before the hold: a later penalty can make it short.
            var balance = await balances.GetAsync(new LedgerParty(LedgerPartyType.Worker, request.WorkerId), cancellationToken);
            if ((balance.WithdrawableBeforeHold ?? 0m) < request.Amount)
            {
                throw new BusinessRuleViolationException(
                    "Số dư có thể rút không còn đủ cho yêu cầu này (có thể do khoản khấu trừ mới). Hãy từ chối yêu cầu.", WithdrawalErrorCodes.Insufficient);
            }

            var written = await writer.AppendAsync([LedgerRules.Withdrawal(request.WorkerId, request.Amount, request.WithdrawalId, adminId)], cancellationToken);
            var entry = written.SingleOrDefault()
                ?? throw new InvalidOperationException($"The WITHDRAWAL entry of request {withdrawalId} already exists although the request is pending.");

            var now = clock.UtcNow;
            if (!await repository.TryApproveAsync(withdrawalId, entry.EntryId, adminId, now, cancellationToken)) throw NotPending();

            await audit.WriteAsync(
                new AuditEntry(AuditActorType.Admin, adminId, AuditEntityType, withdrawalId.ToString(CultureInfo.InvariantCulture), AuditStatusField,
                    DbEnum.ToDb(WithdrawalStatus.Pending), DbEnum.ToDb(WithdrawalStatus.Paid),
                    $"Approved the advance of {request.Amount:0} VND (ledger entry {entry.EntryCode})."), cancellationToken);
            await repository.SaveAsync(cancellationToken);

            request.RequestStatus = WithdrawalStatus.Paid;
            request.EntryId = entry.EntryId;
            request.ReviewedBy = adminId;
            request.ReviewedAt = now;
            return request;
        }, cancellationToken);

        await NotifyAsync(
            result.WorkerId, WithdrawalNotificationTopics.Approved, "Yêu cầu rút tiền đã được duyệt",
            $"Yêu cầu rút {result.Amount:N0} đồng đã được duyệt và trừ vào số dư của bạn.", result, cancellationToken);
        return ToDto(result);
    }

    public async Task<WithdrawalDto> RejectAsync(int adminId, int withdrawalId, string? reason, CancellationToken cancellationToken = default)
    {
        var text = reason?.Trim() ?? string.Empty;
        if (text.Length == 0) throw new ValidationException("reason", "Hãy nhập lý do từ chối.");
        if (text.Length > MaxReasonLength) throw new ValidationException("reason", $"Lý do tối đa {MaxReasonLength} ký tự.");

        var first = await repository.GetByIdAsync(withdrawalId, cancellationToken) ?? throw new NotFoundException("Withdrawal request", withdrawalId);

        var result = await repository.ExecuteLockedAsync(first.WorkerId, async () =>
        {
            var request = await repository.GetByIdAsync(withdrawalId, cancellationToken) ?? throw new NotFoundException("Withdrawal request", withdrawalId);
            if (request.RequestStatus != WithdrawalStatus.Pending) throw NotPending();

            var now = clock.UtcNow;
            if (!await repository.TryRejectAsync(withdrawalId, text, adminId, now, cancellationToken)) throw NotPending();

            await audit.WriteAsync(
                new AuditEntry(AuditActorType.Admin, adminId, AuditEntityType, withdrawalId.ToString(CultureInfo.InvariantCulture), AuditStatusField,
                    DbEnum.ToDb(WithdrawalStatus.Pending), DbEnum.ToDb(WithdrawalStatus.Rejected), text), cancellationToken);
            await repository.SaveAsync(cancellationToken);

            request.RequestStatus = WithdrawalStatus.Rejected;
            request.RejectReason = text;
            request.ReviewedBy = adminId;
            request.ReviewedAt = now;
            return request;
        }, cancellationToken);

        await NotifyAsync(
            result.WorkerId, WithdrawalNotificationTopics.Rejected, "Yêu cầu rút tiền bị từ chối",
            $"Yêu cầu rút {result.Amount:N0} đồng bị từ chối: {text}", result, cancellationToken);
        return ToDto(result);
    }

    /// <summary>Best effort, like the other message handlers: the decision is committed, a failing push never undoes it.</summary>
    private async Task NotifyAsync(int workerId, string topic, string title, string body, WithdrawalRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var data = new Dictionary<string, string>
            {
                ["withdrawalId"] = request.WithdrawalId.ToString(CultureInfo.InvariantCulture),
                ["amount"] = request.Amount.ToString("0", CultureInfo.InvariantCulture),
                ["status"] = DbEnum.ToDb(request.RequestStatus),
            };
            await notifications.SendAsync(new NotificationMessage(UserRole.Worker, workerId, topic, title, body, data), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not notify worker {WorkerId} of the decision on withdrawal {WithdrawalId}", workerId, request.WithdrawalId);
        }
    }

    private static BusinessRuleViolationException NotPending() =>
        new("Yêu cầu không còn ở trạng thái chờ duyệt.", WithdrawalErrorCodes.NotPending);

    private static WithdrawalDto ToDto(WithdrawalRequest r) =>
        new(r.WithdrawalId, r.Amount, DbEnum.ToDb(r.RequestStatus), r.RequestedAt, r.ReviewedAt, r.RejectReason);

    private static AdminWithdrawalDto ToAdminDto(AdminWithdrawalRow row) =>
        new(row.Request.WithdrawalId, row.Request.WorkerId, row.WorkerName, row.WorkerPhone, row.Request.Amount, DbEnum.ToDb(row.Request.RequestStatus),
            row.Request.BankAccountNo, row.Request.BankName, row.Request.RequestedAt, row.Request.ReviewedBy, row.Request.ReviewedAt,
            row.Request.RejectReason, row.Request.EntryId);
}

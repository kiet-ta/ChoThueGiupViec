using System.Globalization;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Application.Features.Dispatch;

/// <summary>
/// Decision D38: the one place that cancels a PREMIUM order because the shift cannot start. <see cref="AssignmentFailed"/> makes Booking cancel the
/// order, refund 100 % and release the hold, and Dispatch cancel every assignment; the customer then gets the in-app apology, only when the order
/// really is cancelled afterwards (otherwise the caller repeats the whole step at its next tick).
/// </summary>
public class PremiumOrderCanceller(
    IPublisher publisher,
    IPremiumStartRepository repository,
    INotificationService notifications,
    ILogger<PremiumOrderCanceller>? logger = null)
{
    public const string ApologyTopic = "premium.order_cancelled";

    private readonly ILogger<PremiumOrderCanceller> _logger = logger ?? NullLogger<PremiumOrderCanceller>.Instance;

    /// <summary>True when the order is cancelled after the call (and the apology was sent).</summary>
    public async Task<bool> CancelAsync(long orderId, string reason, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await publisher.Publish(new AssignmentFailed(orderId, reason, nowUtc), cancellationToken);

        var info = await repository.GetOrderInfoAsync(orderId, cancellationToken);
        if (info is null || info.Status != JobOrderStatus.Cancelled)
        {
            return false;
        }

        var amount = info.TotalAmount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
        try
        {
            await notifications.SendAsync(
                new NotificationMessage(
                    UserRole.Customer, info.CustomerId, ApologyTopic, "TỔ ẤM xin lỗi bạn",
                    $"Xin lỗi bạn, ca dọn nhà của bạn không thể bắt đầu vì đối tác chưa đủ người làm đúng giờ. Đơn đã được huỷ và hoàn 100% ({amount} đ). TỔ ẤM chân thành xin lỗi vì sự bất tiện này.",
                    new Dictionary<string, string> { ["orderId"] = orderId.ToString(CultureInfo.InvariantCulture) }),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not send the apology of order {OrderId}.", orderId);
        }

        return true;
    }
}

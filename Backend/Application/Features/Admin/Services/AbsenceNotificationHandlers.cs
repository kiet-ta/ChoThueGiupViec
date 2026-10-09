using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CommonService.Application.Features.Admin.Services;

/// <summary>Topics of the absence messages (BE-M6-03b), one place for the clients and the tests.</summary>
public static class AbsenceNotificationTopics
{
    public const string Reported = "absence.reported";
    public const string Approved = "absence.approved";
}

/// <summary>
/// BR-05: when a worker reports the customer absent, every active Admin is told so the approval queue is looked at. Best effort:
/// a failure is logged and never reaches the code that published the event.
/// </summary>
public sealed class CustomerAbsentReportedNotificationHandler(
    IAbsenceRepository absences,
    INotificationService notifications,
    ILogger<CustomerAbsentReportedNotificationHandler> logger) : INotificationHandler<CustomerAbsentReported>
{
    public async Task Handle(CustomerAbsentReported notification, CancellationToken cancellationToken)
    {
        try
        {
            var data = new Dictionary<string, string>
            {
                ["assignmentId"] = notification.AssignmentId.ToString(),
                ["orderId"] = notification.OrderId.ToString(),
            };
            foreach (var adminId in await absences.GetActiveAdminIdsAsync(cancellationToken))
            {
                await notifications.SendAsync(
                    new NotificationMessage(
                        UserRole.Admin, adminId, AbsenceNotificationTopics.Reported,
                        "Báo khách vắng mặt mới",
                        $"Thợ báo khách vắng mặt ở đơn #{notification.OrderId}. Vào hàng đợi duyệt vắng mặt để xem cuộc gọi và GPS.",
                        data),
                    cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not notify the admins of the absence report of assignment {AssignmentId}", notification.AssignmentId);
        }
    }
}

/// <summary>
/// When an Admin approves the absence, the customer learns the fee and the refund and the worker learns the compensation (Q10: 40 % to
/// the worker, 60 % back to the customer). Best effort, like the handler above.
/// </summary>
public sealed class CustomerAbsentApprovedNotificationHandler(
    IAbsenceRepository absences,
    INotificationService notifications,
    ILogger<CustomerAbsentApprovedNotificationHandler> logger) : INotificationHandler<CustomerAbsentApproved>
{
    public async Task Handle(CustomerAbsentApproved notification, CancellationToken cancellationToken)
    {
        var data = new Dictionary<string, string>
        {
            ["assignmentId"] = notification.AssignmentId.ToString(),
            ["orderId"] = notification.OrderId.ToString(),
            ["compensationAmount"] = Money(notification.CompensationAmount),
            ["refundAmount"] = Money(notification.RefundAmount),
        };

        await TrySend(notification, "the worker", async () =>
            await notifications.SendAsync(
                new NotificationMessage(
                    UserRole.Worker, notification.WorkerId, AbsenceNotificationTopics.Approved,
                    "Đã duyệt khách vắng mặt",
                    $"Đơn #{notification.OrderId}: bạn nhận {Money(notification.CompensationAmount)} đ tiền bồi hoàn do khách vắng mặt.",
                    data),
                cancellationToken));

        await TrySend(notification, "the customer", async () =>
        {
            var customerId = await absences.GetCustomerIdOfOrderAsync(notification.OrderId, cancellationToken);
            if (customerId is not int id) return;
            await notifications.SendAsync(
                new NotificationMessage(
                    UserRole.Customer, id, AbsenceNotificationTopics.Approved,
                    "Ca làm bị tính phí vắng mặt",
                    $"Đơn #{notification.OrderId}: thợ đã chờ nhưng không liên lạc được. Phí vắng mặt {Money(notification.CompensationAmount)} đ, hoàn lại {Money(notification.RefundAmount)} đ cho bạn.",
                    data),
                cancellationToken);
        });
    }

    private async Task TrySend(CustomerAbsentApproved notification, string who, Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not notify {Who} of the approved absence of assignment {AssignmentId}", who, notification.AssignmentId);
        }
    }

    /// <summary>Whole VND, no decimals (decision G-2).</summary>
    private static string Money(decimal amount) => decimal.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
}

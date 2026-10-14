using CommonService.Application.Features.Booking;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CommonService.Application.Features.Payments.Services;

/// <summary>
/// The worker declined the extension (BR-08, M4's <see cref="ExtensionDeclined"/>; contracts booking.md section 5 and payments.md 3.4):
/// ext_status and worker_decision become DECLINED with decided_at, then a 100 % refund of the extension's paid transaction.
/// Idempotent: an extension already DECLINED does nothing and is not refunded twice; an unpaid or expired extension is only marked.
/// Known gap: if the refund fails it is logged as an error and NOT retried; a new worker for the next shift is M3's.
/// </summary>
public sealed class ExtensionDeclinedHandler(
    IPaymentRepository payments,
    IUnitOfWork unitOfWork,
    IExtensionRefundService refunds,
    ILogger<ExtensionDeclinedHandler> logger) : INotificationHandler<ExtensionDeclined>
{
    public async Task Handle(ExtensionDeclined notification, CancellationToken cancellationToken)
    {
        // The event carries the key as long, the entity as int.
        if (notification.ExtensionId is < 1 or > int.MaxValue)
        {
            logger.LogWarning("ExtensionDeclined with an invalid extension id {ExtensionId} ignored.", notification.ExtensionId);
            return;
        }

        var extensionId = (int)notification.ExtensionId;
        var wasPaid = false;

        var changed = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var extension = await payments.GetExtensionForUpdateAsync(extensionId, cancellationToken);
            if (extension is null)
            {
                logger.LogWarning("ExtensionDeclined for unknown extension {ExtensionId} ignored.", extensionId);
                return false;
            }

            if (extension.ExtStatus == ExtensionStatuses.Declined)
            {
                return false;
            }

            wasPaid = extension.ExtStatus is ExtensionStatuses.Paid or ExtensionStatuses.Accepted;
            extension.ExtStatus = ExtensionStatuses.Declined;
            extension.WorkerDecision = WorkerDecisions.Declined;
            extension.DecidedAt = notification.DeclinedAtUtc;
            return true;
        }, cancellationToken);

        if (!changed || !wasPaid)
        {
            return;
        }

        var refund = await refunds.RefundExtensionAsync(extensionId, notification.Reason, cancellationToken);
        if (!refund.Succeeded)
        {
            logger.LogError(
                "Extension {ExtensionId} was declined but the refund failed: {Message}. It must be refunded by hand until a retry exists.",
                extensionId, refund.Message);
        }
    }
}

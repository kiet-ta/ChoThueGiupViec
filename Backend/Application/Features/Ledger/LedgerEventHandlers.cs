using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CommonService.Application.Features.Ledger;

// One handler per money event (decision D28). Each hands the event to ILedgerPostingService and never throws: the event was
// published after the business transaction committed, so a ledger problem is logged and the reconciliation sweep fills the gap.
// The refund is written only by OrderRefunded: the absence handler writes only ABSENCE_FEE, the verdict handler only PENALTY.

public class OrderPaidLedgerHandler(ILedgerPostingService posting, ILogger<OrderPaidLedgerHandler> logger) : INotificationHandler<OrderPaid>
{
    public async Task Handle(OrderPaid notification, CancellationToken cancellationToken)
    {
        try { await posting.PostOrderPaidAsync(notification, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Ledger: could not record the payment of order {OrderId}; the sweep will retry.", notification.OrderId);
        }
    }
}

public class ExtensionPaidLedgerHandler(ILedgerPostingService posting, ILogger<ExtensionPaidLedgerHandler> logger) : INotificationHandler<ExtensionPaid>
{
    public async Task Handle(ExtensionPaid notification, CancellationToken cancellationToken)
    {
        try { await posting.PostExtensionPaidAsync(notification, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Ledger: could not record the paid extension {ExtensionId}; the sweep will retry.", notification.ExtensionId);
        }
    }
}

public class JobCompletedLedgerHandler(ILedgerPostingService posting, ILogger<JobCompletedLedgerHandler> logger) : INotificationHandler<JobCompleted>
{
    public async Task Handle(JobCompleted notification, CancellationToken cancellationToken)
    {
        try { await posting.PostJobCompletedAsync(notification, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Ledger: could not record the earnings of assignment {AssignmentId}; the sweep will retry.", notification.AssignmentId);
        }
    }
}

public class OrderRefundedLedgerHandler(ILedgerPostingService posting, ILogger<OrderRefundedLedgerHandler> logger) : INotificationHandler<OrderRefunded>
{
    public async Task Handle(OrderRefunded notification, CancellationToken cancellationToken)
    {
        try { await posting.PostRefundAsync(notification, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Ledger: could not record the refund of order {OrderId}; the sweep will retry.", notification.OrderId);
        }
    }
}

public class CustomerAbsentApprovedLedgerHandler(ILedgerPostingService posting, ILogger<CustomerAbsentApprovedLedgerHandler> logger)
    : INotificationHandler<CustomerAbsentApproved>
{
    public async Task Handle(CustomerAbsentApproved notification, CancellationToken cancellationToken)
    {
        try { await posting.PostAbsenceFeeAsync(notification, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Ledger: could not record the absence fee of assignment {AssignmentId}; the sweep will retry.", notification.AssignmentId);
        }
    }
}

public class DisputeResolvedLedgerHandler(ILedgerPostingService posting, ILogger<DisputeResolvedLedgerHandler> logger) : INotificationHandler<DisputeResolved>
{
    public async Task Handle(DisputeResolved notification, CancellationToken cancellationToken)
    {
        try { await posting.PostPenaltyAsync(notification, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Ledger: could not record the penalty of dispute {DisputeId}, assignment {AssignmentId}; the sweep will retry.",
                notification.DisputeId, notification.AssignmentId);
        }
    }
}

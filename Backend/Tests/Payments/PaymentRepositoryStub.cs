using CommonService.Application.Features.Payments;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Entities;

namespace CommonService.Tests.Payments;

/// <summary>Base of in-memory test repositories: every member throws until a test overrides what it needs, plus a pass-through unit of work.</summary>
internal abstract class PaymentRepositoryStub : IPaymentRepository, IUnitOfWork
{
    public virtual Task<OrderForPayment?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<PaymentTransaction?> FindPendingOrderPaymentAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual void Add(PaymentTransaction transaction) => throw new NotSupportedException();
    public virtual Task<PaymentTransaction?> FindByGatewayRefAsync(string gatewayTxnRef, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<bool> TryMarkSuccessAsync(long paymentId, DateTime paidAtUtc, string ipnPayload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<bool> TryMarkExpiredAsync(long paymentId, string ipnPayload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<PaymentTransaction>> ListPendingOrderPaymentsCreatedBeforeAsync(DateTime cutoffUtc, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<long>> ListUnpaidOrderIdsWithoutLivePaymentAsync(DateTime createdBeforeUtc, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<PaymentTransaction?> FindRefundableOrderPaymentAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<bool> TryReserveRefundAsync(long paymentId, decimal refundAmount, string reason, DateTime refundedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task RevertRefundAsync(long paymentId, decimal refundAmount, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<ExtensionForPayment?> GetExtensionAsync(int extensionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<PaymentTransaction?> FindPendingExtensionPaymentAsync(int extensionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<PaymentTransaction>> ListPendingExtensionPaymentsCreatedBeforeAsync(DateTime cutoffUtc, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<PaymentTransaction?> FindRefundableExtensionPaymentAsync(int extensionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<JobOrderExtension?> GetExtensionForUpdateAsync(int extensionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<PaymentTransaction?> GetPaymentAsync(long paymentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<IReadOnlyList<PaymentTransaction>> ListPaymentsOfOrderAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public virtual Task<JobOrder?> GetOrderForUpdateAsync(long orderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default) => action();
}

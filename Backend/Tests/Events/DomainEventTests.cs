using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Tests.Booking;
using CommonService.Tests.Dispatch;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Tests.Events;

public class DomainEventTests
{
    [Fact]
    public async Task Cross_module_publish_subscribe_reaches_handlers_in_different_module_folders()
    {
        DispatchOrderPaidTestHandler.Received.Clear();
        BookingOrderPaidTestHandler.Received.Clear();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(DomainEventTests).Assembly);
        });

        using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IPublisher>();

        var evt = new OrderPaid(
            OrderId: 1001,
            CustomerId: 42,
            Amount: 260_000m,
            ShiftCode: "SHIFT_1",
            ScheduledDate: new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc),
            RequiredWorkers: 1);

        await publisher.Publish(evt);

        Assert.Single(DispatchOrderPaidTestHandler.Received);
        Assert.Single(BookingOrderPaidTestHandler.Received);
        Assert.Equal(1001, DispatchOrderPaidTestHandler.Received[0].OrderId);
        Assert.Equal(1001, BookingOrderPaidTestHandler.Received[0].OrderId);
    }

    [Fact]
    public void All_overview_section_5_domain_events_implement_INotification()
    {
        var expectedEventTypes = new[]
        {
            typeof(OrderPaid),
            typeof(OrderCancelled),
            typeof(OrderRefunded),
            typeof(JobAssigned),
            typeof(AssignmentFailed),
            typeof(WorkerCheckedIn),
            typeof(CustomerAbsentReported),
            typeof(CustomerAbsentApproved),
            typeof(IncidentReported),
            typeof(JobCompleted),
            typeof(ExtensionPaid),
            typeof(ExtensionDeclined),
            typeof(SubscriptionActivated),
            typeof(DisputeResolved),
            typeof(RatingSubmitted),
            typeof(PayoutBatchClosed)
        };

        foreach (var type in expectedEventTypes)
        {
            Assert.True(typeof(INotification).IsAssignableFrom(type),
                $"{type.Name} does not implement INotification");
        }
    }

    [Fact]
    public async Task All_domain_events_can_be_instantiated_and_published()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(DomainEventTests).Assembly);
        });

        using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IPublisher>();
        var now = DateTime.UtcNow;

        var events = new INotification[]
        {
            new OrderPaid(1, 1, 100000m, "S1", now, 1),
            new OrderCancelled(1, 1, "Customer cancelled", now),
            new OrderRefunded(1, 1, 100000m, "Refund", now),
            new JobAssigned(1, 1, 2, null, 10, now),
            new AssignmentFailed(1, "No worker found", now),
            new WorkerCheckedIn(1, 1, 2, now, 25.0),
            new CustomerAbsentReported(1, 1, 2, now),
            new CustomerAbsentApproved(1, 1, 2, 40000m, 60000m, now),
            new IncidentReported(1, 1, 1, 2, "EQUIPMENT_BROKEN", "Broken mop", now),
            new JobCompleted(1, 1, 2, null, 80000m, now),
            new ExtensionPaid(1, 1, 2, 1, 65000m, now),
            new ExtensionDeclined(1, 1, 2, "Busy", now),
            new SubscriptionActivated(1, 10, 2, "PRO_MONTHLY", now, now.AddMonths(1)),
            new DisputeResolved(1, 1, FaultParty.Freelancer, 100000m, "Uphold customer claim", now),
            new RatingSubmitted(1, 1, "CUSTOMER", 5, now),
            new PayoutBatchClosed(1, "2026-10", 50000000m, 25, now)
        };

        foreach (var evt in events)
        {
            await publisher.Publish(evt);
        }

        Assert.Equal(16, events.Length);
    }
}

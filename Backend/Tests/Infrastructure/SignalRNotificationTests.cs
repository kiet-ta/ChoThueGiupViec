using CommonService.Application.Common.Models;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modularity;
using CommonService.Infrastructure.Realtime;
using CommonService.WebAPI.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Tests.Infrastructure;

public class SignalRNotificationTests
{
    // =========================================================================
    // In-memory test doubles for SignalR HubContext
    // =========================================================================

    private class TestNotificationClient : INotificationClient
    {
        public List<NotificationMessageDto> Received { get; } = new();

        public Task ReceiveNotification(NotificationMessageDto notification)
        {
            Received.Add(notification);
            return Task.CompletedTask;
        }
    }

    private class TestHubClients : IHubClients<INotificationClient>
    {
        public Dictionary<string, TestNotificationClient> GroupsMap { get; } = new(StringComparer.OrdinalIgnoreCase);

        public INotificationClient Group(string groupName)
        {
            if (!GroupsMap.TryGetValue(groupName, out var client))
            {
                client = new TestNotificationClient();
                GroupsMap[groupName] = client;
            }
            return client;
        }

        public INotificationClient All => throw new NotImplementedException();
        public INotificationClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotImplementedException();
        public INotificationClient Client(string connectionId) => throw new NotImplementedException();
        public INotificationClient Clients(IReadOnlyList<string> connectionIds) => throw new NotImplementedException();
        public INotificationClient Groups(IReadOnlyList<string> groupNames) => throw new NotImplementedException();
        public INotificationClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotImplementedException();
        public INotificationClient User(string userId) => throw new NotImplementedException();
        public INotificationClient Users(IReadOnlyList<string> userIds) => throw new NotImplementedException();
    }

    private class TestHubContext : IHubContext<NotificationHub, INotificationClient>
    {
        public TestHubClients ClientsMock { get; } = new();
        public IHubClients<INotificationClient> Clients => ClientsMock;
        public IGroupManager Groups => throw new NotImplementedException();
    }

    // =========================================================================
    // Tests
    // =========================================================================

    [Fact]
    public async Task Publishing_customer_payment_status_reaches_subscribed_user_group()
    {
        var hubContext = new TestHubContext();
        var store = new InMemoryNotificationStore();
        var service = new SignalRNotificationService(hubContext, store, NullLogger<SignalRNotificationService>.Instance);

        var message = new NotificationMessage(
            RecipientRole: UserRole.Customer,
            RecipientId: 101,
            Topic: "payment.status",
            Title: "Payment Successful",
            Body: "Your payment of 240,000 VND for order #123 is confirmed.",
            Data: new Dictionary<string, string>
            {
                ["orderId"] = "123",
                ["status"] = "PAID",
                ["amount"] = "240000"
            }
        );

        // Act: Send notification
        await service.SendAsync(message);

        // Assert: Reached the customer:101 group
        var customerGroup = "customer:101";
        Assert.True(hubContext.ClientsMock.GroupsMap.ContainsKey(customerGroup));

        var client = hubContext.ClientsMock.GroupsMap[customerGroup];
        Assert.Single(client.Received);

        var received = client.Received[0];
        Assert.Equal("Customer", received.RecipientRole);
        Assert.Equal(101, received.RecipientId);
        Assert.Equal("payment.status", received.Topic);
        Assert.Equal("Payment Successful", received.Title);
        Assert.Contains("240,000 VND", received.Body);
        Assert.Equal("123", received.Data?["orderId"]);
    }

    [Fact]
    public async Task Publishing_job_tracking_notification_broadcasts_to_topic_and_user_groups()
    {
        var hubContext = new TestHubContext();
        var store = new InMemoryNotificationStore();
        var service = new SignalRNotificationService(hubContext, store, NullLogger<SignalRNotificationService>.Instance);

        var message = new NotificationMessage(
            RecipientRole: UserRole.Customer,
            RecipientId: 202,
            Topic: "job.tracking",
            Title: "Worker Checked In",
            Body: "Worker Nguyen Van A has arrived at your address.",
            Data: new Dictionary<string, string>
            {
                ["assignmentId"] = "888",
                ["workerId"] = "45",
                ["status"] = "IN_PROGRESS"
            }
        );

        await service.SendAsync(message);

        // Assert: Reached user group
        var userGroup = "customer:202";
        Assert.True(hubContext.ClientsMock.GroupsMap.ContainsKey(userGroup));
        Assert.Single(hubContext.ClientsMock.GroupsMap[userGroup].Received);

        // Assert: Also broadcast to topic group
        var topicGroup = "topic:job.tracking";
        Assert.True(hubContext.ClientsMock.GroupsMap.ContainsKey(topicGroup));
        Assert.Single(hubContext.ClientsMock.GroupsMap[topicGroup].Received);
    }

    [Fact]
    public async Task Notifications_are_stored_and_retrievable_via_polling_fallback()
    {
        var hubContext = new TestHubContext();
        var store = new InMemoryNotificationStore();
        var service = new SignalRNotificationService(hubContext, store, NullLogger<SignalRNotificationService>.Instance);

        var now = DateTime.UtcNow;
        var message1 = new NotificationMessage(UserRole.Worker, 55, "worker.offer", "New Job Offer", "Shift 08:00 - 12:00");
        var message2 = new NotificationMessage(UserRole.Worker, 55, "job.tracking", "Customer Update", "Customer provided notes");

        await service.SendAsync(message1);
        await service.SendAsync(message2);

        // Poll via store
        var allRecent = store.GetRecent("Worker", 55);
        Assert.Equal(2, allRecent.Count);

        // Filter by since
        var filtered = store.GetRecent("Worker", 55, since: now.AddSeconds(5));
        Assert.Empty(filtered);
    }

    [Fact]
    public void NotificationsController_Poll_returns_recent_notifications_for_user()
    {
        var store = new InMemoryNotificationStore();
        store.Add(new NotificationMessageDto("Customer", 99, "payment.status", "Paid", "Order 1", TimestampUtc: DateTime.UtcNow));
        store.Add(new NotificationMessageDto("Customer", 99, "job.tracking", "Arrived", "Order 1", TimestampUtc: DateTime.UtcNow));
        store.Add(new NotificationMessageDto("Worker", 77, "worker.offer", "Offer", "Job 2", TimestampUtc: DateTime.UtcNow));

        var fakeUser = new FakeCurrentUser();
        var controller = new NotificationsController(store, fakeUser);

        // Poll for customer 99
        var actionResult = controller.Poll(role: "Customer", userId: 99, since: null);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<ApiResponse<IReadOnlyList<NotificationMessageDto>>>(okResult.Value);

        Assert.True(response.Success);
        Assert.Equal(2, response.Data?.Count);
    }

    [Fact]
    public void NotificationsController_Poll_uses_authenticated_user_claims()
    {
        var store = new InMemoryNotificationStore();
        store.Add(new NotificationMessageDto("Customer", 42, "payment.status", "Paid", "Order 42", TimestampUtc: DateTime.UtcNow));

        var fakeUser = new FakeCurrentUser();
        fakeUser.SignIn(42, UserRole.Customer);
        var controller = new NotificationsController(store, fakeUser);

        // Call without query parameters: automatically defaults to authenticated user
        var actionResult = controller.Poll();
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<ApiResponse<IReadOnlyList<NotificationMessageDto>>>(okResult.Value);

        Assert.True(response.Success);
        Assert.Single(response.Data!);
        Assert.Equal(42, response.Data![0].RecipientId);
    }

    [Fact]
    public void NotificationsController_Poll_rejects_missing_role_and_userId_when_unauthenticated()
    {
        var store = new InMemoryNotificationStore();
        var fakeUser = new FakeCurrentUser();
        var controller = new NotificationsController(store, fakeUser);

        var actionResult = controller.Poll(role: null, userId: null, since: null);
        var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var response = Assert.IsType<ApiResponse<object>>(badRequest.Value);

        Assert.False(response.Success);
    }

    [Fact]
    public void RealtimeModule_registers_SignalRNotificationService_and_overrides_Fake()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder().Build();

        // Register RealtimeModule
        var module = new RealtimeModule();
        module.ConfigureServices(services, config);

        // Register fake ports with TryAdd
        services.AddFakePorts();

        using var provider = services.BuildServiceProvider();
        var notificationService = provider.GetRequiredService<INotificationService>();

        // Real implementation wins over FakeNotificationService
        Assert.IsType<SignalRNotificationService>(notificationService);
    }
}

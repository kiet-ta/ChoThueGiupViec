using System.Reflection;
using System.Security.Claims;
using CommonService.Application.Common.Models;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modularity;
using CommonService.Infrastructure.Realtime;
using CommonService.WebAPI.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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

    private class TestCallerContext(string connectionId, ClaimsPrincipal user) : HubCallerContext
    {
        public override string ConnectionId => connectionId;
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private class RecordingGroupManager : IGroupManager
    {
        public List<(string ConnectionId, string Group)> Added { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add((connectionId, groupName));
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
    public async Task A_message_reaches_only_its_recipients_group_never_a_topic_group()
    {
        // D36 (#894, B4): no per-topic broadcast, so listening to "job.tracking" can no longer read other customers' events.
        var hubContext = new TestHubContext();
        var store = new InMemoryNotificationStore();
        var service = new SignalRNotificationService(hubContext, store, NullLogger<SignalRNotificationService>.Instance);

        await service.SendAsync(new NotificationMessage(
            UserRole.Customer, 202, "job.tracking", "Worker Checked In", "Worker has arrived.",
            new Dictionary<string, string> { ["assignmentId"] = "888" }));

        var group = Assert.Single(hubContext.ClientsMock.GroupsMap);
        Assert.Equal("customer:202", group.Key);
        Assert.Equal("job.tracking", Assert.Single(group.Value.Received).Topic);
    }

    [Fact]
    public async Task Hub_puts_an_authenticated_connection_in_its_own_group_only_and_an_anonymous_one_in_none()
    {
        // D36 (#894, B2): the group comes from the token; there is no JoinUserGroup / JoinTopic to call.
        var signedIn = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "55"), new Claim(ClaimTypes.Role, "Worker")], authenticationType: "Bearer"));
        var groups = new RecordingGroupManager();
        await new NotificationHub { Context = new TestCallerContext("c1", signedIn), Groups = groups }.OnConnectedAsync();
        Assert.Equal([("c1", "worker:55")], groups.Added);

        var anonymous = new RecordingGroupManager();
        await new NotificationHub { Context = new TestCallerContext("c2", new ClaimsPrincipal(new ClaimsIdentity())), Groups = anonymous }.OnConnectedAsync();
        Assert.Empty(anonymous.Added);

        Assert.Null(NotificationHub.ResolveUserGroup(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Worker")], authenticationType: "Bearer")))); // no id claim: no group

        var callable = typeof(NotificationHub).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name).Where(n => n != nameof(NotificationHub.OnConnectedAsync));
        Assert.Empty(callable); // nothing a client can invoke
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
    public void NotificationsController_Poll_returns_only_the_callers_own_notifications()
    {
        var store = new InMemoryNotificationStore();
        store.Add(new NotificationMessageDto("Customer", 42, "payment.status", "Paid", "Order 42", TimestampUtc: DateTime.UtcNow));
        store.Add(new NotificationMessageDto("Customer", 99, "payment.status", "Paid", "Order 99", TimestampUtc: DateTime.UtcNow));
        store.Add(new NotificationMessageDto("Worker", 42, "worker.offer", "Offer", "Job 2", TimestampUtc: DateTime.UtcNow));

        var fakeUser = new FakeCurrentUser();
        fakeUser.SignIn(42, UserRole.Customer);
        var controller = new NotificationsController(store, fakeUser);

        var actionResult = controller.Poll();
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<ApiResponse<IReadOnlyList<NotificationMessageDto>>>(okResult.Value);

        Assert.True(response.Success);
        var item = Assert.Single(response.Data!); // not customer 99's, not worker 42's
        Assert.Equal(("Customer", 42), (item.RecipientRole, item.RecipientId));
    }

    [Fact]
    public void NotificationsController_Poll_is_401_when_anonymous_and_has_no_role_or_userId_parameter()
    {
        // D36 (#894, B3): another user's notifications cannot be asked for.
        var controller = new NotificationsController(new InMemoryNotificationStore(), new FakeCurrentUser());
        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(controller.Poll().Result);
        Assert.False(Assert.IsType<ApiResponse<object>>(unauthorized.Value).Success);

        Assert.NotNull(typeof(NotificationsController).GetCustomAttribute<AuthorizeAttribute>());
        var parameters = typeof(NotificationsController).GetMethod(nameof(NotificationsController.Poll))!.GetParameters().Select(p => p.Name);
        Assert.Equal(["since", "limit"], parameters);
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

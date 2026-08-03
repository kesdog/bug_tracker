using System.Diagnostics;
using System.Net.WebSockets;
using BugTracker.Api.Auth;
using BugTracker.Api.Notifications;
using Microsoft.Extensions.Options;
using Xunit;

namespace BugTracker.Api.Tests;

public sealed class AgentNotificationSocketHubTests
{
    private static readonly AuthenticatedUser Principal = new(
        "agent-1", "agent@example.com", "dev", "agent", "token-hash", DateTimeOffset.UtcNow.AddHours(1));

    [Fact]
    public void Options_RejectInvalidCapsAndCloseTimeout()
    {
        Assert.Throws<InvalidOperationException>(() => new AgentNotificationSocketHub(Options.Create(
            new AgentWebSocketOptions { MaxConnections = 0 })));
        Assert.Throws<InvalidOperationException>(() => new AgentNotificationSocketHub(Options.Create(
            new AgentWebSocketOptions { MaxConnections = 2, MaxConnectionsPerUser = 3 })));
        Assert.Throws<InvalidOperationException>(() => new AgentNotificationSocketHub(Options.Create(
            new AgentWebSocketOptions { CloseTimeoutSeconds = 31 })));
    }

    [Fact]
    public async Task SessionIsRevalidatedImmediatelyBeforeHello()
    {
        var hub = new AgentNotificationSocketHub();
        using var socket = new TestWebSocket(hangOnClose: false);

        await hub.HandleConnectionAsync(
            Principal,
            socket,
            _ => Task.FromResult<IReadOnlyList<NotificationDto>>([]),
            _ => Task.FromResult<AuthenticatedUser?>(null),
            () => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(0, socket.SendCount);
        Assert.Equal(1, socket.CloseCount);
    }

    [Fact]
    public async Task PauseClosesSocketsConcurrently_AndAbortsAfterConfiguredBound()
    {
        var hub = new AgentNotificationSocketHub(Options.Create(new AgentWebSocketOptions
        {
            MaxConnections = 2,
            MaxConnectionsPerUser = 2,
            CloseTimeoutSeconds = 1
        }));
        using var first = new TestWebSocket(hangOnClose: true);
        using var second = new TestWebSocket(hangOnClose: true);
        using var requests = new CancellationTokenSource();

        var firstHandler = ConnectAsync(hub, first, requests.Token);
        var secondHandler = ConnectAsync(hub, second, requests.Token);
        await Task.WhenAll(first.HelloSent.Task, second.HelloSent.Task).WaitAsync(TimeSpan.FromSeconds(2));

        var stopwatch = Stopwatch.StartNew();
        await hub.PauseAndCloseAllAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(1800), $"Close took {stopwatch.Elapsed}.");
        Assert.Equal(1, first.AbortCount);
        Assert.Equal(1, second.AbortCount);
        await Task.WhenAll(firstHandler, secondHandler).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task PauseWaitsForDisconnectedLifecyclePersistenceBeforeReturning()
    {
        var hub = new AgentNotificationSocketHub(Options.Create(new AgentWebSocketOptions
        {
            CloseTimeoutSeconds = 1
        }));
        using var socket = new TestWebSocket(hangOnClose: false);
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var handler = hub.HandleConnectionAsync(
            Principal,
            socket,
            _ => Task.FromResult<IReadOnlyList<NotificationDto>>([]),
            _ => Task.FromResult<AuthenticatedUser?>(Principal),
            () => Task.CompletedTask,
            async (_, _) =>
            {
                callbackStarted.TrySetResult();
                await releaseCallback.Task;
            },
            CancellationToken.None);
        await socket.HelloSent.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var pause = hub.PauseAndCloseAllAsync(CancellationToken.None);
        await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(pause.IsCompleted);

        releaseCallback.TrySetResult();
        await pause.WaitAsync(TimeSpan.FromSeconds(2));
        await handler.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task GlobalCapRejectsExcessSocket()
    {
        var hub = new AgentNotificationSocketHub(Options.Create(new AgentWebSocketOptions
        {
            MaxConnections = 1,
            MaxConnectionsPerUser = 1,
            CloseTimeoutSeconds = 1
        }));
        using var accepted = new TestWebSocket(hangOnClose: false);
        using var excess = new TestWebSocket(hangOnClose: false);
        using var requests = new CancellationTokenSource();

        var acceptedHandler = ConnectAsync(hub, accepted, requests.Token);
        await accepted.HelloSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await ConnectAsync(hub, excess, requests.Token);

        Assert.Equal(0, excess.SendCount);
        Assert.Equal(1, excess.CloseCount);
        requests.Cancel();
        await acceptedHandler.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task PerUserCapRejectsExcessSocketBelowGlobalCap()
    {
        var hub = new AgentNotificationSocketHub(Options.Create(new AgentWebSocketOptions
        {
            MaxConnections = 2,
            MaxConnectionsPerUser = 1,
            CloseTimeoutSeconds = 1
        }));
        using var accepted = new TestWebSocket(hangOnClose: false);
        using var excess = new TestWebSocket(hangOnClose: false);
        using var requests = new CancellationTokenSource();

        var acceptedHandler = ConnectAsync(hub, accepted, requests.Token);
        await accepted.HelloSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await ConnectAsync(hub, excess, requests.Token);

        Assert.Equal(0, excess.SendCount);
        Assert.Equal(1, excess.CloseCount);
        requests.Cancel();
        await acceptedHandler.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task HeartbeatRevalidation_ClosesRevokedSessionBeforeFurtherNotificationDelivery()
    {
        var hub = new AgentNotificationSocketHub(Options.Create(new AgentWebSocketOptions
        {
            HeartbeatIntervalSeconds = 1,
            HeartbeatRetryIntervalSeconds = 1,
            CloseTimeoutSeconds = 1
        }));
        using var socket = new TestWebSocket(hangOnClose: false);
        var validationCount = 0;

        var handler = hub.HandleConnectionAsync(
            Principal,
            socket,
            _ => Task.FromResult<IReadOnlyList<NotificationDto>>([]),
            _ => Task.FromResult<AuthenticatedUser?>(
                Interlocked.Increment(ref validationCount) == 1 ? Principal : null),
            () => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            CancellationToken.None);
        await socket.HelloSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await handler.WaitAsync(TimeSpan.FromSeconds(3));

        await hub.SendNotificationAsync(new NotificationDto(
            "notification-1", Principal.UserId, "ticket-1", "ticket_assigned", "message", false,
            DateTimeOffset.UtcNow.ToString("O")), CancellationToken.None);

        Assert.Equal(2, validationCount);
        Assert.Equal(1, socket.SendCount);
        Assert.Equal(1, socket.CloseCount);
    }

    [Fact]
    public async Task HeartbeatRevalidation_ClosesSessionWhenAgentTypeChanges()
    {
        var hub = new AgentNotificationSocketHub(Options.Create(new AgentWebSocketOptions
        {
            HeartbeatIntervalSeconds = 1,
            HeartbeatRetryIntervalSeconds = 1,
            CloseTimeoutSeconds = 1
        }));
        using var socket = new TestWebSocket(hangOnClose: false);
        var validationCount = 0;
        var changed = Principal with { UserType = "human" };

        await hub.HandleConnectionAsync(
            Principal,
            socket,
            _ => Task.FromResult<IReadOnlyList<NotificationDto>>([]),
            _ => Task.FromResult<AuthenticatedUser?>(
                Interlocked.Increment(ref validationCount) == 1 ? Principal : changed),
            () => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(2, validationCount);
        Assert.Equal(1, socket.SendCount);
        Assert.Equal(1, socket.CloseCount);
    }

    [Fact]
    public async Task EstablishmentCompletionOccursAfterRegistrationValidationAndHello()
    {
        var hub = new AgentNotificationSocketHub();
        using var socket = new TestWebSocket(hangOnClose: false);
        using var request = new CancellationTokenSource();
        var completionObserved = false;

        var handler = hub.HandleConnectionAsync(
            Principal,
            socket,
            _ => Task.FromResult<IReadOnlyList<NotificationDto>>([]),
            _ => Task.FromResult<AuthenticatedUser?>(Principal),
            () =>
            {
                Assert.True(hub.IsUserConnected(Principal.UserId));
                Assert.Equal(1, socket.SendCount);
                completionObserved = true;
                return Task.CompletedTask;
            },
            (_, _) => Task.CompletedTask,
            request.Token);
        await socket.HelloSent.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(completionObserved);
        request.Cancel();
        await handler.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task TokenExpiry_ClosesEstablishedSocketWithoutWaitingForHeartbeat()
    {
        var expiringPrincipal = Principal with { TokenExpiresAt = DateTimeOffset.UtcNow.AddMilliseconds(250) };
        var hub = new AgentNotificationSocketHub();
        using var socket = new TestWebSocket(hangOnClose: false);

        await hub.HandleConnectionAsync(
            expiringPrincipal,
            socket,
            _ => Task.FromResult<IReadOnlyList<NotificationDto>>([]),
            _ => Task.FromResult<AuthenticatedUser?>(expiringPrincipal),
            () => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, socket.SendCount);
        Assert.Equal(1, socket.CloseCount);
    }

    private static Task ConnectAsync(AgentNotificationSocketHub hub, TestWebSocket socket, CancellationToken ct) =>
        hub.HandleConnectionAsync(
            Principal,
            socket,
            _ => Task.FromResult<IReadOnlyList<NotificationDto>>([]),
            _ => Task.FromResult<AuthenticatedUser?>(Principal),
            () => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            ct);

    private sealed class TestWebSocket(bool hangOnClose) : WebSocket
    {
        private readonly CancellationTokenSource _aborted = new();
        private WebSocketState _state = WebSocketState.Open;
        private int _sendCount;
        private int _closeCount;
        private int _abortCount;

        public TaskCompletionSource HelloSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SendCount => Volatile.Read(ref _sendCount);
        public int CloseCount => Volatile.Read(ref _closeCount);
        public int AbortCount => Volatile.Read(ref _abortCount);
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort()
        {
            Interlocked.Increment(ref _abortCount);
            _state = WebSocketState.Aborted;
            _aborted.Cancel();
        }

        public override async Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _closeCount);
            if (hangOnClose)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, _aborted.Token);
                return;
            }
            _state = WebSocketState.Closed;
            _aborted.Cancel();
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            CloseAsync(closeStatus, statusDescription, cancellationToken);

        public override void Dispose()
        {
            _aborted.Cancel();
            _aborted.Dispose();
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _aborted.Token);
            await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
            throw new UnreachableException();
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _sendCount);
            HelloSent.TrySetResult();
            return Task.CompletedTask;
        }
    }
}

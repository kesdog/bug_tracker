using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BugTracker.Api.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BugTracker.Api.Notifications;

public interface IAgentNotificationPublisher
{
    Task SendNotificationAsync(NotificationDto notification, CancellationToken ct);
}

public sealed class AgentNotificationSocketHub : IAgentNotificationPublisher
{
    private const WebSocketCloseStatus ServiceRestartStatus = (WebSocketCloseStatus)1012;
    private const int MaxPingRetries = 5;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, SocketConnection>> _connections = new(StringComparer.Ordinal);
    private readonly object _registrySync = new();
    private readonly AgentWebSocketOptions _options;
    private readonly ILogger<AgentNotificationSocketHub> _logger;
    private bool _acceptingConnections = true;
    private int _connectionCount;

    public AgentNotificationSocketHub(
        IOptions<AgentWebSocketOptions>? configuredOptions = null,
        ILogger<AgentNotificationSocketHub>? logger = null)
    {
        _options = configuredOptions?.Value ?? new AgentWebSocketOptions();
        _options.Validate();
        _logger = logger ?? NullLogger<AgentNotificationSocketHub>.Instance;
    }

    public async Task HandleConnectionAsync(
        AuthenticatedUser principal,
        WebSocket socket,
        Func<CancellationToken, Task<IReadOnlyList<NotificationDto>>> unreadLoader,
        Func<CancellationToken, Task<AuthenticatedUser?>> revalidateSession,
        Func<Task> establishmentCompleted,
        Func<WebSocketState, CancellationToken, Task> connectionCompleted,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var maxDuration = principal.TokenExpiresAt - now;
        if (maxDuration <= TimeSpan.Zero)
        {
            await CloseRawSocketWithTimeoutAndAbortAsync(socket, WebSocketCloseStatus.PolicyViolation, "token expired");
            await InvokeConnectionCompletedAsync(connectionCompleted, socket.State);
            return;
        }

        var connectionId = Guid.NewGuid();
        var connection = new SocketConnection(socket, new SemaphoreSlim(1, 1), new SemaphoreSlim(1, 1));
        var establishmentReleased = false;
        await connection.EstablishmentGate.WaitAsync(ct);
        lock (_registrySync)
        {
            if (!_acceptingConnections
                || _connectionCount >= _options.MaxConnections
                || (_connections.TryGetValue(principal.UserId, out var existingConnections)
                    && existingConnections.Count >= _options.MaxConnectionsPerUser))
            {
                connection.EstablishmentGate.Release();
                establishmentReleased = true;
            }
            else
            {
                var userConnections = _connections.GetOrAdd(principal.UserId, _ => new ConcurrentDictionary<Guid, SocketConnection>());
                userConnections[connectionId] = connection;
                _connectionCount++;
            }
        }

        if (establishmentReleased)
        {
            await CloseWithTimeoutAndAbortAsync(connection, ServiceRestartStatus, "connection unavailable", CancellationToken.None);
            await InvokeConnectionCompletedAsync(connectionCompleted, socket.State);
            connection.WriteLock.Dispose();
            return;
        }
        using var expiryCts = new CancellationTokenSource(maxDuration);
        using var lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(ct, expiryCts.Token);

        try
        {
            var unreadNotifications = await unreadLoader(lifetimeCts.Token);
            var currentPrincipal = await revalidateSession(lifetimeCts.Token);
            if (!IsSameAgentSession(principal, currentPrincipal))
            {
                connection.InvalidateSession();
                await CloseWithTimeoutAndAbortAsync(
                    connection, WebSocketCloseStatus.PolicyViolation, "session no longer valid", CancellationToken.None);
                return;
            }
            await SendAsync(connection, new
            {
                type = "hello",
                userId = principal.UserId,
                unread = unreadNotifications,
                tokenExpiresAt = principal.TokenExpiresAt,
                maxDurationSeconds = (int)Math.Ceiling(maxDuration.TotalSeconds),
                heartbeat = new
                {
                    intervalSeconds = _options.HeartbeatIntervalSeconds,
                    retryIntervalSeconds = _options.HeartbeatRetryIntervalSeconds,
                    maxRetries = MaxPingRetries,
                    clientResponse = new { type = "pong" }
                },
                agentInstructions = AgentSocketSessionInstructions.Current,
                serverTime = DateTimeOffset.UtcNow
            }, lifetimeCts.Token);
            await establishmentCompleted();
            foreach (var notification in unreadNotifications)
            {
                connection.DeliveredNotificationIds.TryAdd(notification.Id, 0);
            }
            connection.EstablishmentGate.Release();
            establishmentReleased = true;

            var receiveTask = ReceiveUntilClosedAsync(connection, lifetimeCts.Token);
            var heartbeatTask = RunHeartbeatAsync(connection, principal, revalidateSession, lifetimeCts.Token);
            await Task.WhenAny(receiveTask, heartbeatTask);
            await lifetimeCts.CancelAsync();

            try
            {
                await Task.WhenAll(receiveTask, heartbeatTask);
            }
            catch (OperationCanceledException)
            {
                // Expected when token lifetime ends, request is aborted, or the peer disconnects.
            }
            catch (WebSocketException)
            {
                // Dead sockets are removed in finally.
            }
        }
        finally
        {
            if (!establishmentReleased)
            {
                connection.EstablishmentGate.Release();
            }
            if (expiryCts.IsCancellationRequested)
            {
                await CloseWithTimeoutAndAbortAsync(connection, WebSocketCloseStatus.NormalClosure, "token expired", CancellationToken.None);
            }

            try
            {
                await InvokeConnectionCompletedAsync(connectionCompleted, socket.State);
            }
            finally
            {
                RemoveConnection(principal.UserId, connectionId);
                connection.WriteLock.Dispose();
                connection.Completion.TrySetResult();
            }
        }
    }

    public async Task PauseAndCloseAllAsync(CancellationToken ct)
    {
        SocketConnection[] connections;
        lock (_registrySync)
        {
            _acceptingConnections = false;
            connections = _connections.Values.SelectMany(value => value.Values).ToArray();
        }

        await Task.WhenAll(connections.Select(connection =>
            CloseWithTimeoutAndAbortAsync(connection, ServiceRestartStatus, "service restart", ct)));
        using var completionTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        completionTimeout.CancelAfter(TimeSpan.FromSeconds(_options.CloseTimeoutSeconds));
        try
        {
            await Task.WhenAll(connections.Select(connection => connection.Completion.Task)).WaitAsync(completionTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            foreach (var connection in connections.Where(connection => !connection.Completion.Task.IsCompleted))
            {
                try
                {
                    connection.Socket.Abort();
                }
                catch (Exception error)
                {
                    _logger.LogDebug(error, "WebSocket abort failed while draining connection lifecycle completion.");
                }
            }

            if (!ct.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Agent WebSocket connection lifecycle did not complete within {_options.CloseTimeoutSeconds} seconds.");
            }
            throw;
        }
    }

    public async Task CloseUserConnectionsAsync(string userId, CancellationToken ct)
    {
        if (!_connections.TryGetValue(userId, out var userConnections))
        {
            return;
        }

        var connections = userConnections.Values.ToArray();
        await Task.WhenAll(connections.Select(connection =>
            CloseWithTimeoutAndAbortAsync(connection, WebSocketCloseStatus.PolicyViolation, "credential rotated", ct)));
    }

    public void ResumeConnections()
    {
        lock (_registrySync)
        {
            _acceptingConnections = true;
        }
    }

    public async Task SendNotificationAsync(NotificationDto notification, CancellationToken ct)
    {
        if (!_connections.TryGetValue(notification.UserId, out var userConnections))
        {
            return;
        }

        var payload = new
        {
            type = ToEventType(notification.Kind),
            eventId = notification.EventId,
            ticketVersion = notification.TicketVersion,
            actionRequired = notification.TicketId is not null,
            notification,
            links = notification.TicketId is null ? null : new { ticket = $"/api/bugs/{notification.TicketId}" },
            agentInstructions = notification.TicketId is null ? null : AgentNotificationInstructions.ForTicket(notification.TicketId, notification.Id),
            serverTime = DateTimeOffset.UtcNow
        };

        foreach (var connection in userConnections.Values.ToArray())
        {
            if (connection.Socket.State != WebSocketState.Open)
            {
                continue;
            }

            try
            {
                await connection.EstablishmentGate.WaitAsync(ct);
                try
                {
                    if (connection.IsSessionValid
                        && connection.DeliveredNotificationIds.TryAdd(notification.Id, 0))
                    {
                        await SendAsync(connection, payload, ct);
                    }
                }
                finally
                {
                    connection.EstablishmentGate.Release();
                }
            }
            catch (WebSocketException)
            {
                connection.Socket.Abort();
            }
            catch (OperationCanceledException)
            {
                connection.Socket.Abort();
            }
        }
    }

    public bool IsUserConnected(string userId)
    {
        if (!_connections.TryGetValue(userId, out var userConnections))
        {
            return false;
        }

        return userConnections.Values.Any(connection => connection.Socket.State == WebSocketState.Open);
    }

    private async Task ReceiveUntilClosedAsync(SocketConnection connection, CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (!ct.IsCancellationRequested && connection.Socket.State == WebSocketState.Open)
        {
            var result = await connection.Socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await CloseWithTimeoutAndAbortAsync(
                    connection, WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
                return;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            if (IsPing(message))
            {
                await SendAsync(connection, new { type = "pong", serverTime = DateTimeOffset.UtcNow }, ct);
            }

            if (IsPong(message))
            {
                connection.MarkPong();
            }
        }
    }

    private async Task RunHeartbeatAsync(
        SocketConnection connection,
        AuthenticatedUser principal,
        Func<CancellationToken, Task<AuthenticatedUser?>> revalidateSession,
        CancellationToken ct)
    {
        var pingInterval = TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds);
        var retryInterval = TimeSpan.FromSeconds(_options.HeartbeatRetryIntervalSeconds);
        while (!ct.IsCancellationRequested && connection.Socket.State == WebSocketState.Open)
        {
            await Task.Delay(pingInterval, ct);
            if (connection.Socket.State != WebSocketState.Open)
            {
                return;
            }

            var sessionValid = false;
            await connection.EstablishmentGate.WaitAsync(ct);
            try
            {
                AuthenticatedUser? currentPrincipal;
                try
                {
                    currentPrincipal = await revalidateSession(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error)
                {
                    _logger.LogWarning(error, "Agent WebSocket session revalidation failed for {UserId}; closing fail-safe.", principal.UserId);
                    currentPrincipal = null;
                }

                sessionValid = IsSameAgentSession(principal, currentPrincipal);
                if (!sessionValid)
                {
                    connection.InvalidateSession();
                }
            }
            finally
            {
                connection.EstablishmentGate.Release();
            }

            if (!sessionValid)
            {
                await CloseWithTimeoutAndAbortAsync(
                    connection, WebSocketCloseStatus.PolicyViolation, "session no longer valid", CancellationToken.None);
                return;
            }

            var acknowledged = false;
            for (var attempt = 0; attempt <= MaxPingRetries; attempt++)
            {
                var pingStartedAt = DateTimeOffset.UtcNow;
                await SendAsync(connection, new
                {
                    type = "ping",
                    attempt,
                    maxRetries = MaxPingRetries,
                    serverTime = pingStartedAt
                }, ct);

                await Task.Delay(retryInterval, ct);
                if (connection.LastPongAt >= pingStartedAt)
                {
                    acknowledged = true;
                    break;
                }
            }

            if (!acknowledged)
            {
                await CloseWithTimeoutAndAbortAsync(
                    connection, WebSocketCloseStatus.NormalClosure, "pong timeout", CancellationToken.None);
                return;
            }
        }
    }

    private static bool IsSameAgentSession(AuthenticatedUser expected, AuthenticatedUser? current) =>
        current is not null
        && string.Equals(current.UserId, expected.UserId, StringComparison.Ordinal)
        && string.Equals(current.TokenHash, expected.TokenHash, StringComparison.Ordinal)
        && string.Equals(current.UserType, "agent", StringComparison.Ordinal)
        && current.TokenExpiresAt == expected.TokenExpiresAt;

    private async Task InvokeConnectionCompletedAsync(
        Func<WebSocketState, CancellationToken, Task> connectionCompleted,
        WebSocketState state)
    {
        try
        {
            await connectionCompleted(state, CancellationToken.None);
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "Agent WebSocket disconnected lifecycle callback failed.");
        }
    }

    private static bool IsPing(string message)
    {
        if (string.Equals(message.Trim(), "ping", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(message);
            return document.RootElement.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "ping", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsPong(string message)
    {
        if (string.Equals(message.Trim(), "pong", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(message);
            return document.RootElement.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "pong", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task SendAsync(SocketConnection connection, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await connection.WriteLock.WaitAsync(ct);
        try
        {
            if (connection.Socket.State == WebSocketState.Open)
            {
                await connection.Socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
            }
        }
        finally
        {
            connection.WriteLock.Release();
        }
    }

    private static async Task CloseSocketAsync(SocketConnection connection, WebSocketCloseStatus status, string description, CancellationToken ct)
    {
        await connection.WriteLock.WaitAsync(ct);
        try
        {
            if (connection.Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await connection.Socket.CloseAsync(status, description, ct);
            }
        }
        catch (WebSocketException)
        {
            // Socket is already unusable.
        }
        finally
        {
            connection.WriteLock.Release();
        }
    }

    private async Task CloseWithTimeoutAndAbortAsync(
        SocketConnection connection,
        WebSocketCloseStatus status,
        string description,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.CloseTimeoutSeconds));
        try
        {
            var closeTask = CloseSocketAsync(connection, status, description, timeout.Token);
            await closeTask.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                connection.Socket.Abort();
            }
            catch (Exception error)
            {
                _logger.LogDebug(error, "WebSocket abort failed after bounded close did not complete.");
            }
        }
    }

    private async Task CloseRawSocketWithTimeoutAndAbortAsync(
        WebSocket socket,
        WebSocketCloseStatus status,
        string description)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_options.CloseTimeoutSeconds));
        try
        {
            await socket.CloseAsync(status, description, timeout.Token).WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            socket.Abort();
        }
        catch (WebSocketException)
        {
            // Socket is already unusable.
        }
    }

    private void RemoveConnection(string userId, Guid connectionId)
    {
        if (!_connections.TryGetValue(userId, out var userConnections))
        {
            return;
        }

        if (userConnections.TryRemove(connectionId, out _))
        {
            lock (_registrySync)
            {
                _connectionCount--;
            }
        }
        if (userConnections.IsEmpty)
        {
            _connections.TryRemove(userId, out _);
        }
    }

    private static string ToEventType(string kind)
    {
        return kind switch
        {
            "ticket_assigned" => "ticket.assigned",
            "ticket_closed" => "ticket.closed",
            "ticket_reopened" => "ticket.reopened",
            "ticket_commented" => "ticket.commented",
            _ => "notification.created"
        };
    }

    private sealed class SocketConnection(WebSocket socket, SemaphoreSlim writeLock, SemaphoreSlim establishmentGate)
    {
        private long _lastPongTicks = DateTimeOffset.UtcNow.UtcTicks;
        private int _sessionValid = 1;

        public WebSocket Socket { get; } = socket;
        public SemaphoreSlim WriteLock { get; } = writeLock;
        public SemaphoreSlim EstablishmentGate { get; } = establishmentGate;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentDictionary<string, byte> DeliveredNotificationIds { get; } = new(StringComparer.Ordinal);
        public DateTimeOffset LastPongAt => new DateTimeOffset(Interlocked.Read(ref _lastPongTicks), TimeSpan.Zero);
        public bool IsSessionValid => Volatile.Read(ref _sessionValid) == 1;

        public void InvalidateSession() => Interlocked.Exchange(ref _sessionValid, 0);

        public void MarkPong()
        {
            Interlocked.Exchange(ref _lastPongTicks, DateTimeOffset.UtcNow.UtcTicks);
        }
    }
}

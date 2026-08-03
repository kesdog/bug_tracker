namespace BugTracker.Api.Notifications;

public sealed class AgentWebSocketOptions
{
    public const int DefaultMaxConnections = 500;
    public const int DefaultMaxConnectionsPerUser = 5;
    public const int DefaultCloseTimeoutSeconds = 5;
    public const int DefaultHeartbeatIntervalSeconds = 30;
    public const int DefaultHeartbeatRetryIntervalSeconds = 15;

    public int MaxConnections { get; init; } = DefaultMaxConnections;

    public int MaxConnectionsPerUser { get; init; } = DefaultMaxConnectionsPerUser;

    public int CloseTimeoutSeconds { get; init; } = DefaultCloseTimeoutSeconds;

    public int HeartbeatIntervalSeconds { get; init; } = DefaultHeartbeatIntervalSeconds;

    public int HeartbeatRetryIntervalSeconds { get; init; } = DefaultHeartbeatRetryIntervalSeconds;

    public void Validate()
    {
        if (MaxConnections is < 1 or > 10_000)
        {
            throw new InvalidOperationException("AgentWebSocket:MaxConnections must be between 1 and 10000.");
        }
        if (MaxConnectionsPerUser is < 1 or > 100 || MaxConnectionsPerUser > MaxConnections)
        {
            throw new InvalidOperationException("AgentWebSocket:MaxConnectionsPerUser must be between 1 and 100 and must not exceed MaxConnections.");
        }
        if (CloseTimeoutSeconds is < 1 or > 30)
        {
            throw new InvalidOperationException("AgentWebSocket:CloseTimeoutSeconds must be between 1 and 30.");
        }
        if (HeartbeatIntervalSeconds is < 1 or > 300)
        {
            throw new InvalidOperationException("AgentWebSocket:HeartbeatIntervalSeconds must be between 1 and 300.");
        }
        if (HeartbeatRetryIntervalSeconds is < 1 or > 60)
        {
            throw new InvalidOperationException("AgentWebSocket:HeartbeatRetryIntervalSeconds must be between 1 and 60.");
        }
    }
}

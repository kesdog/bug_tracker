namespace BugTracker.Api.Database;

public sealed class DemoResetOptions
{
    public const int DefaultDrainTimeoutSeconds = 30;

    public bool Enabled { get; init; }

    public int HourUtc { get; init; }

    public string[] AllowedEnvironments { get; init; } = [];

    public int DrainTimeoutSeconds { get; init; } = DefaultDrainTimeoutSeconds;

    internal void Validate()
    {
        if (HourUtc is < 0 or > 23)
        {
            throw new InvalidOperationException("DemoReset:HourUtc must be between 0 and 23.");
        }
        if (DrainTimeoutSeconds is < 1 or > 300)
        {
            throw new InvalidOperationException("DemoReset:DrainTimeoutSeconds must be between 1 and 300.");
        }
    }

    internal void AssertAllowed(string environment)
    {
        Validate();
        if (!Enabled)
        {
            throw new InvalidOperationException("Demo reset is disabled. Set DemoReset:Enabled explicitly for the caller.");
        }

        if (string.Equals(environment?.Trim(), "Production", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Demo reset is never allowed in the Production environment.");
        }

        if (string.IsNullOrWhiteSpace(environment)
            || !AllowedEnvironments.Any(value => string.Equals(value?.Trim(), environment.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Demo reset is not allowed in environment '{environment}'.");
        }
    }
}

public sealed record DemoResetResult(int Generation, DateTimeOffset ResetAt, int Users, int Projects, int Tickets);

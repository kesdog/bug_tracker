namespace BugTracker.Api.Auth;

public sealed record DemoAccount(string Role, string Email, string Password, string Description);
public sealed record DemoPublicConfiguration(string ResetAtUtc, IReadOnlyList<DemoAccount> Accounts)
{
    public static DemoPublicConfiguration Value { get; } = new("04:00",
    [
        new("Developer", "ava.dev@example.com", "DevPass123!!", "Submit and follow project tickets."),
        new("Senior", "alex.senior@example.com", "SeniorPass123!", "Triage, assign, and manage projects."),
        new("Admin", "admin@example.com", "AdminPass123!", "Review users, audit activity, and all projects.")
    ]);
}

public static class DemoInfoEndpoints
{
    public static IEndpointRouteBuilder MapDemoInfoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/demo/config", (IHostEnvironment environment, IConfiguration configuration) =>
        {
            if (!environment.IsEnvironment("Demo") || !configuration.GetValue<bool>("Demo:PublicEnabled"))
            {
                return Results.NotFound();
            }

            return Results.Ok(DemoPublicConfiguration.Value);
        });
        return app;
    }
}

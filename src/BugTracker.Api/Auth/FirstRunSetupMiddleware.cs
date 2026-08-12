namespace BugTracker.Api.Auth;

public sealed class FirstRunSetupMiddleware : IMiddleware
{
    private static readonly string[] AlwaysAllowed = ["/api/auth/logout", "/api/auth/me", "/api/first-run/status"];

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var principal = context.Items[AuthMiddleware.AuthContextKey] as AuthenticatedUser;
        if (principal is null || !context.Request.Path.StartsWithSegments("/api")) { await next(context); return; }

        var setup = context.RequestServices.GetRequiredService<FirstRunSetupService>();
        var state = await setup.GetAsync(context.RequestAborted);
        if (state.IsComplete) { await next(context); return; }
        if (principal.UserType == "agent") { await RejectAsync(context); return; }
        if (!state.IsRootAdmin(principal.UserId) || !IsAllowed(context.Request, state.Phase)) { await RejectAsync(context); return; }
        await next(context);
    }

    private static bool IsAllowed(HttpRequest request, string phase)
    {
        var path = request.Path.Value ?? string.Empty;
        if (AlwaysAllowed.Contains(path, StringComparer.OrdinalIgnoreCase)) return true;
        return phase switch
        {
            "password_change_required" => HttpMethods.IsPost(request.Method) && path.Equals("/api/first-run/password", StringComparison.OrdinalIgnoreCase),
            "project_required" => (HttpMethods.IsGet(request.Method) || HttpMethods.IsPost(request.Method)) && path.Equals("/api/projects", StringComparison.OrdinalIgnoreCase),
            "ttl_required" => HttpMethods.IsPost(request.Method) && path.Equals("/api/first-run/complete", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static Task RejectAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return context.Response.WriteAsJsonAsync(new { error = "first-run setup is incomplete", errorCode = "setup_incomplete" }, context.RequestAborted);
    }
}

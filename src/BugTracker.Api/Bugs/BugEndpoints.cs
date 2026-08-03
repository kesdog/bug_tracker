using System.Text.RegularExpressions;
namespace BugTracker.Api.Bugs;

public static partial class BugEndpoints
{
    private static readonly Regex SafeImageNameChars = new("[^a-zA-Z0-9._-]+", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedAttachmentPurposes =
    [
        "initial-report",
        "solution-report",
        "close-report"
    ];

    private static readonly HashSet<string> AllowedBugTypes =
    [
        "page_not_loading",
        "form_submission",
        "crash",
        "api",
        "database"
    ];

    private static readonly HashSet<string> AllowedSeverities =
    [
        "low",
        "mid",
        "high",
        "urgent"
    ];

    private static readonly HashSet<string> AllowedPriorities =
    [
        "p0",
        "p1",
        "p2",
        "p3"
    ];

    private static readonly HashSet<string> AllowedTags =
    [
        "front-end",
        "back-end",
        "regression",
        "blocked",
        "needs-repro",
        "ai-reviewed",
        "security",
        "performance"
    ];

    private static readonly HashSet<string> AllowedFrequencies =
    [
        "unknown",
        "once",
        "intermittent",
        "frequent",
        "always"
    ];

    /// <summary>
    /// Registers all bug-related API routes and maps them to handler methods.
    /// </summary>
    public static IEndpointRouteBuilder MapBugEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/bugs", CreateBugAsync);
        app.MapPost("/api/bugs/export", ExportBugsAsync);
        app.MapGet("/api/bugs", ListBugsAsync);
        app.MapGet("/api/bugs/summary", GetBugSummaryAsync);
        app.MapGet("/api/bugs/allocated", ListAllocatedBugsAsync);
        app.MapGet("/api/bugs/assignees", ListAssignableUsersAsync);
        app.MapPatch("/api/bugs/bulk-allocate", BulkAllocateBugsAsync);
        app.MapPatch("/api/bugs/{id}/allocate", AllocateBugAsync);
        app.MapPatch("/api/bugs/{id}/metadata", UpdateBugMetadataAsync);
        app.MapPatch("/api/bugs/{id}/initial-report", UpdateInitialBugReportAsync);
        app.MapPatch("/api/bugs/{id}/report", UpdateBugReportAsync);
        app.MapPatch("/api/bugs/{id}/close", CloseBugAsync);
        app.MapPatch("/api/bugs/{id}/cancel", CancelBugAsync);
        app.MapPatch("/api/bugs/{id}/reopen", ReopenBugAsync);
        app.MapPost("/api/bugs/{id}/comments", AddCommentAsync);
        app.MapPost("/api/bugs/{id}/access-request", RequestTicketAccessAsync);
        app.MapPost("/api/bugs/{id}/attachments", UploadTicketAttachmentsAsync);
        app.MapGet("/api/bugs/{id}/attachments/{attachmentId}", GetTicketAttachmentAsync);
        app.MapGet("/api/bugs/{id}", GetBugByIdAsync);
        return app;
    }
}

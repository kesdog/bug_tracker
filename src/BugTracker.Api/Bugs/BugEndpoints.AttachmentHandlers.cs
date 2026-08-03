using BugTracker.Api.Audit;
using BugTracker.Api.Auth;
using BugTracker.Api.Projects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Bugs;

public static partial class BugEndpoints
{
    private static async Task<IResult> UploadTicketAttachmentsAsync(
        HttpContext context,
        BugRepository repository,
        ProjectAuthorizationService authorizationService,
        ImageValidationService imageValidationService,
        AuditLogger auditLogger,
        [FromRoute] string id,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return Results.BadRequest(new { error = "id is required" });
        }

        if (!context.Request.HasFormContentType)
        {
            return Results.Json(new { error = "multipart/form-data is required" }, statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        var bugId = id.Trim();
        BugTicketDto? ticket;
        try
        {
            ticket = await repository.GetBugByIdAsync(bugId, ct);
        }
        catch (SqliteException ex)
        {
            return ToDatabaseFailureResult(ex);
        }

        if (ticket is null)
        {
            return Results.NotFound(new { error = "ticket not found" });
        }

        if (!await authorizationService.CanManageTicketAsync(principal, ticket, ct))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(ct);
        }
        catch (InvalidDataException)
        {
            return Results.Json(new { error = "multipart request body is too large" }, statusCode: StatusCodes.Status413PayloadTooLarge);
        }
        int? expectedVersion = null;
        if (string.IsNullOrWhiteSpace(form["expectedVersion"]))
        {
            return VersionRequiredResult();
        }
        if (!int.TryParse(form["expectedVersion"], out var parsedVersion) || parsedVersion < 1)
        {
            return Results.BadRequest(new { error = "expectedVersion must be a positive integer" });
        }
        expectedVersion = parsedVersion;
        var purpose = NormalizeAttachmentPurpose(form["purpose"].ToString());
        if (purpose is null)
        {
            return Results.BadRequest(new { error = "purpose must be initial-report, solution-report, or close-report" });
        }

        var files = form.Files.Where(file => string.Equals(file.Name, "files", StringComparison.Ordinal)).ToList();
        if (files.Count == 0)
        {
            return Results.BadRequest(new { error = "at least one file is required" });
        }

        if (files.Count > BugReportLimits.MaxImagesPerReport)
        {
            return Results.Json(
                new { error = $"a maximum of {BugReportLimits.MaxImagesPerReport} images can be uploaded" },
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        var attachments = new List<TicketAttachmentUpload>(files.Count);
        long aggregateSourceBytes = 0;
        foreach (var file in files)
        {
            var result = await ValidateAndBuildImageAttachmentAsync(file, purpose, imageValidationService, ct);
            if (!result.IsValid)
            {
                return Results.Json(new { error = result.Error }, statusCode: result.StatusCode);
            }

            attachments.Add(result.Attachment!);
            aggregateSourceBytes += result.SourceSizeBytes;
            if (aggregateSourceBytes > BugReportLimits.MaxImageAggregateDecodedBytes)
                return Results.Json(new { error = "aggregate image payload is too large" }, statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        try
        {
            var created = await repository.AddTicketAttachmentsAsync(
                bugId,
                principal.UserId,
                principal.UserType,
                attachments,
                expectedVersion,
                DateTimeOffset.UtcNow,
                ct);

            if (created.Conflict is not null) return ToVersionConflictResult(created.Conflict);
            if (created.Value is null)
            {
                if (created.ErrorCode == "forbidden") return Results.StatusCode(StatusCodes.Status403Forbidden);
                if (created.ErrorCode == "attachment_size_limit_exceeded") return Results.Json(new { error = "report images exceed the 12 MiB aggregate quota" }, statusCode: StatusCodes.Status413PayloadTooLarge);
                return created.ErrorCode == "attachment_limit_exceeded"
                    ? Results.Json(new { error = "tickets support at most 3 uploaded images" }, statusCode: StatusCodes.Status413PayloadTooLarge)
                    : created.ErrorCode == "bug_not_found_or_closed"
                        ? Results.BadRequest(new { error = "closed tickets cannot receive new attachments" })
                    : Results.NotFound(new { error = "ticket not found" });
            }
            return Results.Created($"/api/bugs/{bugId}/attachments", new { attachments = created.Value.Attachments, version = created.Value.Version });
        }
        catch (BugDataAccessException ex)
        {
            return ToDatabaseFailureResult(ex);
        }
    }

    private static async Task<IResult> GetTicketAttachmentAsync(
        HttpContext context,
        BugRepository repository,
        ProjectAuthorizationService authorizationService,
        AuditLogger auditLogger,
        [FromRoute] string id,
        [FromRoute] string attachmentId,
        CancellationToken ct)
    {
        var principal = GetPrincipal(context);
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(attachmentId))
        {
            return Results.BadRequest(new { error = "id and attachment_id are required" });
        }

        var bugId = id.Trim();
        BugTicketDto? ticket;
        try
        {
            ticket = await repository.GetBugByIdAsync(bugId, ct);
        }
        catch (SqliteException ex)
        {
            return ToDatabaseFailureResult(ex);
        }

        if (ticket is null)
        {
            return Results.NotFound(new { error = "ticket not found" });
        }

        if (!await authorizationService.CanReadTicketAsync(principal, ticket, ct))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        TicketAttachmentContentDto? attachment;
        try
        {
            attachment = await repository.GetTicketAttachmentAsync(bugId, attachmentId.Trim(), ct);
        }
        catch (SqliteException ex)
        {
            return ToDatabaseFailureResult(ex);
        }

        if (attachment is null)
        {
            return Results.NotFound(new { error = "attachment not found" });
        }

        await auditLogger.LogAsync(
            principal,
            "ticket_attachment_downloaded",
            $"Ticket {bugId} attachment downloaded.",
            bugId,
            new { attachmentId = attachment.Metadata.Id, attachment.Metadata.Name, attachment.Metadata.ContentType },
            ct);

        return Results.File(attachment.Content, attachment.Metadata.ContentType, attachment.Metadata.Name);
    }
    private static string? NormalizeAttachmentPurpose(string? purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            return null;
        }

        var normalized = purpose.Trim().ToLowerInvariant();
        return AllowedAttachmentPurposes.Contains(normalized) ? normalized : null;
    }
    private static async Task<ImageAttachmentValidationResult> ValidateAndBuildImageAttachmentAsync(
        IFormFile file,
        string purpose,
        ImageValidationService imageValidationService,
        CancellationToken ct)
    {
        if (file.Length <= 0)
        {
            return ImageAttachmentValidationResult.Invalid("image file is empty");
        }

        if (file.Length > BugReportLimits.MaxImageDecodedBytes)
        {
            return ImageAttachmentValidationResult.Invalid("image file is too large", StatusCodes.Status413PayloadTooLarge);
        }

        if (Path.GetFileName(file.FileName).Length > BugReportLimits.FileNameCharacters)
            return ImageAttachmentValidationResult.Invalid($"image name must be {BugReportLimits.FileNameCharacters} characters or less");

        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(memory, ct);
        var content = memory.ToArray();

        var validated = imageValidationService.ValidateBytes(content, file.ContentType);
        if (!validated.IsValid)
            return ImageAttachmentValidationResult.Invalid(validated.Error!, GetImageValidationStatusCode(validated.Failure));
        var image = validated.Image!;

        var safeName = BuildSafeImageName(file.FileName);
        return ImageAttachmentValidationResult.Valid(new TicketAttachmentUpload(
            purpose,
            safeName,
            image.ContentType,
            "image",
            image.Content.LongLength,
            image.Width,
            image.Height,
            image.Sha256,
            image.Content), image.SourceSizeBytes);
    }
    private sealed record ImageAttachmentValidationResult(
        bool IsValid,
        TicketAttachmentUpload? Attachment,
        string? Error,
        long SourceSizeBytes,
        int StatusCode)
    {
        public static ImageAttachmentValidationResult Valid(TicketAttachmentUpload attachment, long sourceSizeBytes) =>
            new(true, attachment, null, sourceSizeBytes, StatusCodes.Status200OK);
        public static ImageAttachmentValidationResult Invalid(string error, int statusCode = StatusCodes.Status422UnprocessableEntity) =>
            new(false, null, error, 0, statusCode);
    }
}

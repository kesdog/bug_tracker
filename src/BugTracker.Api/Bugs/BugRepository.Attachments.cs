using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BugTracker.Api.Bugs;

public sealed partial class BugRepository
{
    public async Task<TicketMutationResult<TicketAttachmentsMutationDto>> AddTicketAttachmentsAsync(
        string bugId,
        string actorUserId,
        string actorType,
        IReadOnlyList<TicketAttachmentUpload> attachments,
        int? expectedVersion,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var outcome = await ExecuteAtomicWriteWithRetryAsync(async (connection, token) =>
        {
            var authorization = await _writeAuthorization.AuthorizeAsync(connection,
                new(actorUserId, TicketWriteOperation.Manage, bugId), token);
            if (!authorization.IsAllowed) return TicketMutationResult<TicketAttachmentsMutationDto>.Failure(authorization.ErrorCode!);
            var conflict = await GetVersionConflictAsync(connection, bugId, expectedVersion, token);
            if (conflict is not null) return TicketMutationResult<TicketAttachmentsMutationDto>.VersionConflict(conflict);
            var projectId = await GetBugProjectIdAsync(connection, bugId, token);
            if (string.IsNullOrWhiteSpace(projectId))
            {
                return TicketMutationResult<TicketAttachmentsMutationDto>.Failure("bug_not_found");
            }

            await using (var countCommand = connection.CreateCommand())
            {
                var initialReport = attachments.All(attachment => attachment.Purpose == "initial-report");
                if (!initialReport && attachments.Any(attachment => attachment.Purpose == "initial-report"))
                    return TicketMutationResult<TicketAttachmentsMutationDto>.Failure("attachment_limit_exceeded");
                countCommand.CommandText = initialReport
                    ? """
                      SELECT COUNT(*) + COALESCE((SELECT json_array_length(report_images_json) FROM bug_tickets WHERE id = $id), 0)
                      FROM ticket_attachments WHERE ticket_id = $id AND kind = 'image' AND purpose = 'initial-report';
                      """
                    : """
                      SELECT COUNT(*) + COALESCE((SELECT json_array_length(resolution_report_images_json) FROM bug_tickets WHERE id = $id), 0)
                      FROM ticket_attachments WHERE ticket_id = $id AND kind = 'image' AND purpose <> 'initial-report';
                      """;
                countCommand.Parameters.AddWithValue("$id", bugId);
                var count = Convert.ToInt32(await countCommand.ExecuteScalarAsync(token));
                if (count + attachments.Count > BugReportLimits.MaxImagesPerReport)
                    return TicketMutationResult<TicketAttachmentsMutationDto>.Failure("attachment_limit_exceeded");
            }

            await using (var sizeCommand = connection.CreateCommand())
            {
                var initialReport = attachments.All(attachment => attachment.Purpose == "initial-report");
                sizeCommand.CommandText = initialReport
                    ? "SELECT COALESCE(SUM(size_bytes), 0) FROM ticket_attachments WHERE ticket_id = $id AND kind = 'image' AND purpose = 'initial-report';"
                    : "SELECT COALESCE(SUM(size_bytes), 0) FROM ticket_attachments WHERE ticket_id = $id AND kind = 'image' AND purpose <> 'initial-report';";
                sizeCommand.Parameters.AddWithValue("$id", bugId);
                var storedBytes = Convert.ToInt64(await sizeCommand.ExecuteScalarAsync(token));
                var embeddedBytes = await GetStoredEmbeddedImageSizeAsync(connection, bugId, initialReport, token);
                if (storedBytes + embeddedBytes + attachments.Sum(attachment => attachment.SizeBytes) > BugReportLimits.MaxImageAggregateDecodedBytes)
                    return TicketMutationResult<TicketAttachmentsMutationDto>.Failure("attachment_size_limit_exceeded");
            }

            await using (var versionUpdate = connection.CreateCommand())
            {
                versionUpdate.CommandText = """
                    UPDATE bug_tickets SET version = version + 1, updated_at = $updated
                    WHERE id = $id AND status <> 'closed'
                      AND ($expected_version IS NULL OR version = $expected_version);
                    """;
                versionUpdate.Parameters.AddWithValue("$updated", now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
                versionUpdate.Parameters.AddWithValue("$id", bugId);
                versionUpdate.Parameters.AddWithValue("$expected_version", (object?)expectedVersion ?? DBNull.Value);
                if (await versionUpdate.ExecuteNonQueryAsync(token) == 0)
                    return TicketMutationResult<TicketAttachmentsMutationDto>.Failure("bug_not_found_or_closed");
            }

            var createdAt = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            var created = new List<TicketAttachmentDto>(attachments.Count);

            foreach (var attachment in attachments)
            {
                var metadata = new TicketAttachmentDto(
                    Guid.NewGuid().ToString("N"),
                    bugId,
                    attachment.Purpose,
                    attachment.Name,
                    attachment.ContentType,
                    attachment.Kind,
                    attachment.SizeBytes,
                    attachment.Width,
                    attachment.Height,
                    attachment.Sha256,
                    actorUserId,
                    createdAt);

                const string sql = """
                    INSERT INTO ticket_attachments (
                        attachment_id,
                        ticket_id,
                        uploaded_by_user_id,
                        purpose,
                        file_name,
                        content_type,
                        kind,
                        size_bytes,
                        width,
                        height,
                        sha256,
                        content_blob,
                        created_at
                    )
                    VALUES (
                        $attachment_id,
                        $ticket_id,
                        $uploaded_by_user_id,
                        $purpose,
                        $file_name,
                        $content_type,
                        $kind,
                        $size_bytes,
                        $width,
                        $height,
                        $sha256,
                        $content_blob,
                        $created_at
                    );
                    """;

                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("$attachment_id", metadata.Id);
                command.Parameters.AddWithValue("$ticket_id", metadata.TicketId);
                command.Parameters.AddWithValue("$uploaded_by_user_id", metadata.UploadedByUserId);
                command.Parameters.AddWithValue("$purpose", metadata.Purpose);
                command.Parameters.AddWithValue("$file_name", metadata.Name);
                command.Parameters.AddWithValue("$content_type", metadata.ContentType);
                command.Parameters.AddWithValue("$kind", metadata.Kind);
                command.Parameters.AddWithValue("$size_bytes", metadata.SizeBytes);
                command.Parameters.AddWithValue("$width", (object?)metadata.Width ?? DBNull.Value);
                command.Parameters.AddWithValue("$height", (object?)metadata.Height ?? DBNull.Value);
                command.Parameters.AddWithValue("$sha256", metadata.Sha256);
                command.Parameters.Add("$content_blob", SqliteType.Blob).Value = attachment.Content;
                command.Parameters.AddWithValue("$created_at", metadata.CreatedAt);

                await command.ExecuteNonQueryAsync(token);
                created.Add(metadata);
            }

            var version = await GetTicketVersionAsync(connection, bugId, token);
            await RecordMutationSideEffectsAsync(connection, bugId, version, actorUserId, actorType, "attachment_added",
                $"{created.Count} attachment(s) uploaded.", "ticket_attachment_uploaded", ["attachments"], "ticket_attachment_uploaded",
                $"Ticket {bugId} has new attachments.", createdAt, token);
            return TicketMutationResult<TicketAttachmentsMutationDto>.Success(new(created, version));
        }, ct);
        return outcome;
    }

    private static async Task<int> GetMultipartImageCountAsync(
        SqliteConnection connection,
        string bugId,
        bool initialReport,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = initialReport
            ? "SELECT COUNT(*) FROM ticket_attachments WHERE ticket_id = $id AND kind = 'image' AND purpose = 'initial-report';"
            : "SELECT COUNT(*) FROM ticket_attachments WHERE ticket_id = $id AND kind = 'image' AND purpose <> 'initial-report';";
        command.Parameters.AddWithValue("$id", bugId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    private static async Task<long> GetMultipartImageSizeAsync(
        SqliteConnection connection,
        string bugId,
        bool initialReport,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = initialReport
            ? "SELECT COALESCE(SUM(size_bytes), 0) FROM ticket_attachments WHERE ticket_id = $id AND kind = 'image' AND purpose = 'initial-report';"
            : "SELECT COALESCE(SUM(size_bytes), 0) FROM ticket_attachments WHERE ticket_id = $id AND kind = 'image' AND purpose <> 'initial-report';";
        command.Parameters.AddWithValue("$id", bugId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    private static async Task<long> GetStoredEmbeddedImageSizeAsync(
        SqliteConnection connection,
        string bugId,
        bool initialReport,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = initialReport
            ? "SELECT report_images_json FROM bug_tickets WHERE id = $id;"
            : "SELECT resolution_report_images_json FROM bug_tickets WHERE id = $id;";
        command.Parameters.AddWithValue("$id", bugId);
        var raw = await command.ExecuteScalarAsync(ct);
        if (raw is null or DBNull) return 0;

        try
        {
            var images = JsonSerializer.Deserialize<List<ReportImageDto>>((string)raw, JsonOptions) ?? [];
            return GetEmbeddedImageSize(images);
        }
        catch (JsonException)
        {
            // Treat corrupt legacy data as quota-consuming rather than allowing a bypass.
            return BugReportLimits.MaxImageAggregateDecodedBytes + 1L;
        }
    }

    private static long GetEmbeddedImageSize(IReadOnlyList<ReportImageDto> images)
    {
        long total = 0;
        foreach (var image in images)
        {
            var separator = image.DataUrl.IndexOf(',');
            if (separator < 0) return BugReportLimits.MaxImageAggregateDecodedBytes + 1L;

            try
            {
                total = checked(total + Convert.FromBase64String(image.DataUrl[(separator + 1)..]).LongLength);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                return BugReportLimits.MaxImageAggregateDecodedBytes + 1L;
            }
        }

        return total;
    }

    public async Task<TicketAttachmentContentDto?> GetTicketAttachmentAsync(string bugId, string attachmentId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(readOnly: true, ct);

        const string sql = """
            SELECT
                attachment_id,
                ticket_id,
                purpose,
                file_name,
                content_type,
                kind,
                size_bytes,
                width,
                height,
                sha256,
                uploaded_by_user_id,
                created_at,
                content_blob
            FROM ticket_attachments
            WHERE ticket_id = $ticket_id AND attachment_id = $attachment_id
            LIMIT 1;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$ticket_id", bugId);
        command.Parameters.AddWithValue("$attachment_id", attachmentId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var metadata = MapTicketAttachment(reader);
        var content = (byte[])reader[12];
        return new TicketAttachmentContentDto(metadata, content);
    }
}

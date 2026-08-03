namespace BugTracker.Api.Bugs;

public static partial class BugEndpoints
{
    private static ListFiltersValidationResult ValidateAndBuildListFilters(
        string? priority,
        string? severity,
        string? tag,
        string? projectId,
        string? assigneeUserId,
        string? reporterUserId)
    {
        string? normalizedPriority = null;
        if (!string.IsNullOrWhiteSpace(priority))
        {
            normalizedPriority = priority.Trim().ToLowerInvariant();
            if (!AllowedPriorities.Contains(normalizedPriority))
            {
                return ListFiltersValidationResult.Invalid("invalid priority");
            }
        }

        string? normalizedSeverity = null;
        if (!string.IsNullOrWhiteSpace(severity))
        {
            normalizedSeverity = severity.Trim().ToLowerInvariant();
            if (!AllowedSeverities.Contains(normalizedSeverity))
            {
                return ListFiltersValidationResult.Invalid("invalid severity");
            }
        }

        string? normalizedTag = null;
        if (!string.IsNullOrWhiteSpace(tag))
        {
            normalizedTag = tag.Trim().ToLowerInvariant();
            if (!AllowedTags.Contains(normalizedTag))
            {
                return ListFiltersValidationResult.Invalid("invalid tag");
            }
        }

        var normalizedProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId.Trim();
        var normalizedAssigneeUserId = string.IsNullOrWhiteSpace(assigneeUserId) ? null : assigneeUserId.Trim();
        var normalizedReporterUserId = string.IsNullOrWhiteSpace(reporterUserId) ? null : reporterUserId.Trim();
        return ListFiltersValidationResult.Valid(new BugListFilters(
            normalizedPriority,
            normalizedSeverity,
            normalizedTag,
            normalizedProjectId,
            normalizedAssigneeUserId,
            normalizedReporterUserId));
    }

    private static TagsValidationResult ValidateAndNormalizeTags(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return TagsValidationResult.Valid([]);
        }

        if (tags.Count > 8)
        {
            return TagsValidationResult.Invalid("a maximum of 8 tags is allowed");
        }

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            var value = tag.Trim().ToLowerInvariant();
            if (!AllowedTags.Contains(value))
            {
                return TagsValidationResult.Invalid("invalid tag");
            }

            if (seen.Add(value))
            {
                normalized.Add(value);
            }
        }

        if (seen.Contains("front-end") && seen.Contains("back-end"))
        {
            return TagsValidationResult.Invalid("front-end and back-end tags are mutually exclusive");
        }

        return TagsValidationResult.Valid(normalized);
    }

    private static bool IsPriorityValidForSeverity(string severity, string priority)
    {
        return severity != "urgent" || priority is "p0" or "p1";
    }

    private static OptionalTextValidationResult ValidateOptionalText(string? value, int maxLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return OptionalTextValidationResult.Valid(null);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? OptionalTextValidationResult.Valid(trimmed)
            : OptionalTextValidationResult.Invalid($"{fieldName} must be {maxLength} characters or less");
    }

    private sealed record OptionalTextValidationResult(bool IsValid, string? Value, string? Error)
    {
        public static OptionalTextValidationResult Valid(string? value) => new(true, value, null);
        public static OptionalTextValidationResult Invalid(string error) => new(false, null, error);
    }

    private sealed record TagsValidationResult(bool IsValid, IReadOnlyList<string> Tags, string? Error)
    {
        public static TagsValidationResult Valid(IReadOnlyList<string> tags) => new(true, tags, null);
        public static TagsValidationResult Invalid(string error) => new(false, [], error);
    }

    private sealed record ListFiltersValidationResult(bool IsValid, BugListFilters Filters, string? Error)
    {
        public static ListFiltersValidationResult Valid(BugListFilters filters) => new(true, filters, null);
        public static ListFiltersValidationResult Invalid(string error) => new(false, new BugListFilters(null, null, null, null, null, null), error);
    }

    /// <summary>
    /// Validates and normalizes report images to the DTO format accepted by storage.
    /// </summary>
    private static EmbeddedImagesValidationResult ValidateAndNormalizeImages(
        IReadOnlyList<ReportImageInput>? reportImages,
        ImageValidationService imageValidationService)
    {
        if (reportImages is null || reportImages.Count == 0)
        {
            return EmbeddedImagesValidationResult.Valid([]);
        }

        if (reportImages.Count > BugReportLimits.MaxImagesPerReport)
        {
            return EmbeddedImagesValidationResult.Invalid(
                $"a maximum of {BugReportLimits.MaxImagesPerReport} report images is allowed",
                StatusCodes.Status413PayloadTooLarge);
        }

        var normalized = new List<ReportImageDto>(reportImages.Count);
        long aggregateBytes = 0;
        foreach (var image in reportImages)
        {
            var validation = imageValidationService.ValidateDataUrl(image);
            if (!validation.IsValid)
                return EmbeddedImagesValidationResult.Invalid(validation.Error!, GetImageValidationStatusCode(validation.Failure));
            if (Path.GetFileName(image.Name.Trim()).Length > BugReportLimits.FileNameCharacters)
                return EmbeddedImagesValidationResult.Invalid($"image name must be {BugReportLimits.FileNameCharacters} characters or less");
            var validated = validation.Image!;
            aggregateBytes += validated.SourceSizeBytes;
            if (aggregateBytes > BugReportLimits.MaxImageAggregateDecodedBytes)
                return EmbeddedImagesValidationResult.Invalid("aggregate image payload is too large", StatusCodes.Status413PayloadTooLarge);

            var safeName = BuildSafeImageName(image.Name);
            normalized.Add(new ReportImageDto(
                safeName,
                validated.ContentType,
                $"data:{validated.ContentType};base64,{Convert.ToBase64String(validated.Content)}"));
        }

        return EmbeddedImagesValidationResult.Valid(normalized);
    }

    private static TextEvidenceValidationResult ValidateAndNormalizeTextEvidence(IReadOnlyList<TextEvidenceInput>? textEvidence)
    {
        if (textEvidence is null || textEvidence.Count == 0)
        {
            return TextEvidenceValidationResult.Valid([]);
        }

        if (textEvidence.Count > BugReportLimits.MaxTextEvidenceFiles)
        {
            return TextEvidenceValidationResult.Invalid($"a maximum of {BugReportLimits.MaxTextEvidenceFiles} text evidence files is allowed");
        }

        var normalized = new List<TextEvidenceDto>(textEvidence.Count);
        var aggregateBytes = 0;
        foreach (var evidence in textEvidence)
        {
            if (string.IsNullOrWhiteSpace(evidence.Name) || string.IsNullOrWhiteSpace(evidence.ContentType) || string.IsNullOrWhiteSpace(evidence.Text))
            {
                return TextEvidenceValidationResult.Invalid("each text evidence file must include name, contentType, and text");
            }

            var contentType = evidence.ContentType.Trim().ToLowerInvariant();
            if (contentType != "text/plain")
            {
                return TextEvidenceValidationResult.Invalid("text evidence supports text/plain files only");
            }

            if (Path.GetFileName(evidence.Name.Trim()).Length > BugReportLimits.FileNameCharacters)
                return TextEvidenceValidationResult.Invalid($"text evidence name must be {BugReportLimits.FileNameCharacters} characters or less");
            var safeName = BuildSafeTextEvidenceName(evidence.Name);
            if (!safeName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                return TextEvidenceValidationResult.Invalid("text evidence files must use the .txt extension");
            }

            var text = evidence.Text.Trim();
            var byteCount = System.Text.Encoding.UTF8.GetByteCount(text);
            if (byteCount > BugReportLimits.MaxTextEvidenceBytesPerFile)
            {
                return TextEvidenceValidationResult.Invalid("text evidence file is too large");
            }

            aggregateBytes += byteCount;
            if (aggregateBytes > BugReportLimits.MaxTextEvidenceAggregateBytes)
                return TextEvidenceValidationResult.Invalid("aggregate text evidence is too large");

            normalized.Add(new TextEvidenceDto(safeName, contentType, text));
        }

        return TextEvidenceValidationResult.Valid(normalized);
    }

    /// <summary>
    /// Sanitizes user-provided image names to a safe and stable persisted value.
    /// </summary>
    private static string BuildSafeImageName(string imageName)
    {
        var trimmed = imageName.Trim();
        var fileName = Path.GetFileName(trimmed);
        var normalized = SafeImageNameChars.Replace(fileName, "-").Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "image";
        }

        return normalized;
    }

    private static string BuildSafeTextEvidenceName(string evidenceName)
    {
        var trimmed = evidenceName.Trim();
        var fileName = Path.GetFileName(trimmed);
        var normalized = SafeImageNameChars.Replace(fileName, "-").Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "evidence.txt";
        }

        return normalized;
    }

    private static int GetImageValidationStatusCode(ImageValidationFailure failure) => failure switch
    {
        ImageValidationFailure.PayloadTooLarge => StatusCodes.Status413PayloadTooLarge,
        ImageValidationFailure.UnsupportedMediaType => StatusCodes.Status415UnsupportedMediaType,
        _ => StatusCodes.Status422UnprocessableEntity
    };

    private static IResult ToEmbeddedImageValidationFailure(EmbeddedImagesValidationResult result) =>
        Results.Json(new { error = result.Error }, statusCode: result.StatusCode);

    private sealed record EmbeddedImagesValidationResult(
        bool IsValid,
        IReadOnlyList<ReportImageDto> Images,
        string? Error,
        int StatusCode)
    {
        public static EmbeddedImagesValidationResult Valid(IReadOnlyList<ReportImageDto> images) =>
            new(true, images, null, StatusCodes.Status200OK);
        public static EmbeddedImagesValidationResult Invalid(string error, int statusCode = StatusCodes.Status422UnprocessableEntity) =>
            new(false, [], error, statusCode);
    }

    private sealed record TextEvidenceValidationResult(bool IsValid, IReadOnlyList<TextEvidenceDto> Evidence, string? Error)
    {
        public static TextEvidenceValidationResult Valid(IReadOnlyList<TextEvidenceDto> evidence) => new(true, evidence, null);
        public static TextEvidenceValidationResult Invalid(string error) => new(false, [], error);
    }
}

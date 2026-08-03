using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace BugTracker.Api.Bugs;

public sealed class ImageValidationService
{
    private static readonly IReadOnlyDictionary<string, string> MimeByFormatName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PNG"] = "image/png",
            ["JPEG"] = "image/jpeg",
            ["WEBP"] = "image/webp"
        };

    public ImageValidationResult ValidateDataUrl(ReportImageInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.ContentType) || string.IsNullOrWhiteSpace(input.DataUrl))
            return ImageValidationResult.Invalid("each report image must include name, contentType, and dataUrl");

        var contentType = input.ContentType.Trim().ToLowerInvariant();
        var prefix = $"data:{contentType};base64,";
        if (!input.DataUrl.StartsWith(prefix, StringComparison.Ordinal) || input.DataUrl.Length == prefix.Length)
            return ImageValidationResult.Invalid("report image dataUrl must be a strict base64 data URL");

        var encoded = input.DataUrl.AsSpan(prefix.Length);
        if (encoded.Length % 4 != 0 || encoded.IndexOf(' ') >= 0 || encoded.IndexOf('\t') >= 0 ||
            encoded.IndexOf('\r') >= 0 || encoded.IndexOf('\n') >= 0)
            return ImageValidationResult.Invalid("report image dataUrl contains malformed base64");

        var maximumEncodedLength = ((BugReportLimits.MaxImageDecodedBytes + 2) / 3) * 4;
        if (encoded.Length > maximumEncodedLength)
            return ImageValidationResult.Invalid("image file is too large", ImageValidationFailure.PayloadTooLarge);

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(encoded.ToString());
        }
        catch (FormatException)
        {
            return ImageValidationResult.Invalid("report image dataUrl contains malformed base64");
        }

        if (!Convert.ToBase64String(bytes).AsSpan().SequenceEqual(encoded))
            return ImageValidationResult.Invalid("report image dataUrl contains non-canonical base64");

        return ValidateBytes(bytes, contentType);
    }

    public ImageValidationResult ValidateBytes(byte[] bytes, string? declaredContentType)
    {
        if (bytes.Length == 0) return ImageValidationResult.Invalid("image file is empty");
        if (bytes.Length > BugReportLimits.MaxImageDecodedBytes) return ImageValidationResult.Invalid("image file is too large", ImageValidationFailure.PayloadTooLarge);

        ImageInfo info;
        IImageFormat identifiedFormat;
        try
        {
            identifiedFormat = Image.DetectFormat(bytes);
            info = Image.Identify(bytes);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or ArgumentException)
        {
            return ImageValidationResult.Invalid("image is corrupt or uses an unsupported format");
        }

        if (!MimeByFormatName.TryGetValue(identifiedFormat.Name, out var derivedContentType))
            return ImageValidationResult.Invalid("unsupported image type; use png, jpeg, or webp", ImageValidationFailure.UnsupportedMediaType);

        var normalizedDeclaredType = declaredContentType?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(normalizedDeclaredType) && !string.Equals(normalizedDeclaredType, derivedContentType, StringComparison.Ordinal))
            return ImageValidationResult.Invalid("declared image type does not match file content");

        if (!HasAllowedDimensions(info.Width, info.Height))
            return ImageValidationResult.Invalid("image resolution must be orientation-neutral 3840x2160 or smaller and at most 8,294,400 pixels");

        try
        {
            using var image = Image.Load(bytes);
            var decodedFormat = image.Metadata.DecodedImageFormat;
            if (decodedFormat is null || !string.Equals(decodedFormat.Name, identifiedFormat.Name, StringComparison.OrdinalIgnoreCase))
                return ImageValidationResult.Invalid("image format could not be verified");
            if (image.Frames.Count != 1)
                return ImageValidationResult.Invalid("animated or multi-frame images are not allowed");

            image.Mutate(operation => operation.AutoOrient());
            if (!HasAllowedDimensions(image.Width, image.Height))
                return ImageValidationResult.Invalid("image resolution must be orientation-neutral 3840x2160 or smaller and at most 8,294,400 pixels");

            image.Metadata.ExifProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.XmpProfile = null;

            using var output = new MemoryStream();
            image.Save(output, CreateEncoder(derivedContentType));
            var canonicalBytes = output.ToArray();
            if (canonicalBytes.Length > BugReportLimits.MaxImageDecodedBytes)
                return ImageValidationResult.Invalid("canonical image file is too large", ImageValidationFailure.PayloadTooLarge);

            return ImageValidationResult.Valid(new ValidatedImage(
                derivedContentType,
                canonicalBytes,
                bytes.Length,
                image.Width,
                image.Height,
                Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant()));
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or ArgumentException or NotSupportedException or IOException)
        {
            return ImageValidationResult.Invalid("image is corrupt or could not be fully decoded");
        }
    }

    private static bool HasAllowedDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > BugReportLimits.MaxImagePixels) return false;
        return Math.Max(width, height) <= BugReportLimits.MaxImageLongSide &&
               Math.Min(width, height) <= BugReportLimits.MaxImageShortSide;
    }

    private static IImageEncoder CreateEncoder(string contentType) => contentType switch
    {
        "image/png" => new PngEncoder(),
        "image/jpeg" => new JpegEncoder { Quality = 90 },
        "image/webp" => new WebpEncoder { Quality = 90 },
        _ => throw new NotSupportedException("Unsupported image encoder.")
    };
}

public sealed record ValidatedImage(string ContentType, byte[] Content, int SourceSizeBytes, int Width, int Height, string Sha256);

public enum ImageValidationFailure
{
    InvalidContent,
    UnsupportedMediaType,
    PayloadTooLarge
}

public sealed record ImageValidationResult(
    bool IsValid,
    ValidatedImage? Image,
    string? Error,
    ImageValidationFailure Failure = ImageValidationFailure.InvalidContent)
{
    public static ImageValidationResult Valid(ValidatedImage image) => new(true, image, null);
    public static ImageValidationResult Invalid(
        string error,
        ImageValidationFailure failure = ImageValidationFailure.InvalidContent) => new(false, null, error, failure);
}

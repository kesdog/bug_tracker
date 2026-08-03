using BugTracker.Api.Bugs;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BugTracker.Api.Tests;

public sealed class ImageValidationServiceTests
{
    private readonly ImageValidationService _service = new();

    [Fact]
    public void ValidateDataUrl_MalformedBase64_IsRejected()
    {
        var result = _service.ValidateDataUrl(new ReportImageInput("bad.png", "image/png", "data:image/png;base64,%%%%"));

        Assert.False(result.IsValid);
        Assert.Contains("malformed base64", result.Error);
    }

    [Fact]
    public void ValidateBytes_SpoofedMime_IsRejected()
    {
        var result = _service.ValidateBytes(CreatePng(1, 1), "image/jpeg");

        Assert.False(result.IsValid);
        Assert.Contains("does not match", result.Error);
    }

    [Fact]
    public void ValidateBytes_CorruptPng_IsRejected()
    {
        byte[] corrupt = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

        var result = _service.ValidateBytes(corrupt, "image/png");

        Assert.False(result.IsValid);
        Assert.Contains("corrupt", result.Error);
        Assert.Equal(ImageValidationFailure.InvalidContent, result.Failure);
    }

    [Fact]
    public void ValidateBytes_Gif_IsRejected()
    {
        using var image = new Image<Rgba32>(1, 1, Color.Black);
        using var output = new MemoryStream();
        image.Save(output, new GifEncoder());

        var result = _service.ValidateBytes(output.ToArray(), "image/gif");

        Assert.False(result.IsValid);
        Assert.Contains("unsupported image type", result.Error);
        Assert.Equal(ImageValidationFailure.UnsupportedMediaType, result.Failure);
    }

    [Fact]
    public void ValidateBytes_MultiFrameSupportedImage_IsRejected()
    {
        using var image = new Image<Rgba32>(1, 1, Color.Black);
        image.Frames.AddFrame(image.Frames.RootFrame);
        using var output = new MemoryStream();
        image.Save(output, new WebpEncoder());

        var result = _service.ValidateBytes(output.ToArray(), "image/webp");

        Assert.False(result.IsValid);
        Assert.Contains("multi-frame", result.Error);
    }

    [Fact]
    public void ValidateBytes_OrientationNeutralBoundary_IsEnforced()
    {
        var accepted = _service.ValidateBytes(CreatePng(3840, 2160), "image/png");
        var rejected = _service.ValidateBytes(CreatePng(3841, 2160), "image/png");

        Assert.True(accepted.IsValid, accepted.Error);
        Assert.False(rejected.IsValid);
        Assert.Contains("3840x2160", rejected.Error);
    }

    [Fact]
    public void ValidateBytes_CanonicalizesInput()
    {
        var source = CreatePng(2, 2);
        var result = _service.ValidateBytes(source, "image/png");

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("image/png", result.Image!.ContentType);
        Assert.Equal(2, result.Image.Width);
        Assert.NotEmpty(result.Image.Content);
    }

    private static byte[] CreatePng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, Color.Black);
        using var output = new MemoryStream();
        image.SaveAsPng(output);
        return output.ToArray();
    }
}

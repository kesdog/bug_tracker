namespace BugTracker.Api.Bugs;

/// <summary>Authoritative limits for ticket text and evidence inputs.</summary>
public static class BugReportLimits
{
    public const int TitleCharacters = 200;
    public const int InitialReportCharacters = 20_000;
    public const int SolutionReportCharacters = 20_000;
    public const int EnvironmentCharacters = 500;
    public const int ExpectedBehaviorCharacters = 2_000;
    public const int ActualBehaviorCharacters = 2_000;
    public const int StepsToReproduceCharacters = 4_000;
    public const int CommentCharacters = 2_000;
    public const int ReopenReasonCharacters = 1_000;
    public const int FileNameCharacters = 80;

    public const int MaxTextEvidenceFiles = 3;
    public const int MaxTextEvidenceBytesPerFile = 100_000;
    public const int MaxTextEvidenceAggregateBytes = 300_000;

    public const int MaxImagesPerReport = 3;
    public const int MaxImageDecodedBytes = 4 * 1024 * 1024;
    public const int MaxImageAggregateDecodedBytes = 12 * 1024 * 1024;
    public const int MaxImageLongSide = 3840;
    public const int MaxImageShortSide = 2160;
    public const long MaxImagePixels = 8_294_400;

    // Enough for three maximum-size images after base64 expansion and JSON framing.
    public const long MaxApiRequestBodyBytes = 17L * 1024 * 1024;
    public const long MaxMultipartRequestBodyBytes = 13L * 1024 * 1024;
    public const long PublicAuthRequestBodyBytes = 4L * 1024;
}

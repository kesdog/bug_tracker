namespace BugTracker.Api.Auth;

public static class AuthConfigurationValidator
{
    private const int MinimumProtectedEnvironmentSecretLength = 32;

    private static readonly HashSet<string> KnownPlaceholderSecrets = new(StringComparer.Ordinal)
    {
        "replace-this-with-a-long-random-secret-for-non-dev-env",
        "dev-secret-change-before-prod",
        "REPLACE_WITH_A_LONG_RANDOM_SECRET"
    };

    public static void ValidateTokenSecret(string tokenSecret, string environmentName)
    {
        if (!IsProtectedEnvironment(environmentName))
        {
            return;
        }

        if (tokenSecret.Length < MinimumProtectedEnvironmentSecretLength || KnownPlaceholderSecrets.Contains(tokenSecret))
        {
            throw new InvalidOperationException(
                "Auth:TokenSecret must be replaced with a strong, deployment-specific secret in Demo and Production.");
        }
    }

    private static bool IsProtectedEnvironment(string environmentName) =>
        environmentName.Equals("Demo", StringComparison.OrdinalIgnoreCase) ||
        environmentName.Equals("Production", StringComparison.OrdinalIgnoreCase);
}

using System.Text;

namespace BugTracker.Api.Auth;

public static class UsernamePolicy
{
    public const int MinLength = 3;
    public const int MaxLength = 32;

    public static bool TryNormalize(string? value, out string username, out string error)
    {
        username = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (username.Length is < MinLength or > MaxLength)
        {
            error = $"username must be between {MinLength} and {MaxLength} characters";
            return false;
        }

        if (!char.IsLetterOrDigit(username[0]) || !char.IsLetterOrDigit(username[^1]))
        {
            error = "username must start and end with a letter or number";
            return false;
        }

        if (username.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not ('-' or '_' or '.')))
        {
            error = "username may contain only letters, numbers, periods, hyphens, and underscores";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static string DefaultFromEmail(string email)
    {
        var local = email.Split('@', 2)[0];
        var builder = new StringBuilder();
        foreach (var ch in local.ToLowerInvariant())
        {
            builder.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '_');
        }

        var username = builder.ToString().Trim('_');
        if (username.Length > MaxLength)
        {
            username = username[..MaxLength];
        }

        return username.Length >= MinLength ? username : $"user_{username}".TrimEnd('_');
    }
}

using System.Security.Cryptography;
using System.Text;

namespace BugTracker.Api.Auth;

public static partial class AuthEndpoints
{
    private const int MaxEmailLength = 254;
    private const int MaxPasswordLength = 256;
    private const int MaxUsernameLength = 100;
    private const int MaxCredentialTokenLength = 256;

    private static string NormalizeUserId(string userId)
    {
        var builder = new StringBuilder();
        foreach (var ch in userId.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (ch is '-' or '_' or '.')
            {
                builder.Append('_');
            }
        }

        var normalized = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? $"usr_{Guid.NewGuid():N}"[..14] : normalized;
    }

    private static bool LooksLikeEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0 || atIndex == email.Length - 1)
        {
            return false;
        }

        var domain = email[(atIndex + 1)..];
        return domain.Contains(".", StringComparison.Ordinal) && !domain.StartsWith(".", StringComparison.Ordinal);
    }

    private static string BuildUserIdFromEmail(string email)
    {
        var local = email.Split('@')[0];
        var builder = new StringBuilder();
        foreach (var ch in local)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (ch is '.' or '-' or '_')
            {
                builder.Append('_');
            }
        }

        var slug = builder.Length == 0 ? "user" : builder.ToString().Trim('_');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "user";
        }

        if (slug.Length > 18)
        {
            slug = slug[..18];
        }

        var suffix = RandomNumberGenerator.GetInt32(100, 1000);
        return $"usr_{slug}_{suffix}";
    }

    private static string GenerateApiKey128()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return $"bta_{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static bool IsStrongPassword(string password, out string error)
    {
        if (password.Length < 6)
        {
            error = "password must be at least 6 characters";
            return false;
        }

        if (!password.Any(char.IsDigit))
        {
            error = "password must include at least one number";
            return false;
        }

        if (!password.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            error = "password must include at least one special character";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static AuthenticatedUser? GetPrincipal(HttpContext context)
    {
        return context.Items[AuthMiddleware.AuthContextKey] as AuthenticatedUser;
    }
}

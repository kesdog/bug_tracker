using System.Security.Cryptography;

namespace BugTracker.Api.Auth;

public sealed class TokenService
{
    private readonly byte[] _secretKey;

    public TokenService(string secret)
    {
        _secretKey = System.Text.Encoding.UTF8.GetBytes(secret);
    }

    public string GenerateRawToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    public string HashToken(string token)
    {
        using var hmac = new HMACSHA256(_secretKey);
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }
}

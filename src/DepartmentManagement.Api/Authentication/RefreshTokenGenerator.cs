using System.Security.Cryptography;
using System.Text;

namespace DepartmentManagement.Api.Authentication;

public static class RefreshTokenGenerator
{
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));

        return Convert.ToHexString(bytes);
    }
}
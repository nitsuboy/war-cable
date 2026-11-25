using System.Security.Cryptography;

namespace Duo.Api.Accounts;

internal static class SessionTokens
{
    public const int TokenBytes = 32;

    public static string Create() =>
        Base64Url.Encode(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>SHA-256 over the UTF-8 token, standard base64 so it sorts/stores cleanly.</summary>
    public static string Hash(string token) =>
        Convert.ToBase64String(SHA256.HashData(Base64Url.Utf8(token)));
}

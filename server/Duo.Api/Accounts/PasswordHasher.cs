using System.Security.Cryptography;

namespace Duo.Api.Accounts;

/// <summary>
/// PBKDF2-HMAC-SHA256 password hashing. The iteration count is part of the
/// stored string so old hashes stay verifiable after a future bump.
/// </summary>
public sealed class PasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int MinIterations = 1_000;
    private const int MaxIterations = 2_000_000;

    private readonly int _iterations;

    public PasswordHasher(int iterations = 600_000) => _iterations = iterations;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            _iterations,
            HashAlgorithmName.SHA256,
            HashBytes);

        return string.Join('$', Prefix, _iterations, Base64Url.Encode(salt), Base64Url.Encode(hash));
    }

    public bool Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return false;
        }

        var parts = stored.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations)
            || iterations is < MinIterations or > MaxIterations)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Base64Url.Decode(parts[2]);
            expected = Base64Url.Decode(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0)
        {
            return false;
        }

        var computed = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expected.Length);

        return CryptographicOperations.FixedTimeEquals(computed, expected);
    }
}

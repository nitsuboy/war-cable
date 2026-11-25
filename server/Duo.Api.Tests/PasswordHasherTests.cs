using Duo.Api.Accounts;

namespace Duo.Api.Tests;

public class PasswordHasherTests
{
    private static PasswordHasher Hasher() => new(iterations: 1000);

    [Fact]
    public void Hash_then_verify_roundtrips()
    {
        var hasher = Hasher();
        var stored = hasher.Hash("correct horse battery staple");

        Assert.StartsWith("pbkdf2-sha256$", stored);
        Assert.True(hasher.Verify("correct horse battery staple", stored));
    }

    [Fact]
    public void Verify_rejects_wrong_password()
    {
        var hasher = Hasher();
        var stored = hasher.Hash("password-one");

        Assert.False(hasher.Verify("password-two", stored));
    }

    [Fact]
    public void Same_password_gets_a_fresh_salt_each_time()
    {
        var hasher = Hasher();

        Assert.NotEqual(hasher.Hash("same-password"), hasher.Hash("same-password"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("sha256$1000$c2FsdA$hash")]
    [InlineData("pbkdf2-sha256$999$c2FsdA$hash")]
    [InlineData("pbkdf2-sha256$ten$c2FsdA$hash")]
    [InlineData("pbkdf2-sha256$1000$***$***")]
    [InlineData("pbkdf2-sha256$1000$c2FsdA")]
    public void Verify_rejects_malformed_storage(string stored)
    {
        Assert.False(Hasher().Verify("password", stored));
    }
}

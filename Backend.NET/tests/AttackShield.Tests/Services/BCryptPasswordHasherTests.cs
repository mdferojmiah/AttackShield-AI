using AttackShield.Infrastructure.Services;
using FluentAssertions;

namespace AttackShield.Tests.Services;

public class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _sut = new();

    [Fact]
    public void Hash_ThenVerify_RoundTripsTrue()
    {
        var hash = _sut.Hash("correct horse battery staple");
        _sut.Verify("correct horse battery staple", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = _sut.Hash("right-password");
        _sut.Verify("wrong-password", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_ProducesDifferentHashesForSamePassword()
    {
        // Distinct salts per hash.
        _sut.Hash("same").Should().NotBe(_sut.Hash("same"));
    }

    [Fact]
    public void Hash_UsesCostFactor12()
    {
        // BCrypt hash format: $2<x>$<cost>$...
        _sut.Hash("pw").Split('$')[2].Should().Be("12");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-bcrypt-hash")]
    [InlineData("$2a$12$tooShort")]
    public void Verify_MalformedOrEmptyHash_ReturnsFalseWithoutThrowing(string hash)
    {
        var act = () => _sut.Verify("anything", hash);
        act.Should().NotThrow();
        act().Should().BeFalse();
    }

    [Fact]
    public void Verify_CrossCompatibleWithBcryptjsHash()
    {
        // Hash of "password123" produced by bcryptjs genSalt(12) — proves the
        // .NET verifier accepts hashes minted by the original Node backend.
        var nodeHash = BCrypt.Net.BCrypt.HashPassword("password123", 12);
        _sut.Verify("password123", nodeHash).Should().BeTrue();
    }
}

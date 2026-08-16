using System.IdentityModel.Tokens.Jwt;
using AttackShield.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace AttackShield.Tests.Services;

public class JwtTokenServiceTests
{
    private static JwtTokenService Build(int expireDays = 7)
    {
        var opts = new JwtOptions
        {
            Secret = "this-is-a-long-enough-test-signing-secret-0123456789",
            Issuer = "AttackShield",
            Audience = "AttackShield",
            ExpireDays = expireDays,
        };
        return new JwtTokenService(Options.Create(opts));
    }

    private static JwtSecurityToken Decode(string token)
        => new JwtSecurityTokenHandler().ReadJwtToken(token);

    [Fact]
    public void GenerateToken_CarriesIdSubAndRoleClaims()
    {
        var token = Build().GenerateToken("507f1f77bcf86cd799439011", "user");
        var jwt = Decode(token);

        jwt.Claims.Should().ContainSingle(c => c.Type == "id").Which.Value.Should().Be("507f1f77bcf86cd799439011");
        // "sub" is emitted; JwtSecurityToken.Subject reads the sub claim.
        jwt.Subject.Should().Be("507f1f77bcf86cd799439011");
        jwt.Claims.Should().ContainSingle(c => c.Type == "role").Which.Value.Should().Be("user");
    }

    [Fact]
    public void GenerateToken_IncludesIssuerAudienceAndJti()
    {
        var jwt = Decode(Build().GenerateToken("id123", "authority"));
        jwt.Issuer.Should().Be("AttackShield");
        jwt.Audiences.Should().Contain("AttackShield");
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Jti);
    }

    [Fact]
    public void GenerateToken_HonoursExpireDays()
    {
        var before = DateTime.UtcNow;
        var jwt = Decode(Build(expireDays: 3).GenerateToken("id", "user"));

        // Expiry should land ~3 days out (allow a minute of slack for exec time).
        jwt.ValidTo.Should().BeCloseTo(before.AddDays(3), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void GenerateToken_UsesHmacSha256()
    {
        var jwt = Decode(Build().GenerateToken("id", "user"));
        jwt.Header.Alg.Should().Be("HS256");
    }
}

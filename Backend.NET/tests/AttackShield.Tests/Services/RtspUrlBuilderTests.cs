using AttackShield.Infrastructure.Services;
using FluentAssertions;

namespace AttackShield.Tests.Services;

public class RtspUrlBuilderTests
{
    private readonly RtspUrlBuilder _sut = new();

    [Fact]
    public void Build_ReturnsNull_WhenIpMissing()
    {
        _sut.Build(ip: null, username: "u", password: "p").Should().BeNull();
        _sut.Build(ip: "", username: "u", password: "p").Should().BeNull();
        _sut.Build(ip: "   ", username: "u", password: "p").Should().BeNull();
    }

    [Fact]
    public void Build_Hikvision_UsesChannel101Path_AndPort554()
    {
        var url = _sut.Build("10.0.0.5", null, null, brand: "Hikvision");
        url.Should().Be("rtsp://10.0.0.5:554/Streaming/Channels/101");
    }

    [Fact]
    public void Build_Dahua_UsesRealmonitorPath()
    {
        var url = _sut.Build("10.0.0.5", null, null, brand: "Dahua");
        url.Should().Be("rtsp://10.0.0.5:554/cam/realmonitor?channel=1&subtype=1");
    }

    [Fact]
    public void Build_Meari_DefaultsToPort8554_AndLivePath()
    {
        var url = _sut.Build("10.0.0.5", null, null, brand: "Meari");
        url.Should().Be("rtsp://10.0.0.5:8554/live");
    }

    [Fact]
    public void Build_Meari_ExplicitPort_OverridesDefault8554()
    {
        var url = _sut.Build("10.0.0.5", null, null, port: "5540", brand: "Meari");
        url.Should().Be("rtsp://10.0.0.5:5540/live");
    }

    [Fact]
    public void Build_UnknownBrand_FallsBackToDahuaStylePath()
    {
        var url = _sut.Build("10.0.0.5", null, null, brand: "SomethingElse");
        url.Should().Be("rtsp://10.0.0.5:554/cam/realmonitor?channel=1&subtype=1");
    }

    [Fact]
    public void Build_NullBrand_FallsBackToGenericPath()
    {
        var url = _sut.Build("10.0.0.5", null, null);
        url.Should().Be("rtsp://10.0.0.5:554/cam/realmonitor?channel=1&subtype=1");
    }

    [Fact]
    public void Build_ExplicitPath_OverridesBrandDefault()
    {
        var url = _sut.Build("10.0.0.5", null, null, brand: "Hikvision", path: "/custom/stream");
        url.Should().Be("rtsp://10.0.0.5:554/custom/stream");
    }

    [Fact]
    public void Build_WithCredentials_EmbedsUserAndPassword()
    {
        var url = _sut.Build("10.0.0.5", "admin", "secret", brand: "Hikvision");
        url.Should().Be("rtsp://admin:secret@10.0.0.5:554/Streaming/Channels/101");
    }

    [Fact]
    public void Build_WithoutCredentials_OmitsUserInfo()
    {
        _sut.Build("10.0.0.5", "admin", null).Should().Be("rtsp://10.0.0.5:554/cam/realmonitor?channel=1&subtype=1");
        _sut.Build("10.0.0.5", null, "secret").Should().Be("rtsp://10.0.0.5:554/cam/realmonitor?channel=1&subtype=1");
        _sut.Build("10.0.0.5", "", "").Should().Be("rtsp://10.0.0.5:554/cam/realmonitor?channel=1&subtype=1");
    }

    [Fact]
    public void Build_UrlEncodes_UsernameAndPassword()
    {
        var url = _sut.Build("10.0.0.5", "user@domain", "p@ss:w/rd", brand: "Hikvision");
        // '@' -> %40, ':' -> %3A, '/' -> %2F
        url.Should().Be("rtsp://user%40domain:p%40ss%3Aw%2Frd@10.0.0.5:554/Streaming/Channels/101");
    }

    [Fact]
    public void Build_InvalidPort_FallsBackToDefault554()
    {
        _sut.Build("10.0.0.5", null, null, port: "abc").Should().Be("rtsp://10.0.0.5:554/cam/realmonitor?channel=1&subtype=1");
        _sut.Build("10.0.0.5", null, null, port: "0").Should().Be("rtsp://10.0.0.5:554/cam/realmonitor?channel=1&subtype=1");
        _sut.Build("10.0.0.5", null, null, port: "-5").Should().Be("rtsp://10.0.0.5:554/cam/realmonitor?channel=1&subtype=1");
    }

    [Fact]
    public void Build_BrandMatchIsCaseInsensitive()
    {
        _sut.Build("10.0.0.5", null, null, brand: "HIKVISION").Should().Be("rtsp://10.0.0.5:554/Streaming/Channels/101");
    }
}

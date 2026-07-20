using System.Net;
using System.Text;
using System.Text.Json;
using AttackShield.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AttackShield.Tests.Services;

public class AiServiceClientTests
{
    // Records the last request and returns a canned response (or throws).
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public static StubHandler Returning(HttpStatusCode status, string body = "")
            => new(_ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

        public static StubHandler Throwing(Exception ex) => new(_ => throw ex);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return _responder(request);
        }
    }

    private static AiServiceClient Client(StubHandler handler)
        => new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8000") }, NullLogger<AiServiceClient>.Instance);

    [Fact]
    public async Task Success_ReturnsOk_WithRawJson()
    {
        const string json = "{\"status\":\"started\",\"session\":42}";
        var result = await Client(StubHandler.Returning(HttpStatusCode.OK, json))
            .StartDetectionAsync("rtsp://cam/stream", "Lobby", "user1");

        result.Success.Should().BeTrue();
        result.RawJson.Should().Be(json);
        result.Error.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task NonSuccessStatus_ReturnsFail_NeverThrows(HttpStatusCode status)
    {
        var result = await Client(StubHandler.Returning(status, "{\"error\":\"nope\"}"))
            .CheckHealthAsync();

        result.Success.Should().BeFalse();
        result.RawJson.Should().BeNull();
        result.Error.Should().Contain(((int)status).ToString());
    }

    [Fact]
    public async Task TransportException_ReturnsFail_NeverThrows()
    {
        var result = await Client(StubHandler.Throwing(new HttpRequestException("connection refused")))
            .GetMetricsAsync();

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("connection refused");
    }

    [Fact]
    public async Task StartDetection_PostsSnakeCaseBody()
    {
        var handler = StubHandler.Returning(HttpStatusCode.OK, "{}");
        await Client(handler).StartDetectionAsync("rtsp://cam/stream", "Lobby", "user1");

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/start-detection");

        using var doc = JsonDocument.Parse(handler.LastBody!);
        var root = doc.RootElement;
        root.GetProperty("rtsp_url").GetString().Should().Be("rtsp://cam/stream");
        root.GetProperty("location").GetString().Should().Be("Lobby");
        root.GetProperty("user_id").GetString().Should().Be("user1");
        // camelCase keys must NOT be present.
        root.TryGetProperty("rtspUrl", out _).Should().BeFalse();
        root.TryGetProperty("userId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task StartDetection_SerialisesNullLocationAndUser()
    {
        var handler = StubHandler.Returning(HttpStatusCode.OK, "{}");
        await Client(handler).StartDetectionAsync("rtsp://cam/stream", null, null);

        using var doc = JsonDocument.Parse(handler.LastBody!);
        var root = doc.RootElement;
        root.GetProperty("rtsp_url").GetString().Should().Be("rtsp://cam/stream");
        root.GetProperty("location").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("user_id").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task StopDetection_PostsToStopEndpoint()
    {
        var handler = StubHandler.Returning(HttpStatusCode.OK, "{}");
        var result = await Client(handler).StopDetectionAsync();

        result.Success.Should().BeTrue();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/stop-detection");
    }

    [Fact]
    public async Task GetEndpoints_UseHttpGet()
    {
        var handler = StubHandler.Returning(HttpStatusCode.OK, "{}");
        await Client(handler).GetTrustScoreAsync();

        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/trust-score");
    }
}

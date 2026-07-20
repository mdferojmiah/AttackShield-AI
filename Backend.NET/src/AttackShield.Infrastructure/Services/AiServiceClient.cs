using System.Net.Http.Json;
using System.Text.Json;
using AttackShield.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AttackShield.Infrastructure.Services;

/// <summary>
/// Typed HttpClient wrapper over the Python FastAPI AI service. All methods are
/// best-effort: transport failures return <see cref="AiCallResult.Fail"/> rather
/// than throwing, so the API keeps working when the AI service is offline.
/// The base address and timeout are configured on the injected HttpClient in DI.
/// </summary>
public sealed class AiServiceClient : IAiServiceClient
{
    private readonly HttpClient _http;
    private readonly ILogger<AiServiceClient> _logger;

    public AiServiceClient(HttpClient http, ILogger<AiServiceClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public Task<AiCallResult> StartDetectionAsync(string rtspUrl, string? location, string? userId, CancellationToken ct = default)
    {
        // Snake_case keys — the FastAPI service expects them.
        var body = new Dictionary<string, object?>
        {
            ["rtsp_url"] = rtspUrl,
            ["location"] = location,
            ["user_id"] = userId,
        };
        return PostAsync("/start-detection", body, ct);
    }

    public Task<AiCallResult> StopDetectionAsync(CancellationToken ct = default)
        => PostAsync("/stop-detection", new { }, ct);

    public Task<AiCallResult> CheckHealthAsync(CancellationToken ct = default)
        => GetAsync("/health", ct);

    public Task<AiCallResult> GetMetricsAsync(CancellationToken ct = default)
        => GetAsync("/metrics", ct);

    public Task<AiCallResult> GetTrustScoreAsync(CancellationToken ct = default)
        => GetAsync("/trust-score", ct);

    private async Task<AiCallResult> PostAsync(string path, object body, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.PostAsJsonAsync(path, body, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI service {Path} returned {Status}", path, (int)resp.StatusCode);
                return AiCallResult.Fail($"AI service returned {(int)resp.StatusCode}");
            }
            return AiCallResult.Ok(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI service {Path} unreachable", path);
            return AiCallResult.Fail(ex.Message);
        }
    }

    private async Task<AiCallResult> GetAsync(string path, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(path, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI service {Path} returned {Status}", path, (int)resp.StatusCode);
                return AiCallResult.Fail($"AI service returned {(int)resp.StatusCode}");
            }
            return AiCallResult.Ok(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI service {Path} unreachable", path);
            return AiCallResult.Fail(ex.Message);
        }
    }

    // Exposed for tests that assert JSON round-trips through the client.
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

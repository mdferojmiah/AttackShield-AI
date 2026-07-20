namespace AttackShield.Core.Interfaces;

/// <summary>
/// Talks to the Python FastAPI AI service (port 8000). Every call is best-effort:
/// the AI service may be down, so implementations return a result wrapper rather
/// than throwing for transport failures.
/// </summary>
public interface IAiServiceClient
{
    Task<AiCallResult> StartDetectionAsync(string rtspUrl, string? location, string? userId, CancellationToken ct = default);
    Task<AiCallResult> StopDetectionAsync(CancellationToken ct = default);
    Task<AiCallResult> CheckHealthAsync(CancellationToken ct = default);

    /// <summary>Raw ensemble metrics document from GET /metrics (shape is passed through untouched).</summary>
    Task<AiCallResult> GetMetricsAsync(CancellationToken ct = default);

    /// <summary>Raw trust-score document from GET /trust-score.</summary>
    Task<AiCallResult> GetTrustScoreAsync(CancellationToken ct = default);
}

/// <summary>
/// Result of an AI service call. <see cref="Success"/> is false when the service
/// was unreachable or returned a non-success status; callers fall back to defaults.
/// <see cref="RawJson"/> holds the response body verbatim for pass-through endpoints.
/// </summary>
public sealed record AiCallResult(bool Success, string? RawJson, string? Error)
{
    public static AiCallResult Ok(string? json) => new(true, json, null);
    public static AiCallResult Fail(string error) => new(false, null, error);
}

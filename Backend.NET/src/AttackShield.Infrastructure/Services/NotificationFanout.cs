using System.Net.Http.Json;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AttackShield.Infrastructure.Services;

public sealed class NotificationFanoutOptions
{
    public const string SectionName = "Notifications";
    public SmtpOptions Smtp { get; set; } = new();
    public WebhookOptions Webhooks { get; set; } = new();
}

public sealed class SmtpOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public string FromName { get; set; } = "AttackShield AI";
    public bool EnableSsl { get; set; } = true;

    /// <summary>
    /// Minimum gap between emails for the same recipient and alert type. Guards the
    /// provider's daily send quota when a camera fires repeatedly. 0 disables it.
    /// </summary>
    public int CooldownMinutes { get; set; } = 5;
}

public sealed class WebhookOptions
{
    public bool Enabled { get; set; }
    public string[] Urls { get; set; } = [];
}

/// <summary>
/// Alert payload shared by the webhook and email legs. Serializes to the same
/// camelCase JSON the webhook receivers already consume.
/// </summary>
public sealed record AlertNotification(
    string Type,
    string Title,
    string Message,
    string? Location,
    double Confidence,
    string? CameraName,
    string? ImageUrl,
    DateTime CreatedAt);

/// <summary>
/// Outcome of the email leg, so callers can tell the user why no mail arrived and
/// when the next one becomes possible. <paramref name="NextAllowedAt"/> is UTC.
/// </summary>
/// <param name="Considered">False when email alerts are off for this user or globally.</param>
public sealed record EmailCooldownState(bool Considered, bool Sent, DateTime? NextAllowedAt, int CooldownMinutes)
{
    public static readonly EmailCooldownState NotConsidered = new(false, false, null, 0);
}

public sealed class NotificationFanout
{
    // Reject oversized snapshots rather than let the SMTP server bounce the message.
    private const int MaxAttachmentBytes = 5 * 1024 * 1024;

    private readonly IHttpClientFactory _http;
    private readonly NotificationFanoutOptions _options;
    private readonly ILogger<NotificationFanout> _logger;

    // "recipient|type" -> last send. Singleton lifetime, so this survives requests.
    // Bounded by recipients x alert types, so entries are never evicted.
    private readonly Dictionary<string, DateTime> _lastEmail = new(StringComparer.OrdinalIgnoreCase);

    public NotificationFanout(
        IHttpClientFactory http,
        IOptions<NotificationFanoutOptions> options,
        ILogger<NotificationFanout> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    /// <param name="email">
    /// Recipient address, or null when the user has not opted in to email alerts.
    /// </param>
    /// <returns>What happened on the email leg, for surfacing in the UI.</returns>
    public async Task<EmailCooldownState> PublishAsync(AlertNotification payload, string? email, CancellationToken ct = default)
    {
        if (_options.Webhooks.Enabled)
            await PublishWebhooksAsync(payload, ct);

        if (!_options.Smtp.Enabled || string.IsNullOrWhiteSpace(email))
            return EmailCooldownState.NotConsidered;

        var claim = ClaimCooldown(email, payload.Type);
        if (claim.Sent)
            DispatchEmail(payload, email);
        return claim;
    }

    internal EmailCooldownState ClaimCooldown(string recipient, string type)
    {
        var window = TimeSpan.FromMinutes(Math.Max(_options.Smtp.CooldownMinutes, 0));
        if (window == TimeSpan.Zero)
            return new EmailCooldownState(true, true, null, 0);

        var now = DateTime.UtcNow;
        var key = Key(recipient, type);

        lock (_lastEmail)
        {
            if (_lastEmail.TryGetValue(key, out var previous) && now - previous < window)
            {
                _logger.LogDebug("Email alert for {Type} suppressed by cooldown", type);
                return new EmailCooldownState(true, false, previous + window, _options.Smtp.CooldownMinutes);
            }
            _lastEmail[key] = now;
            return new EmailCooldownState(true, true, now + window, _options.Smtp.CooldownMinutes);
        }
    }

    /// <summary>
    /// The recipient's active throttle with the longest time left — i.e. the one from
    /// their most recent alert email — or null when nothing is throttled. Read-only:
    /// unlike <see cref="ClaimCooldown"/> this never starts a window, so polling is safe.
    /// </summary>
    public (string Type, DateTime NextAllowedAt)? PeekCooldown(string recipient)
    {
        var window = TimeSpan.FromMinutes(Math.Max(_options.Smtp.CooldownMinutes, 0));
        if (window == TimeSpan.Zero) return null;

        var prefix = $"{recipient}|";
        var now = DateTime.UtcNow;

        lock (_lastEmail)
        {
            (string Type, DateTime NextAllowedAt)? latest = null;
            foreach (var (key, sentAt) in _lastEmail)
            {
                if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var next = sentAt + window;
                if (next <= now) continue;
                if (latest is null || next > latest.Value.NextAllowedAt)
                    latest = (key[prefix.Length..], next);
            }
            return latest;
        }
    }

    public int CooldownMinutes => _options.Smtp.CooldownMinutes;

    public bool EmailEnabled => _options.Smtp.Enabled;

    private static string Key(string recipient, string type) => $"{recipient}|{type}";

    private async Task PublishWebhooksAsync(AlertNotification payload, CancellationToken ct)
    {
        foreach (var url in _options.Webhooks.Urls.Where(url => Uri.TryCreate(url, UriKind.Absolute, out _)))
        {
            try
            {
                var response = await _http.CreateClient().PostAsJsonAsync(url, payload, ct);
                if (!response.IsSuccessStatusCode)
                    _logger.LogWarning("Notification webhook {Url} returned {Status}", url, response.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "Notification webhook {Url} failed", url);
            }
        }
    }

    // An SMTP handshake costs seconds and this runs inside POST /api/detections/receive.
    // Detached with CancellationToken.None so the response completing cannot abort the send.
    private void DispatchEmail(AlertNotification payload, string recipient)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await SendEmailAsync(payload, recipient);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Security alert email failed for {Recipient}", recipient);
            }
        }, CancellationToken.None);
    }

    private async Task SendEmailAsync(AlertNotification payload, string recipient)
    {
        var from = string.IsNullOrWhiteSpace(_options.Smtp.From) ? _options.Smtp.Username : _options.Smtp.From;

        using var message = new MailMessage
        {
            From = new MailAddress(from, _options.Smtp.FromName),
            Subject = BuildSubject(payload),
            SubjectEncoding = Encoding.UTF8,
        };
        message.To.Add(recipient);

        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            BuildPlainBody(payload), Encoding.UTF8, MediaTypeNames.Text.Plain));

        var snapshot = TryCreateSnapshot(payload.ImageUrl);
        var htmlView = AlternateView.CreateAlternateViewFromString(
            BuildHtmlBody(payload, snapshot?.ContentId), Encoding.UTF8, MediaTypeNames.Text.Html);
        if (snapshot is not null)
            htmlView.LinkedResources.Add(snapshot);
        message.AlternateViews.Add(htmlView);

        using var client = new SmtpClient(_options.Smtp.Host, _options.Smtp.Port)
        {
            EnableSsl = _options.Smtp.EnableSsl,
            Credentials = new System.Net.NetworkCredential(_options.Smtp.Username, _options.Smtp.Password),
        };
        await client.SendMailAsync(message, CancellationToken.None);
        _logger.LogInformation("Security alert email sent to {Recipient} for {Type}", recipient, payload.Type);
    }

    internal static string BuildSubject(AlertNotification payload)
    {
        var where = string.IsNullOrWhiteSpace(payload.Location) ? "" : $" \u2014 {payload.Location}";
        return $"[AttackShield] {payload.Title}{where}";
    }

    private static string BuildPlainBody(AlertNotification payload)
    {
        var lines = new List<string> { payload.Title, "", payload.Message, "", $"Alert type : {Humanize(payload.Type)}" };
        if (!string.IsNullOrWhiteSpace(payload.Location)) lines.Add($"Location   : {payload.Location}");
        if (!string.IsNullOrWhiteSpace(payload.CameraName)) lines.Add($"Camera     : {payload.CameraName}");
        lines.Add($"Confidence : {payload.Confidence * 100:0.#}%");
        lines.Add($"Detected   : {payload.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
        if (IsWebUrl(payload.ImageUrl)) lines.Add($"Snapshot   : {payload.ImageUrl}");
        lines.Add("");
        lines.Add("You are receiving this because email alerts are enabled in your AttackShield settings.");
        return string.Join(Environment.NewLine, lines);
    }

    // Every interpolated value traces back to detection input, so encode all of it.
    internal static string BuildHtmlBody(AlertNotification payload, string? snapshotContentId)
    {
        var rows = new StringBuilder();
        AppendRow(rows, "Alert type", Humanize(payload.Type));
        if (!string.IsNullOrWhiteSpace(payload.Location)) AppendRow(rows, "Location", payload.Location!);
        if (!string.IsNullOrWhiteSpace(payload.CameraName)) AppendRow(rows, "Camera", payload.CameraName!);
        AppendRow(rows, "Confidence", $"{payload.Confidence * 100:0.#}%");
        AppendRow(rows, "Detected", $"{payload.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");

        var snapshot = "";
        if (snapshotContentId is not null)
            snapshot = $"""<p style="margin:20px 0 0"><img src="cid:{Encode(snapshotContentId)}" alt="Detection snapshot" style="max-width:100%;border-radius:8px;border:1px solid #e2e8f0"></p>""";
        else if (IsWebUrl(payload.ImageUrl))
            snapshot = $"""<p style="margin:20px 0 0"><a href="{Encode(payload.ImageUrl!)}" style="color:#2563eb">View the detection snapshot</a></p>""";

        return $"""
            <!DOCTYPE html>
            <html><body style="margin:0;padding:24px;background:#f1f5f9;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#1e293b">
              <div style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:12px;padding:24px">
                <p style="margin:0 0 4px;font-size:12px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;color:#dc2626">Security alert</p>
                <h1 style="margin:0 0 12px;font-size:20px">{Encode(payload.Title)}</h1>
                <p style="margin:0 0 20px;font-size:14px;line-height:1.6;color:#475569">{Encode(payload.Message)}</p>
                <table style="width:100%;border-collapse:collapse;font-size:14px">{rows}</table>
                {snapshot}
                <p style="margin:24px 0 0;font-size:12px;color:#94a3b8">
                  You are receiving this because email alerts are enabled in your AttackShield settings.
                </p>
              </div>
            </body></html>
            """;
    }

    private static void AppendRow(StringBuilder rows, string label, string value) => rows.Append(
        $"""<tr><td style="padding:6px 0;color:#64748b;width:120px">{Encode(label)}</td><td style="padding:6px 0;font-weight:600">{Encode(value)}</td></tr>""");

    /// <summary>
    /// The AI service sends snapshots as base64 data URIs, which mail clients refuse to
    /// render from an img src. Turn them into an inline part the HTML references by cid.
    /// </summary>
    internal static LinkedResource? TryCreateSnapshot(string? imageUrl)
    {
        const string base64Marker = ";base64";
        if (string.IsNullOrWhiteSpace(imageUrl) || !imageUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            return null;

        var comma = imageUrl.IndexOf(',');
        if (comma < 0) return null;

        var header = imageUrl[5..comma];
        if (!header.EndsWith(base64Marker, StringComparison.OrdinalIgnoreCase)) return null;

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(imageUrl[(comma + 1)..]);
        }
        catch (FormatException)
        {
            return null;
        }
        if (bytes.Length is 0 or > MaxAttachmentBytes) return null;

        return new LinkedResource(new MemoryStream(bytes), header[..^base64Marker.Length])
        {
            ContentId = Guid.NewGuid().ToString("N"),
        };
    }

    private static bool IsWebUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string Humanize(string type) => type.Replace('_', ' ');

    private static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
}

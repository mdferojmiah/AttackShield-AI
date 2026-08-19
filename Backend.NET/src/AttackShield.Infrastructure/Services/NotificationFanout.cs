using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
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
    public bool EnableSsl { get; set; } = true;
}

public sealed class WebhookOptions
{
    public bool Enabled { get; set; }
    public string[] Urls { get; set; } = [];
}

public sealed class NotificationFanout
{
    private readonly IHttpClientFactory _http;
    private readonly NotificationFanoutOptions _options;
    private readonly ILogger<NotificationFanout> _logger;

    public NotificationFanout(
        IHttpClientFactory http,
        IOptions<NotificationFanoutOptions> options,
        ILogger<NotificationFanout> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task PublishAsync(object payload, string? email, CancellationToken ct = default)
    {
        var tasks = new List<Task>();
        if (_options.Webhooks.Enabled)
            tasks.Add(PublishWebhooksAsync(payload, ct));
        if (_options.Smtp.Enabled && !string.IsNullOrWhiteSpace(email))
            tasks.Add(PublishEmailAsync(payload, email, ct));
        await Task.WhenAll(tasks);
    }

    private async Task PublishWebhooksAsync(object payload, CancellationToken ct)
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

    private Task PublishEmailAsync(object payload, string recipient, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        return Task.Run(() =>
        {
            using var message = new MailMessage(_options.Smtp.From, recipient)
            {
                Subject = "AttackShield security alert",
                Body = json,
                IsBodyHtml = false,
            };
            using var client = new SmtpClient(_options.Smtp.Host, _options.Smtp.Port)
            {
                EnableSsl = _options.Smtp.EnableSsl,
                Credentials = new System.Net.NetworkCredential(_options.Smtp.Username, _options.Smtp.Password),
            };
            client.Send(message);
        }, ct).ContinueWith(task =>
        {
            if (task.IsFaulted)
                _logger.LogWarning(task.Exception?.GetBaseException(), "Security alert email failed for {Recipient}", recipient);
        }, CancellationToken.None);
    }
}

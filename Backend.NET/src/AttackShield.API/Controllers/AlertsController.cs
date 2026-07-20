using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttackShield.Api.Controllers;

/// <summary>
/// Authority alert workflow. Ported from the Node alertsController.
/// Every route requires an authority role (authority / senior_authority / admin),
/// matching the router-level authorize() guard in the original.
/// </summary>
[Route("api/alerts")]
[Authorize(Roles = "authority,senior_authority,admin")]
public sealed class AlertsController : ApiControllerBase
{
    private readonly IAlertRepository _alerts;

    public AlertsController(IAlertRepository alerts)
    {
        _alerts = alerts;
    }

    [HttpGet("new")]
    public async Task<IActionResult> GetNew(CancellationToken ct)
    {
        var alerts = await _alerts.GetNewAsync(ct);
        return Ok(new { success = true, data = alerts });
    }

    [HttpGet("my-active")]
    public async Task<IActionResult> GetMyActive(CancellationToken ct)
    {
        var id = CurrentUserId;
        if (id is null) return Fail("Unauthorized", 401);

        var alerts = await _alerts.GetActiveForAuthorityAsync(id, ct);
        return Ok(new { success = true, data = alerts });
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] string? type,
        [FromQuery] string? startDate,
        [FromQuery] string? endDate,
        [FromQuery] string? q,
        CancellationToken ct)
    {
        var id = CurrentUserId;
        if (id is null) return Fail("Unauthorized", 401);

        IEnumerable<Alert> alerts = await _alerts.GetHistoryForAuthorityAsync(id, ct);

        if (!string.IsNullOrWhiteSpace(type))
            alerts = alerts.Where(a => a.Type == type);

        if (DateTime.TryParse(startDate, out var start))
            alerts = alerts.Where(a => a.CreatedAt >= start);

        if (DateTime.TryParse(endDate, out var end))
            alerts = alerts.Where(a => a.CreatedAt <= end);

        // Case-insensitive text search across message/location/title.
        if (!string.IsNullOrWhiteSpace(q))
        {
            alerts = alerts.Where(a =>
                Contains(a.Message, q) || Contains(a.Location, q) || Contains(a.Title, q));
        }

        return Ok(new { success = true, data = alerts.ToList() });
    }

    [HttpPost("{id}/accept")]
    public async Task<IActionResult> Accept(string id, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Fail("Unauthorized", 401);

        var alert = await _alerts.GetByIdAsync(id, ct);
        if (alert is null) return Fail("Alert not found", 404);

        // Only claimable when still new, unless the current authority already owns it.
        if (alert.Status != "new" && alert.AssignedTo != userId)
            return Fail("Alert already handled by another authority");

        alert.Status = "accepted";
        alert.AssignedTo = userId;
        alert.AcceptedAt = DateTime.UtcNow;
        alert.UpdatedAt = DateTime.UtcNow;
        await _alerts.UpdateAsync(id, alert, ct);

        return Ok(new { success = true, data = alert });
    }

    [HttpPost("{id}/dismiss")]
    public async Task<IActionResult> Dismiss(string id, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Fail("Unauthorized", 401);

        var alert = await _alerts.GetByIdAsync(id, ct);
        if (alert is null) return Fail("Alert not found", 404);

        alert.Status = "dismissed";
        alert.AssignedTo = userId;
        alert.UpdatedAt = DateTime.UtcNow;
        await _alerts.UpdateAsync(id, alert, ct);

        return Ok(new { success = true, data = alert });
    }

    [HttpPost("{id}/resolve")]
    public async Task<IActionResult> Resolve(string id, CancellationToken ct)
    {
        var alert = await _alerts.GetByIdAsync(id, ct);
        if (alert is null) return Fail("Alert not found", 404);

        alert.Status = "resolved";
        alert.ResolvedAt = DateTime.UtcNow;
        alert.UpdatedAt = DateTime.UtcNow;
        await _alerts.UpdateAsync(id, alert, ct);

        return Ok(new { success = true, data = alert });
    }

    private static bool Contains(string? haystack, string needle)
        => haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}

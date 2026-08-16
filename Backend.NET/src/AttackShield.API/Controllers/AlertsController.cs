using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttackShield.Api.Controllers;

[Route("api/alerts")]
[Authorize(Roles = "authority,senior_authority,admin")]
public sealed class AlertsController : ApiControllerBase
{
    private readonly IAlertRepository _alerts;

    public AlertsController(IAlertRepository alerts) => _alerts = alerts;

    [HttpGet("new")]
    public async Task<IActionResult> GetNew(CancellationToken ct)
        => Ok(new { success = true, data = await _alerts.GetNewAsync(ct) });

    [HttpGet("my-active")]
    public async Task<IActionResult> GetActive(CancellationToken ct)
        => Ok(new { success = true, data = await _alerts.GetActiveForAuthorityAsync(CurrentUserId!, ct) });

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(CancellationToken ct)
        => Ok(new { success = true, data = await _alerts.GetHistoryForAuthorityAsync(CurrentUserId!, ct) });

    [HttpPost("{id}/accept")]
    public async Task<IActionResult> Accept(string id, CancellationToken ct)
    {
        var alert = await _alerts.GetByIdAsync(id, ct);
        if (alert is null) return Fail("Alert not found", 404);
        if (alert.Status != "new" && alert.AssignedTo != CurrentUserId)
            return Fail("Alert already handled by another authority");

        alert.Status = "accepted";
        alert.AssignedTo = CurrentUserId;
        alert.AcceptedAt = DateTime.UtcNow;
        alert.UpdatedAt = DateTime.UtcNow;
        await _alerts.UpdateAsync(id, alert, ct);
        return Ok(new { success = true, data = alert });
    }

    [HttpPost("{id}/dismiss")]
    public Task<IActionResult> Dismiss(string id, CancellationToken ct)
        => SetStatus(id, "dismissed", ct);

    [HttpPost("{id}/resolve")]
    public Task<IActionResult> Resolve(string id, CancellationToken ct)
        => SetStatus(id, "resolved", ct);

    private async Task<IActionResult> SetStatus(string id, string status, CancellationToken ct)
    {
        var alert = await _alerts.GetByIdAsync(id, ct);
        if (alert is null) return Fail("Alert not found", 404);
        alert.Status = status;
        alert.AssignedTo = CurrentUserId;
        if (status == "resolved") alert.ResolvedAt = DateTime.UtcNow;
        alert.UpdatedAt = DateTime.UtcNow;
        await _alerts.UpdateAsync(id, alert, ct);
        return Ok(new { success = true, data = alert });
    }
}

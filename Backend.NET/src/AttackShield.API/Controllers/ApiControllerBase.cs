using Microsoft.AspNetCore.Mvc;

namespace AttackShield.Api.Controllers;

/// <summary>
/// Shared base for API controllers. Exposes the authenticated principal's id/role
/// (read from the "id"/"role" claims minted by <c>JwtTokenService</c>) and small
/// helpers for the { success, ... } envelope the frontend expects.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Current user id, or null when unauthenticated (optional-auth routes).</summary>
    protected string? CurrentUserId => User.FindFirst("id")?.Value;

    protected string? CurrentUserRole => User.FindFirst("role")?.Value;

    protected IActionResult Fail(string error, int status = 400)
        => StatusCode(status, new { success = false, error });
}

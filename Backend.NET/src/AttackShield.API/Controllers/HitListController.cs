using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

namespace AttackShield.Api.Controllers;

[Authorize]
[Route("api/hit-list")]
public sealed class HitListController : ApiControllerBase
{
    private const int MaxImageCharacters = 3_000_000;
    private readonly IUserRepository _users;

    public HitListController(IUserRepository users) => _users = users;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var user = await GetUser(ct);
        return user is null ? Fail("User not found", 404) : Ok(new { success = true, data = user.HitList });
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] AddHitListRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.ImageUrl))
            return Fail("Name and image are required");
        if (!request.ImageUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
            || request.ImageUrl.Length > MaxImageCharacters)
            return Fail("Image must be a data URL smaller than 2 MB");

        var user = await GetUser(ct);
        if (user is null) return Fail("User not found", 404);

        var entry = new HitListEntry
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = request.Name.Trim(),
            ImageUrl = request.ImageUrl,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
        };
        user.HitList.Add(entry);
        user.UpdatedAt = DateTime.UtcNow;
        await _users.UpdateAsync(user.Id!, user, ct);
        return Ok(new { success = true, data = entry });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var user = await GetUser(ct);
        if (user is null) return Fail("User not found", 404);
        var removed = user.HitList.RemoveAll(entry => entry.Id == id) > 0;
        if (!removed) return Fail("Hit-list entry not found", 404);
        user.UpdatedAt = DateTime.UtcNow;
        await _users.UpdateAsync(user.Id!, user, ct);
        return Ok(new { success = true });
    }

    private Task<User?> GetUser(CancellationToken ct)
        => CurrentUserId is null ? Task.FromResult<User?>(null) : _users.GetByIdAsync(CurrentUserId, ct);
}

public sealed record AddHitListRequest(string? Name, string? ImageUrl, string? Notes);
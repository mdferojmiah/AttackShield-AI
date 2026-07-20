using System.Security.Cryptography;
using AttackShield.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttackShield.Api.Controllers;

/// <summary>
/// Profile, password and session endpoints for <see cref="AuthController"/>.
/// </summary>
public sealed partial class AuthController
{
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var id = CurrentUserId;
        if (id is null) return Fail("Unauthorized", 401);

        // Could be a user or an authority — try both.
        var user = await _users.GetByIdAsync(id, ct);
        if (user is not null)
        {
            user.Password = null;
            return Ok(new { success = true, data = user });
        }

        var authority = await _authorities.GetByIdAsync(id, ct);
        if (authority is not null)
        {
            authority.Password = null;
            return Ok(new { success = true, data = authority });
        }

        return Fail("User not found", 404);
    }

    [HttpPut("profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest req, CancellationToken ct)
    {
        var id = CurrentUserId;
        if (id is null) return Fail("Unauthorized", 401);

        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return Fail("User not found", 404);

        if (req.Name is not null) user.Name = req.Name;
        if (req.Phone is not null) user.Phone = req.Phone;
        if (req.CctvName is not null) user.CctvName = req.CctvName;
        if (req.RtspUrl is not null) user.RtspUrl = req.RtspUrl;
        if (req.Location is not null) user.Location = req.Location;
        user.UpdatedAt = DateTime.UtcNow;

        await _users.UpdateAsync(id, user, ct);
        user.Password = null;

        return Ok(new { success = true, message = "Profile updated successfully", data = user });
    }

    [HttpPut("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        var id = CurrentUserId;
        if (id is null) return Fail("Unauthorized", 401);
        if (string.IsNullOrWhiteSpace(req.CurrentPassword) || string.IsNullOrWhiteSpace(req.NewPassword))
            return Fail("Current and new password are required");

        // The account may be a user or an authority.
        var user = await _users.GetByIdAsync(id, ct);
        if (user is not null)
        {
            if (string.IsNullOrEmpty(user.Password) || !_hasher.Verify(req.CurrentPassword, user.Password))
                return Fail("Current password is incorrect");
            user.Password = _hasher.Hash(req.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;
            await _users.UpdateAsync(id, user, ct);
            return Ok(new { success = true, message = "Password changed successfully" });
        }

        var authority = await _authorities.GetByIdAsync(id, ct);
        if (authority is not null)
        {
            if (string.IsNullOrEmpty(authority.Password) || !_hasher.Verify(req.CurrentPassword, authority.Password))
                return Fail("Current password is incorrect");
            authority.Password = _hasher.Hash(req.NewPassword);
            authority.UpdatedAt = DateTime.UtcNow;
            await _authorities.UpdateAsync(id, authority, ct);
            return Ok(new { success = true, message = "Password changed successfully" });
        }

        return Fail("User not found", 404);
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
            return Fail("Email is required");

        var emailLower = req.Email.ToLowerInvariant();
        var user = await _users.GetByEmailAsync(emailLower, ct);
        var userType = "user";
        var exists = user is not null;

        if (!exists)
        {
            var authority = await _authorities.GetByEmailAsync(emailLower, ct);
            exists = authority is not null;
            userType = "authority";
        }

        // Always return success to prevent email enumeration.
        if (!exists)
            return Ok(new { success = true, message = "If an account exists with this email, a reset link has been sent." });

        var resetToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        ResetTokens[resetToken] = new ResetTokenData(emailLower, userType, DateTime.UtcNow.AddMinutes(30));

        var isProd = string.Equals(_config["ASPNETCORE_ENVIRONMENT"], "Production", StringComparison.OrdinalIgnoreCase);
        return Ok(new
        {
            success = true,
            message = "Password reset link has been sent to your email",
            resetToken = isProd ? null : resetToken,
        });
    }

    [HttpPost("reset-password/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(string token, [FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Password))
            return Fail("Password is required");

        if (!ResetTokens.TryGetValue(token, out var data))
            return Fail("Invalid or expired reset token");

        if (DateTime.UtcNow > data.Expiry)
        {
            ResetTokens.TryRemove(token, out _);
            return Fail("Reset token has expired");
        }

        if (data.UserType == "authority")
        {
            var authority = await _authorities.GetByEmailAsync(data.Email, ct);
            if (authority is null) return Fail("User not found");
            authority.Password = _hasher.Hash(req.Password);
            authority.UpdatedAt = DateTime.UtcNow;
            await _authorities.UpdateAsync(authority.Id!, authority, ct);
        }
        else
        {
            var user = await _users.GetByEmailAsync(data.Email, ct);
            if (user is null) return Fail("User not found");
            user.Password = _hasher.Hash(req.Password);
            user.UpdatedAt = DateTime.UtcNow;
            await _users.UpdateAsync(user.Id!, user, ct);
        }

        ResetTokens.TryRemove(token, out _);
        return Ok(new { success = true, message = "Password reset successfully" });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        // Best-effort: tell the AI service to stop; never block logout on it.
        await _ai.StopDetectionAsync(ct);
        return Ok(new { success = true, message = "Logged out successfully and AIService stopped." });
    }
}

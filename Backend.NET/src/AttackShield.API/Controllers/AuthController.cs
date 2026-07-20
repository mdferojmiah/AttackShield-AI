using System.Collections.Concurrent;
using AttackShield.Core.DTOs;
using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttackShield.Api.Controllers;

/// <summary>
/// Authentication and profile endpoints. Ported from the Node authController.
/// Users and authorities share the login/reset flow; role is inferred by which
/// collection the email is found in. Split across partial files: this one holds
/// signup + login; the rest (profile, password, OAuth) live in the .Account file.
/// </summary>
[Route("api/auth")]
public sealed partial class AuthController : ApiControllerBase
{
    private readonly IUserRepository _users;
    private readonly IAuthorityRepository _authorities;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;
    private readonly IRtspUrlBuilder _rtsp;
    private readonly IAiServiceClient _ai;
    private readonly IConfiguration _config;

    // In-memory password-reset tokens (the original used a Map; Redis in prod).
    private static readonly ConcurrentDictionary<string, ResetTokenData> ResetTokens = new();

    public AuthController(
        IUserRepository users,
        IAuthorityRepository authorities,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        IRtspUrlBuilder rtsp,
        IAiServiceClient ai,
        IConfiguration config)
    {
        _users = users;
        _authorities = authorities;
        _hasher = hasher;
        _jwt = jwt;
        _rtsp = rtsp;
        _ai = ai;
        _config = config;
    }

    private string ApiBase => _config["Backend:BaseUrl"] ?? "http://localhost:5000";
    private string FrontendUrl => _config["Frontend:BaseUrl"] ?? "http://localhost:3000";

    [HttpPost("signup/user")]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterUser([FromBody] RegisterUserRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password) || string.IsNullOrWhiteSpace(req.Name))
            return Fail("Name, email and password are required");

        var existing = await _users.GetByEmailAsync(req.Email, ct);
        if (existing is not null)
            return Fail("User with this email already exists");

        var finalRtsp = req.RtspUrl;
        if (string.IsNullOrWhiteSpace(finalRtsp))
            finalRtsp = _rtsp.Build(req.CameraIp, req.CameraUsername, req.CameraPassword, req.CameraPort, req.CameraBrand, req.CameraPath);

        if (string.IsNullOrWhiteSpace(finalRtsp))
            return Fail("Unable to generate RTSP URL from provided camera details");

        var user = new User
        {
            Name = req.Name,
            Email = req.Email.ToLowerInvariant(),
            Phone = req.Phone,
            Password = _hasher.Hash(req.Password),
            CctvName = req.CctvName,
            RtspUrl = finalRtsp,
            Location = req.Location,
        };
        await _users.InsertAsync(user, ct);

        return StatusCode(201, new
        {
            success = true,
            message = "User registered successfully",
            data = new { id = user.Id, name = user.Name, email = user.Email },
        });
    }

    [HttpPost("signup/authority")]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterAuthority([FromBody] RegisterAuthorityRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password)
            || string.IsNullOrWhiteSpace(req.OfficerId) || string.IsNullOrWhiteSpace(req.Name)
            || string.IsNullOrWhiteSpace(req.StationName))
            return Fail("All fields are required");

        var existing = await _authorities.GetByEmailOrOfficerIdAsync(req.Email, req.OfficerId, ct);
        if (existing is not null)
            return Fail("Authority with this email or officer ID already exists");

        var authority = new Authority
        {
            Name = req.Name,
            Email = req.Email.ToLowerInvariant(),
            OfficerId = req.OfficerId.ToUpperInvariant(),
            StationName = req.StationName,
            Password = _hasher.Hash(req.Password),
        };
        await _authorities.InsertAsync(authority, ct);

        return StatusCode(201, new
        {
            success = true,
            message = "Authority registered successfully",
            data = new { id = authority.Id, name = authority.Name, email = authority.Email, officerId = authority.OfficerId },
        });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return Fail("Email and password are required");

        var emailLower = req.Email.ToLowerInvariant();
        var user = await _users.GetByEmailAsync(emailLower, ct);

        if (user is null)
        {
            // Fall back to the authorities collection.
            var authority = await _authorities.GetByEmailAsync(emailLower, ct);
            if (authority is null)
                return Fail("Invalid email or password", 401);
            if (!authority.IsActive)
                return Fail("Account is deactivated. Please contact support.", 401);
            if (string.IsNullOrEmpty(authority.Password) || !_hasher.Verify(req.Password, authority.Password))
                return Fail("Invalid email or password", 401);

            authority.LastLogin = DateTime.UtcNow;
            await _authorities.UpdateAsync(authority.Id!, authority, ct);
            var authToken = _jwt.GenerateToken(authority.Id!, authority.Role);

            return Ok(new
            {
                success = true,
                token = authToken,
                user = new
                {
                    id = authority.Id,
                    _id = authority.Id,
                    name = authority.Name,
                    email = authority.Email,
                    role = authority.Role,
                    officerId = authority.OfficerId,
                    stationName = authority.StationName,
                    department = authority.Department,
                    isVerified = authority.IsVerified,
                },
                role = authority.Role,
            });
        }

        if (!user.IsActive)
            return Fail("Account is deactivated. Please contact support.", 401);
        if (string.IsNullOrEmpty(user.Password) || !_hasher.Verify(req.Password, user.Password))
            return Fail("Invalid email or password", 401);

        user.LastLogin = DateTime.UtcNow;
        await _users.UpdateAsync(user.Id!, user, ct);
        var token = _jwt.GenerateToken(user.Id!, "user");

        var camera = new
        {
            camera_name = user.CctvName,
            stream_url = $"{ApiBase}/streams/stream.m3u8",
            location = user.Location,
            rtsp_url = user.RtspUrl,
        };

        return Ok(new
        {
            success = true,
            token,
            user = new
            {
                id = user.Id,
                _id = user.Id,
                name = user.Name,
                email = user.Email,
                role = "user",
                camera,
                phone = user.Phone,
                cctvName = user.CctvName,
                location = user.Location,
            },
            role = "user",
        });
    }

    /// <summary>Reset-token record kept in the in-memory store.</summary>
    private sealed record ResetTokenData(string Email, string UserType, DateTime Expiry);
}

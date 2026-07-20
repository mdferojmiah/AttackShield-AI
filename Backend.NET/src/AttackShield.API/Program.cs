using System.Text;
using System.Text.Json;
using AttackShield.Api.Hubs;
using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using AttackShield.Infrastructure;
using AttackShield.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog (read entirely from configuration) ──
builder.Host.UseSerilog((context, services, config) =>
    config.ReadFrom.Configuration(context.Configuration)
          .ReadFrom.Services(services)
          .Enrich.FromLogContext());

// ── Infrastructure: Mongo, repositories, AI client, streaming, auth helpers ──
builder.Services.AddInfrastructure(builder.Configuration);

// ── Controllers + JSON options ──
builder.Services.AddControllers().AddJsonOptions(opts =>
{
    // camelCase is the default; keep enums as strings and skip nulls to match the
    // Node responses (which omit undefined fields).
    opts.JsonSerializerOptions.DefaultIgnoreCondition =
        System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── SignalR + the detection broadcaster used by DetectionsController ──
builder.Services.AddSignalR();
builder.Services.AddScoped<IDetectionBroadcaster, SignalRDetectionBroadcaster>();

// ── Authentication: JWT bearer (default) + Google OAuth ──
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwtSecret = jwtSection["Secret"] ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
var jwtIssuer = jwtSection["Issuer"] ?? "AttackShield";
var jwtAudience = jwtSection["Audience"] ?? "AttackShield";

var googleClientId = builder.Configuration["Google:ClientId"];
var googleClientSecret = builder.Configuration["Google:ClientSecret"];
var googleConfigured = !string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret);

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
});

authBuilder.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        // Tokens carry the role in a "role" claim (see JwtTokenService); tell the
        // handler to use it so [Authorize(Roles = ...)] works.
        RoleClaimType = "role",
        NameClaimType = "id",
    };

    // Let SignalR clients pass the JWT via the access_token query string (the
    // WebSocket handshake can't set an Authorization header).
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/socket"))
                context.Token = accessToken;
            return Task.CompletedTask;
        },
    };
});

// Google OAuth needs a cookie scheme to hold the external login state during the
// challenge/callback round-trip. Only wired up when credentials are configured.
if (googleConfigured)
{
    authBuilder.AddCookie();
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId!;
        options.ClientSecret = googleClientSecret!;
        options.CallbackPath = "/signin-google"; // internal; our /api/auth/google/callback runs after.
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.Scope.Add("profile");
        options.Scope.Add("email");
    });
}
else
{
    Log.Warning("[Auth] Google OAuth credentials not set. Google login will be unavailable.");
}

builder.Services.AddAuthorization();

// ── CORS from Cors:AllowedOrigins ──
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "*" };
const string CorsPolicy = "AppCors";
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        if (allowedOrigins.Contains("*"))
        {
            // SignalR with credentials can't use AllowAnyOrigin; the frontend uses
            // token-in-query (not cookies) so allowing any origin without credentials
            // is fine and matches the original cors({ origin: '*' }).
            policy.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
    });
});

var app = builder.Build();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(CorsPolicy);

// ── Static HLS files at /streams (parity with the Node express.static mount) ──
// FFmpeg writes playlists/segments under the streams root; the frontend polls them
// at high frequency, so they are served without auth and with no-cache + CORS.
var streamOpts = app.Services.GetRequiredService<
    Microsoft.Extensions.Options.IOptions<StreamOptions>>().Value;
var streamsRoot = Path.IsPathRooted(streamOpts.StreamsRoot)
    ? streamOpts.StreamsRoot
    : Path.Combine(app.Environment.ContentRootPath, streamOpts.StreamsRoot);
Directory.CreateDirectory(streamsRoot);

var hlsContentTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
hlsContentTypes.Mappings[".m3u8"] = "application/vnd.apple.mpegurl";
hlsContentTypes.Mappings[".m4s"] = "video/iso.segment";
hlsContentTypes.Mappings[".ts"] = "video/mp2t";

app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(streamsRoot),
    RequestPath = "/streams",
    ContentTypeProvider = hlsContentTypes,
    ServeUnknownFileTypes = true,
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
    },
});

app.UseAuthentication();
app.UseAuthorization();

// Health check (parity with the Node GET /api/health).
app.MapGet("/api/health", () => Results.Ok(new
{
    success = true,
    status = "healthy",
    timestamp = DateTime.UtcNow.ToString("o"),
}));

// ── Google OAuth endpoints (ported from authController.googleCallback) ──
// GET /api/auth/google → challenge Google; GET /api/auth/google/callback → mint our
// JWT and redirect back to the frontend with token + user, matching the Node flow.
var frontendUrl = builder.Configuration["Frontend:BaseUrl"] ?? "http://localhost:3000";

if (googleConfigured)
{
    app.MapGet("/api/auth/google", () => Results.Challenge(
        new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = "/api/auth/google/callback" },
        new[] { GoogleDefaults.AuthenticationScheme }));

    app.MapGet("/api/auth/google/callback", async (HttpContext http, IUserRepository users, IJwtTokenService jwt) =>
    {
        var result = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!result.Succeeded)
            return Results.Redirect($"{frontendUrl}/login?error=google_auth_failed");

        var principal = result.Principal!;
        var googleId = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var email = principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        var name = principal.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        var avatar = principal.FindFirst("urn:google:picture")?.Value;

        if (string.IsNullOrEmpty(googleId))
            return Results.Redirect($"{frontendUrl}/login?error=google_auth_failed");

        // Find by Google id, else link to an existing email, else create a new user.
        var user = await users.GetByGoogleIdAsync(googleId);
        if (user is null && !string.IsNullOrEmpty(email))
        {
            user = await users.GetByEmailAsync(email.ToLowerInvariant());
            if (user is not null)
            {
                user.GoogleId = googleId;
                user.Avatar ??= avatar;
                await users.UpdateAsync(user.Id!, user);
            }
        }

        if (user is null)
        {
            user = new User
            {
                GoogleId = googleId,
                Name = string.IsNullOrWhiteSpace(name) ? (email ?? "User") : name,
                Email = email?.ToLowerInvariant() ?? string.Empty,
                Avatar = avatar,
                Role = "user",
                IsActive = true,
            };
            await users.InsertAsync(user);
        }

        user.LastLogin = DateTime.UtcNow;
        await users.UpdateAsync(user.Id!, user);

        // Clear the transient external-login cookie now that we've issued our JWT.
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        var token = jwt.GenerateToken(user.Id!, user.Role);
        var userData = JsonSerializer.Serialize(new
        {
            _id = user.Id,
            name = user.Name,
            email = user.Email,
            role = user.Role,
            avatar = user.Avatar,
            cctvName = user.CctvName,
            rtspUrl = user.RtspUrl,
            location = user.Location,
        });

        var query = QueryString.Create(new[]
        {
            new KeyValuePair<string, string?>("token", token),
            new KeyValuePair<string, string?>("user", userData),
        });
        return Results.Redirect($"{frontendUrl}/login{query}");
    });
}

app.MapControllers();

// SignalR hub. The original Socket.IO server accepted connections at the server
// root; the frontend still uses socket.io-client, which speaks a different wire
// protocol than SignalR — the client must be swapped to @microsoft/signalr and
// point at this path for real-time events to flow. Mapped at /socket per the task.
app.MapHub<DetectionHub>("/socket");

// Stop every FFmpeg process cleanly on shutdown (parity with cleanupAllStreams).
app.Lifetime.ApplicationStopping.Register(() =>
{
    using var scope = app.Services.CreateScope();
    var streams = scope.ServiceProvider.GetRequiredService<IStreamManager>();
    streams.StopAllAsync().GetAwaiter().GetResult();
});

app.Run();

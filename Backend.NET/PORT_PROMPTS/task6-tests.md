# Task 6 — Unit tests (repositories + services)

## Context
I am porting a Node/Express backend to .NET 8. Solution at `Backend.NET/` with `AttackShield.Core`, `AttackShield.Infrastructure`, `AttackShield.Api`, and a test project (`AttackShield.Tests` or similar — check the `.sln`).

**Prerequisite:** Task 5 (API controllers + `Program.cs`) should be done, and the solution should build. If the API layer is still incomplete, you can still test Core/Infrastructure pieces that compile. Read the existing code before writing tests — match its structure and naming.

## What to test
Write focused unit tests. Use the test framework already referenced in the test project's `.csproj` (likely xUnit). Add a mocking library (Moq or NSubstitute) and an assertion library (FluentAssertions) if not present — pin exact versions.

1. **Services (pure logic, no external deps — highest value):**
   - `RtspUrlBuilder.Build` — brand-specific paths/ports: Hikvision `/Streaming/Channels/101`, Dahua `/cam/realmonitor?channel=1&subtype=1`, Meari `/live` + port `8554` default, generic fallback; credential vs no-credential URL forms; URL-encoding of username/password; returns null when `ip` missing.
   - `BCryptPasswordHasher` — `Hash` then `Verify` round-trips true; wrong password false; malformed/empty hash returns false (no throw).
   - `JwtTokenService.GenerateToken` — token carries `id`, `sub`, `role` claims and honours `Jwt:ExpireDays`. Decode with `JwtSecurityTokenHandler` and assert claims/expiry.
   - `AiServiceClient` — use a fake `HttpMessageHandler` to assert: success returns `AiCallResult.Ok` with raw JSON; non-2xx and transport exceptions return `AiCallResult.Fail` (never throw); `StartDetectionAsync` posts snake_case body (`rtsp_url`/`location`/`user_id`).

2. **Repositories (need Mongo):**
   - Prefer `Mongo2Go` or `EphemeralMongo` (in-memory/ephemeral mongod) if it installs cleanly; pin exact version. If neither installs in this environment, skip live-Mongo tests and instead write tests against the query-building logic that can be exercised without a server, and clearly note in the test file comments why the integration tests are absent.
   - When Mongo is available, cover: `UserRepository.GetByEmailAsync` (case-insensitive), `TouchLastLoginAsync`; `DetectionRepository.FindRecentAsync` window + `detectionType` filter, `CountByTypeSinceAsync`; `AlertRepository.GetNewAsync`/`GetActiveForAuthorityAsync`/`GetHistoryForAuthorityAsync` status filters and sort order.

3. **Detection pipeline logic** (if API builds): the threshold gate, per-type de-dup window, and face fast-path in `DetectionsController` are the riskiest behaviour — test them with mocked repositories + a mocked `IDetectionBroadcaster`, asserting below-threshold returns early, duplicates are ignored, faces skip the dedup gate, and a weapon produces a detection + notification + alert + the right broadcasts.

## Verify
Run `dotnet test` and make everything green. Report pass/fail counts and anything skipped (e.g. Mongo unavailable) with the reason. Do not weaken assertions just to pass. Do not commit unless I ask.

// Task 040 (spaarkeai-compose-r5, FR-09 / gap G10) — the THROUGH-THE-WIRE proof for the manual
// "Refresh Profile" leg: POST /api/compose/documents/{documentRecordId}/refresh-profile routes through
// the REAL endpoint → IComposeService.RefreshProfileAsync → the shared fire-and-forget
// DispatchBackgroundProfile pipeline, returning 202 Accepted. A missing tenant is a clean 400.
//
// The RELOAD/onload re-trigger leg is storm-guarded (re-fires only when the live eTag differs from the
// per-doc profiled-eTag stamp) and best-effort — its dispatch is a detached Task.Run by design
// (fire-and-forget), so it is not deterministically observable in-process; the storm-guard LOGIC + this
// endpoint (which shares the SAME RefreshProfileAsync → DispatchBackgroundProfile path) are the
// verifiable surface. See notes/task-040-deviations.md.
//
// Reuses ComposeFidelitySeamFixture (host + SPE/Dataverse/indexing module-boundary mocks + fake auth).
// ADR-038 seam DoD: through-the-wire WebApplicationFactory slice. NO Mock<HttpMessageHandler>, NO
// DI-registration test, NO ctor-null test.

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeRefreshProfileSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposeRefreshProfileSeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task RefreshProfile_ValidRequest_Returns202_ThroughTheWire()
    {
        _fixture.ResetBoundaries();
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var recordId = Guid.NewGuid();

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/compose/documents/{recordId}/refresh-profile",
            new { tenantId = tenant, documentSpeId = "spe-item-040-refresh", eTag = "\"v1-etag\"" });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            $"a manual Refresh Profile re-dispatches the profile fire-and-forget and accepts (202) — body: {body}");
    }

    [Fact]
    public async Task RefreshProfile_MissingTenant_Returns400_ThroughTheWire()
    {
        _fixture.ResetBoundaries();
        var recordId = Guid.NewGuid();

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/compose/documents/{recordId}/refresh-profile",
            new { documentSpeId = "spe-item-040-refresh" }); // no tenantId

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the refresh-profile endpoint requires a tenantId in the body (ADR-015 Tier 3)");
    }
}

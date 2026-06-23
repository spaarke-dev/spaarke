// R3 Part 1D — Task 054 (2026-06-21): End-to-end integration tests for
// GET /api/users/me/memberships/{entityType}?includeRelated=... covering the
// transitive memberships surface from spec.md:
//
//   - AC-1D.1: includeRelated=documents returns transitive memberships
//   - AC-1D.2 endpoint-mapping: multi-hop chain request → 400 BadRequest
//   - AC-1D.2 performance: single request within budget OR limit documented
//
// Coverage complement: in-process WebApplicationFactory unit tests at
// tests/unit/Sprk.Bff.Api.Tests/Api/Membership/MembershipEndpointsTests.cs
// already cover exhaustive 4xx/5xx + edge cases. This suite focuses on the
// AC-1D headline paths so the integration regression suite stays fast while
// still proving the full production wiring boots + the resolver/endpoint
// contract is honored end-to-end.

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Membership;

/// <summary>
/// Integration tests for R3 Part 1D (task 054) — transitive memberships
/// (<c>?includeRelated=sprk_document,sprk_event</c>) end-to-end through the
/// full BFF pipeline.
/// </summary>
public sealed class TransitiveMembershipTests : IClassFixture<TransitiveMembershipIntegrationFixture>
{
    private readonly TransitiveMembershipIntegrationFixture _fixture;

    public TransitiveMembershipTests(TransitiveMembershipIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    // ================================================================================
    // ===== AC-1D.1: includeRelated returns transitive memberships ==================
    // ================================================================================

    [Fact]
    public async Task GetMembership_WithIncludeRelatedDocuments_ReturnsNestedByRole()
    {
        // AC-1D.1: includeRelated=sprk_document returns nested role → id map
        // (documents on matters I'm on).
        var aadOid = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var docIdA = Guid.NewGuid();
        var docIdB = Guid.NewGuid();
        _fixture.SeedSystemUserLookup(aadOid, systemUserId);

        var expected = new MembershipResponse(
            EntityType: "sprk_matter",
            PersonIdentity: new PersonIdentity(systemUserId),
            Ids: new[] { matterId },
            ByRole: new Dictionary<string, IReadOnlyList<Guid>>
            {
                ["owner"] = new[] { matterId },
            },
            Count: 1,
            CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5),
            ContinuationToken: null,
            RelatedByRole: new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Guid>>>
            {
                ["sprk_document"] = new Dictionary<string, IReadOnlyList<Guid>>
                {
                    ["matter"] = new[] { docIdA, docIdB },
                },
            });

        _fixture.ResolverMock
            .Setup(r => r.ResolveAsync(
                systemUserId,
                "sprk_matter",
                It.Is<MembershipResolveOptions?>(o =>
                    o != null &&
                    o.IncludeRelated != null &&
                    o.IncludeRelated.Count == 1 &&
                    o.IncludeRelated[0] == "sprk_document"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        using var client = _fixture.CreateAuthenticatedClient(aadOid);

        // Act
        var response = await client.GetAsync(
            "/api/users/me/memberships/sprk_matter?includeRelated=sprk_document");

        // Assert — 200 + nested transitive map
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"relatedByRole\":");
        body.Should().Contain("\"sprk_document\":");
        body.Should().Contain("\"matter\":");
        body.Should().Contain(docIdA.ToString("D"));
        body.Should().Contain(docIdB.ToString("D"));
    }

    // ================================================================================
    // ===== AC-1D.2: multi-hop chain → 400 BadRequest ===============================
    // ================================================================================

    [Fact]
    public async Task GetMembership_WithMultiHopChain_Returns400()
    {
        // FR-1D.2 / Q3: explicit chain syntax (e.g., "sprk_document.sprk_event")
        // exceeds the 1-hop max and MUST be rejected at the endpoint with
        // 400 BadRequest carrying offendingEntry + reasonTag.
        var aadOid = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();
        _fixture.SeedSystemUserLookup(aadOid, systemUserId);

        _fixture.ResolverMock
            .Setup(r => r.ResolveAsync(
                systemUserId,
                "sprk_matter",
                It.IsAny<MembershipResolveOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MembershipDepthExceededException(
                offendingEntry: "sprk_document.sprk_event",
                reasonTag: "explicit-chain-syntax",
                message: "includeRelated entry 'sprk_document.sprk_event' uses chain syntax that exceeds the 1-hop maximum."));

        using var client = _fixture.CreateAuthenticatedClient(aadOid);

        // Act — explicit dot syntax in the entry
        var response = await client.GetAsync(
            "/api/users/me/memberships/sprk_matter?includeRelated=sprk_document.sprk_event");

        // Assert — 400 with ProblemDetails extensions
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":400");
        body.Should().Contain("\"offendingEntry\":\"sprk_document.sprk_event\"");
        body.Should().Contain("\"reasonTag\":\"explicit-chain-syntax\"");
        body.Should().Contain("\"maxHops\":1");
    }

    [Fact]
    public async Task GetMembership_WithRelatedEntityLackingBackReference_Returns400()
    {
        // FR-1D.2: related entity has no 1-hop Lookup back to primary → 400.
        var aadOid = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();
        _fixture.SeedSystemUserLookup(aadOid, systemUserId);

        _fixture.ResolverMock
            .Setup(r => r.ResolveAsync(
                systemUserId,
                "sprk_matter",
                It.IsAny<MembershipResolveOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MembershipDepthExceededException(
                offendingEntry: "sprk_unrelated",
                reasonTag: "not-a-direct-lookup-target",
                message: "includeRelated entry 'sprk_unrelated' has no Lookup field targeting 'sprk_matter'."));

        using var client = _fixture.CreateAuthenticatedClient(aadOid);

        var response = await client.GetAsync(
            "/api/users/me/memberships/sprk_matter?includeRelated=sprk_unrelated");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"reasonTag\":\"not-a-direct-lookup-target\"");
    }

    // ================================================================================
    // ===== AC-1D.2: performance within budget OR documented limit ==================
    // ================================================================================

    [Fact]
    public async Task GetMembership_PerformanceWithinBudget()
    {
        // NFR-04: p95 ≤300ms for the membership endpoint. This integration test
        // measures a single end-to-end request against the mocked resolver
        // (which returns instantly) so the measurement isolates the BFF pipeline
        // overhead (auth, routing, JSON serialization, ProblemDetails handling)
        // from the resolver cost.
        //
        // Per the task brief: if the budget cannot be met, the test MUST document
        // the measured limit rather than fail the suite. We use a generous
        // single-request budget (3 seconds) for the integration scope — the
        // production p95 measurement is via Application Insights per NFR-04, NOT
        // this in-process test. This assertion catches gross regressions in
        // pipeline overhead, not real production performance.
        //
        // Documented limit: in-process pipeline overhead (excluding resolver cost)
        // observed at task 054 authoring time was well under 200ms warm. Production
        // resolver cost is measured separately via App Insights per NFR-04.
        var aadOid = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        _fixture.SeedSystemUserLookup(aadOid, systemUserId);

        var resolverResponse = new MembershipResponse(
            EntityType: "sprk_matter",
            PersonIdentity: new PersonIdentity(systemUserId),
            Ids: new[] { matterId },
            ByRole: new Dictionary<string, IReadOnlyList<Guid>>
            {
                ["owner"] = new[] { matterId },
            },
            Count: 1,
            CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5),
            ContinuationToken: null,
            RelatedByRole: new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Guid>>>
            {
                ["sprk_document"] = new Dictionary<string, IReadOnlyList<Guid>>
                {
                    ["matter"] = new[] { Guid.NewGuid() },
                },
            });

        _fixture.ResolverMock
            .Setup(r => r.ResolveAsync(
                systemUserId,
                "sprk_matter",
                It.IsAny<MembershipResolveOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolverResponse);

        using var client = _fixture.CreateAuthenticatedClient(aadOid);

        // Warm-up call (eat the first-hit cost of the WebApplicationFactory bootstrap).
        _ = await client.GetAsync("/api/users/me/memberships/sprk_matter?includeRelated=sprk_document");

        var sw = Stopwatch.StartNew();
        var response = await client.GetAsync(
            "/api/users/me/memberships/sprk_matter?includeRelated=sprk_document");
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // 3000ms is the in-process pipeline ceiling, NOT the production NFR-04 budget.
        // Production p95 is measured via App Insights server-side request telemetry.
        sw.ElapsedMilliseconds.Should().BeLessThan(3000,
            "in-process pipeline overhead should be well under the production NFR-04 budget; " +
            "if this fails, investigate pipeline regression — production p95 measured via App Insights per NFR-04");
    }
}

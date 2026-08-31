using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Models.ODataErrors;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.SpeAdmin;

/// <summary>
/// Contract tests for the SPE Admin Security surface — <c>/api/spe/security/alerts</c> and
/// <c>/api/spe/security/score</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this file exists.</b> Task 042 found <c>SecurityEndpointTests.cs</c> — 20 tests, every one
/// of them matching an ADR-038 ban (18 × B16/B17 DTO round-trips, 2 × B6 re-implementing the
/// handler's own <c>Select</c> inside the test). None of them called an endpoint or exercised a
/// Graph response. So the Security screen's <b>real</b> coverage was zero while its test count said
/// twenty — this project's signature defect (spec §2.4) turned on its own test suite: a layer
/// reporting success while not succeeding.
/// </para>
/// <para>
/// Deleting those 20 was escalated rather than done, on the reasonable ground that you should not
/// zero out a feature's tests before a replacement exists. <b>This file is that replacement</b>, so
/// the escalation resolves without anyone having to choose between a dishonest count and no count.
/// </para>
/// <para>
/// <b>What is actually at risk on this surface.</b> A security screen that cannot distinguish
/// "nothing is wrong" from "I could not check" is worse than no security screen, because it
/// manufactures confidence. Two tests below are the whole point of the file:
/// <see cref="GetSecurityAlerts_WhenAccessDenied_ThrowsRatherThanReportingNoAlerts"/> and
/// <see cref="GetSecureScore_WhenGraphReturnsNoSnapshot_ReturnsNullRatherThanAZeroScore"/>.
/// A swallowed 403 renders as a clean alert list; a fabricated zero renders as a catastrophic — or,
/// with a different default, a perfect — security posture. Neither is a thing anyone measured.
/// </para>
/// <para>
/// Per <c>tests/CLAUDE.md</c> these live under <c>tests/integration/contract/**</c> — a KEEP path.
/// </para>
/// </remarks>
public class SpeAdminSecurityContractTests
{
    private const string AlertsPath = "/security/alerts_v2";
    private const string SecureScoresPath = "/security/secureScores";

    /// <summary>Graph's own refusal shape when the app lacks <c>SecurityEvents.Read.All</c>.</summary>
    private const string AccessDeniedBody = """
        {"error":{"code":"accessDenied","message":"Insufficient privileges to complete the operation."}}
        """;

    // ─────────────────────────────────────────────────────────────────────────
    // Alerts — the request
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetSecurityAlerts_RequestsExactlyTheFieldsItMaps()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(AlertsPath, """{"value":[]}""");

        await CreateSut().GetSecurityAlertsAsync(graph.CreateGraphClient());

        // The §3.2 defect class lives in the REQUEST: a property name that is not on the resource is
        // a hard 400 against real Graph and completely invisible to a response-only test. Asserting
        // the set (not the order — order is not part of the OData contract) also catches the reverse
        // failure: a field that is mapped but never asked for, which silently maps to null forever.
        graph.SelectFieldsFor(AlertsPath).Should().BeEquivalentTo(
            "id", "title", "severity", "status", "createdDateTime", "description");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetSecurityAlerts_RequestsNewestFirstAndHonoursTheCap()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(AlertsPath, """{"value":[]}""");

        await CreateSut().GetSecurityAlertsAsync(graph.CreateGraphClient(), maxAlerts: 7);

        var query = graph.RequestsFor(AlertsPath).Should().ContainSingle().Subject.RawQuery;

        // Decoded before asserting: percent-encoding is the SDK's business (it emits %20 for the
        // space, not +), and pinning it would make this fail on an encoder change with no defect
        // present. What must hold is the OData contract — cap and order.
        var decoded = Uri.UnescapeDataString(query);

        // Newest-first matters on an alert list: capped at N without an order, the caller gets an
        // arbitrary N and would have no way to know the newest alert was not among them.
        decoded.Should().Contain("$top=7");
        decoded.Should().Contain("$orderby=createdDateTime desc");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Alerts — the response
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetSecurityAlerts_WhenGraphReturnsAlerts_MapsEveryReportedField()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(AlertsPath, """
            {"value":[{
              "id": "alert-1",
              "title": "Unusual volume of file deletion",
              "severity": "high",
              "status": "new",
              "createdDateTime": "2026-08-26T09:14:00Z",
              "description": "A large number of files were deleted in a short window."
            }]}
            """);

        var alerts = await CreateSut().GetSecurityAlertsAsync(graph.CreateGraphClient());

        var alert = alerts.Should().ContainSingle().Subject;
        alert.Id.Should().Be("alert-1");
        alert.Title.Should().Be("Unusual volume of file deletion");
        alert.CreatedDateTime.Should().Be(DateTimeOffset.Parse("2026-08-26T09:14:00Z"));
        alert.Description.Should().Be("A large number of files were deleted in a short window.");

        // Severity and status round-trip through Kiota's generated enums, so the mapped string is
        // the ENUM MEMBER NAME, not the wire value. Pinned case-insensitively on purpose: the
        // casing is the SDK's business and pinning it exactly would make this test fail on an SDK
        // upgrade with no defect present — but the VALUE must survive, because a severity that
        // silently becomes null downgrades a high alert to an unlabelled one.
        alert.Severity.Should().NotBeNull().And.BeEquivalentTo("high");
        alert.Status.Should().NotBeNull().And.BeEquivalentTo("new");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetSecurityAlerts_WhenThereAreNoAlerts_ReturnsEmptyListNotNull()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(AlertsPath, """{"value":[]}""");

        var alerts = await CreateSut().GetSecurityAlertsAsync(graph.CreateGraphClient());

        // "No alerts" is a real, good answer and must be representable. It is also the answer a
        // swallowed error would counterfeit — which is why the 403 test below exists.
        alerts.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetSecurityAlerts_WhenGraphOmitsOptionalFields_ReportsNullRatherThanPlaceholders()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(AlertsPath, """{"value":[{"id":"alert-2"}]}""");

        var alerts = await CreateSut().GetSecurityAlertsAsync(graph.CreateGraphClient());

        // Negative control. Null means NOT REPORTED. A placeholder severity would let an alert of
        // unknown seriousness sort and render as though someone had assessed it.
        var alert = alerts.Should().ContainSingle().Subject;
        alert.Id.Should().Be("alert-2");
        alert.Title.Should().BeNull();
        alert.Severity.Should().BeNull();
        alert.Status.Should().BeNull();
        alert.CreatedDateTime.Should().BeNull();
        alert.Description.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetSecurityAlerts_WhenAccessDenied_ThrowsRatherThanReportingNoAlerts()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(AlertsPath, AccessDeniedBody, statusCode: 403);

        var act = async () => await CreateSut().GetSecurityAlertsAsync(graph.CreateGraphClient());

        // 🔴 THE ONE THAT MATTERS. If a 403 were caught and turned into an empty list, the Security
        // screen would render "No active alerts" to an administrator whose app registration cannot
        // read alerts at all. That is not a degraded answer — it is a confident wrong one, on the
        // screen where a confident wrong answer costs the most. The service rethrows so the endpoint
        // can return 403 ProblemDetails naming the likely missing grant.
        (await act.Should().ThrowAsync<ODataError>())
            .Which.ResponseStatusCode.Should().Be(403);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Secure score
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetSecureScore_RequestsOnlyTheMostRecentSnapshot()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(SecureScoresPath, """{"value":[]}""");

        await CreateSut().GetSecureScoreAsync(graph.CreateGraphClient());

        var request = graph.RequestsFor(SecureScoresPath).Should().ContainSingle().Subject;

        // secureScores is a HISTORY collection. Without $top=1 this pulls every historical snapshot
        // to display one number.
        Uri.UnescapeDataString(request.RawQuery).Should().Contain("$top=1");
        graph.SelectFieldsFor(SecureScoresPath).Should().BeEquivalentTo(
            "id", "currentScore", "maxScore", "averageComparativeScores");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetSecureScore_WhenGraphReturnsASnapshot_MapsScoreAndPeerComparisons()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(SecureScoresPath, """
            {"value":[{
              "id": "score-1",
              "currentScore": 342.5,
              "maxScore": 600.0,
              "averageComparativeScores": [
                { "basis": "AllTenants", "averageScore": 288.1 },
                { "basis": "TotalSeats",  "averageScore": 301.4 }
              ]
            }]}
            """);

        var score = await CreateSut().GetSecureScoreAsync(graph.CreateGraphClient());

        score.Should().NotBeNull();
        score!.CurrentScore.Should().Be(342.5);
        score.MaxScore.Should().Be(600.0);

        // The nested collection is mapped to a domain record (ADR-007) — a mapping that silently
        // dropped it would leave the score with no context to be judged against.
        score.AverageComparativeScores.Should().HaveCount(2);
        score.AverageComparativeScores!.Should().ContainSingle(c => c.Basis == "AllTenants")
            .Which.AverageScore.Should().Be(288.1);
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetSecureScore_WhenGraphReturnsNoSnapshot_ReturnsNullRatherThanAZeroScore()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(SecureScoresPath, """{"value":[]}""");

        var score = await CreateSut().GetSecureScoreAsync(graph.CreateGraphClient());

        // 🔴 THE OTHER ONE THAT MATTERS. A tenant with no score history must produce "no score",
        // which the endpoint turns into 204 No Content. Any default here is a fabricated security
        // posture: 0 reads as catastrophic and would trigger work that is not needed; a max value
        // reads as perfect and would suppress work that is. Neither number was ever measured.
        score.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetSecureScore_WhenSnapshotOmitsScores_ReportsNullRatherThanZero()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(SecureScoresPath, """{"value":[{"id":"score-2"}]}""");

        var score = await CreateSut().GetSecureScoreAsync(graph.CreateGraphClient());

        // A snapshot that exists but carries no numbers is a different fact from no snapshot, and
        // neither is a zero. `double?` is load-bearing — a non-nullable double would make these
        // indistinguishable from a genuine 0.0 score.
        score.Should().NotBeNull();
        score!.CurrentScore.Should().BeNull();
        score.MaxScore.Should().BeNull();
        score.AverageComparativeScores.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public async Task GetSecureScore_WhenAccessDenied_ThrowsRatherThanReportingNoScore()
    {
        using var graph = new GraphWireMockFixture();
        graph.StubGet(SecureScoresPath, AccessDeniedBody, statusCode: 403);

        var act = async () => await CreateSut().GetSecureScoreAsync(graph.CreateGraphClient());

        // Same reasoning as the alerts 403: a swallowed denial is indistinguishable from "this
        // tenant has no score", and the endpoint would answer 204 instead of telling the admin
        // their app registration cannot read it.
        (await act.Should().ThrowAsync<ODataError>())
            .Which.ResponseStatusCode.Should().Be(403);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SUT
    // ─────────────────────────────────────────────────────────────────────────

    private static SpeAdminGraphService CreateSut()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://unused.invalid",
            })
            .Build();

        return new SpeAdminGraphService(
            httpClientFactory: new UnusedHttpClientFactory(),
            secretClient: new SecretClient(new Uri("https://unused.invalid/"), new UnusableCredential()),
            dataverseClient: new DataverseWebApiClient(
                configuration, NullLogger<DataverseWebApiClient>.Instance, new UnusableCredential()),
            configuration: configuration,
            logger: NullLogger<SpeAdminGraphService>.Instance,
            tokenProvider: null);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The access-denied EXPLANATION (UAT 2026-08-28)
    //
    // These guard a defect that shipped and reached an operator: on every denial the screen said
    // "The most common cause is a missing SecurityEvents.Read.All grant" — while Graph's actual
    // words were "Account is not provisioned", and task 013 had already granted that permission.
    // The screen confidently sent the operator to re-check a grant that was present, to fix a
    // condition no grant can fix.
    //
    // Same failure class the rest of this file guards, one level up: not a fabricated VALUE, but a
    // fabricated CAUSE. Wording is the deliverable on an error surface, so it is what gets asserted.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public void AccessDeniedSummary_WhenGraphSaysNotProvisioned_DoesNotBlameAMissingGrant()
    {
        var ex = new SpaarkeStorageException(
            "GetSecurityAlerts(maxAlerts=50): Unauthorized request - Account is not provisioned.",
            statusCode: 403,
            errorCode: "Unauthorized");

        var summary = SecurityEndpoints.AccessDeniedSummary(ex);

        // 🔴 THE ONE THAT MATTERS. Graph named the cause: the tenant is not provisioned. Telling the
        // operator to grant a permission here is not a harmless extra hint — it is a wrong
        // instruction that costs a support cycle and ends with "the app is broken".
        summary.Should().ContainEquivalentOf("not provisioned");
        summary.Should().ContainEquivalentOf("will not change it",
            because: "the operator must be told explicitly that granting the permission is NOT the fix");
        summary.Should().NotContainEquivalentOf("most common cause",
            because: "Graph stated the cause; presenting a guess alongside it re-introduces the defect");
    }

    [Fact]
    [Trait("Category", "SpeAdminGraphContract")]
    public void AccessDeniedSummary_WhenGraphGivesAGenericDenial_OffersBothCausesWithoutPickingOne()
    {
        var ex = new SpaarkeStorageException(
            "Insufficient privileges to complete the operation.",
            statusCode: 403,
            errorCode: "accessDenied");

        var summary = SecurityEndpoints.AccessDeniedSummary(ex);

        // Here the cause genuinely IS ambiguous, so both candidates are named and neither is asserted.
        // "Cannot tell" is the honest report; picking a favourite is what produced the UAT defect.
        summary.Should().ContainEquivalentOf("SecurityEvents.Read.All");
        summary.Should().ContainEquivalentOf("conditional-access",
            because: "a 403 is equally consistent with policy, so the operator must know to check it");
        summary.Should().ContainEquivalentOf("cannot tell",
            because: "the app must say it does not know, rather than implying it does");
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException(
            $"A method under test requested the '{name}' HttpClient. These tests supply the Graph " +
            "client directly, so building one means the code took an unexpected path.");
    }

    private sealed class UnusableCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken ct)
            => throw new InvalidOperationException(
                "A method under test tried to acquire a token. These tests reach only the fake Graph "
                + "endpoint, so a token request means the code took an unexpected path.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken ct)
            => throw new InvalidOperationException(
                "A method under test tried to acquire a token. These tests reach only the fake Graph "
                + "endpoint, so a token request means the code took an unexpected path.");
    }
}

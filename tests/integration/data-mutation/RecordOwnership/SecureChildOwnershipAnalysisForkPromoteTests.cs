using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Api.Ai;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// unified-access-control-r2 task 146 r2 (verifier item 14: "Analysis fork and promote are not tested") — the two
/// analysis creates that are not <c>POST /api/ai/analysis/create</c>, through their REAL routes: each asks the ONE owner
/// resolver with the session's DOCUMENT as the parent (the resolver's rules — secure-if-any, the look-through of a
/// user-owned document to what it is filed under — are pinned in RecordOwnershipResolverTests) and creates the analysis
/// owned by the team it answered; a refusal is a 409 with the stable code and nothing is created, archived or bound.
/// </summary>
/// <remarks>The owner resolver is the fixtures' module-boundary double, which records what it was asked.</remarks>
[Trait("status", "new")]
public sealed class SecureChildOwnershipAnalysisForkTests : IClassFixture<AnalysisForkEndpointTestFixture>
{
    private readonly AnalysisForkEndpointTestFixture _fx;

    public SecureChildOwnershipAnalysisForkTests(AnalysisForkEndpointTestFixture fx)
    {
        _fx = fx;
        _fx.Reset();
        _fx.Ownership.TeamId = RecordOwnershipResolverDouble.DefaultTeamId;
        _fx.Ownership.Requests.Clear();
    }

    [Fact]
    public async Task Fork_CreatesTheAnalysisOwnedByTheTeamResolvedFromItsDocument()
    {
        var documentId = Guid.NewGuid();
        var prior = await _fx.Sessions.CreateSessionAsync(
            AnalysisForkEndpointTestFixture.TenantId, TestSessionOwner.Oid, documentId.ToString(), playbookId: null, hostContext: null);

        var response = await _fx.CreateAuthenticatedClient().PostAsJsonAsync("/api/ai/analysis/fork", new
        {
            priorSessionId = prior.SessionId,
            documentId,
            name = "Forked",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        _fx.Ownership.Requests.Should().ContainSingle(r =>
            r.TargetEntityLogicalName == "sprk_document" && r.TargetRecordId == documentId,
            "the analysis is a child of its document — the resolver decides from it");
        _fx.AnalysisServiceMock.Verify(s => s.CreateAnalysisAsync(
            documentId, It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<AnalysisRegardingTarget?>(),
            RecordOwnershipResolverDouble.DefaultTeamId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Fork_WhenTheOwnerIsRefused_Is409WithTheStableCode_AndCreatesArchivesNothing()
    {
        _fx.Ownership.TeamId = null;
        var documentId = Guid.NewGuid();
        var prior = await _fx.Sessions.CreateSessionAsync(
            AnalysisForkEndpointTestFixture.TenantId, TestSessionOwner.Oid, documentId.ToString(), playbookId: null, hostContext: null);

        var response = await _fx.CreateAuthenticatedClient().PostAsJsonAsync("/api/ai/analysis/fork", new
        {
            priorSessionId = prior.SessionId,
            documentId,
            name = "Forked",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (JsonNode.Parse(await response.Content.ReadAsStringAsync())?["reasonCode"]?.GetValue<string>())
            .Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        _fx.AnalysisServiceMock.Verify(s => s.CreateAnalysisAsync(
            It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<AnalysisRegardingTarget?>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _fx.ChatRepo.Archived.Should().BeEmpty();
    }
}

/// <summary>The promote half of <see cref="SecureChildOwnershipAnalysisForkTests"/>.</summary>
[Trait("status", "new")]
public sealed class SecureChildOwnershipAnalysisPromoteTests : IClassFixture<AnalysisPromoteEndpointTestFixture>
{
    private readonly AnalysisPromoteEndpointTestFixture _fx;

    public SecureChildOwnershipAnalysisPromoteTests(AnalysisPromoteEndpointTestFixture fx)
    {
        _fx = fx;
        _fx.Reset();
        _fx.Ownership.TeamId = RecordOwnershipResolverDouble.DefaultTeamId;
        _fx.Ownership.Requests.Clear();
    }

    [Fact]
    public async Task Promote_CreatesTheAnalysisOwnedByTheTeamResolvedFromTheSessionsDocument()
    {
        var documentId = Guid.NewGuid();
        var loose = await _fx.Sessions.CreateSessionAsync(
            AnalysisPromoteEndpointTestFixture.TenantId, TestSessionOwner.Oid, documentId.ToString(), playbookId: null, hostContext: null);

        var response = await _fx.CreateAuthenticatedClient().PostAsJsonAsync("/api/ai/analysis/promote", new
        {
            sessionId = loose.SessionId,
            name = "Promoted",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        _fx.Ownership.Requests.Should().Contain(r =>
            (r.TargetEntityLogicalName == "sprk_document" && r.TargetRecordId == documentId)
            || r.Parents.Any(p => p.EntityLogicalName == "sprk_document" && p.RecordId == documentId));
        _fx.AnalysisServiceMock.Verify(s => s.CreateAnalysisAsync(
            documentId, It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<AnalysisRegardingTarget?>(),
            RecordOwnershipResolverDouble.DefaultTeamId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Promote_WhenTheOwnerIsRefused_Is409WithTheStableCode_AndCreatesBindsNothing()
    {
        _fx.Ownership.TeamId = null;
        var documentId = Guid.NewGuid();
        var loose = await _fx.Sessions.CreateSessionAsync(
            AnalysisPromoteEndpointTestFixture.TenantId, TestSessionOwner.Oid, documentId.ToString(), playbookId: null, hostContext: null);

        var response = await _fx.CreateAuthenticatedClient().PostAsJsonAsync("/api/ai/analysis/promote", new
        {
            sessionId = loose.SessionId,
            name = "Promoted",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (JsonNode.Parse(await response.Content.ReadAsStringAsync())?["reasonCode"]?.GetValue<string>())
            .Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        _fx.AnalysisServiceMock.Verify(s => s.CreateAnalysisAsync(
            It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<AnalysisRegardingTarget?>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _fx.ChatRepo.Bound.Should().BeEmpty();
    }
}

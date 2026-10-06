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
/// unified-access-control-r2 task 146 r2 (verifier item 14: "Analysis fork and promote are not tested"), restated at the
/// sweep integration: <c>POST /api/ai/analysis/fork</c> was DELETED by task 162 (owner round 10 item 1: no caller, not
/// published), so the fork's owner question no longer exists — this pins that the route reaches nothing: no owner is
/// asked, no analysis is created, nothing is archived. Promote (below) keeps 146's owner resolution behind 162's gate.
/// </summary>
[Trait("status", "new")]
public sealed class SecureChildOwnershipAnalysisForkTests : IClassFixture<AnalysisPromoteEndpointTestFixture>
{
    private readonly AnalysisPromoteEndpointTestFixture _fx;

    public SecureChildOwnershipAnalysisForkTests(AnalysisPromoteEndpointTestFixture fx)
    {
        _fx = fx;
        _fx.Reset();
        _fx.Ownership.TeamId = RecordOwnershipResolverDouble.DefaultTeamId;
        _fx.Ownership.Requests.Clear();
    }

    [Fact]
    public async Task Fork_TheDeletedRoute_ReachesNothing_AsksNoOwner_AndCreatesNothing()
    {
        var documentId = Guid.NewGuid();
        var prior = await _fx.Sessions.CreateSessionAsync(
            AnalysisPromoteEndpointTestFixture.TenantId, TestSessionOwner.Oid, documentId.ToString(), playbookId: null, hostContext: null);

        var response = await _fx.CreateAuthenticatedClient().PostAsJsonAsync("/api/ai/analysis/fork", new
        {
            priorSessionId = prior.SessionId,
            documentId,
            name = "Forked",
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        _fx.Ownership.Requests.Should().BeEmpty();
        _fx.AnalysisServiceMock.Verify(s => s.CreateAnalysisAsync(
            It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<AnalysisRegardingTarget?>(),
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _fx.ChatRepo.Archived.Should().BeEmpty();
        _fx.ChatRepo.Bound.Should().BeEmpty();
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
            RecordOwnershipResolverDouble.DefaultTeamId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
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
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _fx.ChatRepo.Bound.Should().BeEmpty();
    }
}

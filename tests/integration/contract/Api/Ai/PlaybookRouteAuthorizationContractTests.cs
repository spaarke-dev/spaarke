using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Agent;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Caching;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// The per-record authorization of the playbook-surface sweep routes of unified-access-control-r2 task 164,
/// exercised through the REAL <c>MapPlaybookEndpoints</c>, <c>MapPlaybookRunEndpoints</c> and
/// <c>MapAgentEndpoints</c>: <c>GET /api/ai/playbooks/by-id/{id}</c> (sweep #55), <c>POST
/// /api/ai/playbooks/{id:guid}/execute</c> (#29), <c>POST /api/agent/run-playbook</c> (#19), <c>GET
/// /api/agent/playbooks/status/{jobId:guid}</c> (#78), and the owner-round-12-item-6 owned-playbook list.
/// </summary>
/// <remarks>
/// <para><b>What is real and what is substituted.</b> The endpoint mappers, <see cref="PlaybookAuthorizationFilter"/>,
/// <see cref="AgentAuthorizationFilter"/>, <see cref="AuthorizationService"/>, <see cref="OperationAccessRule"/>,
/// <see cref="OperationAccessPolicy"/> and <see cref="EndpointResponseCache"/> are the production types. The
/// substitution is at the <see cref="IAccessDataSource"/> boundary (a recording fake answering "what may this caller
/// do to record X of set S"), at <see cref="CallerRecordAccessProbe"/>'s virtual WhoAmI seam, and at the playbook,
/// node, lookup and orchestration service interfaces. No HTTP handler is mocked (ADR-038).</para>
/// </remarks>
public class PlaybookRouteAuthorizationContractTests
{
    /// <summary><c>sprk_analysisplaybook</c>'s entity set — live EntityDefinitions, pinned by task 162.</summary>
    private const string Playbooks = "sprk_analysisplaybooks";

    private const string Documents = "sprk_documents";
    private const string Matters = "sprk_matters";
    private const string CallerOid = "6f0c1a52-0000-4000-8000-000000000164";
    private const string OtherOid = "6f0c1a52-0000-4000-8000-0000000001ff";

    [Fact]
    public void PlaybookEntitySet_IsTheOneTheServiceUses()
    {
        PlaybookService.EntitySetName.Should().Be(Playbooks);
    }

    // =========================================================================================
    // GET /api/ai/playbooks/by-id/{id}  (sweep #55)
    // =========================================================================================

    [Fact]
    public async Task ById_DeniedUnknownAndFaultingPlaybook_AreOneUniform404_ThatNeverEchoesTheId_AndTheCacheIsNeverRead()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var deniedId = Guid.NewGuid();
        var unknownId = Guid.NewGuid();
        var faultingId = Guid.NewGuid();
        host.PrivatePlaybook(deniedId);
        // The playbook exists and the caller holds OTHER rights on it — just not Read.
        host.Access.Grant(Playbooks, deniedId, AccessRights.AppendTo);
        host.Playbooks.Setup(p => p.GetPlaybookAsync(unknownId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlaybookResponse?)null);
        host.Playbooks.Setup(p => p.GetPlaybookAsync(faultingId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("dataverse down"));

        var denied = await host.SendAsync(Get($"/api/ai/playbooks/by-id/{deniedId}"));
        var unknown = await host.SendAsync(Get($"/api/ai/playbooks/by-id/{unknownId}"));
        var faulting = await host.SendAsync(Get($"/api/ai/playbooks/by-id/{faultingId}"));

        foreach (var (response, id) in new[] { (denied, deniedId), (unknown, unknownId), (faulting, faultingId) })
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var body = await response.Content.ReadAsStringAsync();
            DetailAndExtensions(body).Should().NotContain(id.ToString()).And.NotContainEquivalentOf(id.ToString("N"));
        }

        var deniedBody = Normalize(await denied.Content.ReadAsStringAsync());
        deniedBody.Should().Be(Normalize(await unknown.Content.ReadAsStringAsync()));
        deniedBody.Should().Be(Normalize(await faulting.Content.ReadAsStringAsync()));
        JsonNode.Parse(deniedBody)!["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.record_unavailable");
        host.Lookup.VerifyNoOtherCalls(); // the handler (and so its response cache) was never reached
    }

    [Fact]
    public async Task ById_PublicPlaybook_AndPrivatePlaybookTheCallerCanRead_Return200()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var publicId = Guid.NewGuid();
        var readableId = Guid.NewGuid();
        host.PublicPlaybook(publicId);
        host.PrivatePlaybook(readableId);
        host.Access.Grant(Playbooks, readableId, AccessRights.Read);
        host.LookupReturns(publicId);
        host.LookupReturns(readableId);

        (await host.SendAsync(Get($"/api/ai/playbooks/by-id/{publicId}"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await host.SendAsync(Get($"/api/ai/playbooks/by-id/{readableId}"))).StatusCode.Should().Be(HttpStatusCode.OK);

        host.Access.Calls.Should().ContainSingle("the public playbook needs no rights query")
            .Which.Should().Be(new AccessCall(AccessPath.Record, Playbooks, readableId, HasToken: true));
    }

    [Fact]
    public async Task ById_AnAllowedCallerWarmingTheCache_DoesNotLetADeniedCallerReadTheCachedEntry()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = Guid.NewGuid();
        host.PrivatePlaybook(playbookId);
        host.Access.GrantFor(CallerOid, Playbooks, playbookId, AccessRights.Read);
        host.LookupReturns(playbookId);

        var allowed = await host.SendAsync(Get($"/api/ai/playbooks/by-id/{playbookId}", CallerOid));
        var denied = await host.SendAsync(Get($"/api/ai/playbooks/by-id/{playbookId}", OtherOid));

        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.Lookup.Verify(l => l.GetByIdAsync(playbookId.ToString(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task ById_LookupMissAfterAnAllowedDecision_IsTheSameUniform404()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var allowedButMissingId = Guid.NewGuid();
        var deniedId = Guid.NewGuid();
        host.PublicPlaybook(allowedButMissingId);
        host.PrivatePlaybook(deniedId);
        host.Lookup.Setup(l => l.GetByIdAsync(allowedButMissingId.ToString(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(PlaybookNotFoundException.ById(allowedButMissingId));

        var missing = await host.SendAsync(Get($"/api/ai/playbooks/by-id/{allowedButMissingId}"));
        var denied = await host.SendAsync(Get($"/api/ai/playbooks/by-id/{deniedId}"));

        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Normalize(await missing.Content.ReadAsStringAsync()).Should().Be(Normalize(await denied.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task ById_NoBearerToken_Is404_WithoutAnyAppOnlyRightsQuery()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = Guid.NewGuid();
        host.PrivatePlaybook(playbookId);
        host.Access.Grant(Playbooks, playbookId, AccessRights.Read);

        var request = Get($"/api/ai/playbooks/by-id/{playbookId}");
        request.Headers.Authorization = null; // authenticated by the test scheme, but no bearer token to forward

        (await host.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.Access.Calls.Should().BeEmpty("with no caller token the check fails closed without any app-only query");
        host.Lookup.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ById_NonGuidId_Is404_WithoutALookup()
    {
        await using var host = await PlaybookAuthHost.StartAsync();

        (await host.SendAsync(Get("/api/ai/playbooks/by-id/not-a-guid"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.Lookup.VerifyNoOtherCalls();
        host.Playbooks.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ById_Unauthenticated_Is401()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/ai/playbooks/by-id/{Guid.NewGuid()}");

        (await host.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // =========================================================================================
    // POST /api/ai/playbooks/{id:guid}/execute  (sweep #29)
    // =========================================================================================

    [Fact]
    public async Task Execute_DeniedUnknownAndFaultingPlaybook_AreOneUniform404_AndNothingRuns()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var documentId = host.ReadableDocument();
        var deniedId = Guid.NewGuid();
        var unknownId = Guid.NewGuid();
        var faultingId = Guid.NewGuid();
        host.PrivatePlaybook(deniedId);
        host.Playbooks.Setup(p => p.GetPlaybookAsync(unknownId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlaybookResponse?)null);
        host.Playbooks.Setup(p => p.GetPlaybookAsync(faultingId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("dataverse down"));

        var denied = await host.SendAsync(Execute(deniedId, documentId));
        var unknown = await host.SendAsync(Execute(unknownId, documentId));
        var faulting = await host.SendAsync(Execute(faultingId, documentId));

        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var deniedBody = Normalize(await denied.Content.ReadAsStringAsync());
        deniedBody.Should().Be(Normalize(await unknown.Content.ReadAsStringAsync()));
        deniedBody.Should().Be(Normalize(await faulting.Content.ReadAsStringAsync()));
        DetailAndExtensions(await denied.Content.ReadAsStringAsync()).Should().NotContain(deniedId.ToString());
        host.VerifyNothingRan();
    }

    [Fact]
    public async Task Execute_UnreadableDocument_Is403ProblemDetailsBeforeAnySseHeader_AndNothingRuns()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();
        var readable = host.ReadableDocument();
        var unreadable = Guid.NewGuid();

        var response = await host.SendAsync(Execute(playbookId, readable, unreadable));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        DetailAndExtensions(body).Should().NotContain(unreadable.ToString());
        JsonNode.Parse(body)!["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.insufficient_rights");
        host.VerifyNothingRan();
    }

    [Fact]
    public async Task Execute_UnknownDocument_DeniedDocument_AndSeamFault_AreOneUniform403()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();
        var deniedDocument = Guid.NewGuid();
        host.Access.Grant(Documents, deniedDocument, AccessRights.AppendTo);

        var unknown = await host.SendAsync(Execute(playbookId, Guid.NewGuid()));
        var denied = await host.SendAsync(Execute(playbookId, deniedDocument));
        host.Access.ThrowOnEveryCall = new HttpRequestException("RetrievePrincipalAccess failed");
        var fault = await host.SendAsync(Execute(playbookId, Guid.NewGuid()));

        unknown.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var unknownBody = Normalize(await unknown.Content.ReadAsStringAsync());
        unknownBody.Should().Be(Normalize(await denied.Content.ReadAsStringAsync()));
        unknownBody.Should().Be(Normalize(await fault.Content.ReadAsStringAsync()));
        host.VerifyNothingRan();
    }

    [Fact]
    public async Task Execute_SideEffectingPlaybook_RequiresWriteOnEveryDocument()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = Guid.NewGuid();
        host.PublicPlaybook(playbookId);
        host.NodesOf(playbookId, ExecutorType.AiAnalysis, ExecutorType.UpdateRecord);
        var readOnly = Guid.NewGuid();
        var writable = Guid.NewGuid();
        host.Access.Grant(Documents, readOnly, AccessRights.Read);
        host.Access.Grant(Documents, writable, AccessRights.Read | AccessRights.Write);
        host.RunSucceeds();

        var denied = await host.SendAsync(Execute(playbookId, readOnly));
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.VerifyNothingRan();

        var allowed = await host.SendAsync(Execute(playbookId, writable));
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        allowed.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
    }

    [Fact]
    public async Task Execute_ReaderOfEveryDocument_GetsTheSseStream_AndTheRouteIdIsNeverAuthorizedAsADocument()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();
        var documentId = host.ReadableDocument();
        host.RunSucceeds();

        var response = await host.SendAsync(Execute(playbookId, documentId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        host.Orchestration.Verify(o => o.ExecuteAsync(
            It.Is<PlaybookRunRequest>(r => r.PlaybookId == playbookId && r.DocumentIds.SequenceEqual(new[] { documentId })),
            It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Once());
        host.Access.Calls.Should().NotContain(c => c.Id == playbookId && c.Path == AccessPath.Document,
            "the route's {id} is a playbook id and must never be authorized as a document");
    }

    [Fact]
    public async Task Execute_NoBearerToken_Is403_WithoutAnyAppOnlyRightsQuery()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();
        var documentId = host.ReadableDocument();
        var request = Execute(playbookId, documentId);
        request.Headers.Authorization = null;

        (await host.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().BeEmpty();
        host.VerifyNothingRan();
    }

    [Fact]
    public async Task Execute_MissingDocumentIds_Is400_BeforeAnyRightsQuery()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();

        var response = await host.SendAsync(Execute(playbookId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(PlaybookRunEndpoints.DocumentIdsRequiredMessage);
        host.Access.Calls.Should().BeEmpty();
        host.Playbooks.VerifyNoOtherCalls();
        host.VerifyNothingRan();
    }

    [Fact]
    public async Task Execute_RunThatThrows_EmitsAFixedRunFailedMessage_NotTheExceptionText()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();
        var documentId = host.ReadableDocument();
        host.Orchestration
            .Setup(o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowingStream(new InvalidOperationException("secret-internal-detail")));

        var response = await host.SendAsync(Execute(playbookId, documentId));
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain(PlaybookRunEndpoints.RunFailedMessage);
        body.Should().NotContain("secret-internal-detail");
    }

    // =========================================================================================
    // POST /api/agent/run-playbook  (sweep #19)
    // =========================================================================================

    [Fact]
    public async Task AgentRunPlaybook_DeniedUnknownAndFaultingPlaybook_AreOneUniform404_AndNothingRuns()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var documentId = host.ReadableDocument();
        var deniedId = Guid.NewGuid();
        var unknownId = Guid.NewGuid();
        var faultingId = Guid.NewGuid();
        host.PrivatePlaybook(deniedId);
        host.Playbooks.Setup(p => p.GetPlaybookAsync(unknownId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlaybookResponse?)null);
        host.Playbooks.Setup(p => p.GetPlaybookAsync(faultingId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("dataverse down"));

        var denied = await host.SendAsync(RunPlaybook(deniedId, documentId));
        var unknown = await host.SendAsync(RunPlaybook(unknownId, documentId));
        var faulting = await host.SendAsync(RunPlaybook(faultingId, documentId));

        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var deniedBody = Normalize(await denied.Content.ReadAsStringAsync());
        deniedBody.Should().Be(Normalize(await unknown.Content.ReadAsStringAsync()));
        deniedBody.Should().Be(Normalize(await faulting.Content.ReadAsStringAsync()));
        DetailAndExtensions(await denied.Content.ReadAsStringAsync()).Should().NotContain(deniedId.ToString());
        host.VerifyNothingRan();
    }

    [Fact]
    public async Task AgentRunPlaybook_UnreadableDocument_Is403_AndNothingRuns()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();
        var unreadable = Guid.NewGuid();

        var response = await host.SendAsync(RunPlaybook(playbookId, unreadable));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        DetailAndExtensions(await response.Content.ReadAsStringAsync()).Should().NotContain(unreadable.ToString());
        host.VerifyNothingRan();
    }

    [Fact]
    public async Task AgentRunPlaybook_ReaderOfThePlaybookAndTheDocument_Gets202()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = Guid.NewGuid();
        host.PrivatePlaybook(playbookId);
        host.Access.Grant(Playbooks, playbookId, AccessRights.Read);
        host.NodesOf(playbookId, ExecutorType.AiAnalysis);
        var documentId = host.ReadableDocument();
        host.RunSucceeds();

        var response = await host.SendAsync(RunPlaybook(playbookId, documentId));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        host.Orchestration.Verify(o => o.ExecuteAsync(
            It.Is<PlaybookRunRequest>(r => r.PlaybookId == playbookId),
            It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task AgentRunPlaybook_EmptyPlaybookId_Is400_BeforeAnyRightsQuery()
    {
        await using var host = await PlaybookAuthHost.StartAsync();

        var response = await host.SendAsync(RunPlaybook(Guid.Empty, Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(AgentEndpoints.PlaybookIdRequiredDetail);
        host.Access.Calls.Should().BeEmpty();
        host.VerifyNothingRan();
    }

    // =========================================================================================
    // GET /api/agent/playbooks/status/{jobId:guid}  (sweep #78)
    // =========================================================================================

    [Fact]
    public async Task AgentStatus_AnotherCallersRun_AnOwnerlessRun_AndAnUnknownRun_AreOneUniform404_WithNoJobId()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var othersRun = Guid.NewGuid();
        var ownerlessRun = Guid.NewGuid();
        var unknownRun = Guid.NewGuid();
        host.RunStatus(othersRun, startedByOid: OtherOid);
        host.RunStatus(ownerlessRun, startedByOid: null);
        host.Orchestration.Setup(o => o.GetRunStatusAsync(unknownRun, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlaybookRunStatus?)null);

        var others = await host.SendAsync(Get($"/api/agent/playbooks/status/{othersRun}"));
        var ownerless = await host.SendAsync(Get($"/api/agent/playbooks/status/{ownerlessRun}"));
        var unknown = await host.SendAsync(Get($"/api/agent/playbooks/status/{unknownRun}"));

        foreach (var (response, id) in new[] { (others, othersRun), (ownerless, ownerlessRun), (unknown, unknownRun) })
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync()).Should().NotContain(id.ToString());
        }

        var othersBody = Normalize(await others.Content.ReadAsStringAsync());
        othersBody.Should().Be(Normalize(await ownerless.Content.ReadAsStringAsync()));
        othersBody.Should().Be(Normalize(await unknown.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task AgentStatus_TheCallerWhoStartedTheRun_Gets200()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var runId = Guid.NewGuid();
        host.RunStatus(runId, startedByOid: CallerOid);

        var response = await host.SendAsync(Get($"/api/agent/playbooks/status/{runId}", CallerOid));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // =========================================================================================
    // GET /api/ai/playbooks — owned list keyed by the caller's systemuserid (owner round 12 item 6)
    // =========================================================================================

    [Fact]
    public async Task OwnedPlaybookList_FiltersByTheCallersSystemUserId_NotTheEntraOid()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var systemUserId = Guid.NewGuid();
        host.Probe.SystemUserId = systemUserId;
        host.Playbooks.Setup(p => p.ListUserPlaybooksAsync(It.IsAny<Guid>(), It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookListResponse { Items = [], TotalCount = 0, Page = 1, PageSize = 20 });

        (await host.SendAsync(Get("/api/ai/playbooks"))).StatusCode.Should().Be(HttpStatusCode.OK);

        host.Playbooks.Verify(p => p.ListUserPlaybooksAsync(systemUserId, It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()), Times.Once());
        host.Playbooks.Verify(p => p.ListUserPlaybooksAsync(Guid.Parse(CallerOid), It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task OwnedPlaybookList_UnresolvableSystemUserId_IsAnEmptyPage_WithNoQuery()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        host.Probe.SystemUserId = null;

        var response = await host.SendAsync(Get("/api/ai/playbooks"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["items"]!.AsArray().Should().BeEmpty();
        host.Playbooks.Verify(p => p.ListUserPlaybooksAsync(It.IsAny<Guid>(), It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    // =========================================================================================
    // Playbook parameters on execute and agent run-playbook — the shared policy (owner round 16 item 3, task 164 r1)
    // =========================================================================================

    [Theory]
    [InlineData("userId")]
    [InlineData("USERID")]
    [InlineData("TenantId")]
    [InlineData("run.userId")]
    [InlineData("start.channels")]
    [InlineData("userPreferences.timeWindow")]
    public async Task Execute_ServerOwnedParameter_InAnyLetterCase_Is400_BeforeAnyRightsQuery_AndNothingRuns(string key)
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();
        var documentId = host.ReadableDocument();

        var response = await host.SendAsync(Execute(playbookId, new Dictionary<string, string> { [key] = Guid.NewGuid().ToString() }, documentId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>()
            .Should().Be(PlaybookAuthorizationFilter.ParameterRejectedErrorCode);
        host.Access.Calls.Should().BeEmpty("a parameter refusal depends on no record, so it precedes every rights query");
        host.Playbooks.VerifyNoOtherCalls();
        host.VerifyNothingRan();
    }

    [Theory]
    [InlineData("matterId", "not-a-guid")]
    [InlineData("documentOwnerId", "6f0c1a52-0000-4000-8000-000000000999")]
    [InlineData("tone", "formal")]
    [InlineData("focus", "6f0c1a52-0000-4000-8000-000000000999")]
    [InlineData("timeWindowHours", "24'/><condition attribute='ownerid' operator='ne' value='x")]
    [InlineData("todayUtc", "2026-10-04' or")]
    public async Task Execute_ParameterOfTheWrongShape_Is400_AndNothingRuns(string key, string value)
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();
        var documentId = host.ReadableDocument();

        var response = await host.SendAsync(Execute(playbookId, new Dictionary<string, string> { [key] = value }, documentId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Access.Calls.Should().BeEmpty();
        host.VerifyNothingRan();
    }

    [Fact]
    public async Task Execute_RecordParameter_UnknownDeniedAndFaulting_AreOneUniform403_AndNothingRuns()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();
        var documentId = host.ReadableDocument();
        var deniedMatter = Guid.NewGuid();
        host.Access.Grant(Matters, deniedMatter, AccessRights.AppendTo);

        var unknown = await host.SendAsync(Execute(playbookId, MatterParameter(Guid.NewGuid()), documentId));
        var denied = await host.SendAsync(Execute(playbookId, MatterParameter(deniedMatter), documentId));

        unknown.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var unknownBody = Normalize(await unknown.Content.ReadAsStringAsync());
        unknownBody.Should().Be(Normalize(await denied.Content.ReadAsStringAsync()));
        DetailAndExtensions(await denied.Content.ReadAsStringAsync()).Should().NotContain(deniedMatter.ToString());
        host.Access.Calls.Should().Contain(new AccessCall(AccessPath.Record, Matters, deniedMatter, HasToken: true),
            "the record parameter is decided by the caller's own rights on the matter");
        host.VerifyNothingRan();
    }

    [Fact]
    public async Task Execute_RecordParameter_ASeamFault_IsTheSameUniform403()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();
        var documentId = host.ReadableDocument();
        var reference = await host.SendAsync(Execute(playbookId, MatterParameter(Guid.NewGuid()), documentId));

        host.Access.ThrowOnRecordCalls = new HttpRequestException("RetrievePrincipalAccess failed");
        var fault = await host.SendAsync(Execute(playbookId, MatterParameter(Guid.NewGuid()), documentId));

        fault.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Normalize(await fault.Content.ReadAsStringAsync()).Should().Be(Normalize(await reference.Content.ReadAsStringAsync()));
        host.VerifyNothingRan();
    }

    [Fact]
    public async Task Execute_RecordParameterAWritingNodeUses_RequiresWrite_ReadSufficesOtherwise()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var documentId = host.ReadableDocument();
        var readOnlyMatter = Guid.NewGuid();
        var writableMatter = Guid.NewGuid();
        host.Access.Grant(Matters, readOnlyMatter, AccessRights.Read);
        host.Access.Grant(Matters, writableMatter, AccessRights.Read | AccessRights.Write);
        host.Access.Grant(Documents, documentId, AccessRights.Read | AccessRights.Write);
        host.RunSucceeds();

        // A playbook whose UpdateRecord node writes to {{matterId}} (the matter-health-single shape).
        var persisting = Guid.NewGuid();
        host.PublicPlaybook(persisting);
        host.NodesWithConfig(persisting,
            (ExecutorType.AiAnalysis, "{}"),
            (ExecutorType.UpdateRecord, "{\"entityLogicalName\":\"sprk_matter\",\"recordId\":\"{{matterId}}\"}"));

        (await host.SendAsync(Execute(persisting, MatterParameter(readOnlyMatter), documentId)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "Read on the matter does not let the run write to it");
        host.VerifyNothingRan();
        (await host.SendAsync(Execute(persisting, MatterParameter(writableMatter), documentId)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // A read-only playbook that only reads the matter: Read suffices.
        var reading = Guid.NewGuid();
        host.PublicPlaybook(reading);
        host.NodesWithConfig(reading,
            (ExecutorType.QueryDataverse, "{\"fetchXml\":\"<fetch><entity name='sprk_kpiassessment'><filter><condition attribute='sprk_matter' operator='eq' value='{{matterId}}'/></filter></entity></fetch>\"}"));
        (await host.SendAsync(Execute(reading, MatterParameter(readOnlyMatter), documentId)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Execute_ReaderWithAcceptedParameters_RunsAsTheCaller_TheirSystemUserIdIsTheRunUser()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();
        var documentId = host.ReadableDocument();
        var matter = Guid.NewGuid();
        host.Access.Grant(Matters, matter, AccessRights.Read);
        host.RunSucceeds();
        var parameters = new Dictionary<string, string>
        {
            ["matterId"] = matter.ToString(),
            ["timeWindowHours"] = "48",
            ["focus"] = "termination clauses",
        };

        var response = await host.SendAsync(Execute(playbookId, parameters, documentId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Orchestration.Verify(o => o.ExecuteAsync(
            It.Is<PlaybookRunRequest>(r => r.RunUserId == host.Probe.SystemUserId
                                           && r.Parameters!.Count == 3
                                           && r.Parameters["matterId"] == matter.ToString()),
            It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Execute_CallerWhoseSystemUserIdCannotBeResolved_Is403_AndNothingRuns()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();
        var documentId = host.ReadableDocument();
        host.Probe.SystemUserId = null;

        var response = await host.SendAsync(Execute(playbookId, documentId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.VerifyNothingRan();
    }

    [Fact]
    public async Task AgentRunPlaybook_ParameterPolicy_AppliesTheSame_AndTheRunUserIsTheCaller()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var playbookId = host.ReadOnlyPublicPlaybook();
        var documentId = host.ReadableDocument();
        var unreadableMatter = Guid.NewGuid();
        host.RunSucceeds();

        var serverOwned = await host.SendAsync(RunPlaybook(playbookId, documentId, new Dictionary<string, string> { ["userId"] = Guid.NewGuid().ToString() }));
        var unreadable = await host.SendAsync(RunPlaybook(playbookId, documentId, MatterParameter(unreadableMatter)));
        host.VerifyNothingRan();
        var allowed = await host.SendAsync(RunPlaybook(playbookId, documentId, new Dictionary<string, string> { ["focus"] = "risk" }));

        serverOwned.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unreadable.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        allowed.StatusCode.Should().Be(HttpStatusCode.Accepted);
        host.Orchestration.Verify(o => o.ExecuteAsync(
            It.Is<PlaybookRunRequest>(r => r.RunUserId == host.Probe.SystemUserId),
            It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    // =========================================================================================
    // GET /api/agent/playbooks/status/{jobId:guid} — a lookup fault is the same uniform 404 (task 164 r1)
    // =========================================================================================

    [Fact]
    public async Task AgentStatus_ALookupFault_IsTheSameUniform404_AsAnUnknownRun()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var unknownRun = Guid.NewGuid();
        var faultingRun = Guid.NewGuid();
        host.Orchestration.Setup(o => o.GetRunStatusAsync(unknownRun, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlaybookRunStatus?)null);
        host.Orchestration.Setup(o => o.GetRunStatusAsync(faultingRun, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("run store unavailable"));

        var unknown = await host.SendAsync(Get($"/api/agent/playbooks/status/{unknownRun}"));
        var faulting = await host.SendAsync(Get($"/api/agent/playbooks/status/{faultingRun}"));

        faulting.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var faultingBody = await faulting.Content.ReadAsStringAsync();
        faultingBody.Should().NotContain(faultingRun.ToString()).And.NotContain("run store unavailable");
        Normalize(faultingBody).Should().Be(Normalize(await unknown.Content.ReadAsStringAsync()));
    }

    // =========================================================================================
    // Owner round 12 item 6 — the caller's systemuserid on the agent list and on share / unshare (task 164 r1)
    // =========================================================================================

    [Fact]
    public async Task AgentOwnedPlaybookList_FiltersByTheCallersSystemUserId_NotTheEntraOid()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var systemUserId = Guid.NewGuid();
        host.Probe.SystemUserId = systemUserId;
        host.Playbooks.Setup(p => p.ListUserPlaybooksAsync(It.IsAny<Guid>(), It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookListResponse { Items = [], TotalCount = 0, Page = 1, PageSize = 50 });
        host.Playbooks.Setup(p => p.ListPublicPlaybooksAsync(It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookListResponse { Items = [], TotalCount = 0, Page = 1, PageSize = 50 });

        (await host.SendAsync(Get("/api/agent/playbooks"))).StatusCode.Should().Be(HttpStatusCode.OK);

        host.Playbooks.Verify(p => p.ListUserPlaybooksAsync(systemUserId, It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()), Times.Once());
        host.Playbooks.Verify(p => p.ListUserPlaybooksAsync(Guid.Parse(CallerOid), It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Share_And_Unshare_PassTheCallersSystemUserId_NotTheEntraOid()
    {
        await using var host = await PlaybookAuthHost.StartAsync();
        var systemUserId = Guid.NewGuid();
        host.Probe.SystemUserId = systemUserId;
        var playbookId = Guid.NewGuid();
        host.Playbooks.Setup(p => p.GetPlaybookAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse { Id = playbookId, Name = "Owned", IsPublic = false, OwnerId = systemUserId });
        host.Sharing.Setup(s => s.SharePlaybookAsync(playbookId, It.IsAny<SharePlaybookRequest>(), It.IsAny<Guid>()))
            .ReturnsAsync(new ShareOperationResult { Success = true });
        host.Sharing.Setup(s => s.RevokeShareAsync(playbookId, It.IsAny<RevokeShareRequest>(), It.IsAny<Guid>()))
            .ReturnsAsync(new ShareOperationResult { Success = true });
        var teamId = Guid.NewGuid();

        var share = Authenticated(HttpMethod.Post, $"/api/ai/playbooks/{playbookId}/share", CallerOid);
        share.Content = JsonContent.Create(new { teamIds = new[] { teamId } });
        var unshare = Authenticated(HttpMethod.Post, $"/api/ai/playbooks/{playbookId}/unshare", CallerOid);
        unshare.Content = JsonContent.Create(new { teamIds = new[] { teamId } });

        (await host.SendAsync(share)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await host.SendAsync(unshare)).StatusCode.Should().Be(HttpStatusCode.OK);

        host.Sharing.Verify(s => s.SharePlaybookAsync(playbookId, It.IsAny<SharePlaybookRequest>(), systemUserId), Times.Once());
        host.Sharing.Verify(s => s.RevokeShareAsync(playbookId, It.IsAny<RevokeShareRequest>(), systemUserId), Times.Once());
        host.Sharing.Verify(s => s.SharePlaybookAsync(It.IsAny<Guid>(), It.IsAny<SharePlaybookRequest>(), Guid.Parse(CallerOid)), Times.Never());
        host.Sharing.Verify(s => s.RevokeShareAsync(It.IsAny<Guid>(), It.IsAny<RevokeShareRequest>(), Guid.Parse(CallerOid)), Times.Never());
    }

    // =========================================================================================
    // Helpers
    // =========================================================================================

    private static HttpRequestMessage Get(string path, string callerOid = CallerOid) =>
        Authenticated(HttpMethod.Get, path, callerOid);

    private static HttpRequestMessage Execute(Guid playbookId, params Guid[] documentIds)
    {
        var request = Authenticated(HttpMethod.Post, $"/api/ai/playbooks/{playbookId}/execute", CallerOid);
        request.Content = JsonContent.Create(new { documentIds });
        return request;
    }

    private static HttpRequestMessage RunPlaybook(Guid playbookId, Guid documentId)
    {
        var request = Authenticated(HttpMethod.Post, "/api/agent/run-playbook", CallerOid);
        request.Content = JsonContent.Create(new { playbookId, documentId });
        return request;
    }

    private static HttpRequestMessage Execute(Guid playbookId, IReadOnlyDictionary<string, string> parameters, params Guid[] documentIds)
    {
        var request = Authenticated(HttpMethod.Post, $"/api/ai/playbooks/{playbookId}/execute", CallerOid);
        request.Content = JsonContent.Create(new { documentIds, parameters });
        return request;
    }

    private static HttpRequestMessage RunPlaybook(Guid playbookId, Guid documentId, IReadOnlyDictionary<string, string> parameters)
    {
        var request = Authenticated(HttpMethod.Post, "/api/agent/run-playbook", CallerOid);
        request.Content = JsonContent.Create(new { playbookId, documentId, parameters });
        return request;
    }

    private static Dictionary<string, string> MatterParameter(Guid matterId) => new() { ["matterId"] = matterId.ToString() };

    private static HttpRequestMessage Authenticated(HttpMethod method, string path, string callerOid)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        request.Headers.Add(PlaybookAuthzTestAuthHandler.CallerHeader, callerOid);
        return request;
    }

    /// <summary>The comparison the goal defines: everything except correlation/trace ids and the request path.</summary>
    private static string Normalize(string problemJson)
    {
        var node = JsonNode.Parse(problemJson)!.AsObject();
        node.Remove("correlationId");
        node.Remove("traceId");
        node.Remove("instance");
        return node.ToJsonString();
    }

    /// <summary>The body minus <c>instance</c> (the request path), so an id check looks only at detail and extensions.</summary>
    private static string DetailAndExtensions(string problemJson)
    {
        var node = JsonNode.Parse(problemJson)!.AsObject();
        node.Remove("instance");
        return node.ToJsonString();
    }

    private static async IAsyncEnumerable<PlaybookStreamEvent> SucceedingStream(
        Guid playbookId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var runId = Guid.NewGuid();
        yield return PlaybookStreamEvent.RunStarted(runId, playbookId, 1);
        await Task.Yield();
        yield return PlaybookStreamEvent.RunCompleted(runId, playbookId, new PlaybookRunMetrics());
    }

    private static async IAsyncEnumerable<PlaybookStreamEvent> ThrowingStream(Exception ex)
    {
        await Task.Yield();
        throw ex;
#pragma warning disable CS0162 // an iterator needs a yield to be one
        yield break;
#pragma warning restore CS0162
    }

    internal enum AccessPath { Document, Record }

    internal sealed record AccessCall(AccessPath Path, string Set, Guid Id, bool HasToken);

    /// <summary>
    /// The access-data boundary: answers "what may caller U do to record X of set S" from grants, and records every
    /// question. A grant made with <see cref="Grant"/> applies to every caller; <see cref="GrantFor"/> to one oid.
    /// </summary>
    internal sealed class RecordingAccessDataSource : IAccessDataSource
    {
        private readonly Dictionary<(string? Oid, string Set, Guid Id), AccessRights> _rights = new();

        public List<AccessCall> Calls { get; } = new();

        public Exception? ThrowOnEveryCall { get; set; }

        /// <summary>A fault on the entity-generic RECORD path only (documents still answer).</summary>
        public Exception? ThrowOnRecordCalls { get; set; }

        public void Grant(string set, Guid id, AccessRights rights) => _rights[(null, set, id)] = rights;

        public void GrantFor(string oid, string set, Guid id, AccessRights rights) => _rights[(oid, set, id)] = rights;

        public Task<AccessSnapshot> GetUserAccessAsync(
            string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default)
        {
            var id = Guid.Parse(resourceId);
            Calls.Add(new AccessCall(AccessPath.Document, Documents, id, !string.IsNullOrEmpty(userAccessToken)));
            return Answer(userId, Documents, id);
        }

        public Task<AccessSnapshot> GetRecordAccessAsync(
            string userId, string entitySetName, Guid recordId, string? userAccessToken, CancellationToken ct = default)
        {
            Calls.Add(new AccessCall(AccessPath.Record, entitySetName, recordId, !string.IsNullOrEmpty(userAccessToken)));
            return ThrowOnRecordCalls is not null
                ? Task.FromException<AccessSnapshot>(ThrowOnRecordCalls)
                : Answer(userId, entitySetName, recordId);
        }

        private Task<AccessSnapshot> Answer(string userId, string set, Guid id)
        {
            if (ThrowOnEveryCall is not null)
            {
                return Task.FromException<AccessSnapshot>(ThrowOnEveryCall);
            }

            var rights = _rights.TryGetValue((userId, set, id), out var own) ? own
                : _rights.TryGetValue((null, set, id), out var any) ? any
                : AccessRights.None;
            return Task.FromResult(new AccessSnapshot { UserId = userId, ResourceId = id.ToString(), AccessRights = rights });
        }
    }

    /// <summary><see cref="CallerRecordAccessProbe"/> at its virtual WhoAmI seam. A missing token answers null, like the real probe.</summary>
    internal sealed class RecordingSystemUserProbe : CallerRecordAccessProbe
    {
        public RecordingSystemUserProbe()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
        }

        public Guid? SystemUserId { get; set; } = Guid.NewGuid();

        public override Task<Guid?> GetCallerSystemUserIdAsync(string? callerBearerToken, CancellationToken ct = default) =>
            Task.FromResult(string.IsNullOrEmpty(callerBearerToken) ? null : SystemUserId);

        /// <summary>
        /// Table privileges the caller holds (sweep integration: task 162 f1's PERSONAL analysis branch asks for
        /// <c>prvReadsprk_analysis</c>). Empty by default — the same "not held" every test saw before.
        /// </summary>
        public HashSet<string> HeldPrivileges { get; } = new(StringComparer.Ordinal);

        public override Task<bool> CallerHoldsPrivilegeAsync(
            string? callerBearerToken, string privilegeName, CancellationToken ct = default) =>
            Task.FromResult(!string.IsNullOrEmpty(callerBearerToken) && HeldPrivileges.Contains(privilegeName));
    }

    /// <summary>A minimal host over the three REAL endpoint mappers.</summary>
    internal sealed class PlaybookAuthHost : IAsyncDisposable
    {
        private WebApplication? _app;
        private HttpClient? _client;

        public RecordingAccessDataSource Access { get; } = new();
        public RecordingSystemUserProbe Probe { get; } = new();
        public Mock<IPlaybookService> Playbooks { get; } = new(MockBehavior.Strict);
        public Mock<IPlaybookLookupService> Lookup { get; } = new(MockBehavior.Strict);
        public Mock<INodeService> Nodes { get; } = new(MockBehavior.Strict);
        public Mock<IPlaybookOrchestrationService> Orchestration { get; } = new(MockBehavior.Strict);
        public Mock<IPlaybookSharingService> Sharing { get; } = new(MockBehavior.Strict);

        public static async Task<PlaybookAuthHost> StartAsync()
        {
            var host = new PlaybookAuthHost();
            await host.InitializeAsync();
            return host;
        }

        private async Task InitializeAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();

            builder.Services
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = PlaybookAuthzTestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = PlaybookAuthzTestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, PlaybookAuthzTestAuthHandler>(
                    PlaybookAuthzTestAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(opt =>
            {
                foreach (var policy in new[] { "ai-stream", "ai-batch", "dataverse-query" })
                {
                    opt.AddPolicy(policy, _ => RateLimitPartition.GetNoLimiter(policy + "-test"));
                }
            });

            // The REAL authorization stack, substituted only at the access-data and WhoAmI boundaries.
            builder.Services.AddSingleton<IAccessDataSource>(Access);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();
            builder.Services.AddSingleton<CallerRecordAccessProbe>(Probe);

            builder.Services.AddSingleton(Playbooks.Object);
            builder.Services.AddSingleton(Lookup.Object);
            builder.Services.AddSingleton(Nodes.Object);
            builder.Services.AddSingleton(Orchestration.Object);
            builder.Services.AddSingleton<IEndpointResponseCache>(
                new EndpointResponseCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

            // Handler parameter types of routes these tests never call (the mappers need them to be services).
            builder.Services.AddSingleton(Sharing.Object);
            builder.Services.AddSingleton(new Mock<IChatClient>(MockBehavior.Strict).Object);
            builder.Services.AddScoped<ChatSessionManager>(_ =>
                throw new InvalidOperationException("POST /api/agent/message is not exercised by this host"));
            builder.Services.AddScoped<SprkChatAgentFactory>(_ =>
                throw new InvalidOperationException("POST /api/agent/message is not exercised by this host"));

            builder.WebHost.UseTestServer();
            _app = builder.Build();
            _app.UseRouting();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.UseRateLimiter();

            _app.MapPlaybookEndpoints();
            _app.MapPlaybookRunEndpoints();
            _app.MapAgentEndpoints();

            await _app.StartAsync();
            _client = _app.GetTestClient();
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => _client!.SendAsync(request);

        public void PublicPlaybook(Guid id) => Playbook(id, isPublic: true);

        public void PrivatePlaybook(Guid id) => Playbook(id, isPublic: false);

        private void Playbook(Guid id, bool isPublic) =>
            Playbooks.Setup(p => p.GetPlaybookAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PlaybookResponse { Id = id, Name = "Playbook", IsPublic = isPublic, OwnerId = Guid.NewGuid() });

        /// <summary>A public playbook whose only node is read-only, so its documents need Read, not Write.</summary>
        public Guid ReadOnlyPublicPlaybook()
        {
            var id = Guid.NewGuid();
            PublicPlaybook(id);
            NodesOf(id, ExecutorType.AiAnalysis);
            return id;
        }

        public void NodesOf(Guid playbookId, params ExecutorType[] executorTypes) =>
            Nodes.Setup(n => n.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(executorTypes
                    .Select(t => new PlaybookNodeDto { Id = Guid.NewGuid(), PlaybookId = playbookId, SprkExecutortype = t })
                    .ToArray());

        public void NodesWithConfig(Guid playbookId, params (ExecutorType Type, string ConfigJson)[] nodes) =>
            Nodes.Setup(n => n.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(nodes
                    .Select(n => new PlaybookNodeDto { Id = Guid.NewGuid(), PlaybookId = playbookId, SprkExecutortype = n.Type, ConfigJson = n.ConfigJson })
                    .ToArray());

        public Guid ReadableDocument()
        {
            var id = Guid.NewGuid();
            Access.Grant(Documents, id, AccessRights.Read);
            return id;
        }

        public void LookupReturns(Guid id) =>
            Lookup.Setup(l => l.GetByIdAsync(id.ToString(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PlaybookResponse { Id = id, Name = "Playbook" });

        public void RunSucceeds() =>
            Orchestration
                .Setup(o => o.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
                .Returns((PlaybookRunRequest r, HttpContext _, CancellationToken ct) => SucceedingStream(r.PlaybookId, ct));

        public void RunStatus(Guid runId, string? startedByOid) =>
            Orchestration.Setup(o => o.GetRunStatusAsync(runId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PlaybookRunStatus
                {
                    RunId = runId,
                    PlaybookId = Guid.NewGuid(),
                    State = PlaybookRunState.Running,
                    StartedAt = DateTimeOffset.UtcNow,
                    StartedByOid = startedByOid,
                });

        /// <summary>No run was started.</summary>
        public void VerifyNothingRan() =>
            Orchestration.Verify(o => o.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Never());

        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            if (_app is not null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }
    }
}

/// <summary>
/// Authenticates a request carrying <see cref="CallerHeader"/> as a caller whose Entra <c>oid</c> is the header value
/// and whose <c>tid</c> is fixed, independent of the Authorization header — so a test can present an authenticated
/// principal whose bearer token is absent (the "token unreadable" fail-closed case).
/// </summary>
public sealed class PlaybookAuthzTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "PlaybookAuthzTest";
    public const string CallerHeader = "X-Test-Caller-Oid";
    public const string TenantId = "6f0c1a52-0000-4000-8000-0000000000aa";

    public PlaybookAuthzTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(CallerHeader, out var oid) || string.IsNullOrEmpty(oid))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(new[] { new Claim("oid", oid.ToString()), new Claim("tid", TenantId) }, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

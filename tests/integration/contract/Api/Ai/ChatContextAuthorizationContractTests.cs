using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Agent;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.Safety.CrossMatter;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Ai.PlaybookRouteAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// The chat-family per-record authorization of unified-access-control-r2 task 164 (fix round 1, owner round 16 item 2),
/// exercised through the REAL <c>MapChatEndpoints</c>, <c>MapDispatchSessionEndpoint</c> and <c>MapAgentEndpoints</c>:
/// <c>POST /api/ai/chat/sessions</c> (sweep #24), <c>PATCH /api/ai/chat/sessions/{sessionId}/context</c> (#23),
/// <c>POST /api/ai/chat/sessions/{sessionId}/messages</c> (#25), <c>POST /api/ai/chat/sessions/{sessionId}/dispatch</c>
/// (#53) and <c>POST /api/agent/message</c> (#18), plus the owner-round-12-item-6 chat playbook list.
/// </summary>
/// <remarks>
/// <para><b>What is real and what is substituted.</b> The endpoint mappers, <see cref="SessionOwnershipFilterExtensions"/>,
/// <see cref="AiAuthorizationFilter"/>, <see cref="AiAuthorizationService"/>, <see cref="AuthorizationService"/>,
/// <see cref="OperationAccessRule"/>, <see cref="ChatSessionManager"/> (over an in-memory tenant cache) and
/// <see cref="ChatHistoryManager"/> are the production types. Substituted: the access data boundary (a recording fake),
/// the WhoAmI seam, the SPE file seam, the session repository, the playbook service, and the turn's collaborators that a
/// DENIED request never reaches (each is an observable: the persistence load, the context provider, the dispatch
/// orchestrator, the repository create). No HTTP handler is mocked (ADR-038).</para>
/// </remarks>
public class ChatContextAuthorizationContractTests
{
    private const string CallerOid = "6f0c1a52-0000-4000-8000-000000000164";
    private const string OtherOid = "6f0c1a52-0000-4000-8000-0000000001ff";
    private const string Tenant = PlaybookAuthzTestAuthHandler.TenantId;
    private const string Matters = "sprk_matters";
    private const string Projects = "sprk_projects";
    private const string Documents = "sprk_documents";
    private const string Analyses = "sprk_analysises";
    private const string Playbooks = "sprk_analysisplaybooks";

    // =========================================================================================
    // POST /api/ai/chat/sessions  (sweep #24, F0)
    // =========================================================================================

    [Fact]
    public async Task Create_UnreadableHost_UnreadableDocument_UnknownIds_AndASeamFault_AreOneUniform403_AndNothingIsStored()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var deniedMatter = Guid.NewGuid();
        var deniedDocument = Guid.NewGuid();
        host.Access.Grant(Matters, deniedMatter, AccessRights.AppendTo);
        host.Access.Grant(Documents, deniedDocument, AccessRights.AppendTo);

        var responses = new List<(HttpResponseMessage Response, Guid Id)>
        {
            (await host.SendAsync(Create(hostContext: Host("sprk_matter", deniedMatter))), deniedMatter),
            (await host.SendAsync(Create(hostContext: Host("matter", Guid.NewGuid()))), Guid.Empty),
            (await host.SendAsync(Create(documentId: deniedDocument)), deniedDocument),
            (await host.SendAsync(Create(documentId: Guid.NewGuid())), Guid.Empty),
        };
        host.Access.ThrowOnEveryCall = new HttpRequestException("RetrievePrincipalAccess failed");
        responses.Add((await host.SendAsync(Create(hostContext: Host("sprk_project", Guid.NewGuid()))), Guid.Empty));

        var reference = Normalize(await responses[0].Response.Content.ReadAsStringAsync());
        foreach (var (response, id) in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var body = await response.Content.ReadAsStringAsync();
            Normalize(body).Should().Be(reference);
            if (id != Guid.Empty)
            {
                body.Should().NotContain(id.ToString());
            }
        }

        JsonNode.Parse(reference)!["detail"]!.GetValue<string>().Should().Be(AiAuthorizationFilter.ChatContextAccessDeniedDetail);
        host.VerifyNoSessionCreated();
    }

    [Fact]
    public async Task Create_ReaderOfTheHostAndTheDocument_Gets201_AndTheSessionCarriesThem()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var matter = host.Readable(Matters);
        var document = host.Readable(Documents);

        var response = await host.SendAsync(Create(document, Host("sprk_matter", matter)));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        host.Repo.Verify(r => r.CreateSessionAsync(
            It.Is<ChatSession>(s => s.DocumentId == document.ToString() && s.HostContext!.EntityId == matter.ToString()),
            It.IsAny<CancellationToken>()), Times.Once());
    }

    [Theory]
    [InlineData("sprk_analysisoutput")]
    [InlineData("sprk_analysis")]
    public async Task Create_AnAnalysisHost_IsDecidedByTheAnalysisReadRule_ReadOnEveryAnchor_NeverARowReadOnTheAnalysis(string entityType)
    {
        await using var host = await ChatAuthHost.StartAsync();
        var matter = Guid.NewGuid();
        var document = Guid.NewGuid();
        var analysis = host.Analysis(("sprk_regardingmatter", matter), ("sprk_documentid", document));

        // A colleague's (Deep) Read on the analysis ROW itself grants nothing (owner round 15 item 4).
        host.Access.Grant(Analyses, analysis, AccessRights.Read);
        host.Access.Grant(Matters, matter, AccessRights.Read);
        var oneAnchorUnreadable = await host.SendAsync(Create(hostContext: Host(entityType, analysis)));
        host.Access.Grant(Documents, document, AccessRights.Read);
        var everyAnchorReadable = await host.SendAsync(Create(hostContext: Host(entityType, analysis)));

        oneAnchorUnreadable.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        everyAnchorReadable.StatusCode.Should().Be(HttpStatusCode.Created);
        host.Access.Calls.Should().Contain(new AccessCall(AccessPath.Record, Matters, matter, HasToken: true));
        host.Access.Calls.Should().Contain(new AccessCall(AccessPath.Document, Documents, document, HasToken: true));
        host.Access.Calls.Should().NotContain(c => c.Set == Analyses,
            "an analysis is decided by its anchors, never by a business-unit-depth Read on the analysis row");
    }

    /// <summary>
    /// Sweep integration (task 162 note §14.3, owner rounds 15 item 4 and 25 item 4): the chat host decides an analysis
    /// through 162 f1's ONE shared evaluation, so a PERSONAL (anchorless) analysis is a chat host for its creator —
    /// matched by Dataverse systemuserid, holding the Read privilege — and the uniform 403 for a colleague, exactly as
    /// GET answers 200 / 404. Never a row Read on the analysis.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_APersonalAnalysisHost_IsReadableByItsCreatorOnly_ThroughTheSharedRule(bool callerIsCreator)
    {
        await using var host = await ChatAuthHost.StartAsync();
        var creator = Guid.NewGuid();
        host.Probe.SystemUserId = callerIsCreator ? creator : Guid.NewGuid();
        host.Probe.HeldPrivileges.Add(AnalysisAuthorizationFilter.ReadAnalysisPrivilege);
        var personal = host.PersonalAnalysis(creator);
        host.Access.Grant(Analyses, personal, AccessRights.Read); // a row Read grants nothing either way

        var response = await host.SendAsync(Create(hostContext: Host("sprk_analysis", personal)));

        if (callerIsCreator)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            host.VerifyNoSessionCreated();
        }

        host.Access.Calls.Should().NotContain(c => c.Set == Analyses,
            "a personal analysis is decided by its creator, never by a business-unit-depth Read on the analysis row");
    }

    [Fact]
    public async Task Create_AnAnalysisHostWithNoAnchor_AnUnknownOne_AndAnAnchorReadFault_AreTheUniform403()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var standalone = host.Analysis();
        host.Access.Grant(Analyses, standalone, AccessRights.Read);
        var unknown = Guid.NewGuid();
        host.AnalysisRows.Setup(r => r.RetrieveAsync("sprk_analysis", unknown, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sprk_analysis With Id = " + unknown + " Does Not Exist"));
        var faulting = Guid.NewGuid();
        host.AnalysisRows.Setup(r => r.RetrieveAsync("sprk_analysis", faulting, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Dataverse unavailable"));

        var reference = Normalize(await (await host.SendAsync(Create(hostContext: Host("sprk_analysis", standalone)))).Content.ReadAsStringAsync());
        foreach (var id in new[] { standalone, unknown, faulting })
        {
            var response = await host.SendAsync(Create(hostContext: Host("sprk_analysisoutput", id)));
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var body = await response.Content.ReadAsStringAsync();
            Normalize(body).Should().Be(reference);
            body.Should().NotContain(id.ToString());
        }

        host.VerifyNoSessionCreated();
    }

    [Fact]
    public async Task Create_ADocumentHost_IsADocumentRead()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var unreadable = Guid.NewGuid();
        var readable = host.Readable(Documents);

        (await host.SendAsync(Create(hostContext: Host("sprk_document", unreadable)))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await host.SendAsync(Create(hostContext: Host("sprk_document", readable)))).StatusCode.Should().Be(HttpStatusCode.Created);
        host.Access.Calls.Should().Contain(new AccessCall(AccessPath.Document, Documents, unreadable, HasToken: true));
    }

    [Theory]
    [InlineData("contact")]
    [InlineData("account")]
    [InlineData("sprk_event")]
    [InlineData("sprk_communication")]
    public async Task Create_AHostOfAnUnauthorizableType_IsDropped_TheSessionIsCreatedWithoutIt(string entityType)
    {
        await using var host = await ChatAuthHost.StartAsync();

        var response = await host.SendAsync(Create(hostContext: Host(entityType, Guid.NewGuid())));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        host.Access.Calls.Should().BeEmpty("an unauthorizable host is never checked or kept");
        host.Repo.Verify(r => r.CreateSessionAsync(It.Is<ChatSession>(s => s.HostContext == null), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Create_ANonGuidDocumentId_OrHostEntityId_Is400_BeforeAnyRightsQuery()
    {
        await using var host = await ChatAuthHost.StartAsync();

        var badDocument = await host.SendAsync(CreateRaw(new { documentId = "doc-test-001" }));
        var badHost = await host.SendAsync(CreateRaw(new { hostContext = new { entityType = "sprk_matter", entityId = "M-2024-0341" } }));

        badDocument.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        badHost.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Access.Calls.Should().BeEmpty();
        host.VerifyNoSessionCreated();
    }

    [Fact]
    public async Task Create_APlaybookTheCallerMayNotUse_IsTheUniform403_AndAPublicOneIsAccepted()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var privatePlaybook = host.Playbook(isPublic: false);
        var publicPlaybook = host.Playbook(isPublic: true);

        var denied = await host.SendAsync(Create(playbookId: privatePlaybook));
        host.VerifyNoSessionCreated();
        var allowed = await host.SendAsync(Create(playbookId: publicPlaybook));

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        JsonNode.Parse(await denied.Content.ReadAsStringAsync())!["detail"]!.GetValue<string>()
            .Should().Be(AiAuthorizationFilter.ChatContextAccessDeniedDetail);
        allowed.StatusCode.Should().Be(HttpStatusCode.Created);
        host.Access.Calls.Should().Contain(new AccessCall(AccessPath.Record, Playbooks, privatePlaybook, HasToken: true));
    }

    [Fact]
    public async Task Create_NoBearerToken_IsTheUniform403_WithoutAnyAppOnlyRightsQuery()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var matter = host.Readable(Matters);
        var request = Create(hostContext: Host("sprk_matter", matter));
        request.Headers.Authorization = null;

        (await host.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().BeEmpty("with no caller token the check fails closed without any app-only query");
        host.VerifyNoSessionCreated();
    }

    [Fact]
    public async Task Create_Unauthenticated_Is401()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/ai/chat/sessions") { Content = JsonContent.Create(new { }) };

        (await host.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // =========================================================================================
    // PATCH /api/ai/chat/sessions/{sessionId}/context  (sweep #23, F1)
    // =========================================================================================

    [Fact]
    public async Task SwitchContext_AnyIdTheCallerCannotRead_IsTheUniform403_AndTheSessionIsUnchanged()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var originalMatter = host.Readable(Matters);
        var session = host.SeedSession(hostContext: Host("sprk_matter", originalMatter));
        var readableDocument = host.Readable(Documents);

        var deniedHost = await host.SendAsync(Switch(session, new { hostContext = Host("sprk_matter", Guid.NewGuid()) }));
        var deniedDocument = await host.SendAsync(Switch(session, new { documentId = Guid.NewGuid() }));
        var deniedAdditional = await host.SendAsync(Switch(session, new { additionalDocumentIds = new[] { readableDocument, Guid.NewGuid() } }));
        var deniedPlaybook = await host.SendAsync(Switch(session, new { playbookId = host.Playbook(isPublic: false) }));

        foreach (var response in new[] { deniedHost, deniedDocument, deniedAdditional, deniedPlaybook })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var stored = await host.ReadSessionAsync(session);
        stored!.HostContext!.EntityId.Should().Be(originalMatter.ToString(), "a denied switch never reaches the session");
        stored.AdditionalDocumentIds.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task SwitchContext_AReader_Gets204_AndTheSessionPointsAtTheNewContext()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var session = host.SeedSession();
        var project = host.Readable(Projects);
        var document = host.Readable(Documents);

        var response = await host.SendAsync(Switch(session, new { documentId = document, hostContext = Host("sprk_project", project) }));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var stored = await host.ReadSessionAsync(session);
        stored!.DocumentId.Should().Be(document.ToString());
        stored.HostContext!.EntityId.Should().Be(project.ToString());
    }

    [Fact]
    public async Task SwitchContext_ToAnUnauthorizableHostType_DropsTheHost()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var session = host.SeedSession(hostContext: Host("sprk_matter", host.Readable(Matters)));

        var response = await host.SendAsync(Switch(session, new { hostContext = Host("contact", Guid.NewGuid()) }));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await host.ReadSessionAsync(session))!.HostContext.Should().BeNull("the session no longer carries a host it cannot check");
    }

    [Fact]
    public async Task SwitchContext_MoreThanFiveAdditionalDocuments_OrANonGuid_Is400_BeforeAnyRightsQuery()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var session = host.SeedSession();

        var tooMany = await host.SendAsync(Switch(session, new { additionalDocumentIds = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray() }));
        var nonGuid = await host.SendAsync(Switch(session, new { additionalDocumentIds = new[] { "doc-new-001" } }));

        tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        nonGuid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Access.Calls.Should().BeEmpty();
    }

    // =========================================================================================
    // POST /api/ai/chat/sessions/{sessionId}/messages  (sweep #25, F2)
    // =========================================================================================

    [Fact]
    public async Task Messages_APerTurnDocumentTheCallerCannotRead_IsTheUniform403ProblemDetails_NotAStream_AndNoTurnRuns()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var session = host.SeedSession();
        var unreadable = Guid.NewGuid();

        var response = await host.SendAsync(Message(session, documentId: unreadable));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().NotContain(unreadable.ToString());
        host.VerifyNoTurnRan();
    }

    [Fact]
    public async Task Messages_APreFixSessionWhoseStoredContextIsUnreadable_IsDenied()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var hostDenied = host.SeedSession(hostContext: Host("sprk_matter", Guid.NewGuid()));
        var documentDenied = host.SeedSession(documentId: Guid.NewGuid().ToString());
        var additionalDenied = host.SeedSession(additionalDocumentIds: [host.Readable(Documents).ToString(), Guid.NewGuid().ToString()]);
        var playbookDenied = host.SeedSession(playbookId: host.Playbook(isPublic: false));

        foreach (var session in new[] { hostDenied, documentDenied, additionalDenied, playbookDenied })
        {
            (await host.SendAsync(Message(session))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        host.VerifyNoTurnRan();
    }

    [Fact]
    public async Task Messages_AStoredSpeItemDocument_IsDecidedByTheCallersOwnSpeRead()
    {
        await using var host = await ChatAuthHost.StartAsync();
        const string itemId = "01ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var unreadable = host.SeedSession(documentId: itemId, documentDriveId: "b!denied-drive");
        var noDrive = host.SeedSession(documentId: itemId);
        var readable = host.SeedSession(documentId: itemId, documentDriveId: "b!readable-drive");
        host.Files.Setup(f => f.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), "b!readable-drive", itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(itemId, "draft.docx", null, 10, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false, null));

        (await host.SendAsync(Message(unreadable))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await host.SendAsync(Message(noDrive))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "an SPE item with no recorded drive cannot be decided");
        host.VerifyNoTurnRan();

        var allowed = await host.SendAsync(Message(readable), HttpCompletionOption.ResponseHeadersRead);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Files.Verify(f => f.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), "b!readable-drive", itemId, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Messages_AStoredAnalysisHost_IsReDecidedByItsAnchorsOnEveryTurn()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var matter = Guid.NewGuid();
        var analysis = host.Analysis(("sprk_regardingmatter", matter));
        host.Access.Grant(Analyses, analysis, AccessRights.Read);
        var session = host.SeedSession(hostContext: Host("sprk_analysisoutput", analysis));

        (await host.SendAsync(Message(session))).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the caller cannot read the analysis's matter, whatever their Read on the analysis row");
        host.VerifyNoTurnRan();

        host.Access.Grant(Matters, matter, AccessRights.Read);
        var allowed = await host.SendAsync(Message(session), HttpCompletionOption.ResponseHeadersRead);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Messages_AStoredHostOfAnUnauthorizableType_IsDroppedAndPersisted_AndTheTurnProceeds()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var session = host.SeedSession(hostContext: Host("contact", Guid.NewGuid()));

        var response = await host.SendAsync(Message(session), HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await host.ReadSessionAsync(session))!.HostContext.Should().BeNull();
        host.Access.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Messages_AReaderOfEverythingTheSessionCarries_GetsTheSseStream()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var session = host.SeedSession(
            documentId: host.Readable(Documents).ToString(),
            hostContext: Host("sprk_matter", host.Readable(Matters)),
            playbookId: host.Playbook(isPublic: true));

        var response = await host.SendAsync(Message(session), HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        host.Persistence.Verify(p => p.LoadSessionAsync(Tenant, session, It.IsAny<CancellationToken>()), Times.Once(),
            "the turn body ran");
    }

    [Fact]
    public async Task Messages_ANonGuidPerTurnDocument_Is400()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var session = host.SeedSession();

        var request = Authenticated(HttpMethod.Post, $"/api/ai/chat/sessions/{session}/messages");
        request.Content = JsonContent.Create(new { message = "hi", documentId = "doc-test-001" });

        (await host.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.VerifyNoTurnRan();
    }

    [Fact]
    public async Task Messages_NoBearerToken_IsTheUniform403_WithoutAnyAppOnlyRightsQuery()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var session = host.SeedSession(hostContext: Host("sprk_matter", host.Readable(Matters)));
        var request = Message(session);
        request.Headers.Authorization = null;

        (await host.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().BeEmpty();
        host.VerifyNoTurnRan();
    }

    // =========================================================================================
    // POST /api/ai/chat/sessions/{sessionId}/dispatch  (sweep #53, F3)
    // =========================================================================================

    [Fact]
    public async Task Dispatch_AStoredContextTheCallerCannotRead_IsTheUniform403_AndTheOrchestratorNeverRuns()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var hostDenied = host.SeedSession(hostContext: Host("sprk_matter", Guid.NewGuid()));
        var documentDenied = host.SeedSession(documentId: Guid.NewGuid().ToString());

        var first = await host.SendAsync(Dispatch(hostDenied));
        var second = await host.SendAsync(Dispatch(documentDenied));

        first.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        second.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Normalize(await first.Content.ReadAsStringAsync()).Should().Be(Normalize(await second.Content.ReadAsStringAsync()));
        host.Dispatch.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Dispatch_ANullBody_IsRefusedByTheFilter_WithTheHandlersOwn400()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var session = host.SeedSession(hostContext: Host("sprk_matter", Guid.NewGuid()));
        var request = Authenticated(HttpMethod.Post, $"/api/ai/chat/sessions/{session}/dispatch");
        request.Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json");

        var response = await host.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>()
            .Should().Be(DispatchSessionEndpoint.ErrorCodeBindingRequired);
        host.Access.Calls.Should().BeEmpty();
        host.Dispatch.Calls.Should().Be(0);
    }

    /// <summary>
    /// The black-box test above cannot tell the filter's 400 from the handler's (they are the same body by design), so
    /// this one pins the two properties that make a null body safe: the filter is keyed on the handler's DECLARED type
    /// (it recognises the dispatch route with no body at all), and it answers without calling the handler.
    /// </summary>
    [Fact]
    public async Task Dispatch_TheFilterIsKeyedOnTheDeclaredBodyType_AndANullBodyNeverReachesTheHandler()
    {
        var dispatchHandler = typeof(DispatchSessionEndpoint).GetMethod(
            "DispatchAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var historyHandler = typeof(ChatEndpoints).GetMethod(
            "GetHistoryAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var keyed = AiAuthorizationFilter.FindChatContextParameter(dispatchHandler);
        keyed.Should().Be((1, typeof(DispatchSessionRequest)));
        AiAuthorizationFilter.FindChatContextParameter(historyHandler).Should().BeNull("a route with no chat body keeps the old path");

        var httpContext = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim("oid", CallerOid), new System.Security.Claims.Claim("tid", Tenant)], "Test")),
        };
        var invocation = new DefaultEndpointFilterInvocationContext(httpContext, "session-1", null);
        var handlerCalled = false;
        var filter = new AiAuthorizationFilter(new Mock<IAiAuthorizationService>(MockBehavior.Strict).Object);

        var result = await filter.InvokeAsync(invocation, _ => { handlerCalled = true; return ValueTask.FromResult<object?>(null); }, keyed);

        handlerCalled.Should().BeFalse("a null dispatch body is refused by the filter, never passed through");
        ((IStatusCodeHttpResult)result!).StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Dispatch_AReaderOfTheStoredContext_ReachesTheOrchestrator()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var session = host.SeedSession(hostContext: Host("sprk_matter", host.Readable(Matters)));

        var response = await host.SendAsync(Dispatch(session));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Dispatch.Calls.Should().Be(1);
    }

    // =========================================================================================
    // Revocation (F2, F3, F12)
    // =========================================================================================

    [Fact]
    public async Task Revocation_ASessionCreatedWhileTheCallerHeldRead_IsDeniedOnItsNextMessagesDispatchAndAgentTurn()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var matter = host.Readable(Matters);
        var created = await host.SendAsync(Create(hostContext: Host("sprk_matter", matter)));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var sessionId = JsonNode.Parse(await created.Content.ReadAsStringAsync())!["sessionId"]!.GetValue<string>();

        host.Access.Grant(Matters, matter, AccessRights.None); // the caller's Read is revoked

        (await host.SendAsync(Message(sessionId))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await host.SendAsync(Dispatch(sessionId))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await host.SendAsync(AgentMessage(conversationReference: sessionId))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.VerifyNoTurnRan();
        host.Dispatch.Calls.Should().Be(0);
    }

    // =========================================================================================
    // POST /api/agent/message  (sweep #18, F12)
    // =========================================================================================

    [Fact]
    public async Task AgentMessage_ABodyDocumentTheCallerCannotRead_IsTheUniform403_BeforeAnySessionIsCreated()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var unreadable = Guid.NewGuid();

        var response = await host.SendAsync(AgentMessage(documentId: unreadable));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(unreadable.ToString());
        host.VerifyNoSessionCreated();
        host.VerifyNoTurnRan();
    }

    [Fact]
    public async Task AgentMessage_AResumedSessionWithAnUnreadableStoredHost_IsTheUniform403()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var session = host.SeedSession(hostContext: Host("sprk_project", Guid.NewGuid()));

        (await host.SendAsync(AgentMessage(conversationReference: session))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.VerifyNoSessionCreated();
    }

    [Fact]
    public async Task AgentMessage_AReferenceToAnotherUsersSession_IsNotResumed_SoItsStoredContextIsNeitherCheckedNorUsed()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var foreign = host.SeedSession(hostContext: Host("sprk_matter", Guid.NewGuid()), ownerOid: OtherOid);
        var document = host.Readable(Documents);

        var response = await host.SendAsync(AgentMessage(documentId: document, conversationReference: foreign));

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().NotContain(c => c.Path == AccessPath.Record, "the foreign session's host is never consulted");
        host.Repo.Verify(r => r.CreateSessionAsync(It.Is<ChatSession>(s => s.HostContext == null), It.IsAny<CancellationToken>()), Times.Once(),
            "the handler mints a fresh session instead of resuming another user's");
    }

    [Fact]
    public async Task AgentMessage_AReaderOfTheBodyDocument_IsLetThrough()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var document = host.Readable(Documents);

        var response = await host.SendAsync(AgentMessage(documentId: document));

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        host.Repo.Verify(r => r.CreateSessionAsync(It.Is<ChatSession>(s => s.DocumentId == document.ToString()), It.IsAny<CancellationToken>()), Times.Once());
    }

    // =========================================================================================
    // Unchanged elsewhere: a route with no chat DTO keeps the existing path
    // =========================================================================================

    [Fact]
    public async Task History_ARouteWithNoChatRequestBody_KeepsTheExistingPath_AndAsksNothingOfTheStoredContext()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var session = host.SeedSession(hostContext: Host("sprk_matter", Guid.NewGuid()));

        var response = await host.SendAsync(Authenticated(HttpMethod.Get, $"/api/ai/chat/sessions/{session}/history"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Access.Calls.Should().BeEmpty();
    }

    // =========================================================================================
    // GET /api/ai/chat/playbooks — owned list keyed by the caller's systemuserid (owner round 12 item 6)
    // =========================================================================================

    [Fact]
    public async Task ChatOwnedPlaybookList_FiltersByTheCallersSystemUserId_NotTheEntraOid()
    {
        await using var host = await ChatAuthHost.StartAsync();
        var systemUserId = Guid.NewGuid();
        host.Probe.SystemUserId = systemUserId;
        host.Playbooks.Setup(p => p.ListUserPlaybooksAsync(It.IsAny<Guid>(), It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookListResponse { Items = [], TotalCount = 0, Page = 1, PageSize = 50 });
        host.Playbooks.Setup(p => p.ListPublicPlaybooksAsync(It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookListResponse { Items = [], TotalCount = 0, Page = 1, PageSize = 50 });

        (await host.SendAsync(Authenticated(HttpMethod.Get, "/api/ai/chat/playbooks"))).StatusCode.Should().Be(HttpStatusCode.OK);

        host.Playbooks.Verify(p => p.ListUserPlaybooksAsync(systemUserId, It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()), Times.Once());
        host.Playbooks.Verify(p => p.ListUserPlaybooksAsync(Guid.Parse(CallerOid), It.IsAny<PlaybookQueryParameters>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    // =========================================================================================
    // Helpers
    // =========================================================================================

    private static object Host(string entityType, Guid entityId) => new { entityType, entityId = entityId.ToString() };

    private static HttpRequestMessage Create(Guid? documentId = null, object? hostContext = null, Guid? playbookId = null) =>
        CreateRaw(new { documentId = documentId?.ToString(), hostContext, playbookId });

    private static HttpRequestMessage CreateRaw(object body)
    {
        var request = Authenticated(HttpMethod.Post, "/api/ai/chat/sessions");
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static HttpRequestMessage Switch(string sessionId, object body)
    {
        var request = Authenticated(HttpMethod.Patch, $"/api/ai/chat/sessions/{sessionId}/context");
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static HttpRequestMessage Message(string sessionId, Guid? documentId = null)
    {
        var request = Authenticated(HttpMethod.Post, $"/api/ai/chat/sessions/{sessionId}/messages");
        request.Content = JsonContent.Create(new { message = "What are the key risks?", documentId = documentId?.ToString() });
        return request;
    }

    private static HttpRequestMessage Dispatch(string sessionId)
    {
        var request = Authenticated(HttpMethod.Post, $"/api/ai/chat/sessions/{sessionId}/dispatch");
        request.Content = JsonContent.Create(new { bindingId = Guid.NewGuid().ToString() });
        return request;
    }

    private static HttpRequestMessage AgentMessage(Guid? documentId = null, string? conversationReference = null)
    {
        var request = Authenticated(HttpMethod.Post, "/api/agent/message");
        request.Content = JsonContent.Create(new { message = "Summarize this", documentId, conversationReference });
        return request;
    }

    private static HttpRequestMessage Authenticated(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        request.Headers.Add(PlaybookAuthzTestAuthHandler.CallerHeader, CallerOid);
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

    /// <summary>The dispatch orchestrator at its virtual seam: counts calls and streams one chunk.</summary>
    internal sealed class RecordingDispatchOrchestrator : SessionDispatchOrchestrator
    {
        public RecordingDispatchOrchestrator()
            : base(NullLogger<SessionDispatchOrchestrator>.Instance)
        {
        }

        public int Calls { get; private set; }

        public override async IAsyncEnumerable<AnalysisChunk> DispatchAsync(
            SessionDispatchRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            await Task.Yield();
            yield return AnalysisChunk.FromError("dispatched");
        }
    }

    /// <summary>A minimal host over the three REAL endpoint mappers of the chat family.</summary>
    internal sealed class ChatAuthHost : IAsyncDisposable
    {
        private WebApplication? _app;
        private HttpClient? _client;

        public RecordingAccessDataSource Access { get; } = new();
        public RecordingSystemUserProbe Probe { get; } = new();
        public Mock<IPlaybookService> Playbooks { get; } = new(MockBehavior.Loose);
        public Mock<ISpeFileOperations> Files { get; } = new(MockBehavior.Loose);
        public Mock<IChatDataverseRepository> Repo { get; } = new(MockBehavior.Loose);
        public Mock<IChatContextProvider> ContextProvider { get; } = new(MockBehavior.Loose);
        public Mock<ISessionPersistenceService> Persistence { get; } = new(MockBehavior.Loose);

        /// <summary>The app-only analysis anchor read of task 162's analysis-read rule (ids only).</summary>
        public Mock<IGenericEntityService> AnalysisRows { get; } = new(MockBehavior.Loose);
        public RecordingDispatchOrchestrator Dispatch { get; } = new();
        private InMemoryTenantCache Cache { get; } = new();

        public static async Task<ChatAuthHost> StartAsync()
        {
            var host = new ChatAuthHost();
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
                .AddScheme<AuthenticationSchemeOptions, PlaybookAuthzTestAuthHandler>(PlaybookAuthzTestAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(opt =>
            {
                foreach (var policy in new[] { "ai-stream", "ai-context", "ai-batch", "dataverse-query" })
                {
                    opt.AddPolicy(policy, _ => RateLimitPartition.GetNoLimiter(policy + "-test"));
                }
            });

            // The REAL authorization stack, substituted only at the access-data, WhoAmI and SPE boundaries.
            builder.Services.AddSingleton<IAccessDataSource>(Access);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();
            builder.Services.AddScoped<IAiAuthorizationService, AiAuthorizationService>();
            builder.Services.AddSingleton<CallerRecordAccessProbe>(Probe);
            builder.Services.AddSingleton(Files.Object);
            builder.Services.AddSingleton(Playbooks.Object);
            builder.Services.AddSingleton(AnalysisRows.Object);

            // The REAL session stack over an in-memory tenant cache and a substituted repository.
            builder.Services.AddSingleton<ITenantCache>(Cache);
            builder.Services.AddSingleton(Repo.Object);
            builder.Services.AddSingleton(Persistence.Object);
            builder.Services.AddScoped(sp => new ChatSessionManager(
                sp.GetRequiredService<ITenantCache>(), Repo.Object, NullLogger<ChatSessionManager>.Instance));
            builder.Services.AddScoped(sp => new ChatHistoryManager(
                sp.GetRequiredService<ChatSessionManager>(), Repo.Object, NullLogger<ChatHistoryManager>.Instance));
            builder.Services.AddScoped(sp => new PendingPlanManager(
                sp.GetRequiredService<ITenantCache>(), sp.GetRequiredService<ChatSessionManager>(), NullLogger<PendingPlanManager>.Instance));
            var chatClient = new Mock<IChatClient>(MockBehavior.Loose).Object;
            builder.Services.AddSingleton(chatClient);
            builder.Services.AddSingleton(sp => new SprkChatAgentFactory(chatClient, sp, NullLogger<SprkChatAgentFactory>.Instance));
            builder.Services.AddScoped(_ => ContextProvider.Object);
            builder.Services.AddSingleton<SessionDispatchOrchestrator>(Dispatch);

            // Collaborators of the chat routes a denied request never reaches (the mappers need them to be services).
            builder.Services.AddSingleton(new Mock<IMatterContextDetector>(MockBehavior.Loose).Object);
            builder.Services.AddSingleton(new Mock<IConversationHistorySanitizer>(MockBehavior.Loose).Object);
            builder.Services.AddSingleton(new CrossMatterSafetyTelemetry());
            builder.Services.AddSingleton(new AiTelemetry());
            // Task 254: the stamp's optional spend limit — no limit configured (the default), as on most stamps.
            builder.Services.AddSingleton<Sprk.Bff.Api.Services.Ai.Metering.IAiSpendLedger, Sprk.Bff.Api.Services.Ai.Metering.InMemoryAiSpendLedger>();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<Sprk.Bff.Api.Services.Ai.Metering.AiSpendLimit>();
            builder.Services.AddSingleton(new Mock<ISessionRestoreService>(MockBehavior.Loose).Object);
            builder.Services.AddSingleton(new Mock<ISessionTraceReader>(MockBehavior.Loose).Object);
            builder.Services.AddSingleton((AssistantSuggestionService)RuntimeHelpers.GetUninitializedObject(typeof(AssistantSuggestionService)));
            builder.Services.AddSingleton((ChatContextMappingService)RuntimeHelpers.GetUninitializedObject(typeof(ChatContextMappingService)));
            builder.Services.AddSingleton(new Mock<IPlaybookOrchestrationService>(MockBehavior.Strict).Object);

            builder.WebHost.UseTestServer();
            _app = builder.Build();
            _app.UseRouting();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.UseRateLimiter();

            _app.MapChatEndpoints();
            _app.MapDispatchSessionEndpoint();
            _app.MapAgentEndpoints();

            await _app.StartAsync();
            _client = _app.GetTestClient();
        }

        public Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead) =>
            _client!.SendAsync(request, completion);

        /// <summary>A new record of <paramref name="entitySet"/> the caller may Read.</summary>
        public Guid Readable(string entitySet)
        {
            var id = Guid.NewGuid();
            Access.Grant(entitySet, id, AccessRights.Read);
            return id;
        }

        /// <summary>
        /// A new analysis whose app-only anchor read answers exactly <paramref name="anchors"/> (anchor column, parent id);
        /// no anchors is a standalone analysis.
        /// </summary>
        public Guid Analysis(params (string Column, Guid Id)[] anchors)
        {
            var id = Guid.NewGuid();
            var row = new Microsoft.Xrm.Sdk.Entity("sprk_analysis", id);
            foreach (var (column, parentId) in anchors)
            {
                var target = AnalysisAuthorizationFilter.LookupColumns.Single(c => c.Column == column).TargetLogicalName;
                row[column] = new Microsoft.Xrm.Sdk.EntityReference(target, parentId);
            }

            AnalysisRows.Setup(r => r.RetrieveAsync("sprk_analysis", id, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(row);
            return id;
        }

        /// <summary>A new anchorless (personal) analysis created by <paramref name="createdBy"/> (a systemuserid).</summary>
        public Guid PersonalAnalysis(Guid createdBy)
        {
            var id = Guid.NewGuid();
            var row = new Microsoft.Xrm.Sdk.Entity("sprk_analysis", id)
            {
                ["createdby"] = new Microsoft.Xrm.Sdk.EntityReference("systemuser", createdBy),
            };
            AnalysisRows.Setup(r => r.RetrieveAsync("sprk_analysis", id, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(row);
            return id;
        }

        public Guid Playbook(bool isPublic)
        {
            var id = Guid.NewGuid();
            Playbooks.Setup(p => p.GetPlaybookAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PlaybookResponse { Id = id, Name = "Playbook", IsPublic = isPublic, OwnerId = Guid.NewGuid() });
            return id;
        }

        /// <summary>
        /// A session the CALLER owns, as the store already holds it (a session written before this fix included), read
        /// through the repository on the session manager's first cache miss.
        /// </summary>
        public string SeedSession(
            string? documentId = null,
            object? hostContext = null,
            Guid? playbookId = null,
            IReadOnlyList<string>? additionalDocumentIds = null,
            string? documentDriveId = null,
            string ownerOid = CallerOid)
        {
            var sessionId = Guid.NewGuid().ToString("N");
            ChatHostContext? host = null;
            if (hostContext is not null)
            {
                var node = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(hostContext))!;
                host = new ChatHostContext(node["entityType"]!.GetValue<string>(), node["entityId"]!.GetValue<string>());
            }

            Repo.Setup(r => r.GetSessionAsync(Tenant, sessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new ChatSession(
                    SessionId: sessionId,
                    TenantId: Tenant,
                    DocumentId: documentId,
                    PlaybookId: playbookId,
                    CreatedAt: DateTimeOffset.UtcNow,
                    LastActivity: DateTimeOffset.UtcNow,
                    Messages: [],
                    HostContext: host,
                    AdditionalDocumentIds: additionalDocumentIds)
                { OwnerOid = ownerOid, DocumentDriveId = documentDriveId });
            return sessionId;
        }

        public async Task<ChatSession?> ReadSessionAsync(string sessionId)
        {
            using var scope = _app!.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ChatSessionManager>().GetSessionAsync(Tenant, sessionId);
        }

        public void VerifyNoSessionCreated() =>
            Repo.Verify(r => r.CreateSessionAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()), Times.Never());

        /// <summary>No turn body ran: the messages handler never loaded its tabs and no agent asked for context.</summary>
        public void VerifyNoTurnRan()
        {
            Persistence.Verify(p => p.LoadSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
            ContextProvider.Invocations.Should().BeEmpty("no agent was created for a denied turn");
        }

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

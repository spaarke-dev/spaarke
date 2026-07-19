// spaarkeai-compose-r3 task 030 (FR-13/FR-16) — through-the-wire seam test (KEEP path:
// tests/integration/seam/** — end-to-end across the active-document → dispatch → ledger seam with
// production types; a router-unit ≠ working slice, ADR-038 §E-40 / tests/CLAUDE.md, NFR-06).
//
// WHAT THIS PROVES: task 030 added `confidence_band` / `paraId` / `start_offset` / `end_offset` as
// additive fields on `ComposeDraftPayload`, plus two pure Compose-owned derivation helpers
// (`ComposeDraftDisposition.DeriveConfidenceBand` / `ResolveParaIdAnchor`). This test drives a REAL
// compose-draft-alternative dispatch over the REAL app (WebApplicationFactory<Program>) — REAL
// ChatSessionManager, REAL SessionDispatchOrchestrator, REAL ActionRunner, REAL ContextBinder, REAL
// OutputRouter — so the stored ledger payload is genuinely production-shaped raw model JSON (not a
// hand-crafted C# object), and then exercises the Compose-owned materialize + derive seam
// (`ComposeDraftDisposition.DeserializePayload` + `DeriveConfidenceBand`) against that REAL stored
// entry. Only the routing/action/LLM module boundaries are doubled per ADR-038 (mirrors
// ComposeDocSessionDispatchSeamTests.cs's fixture recipe — module-boundary doubles, not a
// Mock<HttpMessageHandler>).
//
// KNOWN GAP (documented, not silently swallowed): the live `GET
// /api/ai/chat/sessions/{id}/compose-outputs` read endpoint (`ChatEndpoints.GetComposeOutputsAsync` /
// `ProjectComposeOutputs`) returns the RAW stored ledger payload verbatim — it does NOT currently
// call `ComposeDraftDisposition.DeserializePayload`/`DeriveConfidenceBand` before returning to the
// client. Wiring live confidence-band computation into that read path is a fast-follow — it lives in
// `Api/Ai/ChatEndpoints.cs`, outside task 030's file scope (`Services/Compose/*.cs` +
// `types/compose-contracts.ts` only, per the task's coordination boundary). This test proves the
// SessionLedger round-trip (store → real JSON → re-materialize → derive) is correct and ready for
// that follow-on wiring; it does not assert the GET endpoint itself returns a populated
// `confidence_band` today (it does not — see the second test).

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Api.Ai;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

/// <summary>Task 030 through-the-wire seam tests. See file header for the exact seam this proves.</summary>
public sealed class ComposeDraftConfidenceBandSeamTests : IClassFixture<ComposeDraftConfidenceBandSeamFixture>
{
    private readonly ComposeDraftConfidenceBandSeamFixture _fx;

    public ComposeDraftConfidenceBandSeamTests(ComposeDraftConfidenceBandSeamFixture fx) => _fx = fx;

    [Fact]
    public async Task DispatchGroundedComposeDraft_ThenDeserializeAndDerive_ReturnsHighConfidenceBand()
    {
        _fx.SeedComposeDraftAlternativeBinding();
        // Production-shaped raw model JSON: target_text + match_mode:strict + a cited source —
        // both grounding signals DeriveConfidenceBand looks for.
        _fx.OpenAi.RawJsonToReturn = """
            {
              "target_text": "thirty (30) days notice",
              "new_text": "sixty (60) days' written notice",
              "match_mode": "strict",
              "rationale": "Standard playbook term is 60 days.",
              "sources": ["doc:precedent-123"]
            }
            """;

        var chatSessionId = Guid.NewGuid().ToString("D");
        await _fx.Sessions.UpdateSessionCacheAsync(new ChatSession(
            SessionId: chatSessionId,
            TenantId: ComposeDraftConfidenceBandSeamFixture.TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: null));

        var client = _fx.CreateAuthenticatedClient();

        var dispatch = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{chatSessionId}/dispatch",
            new
            {
                bindingId = ComposeDraftConfidenceBandSeamFixture.ComposeBindingId.ToString(),
                args = new { selectionText = "Either party may terminate upon thirty (30) days notice." },
            });

        dispatch.StatusCode.Should().Be(HttpStatusCode.OK, "the seeded session + binding resolve to a real dispatch");

        // ── The REAL stored ledger entry — production JSON, produced by the REAL dispatch pipeline
        //    (SessionDispatchOrchestrator → ActionRunner → ContextBinder → OutputRouter), not a
        //    hand-crafted C# object.
        var session = await _fx.Sessions.GetSessionAsync(
            ComposeDraftConfidenceBandSeamFixture.TenantId, chatSessionId);
        session!.Outputs.Should().ContainSingle();
        var entry = session.Outputs!.Single();
        entry.Disposition.Should().Be("compose");

        // Prove the stored payload carries NO confidence_band (the frozen JPS schema never declares
        // it — the model literally cannot self-report it).
        entry.Payload.TryGetProperty("confidence_band", out _).Should().BeFalse(
            "the raw model output never includes confidence_band — it is server-derived, not model-supplied");

        // ── The Compose-owned materialize + derive seam (task 030) — the READ-side capability that
        //    a future caller (the compose-outputs endpoint, or ComposeService) wires in.
        var materialized = ComposeDraftDisposition.DeserializePayload(entry);
        materialized.ConfidenceBand.Should().BeNull("DeserializePayload stays a pure round-trip — it never auto-derives");

        var band = ComposeDraftDisposition.DeriveConfidenceBand(materialized);
        band.Should().Be(ComposeConfidenceBand.High,
            "a cited source + a strict target-text claim together satisfy both grounding signals");
    }

    [Fact]
    public async Task DispatchUngroundedComposeDraft_ThenDeserializeAndDerive_ReturnsLowConfidenceBand()
    {
        _fx.SeedComposeDraftAlternativeBinding();
        // No target_text (insertion-style), no sources — no grounding signal at all.
        _fx.OpenAi.RawJsonToReturn = """
            {
              "new_text": "Consider adding a force majeure clause.",
              "match_mode": "insert"
            }
            """;

        var chatSessionId = Guid.NewGuid().ToString("D");
        await _fx.Sessions.UpdateSessionCacheAsync(new ChatSession(
            SessionId: chatSessionId,
            TenantId: ComposeDraftConfidenceBandSeamFixture.TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: null));

        var client = _fx.CreateAuthenticatedClient();

        var dispatch = await client.PostAsJsonAsync(
            $"/api/ai/chat/sessions/{chatSessionId}/dispatch",
            new
            {
                bindingId = ComposeDraftConfidenceBandSeamFixture.ComposeBindingId.ToString(),
                args = new { selectionText = "General clause context." },
            });

        dispatch.StatusCode.Should().Be(HttpStatusCode.OK);

        var session = await _fx.Sessions.GetSessionAsync(
            ComposeDraftConfidenceBandSeamFixture.TenantId, chatSessionId);
        var entry = session!.Outputs!.Single();

        var materialized = ComposeDraftDisposition.DeserializePayload(entry);
        var band = ComposeDraftDisposition.DeriveConfidenceBand(materialized);

        band.Should().Be(ComposeConfidenceBand.Low,
            "no cited source and no resolvable anchor — the derivation never guesses a mid/high band");

        // ── Documents the KNOWN GAP (file header): the live GET compose-outputs read endpoint does
        //    NOT yet call DeserializePayload/DeriveConfidenceBand — it returns the raw ledger JSON
        //    verbatim, so confidence_band is absent from what the client receives TODAY. This is a
        //    fast-follow (Api/Ai/ChatEndpoints.cs is outside this task's file scope), asserted here so
        //    the gap is visible + tracked rather than silently assumed closed.
        var readBack = await client.GetAsync($"/api/ai/chat/sessions/{chatSessionId}/compose-outputs");
        readBack.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await readBack.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var payloadElement = doc.RootElement[0].GetProperty("payload");
        payloadElement.TryGetProperty("confidence_band", out _).Should().BeFalse(
            "KNOWN GAP: GetComposeOutputsAsync returns the raw stored payload verbatim — live wiring of " +
            "the server-derived band into that read path is a fast-follow outside task 030's file scope");
    }

    [Fact]
    public void ResolveParaIdAnchor_AgainstARealE2ParaIdMap_SurvivesSerializeDeserializeRoundTrip()
    {
        // Proves paraId/offset anchoring (FR-16) survives the SAME serialize/deserialize path the
        // dispatch pipeline uses, using a realistic multi-paragraph E2 substrate shape.
        var draft = new ComposeDraftPayload
        {
            TargetText = "thirty (30) days notice",
            NewText = "sixty (60) days' written notice",
            MatchMode = "strict",
            Sources = new[] { "doc:precedent-123" },
        };
        var paragraphTexts = new[]
        {
            "Recitals. This Agreement is entered into as of the Effective Date.",
            "Either party may terminate this Agreement upon thirty (30) days notice to the other party.",
            "Governing Law. This Agreement is governed by the laws of the State of Delaware.",
        };
        var paraIdMap = new[]
        {
            new ParaIdMapEntry(0, "AAAAAAAA", IsMinted: false),
            new ParaIdMapEntry(1, "BBBBBBBB", IsMinted: false),
            new ParaIdMapEntry(2, "CCCCCCCC", IsMinted: false),
        };

        var anchored = ComposeDraftDisposition.ResolveParaIdAnchor(draft, paraIdMap, paragraphTexts);
        var withBand = anchored with { ConfidenceBand = ComposeDraftDisposition.DeriveConfidenceBand(anchored) };

        var entry = ComposeDraftDisposition.BuildDraftOutput(
            "3f2504e0-4f89-41d3-9a0c-0305e82c3301", "compose-draft-alternative", turn: 1, withBand);
        var materialized = ComposeDraftDisposition.DeserializePayload(entry);

        materialized.ParaId.Should().Be("BBBBBBBB");
        materialized.StartOffset.Should().NotBeNull();
        materialized.EndOffset.Should().NotBeNull();
        materialized.ConfidenceBand.Should().Be(ComposeConfidenceBand.High);
    }
}

/// <summary>
/// Boots the REAL app (WebApplicationFactory&lt;Program&gt;) with a fake auth scheme, a REAL
/// <see cref="ChatSessionManager"/> over an in-memory tenant cache, and module-boundary doubles for
/// routing/action/LLM (ADR-038). Mirrors <c>ComposeDocSessionDispatchSeamFixture</c>'s recipe
/// (spaarkeai-compose-r2 DEF-11 seam test) — a new fixture, not a shared instance, per xUnit
/// <c>IClassFixture</c> scoping; the WebApplicationFactory/SpeFileStore-fake boilerplate is
/// intentionally the SAME shape as the existing fixture rather than a novel one.
/// </summary>
public sealed class ComposeDraftConfidenceBandSeamFixture : WebApplicationFactory<Program>
{
    public const string TenantId = "00000000-0000-0000-0000-0000000000ee";

    internal static readonly Guid ComposeBindingId = Guid.Parse("dddddddd-eeee-ffff-0000-111111111111");
    internal static readonly Guid ComposeActionId = Guid.Parse("22222222-3333-4444-5555-666666666666");

    private const string ComposeSelectionInputSchema =
        """{"type":"object","properties":{"selectionText":{"type":"string"}}}""";

    public ChatSessionManager Sessions { get; } = new(
        new InMemoryTenantCache(),
        Mock.Of<IChatDataverseRepository>(),
        Mock.Of<ILogger<ChatSessionManager>>());

    public Mock<IConsumerRoutingService> ConsumerRoutingMock { get; } = new();
    public Mock<IScopeResolverService> ScopeResolverMock { get; } = new();
    public StubOpenAiClient OpenAi { get; } = new();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["ConnectionStrings:ServiceBus"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["Cors:AllowedOrigins:0"] = "https://localhost:5173",
                ["UAMI_CLIENT_ID"] = "test-client-id",
                ["TENANT_ID"] = "test-tenant-id",
                ["API_APP_ID"] = "test-app-id",
                ["API_CLIENT_SECRET"] = "test-secret",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-app-id",
                ["AzureAd:Audience"] = "api://test-app-id",
                ["Graph:TenantId"] = "test-tenant-id",
                ["Graph:ClientId"] = "test-client-id",
                ["Graph:ClientSecret"] = "test-client-secret",
                ["Graph:UseManagedIdentity"] = "false",
                ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",
                ["Dataverse:EnvironmentUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ClientId"] = "test-client-id",
                ["Dataverse:ClientSecret"] = "test-client-secret",
                ["Dataverse:TenantId"] = "test-tenant-id",
                ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["ServiceBus:QueueName"] = "sdap-jobs",
                ["DocumentIntelligence:Enabled"] = "true",
                ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
                ["DocumentIntelligence:OpenAiKey"] = "test-key",
                ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",
                ["Analysis:Enabled"] = "true",
                ["Analysis:UseStubResolver"] = "true",
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["OfficeRateLimit:Enabled"] = "false",
                ["Redis:Enabled"] = "false",
                ["Redis:AllowInMemoryFallback"] = "true",
                ["ModelSelector:DefaultModel"] = "gpt-4o",
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
                ["AiSearchResilience:MaxRetryAttempts"] = "3",
                ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",
                ["GraphResilience:MaxRetryAttempts"] = "3",
                ["GraphResilience:RetryDelay"] = "00:00:01",
                ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",
                ["SpeAdmin:KeyVaultUri"] = "https://test.vault.azure.net/",
                ["ManagedIdentity:ClientId"] = "test-managed-identity-client-id",
                ["CosmosPersistence:Endpoint"] = "https://test.documents.azure.com:443/",
                ["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",
                ["AgentService:Enabled"] = "false",
                ["AgentService:Endpoint"] = "https://test.services.ai.azure.com/api/projects/test-project",
                ["AgentService:AgentId"] = "test-agent-id",
                ["AgentService:MaxConcurrency"] = "4",
                ["AgentService:ThreadCacheExpiryMinutes"] = "60",
            };
            config.AddInMemoryCollection(dict);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureTestServices(services =>
        {
            services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options =>
            {
                options.ThrowOnBadRequest = false;
            });

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = ComposeDraftConfidenceBandFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = ComposeDraftConfidenceBandFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, ComposeDraftConfidenceBandFakeAuthHandler>(
                ComposeDraftConfidenceBandFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = ComposeDraftConfidenceBandFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = ComposeDraftConfidenceBandFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            services.RemoveAll<IHostedService>();

            var dataverseMock = new Mock<IDataverseService>();
            dataverseMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseMock.Object);

            services.RemoveAll<IComposeService>();
            services.AddSingleton(Mock.Of<IComposeService>());

            services.RemoveAll<ChatSessionManager>();
            services.AddSingleton(Sessions);

            services.RemoveAll<IConsumerRoutingService>();
            services.AddSingleton(ConsumerRoutingMock.Object);
            services.RemoveAll<IScopeResolverService>();
            services.AddSingleton(ScopeResolverMock.Object);
            services.RemoveAll<IOpenAiClient>();
            services.AddSingleton<IOpenAiClient>(OpenAi);
        });
    }

    /// <summary>Seeds the compose-draft-alternative Binding (Compose disposition) + its Action at the
    /// routing module boundary, with an OutputSchemaJson matching the REAL production
    /// `ComposeDraftPayload` shape (target_text/new_text/match_mode/rationale/sources) — unlike the
    /// DEF-11 fixture's minimal `{explanation}` schema, this proves the derivation against
    /// realistically-shaped dispatch output.</summary>
    public void SeedComposeDraftAlternativeBinding()
    {
        ConsumerRoutingMock
            .Setup(c => c.GetBindingByIdAsync(ComposeBindingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Binding
            {
                BindingId = ComposeBindingId,
                ConsumerType = ConsumerTypes.ComposeDraftAlternative,
                ConsumerCode = "default",
                Environment = "*",
                ActionId = ComposeActionId,
                ActionKind = ActionKind.Prompted,
                Disposition = BindingDisposition.Compose,
                InputSchemaJson = ComposeSelectionInputSchema,
            });

        ScopeResolverMock
            .Setup(s => s.GetActionAsync(ComposeActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction
            {
                Id = ComposeActionId,
                Name = "Compose — Draft Alternative",
                SystemPrompt =
                    """{"$schema":"https://spaarke.com/schemas/prompt/v1","instruction":{"role":"You are the Spaarke Compose clause assistant.","task":"Draft an alternative for the clause in the ## Input section."},"output":{"fields":[{"name":"target_text","type":"string"},{"name":"new_text","type":"string"},{"name":"match_mode","type":"string"},{"name":"rationale","type":"string"},{"name":"sources","type":"array"}],"structuredOutput":true}}""",
                OutputSchemaJson =
                    """{"type":"object","additionalProperties":false,"required":["new_text"],"properties":{"target_text":{"type":"string"},"new_text":{"type":"string"},"match_mode":{"type":"string"},"rationale":{"type":"string"},"sources":{"type":"array","items":{"type":"string"}}}}""",
                Temperature = 0.2m,
            });
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }
}

/// <summary>Fake auth handler emitting BOTH tid (tenant) + oid (user) claims.</summary>
internal sealed class ComposeDraftConfidenceBandFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ComposeDraftConfidenceBandFakeAuth";

    public ComposeDraftConfidenceBandFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));
        }

        var oid = Request.Headers["X-Test-User"].ToString();
        if (string.IsNullOrWhiteSpace(oid)) oid = Guid.NewGuid().ToString();

        var claims = new List<Claim>
        {
            new("oid", oid),
            new("tid", ComposeDraftConfidenceBandSeamFixture.TenantId),
            new(ClaimTypes.NameIdentifier, oid),
            new(ClaimTypes.Name, $"Compose Confidence Band Seam Test User {oid}"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

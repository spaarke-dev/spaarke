// Through-the-wire contract tests for the SSE stream of POST /api/ai/chat/sessions/{sessionId}/messages
// (KEEP path: endpoint-contract, ADR-038 §2).
//
// WHY THIS FILE EXISTS
//   It replaces tests/unit/Sprk.Bff.Api.Tests/Integration/SseStreamingIntegrationTests.cs, whose 18 cases
//   re-created the streaming loop inside each test and asserted on that copy — "detached" tests in the
//   sense of ADR-038 Amendment A3: they passed whatever production did. Every test here drives the REAL
//   endpoint (ChatEndpoints.SendMessageAsync → SprkChatAgentFactory → SprkChatAgent + its middleware
//   pipeline → WriteChatSSEAsync → ChatHistoryManager/ChatSessionManager → TenantCache) through a
//   WebApplicationFactory<Program> host, and reads what production actually put on the wire.
//
// WHAT IS FAKED (external boundaries only)
//   - IChatClient: ScriptedChatClient — a scripted token stream (the Azure OpenAI boundary).
//   - IChatContextProvider: AiModule names it as the test seam ("production impl calls
//     Dataverse/ScopeResolverService; tests inject a stub").
//   - Dataverse / Cosmos / catalog stores: IChatDataverseRepository, ISessionPersistenceService,
//     IConsumerRoutingService, IWorkspaceStateService, IGenericEntityService, IDataverseService.
//   - IOpenAiClient (session-title completion) and auth (fake scheme).
//   - IDistributedCache: a RECORDING decorator over a real MemoryDistributedCache — every cache write in
//     the host (TenantCache sits on top of it) is observable for the ADR-014 assertion.
//   - The data-driven chat-tool catalog read (AgentToolCatalogProjector → AnalysisToolService.ListToolsAsync
//     → the typed HttpClient "AnalysisToolService"): a boundary stub answers the Dataverse Web API with an
//     EMPTY OData page ({"value":[]}). The real service still builds the query and parses the page, so every
//     turn runs the catalog-read-SUCCEEDED branch with an empty catalog — no outbound attempt, and no
//     dependence on TestOutboundNetworkGuard refusing the call. The stub is an ADDITIONAL (outer) handler,
//     not the primary one, because the guard wraps whatever primary handler a client ends up with; it
//     asserts nothing and records nothing (not antipattern B1).
//
// COVERAGE NOTE — the model boundary
//   RemoveAll<IChatClient>() removes the WHOLE registered pipeline, including Microsoft.Extensions.AI's
//   FunctionInvokingChatClient that AiModule wraps around the Azure OpenAI client (AddChatClient(...)
//   .UseFunctionInvocation(), AiModule.cs ~152). Spaarke's own agent middleware (content safety, cost
//   control, telemetry, routing) IS real here, but a regression in the function-invocation wrapper or its
//   registration is NOT covered by these tests.
//
// WHAT IS DELIBERATELY NOT HERE
//   - First-token / per-event latency: timing assertions against a scripted client prove nothing about
//     production latency and flake on CI. Performance belongs to tests/load/.
//   - A behavioural 429 for the "ai-stream" policy: the policy is a sliding window of 10 permits with a
//     2-deep QUEUE, so requests 11-12 block until the window slides (up to 60 s) and whether request 13
//     sees a full queue is a race against the in-memory host. Not deterministic, so the endpoint's
//     policy binding is asserted from endpoint metadata instead (ADR-016).

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Sprk.Bff.Api.Services.Workspace;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Contract tests for the SSE stream written by <c>POST /api/ai/chat/sessions/{sessionId}/messages</c>,
/// asserted against what production emits over an in-process <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// </summary>
public sealed class ChatMessageStreamingContractTests : IClassFixture<ChatMessageStreamingFixture>
{
    private const string MessagesRoute = "/api/ai/chat/sessions/{sessionId}/messages";

    /// <summary>Upper bound for waits on server-side signals — a deadlock guard, never asserted as a latency.</summary>
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

    private readonly ChatMessageStreamingFixture _fixture;

    public ChatMessageStreamingContractTests(ChatMessageStreamingFixture fixture)
    {
        _fixture = fixture;
    }

    // =========================================================================================
    // 1. Event sequence + wire format
    // =========================================================================================

    [Fact]
    public async Task SendMessage_ScriptedTokenStream_EmitsTypingStartTokensTypingEndDoneAsCamelCaseDataFrames()
    {
        var caller = NewCaller();
        var sessionId = await _fixture.CreateSessionAsync(caller);
        string[] tokens = ["The ", "indemnity ", "cap ", "is ", "two ", "million."];
        _fixture.ChatClient.ScriptTokens(tokens);

        var (response, body) = await SendTurnAsync(sessionId, caller);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        // Wire format: the WHOLE body is a sequence of `data: {json}\n\n` frames — nothing else.
        var frames = ParseFrames(body);
        string.Concat(frames.Select(f => $"data: {f.RawJson}\n\n")).Should().Be(body,
            "every byte of the stream must belong to a `data: {json}\\n\\n` frame");

        // Event order production emits for a tool-free, citation-free turn.
        frames.Select(f => f.Type).Should().Equal(
            new[] { "typing_start" }
                .Concat(Enumerable.Repeat("token", tokens.Length))
                .Concat(new[] { "typing_end", "done" }));

        // camelCase payload (ChatSseEvent → {"type","content","data"}), no PascalCase leakage.
        foreach (var frame in frames)
        {
            frame.Json.TryGetProperty("type", out _).Should().BeTrue();
            frame.Json.TryGetProperty("content", out _).Should().BeTrue();
            frame.Json.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null,
                "text-only events carry no structured payload (the structured case is the suggestions test)");
            frame.Json.TryGetProperty("Type", out _).Should().BeFalse("the payload is camelCase");
            frame.Json.TryGetProperty("Content", out _).Should().BeFalse("the payload is camelCase");
            frame.Json.TryGetProperty("Data", out _).Should().BeFalse("the payload is camelCase");
        }

        // One token event per scripted update, in order, concatenating to the scripted text.
        var tokenContents = frames.Where(f => f.Type == "token").Select(f => f.Json.GetProperty("content").GetString()).ToList();
        tokenContents.Should().Equal(tokens);
        string.Concat(tokenContents).Should().Be(string.Concat(tokens));

        frames.Single(f => f.Type == "done").Json.GetProperty("content").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task SendMessage_NoDocumentAndResponseAsksForUpload_EmitsTypedActionChipsInSuggestionsDataBeforeDone()
    {
        // ChatEndpoints.BuildMissingContextActionChips: no document on the session or the request, and the
        // assembled response contains a missing-context keyword ("please upload a document") ⇒ the three
        // deterministic 'action' chips ride ONE "suggestions" frame's structured `data` payload.
        var caller = NewCaller();
        var sessionId = await _fixture.CreateSessionAsync(caller);
        _fixture.ChatClient.ScriptTokens(["To review the clause, ", "please upload a document ", "first."]);

        var (response, body) = await SendTurnAsync(sessionId, caller);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var frames = ParseFrames(body);
        frames.Select(f => f.Type).Should().Equal(
            "typing_start", "token", "token", "token", "typing_end", "suggestions", "done");

        var suggestions = frames.Single(f => f.Type == "suggestions").Json;
        suggestions.GetProperty("content").ValueKind.Should().Be(JsonValueKind.Null, "structured events carry `data`, not `content`");
        var data = suggestions.GetProperty("data");
        data.TryGetProperty("Suggestions", out _).Should().BeFalse("the structured payload is camelCase too");
        var chips = data.GetProperty("suggestions").EnumerateArray().ToList();
        chips.Select(c => (
                Kind: c.GetProperty("kind").GetString(),
                Label: c.GetProperty("label").GetString(),
                ActionId: c.GetProperty("actionId").GetString()))
            .Should().Equal(
                ("action", "Upload File", "upload"),
                ("action", "Browse Matter Documents", "search"),
                ("action", "Select a Matter", "select"));
        chips.Should().OnlyContain(c => c.GetProperty("targetBindingId").ValueKind == JsonValueKind.Null,
            "an action chip is routed by actionId, never by a Binding id");
    }

    // =========================================================================================
    // 2. Error mid-stream
    // =========================================================================================

    [Fact]
    public async Task SendMessage_ModelThrowsAfterTwoTokens_EmitsTypingEndThenSafeErrorAndNothingAfter()
    {
        var caller = NewCaller();
        var sessionId = await _fixture.CreateSessionAsync(caller);
        const string secret = "SENTINEL-upstream-provider-internal tools[3].function.parameters";
        _fixture.ChatClient.ScriptTokensThenThrow(["Partial ", "answer "], new InvalidOperationException(secret));

        var (response, body) = await SendTurnAsync(sessionId, caller);

        // The stream was already committed, so the failure surfaces as an SSE frame on a 200 — not a torn connection.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var frames = ParseFrames(body);
        frames.Select(f => f.Type).Should().Equal(
            "typing_start", "token", "token", "typing_end", "error");

        // ADR-019 / G-P3 H3: the error carries the stable code + the ONE safe message, nothing from the exception.
        var error = frames.Last().Json.GetProperty("content").GetString();
        error.Should().StartWith($"[{ChatEndpoints.ChatTurnFailedErrorCode}]");
        error.Should().Be(ChatEndpoints.BuildTurnFailedErrorEvent().Content);
        body.Should().NotContain("SENTINEL", "the exception message must never reach the wire");
        body.Should().NotContain(nameof(InvalidOperationException), "the exception type must never reach the wire");
        body.Should().NotContain("   at Sprk.", "no stack frame may reach the wire");
        body.Should().NotContain("   at System.", "no stack frame may reach the wire");

        // Nothing follows the error: no done, and the half-generated answer is NOT persisted as an assistant turn.
        frames.Should().NotContain(f => f.Type == "done");
        var stored = await _fixture.ReadSessionAsync(sessionId);
        stored!.Messages.Should().NotContain(m => m.Role == ChatMessageRole.Assistant,
            "a failed turn must not be stored as if the partial text were a complete answer");
    }

    // =========================================================================================
    // 3. ADR-014 — streaming tokens are never cached
    // =========================================================================================

    [Fact]
    public async Task SendMessage_StreamedTokens_AreNeverWrittenToTheDistributedCache_AndWritesDoNotScaleWithTokenCount()
    {
        var caller = NewCaller();

        // Warm-up turn: tenant-scoped catalogs cached on a first turn must not be counted against either run.
        var warmUp = await _fixture.CreateSessionAsync(caller);
        _fixture.ChatClient.ScriptTokens(["warm ", "up"]);
        await SendTurnAsync(warmUp, caller);

        var fewTokens = Enumerable.Range(1, 3).Select(i => $"few{(char)('a' + i)}x ").ToArray();
        var manyTokens = Enumerable.Range(1, 30).Select(i => $"many{(char)('a' + (i % 26))}{(char)('a' + (i / 26))}q ").ToArray();

        var fewSession = await _fixture.CreateSessionAsync(caller);
        _fixture.ChatClient.ScriptTokens(fewTokens);
        _fixture.Cache.Clear();
        var (_, fewBody) = await SendTurnAsync(fewSession, caller);
        var fewWrites = _fixture.Cache.Writes;

        var manySession = await _fixture.CreateSessionAsync(caller);
        _fixture.ChatClient.ScriptTokens(manyTokens);
        _fixture.Cache.Clear();
        var (_, manyBody) = await SendTurnAsync(manySession, caller);
        var manyWrites = _fixture.Cache.Writes;

        // The turns really streamed (so the spy is not watching an idle host).
        ParseFrames(fewBody).Count(f => f.Type == "token").Should().Be(3);
        ParseFrames(manyBody).Count(f => f.Type == "token").Should().Be(30);

        // The spy really sees the chat path's writes: the session hot-cache entry was written.
        manyWrites.Should().Contain(w => w.Key.Contains(manySession, StringComparison.Ordinal),
            "the turn's history write-through goes to the session cache entry");

        // ADR-014: cache writes do not grow with the number of streamed tokens...
        manyWrites.Count.Should().Be(fewWrites.Count,
            "a 30-token turn must not write the cache more often than a 3-token turn — tokens are transient");

        // ...and after the warm-up, a turn writes ONLY its own session entry (no token-keyed or side entries).
        fewWrites.Should().OnlyContain(w => w.Key.Contains(fewSession, StringComparison.Ordinal),
            "post-warm-up, the only cache writes of a turn are the session hot-cache write-throughs");
        manyWrites.Should().OnlyContain(w => w.Key.Contains(manySession, StringComparison.Ordinal),
            "post-warm-up, the only cache writes of a turn are the session hot-cache write-throughs");

        // ADR-014: no written value IS an individual token (raw or JSON-encoded).
        foreach (var write in fewWrites.Concat(manyWrites))
        {
            var text = Encoding.UTF8.GetString(write.Value);
            foreach (var token in fewTokens.Concat(manyTokens))
            {
                text.Should().NotBe(token, $"cache key '{write.Key}' must not hold an individual streamed token");
                text.Should().NotBe(JsonSerializer.Serialize(token), $"cache key '{write.Key}' must not hold an individual streamed token");
            }
        }

        // The ASSEMBLED message may legitimately be persisted — and it is: exactly once, as the full text.
        var stored = await _fixture.ReadSessionAsync(manySession);
        stored!.Messages.Where(m => m.Role == ChatMessageRole.Assistant).Select(m => m.Content)
            .Should().ContainSingle().Which.Should().Be(string.Concat(manyTokens));
    }

    // =========================================================================================
    // 4. ADR-016 — rate limiting policy binding
    // =========================================================================================

    [Fact]
    public void SendMessageEndpoint_RouteMetadata_CarriesTheAiStreamRateLimitingPolicy()
    {
        var endpoints = _fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => string.Equals(e.RoutePattern.RawText, "/api/ai/chat" + "/sessions/{sessionId}/messages", StringComparison.Ordinal)
                        && e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST") == true)
            .ToList();

        var endpoint = endpoints.Should().ContainSingle($"exactly one POST {MessagesRoute} endpoint is mapped").Subject;
        var rateLimiting = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>();
        rateLimiting.Should().NotBeNull("the streaming route must carry a rate-limiting policy (ADR-016)");
        rateLimiting!.PolicyName.Should().Be("ai-stream",
            "ADR-016: the costly streaming route is bound to the strict per-user 'ai-stream' policy");
        endpoint.Metadata.GetMetadata<DisableRateLimitingAttribute>().Should().BeNull(
            "nothing may opt the streaming route back out of rate limiting");
    }

    // =========================================================================================
    // 5. Cancellation
    // =========================================================================================

    [Fact]
    public async Task SendMessage_ClientAbortsAfterFirstToken_CancellationReachesTheModelStream_AndTheTurnIsNotPersisted()
    {
        var caller = NewCaller();
        var sessionId = await _fixture.CreateSessionAsync(caller);
        var script = _fixture.ChatClient.ScriptFirstTokenThenWaitForCancellation("Hello ");
        var requestId = Guid.NewGuid().ToString("N");
        var completion = _fixture.Requests.Track(requestId);

        using var client = _fixture.CreateClientFor(caller);
        using var request = MessageRequest(sessionId);
        request.Headers.Add(RequestCompletionTracker.HeaderName, requestId);
        using var abort = new CancellationTokenSource();

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, abort.Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var stream = await response.Content.ReadAsStreamAsync();

        // Read frames until the first token arrives — the model stream is now parked inside the scripted client.
        var received = await ReadUntilFirstTokenAsync(stream);
        received.Select(f => f.Type).Should().Equal("typing_start", "token");

        // Client goes away.
        abort.Cancel();
        response.Dispose();

        // Hang guards only (expiry means cancellation never arrived) — not timing assertions.
        var observed = await script.CancellationObserved.WaitAsync(HangGuard)
            .ContinueWith(t => t.IsCompletedSuccessfully && t.Result, TaskScheduler.Default);
        observed.Should().BeTrue("the request-abort token must flow through the endpoint, agent and middleware into the model stream");

        await completion.WaitAsync(HangGuard);
        script.TokensYielded.Should().Be(1, "the model stream stops at the cancellation point");
        var stored = await _fixture.ReadSessionAsync(sessionId);
        stored!.Messages.Should().BeEmpty("a cancelled turn persists neither the user message nor a partial answer");
    }

    // =========================================================================================
    // Helpers
    // =========================================================================================

    private static string NewCaller() => Guid.NewGuid().ToString();

    private static HttpRequestMessage MessageRequest(string sessionId) =>
        new(HttpMethod.Post, $"/api/ai/chat/sessions/{sessionId}/messages")
        {
            Content = JsonContent.Create(new { message = "Summarise the indemnity position." }),
        };

    private async Task<(HttpResponseMessage Response, string Body)> SendTurnAsync(string sessionId, string caller)
    {
        using var client = _fixture.CreateClientFor(caller);
        using var request = MessageRequest(sessionId);
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return (response, body);
    }

    private sealed record SseFrame(string RawJson, JsonElement Json)
    {
        public string Type => Json.GetProperty("type").GetString()!;
    }

    private static List<SseFrame> ParseFrames(string body)
    {
        body.Should().EndWith("\n\n", "every SSE frame is terminated by a blank line");
        var frames = new List<SseFrame>();
        foreach (var chunk in body[..^2].Split("\n\n"))
        {
            chunk.Should().StartWith("data: ", "the chat stream uses unnamed `data:` frames only");
            var json = chunk["data: ".Length..];
            json.Should().NotContain("\n", "a frame is a single data line");
            frames.Add(new SseFrame(json, JsonDocument.Parse(json).RootElement.Clone()));
        }

        return frames;
    }

    private static async Task<List<SseFrame>> ReadUntilFirstTokenAsync(Stream stream)
    {
        var frames = new List<SseFrame>();
        var buffer = new StringBuilder();
        var bytes = new byte[1024];
        while (!frames.Any(f => f.Type == "token"))
        {
            var read = await stream.ReadAsync(bytes).AsTask().WaitAsync(HangGuard);
            read.Should().BePositive("the stream must deliver the first token before ending");
            buffer.Append(Encoding.UTF8.GetString(bytes, 0, read));

            var text = buffer.ToString();
            int end;
            while ((end = text.IndexOf("\n\n", StringComparison.Ordinal)) >= 0)
            {
                var json = text[..end]["data: ".Length..];
                frames.Add(new SseFrame(json, JsonDocument.Parse(json).RootElement.Clone()));
                text = text[(end + 2)..];
            }

            buffer.Clear().Append(text);
        }

        return frames;
    }
}

/// <summary>
/// In-process BFF host for <see cref="ChatMessageStreamingContractTests"/>. The chat endpoint, session/history
/// managers, agent factory, agent, agent middleware, SSE writer and TenantCache are the production types;
/// only the external boundaries listed in the test file header are substituted.
/// </summary>
public sealed class ChatMessageStreamingFixture : WebApplicationFactory<Program>
{
    public const string TenantId = "tenant-sse-contract-001";

    public ScriptedChatClient ChatClient { get; } = new();

    public RecordingDistributedCache Cache { get; } = new(new MemoryDistributedCache(
        Options.Create(new MemoryDistributedCacheOptions())));

    public RequestCompletionTracker Requests { get; } = new();

    public Mock<IChatDataverseRepository> Repo { get; } = new(MockBehavior.Loose);

    public async Task<string> CreateSessionAsync(string ownerOid)
    {
        using var scope = Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
        var session = await sessions.CreateSessionAsync(TenantId, ownerOid, documentId: null, playbookId: null, hostContext: null);
        return session.SessionId;
    }

    public async Task<ChatSession?> ReadSessionAsync(string sessionId)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ChatSessionManager>().GetSessionAsync(TenantId, sessionId);
    }

    public HttpClient CreateClientFor(string callerOid)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.Timeout = TimeSpan.FromSeconds(60);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        client.DefaultRequestHeaders.Add(StreamingFakeAuthHandler.CallerHeader, callerOid);
        return client;
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
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
                ["Graph:ManagedIdentity:Enabled"] = "false",
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
            });
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
            services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

            // Auth: a fake scheme whose oid comes from a per-test header, so each test is its own
            // rate-limit partition and session owner.
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = StreamingFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = StreamingFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, StreamingFakeAuthHandler>(StreamingFakeAuthHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = StreamingFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = StreamingFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();
            services.RemoveAll<IHostedService>();

            var dataverse = new Mock<IDataverseService>();
            dataverse.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverse.Object);
            services.RemoveAll<IGenericEntityService>();
            services.AddSingleton(new Mock<IGenericEntityService>(MockBehavior.Loose).Object);

            // Session cold tier (Dataverse) and warm tier (Cosmos): the hot tier (cache) holds every
            // session these tests create.
            services.RemoveAll<IChatDataverseRepository>();
            services.AddSingleton(Repo.Object);
            services.RemoveAll<ISessionPersistenceService>();
            services.AddSingleton(new Mock<ISessionPersistenceService>(MockBehavior.Loose).Object);

            // The model boundary.
            services.RemoveAll<IChatClient>();
            services.AddSingleton<IChatClient>(ChatClient);
            services.RemoveAll<IOpenAiClient>();
            services.AddSingleton(new Mock<IOpenAiClient>(MockBehavior.Loose).Object);

            // The context seam AiModule documents for tests.
            var contextProvider = new Mock<IChatContextProvider>(MockBehavior.Loose);
            contextProvider
                .Setup(p => p.GetContextAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<ChatHostContext?>(),
                    It.IsAny<IReadOnlyList<string>?>(), It.IsAny<IReadOnlyList<ChatSessionFile>?>(), It.IsAny<string?>(),
                    It.IsAny<IReadOnlyList<SessionOutput>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatContext("You are a test assistant.", null, null, null));
            services.RemoveAll<IChatContextProvider>();
            services.AddScoped(_ => contextProvider.Object);

            // Dataverse-backed catalogs: an empty capability catalog (no capability tools, no followups)
            // and no durable workspace tabs.
            var routing = new Mock<IConsumerRoutingService>(MockBehavior.Loose);
            routing.Setup(r => r.ListTextProjectableBindingsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<Binding>)Array.Empty<Binding>());
            services.RemoveAll<IConsumerRoutingService>();
            services.AddSingleton(routing.Object);
            var workspace = new Mock<IWorkspaceStateService>(MockBehavior.Loose);
            workspace.Setup(w => w.GetTabsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<WorkspaceTab>)Array.Empty<WorkspaceTab>());
            services.RemoveAll<IWorkspaceStateService>();
            services.AddScoped(_ => workspace.Object);

            // Every cache write in the host is recorded (TenantCache sits on top of IDistributedCache).
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache>(Cache);

            // The chat-tool catalog's Dataverse Web API read answers an empty OData page (see file header).
            services.AddHttpClient(nameof(AnalysisToolService))
                .AddHttpMessageHandler(() => new EmptyODataPageHandler());

            services.AddSingleton(Requests);
            services.AddSingleton<IStartupFilter, RequestCompletionStartupFilter>();

            // Test hosts must not authenticate for real — see TestTokenCredential. Keep LAST.
            services.UseStubTokenCredential();
        });
    }
}

/// <summary>
/// The model boundary: an <see cref="IChatClient"/> whose streaming response is scripted per test.
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    private Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> _script = _ => Empty();

    public void ScriptTokens(IReadOnlyList<string> tokens) => _script = _ => Yield(tokens, null);

    public void ScriptTokensThenThrow(IReadOnlyList<string> tokens, Exception toThrow) => _script = _ => Yield(tokens, toThrow);

    public CancellationScript ScriptFirstTokenThenWaitForCancellation(string firstToken)
    {
        var script = new CancellationScript();
        _script = ct => script.RunAsync(firstToken, ct);
        return script;
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AiChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        _script(cancellationToken);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<AiChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The streaming contract tests script only the streaming response.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Yield(IReadOnlyList<string> tokens, Exception? toThrow)
    {
        foreach (var token in tokens)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, token);
        }

        if (toThrow is not null)
        {
            throw toThrow;
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Empty()
    {
        await Task.Yield();
        yield break;
    }

    /// <summary>Yields one token, then parks until the caller's token is cancelled, recording that it was.</summary>
    public sealed class CancellationScript
    {
        private readonly TaskCompletionSource<bool> _observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _yielded;

        public Task<bool> CancellationObserved => _observed.Task;

        public int TokensYielded => Volatile.Read(ref _yielded);

        internal async IAsyncEnumerable<ChatResponseUpdate> RunAsync(
            string firstToken, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _yielded);
            yield return new ChatResponseUpdate(ChatRole.Assistant, firstToken);

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _observed.TrySetResult(true);
                throw;
            }
        }
    }
}

/// <summary>Records every write to the host's <see cref="IDistributedCache"/>, delegating to a real in-memory cache.</summary>
public sealed class RecordingDistributedCache : IDistributedCache
{
    private readonly IDistributedCache _inner;
    private readonly ConcurrentQueue<(string Key, byte[] Value)> _writes = new();

    public RecordingDistributedCache(IDistributedCache inner) => _inner = inner;

    public IReadOnlyList<(string Key, byte[] Value)> Writes => _writes.ToArray();

    public void Clear() => _writes.Clear();

    public byte[]? Get(string key) => _inner.Get(key);

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => _inner.GetAsync(key, token);

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        _writes.Enqueue((key, value));
        _inner.Set(key, value, options);
    }

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        _writes.Enqueue((key, value));
        return _inner.SetAsync(key, value, options, token);
    }

    public void Refresh(string key) => _inner.Refresh(key);

    public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);

    public void Remove(string key) => _inner.Remove(key);

    public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(key, token);
}

/// <summary>
/// Dataverse Web API boundary stub: answers every request with an empty OData collection page. Registered as
/// an outer (additional) handler, so the request never reaches the primary handler or the network.
/// </summary>
internal sealed class EmptyODataPageHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent("""{"value":[]}""", Encoding.UTF8, "application/json"),
        });
}

/// <summary>Signals when the host has finished a tagged request (the handler and every middleware returned).</summary>
public sealed class RequestCompletionTracker
{
    public const string HeaderName = "X-Test-Request-Id";

    private readonly ConcurrentDictionary<string, TaskCompletionSource> _pending = new();

    public Task Track(string requestId) =>
        _pending.GetOrAdd(requestId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    internal void Complete(string? requestId)
    {
        if (requestId is not null && _pending.TryGetValue(requestId, out var tcs))
        {
            tcs.TrySetResult();
        }
    }
}

internal sealed class RequestCompletionStartupFilter : IStartupFilter
{
    private readonly RequestCompletionTracker _tracker;

    public RequestCompletionStartupFilter(RequestCompletionTracker tracker) => _tracker = tracker;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            try
            {
                await nextMiddleware(context);
            }
            finally
            {
                _tracker.Complete(context.Request.Headers[RequestCompletionTracker.HeaderName].FirstOrDefault());
            }
        });
        next(app);
    };
}

/// <summary>Authenticates any request with a bearer header as the oid named in <see cref="CallerHeader"/>.</summary>
internal sealed class StreamingFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ChatStreamingFakeAuth";
    public const string CallerHeader = "X-Test-Caller-Oid";

    public StreamingFakeAuthHandler(
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

        var oid = Request.Headers[CallerHeader].FirstOrDefault() ?? TestSessionOwner.Oid;
        var claims = new List<Claim>
        {
            new("oid", oid),
            new("sub", oid),
            new("tid", ChatMessageStreamingFixture.TenantId),
            new(ClaimTypes.NameIdentifier, oid),
            new(ClaimTypes.Name, $"Streaming Test User {oid}"),
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

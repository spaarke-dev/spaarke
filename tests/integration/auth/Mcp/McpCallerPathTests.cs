// spaarkeai-mcp-bff-api-r1 — Task 010 (2026-11-14): MCP caller-path verification (spec FR-10 as amended 2026-11-07, owner option C).
//
// WHAT THIS PROVES: an MCP tool on /mcp/legal-operations runs INSIDE the ASP.NET request, so the unchanged IHttpContextAccessor path
// (DataverseUserClient, CallerWhoAmIMemo, the decision services) serves an MCP caller exactly as it serves a REST caller:
//   test 1 — inside a tool, IHttpContextAccessor.HttpContext is the request's own instance and the tool's IServiceProvider is that
//            request's RequestServices;
//            and with two callers' requests in flight at once, each tool call sees its own caller (oid), its own HttpContext and its own
//            RequestServices, never the other request's;
//   test 2 — the REAL DataverseUserClient, called from a tool over the MCP route, asks WhoAmI as the caller (OBO from the MCP request's
//            own bearer token) and remembers the answer for that request only; with no bearer token, with no HttpContext, or with a
//            caller whose token the OBO cache does not hold, it fails closed before any Dataverse call is sent;
//   test 5 — the transport is pinned: Stateless, PerSessionExecutionContext false, on the test host and on the real BFF host, whose
//            production DI also binds every caller-path parameter of the inferred-schema legal-operations tools as a service, never a
//            tool argument (the two static-schema tools are an explicit schema pin only).
// Tests 3 and 4 (the commit) are in tests/integration/data-mutation/Mcp/McpCommitCallerPathTests.cs.
//
// WHAT RUNS: the PRODUCTION AddMcpModule / MapMcpEndpoints / RateLimitingModule and the A10 token filter on a real Kestrel listener
// (McpCallerPathHost below, the task-020 host shape; NOT the BFF Program, except test 5's second case), the REAL DataverseUserClient and OrderedCredentialClientProvider (MSAL). Only the
// wire is replaced: Dataverse by a hand-written HttpMessageHandler, the OBO exchange by an MSAL token cache seeded with one access token
// for the hash of the MCP request's own bearer token (MSAL's own OBO cache lookup, so a different caller token misses), and the BFF's
// managed-identity assertion by a counting stub; the MSAL app-only (client-credentials) cache holds a distinct app token, so an app-only fallback would
// succeed offline and show on the wire. The wire records every header and the body; the host captures logs at Trace (test 2 asserts nothing of the
// caller is logged or forwarded). Tokens are opaque test strings (ADR-028).

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Tests.Api.Mcp;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Mcp;

public sealed class McpCallerPathTests
{
    private static readonly Guid CallerOid = Guid.Parse("01000000-0000-4000-8000-000000000001");
    private static readonly Guid CallerSystemUserId = Guid.Parse("01000000-0000-4000-8000-000000000002");
    private static readonly Guid SecondCallerOid = Guid.Parse("01000000-0000-4000-8000-0000000000a2");
    private const string DataverseUrl = "https://caller-path-010.crm.dynamics.com";

    /// <summary>The Dataverse access token the seeded MSAL cache holds for the caller's assertion: an opaque test string.</summary>
    private const string CallerDataverseToken = "test-dataverse-token-for-caller-010";

    /// <summary>The Dataverse access token the seeded MSAL APP cache (client credentials) holds: what an app-only fallback would send. Opaque test string.</summary>
    private const string AppOnlyDataverseToken = "test-APP-ONLY-dataverse-token-010";

    // ---- test 1: the tool runs inside the request ---------------------------------------------------------------------------

    [Fact]
    public async Task Test1_InsideATool_TheAccessorHoldsTheRequestsOwnHttpContext_AndTheToolsProviderIsItsRequestServices()
    {
        await using var host = await McpCallerPathHost.StartAsync([typeof(CallerPathProbeTool)]);

        var result = await host.CallToolAsync(CallerPathProbeTool.ToolName, new { }, McpTestHost.MintToken(CallerOid));

        (result.TryGetProperty("isError", out var isError) && isError.GetBoolean()).Should().BeFalse(result.GetRawText());
        var seen = host.Recorder;
        seen.ToolCalls.Should().Be(1);
        seen.MiddlewareContext.Should().NotBeNull();
        seen.ToolAccessorContext.Should().NotBeNull("FR-10 option C: the tool reads the caller from IHttpContextAccessor; null would fail every MCP call closed");
        ReferenceEquals(seen.ToolAccessorContext, seen.MiddlewareContext).Should().BeTrue(
            "the accessor holds the very HttpContext the request pipeline created, not another instance");
        seen.ToolTraceIdentifier.Should().Be(seen.MiddlewareTraceIdentifier, "and it is that request's context while the tool runs");
        seen.ToolServices.Should().NotBeNull();
        ReferenceEquals(seen.ToolServices, seen.MiddlewareRequestServices).Should().BeTrue(
            "the SDK resolves a tool's services from the request's own RequestServices (ScopeRequests off), so scoped services are the request's");
        ReferenceEquals(seen.ToolAccessorRequestServices, seen.MiddlewareRequestServices).Should().BeTrue();
        seen.ToolUserOid.Should().Be(CallerOid.ToString("D"), "the context carries the authenticated MCP caller");
    }

    [Fact]
    public async Task Test1_TwoCallersInFlightAtOnce_EachToolCallSeesItsOwnCallerItsOwnHttpContextAndItsOwnRequestServices()
    {
        await using var host = await McpCallerPathHost.StartAsync([typeof(CallerPathProbeTool)]);
        host.Recorder.RequireOverlap(callers: 2); // each tool call waits (inside the tool) until the other caller's call is also running
        var markerA = Guid.NewGuid().ToString("N");
        var markerB = Guid.NewGuid().ToString("N");

        var results = await Task.WhenAll(
            host.CallToolAsync(CallerPathProbeTool.ToolName, new { marker = markerA }, McpTestHost.MintToken(CallerOid), markerA),
            host.CallToolAsync(CallerPathProbeTool.ToolName, new { marker = markerB }, McpTestHost.MintToken(SecondCallerOid), markerB));

        AssertCallSawItsOwnRequest(host.Recorder, results[0], markerA, CallerOid);
        AssertCallSawItsOwnRequest(host.Recorder, results[1], markerB, SecondCallerOid);
        ReferenceEquals(host.Recorder.RequestsByMarker[markerA].Context, host.Recorder.RequestsByMarker[markerB].Context).Should().BeFalse(
            "two requests in flight at once are two HttpContexts");
    }

    private static void AssertCallSawItsOwnRequest(CallerPathRecorder recorder, JsonElement result, string marker, Guid oid)
    {
        (result.TryGetProperty("isError", out var isError) && isError.GetBoolean()).Should().BeFalse(result.GetRawText());
        var seen = result.GetProperty("structuredContent");
        seen.GetProperty("oid").GetString().Should().Be(oid.ToString("D"), "the tool's own result carries the caller of ITS request, not the other caller");
        seen.GetProperty("headerMarker").GetString().Should().Be(marker, "the accessor's context carried the request header of the request that made this call");
        seen.GetProperty("toolServicesAreRequestServices").GetBoolean().Should().BeTrue(
            "the tool's IServiceProvider is the RequestServices of the HttpContext the accessor holds");
        var request = recorder.RequestsByMarker[marker];
        var tool = recorder.ToolsByMarker[marker];
        ReferenceEquals(tool.Context, request.Context).Should().BeTrue("the accessor held this request's own HttpContext instance");
        ReferenceEquals(tool.Services, request.RequestServices).Should().BeTrue("the tool's provider is this request's own RequestServices");
    }

    // ---- test 2: the real DataverseUserClient over the MCP route ------------------------------------------------------------

    [Fact]
    public async Task Test2_TheRealDataverseUserClient_InATool_AsksWhoAmIAsTheCaller_OncePerMcpRequest()
    {
        var token = McpTestHost.MintToken(CallerOid);
        var rig = DataverseRig.Create(seedTokenFor: token);
        await using var host = await McpCallerPathHost.StartAsync([typeof(DataverseWhoAmIProbeTool)], rig.Register);
        await rig.SeedCachesAsync(host.Services);
        var mintsBefore = rig.Assertions.Mints;

        var first = await host.CallToolAsync(DataverseWhoAmIProbeTool.ToolName, new { }, token);

        (first.TryGetProperty("isError", out var isError) && isError.GetBoolean()).Should().BeFalse(first.GetRawText());
        var answer = first.GetProperty("structuredContent");
        answer.GetProperty("userId").GetString().Should().Be(CallerSystemUserId.ToString("D"), "WhoAmI answered for the caller");
        answer.GetProperty("asked").GetInt32().Should().Be(2, "the tool asked twice");
        rig.Wire.Requests.Should().ContainSingle("the second WhoAmI in the same MCP request is answered from CallerWhoAmIMemo");
        var request = rig.Wire.Requests.Single();
        (request.Method, request.Path).Should().Be(("GET", "/api/data/v9.2/WhoAmI()"));
        request.Authorization.Should().Be("Bearer " + CallerDataverseToken,
            "the Dataverse call carries the OBO token MSAL holds for THIS MCP request's bearer token (the seed is keyed by its hash)");
        rig.Assertions.Mints.Should().Be(mintsBefore, "the exchange was MSAL's OBO cache hit: no token request left the process");

        await host.CallToolAsync(DataverseWhoAmIProbeTool.ToolName, new { }, token);

        rig.Wire.Requests.Should().HaveCount(2, "a new MCP request is a new HttpContext: the memo never carries an answer across requests");

        AssertNothingLeaked(host, rig, token);
    }

    /// <summary>
    /// FR-02 check 4: the caller's MCP bearer token and oid are not logged and not forwarded; the OBO (Dataverse) access token is not logged.
    /// Each search string is proven live first (it really reached the process), so an empty result means absence, not a dead search.
    /// </summary>
    private static void AssertNothingLeaked(McpCallerPathHost host, DataverseRig rig, string bearer)
    {
        var oid = CallerOid.ToString("D");

        // Live strings. The bearer reached the client: the OBO cache is keyed by the hash of THIS bearer, and the Dataverse call carries the
        // token that entry holds (so the Dataverse token is real on the wire too). The oid is on the request's principal inside the tool.
        rig.Wire.Requests.Should().OnlyContain(r => r.Authorization == "Bearer " + CallerDataverseToken, "the Dataverse token is live: it is what the wire carried");
        host.Recorder.ToolUserOid.Should().Be(oid, "the oid is live: the tool's request carried this caller");
        var bearerParts = bearer.Split('.');
        bearerParts.Should().HaveCount(3, "the bearer is a JWT: its payload and signature are searched separately");
        string[] callerSecrets = [bearer, bearerParts[1], bearerParts[2], oid];

        // The logs are live: Trace is captured (the memo's Debug line is there) from both real clients.
        var logs = host.Logs.Entries.ToList();
        logs.Should().Contain(e => e.Category.EndsWith(nameof(DataverseUserClient), StringComparison.Ordinal) && e.Message.Contains("memo", StringComparison.Ordinal),
            "the DataverseUserClient's Debug line is captured, so a Debug log of the assertion would be too");
        logs.Should().Contain(e => e.Category.EndsWith(nameof(OrderedCredentialClientProvider), StringComparison.Ordinal),
            "the credential provider's own log is captured");
        var logText = logs.SelectMany(e => new[] { e.Message }.Concat(e.State.Select(s => s.Key + "=" + s.Value))).ToList();

        foreach (var secret in callerSecrets)
        {
            logText.Where(t => t.Contains(secret, StringComparison.Ordinal)).Should().BeEmpty("nothing of the caller's MCP bearer or oid is logged");
            rig.Wire.Requests.Should().OnlyContain(
                r => r.Everything.All(part => !part.Contains(secret, StringComparison.Ordinal)),
                "no outbound header, path or body forwards the caller's bearer or oid (Dataverse gets its own OBO token)");
        }

        logText.Where(t => t.Contains(CallerDataverseToken, StringComparison.Ordinal)).Should().BeEmpty("the OBO access token is not logged");
    }

    [Fact]
    public async Task Test2_Negative_AnAuthenticatedMcpCallerWithNoBearerToken_IsUserContextRequired_AndNothingIsSentOrMinted()
    {
        var rig = DataverseRig.Create(seedTokenFor: null); // the app-only cache is seeded all the same: an app-only fallback would succeed and reach the wire
        await using var host = await McpCallerPathHost.StartAsync([typeof(DataverseWhoAmIProbeTool)], rig.Register, headerlessCaller: true);
        await rig.SeedCachesAsync(host.Services);
        var mintsBefore = rig.Assertions.Mints; // selecting the credential for the cached client mints once; the tool call must add none

        var result = await host.CallToolAsync(DataverseWhoAmIProbeTool.ToolName, new { }, token: null);

        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        result.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        host.Recorder.ToolUserOid.Should().Be(CallerOid.ToString("D"), "the A10 filter admitted an authenticated caller: only the bearer token was missing");
        rig.Wire.Requests.Should().BeEmpty("no Dataverse call is sent without the caller's token, and none with the seeded app-only token: there is no app-only fallback");
        rig.Assertions.Mints.Should().Be(mintsBefore, "the BFF's own credential is never used");
    }

    [Fact]
    public async Task Test2_Negative_TheClientWithNoHttpContext_IsUserContextRequired_AndNothingIsSentOrMinted()
    {
        var rig = DataverseRig.Create(seedTokenFor: null);
        await using var host = await McpCallerPathHost.StartAsync([typeof(DataverseWhoAmIProbeTool)], rig.Register);
        await rig.SeedCachesAsync(host.Services);
        var mintsBefore = rig.Assertions.Mints; // selecting the credential for the cached client mints once; the call must add none

        // Outside any request (this test's own flow): the accessor is null.
        host.Services.GetRequiredService<IHttpContextAccessor>().HttpContext.Should().BeNull();
        var response = await host.Services.GetRequiredService<IDataverseUserClient>().GetAsync("WhoAmI()", CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.ErrorCode.Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        rig.Wire.Requests.Should().BeEmpty("no call is sent, with the caller's token or the seeded app-only one: there is no app-only fallback");
        rig.Assertions.Mints.Should().Be(mintsBefore, "no credential is used");
    }

    [Fact]
    public async Task Test2_Negative_ASecondCallerWhoseTokenTheOboCacheDoesNotHold_FailsClosed_AndNothingIsSentToDataverse()
    {
        var tokenA = McpTestHost.MintToken(CallerOid);
        var tokenB = McpTestHost.MintToken(SecondCallerOid);
        // The OBO cache holds caller A's exchange only. The app-only (client-credentials) cache holds a distinct app token, so an app-only
        // fallback after B's failed exchange WOULD succeed offline and show up on the wire: the empty-wire check below can fire.
        var rig = DataverseRig.Create(seedTokenFor: tokenA);
        await using var host = await McpCallerPathHost.StartAsync([typeof(DataverseWhoAmIProbeTool)], rig.Register);
        await rig.SeedCachesAsync(host.Services);
        var mintsBefore = rig.Assertions.Mints;
        rig.Assertions.FailEveryMintFromNow(); // caller B's cache miss must not reach Entra: any mint attempt is refused offline

        var b = await host.CallToolAsync(DataverseWhoAmIProbeTool.ToolName, new { }, tokenB);

        // The wire first: it is the invariant (no Dataverse call without an OBO token), whatever else a regression would also change.
        rig.Wire.Requests.Should().BeEmpty("no Dataverse call is sent: not with caller A's token, and not with the seeded app-only token (no app-only fallback)");
        rig.Assertions.Mints.Should().Be(mintsBefore + 1, "B's lookup missed the cache and tried exactly one exchange; that attempt was refused");
        (b.TryGetProperty("isError", out var bIsError) && bIsError.GetBoolean()).Should().BeTrue(
            "caller B has no OBO exchange: MSAL's cache is keyed by the assertion, not served from A's entry; " + b.GetRawText());
        b.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be(DataverseUserClientErrorCodes.OboExchangeFailed);

        var a = await host.CallToolAsync(DataverseWhoAmIProbeTool.ToolName, new { }, tokenA);

        (a.TryGetProperty("isError", out var isError) && isError.GetBoolean()).Should().BeFalse(a.GetRawText());
        rig.Wire.Requests.Should().ContainSingle("only caller A's own call reached Dataverse").Which.Authorization.Should().Be("Bearer " + CallerDataverseToken);
    }

    // ---- test 5: the transport pin ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Test5_TheMcpTransport_IsStateless_AndTheExecutionContextFlagStaysOff()
    {
        await using var host = await McpCallerPathHost.StartAsync([typeof(CallerPathProbeTool)]);

        var transport = host.Services.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;

        transport.Stateless.Should().BeTrue(
            "this is the real guard (ADR-001: no session store, no affinity): in stateless mode every request carries its own short-lived server " +
            "context, so a tool runs on the request's own execution context and IHttpContextAccessor.HttpContext is the request's");
        transport.SessionMode.Should().Be(HttpServerSessionMode.Stateless);
#pragma warning disable MCP9006 // Pinning the value is this test's purpose (FR-10); the SDK marks the setting obsolete, not the read.
        transport.PerSessionExecutionContext.Should().BeFalse(
            "inert today: SDK 2.3.1 reads the flag only in its stateful branch, so in stateless mode test 1 passes with it on (task-010 notes); " +
            "pinned only so a future switch to a stateful transport does not silently put tools on the session's execution context, " +
            "where the SDK documents IHttpContextAccessor as unsupported (FR-10)");
#pragma warning restore MCP9006
    }

    [Fact]
    public void Test5_TheRealBffHost_PinsTheTransport_AndBindsEveryCallerPathParameterOfTheInferredSchemaToolsAsAService()
    {
        using var factory = RealBffHost();

        var transport = factory.Services.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;
        transport.Stateless.Should().BeTrue();
#pragma warning disable MCP9006 // Pinning the value is this test's purpose (FR-10); the SDK marks the setting obsolete, not the read.
        transport.PerSessionExecutionContext.Should().BeFalse();
#pragma warning restore MCP9006

        // The production assembly scan over the production DI: a caller-path parameter (IHttpContextAccessor, IDecisionCapability, ...)
        // that the real host did not register would be published as a tool ARGUMENT, so the caller path would not be the request's.
        // This only holds for tools whose input schema the SDK INFERS from the handler's parameters (the three below); the two other
        // legal-operations tools state a static schema (see the pin test next to this one), which says nothing about DI binding.
        var arguments = RealHostToolArguments(factory);

        arguments.Where(t => InferredSchemaTools.Contains(t.Key)).ToDictionary(t => t.Key, t => t.Value).Should().BeEquivalentTo(new Dictionary<string, string[]>
        {
            ["spaarke_get_decision_plan"] = ["signalId"],
            ["spaarke_search_decision_lookup"] = ["parameterCode", "query", "signalId"],
            ["spaarke_complete_decision"] = ["review", "signalId"],
        }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Test5_TheRealBffHost_TheTwoStaticSchemaTools_PublishExactlyTheirPinnedArguments_ASchemaPinNotAnInjectionProof()
    {
        // ShowDecisionsTool and ListDecisionPageTool overwrite ProtocolTool.InputSchema with a static schema, so these rows would stay
        // green even if one of their services were not registered. They are pinned as the published contract (FR-03: the closed category
        // enum, the cursor), nothing more.
        using var factory = RealBffHost();

        var arguments = RealHostToolArguments(factory);

        arguments.Where(t => !InferredSchemaTools.Contains(t.Key)).ToDictionary(t => t.Key, t => t.Value).Should().BeEquivalentTo(new Dictionary<string, string[]>
        {
            ["spaarke_show_decisions"] = ["category"],
            ["spaarke_list_decision_page"] = ["cursor"],
        }, options => options.WithStrictOrdering());
    }

    private static readonly HashSet<string> InferredSchemaTools = ["spaarke_get_decision_plan", "spaarke_search_decision_lookup", "spaarke_complete_decision"];

    private static WebApplicationFactory<Program> RealBffHost() =>
        new CustomWebAppFactory().WithWebHostBuilder(builder => builder.UseSetting("Mcp:Enabled", "true"));

    private static Dictionary<string, string[]> RealHostToolArguments(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<McpToolCatalog>().ToolsFor(McpEndpoints.LegalOperationsArea)
            .ToDictionary(
                t => t.ProtocolTool.Name,
                t => t.ProtocolTool.InputSchema.TryGetProperty("properties", out var properties)
                    ? properties.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray()
                    : []);

    // ---- the Dataverse side of test 2 ---------------------------------------------------------------------------------------

    /// <summary>The REAL <see cref="DataverseUserClient"/> and <see cref="OrderedCredentialClientProvider"/> with only the wire replaced.</summary>
    private sealed class DataverseRig
    {
        private readonly string? _seedTokenFor;

        private DataverseRig(string? seedTokenFor) => _seedTokenFor = seedTokenFor;

        public CountingAssertions Assertions { get; } = new();

        public DataverseWire Wire { get; } = new();

        /// <param name="seedTokenFor">The MCP bearer token whose OBO exchange MSAL answers from its cache; null seeds no OBO entry.
        /// The app-only (client-credentials) cache is always seeded with <see cref="AppOnlyDataverseToken"/>.</param>
        public static DataverseRig Create(string? seedTokenFor) => new(seedTokenFor);

        /// <summary>Registers the REAL credential provider and the REAL <see cref="DataverseUserClient"/>, both with the host's own loggers (so the host's capture sees them).</summary>
        public void Register(IServiceCollection services)
        {
            services.AddSingleton(sp => new OrderedCredentialClientProvider(
                Options.Create(new CredentialSelectionOptions { Order = new List<string> { nameof(CredentialKind.ManagedIdentityFederated) } }),
                new ConfigurationBuilder().Build(),
                sp.GetRequiredService<ILogger<OrderedCredentialClientProvider>>(),
                Assertions));
            services.AddSingleton<IDataverseUserClient>(sp => new DataverseUserClient(
                new HttpClient(Wire),
                Options.Create(new DataverseOptions { EnvironmentUrl = DataverseUrl }),
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<IHttpContextAccessor>(),
                sp.GetRequiredService<ILogger<DataverseUserClient>>(),
                sp.GetRequiredService<OrderedCredentialClientProvider>()));
        }

        /// <summary>Seeds MSAL's caches on the SAME client instance DataverseUserClient will get (the provider caches it per tenant, client and credential).</summary>
        public async Task SeedCachesAsync(IServiceProvider services)
        {
            var client = await services.GetRequiredService<OrderedCredentialClientProvider>().GetClientAsync(McpTestHost.TenantId, McpTestHost.BffClientId);
            SeedAppOnlyAccessToken(client);
            if (_seedTokenFor is not null)
            {
                SeedOboAccessToken(client, _seedTokenFor);
            }
        }

        /// <summary>
        /// One app-only access token (client credentials, no user) in MSAL's APP token cache for the Dataverse scope. Nothing in the production
        /// path asks for it; if a regression added an app-only fallback (AcquireTokenForClient), it would succeed offline from here and the
        /// request would appear on the wire carrying <see cref="AppOnlyDataverseToken"/>.
        /// </summary>
        private static void SeedAppOnlyAccessToken(IConfidentialClientApplication client)
        {
            // Same clock rule as the OBO seed below.
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expires = (now + 3600).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var cache = new JsonObject
            {
                ["AccessToken"] = new JsonObject
                {
                    ["seed-app"] = new JsonObject
                    {
                        ["home_account_id"] = string.Empty,
                        ["environment"] = "login.microsoftonline.com",
                        ["client_id"] = McpTestHost.BffClientId,
                        ["credential_type"] = "AccessToken",
                        ["realm"] = McpTestHost.TenantId,
                        ["target"] = $"{DataverseUrl}/.default",
                        ["secret"] = AppOnlyDataverseToken,
                        ["cached_at"] = now.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["expires_on"] = expires,
                        ["extended_expires_on"] = expires,
                        ["token_type"] = "Bearer",
                    },
                },
                ["Account"] = new JsonObject(),
                ["IdToken"] = new JsonObject(),
                ["RefreshToken"] = new JsonObject(),
                ["AppMetadata"] = new JsonObject(),
            };
            var serialized = Encoding.UTF8.GetBytes(cache.ToJsonString());
            var seeded = 0;
            client.AppTokenCache.SetBeforeAccess(args =>
            {
                if (Interlocked.Exchange(ref seeded, 1) == 0)
                {
                    args.TokenCache.DeserializeMsalV3(serialized, shouldClearExistingCache: false);
                }
            });
        }

        /// <summary>
        /// One OBO access token in MSAL's user token cache, keyed by the hash of <paramref name="userAssertion"/>: the entry MSAL itself
        /// writes after an OBO exchange (the shared MSAL cache schema). AcquireTokenOnBehalfOf for that assertion is then a cache hit, and
        /// for any other assertion a miss.
        /// </summary>
        private static void SeedOboAccessToken(IConfidentialClientApplication client, string userAssertion)
        {
            // MSAL judges the entry against the real system clock and rejects fixed far-future or past values here (tried: expires_on 2038 and 2100, cached_at 2026 and 2099: OBO exchange failed), so this one read of the system clock stays.
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expires = (now + 3600).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var cachedAt = now.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var home = $"{CallerOid:D}.{McpTestHost.TenantId}";
            var cache = new JsonObject
            {
                ["AccessToken"] = new JsonObject
                {
                    ["seed"] = new JsonObject
                    {
                        ["home_account_id"] = home,
                        ["environment"] = "login.microsoftonline.com",
                        ["client_id"] = McpTestHost.BffClientId,
                        ["credential_type"] = "AccessToken",
                        ["realm"] = McpTestHost.TenantId,
                        ["target"] = $"{DataverseUrl}/.default",
                        ["secret"] = CallerDataverseToken,
                        ["cached_at"] = cachedAt,
                        ["expires_on"] = expires,
                        ["extended_expires_on"] = expires,
                        ["token_type"] = "Bearer",
                        // MSAL's OBO cache key: base64url(SHA-256(assertion)), what MSAL computes for the incoming assertion.
                        ["user_assertion_hash"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(userAssertion))),
                    },
                },
                ["Account"] = new JsonObject(),
                ["IdToken"] = new JsonObject(),
                ["RefreshToken"] = new JsonObject(),
                ["AppMetadata"] = new JsonObject(),
            };
            var serialized = Encoding.UTF8.GetBytes(cache.ToJsonString());
            var seeded = 0;
            client.UserTokenCache.SetBeforeAccess(args =>
            {
                if (Interlocked.Exchange(ref seeded, 1) == 0)
                {
                    args.TokenCache.DeserializeMsalV3(serialized, shouldClearExistingCache: false);
                }
            });
        }
    }

    /// <summary>The BFF's managed-identity assertion, counted; an opaque test string, never a real credential.</summary>
    private sealed class CountingAssertions : IClientAssertionProvider
    {
        private int _mints;
        private int _refuse;

        public int Mints => Volatile.Read(ref _mints);

        /// <summary>From now on every mint is counted and then refused, so an OBO cache miss stays offline (it would otherwise go to Entra).</summary>
        public void FailEveryMintFromNow() => Volatile.Write(ref _refuse, 1);

        public Task<string> GetAssertionAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _mints);
            return Volatile.Read(ref _refuse) == 1
                ? throw new MsalClientException("test_mint_refused", "Test: the assertion provider refuses every mint, so no token request can leave the process.")
                : Task.FromResult("test-client-assertion-010");
        }
    }

    /// <summary>One outbound Dataverse request as it left the process: method, path, Authorization, EVERY header (request and content) and the body.</summary>
    private sealed record WireRequest(string Method, string Path, string? Authorization, IReadOnlyList<KeyValuePair<string, string>> Headers, string Body)
    {
        /// <summary>Every place a value could ride out: the path (query included), each header as "name: value", and the body.</summary>
        public IEnumerable<string> Everything => Headers.Select(h => h.Key + ": " + h.Value).Append(Path).Append(Body);
    }

    /// <summary>Dataverse's Web API on the wire (hand-written; ADR-038 bans a mocked handler): records each request in full, answers WhoAmI.</summary>
    private sealed class DataverseWire : HttpMessageHandler
    {
        public ConcurrentQueue<WireRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .Select(h => new KeyValuePair<string, string>(h.Key, string.Join(",", h.Value)))
                .ToList();
            var requestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Enqueue(new WireRequest(request.Method.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString(), headers, requestBody));
            if (request.RequestUri.AbsolutePath == "/api/data/v9.2/WhoAmI()")
            {
                var body = JsonSerializer.Serialize(new
                {
                    BusinessUnitId = "01000000-0000-4000-8000-0000000000b1",
                    UserId = CallerSystemUserId.ToString("D"),
                    OrganizationId = "01000000-0000-4000-8000-0000000000c1",
                });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}

/// <summary>
/// TESTS ONLY (task 010). The task-020 host shape: the PRODUCTION <c>AddMcpModule</c>, <c>MapMcpEndpoints</c> (A10 token filter, rate limit,
/// kill switch) and <c>RateLimitingModule</c> on a real Kestrel listener behind a JwtBearer scheme that accepts <see cref="McpTestHost.MintToken"/>,
/// serving exactly <c>tools</c>. A first middleware records each request's HttpContext, TraceIdentifier and RequestServices
/// (<see cref="CallerPathRecorder"/>). <c>headerlessCaller</c> swaps the default scheme for one that admits an A10-shaped caller from no
/// Authorization header at all: an authenticated principal whose request carries no bearer token.
/// </summary>
internal sealed class McpCallerPathHost : IAsyncDisposable
{
    private const string Issuer = "https://login.microsoftonline.com/" + McpTestHost.TenantId + "/v2.0";
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("mcp-contract-test-signing-key-0123456789-0123456789"));

    private readonly WebApplication _app;
    private readonly HttpClient _client;

    private McpCallerPathHost(WebApplication app, HttpClient client, CallerPathRecorder recorder, DecisionToolsTestHost.CapturingLoggerProvider logs)
    {
        _app = app;
        _client = client;
        Recorder = recorder;
        Logs = logs;
    }

    public CallerPathRecorder Recorder { get; }

    /// <summary>Every log entry the host's logging wrote, at Trace (the shared capture of <see cref="DecisionToolsTestHost"/>).</summary>
    public DecisionToolsTestHost.CapturingLoggerProvider Logs { get; }

    public IServiceProvider Services => _app.Services;

    public static async Task<McpCallerPathHost> StartAsync(
        IEnumerable<Type> tools, Action<IServiceCollection>? services = null, bool headerlessCaller = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        var logs = new DecisionToolsTestHost.CapturingLoggerProvider();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
        builder.Logging.AddProvider(logs);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:Enabled"] = "true",
            ["AzureAd:TenantId"] = McpTestHost.TenantId,
            ["AzureAd:ClientId"] = McpTestHost.BffClientId,
        });

        var recorder = new CallerPathRecorder();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(recorder);
        builder.Services.AddSingleton(TimeProvider.System);
        var authentication = builder.Services.AddAuthentication(headerlessCaller ? HeaderlessCallerHandler.SchemeName : JwtBearerDefaults.AuthenticationScheme);
        authentication.AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = McpTestHost.BffClientId,
            IssuerSigningKey = SigningKey,
        });
        if (headerlessCaller)
        {
            authentication.AddScheme<AuthenticationSchemeOptions, HeaderlessCallerHandler>(HeaderlessCallerHandler.SchemeName, _ => { });
        }

        builder.Services.AddAuthorization();
        builder.Services.AddRateLimitingModule();
        services?.Invoke(builder.Services);
        builder.Services.AddMcpModule(builder.Configuration, tools);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            recorder.MiddlewareContext = context;
            recorder.MiddlewareTraceIdentifier = context.TraceIdentifier;
            recorder.MiddlewareRequestServices = context.RequestServices; // this request's scope: already set when the first middleware runs
            recorder.RecordRequest(context);
            await next(context);
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapMcpEndpoints();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new McpCallerPathHost(app, new HttpClient { BaseAddress = new Uri(address) }, recorder, logs);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>One <c>tools/call</c> on <c>/mcp/legal-operations</c>; returns the JSON-RPC <c>result</c> (an isError result included).</summary>
    /// <param name="token">The bearer token; null sends no Authorization header.</param>
    /// <param name="marker">When set, sent as the <see cref="CallerPathRecorder.MarkerHeader"/> request header, so the middleware and the tool can each record this request under it.</param>
    public Task<JsonElement> CallToolAsync(string tool, object arguments, string? token, string? marker = null) =>
        RpcAsync("tools/call", new { name = tool, arguments }, token, marker);

    public async Task<JsonElement> RpcAsync(string method, object parameters, string? token, string? marker = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.LegalOperationsRoute)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (marker is not null)
        {
            request.Headers.Add(CallerPathRecorder.MarkerHeader, marker);
        }

        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)response.StatusCode}: {body}");
        }

        var json = body.TrimStart().StartsWith('{')
            ? body
            : string.Join("\n", body.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].TrimStart()));
        using var doc = JsonDocument.Parse(json.Split('\n').First(l => l.Contains("\"result\"") || l.Contains("\"error\"")));
        if (doc.RootElement.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException($"JSON-RPC error: {error}");
        }

        return doc.RootElement.GetProperty("result").Clone();
    }

    /// <summary>
    /// Authenticates every request as an ADR-028 A10 caller (Spaarke tenant, exact v2 issuer, BFF audience, user_impersonation, an oid)
    /// WITHOUT reading any header, so the request reaches the tool authenticated but with no bearer token.
    /// </summary>
    private sealed class HeaderlessCallerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "HeaderlessA10Caller";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim("tid", McpTestHost.TenantId),
                    new Claim("iss", Issuer),
                    new Claim("aud", McpTestHost.BffClientId),
                    new Claim("scp", "user_impersonation"),
                    new Claim("oid", "01000000-0000-4000-8000-000000000001"),
                ],
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}

/// <summary>What the request pipeline and the probe tools saw in one <see cref="McpCallerPathHost"/> (the last request).</summary>
internal sealed class CallerPathRecorder
{
    private int _toolCalls;

    public HttpContext? MiddlewareContext { get; set; }

    public string? MiddlewareTraceIdentifier { get; set; }

    public IServiceProvider? MiddlewareRequestServices { get; set; }

    public int ToolCalls => Volatile.Read(ref _toolCalls);

    public HttpContext? ToolAccessorContext { get; private set; }

    public string? ToolTraceIdentifier { get; private set; }

    public IServiceProvider? ToolServices { get; private set; }

    public IServiceProvider? ToolAccessorRequestServices { get; private set; }

    public string? ToolUserOid { get; private set; }

    public void RecordTool(IHttpContextAccessor accessor, IServiceProvider services)
    {
        Interlocked.Increment(ref _toolCalls);
        var http = accessor.HttpContext;
        ToolAccessorContext = http;
        ToolTraceIdentifier = http?.TraceIdentifier;
        ToolServices = services;
        ToolAccessorRequestServices = http?.RequestServices;
        ToolUserOid = OidOf(http);
    }

    // ---- several callers at once: what each request and each tool call saw, keyed by the request's marker -------------------------

    /// <summary>Request header a test sets (via <c>CallToolAsync(..., marker)</c>) to tell its concurrent requests apart.</summary>
    public const string MarkerHeader = "X-Probe-Marker";

    /// <summary>What the first middleware saw of each marked request: its HttpContext and RequestServices.</summary>
    public ConcurrentDictionary<string, (HttpContext Context, IServiceProvider RequestServices)> RequestsByMarker { get; } = new();

    /// <summary>What each marked tool call saw: the accessor's HttpContext and the tool's own IServiceProvider (keyed by the call's own <c>marker</c> ARGUMENT).</summary>
    public ConcurrentDictionary<string, (HttpContext? Context, IServiceProvider Services)> ToolsByMarker { get; } = new();

    private TaskCompletionSource? _overlap;
    private int _overlapCallers;
    private int _overlapArrived;

    public static string? OidOf(HttpContext? http) =>
        http?.User.FindFirst("oid")?.Value ?? http?.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

    public void RecordRequest(HttpContext context)
    {
        var marker = context.Request.Headers[MarkerHeader].ToString();
        if (marker.Length > 0)
        {
            RequestsByMarker[marker] = (context, context.RequestServices);
        }
    }

    /// <summary>Makes each probe tool call wait, inside the tool, until <paramref name="callers"/> calls are running at once.</summary>
    public void RequireOverlap(int callers)
    {
        _overlapCallers = callers;
        Volatile.Write(ref _overlapArrived, 0);
        _overlap = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>No-op unless <see cref="RequireOverlap"/> was set. Fails (rather than hangs) if the other caller's call never arrives.</summary>
    public async Task WaitForOverlapAsync(CancellationToken cancellationToken)
    {
        var gate = _overlap;
        if (gate is null)
        {
            return;
        }

        if (Interlocked.Increment(ref _overlapArrived) >= _overlapCallers)
        {
            gate.TrySetResult();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await gate.Task.WaitAsync(timeout.Token);
    }
}

/// <summary>
/// Probe tool (task 010, test 1): records <c>IHttpContextAccessor.HttpContext</c> and the <see cref="IServiceProvider"/> the SDK resolved
/// the call's services from. It looks its recorder up at run time, so a host that serves every tool of this assembly
/// (<see cref="McpProbeTools.Served"/>) gets a harmless no-op.
/// </summary>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class CallerPathProbeTool : McpToolDefinition
{
    public const string ToolName = "probe_caller_path";

    public override McpServerTool Create(IServiceProvider services) =>
        McpServerTool.Create(
            RunAsync,
            new McpServerToolCreateOptions { Services = services, Name = ToolName, Title = "Probe caller path", ReadOnly = true });

    private static async Task<CallToolResult> RunAsync(
        IHttpContextAccessor accessor, IServiceProvider services, CancellationToken cancellationToken, string? marker = null)
    {
        var recorder = services.GetService<CallerPathRecorder>();
        recorder?.RecordTool(accessor, services);
        if (recorder is not null)
        {
            await recorder.WaitForOverlapAsync(cancellationToken); // with two callers in flight, read the accessor again once the other call is running
        }

        // The tool states what IT saw, in its own result: the caller, the request header of the request the accessor holds, and whether its
        // provider is that request's RequestServices. A test compares these to the caller and marker the request was made with.
        var http = accessor.HttpContext;
        if (recorder is not null && marker is not null && http is not null)
        {
            recorder.ToolsByMarker[marker] = (http, services);
        }

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = "recorded" }],
            StructuredContent = JsonSerializer.SerializeToElement(new
            {
                oid = CallerPathRecorder.OidOf(http),
                headerMarker = http?.Request.Headers[CallerPathRecorder.MarkerHeader].ToString(),
                toolServicesAreRequestServices = http is not null && ReferenceEquals(http.RequestServices, services),
            }),
        };
    }
}

/// <summary>
/// Probe tool (task 010, test 2): asks the registered <see cref="IDataverseUserClient"/> <c>WhoAmI()</c> twice in one call, as a decision
/// tool's capability would, and returns the answer or the client's error code as an isError result.
/// </summary>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class DataverseWhoAmIProbeTool : McpToolDefinition
{
    public const string ToolName = "probe_dataverse_whoami";

    public override McpServerTool Create(IServiceProvider services) =>
        McpServerTool.Create(
            RunAsync,
            new McpServerToolCreateOptions { Services = services, Name = ToolName, Title = "Probe Dataverse WhoAmI", ReadOnly = true });

    private static async Task<CallToolResult> RunAsync(IHttpContextAccessor accessor, IServiceProvider services, CancellationToken cancellationToken)
    {
        services.GetService<CallerPathRecorder>()?.RecordTool(accessor, services);
        if (services.GetService<IDataverseUserClient>() is not { } client)
        {
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "no Dataverse client in this host" }] };
        }

        DataverseUserResponse? last = null;
        var asked = 0;
        for (var i = 0; i < 2; i++)
        {
            last = await client.GetAsync("WhoAmI()", cancellationToken);
            asked++;
            if (!last.IsSuccess)
            {
                break;
            }
        }

        return last!.IsSuccess
            ? new CallToolResult
            {
                Content = [new TextContentBlock { Text = "ok" }],
                StructuredContent = JsonSerializer.SerializeToElement(new { userId = last.Body!.Value.GetProperty("UserId").GetString(), asked }),
            }
            : new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = last.ErrorCode ?? "error" }],
                StructuredContent = JsonSerializer.SerializeToElement(new { reasonCode = last.ErrorCode }),
            };
    }
}

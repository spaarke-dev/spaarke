// CONTRACT — the MCP host at POST /mcp/{area} (spaarkeai-mcp-bff-api-r1 task 020, FR-01, NFR-04). KEEP path: endpoint-contract.
//
// WHAT RUNS: the production AddMcpModule / MapMcpEndpoints / RateLimitingModule on a real Kestrel listener (McpTestHost) behind a real
// JwtBearer scheme, with probe tools (McpTestHost.cs) in this assembly. The real BFF host (CustomWebAppFactory) is booted once per
// kill-switch state to prove the registration cannot abort startup and the route refuses an anonymous caller there too.
// NOT HERE: per-tool policies, A10 (CIAM / home-directory token) refusals and the tools/list exact set (tasks 021, 025).

using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Infrastructure.DI;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Mcp;

public sealed class McpHostContractTests
{
    private static readonly Guid CallerOne = Guid.Parse("66666666-7777-4888-9999-aaaaaaaaaaaa");
    private static readonly Guid CallerTwo = Guid.Parse("66666666-7777-4888-9999-bbbbbbbbbbbb");

    private static readonly object InitializeParams = new
    {
        protocolVersion = "2025-06-18",
        capabilities = new { },
        clientInfo = new { name = "mcp-contract-test", version = "0.0.1" },
    };

    // ---- enabled: the happy path and the area partition ------------------------------------------------------------

    [Fact]
    public async Task InitializeAndToolsList_WhenEnabledWithAValidToken_Succeed_AndListOnlyTheAreasTools()
    {
        await using var host = await McpTestHost.StartAsync(mcpEnabled: true);
        var token = McpTestHost.MintToken(CallerOne);

        var initialize = await host.RpcAsync(McpEndpoints.LegalOperationsRoute, "initialize", InitializeParams, token);
        var list = await host.RpcAsync(McpEndpoints.LegalOperationsRoute, "tools/list", new { }, token);

        initialize.GetProperty("serverInfo").ValueKind.Should().Be(JsonValueKind.Object);
        var names = list.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        names.Should().Contain(["probe_echo", "probe_http_context"], "the probes are tagged legal-operations");
        names.Should().NotContain("probe_other_area", "a tool tagged for another area is never served on this route");
        list.GetProperty("tools").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "probe_echo")
            .GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean()
            .Should().BeTrue("the SDK attribute's annotations reach tools/list");
    }

    [Fact]
    public async Task ToolsCall_WhenEnabled_RunsTheProbeToolOnce()
    {
        await using var host = await McpTestHost.StartAsync(mcpEnabled: true);

        var result = await host.RpcAsync(
            McpEndpoints.LegalOperationsRoute, "tools/call", new { name = "probe_echo", arguments = new { message = "hello" } }, McpTestHost.MintToken(CallerOne));

        result.GetProperty("content")[0].GetProperty("text").GetString().Should().Be("hello");
        host.Probe.EchoInvocations.Should().Be(1, "the positive control for the 'counter stays 0 when disabled' test");
    }

    [Fact]
    public async Task UnknownArea_WithAValidToken_Returns404()
    {
        await using var host = await McpTestHost.StartAsync(mcpEnabled: true);

        using var response = await host.PostRpcAsync("/mcp/unknown-area", "tools/list", new { }, McpTestHost.MintToken(CallerOne));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "v1 maps exactly /mcp/legal-operations");
    }

    // ---- kill switch (ADR-032) ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ToolsCall_WhenDisabled_Returns503ProblemDetails_AndNoToolRuns()
    {
        await using var host = await McpTestHost.StartAsync(mcpEnabled: false);

        using var response = await host.PostRpcAsync(
            McpEndpoints.LegalOperationsRoute, "tools/call", new { name = "probe_echo", arguments = new { message = "hello" } }, McpTestHost.MintToken(CallerOne));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("errorCode").GetString().Should().Be(NullMcpToolCatalog.ErrorCode);
        host.Probe.EchoInvocations.Should().Be(0, "a switched-off server runs no tool handler");
    }

    [Fact]
    public async Task AnonymousRequest_WhenDisabled_Returns401NotTheKillSwitchAnswer()
    {
        await using var host = await McpTestHost.StartAsync(mcpEnabled: false);

        using var response = await host.PostRpcAsync(McpEndpoints.LegalOperationsRoute, "tools/list", new { }, token: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the kill switch never bypasses or replaces authorization (ADR-032, ADR-018)");
    }

    // ---- auth, transport, limits --------------------------------------------------------------------------------------

    [Fact]
    public async Task AnonymousRequest_WhenEnabled_Returns401WithABearerChallenge()
    {
        await using var host = await McpTestHost.StartAsync(mcpEnabled: true);

        using var response = await host.PostRpcAsync(McpEndpoints.LegalOperationsRoute, "tools/list", new { }, token: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Select(h => h.Scheme).Should().Contain("Bearer");
    }

    [Fact]
    public async Task Request_WithoutTheStreamableHttpAcceptHeader_Returns406()
    {
        await using var host = await McpTestHost.StartAsync(mcpEnabled: true);

        using var response = await host.PostRpcAsync(
            McpEndpoints.LegalOperationsRoute, "tools/list", new { }, McpTestHost.MintToken(CallerOne), accept: false);

        response.StatusCode.Should().Be(HttpStatusCode.NotAcceptable, "the transport needs Accept: application/json, text/event-stream");
    }

    [Fact]
    public async Task Request_BodyOverTheLimit_Returns413_AndTheLimitIs256KiB()
    {
        await using var host = await McpTestHost.StartAsync(mcpEnabled: true);
        McpEndpoints.MaxRequestBodyBytes.Should().Be(256 * 1024);
        var oversized = new string('x', McpEndpoints.MaxRequestBodyBytes + 1);
        using var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.LegalOperationsRoute)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "probe_echo", arguments = new { message = oversized } } }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Authorization = new("Bearer", McpTestHost.MintToken(CallerOne));

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        host.Probe.EchoInvocations.Should().Be(0);
    }

    [Fact]
    public async Task Request_BodyJustUnderTheLimit_IsNotRefusedBySize()
    {
        await using var host = await McpTestHost.StartAsync(mcpEnabled: true);
        var envelope = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "probe_echo", arguments = new { message = "" } } });
        var message = new string('x', McpEndpoints.MaxRequestBodyBytes - Encoding.UTF8.GetByteCount(envelope) - 1024);

        var result = await host.RpcAsync(
            McpEndpoints.LegalOperationsRoute, "tools/call", new { name = "probe_echo", arguments = new { message } }, McpTestHost.MintToken(CallerOne));

        result.GetProperty("content")[0].GetProperty("text").GetString()!.Length.Should().Be(message.Length, "the negative control for the 413 test");
    }

    [Fact]
    public async Task Request_FromACallerOverTheMcpLimit_Returns429_WhileAnotherCallerIsNot()
    {
        await using var host = await McpTestHost.StartAsync(mcpEnabled: true);
        var flooder = McpTestHost.MintToken(CallerOne);
        var bystander = McpTestHost.MintToken(CallerTwo);

        var withinBudget = new List<HttpStatusCode>();
        for (var i = 0; i < RateLimitingModule.McpPermitLimit; i++)
        {
            using var ok = await host.PostRpcAsync(McpEndpoints.LegalOperationsRoute, "tools/list", new { }, flooder);
            withinBudget.Add(ok.StatusCode);
        }

        using var refused = await host.PostRpcAsync(McpEndpoints.LegalOperationsRoute, "tools/list", new { }, flooder);
        using var otherCaller = await host.PostRpcAsync(McpEndpoints.LegalOperationsRoute, "tools/list", new { }, bystander);

        withinBudget.Should().OnlyContain(s => s == HttpStatusCode.OK);
        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        refused.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        otherCaller.StatusCode.Should().Be(HttpStatusCode.OK, "the budget is per caller (oid), not shared");
    }

    [Fact]
    public async Task Route_DisablesResponseBufferingItself_EvenWhenTheTransportNeverRuns()
    {
        // A switched-off server never reaches the SDK, which (2.3.1) disables buffering on its own for a served request. So this
        // asserts the ROUTE's filter, not the SDK's behaviour: it still fires on the 503 path.
        await using var host = await McpTestHost.StartAsync(mcpEnabled: false);

        using var response = await host.PostRpcAsync(McpEndpoints.LegalOperationsRoute, "tools/list", new { }, McpTestHost.MintToken(CallerOne));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        host.Buffering.DisableBufferingCalled.Should().BeTrue("the transport may stream an SSE answer (design §11)");
    }

    [Fact]
    public async Task ToolsList_WhenEnabled_IsServedWithBufferingDisabled()
    {
        await using var host = await McpTestHost.StartAsync(mcpEnabled: true);

        await host.RpcAsync(McpEndpoints.LegalOperationsRoute, "tools/list", new { }, McpTestHost.MintToken(CallerOne));

        host.Buffering.DisableBufferingCalled.Should().BeTrue();
    }

    // ---- FR-10: one caller per call -----------------------------------------------------------------------------------

    [Fact]
    public async Task ToolHandsTheCapabilityTheRequestsOwnHttpContext_AcrossAccessorMiddlewareAndCapability()
    {
        await using var host = await McpTestHost.StartAsync(mcpEnabled: true);

        await host.RpcAsync(McpEndpoints.LegalOperationsRoute, "tools/call", new { name = "probe_http_context", arguments = new { } }, McpTestHost.MintToken(CallerOne));

        host.Capability.ReceivedContext.Should().NotBeNull();
        host.Probe.AccessorContextInTool.Should().NotBeNull();
        host.Probe.MiddlewareContext.Should().NotBeNull();
        ReferenceEquals(host.Capability.ReceivedContext, host.Probe.AccessorContextInTool).Should().BeTrue(
            "the context a tool hands IDecisionCapability is IHttpContextAccessor.HttpContext during the call");
        ReferenceEquals(host.Probe.AccessorContextInTool, host.Probe.MiddlewareContext).Should().BeTrue(
            "and it is the very context the request middleware saw: stateless mode does not substitute another");
    }

    // ---- the real BFF host: the registration does not abort startup in either state ------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealBffHost_AnonymousRequest_Returns401_WhateverTheKillSwitch(bool mcpEnabled)
    {
        using var factory = new CustomWebAppFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:Enabled"] = mcpEnabled ? "true" : "false" })));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.LegalOperationsRoute)
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

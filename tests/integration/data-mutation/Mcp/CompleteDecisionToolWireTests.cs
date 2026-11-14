using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Api.Mcp;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Mcp;

/// <summary>
/// spaarkeai-mcp-bff-api-r1 task 024: <c>spaarke_complete_decision</c> over the real MCP transport (the production <c>AddMcpModule</c>,
/// <c>MapMcpEndpoints</c> and A10 token filter on a Kestrel listener), with a scripted capability at the module boundary. Proves what the
/// handler-level tests cannot: tools/list shows the tool app-only and not read-only, the SDK binds <c>signalId</c> and a
/// <see cref="DecisionReview"/> from the JSON-RPC arguments with the REST route's JSON options, the capability receives the request's
/// own <see cref="HttpContext"/> (the instance the middleware saw), and a refusal is an <c>isError</c> tool result rather than a transport
/// error. The commit rules themselves are proven over the real service in <c>CompleteDecisionToolTests</c>.
/// </summary>
public sealed class CompleteDecisionToolWireTests
{
    private static readonly Guid Caller = Guid.Parse("02400000-0000-4000-8000-000000000001");
    private static readonly Guid SignalId = Guid.Parse("02400000-0000-4000-8000-000000000002");
    private static readonly Guid ReviewId = Guid.Parse("02400000-0000-4000-8000-000000000003");
    private static readonly Guid RecordId = Guid.Parse("02400000-0000-4000-8000-000000000004");

    private static readonly object ReviewBody = new
    {
        reviewId = ReviewId,
        confirmed = true,
        actions = new object[]
        {
            new { code = "approve-variance", taken = true, values = new { amount = 5000, note = "ok" } },
            new { code = "revise-budget", taken = false },
        },
        nextSteps = new object[] { new { code = "add-todo", values = new { title = "Chase it" } } },
        because = "The firm agreed the scope change",
    };

    [Fact]
    public async Task ToolsList_ShowsTheCompleteTool_AppOnly_NotReadOnly_Idempotent()
    {
        await using var host = await WireHost.StartAsync(_ => DecisionCapabilityRecorded());

        var list = await host.RpcAsync("tools/list", new { });

        var tool = list.GetProperty("tools").EnumerateArray().Should().ContainSingle(t => t.GetProperty("name").GetString() == "spaarke_complete_decision").Subject;
        tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean().Should().BeFalse();
        tool.GetProperty("annotations").GetProperty("destructiveHint").GetBoolean().Should().BeFalse();
        tool.GetProperty("annotations").GetProperty("idempotentHint").GetBoolean().Should().BeTrue();
        tool.GetProperty("_meta").GetProperty("ui").GetProperty("visibility").EnumerateArray().Select(v => v.GetString()).Should().Equal("app");
        tool.GetProperty("inputSchema").GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("signalId", "review");
    }

    [Fact]
    public async Task ToolsCall_BindsTheArgumentsLikeTheRestRoute_PassesTheRequestsOwnHttpContext_AndReturnsTheResultUnchanged()
    {
        var recorded = DecisionCapabilityRecorded();
        await using var host = await WireHost.StartAsync(_ => recorded);

        var result = await host.RpcAsync("tools/call", new { name = "spaarke_complete_decision", arguments = new { signalId = SignalId, review = ReviewBody } });

        (result.TryGetProperty("isError", out var isError) && isError.GetBoolean()).Should().BeFalse("a recorded decision is not an error result");
        var capability = host.Capability;
        capability.Calls.Should().Be(1);
        capability.SignalId.Should().Be(SignalId);
        capability.Review!.ReviewId.Should().Be(ReviewId);
        capability.Review.Confirmed.Should().BeTrue();
        capability.Review.Because.Should().Be("The firm agreed the scope change");
        capability.Review.Actions!.Select(a => (a.Code, a.Taken)).Should().Equal(("approve-variance", true), ("revise-budget", false));
        capability.Review.Actions![0].Values!["amount"].GetInt32().Should().Be(5000);
        capability.Review.NextSteps!.Single().Code.Should().Be("add-todo");
        capability.ReceivedContext.Should().NotBeNull().And.BeSameAs(host.MiddlewareContext, "the tool passes IHttpContextAccessor.HttpContext, never its own");

        var structured = result.GetProperty("structuredContent");
        structured.GetRawText().Should().Be(JsonSerializer.Serialize(recorded, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        structured.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).Should().Equal("inquiry_email_not_archived");
    }

    [Fact]
    public async Task ToolsCall_ARefusal_IsAnIsErrorToolResult_NotATransportError()
    {
        var refused = new DecisionCommitResult
        {
            Status = DecisionCommitStatus.Conflict,
            ReasonCode = DecisionCommitReasons.DecidedInInvoiceReview,
            ReviewId = ReviewId,
            Detail = "Acme v. Beta record text that must not be echoed",
        };
        await using var host = await WireHost.StartAsync(_ => refused);

        using var response = await host.PostRpcAsync("tools/call", new { name = "spaarke_complete_decision", arguments = new { signalId = SignalId, review = ReviewBody } });
        var result = await host.ParseAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a domain failure is a tool result, never an HTTP error");
        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        result.GetProperty("structuredContent").GetProperty("reasonCode").GetString().Should().Be("ontology.decision.decided_in_invoice_review");
        result.GetRawText().Should().NotContain("Acme v. Beta", "ADR-015: no record text in the result");
    }

    [Fact]
    public async Task ToolsCall_AFailedCommit_IsAnIsErrorResult_WithTheRestStatus_ACorrelationId_AndThePartialFacts()
    {
        var failed = new DecisionCommitResult
        {
            Status = DecisionCommitStatus.Failed,
            ReasonCode = DecisionCommitReasons.StepFailed,
            ReviewId = ReviewId,
            FailedStep = 3,
            Landed = [new DecisionCommitRef("sprk_budgetrevision", RecordId)],
            Resumable = true,
            Detail = "Acme v. Beta record text that must not be echoed",
        };
        await using var host = await WireHost.StartAsync(_ => failed);

        var result = await host.RpcAsync("tools/call", new { name = "spaarke_complete_decision", arguments = new { signalId = SignalId, review = ReviewBody } });

        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        var error = result.GetProperty("structuredContent");
        error.EnumerateObject().Select(p => p.Name).Should().Equal("reasonCode", "message", "status", "correlationId", "partial");
        error.GetProperty("reasonCode").GetString().Should().Be(DecisionCommitReasons.StepFailed);
        error.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status500InternalServerError, "the status the REST route answers for the same failure");
        error.GetProperty("correlationId").GetString().Should().Be(
            host.Capability.ReceivedTraceId, "the support reference is the request's TraceIdentifier, the value REST ProblemDetails carry as correlationId");
        error.GetProperty("partial").GetProperty("failedStep").GetInt32().Should().Be(3);
        error.GetProperty("partial").GetProperty("resumable").GetBoolean().Should().BeTrue();
        error.GetProperty("partial").GetProperty("landed")[0].GetProperty("entity").GetString().Should().Be("sprk_budgetrevision");
        result.GetRawText().Should().NotContain("Acme v. Beta");
    }

    [Fact]
    public async Task ToolsCall_LogsOneTelemetryLinePerCall_WithToolOutcomeLatencyAndCorrelation_AndNoSignalReviewOrText()
    {
        var refused = new DecisionCommitResult { Status = DecisionCommitStatus.Conflict, ReasonCode = DecisionCommitReasons.DecidedInInvoiceReview, ReviewId = ReviewId };
        var calls = 0;
        await using var host = await WireHost.StartAsync(_ => ++calls == 1 ? DecisionCapabilityRecorded() : refused);

        await host.RpcAsync("tools/call", new { name = "spaarke_complete_decision", arguments = new { signalId = SignalId, review = ReviewBody } });
        await host.RpcAsync("tools/call", new { name = "spaarke_complete_decision", arguments = new { signalId = SignalId, review = ReviewBody } });

        var lines = host.Logs.Entries.Where(e => e.Category.EndsWith("." + nameof(CompleteDecisionTool), StringComparison.Ordinal)).ToList();
        lines.Select(e => ((string)e.State["ToolName"]!, (string)e.State["Outcome"]!, (int)e.State["RowCount"]!)).Should().Equal(
            ("spaarke_complete_decision", "ok", 0),
            ("spaarke_complete_decision", DecisionCommitReasons.DecidedInInvoiceReview, 0));
        lines.Should().AllSatisfy(e =>
        {
            ((long)e.State["ElapsedMs"]!).Should().BeGreaterThanOrEqualTo(0);
            ((string?)e.State["CorrelationId"]).Should().NotBeNullOrWhiteSpace();
        });
        var logged = string.Join("\n", lines.Select(e => e.Message + " " + string.Join(" ", e.State.Values)));
        logged.Should().NotContain(SignalId.ToString()).And.NotContain(ReviewId.ToString()).And.NotContain("The firm agreed").And.NotContain("approve-variance");
    }

    private static DecisionCommitResult DecisionCapabilityRecorded() => new()
    {
        Status = DecisionCommitStatus.Recorded,
        ReviewId = ReviewId,
        DecisionRecordId = RecordId,
        Warnings = ["inquiry_email_not_archived"],
    };

    /// <summary>The scripted commit capability: records what it was handed and answers <c>script</c>.</summary>
    private sealed class ScriptedCapability(Func<DecisionReview, DecisionCommitResult> script) : IDecisionCapability
    {
        public int Calls { get; private set; }

        public Guid SignalId { get; private set; }

        public DecisionReview? Review { get; private set; }

        public HttpContext? ReceivedContext { get; private set; }

        public string? ReceivedTraceId { get; private set; }

        public Task<DecisionCommitResult> CommitAsync(HttpContext? http, Guid signalId, DecisionReview review, CancellationToken ct)
        {
            Calls++;
            ReceivedContext = http;
            ReceivedTraceId = http?.TraceIdentifier;
            SignalId = signalId;
            Review = review;
            return Task.FromResult(script(review));
        }

        public Task<SignalReadResult> ListDecideAsync(ClaimsPrincipal? user, CancellationToken ct, WorklistSet set = WorklistSet.Lanes) =>
            throw new NotSupportedException();

        public Task<DecidePageResult> ListDecidePageAsync(ClaimsPrincipal? user, string? cursor, CancellationToken ct) => throw new NotSupportedException();

        public Task<DecisionPlanReadResult> GetPlanAsync(HttpContext? http, Guid signalId, CancellationToken ct) => throw new NotSupportedException();

        public Task<SuppressionReadResult> GetSuppressionAsync(HttpContext? http, Guid signalId, CancellationToken ct) => throw new NotSupportedException();
    }

    /// <summary>An in-process MCP host serving only <see cref="CompleteDecisionTool"/> (the production module, route and token filter).</summary>
    private sealed class WireHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;
        private readonly Func<HttpContext?> _middlewareContext;

        private WireHost(
            WebApplication app, HttpClient client, ScriptedCapability capability, Func<HttpContext?> middlewareContext,
            DecisionToolsTestHost.CapturingLoggerProvider logs)
        {
            _app = app;
            _client = client;
            Capability = capability;
            _middlewareContext = middlewareContext;
            Logs = logs;
        }

        public ScriptedCapability Capability { get; }

        public DecisionToolsTestHost.CapturingLoggerProvider Logs { get; }

        public HttpContext? MiddlewareContext => _middlewareContext();

        public static async Task<WireHost> StartAsync(Func<DecisionReview, DecisionCommitResult> script)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
            var logs = new DecisionToolsTestHost.CapturingLoggerProvider();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(logs);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mcp:Enabled"] = "true",
                ["AzureAd:TenantId"] = McpTestHost.TenantId,
                ["AzureAd:ClientId"] = McpTestHost.BffClientId,
            });

            var capability = new ScriptedCapability(script);
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<IDecisionCapability>(capability);
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = "https://login.microsoftonline.com/" + McpTestHost.TenantId + "/v2.0",
                    ValidAudience = McpTestHost.BffClientId,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("mcp-contract-test-signing-key-0123456789-0123456789")),
                });
            builder.Services.AddAuthorization(o => o.AddPolicy("SystemAdmin", p => p.RequireAssertion(_ => false)));
            builder.Services.AddRateLimitingModule();
            builder.Services.AddMcpModule(builder.Configuration, [typeof(CompleteDecisionTool)]);

            var app = builder.Build();
            HttpContext? middlewareContext = null;
            app.Use(async (context, next) =>
            {
                middlewareContext = context;
                await next(context);
            });
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRateLimiter();
            app.MapMcpEndpoints();
            await app.StartAsync();

            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return new WireHost(app, new HttpClient { BaseAddress = new Uri(address) }, capability, () => middlewareContext, logs);
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        public Task<HttpResponseMessage> PostRpcAsync(string method, object parameters)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.LegalOperationsRoute)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }), Encoding.UTF8, "application/json"),
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", McpTestHost.MintToken(Caller));
            return _client.SendAsync(request);
        }

        public async Task<JsonElement> RpcAsync(string method, object parameters)
        {
            using var response = await PostRpcAsync(method, parameters);
            return await ParseAsync(response);
        }

        /// <summary>The JSON-RPC <c>result</c> of an answer, whether the transport sent plain JSON or one SSE event.</summary>
        public async Task<JsonElement> ParseAsync(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"{(int)response.StatusCode}: {body}");
            }

            var json = body.TrimStart().StartsWith('{')
                ? body
                : string.Join(
                    "\n",
                    body.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].TrimStart()));
            using var doc = JsonDocument.Parse(json.Split('\n').First(l => l.Contains("\"result\"") || l.Contains("\"error\"")));
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException($"JSON-RPC error: {error}");
            }

            return doc.RootElement.GetProperty("result").Clone();
        }
    }
}

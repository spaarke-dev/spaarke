// Regression anchor: the pre-stream error responses of the AI streaming routes are RFC 7807 ProblemDetails.
//
// THE DEFECT. ChatEndpoints.SendMessageAsync / RefineTextAsync, PlaybookRunEndpoints.ExecutePlaybook /
// StreamRunStatus and AnalysisEndpoints.ExecuteAnalysis answered their early failures (missing tenant,
// unknown session/run, feature disabled, multi-document, missing playbookId/documentIds, stream error)
// with `{ "error": "…" }` as plain application/json. @spaarke/auth's authenticatedFetch only parses a JSON
// body that has `title` or `status`, so for every Dataverse-hosted client the message was dropped and the
// user saw "HTTP 400" / "HTTP 404" / "HTTP 503" (ADR-019). Each site now writes
// ProblemDetailsHelper.FromLegacyError: application/problem+json, `detail` = the old text, and the old
// `error` member kept as an extension so no reader of it breaks.
//
// SCOPE. Every site here runs BEFORE the handler sets text/event-stream (read end to end), i.e. a plain HTTP
// error, not an SSE in-stream error event (a different contract, untouched). StreamRunStatus's catch is
// guarded by Response.HasStarted for the same reason.
//
// TECHNIQUE. Reflection into the private static handlers, as Issue975_ChatAttachmentValidationProblemJsonTests
// already does for SendMessageAsync: every branch under test returns before any collaborator other than the
// ones supplied here is touched, so the rest are passed as null. Arguments are bound by parameter TYPE so a
// reordered signature does not silently shift them.
//
// MAINTAIN-class (regression protector; ADR-038 KEEP path tests/integration/regression/**).

using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression.Ai;

public class AiPreStreamErrorProblemDetailsTests
{
    private const string TenantId = "prestream-problem-tenant";
    private const string SessionId = "prestream-problem-session";

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Harness
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private static DefaultHttpContext NewContext(bool withTenant)
    {
        var claims = new List<Claim>
        {
            new("oid", TestSessionOwner.Oid),
            new("http://schemas.microsoft.com/identity/claims/objectidentifier", TestSessionOwner.Oid),
        };
        if (withTenant)
        {
            claims.Add(new Claim("tid", TenantId));
        }

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
            // ProblemHttpResult.ExecuteAsync resolves ILoggerFactory from RequestServices.
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
    }

    /// <summary>A real ChatSessionManager whose repository knows no session (Loose mocks return null).</summary>
    private static ChatSessionManager EmptySessionManager() =>
        new(new Mock<ITenantCache>().Object, new Mock<IChatDataverseRepository>().Object, NullLogger<ChatSessionManager>.Instance);

    private static async Task InvokeAsync(Type endpoints, string method, HttpContext httpContext, params object[] supplied)
    {
        var info = endpoints.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(endpoints.Name, method);

        var args = info.GetParameters().Select(p =>
        {
            if (typeof(HttpContext).IsAssignableFrom(p.ParameterType)) return httpContext;
            var match = supplied.FirstOrDefault(s => p.ParameterType.IsInstanceOfType(s));
            if (match is not null) return match;
            if (p.ParameterType == typeof(ILoggerFactory)) return NullLoggerFactory.Instance;
            if (p.ParameterType.IsGenericType && p.ParameterType.GetGenericTypeDefinition() == typeof(ILogger<>))
            {
                var nullLogger = typeof(NullLogger<>).MakeGenericType(p.ParameterType.GetGenericArguments());
                return nullLogger.GetField("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
            }
            return p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
        }).ToArray();

        await (Task)info.Invoke(null, args)!;
    }

    private static async Task AssertProblemAsync(HttpContext httpContext, HttpStatusCode status, string detail)
    {
        httpContext.Response.StatusCode.Should().Be((int)status);
        httpContext.Response.ContentType.Should().StartWith("application/problem+json",
            "authenticatedFetch only reads a ProblemDetails body; a bare { error } body reached the user as \"HTTP {0}\"",
            (int)status);

        httpContext.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(httpContext.Response.Body);
        body.GetProperty("status").GetInt32().Should().Be((int)status);
        body.GetProperty("title").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("detail").GetString().Should().Be(detail);
        body.GetProperty("error").GetString().Should().Be(detail, "the legacy member is kept for any reader of it");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // ChatEndpoints
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendMessage_NoTenantClaim_Is400Problem()
    {
        var ctx = NewContext(withTenant: false);
        await InvokeAsync(typeof(ChatEndpoints), "SendMessageAsync", ctx, SessionId, new ChatSendMessageRequest("hi"));
        await AssertProblemAsync(ctx, HttpStatusCode.BadRequest, "Tenant ID not found in token claims");
    }

    [Fact]
    public async Task SendMessage_UnknownSession_Is404Problem()
    {
        var ctx = NewContext(withTenant: true);
        await InvokeAsync(typeof(ChatEndpoints), "SendMessageAsync", ctx,
            SessionId, new ChatSendMessageRequest("hi"), EmptySessionManager());
        await AssertProblemAsync(ctx, HttpStatusCode.NotFound, $"Session {SessionId} not found");
    }

    [Fact]
    public async Task Refine_NoTenantClaim_Is400Problem()
    {
        var ctx = NewContext(withTenant: false);
        await InvokeAsync(typeof(ChatEndpoints), "RefineTextAsync", ctx, SessionId, new ChatRefineRequest("text", "simplify"));
        await AssertProblemAsync(ctx, HttpStatusCode.BadRequest, "Tenant ID not found in token claims");
    }

    [Fact]
    public async Task Refine_UnknownSession_Is404Problem()
    {
        var ctx = NewContext(withTenant: true);
        await InvokeAsync(typeof(ChatEndpoints), "RefineTextAsync", ctx,
            SessionId, new ChatRefineRequest("text", "simplify"), EmptySessionManager());
        await AssertProblemAsync(ctx, HttpStatusCode.NotFound, $"Session {SessionId} not found");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // PlaybookRunEndpoints
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecutePlaybook_NoDocumentIds_Is400Problem()
    {
        var ctx = NewContext(withTenant: true);
        await InvokeAsync(typeof(PlaybookRunEndpoints), "ExecutePlaybook", ctx,
            Guid.NewGuid(), new ExecutePlaybookRequest { DocumentIds = [] });
        await AssertProblemAsync(ctx, HttpStatusCode.BadRequest, PlaybookRunEndpoints.DocumentIdsRequiredMessage);
    }

    [Fact]
    public async Task StreamRunStatus_UnknownRun_Is404Problem()
    {
        var runId = Guid.NewGuid();
        var orchestration = new Mock<IPlaybookOrchestrationService>();
        orchestration.Setup(o => o.GetRunStatusAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlaybookRunStatus?)null);

        var ctx = NewContext(withTenant: true);
        await InvokeAsync(typeof(PlaybookRunEndpoints), "StreamRunStatus", ctx, runId, orchestration.Object);
        await AssertProblemAsync(ctx, HttpStatusCode.NotFound, $"Run {runId} not found");
    }

    [Fact]
    public async Task StreamRunStatus_FailureBeforeTheStream_Is500Problem()
    {
        var runId = Guid.NewGuid();
        var orchestration = new Mock<IPlaybookOrchestrationService>();
        orchestration.Setup(o => o.GetRunStatusAsync(runId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store unavailable"));

        var ctx = NewContext(withTenant: true);
        await InvokeAsync(typeof(PlaybookRunEndpoints), "StreamRunStatus", ctx, runId, orchestration.Object);
        await AssertProblemAsync(ctx, HttpStatusCode.InternalServerError, "Stream error");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // AnalysisEndpoints
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private static IOptions<AnalysisOptions> Analysis(bool enabled, bool multiDocument = false) =>
        Options.Create(new AnalysisOptions { Enabled = enabled, MultiDocumentEnabled = multiDocument });

    [Fact]
    public async Task ExecuteAnalysis_FeatureDisabled_Is503Problem()
    {
        var ctx = NewContext(withTenant: true);
        await InvokeAsync(typeof(AnalysisEndpoints), "ExecuteAnalysis", ctx,
            new AnalysisExecuteRequest { DocumentIds = [Guid.NewGuid()], PlaybookId = Guid.NewGuid() }, Analysis(enabled: false));
        await AssertProblemAsync(ctx, HttpStatusCode.ServiceUnavailable, "Analysis feature is disabled");
    }

    [Fact]
    public async Task ExecuteAnalysis_MultiDocumentWhileDisabled_Is400Problem()
    {
        var ctx = NewContext(withTenant: true);
        await InvokeAsync(typeof(AnalysisEndpoints), "ExecuteAnalysis", ctx,
            new AnalysisExecuteRequest { DocumentIds = [Guid.NewGuid(), Guid.NewGuid()], PlaybookId = Guid.NewGuid() },
            Analysis(enabled: true, multiDocument: false));
        await AssertProblemAsync(ctx, HttpStatusCode.BadRequest,
            "Multi-document analysis coming in Phase 2. Currently only single document is supported.");
    }

    [Fact]
    public async Task ExecuteAnalysis_NoPlaybookId_Is400Problem()
    {
        var ctx = NewContext(withTenant: true);
        await InvokeAsync(typeof(AnalysisEndpoints), "ExecuteAnalysis", ctx,
            new AnalysisExecuteRequest { DocumentIds = [Guid.NewGuid()] }, Analysis(enabled: true));
        await AssertProblemAsync(ctx, HttpStatusCode.BadRequest, AnalysisEndpoints.PlaybookIdRequiredMessage);
    }
}

using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api;

/// <summary>
/// Endpoint-shape contract tests for <see cref="ComposeEndpoints"/>.
///
/// <para>
/// <b>ADR-038 KEEP-path category</b>: <c>endpoint-contract</c>. These tests assert the
/// wiring shape of the Compose endpoint group — URL patterns, HTTP verbs, authorization
/// requirements, and the ADR-013-binding facade-injection rule — WITHOUT exercising
/// downstream service behavior (that's the role of task 026 service unit tests + task
/// 027 integration tests).
/// </para>
///
/// <para>
/// <b>Banned-pattern compliance</b> (per ADR-038 + tests/CLAUDE.md):
/// <list type="bullet">
///   <item>No <c>Mock&lt;HttpMessageHandler&gt;</c> (B1) — none used; tests inspect the
///   endpoint metadata via the <see cref="EndpointDataSource"/> abstraction.</item>
///   <item>No DI-registration tests (B3) — we do NOT assert
///   <c>services.AddScoped&lt;IComposeService&gt;</c>; we assert the endpoint's
///   <i>handler signature</i> declares the expected parameter types (a behavior the
///   user observes, not a wiring detail).</item>
///   <item>No ctor null-check tests (B4) — no handler ctor exists.</item>
///   <item>No mirror tests (B6) — every assertion targets a contract the consumer
///   relies on (URL, verb, auth, facade boundary).</item>
/// </list>
/// </para>
/// </summary>
public sealed class ComposeEndpointsTests
{
    /// <summary>
    /// Materialize the Compose endpoints by running <see cref="ComposeEndpoints.MapComposeEndpoints"/>
    /// against a minimal-but-real <see cref="WebApplication"/> route builder, then drain
    /// the produced <see cref="EndpointDataSource"/> for inspection. This is the lightest
    /// way to assert wire shape without booting the full host (per ADR-038 KEEP-path
    /// <c>endpoint-contract</c> category).
    /// </summary>
    private static IReadOnlyList<RouteEndpoint> BuildComposeEndpoints()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(ComposeEndpointsTests).Assembly.GetName().Name,
            EnvironmentName = "Development",
        });
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(_ => { });

        // Register the handler dependencies as services so RequestDelegateFactory
        // recognises them via [FromServices] inference rather than misclassifying as
        // body bindings. We're asserting *shape* here, not behavior, so Moq stubs are
        // sufficient — ADR-038 mock-boundary rule permits mocking the SUT's injected
        // collaborators when the test target is the wiring contract, not the collaborator.
        builder.Services.AddSingleton(Mock.Of<IComposeService>());
        builder.Services.AddSingleton(Mock.Of<IConsumerRoutingService>());
        builder.Services.AddSingleton(Mock.Of<IInvokePlaybookAi>());

        var app = builder.Build();
        app.MapComposeEndpoints();

        // Pull endpoints from the resolved data sources (route builder requires this to
        // materialize). MapXxx in Minimal API registers a deferred data source that the
        // composite source flattens on first read.
        var endpointDataSource = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        return endpointDataSource;
    }

    private static IReadOnlyList<RouteEndpoint> GetComposeEndpoints() =>
        BuildComposeEndpoints()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/compose", StringComparison.Ordinal) == true)
            .ToList();

    [Fact]
    public void MapComposeEndpoints_registers_eight_endpoints_under_api_compose_prefix()
    {
        var routes = GetComposeEndpoints();

        // Spec FR-21 + POML <acceptance-criteria>: 7 endpoints from W3-024 +
        // 1 added in W7-052 (heartbeat per Spike #3 §4.2). Surface = 8.
        routes.Should().HaveCount(8,
            "spec FR-21 + spike #3 §4.2 + W7-052 lock the surface at eight endpoints (7 from W3-024 + heartbeat)");
    }

    [Theory]
    [InlineData("POST", "/api/compose/upload", "ComposeUpload")]
    [InlineData("GET",  "/api/compose/documents/{documentSpeId}", "ComposeLoadDocument")]
    [InlineData("POST", "/api/compose/documents/{documentSpeId}/save", "ComposeSaveDocument")]
    [InlineData("POST", "/api/compose/documents/{documentSpeId}/promote", "ComposePromoteDocument")]
    [InlineData("POST", "/api/compose/documents/{documentId:guid}/checkout", "ComposeCheckoutDocument")]
    [InlineData("POST", "/api/compose/documents/{documentId:guid}/checkin", "ComposeCheckinDocument")]
    [InlineData("POST", "/api/compose/action/{consumerType}", "ComposeDispatchAction")]
    [InlineData("POST", "/api/compose/document/{documentId:guid}/heartbeat", "ComposeRefreshHeartbeat")]
    public void Endpoint_route_pattern_and_verb_match_locked_shape(string verb, string pattern, string name)
    {
        var routes = BuildComposeEndpoints();
        var endpoint = routes
            .FirstOrDefault(e => e.Metadata.OfType<EndpointNameMetadata>()
                .Any(m => m.EndpointName == name));

        endpoint.Should().NotBeNull($"endpoint '{name}' must be registered (POML <steps> step 2 row)");
        endpoint!.RoutePattern.RawText.Should().Be(pattern,
            $"route pattern is locked by spike #3 §8 + spike #4 §5 + POML step 2");

        var verbMetadata = endpoint.Metadata.OfType<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()
            .FirstOrDefault();
        verbMetadata.Should().NotBeNull("every endpoint declares an explicit HTTP verb");
        verbMetadata!.HttpMethods.Should().ContainSingle().Which.Should().Be(verb);
    }

    [Fact]
    public void All_endpoints_require_authorization_per_ADR_008()
    {
        var composeEndpoints = GetComposeEndpoints();
        composeEndpoints.Should().NotBeEmpty();
        foreach (var endpoint in composeEndpoints)
        {
            // ADR-008: authorization is endpoint-filter based, NOT global middleware.
            // The group's RequireAuthorization() injects IAuthorizeData metadata.
            var hasAuth = endpoint.Metadata.OfType<IAuthorizeData>().Any();
            hasAuth.Should().BeTrue(
                $"endpoint '{endpoint.RoutePattern.RawText}' must apply RequireAuthorization() per ADR-008 + ADR-028 + spec NFR-08");
        }
    }

    [Fact]
    public void Dispatch_action_handler_only_injects_PublicContracts_facade_types_per_refined_ADR_013()
    {
        // Refined ADR-013 (2026-05-20) binding rule per project CLAUDE.md §"MUST NOT":
        // CRUD-side endpoint code MUST NOT inject IOpenAiClient, IPlaybookService,
        // IPlaybookOrchestrationService, or IPlaybookExecutionEngine. The dispatch
        // handler is the ONLY Compose endpoint that interacts with AI; verify its
        // parameter types live in Services.Ai.PublicContracts only.

        // Reflect on the private static DispatchAction handler signature.
        var handler = typeof(ComposeEndpoints).GetMethod(
            "DispatchAction",
            BindingFlags.NonPublic | BindingFlags.Static);

        handler.Should().NotBeNull(
            "DispatchAction is the locked-name handler for POST /api/compose/action/{consumerType}");

        var paramTypes = handler!.GetParameters().Select(p => p.ParameterType).ToList();

        // Required facade types ARE injected.
        paramTypes.Should().Contain(typeof(IConsumerRoutingService),
            "spike #4 §5 + refined ADR-013 require IConsumerRoutingService injection");
        paramTypes.Should().Contain(typeof(IInvokePlaybookAi),
            "spike #4 §5 + refined ADR-013 require IInvokePlaybookAi injection");

        // Forbidden AI-internal types are NOT injected. We check by simple-name to avoid
        // hard-importing the AI-internal types here (which would itself be a code-smell
        // since this test asserts the boundary).
        var forbiddenSimpleNames = new[]
        {
            "IOpenAiClient",
            "IPlaybookService",
            "IPlaybookOrchestrationService",
            "IPlaybookExecutionEngine",
        };

        var injectedTypeNames = paramTypes.Select(t => t.Name).ToList();
        foreach (var forbidden in forbiddenSimpleNames)
        {
            injectedTypeNames.Should().NotContain(forbidden,
                $"refined ADR-013 (2026-05-20) bans direct injection of '{forbidden}' " +
                "into CRUD-side endpoint code; AI capabilities flow through " +
                "Services/Ai/PublicContracts/ facades only");
        }
    }

    [Fact]
    public void Document_lifecycle_handlers_inject_IComposeService_facade_only()
    {
        // The Load / Save / Promote handlers delegate to IComposeService (task 021),
        // which itself is a CRUD-shaped orchestrator (no AI internals). Verify the
        // handler signatures do NOT bypass it by reaching into AI internals directly.

        var handlerNames = new[] { "Load", "Save", "Promote" };
        var forbiddenSimpleNames = new[]
        {
            "IOpenAiClient",
            "IPlaybookService",
            "IPlaybookOrchestrationService",
            "IPlaybookExecutionEngine",
            "IInvokePlaybookAi",            // belongs only in DispatchAction
            "IConsumerRoutingService",      // belongs only in DispatchAction
        };

        foreach (var name in handlerNames)
        {
            var handler = typeof(ComposeEndpoints).GetMethod(
                name,
                BindingFlags.NonPublic | BindingFlags.Static);

            handler.Should().NotBeNull($"handler '{name}' must exist");

            var paramTypeNames = handler!.GetParameters().Select(p => p.ParameterType.Name).ToList();
            foreach (var forbidden in forbiddenSimpleNames)
            {
                paramTypeNames.Should().NotContain(forbidden,
                    $"document-lifecycle handler '{name}' must not inject '{forbidden}' — " +
                    "AI dispatch belongs in DispatchAction only (spike #4 §5)");
            }
        }
    }
}

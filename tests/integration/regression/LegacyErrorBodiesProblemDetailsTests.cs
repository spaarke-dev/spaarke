// Regression anchor: the last legacy `{ "error": "..." }` bodies in HandlerEndpoints, PermissionsEndpoints and
// NavMapEndpoints are RFC 7807 ProblemDetails (ADR-019).
//
// THE DEFECT. GET /api/ai/handlers/{id}, POST /api/permissions/batch and the three /api/navmap routes answered
// their 400/404 with `Results.NotFound(new { error })` / `TypedResults.BadRequest(new { error })` -- plain
// application/json. @spaarke/auth's authenticatedFetch only parses a JSON body that has `title` or `status`, so
// the message was dropped and the user saw "HTTP 400" / "HTTP 404" (follow-up to PR #1491, which converted the
// AI streaming routes). Each site now writes ProblemDetailsHelper.FromLegacyError: application/problem+json,
// `detail` = the old text, and the old `error` member kept as an extension -- NavMapClient reads `.error`, so
// it must keep working (its own client test pins that).
//
// TECHNIQUE. Reflection into the private static handlers (same as AiPreStreamErrorProblemDetailsTests): every
// branch under test returns before the collaborators passed as null are touched. Arguments are bound by
// parameter NAME first, then by TYPE, so a reordered signature does not silently shift them.
//
// MAINTAIN-class (regression protector; ADR-038 KEEP path tests/integration/regression/**).

using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Infrastructure.Caching;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

public class LegacyErrorBodiesProblemDetailsTests
{
    private static DefaultHttpContext NewContext() => new()
    {
        // ProblemHttpResult.ExecuteAsync resolves ILoggerFactory from RequestServices.
        RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        Response = { Body = new MemoryStream() },
    };

    private static async Task InvokeAsync(
        Type endpoints, string method, HttpContext httpContext, IDictionary<string, object?> named, params object[] supplied)
    {
        var info = endpoints.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(endpoints.Name, method);

        var args = info.GetParameters().Select(p =>
        {
            if (p.Name is not null && named.TryGetValue(p.Name, out var byName)) return byName;
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

        var returned = info.Invoke(null, args);
        if (returned is Task task)
        {
            await task;
            returned = task.GetType().GetProperty("Result")?.GetValue(task);
        }

        // The handlers return their response instead of writing it: execute it the way the endpoint pipeline does.
        (returned as IResult).Should().NotBeNull("the handler under test returns an IResult");
        await ((IResult)returned!).ExecuteAsync(httpContext);
    }

    private static Dictionary<string, object?> Named(params (string Name, object? Value)[] items) =>
        items.ToDictionary(i => i.Name, i => i.Value);

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
        body.GetProperty("error").GetString().Should().Be(detail, "the legacy member is kept for any reader of it (NavMapClient)");
    }

    // ── HandlerEndpoints: GET /api/ai/handlers/{handlerId} ───────────────────────────────────────

    [Fact]
    public async Task GetHandler_UnknownHandler_Is404Problem()
    {
        var registry = new Mock<IToolHandlerRegistry>();
        registry.Setup(r => r.GetHandler("nope")).Returns((IAnalysisToolHandler?)null);
        var ctx = NewContext();

        await InvokeAsync(typeof(HandlerEndpoints), "GetHandler", ctx, Named(("handlerId", "nope")), registry.Object);

        await AssertProblemAsync(ctx, HttpStatusCode.NotFound, "Handler 'nope' not found or is disabled.");
    }

    [Fact]
    public async Task GetHandler_HandlerWithoutInfo_Is404Problem()
    {
        var registry = new Mock<IToolHandlerRegistry>();
        registry.Setup(r => r.GetHandler("h1")).Returns(new Mock<IAnalysisToolHandler>().Object);
        registry.Setup(r => r.GetAllHandlerInfo()).Returns(Array.Empty<ToolHandlerInfo>());
        var ctx = NewContext();

        await InvokeAsync(typeof(HandlerEndpoints), "GetHandler", ctx, Named(("handlerId", "h1")), registry.Object);

        await AssertProblemAsync(ctx, HttpStatusCode.NotFound, "Handler info for 'h1' not found.");
    }

    // ── PermissionsEndpoints: POST /api/permissions/batch ────────────────────────────────────────

    [Fact]
    public async Task BatchPermissions_EmptyDocumentIds_Is400Problem()
    {
        var ctx = NewContext();
        await InvokeAsync(typeof(PermissionsEndpoints), "GetBatchPermissionsAsync", ctx, Named(),
            new BatchPermissionsRequest { DocumentIds = new List<string>() });
        await AssertProblemAsync(ctx, HttpStatusCode.BadRequest, "DocumentIds cannot be empty");
    }

    [Fact]
    public async Task BatchPermissions_OverBatchLimit_Is400Problem()
    {
        var ctx = NewContext();
        await InvokeAsync(typeof(PermissionsEndpoints), "GetBatchPermissionsAsync", ctx, Named(),
            new BatchPermissionsRequest { DocumentIds = Enumerable.Range(0, 101).Select(i => i.ToString()).ToList() });
        await AssertProblemAsync(ctx, HttpStatusCode.BadRequest, "Maximum batch size is 100 documents");
    }

    // ── NavMapEndpoints: GET /api/navmap/... ─────────────────────────────────────────────────────

    private static IGenericEntityService NotFoundDataverse()
    {
        var svc = new Mock<IGenericEntityService>();
        svc.Setup(s => s.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("entity not found"));
        svc.Setup(s => s.GetLookupNavigationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("relationship not found"));
        svc.Setup(s => s.GetCollectionNavigationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("relationship not found"));
        return svc.Object;
    }

    private static Task NavMapAsync(HttpContext ctx, string method, params (string Name, object? Value)[] named) =>
        InvokeAsync(typeof(NavMapEndpoints), method, ctx, Named(named),
            NotFoundDataverse(), new Mock<IEndpointResponseCache>().Object);

    [Fact]
    public async Task NavMap_EntitySet_EmptyEntity_Is400Problem()
    {
        var ctx = NewContext();
        await NavMapAsync(ctx, "GetEntitySetNameAsync", ("entityLogicalName", " "));
        await AssertProblemAsync(ctx, HttpStatusCode.BadRequest, "entityLogicalName cannot be empty");
    }

    [Fact]
    public async Task NavMap_EntitySet_UnknownEntity_Is404Problem()
    {
        var ctx = NewContext();
        await NavMapAsync(ctx, "GetEntitySetNameAsync", ("entityLogicalName", "sprk_unknown"));
        await AssertProblemAsync(ctx, HttpStatusCode.NotFound, "Entity 'sprk_unknown' not found in Dataverse metadata");
    }

    [Fact]
    public async Task NavMap_Lookup_EmptyChild_Is400Problem()
    {
        var ctx = NewContext();
        await NavMapAsync(ctx, "GetLookupNavigationAsync", ("childEntity", ""), ("relationship", "r"));
        await AssertProblemAsync(ctx, HttpStatusCode.BadRequest, "childEntity cannot be empty");
    }

    [Fact]
    public async Task NavMap_Lookup_EmptyRelationship_Is400Problem()
    {
        var ctx = NewContext();
        await NavMapAsync(ctx, "GetLookupNavigationAsync", ("childEntity", "c"), ("relationship", ""));
        await AssertProblemAsync(ctx, HttpStatusCode.BadRequest, "relationship cannot be empty");
    }

    [Fact]
    public async Task NavMap_Lookup_UnknownRelationship_Is404Problem()
    {
        var ctx = NewContext();
        await NavMapAsync(ctx, "GetLookupNavigationAsync", ("childEntity", "sprk_x"), ("relationship", "sprk_y"));
        await AssertProblemAsync(ctx, HttpStatusCode.NotFound, "Lookup navigation not found for sprk_x.sprk_y");
    }

    [Fact]
    public async Task NavMap_Collection_EmptyParent_Is400Problem()
    {
        var ctx = NewContext();
        await NavMapAsync(ctx, "GetCollectionNavigationAsync", ("parentEntity", ""), ("relationship", "r"));
        await AssertProblemAsync(ctx, HttpStatusCode.BadRequest, "parentEntity cannot be empty");
    }

    [Fact]
    public async Task NavMap_Collection_EmptyRelationship_Is400Problem()
    {
        var ctx = NewContext();
        await NavMapAsync(ctx, "GetCollectionNavigationAsync", ("parentEntity", "p"), ("relationship", ""));
        await AssertProblemAsync(ctx, HttpStatusCode.BadRequest, "relationship cannot be empty");
    }

    [Fact]
    public async Task NavMap_Collection_UnknownRelationship_Is404Problem()
    {
        var ctx = NewContext();
        await NavMapAsync(ctx, "GetCollectionNavigationAsync", ("parentEntity", "sprk_p"), ("relationship", "sprk_r"));
        await AssertProblemAsync(ctx, HttpStatusCode.NotFound, "Collection navigation not found for sprk_p.sprk_r");
    }
}

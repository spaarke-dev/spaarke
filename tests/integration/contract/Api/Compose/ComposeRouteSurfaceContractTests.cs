// Compose route-surface snapshot — the equivalence oracle for endpoint-file refactors.
//
// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `endpoint-contract`
//   - Path:     `tests/integration/contract/Api/Compose/**`
//   - Justification: /api/compose/* IS the Compose client contract. Splitting the endpoint file
//     (r8 task 073) moved 18 mapping statements between 9 files; a lost `RequireAuthorization()`
//     group-convention inheritance, a changed path, or a dropped rate-limit policy is invisible to
//     every other test in the suite because the handler bodies themselves are untouched. This test
//     enumerates the surface the BUILT HOST actually exposes and pins it, so such drift fails loudly.
//
// Why the built host rather than source inspection: a grouped route's final path is composed from
// the group prefix at build time, and `RequireAuthorization()` / `WithTags()` applied to a
// RouteGroupBuilder are CONVENTIONS that flow to every endpoint added to that group — wherever the
// `group.MapPost(...)` statement physically lives. Only the built EndpointDataSource proves they landed.
//
// Scope note: the asserted row deliberately carries the CONTRACT-bearing facets (verb, path,
// endpoint name, authorization posture, rate-limit policy, tags, declared responses, request-size
// cap). It deliberately does NOT pin the raw Endpoint.Metadata list — that list also carries
// compiler/framework artifacts (ParameterBindingMetadata, AsyncStateMachineAttribute,
// RouteDiagnosticsMetadata) which an SDK bump may legitimately change. The task-073 refactor was
// separately verified against the FULL metadata dump, which was byte-identical before and after.
//
// Banned-pattern compliance (ADR-038 §4): no Mock<HttpMessageHandler>, no DI-registration test
// (this asserts the HTTP-observable route surface, not that a type is registered), no ctor-null test.

using FluentAssertions;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Compose;

public class ComposeRouteSurfaceContractTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public ComposeRouteSurfaceContractTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// The approved <c>/api/compose/*</c> surface. One row per route, ordinal-sorted:
    /// <c>VERB path | name=… | auth=… | rateLimit=… | tags=… | produces=… | sizeLimit=…</c>.
    ///
    /// <para>Editing this constant is a CONTRACT CHANGE, not a refactor. Moving a handler between
    /// files, renaming a class, or regrouping the mapping statements must leave it byte-identical.</para>
    /// </summary>
    private static readonly string[] ApprovedSurface =
    [
        "GET api/compose/documents/{documentSpeId} | name=ComposeLoadDocument | auth=authorize | rateLimit=ai-context | tags=Compose | produces=200,400,401,404,500 | sizeLimit=default",
        "GET api/compose/sessions/{sessionId}/annotations | name=ComposeGetAnnotations | auth=authorize | rateLimit=ai-context | tags=Compose | produces=200,400,401,500 | sizeLimit=default",
        "POST api/compose/active-document | name=ComposeRegisterActiveDocument | auth=authorize | rateLimit=ai-context | tags=Compose | produces=200,400,401,404,500 | sizeLimit=default",
        "POST api/compose/document/{documentId:guid}/heartbeat | name=ComposeRefreshHeartbeat | auth=authorize | rateLimit=ai-context | tags=Compose | produces=204,401,404,500 | sizeLimit=default",
        "POST api/compose/document/{documentSpeId}/check-changes | name=ComposeCheckDocumentChanges | auth=authorize | rateLimit=ai-context | tags=Compose | produces=200,400,401,500 | sizeLimit=default",
        "POST api/compose/document/{documentSpeId}/pull-annotations | name=ComposePullAnnotations | auth=authorize | rateLimit=ai-context | tags=Compose | produces=200,400,401,403,404,500 | sizeLimit=default",
        "POST api/compose/document/{documentSpeId}/reanchor-annotations | name=ComposeReanchorAnnotations | auth=authorize | rateLimit=ai-context | tags=Compose | produces=200,400,401,403,404,500 | sizeLimit=default",
        "POST api/compose/documents/create-on-save | name=ComposeCreateOnSaveDocument | auth=authorize | rateLimit=ai-persist | tags=Compose | produces=200,400,401,404,500 | sizeLimit=raised",
        "POST api/compose/documents/{documentId:guid}/checkin | name=ComposeCheckinDocument | auth=authorize | rateLimit=ai-context | tags=Compose | produces=401,501 | sizeLimit=default",
        "POST api/compose/documents/{documentId:guid}/checkout | name=ComposeCheckoutDocument | auth=authorize | rateLimit=ai-context | tags=Compose | produces=401,501 | sizeLimit=default",
        "POST api/compose/documents/{documentRecordId:guid}/refresh-profile | name=ComposeRefreshProfile | auth=authorize | rateLimit=ai-context | tags=Compose | produces=202,400,401,500 | sizeLimit=default",
        "POST api/compose/documents/{documentSpeId}/apply-template | name=ComposeApplyTemplate | auth=authorize | rateLimit=ai-persist | tags=Compose | produces=200,400,401,403,404,500 | sizeLimit=default",
        "POST api/compose/documents/{documentSpeId}/promote | name=ComposePromoteDocument | auth=authorize | rateLimit=ai-context | tags=Compose | produces=200,400,401,500 | sizeLimit=default",
        "POST api/compose/documents/{documentSpeId}/save | name=ComposeSaveDocument | auth=authorize | rateLimit=ai-persist | tags=Compose | produces=200,400,401,404,500 | sizeLimit=raised",
        // sizeLimit default -> raised (#696, 2026-09-01): this door runs synchronous OOXML projection on
        // caller-supplied bytes and had only Kestrel's implicit ~28.6 MB cap. Now bounded on the same two
        // levels as the save routes, from the same ComposeSaveLimits constants. `raised` here means the
        // transport cap is MaxRequestBodyBytes (1.5x the document limit) — LARGER than the default, so a
        // legal 25 MB document reaches the handler and is refused, if at all, by a 400 ProblemDetails that
        // names the limit. `produces` is unchanged for the same reason the save routes leave it unchanged:
        // 413 is a transport backstop, not a declared outcome.
        "POST api/compose/project | name=ComposeProject | auth=authorize | rateLimit=ai-context | tags=Compose | produces=200,400,401 | sizeLimit=raised",
        "POST api/compose/sessions/{sessionId}/annotations | name=ComposeSaveAnnotations | auth=authorize | rateLimit=ai-context | tags=Compose | produces=200,400,401,404,500 | sizeLimit=default",
        "POST api/compose/upload | name=ComposeUpload | auth=authorize | rateLimit=ai-context | tags=Compose | produces=200,400,401,404,500 | sizeLimit=default",
        "POST api/compose/webhooks/spe-doc-changed | name=ComposeSpeDocChangedWebhook | auth=anonymous | rateLimit=webhook-graph | tags=Compose | produces=200,202,400,401,429,500 | sizeLimit=default",
    ];

    [Fact(DisplayName = "Compose: the /api/compose/* route surface matches the approved snapshot")]
    public void ComposeRouteSurface_MatchesApprovedSnapshot()
    {
        var actual = DumpComposeSurface();

        actual.Should().Equal(
            ApprovedSurface,
            "the /api/compose/* route surface (paths, verbs, endpoint names, authorization posture, "
            + "rate-limit policies, tags, declared responses and request-size caps) is a client "
            + "contract — moving handlers between files must not change any of it");
    }

    [Fact(DisplayName = "Compose: every /api/compose route except the Graph webhook requires authorization (ADR-008)")]
    public void EveryComposeRoute_RequiresAuthorization_ExceptTheGraphWebhook()
    {
        // "Open" means EFFECTIVELY open: either the group's authorize metadata never reached the
        // endpoint, or an AllowAnonymous shadows it (AllowAnonymous wins at runtime, so checking
        // only for the presence of IAuthorizeData would miss it).
        var open = ComposeEndpoints()
            .Where(e => !e.Metadata.OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any()
                        || e.Metadata.OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any())
            .Select(Normalize)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        // The Graph change-notification receiver is unauthenticated BY GRAPH'S CONTRACT (its
        // validation handshake carries no token); it is defended by the HMAC signature filter plus a
        // constant-time clientState check instead. It is the ONLY permitted exception.
        open.Should().Equal(
            ["api/compose/webhooks/spe-doc-changed"],
            "ADR-008 authorization rides the route GROUP's RequireAuthorization() convention; any "
            + "other open /api/compose route means an endpoint escaped the group when it was mapped");
    }

    private List<RouteEndpoint> ComposeEndpoints() =>
        _factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => Normalize(e).StartsWith("api/compose", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private string[] DumpComposeSurface() =>
        ComposeEndpoints()
            .Select(Describe)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToArray();

    private static string Normalize(RouteEndpoint endpoint)
        => (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');

    private static string Describe(RouteEndpoint endpoint)
    {
        var verbs = string.Join("|", endpoint.Metadata
            .OfType<HttpMethodMetadata>()
            .SelectMany(m => m.HttpMethods)
            .OrderBy(v => v, StringComparer.Ordinal));

        var name = endpoint.Metadata.OfType<IEndpointNameMetadata>().FirstOrDefault()?.EndpointName ?? "(none)";

        var anonymous = endpoint.Metadata.OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any();
        var authorize = endpoint.Metadata.OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any();
        var auth = anonymous ? "anonymous" : authorize ? "authorize" : "NONE";

        // Read via reflection so the snapshot does not depend on the rate-limiting attribute's
        // assembly-level API shape (only on the policy name it carries).
        var rateLimit = endpoint.Metadata
            .Where(m => m.GetType().Name == "EnableRateLimitingAttribute")
            .Select(m => m.GetType().GetProperty("PolicyName")?.GetValue(m) as string ?? "(unnamed)")
            .FirstOrDefault() ?? "(none)";

        var tags = string.Join("+", endpoint.Metadata.OfType<ITagsMetadata>().SelectMany(t => t.Tags));

        var produces = string.Join(",", endpoint.Metadata
            .OfType<IProducesResponseTypeMetadata>()
            .Select(p => p.StatusCode)
            .OrderBy(s => s));

        // FR-S08 (r8 task 015): the two save routes raise the Kestrel request-body cap above the
        // document limit. Losing that metadata reinstates the transport-level rejection with no body.
        var sizeLimit = endpoint.Metadata.Any(m => m.GetType().Name == "RequestSizeLimitAttribute")
            ? "raised"
            : "default";

        return $"{verbs} {Normalize(endpoint)} | name={name} | auth={auth} | rateLimit={rateLimit}"
            + $" | tags={tags} | produces={produces} | sizeLimit={sizeLimit}";
    }
}

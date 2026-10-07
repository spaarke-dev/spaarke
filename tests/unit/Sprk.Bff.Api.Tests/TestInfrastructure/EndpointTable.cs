using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;

namespace Sprk.Bff.Api.Tests;

/// <summary>
/// "Is this route registered?" answered from the REAL app's endpoint table (unified-access-control-r2 task 167 f1).
///
/// <para><b>Why this exists.</b> Many endpoint tests proved a route exists by sending an ANONYMOUS request and asserting
/// the answer was not 404 ("401 means the route is there and wants a token"). Owner round 14 item 2 gave the BFF an
/// authorization FallbackPolicy (an authenticated user), which ASP.NET Core also applies to a request that matches NO
/// endpoint — so an anonymous request now answers 401 whether or not the route is registered, and those assertions
/// could no longer fail. Reading the endpoint table cannot be blurred by authentication: it is the same evidence the
/// route-retirement regression tests use for ABSENCE.</para>
///
/// <para>Matching is by HTTP method and route TEMPLATE (segments, literals, parameters and catch-alls); inline route
/// constraints such as <c>:guid</c> are not evaluated, so pass a path whose values satisfy them.</para>
/// </summary>
internal static class EndpointTable
{
    public static IReadOnlyList<RouteEndpoint> Endpoints(WebApplicationFactory<Program> factory)
        => factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

    /// <summary>True when an endpoint accepts <paramref name="method"/> and its template matches <paramref name="path"/>
    /// (any query string is ignored).</summary>
    public static bool Maps(WebApplicationFactory<Program> factory, string method, string path)
    {
        var pathOnly = new PathString(path.Split('?')[0]);
        foreach (var endpoint in Endpoints(factory))
        {
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            if (methods is { Count: > 0 } && !methods.Contains(method, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var matcher = new TemplateMatcher(new RouteTemplate(endpoint.RoutePattern), new RouteValueDictionary(endpoint.RoutePattern.Defaults));
            if (matcher.TryMatch(pathOnly, new RouteValueDictionary()))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Asserts that <paramref name="method"/> <paramref name="path"/> reaches a mapped endpoint.</summary>
    public static void AssertMapped(WebApplicationFactory<Program> factory, string method, string path)
        => Maps(factory, method, path).Should().BeTrue(
            $"{method} {path} must be a registered route. (An anonymous request answers 401 whether or not it is: the "
            + "authorization FallbackPolicy challenges unmatched requests too, so the endpoint table is the evidence.)");
}

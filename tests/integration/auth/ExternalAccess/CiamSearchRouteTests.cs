// ebilling-r1 task 028 (FR-16, D-55, D-63), kept by task 124: a firm (CIAM) user has no content or RAG search route.
//
// KEEP-path classification (ADR-038 section 2 path #1, security-auth): the test asserts an AUTHORIZATION decision for an external
// caller, read from the REAL app's endpoint table. Compiled into Sprk.Bff.Api.Tests by the existing tests/integration/auth glob.
//
// History: this class lived in ProcedureLibraryCiamDenyTests.cs beside two tests that proved a Procedure Library SPE document
// streamed no bytes to a firm user. Task 124 retired the Procedure Library container (a Procedure file is now a Stored File in the
// Finance file store), so those two went; the Stored File deny proof is ExternalInvoiceFileRoutesTests.ProcedureSourceFile_IsNeverListedOrServedToAnExternalCaller
// and the file-store route census guard (ExternalDocumentNarrowingGuardTests / ExternalFileStoreRouteCensusTests). This one is a general route fact and stays.

using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// No search route is reachable with a CIAM token: the only routes admitting the Ciam scheme are the <c>/api/v1/external</c>
/// group's, and none of them searches. Read from the REAL app's endpoint table and policies.
/// </summary>
[Trait("category", "external-access")]
public sealed class CiamSearchRouteTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public CiamSearchRouteTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task OnlyExternalGroupRoutesAdmitTheCiamScheme_AndNoneOfThemSearches_AndNoSearchRouteIsAnonymous()
    {
        var policies = _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var routes = EndpointTable.Endpoints(_factory);

        var ciamRoutes = new List<string>();
        var searchRoutes = new List<(string Route, bool Anonymous, bool AdmitsCiam)>();
        foreach (var endpoint in routes)
        {
            var route = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
            var anonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var policy = await AuthorizationPolicy.CombineAsync(
                policies,
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>())
                ?? await policies.GetFallbackPolicyAsync();
            var admitsCiam = policy?.AuthenticationSchemes.Contains(AuthSchemes.Ciam) == true;

            if (admitsCiam)
                ciamRoutes.Add(route);
            if (route.Contains("search", StringComparison.OrdinalIgnoreCase))
                searchRoutes.Add((route, anonymous, admitsCiam));
        }

        // Must-fire control: the detection sees the external group's Ciam scheme (otherwise every assertion below is vacuous).
        ciamRoutes.Should().Contain(r => r.StartsWith("/api/v1/external/", StringComparison.OrdinalIgnoreCase));
        searchRoutes.Should().NotBeEmpty("the BFF has search routes; finding none means the scan is broken");

        ciamRoutes.Should().OnlyContain(
            r => r.Equals("/api/v1/external", StringComparison.OrdinalIgnoreCase)
                 || r.StartsWith("/api/v1/external/", StringComparison.OrdinalIgnoreCase),
            "a CIAM token authenticates only on the external group (not on the workforce /api/v1/external-access group)");
        ciamRoutes.Should().NotContain(r => r.Contains("search", StringComparison.OrdinalIgnoreCase),
            "the external group has no content or RAG search route (D-63)");
        searchRoutes.Should().OnlyContain(s => !s.Anonymous && !s.AdmitsCiam,
            "every search route is authenticated on the workforce scheme, which rejects CIAM tokens");
    }
}

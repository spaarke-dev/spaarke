using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.SpeAdmin;

/// <summary>
/// Tests the <b>two independent authorization layers</b> guarding SPE Admin, and — more importantly —
/// the boundary between what each layer may CLAIM.
/// </summary>
/// <remarks>
/// <para>
/// What breaks if these are deleted: SPE Admin's whole reason for existing in R2 is that it reported
/// success when it was not succeeding, and named wrong causes when it failed. The denial path is where
/// that defect is most costly, because an admin acts on what the message says. A message naming the
/// wrong permission sends them to the wrong administrator, and the real fix never happens.
/// </para>
/// <para>
/// <b>The load-bearing fact these tests protect</b> (measured 2026-08-22, task 012): Entra directory
/// roles are <b>not visible to the BFF</b>. <c>SDAP-BFF-SPE-API</c> leaves
/// <c>groupMembershipClaims</c> unset, so no <c>wids</c> claim is emitted — confirmed with a positive
/// control, a real token issued to a confirmed member of the tenant's SharePoint Embedded
/// Administrator role carried no <c>wids</c> at all. Therefore <b>claim-absence does not imply
/// role-absence</b>, and layer 1 must never speak about directory roles. Several tests below exist
/// purely to keep a future "helpful" addition from reintroducing that.
/// </para>
/// <para>ADR-038 §2 path #1 (security-auth). No mocks, no host — the filter and the translator are
/// invoked directly.</para>
/// </remarks>
public class SpeAdminAuthorizationLayerTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static HttpContext ContextFor(params Claim[] claims)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/spe/containertypes";
        // "roles" is the role claim type so IsInRole() resolves the same claims Entra emits.
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth", "name", "roles"));
        return context;
    }

    private static async Task<object?> InvokeAsync(HttpContext context, bool nextCalled = false)
    {
        var filter = new SpeAdminAuthorizationFilter(logger: null);
        var invocation = EndpointFilterInvocationContext.Create(context);

        return await filter.InvokeAsync(
            invocation,
            _ => ValueTask.FromResult<object?>(nextCalled ? "PASSED-THROUGH" : "PASSED-THROUGH"));
    }

    private static ProblemHttpResult AsProblem(object? result)
    {
        result.Should().BeOfType<ProblemHttpResult>(
            "an authorization denial must be RFC 7807 ProblemDetails per ADR-019");
        return (ProblemHttpResult)result!;
    }

    private static readonly Claim SignedIn = new("oid", "8f14e45f-ceea-467a-9a2b-4d5a1b2c3d4e");

    // ─────────────────────────────────────────────────────────────────────────
    // Layer 1 — the Spaarke admin app role
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "SpeAdminAuthorization")]
    public async Task NoIdentity_Returns401_NotTheRoleMessage()
    {
        // An unauthenticated caller's permissions are UNKNOWN, not insufficient. Returning 403 here
        // would tell someone to go request a role when all they need to do is sign in.
        var result = await InvokeAsync(ContextFor());

        var problem = AsProblem(result);
        problem.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be("sdap.access.deny.unauthenticated");
        problem.ProblemDetails.Detail.Should().Contain("Sign in");
    }

    [Fact]
    [Trait("Category", "SpeAdminAuthorization")]
    public async Task NoIdentity_DoesNotMentionAnyRole()
    {
        var problem = AsProblem(await InvokeAsync(ContextFor()));

        problem.ProblemDetails.Detail.Should().NotContainAny(
            ["SharePoint Embedded Administrator", "Global Administrator", "SystemAdmin"]);
    }

    [Fact]
    [Trait("Category", "SpeAdminAuthorization")]
    public async Task SignedInWithoutAdminRole_Returns403_NamingTheSpaarkePermission()
    {
        var problem = AsProblem(await InvokeAsync(ContextFor(SignedIn)));

        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be("sdap.access.deny.role_insufficient");

        // Names what was actually checked and who grants it — the two things an admin needs in order
        // to act. "Access denied" (the previous text) supplied neither.
        problem.ProblemDetails.Detail.Should().Contain("Spaarke administrator");
        problem.ProblemDetails.Detail.Should().Contain("SystemAdmin");
    }

    /// <summary>THE test this class exists for.</summary>
    [Fact]
    [Trait("Category", "SpeAdminAuthorization")]
    public async Task SignedInWithoutAdminRole_DoesNotAssertTheUserLacksAnEntraDirectoryRole()
    {
        // The filter cannot observe directory roles (no `wids` — see class remarks). Any sentence
        // asserting the caller lacks one would be a guess, and would be told to genuine role holders.
        // The message MAY note the two are separate; it MUST NOT claim the caller lacks the Entra role.
        var problem = AsProblem(await InvokeAsync(ContextFor(SignedIn)));
        var detail = problem.ProblemDetails.Detail!;

        detail.Should().Contain("separate from",
            "the message should distinguish the two layers rather than conflate them");

        detail.Should().NotContainAny(
            ["you lack", "you do not have the SharePoint", "missing the SharePoint Embedded"],
            "layer 1 cannot see Entra directory roles, so it must not make claims about them");
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("SystemAdmin")]
    [Trait("Category", "SpeAdminAuthorization")]
    public async Task SignedInWithAnAdminAppRole_PassesThrough(string role)
    {
        var result = await InvokeAsync(ContextFor(SignedIn, new Claim("roles", role)));

        result.Should().Be("PASSED-THROUGH");
    }

    [Fact]
    [Trait("Category", "SpeAdminAuthorization")]
    public async Task AdminAppRole_PassesLayer1_EvenThoughNoDirectoryRoleClaimIsPresent()
    {
        // Pins the architecture: layer 1 gates on the Spaarke app role ALONE. A caller with no `wids`
        // claim — i.e. every caller, since the BFF never receives one — still passes here. Whether
        // Graph will serve them is layer 2's business, decided by Graph, not guessed at here.
        var context = ContextFor(SignedIn, new Claim("roles", "Admin"));
        context.User.FindFirst("wids").Should().BeNull("the BFF's tokens do not carry `wids`");

        (await InvokeAsync(context)).Should().Be("PASSED-THROUGH");
    }

    [Fact]
    [Trait("Category", "SpeAdminAuthorization")]
    public async Task AnUnrelatedRole_DoesNotSatisfyLayer1()
    {
        // Negative: a role that merely contains an admin-ish word must not be accepted.
        var result = await InvokeAsync(ContextFor(SignedIn, new Claim("roles", "AdminReadOnly")));

        AsProblem(result).StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Layer 2 — the Entra directory role, reported where Graph is authoritative
    // ─────────────────────────────────────────────────────────────────────────

    private static ProblemHttpResult GraphDenied(string? errorCode = "accessDenied") =>
        (ProblemHttpResult)ContainerTypeEndpoints.EntraRoleDeniedProblem(
            new SpaarkeStorageException(
                "Access denied", statusCode: 403, errorCode: errorCode, graphRequestId: "req-abc-123"),
            "Could not list container types.",
            traceId: "trace-xyz");

    [Fact]
    [Trait("Category", "SpeAdminAuthorization")]
    public void GraphDenial_IsReportedAs403_NotAsAServerError()
    {
        // Before task 012 all four container-type operations passed a hardcoded 500, so a permission
        // denial reached the admin as "Internal Server Error" — indistinguishable from a crash, and
        // the single most misleading thing this screen could say.
        GraphDenied().StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    [Trait("Category", "SpeAdminAuthorization")]
    public void GraphDenial_NamesTheRoleAndWhatItEnables()
    {
        var detail = GraphDenied().ProblemDetails.Detail!;

        detail.Should().Contain("SharePoint Embedded Administrator");
        detail.Should().Contain("Global Administrator");
        detail.Should().Contain("tenant-wide", "naming a role without saying what it grants is not actionable");
        detail.Should().Contain("separate from", "it must be distinguished from the Spaarke permission");
    }

    [Fact]
    [Trait("Category", "SpeAdminAuthorization")]
    public void GraphDenial_DoesNotAssertTheCallerLacksTheRole()
    {
        // Graph reports that the request was denied, NOT why. 403 also covers an unregistered
        // container type, a consent gap, and a config pointing at another tenant. Asserting
        // "you lack role X" from this signal is a guess — and wrong for anyone who holds it.
        var detail = GraphDenied().ProblemDetails.Detail!;

        detail.Should().Contain("cannot see whether you hold it");
        detail.Should().Contain("another cause",
            "the message must leave room for the other things that produce a Graph 403");
        detail.Should().NotContainAny(["you lack", "you are not a member", "you do not hold"]);
    }

    [Fact]
    [Trait("Category", "SpeAdminAuthorization")]
    public void GraphDenial_CarriesTheDiagnosticsAnOperatorQuotesToSupport()
    {
        var problem = GraphDenied().ProblemDetails;

        problem.Extensions["errorCode"].Should().Be("spe.containertypes.entra_role_required");
        problem.Extensions["graphRequestId"].Should().Be("req-abc-123");
        problem.Extensions["graphStatusCode"].Should().Be(403);
        problem.Extensions["traceId"].Should().Be("trace-xyz");
    }

    [Fact]
    [Trait("Category", "SpeAdminAuthorization")]
    public void GraphDenial_StillNamesThePrerequisite_WhenGraphSuppliesNoErrorCode()
    {
        // Graph does not always populate a code. The role guidance must not depend on it — otherwise
        // the least informative failures would also lose the one piece of actionable advice.
        var detail = GraphDenied(errorCode: null).ProblemDetails.Detail!;

        detail.Should().Contain("SharePoint Embedded Administrator");
    }

    [Fact]
    [Trait("Category", "SpeAdminAuthorization")]
    public void TheTwoLayersUseDistinctCodes_SoAClientCanTellThemApart()
    {
        // A single code for both would force the client back to one generic message, which is the
        // state task 012 removed.
        GraphDenied().ProblemDetails.Extensions["errorCode"]
            .Should().NotBe("sdap.access.deny.role_insufficient");
    }
}

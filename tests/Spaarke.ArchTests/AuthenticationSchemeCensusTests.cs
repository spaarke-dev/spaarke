using System.Reflection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// The authentication-scheme census (spaarke-SPA-external-access-platform-r3 task 045 · ADR-028 Amendment A9 · design
/// <c>notes/a9-bff-design.md</c> §2): the mechanical guard on the A9 security control. Read from the REAL BFF booted
/// in-process (<see cref="BootedBff"/>, Development and Production), never from source.
///
/// <para><b>What it pins.</b></para>
/// <list type="number">
///   <item>The registered authentication schemes are exactly <see cref="ExpectedSchemes"/>: the workforce default
///   (<c>Bearer</c>), <c>Ciam</c>, <c>WorkforceHome</c> and the named API-key scheme(s). A new scheme fails here until it is
///   reviewed and added.</item>
///   <item><c>WorkforceHome</c> — a token from a CUSTOMER'S home directory — is named by the
///   <see cref="AuthPolicies.ExternalCollaboration"/> policy and by NO other registered policy, nor by the default or the
///   fallback policy. Adding it to any other policy would let a home token reach a workforce (OBO) route.</item>
///   <item>The routes that carry <c>ExternalCollaboration</c> — and, separately, the routes whose effective
///   authorization policy (as the middleware builds it) admits <c>WorkforceHome</c> — are exactly
///   <see cref="PinnedExternalCollaborationRoutes"/>, every one under <c>/api/v1/external/</c>. A route added to the group
///   fails here until it is reviewed and added.</item>
/// </list>
///
/// <para><b>Owner decision 2026-10-13 (task 045 escalation, option A).</b> The three contact-side Grant Access routes
/// (<c>GET|POST /api/v1/external/contact-grants</c>, <c>POST /api/v1/external/contact-grants/revoke</c>,
/// <c>ContactGrantEndpoints.cs</c>) are on this group and stay reachable by a WorkforceHome-authenticated contact, like any
/// contact: <c>ContactGrantorAuthorizationFilter</c> still applies (own organization, at or below the caller's level). This
/// census does not claim they are unreachable; it pins them in the list so any NEW route on the group is reviewed.</para>
/// </summary>
[Collection(BootedBffCollection.Name)]
public sealed class AuthenticationSchemeCensusTests
{
    private readonly BootedBff _booted;

    public AuthenticationSchemeCensusTests(BootedBff booted) => _booted = booted;

    /// <summary>Every authentication scheme the BFF may register. The API-key scheme names are read from code.</summary>
    private static readonly IReadOnlySet<string> ExpectedSchemes = new HashSet<string>(StringComparer.Ordinal)
    {
        JwtBearerDefaults.AuthenticationScheme,
        AuthSchemes.Ciam,
        AuthSchemes.WorkforceHome,
        AuthSchemes.RagApiKey,
    };

    /// <summary>
    /// Every route that carries <see cref="AuthPolicies.ExternalCollaboration"/>, as "METHOD /route/pattern". Reviewed
    /// 2026-10-14 (task 045): every one is a collaboration route of the app-only external plane, behind
    /// <c>CallerPrincipalAuthorizationFilter</c>; the contact-grants rows are the owner-allowed management routes above.
    /// </summary>
    private static readonly IReadOnlySet<string> PinnedExternalCollaborationRoutes = new HashSet<string>(StringComparer.Ordinal)
    {
        "DELETE /api/v1/external/documents/{id:guid}",
        "DELETE /api/v1/external/events/{id:guid}",
        "DELETE /api/v1/external/servicerequests/{id:guid}",
        "DELETE /api/v1/external/todos/{id:guid}",
        "GET /api/v1/external/api/dataverse/metadata/{entityLogicalName}",
        "GET /api/v1/external/api/dataverse/record/{entityLogicalName}/{id:guid}",
        "GET /api/v1/external/api/dataverse/savedqueries/{entityLogicalName}",
        "GET /api/v1/external/api/dataverse/savedquery/{savedQueryId:guid}",
        "GET /api/v1/external/contact-grants",   // owner-allowed management route (2026-10-13, option A)
        "GET /api/v1/external/invoices/{id:guid}/documents",
        "GET /api/v1/external/invoices/{id:guid}/documents/{documentId:guid}/content",
        "GET /api/v1/external/invoices/{id:guid}/documents/{documentId:guid}/versions",
        "GET /api/v1/external/matters/{id:guid}/documents",
        "GET /api/v1/external/matters/{id:guid}/documents/{documentId:guid}/content",
        "GET /api/v1/external/matters/{id:guid}/documents/{documentId:guid}/versions",
        "GET /api/v1/external/matters/{id:guid}/events",
        "GET /api/v1/external/matters/{id:guid}/todos",
        "GET /api/v1/external/me",
        "GET /api/v1/external/me/entitlements",
        "GET /api/v1/external/notifications",
        "GET /api/v1/external/projects",
        "GET /api/v1/external/projects/{id:guid}",
        "GET /api/v1/external/projects/{id:guid}/contacts",
        "GET /api/v1/external/projects/{id:guid}/documents",
        "GET /api/v1/external/projects/{id:guid}/documents/{documentId:guid}/content",
        "GET /api/v1/external/projects/{id:guid}/documents/{documentId:guid}/versions",
        "GET /api/v1/external/projects/{id:guid}/events",
        "GET /api/v1/external/projects/{id:guid}/organizations",
        "GET /api/v1/external/projects/{id:guid}/todos",
        "GET /api/v1/external/servicerequests/{id:guid}/documents",
        "GET /api/v1/external/servicerequests/{id:guid}/documents/{documentId:guid}/content",
        "GET /api/v1/external/servicerequests/{id:guid}/documents/{documentId:guid}/versions",
        "GET /api/v1/external/workassignments/{id:guid}/documents",
        "GET /api/v1/external/workassignments/{id:guid}/documents/{documentId:guid}/content",
        "GET /api/v1/external/workassignments/{id:guid}/documents/{documentId:guid}/versions",
        "GET /api/v1/external/workassignments/{id:guid}/events",
        "GET /api/v1/external/workassignments/{id:guid}/todos",
        "GET /api/v1/external/{root}/{id:guid}/messages",
        "PATCH /api/v1/external/todos/{id:guid}",
        "POST /api/v1/external/api/dataverse/fetch",
        "POST /api/v1/external/contact-grants",   // owner-allowed management route (2026-10-13, option A)
        "POST /api/v1/external/contact-grants/revoke",   // owner-allowed management route (2026-10-13, option A)
        "POST /api/v1/external/invoices/{id:guid}/documents",
        "POST /api/v1/external/matters/{id:guid}/documents",
        "POST /api/v1/external/matters/{id:guid}/todos",
        "POST /api/v1/external/notifications/seen",
        "POST /api/v1/external/projects/{id:guid}/documents",
        "POST /api/v1/external/projects/{id:guid}/events",
        "POST /api/v1/external/projects/{id:guid}/todos",
        // r3 task 047 (ADR-028 A9): its OWN group, ExternalCollaboration WITHOUT CallerPrincipalAuthorizationFilter (the
        // caller is not yet resolved); the handler admits WorkforceHome only. The only caller of the WorkforceHome create
        // (RegistrationIsTheOnlyCallerOfTheWorkforceHomeWrites below).
        "POST /api/v1/external/registration",
        "POST /api/v1/external/servicerequests",
        "POST /api/v1/external/servicerequests/{id:guid}/documents",
        "POST /api/v1/external/workassignments/{id:guid}/documents",
        "POST /api/v1/external/workassignments/{id:guid}/todos",
        // r3 task 048 (ADR-028 A9): its OWN group, as 047; the handler admits WorkforceHome only and links only the
        // caller's own two identities (RegistrationIsTheOnlyCallerOfTheWorkforceHomeWrites below).
        "POST /api/v1/external/workforce/link",
        "POST /api/v1/external/{root}/{id:guid}/messages",
    };

    private const string ExternalPrefix = "/api/v1/external/";

    private IEnumerable<BootedApp> Apps => new[] { _booted.Development, _booted.Production };

    [Fact(DisplayName = "Task 045: the registered authentication schemes are exactly the census")]
    public async Task RegisteredSchemes_AreExactlyTheCensus()
    {
        // Every public scheme-name constant is a registered scheme too, so a new constant is reviewed here as well.
        var declared = typeof(AuthSchemes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
        Assert.All(declared, name => Assert.Contains(name, ExpectedSchemes));

        foreach (var app in Apps)
        {
            var registered = (await app.Services.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync())
                .Select(s => s.Name)
                .ToHashSet(StringComparer.Ordinal);

            Assert.True(
                registered.SetEquals(ExpectedSchemes),
                $"{app.Environment}: registered schemes [{string.Join(", ", registered.Order(StringComparer.Ordinal))}] "
                + $"differ from the census [{string.Join(", ", ExpectedSchemes.Order(StringComparer.Ordinal))}]. A new "
                + "authentication scheme widens who can sign in to the BFF: review it (ADR-028), then add it here.");
        }
    }

    [Fact(DisplayName = "Task 045: the default authenticate and challenge schemes stay the workforce Bearer scheme")]
    public async Task DefaultSchemes_AreTheWorkforceBearerScheme()
    {
        // Review K2: a token reaching any route that does not name its scheme is authenticated by the DEFAULT scheme.
        // Registering WorkforceHome (or any scheme) must never change either default.
        foreach (var app in Apps)
        {
            var schemes = app.Services.GetRequiredService<IAuthenticationSchemeProvider>();

            Assert.Equal(JwtBearerDefaults.AuthenticationScheme, (await schemes.GetDefaultAuthenticateSchemeAsync())?.Name);
            Assert.Equal(JwtBearerDefaults.AuthenticationScheme, (await schemes.GetDefaultChallengeSchemeAsync())?.Name);
        }
    }

    [Fact(DisplayName = "Task 045: WorkforceHome is named by the ExternalCollaboration policy and no other")]
    public async Task WorkforceHome_IsNamedByExternalCollaborationOnly()
    {
        foreach (var app in Apps)
        {
            using var scope = app.Services.CreateScope();
            var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

            var naming = new List<string>();
            foreach (var name in RouteAuthorizationGuardTests.RegisteredPolicyNames(options))
            {
                var policy = await provider.GetPolicyAsync(name);
                if (policy is not null && policy.AuthenticationSchemes.Contains(AuthSchemes.WorkforceHome))
                {
                    naming.Add($"policy '{name}'");
                }
            }

            if ((await provider.GetDefaultPolicyAsync()).AuthenticationSchemes.Contains(AuthSchemes.WorkforceHome))
            {
                naming.Add("the default policy");
            }

            if ((await provider.GetFallbackPolicyAsync())?.AuthenticationSchemes.Contains(AuthSchemes.WorkforceHome) == true)
            {
                naming.Add("the fallback policy");
            }

            Assert.True(
                naming.SequenceEqual(new[] { $"policy '{AuthPolicies.ExternalCollaboration}'" }),
                $"{app.Environment}: WorkforceHome must be named by the {AuthPolicies.ExternalCollaboration} policy ONLY "
                + "(ADR-028 A9: a customer's home-directory token reaches the app-only external plane and nothing else; no "
                + $"OBO). Named by: [{string.Join(", ", naming)}].");
        }
    }

    [Fact(DisplayName = "Task 045: the routes carrying ExternalCollaboration (and admitting WorkforceHome) are the pinned list")]
    public async Task RoutesCarryingExternalCollaboration_AreThePinnedList()
    {
        foreach (var app in Apps)
        {
            using var scope = app.Services.CreateScope();
            var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

            var namingPolicy = new SortedSet<string>(StringComparer.Ordinal);
            var admittingScheme = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var endpoint in app.Endpoints)
            {
                var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
                var key = $"{(methods is { Count: > 0 } ? string.Join(",", methods.Order(StringComparer.Ordinal)) : "*")} "
                          + endpoint.RoutePattern.RawText;

                if (endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                    .Any(d => d.Policy == AuthPolicies.ExternalCollaboration))
                {
                    namingPolicy.Add(key);
                }

                if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null &&
                    (await RouteAuthorizationGuardTests.MiddlewarePolicyAsync(provider, endpoint))?
                        .AuthenticationSchemes.Contains(AuthSchemes.WorkforceHome) == true)
                {
                    admittingScheme.Add(key);
                }
            }

            Assert.True(namingPolicy.Count > 0, $"{app.Environment}: no route carries {AuthPolicies.ExternalCollaboration} — the census read nothing");

            var outside = namingPolicy.Union(admittingScheme)
                .Where(k => !k[(k.IndexOf(' ') + 1)..].StartsWith(ExternalPrefix, StringComparison.Ordinal))
                .ToList();
            Assert.True(outside.Count == 0,
                $"{app.Environment}: routes outside {ExternalPrefix} carry {AuthPolicies.ExternalCollaboration} or admit "
                + $"WorkforceHome:\n  {string.Join("\n  ", outside)}");

            Assert.True(
                namingPolicy.SetEquals(PinnedExternalCollaborationRoutes),
                $"{app.Environment}: the routes carrying {AuthPolicies.ExternalCollaboration} changed. A WorkforceHome "
                + "(customer home-directory) contact reaches every route on this group: review the route, then update "
                + "PinnedExternalCollaborationRoutes.\n  added: "
                + string.Join("\n         ", namingPolicy.Except(PinnedExternalCollaborationRoutes))
                + "\n  removed: "
                + string.Join("\n           ", PinnedExternalCollaborationRoutes.Except(namingPolicy)));

            Assert.True(
                admittingScheme.SetEquals(PinnedExternalCollaborationRoutes),
                $"{app.Environment}: the routes whose effective policy admits WorkforceHome differ from the pinned "
                + "ExternalCollaboration list:\n  "
                + string.Join("\n  ", admittingScheme.Except(PinnedExternalCollaborationRoutes)
                    .Concat(PinnedExternalCollaborationRoutes.Except(admittingScheme).Select(r => "(missing) " + r))));
        }
    }

    // ── r3 task 047: only the registration route reaches the WorkforceHome create and bind ──────────────────────────────

    /// <summary>
    /// The WorkforceHome writes and the ONE type allowed to reference each (by its outermost declaring type, so closures and
    /// async state machines count as their owner). A WorkforceHome create needs <see cref="Sprk.Bff.Api.Infrastructure.ExternalAccess.NewContactDetails"/>'s
    /// home tenant and owner (<c>CreatePayload</c> refuses one without them), so setting either is the create's fingerprint.
    /// </summary>
    private static readonly (Type Owner, string Member, Type AllowedCaller)[] WorkforceHomeWrites =
    {
        (typeof(Sprk.Bff.Api.Infrastructure.ExternalAccess.ContactIdentityBinder), "RegisterHomeContactAsync",
            typeof(Sprk.Bff.Api.Api.ExternalAccess.ExternalRegistrationEndpoints)),
        (typeof(Sprk.Bff.Api.Infrastructure.ExternalAccess.IContactIdentityStore), "BindHomeOidAsync",
            typeof(Sprk.Bff.Api.Infrastructure.ExternalAccess.ContactIdentityBinder)),
        (typeof(Sprk.Bff.Api.Infrastructure.ExternalAccess.NewContactDetails), "set_ExternalTenantId",
            typeof(Sprk.Bff.Api.Infrastructure.ExternalAccess.ContactIdentityBinder)),
        (typeof(Sprk.Bff.Api.Infrastructure.ExternalAccess.NewContactDetails), "set_OwningTeamId",
            typeof(Sprk.Bff.Api.Infrastructure.ExternalAccess.ContactIdentityBinder)),
        // r3 task 048: the stored home link on a systemuser — only the link route, through the binder.
        (typeof(Sprk.Bff.Api.Infrastructure.ExternalAccess.ContactIdentityBinder), "LinkHomeSystemUserAsync",
            typeof(Sprk.Bff.Api.Api.ExternalAccess.ExternalWorkforceLinkEndpoints)),
        (typeof(Sprk.Bff.Api.Infrastructure.ExternalAccess.IContactIdentityStore), "WriteHomeLinkAsync",
            typeof(Sprk.Bff.Api.Infrastructure.ExternalAccess.ContactIdentityBinder)),
    };

    [Fact(DisplayName = "Task 047: only ExternalRegistrationEndpoints reaches the WorkforceHome create and bind")]
    public void RegistrationIsTheOnlyCallerOfTheWorkforceHomeWrites()
    {
        var assembly = typeof(Sprk.Bff.Api.Api.ExternalAccess.ExternalRegistrationEndpoints).Assembly;
        var references = IlCallScan.MethodReferences(assembly.GetTypes()).ToList();

        foreach (var (owner, member, allowed) in WorkforceHomeWrites)
        {
            var callers = references
                .Where(r => r.Target.DeclaringType == owner && r.Target.Name == member)
                .Select(r => IlCallScan.Outermost(r.Caller))
                .Distinct()
                .ToList();

            // Must fire: the scan sees the allowed caller (a scan that saw nothing would pass vacuously).
            Assert.True(callers.Contains(allowed),
                $"{owner.Name}.{member}: the IL scan did not see {allowed.Name} call it — the guard would be vacuous.");

            var others = callers.Where(c => c != allowed).Select(c => c.FullName).ToList();
            Assert.True(others.Count == 0,
                $"{owner.Name}.{member} is reached from [{string.Join(", ", others)}]. ADR-028 A9 (r3 task 047): only the "
                + "registration route (POST /api/v1/external/registration) may create or bind a WorkforceHome contact, and "
                + "(task 048) only the link route (POST /api/v1/external/workforce/link) may write a systemuser's stored home "
                + "link — both WorkforceHome scheme only. Route the new caller through the allowed type, or review it (UAC) "
                + "and widen this pin.");
        }
    }

    [Fact(DisplayName = "Task 047: the only-caller guard fires on a caller it does not allow")]
    public void TheOnlyCallerGuard_SeesACallerItDoesNotAllow()
    {
        // Must fire: the seeded reference below is visible to the same scan and attributed to a type no pin allows.
        var callers = IlCallScan.MethodReferences(new[] { typeof(OnlyCallerSeed) })
            .Where(r => r.Target.DeclaringType == typeof(Sprk.Bff.Api.Infrastructure.ExternalAccess.NewContactDetails)
                        && r.Target.Name == "set_OwningTeamId")
            .Select(r => IlCallScan.Outermost(r.Caller))
            .ToList();

        Assert.Equal(new[] { typeof(AuthenticationSchemeCensusTests) }, callers);
        Assert.DoesNotContain(WorkforceHomeWrites, w => callers.Contains(w.AllowedCaller));
    }

    /// <summary>A seeded disallowed caller for <see cref="TheOnlyCallerGuard_SeesACallerItDoesNotAllow"/>. Never run.</summary>
    private static class OnlyCallerSeed
    {
        internal static object Seed() => new Sprk.Bff.Api.Infrastructure.ExternalAccess.NewContactDetails("a", "b", null)
        {
            OwningTeamId = Guid.Empty,
        };
    }
}

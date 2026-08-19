// -----------------------------------------------------------------------------
// AuthModule.cs
//
// L2 CONTROL-PLANE AUTHENTICATION + AUTHORIZATION composition.
//
// SPEC / ADR references:
//   - spec.md FR-20:  audience api://spaarke-provisioning-controlplane-{env};
//                     JWT bearer; Operator + Reader app-role policies;
//                     L3 skill authenticates via az account get-access-token.
//   - ADR-028 MUST:   Use Microsoft.Identity.Web AddMicrosoftIdentityWebApi
//                     for inbound JWT validation (single canonical stack).
//                     Tenant-specific authority (NOT common/organizations);
//                     TenantId is single-issuer per platform-controlplane.bicep
//                     (effectiveJwtTenantId defaults to subscription().tenantId).
//   - ADR-010:        Feature-module extension methods to keep Program.cs at
//                     ~15 non-framework DI lines (NFR-07 god-class ratchet).
//   - ADR-032:        No conditional service branches inside this module — L2
//                     scaffold has no feature gates yet. If a future task
//                     introduces one, apply the Null-Object kill-switch
//                     pattern (P1/P2/P3) — do NOT emit if (flag) { AddService }
//                     without the else branch.
//
// SCOPE (this task): scaffold ONLY. Placeholder AzureAd config from
// appsettings.json binds without OIDC metadata fetch (Microsoft.Identity.Web
// lazy-loads the discovery doc on first token validation). /ping is anonymous
// so start-up succeeds without a live authority; POST /api/runs returns 401
// unauth from the AuthenticationMiddleware BEFORE OIDC discovery runs.
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

namespace Sprk.Provisioning.ControlPlane.Modules;

/// <summary>
/// DI registration for L2 JWT bearer authentication + Operator/Reader
/// authorization policies. Mirrors the BFF <c>AuthorizationModule</c>
/// composition pattern; deliberately owns its own audience configuration
/// (no BFF assembly dependency per ADR-010).
/// </summary>
public static class AuthModule
{
    /// <summary>Canonical policy names surfaced to endpoint <c>.RequireAuthorization("…")</c> calls.</summary>
    public static class Policies
    {
        /// <summary>Operator app-role. Required for mutating endpoints (POST /api/runs, …).</summary>
        public const string Operator = "Operator";

        /// <summary>Reader app-role. Grants read-only access; Operator implicitly satisfies Reader.</summary>
        public const string Reader = "Reader";
    }

    /// <summary>
    /// Registers <see cref="JwtBearerDefaults.AuthenticationScheme"/> via
    /// <see cref="MicrosoftIdentityWebApiAuthenticationBuilderExtensions.AddMicrosoftIdentityWebApi"/>
    /// against the <c>AzureAd</c> config section, and the Operator / Reader
    /// role-based policies against the <c>roles</c> claim.
    /// </summary>
    public static IServiceCollection AddAuthModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Inbound JWT validation (workforce Entra tenant — single-issuer per
        // platform-controlplane.bicep line 168: effectiveJwtTenantId defaults
        // to subscription().tenantId). The AzureAd config section carries
        // Instance / TenantId / Audience / ClientId — all bindable via KV
        // references in Bicep-emitted appsettings for the deployed L2 App
        // Service. In the scaffold the values are placeholders (see
        // appsettings.json); OIDC discovery is lazy so this does not fail
        // start-up.
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));

        // Operator / Reader app-role policies.
        //
        // Microsoft.Identity.Web maps the `roles` claim (Azure AD app-role
        // assignment) onto ClaimTypes.Role, so IsInRole("Operator") /
        // IsInRole("Reader") work directly. Operator satisfies Reader per the
        // FR-20 role hierarchy: mutating endpoints require Operator; read
        // endpoints require Reader OR Operator.
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.Operator, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole(Policies.Operator);
            });

            options.AddPolicy(Policies.Reader, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx =>
                    ctx.User.IsInRole(Policies.Operator) ||
                    ctx.User.IsInRole(Policies.Reader));
            });
        });

        return services;
    }
}

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Authorization;
using Sprk.Bff.Api.Infrastructure.Routing;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for authentication and authorization services (ADR-008, ADR-010).
///
/// <para><b>Inbound</b> — validating tokens presented TO the BFF: Azure AD JWT bearer (workforce),
/// the CIAM scheme for external users, named API-key schemes, the authorization handler, and all
/// authorization policies.</para>
///
/// <para><b>Outbound</b> — the credential the BFF presents when authenticating AS ITSELF to Entra:
/// <c>IClientAssertionProvider</c> (ADR-028 Amendment A4, added by auth-v4 task 020). Kept in this
/// module because it answers the same question as everything else here — how the BFF authenticates —
/// and because it serves Graph, Dataverse and the Copilot agent alike rather than belonging to any one
/// of them.</para>
/// </summary>
public static class AuthorizationModule
{
    // Idempotency guard for the JwtBearerOptions PostConfigure delegate (task 046).
    // The DI container can invoke PostConfigure<TOptions> delegates more than once when the
    // options instance is reconfigured (e.g. via IOptionsMonitor reload, named-vs-default
    // resolution, or repeat module registration). Without a guard, every invocation would
    // re-chain a new OnAuthenticationFailed handler on top of the previous one and re-merge
    // the audience set, masking the real source of audience-list mutations.
    // 0 = not yet configured, 1 = configured. Interlocked.CompareExchange guarantees only
    // the first caller wins, even if PostConfigure is called concurrently during host build.
    private static int _jwtPostConfigureApplied;

    /// <summary>
    /// Adds authentication (Azure AD JWT), authorization handler, and all authorization policies.
    /// </summary>
    public static IServiceCollection AddAuthorizationModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Azure AD JWT Bearer Token Validation (workforce tenant — sets the DEFAULT scheme)
        var authenticationBuilder = services
            .AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme);
        authenticationBuilder.AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));

        // External Identity (CIAM) JwtBearer scheme (external-access-platform-r1 task 020 · ADR-028 Amendment A1).
        // External users authenticate against the Entra External ID (CIAM) authority (*.ciamlogin.com) —
        // a DISTINCT issuer/audience from the workforce tenant that the default scheme cannot validate.
        // Appended to the EXISTING workforce authentication builder: NO new AddAuthentication is introduced,
        // so the workforce default scheme is preserved (spec FR-07). Task 021 pins AuthSchemes.Ciam onto the
        // /api/v1/external group. NOTE: the default-scheme PostConfigure<JwtBearerOptions> audience-merge below
        // is registered for JwtBearerDefaults.AuthenticationScheme ONLY, so it does NOT apply to this "Ciam"
        // named-options instance (spec FR-07 negative criterion — no additional guard required).
        authenticationBuilder.AddJwtBearer(AuthSchemes.Ciam, options =>
        {
            var ciam = configuration.GetSection("Ciam");
            var instance = ciam["Instance"]?.TrimEnd('/');
            var ciamTenantId = ciam["TenantId"];
            // External ID v2 authority: https://{subdomain}.ciamlogin.com/{tenantId}/v2.0
            options.Authority = $"{instance}/{ciamTenantId}/v2.0";
            options.Audience = ciam["Audience"];
            options.TokenValidationParameters.ValidateIssuer = true;
            options.TokenValidationParameters.ValidateAudience = true;
            options.TokenValidationParameters.ValidateLifetime = true;
            options.TokenValidationParameters.ValidateIssuerSigningKey = true;
        });

        // Named API key authentication schemes (task AUTHV2-045).
        // Replaces ad-hoc header validation on /api/ai/rag/enqueue-indexing. Each scheme binds
        // to its own configuration key so keys rotate independently with per-consumer blast radius.
        //
        // Endpoints opt-in via .RequireAuthorization(policyName); the policy below specifies
        // the scheme so the JwtBearer default doesn't have to be unset to use these.
        //
        // Configuration keys (Key Vault references in production):
        //   - Rag:ApiKey → RAG bulk indexing webhook access
        //
        // The BuilderAdminApiKey scheme (BuilderAdmin:ApiKey config) was REMOVED 2026-07-07
        // (redesign-r1 task 050) together with its sole consumer, the /api/admin/builder-scopes/*
        // endpoints — an orphaned ambient API-key scheme is attack surface with no caller.
        services.AddAuthentication()
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                AuthSchemes.RagApiKey,
                options =>
                {
                    options.ConfigKey = "Rag:ApiKey";
                    options.IdentityName = "rag-api-key";
                });

        // Accept tokens from M365 Copilot API Plugin (uses a different audience URI
        // issued via the Teams Developer Portal Entra SSO registration).
        // PostConfigure runs AFTER AddMicrosoftIdentityWebApi's own configuration,
        // ensuring our audience list isn't overwritten by the library.
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                // Idempotency guard (task 046): ensure the audience-merge + event-handler
                // wiring runs at most once per process. CompareExchange returns the original
                // value; if it was already 1, another caller already ran the delegate.
                if (Interlocked.CompareExchange(ref _jwtPostConfigureApplied, 1, 0) != 0)
                {
                    return;
                }

                var copilotAudience = configuration["AgentToken:CopilotAudience"];
                if (!string.IsNullOrEmpty(copilotAudience))
                {
                    var existingAudiences = options.TokenValidationParameters.ValidAudiences?.ToList()
                        ?? [];
                    var primaryAudience = options.TokenValidationParameters.ValidAudience;

                    var audiences = new HashSet<string>(existingAudiences, StringComparer.OrdinalIgnoreCase);
                    if (!string.IsNullOrEmpty(primaryAudience))
                        audiences.Add(primaryAudience);
                    audiences.Add(copilotAudience);

                    options.TokenValidationParameters.ValidAudiences = audiences;
                    // Clear singular to avoid conflicts with the plural list
                    options.TokenValidationParameters.ValidAudience = null;
                }

                // Loud warning if the audience list is empty after PostConfigure — every
                // token validation will fail in that state, and the symptom (401 on every
                // request) is far enough from the cause that we want a startup-time signal.
                // ILogger isn't reliably available here (PostConfigure runs during host build
                // before logging providers are guaranteed wired), so we emit to Console.Error
                // which is captured by App Service / container stdout pipelines.
                var finalAudiences = options.TokenValidationParameters.ValidAudiences?.ToList() ?? [];
                if (finalAudiences.Count == 0 && string.IsNullOrEmpty(options.TokenValidationParameters.ValidAudience))
                {
                    Console.Error.WriteLine(
                        "[CRITICAL] JWT audience list is empty after PostConfigure — all tokens will fail validation. " +
                        "Check AzureAd:ClientId and AgentToken:CopilotAudience configuration.");
                }

                // Log auth failures with token details for diagnosing Copilot token issues
                var existingOnFailed = options.Events?.OnAuthenticationFailed;
                options.Events ??= new JwtBearerEvents();
                options.Events.OnAuthenticationFailed = async context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("CopilotAuth");
                    var authHeader = context.HttpContext.Request.Headers.Authorization.ToString();
                    if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
                    {
                        try
                        {
                            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                            var jwt = handler.ReadJwtToken(authHeader["Bearer ".Length..]);
                            logger.LogWarning(
                                "JWT auth failed. Audience={Audience}, Issuer={Issuer}, AppId={AppId}, Error={Error}",
                                string.Join(",", jwt.Audiences),
                                jwt.Issuer,
                                jwt.Claims.FirstOrDefault(c => c.Type == "appid")?.Value ?? jwt.Claims.FirstOrDefault(c => c.Type == "azp")?.Value,
                                context.Exception?.Message);
                        }
                        catch { /* token unreadable — skip logging */ }
                    }
                    if (existingOnFailed != null) await existingOnFailed(context);
                };
            });

        // Register authorization handler (Scoped to match AuthorizationService dependency)
        services.AddScoped<IAuthorizationHandler, ResourceAccessHandler>();

        // ─────────────────────────────────────────────────────────────────────────────────────
        // OUTBOUND credential (auth-v4 task 020 · FR-B1 · ADR-028 A4)
        //
        // Everything above this line is INBOUND — validating tokens presented TO the BFF. This is
        // the first outbound registration in this module: the credential the BFF presents when it
        // authenticates as ITSELF to Entra (OBO exchanges and app-only confidential clients).
        //
        // Placed here rather than in SpaarkeCore because the provider is not a Dataverse concern —
        // task 022 wires it into GraphClientFactory, DataverseAccessDataSource, DataverseUserClient
        // and AgentTokenService alike. This module is where a reader looks for "how does the BFF
        // authenticate", and that question now has both an inbound and an outbound answer.
        //
        // NOT registered inline in Program.cs: ADR-010 forbids inline registrations, and the
        // TokenCredential precedent at Program.cs:46 is itself the anti-pattern, not the model.
        //
        // SINGLETON per ADR-028 A4's "reuse the instance" rule. It avoids rebuilding the underlying
        // managed-identity application and re-entering MSAL's cache per resolution.
        // NOT because a per-request instance would cost an IMDS round trip — that claim was measured
        // FALSE at this task's code-review gate (MSAL's managed-identity token cache is process-static
        // and keyed by identity, so fresh assertion objects issue no extra IMDS traffic). The
        // registration is right; the reason had to be corrected.
        //
        // Registered against the INTERFACE despite one implementation. ADR-010 prefers concrete
        // registration, but the seam is genuine and structural: Spaarke.Dataverse is the base layer
        // and cannot reference this assembly (LayerDependencyTests FR-14), so shared-library types
        // can only receive this by dependency inversion. A4 also names a second implementation —
        // a Key Vault certificate — as the sanctioned alternative where MI-FIC's same-tenant rule
        // cannot hold.
        //
        // NOTE: ADR010_DITests does NOT see this pair, and its ceiling was NOT raised. That test
        // scans typeof(Program).Assembly — the BFF only — and the interface is declared in
        // Spaarke.Dataverse, so a cross-assembly 1:1 seam is invisible to it. Real count is 151
        // against a ceiling of 153; raising it would have granted headroom for a future IN-assembly
        // interface to land unreviewed. The blind spot is booked onto task 061.
        // See ADR010_DITests.cs:164-173 for the verified numbers.
        services.AddSingleton<IClientAssertionProvider, ManagedIdentityAssertionProvider>();

        services.AddCredentialSelection(configuration);

        // BFF `tid`→environment routing (teams-app-r1 task 060 · ADR-028 A2 · spec FR-09).
        // Config-driven map (TenantRouting section; Key Vault refs in prod, mirroring AzureAd/Ciam)
        // routing an authenticated workforce `tid` to exactly ONE environment for the three
        // deployment models (Spaarke-hosted dedicated / customer-hosted / true SaaS). Deny-by-design:
        // an unmapped/ambiguous/malformed/absent `tid` is DENIED — never defaulted to any environment.
        // Singleton: the mapping is deploy-time static config, precomputed once into a tid lookup.
        // No Graph SDK / AI-internal types injected (BFF §10 / broker-only).
        // task 061 fail-fast sweep: bind under the canonical AddOptions chain with ValidateOnStart.
        // Behavior-neutral — the options class carries NO DataAnnotations (deny-by-design: an empty/absent
        // Tenants list denies every request at resolution time, so an absent "TenantRouting" section binds an
        // empty-but-valid object and boots). Wiring ValidateOnStart here makes any FUTURE annotation fail-fast
        // and brings the registration into the uniform standard (task 040 allowlist consumes the exemption list;
        // this type is NOT exempt — it validates on start).
        services.AddOptions<TenantEnvironmentRoutingOptions>()
            .Bind(configuration.GetSection(TenantEnvironmentRoutingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<ITenantEnvironmentRouter, TenantEnvironmentRouter>();

        // Authorization policies - granular operation-level policies matching SPE/Graph API operations
        services.AddAuthorization(options =>
        {
            // DriveItem Content Operations
            options.AddPolicy("canpreviewfiles", p =>
                p.Requirements.Add(new ResourceAccessRequirement("driveitem.preview")));
            options.AddPolicy("candownloadfiles", p =>
                p.Requirements.Add(new ResourceAccessRequirement("driveitem.content.download")));
            options.AddPolicy("canuploadfiles", p =>
                p.Requirements.Add(new ResourceAccessRequirement("driveitem.content.upload")));
            options.AddPolicy("canreplacefiles", p =>
                p.Requirements.Add(new ResourceAccessRequirement("driveitem.content.replace")));

            // DriveItem Metadata Operations
            options.AddPolicy("canreadmetadata", p =>
                p.Requirements.Add(new ResourceAccessRequirement("driveitem.get")));
            options.AddPolicy("canupdatemetadata", p =>
                p.Requirements.Add(new ResourceAccessRequirement("driveitem.update")));
            options.AddPolicy("canlistchildren", p =>
                p.Requirements.Add(new ResourceAccessRequirement("driveitem.list.children")));

            // DriveItem File Management
            options.AddPolicy("candeletefiles", p =>
                p.Requirements.Add(new ResourceAccessRequirement("driveitem.delete")));
            options.AddPolicy("canmovefiles", p =>
                p.Requirements.Add(new ResourceAccessRequirement("driveitem.move")));
            options.AddPolicy("cancopyfiles", p =>
                p.Requirements.Add(new ResourceAccessRequirement("driveitem.copy")));
            options.AddPolicy("cancreatefolders", p =>
                p.Requirements.Add(new ResourceAccessRequirement("driveitem.create.folder")));

            // DriveItem Sharing & Permissions
            options.AddPolicy("cansharefiles", p =>
                p.Requirements.Add(new ResourceAccessRequirement("driveitem.createlink")));
            options.AddPolicy("canmanagefilepermissions", p =>
                p.Requirements.Add(new ResourceAccessRequirement("driveitem.permissions.add")));

            // DriveItem Versioning
            options.AddPolicy("canviewversions", p =>
                p.Requirements.Add(new ResourceAccessRequirement("driveitem.versions.list")));
            options.AddPolicy("canrestoreversions", p =>
                p.Requirements.Add(new ResourceAccessRequirement("driveitem.versions.restore")));

            // Container Operations
            options.AddPolicy("canlistcontainers", p =>
                p.Requirements.Add(new ResourceAccessRequirement("container.list")));
            options.AddPolicy("cancreatecontainers", p =>
                p.Requirements.Add(new ResourceAccessRequirement("container.create")));
            options.AddPolicy("candeletecontainers", p =>
                p.Requirements.Add(new ResourceAccessRequirement("container.delete")));
            options.AddPolicy("canupdatecontainers", p =>
                p.Requirements.Add(new ResourceAccessRequirement("container.update")));
            options.AddPolicy("canmanagecontainerpermissions", p =>
                p.Requirements.Add(new ResourceAccessRequirement("container.permissions.add")));

            // Advanced Operations
            options.AddPolicy("cansearchfiles", p =>
                p.Requirements.Add(new ResourceAccessRequirement("driveitem.search")));
            options.AddPolicy("cantrackchanges", p =>
                p.Requirements.Add(new ResourceAccessRequirement("driveitem.delta")));
            options.AddPolicy("canmanagecompliancelabels", p =>
                p.Requirements.Add(new ResourceAccessRequirement("driveitem.sensitivitylabel.assign")));

            // Legacy Compatibility
            options.AddPolicy("canreadfiles", p =>
                p.Requirements.Add(new ResourceAccessRequirement("preview_file")));
            options.AddPolicy("canwritefiles", p =>
                p.Requirements.Add(new ResourceAccessRequirement("upload_file")));
            // "canmanagecontainers" REMOVED 2026-08-25 (spaarke-auth-v4-dataverse-MI task 090,
            // obligation 031-A) together with its only six consumers in DocumentsEndpoints.
            // It bound ResourceAccessRequirement("create_container") — a PER-RESOURCE requirement —
            // onto COLLECTION endpoints that carry no resource, so it could never be satisfied and
            // returned 403 to every caller. Leaving an unsatisfiable policy registered after its
            // consumers are gone is a trap: the next endpoint to reference it inherits a permanent 403.

            // Named API key policies (task AUTHV2-045).
            // Each policy is bound to a single auth scheme so the matching ApiKey handler runs
            // even when JwtBearer is the default. RequireAuthenticatedUser enforces a 401 when
            // the API key is missing or invalid (instead of silently falling back to JwtBearer).
            // BuilderAdminApiKey + BuilderAdminOrOAuth policies removed 2026-07-07 (redesign-r1
            // task 050) with the builder-scope admin endpoints.
            options.AddPolicy(AuthPolicies.RagApiKey, p =>
            {
                p.AuthenticationSchemes = new[] { AuthSchemes.RagApiKey };
                p.RequireAuthenticatedUser();
            });

            // External Secure Project Workspace policy (task 021 · ADR-028 Amendment A1).
            // Pins the "Ciam" JwtBearer scheme (task 020) onto the /api/v1/external group so ONLY
            // CIAM-issued tokens authenticate there — a workforce token (default scheme) does NOT,
            // and a CIAM token does NOT reach the workforce-default /api/v1/external-access group.
            // RequireAuthenticatedUser enforces a 401 rather than falling back to the default scheme.
            options.AddPolicy(AuthPolicies.CiamExternal, p =>
            {
                p.AuthenticationSchemes = new[] { AuthSchemes.Ciam };
                p.RequireAuthenticatedUser();
            });

            // Principal-agnostic collaboration policy for /api/v1/external (teams-app-r1 task 025 ·
            // R2 FR-22). Accepts BOTH the CIAM scheme AND the workforce default JwtBearer scheme so a
            // CIAM external contact AND a workforce (Teams-host) user authenticate on ONE endpoint set;
            // the CallerPrincipalAuthorizationFilter resolves either to a plane-agnostic principal. A
            // token validates against exactly one authority (only one scheme succeeds per request), so
            // the CIAM path is unchanged (FR-15) while the workforce plane is now served here.
            options.AddPolicy(AuthPolicies.ExternalCollaboration, p =>
            {
                p.AuthenticationSchemes = new[]
                {
                    AuthSchemes.Ciam,
                    JwtBearerDefaults.AuthenticationScheme
                };
                p.RequireAuthenticatedUser();
            });

            // Admin Policies
            options.AddPolicy("SystemAdmin", p =>
            {
                p.RequireAuthenticatedUser();
                p.RequireAssertion(context =>
                {
                    var hasAdminRole = context.User.IsInRole("Admin") ||
                                       context.User.IsInRole("SystemAdmin") ||
                                       context.User.HasClaim(c => c.Type == "roles" && c.Value == "Admin") ||
                                       context.User.HasClaim(c => c.Type == "roles" && c.Value == "SystemAdmin");

                    var hasAdminScope = context.User.HasClaim(c =>
                        c.Type == "http://schemas.microsoft.com/identity/claims/scope" &&
                        c.Value.Contains("admin", StringComparison.OrdinalIgnoreCase));

                    return hasAdminRole || hasAdminScope;
                });
            });
        });

        return services;
    }

    /// <summary>
    /// Ordered credential selection (auth-v4 task 021, FR-B2) — <b>the rollback mechanism</b>.
    ///
    /// <para>Called by <see cref="AddAuthorizationModule"/>, and kept in this file because it answers the
    /// same question as everything else here: how the BFF authenticates. It is a separate public method
    /// rather than an inline block for one reason — "an empty or invalid credential order fails fast
    /// <i>at startup</i>" is an acceptance criterion, and proving it requires booting a host against
    /// this exact registration. Asserting it against a re-declaration in a test would prove only that
    /// the test can configure options.</para>
    /// </summary>
    public static IServiceCollection AddCredentialSelection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Design §6 claims rollback at every phase is "a credential reorder or a slot swap back".
        // That is true only because the order below is read from configuration at runtime. Without
        // it, backing out of MI-FIC means a code change and a redeploy — during an incident, on the
        // OBO path, which fails closed for every user simultaneously (NFR-03/NFR-06).
        //
        // The canonical default is applied HERE rather than as a property initializer on the options
        // class, and only when the section is absent. Two reasons, both load-bearing:
        //   1. The configuration binder MERGES into an existing collection. With a pre-populated
        //      default, an operator narrowing the order to [ManagedIdentityFederated] — precisely the
        //      edit that proves the secret is unused — would silently get the trailing ClientSecret
        //      default back, and the secret they were eliminating would still be live.
        //   2. An EXPLICITLY empty list must fail fast (FR-B2 acceptance criterion). Defaulting inside
        //      the options class would make "empty" unreachable and quietly choose a credential for an
        //      operator who deliberately blanked the list to force the decision.
        // An ABSENT section still boots on the canonical order, so this is behavior-neutral for every
        // existing environment and test fixture (FAILURE-MODES AP-7: converting a silent fallback into
        // fail-fast has unbounded blast radius; the default is what bounds it).
        var credentialOrderConfigured = configuration
            .GetSection($"{CredentialSelectionOptions.SectionName}:Order").Exists();

        services.AddOptions<CredentialSelectionOptions>()
            .Bind(configuration.GetSection(CredentialSelectionOptions.SectionName))
            .PostConfigure(options =>
            {
                if (!credentialOrderConfigured)
                {
                    // ⚠️ ClientSecret STAYS IN THIS DEFAULT even though ADR-028 E-3 is CLOSED (task 033,
                    // 2026-08-24). This looks like an oversight. It is not — it was tried and reverted,
                    // and the reason is recorded here so it is not "fixed" again.
                    //
                    // Task 033 carried an obligation reading "also delete ClientSecret from the default
                    // order in AddCredentialSelection". Executing it literally BREAKS EVERY UNCONFIGURED
                    // ENVIRONMENT. CredentialSelectionOptionsValidator fails fast when
                    // ManagedIdentityFederated is the ONLY credential and no UAMI clientId is set —
                    // correctly, since there would be nothing to fall through to. But every test fixture
                    // in this repo and every local `dotnet run` has NO Graph:Credentials section AND no
                    // UAMI (a workstation has no route to IMDS), so a MI-FIC-only default makes all of
                    // them refuse to start. Caught by CredentialOrderingSeamTests; task 010 shipped this
                    // exact regression once already (FAILURE-MODES AP-7 — converting a silent fallback
                    // into fail-fast has unbounded blast radius; the default is what bounds it).
                    //
                    // The secret-free guarantee is delivered where it actually matters, by CONFIGURATION
                    // on the deployed environments: Graph:Credentials:Order = [ManagedIdentityFederated]
                    // plus RequireSecretFreeIdentity = true. That is strictly better than a narrower
                    // default, because it is explicit, auditable, per-environment, and it cannot silently
                    // disable local development. And it is not weaker: the secret is deleted from app
                    // settings AND Key Vault, so on a deployed environment this default has nothing left
                    // to resolve even if it is reached.
                    options.Order = new List<string>
                    {
                        nameof(CredentialKind.ManagedIdentityFederated),
                        nameof(CredentialKind.ClientSecret),   // see the note above before removing
                    };
                }
            })
            .ValidateOnStart();

        // Relational rules (unknown kind, duplicate, cert-without-name, single-failure suppression)
        // cannot be expressed as data annotations — see the validator.
        services.AddSingleton<IValidateOptions<CredentialSelectionOptions>, CredentialSelectionOptionsValidator>();

        // FR-B4 (task 023) — the UAMI / app-registration conflation guard. Registered against the same
        // options type so it runs under the SAME ValidateOnStart above: the two identities are only
        // meaningful together with the credential order (rule 3 fires only when MI-FIC has no fallback),
        // so validating them separately would need the order duplicated in two places.
        services.AddSingleton<IValidateOptions<CredentialSelectionOptions>, IdentityConfigurationValidator>();

        // SINGLETON, and it owns the ONE confidential-client cache. Task 022 collapses the three
        // per-class CCA caches (DataverseAccessDataSource, DataverseUserClient, AgentTokenService) onto
        // it — which is what CLOSES task 011's time-boxed A4 exception. If this cache is not authored
        // here, that exception becomes permanent.
        //
        // Constructed by factory rather than by convention so the two OPTIONAL dependencies stay
        // optional: SecretClient is registered by SpeAdminModule (only when a Key Vault URI is
        // configured) and TimeProvider is not registered at all in this app. GetService returns null
        // for both, which the provider handles; constructor injection would instead fail to resolve.
        services.AddSingleton(sp => new OrderedCredentialClientProvider(
            sp.GetRequiredService<IOptions<CredentialSelectionOptions>>(),
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILogger<OrderedCredentialClientProvider>>(),
            sp.GetService<IClientAssertionProvider>(),
            sp.GetService<Azure.Security.KeyVault.Secrets.SecretClient>(),
            sp.GetService<TimeProvider>()));

        // Registered against the interface AND concretely, resolving to the SAME singleton.
        // Only ONE consumer needs the interface: DataverseAccessDataSource lives in Spaarke.Dataverse,
        // the base layer, which cannot reference this assembly (LayerDependencyTests, FR-14) and can
        // therefore only receive the provider by dependency inversion. The three BFF-side consumers
        // inject the concrete type, which ADR-010 prefers — do not add the interface to call sites that
        // do not need it.
        //
        // ADR010_DITests is NOT affected and its ceiling is NOT raised, for the same verified reason as
        // IClientAssertionProvider above: that test scans typeof(Program).Assembly, and
        // IConfidentialClientProvider is declared in Spaarke.Dataverse, so a cross-assembly 1:1 seam is
        // invisible to it. See ADR010_DITests.cs:164-173.
        services.AddSingleton<IConfidentialClientProvider>(
            sp => sp.GetRequiredService<OrderedCredentialClientProvider>());
        return services;
    }
}

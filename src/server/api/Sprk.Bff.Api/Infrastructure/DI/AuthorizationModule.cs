using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Authorization;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
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
    // Idempotency guard for the default scheme's Copilot-audience PostConfigure delegate (task 046; per options
    // instance since ISS-001 / #1623).
    // The delegate chains an OnAuthenticationFailed handler on top of the existing one, so running it twice on the
    // SAME JwtBearerOptions instance would wrap the event twice (every failure logged twice). The guard is therefore
    // keyed on the options INSTANCE: the first pass on an instance wins and any later pass on it is a no-op, while a
    // new instance (a second host in the process, or an instance rebuilt after a configuration reload) is configured
    // in full. It was a process-wide flag until ISS-001, which left every instance after the first without the
    // Copilot audience (Copilot tokens answered 401) and without the CopilotAuth failure logging.
    // ConditionalWeakTable holds its keys weakly, so a discarded instance is not kept alive by this table, and TryAdd
    // is atomic, so only one caller can claim an instance even if PostConfigure ran concurrently.
    private static readonly ConditionalWeakTable<JwtBearerOptions, object> _copilotMergeApplied = new();
    private static readonly object _copilotMergeMarker = new();

    /// <summary>
    /// Adds authentication (Azure AD JWT), authorization handler, and all authorization policies.
    /// </summary>
    public static IServiceCollection AddAuthorizationModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // FR-23 (task 010 review fix): refuse to start when the CIAM tenant IS the workforce tenant. The
        // default-scheme CIAM guard below rejects every token whose tid equals Ciam:TenantId, so identical
        // values would turn it into a whole-stamp 401 outage for every workforce token. Fail at registration
        // (deterministic startup refusal), not on the first request.
        ThrowIfCiamTenantEqualsWorkforceTenant(configuration);

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
            // FR-22 (external-access-platform-r3 task 010): the per-customer audience is accepted in BOTH
            // forms — the bare app id (v2 access tokens) and api://{appId} (v1 / identifier-URI tokens) —
            // derived from the ONE configured Ciam:Audience. Set as ValidAudiences rather than
            // options.Audience so the framework does not collapse it back to a single value. Empty
            // Ciam:Audience yields an empty list, so no audience is accepted (today's behaviour, not widened).
            options.TokenValidationParameters.ValidAudiences = BuildCiamValidAudiences(ciam["Audience"]);
            options.TokenValidationParameters.ValidateIssuer = true;
            options.TokenValidationParameters.ValidateAudience = true;
            options.TokenValidationParameters.ValidateLifetime = true;
            options.TokenValidationParameters.ValidateIssuerSigningKey = true;
        });

        AddWorkforceHomeScheme(services, authenticationBuilder, configuration);

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
                // Idempotency guard (task 046; per options instance since ISS-001): the audience merge and the
                // event-handler wrap run at most once for THIS instance. TryAdd is false when the instance was
                // already configured, so a second pass over it is a no-op; every other instance is configured in full.
                if (!_copilotMergeApplied.TryAdd(options, _copilotMergeMarker))
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

        // Task 077 (ADR-028 A9): the default scheme accepts the BFF app's audience in both forms, so the app's switch
        // to requestedAccessTokenVersion = 2 (aud = the bare app id for every caller) does not turn into a 401 on every
        // workforce surface. A SEPARATE PostConfigure, NOT inside the Copilot delegate above: that one has its own
        // per-instance guard and only merges when AgentToken:CopilotAudience is set. Only the audience changes
        // (AddWorkforceAudiences).
        // An ID token issued to a client signing in AS the BFF app also carries aud = the bare app id; it is refused here
        // (077 review F4) because it carries neither azpacr (v2) nor appidacr (v1), which every Entra access token has.
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                var clientId = configuration["AzureAd:ClientId"];
                AddWorkforceAudiences(options.TokenValidationParameters, clientId);

                options.Events ??= new JwtBearerEvents();
                var existingOnTokenValidated = options.Events.OnTokenValidated;
                options.Events.OnTokenValidated = async context =>
                {
                    if (IsIdTokenForApp(context.SecurityToken, context.Principal, clientId))
                    {
                        context.HttpContext.RequestServices.GetService<ILoggerFactory>()?
                            .CreateLogger("WorkforceSchemeAudienceGuard")
                            .LogWarning(
                                "[AUTH] sdap.auth.deny.id_token_on_workforce_scheme: a token with aud = the BFF app id and no azpacr/appidacr (an ID token) was presented to the default (workforce) scheme and was rejected.");
                        context.Fail("Only access tokens are accepted by the workforce scheme.");
                        return;
                    }

                    if (existingOnTokenValidated != null) await existingOnTokenValidated(context);
                };
            });

        // FR-23 (external-access-platform-r3 task 010 · auth review R1): the DEFAULT (workforce) scheme
        // rejects every CIAM token — iss containing ciamlogin.com, or tid equal to Ciam:TenantId — the same
        // rule CallerPrincipalResolver.DeterminePlane uses to classify the CIAM plane. It runs after the
        // library's own validation and does not depend on whether that validation would also have rejected
        // the token (spec FR-23: no reliance on library issuer defaults). The tid rule also covers a token the
        // CIAM tenant issues through login.microsoftonline.com. Ciam:TenantId is read from configuration.
        // A third rule — the tenant path segment of iss equals Ciam:TenantId — is a strict superset that also
        // catches a tid-less token and CIAM custom URL domains (relies on Ciam:TenantId being set).
        //
        // Deliberately a SEPARATE PostConfigure, NOT inside the Copilot delegate above: a security guard must not
        // share that delegate's early return (_copilotMergeApplied) or its AgentToken:CopilotAudience condition.
        // It applies to every instance of the default scheme on its own.
        //
        // Effect on the shared ExternalCollaboration policy (Ciam + default): a CIAM token still
        // authenticates there through the Ciam scheme; only the default scheme's attempt fails.
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                var ciamTenantId = configuration["Ciam:TenantId"];
                options.Events ??= new JwtBearerEvents();
                var existingOnTokenValidated = options.Events.OnTokenValidated;
                options.Events.OnTokenValidated = async context =>
                {
                    // Both the validated token and the principal are read, so neither claim mapping nor
                    // the token-handler type can hide the issuer or tenant from the guard.
                    string? rawTenantId = null;
                    if (context.SecurityToken is Microsoft.IdentityModel.JsonWebTokens.JsonWebToken jwt &&
                        jwt.TryGetPayloadValue<string>("tid", out var tid))
                    {
                        rawTenantId = tid;
                    }

                    var tokenIssuer = context.SecurityToken?.Issuer;
                    var principalIssuer = context.Principal?.FindFirst("iss")?.Value;
                    var resolvedTenantId = TenantResolution.ResolveTenantId(context.Principal);

                    string? denyReason = null;
                    if (IsCiamIssuer(tokenIssuer) || IsCiamIssuer(principalIssuer))
                    {
                        denyReason = "issuer is a ciamlogin.com authority";
                    }
                    else if (IsCiamTenant(resolvedTenantId, ciamTenantId) ||
                             IsCiamTenant(rawTenantId, ciamTenantId))
                    {
                        denyReason = "tid equals Ciam:TenantId";
                    }
                    else if (IsCiamIssuerTenant(tokenIssuer, ciamTenantId) ||
                             IsCiamIssuerTenant(principalIssuer, ciamTenantId))
                    {
                        // Tenant path segment of iss == Ciam:TenantId — catches a tid-less token issued via
                        // login.microsoftonline.com/{ciamTid}/v2.0 and CIAM custom URL domains.
                        denyReason = "issuer tenant equals Ciam:TenantId";
                    }

                    if (denyReason is not null)
                    {
                        // Issuer HOST and tid are logged as structured properties (neither is a secret).
                        // The token and headers are never logged.
                        var issuerHost = Uri.TryCreate(tokenIssuer ?? principalIssuer, UriKind.Absolute, out var issuerUri)
                            ? issuerUri.Host
                            : null;
                        var loggedTid = rawTenantId ?? resolvedTenantId;
                        if (loggedTid is { Length: > 64 }) loggedTid = loggedTid[..64];
                        context.HttpContext.RequestServices.GetService<ILoggerFactory>()?
                            .CreateLogger("WorkforceSchemeCiamGuard")
                            .LogWarning(
                                "[AUTH] sdap.auth.deny.ciam_token_on_workforce_scheme: a CIAM token was presented to the default (workforce) scheme and was rejected ({Reason}). IssuerHost={IssuerHost} Tid={Tid}",
                                denyReason,
                                issuerHost,
                                loggedTid);
                        context.Fail("CIAM tokens are not accepted by the workforce scheme.");
                        return;
                    }

                    if (existingOnTokenValidated != null) await existingOnTokenValidated(context);
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
            // Fail closed at runtime: an endpoint that declares neither an authorization requirement nor
            // AllowAnonymous gets "authenticated user" instead of "anyone" (owner round 14 item 2).
            ApplyFallbackPolicy(options);

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
            //
            // "canwritefiles" + "canreadfiles" REMOVED 2026-09-07 (unified-access-control-r2 task 083)
            // for the same reason "canmanagecontainers" was removed below, and by the same precedent.
            //
            // "canwritefiles" bound ResourceAccessRequirement("upload_file"). Its only two consumers
            // were PUT /api/drives/{driveId}/upload and DELETE /api/drives/{driveId}/items/{itemId} in
            // Api/DocumentsEndpoints.cs, both deleted by task 083 — so the policy is orphaned. It is
            // deleted rather than left registered because it is not merely unused, it is WRONG-DOMAIN:
            // ResourceAccessHandler.ExtractResourceId accepts containerId / driveId / documentId
            // interchangeably and then resolves the value as sprk_documents({id}), so the policy
            // authorizes a DRIVE id against DOCUMENT rights. The next endpoint author to reach for a
            // plausibly-named "canwritefiles" would inherit that, and inherit it silently — the
            // failure is a wrong ALLOW/DENY, not a startup error.
            //
            // "canreadfiles" (preview_file) was ALREADY orphaned before task 083 — zero consumers
            // anywhere in src/ — and carries the identical wrong-resource-domain shape. It is removed
            // in the same edit rather than left as the one surviving member of a retired pair.
            //
            // If a files read/write policy is genuinely needed again, it must evaluate the OWNING
            // RECORD via the task 075/076 resolver, not an SPE key lifted off the route.
            //
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
            //
            // ADR-028 A9 (task 045): also the WorkforceHome scheme — a workforce contact's home-directory token.
            // This is the ONLY policy that names it (app-only external plane, no OBO). A Spaarke-tenant or CIAM
            // token fails WorkforceHome's allowlist, so it still authenticates through exactly one scheme here.
            // AuthenticationSchemeCensusTests pins the scheme set, this policy, and the routes that carry it.
            options.AddPolicy(AuthPolicies.ExternalCollaboration, p =>
            {
                p.AuthenticationSchemes = new[]
                {
                    AuthSchemes.Ciam,
                    JwtBearerDefaults.AuthenticationScheme,
                    AuthSchemes.WorkforceHome
                };
                p.RequireAuthenticatedUser();
            });

            // Admin Policies
            //
            // "SystemAdmin" admits ONLY the Admin or SystemAdmin app role. Until unified-access-control-r2
            // task 165 it also admitted any token whose delegated scope claim CONTAINED the substring
            // "admin" (case-insensitive, matched against the whole space-separated scp string), so a future
            // scope such as "Files.ReadAdmin" — or any API's scope name with "admin" in it — would have
            // unlocked every operator surface sharing this policy (/api/admin/jobs, /api/admin/membership,
            // /api/admin/record-matching, /api/ai/rag/admin). No delegated scope the BFF registrations expose
            // contains "admin" (verified 2026-10-03), so the branch admitted nobody legitimate; an administrator is
            // identified by the app role, the same signal SpeAdminAuthorizationFilter checks.
            options.AddPolicy("SystemAdmin", p =>
            {
                p.RequireAuthenticatedUser();
                p.RequireAssertion(context =>
                    context.User.IsInRole("Admin") ||
                    context.User.IsInRole("SystemAdmin") ||
                    context.User.HasClaim(c => c.Type == "roles" && c.Value == "Admin") ||
                    context.User.HasClaim(c => c.Type == "roles" && c.Value == "SystemAdmin"));
            });
        });

        return services;
    }

    private const string ApiUriPrefix = "api://";

    /// <summary>The Entra instance A9 names: the authority, and the only host an accepted home token's <c>iss</c> may carry.</summary>
    private const string EntraInstance = "https://login.microsoftonline.com/";

    /// <summary>
    /// Why the WorkforceHome allowlist refused a token (logged as <c>Reason</c>; never sent to the caller).
    /// </summary>
    internal static class WorkforceHomeDeny
    {
        /// <summary>The validated token is not a <see cref="Microsoft.IdentityModel.JsonWebTokens.JsonWebToken"/>; nothing can be read from it.</summary>
        internal const string TokenUnreadable = "token_unreadable";

        /// <summary>No <c>tid</c>, or one that is not a non-zero GUID.</summary>
        internal const string TenantMissing = "tid_missing_or_malformed";

        /// <summary><c>tid</c> = <c>AzureAd:TenantId</c>: a Spaarke-tenant token belongs to the default scheme.</summary>
        internal const string SpaarkeTenant = "tid_is_spaarke_tenant";

        /// <summary><c>AzureAd:TenantId</c> is not a non-zero GUID, so the "not the Spaarke tenant" rule cannot be applied: fail closed.</summary>
        internal const string SpaarkeTenantUnknown = "spaarke_tenant_id_not_configured";

        /// <summary><c>tid</c> = <c>Ciam:TenantId</c>: a CIAM token belongs to the Ciam scheme.</summary>
        internal const string CiamTenant = "tid_is_ciam_tenant";

        /// <summary><c>WorkforceIdentity:CustomerTenantIds</c> is empty: no home directory is accepted.</summary>
        internal const string TenantListEmpty = "customer_tenant_list_empty";

        /// <summary><c>tid</c> is not one of this stamp's customer home directories.</summary>
        internal const string TenantNotListed = "tid_not_listed";

        /// <summary><c>iss</c> is not exactly <c>https://login.microsoftonline.com/{tid}/v2.0</c> (a v1 issuer included).</summary>
        internal const string IssuerNotExactV2 = "iss_not_exact_v2_for_tid";

        /// <summary><c>idtyp</c> is present and is not <c>user</c>.</summary>
        internal const string AppToken = "app_token";

        /// <summary>No delegated <c>scp</c> containing <c>user_impersonation</c> (an app token without <c>idtyp</c> included).</summary>
        internal const string ScopeMissing = "user_impersonation_scope_missing";

        /// <summary>No <c>acct</c> claim: membership is never inferred.</summary>
        internal const string AcctMissing = "acct_missing";

        /// <summary><c>acct</c> is not <c>0</c> (a guest of the home directory, or an unrecognised value).</summary>
        internal const string AcctNotMember = "acct_not_member";
    }

    /// <summary>
    /// ADR-028 Amendment A9 (external-access-platform-r3 task 045 · design <c>notes/a9-bff-design.md</c> §2, §9): the
    /// <see cref="AuthSchemes.WorkforceHome"/> JwtBearer scheme, for a workforce contact's token issued by their customer's
    /// home directory to the stamp's BFF app.
    /// </summary>
    /// <remarks>
    /// <para><b>Library layer (Microsoft.Identity.Web, unmodified).</b> Registered with <c>AddMicrosoftIdentityWebApi</c>
    /// on the <c>organizations</c> authority, so Identity Web sets the multitenant <c>AadIssuerValidator</c> (the issuer must
    /// be the v1 or v2 Entra issuer for the token's own <c>tid</c>) and calls <c>EnableAadSigningKeyIssuerValidation()</c>.
    /// The issuer validator is NOT replaced and <c>ValidIssuers</c> is NOT set (design §9). The audience is the stamp BFF
    /// app only, in both forms (bare id or <c>api://{id}</c>) — never the Copilot audience, which the default scheme's
    /// merge adds to the default scheme only. No token acquisition is enabled: there is no OBO on this plane.</para>
    /// <para><b>A9 allowlist (<c>OnTokenValidated</c>, after the library).</b> See
    /// <see cref="WorkforceHomeDenyReason"/>. <c>WorkforceIdentity:CustomerTenantIds</c> is read through
    /// <c>IOptionsMonitor</c> per request, so a change applies without a restart; an empty list rejects every token.</para>
    /// <para><b>Where it is accepted.</b> Only the <see cref="AuthPolicies.ExternalCollaboration"/> policy names it.</para>
    /// <para><b>Plane marker.</b> The identity it produces has <see cref="System.Security.Claims.ClaimsIdentity.AuthenticationType"/>
    /// = <see cref="AuthSchemes.WorkforceHome"/>, set by the handler from <c>TokenValidationParameters</c> — no token
    /// content can forge it — so principal resolution (task 046) branches on the authenticating scheme, not on <c>iss</c>.</para>
    /// </remarks>
    private static void AddWorkforceHomeScheme(
        IServiceCollection services,
        Microsoft.AspNetCore.Authentication.AuthenticationBuilder authenticationBuilder,
        IConfiguration configuration)
    {
        var stampBffAppId = configuration["AzureAd:ClientId"];

        authenticationBuilder.AddMicrosoftIdentityWebApi(
            jwtBearer =>
            {
                // The same two-form derivation FR-22 uses for the Ciam audience; empty → no audience accepted.
                jwtBearer.TokenValidationParameters.ValidAudiences = BuildCiamValidAudiences(stampBffAppId);
                jwtBearer.TokenValidationParameters.AuthenticationType = AuthSchemes.WorkforceHome;
            },
            identity =>
            {
                identity.Instance = EntraInstance;
                identity.TenantId = "organizations";
                identity.ClientId = stampBffAppId;
            },
            AuthSchemes.WorkforceHome);

        services.PostConfigure<JwtBearerOptions>(AuthSchemes.WorkforceHome, options =>
        {
            var spaarkeTenantId = configuration["AzureAd:TenantId"];
            var ciamTenantId = configuration["Ciam:TenantId"];
            options.Events ??= new JwtBearerEvents();
            var existingOnTokenValidated = options.Events.OnTokenValidated;
            options.Events.OnTokenValidated = async context =>
            {
                var customerTenantIds = context.HttpContext.RequestServices
                    .GetService<IOptionsMonitor<WorkforceIdentityOptions>>()?
                    .CurrentValue.ParsedCustomerTenantIds() ?? new HashSet<Guid>();

                var reason = WorkforceHomeDenyReason(
                    context.SecurityToken, customerTenantIds, spaarkeTenantId, ciamTenantId, out var tokenTenantId);
                if (reason is not null)
                {
                    // A Spaarke-tenant or CIAM token reaches this scheme on every ExternalCollaboration request it makes
                    // (the policy tries each scheme); that refusal is expected, so it is logged at Debug, not Warning.
                    var expected = reason is WorkforceHomeDeny.SpaarkeTenant or WorkforceHomeDeny.CiamTenant;
                    if (tokenTenantId is { Length: > 64 }) tokenTenantId = tokenTenantId[..64];
                    context.HttpContext.RequestServices.GetService<ILoggerFactory>()?
                        .CreateLogger("WorkforceHomeScheme")
                        .Log(
                            expected ? LogLevel.Debug : LogLevel.Warning,
                            "[AUTH] sdap.auth.deny.workforce_home: a token was rejected by the WorkforceHome scheme ({Reason}). Tid={Tid}",
                            reason,
                            tokenTenantId);
                    context.Fail("The token is not accepted by the WorkforceHome scheme.");
                    return;
                }

                if (existingOnTokenValidated != null) await existingOnTokenValidated(context);
            };
        });
    }

    /// <summary>
    /// The A9 allowlist for a token the library has already validated on the WorkforceHome scheme (signature with
    /// signing-key issuer validation, the Entra issuer chain, the stamp audience, lifetime). Returns null when the token
    /// is accepted, otherwise a <see cref="WorkforceHomeDeny"/> reason. Every claim is read from the validated token
    /// itself, so inbound claim mapping cannot hide or rename one. Accepts only when ALL hold:
    /// <list type="number">
    ///   <item><c>tid</c> is a non-zero GUID, <c>AzureAd:TenantId</c> is a non-zero GUID (else every token is rejected), is neither <c>AzureAd:TenantId</c> nor <c>Ciam:TenantId</c>, and is in
    ///   <paramref name="customerTenantIds"/> (empty rejects all);</item>
    ///   <item><c>iss</c> equals <c>https://login.microsoftonline.com/{tid}/v2.0</c> exactly (ordinal);</item>
    ///   <item>a user token: <c>idtyp</c>, if present, is <c>user</c>, and <c>scp</c> contains <c>user_impersonation</c>;</item>
    ///   <item><c>acct</c> = <c>0</c>.</item>
    /// </list>
    /// </summary>
    internal static string? WorkforceHomeDenyReason(
        Microsoft.IdentityModel.Tokens.SecurityToken? token,
        IReadOnlySet<Guid> customerTenantIds,
        string? spaarkeTenantId,
        string? ciamTenantId,
        out string? tokenTenantId)
    {
        ArgumentNullException.ThrowIfNull(customerTenantIds);
        tokenTenantId = null;

        if (token is not Microsoft.IdentityModel.JsonWebTokens.JsonWebToken jwt)
        {
            return WorkforceHomeDeny.TokenUnreadable;
        }

        if (!jwt.TryGetPayloadValue<string>("tid", out var tid) ||
            !Guid.TryParseExact(tid, "D", out var tenant) ||
            tenant == Guid.Empty)
        {
            return WorkforceHomeDeny.TenantMissing;
        }

        tokenTenantId = tid;

        // Fail closed: the "tid is not the Spaarke tenant" rule needs the Spaarke tenant. When AzureAd:TenantId is unset,
        // a placeholder or otherwise not a non-zero GUID the rule cannot be applied, so no home token is accepted (it was
        // skipped, i.e. fail open, before review K3). No startup refusal: the scheme just rejects until configured.
        if (!Guid.TryParse(spaarkeTenantId?.Trim(), out var spaarke) || spaarke == Guid.Empty)
        {
            return WorkforceHomeDeny.SpaarkeTenantUnknown;
        }

        if (tenant == spaarke)
        {
            return WorkforceHomeDeny.SpaarkeTenant;
        }

        if (Guid.TryParse(ciamTenantId?.Trim(), out var ciam) && tenant == ciam)
        {
            return WorkforceHomeDeny.CiamTenant;
        }

        if (customerTenantIds.Count == 0)
        {
            return WorkforceHomeDeny.TenantListEmpty;
        }

        if (!customerTenantIds.Contains(tenant))
        {
            return WorkforceHomeDeny.TenantNotListed;
        }

        if (!string.Equals(jwt.Issuer, $"{EntraInstance}{tid}/v2.0", StringComparison.Ordinal))
        {
            return WorkforceHomeDeny.IssuerNotExactV2;
        }

        if (jwt.TryGetClaim("idtyp", out var idtyp) && !string.Equals(idtyp.Value, "user", StringComparison.Ordinal))
        {
            return WorkforceHomeDeny.AppToken;
        }

        if (!jwt.TryGetPayloadValue<string>("scp", out var scp) ||
            !scp.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("user_impersonation", StringComparer.Ordinal))
        {
            return WorkforceHomeDeny.ScopeMissing;
        }

        if (!jwt.TryGetClaim(WorkforceCallerClaims.AcctClaim, out var acct) ||
            string.IsNullOrWhiteSpace(acct.Value))
        {
            return WorkforceHomeDeny.AcctMissing;
        }

        return acct.Value.Trim() == "0" ? null : WorkforceHomeDeny.AcctNotMember;
    }

    /// <summary>
    /// FR-23 startup refusal: throws when <c>Ciam:TenantId</c> and <c>AzureAd:TenantId</c> both parse as GUIDs
    /// and are equal (compared as <see cref="Guid"/>, so case / format / padding do not matter). Unset, blank,
    /// placeholder (<c>#{…}#</c>) or any non-GUID value on either side never triggers it. The message names
    /// both keys and no value.
    /// </summary>
    internal static void ThrowIfCiamTenantEqualsWorkforceTenant(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (Guid.TryParse(configuration["Ciam:TenantId"]?.Trim(), out var ciam) &&
            Guid.TryParse(configuration["AzureAd:TenantId"]?.Trim(), out var workforce) &&
            ciam == workforce)
        {
            throw new InvalidOperationException(
                "Ciam:TenantId must not equal AzureAd:TenantId. The default (workforce) scheme rejects every token " +
                "whose tid is the CIAM tenant, so identical values would reject every workforce token. " +
                "Set Ciam:TenantId to the External ID (CIAM) tenant, not the workforce tenant.");
        }
    }

    /// <summary>
    /// FR-23: true when the first path segment of the issuer URL (<c>https://host/{tid}/v2.0</c>,
    /// <c>https://sts.windows.net/{tid}/</c>) is a GUID equal to <c>Ciam:TenantId</c>. Covers a CIAM-tenant token
    /// with no <c>tid</c> claim issued through login.microsoftonline.com, and CIAM custom URL domains. A custom
    /// CIAM URL domain relies on <c>Ciam:TenantId</c> being set (provisioning H4b always sets it); always false
    /// when it is unset or not a GUID.
    /// </summary>
    internal static bool IsCiamIssuerTenant(string? issuer, string? ciamTenantId)
    {
        if (!Guid.TryParse(ciamTenantId?.Trim(), out var ciam) ||
            string.IsNullOrWhiteSpace(issuer) ||
            !Uri.TryCreate(issuer, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var segment = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return Guid.TryParse(segment, out var issuerTenant) && issuerTenant == ciam;
    }

    /// <summary>
    /// FR-22: the audiences the <c>Ciam</c> scheme accepts, derived from the one configured
    /// <c>Ciam:Audience</c> — the value itself plus its other form (<c>{appId}</c> ⇄ <c>api://{appId}</c>).
    /// </summary>
    /// <remarks>
    /// The other form is derived only when the app-id part is a GUID: a bare app id is always a GUID, so a
    /// non-GUID identifier URI (or an unreplaced deployment placeholder) gets no derived form and keeps
    /// today's single-audience behaviour rather than widening to a value no token legitimately carries.
    /// Empty or whitespace configuration returns an empty list — no audience is accepted (fail closed).
    /// </remarks>
    internal static IReadOnlyList<string> BuildCiamValidAudiences(string? configuredAudience)
    {
        if (string.IsNullOrWhiteSpace(configuredAudience))
        {
            return Array.Empty<string>();
        }

        var audience = configuredAudience.Trim();
        if (audience.StartsWith(ApiUriPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var appId = audience[ApiUriPrefix.Length..];
            return Guid.TryParseExact(appId, "D", out _)
                ? new[] { audience, appId }
                : new[] { audience };
        }

        return Guid.TryParseExact(audience, "D", out _)
            ? new[] { audience, ApiUriPrefix + audience }
            : new[] { audience };
    }

    /// <summary>
    /// Task 077 (ADR-028 A9): adds the BFF app's own audience in both forms — the bare app id (v2 access tokens) and
    /// <c>api://{appId}</c> (v1) — to the default scheme, merged with what is already accepted
    /// (<c>AzureAd:Audience</c>, the Copilot audience).
    /// </summary>
    /// <remarks>
    /// A9 sets <c>requestedAccessTokenVersion</c> = 2 on the BFF app, which makes <c>aud</c> the bare app id for every
    /// caller; <c>AzureAd:Audience</c> holds only the <c>api://</c> form. Only a GUID client id adds anything, so an
    /// unset value or an unreplaced placeholder leaves the parameters exactly as they were. Only the audience
    /// properties change; idempotent.
    /// </remarks>
    internal static void AddWorkforceAudiences(Microsoft.IdentityModel.Tokens.TokenValidationParameters parameters, string? clientId)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!Guid.TryParseExact(clientId?.Trim(), "D", out _))
        {
            return;
        }

        var audiences = new HashSet<string>(parameters.ValidAudiences ?? [], StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(parameters.ValidAudience))
        {
            audiences.Add(parameters.ValidAudience);
        }

        audiences.UnionWith(BuildCiamValidAudiences(clientId));

        parameters.ValidAudiences = audiences;
        // Clear the singular so the plural list is the one source, as the Copilot merge does.
        parameters.ValidAudience = null;
    }

    /// <summary>
    /// Task 077 review F4: true when <paramref name="token"/> has <c>aud</c> = the bare BFF app id but neither
    /// <c>azpacr</c> (v2 access tokens) nor <c>appidacr</c> (v1 access tokens), i.e. an ID token issued to a client that
    /// signs in as the BFF app. Tokens with any other audience are not judged here (their behaviour is unchanged).
    /// </summary>
    internal static bool IsIdTokenForApp(
        Microsoft.IdentityModel.Tokens.SecurityToken? token, System.Security.Claims.ClaimsPrincipal? principal, string? clientId)
    {
        if (!Guid.TryParseExact(clientId?.Trim(), "D", out var appId))
        {
            return false;
        }

        var bareAudience = appId.ToString("D");
        if (token is Microsoft.IdentityModel.JsonWebTokens.JsonWebToken jwt)
        {
            return jwt.Audiences.Any(a => string.Equals(a, bareAudience, StringComparison.OrdinalIgnoreCase))
                && !jwt.TryGetClaim("azpacr", out _) && !jwt.TryGetClaim("appidacr", out _);
        }

        // Any other token type (a legacy handler): judge from the principal's claims, as the FR-23 guard does, so the
        // refusal cannot fail open on the handler type (077 re-check K2).
        return principal is not null
            && principal.FindAll("aud").Any(c => string.Equals(c.Value, bareAudience, StringComparison.OrdinalIgnoreCase))
            && principal.FindFirst("azpacr") is null && principal.FindFirst("appidacr") is null;
    }

    /// <summary>
    /// FR-23: true when the issuer is a CIAM (<c>*.ciamlogin.com</c>) authority — the issuer half of
    /// <c>CallerPrincipalResolver.DeterminePlane</c>.
    /// </summary>
    internal static bool IsCiamIssuer(string? issuer)
        => !string.IsNullOrEmpty(issuer) &&
           issuer.Contains("ciamlogin.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// FR-23: true when <paramref name="tenantId"/> equals the configured <c>Ciam:TenantId</c> — the tenant
    /// half of <c>CallerPrincipalResolver.DeterminePlane</c>. Always false when <c>Ciam:TenantId</c> is unset,
    /// so an unconfigured CIAM tenant never matches an absent or blank <c>tid</c>.
    /// </summary>
    internal static bool IsCiamTenant(string? tenantId, string? ciamTenantId)
        => !string.IsNullOrWhiteSpace(ciamTenantId) &&
           !string.IsNullOrWhiteSpace(tenantId) &&
           string.Equals(tenantId.Trim(), ciamTenantId.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The authorization <b>FallbackPolicy</b>: an authenticated user (owner round 14 item 2,
    /// unified-access-control-r2 task 167).
    ///
    /// <para><b>What it does.</b> ASP.NET Core applies the fallback policy to every request whose endpoint
    /// carries no authorization metadata — no <c>RequireAuthorization(...)</c>, no <c>[Authorize]</c>, no
    /// <c>AllowAnonymous()</c> — and to a request that matches no endpoint at all. Without it such an endpoint
    /// is callable by anyone, signed in or not. With it, the same omission answers 401: the runtime fails
    /// CLOSED. An explicit <c>AllowAnonymous()</c> still opens a route (the liveness probes, the signed
    /// webhooks, the public config bundle), and an explicit <c>RequireAuthorization(...)</c> still decides on
    /// its own policy — the fallback never applies to an endpoint that declares either.</para>
    ///
    /// <para><b>Why the build-time guard stays.</b> <c>RouteAuthorizationGuardTests.NoRouteIsAnonymousByOmission</c>
    /// still fails the build on any route lacking <c>RequireAuthorization*</c> or <c>AllowAnonymous</c> on its
    /// route or group chain. The fallback protects the runtime if the guard is ever bypassed; the guard keeps
    /// every route's intent DECLARED and reviewable. Neither replaces the other (owner round 14 item 2).</para>
    ///
    /// <para><b>Consequence for an unknown path.</b> A request that matches no route is challenged with 401
    /// unless it is authenticated; an authenticated caller still gets 404. A route-ABSENCE test therefore
    /// asserts 404 with a bearer token (or reads the endpoint table), never 404 without one. CORS preflights
    /// are unaffected: <c>UseCors</c> answers them before <c>UseAuthorization</c> runs.</para>
    ///
    /// <para>Public so a test can build a host on THIS policy rather than on a re-declaration of it
    /// (<c>tests/integration/auth/UnifiedAccessControl/AuthorizationFallbackPolicyTests.cs</c>) — the same
    /// reasoning as <see cref="AddCredentialSelection"/> below.</para>
    /// </summary>
    public static void ApplyFallbackPolicy(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
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
        // configured) and TimeProvider is registered only by feature modules (TryAddSingleton in
        // DocumentsModule, MembershipModule, CommunicationModule, InsightsIngestModule, WorkspaceModule,
        // ExternalAccessModule). GetService returns null for either when absent, which the provider
        // handles; constructor injection would instead fail to resolve. (This comment previously said
        // TimeProvider "is not registered at all in this app" — stale; corrected 2026-09-10, task 097.)
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

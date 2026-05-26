using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Authorization;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for authentication and authorization services (ADR-008, ADR-010).
/// Registers Azure AD JWT bearer authentication, authorization handler, and all authorization policies.
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
        // Azure AD JWT Bearer Token Validation
        services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));

        // Named API key authentication schemes (task AUTHV2-045).
        // Replaces ad-hoc header validation on /api/admin/builder-scopes/import and
        // /api/ai/rag/enqueue-indexing. Each scheme binds to its own configuration key so
        // the keys can be rotated independently and blast-radius is isolated per consumer.
        //
        // Endpoints opt-in via .RequireAuthorization(policyName); the policy below specifies
        // the scheme so the JwtBearer default doesn't have to be unset to use these.
        //
        // Configuration keys (Key Vault references in production):
        //   - BuilderAdmin:ApiKey  → admin scope import CLI/script access
        //   - Rag:ApiKey           → RAG bulk indexing webhook access
        services.AddAuthentication()
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                AuthSchemes.BuilderAdminApiKey,
                options =>
                {
                    options.ConfigKey = "BuilderAdmin:ApiKey";
                    options.IdentityName = "builder-admin-api-key";
                })
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
            options.AddPolicy("canmanagecontainers", p =>
                p.Requirements.Add(new ResourceAccessRequirement("create_container")));

            // Named API key policies (task AUTHV2-045).
            // Each policy is bound to a single auth scheme so the matching ApiKey handler runs
            // even when JwtBearer is the default. RequireAuthenticatedUser enforces a 401 when
            // the API key is missing or invalid (instead of silently falling back to JwtBearer).
            options.AddPolicy(AuthPolicies.BuilderAdminApiKey, p =>
            {
                p.AuthenticationSchemes = new[] { AuthSchemes.BuilderAdminApiKey };
                p.RequireAuthenticatedUser();
            });
            options.AddPolicy(AuthPolicies.RagApiKey, p =>
            {
                p.AuthenticationSchemes = new[] { AuthSchemes.RagApiKey };
                p.RequireAuthenticatedUser();
            });

            // Composite policy: BuilderAdmin endpoints accept EITHER OAuth bearer (Azure AD) OR
            // the BuilderAdmin API key. Preserves prior dual-auth behavior while delegating the
            // API key validation to the named scheme. Useful for endpoints that need to support
            // both interactive (Dataverse/PCF) and automation (CLI/script) callers.
            options.AddPolicy(AuthPolicies.BuilderAdminOrOAuth, p =>
            {
                p.AuthenticationSchemes = new[]
                {
                    JwtBearerDefaults.AuthenticationScheme,
                    AuthSchemes.BuilderAdminApiKey,
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
}

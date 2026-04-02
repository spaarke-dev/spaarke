using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Sprk.Bff.Api.Infrastructure.Authorization;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for authentication and authorization services (ADR-008, ADR-010).
/// Registers Azure AD JWT bearer authentication, authorization handler, and all authorization policies.
/// </summary>
public static class AuthorizationModule
{
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

        // Accept tokens from M365 Copilot API Plugin (uses a different audience URI
        // issued via the Teams Developer Portal Entra SSO registration).
        // PostConfigure runs AFTER AddMicrosoftIdentityWebApi's own configuration,
        // ensuring our audience list isn't overwritten by the library.
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
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

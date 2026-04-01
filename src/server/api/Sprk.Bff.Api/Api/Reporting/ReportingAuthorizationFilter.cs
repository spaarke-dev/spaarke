using System.Security.Claims;
using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Api.Reporting;

/// <summary>
/// Extension methods for adding ReportingAuthorizationFilter to endpoints.
/// Follows ADR-008 endpoint filter pattern.
/// </summary>
public static class ReportingAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds reporting module authorization to an endpoint or route group.
    /// Enforces module gate, user authentication, and security role checks in order.
    /// Stores the resolved privilege level in HttpContext.Items for downstream use.
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddReportingAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var logger = context.HttpContext.RequestServices.GetService<ILogger<ReportingAuthorizationFilter>>();
            var filter = new ReportingAuthorizationFilter(configuration, logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Endpoint filter that gates all Reporting endpoints behind three sequential checks:
/// module enablement, user authentication, and Dataverse security role.
/// </summary>
/// <remarks>
/// <para>
/// Follows ADR-008: Use endpoint filters for authorization — no global auth middleware.
/// Each reporting route group applies this filter via <see cref="ReportingAuthorizationFilterExtensions"/>.
/// </para>
///
/// <para>
/// <strong>Authorization Flow:</strong>
/// <list type="number">
///   <item>Module gate — reads <c>Reporting:ModuleEnabled</c> from IConfiguration (backed by
///   the <c>sprk_ReportingModuleEnabled</c> Dataverse environment variable). Returns 404 Not Found
///   if false or missing — this hides the module entirely rather than revealing it as forbidden.</item>
///   <item>Authentication — verifies the request carries a valid authenticated user identity.
///   Returns 401 Unauthorized if the user is not authenticated.</item>
///   <item>Security role — verifies the user has the <c>sprk_ReportingAccess</c> role claim.
///   Returns 403 Forbidden if the role is absent.</item>
///   <item>Privilege extraction — determines whether the user is a Viewer, Author, or Admin
///   based on additional role claims, then stores the result in
///   <c>HttpContext.Items["ReportingPrivilegeLevel"]</c> for endpoint handlers to consume.</item>
/// </list>
/// </para>
///
/// <para>
/// <strong>Consuming privilege in endpoint handlers:</strong>
/// <code>
/// var privilege = (ReportingPrivilegeLevel)httpContext.Items["ReportingPrivilegeLevel"]!;
/// if (privilege &lt; ReportingPrivilegeLevel.Author) return Results.Forbid();
/// </code>
/// </para>
///
/// <para>
/// <strong>Configuration keys:</strong>
/// <list type="bullet">
///   <item><c>Reporting:ModuleEnabled</c> — maps to env var <c>Reporting__ModuleEnabled</c>
///   or Dataverse env var <c>sprk_ReportingModuleEnabled</c></item>
/// </list>
/// </para>
/// </remarks>
public class ReportingAuthorizationFilter : IEndpointFilter
{
    /// <summary>IConfiguration key that enables/disables the Reporting module.</summary>
    internal const string ModuleEnabledConfigKey = "Reporting:ModuleEnabled";

    /// <summary>Dataverse security role claim required to access reporting endpoints.</summary>
    internal const string ReportingAccessRole = "sprk_ReportingAccess";

    /// <summary>Dataverse security role claim granting report authoring privileges.</summary>
    internal const string ReportingAuthorRole = "sprk_ReportingAuthor";

    /// <summary>Dataverse security role claim granting workspace/catalog admin privileges.</summary>
    internal const string ReportingAdminRole = "sprk_ReportingAdmin";

    /// <summary>
    /// HttpContext.Items key under which the resolved <see cref="ReportingPrivilegeLevel"/> is stored.
    /// </summary>
    public const string PrivilegeLevelItemKey = "ReportingPrivilegeLevel";

    // Deny codes following pattern: {domain}.{area}.{action}.{reason}
    private const string DenyCodeRoleInsufficient = "sdap.reporting.deny.role_insufficient";

    private readonly IConfiguration _configuration;
    private readonly ILogger<ReportingAuthorizationFilter>? _logger;

    public ReportingAuthorizationFilter(
        IConfiguration configuration,
        ILogger<ReportingAuthorizationFilter>? logger = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // ── Check 1: Module Gate ─────────────────────────────────────────────────────────
        // Read "Reporting:ModuleEnabled" from IConfiguration. This key is backed by the
        // sprk_ReportingModuleEnabled Dataverse environment variable (via env var injection)
        // or by appsettings.json for local development.
        // Return 404 Not Found (not 403) to hide the module from unauthorized environments.
        var moduleEnabled = _configuration.GetValue<bool?>(ModuleEnabledConfigKey);
        if (moduleEnabled is not true)
        {
            _logger?.LogDebug(
                "[REPORTING-AUTH] Module gate blocked request: {ConfigKey}={Value}. Path={Path}",
                ModuleEnabledConfigKey,
                moduleEnabled?.ToString() ?? "(not set)",
                httpContext.Request.Path);

            return Results.Problem(
                statusCode: 404,
                title: "Not Found",
                detail: "The requested resource was not found.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
        }

        // ── Check 2: User Authentication ─────────────────────────────────────────────────
        // Base JWT authentication middleware must already have run. Verify identity is present.
        var user = httpContext.User;
        var userId = user.FindFirst("oid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId) || user.Identity?.IsAuthenticated is not true)
        {
            _logger?.LogWarning(
                "[REPORTING-AUTH] Unauthenticated request blocked. Path={Path}",
                httpContext.Request.Path);

            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        // ── Check 3: Security Role ────────────────────────────────────────────────────────
        // Verify the user has the sprk_ReportingAccess Dataverse security role.
        // Role claims are injected into the JWT by the Spaarke auth pipeline as custom "roles"
        // claims. Check both IsInRole and HasClaim to cover different token shapes.
        var hasAccess = user.IsInRole(ReportingAccessRole)
            || user.HasClaim(c => c.Type == "roles" && c.Value == ReportingAccessRole)
            || user.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == ReportingAccessRole);

        if (!hasAccess)
        {
            _logger?.LogWarning(
                "[REPORTING-AUTH] Access DENIED: User {UserId} lacks {Role}. Path={Path}",
                userId,
                ReportingAccessRole,
                httpContext.Request.Path);

            return ProblemDetailsHelper.Forbidden(DenyCodeRoleInsufficient);
        }

        // ── Check 4: Extract Privilege Level ─────────────────────────────────────────────
        // Determine the highest privilege the user holds and store it in HttpContext.Items
        // so individual endpoint handlers can enforce fine-grained authoring/admin checks
        // without re-reading claims on every handler.
        var privilege = DeterminePrivilegeLevel(user);

        httpContext.Items[PrivilegeLevelItemKey] = privilege;

        _logger?.LogDebug(
            "[REPORTING-AUTH] Access GRANTED: UserId={UserId}, Privilege={Privilege}. Path={Path}",
            userId,
            privilege,
            httpContext.Request.Path);

        return await next(context);
    }

    /// <summary>
    /// Determines the highest <see cref="ReportingPrivilegeLevel"/> the user holds.
    /// Admin supersedes Author, which supersedes Viewer.
    /// </summary>
    private static ReportingPrivilegeLevel DeterminePrivilegeLevel(ClaimsPrincipal user)
    {
        // Check Admin first (highest privilege)
        if (user.IsInRole(ReportingAdminRole)
            || user.HasClaim(c => c.Type == "roles" && c.Value == ReportingAdminRole)
            || user.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == ReportingAdminRole))
        {
            return ReportingPrivilegeLevel.Admin;
        }

        // Check Author
        if (user.IsInRole(ReportingAuthorRole)
            || user.HasClaim(c => c.Type == "roles" && c.Value == ReportingAuthorRole)
            || user.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == ReportingAuthorRole))
        {
            return ReportingPrivilegeLevel.Author;
        }

        // Default to Viewer (the sprk_ReportingAccess check already passed in Check 3)
        return ReportingPrivilegeLevel.Viewer;
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Infrastructure.Authentication;

namespace Sprk.Bff.Api.Api.Mcp;

/// <summary>
/// The MCP plane's authorization rules (spaarkeai-mcp-bff-api-r1 FR-02, FR-07, design §8 B-7; ADR-028 Amendment A10):
/// which tokens the <c>/mcp/{area}</c> routes accept, and what every tool must declare.
/// </summary>
/// <remarks>
/// <para><b>A10 token rule (<see cref="A10DenyReason"/>).</b> The route's <c>.RequireAuthorization()</c> runs the default
/// (workforce) JwtBearer scheme, which already validates signature, lifetime, the Spaarke-tenant issuer and the BFF audience
/// (and refuses CIAM tokens, FR-23). That scheme also serves other planes, so it accepts more than A10 allows: the Copilot
/// audience (<c>AgentToken:CopilotAudience</c>, the April surface) and app-only tokens. A10 is narrower, and it is enforced
/// here, explicitly, rather than by relying on library issuer defaults: Spaarke tenant, exact v2 issuer, the BFF audience
/// only, a delegated <c>user_impersonation</c> token for a user. The shared scheme is not changed (a change there would reach
/// every workforce route).</para>
/// <para><b>Per-tool rule (<see cref="ValidateTool"/>).</b> Every tool carries <c>[Authorize]</c> on its
/// <see cref="McpToolDefinition"/> class (the guard reads it there), checked by the SDK's <c>AddAuthorizationFilters()</c> on
/// <c>tools/list</c> and <c>tools/call</c>; it may name an existing workforce policy but never an external, CIAM or API-key
/// policy or scheme, and never <c>[AllowAnonymous]</c> (refused in source by the ArchTests). Every tool sets <c>ReadOnly</c> and a title explicitly. The catalog
/// refuses to start on a tool that breaks this; <c>RouteAuthorizationGuardTests</c> checks the same rules in source.</para>
/// </remarks>
public static class McpToolAuthorization
{
    /// <summary>Stable <c>errorCode</c> of the 401 ProblemDetails for a token the MCP plane does not accept.</summary>
    public const string RefusedErrorCode = "mcp.token_not_accepted";

    /// <summary>The only scope an MCP caller's token may be for (ADR-028 A10).</summary>
    public const string RequiredScope = "user_impersonation";

    private const string EntraInstance = "https://login.microsoftonline.com/";
    private const string ApiUriPrefix = "api://";
    private const string ObjectIdSchemaClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string ScopeSchemaClaim = "http://schemas.microsoft.com/identity/claims/scope";

    /// <summary>
    /// Policies a tool may never name (FR-07): the external plane and the API-key webhook. Each authenticates a scheme other
    /// than the workforce one, so naming it would let a non-A10 caller through.
    /// </summary>
    public static readonly IReadOnlySet<string> ForbiddenPolicies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        AuthPolicies.ExternalCollaboration,
        AuthPolicies.CiamExternal,
        AuthPolicies.RagApiKey,
    };

    /// <summary>Why <see cref="A10DenyReason"/> refused a caller (logged as <c>Reason</c>; never sent to the caller).</summary>
    public static class Deny
    {
        /// <summary>No authenticated identity.</summary>
        public const string NotAuthenticated = "not_authenticated";

        /// <summary>An identity from the CIAM (A1) or home-directory (A9) scheme.</summary>
        public const string ForeignScheme = "ciam_or_home_directory_scheme";

        /// <summary><c>AzureAd:TenantId</c> or <c>AzureAd:ClientId</c> is not a non-zero GUID: fail closed.</summary>
        public const string NotConfigured = "spaarke_tenant_or_bff_app_not_configured";

        /// <summary>No <c>tid</c>, or a <c>tid</c> other than the Spaarke tenant.</summary>
        public const string TenantNotSpaarke = "tid_not_spaarke_tenant";

        /// <summary><c>iss</c> missing or not exactly <c>https://login.microsoftonline.com/{spaarkeTenant}/v2.0</c>.</summary>
        public const string IssuerNotExactV2 = "iss_not_exact_v2_for_spaarke_tenant";

        /// <summary><c>aud</c> missing or not the BFF app (the Copilot audience included).</summary>
        public const string AudienceNotBff = "aud_not_bff_app";

        /// <summary><c>idtyp</c> present and not <c>user</c>.</summary>
        public const string AppToken = "app_token";

        /// <summary>No delegated <c>scp</c> containing <c>user_impersonation</c>.</summary>
        public const string ScopeMissing = "user_impersonation_scope_missing";

        /// <summary>No <c>oid</c>, or one that is not a non-zero GUID.</summary>
        public const string ObjectIdMissing = "oid_missing_or_malformed";
    }

    /// <summary>
    /// Null when <paramref name="user"/> is an ADR-028 A10 caller, otherwise a <see cref="Deny"/> reason. Each claim is read in
    /// its short and its mapped (schema URI) form, so inbound claim mapping can neither hide nor rename one; a multi-valued
    /// claim is accepted only when EVERY value passes.
    /// </summary>
    internal static string? A10DenyReason(ClaimsPrincipal? user, string? spaarkeTenantId, string? bffClientId)
    {
        if (user?.Identities.Any(i => i.IsAuthenticated) != true)
        {
            return Deny.NotAuthenticated;
        }

        if (user.Identities.Any(i =>
                string.Equals(i.AuthenticationType, AuthSchemes.Ciam, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(i.AuthenticationType, AuthSchemes.WorkforceHome, StringComparison.OrdinalIgnoreCase)))
        {
            return Deny.ForeignScheme;
        }

        if (!TryParseNonEmptyGuid(spaarkeTenantId, out var spaarke) || !TryParseNonEmptyGuid(bffClientId, out var bffApp))
        {
            return Deny.NotConfigured;
        }

        var tenants = Values(user, TenantResolution.TenantIdClaim, TenantResolution.TenantIdSchemaClaim);
        if (tenants.Count == 0 || !tenants.All(t => TryParseNonEmptyGuid(t, out var tid) && tid == spaarke))
        {
            return Deny.TenantNotSpaarke;
        }

        var expectedIssuer = $"{EntraInstance}{spaarke:D}/v2.0";
        var issuers = Values(user, "iss");
        if (issuers.Count == 0 || !issuers.All(i => string.Equals(i, expectedIssuer, StringComparison.Ordinal)))
        {
            return Deny.IssuerNotExactV2;
        }

        var audiences = Values(user, "aud");
        if (audiences.Count == 0 || !audiences.All(a => IsBffAudience(a, bffApp)))
        {
            return Deny.AudienceNotBff;
        }

        if (Values(user, "idtyp").Any(t => !string.Equals(t, "user", StringComparison.Ordinal)))
        {
            return Deny.AppToken;
        }

        var scopes = Values(user, "scp", ScopeSchemaClaim);
        if (!scopes.SelectMany(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Contains(RequiredScope, StringComparer.Ordinal))
        {
            return Deny.ScopeMissing;
        }

        var objectIds = Values(user, "oid", ObjectIdSchemaClaim);
        if (objectIds.Count == 0 || !objectIds.All(o => TryParseNonEmptyGuid(o, out _)))
        {
            return Deny.ObjectIdMissing;
        }

        return null;
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when <paramref name="tool"/> (built by <paramref name="definition"/>) breaks
    /// the per-tool rule: no <c>[Authorize]</c>, a forbidden policy, an authentication scheme named on the
    /// attribute, no explicit <c>ReadOnly</c>, or no title. Called by <see cref="McpToolCatalog"/> for every tool at startup.
    /// </summary>
    public static void ValidateTool(McpServerTool tool, Type definition)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(definition);

        var name = tool.ProtocolTool.Name;
        var where = $"MCP tool '{name}' ({definition.FullName})";

        // Anonymity is not checked here: the anonymity guard (RouteAuthorizationGuardTests.AnonymityIsDeclaredOnlyOnAScannedChain)
        // refuses the attribute anywhere in BFF source, and the MCP tool rule refuses it on a tool class.
        var authorize = tool.Metadata.OfType<IAuthorizeData>().ToArray();
        if (authorize.Length == 0)
        {
            throw new InvalidOperationException(
                $"{where} has no [Authorize]. Put [Authorize] (or [Authorize(Policy = \"<existing workforce policy>\")]) on the tool class and build the tool from a static method of that class (B-7).");
        }

        foreach (var data in authorize)
        {
            if (!string.IsNullOrWhiteSpace(data.AuthenticationSchemes))
            {
                throw new InvalidOperationException($"{where} names an authentication scheme on [Authorize]; MCP tools run on the A10 workforce scheme only.");
            }

            if (data.Policy is { } policy && ForbiddenPolicies.Contains(policy))
            {
                throw new InvalidOperationException($"{where} names the '{policy}' policy, which MCP tools may never use (FR-07).");
            }
        }

        if (tool.ProtocolTool.Annotations?.ReadOnlyHint is null)
        {
            throw new InvalidOperationException($"{where} does not set ReadOnly explicitly (FR-03: every tool has an explicit readOnlyHint).");
        }

        if (string.IsNullOrWhiteSpace(tool.ProtocolTool.Title) && string.IsNullOrWhiteSpace(tool.ProtocolTool.Annotations?.Title))
        {
            throw new InvalidOperationException($"{where} has no title (FR-03).");
        }
    }

    private static List<string> Values(ClaimsPrincipal user, params string[] claimTypes) =>
        claimTypes.SelectMany(type => user.FindAll(type)).Select(c => c.Value).ToList();

    private static bool IsBffAudience(string audience, Guid bffApp)
    {
        var id = audience.StartsWith(ApiUriPrefix, StringComparison.OrdinalIgnoreCase) ? audience[ApiUriPrefix.Length..] : audience;
        return Guid.TryParseExact(id, "D", out var app) && app == bffApp;
    }

    private static bool TryParseNonEmptyGuid(string? value, out Guid guid) =>
        Guid.TryParse(value?.Trim(), out guid) && guid != Guid.Empty;
}

/// <summary>
/// Endpoint filter that applies the ADR-028 A10 token rule (<see cref="McpToolAuthorization.A10DenyReason"/>) to an MCP route,
/// after <c>.RequireAuthorization()</c> and before the transport: a refused caller gets 401 with
/// <c>WWW-Authenticate: Bearer error="invalid_token"</c> and no tool, list or handshake runs. It logs the deny reason and nothing
/// from the token (no claim values, NFR-05).
/// </summary>
public sealed class McpConnectorTokenFilter(IConfiguration configuration, ILogger<McpConnectorTokenFilter> logger) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var reason = McpToolAuthorization.A10DenyReason(
            context.HttpContext.User, configuration["AzureAd:TenantId"], configuration["AzureAd:ClientId"]);
        if (reason is null)
        {
            return next(context);
        }

        logger.LogWarning(
            "[AUTH] sdap.auth.deny.mcp_connector_token: a token the MCP plane does not accept (ADR-028 A10) was refused ({Reason}).",
            reason);
        context.HttpContext.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
        return ValueTask.FromResult<object?>(Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Unauthorized",
            detail: "This token is not accepted by the Spaarke MCP server.",
            extensions: new Dictionary<string, object?> { ["errorCode"] = McpToolAuthorization.RefusedErrorCode }));
    }
}

/// <summary>Route-builder extension that applies <see cref="McpConnectorTokenFilter"/>.</summary>
public static class McpConnectorTokenFilterExtensions
{
    /// <summary>Refuses (401) any caller that is not an ADR-028 A10 caller. Chain it right AFTER the route's <c>.RequireAuthorization()</c>.</summary>
    public static TBuilder RequireMcpConnectorToken<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddEndpointFilter<TBuilder, McpConnectorTokenFilter>();
    }
}

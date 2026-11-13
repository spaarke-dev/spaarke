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
/// <c>tools/list</c> and <c>tools/call</c>. The SDK takes that metadata from the declaring class of the handler method (or from
/// <c>McpServerToolCreateOptions.Metadata</c>), so the rule requires the metadata the SDK enforces to EQUAL the tool class's own
/// <c>[Authorize]</c>: a handler from a helper class, or a <c>Metadata</c> override, cannot swap the policy. The policy is one of
/// <see cref="AllowedToolPolicies"/> (and the catalog checks it is registered); no scheme is named; no anonymity metadata. Every
/// tool sets <c>ReadOnly</c> and a title explicitly. With <c>Mcp:Enabled</c> true the catalog is built at startup
/// (<see cref="McpEndpoints.MapMcpEndpoints"/>), so a tool that breaks this stops the app from starting;
/// <c>RouteAuthorizationGuardTests</c> checks the same rules in source.</para>
/// <para><b>Limit, stated.</b> The SDK's per-tool filter judges every tool against the caller the A10 filter admitted: it evaluates
/// the tool's policy requirements on <c>HttpContext.User</c> and does not re-run a policy's own authentication schemes.</para>
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
    /// The ONLY policies a tool's <c>[Authorize]</c> may name (FR-07, B-7): <c>""</c> is bare <c>[Authorize]</c> (the DefaultPolicy,
    /// an authenticated workforce user), and <c>"SystemAdmin"</c> is the workforce admin-role policy <c>AuthorizationModule</c>
    /// registers. An allow-list, not a deny-list: a policy that authenticates another scheme (<c>ExternalCollaboration</c>,
    /// <c>CiamExternal</c>, <c>RagApiKey</c>) or one added later is refused until it is added here deliberately.
    /// <c>RouteAuthorizationGuardTests</c> reads this set; there is no second copy. Ordinal: the exact registered name.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedToolPolicies = new HashSet<string>(StringComparer.Ordinal)
    {
        "",
        "SystemAdmin",
    };

    // The full name of the ASP.NET Core anonymity metadata interface. Matched by name because the anonymity guard
    // (RouteAuthorizationGuardTests.AnonymityIsDeclaredOnlyOnAScannedChain) bans any identifier containing it from BFF source.
    private const string AnonymityMetadataInterface = "Microsoft.AspNetCore.Authorization.IAllowAnonymous";

    /// <summary>Why <see cref="A10DenyReason"/> refused a caller (logged as <c>Reason</c>; never sent to the caller).</summary>
    public static class Deny
    {
        /// <summary>No authenticated identity.</summary>
        public const string NotAuthenticated = "not_authenticated";

        /// <summary>
        /// An identity from the home-directory (A9) scheme, which stamps its scheme name as the identity's authentication type.
        /// The CIAM (A1) JwtBearer scheme sets no authentication type, so CIAM refusal rests on the <c>tid</c> and <c>iss</c>
        /// checks (<see cref="TenantNotSpaarke"/>, <see cref="IssuerNotExactV2"/>), not on this one. The value is unchanged.
        /// </summary>
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

        // Only WorkforceHome stamps its scheme as the authentication type; a CIAM identity is refused below by tid and iss.
        if (user.Identities.Any(i => string.Equals(i.AuthenticationType, AuthSchemes.WorkforceHome, StringComparison.OrdinalIgnoreCase)))
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
    /// the per-tool rule: no <c>[Authorize]</c> on the tool class; enforced <c>[Authorize]</c> metadata that differs from the
    /// class's own; anonymity metadata; a policy outside <see cref="AllowedToolPolicies"/>; an authentication scheme named on the
    /// attribute; no explicit <c>ReadOnly</c>; or no title. Called by <see cref="McpToolCatalog"/> for every tool at startup.
    /// </summary>
    public static void ValidateTool(McpServerTool tool, Type definition)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(definition);

        var name = tool.ProtocolTool.Name;
        var where = $"MCP tool '{name}' ({definition.FullName})";

        // The source guard bans the attribute in BFF code; this refuses anonymity metadata however it got onto the tool.
        if (tool.Metadata.Any(IsAnonymityMetadata) || definition.GetCustomAttributes(inherit: true).Any(IsAnonymityMetadata))
        {
            throw new InvalidOperationException($"{where} carries anonymity metadata; every MCP tool requires an authenticated A10 caller (FR-02).");
        }

        var declared = definition.GetCustomAttributes(inherit: false).OfType<IAuthorizeData>().ToArray();
        if (declared.Length == 0)
        {
            throw new InvalidOperationException(
                $"{where} has no [Authorize]. Put [Authorize] (or [Authorize(Policy = \"SystemAdmin\")]) on the tool class and build the tool from a static method of that class (B-7).");
        }

        // What the SDK's authorization filter enforces is tool.Metadata: the handler's DECLARING class plus the method, or
        // McpServerToolCreateOptions.Metadata. It must be exactly the tool class's own [Authorize], or a helper-class handler
        // (or a Metadata override) would enforce a different policy than the one the class, the guard and the ledger show.
        var enforced = tool.Metadata.OfType<IAuthorizeData>().ToArray();
        if (!AuthorizeKeys(enforced).SequenceEqual(AuthorizeKeys(declared), StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{where}: the [Authorize] the SDK enforces ({Describe(enforced)}) is not the tool class's own ({Describe(declared)}). Build the tool "
                + "from a static method of the tool class passed as an unqualified method group, and do not set McpServerToolCreateOptions.Metadata (B-7).");
        }

        foreach (var data in declared)
        {
            if (!string.IsNullOrWhiteSpace(data.AuthenticationSchemes))
            {
                throw new InvalidOperationException($"{where} names an authentication scheme on [Authorize]; MCP tools run on the A10 workforce scheme only.");
            }

            if (!AllowedToolPolicies.Contains(data.Policy ?? string.Empty))
            {
                throw new InvalidOperationException(
                    $"{where} names the '{data.Policy}' policy, which is not in McpToolAuthorization.AllowedToolPolicies (FR-07, B-7).");
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

    private static bool IsAnonymityMetadata(object metadata) => metadata.GetType().GetInterface(AnonymityMetadataInterface) is not null;

    // One comparable key per [Authorize]: policy, roles and schemes (null and empty are the same), sorted as a multiset.
    private static IEnumerable<string> AuthorizeKeys(IEnumerable<IAuthorizeData> data) =>
        data.Select(d => $"policy={d.Policy ?? string.Empty};roles={d.Roles ?? string.Empty};schemes={d.AuthenticationSchemes ?? string.Empty}")
            .OrderBy(k => k, StringComparer.Ordinal);

    private static string Describe(IEnumerable<IAuthorizeData> data) =>
        AuthorizeKeys(data).ToList() is { Count: > 0 } keys ? string.Join(" | ", keys) : "none";

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

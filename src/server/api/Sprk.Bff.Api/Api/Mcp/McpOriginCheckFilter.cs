using Microsoft.Extensions.Primitives;

namespace Sprk.Bff.Api.Api.Mcp;

/// <summary>
/// How <see cref="McpOriginCheckFilter"/> acts on a request it would reject (<c>Mcp:OriginCheck:Mode</c>).
/// </summary>
public enum McpOriginCheckMode
{
    /// <summary>Never rejects; logs one Information line per request with the decision <see cref="Enforce"/> would make. The default.</summary>
    LogOnly,

    /// <summary>Rejects a request whose <c>Origin</c> is present and not allowed, or whose <c>Host</c> is not an allowed host, with 403.</summary>
    Enforce,
}

/// <summary>
/// Host/Origin validation for the MCP routes (ISS-007 #2835; MCP Streamable HTTP transport, "Security Warning": validate <c>Origin</c> to
/// defeat DNS rebinding). Owner-approved rule (2026-11-26):
/// <list type="bullet">
/// <item>An <c>Origin</c> header that is PRESENT and not in <c>Mcp:OriginCheck:AllowedOrigins</c> is rejected with 403. No <c>Origin</c> passes:
/// Copilot and Cowork call server to server, and a browser always sends <c>Origin</c> on a cross-origin request.</item>
/// <item><c>Host</c> must be one of <c>Mcp:OriginCheck:AllowedHosts</c>; when that list is empty, the BFF's own configured host names are used
/// (see <see cref="ResolveAllowedHosts"/>). When none is configured, <c>Host</c> is not restricted.</item>
/// <item><c>Mcp:OriginCheck:Mode</c> is <see cref="McpOriginCheckMode.LogOnly"/> (default) or <see cref="McpOriginCheckMode.Enforce"/>. The
/// default stays LogOnly until what Copilot and Cowork actually send has been observed in the logs.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>Ordering (auth outcomes never change).</b> An endpoint filter runs after <c>UseAuthorization</c> and, on the route, after
/// <see cref="McpConnectorTokenFilter"/> (see <c>McpEndpoints</c>). So an unauthenticated request, or one with a token the MCP plane does not
/// accept, gets the same 401 it always got, whatever its <c>Origin</c>; this filter can only turn a request that would have been served into a
/// 403. That is deliberate: a rebinding page holds no valid bearer token (the browser does not attach one for the attacker's origin), so 401
/// already stops it, and answering 403 before 401 would let an unauthenticated probe tell an allowed Origin from a refused one. Hence:
/// <b>unauthenticated + bad Origin = 401; authenticated + bad Origin in Enforce = 403.</b></para>
/// <para><b>Logging.</b> One line per request in LogOnly: whether <c>Origin</c> is present, its value (an origin is scheme + host, not personal
/// data; capped at 200 characters), the <c>Host</c>, and the decision Enforce would make. Never a token, the caller, or any other header.
/// Enforce logs only a Warning for a rejection.</para>
/// <para><b>Reuse (CLAUDE.md §11).</b> Host names come from the existing ASP.NET Core <c>AllowedHosts</c> setting and the existing
/// <c>Insights:CitationHref:BffBaseUrl</c> (the BFF's public origin); the filter follows <see cref="McpConnectorTokenFilter"/> (an
/// <see cref="IEndpointFilter"/> that reads <see cref="IConfiguration"/>) and the <c>RequireMcpEnabled</c> wiring extension. Configuration is
/// read per request so an environment change needs no code and the registration needs no new line.</para>
/// </remarks>
public sealed class McpOriginCheckFilter(IConfiguration configuration, ILogger<McpOriginCheckFilter> logger) : IEndpointFilter
{
    /// <summary>The configuration section: <c>Mode</c>, <c>AllowedOrigins</c>, <c>AllowedHosts</c>.</summary>
    public const string SectionName = "Mcp:OriginCheck";

    /// <summary>ProblemDetails <c>errorCode</c> when the <c>Origin</c> is refused.</summary>
    public const string OriginRefusedErrorCode = "mcp.origin_not_allowed";

    /// <summary>ProblemDetails <c>errorCode</c> when the <c>Host</c> is refused.</summary>
    public const string HostRefusedErrorCode = "mcp.host_not_allowed";

    private const int MaxLoggedLength = 200;

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        var section = configuration.GetSection(SectionName);
        var mode = ParseMode(section["Mode"], out var modeValue);

        var originHeader = request.Headers.Origin;
        var originPresent = request.Headers.ContainsKey("Origin");
        var host = request.Host;

        var reason = Evaluate(
            originPresent,
            originHeader,
            host,
            section.GetSection("AllowedOrigins").Get<string[]>(),
            ResolveAllowedHosts(configuration));

        if (mode == McpOriginCheckMode.LogOnly)
        {
            logger.LogInformation(
                "[MCP-ORIGIN] mode=LogOnly originPresent={OriginPresent} origin={Origin} host={Host} wouldReject={WouldReject} wouldReason={WouldReason}",
                originPresent,
                Truncate(originHeader.ToString()),
                Truncate(host.Value),
                reason is not null,
                reason ?? "allowed");
            return next(context);
        }

        if (reason is null)
        {
            return next(context);
        }

        if (modeValue is not null)
        {
            // Mode is set but is neither LogOnly nor Enforce: fail closed (a typo of "Enforce" must not leave the check off).
            logger.LogWarning(
                "[MCP-ORIGIN] Mcp:OriginCheck:Mode '{ModeValue}' is not LogOnly or Enforce; treated as Enforce.",
                Truncate(modeValue));
        }

        logger.LogWarning(
            "[MCP-ORIGIN] refused ({Reason}): originPresent={OriginPresent} origin={Origin} host={Host}",
            reason,
            originPresent,
            Truncate(originHeader.ToString()),
            Truncate(host.Value));
        return ValueTask.FromResult<object?>(Results.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Forbidden",
            detail: reason == OriginRefusedErrorCode
                ? "This Origin is not allowed to call the Spaarke MCP server."
                : "This Host is not allowed on the Spaarke MCP server.",
            extensions: new Dictionary<string, object?> { ["errorCode"] = reason }));
    }

    /// <summary>
    /// Startup check of <c>Mcp:OriginCheck</c>, called once from <c>MapMcpEndpoints</c> when the MCP server is on (<paramref name="enabled"/>).
    /// Logs one Warning for each setting that would make the check weaker than it looks, and nothing for a valid configuration:
    /// <list type="bullet">
    /// <item>a <c>Mode</c> that is neither LogOnly nor Enforce (it is treated as Enforce, so a typo fails closed, but the operator should know);</item>
    /// <item>Enforce (or an unrecognised Mode) with no allowed host resolved from <c>Mcp:OriginCheck:AllowedHosts</c>, <c>AllowedHosts</c> or
    /// <c>Insights:CitationHref:BffBaseUrl</c>: only <c>Origin</c> is checked and any <c>Host</c> passes.</item>
    /// </list>
    /// Configuration is read per request, but an App Service setting change restarts the app, so this runs for every configuration that serves.
    /// </summary>
    internal static void WarnIfMisconfigured(ILogger logger, IConfiguration configuration, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!enabled)
        {
            return;
        }

        var mode = ParseMode(configuration.GetSection(SectionName)["Mode"], out var unrecognised);
        if (unrecognised is not null)
        {
            logger.LogWarning(
                "[MCP-ORIGIN] Mcp:OriginCheck:Mode '{ModeValue}' is not LogOnly or Enforce; it is treated as Enforce.",
                Truncate(unrecognised));
        }

        if (mode == McpOriginCheckMode.Enforce && ResolveAllowedHosts(configuration).Count == 0)
        {
            logger.LogWarning(
                "[MCP-ORIGIN] Mcp:OriginCheck is in Enforce but no allowed host is configured (Mcp:OriginCheck:AllowedHosts, AllowedHosts, "
                + "Insights:CitationHref:BffBaseUrl), so the Host header is not checked; only Origin is. Set Mcp:OriginCheck:AllowedHosts.");
        }
    }

    /// <summary>
    /// <see cref="McpOriginCheckMode.LogOnly"/> when unset (the default); otherwise the named mode, case-insensitive. A value that names neither is
    /// <see cref="McpOriginCheckMode.Enforce"/> and is returned in <paramref name="unrecognised"/> so the caller can warn.
    /// </summary>
    internal static McpOriginCheckMode ParseMode(string? value, out string? unrecognised)
    {
        unrecognised = null;
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals(nameof(McpOriginCheckMode.LogOnly), StringComparison.OrdinalIgnoreCase))
        {
            return McpOriginCheckMode.LogOnly;
        }

        if (value.Trim().Equals(nameof(McpOriginCheckMode.Enforce), StringComparison.OrdinalIgnoreCase))
        {
            return McpOriginCheckMode.Enforce;
        }

        unrecognised = value;
        return McpOriginCheckMode.Enforce;
    }

    /// <summary>The refusal <c>errorCode</c> Enforce would answer with, or null when the request would pass.</summary>
    internal static string? Evaluate(
        bool originPresent, StringValues origin, HostString host, string[]? allowedOrigins, IReadOnlyList<string> allowedHosts)
    {
        // Every value must be allowed; a header that is present with no value at all fails closed.
        if (originPresent && (origin.Count == 0 || !origin.All(o => OriginAllowed(o, allowedOrigins))))
        {
            return OriginRefusedErrorCode;
        }

        if (allowedHosts.Count > 0 && !allowedHosts.Any(entry => HostMatches(entry, host)))
        {
            return HostRefusedErrorCode;
        }

        return null;
    }

    /// <summary>
    /// <c>Mcp:OriginCheck:AllowedHosts</c>; when empty, the BFF's own host names: the ASP.NET Core <c>AllowedHosts</c> setting (semicolon list,
    /// unless it contains <c>*</c>, which allows any host) and the host of <c>Insights:CitationHref:BffBaseUrl</c> (the BFF's public origin).
    /// Empty means "not restricted".
    /// </summary>
    internal static IReadOnlyList<string> ResolveAllowedHosts(IConfiguration configuration)
    {
        var explicitHosts = Clean(configuration.GetSection(SectionName).GetSection("AllowedHosts").Get<string[]>());
        if (explicitHosts.Count > 0)
        {
            return explicitHosts;
        }

        var aspNetHosts = Clean((configuration["AllowedHosts"] ?? string.Empty).Split(';'));
        if (aspNetHosts.Count > 0 && !aspNetHosts.Contains("*"))
        {
            return aspNetHosts;
        }

        // AllowedHosts is "*" (the template default) or unset, so it names no host. Fall back to the BFF's public origin when configured.
        return Uri.TryCreate(configuration["Insights:CitationHref:BffBaseUrl"], UriKind.Absolute, out var bff)
            ? [bff.Authority]
            : [];
    }

    private static bool OriginAllowed(string? origin, string[]? allowedOrigins) =>
        !string.IsNullOrWhiteSpace(origin)
        && allowedOrigins is not null
        && allowedOrigins.Any(a => string.Equals(a?.Trim().TrimEnd('/'), origin.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase));

    // Same matching rules as the ASP.NET Core host filter: "host" (any port), "host:port", "*.example.com" (subdomains only), "*".
    private static bool HostMatches(string entry, HostString host)
    {
        if (entry == "*")
        {
            return true;
        }

        if (string.IsNullOrEmpty(host.Host))
        {
            return false;
        }

        var allowed = new HostString(entry);
        if (allowed.Port is { } port && host.Port != port)
        {
            return false;
        }

        return allowed.Host.StartsWith("*.", StringComparison.Ordinal)
            ? host.Host.EndsWith(allowed.Host[1..], StringComparison.OrdinalIgnoreCase)
            : string.Equals(allowed.Host, host.Host, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> Clean(string[]? values) =>
        (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList();

    private static string Truncate(string? value) =>
        value is null ? string.Empty : value.Length <= MaxLoggedLength ? value : value[..MaxLoggedLength];
}

/// <summary>Route-builder extension that applies <see cref="McpOriginCheckFilter"/> (the shape of <c>RequireMcpConnectorToken</c>).</summary>
public static class McpOriginCheckFilterExtensions
{
    /// <summary>Validates <c>Origin</c> and <c>Host</c> on the route per <c>Mcp:OriginCheck</c>. Chain it AFTER <c>RequireMcpConnectorToken()</c> so no auth outcome changes.</summary>
    public static TBuilder RequireMcpOriginCheck<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddEndpointFilter<TBuilder, McpOriginCheckFilter>();
    }
}

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.DI;

namespace Sprk.Bff.Api.Api.Mcp;

/// <summary>
/// Maps the MCP routes (spaarkeai-mcp-bff-api-r1 FR-01, design §8 B-6, ADR-010): <c>POST /mcp/{area}</c>, one stateless
/// Streamable HTTP endpoint per product area. v1 maps exactly <c>/mcp/legal-operations</c>; "core" is avoided because it means
/// <i>core record</i>. Each route lists only the tools tagged with its area (<see cref="McpToolCatalog"/>), chosen per request in
/// <c>McpModule</c>'s <c>ConfigureSessionOptions</c> from the <see cref="McpAreaMetadata"/> stamped here (Phase 0 T13, notes/phase0-results.md).
/// </summary>
/// <remarks>
/// <para><b>Chain, outermost first.</b> <c>.RequireAuthorization()</c> on the default workforce scheme (ADR-028; A10 refusals and
/// per-tool policies are task 021's); the <c>mcp</c> rate-limit policy per caller (ADR-016); a 256 KiB request-body limit (ADR-001;
/// a Decision review is a few KB); response buffering off (the transport may answer with an SSE stream; SDK 2.3.1 also does this
/// itself, and the route does not rely on that); then <see cref="McpEnabledFilter"/> (ADR-032 kill switch, after authorization: a
/// switched-off server still answers an anonymous caller 401).</para>
/// <para>The route is mapped unconditionally; its SDK services are registered unconditionally (ADR-032).</para>
/// </remarks>
public static class McpEndpoints
{
    /// <summary>The v1 route. A new area adds its own <c>MapMcp</c> statement here and its own constant; tools find their route by attribute.</summary>
    public const string LegalOperationsRoute = "/mcp/legal-operations";

    /// <summary>The area segment of <see cref="LegalOperationsRoute"/> and the value tools put in <see cref="McpAreaAttribute"/>.</summary>
    public const string LegalOperationsArea = "legal-operations";

    /// <summary>
    /// The request-body ceiling on every MCP route: 256 KiB. The largest legitimate request is a Decision review (a few KB of
    /// parameters); the rest are small JSON-RPC envelopes. Enforced by Kestrel through <see cref="RequestSizeLimitAttribute"/> metadata.
    /// </summary>
    public const int MaxRequestBodyBytes = 256 * 1024;


    public static void MapMcpEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // The policy name is a literal on purpose: the health-probe policy guard (RouteAuthorizationGuardTests) reads only string literals.
        // Keep it equal to RateLimitingModule.McpPolicyName.
        app.MapMcp(LegalOperationsRoute)
            .RequireAuthorization()
            .RequireRateLimiting("mcp")
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodyBytes))
            .WithMetadata(new McpAreaMetadata(LegalOperationsArea))
            .AddEndpointFilter(DisableResponseBufferingAsync)
            .RequireMcpEnabled();
    }

    // Same call WorkspaceMatterEndpoints.cs:196-200 makes for its streamed response: the transport may answer with an SSE stream.
    private static async ValueTask<object?> DisableResponseBufferingAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        return await next(context);
    }
}

/// <summary>
/// The kill-switch filter (ADR-032 P3, ADR-018): with <c>Mcp:Enabled</c> false the route answers 503 "Feature Disabled"
/// ProblemDetails (<c>errorCode</c> <see cref="NullMcpToolCatalog.ErrorCode"/>) and the transport, and so every tool, never runs.
/// Runs after <c>RequireAuthorization()</c>; the switch never replaces authorization.
/// </summary>
public sealed class McpEnabledFilter(McpToolCatalog catalog) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            catalog.EnsureEnabled();
        }
        catch (FeatureDisabledException ex)
        {
            return ValueTask.FromResult<object?>(ex.AsFeatureDisabled503());
        }

        return next(context);
    }
}

/// <summary>Route-builder extension that applies <see cref="McpEnabledFilter"/> (the shape of <c>RequireEbillingEnabled</c>).</summary>
public static class McpEnabledFilterExtensions
{
    /// <summary>Makes the route return 503 <c>mcp.disabled</c> when <c>Mcp:Enabled</c> is false. Chain it AFTER the route's authorization.</summary>
    public static TBuilder RequireMcpEnabled<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddEndpointFilter<TBuilder, McpEnabledFilter>();
    }
}

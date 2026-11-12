using System.Reflection;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Api.Mcp;

/// <summary>
/// Tags a tool class with the product area (<c>/mcp/{area}</c>) whose route serves it (spaarkeai-mcp-bff-api-r1 FR-01, design §8 B-6).
/// Put it on one <see cref="McpToolDefinition"/> subclass per tool under <c>Api/Mcp/{Area}/{Group}/</c>; <see cref="McpToolCatalog"/>
/// finds it, so adding a tool edits no shared registration file.
/// </summary>
/// <param name="area">The route area, e.g. <c>legal-operations</c>. Lower-case, hyphenated; never "core" (it means core record).</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class McpAreaAttribute(string area) : Attribute
{
    public string Area { get; } = !string.IsNullOrWhiteSpace(area) ? area : throw new ArgumentException("An MCP area is required.", nameof(area));
}

/// <summary>
/// One MCP tool, as the catalog finds it: a class tagged <see cref="McpAreaAttribute"/> with a parameterless constructor whose
/// <see cref="Create"/> builds the tool from a delegate (<see cref="McpServerTool.Create(Delegate, McpServerToolCreateOptions?)"/>).
/// </summary>
/// <remarks>
/// <para>Write the tool's work as a <c>static</c> method and pass it as a method group, taking what it needs as PARAMETERS: the SDK binds
/// a parameter that is a registered service (<c>IHttpContextAccessor</c>, <c>IDecisionCapability</c>, ...) from the request's service
/// provider on every call (set <c>McpServerToolCreateOptions.Services</c> to the <c>services</c> argument so the SDK can tell a service
/// parameter from a tool argument), so a tool holds no state between calls and takes scoped services safely. It must pass exactly
/// <c>IHttpContextAccessor.HttpContext</c> to the capability (spec FR-10), never a context it built or cached.</para>
/// <para>Why a delegate and not the SDK's attribute scan: choosing a method by reflection (<c>Type.GetMethods</c>) is banned in the BFF
/// (<c>PoaShareClientSingletonGuardTests</c> C7, owner round 48), and the SDK's own <c>WithToolsFromAssembly</c> cannot partition tools by area.</para>
/// </remarks>
public abstract class McpToolDefinition
{
    /// <summary>
    /// Builds the tool. Called once, when the catalog is first resolved; give it an explicit name, title, annotations and a
    /// use-for / not-for description. <paramref name="services"/> is the application provider: pass it as
    /// <c>McpServerToolCreateOptions.Services</c> (it only tells the SDK which parameters are services; each call resolves them from its own request).
    /// </summary>
    public abstract McpServerTool Create(IServiceProvider services);
}

/// <summary>The area an MCP route serves, stamped on the endpoint by <see cref="McpEndpoints"/> and read per request.</summary>
/// <param name="Area">The area segment of <c>/mcp/{area}</c>.</param>
public sealed record McpAreaMetadata(string Area);

/// <summary>
/// The MCP tools of every area, discovered once by attribute (<see cref="McpAreaAttribute"/> on a <see cref="McpToolDefinition"/>
/// subclass) and partitioned by area, so each <c>/mcp/{area}</c> route lists only its own tools. The SDK's own
/// <c>WithToolsFromAssembly</c> registers one flat set for the server, which cannot do that.
/// </summary>
/// <remarks>
/// <b>Kill switch (ADR-032, pattern P3).</b> This is the concrete the module swaps for <see cref="NullMcpToolCatalog"/> when
/// <c>Mcp:Enabled</c> is false: <see cref="EnsureEnabled"/> then throws <see cref="FeatureDisabledException"/>, the route's
/// <see cref="McpEnabledFilter"/> turns that into 503 ProblemDetails, and the transport (and so every tool) never runs.
/// </remarks>
public class McpToolCatalog
{
    private readonly Dictionary<string, McpServerTool[]> _toolsByArea;

    /// <summary>For the null object only: no tools, nothing scanned.</summary>
    protected McpToolCatalog() => _toolsByArea = new Dictionary<string, McpServerTool[]>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Scans <paramref name="assemblies"/> for tool classes. Throws at construction on a malformed tool.</summary>
    public McpToolCatalog(IEnumerable<Assembly> assemblies, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(services);

        var byArea = new Dictionary<string, List<McpServerTool>>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in assemblies.Distinct().SelectMany(a => a.GetTypes()).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            var area = type.GetCustomAttribute<McpAreaAttribute>(inherit: false)?.Area;
            if (area is null || type.IsAbstract)
            {
                continue;
            }

            if (!typeof(McpToolDefinition).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"{type.FullName} is tagged [McpArea(\"{area}\")] but does not derive from {nameof(McpToolDefinition)}.");
            }

            var tool = ((McpToolDefinition)Activator.CreateInstance(type, nonPublic: true)!).Create(services);

            // A tool name is unique across areas too: the connector's approval card and the model address a tool by name.
            if (!seenNames.Add(tool.ProtocolTool.Name))
            {
                throw new InvalidOperationException($"MCP tool name '{tool.ProtocolTool.Name}' is declared more than once.");
            }

            if (!byArea.TryGetValue(area, out var list))
            {
                byArea[area] = list = [];
            }

            list.Add(tool);
        }

        _toolsByArea = byArea.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The tools of <paramref name="area"/>; empty for an area with none (it never falls back to another area's tools).</summary>
    public virtual IReadOnlyList<McpServerTool> ToolsFor(string area) =>
        _toolsByArea.TryGetValue(area, out var tools) ? tools : [];

    /// <summary>Throws <see cref="FeatureDisabledException"/> when MCP is switched off; the real catalog never does.</summary>
    public virtual void EnsureEnabled()
    {
    }
}

/// <summary>
/// ADR-032 P3 null object for <see cref="McpToolCatalog"/>, registered when <c>Mcp:Enabled</c> is false: no tools, and
/// <see cref="EnsureEnabled"/> refuses. Minimal dependencies (none), so it has no feature-gated transitive dependency.
/// </summary>
public sealed class NullMcpToolCatalog : McpToolCatalog
{
    /// <summary>Stable <c>errorCode</c> of the 503 ProblemDetails.</summary>
    public const string ErrorCode = "mcp.disabled";

    public override IReadOnlyList<McpServerTool> ToolsFor(string area) => [];

    public override void EnsureEnabled() =>
        throw new FeatureDisabledException(ErrorCode, "The Spaarke MCP server is not enabled in this environment.");
}

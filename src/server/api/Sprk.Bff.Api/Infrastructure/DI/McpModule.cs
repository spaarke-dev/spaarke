using System.Reflection;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Mcp;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI module for the MCP host (ADR-010 feature module; spaarkeai-mcp-bff-api-r1 FR-01, design §8 B-6). Registers the stateless
/// ModelContextProtocol.AspNetCore transport, the attribute-discovered tool catalog and the <c>Mcp:Enabled</c> kill switch. The
/// route is mapped by <see cref="McpEndpoints.MapMcpEndpoints"/> from <c>EndpointMappingExtensions</c>.
/// </summary>
/// <remarks>
/// <para><b>Stateless, no affinity.</b> No session store, no <c>Mcp-Session-Id</c>, no GET/DELETE/SSE routes (ADR-001, ADR-052): every
/// request builds its own server context, and the SDK runs <c>ConfigureSessionOptions</c> on every request in that mode, which is
/// where the route's tool set is chosen. <c>PerSessionExecutionContext</c> is left at its default (<c>false</c>; setting it is obsolete in stateless mode) so tools run on the request's
/// execution context and <c>IHttpContextAccessor.HttpContext</c> is populated (task 010 pins both).</para>
/// <para><b>Kill switch (ADR-032, P3).</b> The SDK services and the route are registered and mapped in both states, so toggling the
/// flag cannot abort startup. <c>Mcp:Enabled</c> false (the default) swaps the real <see cref="McpToolCatalog"/> for
/// <see cref="NullMcpToolCatalog"/>, which makes the route's <see cref="McpEnabledFilter"/> answer 503 before any tool runs.</para>
/// </remarks>
public static class McpModule
{
    public static IServiceCollection AddMcpModule(this IServiceCollection services, IConfiguration configuration) =>
        services.AddMcpModule(configuration, typeof(McpModule).Assembly);

    /// <summary>The scan set is a parameter so a test host can add an assembly holding probe tools; production passes the BFF assembly.</summary>
    internal static IServiceCollection AddMcpModule(this IServiceCollection services, IConfiguration configuration, params Assembly[] toolAssemblies)
    {
        services.Configure<McpOptions>(configuration.GetSection(McpOptions.SectionName));

        var options = configuration.GetSection(McpOptions.SectionName).Get<McpOptions>() ?? new McpOptions();
        if (options.Enabled)
        {
            services.AddSingleton(sp => new McpToolCatalog(toolAssemblies, sp));
        }
        else
        {
            // ADR-032 P3 Null-Object, registered under the SAME concrete type the route's filter and the transport resolve.
            services.AddSingleton<McpToolCatalog>(_ => new NullMcpToolCatalog());
        }

        services.AddMcpServer().WithHttpTransport(transport =>
        {
            transport.SessionMode = HttpServerSessionMode.Stateless;
            transport.ConfigureSessionOptions = (httpContext, serverOptions, _) =>
            {
                // Runs on EVERY request in stateless mode: the route's tool set, by the area the route was mapped for. No area
                // metadata (cannot happen on a mapped route) means no tools, never another area's.
                var area = httpContext.GetEndpoint()?.Metadata.GetMetadata<McpAreaMetadata>()?.Area;
                var tools = new McpServerPrimitiveCollection<McpServerTool>();
                if (area is not null)
                {
                    foreach (var tool in httpContext.RequestServices.GetRequiredService<McpToolCatalog>().ToolsFor(area))
                    {
                        tools.Add(tool);
                    }
                }

                serverOptions.ToolCollection = tools;
                return Task.CompletedTask;
            };
        });

        return services;
    }
}

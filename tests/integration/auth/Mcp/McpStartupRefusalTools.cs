// spaarkeai-mcp-bff-api-r1 — Task 021 fix round 1 (2026-11-12)
// Tools that break the per-tool rule ON PURPOSE. They live in their own namespace, which McpProbeTools.Served leaves out of
// every MCP test host's scan set; only McpAuthTests' startup-refusal tests add one, to prove the app refuses to start on it.

using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Mcp;

namespace Sprk.Bff.Api.Tests.Auth.Mcp.StartupRefusals;

/// <summary>
/// The reviewers' reproduction (F4): the tool CLASS names <c>SystemAdmin</c>, but its handler is a static method of a helper class
/// carrying bare <c>[Authorize]</c>. The SDK takes the metadata from the handler's declaring class, so before fix round 1 the
/// guard and the catalog read SystemAdmin while the SDK enforced the default policy (a non-admin saw the tool).
/// </summary>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize(Policy = "SystemAdmin")]
internal sealed class McpHelperHandlerRefusedTool : McpToolDefinition
{
    public override McpServerTool Create(IServiceProvider services) =>
        McpServerTool.Create(
            McpBareAuthorizeHandlers.Run,
            new McpServerToolCreateOptions { Services = services, Name = "probe_refused_helper_handler", Title = "Probe refused helper handler", ReadOnly = true });
}

/// <summary>The helper class whose bare <c>[Authorize]</c> the SDK would have enforced.</summary>
[Authorize]
internal static class McpBareAuthorizeHandlers
{
    public static string Run() => "helper";
}

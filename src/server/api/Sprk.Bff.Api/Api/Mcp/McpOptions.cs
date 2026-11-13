namespace Sprk.Bff.Api.Api.Mcp;

/// <summary>
/// Configuration for the MCP host (<c>Mcp</c> section; spaarkeai-mcp-bff-api-r1 FR-01, ADR-032).
/// </summary>
public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    /// <summary>
    /// The kill switch. <c>false</c> (the default) keeps <c>/mcp/legal-operations</c> mapped but answers 503 ProblemDetails
    /// from a null object before any tool runs; turned on per environment (dev first). Read once at registration
    /// (<see cref="Infrastructure.DI.McpModule"/>), like every ADR-032 kill switch, so a change needs a restart.
    /// </summary>
    public bool Enabled { get; set; }
}

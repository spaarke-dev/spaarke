namespace Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;

/// <summary>
/// Shared citation-identifier shaping for the <c>dataverse.*</c> tool namespace
/// (ADR-039: "read results carry the identifiers needed for citation enforcement at P2").
/// </summary>
/// <remarks>
/// <para>
/// Paths use the GA Dataverse MCP filesystem-style convention
/// (<c>tables/{logicalName}/records/{guid}</c> — the exact shape <c>search_data</c> and
/// <c>describe</c> return on the GA MCP surface) so citations remain stable across a future
/// transport swap. Citations carry deterministic identifiers ONLY (ADR-015): entity logical
/// name + record id — never row content.
/// </para>
/// <para>
/// Reused by task 009's write handlers (created/updated record references in confirmations).
/// </para>
/// </remarks>
public static class DataverseRecordCitations
{
    /// <summary>GA-MCP-style record path: <c>tables/{logicalName}/records/{id}</c>.</summary>
    public static string RecordPath(string entityLogicalName, Guid recordId) =>
        $"tables/{entityLogicalName}/records/{recordId:D}";

    /// <summary>GA-MCP-style table path: <c>tables/{logicalName}</c>.</summary>
    public static string TablePath(string entityLogicalName) =>
        $"tables/{entityLogicalName}";

    /// <summary>
    /// Builds an adapter-compatible citation envelope for a Dataverse record. ChunkId is the
    /// GA-MCP-style record path (deterministic, replayable via dataverse.describe); SourceName
    /// is the entity logical name (identifier — safe under ADR-015).
    /// </summary>
    public static ToolResultCitation ForRecord(string entityLogicalName, Guid recordId) =>
        new(
            ChunkId: RecordPath(entityLogicalName, recordId),
            SourceName: entityLogicalName,
            SourceType: "dataverse");
}

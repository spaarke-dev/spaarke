namespace Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;

/// <summary>
/// FROZEN tool identifiers for the <c>dataverse.*</c> tool namespace
/// (spaarke-ai-architecture-redesign-r1 task 008, FR-P0-07 read half; task 009 write half).
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-039 name-freeze (BINDING)</b>: the local segment of every tool id MUST equal the
/// corresponding tool name on the <b>GA Dataverse MCP tool surface</b> so that a future swap
/// to the MCP transport (task-010 spike) is contract-free. The GA list is documented in
/// <c>projects/spaarke-ai-architecture-redesign-r1/notes/audit-inputs/research-dataverse-mcp-2026-07.md</c>
/// (sourced from https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp,
/// ms.date 2026-06-05):
/// </para>
/// <para>
/// GA tools: <c>search_data</c>, <c>search</c>, <c>read_query</c>, <c>create_record</c>,
/// <c>update_record</c>, <c>delete_record</c>, <c>create_table</c>, <c>update_table</c>,
/// <c>delete_table</c>, <c>describe</c>, <c>upsert_skill</c>, <c>delete_skill</c>,
/// <c>init_file_upload</c>, <c>commit_file_upload</c>, <c>file_download</c>.
/// </para>
/// <para>
/// This task ships the three READ tools (<c>describe</c>, <c>read_query</c>, <c>search_data</c>);
/// task 009 ships the three WRITE tools (<c>create_record</c>, <c>update_record</c>,
/// <c>delete_record</c>). Argument property names are frozen too — see each handler's
/// <c>sprk_jsonschema</c> seed (e.g. <c>read_query</c> takes <c>querytext</c>, exactly as GA MCP).
/// </para>
/// <para>
/// Changing any value in this file is an ADR-039 violation unless the GA MCP list itself
/// changes; the name-freeze regression test
/// (<c>DataverseToolNameFreezeTests</c>) pins these values.
/// </para>
/// </remarks>
public static class DataverseToolNames
{
    /// <summary>Namespace segment shared by all Dataverse tools (<c>sprk_namespace</c> column value).</summary>
    public const string Namespace = "dataverse";

    /// <summary>GA MCP <c>describe</c> — table/record/metadata description. FROZEN.</summary>
    public const string Describe = "dataverse.describe";

    /// <summary>GA MCP <c>read_query</c> — SQL SELECT over Dataverse data. FROZEN.</summary>
    public const string ReadQuery = "dataverse.read_query";

    /// <summary>GA MCP <c>search_data</c> — keyword search over Dataverse records. FROZEN.</summary>
    public const string SearchData = "dataverse.search_data";

    /// <summary>
    /// Task-009 write-tool ids, declared here so both tasks share one frozen surface.
    /// GA MCP <c>create_record</c> / <c>update_record</c> / <c>delete_record</c>.
    /// </summary>
    public const string CreateRecord = "dataverse.create_record";

    /// <inheritdoc cref="CreateRecord"/>
    public const string UpdateRecord = "dataverse.update_record";

    /// <inheritdoc cref="CreateRecord"/>
    public const string DeleteRecord = "dataverse.delete_record";
}

using FluentAssertions;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// ADR-039 name-freeze regression tests for the <c>dataverse.*</c> tool namespace
/// (spaarke-ai-architecture-redesign-r1 task 008, FR-P0-07).
/// </summary>
/// <remarks>
/// <para>
/// <b>Contract anchor</b>: the local segment of each <c>dataverse.*</c> tool id and the
/// LLM-facing argument property names MUST match the <b>GA Dataverse MCP tool surface</b> so a
/// future transport swap (task-010 spike) requires zero contract changes. Source of the frozen
/// list: <c>projects/spaarke-ai-architecture-redesign-r1/notes/audit-inputs/research-dataverse-mcp-2026-07.md</c>
/// (Learn ms.date 2026-06-05) — GA tools include <c>describe</c>, <c>read_query</c>,
/// <c>search_data</c>, <c>create_record</c>, <c>update_record</c>, <c>delete_record</c>.
/// </para>
/// <para>
/// If any assertion here fails, either (a) someone renamed a frozen contract — an ADR-039
/// violation; revert — or (b) the GA MCP list itself changed — update the research note first,
/// then these constants, then the sprk_analysistool seed rows, in one PR.
/// </para>
/// </remarks>
public sealed class DataverseToolNameFreezeTests
{
    // ═════════════════════════════════════════════════════════════════════════════
    // Tool ids frozen against the GA Dataverse MCP tool names
    // ═════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(DataverseToolNames.Describe, "describe")]
    [InlineData(DataverseToolNames.ReadQuery, "read_query")]
    [InlineData(DataverseToolNames.SearchData, "search_data")]
    public void ReadToolIds_MatchGaDataverseMcpNames(string toolId, string gaMcpToolName)
    {
        toolId.Should().Be(
            $"{DataverseToolNames.Namespace}.{gaMcpToolName}",
            because: "ADR-039 freezes dataverse.* tool ids against the GA Dataverse MCP tool list " +
                     "(research-dataverse-mcp-2026-07.md) so the task-010 transport swap is contract-free");
    }

    [Theory]
    [InlineData(DataverseToolNames.CreateRecord, "create_record")]
    [InlineData(DataverseToolNames.UpdateRecord, "update_record")]
    [InlineData(DataverseToolNames.DeleteRecord, "delete_record")]
    public void WriteToolIds_MatchGaDataverseMcpNames(string toolId, string gaMcpToolName)
    {
        toolId.Should().Be(
            $"{DataverseToolNames.Namespace}.{gaMcpToolName}",
            because: "task 009 builds the write handlers on the same frozen surface");
    }

    [Fact]
    public void Namespace_IsDataverse()
    {
        DataverseToolNames.Namespace.Should().Be("dataverse",
            because: "sprk_namespace='dataverse' is the FR-P0-04 bijection key for these rows");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Argument property names frozen against the GA MCP argument shapes
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Describe_ArgumentNames_MatchGaMcpContract()
    {
        // GA: describe(path, scope?) — path required, scope optional.
        var handler = new DataverseDescribeHandler(
            new Moq.Mock<IDataverseUserClient>().Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DataverseDescribeHandler>.Instance);

        var parameters = handler.Metadata.Parameters;
        parameters.Should().Contain(p => p.Name == "path" && p.Required,
            because: "GA MCP describe takes a required 'path'");
        parameters.Should().Contain(p => p.Name == "scope" && !p.Required,
            because: "GA MCP describe takes an optional 'scope'");
    }

    [Fact]
    public void ReadQuery_ArgumentNames_MatchGaMcpContract()
    {
        // GA: read_query(querytext) — single required string.
        var handler = new DataverseReadQueryHandler(
            new Moq.Mock<IDataverseUserClient>().Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DataverseReadQueryHandler>.Instance);

        var parameters = handler.Metadata.Parameters;
        parameters.Should().ContainSingle(because: "GA MCP read_query takes exactly one argument");
        parameters[0].Name.Should().Be("querytext",
            because: "the GA MCP read_query argument is named 'querytext' — frozen per ADR-039");
        parameters[0].Required.Should().BeTrue();
    }

    [Fact]
    public void SearchData_ArgumentNames_MatchGaMcpContract()
    {
        // GA: search_data(query, scope). Native transport documented deviation: scope optional
        // (no MCP scope registry) — property NAME frozen so the swap only tightens the schema.
        var handler = new DataverseSearchDataHandler(
            new Moq.Mock<IDataverseUserClient>().Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DataverseSearchDataHandler>.Instance);

        var parameters = handler.Metadata.Parameters;
        parameters.Should().Contain(p => p.Name == "query" && p.Required,
            because: "GA MCP search_data takes a required 'query'");
        parameters.Should().Contain(p => p.Name == "scope",
            because: "GA MCP search_data takes 'scope'; the native transport accepts it (optional — documented deviation)");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Write-tool argument names frozen against the GA MCP argument shapes (task 009;
    // shapes captured from the live GA Dataverse MCP tool schemas, 2026-07-05)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CreateRecord_ArgumentNames_MatchGaMcpContract()
    {
        // GA: create_record(tablename, item) — both required.
        var handler = new DataverseCreateRecordHandler(
            new Moq.Mock<IDataverseUserClient>().Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DataverseCreateRecordHandler>.Instance);

        var parameters = handler.Metadata.Parameters;
        parameters.Should().HaveCount(2, because: "GA MCP create_record takes exactly two arguments");
        parameters.Should().Contain(p => p.Name == "tablename" && p.Required,
            because: "GA MCP create_record takes a required 'tablename'");
        parameters.Should().Contain(p => p.Name == "item" && p.Required && p.Type == ToolParameterType.Object,
            because: "GA MCP create_record takes a required 'item' object");
    }

    [Fact]
    public void UpdateRecord_ArgumentNames_MatchGaMcpContract()
    {
        // GA: update_record(tablename, recordId, item) — all required.
        var handler = new DataverseUpdateRecordHandler(
            new Moq.Mock<IDataverseUserClient>().Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DataverseUpdateRecordHandler>.Instance);

        var parameters = handler.Metadata.Parameters;
        parameters.Should().HaveCount(3, because: "GA MCP update_record takes exactly three arguments");
        parameters.Should().Contain(p => p.Name == "tablename" && p.Required);
        parameters.Should().Contain(p => p.Name == "recordId" && p.Required,
            because: "the GA MCP argument is named 'recordId' (camelCase) — frozen per ADR-039");
        parameters.Should().Contain(p => p.Name == "item" && p.Required && p.Type == ToolParameterType.Object);
    }

    [Fact]
    public void DeleteRecord_ArgumentNames_MatchGaMcpContract()
    {
        // GA: delete_record(tablename, hasUserApproved, recordId) — all required.
        var handler = new DataverseDeleteRecordHandler(
            new Moq.Mock<IDataverseUserClient>().Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DataverseDeleteRecordHandler>.Instance);

        var parameters = handler.Metadata.Parameters;
        parameters.Should().HaveCount(3, because: "GA MCP delete_record takes exactly three arguments");
        parameters.Should().Contain(p => p.Name == "tablename" && p.Required);
        parameters.Should().Contain(p => p.Name == "hasUserApproved" && p.Required && p.Type == ToolParameterType.Boolean,
            because: "the GA MCP consent argument is named 'hasUserApproved' — frozen per ADR-039");
        parameters.Should().Contain(p => p.Name == "recordId" && p.Required);
    }

    [Fact]
    public void WriteHandlers_AreChatOnly()
    {
        var mock = new Moq.Mock<IDataverseUserClient>().Object;
        IToolHandler[] handlers =
        {
            new DataverseCreateRecordHandler(mock, Microsoft.Extensions.Logging.Abstractions.NullLogger<DataverseCreateRecordHandler>.Instance),
            new DataverseUpdateRecordHandler(mock, Microsoft.Extensions.Logging.Abstractions.NullLogger<DataverseUpdateRecordHandler>.Instance),
            new DataverseDeleteRecordHandler(mock, Microsoft.Extensions.Logging.Abstractions.NullLogger<DataverseDeleteRecordHandler>.Instance)
        };

        foreach (var handler in handlers)
        {
            handler.SupportedInvocationContexts.Should().Be(
                InvocationContextKind.Chat,
                because: $"{handler.HandlerId} is an agent-loop tool (P2), not a playbook node");
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Read-only declaration — all three read handlers are chat-only, side-effect-free
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadHandlers_AreChatOnly()
    {
        var mock = new Moq.Mock<IDataverseUserClient>().Object;
        IToolHandler[] handlers =
        {
            new DataverseDescribeHandler(mock, Microsoft.Extensions.Logging.Abstractions.NullLogger<DataverseDescribeHandler>.Instance),
            new DataverseReadQueryHandler(mock, Microsoft.Extensions.Logging.Abstractions.NullLogger<DataverseReadQueryHandler>.Instance),
            new DataverseSearchDataHandler(mock, Microsoft.Extensions.Logging.Abstractions.NullLogger<DataverseSearchDataHandler>.Instance)
        };

        foreach (var handler in handlers)
        {
            handler.SupportedInvocationContexts.Should().Be(
                InvocationContextKind.Chat,
                because: $"{handler.HandlerId} is an agent-loop tool (P2), not a playbook node");
        }
    }
}

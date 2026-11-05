using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 048 (owner decision 2026-11-04, D-204 extended to updates): a Signal, a Decision Record
/// and a Budget Revision are changed only by their writers. The chat update tool (<c>DataverseUpdateRecordHandler</c>) refuses the
/// three tables in <c>ValidateChat</c> (so the gate never shows a confirm dialog) and again in <c>ExecuteChatAsync</c>, with the one
/// message and code of <see cref="RecordTableRules.RefuseWriterOnlyTableUpdate"/>, before any read or write, for any item and record.
/// Every other table is updated as before.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    public static TheoryData<string> WriterOnlyTables => new() { "sprk_signal", "sprk_decisionrecord", "sprk_budgetrevision" };

    private Sprk.Bff.Api.Services.Ai.ToolValidationResult ChatValidateOf(string table, Guid recordId, params (string Column, JsonElement Value)[] item)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => Children());
        return new DataverseUpdateRecordHandler(
                _user,
                new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().AfterWriteRestamp,
                new CapturingLog<DataverseUpdateRecordHandler>(),
                _world.Resolver(),
                Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(),
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>())
            .ValidateChat(
                BuildChatInvocationContext(
                    toolArgumentsJson: JsonSerializer.Serialize(new
                    {
                        tablename = table,
                        recordId,
                        item = item.ToDictionary(i => i.Column, i => i.Value),
                    })),
                BuildAnalysisTool(nameof(DataverseUpdateRecordHandler)));
    }

    [Theory(DisplayName = "048 (D-204, must fire): a chat update of a table only its writer changes is refused at validation, with the one message")]
    [MemberData(nameof(WriterOnlyTables))]
    public void ChatValidate_ATableOnlyItsWriterChanges_IsRefused_BeforeTheConfirmDialog(string table)
    {
        var validation = ChatValidateOf(table, PairRow, ("sprk_name", Text("Renamed")));

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().ContainSingle().Which.Should().Be(RecordTableRules.ChangedOnlyByItsWriterMessage(table));
    }

    [Theory(DisplayName = "048 (D-204, must fire): a chat update of a table only its writer changes is refused on execute, before any read or write")]
    [MemberData(nameof(WriterOnlyTables))]
    public async Task ChatUpdate_ATableOnlyItsWriterChanges_IsRefused_AndNothingIsReadOrWritten(string table)
    {
        var reads = new List<string>();
        _user.GetOverride = path =>
        {
            reads.Add(path);
            return null;
        };

        // Upper case and an ordinary column: the table is matched in any case, and no item makes it valid.
        var result = await ChatUpdateOf(table.ToUpperInvariant(), PairRow, ("sprk_name", Text("Renamed")));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(RecordTableRules.ChangedOnlyByItsWriterReasonCode);
        result.ErrorMessage.Should().Be(RecordTableRules.ChangedOnlyByItsWriterMessage(table));
        result.ErrorMessage.Should().Contain("REJECTED and nothing was written");
        _user.Patches.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
        reads.Should().BeEmpty("the refusal is decided on the table alone, before any read");
    }

    [Theory(DisplayName = "048 (D-204): the refusal is the same for a missing record as for an existing one")]
    [MemberData(nameof(WriterOnlyTables))]
    public async Task ChatUpdate_ATableOnlyItsWriterChanges_TheRefusalIsTheSameForAnyRecord(string table)
    {
        _user.MissingRows.Add(OtherPairRow);

        var existing = await ChatUpdateOf(table, PairRow, ("sprk_name", Text("Renamed")));
        var missing = await ChatUpdateOf(table, OtherPairRow, ("sprk_name", Text("Renamed")));

        existing.Success.Should().BeFalse();
        (missing.Success, missing.ErrorCode, missing.ErrorMessage)
            .Should().Be((existing.Success, existing.ErrorCode, existing.ErrorMessage));
        _user.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "048 (D-204, must not fire): a chat update of another table validates and is written")]
    public async Task ChatUpdate_AnotherTable_ValidatesAndIsWritten()
    {
        ChatValidateOf("sprk_todo", PairRow, ("sprk_name", Text("Renamed"))).IsValid.Should().BeTrue();

        var result = await ChatUpdateOf("sprk_todo", PairRow, ("sprk_name", Text("Renamed")));

        result.Success.Should().BeTrue(result.ErrorMessage);
        PatchedKeys().Should().BeEquivalentTo("sprk_name");
    }
}

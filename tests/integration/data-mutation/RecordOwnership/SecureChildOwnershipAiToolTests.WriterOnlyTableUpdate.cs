using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
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
        validation.Errors.Should().ContainSingle().Which.Should().Be(DataverseUpdateRecordHandler.ChangedOnlyByItsWriterChatMessage(table))
            .And.Contain("Do NOT retry this update", "the chat tool's wording is for the model");
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
        result.ErrorMessage.Should().Be(DataverseUpdateRecordHandler.ChangedOnlyByItsWriterChatMessage(table));
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

    // ── Task 048 fix round 1: the shared cores ask the check themselves, whatever their callers pass ─────────────────

    [Theory(DisplayName = "048 fix 1 (D-204, must fire): the child-records update core refuses a table only its writer changes in a plain sentence, before any read or write")]
    [MemberData(nameof(WriterOnlyTables))]
    public async Task ChildRecordUpdateCore_ATableOnlyItsWriterChanges_IsRefused_BeforeAnyReadOrWrite(string table)
    {
        var reads = new List<string>();
        _user.GetOverride = path =>
        {
            reads.Add(path);
            return null;
        };

        var result = await Sprk.Bff.Api.Api.ChildRecordEndpoints.UpdateAsync(
            table, PairRow, JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["sprk_name"] = "Renamed" }),
            _user, _world.Resolver(),
            new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().Restamper, Children(),
            new DefaultHttpContext(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Program>.Instance, CancellationToken.None);

        var problem = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(Sprk.Bff.Api.Api.ChildRecordEndpoints.DeniedCode);
        problem.ProblemDetails.Detail.Should().Be(RecordTableRules.ChangedOnlyByItsWriterMessage(table))
            .And.NotContain("Do NOT retry", "people read this message, not the chat model");
        _user.Patches.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
        reads.Should().BeEmpty("the refusal is decided on the table alone, before any read");
    }

    [Theory(DisplayName = "048 fix 1 (D-204, must fire): the decision executors' in-process child update refuses a table only its writer changes")]
    [MemberData(nameof(WriterOnlyTables))]
    public async Task DecisionRouteUpdate_ATableOnlyItsWriterChanges_IsRefused_AndNothingIsWritten(string table)
    {
        var reply = await DecisionRouteUpdate(table, PairRow, new Dictionary<string, object?> { ["sprk_name"] = "Renamed" });

        reply.Status.Should().Be(StatusCodes.Status403Forbidden, reply.Detail);
        reply.Detail.Should().Be(RecordTableRules.ChangedOnlyByItsWriterMessage(table));
        _user.Patches.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    [Fact(DisplayName = "048 fix 1 (D-204, must not fire): the decision executors' in-process child update still updates a to-do")]
    public async Task DecisionRouteUpdate_ATodo_IsStillWritten()
    {
        var reply = await DecisionRouteUpdate("sprk_todo", PairRow, new Dictionary<string, object?> { ["sprk_name"] = "Renamed" });

        reply.Status.Should().Be(StatusCodes.Status204NoContent, reply.Detail);
        PatchedKeys().Should().BeEquivalentTo("sprk_name");
    }

    [Fact(DisplayName = "048 fix 1 (D-204, must not fire): the child-records update core still updates an ordinary table")]
    public async Task ChildRecordUpdateCore_AnOrdinaryTable_IsStillWritten()
    {
        var result = await Sprk.Bff.Api.Api.ChildRecordEndpoints.UpdateAsync(
            "sprk_todo", PairRow, JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["sprk_name"] = "Renamed" }),
            _user, _world.Resolver(),
            new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().Restamper, Children(),
            new DefaultHttpContext(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Program>.Instance, CancellationToken.None);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NoContent>();
        PatchedKeys().Should().BeEquivalentTo("sprk_name");
    }

    [Theory(DisplayName = "048 fix 1 (D-204, must fire): the re-file core refuses a table only its writer changes before any read or write")]
    [MemberData(nameof(WriterOnlyTables))]
    public async Task RefileCore_ATableOnlyItsWriterChanges_IsRefused_AndNothingIsReadOrWritten(string table)
    {
        var reads = new List<string>();
        _user.GetOverride = path =>
        {
            reads.Add(path);
            return null;
        };

        var outcome = await Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite.RefileAsync(
            _user, _world.Resolver(), table, PairRow, table + "s", table + "id",
            new Sprk.Bff.Api.Services.Dataverse.DataverseWriteItemMapper.MappedItem(
                JsonSerializer.Serialize(new { sprk_name = "Renamed" }), ["sprk_name"]),
            callerObjectId: null, CancellationToken.None);

        outcome.Invalid.Should().Be(RecordTableRules.ChangedOnlyByItsWriterMessage(table));
        (outcome.Written, outcome.NotARefile).Should().Be((false, false));
        _user.Patches.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
        reads.Should().BeEmpty("the refusal is decided on the table alone, before any read");
    }
}

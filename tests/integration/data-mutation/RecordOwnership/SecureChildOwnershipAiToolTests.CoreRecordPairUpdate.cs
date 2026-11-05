using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// The core record pair (<c>sprk_corerecordtype</c> + <c>sprk_corerecordid</c>) of a Signal and a Decision Record is fixed after
/// create on BFF update paths (<c>RecordTableRule.FixedAfterCreateColumns</c>). The chat update tool
/// (<c>DataverseUpdateRecordHandler</c>) refuses an item of either table that names either column, in any key form, as a tool
/// error with nothing written; the check is on the item's shape only, so it answers the same for any record id. Every other
/// column of those tables, and the same column names on any other table, are written as before.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private const string FixedAfterCreateCode = "record_update.fixed_after_create";

    private static readonly Guid PairRow = Guid.Parse("c0e00000-0000-4000-8000-000000000001");
    private static readonly Guid OtherPairRow = Guid.Parse("c0e00000-0000-4000-8000-000000000002");
    private static readonly Guid PairRecordType = Guid.Parse("c0e00000-0000-4000-8000-0000000000a1");

    public static TheoryData<string> CorePairTables => new() { "sprk_signal", "sprk_decisionrecord" };

    /// <summary>Each core-pair key form an item can carry, alone or with an ordinary column.</summary>
    public static TheoryData<string, string> CorePairItems()
    {
        var data = new TheoryData<string, string>();
        foreach (var table in new[] { "sprk_signal", "sprk_decisionrecord" })
        {
            data.Add(table, "id");
            data.Add(table, "type");
            data.Add(table, "both");
            data.Add(table, "id-mixed-case");
            data.Add(table, "type-mixed-case");
            data.Add(table, "type-bind");
            data.Add(table, "both-plus-allowed");
        }

        return data;
    }

    private static (string Column, JsonElement Value)[] CorePairItem(string shape) => shape switch
    {
        "id" => [("sprk_corerecordid", Text(PairRecordType.ToString("D")))],
        "type" => [Lookup("sprk_corerecordtype", "sprk_recordtype_ref", PairRecordType)],
        "both" =>
        [
            ("sprk_corerecordid", Text(PairRecordType.ToString("D"))),
            Lookup("sprk_corerecordtype", "sprk_recordtype_ref", PairRecordType),
        ],
        "id-mixed-case" => [("SPRK_CoreRecordId", Text(PairRecordType.ToString("D")))],
        "type-mixed-case" => [Lookup("Sprk_CoreRecordType", "sprk_recordtype_ref", PairRecordType)],
        "type-bind" => [("sprk_CoreRecordType@odata.bind", Text($"/sprk_recordtype_refs({PairRecordType:D})"))],
        "both-plus-allowed" =>
        [
            ("sprk_name", Text("Renamed")),
            ("sprk_corerecordid", Text(PairRecordType.ToString("D"))),
            Lookup("sprk_corerecordtype", "sprk_recordtype_ref", PairRecordType),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
    };

    private Task<Sprk.Bff.Api.Services.Ai.ToolResult> ChatUpdateOf(
        string table, Guid recordId, params (string Column, JsonElement Value)[] item)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => Children());
        var scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new DataverseUpdateRecordHandler(
                _user,
                new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().AfterWriteRestamp,
                new CapturingLog<DataverseUpdateRecordHandler>(),
                _world.Resolver(),
                Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(),
                scopes)
            .ExecuteChatAsync(
                BuildChatInvocationContext(
                    toolArgumentsJson: JsonSerializer.Serialize(new
                    {
                        tablename = table,
                        recordId,
                        item = item.ToDictionary(i => i.Column, i => i.Value),
                    })) with
                { UserId = Guid.NewGuid().ToString() },
                BuildAnalysisTool(nameof(DataverseUpdateRecordHandler)), CancellationToken.None);
    }

    [Theory]
    [MemberData(nameof(CorePairItems))]
    public async Task ChatUpdate_ACorePairColumn_OnASignalOrDecisionRecord_IsRefused_AndNothingIsWrittenOrRead(string table, string shape)
    {
        var reads = new List<string>();
        _user.GetOverride = path =>
        {
            reads.Add(path);
            return null;
        };

        var result = await ChatUpdateOf(table, PairRow, CorePairItem(shape));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(FixedAfterCreateCode, result.ErrorMessage);
        result.ErrorMessage.Should().Contain("NOT written");
        _user.Patches.Should().BeEmpty("nothing of the item is written, not even its other columns");
        reads.Should().BeEmpty("the refusal is decided on the item's shape, before any read");
    }

    [Theory]
    [MemberData(nameof(CorePairTables))]
    public async Task ChatUpdate_TheRefusal_IsTheSameForAMissingRecordAsForAnExistingOne(string table)
    {
        _user.MissingRows.Add(OtherPairRow);

        var existing = await ChatUpdateOf(table, PairRow, CorePairItem("both"));
        var missing = await ChatUpdateOf(table, OtherPairRow, CorePairItem("both"));

        existing.Success.Should().BeFalse();
        (missing.Success, missing.ErrorCode, missing.ErrorMessage)
            .Should().Be((existing.Success, existing.ErrorCode, existing.ErrorMessage));
        _user.Patches.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(CorePairTables))]
    public async Task ChatUpdate_AnotherColumn_OnASignalOrDecisionRecord_IsWritten(string table)
    {
        var result = await ChatUpdateOf(table, PairRow, ("sprk_name", Text("Renamed")));

        result.Success.Should().BeTrue(result.ErrorMessage);
        PatchedKeys().Should().BeEquivalentTo("sprk_name");
    }

    // ── The shared update cores: today every caller passes a table without the pair; each core asks the check itself ──

    [Fact]
    public async Task ChildRecordUpdateCore_ACorePairColumnOfASignal_IsRefused_AndNothingIsReadOrWritten()
    {
        var reads = new List<string>();
        _user.GetOverride = path =>
        {
            reads.Add(path);
            return null;
        };

        var result = await Sprk.Bff.Api.Api.ChildRecordEndpoints.UpdateAsync(
            "sprk_signal", PairRow,
            JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["sprk_name"] = "Renamed",
                ["sprk_corerecordid"] = PairRecordType.ToString("D"),
            }),
            _user, _world.Resolver(),
            new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().Restamper, Children(),
            new Microsoft.AspNetCore.Http.DefaultHttpContext(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Program>.Instance, CancellationToken.None);

        var problem = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(Microsoft.AspNetCore.Http.StatusCodes.Status403Forbidden);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(Sprk.Bff.Api.Api.ChildRecordEndpoints.DeniedCode);
        problem.ProblemDetails.Detail.Should().Contain("sprk_corerecordid of a sprk_signal is set when the record is created");
        _user.Patches.Should().BeEmpty("nothing of the body is written, not even its other columns");
        _user.Posts.Should().BeEmpty();
        reads.Should().BeEmpty("the refusal is decided on the body's shape, before any read");
    }

    [Fact]
    public async Task RefileCore_ACorePairColumnOfADecisionRecord_IsRefused_AndNothingIsReadOrWritten()
    {
        var reads = new List<string>();
        _user.GetOverride = path =>
        {
            reads.Add(path);
            return null;
        };

        var outcome = await Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite.RefileAsync(
            _user, _world.Resolver(), "sprk_decisionrecord", PairRow, "sprk_decisionrecords", "sprk_decisionrecordid",
            new Sprk.Bff.Api.Services.Dataverse.DataverseWriteItemMapper.MappedItem(
                JsonSerializer.Serialize(new { sprk_name = "Renamed", sprk_corerecordid = PairRecordType.ToString("D") }),
                ["sprk_name", "sprk_corerecordid"]),
            callerObjectId: null, CancellationToken.None);

        outcome.Invalid.Should().Contain($"({FixedAfterCreateCode})")
            .And.Contain("sprk_corerecordid of a sprk_decisionrecord is set when the record is created");
        (outcome.Written, outcome.NotARefile).Should().Be((false, false));
        _user.Patches.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
        reads.Should().BeEmpty("the refusal is decided on the item's shape, before any read");
    }

    [Fact]
    public async Task ChatUpdate_AColumnNamedLikeTheCorePair_OnAnotherTable_IsWritten()
    {
        // No other table carries the pair (SpaarkeMaster Entity.xml); a table without it is not asked.
        var result = await ChatUpdateOf("sprk_todo", PairRow, ("sprk_corerecordid", Text(PairRecordType.ToString("D"))));

        result.Success.Should().BeTrue(result.ErrorMessage);
        PatchedKeys().Should().Contain("sprk_corerecordid");
    }
}

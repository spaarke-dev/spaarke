using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// fm task 054b: a communication's participants (<c>sprk_sentby</c>, <c>sprk_directrecipient</c>) and thread lookup, and its
/// thread's type and default flag, are written only by the BFF's typed writers
/// (<see cref="InheritedAccessPermission.BffOwnedCommunicationColumns"/>). The chat tools refuse an item naming one, before any
/// write: the create tool both on its run-as-user path (an unfiled communication, a thread) and through the one server create
/// (a filed communication), the update tool (the one update-side check), and the one re-file core. The filing routes accept
/// regarding columns only. Must not fire: an ordinary communication is still created, and a filed one still gets the
/// server-stamped "for" person.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    public static TheoryData<string> CommunicationBffColumns => new() { "sprk_sentby", "sprk_directrecipient", "sprk_communicationthread" };

    public static TheoryData<string, string> EveryBffOwnedCommunicationColumn => new()
    {
        { "sprk_communication", "sprk_sentby" },
        { "sprk_communication", "sprk_directrecipient" },
        { "sprk_communication", "sprk_communicationthread" },
        { "sprk_communicationthread", "sprk_threadtype" },
        { "sprk_communicationthread", "sprk_isdefaultthread" },
    };

    private static readonly Guid SomeThread = Guid.Parse("054b0000-0000-4000-8000-0000000000f1");

    /// <summary>The item value for <paramref name="column"/>: a lookup for the three lookups, a plain value for the flags.</summary>
    private static (string Column, JsonElement Value) BffColumnItem(string column) => column switch
    {
        "sprk_sentby" or "sprk_directrecipient" => Lookup(column, "systemuser", Caller),
        "sprk_communicationthread" => Lookup(column, "sprk_communicationthread", SomeThread),
        "sprk_threadtype" => (column, JsonSerializer.SerializeToElement(100000001)),
        _ => (column, JsonSerializer.SerializeToElement(true)),
    };

    // ── dataverse.create_record ───────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "fm 054b (must fire): a chat create of an UNFILED communication naming a BFF-owned column is refused, and nothing is created")]
    [MemberData(nameof(CommunicationBffColumns))]
    public async Task CreateRecord_AnUnfiledCommunicationNamingABffOwnedColumn_IsRefused_054b(string column)
    {
        var result = await CreateRecord("sprk_communication", ("sprk_name", Text("Note")), BffColumnItem(column));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain($"'{column}'");
        _user.Posts.Should().BeEmpty("the run-as-user create is never sent");
        _appCreates.Should().BeEmpty();
    }

    [Theory(DisplayName = "fm 054b (must fire): a chat create of a FILED communication naming a BFF-owned column is refused by the one server create, and nothing is created")]
    [MemberData(nameof(CommunicationBffColumns))]
    public async Task CreateRecord_AFiledCommunicationNamingABffOwnedColumn_IsRefused_054b(string column)
    {
        var result = await CreateRecord(
            "sprk_communication", Lookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter), BffColumnItem(column));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain($"'{column}'");
        _user.Posts.Should().BeEmpty();
        _appCreates.Should().BeEmpty();
    }

    [Theory(DisplayName = "fm 054b (must fire): a chat create of a thread naming its type or default flag is refused, and nothing is created")]
    [InlineData("sprk_threadtype")]
    [InlineData("sprk_isdefaultthread")]
    public async Task CreateRecord_AThreadNamingItsTypeOrDefaultFlag_IsRefused_054b(string column)
    {
        var result = await CreateRecord("sprk_communicationthread", ("sprk_name", Text("Thread")), BffColumnItem(column));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain($"'{column}'");
        _user.Posts.Should().BeEmpty();
        _appCreates.Should().BeEmpty();
    }

    [Fact(DisplayName = "fm 054b (must not fire): a filed chat-created communication still gets the server-stamped 'for' person")]
    public async Task CreateRecord_AFiledCommunication_StillGetsTheServerStampedSender_054b()
    {
        var result = await CreateRecord("sprk_communication", Lookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var fields = _appCreates.Should().ContainSingle(c => c.Table == "sprk_communication").Subject.Fields;
        Bind(fields, "sprk_SentBy@odata.bind").Should().Be($"/systemusers({Caller:D})");
        fields.Keys.Should().NotContain(k => k.StartsWith("sprk_DirectRecipient", StringComparison.OrdinalIgnoreCase)
            || k.StartsWith("sprk_CommunicationThread", StringComparison.OrdinalIgnoreCase));
    }

    // ── dataverse.update_record and the one re-file core ──────────────────────────────────────────────────────────

    [Theory(DisplayName = "fm 054b (must fire): a chat update naming a BFF-owned column of a communication or its thread is refused, before any read or write")]
    [MemberData(nameof(EveryBffOwnedCommunicationColumn))]
    public async Task ChatUpdate_NamingABffOwnedColumn_IsRefused_AndNothingIsReadOrWritten_054b(string table, string column)
    {
        var reads = new List<string>();
        _user.GetOverride = path =>
        {
            reads.Add(path);
            return null;
        };

        var result = await ChatUpdateOf(table, PairRow, ("sprk_name", Text("Renamed")), BffColumnItem(column));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(InheritedAccessPermission.BffOwnedCommunicationColumnReasonCode, result.ErrorMessage);
        result.ErrorMessage.Should().Contain("NOT written").And.Contain(column);
        _user.Patches.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
        reads.Should().BeEmpty("the refusal is decided on the item's shape, before any read");
    }

    [Theory(DisplayName = "fm 054b (must fire): the one re-file core refuses an item naming a BFF-owned column of a communication, before any read or write")]
    [MemberData(nameof(CommunicationBffColumns))]
    public async Task RefileCore_NamingABffOwnedColumn_IsRefused_AndNothingIsReadOrWritten_054b(string column)
    {
        var reads = new List<string>();
        _user.GetOverride = path =>
        {
            reads.Add(path);
            return null;
        };

        var outcome = await OwnedChildWrite.RefileAsync(
            _user, _world.Resolver(), "sprk_communication", PairRow, "sprk_communications", "sprk_communicationid",
            new DataverseWriteItemMapper.MappedItem(
                JsonSerializer.Serialize(new Dictionary<string, object> { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})" }),
                ["sprk_regardingmatter", column]),
            callerObjectId: null, CancellationToken.None);

        outcome.Invalid.Should().Contain(column).And.Contain(InheritedAccessPermission.BffOwnedCommunicationColumnReasonCode);
        (outcome.Written, outcome.NotARefile).Should().Be((false, false));
        _user.Patches.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
        reads.Should().BeEmpty("the refusal is decided on the item's shape, before any read");
    }

    // ── The filing routes (/communications/{id}/filing and the child-record filing route) ────────────────────────

    [Theory(DisplayName = "fm 054b (pin): the filing shape refuses each BFF-owned column of a communication as not a filing column")]
    [InlineData("sprk_sentby")]
    [InlineData("sprk_SentBy@odata.bind")]
    [InlineData("sprk_directrecipient")]
    [InlineData("sprk_DirectRecipient@odata.bind")]
    [InlineData("sprk_communicationthread")]
    [InlineData("sprk_CommunicationThread@odata.bind")]
    [InlineData("sprk_threadtype")]
    [InlineData("sprk_isdefaultthread")]
    public void FilingShape_RefusesABffOwnedColumnOfACommunication_054b(string key)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            [key] = "x",
        }));

        var problem = Sprk.Bff.Api.Api.ChildRecordEndpoints.FilingShapeProblem(document.RootElement.Clone(), "sprk_communication");

        problem.Should().NotBeNull();
        Status(problem!).Should().Be(StatusCodes.Status400BadRequest);
        ReasonCode(problem!).Should().Be(Sprk.Bff.Api.Api.ChildRecordEndpoints.NotFilingCode);
    }

    [Fact(DisplayName = "fm 054b (pin): the generic child-record re-file route does not take a communication")]
    public void ChildRecordRefileRoute_DoesNotTakeACommunication_054b() =>
        Sprk.Bff.Api.Api.ChildRecordEndpoints.RefileTables.Should().NotContain("sprk_communication")
            .And.NotContain("sprk_communicationthread");
}

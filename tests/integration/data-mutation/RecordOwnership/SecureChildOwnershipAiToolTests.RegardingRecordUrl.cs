using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 045f on the CALLER update paths — the child-record PATCH, the event filing route and
/// the chat tool <c>dataverse.update_record</c> — through the real handlers and the caller's scripted Dataverse: on a pair table
/// outside the invoice family, a record URL the write sends names the record the row's regarding pair names after the write
/// (the pair the same write sends, else the stored one, read as the caller) and is written in canonical form. A URL naming
/// another table or record, or no record, is refused through the path's existing invalid-payload form with
/// <see cref="RegardingPairWrite.RecordUrlMismatchCode"/>, and nothing is written; a stored-pair read that fails writes nothing.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid RecordTypeProject = Guid.Parse("a2470000-0000-4000-8000-0000000000c3");
    private static readonly Guid UrlOtherMatter = Guid.Parse("a2470000-0000-4000-8000-0000000045f1");

    private static string RecordUrlOf(string table, Guid id) =>
        RegardingRecordType.BuildRecordUrl(table, id.ToString("D").ToLowerInvariant());

    /// <summary>A to-do whose stored pair names the ordinary matter (by its type and id), as the caller reads it.</summary>
    private void ToDoWhosePairNamesTheOrdinaryMatter()
    {
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _user.RowValues[Todo] = new()
        {
            ["sprk_regardingrecordid"] = OrdinaryMatter.ToString("D"),
            ["_sprk_regardingrecordtype_value"] = RecordTypeMatter.ToString("D"),
        };
        CatalogKnowsTheMatterType();
    }

    /// <summary>An event whose stored pair names the ordinary project; the catalog knows the matter and the project types.</summary>
    private void EventWhosePairNamesTheOrdinaryProject()
    {
        _world.WithRecord("sprk_event", Event, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _user.RowValues[Event] = new()
        {
            ["sprk_regardingrecordid"] = OrdinaryProject.ToString("D"),
            ["_sprk_regardingrecordtype_value"] = RecordTypeProject.ToString("D"),
        };
        _user.RecordTypes[RecordTypeProject] = "sprk_project";
        CatalogKnowsTheMatterType();
    }

    private void RefusedAsUrlMismatch(IResult result)
    {
        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        Detail(result).Should().Contain(RegardingPairWrite.RecordUrlMismatchCode).And.NotContain(OrdinaryMatter.ToString("D"));
        _user.Patches.Should().BeEmpty();
    }

    private void RefusedAsUrlMismatch(ToolResult result)
    {
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed, result.ErrorMessage);
        result.ErrorMessage.Should().Contain(RegardingPairWrite.RecordUrlMismatchCode);
        _user.Patches.Should().BeEmpty();
    }

    public static TheoryData<string, string> UrlsNamingAnotherRecordOrNone() => new()
    {
        { "another record", $"/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={UrlOtherMatter:D}" },
        { "another table", $"/main.aspx?pagetype=entityrecord&etn=sprk_project&id={OrdinaryMatter:D}" },
        { "no record", "/main.aspx?etn=sprk_matter" },
        { "not a URL", "Acme v. Widget" },
    };

    public static TheoryData<string, string> UrlsOfTheOrdinaryMatter() => new()
    {
        { "relative", $"/main.aspx?pagetype=entityrecord&etn=sprk_matter&id={OrdinaryMatter:D}" },
        { "absolute on another host", $"https://other.example/main.aspx?appid=0a1b2c3d-0000-4000-8000-000000000001&pagetype=entityrecord&etn=sprk_matter&id={OrdinaryMatter:D}" },
        { "upper case", $"http://other.example/main.aspx?etn=SPRK_MATTER&id={OrdinaryMatter.ToString("D").ToUpperInvariant()}" },
        { "braced", $"/main.aspx?etn=sprk_matter&id={OrdinaryMatter:B}" },
    };

    // ── The child-record PATCH ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(UrlsNamingAnotherRecordOrNone))]
    public async Task ChildRefile_AUrlNamingAnotherRecordThanTheStoredPair_OrNone_Is400_AndNothingIsWritten(string form, string url)
    {
        ToDoWhosePairNamesTheOrdinaryMatter();

        var result = await RefileChild("sprk_todo", Todo, new() { ["sprk_regardingrecordurl"] = url });

        RefusedAsUrlMismatch(result);
        form.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ChildRefile_AUrlOnARowWithNoPair_Is400_AndNothingIsWritten()
    {
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);

        var result = await RefileChild("sprk_todo", Todo, new() { ["sprk_regardingrecordurl"] = RecordUrlOf("sprk_matter", OrdinaryMatter) });

        RefusedAsUrlMismatch(result);
    }

    [Fact]
    public async Task ChildRefile_AStoredPairReadFault_WritesNothing()
    {
        // Only the full-row read the URL decision makes (the route's own reads select columns, so they still answer).
        ToDoWhosePairNamesTheOrdinaryMatter();
        _user.GetOverride = path => path == $"sprk_todos({Todo:D})"
            ? DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.BadRequest, "Scripted fault.")
            : null;

        var result = await RefileChild("sprk_todo", Todo, new() { ["sprk_regardingrecordurl"] = RecordUrlOf("sprk_matter", OrdinaryMatter) });

        Status(result).Should().Be(StatusCodes.Status503ServiceUnavailable, Detail(result));
        _user.Patches.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(UrlsOfTheOrdinaryMatter))]
    public async Task ChildRefile_AUrlOfTheStoredPairsRecord_IsWritten_InCanonicalForm(string form, string url)
    {
        ToDoWhosePairNamesTheOrdinaryMatter();

        var result = await RefileChild("sprk_todo", Todo, new() { ["sprk_regardingrecordurl"] = url });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        PatchedColumn(_user.Patches.Should().ContainSingle().Subject.Body, "sprk_regardingrecordurl").GetString()
            .Should().Be(RecordUrlOf("sprk_matter", OrdinaryMatter), form);
    }

    [Fact]
    public async Task ChildRefile_AUrlClear_IsWritten()
    {
        ToDoWhosePairNamesTheOrdinaryMatter();

        var result = await RefileChild("sprk_todo", Todo, new() { ["sprk_regardingrecordurl"] = null });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        PatchedColumn(_user.Patches.Should().ContainSingle().Subject.Body, "sprk_regardingrecordurl").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    // ── The event filing route: a pair move ─────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> MoveTheEventToTheOrdinaryMatter(string? url)
    {
        var payload = new Dictionary<string, object?>
        {
            ["sprk_regardingrecordid"] = OrdinaryMatter.ToString("D"),
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeMatter:D})",
        };
        if (url is not null)
            payload["sprk_regardingrecordurl"] = url;
        return payload;
    }

    [Fact]
    public async Task EventFiling_APairMoveWithAUrlOfTheOldRecord_Is400_AndNothingIsWritten()
    {
        EventWhosePairNamesTheOrdinaryProject();

        var result = await RefileEvent(MoveTheEventToTheOrdinaryMatter(RecordUrlOf("sprk_project", OrdinaryProject)));

        Status(result).Should().Be(StatusCodes.Status400BadRequest, Detail(result));
        Detail(result).Should().Contain(RegardingPairWrite.RecordUrlMismatchCode);
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task EventFiling_APairMoveWithAUrlOfTheNewRecord_IsWritten_InCanonicalForm()
    {
        EventWhosePairNamesTheOrdinaryProject();

        var result = await RefileEvent(MoveTheEventToTheOrdinaryMatter($"https://other.example/main.aspx?etn=sprk_matter&id={OrdinaryMatter:B}"));

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        PatchedColumn(_user.Patches.Should().ContainSingle().Subject.Body, "sprk_regardingrecordurl").GetString()
            .Should().Be(RecordUrlOf("sprk_matter", OrdinaryMatter));
    }

    [Fact]
    public async Task EventFiling_APairMoveWithNoUrl_KeepsThePlannersDerivedUrl()
    {
        EventWhosePairNamesTheOrdinaryProject();

        var result = await RefileEvent(MoveTheEventToTheOrdinaryMatter(url: null));

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        PatchedColumn(_user.Patches.Should().ContainSingle().Subject.Body, "sprk_regardingrecordurl").GetString()
            .Should().Be(RegardingRecordType.BuildRecordUrl("sprk_matter", OrdinaryMatter.ToString("D")));
    }

    // ── The chat tool ──────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(UrlsNamingAnotherRecordOrNone))]
    public async Task UpdateRecord_AUrlNamingAnotherRecordThanTheStoredPair_OrNone_IsRefused_AndNothingIsWritten(string form, string url)
    {
        ToDoWhosePairNamesTheOrdinaryMatter();

        var result = await UpdateRecord("sprk_todo", Todo, ("sprk_regardingrecordurl", Text(url)));

        RefusedAsUrlMismatch(result);
        form.Should().NotBeEmpty();
    }

    [Fact]
    public async Task UpdateRecord_APairMoveWithAUrlOfTheOldRecord_IsRefused_AndNothingIsWritten()
    {
        EventWhosePairNamesTheOrdinaryProject();

        var result = await UpdateRecord("sprk_event", Event,
            ("sprk_regardingrecordid", Text(OrdinaryMatter.ToString("D"))),
            Lookup("sprk_regardingrecordtype", "sprk_recordtype_ref", RecordTypeMatter),
            ("sprk_regardingrecordurl", Text(RecordUrlOf("sprk_project", OrdinaryProject))));

        RefusedAsUrlMismatch(result);
    }

    [Fact]
    public async Task UpdateRecord_AProjectWhosePairIsBffManaged_AUrlOfAnotherRecord_IsRefused_AndOfItsOwnIsWritten_InCanonicalForm()
    {
        _world.WithRecord("sprk_project", OrdinaryProject, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _user.RowValues[OrdinaryProject] = new()
        {
            ["sprk_regardingrecordid"] = OrdinaryMatter.ToString("D"),
            ["_sprk_regardingrecordtype_value"] = RecordTypeMatter.ToString("D"),
        };
        CatalogKnowsTheMatterType();

        RefusedAsUrlMismatch(await UpdateRecord("sprk_project", OrdinaryProject,
            ("sprk_regardingrecordurl", Text(RecordUrlOf("sprk_matter", UrlOtherMatter)))));

        var own = await UpdateRecord("sprk_project", OrdinaryProject,
            ("sprk_regardingrecordurl", Text($"https://other.example/main.aspx?etn=sprk_matter&id={OrdinaryMatter:B}")));

        own.Success.Should().BeTrue(own.ErrorMessage);
        PatchedColumn(_user.Patches.Should().ContainSingle().Subject.Body, "sprk_regardingrecordurl").GetString()
            .Should().Be(RecordUrlOf("sprk_matter", OrdinaryMatter));
    }

    [Fact]
    public async Task UpdateRecord_AStoredPairReadFault_WritesNothing()
    {
        ToDoWhosePairNamesTheOrdinaryMatter();
        _user.GetOverride = path => path.StartsWith($"sprk_todos({Todo:D})", StringComparison.Ordinal)
            ? DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.BadRequest, "Scripted fault.")
            : null;

        var result = await UpdateRecord("sprk_todo", Todo, ("sprk_regardingrecordurl", Text(RecordUrlOf("sprk_matter", OrdinaryMatter))));

        result.Success.Should().BeFalse();
        _user.Patches.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(UrlsOfTheOrdinaryMatter))]
    public async Task UpdateRecord_AUrlOfTheStoredPairsRecord_IsWritten_InCanonicalForm(string form, string url)
    {
        ToDoWhosePairNamesTheOrdinaryMatter();

        var result = await UpdateRecord("sprk_todo", Todo, ("sprk_regardingrecordurl", Text(url)));

        result.Success.Should().BeTrue(result.ErrorMessage);
        PatchedColumn(_user.Patches.Should().ContainSingle().Subject.Body, "sprk_regardingrecordurl").GetString()
            .Should().Be(RecordUrlOf("sprk_matter", OrdinaryMatter), form);
    }

    [Fact]
    public async Task UpdateRecord_AUrlClear_IsWritten()
    {
        ToDoWhosePairNamesTheOrdinaryMatter();

        var result = await UpdateRecord("sprk_todo", Todo, ("sprk_regardingrecordurl", JsonNull));

        result.Success.Should().BeTrue(result.ErrorMessage);
        PatchedColumn(_user.Patches.Should().ContainSingle().Subject.Body, "sprk_regardingrecordurl").ValueKind
            .Should().Be(JsonValueKind.Null);
    }
}

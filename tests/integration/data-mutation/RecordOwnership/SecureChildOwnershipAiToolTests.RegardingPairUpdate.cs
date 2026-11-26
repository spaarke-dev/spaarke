using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 045b: the update planner on the CALLER update paths — the browser re-file core
/// (<c>PATCH /api/v1/child-records/{table}/{id}</c>, the event and communication filing routes) and the chat tool
/// <c>dataverse.update_record</c> — through the real handlers and the caller's scripted Dataverse. An update that leaves a
/// type with no id has the id filled from the row's one agreeing lookup, or the type cleared (owner ruling Q2 (a)); a moved
/// pair's display columns are re-derived (Q7 (b)); a catalog read that fails writes nothing.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static JsonElement PatchedColumn(string body, string column)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.EnumerateObject()
            .First(p => string.Equals(SecureRootInheritance.NormalizeColumn(p.Name), column, StringComparison.OrdinalIgnoreCase))
            .Value.Clone();
    }

    private void ToDoFiledUnderTheOrdinaryMatterByItsTypedLookupOnly()
    {
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _user.RowValues[Todo] = new() { ["_sprk_regardingmatter_value"] = OrdinaryMatter.ToString("D") };
        _user.RowValues[OrdinaryMatter] = new() { [RegardingRecordType.GetPrimaryNameField("sprk_matter")!] = "Acme v. Widget" };
    }

    [Fact]
    public async Task ChildRefile_ATypeOnlyUpdate_FillsTheIdFromTheRowsTypedLookup_AndItsDisplay()
    {
        ToDoFiledUnderTheOrdinaryMatterByItsTypedLookupOnly();
        CatalogKnowsTheMatterType();

        var result = await RefileChild("sprk_todo", Todo, new()
        {
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        var body = _user.Patches.Should().ContainSingle().Subject.Body;
        PatchedColumn(body, SecureRootInheritance.PairIdColumn).GetString().Should().Be(OrdinaryMatter.ToString("D"));
        PatchedColumn(body, "sprk_regardingrecordname").GetString().Should().Be("Acme v. Widget");
        PatchedColumn(body, "sprk_regardingrecordurl").GetString()
            .Should().Be(RegardingRecordType.BuildRecordUrl("sprk_matter", OrdinaryMatter.ToString("D")));
    }

    [Fact]
    public async Task ChildRefile_ATypeOnlyUpdate_WithNoAgreeingLookup_ClearsTheType_AndWritesNoHalfPair()
    {
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        CatalogKnowsTheMatterType();

        var result = await RefileChild("sprk_todo", Todo, new()
        {
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        var body = _user.Patches.Should().ContainSingle().Subject.Body;
        PatchedColumn(body, SecureRootInheritance.PairTypeColumn).ValueKind.Should().Be(JsonValueKind.Null, "a type naming nothing is cleared");
        PatchedColumn(body, SecureRootInheritance.PairIdColumn).ValueKind.Should().Be(JsonValueKind.Null);
        PatchedColumn(body, "sprk_regardingrecordname").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ChildRefile_ATypeOnlyUpdate_ToBeCleared_WithoutAppendToOnTheType_IsTheUniform404_AndNothingIsWritten()
    {
        // As 045a F4: the clear removes the type before the re-file checks, so AppendTo on it is asked first — a caller who may
        // not append to the type gets the same not-found as for a missing catalog row, never a cleared write.
        _world.WithRecord("sprk_todo", Todo, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        CatalogKnowsTheMatterType();
        _user.Held.Remove("prvAppendTosprk_recordtype_ref"); // the catalog is organization-owned: AppendTo is a privilege

        var result = await RefileChild("sprk_todo", Todo, new()
        {
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status404NotFound, Detail(result));
        ReasonCode(result).Should().Be(ChildRecordEndpoints.NotFoundCode);
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task ChildRefile_ATypeOnlyUpdate_WhoseCatalogReadFails_WritesNothing()
    {
        ToDoFiledUnderTheOrdinaryMatterByItsTypedLookupOnly();
        _user.GetOverride = path => path.StartsWith("sprk_recordtype_refs(", StringComparison.Ordinal)
            ? DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.BadRequest, "Scripted fault.")
            : null;

        var result = await RefileChild("sprk_todo", Todo, new()
        {
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeMatter:D})",
        });

        Status(result).Should().BeGreaterThanOrEqualTo(400, "never a half pair");
        _user.Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task EventFiling_MovingThePair_ReDerivesItsDisplayColumns()
    {
        _world.WithRecord("sprk_event", Event, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        CatalogKnowsTheMatterType();
        _user.RowValues[OrdinaryMatter] = new() { [RegardingRecordType.GetPrimaryNameField("sprk_matter")!] = "Acme v. Widget" };

        var result = await RefileEvent(new()
        {
            ["sprk_regardingrecordid"] = OrdinaryMatter.ToString("D"),
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        var body = _user.Patches.Should().ContainSingle().Subject.Body;
        PatchedColumn(body, "sprk_regardingrecordname").GetString().Should().Be("Acme v. Widget");
        PatchedColumn(body, "sprk_regardingrecordurl").GetString()
            .Should().Be(RegardingRecordType.BuildRecordUrl("sprk_matter", OrdinaryMatter.ToString("D")));
    }

    /// <summary>045c coordinator finding (Q7): the communication filing route writes only what it is sent; a caller moving the
    /// pair without its number no longer leaves the old record's number (re-derived from the type, else cleared).</summary>
    [Fact]
    public async Task CommunicationFiling_MovingThePairWithoutItsNumber_ClearsOrReDerivesIt_NeverLeavesItStale()
    {
        var communication = Guid.Parse("a2470000-0000-4000-8000-000000000005");
        _world.WithRecord("sprk_communication", communication, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _user.RowValues[communication] = new() { ["sprk_regardingrecordnumber"] = "OLD-0001" };
        CatalogKnowsTheMatterType();
        _user.RowValues[OrdinaryMatter] = new() { [RegardingRecordType.GetPrimaryNameField("sprk_matter")!] = "Acme v. Widget" };

        var result = await RefileCommunication(communication, new()
        {
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            ["sprk_regardingrecordid"] = OrdinaryMatter.ToString("D"),
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeMatter:D})",
            ["sprk_regardingrecordname"] = "Acme v. Widget",
        });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        var body = _user.Patches.Should().ContainSingle().Subject.Body;
        PatchedColumn(body, "sprk_regardingrecordnumber").ValueKind.Should().Be(JsonValueKind.Null,
            "the matter type's catalog row names no number column here, so the number is cleared — never the old one");
        PatchedColumn(body, "sprk_regardingrecordurl").GetString()
            .Should().Be(RegardingRecordType.BuildRecordUrl("sprk_matter", OrdinaryMatter.ToString("D")));
    }

    /// <summary>Q7 (b) on a re-type over the SAME id: the pair names another record (the type decides which table the id is
    /// read in), so the display columns the update does not send are re-derived from the new type — the number from the
    /// column its catalog row names — never left describing the record the old type named.</summary>
    [Fact]
    public async Task CommunicationFiling_ReTypingThePairOverTheSameId_ReDerivesItsNumberAndDisplay()
    {
        var communication = Guid.Parse("a2470000-0000-4000-8000-000000000006");
        var projectType = Guid.Parse("a2470000-0000-4000-8000-0000000000c2");
        _world.WithRecord("sprk_communication", communication, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _user.RecordTypes[projectType] = "sprk_project";
        CatalogKnowsTheMatterType();
        _user.RowValues[RecordTypeMatter] = new() { [RegardingRecordType.RecordNumberFieldColumn] = "sprk_matternumber" };
        _user.RowValues[communication] = new()
        {
            ["sprk_regardingrecordid"] = OrdinaryMatter.ToString("D"),
            ["_sprk_regardingrecordtype_value"] = projectType.ToString("D"),
            ["sprk_regardingrecordname"] = "Old project",
            ["sprk_regardingrecordnumber"] = "OLD-0001",
        };
        _user.RowValues[OrdinaryMatter] = new()
        {
            [RegardingRecordType.GetPrimaryNameField("sprk_matter")!] = "Acme v. Widget",
            ["sprk_matternumber"] = "M-0042",
        };

        var result = await RefileCommunication(communication, new()
        {
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeMatter:D})",
        });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        var body = _user.Patches.Should().ContainSingle().Subject.Body;
        PatchedColumn(body, "sprk_regardingrecordnumber").GetString().Should().Be("M-0042", "re-derived from the new type's number column");
        PatchedColumn(body, "sprk_regardingrecordname").GetString().Should().Be("Acme v. Widget");
        PatchedColumn(body, "sprk_regardingrecordurl").GetString()
            .Should().Be(RegardingRecordType.BuildRecordUrl("sprk_matter", OrdinaryMatter.ToString("D")));
    }

    [Fact]
    public async Task UpdateRecord_ATypeOnlyUpdate_FillsTheIdFromTheRowsTypedLookup()
    {
        ToDoFiledUnderTheOrdinaryMatterByItsTypedLookupOnly();
        CatalogKnowsTheMatterType();

        var result = await UpdateRecord("sprk_todo", Todo, Lookup("sprk_regardingrecordtype", "sprk_recordtype_ref", RecordTypeMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var body = _user.Patches.Should().ContainSingle().Subject.Body;
        PatchedColumn(body, SecureRootInheritance.PairIdColumn).GetString().Should().Be(OrdinaryMatter.ToString("D"));
    }

    [Fact]
    public async Task ChildRefile_AnUpdateTouchingNoPair_IsUnchanged()
    {
        // Must not fire: a rename of a to-do filed by its typed lookup only writes the rename, nothing about a pair.
        ToDoFiledUnderTheOrdinaryMatterByItsTypedLookupOnly();

        var result = await RefileChild("sprk_todo", Todo, new() { ["sprk_name"] = "Renamed" });

        Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
        _user.Patches.Should().ContainSingle().Which.Body.Should().NotContain("sprk_regarding");
    }
}

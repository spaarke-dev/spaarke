using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 022c — the Restricted-source rule on Repair: values mapped from a source whose
/// <c>sprk_accesspermission</c> is Restricted, or cannot be read, are written only into a row that is Restricted (its STORED
/// value, <see cref="RecordCreationService.StoredRowIsRestricted"/>). Otherwise nothing is mapped, reported as
/// <see cref="RecordRepairReason.MappingSourceRestricted"/>.
/// </summary>
public sealed partial class RecordRepairModeTests
{
    private static OptionSetValue Level(int value) => new(value);

    private void TheMattersAccessPermissionIs(object? value)
    {
        var matter = _world.Directory.Row("sprk_matter", RepairWorld.Matter);
        if (value is null)
            matter.Attributes.Remove(InheritedAccessPermission.Column);
        else
            matter[InheritedAccessPermission.Column] = value;
    }

    private Guid EventUnderTheMatterWhoseAccessPermissionIs(object? value)
    {
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null);
        if (value is not null)
            _world.Directory.Row("sprk_event", id)[InheritedAccessPermission.Column] = value;
        return id;
    }

    private void NothingWasMapped(RecordRepairOutcome outcome)
    {
        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain(["sprk_clientname", "sprk_practicearea", "sprk_priorityname"]);
        outcome.Fields.Should().Contain(f => f.Part == RecordRepairPart.Mapping && f.Column == null
            && f.ReasonCode == RecordRepairReason.MappingSourceRestricted);
        outcome.Fields.Should().NotContain(f => f.Part == RecordRepairPart.Mapping && f.State == RecordRepairFieldState.Repaired);
    }

    // ───────────────────────── must fire (AC1) ─────────────────────────

    public static TheoryData<string, object?> RowsThatAreNotRestricted => new()
    {
        { "Standard", Level(InheritedAccessPermission.Standard) },
        { "Limited", Level(InheritedAccessPermission.Limited) },
        { "no value", null },
        { "an unknown option", Level(999) },
        { "a value of another type", InheritedAccessPermission.Restricted },
    };

    [Theory]
    [MemberData(nameof(RowsThatAreNotRestricted))]
    public async Task ARestrictedSource_IntoARowThatIsNotRestricted_MapsNothing_WithTheRestrictedReason(string because, object? row)
    {
        TheMattersAccessPermissionIs(Level(InheritedAccessPermission.Restricted));
        var id = EventUnderTheMatterWhoseAccessPermissionIs(row);

        var outcome = await _world.RepairAsync("sprk_event", id);

        NothingWasMapped(outcome);
        _world.Directory.Row("sprk_event", id).Attributes.Should().NotContainKey("sprk_clientname", because);
    }

    public static TheoryData<string, object> UnreadableSourceValues => new()
    {
        { "an unknown option", Level(999) },
        { "a value of another type", "Restricted" },
    };

    [Theory]
    [MemberData(nameof(UnreadableSourceValues))]
    public async Task AnUnreadableSource_IntoARowThatIsNotRestricted_MapsNothing_WithTheRestrictedReason(string because, object value)
    {
        TheMattersAccessPermissionIs(value);
        var id = EventUnderTheMatterWhoseAccessPermissionIs(Level(InheritedAccessPermission.Standard));

        var outcome = await _world.RepairAsync("sprk_event", id);

        NothingWasMapped(outcome);
        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("sprk_clientname", because);
    }

    [Fact]
    public async Task ARestrictedSource_IntoARowWhoseTableHasNoColumn_MapsNothing_WithTheRestrictedReason()
    {
        InheritedAccessPermission.CarriesColumn("sprk_reportcard").Should().BeFalse();
        TheMattersAccessPermissionIs(Level(InheritedAccessPermission.Restricted));
        var id = _world.ReportCardUnderTheMatter();

        var outcome = await _world.RepairAsync("sprk_reportcard", id);

        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("sprk_clientname");
        outcome.Fields.Should().Contain(f => f.Part == RecordRepairPart.Mapping && f.ReasonCode == RecordRepairReason.MappingSourceRestricted);
    }

    [Fact]
    public async Task TheReason_IsLoggedWithTheRowsId()
    {
        TheMattersAccessPermissionIs(Level(InheritedAccessPermission.Restricted));
        var id = EventUnderTheMatterWhoseAccessPermissionIs(Level(InheritedAccessPermission.Standard));

        await _world.RepairAsync("sprk_event", id);

        _world.Log.Entries.Should().Contain(e => e.Message.Contains(id.ToString()) && e.Message.Contains("is not Restricted"));
    }

    // ───────────────────────── must not fire (AC2) ─────────────────────────

    [Theory]
    [InlineData("Restricted")]
    [InlineData("an unknown option")]
    public async Task ARestrictedOrUnreadableSource_IntoARestrictedRow_Maps(string source)
    {
        TheMattersAccessPermissionIs(Level(source == "Restricted" ? InheritedAccessPermission.Restricted : 999));
        var id = EventUnderTheMatterWhoseAccessPermissionIs(Level(InheritedAccessPermission.Restricted));

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().ContainSingle().Which.Fields["sprk_clientname"].Should().Be(RepairWorld.MatterClient);
        outcome.Fields.Should().NotContain(f => f.ReasonCode == RecordRepairReason.MappingSourceRestricted);
    }

    public static TheoryData<string, object?> SourcesThatAreNotRestricted => new()
    {
        { "Standard", Level(InheritedAccessPermission.Standard) },
        { "Limited", Level(InheritedAccessPermission.Limited) },
        { "no value", null },
    };

    [Theory]
    [MemberData(nameof(SourcesThatAreNotRestricted))]
    public async Task ASourceThatIsNotRestricted_MapsUnchanged_AndTheRowIsNotReadAgain(string because, object? source)
    {
        TheMattersAccessPermissionIs(source);
        var id = EventUnderTheMatterWhoseAccessPermissionIs(Level(InheritedAccessPermission.Standard));

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().ContainSingle().Which.Fields["sprk_clientname"].Should().Be(RepairWorld.MatterClient, because);
        outcome.Fields.Should().NotContain(f => f.ReasonCode == RecordRepairReason.MappingSourceRestricted);
        _world.SourceReads.Should().NotContain(r => r.Table == "sprk_event", "the row's value is held by the whole-row read");
    }

    [Fact]
    public async Task TheSourceRead_AsksForTheAccessPermission_InItsOneRead()
    {
        var id = EventUnderTheMatterWhoseAccessPermissionIs(null);

        await _world.RepairAsync("sprk_event", id);

        _world.SourceReads.Should().ContainSingle(r => r.Table == "sprk_matter")
            .Which.Columns.Should().Contain(InheritedAccessPermission.Column);
    }

    [Fact]
    public async Task ASourceTableWithoutTheColumn_IsNotAskedForIt_AndMaps()
    {
        InheritedAccessPermission.SourceCarriesColumn("sprk_invoice").Should().BeFalse();
        var id = _world.ToDoUnderAnOrdinaryInvoiceHolding("INV-022C");

        var outcome = await _world.RepairAsync("sprk_todo", id);

        _world.SourceReads.Should().ContainSingle(r => r.Table == "sprk_invoice")
            .Which.Columns.Should().NotContain(InheritedAccessPermission.Column);
        _world.Writes.Should().ContainSingle().Which.Fields["sprk_description"].Should().Be("INV-022C");
        outcome.Fields.Should().NotContain(f => f.ReasonCode == RecordRepairReason.MappingSourceRestricted);
    }

    [Fact]
    public async Task AStandardSource_IntoARowWhoseTableHasNoColumn_Maps()
    {
        TheMattersAccessPermissionIs(Level(InheritedAccessPermission.Standard));
        var id = _world.ReportCardUnderTheMatter();

        await _world.RepairAsync("sprk_reportcard", id);

        _world.Writes.Should().ContainSingle().Which.Fields["sprk_clientname"].Should().Be(RepairWorld.MatterClient);
    }

    // ───────────────────────── the shared answer (one place, both writers) ─────────────────────────

    public static TheoryData<string, string, object?, bool> StoredRows => new()
    {
        { "Restricted", "sprk_event", Level(InheritedAccessPermission.Restricted), true },
        { "Restricted on a root", "sprk_matter", Level(InheritedAccessPermission.Restricted), true },
        { "Standard", "sprk_event", Level(InheritedAccessPermission.Standard), false },
        { "Limited", "sprk_event", Level(InheritedAccessPermission.Limited), false },
        { "no value", "sprk_event", null, false },
        { "an unknown option", "sprk_event", Level(999), false },
        { "a value of another type", "sprk_event", InheritedAccessPermission.Restricted, false },
        { "a table without the column", "sprk_reportcard", Level(InheritedAccessPermission.Restricted), false },
        { "a source-only table", "sprk_agreement", Level(InheritedAccessPermission.Restricted), false },
    };

    [Theory]
    [MemberData(nameof(StoredRows))]
    public void AStoredRow_IsRestricted_OnlyWhenItsTableCarriesTheColumn_AndItHoldsTheRestrictedOption(
        string because, string table, object? value, bool restricted)
    {
        var row = new Entity(table, Guid.NewGuid());
        if (value is not null)
            row[InheritedAccessPermission.Column] = value;

        RecordCreationService.StoredRowIsRestricted(table, row).Should().Be(restricted, because);
    }

    [Fact]
    public void NoRow_IsNotRestricted() =>
        RecordCreationService.StoredRowIsRestricted("sprk_event", null).Should().BeFalse();
}

internal sealed partial class RepairWorld
{
    /// <summary>Task 022c: a report card (a table without <c>sprk_accesspermission</c>) filed under the matter, owned by the
    /// child unit's team, with a matter → report card mapping of the client name.</summary>
    public Guid ReportCardUnderTheMatter()
    {
        Map("sprk_matter", "sprk_reportcard", Copy("sprk_clientname", "sprk_clientname", 1));
        return RowOf("sprk_reportcard", Directory.ChildBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("team", Directory.ChildTeam),
            ["owningteam"] = new EntityReference("team", Directory.ChildTeam),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Matter),
        });
    }

    /// <summary>Task 022c: a to-do filed under an ordinary invoice of the matter (an invoice carries no
    /// <c>sprk_accesspermission</c>), owned by the child unit's team and holding its stamp, with an invoice → to-do mapping.
    /// The invoice holds a Restricted value under the column name, which a table without the column is never asked for.</summary>
    public Guid ToDoUnderAnOrdinaryInvoiceHolding(string invoiceNumber)
    {
        var invoice = Guid.NewGuid();
        Directory.WithRecord("sprk_invoice", invoice, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new Dictionary<string, object>
            {
                ["sprk_matter"] = new EntityReference("sprk_matter", Matter),
                ["sprk_invoicenumber"] = invoiceNumber,
                [InheritedAccessPermission.Column] = new OptionSetValue(InheritedAccessPermission.Restricted),
            });
        _stamps.Row("sprk_matter", Matter)
            .Row("sprk_invoice", invoice, [("sprk_matter", "sprk_matter", Matter)]);
        Map("sprk_invoice", "sprk_todo", Copy("sprk_invoicenumber", "sprk_description", 1));
        return RowOf("sprk_todo", Directory.ChildBu, new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("team", Directory.ChildTeam),
            ["owningteam"] = new EntityReference("team", Directory.ChildTeam),
            ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", invoice),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Matter),
        });
    }
}

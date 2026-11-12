using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 022b — the Restricted-source rule, through the REAL service: values mapped from a
/// source whose Access Permission is Restricted (or whose value cannot be read) are written only into a row that is Restricted
/// once created; otherwise they are removed before the create, the caller's values are kept, and the create warns with
/// <see cref="RecordCreationWarning.MappingSourceRestrictedCode"/>. A document's Access Permission is decided in the create
/// (with its record) from its filing parents.
/// </summary>
/// <remarks>
/// The world: <see cref="RestrictedMatter"/> holds Restricted, in the resolver's world (what a child's parents give it) and in
/// the app-only source read (what the source holds). The matter and the four inheriting tables carry the column there.
/// </remarks>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    private static readonly Guid RestrictedMatter = Guid.Parse("a022b000-0000-4000-8000-000000000001");

    /// <summary>The Restricted matter in every world, and the column on the matter and the inheriting tables.</summary>
    private void TheRestrictedMatter(object? sourceValue = null)
    {
        _world.WithOrdinaryRoot("sprk_matter", RestrictedMatter);
        _stamps.Row("sprk_matter", RestrictedMatter)
            .Set("sprk_matter", RestrictedMatter, InheritedAccessPermission.Column, new OptionSetValue(InheritedAccessPermission.Restricted))
            .WithColumn("sprk_matter", InheritedAccessPermission.Column)
            .WithColumn("sprk_event", InheritedAccessPermission.Column)
            .WithColumn("sprk_todo", InheritedAccessPermission.Column)
            .WithColumn("sprk_communication", InheritedAccessPermission.Column)
            .WithColumn("sprk_document", InheritedAccessPermission.Column)
            .WithColumn("sprk_document", AccessInheritance.Column);
        _shareWorld.OrdinaryRoot("sprk_matter", RestrictedMatter);
        _readable.Add(RestrictedMatter);
        _sources[("sprk_matter", RestrictedMatter)] = new Entity("sprk_matter", RestrictedMatter)
        {
            ["sprk_matterdescription"] = "From the restricted matter",
            ["sprk_location"] = "Courtroom 9",
            [InheritedAccessPermission.Column] = sourceValue ?? new OptionSetValue(InheritedAccessPermission.Restricted),
        };
    }

    private static int? ServerInt(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value as int? : null;

    // ── Must not fire: the row ends Restricted, or the source is not Restricted ─────────────────────────────────────────

    [Fact]
    public async Task RestrictedSource_AnEventFiledUnderTheRestrictedMatter_Maps_AndIsCreatedRestricted()
    {
        TheRestrictedMatter();
        MatterToEventProfile();

        var outcome = await Service().CreateAsync(EventUnder(RestrictedMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_description").Should().Be("From the restricted matter");
        ServerInt(fields, InheritedAccessPermission.Column).Should().Be(InheritedAccessPermission.Restricted);
        outcome.MappingWarnings.Should().NotContain(w => w.Code == RecordCreationWarning.MappingSourceRestrictedCode);
    }

    [Fact]
    public async Task RestrictedSource_AToDoFiledUnderTheRestrictedMatter_Maps()
    {
        TheRestrictedMatter();
        _user.Columns["sprk_todo"] = new()
        {
            ["sprk_name"] = ("String", true),
            ["sprk_description"] = ("Memo", true),
            ["sprk_regardingmatter"] = ("Lookup", true),
        };
        Profile("sprk_matter", "sprk_todo", Rule("sprk_matterdescription", "sprk_description", Memo));

        var outcome = await Service().CreateAsync(
            Request("sprk_todo", new Dictionary<string, object?>
            {
                ["sprk_name"] = "Call the client",
                ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({RestrictedMatter:D})",
            }), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_description").Should().Be("From the restricted matter");
        ServerInt(fields, InheritedAccessPermission.Column).Should().Be(InheritedAccessPermission.Restricted);
        outcome.MappingWarnings.Should().NotContain(w => w.Code == RecordCreationWarning.MappingSourceRestrictedCode);
    }

    [Fact]
    public async Task RestrictedSource_ACommunicationFiledUnderTheRestrictedMatter_Maps()
    {
        TheRestrictedMatter();
        _user.Columns["sprk_communication"] = new()
        {
            ["sprk_subject"] = ("String", true),
            ["sprk_body"] = ("Memo", true),
            ["sprk_regardingmatter"] = ("Lookup", true),
        };
        Profile("sprk_matter", "sprk_communication", Rule("sprk_matterdescription", "sprk_body", Memo));

        var outcome = await Service().CreateAsync(
            Request("sprk_communication", new Dictionary<string, object?>
            {
                ["sprk_subject"] = "Status",
                ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({RestrictedMatter:D})",
            }), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_body").Should().Be("From the restricted matter");
        ServerInt(fields, InheritedAccessPermission.Column).Should().Be(InheritedAccessPermission.Restricted);
        outcome.MappingWarnings.Should().NotContain(w => w.Code == RecordCreationWarning.MappingSourceRestrictedCode);
    }

    /// <summary>AC2 + AC3: a document filed under the Restricted matter maps, and its create request carries Restricted with
    /// its own-narrowing record — decided by the document decision, in the same create.</summary>
    [Fact]
    public async Task RestrictedSource_ADocumentFiledUnderTheRestrictedMatter_Maps_AndIsCreatedRestrictedWithItsRecord()
    {
        TheRestrictedMatter();
        Profile("sprk_matter", "sprk_document", Rule("sprk_matterdescription", "sprk_documentdescription", Memo));

        var outcome = await Service().CreateAsync(
            Request("sprk_document", new Dictionary<string, object?>
            {
                ["sprk_documentname"] = "Brief.docx",
                ["sprk_Matter@odata.bind"] = $"/sprk_matters({RestrictedMatter:D})",
            }), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_documentdescription").Should().Be("From the restricted matter");
        ServerInt(fields, InheritedAccessPermission.Column).Should().Be(InheritedAccessPermission.Restricted,
            "AC3: the document's Access Permission is set in the create from its filing parent");
        var record = fields.Should().ContainKey(AccessInheritance.Column).WhoseValue.Should().BeOfType<string>().Subject;
        var (parsed, unreadable) = AccessInheritance.Parse(record);
        unreadable.Should().BeFalse();
        parsed!.FloorPermission.Should().Be(InheritedAccessPermission.Restricted, "the record holds what the parent gave");
        parsed.OwnPermission.Should().BeNull("the caller sent no narrowing of its own");
        outcome.MappingWarnings.Should().NotContain(w => w.Code == RecordCreationWarning.MappingSourceRestrictedCode);
    }

    [Fact]
    public async Task RestrictedSource_ADocumentTheCallerNarrowsToRestricted_UnderAnOrdinaryMatter_Maps_FromARestrictedSource()
    {
        TheRestrictedMatter();
        _stamps.Set("sprk_matter", OrdinaryMatter, InheritedAccessPermission.Column, new OptionSetValue(InheritedAccessPermission.Standard));
        Profile("sprk_matter", "sprk_document", Rule("sprk_matterdescription", "sprk_documentdescription", Memo));

        var outcome = await Service().CreateAsync(
            Request("sprk_document", new Dictionary<string, object?>
            {
                ["sprk_documentname"] = "Brief.docx",
                ["sprk_Matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
                [InheritedAccessPermission.Column] = InheritedAccessPermission.Restricted,
            }, mappingSource: ("sprk_matter", RestrictedMatter)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_documentdescription").Should().Be("From the restricted matter",
            "the document is Restricted once created: max(its own narrowing, what its parent gives)");
        var (parsed, _) = AccessInheritance.Parse(fields[AccessInheritance.Column] as string);
        parsed!.OwnPermission.Should().Be(InheritedAccessPermission.Restricted, "the caller's narrowing is recorded as its own");
        outcome.MappingWarnings.Should().NotContain(w => w.Code == RecordCreationWarning.MappingSourceRestrictedCode);
    }

    [Theory]
    [InlineData(InheritedAccessPermission.Standard)]
    [InlineData(InheritedAccessPermission.Limited)]
    public async Task RestrictedSource_AStandardOrLimitedSource_MapsUnchanged_IntoARowWithNoRestrictedAncestor(int level)
    {
        TheRestrictedMatter(new OptionSetValue(level));
        MatterToEventProfile();

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", RestrictedMatter)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_description").Should().Be("From the restricted matter");
        outcome.MappingWarnings.Should().BeEmpty();
    }

    [Fact]
    public async Task RestrictedSource_ASourceWithNoValue_CountsAsStandard_AndMapsIntoARowWithNoRestrictedAncestor()
    {
        TheRestrictedMatter();
        _sources[("sprk_matter", RestrictedMatter)].Attributes.Remove(InheritedAccessPermission.Column);
        MatterToEventProfile();

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", RestrictedMatter)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_description").Should().Be("From the restricted matter");
        outcome.MappingWarnings.Should().BeEmpty();
    }

    [Fact]
    public async Task RestrictedSource_TheSourceReadAsksForTheColumn_InTheSameAppOnlyRead()
    {
        TheRestrictedMatter();
        MatterToEventProfile();

        await Service().CreateAsync(EventUnder(RestrictedMatter), AsCaller(), CancellationToken.None);

        _sourceReads.Where(r => r.Table == "sprk_matter").Should().ContainSingle("one app-only read of the source")
            .Which.Columns.Should().Contain(InheritedAccessPermission.Column).And.Contain("sprk_matterdescription");
    }

    // ── Must fire ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>AC1: an explicit Restricted source that is not the filing parent, into a row that is not Restricted.</summary>
    [Fact]
    public async Task RestrictedSource_AnExplicitRestrictedSource_NotTheFilingParent_IntoANonRestrictedEvent_IsStripped()
    {
        TheRestrictedMatter();
        MatterToEventProfile();

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", RestrictedMatter)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue("the create still proceeds; only the mapped values are removed");
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_description").And.NotContainKey("sprk_location");
        StringOf(fields, "sprk_eventname").Should().Be("Hearing", "the caller's own values are created");
        StringOf(fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({OrdinaryMatter:D})", "nor the filing");
        var warning = outcome.MappingWarnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(RecordCreationWarning.MappingSourceRestrictedCode);
        warning.Detail.Should().Be(RecordCreationService.MappingSourceRestrictedStripped).And.NotContain(RestrictedMatter.ToString("D"));
    }

    /// <summary>AC1: the Project wizard's no-parent create from a Restricted source (a caller's project is never created
    /// Restricted: the column is the server's on a root).</summary>
    [Fact]
    public async Task RestrictedSource_TheProjectWizardsNoParentCreate_FromARestrictedMatter_MapsNothing()
    {
        TheRestrictedMatter();
        MatterToProjectProfile();

        var result = await RouteCreate("sprk_project", new() { ["sprk_projectname"] = "Phase 1" }, "sprk_matter", RestrictedMatter.ToString("D"));

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        _creates.Should().ContainSingle().Which.Fields.Should().NotContainKey("sprk_description");
        WarningsOf(result).Should().Contain(RecordCreationService.MappingSourceRestrictedStripped);
    }

    /// <summary>A project's Access Permission is the server's: a caller's create never writes it, so a project is not
    /// Restricted in its create, even filed under the Restricted matter (its parents raise it after the create). The rule
    /// reads the value written in the create, so the mapped values are removed.</summary>
    [Fact]
    public async Task RestrictedSource_AProjectFiledUnderTheRestrictedMatter_IsNotRestrictedInItsCreate_SoMapsNothing()
    {
        TheRestrictedMatter();
        MatterToProjectProfile();
        _scripted.RecordTypes[RecordTypeCatalogSetup.MatterTypeId] = "sprk_matter";
        _stamps.RecordType(RecordTypeCatalogSetup.MatterTypeId, "sprk_matter");
        _sourceReader.AnswersRecordTypeCatalog();

        var result = await RouteCreate("sprk_project", AProjectFiledUnder(RestrictedMatter));

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        _creates.Should().ContainSingle().Which.Fields.Should().NotContainKey("sprk_description")
            .And.NotContainKey(InheritedAccessPermission.Column);
        WarningsOf(result).Should().Contain(RecordCreationService.MappingSourceRestrictedStripped);
    }

    /// <summary>AC1: a target table without the column is never Restricted — even filed under the Restricted source.</summary>
    [Fact]
    public async Task RestrictedSource_IntoATableWithoutTheColumn_IsStripped_EvenFiledUnderTheRestrictedSource()
    {
        TheRestrictedMatter();
        InheritedAccessPermission.CarriesColumn("sprk_invoice").Should().BeFalse("the case needs a table without the column");
        Profile("sprk_matter", "sprk_invoice", Rule("sprk_matterdescription", "sprk_description", Memo));

        var outcome = await Service().CreateAsync(
            Request("sprk_invoice", new Dictionary<string, object?>
            {
                ["sprk_invoicenumber"] = "INV-1",
                ["sprk_Matter@odata.bind"] = $"/sprk_matters({RestrictedMatter:D})",
            }), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_description");
        StringOf(fields, "sprk_Matter@odata.bind").Should().Be($"/sprk_matters({RestrictedMatter:D})");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w =>
                w.Code == RecordCreationWarning.MappingSourceRestrictedCode && w.Detail == RecordCreationService.MappingSourceRestrictedStripped);
    }

    [Fact]
    public async Task RestrictedSource_AStrippedValueThatFilesTheRow_RefusesTheCreate()
    {
        TheRestrictedMatter();
        _sources[("sprk_project", SourceProject)]["sprk_regardingmatter"] = new EntityReference("sprk_matter", OrdinaryMatter);
        _sources[("sprk_project", SourceProject)][InheritedAccessPermission.Column] = new OptionSetValue(InheritedAccessPermission.Restricted);
        ProjectToEventProfile();

        var outcome = await Service().CreateAsync(
            Event(new { sprk_eventname = "Hearing" }, mappingSource: ("sprk_project", SourceProject)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeFalse("a removed value would have filed the row");
        outcome.Refusal!.Code.Should().Be(RecordCreationRefusal.MappingSourceSecureFilingCode);
        outcome.Refusal.Detail.Should().Contain("Restricted").And.NotContain(SourceProject.ToString("D"));
        Problem(outcome, "sprk_event").Status.Should().Be(StatusCodes.Status400BadRequest);
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task RestrictedSource_AfterAStrip_ThePreWriteRulesRunAgain_OnTheStrippedRow()
    {
        TheRestrictedMatter();
        MatterToEventProfile();
        var asked = WithTestRule();

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", RestrictedMatter)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        asked.Should().HaveCountGreaterThan(1, "the pre-write rules are asked again after the strip");
        asked[^1].Item.JsonBody.Should().NotContain("sprk_description", "the last pass sees the stripped row");
    }

    // ── AC4: fail closed ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RestrictedSource_WhenTheParentsCannotBeRead_TheRowIsNotKnownRestricted_AndTheValuesAreStripped()
    {
        TheRestrictedMatter();
        MatterToEventProfile();
        _stamps.FailScan("sprk_matter");

        var outcome = await Service().CreateAsync(EventUnder(RestrictedMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_description").And.NotContainKey("sprk_location");
        StringOf(fields, "sprk_eventname").Should().Be("Hearing");
        var warning = outcome.MappingWarnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(RecordCreationWarning.MappingSourceRestrictedCode);
        warning.Detail.Should().Be(RecordCreationService.MappingSourceRestrictedUndetermined).And.NotContain("Restricted");
    }

    [Fact]
    public async Task RestrictedSource_WhenTheParentsReadFaults_ForADocument_TheValuesAreStripped_AndNothingIsWritten()
    {
        TheRestrictedMatter();
        Profile("sprk_matter", "sprk_document", Rule("sprk_matterdescription", "sprk_documentdescription", Memo));
        _stamps.FailScan("sprk_matter");

        var outcome = await Service().CreateAsync(
            Request("sprk_document", new Dictionary<string, object?>
            {
                ["sprk_documentname"] = "Brief.docx",
                ["sprk_Matter@odata.bind"] = $"/sprk_matters({RestrictedMatter:D})",
            }), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_documentdescription");
        fields.Should().NotContainKey(InheritedAccessPermission.Column, "a value that could not be decided is not written")
            .And.NotContainKey(AccessInheritance.Column);
        outcome.MappingWarnings.Should().ContainSingle().Which.Detail.Should().Be(RecordCreationService.MappingSourceRestrictedUndetermined);
    }

    [Fact]
    public async Task RestrictedSource_ASourceValueThatIsNoKnownLevel_IsStripped_AsUndetermined()
    {
        TheRestrictedMatter(new OptionSetValue(5));
        MatterToEventProfile();

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", RestrictedMatter)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _creates.Should().ContainSingle().Which.Fields.Should().NotContainKey("sprk_description");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w =>
                w.Code == RecordCreationWarning.MappingSourceRestrictedCode
                && w.Detail == RecordCreationService.MappingSourceRestrictedUndetermined);
    }

    [Fact]
    public async Task RestrictedSource_AnUnreadableSourceValue_StillMapsIntoARowThatEndsRestricted()
    {
        TheRestrictedMatter(new OptionSetValue(5));
        MatterToEventProfile();

        var outcome = await Service().CreateAsync(EventUnder(RestrictedMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_description").Should().Be("From the restricted matter",
            "whatever the source holds, the row is Restricted once created");
        outcome.MappingWarnings.Should().BeEmpty();
    }

    [Fact]
    public async Task RestrictedSource_NoRestrictedAncestor_AndAnOrdinarySource_MapsNormally()
    {
        TheRestrictedMatter();
        MatterToEventProfile();

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", OrdinaryMatter)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_description").Should().Be("From the matter");
        outcome.MappingWarnings.Should().BeEmpty();
    }

    /// <summary>The step-10 value is decided once on the item the check saw: an event written Restricted keeps the value the
    /// check decided (no second resolution after it).</summary>
    [Fact]
    public async Task RestrictedSource_TheStepTenValue_IsTheOneTheCheckDecided()
    {
        TheRestrictedMatter();
        MatterToEventProfile();

        var outcome = await Service().CreateAsync(EventUnder(RestrictedMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.Steps.Single(s => s.Step == RecordCreationStep.StampAndInheritance).Note.Should().Contain("I-16 set");
        ServerInt(_creates.Single().Fields, InheritedAccessPermission.Column).Should().Be(InheritedAccessPermission.Restricted);
    }
}

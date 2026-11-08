using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 045a (G1; owner decisions 2026-11-07 Q2(a), Q3(a)): a create whose regarding pair
/// names a record type but no record id is completed or cleared BEFORE the caller checks (step 4½), so step 5 sees the pair
/// as it will be written. The id is filled from the row's single agreeing lookup of that type the caller sent (or the server
/// set); none, or more than one, clears the type (and any display columns) with one warning. A mapped lookup never fills it,
/// nor does an I-1 stamp column while an intermediate is bound. A catalog read that fails leaves the row as sent, so step 5
/// answers exactly as before.
/// </summary>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    private static readonly Guid ProjectType = Guid.Parse("a045a000-0000-4000-8000-0000000000e2");
    private static readonly Guid EventType = Guid.Parse("a045a000-0000-4000-8000-0000000000e3");
    private static readonly Guid DocumentType = Guid.Parse("a045a000-0000-4000-8000-0000000000e4");
    private static readonly Guid TableLessType = Guid.Parse("a045a000-0000-4000-8000-0000000000e5");
    private static readonly Guid TypeOnlyParentEvent = Guid.Parse("a045a000-0000-4000-8000-000000000011");
    private static readonly Guid AnchorDocument = Guid.Parse("a045a000-0000-4000-8000-000000000021");
    private static readonly Guid FilingDocument = Guid.Parse("a045a000-0000-4000-8000-000000000022");

    private const string TypeBind = "sprk_RegardingRecordType@odata.bind";

    private void TheRecordTypes()
    {
        _scripted.RecordTypes[MatterType] = "sprk_matter";
        _scripted.RecordTypes[ProjectType] = "sprk_project";
        _scripted.RecordTypes[EventType] = "sprk_event";
        _scripted.RecordTypes[DocumentType] = "sprk_document";
    }

    private static KeyValuePair<string, object?> PairType(Guid type) => new(TypeBind, $"/sprk_recordtype_refs({type:D})");

    private static KeyValuePair<string, object?> FiledUnder(string navigation, string set, Guid id) =>
        new($"{navigation}@odata.bind", $"/{set}({id:D})");

    private RecordCreationRequest TypeOnlyEvent(params KeyValuePair<string, object?>[] values) =>
        TypeOnly("sprk_event", "sprk_eventname", values);

    private RecordCreationRequest TypeOnly(string table, string nameColumn, KeyValuePair<string, object?>[] values, bool mapping = false,
        (string Table, Guid Id)? mappingSource = null)
    {
        var body = new Dictionary<string, object?> { [nameColumn] = "Hearing" };
        foreach (var (key, value) in values)
            body[key] = value;
        return Request(table, body, mapping, mappingSource);
    }

    private int CatalogReads() => _user.Paths.Count(p => p.StartsWith("sprk_recordtype_refs(", StringComparison.Ordinal));

    private List<string> AppendToAsked() =>
        [.. _user.Paths.Where(p => p.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)
            || p.Contains("RetrieveUserSetOfPrivilegesByNames", StringComparison.Ordinal))];

    private static void ThePairWasNotWritten(IReadOnlyDictionary<string, object?> fields) =>
        fields.Keys.Should().NotContain(k => k.StartsWith("sprk_regardingrecord", StringComparison.OrdinalIgnoreCase),
            "a cleared pair writes neither half, nor its display columns");

    private void TheOneClearWarning(RecordCreationOutcome outcome, params Guid[] neverNamed)
    {
        var warning = outcome.MappingWarnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(RecordCreationWarning.RegardingTypeClearedCode);
        warning.Column.Should().Be(SecureRootInheritance.PairTypeColumn);
        foreach (var id in neverNamed)
            warning.Detail.Should().NotContain(id.ToString("D"), "the warning names no record");
        outcome.Warnings.Should().Equal(warning.Detail);
    }

    // =====================================================================================
    // Filled: exactly one agreeing lookup of the type
    // =====================================================================================

    [Fact(DisplayName = "045a: a type-only pair with one agreeing lookup is filled with that record before the caller checks")]
    public async Task CreateAsync_ATypeOnlyPairWithOneAgreeingLookup_IsFilledWithThatRecord_AndCheckedAsAWholePair()
    {
        TheRecordTypes();

        var outcome = await Service().CreateAsync(
            TypeOnlyEvent(PairType(MatterType), FiledUnder("sprk_RegardingMatter", "sprk_matters", OrdinaryMatter)),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, SecureRootInheritance.PairIdColumn).Should().Be(OrdinaryMatter.ToString("D"), "G1: the id is filled");
        StringOf(fields, TypeBind).Should().Be($"/sprk_recordtype_refs({MatterType:D})", "the type the caller sent stands");
        outcome.MappingWarnings.Should().BeEmpty("Q3(a): a fill carries no warning");
        outcome.Warnings.Should().BeEmpty();

        // Step 5 asks exactly what it asks of the same pair sent whole: AppendTo on the pair's record is not skipped.
        var filledAsked = AppendToAsked();
        var filledCatalogReads = CatalogReads();
        _user.Paths.Clear();
        var whole = await Service().CreateAsync(
            TypeOnlyEvent(PairType(MatterType), FiledUnder("sprk_RegardingMatter", "sprk_matters", OrdinaryMatter),
                new(SecureRootInheritance.PairIdColumn, OrdinaryMatter.ToString("D"))),
            AsCaller(), CancellationToken.None);
        whole.Succeeded.Should().BeTrue(whole.Refusal?.Detail);
        filledAsked.Should().Equal(AppendToAsked(), "the filled pair is checked as the whole pair is");
        filledCatalogReads.Should().Be(CatalogReads() + 1, "step 4½ reads the type's table once, as the caller");
    }

    [Fact(DisplayName = "045a: a type-only pair after a dropped mapped value keeps the filled id when step 5 rebuilds the row")]
    public async Task CreateAsync_AFilledPair_WhenAMappedValueIsDropped_StillCarriesTheFilledId()
    {
        TheRecordTypes();
        MatterToEventProfile();
        _scripted.NoAppendToIn.Add(("contacts", Assignee)); // the mapped sprk_assignedto is dropped: the row is rebuilt from the caller's

        var outcome = await Service().CreateAsync(
            TypeOnly("sprk_event", "sprk_eventname",
                [PairType(MatterType), FiledUnder("sprk_RegardingMatter", "sprk_matters", OrdinaryMatter)], mapping: true),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, SecureRootInheritance.PairIdColumn).Should().Be(OrdinaryMatter.ToString("D"));
        fields.Should().NotContainKey("sprk_AssignedTo@odata.bind");
        outcome.MappingWarnings.Should().ContainSingle().Which.Code.Should().Be(RecordCreationWarning.MappedValueDroppedCode);
    }

    [Fact(DisplayName = "045a: an event filed under an event, typed event, is filled with the parent event")]
    public async Task CreateAsync_ATypeOnlyEventPairUnderAParentEvent_IsFilledWithTheParentEvent()
    {
        TheRecordTypes();
        WithTheTypeOnlyParentEvent();

        var outcome = await Service().CreateAsync(
            TypeOnlyEvent(PairType(EventType), FiledUnder("sprk_RegardingEvent", "sprk_events", TypeOnlyParentEvent)),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, SecureRootInheritance.PairIdColumn).Should().Be(TypeOnlyParentEvent.ToString("D"));
        outcome.MappingWarnings.Should().BeEmpty();
    }

    // =====================================================================================
    // Cleared: no candidate, more than one, a mapped one, a stamp beside an intermediate, a type naming no table
    // =====================================================================================

    public static TheoryData<string> BlankIds => new() { "absent", "null", "empty", "whitespace" };

    [Theory(DisplayName = "045a: a type-only pair with no agreeing lookup is cleared, display columns too, with one warning")]
    [MemberData(nameof(BlankIds))]
    public async Task CreateAsync_ATypeOnlyPairWithNoAgreeingLookup_IsCleared_WithItsDisplayColumns_AndOneWarning(string id)
    {
        TheRecordTypes();
        var values = new List<KeyValuePair<string, object?>>
        {
            PairType(ProjectType), // the row names no project
            FiledUnder("sprk_RegardingMatter", "sprk_matters", OrdinaryMatter),
            new("sprk_regardingrecordname", "Phase 2"),
            new("sprk_regardingrecordurl", "https://example.invalid/phase-2"),
        };
        if (id != "absent")
            values.Add(new(SecureRootInheritance.PairIdColumn, id switch { "null" => null, "empty" => "", _ => "   " }));

        var outcome = await Service().CreateAsync(TypeOnlyEvent([.. values]), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        ThePairWasNotWritten(fields);
        StringOf(fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({OrdinaryMatter:D})", "the filing stands");
        TheOneClearWarning(outcome, ProjectType, OrdinaryMatter);
    }

    [Fact(DisplayName = "045a: two agreeing lookups naming different records clear the type")]
    public async Task CreateAsync_ATypeOnlyPairWithTwoDifferentAgreeingLookups_IsCleared()
    {
        TheRecordTypes();
        foreach (var document in new[] { AnchorDocument, FilingDocument })
        {
            _world.WithRecord("sprk_document", document, Directory.ChildBu, owningTeam: Directory.ChildTeam);
            _stamps.Row("sprk_document", document);
        }

        var outcome = await Service().CreateAsync(
            TypeOnly("sprk_analysis", "sprk_name",
            [
                PairType(DocumentType),
                FiledUnder("sprk_DocumentId", "sprk_documents", AnchorDocument),
                FiledUnder("sprk_RegardingDocument", "sprk_documents", FilingDocument),
            ]),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        ThePairWasNotWritten(_creates.Should().ContainSingle().Subject.Fields);
        TheOneClearWarning(outcome, AnchorDocument, FilingDocument);
    }

    [Fact(DisplayName = "045a: the same record in two agreeing lookups is one candidate, and fills")]
    public async Task CreateAsync_ATypeOnlyPairWithTheSameRecordInTwoAgreeingLookups_IsFilled()
    {
        TheRecordTypes();
        _world.WithRecord("sprk_document", AnchorDocument, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _stamps.Row("sprk_document", AnchorDocument);

        var outcome = await Service().CreateAsync(
            TypeOnly("sprk_analysis", "sprk_name",
            [
                PairType(DocumentType),
                FiledUnder("sprk_DocumentId", "sprk_documents", AnchorDocument),
                FiledUnder("sprk_RegardingDocument", "sprk_documents", AnchorDocument),
            ]),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, SecureRootInheritance.PairIdColumn)
            .Should().Be(AnchorDocument.ToString("D"));
        outcome.MappingWarnings.Should().BeEmpty();
    }

    [Fact(DisplayName = "045a: a mapped lookup never fills the pair")]
    public async Task CreateAsync_ATypeOnlyPairWhoseOnlyCandidateIsAMappedLookup_IsCleared()
    {
        TheRecordTypes();
        ProjectToEventProfile(); // maps the project's sprk_regardingmatter (SecureMatter) onto the event

        var outcome = await Service().CreateAsync(
            TypeOnly("sprk_event", "sprk_eventname", [PairType(MatterType)], mapping: true, mappingSource: ("sprk_project", SourceProject)),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        ThePairWasNotWritten(fields);
        StringOf(fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({SecureMatter:D})", "the mapped value is written");
        TheOneClearWarning(outcome, SecureMatter);
    }

    [Fact(DisplayName = "045a: a sent I-1 stamp beside a bound intermediate never fills the pair")]
    public async Task CreateAsync_ATypeOnlyPairWhoseOnlyCandidateIsAStampBesideAnIntermediate_IsCleared()
    {
        TheRecordTypes();
        WithTheTypeOnlyParentEvent();

        var outcome = await Service().CreateAsync(
            TypeOnlyEvent(
                PairType(MatterType),
                FiledUnder("sprk_RegardingEvent", "sprk_events", TypeOnlyParentEvent),
                FiledUnder("sprk_RegardingMatter", "sprk_matters", OrdinaryMatter)),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        ThePairWasNotWritten(fields);
        StringOf(fields, "sprk_RegardingEvent@odata.bind").Should().Be($"/sprk_events({TypeOnlyParentEvent:D})");
        TheOneClearWarning(outcome, OrdinaryMatter, TypeOnlyParentEvent);
    }

    [Fact(DisplayName = "045a: a type whose catalog row names no table is cleared")]
    public async Task CreateAsync_ATypeOnlyPairWhoseTypeNamesNoTable_IsCleared()
    {
        TheRecordTypes(); // TableLessType is not among them: its row has no sprk_recordlogicalname

        var outcome = await Service().CreateAsync(
            TypeOnlyEvent(PairType(TableLessType), FiledUnder("sprk_RegardingMatter", "sprk_matters", OrdinaryMatter)),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        ThePairWasNotWritten(_creates.Should().ContainSingle().Subject.Fields);
        TheOneClearWarning(outcome, TableLessType);
    }

    // =====================================================================================
    // Unchanged: a catalog read that fails; a whole, empty or id-only pair
    // =====================================================================================

    public static TheoryData<string> CatalogFailures => new() { "forbidden", "not-found" };

    [Theory(DisplayName = "045a: a type the caller cannot read is refused as before, and nothing is written")]
    [MemberData(nameof(CatalogFailures))]
    public async Task CreateAsync_ATypeOnlyPairWhoseTypeTheCallerCannotRead_IsTheUniform404_AsBefore(string failure)
    {
        TheRecordTypes();
        (failure == "forbidden" ? _scripted.UnreadableRows : _scripted.InvisibleRows).Add(MatterType);

        var outcome = await Service().CreateAsync(
            TypeOnlyEvent(PairType(MatterType), FiledUnder("sprk_RegardingMatter", "sprk_matters", OrdinaryMatter)),
            AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        var problem = Problem(outcome, "sprk_event");
        problem.Status.Should().Be(StatusCodes.Status404NotFound);
        JsonSerializer.Serialize(problem).Should().NotContain(MatterType.ToString("D"));
        outcome.MappingWarnings.Should().BeEmpty();
        _creates.Should().BeEmpty();
    }

    [Fact(DisplayName = "045a: a catalog read that fails upstream leaves the row as sent; step 5 answers as before")]
    public async Task CreateAsync_ATypeOnlyPairWhoseCatalogReadFailsUpstream_IsLeftAsSent_ForStep5()
    {
        TheRecordTypes();
        _user.FailOn = path => path.StartsWith("sprk_recordtype_refs(", StringComparison.Ordinal)
            ? DataverseUserResponse.Fail(503, "ServiceUnavailable", "0x80072321 The service is busy.")
            : null;

        var outcome = await Service().CreateAsync(
            TypeOnlyEvent(PairType(MatterType), FiledUnder("sprk_RegardingMatter", "sprk_matters", OrdinaryMatter)),
            AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied,
            "step 5's AppendTo on the type's row cannot be answered either: the uniform 404, as before");
        Problem(outcome, "sprk_event").Status.Should().Be(StatusCodes.Status404NotFound);
        outcome.MappingWarnings.Should().BeEmpty("an unanswered read never clears the caller's type");
        _creates.Should().BeEmpty();
    }

    [Fact(DisplayName = "045a: a whole pair, no pair and an id-only pair are not touched (must not fire)")]
    public async Task CreateAsync_AWholeEmptyOrIdOnlyPair_IsUntouched_WithNoExtraReadAndNoWarning()
    {
        TheRecordTypes();

        var whole = await Service().CreateAsync(
            TypeOnlyEvent(PairType(MatterType), FiledUnder("sprk_RegardingMatter", "sprk_matters", OrdinaryMatter),
                new(SecureRootInheritance.PairIdColumn, OrdinaryMatter.ToString("D"))),
            AsCaller(), CancellationToken.None);
        whole.Succeeded.Should().BeTrue(whole.Refusal?.Detail);
        whole.MappingWarnings.Should().BeEmpty();
        CatalogReads().Should().Be(2, "step 5's reads of the pair's type (the pair check and the type row's AppendTo), none more");

        _user.Paths.Clear();
        var none = await Service().CreateAsync(
            TypeOnlyEvent(FiledUnder("sprk_RegardingMatter", "sprk_matters", OrdinaryMatter)), AsCaller(), CancellationToken.None);
        none.Succeeded.Should().BeTrue(none.Refusal?.Detail);
        none.MappingWarnings.Should().BeEmpty();
        CatalogReads().Should().Be(0);

        _user.Paths.Clear();
        var idOnly = await Service().CreateAsync(
            TypeOnlyEvent(FiledUnder("sprk_RegardingMatter", "sprk_matters", OrdinaryMatter),
                new(SecureRootInheritance.PairIdColumn, OrdinaryMatter.ToString("D"))),
            AsCaller(), CancellationToken.None);
        idOnly.Succeeded.Should().BeTrue(idOnly.Refusal?.Detail);
        idOnly.MappingWarnings.Should().BeEmpty();
        CatalogReads().Should().Be(0, "an id with no type is not step 4½'s");

        _creates.Should().HaveCount(3);
        StringOf(_creates[0].Fields, SecureRootInheritance.PairIdColumn).Should().Be(OrdinaryMatter.ToString("D"));
        _creates[1].Fields.Keys.Should().NotContain(k => k.StartsWith("sprk_regardingrecord", StringComparison.OrdinalIgnoreCase));
        StringOf(_creates[2].Fields, SecureRootInheritance.PairIdColumn).Should().Be(OrdinaryMatter.ToString("D"));
        _creates[2].Fields.Should().NotContainKey(TypeBind, "nothing is added to an id-only pair");
    }

    /// <summary>An event the caller files under (an intermediate), itself filed under the ordinary matter.</summary>
    private void WithTheTypeOnlyParentEvent()
    {
        _world.WithRecord("sprk_event", TypeOnlyParentEvent, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _stamps.Row("sprk_event", TypeOnlyParentEvent, [("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)]);
        _shareWorld.OrdinaryChild("sprk_event", TypeOnlyParentEvent, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));
    }
}

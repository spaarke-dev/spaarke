using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
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

    /// <summary>
    /// A type row the caller cannot read at all (every read of it 403) or that does not exist (404): the uniform 404, nothing
    /// written. Fix round 2: the two now reach it by different steps — a 403 is treated as a type naming no table, so step 4½
    /// asks AppendTo before clearing and that ask's existence read refuses; a 404 is left as sent and step 5 refuses — and
    /// still answer byte for byte alike (the F4 theory compares them).
    /// </summary>
    [Theory(DisplayName = "045a: a type the caller cannot read at all, or that does not exist, is the uniform 404, and nothing is written")]
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

    // =====================================================================================
    // Fix round 1
    // =====================================================================================

    [Fact(DisplayName = "045a fix round 1 (F2): a catalog read that fails upstream ONCE (step 4½'s) is refused, never stored type-only")]
    public async Task CreateAsync_ATypeOnlyPairWhoseFirstCatalogReadFailsUpstream_IsRefused_AndNothingIsWritten()
    {
        TheRecordTypes();
        var failed = 0;
        _user.FailOn = path => path.StartsWith("sprk_recordtype_refs(", StringComparison.Ordinal) && failed++ == 0
            ? DataverseUserResponse.Fail(503, "ServiceUnavailable", "0x80072321 The service is busy.")
            : null;

        var outcome = await Service().CreateAsync(
            TypeOnlyEvent(PairType(MatterType), FiledUnder("sprk_RegardingMatter", "sprk_matters", OrdinaryMatter)),
            AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Upstream, "the caller's own read did not answer");
        Problem(outcome, "sprk_event").Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        outcome.MappingWarnings.Should().BeEmpty();
        _creates.Should().BeEmpty("a type-only pair is never written past a read that did not answer");
    }

    /// <summary>Fix round 2: only the read of WHICH table the type names is refused (403); the row itself can be appended to.</summary>
    private void TheTypesTableIsUnreadable() =>
        _user.FailOn = path => path.StartsWith("sprk_recordtype_refs(", StringComparison.Ordinal)
                               && path.Contains("$select=sprk_recordlogicalname", StringComparison.Ordinal)
            ? DataverseUserResponse.Fail(403, "AccessDenied", "Principal user is missing prvRead privilege.")
            : null;

    [Fact(DisplayName = "045a fix round 2: a 403 on step 4½'s catalog read, with AppendTo on the type row allowed, clears the type with the warning")]
    public async Task CreateAsync_ATypeOnlyPairWhoseTableReadIsForbidden_ButAppendToIsAllowed_IsCleared_WithTheWarning()
    {
        TheRecordTypes();
        TheTypesTableIsUnreadable();

        var outcome = await Service().CreateAsync(
            TypeOnlyEvent(PairType(MatterType), FiledUnder("sprk_RegardingMatter", "sprk_matters", OrdinaryMatter)),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        ThePairWasNotWritten(_creates.Should().ContainSingle().Subject.Fields);
        TheOneClearWarning(outcome, MatterType, OrdinaryMatter);
    }

    [Fact(DisplayName = "045a fix round 2: a 403 on step 4½'s catalog read, with AppendTo on the type row denied, is the uniform 404")]
    public async Task CreateAsync_ATypeOnlyPairWhoseTableReadIsForbidden_AndAppendToIsDenied_IsTheUniform404()
    {
        TheRecordTypes();
        TheTypesTableIsUnreadable();
        _scripted.Held.Remove("prvAppendTosprk_recordtype_ref");

        var outcome = await Service().CreateAsync(
            TypeOnlyEvent(PairType(MatterType), FiledUnder("sprk_RegardingMatter", "sprk_matters", OrdinaryMatter)),
            AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        Problem(outcome, "sprk_event").Status.Should().Be(StatusCodes.Status404NotFound);
        outcome.MappingWarnings.Should().BeEmpty();
        _creates.Should().BeEmpty();
    }

    public static TheoryData<string> ClearedTypes => new() { "no-candidate", "names-no-table" };

    [Theory(DisplayName = "045a fix round 1 (F4): a type cleared by step 4½ still costs AppendTo on its catalog row — a caller without it gets step 5's uniform 404, not a create")]
    [MemberData(nameof(ClearedTypes))]
    public async Task CreateAsync_ATypeOnlyPairThatWouldBeCleared_WithoutAppendToOnTheType_IsTheUniform404_AsBefore(string shape)
    {
        TheRecordTypes();
        var type = shape == "no-candidate" ? ProjectType : TableLessType;
        _scripted.Held.Remove("prvAppendTosprk_recordtype_ref");

        var denied = await Service().CreateAsync(
            TypeOnlyEvent(PairType(type), FiledUnder("sprk_RegardingMatter", "sprk_matters", OrdinaryMatter)),
            AsCaller(), CancellationToken.None);
        _scripted.Held.Add("prvAppendTosprk_recordtype_ref");
        _scripted.InvisibleRows.Add(MatterType); // a type row that does not exist: step 5 answers it today
        var missing = await Service().CreateAsync(
            TypeOnlyEvent(PairType(MatterType), FiledUnder("sprk_RegardingMatter", "sprk_matters", OrdinaryMatter)),
            AsCaller(), CancellationToken.None);

        denied.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        Problem(denied, "sprk_event").Status.Should().Be(StatusCodes.Status404NotFound);
        JsonSerializer.Serialize(Problem(denied, "sprk_event")).Should().Be(JsonSerializer.Serialize(Problem(missing, "sprk_event")),
            "a type row the caller may not append to and one that does not exist answer alike");
        denied.MappingWarnings.Should().BeEmpty();
        _creates.Should().BeEmpty();
    }

    [Fact(DisplayName = "045a fix round 1 (K2): the system principal's type-only pair is cleared without a read, with the one warning; its whole pair is untouched")]
    public async Task CreateAsync_ASystemPrincipalsTypeOnlyPair_IsCleared_WithoutARead()
    {
        async Task<RecordCreationRequest> SystemEvent(bool withId)
        {
            var body = new Dictionary<string, object?>
            {
                ["sprk_eventname"] = "Hearing",
                ["sprk_regardingmatter"] = new { relatedTable = "sprk_matter", recordId = OrdinaryMatter },
                ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = MatterType },
                ["sprk_regardingrecordname"] = "Acme v Widget",
            };
            if (withId)
                body[SecureRootInheritance.PairIdColumn] = OrdinaryMatter.ToString("D");
            var mapped = await DataverseWriteItemMapper.MapAsync(
                _scripted, "sprk_event", JsonSerializer.SerializeToElement(body), CancellationToken.None);
            mapped.Item.Should().NotBeNull(mapped.ValidationError);
            return new RecordCreationRequest("sprk_event", new RecordCreationPayload.Mapped(mapped.Item!)) { CorrelationId = FixedCorrelation };
        }

        var system = RecordCreationPrincipal.ForSystem(RecordRequester.Of(Caller, CallerOid));
        var typeOnly = await Service().CreateAsync(await SystemEvent(withId: false), system, CancellationToken.None);
        var whole = await Service().CreateAsync(await SystemEvent(withId: true), system, CancellationToken.None);

        typeOnly.Succeeded.Should().BeTrue(typeOnly.Refusal?.Detail);
        whole.Succeeded.Should().BeTrue(whole.Refusal?.Detail);
        _creates.Should().HaveCount(2);
        ThePairWasNotWritten(_creates[0].Fields);
        StringOf(_creates[0].Fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({OrdinaryMatter:D})");
        TheOneClearWarning(typeOnly, MatterType, OrdinaryMatter);
        StringOf(_creates[1].Fields, SecureRootInheritance.PairIdColumn).Should().Be(OrdinaryMatter.ToString("D"));
        _creates[1].Fields.Should().ContainKey(TypeBind);
        whole.MappingWarnings.Should().BeEmpty();
        _user.Paths.Should().NotContain(p => p.StartsWith("sprk_recordtype_refs(", StringComparison.Ordinal), "no caller to read as");
    }

    [Fact(DisplayName = "045a (PR #2659 review): a matter's standalone regarding type and number are kept as sent, as the caller and as the system principal")]
    public async Task CreateAsync_AMatterWithAStandaloneRegardingType_KeepsTheTypeAndNumber_AsCallerAndSystem()
    {
        // A matter holds sprk_regardingrecordtype and sprk_regardingrecordnumber but no sprk_regardingrecordid: it has no pair to
        // keep whole (OwnedChildWrite.RegardingPairTables), so step 4½ never fills or clears it.
        TheRecordTypes();
        var callerMatter = Request("sprk_matter", new Dictionary<string, object?>
        {
            ["sprk_mattername"] = "Smith v Jones",
            [TypeBind] = $"/sprk_recordtype_refs({ProjectType:D})",
            ["sprk_regardingrecordnumber"] = "PRJ-0042",
        }, mapping: false);
        var systemBody = new Dictionary<string, object?>
        {
            ["sprk_mattername"] = "Smith v Jones",
            ["sprk_regardingrecordtype"] = new { relatedTable = "sprk_recordtype_ref", recordId = ProjectType },
            ["sprk_regardingrecordnumber"] = "PRJ-0042",
        };
        var mapped = await DataverseWriteItemMapper.MapAsync(
            _scripted, "sprk_matter", JsonSerializer.SerializeToElement(systemBody), CancellationToken.None);
        mapped.Item.Should().NotBeNull(mapped.ValidationError);
        var systemMatter = new RecordCreationRequest("sprk_matter", new RecordCreationPayload.Mapped(mapped.Item!))
        {
            CorrelationId = FixedCorrelation,
        };

        var asCaller = await Service().CreateAsync(callerMatter, AsCaller(), CancellationToken.None);
        var asSystem = await Service().CreateAsync(
            systemMatter, RecordCreationPrincipal.ForSystem(RecordRequester.Of(Caller, CallerOid)), CancellationToken.None);

        foreach (var outcome in new[] { asCaller, asSystem })
        {
            outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
            outcome.MappingWarnings.Should().NotContain(w => w.Code == RecordCreationWarning.RegardingTypeClearedCode);
        }

        _creates.Should().HaveCount(2);
        foreach (var create in _creates)
        {
            StringOf(create.Fields, TypeBind).Should().Be($"/sprk_recordtype_refs({ProjectType:D})", "the matter's type stands");
            StringOf(create.Fields, "sprk_regardingrecordnumber").Should().Be("PRJ-0042", "the matter's number stands");
            create.Fields.Should().NotContainKey(SecureRootInheritance.PairIdColumn, "a matter has no regarding id to fill");
        }
    }

    /// <summary>An event the caller files under (an intermediate), itself filed under the ordinary matter.</summary>
    private void WithTheTypeOnlyParentEvent()
    {
        _world.WithRecord("sprk_event", TypeOnlyParentEvent, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        _stamps.Row("sprk_event", TypeOnlyParentEvent, [("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)]);
        _shareWorld.OrdinaryChild("sprk_event", TypeOnlyParentEvent, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));
    }
}

using FluentAssertions;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;
using UnifiedCreateRequest = Sprk.Bff.Api.Services.RecordCreation.RecordCreationRequest;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 015 (spec FR-10, write-path invariant I-5) and task 039 (FR-38, #2399) — step 8 of
/// the unified create: on a table the rules registry marks search-indexed (matter, project, document, work assignment, event,
/// invoice), step 8 is the ONLY writer of the OWNING business unit's <c>sprk_searchindexname</c> and
/// <c>sprk_ai_search_index</c>: whatever a client sent for either column is dropped (never refused, never asked AppendTo,
/// logged as <c>client_search_index_dropped</c> when it differs), and the final owning team's unit supplies its two values or
/// nothing; a table not marked search-indexed is untouched; a unit that cannot be read fills nothing and says so, and the
/// create stands.
/// </summary>
/// <remarks>
/// The caller sits in the GENERAL business unit and the ordinary matter in the CHILD one, each unit with its own defaults,
/// so a row filed under the matter (owned by the child unit's team) proves the default is the OWNER's unit, not the
/// caller's. Same world as <see cref="CrossSurfaceOwnerTests"/> (<see cref="CreateWorld"/>).
/// </remarks>
[Trait("status", "new")]
public sealed class SearchIndexDefaultTests
{
    private const string GeneralIndexName = "spaarke-general-index";
    private const string ChildIndexName = "spaarke-child-index";
    private static readonly Guid GeneralIndex = Guid.Parse("a0150000-0000-4000-8000-0000000000a1");
    private static readonly Guid ChildIndex = Guid.Parse("a0150000-0000-4000-8000-0000000000a2");
    private static readonly Guid ExplicitIndex = Guid.Parse("a0150000-0000-4000-8000-0000000000a3");
    private const string NameColumn = "sprk_searchindexname";
    private const string LookupBind = "sprk_AI_Search_Index@odata.bind";

    private readonly CreateWorld _world = new();

    public SearchIndexDefaultTests()
    {
        _world.SearchIndexDefaults[Directory.GeneralBu] = (GeneralIndexName, GeneralIndex);
        _world.SearchIndexDefaults[Directory.ChildBu] = (ChildIndexName, ChildIndex);
    }

    public static TheoryData<string> SearchIndexedTables => new() { "sprk_matter", "sprk_project", "sprk_document" };

    /// <summary>The six search-indexed tables (task 039: work assignment, event and invoice joined matter, project, document).</summary>
    public static TheoryData<string> AllSearchIndexedTables => new()
    {
        "sprk_matter", "sprk_project", "sprk_document", "sprk_workassignment", "sprk_event", "sprk_invoice",
    };

    private static readonly Guid UserBusinessUnitIndex = GeneralIndex;

    /// <summary>
    /// A minimal create of <paramref name="table"/> carrying the client's own search index (both columns), as a server-built
    /// mapped item. The matter and the project file under nothing (the caller's general unit owns them); the other four are
    /// filed under the ordinary matter, so the child unit's team owns them.
    /// </summary>
    private static (UnifiedCreateRequest Request, Guid ExpectedBusinessUnit) WithClientIndex(
        string table, Guid clientIndex, string? clientName, string bindKey = LookupBind)
    {
        var body = new Dictionary<string, object?>();
        var lookups = new List<DataverseWriteItemMapper.MappedLookup>();
        var filed = true;
        switch (table)
        {
            case "sprk_matter":
                body["sprk_mattername"] = "Acme v. Widget";
                filed = false;
                break;
            case "sprk_project":
                body["sprk_projectname"] = "Phase 2";
                filed = false;
                break;
            case "sprk_document":
                body["sprk_documentname"] = "Lease.docx";
                body["sprk_Matter@odata.bind"] = $"/sprk_matters({CreateWorld.OrdinaryMatter:D})";
                lookups.Add(new("sprk_matter", "sprk_matter", "sprk_matters", CreateWorld.OrdinaryMatter));
                break;
            case "sprk_workassignment":
                body["sprk_name"] = "Review the lease";
                body["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({CreateWorld.OrdinaryMatter:D})";
                lookups.Add(new("sprk_regardingmatter", "sprk_matter", "sprk_matters", CreateWorld.OrdinaryMatter));
                break;
            case "sprk_event":
                body["sprk_eventname"] = "Hearing";
                body["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({CreateWorld.OrdinaryMatter:D})";
                lookups.Add(new("sprk_regardingmatter", "sprk_matter", "sprk_matters", CreateWorld.OrdinaryMatter));
                break;
            default:
                body["sprk_name"] = "INV-1";
                body["sprk_Matter@odata.bind"] = $"/sprk_matters({CreateWorld.OrdinaryMatter:D})";
                lookups.Add(new("sprk_matter", "sprk_matter", "sprk_matters", CreateWorld.OrdinaryMatter));
                break;
        }

        if (clientName is not null)
            body[NameColumn] = clientName;
        if (clientIndex != Guid.Empty)
        {
            body[bindKey] = $"/sprk_aisearchindexes({clientIndex:D})";
            lookups.Add(new("sprk_ai_search_index", "sprk_aisearchindex", "sprk_aisearchindexes", clientIndex));
        }

        return (CreateWorld.Mapped(table, body, [.. lookups]), filed ? Directory.ChildBu : Directory.GeneralBu);
    }

    private static (string Name, Guid Index) DefaultsOf(Guid businessUnit) =>
        businessUnit == Directory.ChildBu ? (ChildIndexName, ChildIndex) : (GeneralIndexName, GeneralIndex);

    // =====================================================================================
    // Unset → the owning business unit's default
    // =====================================================================================

    [Fact]
    public async Task AMatterFiledUnderNothing_TakesItsOwningBusinessUnitsDefaults()
    {
        var fields = await CreatedFieldsAsync(await _world.MappedAsync("sprk_matter", new { sprk_mattername = "Acme v. Widget" }));

        CreateWorld.Owner(fields).Should().Be(Directory.GeneralTeam);
        Text(fields, NameColumn).Should().Be(GeneralIndexName);
        Text(fields, LookupBind).Should().Be($"/sprk_aisearchindexes({GeneralIndex:D})");
    }

    [Fact]
    public async Task AProjectFiledUnderAMatter_TakesTheMattersBusinessUnitsDefaults_NotTheCallers()
    {
        var fields = await CreatedFieldsAsync(await _world.ChatProjectAsync(CreateWorld.OrdinaryMatter));

        CreateWorld.Owner(fields).Should().Be(Directory.ChildTeam);
        Text(fields, NameColumn).Should().Be(ChildIndexName, "the OWNING unit's (the matter's), never the caller's general unit");
        Text(fields, LookupBind).Should().Be($"/sprk_aisearchindexes({ChildIndex:D})");
    }

    [Fact]
    public async Task ADocumentFiledUnderAMatter_TakesTheMattersBusinessUnitsDefaults_NotTheCallers()
    {
        var fields = await CreatedFieldsAsync(await _world.MappedAsync("sprk_document",
            new { sprk_documentname = "Lease.docx", sprk_matter = CreateWorld.Lookup("sprk_matter", CreateWorld.OrdinaryMatter) }));

        CreateWorld.Owner(fields).Should().Be(Directory.ChildTeam);
        Text(fields, NameColumn).Should().Be(ChildIndexName);
        Text(fields, LookupBind).Should().Be($"/sprk_aisearchindexes({ChildIndex:D})");
    }

    // A project created INTO isolation takes the Secure BU's defaults: RecordCreationServiceSecureRootSeamTests (it needs
    // the provisioning fixture's plan, which this world does not carry).

    // =====================================================================================
    // Task 039 (FR-38, #2399): whatever the client sent for either column is dropped; step 8 writes the owning unit's
    // =====================================================================================

    [Theory]
    [MemberData(nameof(AllSearchIndexedTables))]
    public async Task AClientsSearchIndex_IsDropped_AndTheOwningUnitsIsWritten_OnEverySearchIndexedTable(string table)
    {
        var (request, unit) = WithClientIndex(table, ExplicitIndex, "explicit-index");

        var fields = await CreatedFieldsAsync(request);

        var (name, index) = DefaultsOf(unit);
        Text(fields, NameColumn).Should().Be(name, "the client's name is dropped; the owning unit's is written");
        Text(fields, LookupBind).Should().Be($"/sprk_aisearchindexes({index:D})", "the client's index is dropped; the owning unit's is written");
        fields.Keys.Where(k => k.StartsWith("sprk_ai_search_index", StringComparison.OrdinalIgnoreCase)).Should().ContainSingle(
            "the body carries the server's bind only, never the client's spelling of it").Which.Should().Be(LookupBind);
    }

    /// <summary>The drop, proved where the overwrite cannot hide it: the owning unit has no index, so nothing replaces the client's.</summary>
    [Theory]
    [MemberData(nameof(AllSearchIndexedTables))]
    public async Task AClientsSearchIndex_IsNeverPassedOn_WhenTheOwningUnitHasNone(string table)
    {
        _world.SearchIndexDefaults.Clear();
        var (request, _) = WithClientIndex(table, ExplicitIndex, "explicit-index");

        var fields = await CreatedFieldsAsync(request);

        fields.Should().NotContainKey(NameColumn);
        fields.Keys.Should().NotContain(k => k.StartsWith("sprk_ai_search_index", StringComparison.OrdinalIgnoreCase));
        _world.Log.Messages.Should().Contain(m => m.Contains("search_index_unset_for_owner_bu"));
    }

    [Theory]
    [MemberData(nameof(AllSearchIndexedTables))]
    public async Task AClientsSearchIndex_InTheLowerCaseSpelling_IsDroppedToo_AndTheBodyHasOneBindKey(string table)
    {
        var (request, unit) = WithClientIndex(table, ExplicitIndex, clientName: null, bindKey: "sprk_ai_search_index@odata.bind");

        var fields = await CreatedFieldsAsync(request);

        fields.Keys.Where(k => string.Equals(k, LookupBind, StringComparison.OrdinalIgnoreCase)).Should().ContainSingle()
            .Which.Should().Be(LookupBind);
        Text(fields, LookupBind).Should().Be($"/sprk_aisearchindexes({DefaultsOf(unit).Index:D})");
    }

    [Theory]
    [MemberData(nameof(SearchIndexedTables))]
    public async Task AnExplicitName_IsDropped_AndTheOwningUnitsNameAndIndexAreWritten(string table)
    {
        var fields = await CreatedFieldsAsync(CreateWorld.Mapped(table,
            new Dictionary<string, object?> { [NameColumn] = "explicit-index" }));

        Text(fields, NameColumn).Should().Be(GeneralIndexName, "the name column is the server's too (the legacy text column)");
        Text(fields, LookupBind).Should().Be($"/sprk_aisearchindexes({GeneralIndex:D})");
    }

    [Fact]
    public async Task TheClientsNameAlone_IsReplacedByTheOwningUnitsName_OrByNothing_WhenTheUnitHasNone()
    {
        var (request, _) = WithClientIndex("sprk_workassignment", Guid.Empty, "client-name");
        var named = await CreatedFieldsAsync(request);
        Text(named, NameColumn).Should().Be(ChildIndexName);

        _world.Creates.Clear();
        _world.SearchIndexDefaults[Directory.ChildBu] = (null, ChildIndex);
        var (again, _) = WithClientIndex("sprk_workassignment", Guid.Empty, "client-name");
        var unnamed = await CreatedFieldsAsync(again);
        unnamed.Should().NotContainKey(NameColumn, "the unit has no name, so none is sent (never the client's)");
        Text(unnamed, LookupBind).Should().Be($"/sprk_aisearchindexes({ChildIndex:D})");
    }

    /// <summary>
    /// The #2399 repro: a work assignment created by a caller of the GENERAL unit under a matter owned by the CHILD unit, the
    /// client binding the user's own unit's index, a catalog row the caller cannot AppendTo. It used to be refused (the caller
    /// has no AppendTo on the row); the bind is dropped before the caller's checks, so no AppendTo is asked on the catalog and the
    /// row takes the final owning unit's index.
    /// </summary>
    [Fact]
    public async Task Issue2399_AWorkAssignmentBindingTheUsersUnitIndexTheCallerCannotAppendTo_IsNotRefused_AndAsksNoAppendToOnTheCatalog()
    {
        _world.Caller.NoAppendToIn.Add(("sprk_aisearchindexes", UserBusinessUnitIndex));
        var asked = new List<string>();
        _world.Caller.GetOverride = path =>
        {
            if (path.Contains("sprk_aisearchindexes(", StringComparison.Ordinal))
                asked.Add(path);
            return null;
        };
        var (request, _) = WithClientIndex("sprk_workassignment", UserBusinessUnitIndex, GeneralIndexName);

        var outcome = await _world.CreateAsync(request);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.Warnings.Should().BeEmpty();
        asked.Should().BeEmpty("the bind is dropped before step 5, so AppendTo is never asked on a catalog row (#2399)");
        var fields = _world.Creates.Should().ContainSingle().Subject.Fields;
        CreateWorld.Owner(fields).Should().Be(Directory.ChildTeam);
        Text(fields, LookupBind).Should().Be($"/sprk_aisearchindexes({ChildIndex:D})", "the final owning unit's index, not the user's");
        Text(fields, NameColumn).Should().Be(ChildIndexName);
    }

    [Fact]
    public async Task AClientsSearchIndex_IsNotRefused_EvenWhenTheCatalogRowDoesNotExistForTheCaller()
    {
        _world.Caller.Missing.Add(ExplicitIndex);
        var (request, _) = WithClientIndex("sprk_event", ExplicitIndex, "explicit-index");

        var outcome = await _world.CreateAsync(request);

        outcome.Succeeded.Should().BeTrue("a dropped value is never a refusal (step 2's blocked-column refusal does not fire)");
        outcome.MappingWarnings.Should().BeEmpty();
    }

    // =====================================================================================
    // Step 8 uses the FINAL owning team's unit: the Secure team's, and nothing when that unit has no index
    // =====================================================================================

    [Fact]
    public async Task AnEventUnderASecureMatter_TakesTheSecureTeamsBusinessUnitsIndex()
    {
        _world.SearchIndexDefaults[Directory.SecureBu] = ("spaarke-secure-index", Guid.Parse("a0150000-0000-4000-8000-0000000000a9"));

        var fields = await CreatedFieldsAsync(EventUnder(CreateWorld.SecureMatter, ExplicitIndex, "client-name"));

        CreateWorld.Owner(fields).Should().Be(Directory.SecureNamedTeam, "isolated: the named Secure Record Owners team");
        Text(fields, NameColumn).Should().Be("spaarke-secure-index", "the secure team's unit, never the caller's or the root's");
        Text(fields, LookupBind).Should().Be("/sprk_aisearchindexes(a0150000-0000-4000-8000-0000000000a9)");
    }

    [Fact]
    public async Task AnEventUnderASecureMatter_WhoseUnitHasNoIndex_GetsNone_NotTheCallersNorTheRoots_AndLogsIt()
    {
        // The caller's (general) and the matter's (child) units HAVE an index; the secure unit has none: nothing falls back.
        var fields = await CreatedFieldsAsync(EventUnder(CreateWorld.SecureMatter, ExplicitIndex, "client-name"));

        CreateWorld.Owner(fields).Should().Be(Directory.SecureNamedTeam);
        fields.Should().NotContainKey(NameColumn).And.NotContainKey(LookupBind);
        var line = _world.Log.Entries.Should().ContainSingle(e => e.Message.Contains("search_index_unset_for_owner_bu")).Subject;
        line.Message.Should().Contain(Directory.SecureNamedTeam.ToString()).And.Contain(Directory.SecureBu.ToString());
        line.Message.Should().Contain("sprk_event");
        line.Message.Should().NotContain("client-name").And.NotContain("@");
    }

    private static UnifiedCreateRequest EventUnder(Guid matter, Guid clientIndex, string clientName) =>
        CreateWorld.Mapped("sprk_event",
            new Dictionary<string, object?>
            {
                ["sprk_eventname"] = "Hearing",
                ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({matter:D})",
                [NameColumn] = clientName,
                [LookupBind] = $"/sprk_aisearchindexes({clientIndex:D})",
            },
            new DataverseWriteItemMapper.MappedLookup("sprk_regardingmatter", "sprk_matter", "sprk_matters", matter),
            new DataverseWriteItemMapper.MappedLookup("sprk_ai_search_index", "sprk_aisearchindex", "sprk_aisearchindexes", clientIndex));

    // =====================================================================================
    // client_search_index_dropped: logged when the dropped value differs from the applied one; absent when equal; ids only
    // =====================================================================================

    [Fact]
    public async Task ADroppedIndexThatDiffers_IsLogged_WithIds_Origin_AndTheTable_AndNoNamesOrEmail()
    {
        var (request, _) = WithClientIndex("sprk_workassignment", UserBusinessUnitIndex, GeneralIndexName);

        var outcome = await _world.CreateAsync(request);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var lines = _world.Log.Entries.Where(e => e.Message.Contains("client_search_index_dropped")).ToList();
        lines.Should().HaveCount(2, "one for the lookup, one for the name column");
        lines.Should().OnlyContain(l => l.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        var lookup = lines.Single(l => l.Message.Contains("column=sprk_ai_search_index"));
        lookup.Message.Should().Contain("table=sprk_workassignment").And.Contain($"record={outcome.CreatedId}")
            .And.Contain("origin=caller")
            .And.Contain($"droppedIndexId={UserBusinessUnitIndex}").And.Contain($"appliedIndexId={ChildIndex}");
        var name = lines.Single(l => l.Message.Contains("column=sprk_searchindexname"));
        name.Message.Should().Contain("droppedIndexName=" + GeneralIndexName).And.Contain("appliedIndexName=" + ChildIndexName);
        lines.Should().OnlyContain(l => !l.Message.Contains("Review the lease") && !l.Message.Contains('@'),
            "ids and catalog names only: no record name, no person, no email");
    }

    [Fact]
    public async Task ADroppedIndexThatEqualsTheApplied_IsNotLogged_NameComparedTrimmedAndIgnoringCase()
    {
        var (request, _) = WithClientIndex("sprk_workassignment", ChildIndex, "  " + ChildIndexName.ToUpperInvariant() + " ");

        var outcome = await _world.CreateAsync(request);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _world.Log.Messages.Should().NotContain(m => m.Contains("client_search_index_dropped"),
            "the same id, and the same name trimmed and ordinal-ignore-case, is nothing to report");
    }

    [Fact]
    public async Task ANullBindAndABlankName_AgainstAUnitWithNoIndex_AreBothNone_AndNotLogged()
    {
        _world.SearchIndexDefaults.Clear();
        var request = CreateWorld.Mapped("sprk_event",
            new Dictionary<string, object?>
            {
                ["sprk_eventname"] = "Hearing",
                ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({CreateWorld.OrdinaryMatter:D})",
                ["sprk_ai_search_index@odata.bind"] = null,
                [NameColumn] = "   ",
            },
            new DataverseWriteItemMapper.MappedLookup("sprk_regardingmatter", "sprk_matter", "sprk_matters", CreateWorld.OrdinaryMatter));

        var outcome = await _world.CreateAsync(request);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _world.Log.Messages.Should().NotContain(m => m.Contains("client_search_index_dropped"), "both none is equal");
        _world.Creates.Should().ContainSingle().Subject.Fields.Should().NotContainKey(NameColumn).And.NotContainKey(LookupBind);
    }

    [Theory]
    [MemberData(nameof(SearchIndexedTables))]
    public async Task ABlankName_IsUnset_AndTakesTheOwningBusinessUnitsDefault(string table)
    {
        var fields = await CreatedFieldsAsync(CreateWorld.Mapped(table, new Dictionary<string, object?> { [NameColumn] = "  " }));

        Text(fields, NameColumn).Should().Be(GeneralIndexName);
    }

    // =====================================================================================
    // An explicit null bind → unset, filled under ONE key (UAC-r2's review of 015, K3; task 018a)
    // =====================================================================================

    [Theory]
    [InlineData("sprk_ai_search_index@odata.bind", "a lower-case spelling of the bind key")]
    [InlineData(LookupBind, "the canonical spelling of the bind key")]
    public async Task AnExplicitNullBind_IsUnset_AndIsFilledUnderTheCanonicalKeyOnly(string nullKey, string because)
    {
        var fields = await CreatedFieldsAsync(CreateWorld.Mapped("sprk_matter",
            new Dictionary<string, object?> { ["sprk_mattername"] = "Acme v. Widget", [nullKey] = null }));

        fields.Keys.Where(k => string.Equals(k, LookupBind, StringComparison.OrdinalIgnoreCase))
            .Should().ContainSingle($"{because}: the body never carries two keys that differ only in case")
            .Which.Should().Be(LookupBind, "the default is written under the canonical key");
        Text(fields, LookupBind).Should().Be($"/sprk_aisearchindexes({GeneralIndex:D})", "an explicit null is unset (fill-only)");
    }

    // =====================================================================================
    // Not search-indexed → untouched; unreadable → nothing filled, said so, created
    // =====================================================================================

    /// <summary>
    /// Must-not-fire (task 039): a table the registry does not mark search-indexed (a To Do, which carries a lookup) keeps the
    /// caller's AppendTo check and writes its lookup; step 8 is a no-op and reads no business unit.
    /// </summary>
    [Fact]
    public async Task ATableNotMarkedSearchIndexed_ReceivesNoSearchIndexValue_KeepsTheCallersAppendToCheck_AndWritesItsLookup()
    {
        var todo = await _world.MappedAsync("sprk_todo",
            new { sprk_name = "Call the client", sprk_regardingmatter = CreateWorld.Lookup("sprk_matter", CreateWorld.OrdinaryMatter) });

        var outcome = await _world.CreateAsync(todo);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _world.Creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey(NameColumn).And.NotContainKey(LookupBind);
        fields.Should().ContainKey("sprk_RegardingMatter@odata.bind", "the lookup is written");
        _world.BusinessUnitReads.Should().BeEmpty();
        outcome.Steps.Single(s => s.Step == RecordCreationStep.SearchIndexDefault).State.Should().Be(RecordCreationStepState.NoOp);

        // The caller's AppendTo check is unchanged: a matter the caller cannot append to is the same refusal as before.
        _world.Caller.NoAppendTo.Add(CreateWorld.OrdinaryMatter);
        _world.Creates.Clear();
        var denied = await _world.CreateAsync(await _world.MappedAsync("sprk_todo",
            new { sprk_name = "Call the client", sprk_regardingmatter = CreateWorld.Lookup("sprk_matter", CreateWorld.OrdinaryMatter) }));
        denied.Succeeded.Should().BeFalse("AppendTo on the filing lookup is still the caller's check on a table that is not search-indexed");
        _world.Creates.Should().BeEmpty();
    }

    [Fact]
    public async Task ATableNotMarkedSearchIndexed_PassesAClientsIndexNamedColumnThroughUntouched()
    {
        var request = CreateWorld.Mapped("sprk_todo",
            new Dictionary<string, object?>
            {
                ["sprk_name"] = "Call the client",
                [NameColumn] = "kept-because-the-table-is-not-indexed",
            });

        var outcome = await _world.CreateAsync(request);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        Text(_world.Creates.Should().ContainSingle().Subject.Fields, NameColumn).Should().Be("kept-because-the-table-is-not-indexed",
            "the drop is scoped by the registry: only a search-indexed table's index is the server's");
        _world.Log.Messages.Should().NotContain(m => m.Contains("client_search_index_dropped"));
    }

    [Fact]
    public async Task WhenTheOwningBusinessUnitCannotBeRead_NothingIsFilled_TheCallerIsTold_AndTheRecordIsCreated()
    {
        _world.BusinessUnitReadFault = new TimeoutException("business unit read timed out");

        var outcome = await _world.CreateAsync(await _world.MappedAsync("sprk_matter", new { sprk_mattername = "Acme v. Widget" }));

        outcome.Succeeded.Should().BeTrue("a routing hint never fails a create");
        var fields = _world.Creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey(NameColumn).And.NotContainKey(LookupBind);
        outcome.Warnings.Should().ContainSingle().Which.Should().Be(
            "Business-unit search defaults could not be read, so they were not applied. Document indexing for this matter " +
            "falls back to its business unit's settings.");
        outcome.Warnings.Single().Should().NotContain("timed out", "the fault's text stays in the log");
    }

    /// <summary>A created column's text: a value the server added, or one passed through from the payload's JSON body.</summary>
    private static string? Text(IReadOnlyDictionary<string, object?> fields, string column) =>
        fields[column] is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } json ? json.GetString() : fields[column] as string;

    private async Task<IReadOnlyDictionary<string, object?>> CreatedFieldsAsync(RecordCreationRequest request)
    {
        var outcome = await _world.CreateAsync(request);
        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.Warnings.Should().BeEmpty();
        return _world.Creates.Should().ContainSingle().Subject.Fields;
    }
}

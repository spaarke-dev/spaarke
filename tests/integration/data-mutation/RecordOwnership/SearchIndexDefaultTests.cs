using FluentAssertions;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;
using UnifiedCreateRequest = Sprk.Bff.Api.Services.RecordCreation.RecordCreationRequest;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 tasks 015 and 039 (FR-38, #2399), as amended by task 041 (FR-40, #2502) — the search
/// index on the unified create: on a table the rules registry marks search-indexed (matter, project, document, work assignment,
/// event, invoice), whatever a client sent for <c>sprk_searchindexname</c> or <c>sprk_ai_search_index</c> is dropped (never
/// refused, never asked AppendTo, logged as <c>client_search_index_dropped</c> when it differs from what was applied — since
/// 041 nothing is applied), and step 8 is a recorded no-op that writes neither column and reads no business unit (the index
/// follows the content type); a table not marked search-indexed is untouched.
/// </summary>
/// <remarks>
/// The caller sits in the GENERAL business unit and the ordinary matter in the CHILD one, and BOTH units hold an index, so a
/// create that writes no column proves no unit's index is copied, the owner's or the caller's. Same world as
/// <see cref="CrossSurfaceOwnerTests"/> (<see cref="CreateWorld"/>).
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

    // =====================================================================================
    // Task 041 (FR-40, #2502): the index follows the content type — step 8 is a recorded no-op and writes neither column
    // =====================================================================================

    /// <summary>
    /// Design §14.7 must-fire 5: a create on each of the six search-indexed tables, owned by a business unit that HAS an index
    /// (the general unit for the matter and the project, the child unit for the four filed under the ordinary matter), sends
    /// neither column; step 8 is traced <see cref="RecordCreationStepState.NoOp"/>; no business unit is read for the index.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllSearchIndexedTables))]
    public async Task ACreateWhoseOwningUnitHasAnIndex_WritesNeitherColumn_TracesStep8NoOp_AndReadsNoBusinessUnit(string table)
    {
        var (request, unit) = WithClientIndex(table, Guid.Empty, clientName: null);

        var outcome = await _world.CreateAsync(request);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.Warnings.Should().BeEmpty();
        var fields = _world.Creates.Should().ContainSingle().Subject.Fields;
        _world.SearchIndexDefaults.Should().ContainKey(unit, "the owning unit has an index, and it is not copied");
        AssertNoIndexColumn(fields);
        outcome.Steps.Single(s => s.Step == RecordCreationStep.SearchIndexDefault).State.Should().Be(RecordCreationStepState.NoOp);
        _world.BusinessUnitReads.Should().BeEmpty("the index is not read from any business unit");
    }

    // A project created INTO isolation: RecordCreationServiceSecureRootSeamTests (it needs the provisioning fixture's plan,
    // which this world does not carry).

    /// <summary>Neither index column is in the create body, under any spelling, and none is sent as <c>null</c> (AC8).</summary>
    private static void AssertNoIndexColumn(IReadOnlyDictionary<string, object?> fields)
    {
        fields.Keys.Should().NotContain(k => string.Equals(k, NameColumn, StringComparison.OrdinalIgnoreCase));
        fields.Keys.Should().NotContain(k => k.StartsWith("sprk_ai_search_index", StringComparison.OrdinalIgnoreCase));
    }

    // =====================================================================================
    // Task 039 (FR-38, #2399): whatever the client sent for either column is dropped; since task 041 nothing replaces it
    // =====================================================================================

    [Theory]
    [MemberData(nameof(AllSearchIndexedTables))]
    public async Task AClientsSearchIndex_IsDropped_AndNothingIsWritten_OnEverySearchIndexedTable(string table)
    {
        var (request, _) = WithClientIndex(table, ExplicitIndex, "explicit-index");

        var fields = await CreatedFieldsAsync(request);

        AssertNoIndexColumn(fields);
    }

    /// <summary>
    /// The drop where the owning unit has no index: nothing replaces the client's, and task 041 withdrew
    /// <c>search_index_unset_for_owner_bu</c> (step 8 reads no unit, so it has nothing to report).
    /// </summary>
    [Theory]
    [MemberData(nameof(AllSearchIndexedTables))]
    public async Task AClientsSearchIndex_IsNeverPassedOn_WhenTheOwningUnitHasNone(string table)
    {
        _world.SearchIndexDefaults.Clear();
        var (request, _) = WithClientIndex(table, ExplicitIndex, "explicit-index");

        var fields = await CreatedFieldsAsync(request);

        AssertNoIndexColumn(fields);
        _world.Log.Messages.Should().NotContain(m => m.Contains("search_index_unset_for_owner_bu"));
    }

    [Theory]
    [MemberData(nameof(AllSearchIndexedTables))]
    public async Task AClientsSearchIndex_InTheLowerCaseSpelling_IsDroppedToo_AndNoBindKeyIsSent(string table)
    {
        var (request, _) = WithClientIndex(table, ExplicitIndex, clientName: null, bindKey: "sprk_ai_search_index@odata.bind");

        var fields = await CreatedFieldsAsync(request);

        AssertNoIndexColumn(fields);
    }

    [Theory]
    [MemberData(nameof(SearchIndexedTables))]
    public async Task AnExplicitName_IsDropped_AndNeitherColumnIsWritten(string table)
    {
        var fields = await CreatedFieldsAsync(CreateWorld.Mapped(table,
            new Dictionary<string, object?> { [NameColumn] = "explicit-index" }));

        AssertNoIndexColumn(fields);
    }

    [Fact]
    public async Task TheClientsNameAlone_IsDropped_AndNothingIsWritten_WhetherOrNotTheUnitHasAName()
    {
        var (request, _) = WithClientIndex("sprk_workassignment", Guid.Empty, "client-name");
        AssertNoIndexColumn(await CreatedFieldsAsync(request));

        _world.Creates.Clear();
        _world.SearchIndexDefaults[Directory.ChildBu] = (null, ChildIndex);
        var (again, _) = WithClientIndex("sprk_workassignment", Guid.Empty, "client-name");
        AssertNoIndexColumn(await CreatedFieldsAsync(again));
    }

    /// <summary>
    /// The #2399 repro: a work assignment created by a caller of the GENERAL unit under a matter owned by the CHILD unit, the
    /// client binding the user's own unit's index, a catalog row the caller cannot AppendTo. It used to be refused (the caller
    /// has no AppendTo on the row); the bind is dropped before the caller's checks, so no AppendTo is asked on the catalog, and
    /// since task 041 the row gets no index column at all.
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
        AssertNoIndexColumn(fields);
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
    // A secure record gets no index column either: the owning Secure team's unit is not read (task 041)
    // =====================================================================================

    [Fact]
    public async Task AnEventUnderASecureMatter_WhoseUnitHasAnIndex_GetsNoIndexColumn_AndNoUnitIsRead()
    {
        _world.SearchIndexDefaults[Directory.SecureBu] = ("spaarke-secure-index", Guid.Parse("a0150000-0000-4000-8000-0000000000a9"));

        var fields = await CreatedFieldsAsync(EventUnder(CreateWorld.SecureMatter, ExplicitIndex, "client-name"));

        CreateWorld.Owner(fields).Should().Be(Directory.SecureNamedTeam, "isolated: the named Secure Record Owners team");
        AssertNoIndexColumn(fields);
        _world.BusinessUnitReads.Should().BeEmpty();
        _world.Log.Messages.Should().NotContain(m => m.Contains("search_index_unset_for_owner_bu"));
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
        lines.Should().OnlyContain(l => l.Level == Microsoft.Extensions.Logging.LogLevel.Information,
            "Information until W3 lands (UAC-r2, task 041); task 035 returns it to Warning");
        var lookup = lines.Single(l => l.Message.Contains("column=sprk_ai_search_index"));
        lookup.Message.Should().Contain("table=sprk_workassignment").And.Contain($"record={outcome.CreatedId}")
            .And.Contain("origin=caller")
            .And.Contain($"droppedIndexId={UserBusinessUnitIndex}").And.Contain("appliedIndexId=(null)");
        var name = lines.Single(l => l.Message.Contains("column=sprk_searchindexname"));
        name.Message.Should().Contain("droppedIndexName=" + GeneralIndexName).And.Contain("appliedIndexName=(null)");
        lines.Should().OnlyContain(l => !l.Message.Contains("Review the lease") && !l.Message.Contains('@'),
            "ids and catalog names only: no record name, no person, no email");
    }

    /// <summary>Review R1 (UAC-r2 condition 4): a client's free text in the name column never reaches the log.</summary>
    [Theory]
    [InlineData("jane.doe@contoso.com")]
    [InlineData("Jane Doe, Acme Corp: privileged and confidential strategy memo for the Widget matter, do not forward")]
    public async Task ADroppedNameThatIsNotAnIndexName_NeverReachesTheLog_OnlyItsLength(string freeText)
    {
        var (request, _) = WithClientIndex("sprk_workassignment", Guid.Empty, freeText);

        var outcome = await _world.CreateAsync(request);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var line = _world.Log.Messages.Should().ContainSingle(m => m.Contains("client_search_index_dropped")).Subject;
        line.Should().NotContain("jane").And.NotContain("contoso").And.NotContain("Acme").And.NotContain("memo").And.NotContain("@");
        line.Should().Contain("<not an index name>").And.Contain($"length {freeText.Length}");
        line.Should().Contain("appliedIndexName=(null)", "nothing is applied (task 041), so no catalog name is logged");
    }

    [Fact]
    public async Task ADroppedNameThatIsAnIndexName_IsLoggedAsIs()
    {
        var (request, _) = WithClientIndex("sprk_workassignment", Guid.Empty, "client-made-index");

        await _world.CreateAsync(request);

        _world.Log.Messages.Should().ContainSingle(m => m.Contains("client_search_index_dropped"))
            .Which.Should().Contain("droppedIndexName=client-made-index");
    }

    /// <summary>
    /// Task 041: the owning unit's own index, sent by the client, is no longer "equal to the applied" — nothing is applied, so
    /// every dropped value differs and is logged (FR-38's "log only when it differs" rule itself is unchanged; the both-none
    /// case below still logs nothing).
    /// </summary>
    [Fact]
    public async Task ADroppedIndexThatEqualsTheOwningUnitsIndex_IsLogged_BecauseNothingIsApplied()
    {
        var (request, _) = WithClientIndex("sprk_workassignment", ChildIndex, "  " + ChildIndexName.ToUpperInvariant() + " ");

        var outcome = await _world.CreateAsync(request);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _world.Log.Messages.Where(m => m.Contains("client_search_index_dropped")).Should().HaveCount(2,
            "one line per column: nothing is applied, so the unit's own index differs from it");
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
    public async Task ABlankName_IsDropped_AndNeitherColumnIsWritten(string table)
    {
        var fields = await CreatedFieldsAsync(CreateWorld.Mapped(table, new Dictionary<string, object?> { [NameColumn] = "  " }));

        AssertNoIndexColumn(fields);
    }

    // =====================================================================================
    // An explicit null bind → dropped, and never sent as null (UAC-r2's review of 015, K3; task 018a; task 041 AC8)
    // =====================================================================================

    [Theory]
    [InlineData("sprk_ai_search_index@odata.bind", "a lower-case spelling of the bind key")]
    [InlineData(LookupBind, "the canonical spelling of the bind key")]
    public async Task AnExplicitNullBind_IsDropped_AndNoBindKeyIsSent_NotEvenAsNull(string nullKey, string because)
    {
        var fields = await CreatedFieldsAsync(CreateWorld.Mapped("sprk_matter",
            new Dictionary<string, object?> { ["sprk_mattername"] = "Acme v. Widget", [nullKey] = null }));

        fields.Keys.Where(k => string.Equals(k, LookupBind, StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty($"{because}: an existing value is left as it is, never cleared by a null");
        AssertNoIndexColumn(fields);
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

    /// <summary>
    /// Review R5, must-not-fire on the lookup itself: on a table that is NOT search-indexed, a bind of the index lookup is still
    /// the caller's own value — refused when the caller cannot AppendTo the catalog row (as before 039), written when they can.
    /// </summary>
    [Fact]
    public async Task ATableNotMarkedSearchIndexed_KeepsTheCallersSearchIndexLookup_AndItsAppendToCheck()
    {
        UnifiedCreateRequest Todo() => CreateWorld.Mapped("sprk_todo",
            new Dictionary<string, object?>
            {
                ["sprk_name"] = "Call the client",
                [LookupBind] = $"/sprk_aisearchindexes({ExplicitIndex:D})",
            },
            new DataverseWriteItemMapper.MappedLookup("sprk_ai_search_index", "sprk_aisearchindex", "sprk_aisearchindexes", ExplicitIndex));

        _world.Caller.NoAppendToIn.Add(("sprk_aisearchindexes", ExplicitIndex));
        var denied = await _world.CreateAsync(Todo());

        denied.Succeeded.Should().BeFalse("AppendTo on the lookup is still the caller's check on a table that is not search-indexed");
        _world.Creates.Should().BeEmpty();
        _world.Log.Messages.Should().NotContain(m => m.Contains("client_search_index_dropped"));

        _world.Caller.NoAppendToIn.Clear();
        var allowed = await _world.CreateAsync(Todo());

        allowed.Succeeded.Should().BeTrue(allowed.Refusal?.Detail);
        var fields = _world.Creates.Should().ContainSingle().Subject.Fields;
        Text(fields, LookupBind).Should().Be($"/sprk_aisearchindexes({ExplicitIndex:D})", "the caller's own lookup is written as given");
        _world.Log.Messages.Should().NotContain(m => m.Contains("client_search_index_dropped"));
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

    /// <summary>Task 041: step 8 reads no business unit, so a unit that cannot be read neither warns nor touches the create.</summary>
    [Fact]
    public async Task WhenTheOwningBusinessUnitCannotBeRead_TheCreateIsUnaffected_AndNothingIsSaidAboutTheIndex()
    {
        _world.BusinessUnitReadFault = new TimeoutException("business unit read timed out");

        var outcome = await _world.CreateAsync(await _world.MappedAsync("sprk_matter", new { sprk_mattername = "Acme v. Widget" }));

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        AssertNoIndexColumn(_world.Creates.Should().ContainSingle().Subject.Fields);
        outcome.Warnings.Should().BeEmpty("the create no longer reads the unit's search defaults, so it has nothing to report");
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

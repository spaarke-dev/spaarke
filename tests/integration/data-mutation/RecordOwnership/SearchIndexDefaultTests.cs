using FluentAssertions;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 015 (spec FR-10, write-path invariant I-5) — step 8 of the unified create: on a
/// table the rules registry marks search-indexed (matter, project, document), the OWNING business unit's
/// <c>sprk_searchindexname</c> and <c>sprk_ai_search_index</c>, each filled only when the row carries no value for it; an
/// explicit value wins; a table not marked search-indexed is untouched; a unit that cannot be read fills nothing and says
/// so, and the create stands.
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
    // Explicit → kept (fill-only)
    // =====================================================================================

    [Theory]
    [MemberData(nameof(SearchIndexedTables))]
    public async Task AnExplicitSearchIndex_IsKept_OnEverySearchIndexedTable(string table)
    {
        var fields = await CreatedFieldsAsync(CreateWorld.Mapped(table,
            new Dictionary<string, object?>
            {
                [NameColumn] = "explicit-index",
                [LookupBind] = $"/sprk_aisearchindexes({ExplicitIndex:D})",
            },
            new DataverseWriteItemMapper.MappedLookup("sprk_ai_search_index", "sprk_aisearchindex", "sprk_aisearchindexes", ExplicitIndex)));

        Text(fields, NameColumn).Should().Be("explicit-index");
        Text(fields, LookupBind).Should().Be($"/sprk_aisearchindexes({ExplicitIndex:D})");
        _world.BusinessUnitReads.Should().BeEmpty("both values were supplied, so nothing is read");
    }

    [Theory]
    [MemberData(nameof(SearchIndexedTables))]
    public async Task AnExplicitName_IsKept_AndOnlyTheUnsetLookupIsFilled(string table)
    {
        var fields = await CreatedFieldsAsync(CreateWorld.Mapped(table,
            new Dictionary<string, object?> { [NameColumn] = "explicit-index" }));

        Text(fields, NameColumn).Should().Be("explicit-index");
        Text(fields, LookupBind).Should().Be($"/sprk_aisearchindexes({GeneralIndex:D})", "the unset lookup takes the owning unit's");
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

    [Fact]
    public async Task ATableNotMarkedSearchIndexed_ReceivesNoSearchIndexValue()
    {
        var outcome = await _world.CreateAsync(await _world.MappedAsync("sprk_event",
            new { sprk_eventname = "Hearing", sprk_regardingmatter = CreateWorld.Lookup("sprk_matter", CreateWorld.OrdinaryMatter) }));

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _world.Creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey(NameColumn).And.NotContainKey(LookupBind);
        _world.BusinessUnitReads.Should().BeEmpty();
        outcome.Steps.Single(s => s.Step == RecordCreationStep.SearchIndexDefault).State.Should().Be(RecordCreationStepState.NoOp);
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

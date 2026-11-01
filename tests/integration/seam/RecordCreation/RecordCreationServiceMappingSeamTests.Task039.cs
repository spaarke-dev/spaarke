using FluentAssertions;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 039 (FR-38, #2399) — the two ORIGINS of a search-index value that reach the create
/// after the caller's own payload: a field-mapping rule that targets an index column (step 3), and an adapter's post-mapping
/// default (step 3½). Both are dropped before the caller's checks on a search-indexed table: no mapped-value or default
/// warning names either column, no AppendTo is asked on the catalog row, and step 8 is the only writer.
/// </summary>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    private const string IndexNameColumn = "sprk_searchindexname";
    private const string IndexLookupColumn = "sprk_ai_search_index";
    private static readonly Guid SourceIndex = Guid.Parse("a0140000-0000-4000-8000-0000000000d1");

    private void EventIndexColumns()
    {
        _user.Columns["sprk_event"][IndexNameColumn] = ("String", true);
        _user.Columns["sprk_event"][IndexLookupColumn] = ("Lookup", true);
    }

    [Fact]
    public async Task SearchIndex_AMappedValueForEitherIndexColumn_IsDropped_WithNoWarning_AndNoAppendToAsked()
    {
        EventIndexColumns();
        Profile("sprk_matter", "sprk_event",
            Rule("sprk_location", IndexNameColumn, Text),
            Rule(IndexLookupColumn, IndexLookupColumn, Lookup),
            Rule("sprk_matterdescription", "sprk_description", Memo));
        // The source row carries an index of its own (a real mapping rule would copy it). If the mapped lookup reached the
        // caller's checks, the caller could not append to it and a warning would name the column; and AppendTo would be asked.
        _sources[("sprk_matter", OrdinaryMatter)][IndexLookupColumn] = new Microsoft.Xrm.Sdk.EntityReference("sprk_aisearchindex", SourceIndex);
        _scripted.NoAppendToIn.Add(("sprk_aisearchindexes", SourceIndex));
        var asked = new List<string>();
        _scripted.GetOverride = path =>
        {
            if (path.Contains("sprk_aisearchindexes(", StringComparison.Ordinal))
                asked.Add(path);
            return null;
        };
        var log = new CapturingLog<RecordCreationService>();
        _logger = log;

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", OrdinaryMatter)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.MappingWarnings.Should().NotContain(w =>
                string.Equals(w.Column, IndexNameColumn, StringComparison.OrdinalIgnoreCase)
                || string.Equals(w.Column, IndexLookupColumn, StringComparison.OrdinalIgnoreCase)
                || (w.Detail ?? string.Empty).Contains(IndexNameColumn)
                || (w.Detail ?? string.Empty).Contains(IndexLookupColumn),
            "no mapped-value warning mentions either index column: the value is the server's, not a refused mapping");
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey(IndexNameColumn);
        fields.Keys.Should().NotContain(k => k.StartsWith("sprk_ai_search_index", StringComparison.OrdinalIgnoreCase)
                || k.StartsWith("sprk_AI_Search_Index", StringComparison.OrdinalIgnoreCase),
            "this world's business units carry no index, so step 8 writes none and the mapped value is not passed on");
        asked.Should().BeEmpty("no AppendTo is asked on the catalog row: the mapped bind is dropped before step 5");
        StringOf(fields, "sprk_description").Should().Be("From the matter", "the other mapped values are written as before");
        log.Messages.Should().Contain(m => m.Contains("client_search_index_dropped") && m.Contains("origin=mapping")
            && m.Contains("column=" + IndexNameColumn));
        log.Messages.Should().Contain(m => m.Contains("client_search_index_dropped") && m.Contains("origin=mapping")
            && m.Contains("column=" + IndexLookupColumn));
    }

    /// <summary>
    /// Round 3 (G8): the late drop removes the mapping warnings ABOUT an index column (matched on the warning's column), not every
    /// warning whose text merely mentions one. A profile named after the column and holding no rules gets its own warning, which stays.
    /// </summary>
    [Fact]
    public async Task SearchIndex_AnUnrelatedWarningThatMentionsAnIndexColumn_IsKept()
    {
        EventIndexColumns();
        Profile("sprk_matter", "sprk_event");
        _profiles[("sprk_matter", "sprk_event")].Name = "Copy sprk_ai_search_index defaults";

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", OrdinaryMatter)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.MappingWarnings.Should().ContainSingle(w => w.Detail.Contains("has no rules") && w.Detail.Contains(IndexLookupColumn),
            "the warning is about the profile, not about a dropped index value");
    }

    [Fact]
    public async Task SearchIndex_APostMappingDefaultForTheNameColumn_IsDropped_WithNoWarning_AndLoggedAsADefault()
    {
        EventIndexColumns();
        var log = new CapturingLog<RecordCreationService>();
        _logger = log;

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter) with
            {
                Mapping = null,
                PostMappingDefaults = new Dictionary<string, object?> { [IndexNameColumn] = "adapter-default-index" },
            },
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.MappingWarnings.Should().BeEmpty("a default the server drops is not a default the caller could not set");
        _creates.Should().ContainSingle().Subject.Fields.Should().NotContainKey(IndexNameColumn);
        log.Messages.Should().ContainSingle(m => m.Contains("client_search_index_dropped"))
            .Which.Should().Contain("origin=default").And.Contain("column=" + IndexNameColumn);
    }

    [Fact]
    public async Task SearchIndex_ACallersName_OnAnEventWithNoMapping_IsDropped_AsTheCallers()
    {
        EventIndexColumns();
        var log = new CapturingLog<RecordCreationService>();
        _logger = log;

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, extra: (IndexNameColumn, "client-index")) with { Mapping = null },
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _creates.Should().ContainSingle().Subject.Fields.Should().NotContainKey(IndexNameColumn);
        log.Messages.Should().ContainSingle(m => m.Contains("client_search_index_dropped")).Which.Should().Contain("origin=caller");
    }
}

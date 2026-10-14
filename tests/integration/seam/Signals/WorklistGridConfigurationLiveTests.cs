using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Xrm.Sdk.Metadata;
using Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Signals;

/// <summary>
/// Real-Dataverse pairing for the worklist <c>sprk_gridconfiguration</c> definition (task 050;
/// <c>config/worklist.gridconfiguration.json</c>). <see cref="WorklistGridConfigurationTests"/> checks the definition against
/// the repo's solution source; this checks it against the DEPLOYED schema: each of the five queries runs as a read, every
/// column it names exists with the type the query relies on, and every option value it uses is a live option. Read-only: no
/// row is written. Opt-in with <see cref="SignalPredicateTests.UrlEnvVar"/> (same convention as <see cref="SignalPredicateTests"/>):
/// without it every test returns at once, so a pass in CI means "skipped", not "verified".
/// </summary>
/// <remarks>
/// Run status (2026-10-13, spaarkedev1, <c>AZURE_TOKEN_CREDENTIALS=AzureCliCredential</c>): see
/// <c>projects/spaarke-ontology-platform-r1/notes/task-050-worklist-gridconfiguration.md</c>. The dev table held no Signals
/// at that time, so the queries return zero rows: they prove the schema, the operators and the option values, not
/// membership. Membership against rows needs the data a later task seeds (zz- prefixed, deleted after).
/// </remarks>
[Trait("status", "repaired")]
[Trait("Category", "Live")]
public sealed class WorklistGridConfigurationLiveTests : IClassFixture<SignalPredicateTests.LiveDataverse>
{
    private readonly SignalPredicateTests.LiveDataverse _dv;
    private readonly ITestOutputHelper _out;

    public WorklistGridConfigurationLiveTests(SignalPredicateTests.LiveDataverse dv, ITestOutputHelper output)
    {
        _dv = dv;
        _out = output;
    }

    [Fact]
    public async Task EveryQuery_RunsAsARead_AgainstTheDeployedSchema()
    {
        if (!_dv.IsLive) return;

        foreach (var (name, doc) in WorklistGridConfigurationTests.Queries(WorklistGridConfigurationTests.LoadDefinitionText()))
        {
            var rows = await _dv.RunFetchRowsAsync(doc.ToString(SaveOptions.DisableFormatting));
            _out.WriteLine($"{name}: {rows.Count} row(s)");
            rows.Count.Should().BeLessThan(5000, "the first page is the whole answer on dev; paging is task 038's job");
        }
    }

    [Fact]
    public async Task ABadColumn_IsRefusedByDataverse_SoTheRunAboveWouldHaveFailedOnOne()
    {
        if (!_dv.IsLive) return;

        var bad = WorklistGridConfigurationTests.LoadDefinitionText().Replace("sprk_worktype", "sprk_noSuchColumn");
        var doc = WorklistGridConfigurationTests.Queries(bad)["lane:decide"];

        var run = () => _dv.RunFetchRowsAsync(doc.ToString(SaveOptions.DisableFormatting));

        await run.Should().ThrowAsync<Exception>("an unknown column must fail, not silently return rows");
    }

    [Fact]
    public async Task TheColumnsTheQueriesRelyOn_HaveTheLiveTypesAndOptions()
    {
        if (!_dv.IsLive) return;

        (await _dv.GetAttributeAsync("sprk_signal", "sprk_corerecordtype")).Should().BeOfType<LookupAttributeMetadata>();
        (await _dv.GetAttributeAsync("sprk_signal", "sprk_corerecordid")).Should().BeOfType<StringAttributeMetadata>();

        // The time operators (last-x-hours, next-x-years, olderthan-x-minutes) are valid only on date-and-time columns;
        // sprk_duedate is Date Only and is deliberately not filtered on.
        foreach (var column in new[] { "sprk_suppresseduntil", "sprk_resolvedon", "sprk_firstdetected" })
        {
            var attribute = await _dv.GetAttributeAsync("sprk_signal", column);
            attribute.Should().BeOfType<DateTimeAttributeMetadata>(column);
            ((DateTimeAttributeMetadata)attribute).Format.Should().Be(DateTimeFormat.DateAndTime, column);
        }

        ((DateTimeAttributeMetadata)await _dv.GetAttributeAsync("sprk_signal", "sprk_duedate")).Format.Should().Be(DateTimeFormat.DateOnly);

        var status = (PicklistAttributeMetadata)await _dv.GetAttributeAsync("sprk_signal", "sprk_signalstatus");
        status.OptionSet.Options.Select(o => o.Value).Should().BeEquivalentTo(new int?[] { 100000000, 100000001, 100000002 });

        var lane = (PicklistAttributeMetadata)await _dv.GetAttributeAsync("sprk_signal", "sprk_lane");
        lane.OptionSet.Options.Select(o => o.Value).Should().BeEquivalentTo(new int?[] { 100000000, 100000001 });

        var workType = (PicklistAttributeMetadata)await _dv.GetAttributeAsync("sprk_policy", "sprk_worktype");
        workType.OptionSet.Options.Should().HaveCount(6, "the route maps six work types (task 038)");
        (await _dv.GetAttributeAsync("sprk_policy", "sprk_shortname")).Should().BeOfType<StringAttributeMetadata>();
        (await _dv.GetAttributeAsync("sprk_policy", "sprk_severity")).Should().BeOfType<PicklistAttributeMetadata>();
    }
}

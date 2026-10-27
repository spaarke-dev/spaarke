using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Signals;

/// <summary>
/// Task 063: the Know-promotion rule <c>POL-MATTER-NOBUDGET</c> ("this matter has no budget", <c>config/know-promotion.policies.json</c>)
/// compiled with the production <see cref="PredicateCompiler"/> and its ONE FetchXML run read-only against spaarkedev1, through the
/// same SDK call the evaluator makes. Nothing is written: no policy row, no Signal, no test row.
/// </summary>
/// <remarks>
/// <para><b>What it proves on real rows.</b> A matter that HAS a <c>sprk_budget</c> row is excluded (the notExists is false for it)
/// and one that has none is included (true), asserted by id and per matter through the compiler's <c>subjectId</c> narrowing, and the
/// whole result equals an independent computation from two plain queries (matters created before the cutoff, minus the matters named
/// by an active budget row). A wrong join column would make the notExists true for every matter, which the first assertion catches.
/// The five-day gate is shown on real rows by moving the clock (a <see cref="FakeTimeProvider"/>), not by writing a row.</para>
/// <para><b>Rows used (spaarkedev1, read 2026-10-16).</b> HAS a budget: <c>2444af6d-e1f2-f011-8406-7ced8d1dc988</c> (budget
/// <c>e3cbe1fc-0f09-f111-8406-7c1e525abd8b</c>, created 2026-01-16). NO budget: <c>3a785f76-c773-f011-b4cb-6045bdd8b757</c> (created
/// 2025-08-07). Recent, no budget: <c>ced2c3d9-47bf-f111-aaaf-0022482913fc</c> (created 2026-10-03). If dev data changes these ids must
/// be re-read; the test says which one moved.</para>
/// <para>Skip-via-return, opt-in, same convention and fixture as <see cref="SignalPredicateTests"/>.</para>
/// </remarks>
[Trait("status", "new")]
[Trait("Category", "Live")]
public sealed class KnowPromotionPolicySeamTests : IClassFixture<SignalPredicateTests.LiveDataverse>
{
    private static readonly Guid MatterWithBudget = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc988");
    private static readonly Guid MatterWithoutBudget = Guid.Parse("3a785f76-c773-f011-b4cb-6045bdd8b757");
    private static readonly Guid RecentMatterWithoutBudget = Guid.Parse("ced2c3d9-47bf-f111-aaaf-0022482913fc"); // created 2026-10-03

    private readonly SignalPredicateTests.LiveDataverse _dv;
    private readonly ITestOutputHelper _out;

    public KnowPromotionPolicySeamTests(SignalPredicateTests.LiveDataverse dv, ITestOutputHelper output)
    {
        _dv = dv;
        _out = output;
    }

    private static string RuleBody()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs")))
        {
            dir = dir.Parent;
        }

        var path = Path.Combine(dir?.FullName ?? throw new InvalidOperationException("repository root not found"), "config", "know-promotion.policies.json");
        var policy = JsonNode.Parse(File.ReadAllText(path))!["policies"]![0]!;
        return policy["versions"]![0]!["ruleBody"]!.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static PredicateCompiler Compiler(DateTimeOffset now) => new(new RuleBodySchemaValidator(), new FakeTimeProvider(now));

    private async Task<IReadOnlyList<Guid>> RunAsync(CompiledPredicate compiled)
    {
        var ids = await _dv.RunAsync(compiled);
        _out.WriteLine(compiled.FetchXml);
        _out.WriteLine($"-> {ids.Count} row(s)");
        return ids;
    }

    [Fact]
    public async Task AMatterWithABudgetIsOut_AMatterWithNoneIsIn_AndTheWholeSetEqualsTheIndependentComputation()
    {
        if (!_dv.IsLive) return;

        var now = DateTimeOffset.UtcNow;
        var body = RuleBody();
        _out.WriteLine($"principal: {await _dv.WhoAmIAsync()}; run at {now:O}");

        var all = await RunAsync(Compiler(now).Compile(body));

        all.Should().NotContain(MatterWithBudget, "it has an active sprk_budget row, so the notExists is false");
        all.Should().Contain(MatterWithoutBudget, "it was created long ago and has no sprk_budget row, so the notExists is true");
        all.Count.Should().BeGreaterThan(0).And.BeLessThan(await _dv.CountMattersAsync(), "a rule that returns every matter is not a filter");

        // Per matter (the compiler's subjectId narrowing, the evaluator's per-matter trigger path).
        (await RunAsync(Compiler(now).Compile(body, MatterWithBudget))).Should().BeEmpty();
        (await RunAsync(Compiler(now).Compile(body, MatterWithoutBudget))).Should().Equal(MatterWithoutBudget);

        // Independent computation from two plain queries: matters created on or before the cutoff, minus the matters named by an active budget.
        var cutoff = TruncateToSeconds(now.AddDays(-5)).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var old = (await _dv.RunFetchAsync(
            $"""<fetch><entity name="sprk_matter"><attribute name="sprk_matterid" /><filter><condition attribute="createdon" operator="le" value="{cutoff}" /></filter></entity></fetch>""",
            "sprk_matterid")).ToHashSet();
        var budgeted = (await _dv.RunFetchRowsAsync(
            """<fetch><entity name="sprk_budget"><attribute name="sprk_matter" /><filter><condition attribute="statecode" operator="eq" value="0" /><condition attribute="sprk_matter" operator="not-null" /></filter></entity></fetch>"""))
            .Select(e => e.GetAttributeValue<EntityReference>("sprk_matter").Id).ToHashSet();
        _out.WriteLine($"old matters {old.Count}, matters with an active budget {budgeted.Count}");
        budgeted.Should().Contain(MatterWithBudget, "the fixture matter really is named by a budget row");

        all.Should().BeEquivalentTo(old.Except(budgeted));
    }

    [Fact]
    public async Task TheFiveDayGate_ExcludesAYoungMatterWithNoBudget_OnRealRows()
    {
        if (!_dv.IsLive) return;

        var body = RuleBody();
        // The recent matter was created 2026-10-03. Anchored at 2026-10-05 (two days later) it is NOT yet 5 days old: out. Anchored at
        // 2026-10-16 it is 13 days old: in. Same row, same budget state, only the clock moves.
        var young = await RunAsync(Compiler(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero)).Compile(body, RecentMatterWithoutBudget));
        var aged = await RunAsync(Compiler(new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero)).Compile(body, RecentMatterWithoutBudget));

        young.Should().BeEmpty("created 2026-10-03, so only 2 days old at 2026-10-05");
        aged.Should().Equal(RecentMatterWithoutBudget);
    }

    private static DateTimeOffset TruncateToSeconds(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
}

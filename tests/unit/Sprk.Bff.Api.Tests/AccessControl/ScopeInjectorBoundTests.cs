// unified-access-control-r2 task 018 — FR-17 (finding A-16): the Tier-2 scope injector's `in`-clause
// bound.
//
// A-16: Tier2ScopeFilterInjector.Inject emitted one <value> per accessible id with NO cap. A caller
// with a large composed accessible set (high ADR-034 membership, or a child module fanning out across
// three parent-lookup dimensions) produced a single <condition operator='in'> past Dataverse's
// documented ~500-values-per-condition guidance — the module fetch then failed/500'd or returned
// empty. The fix CHUNKS each dimension into <=MaxValuesPerInCondition sibling conditions.
//
// WHY THESE TESTS EXIST (and what they are actually guarding):
// The chunk bound is a query-SHAPE parameter, and the whole safety argument is that it is *not* a
// scope parameter. So the load-bearing assertions here are the two directions of scope invariance:
//   - union(emitted values) is not a SUBSET of the accessible set  => no silent under-grant
//                                                                     (nothing to surface per NFR-03)
//   - union(emitted values) is not a SUPERSET, and no chunk migrates to another dimension's
//     attribute                                                    => no over-grant / disclosure
// plus the off-by-one that would emit an invalid `IN ()` at an exact multiple of the bound.
//
// Per the project's threshold discipline these are pinned AT the bound and JUST UNDER it, not only
// far above it — an off-by-one is invisible to a "give it 10,000 ids" test.
//
// Pure domain logic: no Dataverse, no HTTP, no test doubles at all (the injector is a static pure
// function over its arguments), so there is no double that could default permissive on unmodelled
// input. Sibling coverage for the injector's non-bound behaviour lives in
// tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/Tier2ScopeFilterInjectorTests.cs; this file is the
// separately auditable FR-17/A-16 deliverable.

using System.Xml.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

public sealed class ScopeInjectorBoundTests
{
    private const int Bound = Tier2ScopeFilterInjector.MaxValuesPerInCondition;

    private const string DocFetchXml =
        "<fetch><entity name=\"sprk_document\">" +
        "<attribute name=\"sprk_documentid\"/><attribute name=\"sprk_project\"/>" +
        "<attribute name=\"sprk_matter\"/>" +
        "<order attribute=\"createdon\" descending=\"true\"/>" +
        "</entity></fetch>";

    private static HashSet<Guid> Ids(int count)
    {
        var set = new HashSet<Guid>(count);
        while (set.Count < count)
        {
            set.Add(Guid.NewGuid());
        }

        return set;
    }

    private static List<XElement> ConditionsOf(string fetchXml) =>
        XDocument.Parse(fetchXml).Root!.Element("entity")!.Element("filter")!
            .Elements("condition").ToList();

    private static List<string> ValuesOf(XElement condition) =>
        condition.Elements("value").Select(v => v.Value).ToList();

    // ── The bound itself ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Bound_IsFiveHundred_MatchingDataverseInOperatorGuidance()
    {
        // Pinned so the number cannot drift silently away from the ~500/condition Dataverse guidance
        // that MembershipResolverService.BuildTransitiveFetchXml documents for the same reason.
        // Every other test in this file is expressed relative to the constant, so without this one a
        // change to the number would leave the suite green and meaningless.
        Bound.Should().Be(500);
    }

    // ── Threshold behaviour: at the bound, just under, just over ────────────────────────────────

    [Fact]
    public void Inject_WhenIdCountIsJustUnderBound_EmitsExactlyOneCondition()
    {
        var ids = Ids(Bound - 1);

        var conditions = ConditionsOf(Tier2ScopeFilterInjector.Inject(DocFetchXml, "sprk_project", ids));

        conditions.Should().ContainSingle("a set below the bound must not be split");
        ValuesOf(conditions[0]).Should().HaveCount(Bound - 1);
    }

    [Fact]
    public void Inject_WhenIdCountIsExactlyBound_EmitsExactlyOneFullCondition()
    {
        // The threshold case. A `>=` / `>` slip here either splits unnecessarily or — worse — emits a
        // trailing empty condition (`IN ()`), which Dataverse rejects outright.
        var ids = Ids(Bound);

        var conditions = ConditionsOf(Tier2ScopeFilterInjector.Inject(DocFetchXml, "sprk_project", ids));

        conditions.Should().ContainSingle("a set exactly at the bound still fits one condition");
        ValuesOf(conditions[0]).Should().HaveCount(Bound);
    }

    [Fact]
    public void Inject_WhenIdCountIsJustOverBound_SplitsIntoTwoConditionsWithRemainder()
    {
        var ids = Ids(Bound + 1);

        var conditions = ConditionsOf(Tier2ScopeFilterInjector.Inject(DocFetchXml, "sprk_project", ids));

        conditions.Should().HaveCount(2);
        conditions.Select(c => ValuesOf(c).Count).Should().BeEquivalentTo(new[] { Bound, 1 });
    }

    [Fact]
    public void Inject_WhenIdCountIsAnExactMultipleOfBound_EmitsNoEmptyTrailingCondition()
    {
        // The classic chunking off-by-one: count / size == 2 but a naive loop emits a third, empty
        // condition. An empty `IN ()` is invalid FetchXML — the exact 500/malformed-query failure
        // A-16 is about.
        var ids = Ids(Bound * 2);

        var conditions = ConditionsOf(Tier2ScopeFilterInjector.Inject(DocFetchXml, "sprk_project", ids));

        conditions.Should().HaveCount(2);
        conditions.Should().OnlyContain(c => c.Elements("value").Any(), "an `IN ()` is invalid FetchXML");
        conditions.Select(c => ValuesOf(c).Count).Should().AllBeEquivalentTo(Bound);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(Bound - 1)]
    [InlineData(Bound)]
    [InlineData(Bound + 1)]
    [InlineData(Bound * 2)]
    [InlineData((Bound * 2) + 1)]
    public void Inject_NoEmittedConditionEverExceedsTheBound(int idCount)
    {
        var conditions = ConditionsOf(
            Tier2ScopeFilterInjector.Inject(DocFetchXml, "sprk_project", Ids(idCount)));

        conditions.Should().OnlyContain(c => c.Elements("value").Count() <= Bound);
        conditions.Should().OnlyContain(c => c.Elements("value").Any());
    }

    // ── Scope invariance — the actual security property ─────────────────────────────────────────

    [Theory]
    [InlineData(Bound - 1)]
    [InlineData(Bound)]
    [InlineData(Bound + 1)]
    [InlineData((Bound * 3) + 7)]
    public void Inject_UnionOfChunkedValues_EqualsTheAccessibleSetExactly(int idCount)
    {
        // Both failure directions at once:
        //   a dropped id  => union is a strict subset  => the caller silently loses records they may
        //                    see (under-grant; would then need an NFR-03 signal we deliberately do not
        //                    have, because chunking never drops)
        //   an extra/duplicated id => union is a strict superset => the query matches rows outside the
        //                    caller's set (over-grant — the disclosure direction)
        var ids = Ids(idCount);

        var emitted = ConditionsOf(Tier2ScopeFilterInjector.Inject(DocFetchXml, "sprk_project", ids))
            .SelectMany(ValuesOf)
            .ToList();

        emitted.Should().HaveCount(idCount, "chunking must not duplicate or drop ids");
        emitted.Select(Guid.Parse).Should().BeEquivalentTo(ids);
    }

    [Fact]
    public void Inject_ChunkedDimension_EmitsEveryChunkAgainstTheSameAttribute()
    {
        // The over-grant perturbation specific to chunking: a chunk escaping onto a DIFFERENT
        // dimension's attribute would scope, say, sprk_matter by project ids — matching rows the
        // caller cannot see.
        var conditions = ConditionsOf(
            Tier2ScopeFilterInjector.Inject(DocFetchXml, "sprk_project", Ids(Bound + 1)));

        conditions.Should().HaveCount(2);
        conditions.Should().OnlyContain(c => c.Attribute("attribute")!.Value == "sprk_project");
        conditions.Should().OnlyContain(c => c.Attribute("operator")!.Value == "in");
    }

    [Fact]
    public void Inject_MultipleChunkedDimensions_KeepEachAttributesUnionSeparateAndExact()
    {
        var projectIds = Ids(Bound + 1);   // chunks into 2
        var matterIds = Ids(Bound * 2);    // chunks into 2
        var dims = new[]
        {
            new Tier2ScopeFilterInjector.ScopeFilterDimension("sprk_project", projectIds),
            new Tier2ScopeFilterInjector.ScopeFilterDimension("sprk_matter", matterIds),
        };

        var conditions = ConditionsOf(Tier2ScopeFilterInjector.Inject(DocFetchXml, dims));

        conditions.Should().HaveCount(4);

        var byAttribute = conditions.GroupBy(c => c.Attribute("attribute")!.Value)
            .ToDictionary(g => g.Key, g => g.SelectMany(ValuesOf).Select(Guid.Parse).ToList());

        byAttribute.Should().ContainKeys("sprk_project", "sprk_matter");
        byAttribute["sprk_project"].Should().BeEquivalentTo(projectIds);
        byAttribute["sprk_matter"].Should().BeEquivalentTo(matterIds);
    }

    [Fact]
    public void Inject_ChunkedFilter_UsesOrCombinatorSoSiblingChunksReUnion()
    {
        // Load-bearing invariant: chunk re-union `(attr in c1) OR (attr in c2) == attr in (c1 ∪ c2)`
        // holds ONLY under an `or` filter. If this ever flips to 'and', disjoint chunks intersect to
        // nothing and every high-membership caller sees zero rows.
        var filter = XDocument.Parse(
                Tier2ScopeFilterInjector.Inject(DocFetchXml, "sprk_project", Ids(Bound + 1)))
            .Root!.Element("entity")!.Element("filter")!;

        filter.Attribute("type")!.Value.Should().Be("or");
    }

    // ── Validity + fail-closed ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Inject_LargeSet_StillProducesWellFormedCanonicalFetchXml()
    {
        var result = Tier2ScopeFilterInjector.Inject(DocFetchXml, "sprk_project", Ids((Bound * 4) + 3));

        var act = () => XDocument.Parse(result);
        act.Should().NotThrow("a chunked scope filter must remain well-formed XML");

        var children = XDocument.Parse(result).Root!.Element("entity")!.Elements().ToList();
        var filterIndex = children.FindIndex(e => e.Name == "filter");
        var orderIndex = children.FindIndex(e => e.Name == "order");

        filterIndex.Should().BeGreaterThanOrEqualTo(0);
        orderIndex.Should().BeGreaterThan(filterIndex, "FetchXML requires filter before order");
        children.Where(e => e.Name == "attribute").Select(a => a.Attribute("name")!.Value)
            .Should().Contain(new[] { "sprk_documentid", "sprk_project", "sprk_matter" });
    }

    [Fact]
    public void Inject_WhenAccessibleSetEmpty_ThrowsRatherThanEmittingAnUnfilteredQuery()
    {
        // Fail-closed (ADR-003): the degenerate set must never fall through to a query with no scope
        // filter. The endpoint short-circuits this case to 0 rows before calling in; the throw is the
        // backstop if a future caller forgets.
        var act = () => Tier2ScopeFilterInjector.Inject(DocFetchXml, "sprk_project", new HashSet<Guid>());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Inject_WhenEveryDimensionEmpty_ThrowsRatherThanEmittingAnUnfilteredQuery()
    {
        var dims = new[]
        {
            new Tier2ScopeFilterInjector.ScopeFilterDimension("sprk_project", new HashSet<Guid>()),
            new Tier2ScopeFilterInjector.ScopeFilterDimension("sprk_matter", new HashSet<Guid>()),
        };

        var act = () => Tier2ScopeFilterInjector.Inject(DocFetchXml, dims);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Inject_MixedEmptyAndChunkedDimensions_OmitsEmptyAndStillChunksTheRest()
    {
        var projectIds = Ids(Bound + 1);
        var dims = new[]
        {
            new Tier2ScopeFilterInjector.ScopeFilterDimension("sprk_project", projectIds),
            new Tier2ScopeFilterInjector.ScopeFilterDimension("sprk_matter", new HashSet<Guid>()),
        };

        var conditions = ConditionsOf(Tier2ScopeFilterInjector.Inject(DocFetchXml, dims));

        conditions.Should().HaveCount(2, "the empty dimension is omitted; the large one splits in two");
        conditions.Should().OnlyContain(c => c.Attribute("attribute")!.Value == "sprk_project");
        conditions.SelectMany(ValuesOf).Select(Guid.Parse).Should().BeEquivalentTo(projectIds);
    }
}

using FluentAssertions;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ontology;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ontology;

/// <summary>
/// Ontology platform R1 task 103: the pure rules of the Classification area (the recall-against-floor verdict, the rule-body scan for
/// a category id, and the name/guidance validation). The routes that use them are pinned through the real pipeline by
/// <c>tests/integration/auth/Ontology/OntologyAdminClassificationEndpointTests.cs</c>.
/// </summary>
public class OntologyAdminClassificationPureTests
{
    // ── Recall against the floor (D-49: 80 percent on at least 50 items) ──────────────────────────────────────────

    [Theory]
    [InlineData(88.89, 92, "MeetsFloor")]
    [InlineData(80.0, 50, "MeetsFloor")]       // both boundaries are inclusive
    [InlineData(79.99, 92, "BelowFloor")]
    [InlineData(0.0, 92, "BelowFloor")]        // a measured zero is a failure, not "not measured"
    [InlineData(95.0, 49, "SetTooSmall")]      // recall clears the percent but the set is under the floor size
    [InlineData(95.0, null, "SetTooSmall")]    // an unknown set size is not evidence
    public void Recall_IsJudgedAgainstTheFloor(double recall, int? setSize, string expected)
    {
        var result = OntologyAdminClassificationService.BuildRecall((decimal)recall, setSize, "2026-10-10");

        result.Status.Should().Be(expected);
        result.Recall.Should().Be((decimal)recall);
        result.LabelledSetSize.Should().Be(setSize);
        result.MeasuredOn.Should().Be("2026-10-10");
    }

    [Fact]
    public void ACategoryNeverMeasured_IsNotMeasured_WithNoFigures()
    {
        var result = OntologyAdminClassificationService.BuildRecall(null, 92, "2026-10-10");

        result.Status.Should().Be("NotMeasured");
        result.Recall.Should().BeNull();
        result.LabelledSetSize.Should().BeNull();
        result.MeasuredOn.Should().BeNull();
    }

    [Fact]
    public void TheFloor_IsEightyPercentOnFiftyItems_AndTheWindowThirtyDays()
    {
        OntologyAdminClassificationService.RecallFloor.Should().Be(new ClassificationRecallFloor(80, 50));
        OntologyAdminClassificationService.VolumeWindowDays.Should().Be(30);
    }

    // ── Which category ids a rule body names ──────────────────────────────────────────────────────────────────────

    private static readonly Guid Fee = Guid.Parse("8b62dd84-1fbc-f111-aaaf-3833c5e9614d");
    private static readonly Guid Scope = Guid.Parse("8d62dd84-1fbc-f111-aaaf-3833c5e9614d");

    [Fact]
    public void TheSeededRuleBody_NamesBothCategories()
    {
        // POL-COMMIT-BUDGET's body as seeded by task 004 (notes/004-seed-policy-rows.md), read back from spaarkedev1.
        const string body = """
            {"type":"Existence","subject":"sprk_matter","when":{},"all":[
              {"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{
                "sprk_triagecategory":["8b62dd84-1fbc-f111-aaaf-3833c5e9614d","8d62dd84-1fbc-f111-aaaf-3833c5e9614d"],
                "sprk_receiveddate":{">=":"now-30d"},"sprk_reviewoutcome":{"<>":100000003}}},
              {"notExists":"sprk_budgetrevision","path":"sprk_matter","filter":{"sprk_revisedon":{">=":"now-30d"}}}]}
            """;

        OntologyAdminClassificationReferences.GuidsIn(body).Should().BeEquivalentTo(new[] { Fee, Scope });
    }

    [Theory]
    [InlineData("""{"f":{"sprk_triagecategory":"8b62dd84-1fbc-f111-aaaf-3833c5e9614d"}}""")]
    [InlineData("""{"f":{"sprk_triagecategory":["8B62DD84-1FBC-F111-AAAF-3833C5E9614D"]}}""")]
    [InlineData("""{"f":{"sprk_triagecategory":"8b62dd841fbcf111aaaf3833c5e9614d"}}""")]
    [InlineData("""{"f":{"sprk_triagecategory":{"<>":"{8b62dd84-1fbc-f111-aaaf-3833c5e9614d}"}}}""")]
    [InlineData("""{"deeply":[[[{"nested":"8b62dd84-1fbc-f111-aaaf-3833c5e9614d"}]]]}""")]
    [InlineData("""{"f":{"sprk_triagecategory":["8b62dd84-1fbc-f111-aaaf-3833c5e9614d"]""")] // damaged JSON: scanned as text, fail closed
    [InlineData("""not json at all 8b62dd84-1fbc-f111-aaaf-3833c5e9614d""")]
    [InlineData("""not json at all 8b62dd841fbcf111aaaf3833c5e9614d""")]
    public void AnyFormatOfTheId_IsFound(string body) =>
        OntologyAdminClassificationReferences.GuidsIn(body).Should().Contain(Fee);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("""{"f":{"sprk_receiveddate":{">=":"now-30d"},"n":100000003,"x":"not-a-guid"}}""")]
    [InlineData("""{"f":"8b62dd84-1fbc-f111-aaaf"}""")]  // a truncated id is not an id
    public void ABodyThatNamesNoId_FindsNothing(string? body) =>
        OntologyAdminClassificationReferences.GuidsIn(body).Should().BeEmpty();

    // ── K3: a rule body that names a category by its NAME ─────────────────────────────────────────────────────────────

    private static readonly Dictionary<Guid, string> Categories = new()
    {
        [Fee] = "Fee / rate change",
        [Scope] = "Scope / budget change",
    };

    [Theory]
    [InlineData("""{"all":[{"filter":{"sprk_triagecategory":"Fee / rate change"}}]}""")]
    [InlineData("""{"all":[{"filter":{"sprk_triagecategory":["x","FEE / RATE CHANGE"]}}]}""")]   // case-insensitive, inside an array
    [InlineData("""{"all":[{"filter":{"sprk_triagecategory":{"=":"  Fee / rate change "}}}]}""")] // trimmed, inside an operator object
    [InlineData("{\"all\":[{\"filter\":{\"sprk_triagecategory\":\"Fee / rate change\"")]  // damaged JSON: scanned as text, fail closed
    public void ARuleBody_NamingACategoryByItsName_IsAReference(string body) =>
        OntologyAdminClassificationReferences.CategoriesNamedIn(body, Categories).Should().BeEquivalentTo(new[] { Fee });

    [Theory]
    [InlineData("""{"Fee / rate change":{"sprk_receiveddate":{">=":"now-30d"}}}""")]   // a property NAME is not a value
    [InlineData("""{"f":"Fee / rate change and more"}""")]                          // only a whole string value equals the name
    [InlineData("""{"f":"Fee"}""")]
    [InlineData(null)]
    [InlineData("   ")]
    public void ARuleBody_ThatDoesNotNameACategoryByName_IsNotAReference(string? body) =>
        OntologyAdminClassificationReferences.CategoriesNamedIn(body, Categories).Should().BeEmpty();

    [Fact]
    public void AnIdAndANameInOneBody_AreBothFound_AndEachCategoryOnce()
    {
        const string body = """{"a":"8b62dd84-1fbc-f111-aaaf-3833c5e9614d","b":"Scope / budget change","c":"Fee / rate change"}""";

        OntologyAdminClassificationReferences.CategoriesNamedIn(body, Categories).Should().BeEquivalentTo(new[] { Fee, Scope });
    }

    // ── K3: a recall measured before the category's last edit ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-10-11T08:00:00Z", "2026-10-10", true)]     // edited on a later day
    [InlineData("2026-10-10T23:59:59Z", "2026-10-10", false)]    // the measurement writes the row too: a same-day edit is not told apart
    [InlineData("2026-10-09T08:00:00Z", "2026-10-10", false)]    // edited before the measurement
    [InlineData("2026-10-11T08:00:00+00:00", "2026-10-10T00:00:00Z", true)]
    [InlineData(null, "2026-10-10", false)]                      // a date that cannot be read is never a claim
    [InlineData("2026-10-11T08:00:00Z", null, false)]
    [InlineData("not a date", "2026-10-10", false)]
    [InlineData("2026-10-11T08:00:00Z", "not a date", false)]
    public void ARecall_IsMarkedMeasuredBeforeTheLastEdit_OnlyWhenTheRowWasEditedOnALaterDay(string? modifiedOn, string? measuredOn, bool expected)
    {
        OntologyAdminClassificationService.EditedAfter(modifiedOn, measuredOn).Should().Be(expected);
        OntologyAdminClassificationService.BuildRecall(88.89m, 92, measuredOn, modifiedOn).MeasuredBeforeLastEdit.Should().Be(expected && measuredOn is not null);
    }

    [Fact]
    public void ACategoryNeverMeasured_IsNeverMarkedStale_HoweverRecentlyItWasEdited() =>
        OntologyAdminClassificationService.BuildRecall(null, null, null, "2026-10-12T00:00:00Z").MeasuredBeforeLastEdit.Should().BeFalse();

    // ── K2: the routing configuration is keyed by category NAME ───────────────────────────────────────────────────────

    [Fact]
    public void RoutingEntryNaming_FindsTheGlobalEntry_IgnoringCaseAndSpaces_WhateverTheSwitchSays()
    {
        var options = new CategoryRoutingOptions { Enabled = false };
        options.CategoryToTeam["  fee / RATE change "] = "Billing team";

        OntologyAdminClassificationService.RoutingEntryNaming(options, "Fee / rate change")
            .Should().Be("Communication:CategoryRouting:CategoryToTeam");
        OntologyAdminClassificationService.RoutingEntryNaming(options, "Scope / budget change").Should().BeNull();
    }

    [Fact]
    public void RoutingEntryNaming_FindsAPerTenantEntry_AndNamesTheTenant()
    {
        var options = new CategoryRoutingOptions();
        options.Tenants["tenant-a"] = new CategoryRoutingTenantOverride { Enabled = null, CategoryToTeam = null };
        options.Tenants["tenant-b"] = new CategoryRoutingTenantOverride
        {
            CategoryToTeam = new Dictionary<string, string> { ["Fee / rate change"] = "Team B" },
        };

        OntologyAdminClassificationService.RoutingEntryNaming(options, "Fee / rate change")
            .Should().Be("Communication:CategoryRouting:Tenants:tenant-b:CategoryToTeam");
    }

    [Fact]
    public void RoutingEntryNaming_IsNullForAnEmptyConfiguration() =>
        OntologyAdminClassificationService.RoutingEntryNaming(new CategoryRoutingOptions(), "Fee / rate change").Should().BeNull();

    // ── Validation ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidName_IsTrimmed_AndBlankGuidanceBecomesNull()
    {
        var errors = OntologyAdminClassificationService.Validate("  Settlement ", "   ", nameRequired: true, out var name, out var guidance);

        errors.Should().BeEmpty();
        name.Should().Be("Settlement");
        guidance.Should().BeNull();
    }

    [Fact]
    public void ALeftOutName_IsFineOnAnEdit_ButRequiredOnACreate()
    {
        OntologyAdminClassificationService.Validate(null, "g", nameRequired: false, out var editName, out _).Should().BeEmpty();
        editName.Should().BeNull();
        OntologyAdminClassificationService.Validate(null, "g", nameRequired: true, out _, out _).Should().ContainKey("name");
    }

    [Theory]
    [InlineData("", "name")]
    [InlineData("tab\there", "name")]
    [InlineData("carriage\rreturn", "name")]
    public void BadNames_AreRefused(string name, string field) =>
        OntologyAdminClassificationService.Validate(name, null, nameRequired: true, out _, out _).Should().ContainKey(field);

    [Fact]
    public void TheLengthLimits_AreInclusive()
    {
        OntologyAdminClassificationService.Validate(
            new string('n', OntologyAdminClassificationService.MaxNameLength),
            new string('g', OntologyAdminClassificationService.MaxGuidanceLength),
            nameRequired: true, out _, out _).Should().BeEmpty();
        OntologyAdminClassificationService.Validate(
            new string('n', OntologyAdminClassificationService.MaxNameLength + 1),
            new string('g', OntologyAdminClassificationService.MaxGuidanceLength + 1),
            nameRequired: true, out _, out _).Should().ContainKeys("name", "guidance");
    }

    // ── The refusal wording names the rule ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheRenameRefusal_NamesEveryRuleAndVersion_InAStableOrder()
    {
        var policy = Guid.NewGuid();
        var refs = new[]
        {
            new ClassificationRuleReference(policy, "POL-B", null, true, Guid.NewGuid(), 1, ClassificationVersionState.Earlier),
            new ClassificationRuleReference(policy, "POL-A", "A", true, Guid.NewGuid(), 2, ClassificationVersionState.InForce),
            new ClassificationRuleReference(policy, "POL-A", "A", true, Guid.NewGuid(), 3, ClassificationVersionState.Draft),
        };

        OntologyAdminClassificationService.DescribeBlockers(refs)
            .Should().Be("POL-A (version 3, draft), POL-A (version 2, in force), POL-B (version 1, earlier version)");
    }
}

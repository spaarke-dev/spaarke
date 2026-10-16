using FluentAssertions;
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

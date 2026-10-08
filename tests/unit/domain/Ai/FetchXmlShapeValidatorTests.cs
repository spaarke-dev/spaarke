using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai;

/// <summary>
/// ISS-018 (#1452, owner decision D-77): the one FetchXML list-shape check. Dataverse reads a list operator's values only
/// from <c>&lt;value&gt;</c> children; <c>operator="in" value="a,b"</c> is a condition with NO values and the query fails
/// ("The value passed for ConditionOperator.In is empty") — every notification playbook failed on it in dev for 89+ days.
/// The executor (rendered mode), the repo regression test and deploy lint C (authored mode) all run this class.
/// </summary>
public class FetchXmlShapeValidatorTests
{
    private const string A = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string B = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    private static string Fetch(string condition) =>
        $"<fetch top=\"50\"><entity name=\"sprk_event\"><filter type=\"or\">{condition}" +
        "<condition attribute=\"ownerid\" operator=\"eq\" value=\"" + A + "\"/></filter></entity></fetch>";

    // ── The defect (ISS-018): the comma form, in both modes ─────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommaValuedIn_IsRejected_NamingAttributeAndOperator(bool authored)
    {
        var problems = FetchXmlShapeValidator.Validate(
            Fetch($"<condition attribute=\"sprk_regardingmatter\" operator=\"in\" value=\"{A},{B}\"/>"), authored);

        problems.Should().Contain(p => p.Contains("sprk_regardingmatter") && p.Contains("operator=\"in\"") && p.Contains("value attribute"));
    }

    [Fact]
    public void AuthoredJoinIdsForm_IsRejected()
    {
        var problems = FetchXmlShapeValidator.Validate(
            Fetch("<condition attribute=\"sprk_regardingmatter\" operator=\"in\" value=\"{{joinIds myMatters.ids}}\"/>"),
            authoredTemplate: true);

        problems.Should().Contain(p => p.Contains("joinIds"));
        problems.Should().Contain(p => p.Contains("value attribute"));
    }

    [Theory]
    [InlineData("not-in")]
    [InlineData("contain-values")]
    [InlineData("not-contain-values")]
    public void OtherListOperators_WithAValueAttribute_AreRejected(string op)
    {
        FetchXmlShapeValidator.Validate(Fetch($"<condition attribute=\"x\" operator=\"{op}\" value=\"1,2\"/>"))
            .Should().Contain(p => p.Contains($"operator=\"{op}\""));
    }

    [Fact]
    public void Between_WithAValueAttribute_IsRejected()
    {
        FetchXmlShapeValidator.Validate(Fetch("<condition attribute=\"n\" operator=\"between\" value=\"6,20\"/>"))
            .Should().Contain(p => p.Contains("between") && p.Contains("value attribute"));
    }

    // ── Arity ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void In_WithZeroValueChildren_IsRejected()
    {
        FetchXmlShapeValidator.Validate(Fetch("<condition attribute=\"sprk_regardingmatter\" operator=\"in\"></condition>"))
            .Should().Contain(p => p.Contains("at least 1"));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void Between_NeedsExactlyTwoValues(int count, bool rejected)
    {
        var values = string.Concat(Enumerable.Range(1, count).Select(i => $"<value>{i}</value>"));
        var problems = FetchXmlShapeValidator.Validate(Fetch($"<condition attribute=\"n\" operator=\"between\">{values}</condition>"));
        (problems.Count > 0).Should().Be(rejected);
    }

    [Fact]
    public void An_EmptyValueChild_IsRejected()
    {
        FetchXmlShapeValidator.Validate(Fetch("<condition attribute=\"x\" operator=\"in\"><value></value></condition>"))
            .Should().Contain(p => p.Contains("empty <value>"));
    }

    // ── Positive controls: the shapes that must pass ────────────────────────────────────────

    [Fact]
    public void In_WithValueChildren_Passes()
    {
        FetchXmlShapeValidator.Validate(Fetch($"<condition attribute=\"x\" operator=\"in\"><value>{A}</value><value>{B}</value></condition>"))
            .Should().BeEmpty();
    }

    [Fact]
    public void TheImpossibleMatch_ForAnEmptyList_Passes()
    {
        FetchXmlShapeValidator.Validate(Fetch(
            "<condition attribute=\"x\" operator=\"in\"><value>00000000-0000-0000-0000-000000000000</value></condition>"))
            .Should().BeEmpty();
    }

    [Fact]
    public void ScalarOperators_WithAValueAttribute_Pass_EvenWithACommaInTheText()
    {
        FetchXmlShapeValidator.Validate(Fetch("<condition attribute=\"name\" operator=\"like\" value=\"%Smith, John%\"/>"))
            .Should().BeEmpty();
    }

    [Fact]
    public void AuthoredFetchInGuids_Passes_ButNotForBetween()
    {
        FetchXmlShapeValidator.Validate(
            Fetch("<condition attribute=\"x\" operator=\"in\">{{fetchInGuids myMatters.ids}}</condition>"), authoredTemplate: true)
            .Should().BeEmpty();
        FetchXmlShapeValidator.Validate(
            Fetch("<condition attribute=\"x\" operator=\"between\">{{fetchInGuids myMatters.ids}}</condition>"), authoredTemplate: true)
            .Should().NotBeEmpty();
    }

    [Fact]
    public void AuthoredFetchInGuids_UnderNotIn_IsRejected_TheEmptyListWouldMatchEveryRow()
    {
        FetchXmlShapeValidator.Validate(
            Fetch("<condition attribute=\"x\" operator=\"not-in\">{{fetchInGuids myMatters.ids}}</condition>"), authoredTemplate: true)
            .Should().Contain(p => p.Contains("operator=\"not-in\""));
    }

    [Fact]
    public void AuthoredEachBlock_InAList_IsRejected_UseTheHelper()
    {
        FetchXmlShapeValidator.Validate(
            Fetch("<condition attribute=\"x\" operator=\"in\">{{#each ids}}<value>{{this}}</value>{{/each}}</condition>"),
            authoredTemplate: true)
            .Should().Contain(p => p.Contains("fetchInGuids"));
    }

    [Fact]
    public void AuthoredScalarTemplates_Pass()
    {
        FetchXmlShapeValidator.Validate(
            Fetch("<condition attribute=\"sprk_duedate\" operator=\"lt\" value=\"{{todayUtc}}\"/>"), authoredTemplate: true)
            .Should().BeEmpty();
    }

    // ── Rendered mode only ──────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderedText_WithALeftoverTemplate_IsRejected()
    {
        FetchXmlShapeValidator.Validate(Fetch("<condition attribute=\"sprk_duedate\" operator=\"lt\" value=\"{{todayUtc}}\"/>"))
            .Should().Contain(p => p.Contains("unrendered template"));
    }

    [Fact]
    public void MalformedXml_IsRejected()
    {
        FetchXmlShapeValidator.Validate("<fetch><entity name=\"x\">").Should().Contain(p => p.Contains("not well-formed"));
    }
}

using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Ontology R1 task 227 (#1848): every string the decision wizard shows from the catalog is addressed to the person deciding.
/// A project decision id ("(D-18)") or a description of the product from outside it ("the user picks") is developer text. The
/// wizard shows catalog copy as it arrives (it never rewrites it), so the catalog is the only place this can be held.
/// </summary>
/// <remarks>Maintain-class (ADR-038, <c>tests/unit/domain/**</c>): reads the catalog's own data, no fakes.</remarks>
[Trait("status", "new")]
public class DecisionCatalogUserTextTests
{
    private static readonly Regex DeveloperWording = new(
        @"\(D-\d|\bD-\d+\b|\bthe user\b|\bFR-\d|\bNFR-\d|\b[Tt]ask \d{2,3}\b",
        RegexOptions.CultureInvariant);

    /// <summary>Every user-visible string of every catalog definition: labels, parameter labels, hints, option labels, effect lines.</summary>
    private static IEnumerable<(string Where, string Text)> UserVisibleStrings()
    {
        var definitions = DecisionActionCatalog.All
            .Concat(DecisionActionCatalog.BillingActions)
            .Concat(DecisionActionCatalog.RecordProcedureActions);

        foreach (var action in definitions)
        {
            yield return ($"{action.Code}.Label", action.Label);
            for (var i = 0; i < action.EffectLines.Count; i++)
            {
                yield return ($"{action.Code}.EffectLines[{i}]", action.EffectLines[i]);
            }

            foreach (var parameter in action.Parameters)
            {
                yield return ($"{action.Code}.{parameter.Code}.Label", parameter.Label);
                if (parameter.Hint is not null)
                {
                    yield return ($"{action.Code}.{parameter.Code}.Hint", parameter.Hint);
                }

                foreach (var option in parameter.Options ?? [])
                {
                    yield return ($"{action.Code}.{parameter.Code}.Option[{option.Value}]", option.Label);
                }
            }
        }

        foreach (var lane in Enum.GetValues<DecisionLane>())
        {
            foreach (var reason in DecisionActionCatalog.DismissalReasons(lane))
            {
                yield return ($"dismissal[{lane}].{reason.Code}.Label", reason.Label);
            }
        }
    }

    [Fact]
    public void NoUserVisibleCatalogString_CarriesADecisionIdOrDeveloperWording()
    {
        var strings = UserVisibleStrings().ToList();
        strings.Should().NotBeEmpty("the scan must reach the catalog's strings (must-fire control)");

        strings.Where(s => DeveloperWording.IsMatch(s.Text)).Select(s => $"{s.Where}: {s.Text}")
            .Should().BeEmpty("the wizard shows catalog copy as it arrives, so it is written for the person deciding");
    }

    [Fact]
    public void ReviseBudgetBudgetHint_IsAddressedToTheDecider()
    {
        DecisionActionCatalog.TryGet("revise-budget", out var action).Should().BeTrue();
        var hint = action.Parameters.Single(p => p.Code == "budget").Hint;

        hint.Should().Be("Chosen automatically when the matter has one budget; pick the budget to revise when it has several.");
    }

    [Theory]
    [InlineData("Pre-selected when the matter has one budget; the user picks one when it has several (D-18)")]
    [InlineData("Shown to the user (D-7)")]
    [InlineData("Per FR-49 the wizard asks")]
    public void TheScan_FiresOnDeveloperWording(string sample) => DeveloperWording.IsMatch(sample).Should().BeTrue();

    [Theory]
    [InlineData("Chosen automatically when the matter has one budget; pick the budget to revise when it has several.")]
    [InlineData("Records a budget revision on the matter")]
    [InlineData("Copied recipients; each sees the other recipients")]
    public void TheScan_DoesNotFireOnCleanCopy(string sample) => DeveloperWording.IsMatch(sample).Should().BeFalse();
}

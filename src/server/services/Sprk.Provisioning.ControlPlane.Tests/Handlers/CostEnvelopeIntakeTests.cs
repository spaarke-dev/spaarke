// -----------------------------------------------------------------------------
// CostEnvelopeIntakeTests.cs — task 229: the ONE rule set POST /api/runs and H0 apply to the cost tier + estimate.
// The endpoint and H0 tests cover the wiring; these pin the rule's edges (exact-case tiers; the estimate is exactly the
// skill's ^[0-9]+(\.[0-9]+)?$ — no sign, separator, exponent, whitespace or bare leading/trailing '.').
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.Preflight;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class CostEnvelopeIntakeTests
{
    [Theory]
    [InlineData("smb", "0", 0)]
    [InlineData("enterprise", "450", 450)]
    [InlineData("dedicated", "1200.50", 1200.50)]
    public void Validate_AnAcceptedTierAndAPlainEstimate_IsValid(string tier, string estimate, double expected)
    {
        var outcome = CostEnvelopeIntake.Validate(tier, estimate);

        outcome.Should().Be(new CostEnvelopeIntakeOutcome.Valid(tier, (decimal)expected));
    }

    [Theory]
    [InlineData(" 450")]
    [InlineData("450 ")]
    [InlineData(".5")]
    [InlineData("5.")]
    [InlineData("+5")]
    [InlineData("-5")]
    [InlineData("1e3")]
    [InlineData("1,200")]
    [InlineData("١٢٣")]                                  // non-ASCII digits
    [InlineData("99999999999999999999999999999999")]     // beyond decimal
    public void Validate_AnEstimateTheSkillWouldRefuse_IsRefused(string estimate)
    {
        var outcome = CostEnvelopeIntake.Validate("smb", estimate);

        outcome.Should().BeOfType<CostEnvelopeIntakeOutcome.Invalid>()
            .Which.RejectionCode.Should().Be(CostEnvelopeIntake.InvalidEstimateRejectionCode);
    }

    [Theory]
    [InlineData("shared-trial")]
    [InlineData("SMB")]
    [InlineData("standard")]
    public void Validate_ATierWithoutACeiling_IsRefused(string tier)
    {
        var outcome = CostEnvelopeIntake.Validate(tier, "450");

        outcome.Should().BeOfType<CostEnvelopeIntakeOutcome.Invalid>()
            .Which.RejectionCode.Should().Be(CostEnvelopeIntake.UnknownTierRejectionCode);
    }

    [Fact]
    public void Tiers_AreTheThreeBudgetClassesOfADedicatedStamp()
    {
        CostEnvelopeIntake.Tiers.Should().BeEquivalentTo(new[] { "smb", "enterprise", "dedicated" },
            "every stamp is dedicated since D-12; the shared-trial tier is retired (T229)");
    }
}

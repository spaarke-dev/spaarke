using FluentAssertions;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 084e: <see cref="RulePrecedence.Resolve{T}"/>, the one precedence function the evaluator (step 4) and the record's rules-in-force
/// read share (ontology-r1 R1-R3, O-1 to O-4). The versions are plain records with value equality, as <see cref="BillingRuleCandidate"/> is.
/// </summary>
public class RulePrecedenceTests
{
    private static readonly ProcedureRank AllRecords = new(ProcedureLevel.AllRecords, 0, FirmSpecific: false);
    private static readonly ProcedureRank Firm = new(ProcedureLevel.Firm, 0, FirmSpecific: true);
    private static readonly ProcedureRank ManualOwn = new(ProcedureLevel.ManualOwn, 0, FirmSpecific: false);

    private sealed record V(string Name, ProcedureRank Rank, bool Cap = true, bool Valid = true);

    /// <summary>Resolves and records every delegate call, as (delegate, version name).</summary>
    private static (PrecedenceResult<V> Result, List<string> Calls) Resolve(BillingRulePrecedence precedence, params V[] versions)
    {
        var calls = new List<string>();
        var result = RulePrecedence.Resolve(
            precedence, versions, v => v.Rank,
            v => { calls.Add("cap " + v.Name); return v.Cap; },
            v => { calls.Add("valid " + v.Name); return v.Valid; });
        return (result, calls);
    }

    private static IEnumerable<(string, PrecedenceOutcome)> Outcomes(PrecedenceResult<V> result) => result.Outcomes.Select(o => (o.Version.Name, o.Outcome));

    [Fact(DisplayName = "084e must-fire (Replace): a valid higher version displaces the lower one outright; it is the Top and the only displacing version")]
    public void Replace_ValidHigher_DisplacesTheLowerOutright()
    {
        var (result, _) = Resolve(BillingRulePrecedence.Replace, new V("lower", AllRecords), new V("higher", Firm));

        Outcomes(result).Should().Equal(("lower", PrecedenceOutcome.DisplacedOutright), ("higher", PrecedenceOutcome.Applies));
        result.Top.Should().Be(Firm);
        result.Displacing.Select(v => v.Name).Should().Equal("higher");
    }

    [Fact(DisplayName = "084e must-not-fire (R1, Replace): an invalid higher version displaces nothing; the Top is the highest rank holding a VALID version")]
    public void Replace_InvalidHigher_LeavesTheLowerInForce()
    {
        var (result, _) = Resolve(BillingRulePrecedence.Replace, new V("lower", AllRecords), new V("higher", Firm, Valid: false));

        Outcomes(result).Should().Equal(("lower", PrecedenceOutcome.Applies), ("higher", PrecedenceOutcome.Applies));
        result.Top.Should().Be(AllRecords, "never the plain top rank");
        result.Displacing.Select(v => v.Name).Should().Equal("lower");
    }

    [Fact(DisplayName = "084e must-fire (R2, Split): a capped valid higher version makes the lower one CapDisplaced, not displaced outright")]
    public void Split_CappedHigher_CapDisplacesTheLower()
    {
        var (result, _) = Resolve(BillingRulePrecedence.Split, new V("lower", AllRecords), new V("higher", Firm, Cap: true));

        Outcomes(result).Should().Equal(("lower", PrecedenceOutcome.CapDisplaced), ("higher", PrecedenceOutcome.Applies));
        result.Displacing.Select(v => v.Name).Should().Equal("higher");
    }

    [Fact(DisplayName = "084e must-not-fire (R2, Split): a capless higher version leaves the lower cap in force, and its validity is never asked")]
    public void Split_CaplessHigher_LeavesTheLowerCap_AndIsNotAskedForValidity()
    {
        var (result, calls) = Resolve(BillingRulePrecedence.Split, new V("lower", AllRecords), new V("higher", Firm, Cap: false));

        Outcomes(result).Should().Equal(("lower", PrecedenceOutcome.Applies), ("higher", PrecedenceOutcome.Applies));
        result.Top.Should().Be(AllRecords);
        calls.Should().Equal("cap higher", "cap lower", "valid lower");
    }

    [Fact(DisplayName = "084e must-not-fire (R2, Additive): nothing is displaced at any rank, and no delegate is called")]
    public void Additive_NothingDisplaced_NothingCalled()
    {
        var (result, calls) = Resolve(BillingRulePrecedence.Additive, new V("lower", AllRecords), new V("higher", ManualOwn));

        Outcomes(result).Should().OnlyContain(o => o.Item2 == PrecedenceOutcome.Applies);
        result.Top.Should().BeNull();
        result.Displacing.Should().BeEmpty();
        calls.Should().BeEmpty();
    }

    [Fact(DisplayName = "084e (R2, R3, O-3): a tie at the Top is never marked, both are listed as displacing (input order), the lower one is displaced")]
    public void TieAtTop_BothApply_BothDisplace()
    {
        // Two equal ranks built separately: a tie is ProcedureRank's own equality (O-3).
        var (result, _) = Resolve(
            BillingRulePrecedence.Replace,
            new V("b", new ProcedureRank(ProcedureLevel.Firm, 0, true)), new V("lower", AllRecords), new V("a", new ProcedureRank(ProcedureLevel.Firm, 0, true)));

        Outcomes(result).Should().Equal(("b", PrecedenceOutcome.Applies), ("lower", PrecedenceOutcome.DisplacedOutright), ("a", PrecedenceOutcome.Applies));
        result.Displacing.Select(v => v.Name).Should().Equal("b", "a");
    }

    [Fact(DisplayName = "084e (O-1): valid and statesCap are never called on a version ranked below the Top; each version is asked at most once")]
    public void BelowTop_IsNeverAsked_AndNothingIsAskedTwice()
    {
        var (result, calls) = Resolve(
            BillingRulePrecedence.Replace,
            new V("lowest", AllRecords), new V("broken", ManualOwn, Valid: false), new V("top", Firm), new V("tied", Firm), new V("lower", new(ProcedureLevel.Firm, 1, true)));

        result.Top.Should().Be(Firm);
        calls.Should().Equal("valid broken", "valid top", "valid tied");
        Outcomes(result).Should().Equal(
            ("lowest", PrecedenceOutcome.DisplacedOutright), ("broken", PrecedenceOutcome.Applies), ("top", PrecedenceOutcome.Applies),
            ("tied", PrecedenceOutcome.Applies), ("lower", PrecedenceOutcome.DisplacedOutright));
    }

    [Fact(DisplayName = "084e (O-6 must-not-fire): a version above the Top that does not displace (invalid, or capless under Split) Applies")]
    public void AboveTop_NotDisplacing_Applies()
    {
        var (split, _) = Resolve(BillingRulePrecedence.Split, new V("capless", ManualOwn, Cap: false), new V("capped", Firm), new V("lower", AllRecords));

        Outcomes(split).Should().Equal(
            ("capless", PrecedenceOutcome.Applies), ("capped", PrecedenceOutcome.Applies), ("lower", PrecedenceOutcome.CapDisplaced));
        split.Displacing.Select(v => v.Name).Should().Equal("capped");
    }

    [Fact(DisplayName = "084e (O-2): outcomes are a list in input order; value-equal versions both appear (no dictionary keyed by the version)")]
    public void Outcomes_AreInInputOrder_AndKeepDuplicates()
    {
        var same = new V("same", AllRecords);

        var (result, _) = Resolve(BillingRulePrecedence.Replace, same, new V("higher", Firm), same);

        result.Outcomes.Should().HaveCount(3);
        Outcomes(result).Should().Equal(
            ("same", PrecedenceOutcome.DisplacedOutright), ("higher", PrecedenceOutcome.Applies), ("same", PrecedenceOutcome.DisplacedOutright));
    }

    [Fact(DisplayName = "084e: no version passes, so there is no Top and everything applies")]
    public void NoValidVersion_NoTop_AllApply()
    {
        var (result, _) = Resolve(BillingRulePrecedence.Replace, new V("a", AllRecords, Valid: false), new V("b", Firm, Valid: false));

        result.Top.Should().BeNull();
        result.Displacing.Should().BeEmpty();
        Outcomes(result).Should().OnlyContain(o => o.Item2 == PrecedenceOutcome.Applies);
    }
}

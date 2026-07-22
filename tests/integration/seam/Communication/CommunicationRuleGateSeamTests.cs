using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Vertical-slice seam (ADR-038 <c>tests/integration/seam/**</c> / spec FR-12) for
/// <see cref="CommunicationRuleGate"/> — the comms policy gate. Exercises the REAL match + confidence-threshold
/// evaluation over the dedicated <c>sprk_communicationrule</c> table (owner-chosen Path B), doubling only the
/// Dataverse module boundary (<see cref="IGenericEntityService"/>). Covers all four required decision branches:
/// (a) rule match + confidence ≥ threshold → authorize; (b) no matching rule → deny; (c) rule match + confidence
/// below threshold → deny; (d) the privilege flag is present on every decision and the gate has NO dependency on
/// <c>IEventRulesService</c> (FireAsync-never is structural — the gate's ctor takes only IGenericEntityService +
/// IOptions + ILogger). Placement rationale: the gate composes a table read (boundary) + branchy evaluation, so a
/// seam test over real rule rows through the boundary matches the 023/024/040 precedent (POML step 6).
/// </summary>
public sealed class CommunicationRuleGateSeamTests
{
    private static CommunicationRuleGate Gate(double defaultThreshold, params DataverseEntity[] rules)
    {
        var entity = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entity
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationrule"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(rules.ToList()));

        var options = Options.Create(new CommsPolicyOptions { DefaultConfidenceThreshold = defaultThreshold });
        return new CommunicationRuleGate(entity.Object, options, NullLogger<CommunicationRuleGate>.Instance);
    }

    private static DataverseEntity Rule(
        Guid id, Guid? matterId, decimal? threshold, bool privilege = false, int priority = 500)
    {
        var e = new DataverseEntity("sprk_communicationrule", id)
        {
            ["sprk_enabled"] = true,
            ["sprk_flagprivilege"] = privilege,
            ["sprk_priority"] = priority,
        };
        if (matterId.HasValue)
        {
            e["sprk_matter"] = new EntityReference("sprk_matter", matterId.Value);
        }
        if (threshold.HasValue)
        {
            e["sprk_confidencethreshold"] = threshold.Value;
        }
        return e;
    }

    private static CommunicationRuleDecisionRequest Request(Guid? matterId, double confidence) =>
        new(Guid.NewGuid(), Tenant: null, MatterId: matterId, Confidence: confidence);

    // (a) rule match (matter) + confidence ≥ per-rule threshold → AUTHORIZE
    [Fact]
    public async Task Evaluate_MatterRuleMatched_ConfidenceAtOrAboveThreshold_Authorizes()
    {
        var matter = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var gate = Gate(0.8, Rule(ruleId, matter, threshold: 0.7m, privilege: false));

        var decision = await gate.EvaluateAsync(Request(matter, confidence: 0.9));

        decision.Authorize.Should().BeTrue();
        decision.MatchedRuleId.Should().Be(ruleId);
        decision.Threshold.Should().Be(0.7);
        decision.PrivilegeFlagged.Should().BeFalse("the matched rule does not flag privilege");
        decision.Reason.Should().Be("rule-matched-confidence-met");
    }

    // (b) no matching rule → DENY (logged no-action)
    [Fact]
    public async Task Evaluate_NoMatchingRule_Denies()
    {
        var otherMatter = Guid.NewGuid();
        var gate = Gate(0.8, Rule(Guid.NewGuid(), otherMatter, threshold: 0.5m));

        var decision = await gate.EvaluateAsync(Request(Guid.NewGuid(), confidence: 0.99));

        decision.Authorize.Should().BeFalse();
        decision.MatchedRuleId.Should().BeNull();
        decision.Reason.Should().Be("no-matching-rule");
        decision.PrivilegeFlagged.Should().BeFalse("no rule matched, so nothing is privilege-flagged");
    }

    // (c) rule match + confidence below threshold → DENY (logged no-action), privilege carried from the matched rule
    [Fact]
    public async Task Evaluate_MatterRuleMatched_ConfidenceBelowThreshold_Denies()
    {
        var matter = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var gate = Gate(0.8, Rule(ruleId, matter, threshold: 0.8m, privilege: true));

        var decision = await gate.EvaluateAsync(Request(matter, confidence: 0.5));

        decision.Authorize.Should().BeFalse();
        decision.MatchedRuleId.Should().Be(ruleId);
        decision.Threshold.Should().Be(0.8);
        decision.Reason.Should().Be("rule-matched-confidence-below-threshold");
        decision.PrivilegeFlagged.Should().BeTrue("the matched rule flags privilege — surfaced on the deny path too (ADR-015)");
    }

    // Default-threshold fallback + all-matter rule (blank sprk_matter) + privilege on authorize
    [Fact]
    public async Task Evaluate_AllMatterRuleNoThreshold_UsesDefaultThreshold_AuthorizesAndFlagsPrivilege()
    {
        var ruleId = Guid.NewGuid();
        // matterId null = "all matters"; threshold null = fall back to the CommsPolicyOptions default (0.8).
        var gate = Gate(0.8, Rule(ruleId, matterId: null, threshold: null, privilege: true));

        var decision = await gate.EvaluateAsync(Request(Guid.NewGuid(), confidence: 0.85));

        decision.Authorize.Should().BeTrue("0.85 ≥ the default threshold 0.8");
        decision.MatchedRuleId.Should().Be(ruleId);
        decision.Threshold.Should().Be(0.8, "the rule set no threshold, so the CommsPolicyOptions default applies");
        decision.PrivilegeFlagged.Should().BeTrue("privilege is flagged on the authorize path, never auto-decided (ADR-015)");
    }

    // Lower sprk_priority wins when several rules match.
    [Fact]
    public async Task Evaluate_MultipleMatchingRules_LowestPriorityWins()
    {
        var matter = Guid.NewGuid();
        var winner = Guid.NewGuid();
        var loser = Guid.NewGuid();
        var gate = Gate(0.8,
            Rule(loser, matter, threshold: 0.9m, priority: 200),
            Rule(winner, matter, threshold: 0.5m, priority: 100)); // lower priority → wins

        var decision = await gate.EvaluateAsync(Request(matter, confidence: 0.6));

        decision.MatchedRuleId.Should().Be(winner);
        decision.Authorize.Should().BeTrue("the winning rule's 0.5 threshold is met by 0.6");
        decision.Threshold.Should().Be(0.5);
    }
}

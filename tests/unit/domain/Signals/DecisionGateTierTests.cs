using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Task 043 (D-52): the gate tier a decision records comes from a PURE function behind the AI PublicContracts facade, from each
/// catalog action's declared risk. Maintain-class (ADR-038 <c>tests/unit/domain/**</c>): pure mapping, no I/O.
/// </summary>
public class DecisionGateTierTests
{
    [Fact]
    public void EveryCatalogAction_DeclaresAGate()
    {
        foreach (var action in DecisionActionCatalog.All)
            action.Gate.Should().NotBeNull($"{action.Code}: an action without a declared gate is refused by the commit route, never defaulted");
    }

    [Theory]
    [InlineData("send-budget-inquiry", GateRiskTier.Tier4)]
    [InlineData("send-reminder", GateRiskTier.Tier4)]
    [InlineData("send-email", GateRiskTier.Tier4)]
    [InlineData("revise-budget", GateRiskTier.Tier2b)]
    [InlineData("approve-variance", GateRiskTier.Tier2b)]
    [InlineData("mark-complete", GateRiskTier.Tier2b)]
    [InlineData("record-the-response", GateRiskTier.Tier2b)]
    [InlineData("reschedule", GateRiskTier.Tier3)]
    [InlineData("extend-response-date", GateRiskTier.Tier3)]
    [InlineData("reassign", GateRiskTier.Tier3)]
    [InlineData("add-todo", GateRiskTier.Tier3)]
    [InlineData("create-event", GateRiskTier.Tier3)]
    [InlineData("assign-work", GateRiskTier.Tier3)]
    [InlineData("assign-task", GateRiskTier.Tier3)]
    public void EachAction_ResolvesToItsTier(string code, GateRiskTier expected)
    {
        DecisionActionCatalog.TryGet(code, out var action).Should().BeTrue();
        DecisionGateTier.Resolve(action.Gate!).Should().Be(expected);
    }

    [Fact]
    public void AnUnderDeclaredTier_IsRaisedByItsFactors_NeverLowered()
    {
        // The chat gate's own rule: an external send is Tier4 whatever is declared.
        DecisionGateTier.Resolve(new DecisionGateFactors(GateRiskTier.Tier0, Reversible: true, ExternallyVisible: true))
            .Should().Be(GateRiskTier.Tier4);
        DecisionGateTier.Resolve(new DecisionGateFactors(GateRiskTier.Tier3, Reversible: true))
            .Should().Be(GateRiskTier.Tier3, "a declared tier above its factors stands");
    }

    [Fact]
    public void Strictest_IsBySeverity_AndNullWhenNothingWasTaken()
    {
        DecisionGateTier.Strictest([GateRiskTier.Tier2b, GateRiskTier.Tier4, GateRiskTier.Tier3]).Should().Be(GateRiskTier.Tier4);
        DecisionGateTier.Strictest([GateRiskTier.Tier2c, GateRiskTier.Tier2b]).Should().Be(GateRiskTier.Tier2c);
        DecisionGateTier.Strictest([]).Should().BeNull();
    }

    [Fact]
    public void TheRecordedText_IsTheTierToken()
    {
        DecisionGateTier.ToText(GateRiskTier.Tier4).Should().Be("Tier4");
        DecisionGateTier.ToText(GateRiskTier.Tier2b).Should().Be("Tier2b");
    }

    [Fact]
    public void OnlyTheInquiryAndTheReminderLeaveTheItemOpen_AndOnlyTheMessagesSend()
    {
        // D-119: the inquiry is an intermediate step; D-128 Q18: so is the Do-lane reminder. Everything else resolves when taken.
        DecisionActionCatalog.All.Where(a => !a.ResolvesSignal).Select(a => a.Code).Should().Equal("send-budget-inquiry", "send-reminder");
        DecisionActionCatalog.All.Where(a => a.SendsMessage).Select(a => a.Code)
            .Should().BeEquivalentTo("send-budget-inquiry", "send-reminder", "send-email");
    }

    [Fact]
    public void TheCommitService_TakesNoChatGateOrSessionType()
    {
        // D-52: no chat session and no ConfirmationPolicyEngine state; only the PublicContracts facade from the AI module.
        // The one other allowance is the identity service (Ai.Membership: user -> linked contact, task 141), which is neither a chat
        // gate nor a session; the commit records the deciding user's contact on the Decision Record (D-129).
        var parameters = typeof(Sprk.Bff.Api.Services.Signals.DecisionCommitService).GetConstructors()
            .SelectMany(c => c.GetParameters()).Select(p => p.ParameterType.Namespace ?? string.Empty);
        parameters.Should().NotContain(ns => ns.StartsWith("Sprk.Bff.Api.Services.Ai", StringComparison.Ordinal)
                                             && ns != "Sprk.Bff.Api.Services.Ai.PublicContracts"
                                             && ns != "Sprk.Bff.Api.Services.Ai.Membership");
    }
}

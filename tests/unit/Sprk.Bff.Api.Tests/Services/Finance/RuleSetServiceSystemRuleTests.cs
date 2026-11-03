using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.RuleSetServiceTests;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 049, D-125: the system policy (rule type SubmissionIntegrity) is never edited, accepted, rejected, given a new
/// version or published through <see cref="RuleSetService"/>. It lives in no rule set in production, so these paths cannot reach it;
/// a row that is put in one anyway is refused, and a control proves an ordinary rule is unaffected. An administrator may still turn it
/// off (<c>sprk_enabled</c>), which is not a rule-set operation.
/// </summary>
public sealed class RuleSetServiceSystemRuleTests
{
    private static readonly Guid CallerOid = Guid.NewGuid();
    private static readonly DateOnly Effective = new(2026, 11, 1);

    private static (Guid Policy, Guid Version) SystemRule(Harness h, Guid set, ProposalState state) =>
        h.Rule(set, "BILL-SYSTEM-SUBMISSION", state, type: nameof(RuleType.SubmissionIntegrity), body: "{}",
            consequence: (int)BillingConsequence.Flag);

    [Fact]
    public async Task Edit_OfASystemRule_IsRefused_WithTheSystemRuleCode_AndWritesNothing()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, _) = SystemRule(h, set, ProposalState.Accepted);

        var outcome = await h.Service().EditAsync(
            set, policy, new ProposalEdit(null, null, null, "A new title", null, null, null, CallerOid), default);

        outcome.ErrorCode.Should().Be(RuleSetService.CodeSystemRule);
        outcome.StatusCode.Should().Be(409);
        h.Writes.Should().BeEmpty("no new version is created and the draft is not updated");
    }

    [Fact]
    public async Task Accept_AndReject_OfASystemRule_AreRefused_AndWriteNothing()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, _) = SystemRule(h, set, ProposalState.Proposed);
        var service = h.Service();

        var accepted = await service.DecideAsync(set, policy, true, h.WorkingToken(policy), default);
        var rejected = await service.DecideAsync(set, policy, false, h.WorkingToken(policy), default, "Not applicable");

        accepted.ErrorCode.Should().Be(RuleSetService.CodeSystemRule);
        rejected.ErrorCode.Should().Be(RuleSetService.CodeSystemRule);
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_OfASetHoldingAnAcceptedSystemRule_IsRefused_AndWritesNothing()
    {
        var h = new Harness();
        var set = h.RuleSet();
        SystemRule(h, set, ProposalState.Accepted);

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.Errors.Should().ContainSingle().Which.Reason.Should().Be(OntologyWriterFailureReason.RuleTypeUnsupported);
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task AnOrdinaryRule_IsStillEditable_MustNotFire()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, _) = h.Rule(set, "BILL-A", ProposalState.Proposed);

        var outcome = await h.Service().EditAsync(
            set, policy, new ProposalEdit(null, null, null, "A new title", null, null, null, CallerOid), default);

        outcome.ErrorCode.Should().NotBe(RuleSetService.CodeSystemRule);
        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done);
    }
}

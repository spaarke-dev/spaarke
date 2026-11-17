using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ISS-032 (#2883): the approval state (<see cref="InvoiceApprovalService.GetAsync"/>) reports a reject, return or approve that moved the
/// invoice's status but whose Decision Record was never written (<see cref="InvoiceApprovalStateDto.IncompleteDecision"/>), to the caller whose
/// repeat finishes it, over the in-memory Dataverse (<see cref="ApprovalWorld"/>) with the REAL resolver, recorder and writer harness. The
/// strands are made the way the decision services' own tests make them (a fault after the transition).
/// </summary>
public sealed class InvoiceDecisionIncompleteHintTests
{
    public enum Verb { Reject, Return, Approve }

    private static ApprovalWorld World(Verb verb)
    {
        var w = new ApprovalWorld();
        if (verb == Verb.Approve)
        {
            w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        }

        return w;
    }

    /// <summary>The decision as Alice's (or <paramref name="user"/>'s) call; true when it finished.</summary>
    private static async Task<bool> Decide(ApprovalWorld w, Verb verb, Guid user) => verb switch
    {
        Verb.Reject => (await w.RejectAs(user)).Outcome is InvoiceDecisionOutcome.Decided or InvoiceDecisionOutcome.AlreadyDecided,
        Verb.Return => (await w.ReturnAs(user)).Outcome is InvoiceDecisionOutcome.Decided or InvoiceDecisionOutcome.AlreadyDecided,
        _ => (await w.ApproveAs(user)).Outcome is ApprovalOutcome.Approved or ApprovalOutcome.AlreadyApproved,
    };

    private static int TargetStatusOf(Verb verb) => verb switch
    {
        Verb.Reject => InvoiceStatusCodes.Rejected,
        Verb.Return => InvoiceStatusCodes.ReturnedToFirm,
        _ => InvoiceStatusCodes.Approved,
    };

    private static string ActionOf(Verb verb) => verb switch
    {
        Verb.Reject => "reject-invoice",
        Verb.Return => "return-to-firm",
        _ => "approve-invoice",
    };

    /// <summary>Alice's decision, stranded after its transition by a fault at <paramref name="step"/>.</summary>
    private static async Task Strand(ApprovalWorld w, Verb verb, string step)
    {
        if (step == "transition-after-commit")
        {
            w.TransitionThrowsAfterCommit = true;
        }
        else
        {
            // "identity" is the recorder's read of the decider's contact: the record is refused, nothing is written.
            w.FaultsOnce[step] = new InvalidOperationException("dataverse down");
        }

        (await Decide(w, verb, AliceUser)).Should().BeFalse("the call stopped after its transition");
        w.InvoiceStatus.Should().Be(TargetStatusOf(verb), "the transition happened");
        w.TransitionThrowsAfterCommit = false;
    }

    public static TheoryData<Verb, string, string> BeforeTheRecord => new()
    {
        { Verb.Reject, "portion", "portion" },
        { Verb.Reject, "identity", "record" },
        { Verb.Reject, "transition-after-commit", "portion" },
        { Verb.Return, "portion", "portion" },
        { Verb.Return, "identity", "record" },
        { Verb.Return, "transition-after-commit", "portion" },
        { Verb.Approve, "portion", "portion" },
        { Verb.Approve, "identity", "record" },
        { Verb.Approve, "transition-after-commit", "portion" },
    };

    public static TheoryData<Verb> Verbs => new() { Verb.Reject, Verb.Return, Verb.Approve };

    // ── Must fire ──

    [Theory(DisplayName = "ISS-032: a transition with no Decision Record reports incompleteDecision with the verb and the step to the claimant; their repeat finishes it and the hint goes")]
    [MemberData(nameof(BeforeTheRecord))]
    public async Task StrandedBeforeTheRecord_IsReported_AndTheRepeatClearsIt(Verb verb, string step, string failedStep)
    {
        var w = World(verb);
        await Strand(w, verb, step);
        w.RecordsOf(ActionOf(verb)).Should().BeEmpty();

        var state = (await w.GetAs(AliceUser))!;

        state.IncompleteDecision.Should().Be(new InvoiceIncompleteDecisionDto(verb.ToString(), failedStep));

        // The same repeat the open wizard would send finishes it: one record, and the hint is gone.
        (await Decide(w, verb, AliceUser)).Should().BeTrue();
        w.RecordsOf(ActionOf(verb)).Should().ContainSingle();
        (await w.GetAs(AliceUser))!.IncompleteDecision.Should().BeNull("the decision now has its record");
    }

    [Theory(DisplayName = "ISS-032: a reject or return whose claimant is no longer an eligible approver is still hinted (their repeat does not re-check eligibility)")]
    [InlineData(Verb.Reject)]
    [InlineData(Verb.Return)]
    public async Task RejectReturn_ClaimantNoLongerEligible_IsStillHinted(Verb verb)
    {
        var w = World(verb);
        await Strand(w, verb, "identity");
        w.Core["sprk_assignedattorney1"] = new EntityReference("contact", Carol);

        var state = (await w.GetAs(AliceUser))!;

        state.CallerEligible.Should().BeFalse();
        state.IncompleteDecision.Should().Be(new InvoiceIncompleteDecisionDto(verb.ToString(), "record"));
        (await Decide(w, verb, AliceUser)).Should().BeTrue("the repeat the hint offers finishes it");
    }

    // ── Must not fire ──

    [Theory(DisplayName = "ISS-032 (must-not-fire): a decision with its record reports no incompleteDecision - complete, and stranded after the record ('link')")]
    [MemberData(nameof(Verbs))]
    public async Task DecisionWithItsRecord_ReportsNone(Verb verb)
    {
        var complete = World(verb);
        (await Decide(complete, verb, AliceUser)).Should().BeTrue();
        (await complete.GetAs(AliceUser))!.IncompleteDecision.Should().BeNull();

        var afterRecord = World(verb);
        await Strand(afterRecord, verb, "link");
        afterRecord.RecordsOf(ActionOf(verb)).Should().ContainSingle("the record was written before the link failed");
        (await afterRecord.GetAs(AliceUser))!.IncompleteDecision.Should().BeNull("the Decision Record exists");
    }

    [Fact(DisplayName = "ISS-032 (must-not-fire): an invoice In Review, and one that is not decided, report no incompleteDecision")]
    public async Task UndecidedInvoice_ReportsNone()
    {
        var w = World(Verb.Approve);
        (await w.GetAs(AliceUser))!.IncompleteDecision.Should().BeNull();
    }

    [Theory(DisplayName = "ISS-032: a fault looking the record up reports no incompleteDecision (never a guess) and is logged with ids only")]
    [InlineData(Verb.Reject)]
    [InlineData(Verb.Return)]
    [InlineData(Verb.Approve)]
    public async Task LookupFault_ReportsNone_AndLogs(Verb verb)
    {
        var w = World(verb);
        await Strand(w, verb, "portion");
        if (verb == Verb.Approve)
        {
            // The writer-created approve record read (the repeat's own lookup) goes through the Decision Record table.
            w.FaultsOnce["read:" + InvoiceReviewReadService.DecisionRecordEntity] = new TimeoutException("dataverse down");
        }
        else
        {
            // The writer's stored-set lookup (the repeat's own lookup).
            w.H.LookupFault = new TimeoutException("dataverse down");
        }

        var state = (await w.GetAs(AliceUser))!;

        state.IncompleteDecision.Should().BeNull();
        state.Status.Should().NotBeNull("the rest of the state is still read");
        var warning = w.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("could not be read") && e.Message.Contains("no hint")).Subject;
        // Approve's read faults as itself; the writer's lookup wraps a fault as its own refusal, logged by its reason.
        warning.Message.Should().Contain(w.InvoiceId.ToString()).And.Contain(verb.ToString())
            .And.Contain(verb == Verb.Approve ? nameof(TimeoutException) : DecisionRecordRefusalReason.BillingRecordLookupFailed);
        warning.Message.Should().NotContain("dataverse down", "no exception text in the log");

        w.H.LookupFault = null;
        (await w.GetAs(AliceUser))!.IncompleteDecision.Should().NotBeNull("the next read, without the fault, reports it");
    }

    [Theory(DisplayName = "ISS-032: a caller who may not make the decision gets no hint - another eligible approver (not the claimant), the claimant without the decide right, a caller with no claim and no eligibility")]
    [MemberData(nameof(Verbs))]
    public async Task CallerWhoMayNotFinish_GetsNoHint(Verb verb)
    {
        var w = World(verb);
        await Strand(w, verb, "identity");

        var bob = (await w.GetAs(BobUser))!;
        bob.CallerEligible.Should().BeTrue("Bob is an eligible approver, but not the one whose decision stopped");
        bob.IncompleteDecision.Should().BeNull();

        (await w.GetAs(CarolUser))!.IncompleteDecision.Should().BeNull();
        (await w.GetAs(null))!.IncompleteDecision.Should().BeNull("an unproven identity is nobody");

        w.CanDecide = false;
        (await w.GetAs(AliceUser))!.IncompleteDecision.Should().BeNull("no Write on the invoice: the decide routes refuse the repeat");
        (await w.Service().GetAsync(w.InvoiceId, AliceUser, null, CancellationToken.None))!.IncompleteDecision.Should().BeNull("no caller to ask is no");

        w.CanDecide = true;
        (await w.GetAs(AliceUser))!.IncompleteDecision.Should().NotBeNull("(control) the claimant with the decide right sees it");
    }

    [Fact(DisplayName = "ISS-032: an approve whose claimant is no longer an eligible approver gets no hint (approve's repeat re-checks eligibility and would refuse it)")]
    public async Task Approve_ClaimantNoLongerEligible_GetsNoHint()
    {
        var w = World(Verb.Approve);
        await Strand(w, Verb.Approve, "identity");
        w.Core["sprk_assignedattorney1"] = new EntityReference("contact", Carol);

        (await w.GetAs(AliceUser))!.IncompleteDecision.Should().BeNull();
    }

    [Fact(DisplayName = "ISS-032: the claimant whose contact cannot be read gets no hint (fail closed)")]
    public async Task CallerIdentityFault_GetsNoHint()
    {
        var w = World(Verb.Reject);
        await Strand(w, Verb.Reject, "identity");
        w.FaultsOnce["caller-identity"] = new InvalidOperationException("dataverse down");

        (await w.GetAs(AliceUser))!.IncompleteDecision.Should().BeNull();
    }
}

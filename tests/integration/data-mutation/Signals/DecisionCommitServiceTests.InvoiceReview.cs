using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// Task 174 (D-207; ebilling EB-D-194): invoice and invoice-line decisions are made only in Invoice Review, so the commit route refuses
/// a Signal whose subject is <c>sprk_invoice</c> or <c>sprk_billingevent</c> with 409 <c>decision.decided_in_invoice_review</c>. The
/// refusal comes AFTER the caller's readability check (an item the caller cannot read is still the uniform 404) and the app-only read
/// of the Signals, and BEFORE the caller-rights probe, any executor and every write.
/// </summary>
public sealed partial class DecisionCommitServiceTests
{
    private static readonly Guid BillingSubjectId = Guid.Parse("c0430000-0000-4000-8000-0000000000b1");

    private static void AssertNoRightsAskedAndNothingWritten(Rig rig, string because)
    {
        rig.RightsAsked.Should().BeEmpty($"the caller-rights probe is never asked: {because}");
        rig.Probe.Verify(
            p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        rig.Org.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
        rig.Org.Verify(o => o.ExecuteAsync(It.IsAny<Microsoft.Xrm.Sdk.Messages.UpdateRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        rig.Writes.Should().BeEmpty(because);
        rig.Records.Should().BeEmpty(because);
        rig.SignalUpdates.Should().BeEmpty(because);
        rig.Log.Should().NotContain(l => l.StartsWith("preflight:", StringComparison.Ordinal), because);
        rig.Status(Primary).Should().Be(Open);
    }

    [Theory]
    [InlineData("sprk_billingevent")]
    [InlineData("sprk_invoice")]
    public async Task Task174_ASignalWhoseSubjectIsAnInvoiceOrALine_Is409DecidedInInvoiceReview_BeforeTheRightsProbeAndAnyWrite(string subject)
    {
        var rig = new Rig();
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, subjectEntity: subject, subjectId: BillingSubjectId);

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Conflict);
        result.ReasonCode.Should().Be("decision.decided_in_invoice_review");
        result.ReasonCode.Should().Be(DecisionCommitReasons.DecidedInInvoiceReview);
        AssertNoRightsAskedAndNothingWritten(rig, "an invoice or line is decided in Invoice Review");

        // The route's own mapping: 409 ProblemDetails with the reason code, never today's 403 SubjectWriteMissing.
        var reply = DecisionCommitEndpoints.ToHttp(result, Http());
        var problem = reply.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be("decision.decided_in_invoice_review");
    }

    [Fact]
    public async Task Task174_ADismissalOfABillingSignal_IsRefusedToo_NothingIsWritten()
    {
        var rig = new Rig();
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_billingevent", subjectId: BillingSubjectId);

        var result = await rig.CommitAsync(Dismissal("not-material"));

        result.Status.Should().Be(DecisionCommitStatus.Conflict);
        result.ReasonCode.Should().Be(DecisionCommitReasons.DecidedInInvoiceReview);
        AssertNoRightsAskedAndNothingWritten(rig, "a dismissal is a decision");
    }

    [Fact]
    public async Task Task174_ACallerWhoCannotReadABillingSignal_GetsTheUniform404_NotTheBillingRefusal()
    {
        // Nothing about an item shows to a caller who cannot read it, so the readability check (1a) answers before the subject is read.
        var rig = new Rig();
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_billingevent", subjectId: BillingSubjectId);
        rig.CallerCannotRead.Add(Primary);

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.NotFound);
        result.ReasonCode.Should().BeNull();
        result.Detail.Should().BeNull();
        AssertNoRightsAskedAndNothingWritten(rig, "an unreadable item is the uniform 404");
    }

    [Fact]
    public async Task Task174_AnAlsoResolveBillingSignal_RefusesTheWholeReview_AndNothingIsWritten()
    {
        // The primary is an ordinary matter Signal the caller may decide; the second is a billing Signal. The review is one decision.
        var rig = new Rig();
        rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_billingevent", subjectId: BillingSubjectId);

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false, also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.Conflict);
        result.ReasonCode.Should().Be(DecisionCommitReasons.DecidedInInvoiceReview);
        AssertNoRightsAskedAndNothingWritten(rig, "one billing Signal in the set refuses the whole review");
    }

    [Fact]
    public async Task Task174_AnAlsoResolveBillingSignalTheCallerCannotRead_IsTheUniform404_NotTheBillingRefusal()
    {
        var rig = new Rig();
        rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_invoice", subjectId: BillingSubjectId);
        rig.CallerCannotRead.Add(Second);

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false, also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.NotFound);
        result.ReasonCode.Should().BeNull();
        AssertNoRightsAskedAndNothingWritten(rig, "an unreadable Also resolve item is the uniform 404");
    }

    [Fact]
    public async Task Task174_AMatterSubjectSignal_IsUnchanged_AndStillAsksTheCallersRights()
    {
        var rig = new Rig();
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_matter", subjectId: Matter);

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.RightsAsked.Should().NotBeEmpty("the caller-rights probe still runs for every other subject");
    }
}

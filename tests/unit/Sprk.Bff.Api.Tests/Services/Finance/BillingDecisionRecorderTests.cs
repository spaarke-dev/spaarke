using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 074 (FR-30; D-75, D-100, D-101): <see cref="BillingDecisionRecorder"/>, the one Finance-side owner of a billing decision's
/// Decision Record. It runs over the REAL <see cref="DecisionRecordWriter"/> on the writer's test harness (the Dataverse boundary mocked at the
/// SDK), so each test proves the record the caller gets, not the request the recorder builds.
/// </summary>
public sealed class BillingDecisionRecorderTests
{
    private static readonly Guid Matter = Guid.Parse("0740c000-0000-4000-8000-0000000000a0");
    private static readonly Guid Project = Guid.Parse("0740c000-0000-4000-8000-0000000000b0");
    private static readonly Guid Invoice = Guid.Parse("0740c000-0000-4000-8000-0000000000c0");
    private static readonly Guid Line = Guid.Parse("0740c000-0000-4000-8000-0000000000d0");
    private static readonly Guid Reviewer = Guid.Parse("0740c000-0000-4000-8000-0000000000e0");
    private static readonly Guid ReviewerContact = Guid.Parse("0740c000-0000-4000-8000-0000000000e1");

    /// <summary>The identity service answering <see cref="Reviewer"/>'s linked contact as <see cref="ReviewerContact"/> (D-129).</summary>
    private static Mock<IIdentityNormalizationService> Identity(Guid? contact = null)
    {
        var identity = new Mock<IIdentityNormalizationService>();
        identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, CancellationToken _) => new PersonIdentity(user, ContactId: user == Reviewer ? contact ?? ReviewerContact : null));
        return identity;
    }

    private static (DecisionRecordWriterTests.Harness H, BillingDecisionRecorder Recorder) Build() => Build(Identity());

    private static (DecisionRecordWriterTests.Harness H, BillingDecisionRecorder Recorder) Build(Mock<IIdentityNormalizationService> identity)
    {
        var h = DecisionRecordWriterTests.WithBillingLookups(new DecisionRecordWriterTests.Harness());
        h.Rows[("sprk_invoice", Invoice)] = new Entity("sprk_invoice", Invoice) { ["sprk_matter"] = new EntityReference("sprk_matter", Matter) };
        h.Rows[("sprk_billingevent", Line)] = new Entity("sprk_billingevent", Line)
        {
            ["sprk_project"] = new EntityReference("sprk_project", Project),
            ["sprk_invoice"] = new EntityReference("sprk_invoice", Invoice),
        };
        return (h, new BillingDecisionRecorder(h.Writer, identity.Object, NullLogger<BillingDecisionRecorder>.Instance));
    }

    private static readonly IReadOnlyDictionary<string, object?> Facts = new Dictionary<string, object?> { ["billedTotal"] = 1200m };

    private static BillingDecision Decision(string code, Guid subject, BillingDecider? decider = null, string? reason = null, Guid? id = null,
        IReadOnlyDictionary<string, object?>? values = null) =>
        new(id ?? Guid.NewGuid(), code, subject, decider ?? BillingDecider.Human(Reviewer, "Billing reviewer"), Facts, "Done", reason, values);

    [Theory]
    [InlineData("approve-invoice", null)]
    [InlineData("reject-invoice", "duplicate")]
    [InlineData("return-to-firm", "missing timekeeper")]
    [InlineData("override-submission", "partner approved")]
    public async Task EachInvoiceDecision_WritesExactlyOneRecord_FiledUnderTheInvoice_AndARetryWritesNone(string code, string? reason)
    {
        var (h, recorder) = Build();
        var decision = Decision(code, Invoice, reason: reason);

        var first = await recorder.RecordAsync(decision);
        var retry = await recorder.RecordAsync(decision);

        first.AlreadyRecorded.Should().BeFalse();
        retry.AlreadyRecorded.Should().BeTrue();
        retry.RecordId.Should().Be(first.RecordId);
        first.RecordClass.Should().Be(RecordedDecisionClass.Judgement);
        h.Org.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
        h.Row.GetAttributeValue<EntityReference>("sprk_regardinginvoice").Id.Should().Be(Invoice, "D-75, D-101: subject the invoice");
        h.Row.GetAttributeValue<string>("sprk_actioncode").Should().Be(code);
    }

    [Theory]
    [InlineData("accept-line", null)]
    [InlineData("reduce-line", "rate above card")]
    [InlineData("disallow-line", "block billing")]
    public async Task EachLineDecision_IsFiledUnderTheLine_WithTheLinesOwnCore(string code, string? reason)
    {
        var (h, recorder) = Build();

        await recorder.RecordAsync(Decision(code, Line, reason: reason, values: new Dictionary<string, object?> { ["approvedAmount"] = 800m }));

        h.Row.GetAttributeValue<EntityReference>("sprk_regardinginvoiceline").Id.Should().Be(Line, "D-75: the line is the subject");
        h.Row.GetAttributeValue<string>("sprk_corerecordid").Should().Be(Project.ToString("D"), "the line's own core, never its invoice's matter");
    }

    [Fact]
    public async Task ASystemDecision_IsRecordedWithNoConfirmer_AndAnAutomatedDecider()
    {
        var (h, recorder) = Build();

        await recorder.RecordAsync(Decision("reject-invoice", Invoice, BillingDecider.Automated("submission pipeline (blocking failure)"), "blocking_failure"));

        h.Row.Contains("sprk_confirmedby").Should().BeFalse();
        h.Row.Contains("sprk_assignedtointernal").Should().BeFalse();
        h.Row.Contains("sprk_gatetier").Should().BeFalse();
        h.Row.GetAttributeValue<string>("sprk_factsnapshot").Should().Contain("automated: submission pipeline (blocking failure)");
    }

    [Fact]
    public async Task AHumanDecision_RecordsTheConfirmingUser_AndTheContactTheIdentityLookupGives()
    {
        // ontology-r1 K3: the caller names only the confirming user; the contact (sprk_assignedtointernal, D-129) comes from the identity
        // lookup of THAT user, never from the caller.
        var lookedUp = Guid.Parse("0740c000-0000-4000-8000-0000000000e2");
        var identity = Identity(contact: lookedUp);
        var (h, recorder) = Build(identity);

        await recorder.RecordAsync(Decision("approve-invoice", Invoice));

        h.Row.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(Reviewer);
        h.Row.GetAttributeValue<EntityReference>("sprk_assignedtointernal").Id.Should().Be(lookedUp, "the lookup's answer for the confirmer");
        h.Row.GetAttributeValue<string>("sprk_factsnapshot").Should().Contain("Billing reviewer");
        identity.Verify(i => i.ResolveAsync(Reviewer, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AHumanWithNoLinkedContact_IsRecorded_WithAssignedToInternalEmpty()
    {
        var identity = new Mock<IIdentityNormalizationService>();
        identity.Setup(i => i.ResolveAsync(Reviewer, It.IsAny<CancellationToken>())).ReturnsAsync(new PersonIdentity(Reviewer));
        var (h, recorder) = Build(identity);

        await recorder.RecordAsync(Decision("approve-invoice", Invoice));

        h.Row.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(Reviewer);
        h.Row.Contains("sprk_assignedtointernal").Should().BeFalse("the commit route's rule: no contact is not a failure");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ADecidersContactThatCannotBeRead_IsRefused_AndNothingIsWritten(bool serviceThrows)
    {
        var identity = new Mock<IIdentityNormalizationService>();
        if (serviceThrows)
        {
            identity.Setup(i => i.ResolveAsync(Reviewer, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("dataverse slow"));
        }
        else
        {
            identity.Setup(i => i.ResolveAsync(Reviewer, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PersonIdentity(Reviewer) { Faults = IdentityReadFaults.ContactBinding });
        }

        var (h, recorder) = Build(identity);

        (await recorder.Invoking(r => r.RecordAsync(Decision("approve-invoice", Invoice))).Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.BillingRecordLookupFailed);
        // The already-recorded check runs first (a true repeat must still answer), so the writer has READ by now; a NEW decision with an
        // identity fault is still refused before anything is created or asked.
        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().BeEmpty("refused before the owner question");
        h.Org.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ATrueRepeat_IsAlreadyRecorded_EvenWhenTheIdentityLookupWouldNowFault(bool serviceThrows)
    {
        // Follow-up item 4: the first call recorded the decision; the retry arrives during an identity fault. The record exists, so the
        // answer is AlreadyRecorded; the contact is not needed (and not read) for a record that is not written.
        var identity = Identity();
        var (h, recorder) = Build(identity);
        var decision = Decision("approve-invoice", Invoice);
        var first = await recorder.RecordAsync(decision);

        identity.Reset();
        if (serviceThrows)
        {
            identity.Setup(i => i.ResolveAsync(Reviewer, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("dataverse slow"));
        }
        else
        {
            identity.Setup(i => i.ResolveAsync(Reviewer, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PersonIdentity(Reviewer) { Faults = IdentityReadFaults.ContactBinding });
        }

        var retry = await recorder.RecordAsync(decision);

        retry.AlreadyRecorded.Should().BeTrue();
        retry.RecordId.Should().Be(first.RecordId);
        identity.Invocations.Should().BeEmpty("a true repeat needs no contact");
        h.Org.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ADifferentDecisionUnderTheSameId_DuringAnIdentityFault_IsRefusedForItsContent_NotAnswered()
    {
        // Must-not-fire for item 4: moving the contact lookup after the already-recorded check must not turn a changed decision into an answer.
        var identity = Identity();
        var (h, recorder) = Build(identity);
        var id = Guid.NewGuid();
        await recorder.RecordAsync(Decision("reject-invoice", Invoice, reason: "duplicate", id: id));
        identity.Reset();
        identity.Setup(i => i.ResolveAsync(Reviewer, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("dataverse slow"));

        (await recorder.Invoking(r => r.RecordAsync(Decision("reject-invoice", Invoice, reason: "a different reason", id: id)))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        h.Created.Should().ContainSingle("the first call's record only");
    }

    [Fact]
    public async Task ASystemDecision_DoesNotLookUpAContact()
    {
        var identity = Identity();
        var (_, recorder) = Build(identity);

        await recorder.RecordAsync(Decision("reject-invoice", Invoice, BillingDecider.Automated("submission pipeline"), "blocking_failure"));

        identity.Verify(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("override-submission")]
    [InlineData("reject-invoice")]
    [InlineData("return-to-firm")]
    public async Task AnOverrideRejectOrReturnWithoutAReason_IsRefusedBeforeAnyWrite_ByPreflightAndByRecord(string code)
    {
        var identity = Identity();
        var (h, recorder) = Build(identity);
        var decision = Decision(code, Invoice, reason: null);

        recorder.Invoking(r => r.Preflight(decision)).Should().Throw<DecisionRecordRefusedException>()
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        (await recorder.Invoking(r => r.RecordAsync(decision)).Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        h.Entities.Invocations.Should().BeEmpty();
        identity.Invocations.Should().BeEmpty("an invalid decision is refused before the identity read");
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task ASignalsCode_IsRefused_AndNothingIsWritten()
    {
        var (h, recorder) = Build();

        (await recorder.Invoking(r => r.RecordAsync(Decision("mark-complete", Invoice))).Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.RecordClassUnderivable);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public void Preflight_PassesAValidDecision_AndReadsNothing()
    {
        var (h, recorder) = Build();

        recorder.Invoking(r => r.Preflight(Decision("approve-invoice", Invoice))).Should().NotThrow();
        h.Entities.Invocations.Should().BeEmpty();
    }

    [Fact]
    public void BillingDecider_Automated_PrefixesTheRole_AndHasNoUser()
    {
        var system = BillingDecider.Automated("x");

        system.Role.Should().Be("automated: x");
        system.SystemUserId.Should().BeNull();
    }
}

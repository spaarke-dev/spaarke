using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// ebilling-r1 task 074 (D-100, owner-approved section 6.5 path B): <see cref="DecisionRecordWriter.WriteBillingDecisionAsync"/>, the additive
/// billing entry. Each region proves one of ontology-r1's binding conditions (C1 to C6; C7, the additive-only diff, is checked mechanically).
/// The harness is task 149's: the "already recorded" lookup's conditions are EVALUATED against what it holds, and a row it created is stored
/// with <c>createdby</c> = the writer's own user.
/// </summary>
public sealed partial class DecisionRecordWriterTests
{
    private static readonly Guid InvoiceId = Guid.Parse("0740b111-0000-4000-8000-0000000000a1");
    private static readonly Guid LineId = Guid.Parse("0740b111-0000-4000-8000-0000000000b1");
    private static readonly Guid BillingDecision = Guid.Parse("0740b111-0000-4000-8000-0000000000d1");
    private static readonly Guid Reviewer = Guid.Parse("0740b111-0000-4000-8000-0000000000e1");

    /// <summary>A harness whose invoice is filed under <see cref="MatterId"/> and whose line under its own project (D-75).</summary>
    private static Harness BillingHarness(Guid? lineProject = null)
    {
        var h = new Harness();
        h.Rows[("sprk_invoice", InvoiceId)] = new Entity("sprk_invoice", InvoiceId) { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) };
        h.Rows[("sprk_billingevent", LineId)] = new Entity("sprk_billingevent", LineId)
        {
            ["sprk_project"] = new EntityReference("sprk_project", lineProject ?? ProjectId),
            ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
        };
        return h;
    }

    private static BillingDecisionRecordRequest Billing(
        string code = "approve-invoice",
        DecisionRecordRef? subject = null,
        Guid? confirmer = null,
        bool system = false,
        string? role = null,
        string? reason = null,
        IReadOnlyDictionary<string, object?>? values = null,
        Guid? decisionId = null,
        IReadOnlyDictionary<string, object?>? facts = null,
        Guid? policyVersion = null) => new(
        decisionId ?? BillingDecision,
        code,
        subject ?? (code is "accept-line" or "reduce-line" or "disallow-line"
            ? new DecisionRecordRef("sprk_billingevent", LineId)
            : new DecisionRecordRef("sprk_invoice", InvoiceId)),
        system ? null : confirmer ?? Reviewer,
        role ?? (system ? "automated: submission pipeline" : "Billing reviewer"),
        values,
        "Done",
        facts ?? new Dictionary<string, object?> { ["billedTotal"] = 1200m, ["approvedTotal"] = 1000m },
        reason,
        policyVersion);

    /// <summary>The writer touched nothing: no reader call (lookup, stamp or catalog), no WhoAmI, no owner question and no create.</summary>
    private static void NoIo(Harness h)
    {
        h.Entities.Invocations.Should().BeEmpty("refused before any read");
        h.WhoAmICalls.Should().Be(0);
        h.ProbeCalls.Should().Be(0);
        h.OwnerAsks.Should().BeEmpty();
        h.Org.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Created.Should().BeEmpty();
    }

    // ── C1: the two entries take disjoint code sets ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("approve-invoice")]
    [InlineData("reject-invoice")]
    [InlineData("accept-line")]
    [InlineData("override-submission")]
    public async Task Billing_C1_ABillingCodeThroughTheExistingWriteAsync_IsRefused_AndNothingIsWritten(string billingCode)
    {
        var h = BillingHarness();

        var ex = (await h.Writer.Invoking(w => w.WriteAsync(Request(steps: [Taken(billingCode)]))).Should()
            .ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.RecordClassUnderivable, "WriteAsync's catalog (TryGet) does not know billing codes");
        h.Created.Should().BeEmpty();
    }

    [Theory]
    [InlineData("mark-complete")]
    [InlineData("approve-variance")]
    [InlineData("send-budget-inquiry")]
    [InlineData("condition-cleared")]
    [InlineData("not-a-code")]
    public async Task Billing_C1_AnOntologyOrSignalsCodeThroughTheBillingEntry_IsRefused_BeforeAnyIo(string signalsCode)
    {
        var h = BillingHarness();

        var ex = (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing(signalsCode, subject: new DecisionRecordRef("sprk_invoice", InvoiceId))))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.RecordClassUnderivable);
        NoIo(h);
    }

    // ── C2: the subject is the code's own table, checked before any I/O ────────────────────────────────────────────

    [Theory]
    [InlineData("accept-line", "sprk_invoice")]
    [InlineData("reduce-line", "sprk_invoice")]
    [InlineData("disallow-line", "sprk_invoice")]
    [InlineData("approve-invoice", "sprk_billingevent")]
    [InlineData("reject-invoice", "sprk_billingevent")]
    [InlineData("return-to-firm", "sprk_billingevent")]
    [InlineData("override-submission", "sprk_billingevent")]
    [InlineData("approve-invoice", "sprk_matter")]
    [InlineData("accept-line", "sprk_todo")]
    [InlineData("override-submission", "sprk_invoicesubmission")]
    public async Task Billing_C2_ASubjectThatIsNotTheCodesTable_IsRefused_WithZeroReaderCallsAndZeroCreates(string code, string table)
    {
        var h = BillingHarness();
        var request = Billing(code, subject: new DecisionRecordRef(table, Guid.NewGuid()), reason: "r",
            values: new Dictionary<string, object?> { ["approvedAmount"] = 10m });

        var ex = (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(request)).Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.SubjectUnsupported);
        NoIo(h);
    }

    [Fact]
    public void Billing_C2_TheBillingEntrysSubjects_AreTheLineForLineCodes_AndTheInvoiceForEveryOther()
    {
        DecisionActionCatalog.BillingActions.Select(d => (d.Code, DecisionRecordWriter.BillingSubjectEntityOf(d.Code))).Should().BeEquivalentTo(new[]
        {
            ("accept-line", "sprk_billingevent"), ("reduce-line", "sprk_billingevent"), ("disallow-line", "sprk_billingevent"),
            ("approve-invoice", "sprk_invoice"), ("reject-invoice", "sprk_invoice"), ("return-to-firm", "sprk_invoice"),
            ("override-submission", "sprk_invoice"),
        }, "D-75, D-101: no submission subject; the override and the automatic rejection file under the invoice");
        DecisionRecordWriter.BillingSubjectEntityOf("mark-complete").Should().BeNull("a Signals code has no billing subject");
        DecisionActionCatalog.BillingActions.Should().OnlyContain(d => !d.Code.Contains('_') && !d.Code.Contains('%') && !d.Code.Contains('['),
            "the lookup matches the code inside a LIKE pattern");
    }

    // ── C3: exactly once per decision id, only the writer's own record counts ─────────────────────────────────────

    [Fact]
    public async Task Billing_C3_ARepeatedDecisionId_ReturnsAlreadyRecorded_WithExactlyOneCreate()
    {
        var h = BillingHarness();

        var first = await h.Writer.WriteBillingDecisionAsync(Billing());
        var second = await h.Writer.WriteBillingDecisionAsync(Billing());

        first.AlreadyRecorded.Should().BeFalse();
        second.AlreadyRecorded.Should().BeTrue();
        second.RecordId.Should().Be(first.RecordId);
        second.ConfirmedByUserId.Should().Be(Reviewer, "the stored confirmer");
        h.Org.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
        h.OwnerAsks.Should().ContainSingle("a repeat stops at the lookup");
    }

    [Fact]
    public async Task Billing_C3_TheExistenceCheck_ReadsCreatedByEqualToTheWriterIdentity_ThroughTheSysadminReader()
    {
        var h = BillingHarness();

        await h.Writer.WriteBillingDecisionAsync(Billing());

        var lookup = h.Lookups.Where(q => q.EntityName == "sprk_decisionrecord").Should().ContainSingle().Which;
        lookup.Criteria.Conditions.Should().Contain(c =>
            c.AttributeName == "createdby" && c.Operator == ConditionOperator.Equal && c.Values.Single().Equals(WriterUser));
        lookup.Criteria.Conditions.Should().Contain(c =>
            c.AttributeName == "sprk_regardinginvoice" && c.Operator == ConditionOperator.Equal && c.Values.Single().Equals(InvoiceId));
        h.Entities.Verify(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_decisionrecord"), It.IsAny<CancellationToken>()),
            Times.Once, "the shared sysadmin reader, not the writer identity's connection");
        h.WhoAmICalls.Should().Be(1);
    }

    [Fact]
    public async Task Billing_C3_ARowWithTheSameKeyAndCodeButAnotherCreator_IsNotAlreadyRecorded_AndTheRecordIsWritten()
    {
        // ontology-r1 F3-1: the forged row carries everything the writer's own record would (the key, this decision's content hash, the billing
        // code in sprk_actioncode), so ONLY the createdby condition keeps it from answering as this decision's record. Without
        // sprk_actioncode the code filter skipped it and the test proved nothing about createdby.
        var h = BillingHarness();
        var forged = new Entity("sprk_decisionrecord", Guid.NewGuid())
        {
            ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", InvoiceId),
            ["sprk_steps"] = $"{{\"reviewId\":\"{BillingDecisionKey.IdOf("sprk_invoice", InvoiceId, BillingDecision):D}\",\"reviewHash\":\"{DecisionRecordWriter.BillingDecisionHash(Billing())}\",\"steps\":[{{\"code\":\"approve-invoice\"}}]}}",
            ["sprk_actioncode"] = "approve-invoice",
            ["createdby"] = new EntityReference("systemuser", ConsoleUser),
            ["sprk_confirmedby"] = new EntityReference("systemuser", Framed),
        };
        h.Seeded.Add(forged);

        var result = await h.Writer.WriteBillingDecisionAsync(Billing());

        result.AlreadyRecorded.Should().BeFalse("a row a person created is never the decision's record");
        result.RecordId.Should().NotBe(forged.Id);
        h.Created.Should().ContainSingle();
        h.Row.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(Reviewer);
    }

    [Fact]
    public async Task Billing_C3_AWriterRecordWithTheSameKeyButAnotherCode_IsNotThisDecisionsRecord()
    {
        // A Signals review's record (catalog codes) could carry a client-chosen review id equal to the key; it never carries a billing code.
        var h = BillingHarness();
        h.Seeded.Add(new Entity("sprk_decisionrecord", Guid.NewGuid())
        {
            ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", InvoiceId),
            ["sprk_steps"] = $"{{\"reviewId\":\"{BillingDecisionKey.IdOf("sprk_invoice", InvoiceId, BillingDecision):D}\",\"steps\":[{{\"code\":\"mark-complete\"}}]}}",
            ["sprk_actioncode"] = "mark-complete",
            ["createdby"] = new EntityReference("systemuser", WriterUser),
        });

        var result = await h.Writer.WriteBillingDecisionAsync(Billing());

        result.AlreadyRecorded.Should().BeFalse();
        h.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task Billing_C3_TheSameDecisionIdWithAnotherBillingCode_IsRefused_AndNoSecondRecordIsWritten()
    {
        // Adversarial F2: the key is (subject, decision id), with no code. A retry that changed its mind after a create timeout (reject, then
        // return-to-firm under the same id) would otherwise write a second, contradictory record under one key.
        var h = BillingHarness();
        var first = await h.Writer.WriteBillingDecisionAsync(Billing("reject-invoice", reason: "Duplicate of INV-0001"));

        var ex = (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing("return-to-firm", reason: "Please resubmit")))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        first.AlreadyRecorded.Should().BeFalse();
        ex.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        ex.Message.Should().Contain("another billing action", "refused by the lookup, not by validation");
        h.Lookups.Where(q => q.EntityName == "sprk_decisionrecord").Should().HaveCount(2, "both calls reached the lookup");
        h.Created.Should().ContainSingle("the first decision's record only")
            .Which.GetAttributeValue<string>("sprk_actioncode").Should().Be("reject-invoice");
        h.Org.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Billing_C3_AnotherDecisionIdOrAnotherSubject_IsItsOwnRecord()
    {
        var h = BillingHarness();

        await h.Writer.WriteBillingDecisionAsync(Billing("accept-line"));
        var otherDecision = await h.Writer.WriteBillingDecisionAsync(Billing("accept-line", decisionId: Guid.NewGuid()));

        otherDecision.AlreadyRecorded.Should().BeFalse();
        h.Created.Should().HaveCount(2);
        BillingDecisionKey.IdOf("sprk_billingevent", LineId, BillingDecision).Should().NotBe(BillingDecisionKey.IdOf("sprk_invoice", InvoiceId, BillingDecision));
        BillingDecisionKey.Of("sprk_invoice", InvoiceId, BillingDecision).Should().StartWith("billing-dr-");
    }

    // ── ontology-r1 K2: AlreadyRecorded only for the same content, not just the same code ─────────────────────────

    private static BillingDecisionRecordRequest Reduce(decimal approvedAmount) =>
        Billing("reduce-line", reason: "rate above card", values: new Dictionary<string, object?> { ["approvedAmount"] = approvedAmount });

    [Fact]
    public async Task Billing_K2_TheSameIdAndCodeWithOtherValues_IsRefused_AndNoSecondRecordIsWritten()
    {
        var h = BillingHarness();
        var first = await h.Writer.WriteBillingDecisionAsync(Reduce(800m));

        var ex = (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Reduce(700m))).Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        first.AlreadyRecorded.Should().BeFalse();
        ex.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid, "700 is not a repeat of the recorded 800");
        ex.Message.Should().Contain("other content");
        h.Org.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Billing_K2_ATrueRepeat_IsAlreadyRecorded_AndTheRecordStoresItsContentHash()
    {
        var h = BillingHarness();
        var first = await h.Writer.WriteBillingDecisionAsync(Reduce(800m));

        var repeat = await h.Writer.WriteBillingDecisionAsync(Reduce(800m));

        repeat.AlreadyRecorded.Should().BeTrue();
        repeat.RecordId.Should().Be(first.RecordId);
        h.Org.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
        using var steps = JsonDocument.Parse(h.Row.GetAttributeValue<string>("sprk_steps"));
        steps.RootElement.GetProperty("reviewHash").GetString().Should().Be(DecisionRecordWriter.BillingDecisionHash(Reduce(800m)));
        DecisionRecordWriter.BillingDecisionHash(Reduce(800m) with { Facts = new Dictionary<string, object?> { ["reread"] = true }, DecidedOn = Now })
            .Should().Be(DecisionRecordWriter.BillingDecisionHash(Reduce(800m)), "a retry may re-read its facts and clock");
    }

    [Fact]
    public async Task Billing_K2_AWriterRecordWithTheSameKeyAndCodeButNoReadableHash_IsRefused_NeverAnsweredNorWrittenOver()
    {
        var h = BillingHarness();
        h.Seeded.Add(new Entity("sprk_decisionrecord", Guid.NewGuid())
        {
            ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", InvoiceId),
            ["sprk_steps"] = $"{{\"reviewId\":\"{BillingDecisionKey.IdOf("sprk_invoice", InvoiceId, BillingDecision):D}\",\"steps\":[{{\"code\":\"approve-invoice\"}}]}}",
            ["sprk_actioncode"] = "approve-invoice",
            ["createdby"] = new EntityReference("systemuser", WriterUser),
        });

        (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing())).Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        h.Created.Should().BeEmpty();
    }

    // ── C4: a lookup fault writes nothing ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Billing_C4_ALookupFault_WritesNothing()
    {
        var h = BillingHarness();
        h.LookupFault = new TimeoutException("dataverse slow");

        var ex = (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing())).Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.BillingRecordLookupFailed);
        ex.InnerException.Should().BeOfType<TimeoutException>();
        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().BeEmpty();
    }

    [Fact]
    public async Task Billing_C4_AWriterIdentityFault_WritesNothing()
    {
        var h = BillingHarness();
        h.WhoAmIFault = new InvalidOperationException("whoami down");

        (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing())).Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.BillingRecordLookupFailed);
        h.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Billing_C4_ALookupTimeoutSurfacingAsACancellationTheCallerDidNotAskFor_IsTheLookupRefusal_AndWritesNothing()
    {
        // Adversarial K4: an HTTP timeout surfaces as TaskCanceledException with the caller's token NOT cancelled. The writer's contract is
        // "DecisionRecordRefusedException, or OperationCanceledException only when the caller cancelled".
        var h = BillingHarness();
        h.LookupFault = new TaskCanceledException("http timeout");

        var ex = (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing(), CancellationToken.None))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.BillingRecordLookupFailed);
        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().BeEmpty();
    }

    // ── C5: a null confirmer only for an automated decider ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Billing reviewer")]
    [InlineData("Automated: pipeline")]
    [InlineData(" automated: pipeline")]
    public async Task Billing_C5_ANullConfirmerWithoutAnAutomatedDecider_IsRefused_BeforeAnyIo(string role)
    {
        var h = BillingHarness();

        (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing("reject-invoice", system: true, role: role, reason: "late")))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing("reject-invoice", confirmer: Guid.Empty, role: role, reason: "late")))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        NoIo(h);
    }

    [Fact]
    public async Task Billing_C5_ASystemDecision_IsRecordedWithNoConfirmer_NoRequester_AndNoGateTier()
    {
        var h = BillingHarness();

        var result = await h.Writer.WriteBillingDecisionAsync(
            Billing("reject-invoice", system: true, role: "automated: submission pipeline (blocking failure)", reason: "blocking_failure - rate missing"));

        result.ConfirmedByUserId.Should().BeNull();
        h.Row.Contains("sprk_confirmedby").Should().BeFalse("D-101: the automatic rejection is a system decision");
        h.Row.Contains("sprk_gatetier").Should().BeFalse("Q6: a system decision records a null gate tier");
        h.Row.Contains("sprk_createdbyperson").Should().BeFalse("no person asked for it");
        h.OwnerAsks.Single().RequestedBy.Should().BeNull();
        h.Row.GetAttributeValue<string>("sprk_factsnapshot").Should().Contain("automated: submission pipeline (blocking failure)");
        h.Row.GetAttributeValue<EntityReference>("sprk_regardinginvoice").Id.Should().Be(InvoiceId, "D-101: filed under the invoice");
    }

    [Fact]
    public async Task Billing_C5_AHumanDecision_RecordsTheConfirmingUser_AndTheCodesGateTier()
    {
        var h = BillingHarness();

        var result = await h.Writer.WriteBillingDecisionAsync(Billing("approve-invoice"));

        result.ConfirmedByUserId.Should().Be(Reviewer);
        h.Row.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(Reviewer);
        h.Row.GetAttributeValue<string>("sprk_gatetier").Should().Be("Tier4");
        h.OwnerAsks.Single().RequestedBy.Should().NotBeNull("the confirming user is the requester (sprk_createdbyperson)");
    }

    [Fact]
    public async Task Billing_C5_AUserConfirmedDecisionWithAnAutomatedDecider_IsRefused()
    {
        // Edge beyond the stated contract (one line in notes): a confirmed decision labelled automated would misstate who decided.
        var h = BillingHarness();

        (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing(role: "automated: pipeline"))).Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        NoIo(h);
    }

    [Fact]
    public async Task Billing_C5_ASystemDecisionNamingADecidingContact_IsRefused()
    {
        // Edge beyond the stated contract (one line in notes): sprk_assignedtointernal would name a person for a decision no person made (D-129).
        var h = BillingHarness();

        (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing("reject-invoice", system: true, reason: "late") with { DeciderContactId = Guid.NewGuid() }))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        NoIo(h);
    }

    // ── C6: the 026 invoice-family checks run on the billing path (the shared FiledAndOwnedAsync) ─────────────────

    [Theory]
    [InlineData("sprk_billingevent", "no-core")]
    [InlineData("sprk_billingevent", "two-roots")]
    [InlineData("sprk_invoice", "no-core")]
    [InlineData("sprk_invoice", "two-roots")]
    public async Task Billing_C6_AnInvoiceOrLineNotFiledUnderExactlyOneCore_IsUnfileable_AndNothingIsWritten(string table, string condition)
    {
        var subjectId = Guid.NewGuid();
        var h = new Harness();
        var row = new Entity(table, subjectId);
        if (table == "sprk_billingevent") row["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId);
        if (condition == "two-roots")
        {
            row["sprk_matter"] = new EntityReference("sprk_matter", MatterId);
            row["sprk_project"] = new EntityReference("sprk_project", Guid.NewGuid());
        }

        h.Rows[(table, subjectId)] = row;
        var code = table == "sprk_billingevent" ? "accept-line" : "approve-invoice";

        var ex = (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing(code, subject: new DecisionRecordRef(table, subjectId))))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.SubjectUnfileable);
        ex.Message.Should().NotContain(subjectId.ToString("D"), "the refusal names no record");
        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().BeEmpty();
    }

    [Fact]
    public async Task Billing_C6_AFirstDerivationThatFaults_IsAFilingFault_NotAPermanentRefusal_EvenWhenARereadWouldSucceed()
    {
        // Adversarial F1: the first core derivation is throttled, a re-read a moment later succeeds. Handing "no core" on let the filing
        // step's own derivation succeed and the 026 core check refuse with SubjectUnfileable, a reason callers treat as "a retry won't
        // change this". A fault is FilingFailed (a retry may succeed), and nothing is written.
        var h = BillingHarness();
        var invoice = h.Rows[("sprk_invoice", InvoiceId)];
        var reads = 0;
        h.Entities.Setup(e => e.RetrieveAsync("sprk_invoice", InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns(() => Interlocked.Increment(ref reads) == 1
                ? Task.FromException<Entity>(new TimeoutException("throttled"))
                : Task.FromResult(invoice));

        var ex = (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing()))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.FilingFailed);
        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().BeEmpty();
    }

    [Fact]
    public async Task Billing_C6_ALineDecision_IsFiledUnderTheLine_WithTheLinesOwnCore_NeverItsInvoicesMatter()
    {
        var project = Guid.NewGuid();
        var h = BillingHarness(lineProject: project);

        var result = await h.Writer.WriteBillingDecisionAsync(Billing("reduce-line", reason: "rate above card",
            values: new Dictionary<string, object?> { ["approvedAmount"] = 800m }));

        result.RecordClass.Should().Be(RecordedDecisionClass.Judgement);
        h.Row.GetAttributeValue<EntityReference>("sprk_regardinginvoiceline").Id.Should().Be(LineId, "D-75: the line is the subject");
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingproject").Id.Should().Be(project);
        h.Row.GetAttributeValue<string>("sprk_corerecordid").Should().Be(project.ToString("D"), "the core is the line's own (D-75)");
        h.Row.GetAttributeValue<EntityReference>("sprk_project").Id.Should().Be(project);
        h.Row.Contains("sprk_regardingmatter").Should().BeFalse("the invoice's matter is never borrowed");
        h.Row.Contains("sprk_matter").Should().BeFalse();
        h.Entities.Verify(e => e.RetrieveAsync("sprk_invoice", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Billing_C6_AnInvoiceDecision_IsFiledUnderTheInvoice_WithTheInvoicesOwnMatterAsItsCore()
    {
        var h = BillingHarness();

        await h.Writer.WriteBillingDecisionAsync(Billing("approve-invoice"));

        h.Row.GetAttributeValue<EntityReference>("sprk_regardinginvoice").Id.Should().Be(InvoiceId);
        h.Row.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId);
        h.Row.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(MatterId);
        h.Row.GetAttributeValue<string>("sprk_corerecordid").Should().Be(MatterId.ToString("D"));
        h.Row.GetAttributeValue<EntityReference>("sprk_corerecordtype").Id
            .Should().Be(Tests.TestInfrastructure.RecordTypeCatalogStub.SpaarkeDev1["sprk_matter"]);
    }

    // ── The record's content (FR-30, FR-19, FR-37) ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Billing_ARecordCarriesTheCodesClass_OutcomeAuthorized_TheCode_TheReason_AndTheKey()
    {
        var h = BillingHarness();
        var signal = Guid.NewGuid();

        await h.Writer.WriteBillingDecisionAsync(Billing("reject-invoice", reason: "duplicate - already paid") with
        {
            PolicyVersionId = PolicyVersion,
            ResolvedSignalIds = [signal],
            Values = new Dictionary<string, object?> { ["reason"] = "duplicate - already paid" },
        });

        Option(h.Row, "sprk_recordclass").Should().Be(100000000, "Judgement");
        Option(h.Row, "sprk_decisionoutcome").Should().Be(100000000, "Q3: Authorized, the decision was taken; the code says which");
        h.Row.GetAttributeValue<string>("sprk_actioncode").Should().Be("reject-invoice");
        h.Row.GetAttributeValue<string>("sprk_reason").Should().Be("duplicate - already paid");
        h.Row.GetAttributeValue<string>("sprk_name").Should().Be("Judgement: Reject invoice");
        h.Row.GetAttributeValue<EntityReference>("sprk_policyversion").Id.Should().Be(PolicyVersion);
        h.Row.Contains("sprk_action").Should().BeFalse();
        using var steps = JsonDocument.Parse(h.Row.GetAttributeValue<string>("sprk_steps"));
        steps.RootElement.GetProperty("reviewId").GetString().Should().Be(BillingDecisionKey.IdOf("sprk_invoice", InvoiceId, BillingDecision).ToString("D"));
        steps.RootElement.GetProperty("resolvedSignalIds").EnumerateArray().Select(s => s.GetString()).Should().Equal(signal.ToString("D"));
        var step = steps.RootElement.GetProperty("steps").EnumerateArray().Should().ContainSingle().Which;
        step.GetProperty("code").GetString().Should().Be("reject-invoice");
        step.GetProperty("taken").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Billing_ACleanDecision_NeedsNoPolicyVersionAndNoSignal_AndAcceptLineIsRoutine()
    {
        var h = BillingHarness();

        var result = await h.Writer.WriteBillingDecisionAsync(Billing("accept-line"));

        result.RecordClass.Should().Be(RecordedDecisionClass.Routine, "Q4");
        result.ResolvedSignalIds.Should().BeEmpty();
        h.Row.Contains("sprk_policyversion").Should().BeFalse("a clean decision has no policy version; none is invented");
        h.Row.GetAttributeValue<string>("sprk_gatetier").Should().Be("Tier2b");
    }

    [Theory]
    [InlineData("reduce-line")]
    [InlineData("disallow-line")]
    [InlineData("reject-invoice")]
    [InlineData("return-to-firm")]
    [InlineData("override-submission")]
    public async Task Billing_ACodeWhoseReasonIsRequired_WithoutAReason_IsRefused_BeforeAnyIo(string code)
    {
        var h = BillingHarness();
        var values = new Dictionary<string, object?> { ["approvedAmount"] = 10m };

        (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing(code, reason: "  ", values: values))).Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        h.Writer.Invoking(w => w.PreflightBillingDecision(Billing(code, reason: null, values: values))).Should().Throw<DecisionRecordRefusedException>()
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        NoIo(h);
    }

    [Fact]
    public async Task Billing_AReduceWithoutItsApprovedAmount_IsRefused_BeforeAnyIo()
    {
        var h = BillingHarness();

        (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing("reduce-line", reason: "too high"))).Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        NoIo(h);
    }

    [Fact]
    public async Task Billing_NoFacts_IsRefused_BeforeAnyIo()
    {
        var h = BillingHarness();

        (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing(facts: new Dictionary<string, object?>()))).Should().ThrowAsync<DecisionRecordRefusedException>())
            .Which.Reason.Should().Be(DecisionRecordRefusalReason.FactSnapshotMissing);
        NoIo(h);
    }

    [Fact]
    public void Billing_Preflight_PassesAValidDecision_WithNoIo()
    {
        var h = BillingHarness();

        h.Writer.Invoking(w => w.PreflightBillingDecision(Billing("approve-invoice"))).Should().NotThrow();
        NoIo(h);
    }

    // ── ontology-r1 F4-1 (D-123, secure-if-any): the resolved Signals' subjects reach the owner question ───────────

    private static readonly Guid BillingSignal = Guid.Parse("0740b111-0000-4000-8000-0000000000f1");

    /// <summary>A Signal filed the way the Signal writer files one: the pair id and the subject lookup that repeats it (task 141).</summary>
    private static Entity SignalAbout(Guid signalId, string subjectTable, Guid subjectId) => new("sprk_signal", signalId)
    {
        ["sprk_regardingrecordid"] = subjectId.ToString("D"),
        [SignalWriter.RegardingLookupByEntity[subjectTable]] = new EntityReference(subjectTable, subjectId),
    };

    private static System.ServiceModel.FaultException<OrganizationServiceFault> NotFound() =>
        new(new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040217) }, "Does Not Exist");

    private static void NothingWritten(Harness h)
    {
        h.Created.Should().BeEmpty();
        h.OwnerAsks.Should().BeEmpty("refused before the owner question");
        h.Org.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Billing_F41_AnInvoiceDecisionResolvingALineSignal_AsksTheOwnerAboutTheLine()
    {
        // Must-fire: the invoice decision's own parents are its matter, the invoice and its derived cores; the line is a parent ONLY because
        // the resolved Signal is about it. A line under a secure record must make the record secure (D-123).
        var h = BillingHarness();
        h.Rows[("sprk_signal", BillingSignal)] = SignalAbout(BillingSignal, "sprk_billingevent", LineId);

        await h.Writer.WriteBillingDecisionAsync(Billing("approve-invoice") with { ResolvedSignalIds = [BillingSignal] });

        h.OwnerAsks.Should().ContainSingle().Which.Parents.Should().Contain(new RecordOwnershipParent("sprk_billingevent", LineId));
        h.Entities.Verify(e => e.RetrieveAsync("sprk_signal", BillingSignal, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Once,
            "read app-only through the shared sysadmin reader");
        h.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task Billing_F41_AMissingSignal_IsRefused_BeforeAnyCreate()
    {
        var h = BillingHarness();
        h.Entities.Setup(e => e.RetrieveAsync("sprk_signal", BillingSignal, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFound());

        var ex = (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing("approve-invoice") with { ResolvedSignalIds = [BillingSignal] }))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.OwnerRefused);
        ex.OwnerRefusalCode.Should().Be(RecordOwnerRefusal.ParentUndetermined);
        NothingWritten(h);
    }

    [Fact]
    public async Task Billing_F41_ASignalWhoseSubjectCannotBeDetermined_IsRefused()
    {
        // The pair id is set but no subject lookup carries it: an UNKNOWN parent, never dropped (DecisionCommitService.SignalSubjectsOf).
        var h = BillingHarness();
        h.Rows[("sprk_signal", BillingSignal)] = new Entity("sprk_signal", BillingSignal) { ["sprk_regardingrecordid"] = LineId.ToString("D") };

        var ex = (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing("approve-invoice") with { ResolvedSignalIds = [BillingSignal] }))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.OwnerRefused);
        ex.OwnerRefusalCode.Should().Be(RecordOwnerRefusal.ParentUndetermined);
        NothingWritten(h);
    }

    [Fact]
    public async Task Billing_F41_AFaultOnTheSignalRead_WritesNothing()
    {
        var h = BillingHarness();
        h.Entities.Setup(e => e.RetrieveAsync("sprk_signal", BillingSignal, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("throttled"));

        var ex = (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing("approve-invoice") with { ResolvedSignalIds = [BillingSignal] }))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which;

        ex.Reason.Should().Be(DecisionRecordRefusalReason.OwnerResolutionFailed, "a fault is not an answer; a retry may succeed");
        ex.InnerException.Should().BeOfType<TimeoutException>();
        NothingWritten(h);
    }

    [Fact]
    public async Task Billing_F41_APolicyVersionThatDoesNotExist_IsRefused_AndAReadFaultWritesNothing()
    {
        var h = BillingHarness();
        h.Entities.Setup(e => e.RetrieveAsync("sprk_policyversion", PolicyVersion, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFound());

        (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing(policyVersion: PolicyVersion)))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.ReviewInvalid);
        NothingWritten(h);

        h.Entities.Setup(e => e.RetrieveAsync("sprk_policyversion", PolicyVersion, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("throttled"));
        (await h.Writer.Invoking(w => w.WriteBillingDecisionAsync(Billing(policyVersion: PolicyVersion)))
            .Should().ThrowAsync<DecisionRecordRefusedException>()).Which.Reason.Should().Be(DecisionRecordRefusalReason.BillingRecordLookupFailed);
        NothingWritten(h);
    }
}

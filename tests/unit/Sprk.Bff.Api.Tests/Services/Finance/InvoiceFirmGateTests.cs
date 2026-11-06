using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 095d-0 (EB-D-185): <see cref="InvoiceFirmGate"/> - no firm, held for one, or the post-Set-firm re-run pending refuses; a firm
/// with no re-run in flight is open. Over an in-memory model of the two app-only reads it makes (the invoice's own hold, the linked submissions).
/// </summary>
public sealed class InvoiceFirmGateTests
{
    private static readonly Guid InvoiceId = Guid.Parse("0950aaaa-0000-4000-8000-000000000001");
    private static readonly Guid FirmId = Guid.Parse("0950aaaa-0000-4000-8000-0000000000f1");
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The findings of a submission whose firm was set through Set firm (095d-1): a real stored decision with <paramref name="rulesRerun"/>.</summary>
    internal static string FindingsWithAssignFirm(string rulesRerun, Guid? firmId = null) =>
        WithAssignFirm(new JsonObject { ["pipelineFindings"] = new JsonArray(), ["lineFindings"] = new JsonArray() }, rulesRerun, firmId).ToJsonString();

    /// <summary>Adds the stored Set-firm decision (<c>assignFirm</c>) with <paramref name="rulesRerun"/> to <paramref name="findings"/>; it names
    /// <paramref name="firmId"/> (this file's firm when null).</summary>
    internal static JsonObject WithAssignFirm(JsonObject findings, string rulesRerun, Guid? firmId = null)
    {
        var stored = new AssignFirmStoredDecision(
            Guid.NewGuid(), firmId ?? FirmId, null, "INV-7.3", null, "INV-7.1", 1, Guid.NewGuid(), Now,
            new InvoiceSubmissionAfterSteps.ResolvedSignalSet(Now.UtcDateTime, []), [], 0, rulesRerun);
        findings[InvoiceAssignFirmService.StoredDecisionMember] = stored.ToJson();
        return findings;
    }

    /// <summary>The findings with an <c>assignFirm</c> member that is present but cannot be read.</summary>
    internal static string FindingsWithUnreadableAssignFirm() =>
        new JsonObject { [InvoiceAssignFirmService.StoredDecisionMember] = "not a decision" }.ToJsonString();

    private static Entity Invoice(bool withFirm = true)
    {
        var invoice = new Entity("sprk_invoice", InvoiceId);
        if (withFirm)
        {
            invoice[InvoiceSubmissionPersister.InvoiceVendorOrg] = new EntityReference("sprk_organization", FirmId);
        }

        return invoice;
    }

    private static Entity Submission(string? findings, string? reasonCode = null) =>
        new(FinanceBaseValues.SubmissionEntity, Guid.NewGuid())
        {
            [InvoiceSubmissionPersister.SubmissionInvoice] = new EntityReference("sprk_invoice", InvoiceId),
            [InvoiceSubmissionPersister.SubmissionReasonCode] = reasonCode,
            [InvoiceSubmissionPersister.SubmissionFindingsJson] = findings,
        };

    private static Mock<IGenericEntityService> Entities(string? ownHold, IEnumerable<Entity> submissions, Exception? fault = null)
    {
        var entities = new Mock<IGenericEntityService>();
        var rows = submissions.ToList();
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
            {
                if (fault is not null)
                {
                    return Task.FromException<EntityCollection>(fault);
                }

                if (query.EntityName == "sprk_invoice")
                {
                    var own = new Entity("sprk_invoice", InvoiceId);
                    if (ownHold is not null)
                    {
                        own[InvoiceFirmHold.InvoiceHoldColumn] = ownHold;
                    }

                    return Task.FromResult(new EntityCollection([own]));
                }

                return Task.FromResult(new EntityCollection(rows));
            });
        return entities;
    }

    [Fact(DisplayName = "095d-0 must-fire: an invoice with no firm is NoFirm, whatever else is stored")]
    public async Task NoFirm()
    {
        var entities = Entities(null, [Submission(FindingsWithAssignFirm("completed"))]);

        (await InvoiceFirmGate.CheckAsync(entities.Object, Invoice(withFirm: false), CancellationToken.None)).Should().Be(InvoiceFirmGateResult.NoFirm);
    }

    [Theory(DisplayName = "095d-0 must-fire: an invoice with a firm but held for one (its own hold, or a submission storing firm_missing) is HeldForFirm")]
    [InlineData("own")]
    [InlineData("submission-reason")]
    public async Task HeldForFirm(string how)
    {
        var entities = how == "own"
            ? Entities(SubmissionReasonCodes.FirmMissing, [Submission("{\"pipelineFindings\":[],\"lineFindings\":[]}")])
            : Entities(null, [Submission("{\"pipelineFindings\":[],\"lineFindings\":[]}", SubmissionReasonCodes.FirmMissing)]);

        (await InvoiceFirmGate.CheckAsync(entities.Object, Invoice(), CancellationToken.None)).Should().Be(InvoiceFirmGateResult.HeldForFirm);
    }

    [Theory(DisplayName = "095d-0 must-fire: a stored Set-firm decision whose rulesRerun is not 'completed' - pending, any other word - is RerunPending; one that cannot be read is too (fail closed)")]
    [InlineData("pending")]
    [InlineData("running")]
    [InlineData("")]
    [InlineData("unreadable")]
    public async Task RerunPending(string state)
    {
        var findings = state == "unreadable" ? FindingsWithUnreadableAssignFirm() : FindingsWithAssignFirm(state);
        var entities = Entities(null, [Submission(findings)]);

        (await InvoiceFirmGate.CheckAsync(entities.Object, Invoice(), CancellationToken.None)).Should().Be(InvoiceFirmGateResult.RerunPending);
        InvoiceFirmGate.IsRerunPending(findings).Should().BeTrue();
    }

    [Fact(DisplayName = "095d-0 must-fire: one linked submission with the re-run pending is enough, whatever the others store")]
    public async Task AnyLinkedSubmissionWithTheRerunPending()
    {
        var entities = Entities(null, [Submission(FindingsWithAssignFirm("completed")), Submission(FindingsWithAssignFirm("pending"))]);

        (await InvoiceFirmGate.CheckAsync(entities.Object, Invoice(), CancellationToken.None)).Should().Be(InvoiceFirmGateResult.RerunPending);
    }

    [Fact(DisplayName = "095d-0 must-fire: findings that cannot be parsed at all hold the invoice (InvoiceFirmHold's fail closed) - never Open")]
    public async Task UnparseableFindings_AreNeverOpen()
    {
        var entities = Entities(null, [Submission("{ not json")]);

        (await InvoiceFirmGate.CheckAsync(entities.Object, Invoice(), CancellationToken.None)).Should().NotBe(InvoiceFirmGateResult.Open);
    }

    [Fact(DisplayName = "095d-0 must-fire: a read fault is thrown, never read as Open")]
    public async Task ReadFault_Throws()
    {
        var entities = Entities(null, [], fault: new TimeoutException("dataverse"));

        await FluentActions.Awaiting(() => InvoiceFirmGate.CheckAsync(entities.Object, Invoice(), CancellationToken.None))
            .Should().ThrowAsync<TimeoutException>();
    }

    [Fact(DisplayName = "095d-0 must-not-fire: a firm, no hold and a stored Set-firm decision with rulesRerun 'completed' is Open")]
    public async Task Completed_IsOpen()
    {
        var entities = Entities(null, [Submission(FindingsWithAssignFirm(InvoiceAssignFirmService.RulesRerunCompleted))]);

        (await InvoiceFirmGate.CheckAsync(entities.Object, Invoice(), CancellationToken.None)).Should().Be(InvoiceFirmGateResult.Open);
        InvoiceFirmGate.IsRerunPending(FindingsWithAssignFirm("completed")).Should().BeFalse();
    }

    [Theory(DisplayName = "095d-0 must-not-fire: a firm that came with the intake - no assignFirm member, blank findings, no submission at all - is Open")]
    [InlineData("{\"pipelineFindings\":[],\"lineFindings\":[]}")]
    [InlineData("")]
    [InlineData(null)]
    public async Task NoAssignFirmMember_IsOpen(string? findings)
    {
        (await InvoiceFirmGate.CheckAsync(Entities(null, [Submission(findings)]).Object, Invoice(), CancellationToken.None)).Should().Be(InvoiceFirmGateResult.Open);
        (await InvoiceFirmGate.CheckAsync(Entities(null, []).Object, Invoice(), CancellationToken.None)).Should().Be(InvoiceFirmGateResult.Open);
        InvoiceFirmGate.IsRerunPending(findings).Should().BeFalse();
    }

    // ── EB-D-185 R2 / R3 (uac-r2): the marker Set firm leaves, and the order ──

    private const string Marker = InvoiceFirmHold.InvoiceRerunPendingValue;

    [Fact(DisplayName = "095d-0 R3 must-fire: the invoice's own rerun-pending marker is RerunPending with NO linked submission at all (the link removed or re-pointed after Set firm cannot release the gate)")]
    public async Task Marker_WithNoSubmissionFound_IsRerunPending() =>
        (await InvoiceFirmGate.CheckAsync(Entities(Marker, []).Object, Invoice(), CancellationToken.None)).Should().Be(InvoiceFirmGateResult.RerunPending);

    [Theory(DisplayName = "095d-0 R3 must-fire: the marker wins over whatever a submission stores - a re-pointed link to an unrelated submission, or one whose re-run reads 'completed', cannot release it (only the 095d-2 re-run clears the marker)")]
    [InlineData("unrelated")]
    [InlineData("completed")]
    public async Task Marker_WinsOverTheSubmissions(string stored)
    {
        var findings = stored == "completed" ? FindingsWithAssignFirm(InvoiceAssignFirmService.RulesRerunCompleted) : "{\"pipelineFindings\":[],\"lineFindings\":[]}";

        (await InvoiceFirmGate.CheckAsync(Entities(Marker, [Submission(findings)]).Object, Invoice(), CancellationToken.None))
            .Should().Be(InvoiceFirmGateResult.RerunPending);
    }

    [Theory(DisplayName = "095d-0 R3 must-fire (uac-r2 condition 1): any non-empty own-hold value that is not the marker - the firm_missing value, an unknown word, another casing, a padded marker - is HeldForFirm; only empty passes")]
    [InlineData("ebilling.submission.firm_missing")]
    [InlineData("garbage")]
    [InlineData("EBILLING.SUBMISSION.RERUN_PENDING")]
    [InlineData(" ebilling.submission.rerun_pending")]
    [InlineData("ebilling.submission.rerun_completed")]
    public async Task UnknownOwnHoldValue_IsHeld(string value) =>
        (await InvoiceFirmGate.CheckAsync(Entities(value, [Submission(FindingsWithAssignFirm("pending"))]).Object, Invoice(), CancellationToken.None))
            .Should().Be(InvoiceFirmGateResult.HeldForFirm, "an unrecognised value fails closed; the stored hold wins over a pending member");

    [Fact(DisplayName = "095d-0 R3 must-not-fire (uac-r2 condition 1): an EMPTY own hold passes (a firm, no marker, nothing pending is Open)")]
    public async Task EmptyOwnHold_IsOpen() =>
        (await InvoiceFirmGate.CheckAsync(Entities(string.Empty, []).Object, Invoice(), CancellationToken.None)).Should().Be(InvoiceFirmGateResult.Open);

    [Fact(DisplayName = "095d-0 R2 must-fire: the row Set firm leaves - marker, the submission still storing the firm_missing reason, assignFirm pending - is RerunPending, not HeldForFirm")]
    public async Task RealPostSetFirmRow_IsRerunPending() =>
        (await InvoiceFirmGate.CheckAsync(
            Entities(Marker, [Submission(FindingsWithAssignFirm("pending"), SubmissionReasonCodes.FirmMissing)]).Object, Invoice(), CancellationToken.None))
            .Should().Be(InvoiceFirmGateResult.RerunPending);

    [Fact(DisplayName = "095d-0 R2 must-fire: a Set-firm row with NO marker (cleared) but the submission still storing the firm_missing reason and assignFirm pending is RerunPending - the re-run is checked before the hold")]
    public async Task PendingRerun_IsCheckedBeforeTheHold() =>
        (await InvoiceFirmGate.CheckAsync(
            Entities(null, [Submission(FindingsWithAssignFirm("pending"), SubmissionReasonCodes.FirmMissing)]).Object, Invoice(), CancellationToken.None))
            .Should().Be(InvoiceFirmGateResult.RerunPending);

    [Fact(DisplayName = "095d-0 R2 must-fire: findings that cannot be parsed hold the invoice even beside a pending member elsewhere")]
    public async Task UnreadableFindings_BesideAPendingOne_StayHeld() =>
        (await InvoiceFirmGate.CheckAsync(
            Entities(null, [Submission(FindingsWithAssignFirm("pending")), Submission("{ not json")]).Object, Invoice(), CancellationToken.None))
            .Should().Be(InvoiceFirmGateResult.HeldForFirm);

    // ── EB-D-185 R1: Return to Firm after Set firm ──

    [Fact(DisplayName = "095d-0 R1 must-not-fire: Return is open on the row Set firm leaves (marker, firm_missing reason, assignFirm naming the invoice's firm)")]
    public async Task Return_OnTheRealPostSetFirmRow_IsOpen() =>
        (await InvoiceFirmGate.CheckForReturnAsync(
            Entities(Marker, [Submission(FindingsWithAssignFirm("pending"), SubmissionReasonCodes.FirmMissing)]).Object, Invoice(), CancellationToken.None))
            .Should().Be(InvoiceFirmNeed.None);

    [Theory(DisplayName = "095d-0 R1 must-fire: Return stays refused for no firm, an own hold that is not the marker, a firm_missing submission with no matching Set firm (native firm, another firm, unreadable findings)")]
    [InlineData("no-firm")]
    [InlineData("own-firm-missing")]
    [InlineData("own-unknown")]
    [InlineData("native-firm")]
    [InlineData("other-firm")]
    [InlineData("unreadable-findings")]
    [InlineData("unreadable-assign-firm")]
    public async Task Return_StaysRefused(string how)
    {
        var otherFirm = Guid.Parse("0950aaaa-0000-4000-8000-0000000000f2");
        var pending = FindingsWithAssignFirm("pending");
        (string? Own, string? Findings, Entity Invoice) c = how switch
        {
            "no-firm" => (Marker, pending, Invoice(withFirm: false)),
            "own-firm-missing" => (SubmissionReasonCodes.FirmMissing, pending, Invoice()),
            "own-unknown" => ("garbage", pending, Invoice()),
            "native-firm" => (null, "{\"pipelineFindings\":[],\"lineFindings\":[]}", Invoice()),
            "other-firm" => (Marker, FindingsWithAssignFirm("pending", otherFirm), Invoice()),
            "unreadable-findings" => (Marker, "{ not json", Invoice()),
            _ => (Marker, FindingsWithUnreadableAssignFirm(), Invoice()),
        };

        (await InvoiceFirmGate.CheckForReturnAsync(Entities(c.Own, [Submission(c.Findings, SubmissionReasonCodes.FirmMissing)]).Object, c.Invoice, CancellationToken.None))
            .Should().NotBe(InvoiceFirmNeed.None);
    }

    [Fact(DisplayName = "095d-0 R1: Return answers NoFirm for a firmless invoice and Held for the stored firm_missing hold")]
    public async Task Return_NamesTheNeed()
    {
        (await InvoiceFirmGate.CheckForReturnAsync(Entities(null, []).Object, Invoice(withFirm: false), CancellationToken.None)).Should().Be(InvoiceFirmNeed.NoFirm);
        (await InvoiceFirmGate.CheckForReturnAsync(Entities(SubmissionReasonCodes.FirmMissing, []).Object, Invoice(), CancellationToken.None)).Should().Be(InvoiceFirmNeed.Held);
    }

    [Fact(DisplayName = "095d-0 R1: a read fault on Return's check is thrown, never read as None")]
    public async Task Return_ReadFault_Throws() =>
        await FluentActions.Awaiting(() => InvoiceFirmGate.CheckForReturnAsync(Entities(null, [], fault: new TimeoutException("dataverse")).Object, Invoice(), CancellationToken.None))
            .Should().ThrowAsync<TimeoutException>();

    [Fact(DisplayName = "095d-0 R3: the marker is a hold for every other reader (InvoiceFirmHold: held; the pure state reads it as RerunPending, a blank value as None)")]
    public void TheMarker_IsAHoldForEveryOtherReader()
    {
        var invoice = new Entity("sprk_invoice", InvoiceId) { [InvoiceFirmHold.InvoiceHoldColumn] = Marker };

        InvoiceFirmHold.IsInvoiceHeld(invoice).Should().BeTrue();
        InvoiceFirmHold.StateOf(invoice).Should().Be(InvoiceFirmHold.InvoiceHoldState.RerunPending);
        InvoiceFirmHold.StateOf(new Entity("sprk_invoice", InvoiceId) { [InvoiceFirmHold.InvoiceHoldColumn] = " " }).Should().Be(InvoiceFirmHold.InvoiceHoldState.None);
        InvoiceFirmHold.StateOf(new Entity("sprk_invoice", InvoiceId) { [InvoiceFirmHold.InvoiceHoldColumn] = 7 }).Should().Be(InvoiceFirmHold.InvoiceHoldState.Held);
        Marker.Length.Should().BeLessThanOrEqualTo(100, "the column is a 100-character string");
    }

    [Fact(DisplayName = "095d-0: the completed word is the one 095d-2 will write, and the pending word is the one 095d-1 writes today")]
    public void TheSentinels() =>
        (InvoiceAssignFirmService.RulesRerunPending, InvoiceAssignFirmService.RulesRerunCompleted).Should().Be(("pending", "completed"));
}

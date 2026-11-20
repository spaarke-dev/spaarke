using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;
using World = Sprk.Bff.Api.Tests.Services.Finance.FirmFindingsProjectionTests.World;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// <c>tests/integration/tenant/**</c> (ebilling-r1 task 130g, EB-D-259): the firm outcome of a Rejected or Returned-to-firm invoice carries the notice
/// prose the reviewer confirmed, read from the invoice's OWN submission (<c>sprk_firmnoticetext</c>) and re-screened on every read. The projection is
/// the REAL <see cref="FirmFindingsProjection"/> over 130's Dataverse double (<c>World</c>); the firm boundary is that nothing of the stored notice but
/// its screened prose, and nothing internal, reaches a firm, and that another submission's notice is never read.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the stored column is firm-facing source data the BFF alone writes; this is the one place it is shown to a firm, so a
/// loosened screen, a leaked subject or html, or a notice of another invoice would reach an external reader.
/// </remarks>
public sealed class FirmOutcomeNoticeProseTests
{
    private static readonly Guid SubmissionId = Guid.Parse("20000000-0000-4000-8000-0000000000b1");
    private static readonly Guid InvoiceId = InvoiceSubmissionPersister.InvoiceIdFor(SubmissionId);

    private const string Prose = "Your invoice was returned because several timekeeper rates need review. Please check them and send a corrected invoice.";
    private const string SubjectMarker = "SENT-SUBJECT-MARKER";
    private const string HtmlMarker = "SENT-HTML-MARKER";
    private const string Column = "sprk_firmnoticetext";

    private static string Stored(
        string kind, string? prose = Prose, int version = 1, string source = "Reviewer", string subject = SubjectMarker + " (TRK-1)")
    {
        var map = new Dictionary<string, object?>
        {
            ["v"] = version,
            ["source"] = source,
            ["kind"] = kind,
            ["subject"] = subject,
            ["prose"] = prose,
            ["html"] = $"<p>{HtmlMarker}</p>",
            ["htmlOmitted"] = false,
        };
        return JsonSerializer.Serialize(map);
    }

    /// <summary>A decided invoice with one reduced line (so the items are real) and a submission row holding <paramref name="stored"/>.</summary>
    private static World DecidedWorld(int status, string? stored, Guid? submissionId = null, bool withDecidedLine = true)
    {
        var world = new World(status);
        if (withDecidedLine)
        {
            world.AddReducedWithCitation(1, "Rate exceeds the agreed rate.", Guid.Parse("20000000-0000-4000-8000-0000000000d2"), "Your agreed rate is lower.", 100m, 60m);
        }

        if (stored is not null)
        {
            world.Submissions.Add(new Entity("sprk_invoicesubmission", submissionId ?? SubmissionId)
            {
                ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
                [Column] = stored,
            });
        }

        return world;
    }

    private static async Task<FirmOutcome> OutcomeAsync(World world) =>
        (await world.Projection().GetForInvoiceAsync(InvoiceId, CancellationToken.None))!;

    // ── AC1: shown for Rejected and Returned, when the stored kind matches ──

    [Theory(DisplayName = "130g AC1: a Rejected or Returned invoice whose own submission stores its matching notice projects the stored prose, with the items intact")]
    [InlineData(InvoiceStatusCodes.Rejected, FirmOutcomeKind.Rejected, "InvoiceRejected")]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm, FirmOutcomeKind.ReturnedToFirm, "ReturnedToFirm")]
    public async Task RejectedOrReturned_WithItsMatchingStoredNotice_ProjectsThePlainProse(int status, FirmOutcomeKind kind, string storedKind)
    {
        var world = DecidedWorld(status, Stored(storedKind));

        var outcome = await OutcomeAsync(world);

        outcome.Kind.Should().Be(kind);
        outcome.NoticeProse.Should().Be(Prose);
        outcome.Degraded.Should().BeFalse();
        outcome.Items.Should().ContainSingle().Which.Reason.Should().Be("Rate exceeds the agreed rate.");
        world.Reads("sprk_invoicesubmission").Should().Be(1, "one read of the invoice's own submission");
    }

    [Theory(DisplayName = "130g: the notice's source (Reviewer, Action or Template) does not decide whether it is shown: every stored notice was screened before it was sent")]
    [InlineData("Reviewer")]
    [InlineData("Action")]
    [InlineData("Template")]
    public async Task TheStoredSource_IsNotRead_AndNeverExposed(string source)
    {
        var outcome = await OutcomeAsync(DecidedWorld(InvoiceStatusCodes.ReturnedToFirm, Stored("ReturnedToFirm", source: source)));

        outcome.NoticeProse.Should().Be(Prose);
        JsonSerializer.Serialize(outcome).Should().NotContain("\"source\"").And.NotContain("Reviewer").And.NotContain("Action").And.NotContain("Template");
    }

    [Fact(DisplayName = "130g AC1: the stored prose is read only from the invoice's OWN submission; another submission linked to the same invoice is never shown")]
    public async Task ANoticeOnAnotherSubmissionOfTheInvoice_IsNeverShown()
    {
        var other = Guid.Parse("20000000-0000-4000-8000-0000000000b8");
        var world = DecidedWorld(InvoiceStatusCodes.ReturnedToFirm, Stored("ReturnedToFirm", "A different submission's text with no figures."), submissionId: other);

        var outcome = await OutcomeAsync(world);

        outcome.NoticeProse.Should().BeNull("only the submission whose id derives the invoice's is its own");
        world.Reads("sprk_invoicesubmission").Should().Be(1, "control: the linked submission was really read and passed over");
    }

    // ── AC2 must-not-fire ──

    [Theory(DisplayName = "130g AC2 (must-not-fire): In Review, Approved, Approved with adjustments, Sent to AP and Paid carry no prose even when a notice is stored, and the stored notice is not even read")]
    [InlineData(InvoiceStatusCodes.InReview, true, FirmOutcomeKind.InvoiceInReview)]
    [InlineData(InvoiceStatusCodes.Approved, false, FirmOutcomeKind.Approved)]
    [InlineData(InvoiceStatusCodes.Approved, true, FirmOutcomeKind.ApprovedWithAdjustments)]
    [InlineData(InvoiceStatusCodes.SentToAp, true, FirmOutcomeKind.SentToAp)]
    [InlineData(InvoiceStatusCodes.Paid, true, FirmOutcomeKind.Paid)]
    public async Task OtherOutcomes_NeverCarryTheProse(int status, bool withDecidedLine, FirmOutcomeKind expected)
    {
        foreach (var storedKind in new[] { "InvoiceRejected", "ReturnedToFirm" })
        {
            var world = DecidedWorld(status, Stored(storedKind), withDecidedLine: withDecidedLine);

            var outcome = await OutcomeAsync(world);

            outcome.Kind.Should().Be(expected, "control: the intended outcome was really exercised");
            outcome.NoticeProse.Should().BeNull();
            JsonSerializer.Serialize(outcome).Should().NotContain("noticeProse").And.NotContain(Prose);
            world.Reads("sprk_invoicesubmission").Should().Be(0, "an outcome that never shows the prose does not read the column");
        }
    }

    [Fact(DisplayName = "130g AC2 (must-not-fire): a rejected SUBMISSION has no prose even when its own row stores a SubmissionRejected notice")]
    public async Task ARejectedSubmission_NeverCarriesTheProse()
    {
        var world = new World();
        var submission = world.AddRejectedSubmission(findingsJson: null);
        submission[Column] = Stored("SubmissionRejected");

        var outcome = await world.Projection().GetForSubmissionAsync(SubmissionId, invoiceDecided: true, CancellationToken.None);

        outcome!.Kind.Should().Be(FirmOutcomeKind.SubmissionRejected);
        outcome.NoticeProse.Should().BeNull();
        JsonSerializer.Serialize(outcome).Should().NotContain(Prose).And.NotContain(SubjectMarker).And.NotContain(HtmlMarker);
        world.Queries.Where(q => q.EntityName == "sprk_invoicesubmission").SelectMany(q => q.ColumnSet.Columns).Should().NotContain(Column);
    }

    [Theory(DisplayName = "130g AC2 (must-not-fire): a stored notice of another kind than this decision's gives no prose")]
    [InlineData(InvoiceStatusCodes.Rejected, "ReturnedToFirm")]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm, "InvoiceRejected")]
    [InlineData(InvoiceStatusCodes.Rejected, "SubmissionRejected")]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm, "ApprovedWithAdjustments")]
    [InlineData(InvoiceStatusCodes.Rejected, "invoicerejected")]
    [InlineData(InvoiceStatusCodes.Rejected, "")]
    public async Task AKindMismatch_GivesNoProse(int status, string storedKind)
    {
        var world = DecidedWorld(status, Stored(storedKind));

        var outcome = await OutcomeAsync(world);

        outcome.NoticeProse.Should().BeNull();
        outcome.Degraded.Should().BeFalse();
        outcome.Items.Should().HaveCount(1);
        world.Logger.Lines.Should().Contain(l => l.Level == LogLevel.Warning && l.Message.Contains("kind-mismatch") && l.Message.Contains(InvoiceId.ToString()));
    }

    // ── AC3: re-screen, malformed, version, blank, missing ──

    [Theory(DisplayName = "130g AC3: prose the screen refuses (a digit, a currency sign, an amount word, a link, an address, a control character) gives no prose and does not fail the outcome")]
    [InlineData("Your invoice has 3 problems.")]
    [InlineData("You were over by $ on the rates.")]
    [InlineData("You were over by € on the rates.")]
    [InlineData("The rate was about fifty percent too high.")]
    [InlineData("Please see https://example.org for the rules.")]
    [InlineData("Please see www.example.org for the rules.")]
    [InlineData("Write to billing@example.org about it.")]
    [InlineData("Please read the guidelines at example.org today.")]
    [InlineData("Line one of your invoice was reduced.")]
    [InlineData("Your invoice\u0007 was returned.")]
    public async Task ProseTheScreenRefuses_GivesNoProse_AndTheOutcomeStands(string refused)
    {
        var world = DecidedWorld(InvoiceStatusCodes.Rejected, Stored("InvoiceRejected", refused));

        var outcome = await OutcomeAsync(world);

        outcome.NoticeProse.Should().BeNull();
        outcome.Kind.Should().Be(FirmOutcomeKind.Rejected);
        outcome.Degraded.Should().BeFalse();
        outcome.Items.Should().HaveCount(1);
        JsonSerializer.Serialize(outcome).Should().NotContain("noticeProse").And.NotContain(refused);
        // The refusal is logged by its code and the ids: never by the text.
        world.Logger.Lines.Should().Contain(l => l.Level == LogLevel.Warning && l.Message.Contains("refused:") && l.Message.Contains(InvoiceId.ToString()));
        world.Logger.Lines.Should().NotContain(l => l.Message.Contains(refused));
    }

    [Fact(DisplayName = "130g AC3: prose over the screen's length cap gives no prose")]
    public async Task ProseOverTheCap_GivesNoProse()
    {
        var tooLong = string.Join(' ', Enumerable.Repeat("Please review the rates", 80));
        tooLong.Length.Should().BeGreaterThan(FirmNoticeProse.MaxProseLength);

        (await OutcomeAsync(DecidedWorld(InvoiceStatusCodes.Rejected, Stored("InvoiceRejected", tooLong)))).NoticeProse.Should().BeNull();
    }

    [Theory(DisplayName = "130g AC3: malformed JSON, a non-object, an unknown or missing version, a blank or missing or non-string prose and a missing kind give no prose and never fail the outcome")]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("\"just text\"")]
    [InlineData("null")]
    [InlineData("{\"v\":2,\"kind\":\"InvoiceRejected\",\"prose\":\"Please review the rates.\"}")]
    [InlineData("{\"v\":\"1\",\"kind\":\"InvoiceRejected\",\"prose\":\"Please review the rates.\"}")]
    [InlineData("{\"v\":1.5,\"kind\":\"InvoiceRejected\",\"prose\":\"Please review the rates.\"}")]
    [InlineData("{\"kind\":\"InvoiceRejected\",\"prose\":\"Please review the rates.\"}")]
    [InlineData("{\"v\":1,\"prose\":\"Please review the rates.\"}")]
    [InlineData("{\"v\":1,\"kind\":7,\"prose\":\"Please review the rates.\"}")]
    [InlineData("{\"v\":1,\"kind\":\"InvoiceRejected\",\"prose\":\"   \"}")]
    [InlineData("{\"v\":1,\"kind\":\"InvoiceRejected\",\"prose\":\"\"}")]
    [InlineData("{\"v\":1,\"kind\":\"InvoiceRejected\",\"prose\":null}")]
    [InlineData("{\"v\":1,\"kind\":\"InvoiceRejected\",\"prose\":42}")]
    [InlineData("{\"v\":1,\"kind\":\"InvoiceRejected\"}")]
    [InlineData("{\"v\":1,\"kind\":\"InvoiceRejected\",\"prose\":\"Please review the rates.\"")] // truncated
    public async Task AnUnreadableStoredNotice_GivesNoProse_AndTheOutcomeStands(string stored)
    {
        var outcome = await OutcomeAsync(DecidedWorld(InvoiceStatusCodes.Rejected, stored));

        outcome.NoticeProse.Should().BeNull();
        outcome.Kind.Should().Be(FirmOutcomeKind.Rejected);
        outcome.Degraded.Should().BeFalse();
        outcome.Items.Should().HaveCount(1);
    }

    [Fact(DisplayName = "130g AC3 (control): the same minimal stored object with a valid version, kind and prose IS shown, so the cases above fail for their stated reason only")]
    public async Task TheMinimalValidObject_IsShown()
    {
        const string minimal = "{\"v\":1,\"kind\":\"InvoiceRejected\",\"prose\":\"Please review the rates.\"}";

        (await OutcomeAsync(DecidedWorld(InvoiceStatusCodes.Rejected, minimal))).NoticeProse.Should().Be("Please review the rates.");
    }

    [Theory(DisplayName = "130g AC3: a missing own submission, and a submission whose column is blank, give no prose and do not fail the outcome")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoStoredNotice_GivesNoProse(string? stored)
    {
        var world = DecidedWorld(InvoiceStatusCodes.ReturnedToFirm, stored);

        var outcome = await OutcomeAsync(world);

        outcome.NoticeProse.Should().BeNull();
        outcome.Kind.Should().Be(FirmOutcomeKind.ReturnedToFirm);
        outcome.Degraded.Should().BeFalse();
        outcome.Items.Should().HaveCount(1);
        world.Reads("sprk_invoicesubmission").Should().Be(1);
    }

    // ── AC4: nothing else of the stored notice, nothing internal ──

    [Theory(DisplayName = "130g AC4: the stored subject, html and source appear nowhere in the serialized outcome, and neither do the internal reason and note; only the allow-listed keys are present")]
    [InlineData(InvoiceStatusCodes.Rejected, "InvoiceRejected", "reject-invoice")]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm, "ReturnedToFirm", "return-to-firm")]
    public async Task TheSerializedOutcome_CarriesOnlyTheProse_OfTheStoredNotice(int status, string storedKind, string actionCode)
    {
        const string decisionReason = "SENT-DECISION-REASON";
        const string decisionNote = "SENT-DECISION-NOTE";
        var world = DecidedWorld(status, Stored(storedKind));
        world.AddWholeInvoiceDecision(actionCode, decisionReason, decisionNote);

        var outcome = await OutcomeAsync(world);
        var json = JsonSerializer.Serialize(outcome);

        json.Should().NotContain(SubjectMarker).And.NotContain(HtmlMarker).And.NotContain("TRK-1)")
            .And.NotContain(decisionReason).And.NotContain(decisionNote);
        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "Kind", "TrackingNumber", "StatusLabel", "BilledTotal", "ApprovedTotal", "Items", "Degraded", "NoticeProse");
        // Controls: the prose is there, and the store really holds the subject, the html, the reason and the note.
        outcome.NoticeProse.Should().Be(Prose);
        world.Submissions.Single().GetAttributeValue<string>(Column).Should().Contain(SubjectMarker).And.Contain(HtmlMarker);
        world.Records.Should().Contain(r => r.GetAttributeValue<string>("sprk_reason") == decisionReason);
        world.Invoices[0].GetAttributeValue<string>("sprk_decisionnote").Should().Be(decisionNote);
    }

    [Fact(DisplayName = "130g AC4: the read asks for the notice column only (never the findings, the override stamp or any other column of the submission)")]
    public async Task TheSubmissionRead_AsksForTheNoticeColumnOnly()
    {
        var world = DecidedWorld(InvoiceStatusCodes.Rejected, Stored("InvoiceRejected"));

        await OutcomeAsync(world);

        var query = world.Queries.Single(q => q.EntityName == "sprk_invoicesubmission");
        query.ColumnSet.Columns.Should().Equal(Column);
    }

    // ── AC5: a read fault ──

    [Theory(DisplayName = "130g AC5: a fault reading the stored notice leaves the rest of the outcome intact with no prose, and logs a Warning with the invoice id and no text")]
    [InlineData(InvoiceStatusCodes.Rejected, "InvoiceRejected")]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm, "ReturnedToFirm")]
    public async Task AFaultReadingTheNotice_OmitsOnlyTheProse(int status, string storedKind)
    {
        var baseline = await OutcomeAsync(DecidedWorld(status, stored: null));
        var world = DecidedWorld(status, Stored(storedKind));
        world.FaultingEntities.Add("sprk_invoicesubmission");

        var outcome = await OutcomeAsync(world);

        outcome.NoticeProse.Should().BeNull();
        outcome.Degraded.Should().BeFalse("the notice is supplementary: the items are already firm-safe");
        outcome.Should().BeEquivalentTo(baseline, o => o.WithStrictOrdering());
        outcome.Items.Should().HaveCount(1);
        world.Logger.Lines.Should().Contain(l =>
            l.Level == LogLevel.Warning && l.Message.Contains(FirmFindingsProjection.NoticeProseNotShownCode) && l.Message.Contains(InvoiceId.ToString()));
        world.Logger.Lines.Should().NotContain(l => l.Message.Contains(Prose) || l.Message.Contains(SubjectMarker) || l.Message.Contains("is down"));
    }

    [Fact(DisplayName = "130g AC5: a cancelled request is still cancelled while the notice is read: the fault rule never swallows it")]
    public async Task ACancellationWhileReadingTheNotice_Propagates()
    {
        var world = DecidedWorld(InvoiceStatusCodes.Rejected, Stored("InvoiceRejected"));
        using var cts = new CancellationTokenSource();
        world.Entities.Setup(e => e.RetrieveMultipleAsync(
                It.Is<Microsoft.Xrm.Sdk.Query.QueryExpression>(q => q.EntityName == "sprk_invoicesubmission"), It.IsAny<CancellationToken>()))
            .Returns<Microsoft.Xrm.Sdk.Query.QueryExpression, CancellationToken>((_, _) =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        var act = () => world.Projection().GetForInvoiceAsync(InvoiceId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── AC6: the composer never echoes the stored prose into a new notice ──

    private const string StoredMarker = "Marker NOTICEPROSEMARKER from an earlier notice.";

    private static FirmNoticeComposer NoticeComposer(World world, bool aiEnabled)
    {
        var ai = new Mock<IInvoiceAi>(MockBehavior.Strict);
        ai.Setup(a => a.DraftFirmNoticeAsync(It.IsAny<InvoiceFirmNoticeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvoiceFirmNoticeDraft { Subject = "Invoice decided", Body = "Your invoice was decided." });
        var settings = new Mock<IEbillingSettingsReader>();
        settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EbillingSettings { FirmNoticeAiEnabled = aiEnabled });
        var projection = world.Projection();
        return new FirmNoticeComposer(
            (id, ct) => projection.GetForInvoiceAsync(id, ct),
            (id, decided, ct) => projection.GetForSubmissionAsync(id, decided, ct),
            settings.Object, ai.Object, NullLogger<FirmNoticeComposer>.Instance);
    }

    [Theory(DisplayName = "130g AC6: a Return or Reject notice composed AFTER the decision does not contain the stored prose (template, model or reviewer-confirmed); the projection it reads does carry it")]
    [InlineData(InvoiceStatusCodes.Rejected, FirmNoticeKind.InvoiceRejected, false)]
    [InlineData(InvoiceStatusCodes.Rejected, FirmNoticeKind.InvoiceRejected, true)]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm, FirmNoticeKind.ReturnedToFirm, false)]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm, FirmNoticeKind.ReturnedToFirm, true)]
    public async Task TheComposer_NeverEchoesTheStoredProse(int status, FirmNoticeKind kind, bool aiEnabled)
    {
        var world = DecidedWorld(status, Stored(kind.ToString(), StoredMarker));
        var composer = NoticeComposer(world, aiEnabled);

        var drafted = await composer.ComposeForInvoiceAsync(kind, InvoiceId, CancellationToken.None);
        var confirmed = await composer.ComposeConfirmedForInvoiceAsync(kind, InvoiceId, "Invoice decided", "Please review the rates and respond.", CancellationToken.None);

        drafted.Status.Should().Be(FirmNoticeStatus.Composed);
        confirmed.Status.Should().Be(ConfirmedFirmNoticeStatus.Composed);
        foreach (var draft in new[] { drafted.Draft!, confirmed.Draft! })
        {
            draft.Subject.Should().NotContain("NOTICEPROSEMARKER");
            draft.Prose.Should().NotContain("NOTICEPROSEMARKER");
            draft.Html.Should().NotContain("NOTICEPROSEMARKER").And.NotContain("earlier notice");
        }

        // Controls: the stored prose really reached the projection the composer reads, and the notices really were built from it.
        (await OutcomeAsync(world)).NoticeProse.Should().Be(StoredMarker);
        drafted.Draft!.Html.Should().Contain("Rate exceeds the agreed rate.");
        confirmed.Draft!.Prose.Should().Be("Please review the rates and respond.");
    }
}

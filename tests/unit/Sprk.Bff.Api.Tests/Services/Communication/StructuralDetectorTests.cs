using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Detectors;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Rung 3 structural-detector tests: each detector resolves category + obligations from message
/// structure (positive + negative), the rung aggregates them (metadata-only, direction-symmetric),
/// and the engine treats a metadata-only detection as NON-resolving (stays Pending Review) — the
/// guard that keeps a bare "this is a calendar invite" from falsely marking the communication Resolved.
/// </summary>
public class StructuralDetectorTests
{
    private static NormalizedMessage Msg(string? subject = null, string? body = null, params NormalizedAttachment[] attachments) =>
        new()
        {
            Direction = CommunicationDirection.Incoming,
            Subject = subject,
            BodyText = body,
            Attachments = attachments,
        };

    // ── CalendarInvite ────────────────────────────────────────────────────────
    [Fact]
    public void CalendarInvite_WithIcsAttachment_ResolvesEventCategoryAndObligation()
    {
        var m = new CalendarInviteDetector().Detect(
            Msg("Meeting", attachments: new NormalizedAttachment { Name = "invite.ics", ContentType = "text/calendar" }));

        m.Should().NotBeNull();
        m!.Category.Should().Be("event");
        m.Obligations.Should().Contain("calendar-response");
        m.Confidence.Should().BeInRange(0.70, 0.95);
        m.Target.Should().BeNull(); // specific sprk_event resolution deferred
    }

    [Fact]
    public void CalendarInvite_WithoutIcs_ReturnsNull() =>
        new CalendarInviteDetector().Detect(Msg("Meeting", body: "let's meet")).Should().BeNull();

    // ── ESignCompletion ───────────────────────────────────────────────────────
    [Fact]
    public void ESign_DocuSignCompleted_ResolvesExecutedDocumentObligation()
    {
        var m = new ESignCompletionDetector().Detect(
            Msg("Completed: NDA", body: "Your DocuSign document has been completed by all parties."));

        m.Should().NotBeNull();
        m!.Category.Should().Be("esign-completion");
        m.Obligations.Should().Contain("executed-document");
        m.Confidence.Should().BeInRange(0.70, 0.95);
    }

    [Fact]
    public void ESign_OrdinaryCompletedProse_ReturnsNull() =>
        new ESignCompletionDetector().Detect(Msg("Task completed", body: "I completed the report.")).Should().BeNull();

    // ── InvoiceNumber ─────────────────────────────────────────────────────────
    [Theory]
    [InlineData("Payment for INV-10453")]
    [InlineData("Invoice #10453 attached")]
    [InlineData("Invoice No. 10453 due")]
    public void Invoice_WithNumber_ResolvesInvoiceCategoryWithNumberInProvenance(string subject)
    {
        var m = new InvoiceNumberDetector().Detect(Msg(subject));

        m.Should().NotBeNull();
        m!.Category.Should().Be("invoice");
        m.Provenance.Should().Contain("10453");
        m.Confidence.Should().BeInRange(0.70, 0.95);
    }

    [Fact]
    public void Invoice_WithoutNumber_ReturnsNull() =>
        new InvoiceNumberDetector().Detect(Msg("Re: our discussion")).Should().BeNull();

    // ── CourtEFiling ──────────────────────────────────────────────────────────
    [Theory]
    [InlineData("Notice of Electronic Filing - Case No. 2:26-cv-00123")]
    [InlineData("NOTICE OF HEARING scheduled")]
    public void Court_WithEFilingOrNotice_ResolvesDeadlineObligation(string subject)
    {
        var m = new CourtEFilingDetector().Detect(Msg(subject));

        m.Should().NotBeNull();
        m!.Category.Should().Be("court-notice");
        m.Obligations.Should().Contain("deadline-response");
    }

    [Fact]
    public void Court_OrdinaryText_ReturnsNull() =>
        new CourtEFilingDetector().Detect(Msg("Notice: office closed Friday")).Should().BeNull();

    // ── Rung aggregation + symmetry ───────────────────────────────────────────
    [Fact]
    public async Task Rung_AggregatesDetectors_AsMetadataOnlyMatches()
    {
        var rung = new StructuralDetectorRung(new IStructuralDetector[]
        {
            new CalendarInviteDetector(), new ESignCompletionDetector(),
            new InvoiceNumberDetector(), new CourtEFilingDetector(),
        });

        var matches = await rung.EvaluateAsync(
            Msg("Invoice #10453", attachments: new NormalizedAttachment { Name = "x.ics", ContentType = "text/calendar" }),
            new AssociationContext(), CancellationToken.None);

        matches.Should().HaveCount(2); // calendar + invoice
        matches.Should().OnlyContain(m => m.Rung == RungKind.StructuralDetector && m.Target == null && m.RegardingFieldName == null);
        matches.Select(m => m.Category).Should().Contain(new[] { "event", "invoice" });
    }

    [Fact]
    public async Task Rung_DirectionSymmetry()
    {
        var rung = new StructuralDetectorRung(new IStructuralDetector[] { new InvoiceNumberDetector() });
        var inbound = await rung.EvaluateAsync(Msg("INV-10453") with { Direction = CommunicationDirection.Incoming }, new AssociationContext(), CancellationToken.None);
        var outbound = await rung.EvaluateAsync(Msg("INV-10453") with { Direction = CommunicationDirection.Outgoing }, new AssociationContext(), CancellationToken.None);
        inbound.Should().BeEquivalentTo(outbound);
        inbound.Should().ContainSingle();
    }

    // ── Engine guard: metadata-only detection does NOT resolve the association ──
    [Fact]
    public async Task Engine_MetadataOnlyDetection_StaysPendingReview()
    {
        var dv = new Mock<IDataverseService>();
        dv.Setup(d => d.UpdateAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var engine = new IncomingAssociationResolver(
            new IAssociationRung[] { new StructuralDetectorRung(new IStructuralDetector[] { new CalendarInviteDetector() }) },
            dv.Object, dv.Object, Mock.Of<ILogger<IncomingAssociationResolver>>());

        var commId = Guid.NewGuid();
        await engine.ResolveAsync(
            commId,
            Msg("Meeting", attachments: new NormalizedAttachment { Name = "invite.ics", ContentType = "text/calendar" }),
            new AssociationContext(), CancellationToken.None);

        // Calendar invite is a metadata-only detection (no regarding target) → Pending Review, no regarding fields.
        dv.Verify(d => d.UpdateAsync("sprk_communication", commId,
            It.Is<Dictionary<string, object>>(f =>
                ((OptionSetValue)f["sprk_associationstatus"]).Value == 100000001 &&
                !f.ContainsKey("sprk_regardingmatter") && !f.ContainsKey("sprk_regardingevent")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

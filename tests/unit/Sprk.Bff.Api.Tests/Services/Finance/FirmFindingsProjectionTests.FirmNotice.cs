using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 131, review round 1 (F2-b): the firm notice over the REAL <see cref="FirmFindingsProjection"/> and 130's Dataverse double
/// (<c>World</c>). A partial of the projection's test class so it can use that rig as it is.
/// </summary>
public sealed partial class FirmFindingsProjectionTests
{
    [Fact(DisplayName = "131 review 1 (model 7.4): through the REAL projection, no override reason, Procedure name or Procedure id reaches the model request or the notice")]
    public async Task FirmNotice_NoOverrideReasonProcedureNameOrProcedureId_ReachesTheModelOrTheNotice()
    {
        const string overrideReason = "SENT-OVERRIDE-REASON";
        const string procedureName = "SENT-PROCEDURE-NAME";
        const string ruleSetName = "SENT-RULESET-NAME";

        // A line the reviewer reduced (reason and firm reason are firm-visible by design), with an override record against the SAME line whose reason
        // must never be shown, a Procedure that has a name and an id, and a rule set that has a name. The clause flag is on, so the Procedure is read.
        var world = new World();
        world.Procedures[0]["sprk_name"] = procedureName;
        world.RuleSets[0]["sprk_name"] = ruleSetName;
        var (line, _) = world.AddReducedWithCitation(1, "Shown reason.", VersionA, "Shown firm reason");
        world.AddRecord("override-submission", line.Id, overrideReason, InvoiceId);
        world.Clause = _ => FlagOnText;

        var requests = new List<InvoiceFirmNoticeRequest>();
        var ai = new Mock<IInvoiceAi>(MockBehavior.Strict);
        ai.Setup(a => a.DraftFirmNoticeAsync(It.IsAny<InvoiceFirmNoticeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InvoiceFirmNoticeRequest request, CancellationToken _) =>
            {
                requests.Add(request);
                // A model that repeats what it was told: whatever reached the request would reach the notice.
                return new InvoiceFirmNoticeDraft
                {
                    Subject = "Invoice returned for correction",
                    Body = "Your invoice was returned. " + string.Join(" ", request.Findings.Select(f => f.Issue)),
                };
            });
        var settings = new Mock<IEbillingSettingsReader>();
        settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EbillingSettings { FirmNoticeAiEnabled = true });
        var projection = world.Projection();
        var composer = new FirmNoticeComposer(
            (id, ct) => projection.GetForInvoiceAsync(id, ct),
            (id, decided, ct) => projection.GetForSubmissionAsync(id, decided, ct),
            settings.Object, ai.Object, NullLogger<FirmNoticeComposer>.Instance);

        var result = await composer.ComposeForInvoiceAsync(FirmNoticeKind.ReturnedToFirm, InvoiceId, CancellationToken.None);

        result.Status.Should().Be(FirmNoticeStatus.Composed);
        var draft = result.Draft!;

        // The leak check comes first: none of the three, in the subject, the prose, the HTML or the request the model was sent.
        var everything = new[]
        {
            draft.Subject, draft.Prose, draft.Html, JsonSerializer.Serialize(requests),
        };
        foreach (var text in everything)
        {
            text.Should().NotContain(overrideReason).And.NotContain(procedureName).And.NotContain(ruleSetName);
            text.Should().NotContainEquivalentOf(ProcedureA.ToString("D")).And.NotContainEquivalentOf(ProcedureA.ToString("N"));
        }

        // Controls (so the check above is not vacuous): the notice was really built from the projection, the override record really was in the store
        // against the same line, and the Procedure was really read, so its name and id were within reach.
        draft.Source.Should().Be(FirmNoticeSource.Action);
        draft.Html.Should().Contain("Shown reason.").And.Contain(FirmLabel).And.Contain("Shown firm reason", "the firm-visible data is in the notice");
        world.Records.Should().Contain(r => r.GetAttributeValue<string>("sprk_reason") == overrideReason);
        world.Reads("sprk_procedure").Should().BeGreaterThan(0);
        requests.Should().ContainSingle();
    }

    private static FirmNoticeComposer NoticeComposer(World world, bool aiEnabled, List<InvoiceFirmNoticeRequest>? requests = null)
    {
        var ai = new Mock<IInvoiceAi>(MockBehavior.Strict);
        ai.Setup(a => a.DraftFirmNoticeAsync(It.IsAny<InvoiceFirmNoticeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InvoiceFirmNoticeRequest request, CancellationToken _) =>
            {
                requests?.Add(request);
                // A model that repeats what it was told: whatever reached the request would reach the notice.
                return new InvoiceFirmNoticeDraft
                {
                    Subject = "Invoice decided",
                    Body = "Your invoice was decided. " + string.Join(" ", request.Findings.Select(f => f.Issue)),
                };
            });
        var settings = new Mock<IEbillingSettingsReader>();
        settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EbillingSettings { FirmNoticeAiEnabled = aiEnabled });
        var projection = world.Projection();
        return new FirmNoticeComposer(
            (id, ct) => projection.GetForInvoiceAsync(id, ct),
            (id, decided, ct) => projection.GetForSubmissionAsync(id, decided, ct),
            settings.Object, ai.Object, NullLogger<FirmNoticeComposer>.Instance);
    }

    [Theory(DisplayName = "130f (EB-D-259): the notice for a Reject and for a Return, over the REAL projection, renders the findings table the firm saw In Review")]
    [InlineData(InvoiceStatusCodes.Rejected, FirmNoticeKind.InvoiceRejected)]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm, FirmNoticeKind.ReturnedToFirm)]
    public async Task DecidedNotice_RendersTheFindingsTable(int status, FirmNoticeKind kind)
    {
        var world = new World(status);
        world.AddReducedWithCitation(1, "Rate exceeds the agreed rate.", VersionA, "Your agreed rate is lower.", 100m, 60m);
        var disallowed = world.AddLine(2, InvoiceLineStatus.Disallowed, 80m, 0m);
        world.AddRecord("disallow-line", disallowed.Id, "Block billing.", InvoiceId);
        world.Clause = _ => FlagOnText;

        var result = await NoticeComposer(world, aiEnabled: false).ComposeForInvoiceAsync(kind, InvoiceId, CancellationToken.None);

        result.Status.Should().Be(FirmNoticeStatus.Composed);
        var html = result.Draft!.Html;
        html.Should().Contain(FirmNoticeFallbackText.FindingsHeading).And.Contain("Rate exceeds the agreed rate.").And.Contain("Block billing.")
            .And.Contain("Reduced").And.Contain("Not allowed").And.Contain("Your agreed rate is lower.").And.Contain(FirmLabel);
        html.Should().NotContain("Invoice adjustment");
    }

    [Theory(DisplayName = "130f K4: the notice table of a Rejected or Returned invoice has no Approved column and no approved amount, and so does the preview of a Reject or Return while still In Review; Approved-with-adjustments keeps it")]
    [InlineData(InvoiceStatusCodes.Rejected, FirmNoticeKind.InvoiceRejected, false)]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm, FirmNoticeKind.ReturnedToFirm, false)]
    [InlineData(InvoiceStatusCodes.InReview, FirmNoticeKind.InvoiceRejected, false)]
    [InlineData(InvoiceStatusCodes.InReview, FirmNoticeKind.ReturnedToFirm, false)]
    [InlineData(InvoiceStatusCodes.Approved, FirmNoticeKind.ApprovedWithAdjustments, true)]
    public async Task Notice_TheApprovedColumn_IsShownOnlyWhenSomethingCouldBeApproved(int status, FirmNoticeKind kind, bool showsApproved)
    {
        var world = new World(status);
        world.AddReducedWithCitation(1, "Rate exceeds the agreed rate.", VersionA, "Your agreed rate is lower.", 100m, 60m);
        world.Clause = _ => FlagOnText;

        var result = await NoticeComposer(world, aiEnabled: false).ComposeForInvoiceAsync(kind, InvoiceId, CancellationToken.None);

        result.Status.Should().Be(FirmNoticeStatus.Composed);
        var html = result.Draft!.Html;
        // The other column labels are untouched either way; the amounts: 100.00 billed (always), 60.00 approved (only with the column).
        html.Should().Contain(">Item<").And.Contain(">Decision<").And.Contain(">Billed<").And.Contain(">Reason<").And.Contain(">Rule<").And.Contain("100.00");
        if (showsApproved)
        {
            html.Should().Contain(">Approved<").And.Contain("60.00");
        }
        else
        {
            html.Should().NotContain(">Approved<").And.NotContain("60.00").And.NotContain("Approved total");
            // Six cells per row became five: one th and one td fewer than the Approved-column layout.
            System.Text.RegularExpressions.Regex.Matches(html, "<th ").Count.Should().Be(5);
            System.Text.RegularExpressions.Regex.Matches(html, "<td ").Count.Should().Be(5);
        }
    }

    [Theory(DisplayName = "130f must-not-fire (EB-D-259): a reject or return reason and note, and an override reason, seeded in the store, appear in no part of the notice or of the model request")]
    [InlineData(InvoiceStatusCodes.Rejected, FirmNoticeKind.InvoiceRejected, "reject-invoice")]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm, FirmNoticeKind.ReturnedToFirm, "return-to-firm")]
    public async Task DecidedNotice_NoDecisionReasonNoteOrOverrideReason_ReachesTheNotice(int status, FirmNoticeKind kind, string actionCode)
    {
        const string decisionReason = "SENT-DECISION-REASON";
        const string decisionNote = "SENT-DECISION-NOTE";
        const string overrideReason = "SENT-OVERRIDE-REASON";
        var world = new World(status);
        var (line, _) = world.AddReducedWithCitation(1, "Shown reason.", VersionA, "Shown firm reason");
        world.AddWholeInvoiceDecision(actionCode, decisionReason, decisionNote);
        world.AddRecord("override-submission", line.Id, overrideReason, InvoiceId);
        world.Clause = _ => FlagOnText;

        foreach (var aiEnabled in new[] { false, true })
        {
            var requests = new List<InvoiceFirmNoticeRequest>();
            var result = await NoticeComposer(world, aiEnabled, requests).ComposeForInvoiceAsync(kind, InvoiceId, CancellationToken.None);

            result.Status.Should().Be(FirmNoticeStatus.Composed);
            var draft = result.Draft!;
            foreach (var text in new[] { draft.Subject, draft.Prose, draft.Html, JsonSerializer.Serialize(requests) })
            {
                text.Should().NotContain(decisionReason).And.NotContain(decisionNote).And.NotContain(overrideReason);
            }

            // Controls: the firm-visible findings are in the notice, so the check above is not vacuous.
            draft.Html.Should().Contain("Shown reason.").And.Contain("Shown firm reason");
            draft.Source.Should().Be(aiEnabled ? FirmNoticeSource.Action : FirmNoticeSource.Template);
        }

        world.Records.Should().Contain(r => r.GetAttributeValue<string>("sprk_reason") == decisionReason);
        world.Records.Should().Contain(r => (r.GetAttributeValue<string>("sprk_steps") ?? string.Empty).Contains(decisionNote));
        world.Invoices[0].GetAttributeValue<string>("sprk_decisionnote").Should().Be(decisionNote);
        world.Records.Should().Contain(r => r.GetAttributeValue<string>("sprk_reason") == overrideReason);
    }

    [Theory(DisplayName = "130f: a fault reading the items of a Rejected or Returned invoice gives no notice (the projection's fail-safe outcome is Degraded)")]
    [InlineData(InvoiceStatusCodes.Rejected, FirmNoticeKind.InvoiceRejected)]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm, FirmNoticeKind.ReturnedToFirm)]
    public async Task DecidedNotice_AFaultReadingTheItems_IsUnavailable(int status, FirmNoticeKind kind)
    {
        var world = new World(status);
        world.AddReducedWithCitation(1, "Reason.", VersionA, "Firm reason.");
        world.FaultingEntities.Add("sprk_decisionrecord");

        var result = await NoticeComposer(world, aiEnabled: false).ComposeForInvoiceAsync(kind, InvoiceId, CancellationToken.None);

        result.Status.Should().Be(FirmNoticeStatus.Unavailable);
        result.Draft.Should().BeNull();
    }
}

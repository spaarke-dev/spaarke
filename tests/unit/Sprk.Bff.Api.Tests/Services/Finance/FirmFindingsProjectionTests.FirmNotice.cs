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
}

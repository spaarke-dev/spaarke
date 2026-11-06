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
/// ebilling-r1 task 095d-0 (EB-D-148): <see cref="InvoiceFirmGate"/> - no firm, held for one, or the post-Set-firm re-run pending refuses; a firm
/// with no re-run in flight is open. Over an in-memory model of the two app-only reads it makes (the invoice's own hold, the linked submissions).
/// </summary>
public sealed class InvoiceFirmGateTests
{
    private static readonly Guid InvoiceId = Guid.Parse("0950aaaa-0000-4000-8000-000000000001");
    private static readonly Guid FirmId = Guid.Parse("0950aaaa-0000-4000-8000-0000000000f1");
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The findings of a submission whose firm was set through Set firm (095d-1): a real stored decision with <paramref name="rulesRerun"/>.</summary>
    internal static string FindingsWithAssignFirm(string rulesRerun) =>
        WithAssignFirm(new JsonObject { ["pipelineFindings"] = new JsonArray(), ["lineFindings"] = new JsonArray() }, rulesRerun).ToJsonString();

    /// <summary>Adds the stored Set-firm decision (<c>assignFirm</c>) with <paramref name="rulesRerun"/> to <paramref name="findings"/>.</summary>
    internal static JsonObject WithAssignFirm(JsonObject findings, string rulesRerun)
    {
        var stored = new AssignFirmStoredDecision(
            Guid.NewGuid(), FirmId, null, "INV-7.3", null, "INV-7.1", 1, Guid.NewGuid(), Now,
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

    [Fact(DisplayName = "095d-0: the completed word is the one 095d-2 will write, and the pending word is the one 095d-1 writes today")]
    public void TheSentinels() =>
        (InvoiceAssignFirmService.RulesRerunPending, InvoiceAssignFirmService.RulesRerunCompleted).Should().Be(("pending", "completed"));
}

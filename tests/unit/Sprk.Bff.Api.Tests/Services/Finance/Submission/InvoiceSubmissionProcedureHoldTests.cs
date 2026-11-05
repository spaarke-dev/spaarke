using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.Domain.Signals.Billing;
using Xunit;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 082c-2 (brief 082c C11, C14, C16, C18): what a Procedure hold stores and what readers see of it. The outcome is the real
/// processor's (over the processor harness); the findings JSON is the persister's builder; the system findings are part D's, read back from it.
/// </summary>
public sealed class InvoiceSubmissionProcedureHoldTests
{
    private static readonly Guid InvoiceId = Guid.Parse("082c2000-0000-4000-8000-0000000000a1");
    private static readonly BillingSystemPolicyVersion System = new(
        Guid.Parse("082c2000-0000-4000-8000-0000000000b1"), BillingRuleCatalogue.SystemPolicyCode, Guid.Parse("082c2000-0000-4000-8000-0000000000b2"));

    private static ProcedureResolution Ended(DateOnly from, DateOnly to)
    {
        var kit = new ProcedureKit().Procedure("Ended", from: from, to: to);
        kit.Rule("Ended", "ENDED-INCREMENT", RuleType.TimeIncrement);
        return kit.Build();
    }

    /// <summary>An SPA gap over a 073 rejection: the aged 20 January line (InvoiceAge rejects it) and the invoice date are after the version ended.</summary>
    private static async Task<SubmissionEvaluationOutcome> SpaGapOverARejectionAsync()
    {
        var h = new ProcessorHarness
        {
            Procedures = Ended(new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 10)),
            Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()],
        };
        h.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
        return await h.RunAsync();
    }

    private static BillingRuleCandidate SystemRule() => new()
    {
        PolicyId = System.PolicyId,
        PolicyCode = System.PolicyCode,
        PolicyVersionId = System.PolicyVersionId,
        RuleSetId = Guid.Empty,
        Scope = RuleSetScope.ClientDefault,
        RuleType = "100000014",
        RuleBody = "{}",
        Consequence = (int)BillingConsequence.Flag,
        IsCurrent = true,
        VersionNumber = 1,
    };

    /// <summary>The reader-facing text of a stored outcome's Procedure parts: its pipeline and line findings, and the system Signal part D maps
    /// from them (headline, rendered sentence, fact values).</summary>
    private static string ReaderText(string findingsJson)
    {
        var root = JsonNode.Parse(findingsJson)!.AsObject();
        var stored = InvoiceSubmissionAfterSteps.StoredFindings.Parse(findingsJson)!;
        var system = stored.SystemFindings(System);
        var mapped = system.Count == 0
            ? []
            : BillingFindingSignalMapper.MapAll(system, new Dictionary<Guid, BillingRuleCandidate> { [System.PolicyVersionId] = SystemRule() },
                new BillingSignalSubjects(InvoiceId, "INV-100", new Dictionary<int, Guid>()));
        return string.Join("\n", new[]
        {
            root["pipelineFindings"]!.ToJsonString(),
            root[InvoiceSubmissionPersister.LineFindingsMember]!.ToJsonString(),
        }.Concat(mapped.SelectMany(m => new[]
        {
            m.Request.ShortHeadline,
            SignalWriter.RenderSentence(m.Request.MessageTemplate, m.Request.FactValues),
            string.Join("|", m.Request.FactValues.Values.Select(v => v?.ToString())),
        })));
    }

    // ── The open item of brief section 6: a rejects=true finding under the Accepted kind (SPA) ──

    [Fact(DisplayName = "082c-2 C11 (brief section 6 open item): an SPA gap over a 073 rejection stores kind Accepted + procedure_gap, the deciding finding rejects=true, and its Signal presented as held, not rejected")]
    public async Task SpaGapOverARejection_FindingsJson_ShowsTheRejectingFindingUnderAccepted()
    {
        var outcome = await SpaGapOverARejectionAsync();

        var json = InvoiceSubmissionPersister.BuildFindingsJson(outcome, InvoiceId);
        var root = JsonNode.Parse(json)!.AsObject();

        root["kind"]!.GetValue<string>().Should().Be(nameof(SubmissionEvaluationKind.Accepted));
        root["reasonCode"]!.GetValue<string>().Should().Be(SubmissionReasonCodes.ProcedureGap);
        root["decision"]!["outcome"]!.GetValue<string>().Should().Be(nameof(SubmissionOutcome.Rejected));
        root["findings"]!.AsArray().OfType<JsonObject>().Should().Contain(f => f["rejects"]!.GetValue<bool>(),
            "the 073 deciding finding keeps its rejects=true; the submission is held, not rejected");
        root["signalPlan"]!.AsArray().OfType<JsonObject>().Should().NotBeEmpty()
            .And.OnlyContain(e => e["decision"]!.GetValue<string>() != nameof(BillingGroupDecision.Rejects),
                "an Accepted invoice's Signal never presents a rejection the firm did not receive");
        InvoiceSubmissionPersister.TerminalFor(outcome).Should().Be((InvoiceSubmissionStatus.Accepted, SubmissionReasonCodes.ProcedureGap));
        var stored = InvoiceSubmissionAfterSteps.StoredFindings.Parse(json)!;
        stored.Kind.Should().Be(SubmissionEvaluationKind.Accepted);
        stored.SystemFindings(System).Should().ContainSingle(f => f.ReasonCode == BillingReasonCodes.SubmissionProcedureGap);
    }

    // ── C16: the sentinel scan ──

    [Fact(DisplayName = "082c-2 C16 sentinel, must-fire controls: a BRR- code and a GUID in either form are found")]
    public void Sentinel_MustFire()
    {
        var id = Guid.NewGuid();

        ReaderSentinel.Scan("rule " + ProcedureAssignmentServiceCode(id)).Should().NotBeEmpty();
        ReaderSentinel.Scan("row " + id.ToString("D")).Should().NotBeEmpty();
        ReaderSentinel.Scan("row " + id.ToString("N")).Should().NotBeEmpty();
    }

    private static string ProcedureAssignmentServiceCode(Guid id) =>
        Sprk.Bff.Api.Services.Finance.Procedures.ProcedureAssignmentService.RecordRuleCodePrefix + id.ToString("N");

    [Fact(DisplayName = "082c-2 C16 sentinel, must-not-fire: a normal gap finding's reader text (pipeline, lines, Signal) holds no id; the scan is not vacuous")]
    public async Task Sentinel_GapReaderText_HoldsNoId()
    {
        var h = new ProcessorHarness { Procedures = Ended(new DateOnly(2025, 1, 1), new DateOnly(2026, 3, 10)) };
        var outcome = await h.RunAsync();

        var text = ReaderText(InvoiceSubmissionPersister.BuildFindingsJson(outcome, InvoiceId));

        text.Should().Contain("Ended").And.Contain(BillingRuleCatalogue.ProcedureGapHeadline, "control: the scan reads the gap's text");
        ReaderSentinel.Scan(text).Should().BeEmpty();
    }

    [Fact(DisplayName = "082c-2 C16 sentinel: a final-delivery fault whose message names ids reaches no reader text (pipeline, lines, Signal, status view)")]
    public async Task Sentinel_UnresolvedReaderText_HoldsNoExceptionMessage()
    {
        var planted = "Procedure row " + Guid.NewGuid().ToString("D") + " names " + ProcedureAssignmentServiceCode(Guid.NewGuid());
        var h = new ProcessorHarness { EvaluationFault = new ProcedureResolutionFaultException(planted), FinalDelivery = true };
        var outcome = await h.RunAsync();

        var json = InvoiceSubmissionPersister.BuildFindingsJson(outcome, InvoiceId);
        var text = ReaderText(json) + "\n" + JsonSerializer.Serialize(SubmissionHoldWarning.For(outcome.ReasonCode));

        text.Should().Contain(BillingRuleCatalogue.ProceduresUnresolvedHeadline, "control");
        text.Should().NotContain("Procedure row");
        ReaderSentinel.Scan(text).Should().BeEmpty();
        json.Should().NotContain("Procedure row", "the whole stored copy, not only its reader parts");
    }

    // ── 3(c) / C18: the status view's warning ──

    [Theory(DisplayName = "082c-2 3(c): the status view's warning is derived from the reason code with the owner-approved text; absent for any other reason")]
    [InlineData(SubmissionReasonCodes.ProcedureGap, SubmissionHoldWarning.ProcedureGapMessage)]
    [InlineData(SubmissionReasonCodes.ProceduresUnresolved, SubmissionHoldWarning.ProceduresUnresolvedMessage)]
    [InlineData(SubmissionReasonCodes.FirmMissing, null)]
    [InlineData(SubmissionReasonCodes.Blocked, null)]
    [InlineData(null, null)]
    public void StatusView_Warning_IsDerivedFromTheReasonCode(string? reason, string? message)
    {
        var row = new Microsoft.Xrm.Sdk.Entity("sprk_invoicesubmission", Guid.NewGuid())
        {
            ["sprk_outcomereasoncode"] = reason,
            ["sprk_processingstatus"] = new Microsoft.Xrm.Sdk.OptionSetValue((int)InvoiceSubmissionStatus.Accepted),
        };

        var view = InvoiceSubmissionService.ToView(row);
        var json = JsonSerializer.Serialize(view, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        if (message is null)
        {
            view.Warning.Should().BeNull();
            json.Should().NotContain("\"warning\"", "omitted when null");
            return;
        }

        view.Warning.Should().Be(new SubmissionHoldWarning(reason!, message));
        JsonNode.Parse(json)!["warning"]!["message"]!.GetValue<string>().Should().Be(message);
        ReaderSentinel.Scan(json.Replace(view.SubmissionId.ToString("D"), string.Empty)).Should().BeEmpty();
    }

    [Fact(DisplayName = "082c-2 3(c), R4 wording: the two texts are the owner-approved ones verbatim and name no Procedure, date or count")]
    public void Warning_Texts_AreTheApprovedOnes()
    {
        SubmissionHoldWarning.ProcedureGapMessage.Should().Be(
            "We received this invoice. It has not been rejected, but it is on hold for review: no billing terms are set up for some of its work dates on the record you chose. Check that you chose the right record and that the work dates are correct. If both are right, you don't need to do anything; the client will follow up.");
        SubmissionHoldWarning.ProceduresUnresolvedMessage.Should().Be(
            "We received this invoice. It has not been rejected, but it is on hold for review because its billing terms could not be checked. You don't need to do anything; the client will follow up.");
        foreach (var text in new[] { SubmissionHoldWarning.ProcedureGapMessage, SubmissionHoldWarning.ProceduresUnresolvedMessage })
        {
            text.Should().NotContainAny("procedure", "Procedure").And.NotMatchRegex(@"\d");
        }
    }
}

using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

// ebilling-r1 task 095d-0 (EB-D-185; ontology-r1 2026-11-05): no Override without a firm. The Override verb is refused 409 ebilling.override.firm_missing while
// the invoice has no firm, is held for one, or its post-Set-firm re-run is pending (InvoiceFirmGate) - an ordinary release and the 114 Procedure-hold release
// alike - BEFORE the holds derivation, the Signal set's store, the Decision Record and every Signal close, and AFTER the caller's rights (403 first).
// Return is not gated.
public partial class SubmissionOverrideServiceTests
{
    // AfterSetFirm is the row Set firm really leaves (EB-D-185 R2/R3): the invoice's own hold is the rerun-pending marker and the overridden
    // submission stores a pending assignFirm; AfterSetFirmMarkerOnly is the marker alone (the submission link removed or re-pointed, so nothing of
    // the re-run is found through it); UnknownOwnValue is an unrecognised word in the invoice's own hold (fail closed).
    public enum GateState { NoFirm, HeldOwnColumn, HeldBySubmission, RerunPending, RerunUnreadable, AfterSetFirm, AfterSetFirmMarkerOnly, UnknownOwnValue }

    public enum OverrideKind { Ordinary, ProcedureHoldInternal, ProcedureHoldSpa }

    public static TheoryData<OverrideKind, GateState> AllGateCombinations()
    {
        var data = new TheoryData<OverrideKind, GateState>();
        foreach (var kind in Enum.GetValues<OverrideKind>())
        {
            foreach (var state in Enum.GetValues<GateState>())
            {
                data.Add(kind, state);
            }
        }

        return data;
    }

    private static InvoiceFirmGateResult ExpectedGate(GateState state) => state switch
    {
        GateState.NoFirm => InvoiceFirmGateResult.NoFirm,
        GateState.HeldOwnColumn or GateState.HeldBySubmission or GateState.UnknownOwnValue => InvoiceFirmGateResult.HeldForFirm,
        _ => InvoiceFirmGateResult.RerunPending,
    };

    /// <summary>A world of <paramref name="kind"/> in <paramref name="state"/>, with the shared SubmissionIntegrity Signal and a rule Signal open, so a close would show.</summary>
    private static async Task<(World W, Guid Shared, Guid Rule)> GatedWorldAsync(OverrideKind kind, GateState state)
    {
        Func<JsonObject, JsonObject>? edit = state switch
        {
            GateState.RerunPending or GateState.AfterSetFirm => json => InvoiceFirmGateTests.WithAssignFirm(json, InvoiceAssignFirmService.RulesRerunPending),
            GateState.RerunUnreadable => json =>
            {
                json[InvoiceAssignFirmService.StoredDecisionMember] = "not a decision";
                return json;
            }
            ,
            _ => null,
        };
        var w = kind switch
        {
            OverrideKind.Ordinary => await World.CreateAsync(edit),
            OverrideKind.ProcedureHoldInternal => await World.CreateHeldAsync(InvoiceSubmissionChannel.Internal, editFindings: edit),
            _ => await World.CreateHeldAsync(InvoiceSubmissionChannel.Spa, editFindings: edit),
        };
        switch (state)
        {
            case GateState.NoFirm:
                w.Invoice!.Attributes.Remove(InvoiceSubmissionPersister.InvoiceVendorOrg);
                break;
            case GateState.HeldOwnColumn:
                w.InvoiceOwnHold = FirmMissing;
                break;
            case GateState.HeldBySubmission:
                w.OtherSubmissions.Add(OtherSubmission(FirmMissing, "{\"pipelineFindings\":[],\"lineFindings\":[]}"));
                break;
            case GateState.AfterSetFirm:
            case GateState.AfterSetFirmMarkerOnly:
                w.InvoiceOwnHold = InvoiceFirmHold.InvoiceRerunPendingValue;
                break;
            case GateState.UnknownOwnValue:
                w.InvoiceOwnHold = "ebilling.submission.something_new";
                break;
        }

        return (w, AddSharedIntegritySignal(w), w.AddSignal("sprk_invoice", World.InvoiceId, "EB-AGE"));
    }

    private static void AssertRefusedByTheFirmGate(World w, BlockedDecisionResult result, GateState state)
    {
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("ebilling.override.firm_missing");
        result.Detail.Should().Be(BlockedSubmissionErrors.FirmDetailFor(ExpectedGate(state)));
        w.RecordCreates.Should().Be(0, "no Decision Record");
        w.Recorded.Should().BeEmpty("the Decision Record writer was never reached");
        w.Closes.Should().BeEmpty("no Signal closed");
        w.ClosedBy.Should().BeEmpty();
        w.SetStores.Should().BeEmpty("the Signal set was not stored: a refused request leaves nothing for a retry to trip over");
        w.Signals.Should().OnlyContain(s => s.GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value == SignalWriter.SignalStatusOpen);
        w.Signals.Should().OnlyContain(s => !s.Contains("sprk_decisionrecord"));
        w.Transactions.Should().BeEmpty("no release transaction");
        w.InvoiceDetail.Should().BeNull("no review-detail write");
        w.AssertNothingWritten();
        JsonNode.Parse(w.FindingsJson)!.AsObject().ContainsKey(SubmissionOverrideService.HumanDecisionSetMember).Should().BeFalse("nothing in the stored findings either");
    }

    [Theory(DisplayName = "095d-0 must-fire: an Override - ordinary, Procedure hold on an Internal or an SPA submission - on an invoice with no firm, held for one, or with the Set-firm re-run pending (or its stored decision unreadable) is 409 ebilling.override.firm_missing; nothing stored, recorded or closed")]
    [MemberData(nameof(AllGateCombinations))]
    public async Task Override_WithoutAnOpenFirmGate_IsRefused_AndNothingIsWritten(OverrideKind kind, GateState state)
    {
        var (w, _, _) = await GatedWorldAsync(kind, state);

        var result = await w.OverrideAsync();

        AssertRefusedByTheFirmGate(w, result, state);
    }

    [Fact(DisplayName = "095d-0 must-fire: a fault reading the firm gate is 503 before any write (fail closed)")]
    public async Task Override_WhenTheFirmGateCannotBeRead_IsUnavailable_BeforeAnyWrite()
    {
        var w = await World.CreateAsync();
        AddSharedIntegritySignal(w);
        w.HoldReadFault = new TimeoutException("a message that names row " + Guid.NewGuid());

        var result = await w.OverrideAsync();

        ShouldBeUnavailable(result);
        w.RecordCreates.Should().Be(0);
        w.Closes.Should().BeEmpty();
        w.SetStores.Should().BeEmpty();
        w.AssertNothingWritten();
        w.Log.Entries.Should().NotContain(e => e.Message.Contains("names row", StringComparison.Ordinal), "the log names the exception type, never its message");
    }

    [Fact(DisplayName = "095d-0 order: the 409 comes BEFORE the Signal set is stored - a refusal and its retry on a firm that has since been set decides with exactly one record")]
    public async Task Override_Refused_ThenTheFirmIsSet_TheRetryDecidesOnce()
    {
        var (w, shared, rule) = await GatedWorldAsync(OverrideKind.ProcedureHoldInternal, GateState.NoFirm);

        (await w.OverrideAsync()).StatusCode.Should().Be(409);
        w.SetStores.Should().BeEmpty();

        w.Invoice!["sprk_vendororg"] = new EntityReference("sprk_organization", World.Firm); // Set firm and its re-run finished
        var retry = await w.OverrideAsync();

        retry.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.RecordCreates.Should().Be(1);
        w.Recorded.Single().ResolvedSignalIds.Should().BeEquivalentTo([shared, rule]);
        w.ClosedBy.Keys.Should().BeEquivalentTo([shared, rule]);
    }

    // ── Must-not-fire ──

    [Theory(DisplayName = "095d-0 must-not-fire: a firm, no hold and a stored Set-firm decision whose rulesRerun is 'completed' - an Override proceeds (ordinary and Procedure hold)")]
    [InlineData(OverrideKind.Ordinary)]
    [InlineData(OverrideKind.ProcedureHoldInternal)]
    public async Task Override_WhenTheRerunIsCompleted_Proceeds(OverrideKind kind)
    {
        Func<JsonObject, JsonObject> edit = json => InvoiceFirmGateTests.WithAssignFirm(json, InvoiceAssignFirmService.RulesRerunCompleted);
        var w = kind == OverrideKind.Ordinary
            ? await World.CreateAsync(edit)
            : await World.CreateHeldAsync(InvoiceSubmissionChannel.Internal, editFindings: edit);

        var result = await w.OverrideAsync();

        result.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.RecordCreates.Should().Be(1);
    }

    [Fact(DisplayName = "095d-0 must-not-fire: a firm that came with the intake - no assignFirm member - an Override proceeds (the existing 114 tests run on this shape; pinned once here)")]
    public async Task Override_WithAFirmAndNoAssignFirmMember_Proceeds()
    {
        var w = await World.CreateAsync();

        JsonNode.Parse(w.FindingsJson)!.AsObject().ContainsKey(InvoiceAssignFirmService.StoredDecisionMember).Should().BeFalse("control");
        (await w.OverrideAsync()).Kind.Should().Be(BlockedDecisionKind.Decided);
    }

    [Theory(DisplayName = "095d-0 must-not-fire: a Return stays available on an invoice with no firm, held for one, or with the re-run pending (Return applies no guideline and pays no one)")]
    [InlineData(GateState.NoFirm)]
    [InlineData(GateState.HeldOwnColumn)]
    [InlineData(GateState.RerunPending)]
    public async Task Return_IsNotGated(GateState state)
    {
        var (w, _, _) = await GatedWorldAsync(OverrideKind.Ordinary, state);

        var result = await w.ReturnAsync();

        result.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.RecordCreates.Should().Be(1);
    }

    // ── Order: the caller's rights first (403), then the gate (409) ──

    [Theory(DisplayName = "095d-0 order: a caller without the Override privilege on an invoice that is not open gets 403 ebilling.override.not_permitted, not 409 - the gate reads nothing for them")]
    [InlineData(GateState.NoFirm)]
    [InlineData(GateState.HeldOwnColumn)]
    [InlineData(GateState.RerunPending)]
    public async Task Override_WithoutThePrivilege_OnAnInvoiceThatIsNotOpen_Is403_NotConflict(GateState state)
    {
        var (w, _, _) = await GatedWorldAsync(OverrideKind.Ordinary, state);
        w.Probe.Release(EbillingOverridePrivilege.PrivilegeName);
        w.HoldReadFault = new TimeoutException("dataverse"); // were the gate reached, this would be a 503

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.NotPermitted);
        w.AssertNothingWritten();
    }

    [Theory(DisplayName = "095d-0 order: a caller with the privilege but without the rights to close the invoice's Signals (AppendTo on the core, Write on the invoice) on an invoice that is not open gets 403 ebilling.blocked.not_authorized, not 409")]
    [InlineData(GateState.NoFirm)]
    [InlineData(GateState.HeldOwnColumn)]
    [InlineData(GateState.RerunPending)]
    public async Task Override_WithoutTheCloseRights_OnAnInvoiceThatIsNotOpen_Is403_NotConflict(GateState state)
    {
        var (w, _, _) = await GatedWorldAsync(OverrideKind.Ordinary, state);
        w.Probe.Rights = ReadOnlyRights;

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.NotAuthorized);
        w.AssertNothingWritten();
    }

    [Fact(DisplayName = "095d-0: the firm_missing sentences are one per gate state, none names a firm, and the code is the override family's")]
    public void TheSentences()
    {
        BlockedSubmissionErrors.FirmMissing.Should().Be("ebilling.override.firm_missing");
        var sentences = new[] { InvoiceFirmGateResult.NoFirm, InvoiceFirmGateResult.HeldForFirm, InvoiceFirmGateResult.RerunPending }
            .Select(BlockedSubmissionErrors.FirmDetailFor).ToList();
        sentences.Should().OnlyHaveUniqueItems();
        sentences.Last().Should().Be("This invoice's firm was just assigned and its billing rules are being checked again. Try again when the check finishes.");
    }
}

using System.Globalization;
using System.ServiceModel;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// ebilling-r1 task 049 (D-122): <see cref="SignalWriter.CloseActedByDecisionAsync"/>, the close of a Signal resolved by an AUTOMATED
/// decision's Decision Record (the automatic rejection of an invoice). Closes only an open Signal, or one the system closed after the caller
/// read its set; never a person's resolution; one row-version conditional write with no person in <c>sprk_resolvedby</c>. Tasks 100 and 105
/// (the HumanClose_ tests): with a decider, the same write also names the person in <c>sprk_resolvedby</c>.
/// </summary>
/// <remarks>Every option value asserted here is read from the SpaarkeMaster solution source by its label (<see cref="SchemaOption"/>), and
/// <see cref="AutomatedClose_TheWritersOptionConstants_MatchTheSolutionSource"/> pins the writer's constants to the same source (ADR-038
/// project rule: an option value is checked against the real schema, not only against the constant the code uses).</remarks>
public partial class SignalWriterTests
{
    private static readonly Guid ClosedSignalId = Guid.Parse("04900000-0000-4000-8000-0000000c1051");
    private static readonly Guid AutoRecordId = Guid.Parse("04900000-0000-4000-8000-0000000c10de");
    private static readonly Guid OtherRecordId = Guid.Parse("04900000-0000-4000-8000-0000000c10df");
    private static readonly DateTime SetReadAt = new(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset CloseNow = new(2026, 10, 19, 9, 5, 0, TimeSpan.Zero);

    // ── The real schema: option values by label, from the solution source ──

    private static int SchemaOption(string column, string label)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs")))
        {
            dir = dir.Parent;
        }

        var path = Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Repository root not found."),
            "src", "dataverse", "solutions", "SpaarkeMaster", "Entities", "sprk_Signal", "Entity.xml");
        return XDocument.Load(path).Descendants("attribute")
            .Single(a => (string?)a.Element("LogicalName") == column)
            .Descendants("option")
            .Where(o => o.Descendants("label").Any(l => (string?)l.Attribute("languagecode") == "1033" && (string?)l.Attribute("description") == label))
            .Select(o => int.Parse((string)o.Attribute("value")!, CultureInfo.InvariantCulture))
            .Single();
    }

    private static int Status(string label) => SchemaOption("sprk_signalstatus", label);

    private static int Resolution(string label) => SchemaOption("sprk_resolutiontype", label);

    [Theory(DisplayName = "049 D-122: the writer's status and resolution constants are the solution source's options of that label")]
    [InlineData("sprk_signalstatus", "Open", SignalWriter.SignalStatusOpen)]
    [InlineData("sprk_signalstatus", "Acknowledged", SignalWriter.SignalStatusAcknowledged)]
    [InlineData("sprk_signalstatus", "Resolved", SignalWriter.SignalStatusResolved)]
    [InlineData("sprk_resolutiontype", "Acted", SignalWriter.ResolutionActed)]
    [InlineData("sprk_resolutiontype", "Dismissed", SignalWriter.ResolutionDismissed)]
    [InlineData("sprk_resolutiontype", "Condition Cleared", SignalWriter.ResolutionConditionCleared)]
    [InlineData("sprk_resolutiontype", "Superseded", SignalWriter.ResolutionSuperseded)]
    [InlineData("sprk_resolutiontype", "Policy Retired", SignalWriter.ResolutionPolicyRetired)]
    public void AutomatedClose_TheWritersOptionConstants_MatchTheSolutionSource(string column, string label, int constant) =>
        SchemaOption(column, label).Should().Be(constant);

    // ── Harness ──

    private static (SignalWriter Writer, FakeOrganizationService Org, CapturingLogger<SignalWriter> Log) AutomatedCloseRig(
        string status, string? resolution = null, DateTime? resolvedOn = null, Guid? record = null)
    {
        var org = new FakeOrganizationService();
        var row = new Entity("sprk_signal", ClosedSignalId) { RowVersion = "1", ["sprk_signalstatus"] = new OptionSetValue(Status(status)) };
        if (resolution is not null) row["sprk_resolutiontype"] = new OptionSetValue(Resolution(resolution));
        if (resolvedOn is { } on) row["sprk_resolvedon"] = on;
        if (record is { } id) row["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", id);
        org.Rows[ClosedSignalId] = row;
        var log = new CapturingLogger<SignalWriter>();
        return (Build(org, new FakeSysadminClient(), new FakeTimeProvider(CloseNow), log), org, log);
    }

    private static Task<SignalAutomatedCloseOutcome> Close(SignalWriter writer) =>
        writer.CloseActedByDecisionAsync(ClosedSignalId, AutoRecordId, SetReadAt, "BILL-AGE");

    // ── Closes ──

    [Theory(DisplayName = "049 D-122: an Open or Acknowledged Signal is closed Acted with exactly status, resolution, resolvedon = lastevaluated, the record; no resolvedby, no notes")]
    [InlineData("Open")]
    [InlineData("Acknowledged")]
    public async Task AutomatedClose_AnOpenSignal_IsClosedActed_WithNoPerson(string status)
    {
        var (writer, org, _) = AutomatedCloseRig(status);

        var outcome = await Close(writer);

        outcome.Should().Be(SignalAutomatedCloseOutcome.Closed);
        var (id, fields) = org.ConditionalUpdates.Should().ContainSingle().Subject;
        id.Should().Be(ClosedSignalId);
        fields.Keys.Should().BeEquivalentTo("sprk_signalstatus", "sprk_resolutiontype", "sprk_resolvedon", "sprk_lastevaluated", "sprk_decisionrecord");
        fields["sprk_signalstatus"].Should().Be(new OptionSetValue(Status("Resolved")));
        fields["sprk_resolutiontype"].Should().Be(new OptionSetValue(Resolution("Acted")));
        fields["sprk_resolvedon"].Should().Be(CloseNow.UtcDateTime);
        fields["sprk_lastevaluated"].Should().Be(fields["sprk_resolvedon"], "D-120: the same instant, never a 'cleared observed' marker");
        fields["sprk_decisionrecord"].Should().Be(new EntityReference("sprk_decisionrecord", AutoRecordId));
        fields.Should().NotContainKey("sprk_resolvedby", "an automated decider has no person, and none is invented");
        fields.Should().NotContainKey("sprk_resolutionnotes");
        org.UpdateCalls.Should().BeEmpty("the only write is the row-version conditional one");
        org.RetrievedColumns.Should().ContainSingle(r => r.Entity == "sprk_signal").Which.Columns.Should().BeEquivalentTo(
            ["sprk_signalstatus", "sprk_resolutiontype", "sprk_resolvedon", "sprk_decisionrecord", "versionnumber"],
            "the decision reads exactly these columns: a missing one (resolvedon) silently changes what the guards see");
    }

    [Theory(DisplayName = "049 D-122: a Signal the system closed AFTER the set was read is re-closed with the record, so Signal and record agree")]
    [InlineData("Condition Cleared")]
    [InlineData("Superseded")]
    [InlineData("Policy Retired")]
    public async Task AutomatedClose_SystemClosedAfterTheSetWasRead_IsReclosed(string resolution)
    {
        var (writer, org, _) = AutomatedCloseRig("Resolved", resolution, SetReadAt.AddMinutes(1));

        (await Close(writer)).Should().Be(SignalAutomatedCloseOutcome.Closed);

        var fields = org.ConditionalUpdates.Should().ContainSingle().Subject.Fields;
        fields["sprk_resolutiontype"].Should().Be(new OptionSetValue(Resolution("Acted")));
        fields["sprk_decisionrecord"].Should().Be(new EntityReference("sprk_decisionrecord", AutoRecordId));
    }

    // ── Human decisions (tasks 100 and 105): the same close, naming the person ──

    private static readonly Guid DeciderUserId = Guid.Parse("10000000-0000-4000-8000-0000000c1d5e");

    [Theory(DisplayName = "100/105 D-122: a human decision's close carries sprk_resolvedby = the systemuser in the ONE conditional write; never notes")]
    [InlineData("Open")]
    [InlineData("Acknowledged")]
    public async Task HumanClose_AnOpenSignal_IsClosedActed_NamingThePerson_InTheOneConditionalWrite(string status)
    {
        var (writer, org, _) = AutomatedCloseRig(status);

        var outcome = await writer.CloseActedByDecisionAsync(ClosedSignalId, AutoRecordId, SetReadAt, "BILL-AGE", DeciderUserId);

        outcome.Should().Be(SignalAutomatedCloseOutcome.Closed);
        var (id, fields) = org.ConditionalUpdates.Should().ContainSingle().Subject;
        id.Should().Be(ClosedSignalId);
        fields.Keys.Should().BeEquivalentTo(
            "sprk_signalstatus", "sprk_resolutiontype", "sprk_resolvedon", "sprk_lastevaluated", "sprk_decisionrecord", "sprk_resolvedby");
        fields["sprk_resolutiontype"].Should().Be(new OptionSetValue(Resolution("Acted")), "every human outcome closes as Acted, never Dismissed");
        fields["sprk_resolvedby"].Should().Be(new EntityReference("systemuser", DeciderUserId));
        fields["sprk_decisionrecord"].Should().Be(new EntityReference("sprk_decisionrecord", AutoRecordId));
        fields.Should().NotContainKey("sprk_resolutionnotes");
        org.UpdateCalls.Should().BeEmpty("the person is stamped in the same conditional write, never a second one");
    }

    [Fact(DisplayName = "100/105 D-122: a null decider (the automated caller, 049) writes no sprk_resolvedby - byte-identical to before")]
    public async Task HumanClose_NullDecider_WritesNoResolvedBy()
    {
        var (writer, org, _) = AutomatedCloseRig("Open");

        (await writer.CloseActedByDecisionAsync(ClosedSignalId, AutoRecordId, SetReadAt, "BILL-AGE", decidedByUserId: null))
            .Should().Be(SignalAutomatedCloseOutcome.Closed);

        org.ConditionalUpdates.Should().ContainSingle().Which.Fields.Should().NotContainKey("sprk_resolvedby");
    }

    [Fact(DisplayName = "100/105 D-122: a person's earlier resolution is still left as it is by a human decision's close (same read-then-decide rules)")]
    public async Task HumanClose_APersonsResolution_IsLeftAsIs()
    {
        var (writer, org, _) = AutomatedCloseRig("Resolved", "Dismissed", SetReadAt.AddMinutes(1));

        (await writer.CloseActedByDecisionAsync(ClosedSignalId, AutoRecordId, SetReadAt, "BILL-AGE", DeciderUserId))
            .Should().Be(SignalAutomatedCloseOutcome.LeftAsIs);

        org.ConditionalUpdates.Should().BeEmpty();
        org.UpdateCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "100/105 D-122: an empty decider id is refused before any read (null is the automated form, never Guid.Empty)")]
    public async Task HumanClose_EmptyDecider_IsRefused()
    {
        var (writer, org, _) = AutomatedCloseRig("Open");

        var act = () => writer.CloseActedByDecisionAsync(ClosedSignalId, AutoRecordId, SetReadAt, "BILL-AGE", Guid.Empty);

        await act.Should().ThrowAsync<ArgumentException>();
        org.RetrievedColumns.Should().BeEmpty();
    }

    // ── Leaves ──

    [Fact(DisplayName = "049 D-122: a Signal already Resolved by THIS record is not written again")]
    public async Task AutomatedClose_AlreadyClosedByThisRecord_WritesNothing()
    {
        var (writer, org, _) = AutomatedCloseRig("Resolved", "Acted", SetReadAt.AddMinutes(2), AutoRecordId);

        (await Close(writer)).Should().Be(SignalAutomatedCloseOutcome.AlreadyClosedByThisRecord);

        org.ConditionalUpdates.Should().BeEmpty();
        org.UpdateCalls.Should().BeEmpty("no unconditional write either");
    }

    [Fact(DisplayName = "049 D-122: a system close with NO resolvedon is not 'after the read' - left as it is, nothing written (a null time never passes the guard)")]
    public async Task AutomatedClose_SystemClosedWithNoResolvedOn_IsLeftAsIs()
    {
        var (writer, org, _) = AutomatedCloseRig("Resolved", "Condition Cleared", resolvedOn: null);

        (await Close(writer)).Should().Be(SignalAutomatedCloseOutcome.LeftAsIs);

        org.ConditionalUpdates.Should().BeEmpty();
        org.UpdateCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 D-122: a system close at exactly the instant the set was read counts as after the read (>=, not >) and is re-closed")]
    public async Task AutomatedClose_SystemClosedAtExactlyTheSetReadTime_IsReclosed()
    {
        var (writer, org, _) = AutomatedCloseRig("Resolved", "Condition Cleared", SetReadAt);

        (await Close(writer)).Should().Be(SignalAutomatedCloseOutcome.Closed);

        org.ConditionalUpdates.Should().ContainSingle();
        org.UpdateCalls.Should().BeEmpty();
    }

    [Theory(DisplayName = "049 D-122: a person's resolution, another record's close, or a system close before the set was read is left as it is, with one Information line (ids and code only)")]
    [InlineData("Acted", false, true)]
    [InlineData("Dismissed", false, false)]
    [InlineData("Acted", false, false)]
    [InlineData("Condition Cleared", true, false)]
    [InlineData("Superseded", true, false)]
    public async Task AutomatedClose_APersonsOrAnEarlierResolution_IsLeftAsIs(string resolution, bool systemBeforeTheSet, bool otherRecord)
    {
        var (writer, org, log) = AutomatedCloseRig("Resolved", resolution,
            systemBeforeTheSet ? SetReadAt.AddMinutes(-1) : SetReadAt.AddMinutes(1), otherRecord ? OtherRecordId : null);

        (await Close(writer)).Should().Be(SignalAutomatedCloseOutcome.LeftAsIs);

        org.ConditionalUpdates.Should().BeEmpty("a person's resolution is never overwritten");
        org.UpdateCalls.Should().BeEmpty("nor by an unconditional write");
        var info = log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Information).Subject;
        info.Message.Should().Contain(ClosedSignalId.ToString()).And.Contain("BILL-AGE").And.Contain(AutoRecordId.ToString());
        log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Theory(DisplayName = "K1 (ontology-r1 review of #1911): a system close after the set was read that already names a Decision Record is left as it is - the write would replace that link")]
    [InlineData("Condition Cleared")]
    [InlineData("Superseded")]
    [InlineData("Policy Retired")]
    public async Task AutomatedClose_ASystemCloseLinkedToARecord_IsLeftAsIs_AndNothingIsWritten(string resolution)
    {
        var (writer, org, log) = AutomatedCloseRig("Resolved", resolution, SetReadAt.AddMinutes(1), OtherRecordId);

        (await writer.CloseActedByDecisionAsync(ClosedSignalId, AutoRecordId, SetReadAt, "BILL-AGE", DeciderUserId))
            .Should().Be(SignalAutomatedCloseOutcome.LeftAsIs);

        org.ConditionalUpdates.Should().BeEmpty("the other record's link is never overwritten");
        org.UpdateCalls.Should().BeEmpty();
        log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Information);
    }

    // ── Concurrency ──

    [Fact(DisplayName = "049 D-122: a version mismatch re-reads once and decides again; the second write closes it")]
    public async Task AutomatedClose_OneMismatch_ReReadsOnce_ThenCloses()
    {
        var (writer, org, _) = AutomatedCloseRig("Open");
        var moved = false;
        org.BeforeConditionalUpdate = id =>
        {
            if (moved) return;
            moved = true;
            org.Rows[id].RowVersion = "2"; // a concurrent refresh between the read and the write
        };

        (await Close(writer)).Should().Be(SignalAutomatedCloseOutcome.Closed);

        org.RetrievedColumns.Count(r => r.Entity == "sprk_signal").Should().Be(2, "one read, one re-read");
        org.ConditionalUpdates.Should().ContainSingle();
        org.UpdateCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 D-122: the re-read sees the Signal already Resolved by THIS record (a concurrent run closed it) -> AlreadyClosedByThisRecord, no second write")]
    public async Task AutomatedClose_ConcurrentRunClosedItWithThisRecord_IsAlreadyClosedOnTheReRead()
    {
        var (writer, org, _) = AutomatedCloseRig("Open");
        org.BeforeConditionalUpdate = id =>
        {
            org.BeforeConditionalUpdate = null;
            org.Rows[id]["sprk_signalstatus"] = new OptionSetValue(Status("Resolved"));
            org.Rows[id]["sprk_resolutiontype"] = new OptionSetValue(Resolution("Acted"));
            org.Rows[id]["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", AutoRecordId);
            org.Rows[id].RowVersion = "2";
        };

        (await Close(writer)).Should().Be(SignalAutomatedCloseOutcome.AlreadyClosedByThisRecord);

        org.ConditionalUpdates.Should().BeEmpty();
        org.UpdateCalls.Should().BeEmpty();
        org.RetrievedColumns.Count(r => r.Entity == "sprk_signal").Should().Be(2, "one read, one re-read");
    }

    [Fact(DisplayName = "049 D-122: a person resolving it between the read and the write is seen on the re-read and left as it is")]
    public async Task AutomatedClose_APersonResolvesBetweenReadAndWrite_IsLeftAsIs()
    {
        var (writer, org, _) = AutomatedCloseRig("Open");
        org.BeforeConditionalUpdate = id =>
        {
            org.BeforeConditionalUpdate = null;
            org.Rows[id]["sprk_signalstatus"] = new OptionSetValue(Status("Resolved"));
            org.Rows[id]["sprk_resolutiontype"] = new OptionSetValue(Resolution("Dismissed"));
            org.Rows[id].RowVersion = "2";
        };

        (await Close(writer)).Should().Be(SignalAutomatedCloseOutcome.LeftAsIs);

        org.ConditionalUpdates.Should().BeEmpty();
        org.UpdateCalls.Should().BeEmpty();
        org.Rows[ClosedSignalId].GetAttributeValue<OptionSetValue>("sprk_resolutiontype").Value.Should().Be(Resolution("Dismissed"));
    }

    [Fact(DisplayName = "049 D-122: two mismatches -> ChangedSinceRead, nothing written")]
    public async Task AutomatedClose_TwoMismatches_ChangedSinceRead()
    {
        var (writer, org, _) = AutomatedCloseRig("Open");
        var version = 1;
        org.BeforeConditionalUpdate = id => org.Rows[id].RowVersion = (++version).ToString(CultureInfo.InvariantCulture);

        (await Close(writer)).Should().Be(SignalAutomatedCloseOutcome.ChangedSinceRead);

        org.ConditionalUpdates.Should().BeEmpty();
        org.UpdateCalls.Should().BeEmpty();
        org.RetrievedColumns.Count(r => r.Entity == "sprk_signal").Should().Be(2, "re-read once, not more");
    }

    // ── Failure ──

    [Fact(DisplayName = "049 D-122: a Dataverse fault is logged at Error (WriteRefused), metered as signal_close_failed and rethrown")]
    public async Task AutomatedClose_ADataverseFault_IsLoggedMeteredAndRethrown()
    {
        var scope = Guid.NewGuid();
        FailureMetricScope.Value = scope;
        var (listener, reasons) = ListenFailureReasonsScoped(scope);
        using var _ = listener;
        var (writer, org, log) = AutomatedCloseRig("Open");
        org.ConditionalUpdateFault = new FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040265), Message = "Generic SQL error." }, "Generic SQL error.");

        var act = () => Close(writer);

        await act.Should().ThrowAsync<FaultException<OrganizationServiceFault>>();
        reasons().Should().Equal(OntologyWriterFailureReason.SignalCloseFailed);
        var error = log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        error.EventId.Id.Should().Be(OntologyWriterEvents.WriteRefused.Id);
        error.Field("PolicyCode").Should().Be("BILL-AGE");
        org.UpdateCalls.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 D-122: a privilege refusal is metered as DataverseAccessDenied (not signal_close_failed), logged at Error and rethrown")]
    public async Task AutomatedClose_AnAccessDeniedFault_IsMeteredAsDataverseAccessDenied()
    {
        var scope = Guid.NewGuid();
        FailureMetricScope.Value = scope;
        var (listener, reasons) = ListenFailureReasonsScoped(scope);
        using var _ = listener;
        var (writer, org, log) = AutomatedCloseRig("Open");
        org.ConditionalUpdateFault = new FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040220), Message = "does not have Write right(s) for entity sprk_signal." },
            "does not have Write right(s) for entity sprk_signal.");

        var act = () => Close(writer);

        await act.Should().ThrowAsync<FaultException<OrganizationServiceFault>>();
        reasons().Should().Equal(OntologyWriterFailureReason.DataverseAccessDenied);
        log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which.Field("Reason").Should().Be(OntologyWriterFailureReason.DataverseAccessDenied);
        org.UpdateCalls.Should().BeEmpty();
    }

    private static FaultException<OrganizationServiceFault> Fault(int code, string message) =>
        new(new OrganizationServiceFault { ErrorCode = code, Message = message }, message);

    // R4 (adversarial review F2): a Signal deleted after the Decision Record listed it is not a write failure. The caller
    // (InvoiceSubmissionAfterSteps) logs one Warning and goes on; the writer must not also log Error and meter signal_close_failed.
    [Theory(DisplayName = "049 R4: a Signal that no longer exists (ObjectDoesNotExist) is rethrown as it was - no signal_close_failed metric, no Error log")]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("wrapped")]
    public async Task AutomatedClose_ASignalThatNoLongerExists_IsRethrownUnmeteredAndNotLoggedAtError(string whereItIsGone)
    {
        var scope = Guid.NewGuid();
        FailureMetricScope.Value = scope;
        var (listener, reasons) = ListenFailureReasonsScoped(scope);
        using var _ = listener;
        var (writer, org, log) = AutomatedCloseRig("Open");
        var gone = Fault(unchecked((int)0x80040217), "sprk_signal With Id = " + ClosedSignalId + " Does Not Exist");
        if (whereItIsGone == "write") org.ConditionalUpdateFault = gone;
        else org.SignalRetrieveFault = whereItIsGone == "wrapped" ? new InvalidOperationException("retrieve failed", gone) : gone;

        var act = () => Close(writer);

        var thrown = await act.Should().ThrowAsync<Exception>();
        thrown.Which.Should().BeSameAs(whereItIsGone == "write" ? gone : org.SignalRetrieveFault, "the caller recognises it by the same test; nothing is wrapped or replaced");
        reasons().Should().BeEmpty("a deleted Signal is not a failed close");
        log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
        org.UpdateCalls.Should().BeEmpty();
    }

    [Theory(DisplayName = "049 R4: any other fault on the read or the write is still logged at Error (WriteRefused), metered as signal_close_failed and rethrown")]
    [InlineData("read")]
    [InlineData("write")]
    public async Task AutomatedClose_AnotherFault_IsStillLoggedAtErrorAndMetered(string where)
    {
        var scope = Guid.NewGuid();
        FailureMetricScope.Value = scope;
        var (listener, reasons) = ListenFailureReasonsScoped(scope);
        using var _ = listener;
        var (writer, org, log) = AutomatedCloseRig("Open");
        var other = Fault(unchecked((int)0x80040216), "A different Dataverse fault.");   // one off ObjectDoesNotExist
        if (where == "write") org.ConditionalUpdateFault = other; else org.SignalRetrieveFault = other;

        var act = () => Close(writer);

        await act.Should().ThrowAsync<FaultException<OrganizationServiceFault>>();
        reasons().Should().Equal(OntologyWriterFailureReason.SignalCloseFailed);
        log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which.EventId.Id.Should().Be(OntologyWriterEvents.WriteRefused.Id);
    }

    [Fact(DisplayName = "049 D-122: a Signal read without a row version is never written (no unconditional fallback); the refusal is logged, metered as signal_close_failed and thrown")]
    public async Task AutomatedClose_ReadWithoutARowVersion_ThrowsAndWritesNothing()
    {
        var scope = Guid.NewGuid();
        FailureMetricScope.Value = scope;
        var (listener, reasons) = ListenFailureReasonsScoped(scope);
        using var _ = listener;
        var (writer, org, log) = AutomatedCloseRig("Open");
        org.Rows[ClosedSignalId].RowVersion = null;

        var act = () => Close(writer);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*row version*");
        org.ConditionalUpdates.Should().BeEmpty();
        org.UpdateCalls.Should().BeEmpty();
        reasons().Should().Equal(OntologyWriterFailureReason.SignalCloseFailed);
        log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error);
    }

    [Fact(DisplayName = "049 D-122: an empty id or a non-UTC set time is refused before any read")]
    public async Task AutomatedClose_InvalidArguments_AreRefused()
    {
        var (writer, org, _) = AutomatedCloseRig("Open");

        var noSignal = () => writer.CloseActedByDecisionAsync(Guid.Empty, AutoRecordId, SetReadAt, "BILL-AGE");
        var noRecord = () => writer.CloseActedByDecisionAsync(ClosedSignalId, Guid.Empty, SetReadAt, "BILL-AGE");
        var local = () => writer.CloseActedByDecisionAsync(ClosedSignalId, AutoRecordId, DateTime.SpecifyKind(SetReadAt, DateTimeKind.Local), "BILL-AGE");
        var unspecified = () => writer.CloseActedByDecisionAsync(ClosedSignalId, AutoRecordId, DateTime.SpecifyKind(SetReadAt, DateTimeKind.Unspecified), "BILL-AGE");

        await noSignal.Should().ThrowAsync<ArgumentException>();
        await noRecord.Should().ThrowAsync<ArgumentException>();
        await local.Should().ThrowAsync<ArgumentException>();
        await unspecified.Should().ThrowAsync<ArgumentException>();
        org.RetrievedColumns.Should().BeEmpty();
    }
}

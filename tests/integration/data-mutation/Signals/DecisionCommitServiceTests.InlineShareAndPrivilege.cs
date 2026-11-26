using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// Ontology task 225: two fixes on the decision commit path, through the REAL <see cref="DecisionCommitService"/> and writer over the commit rig.
/// <list type="bullet">
/// <item><b>D-229 (#1980, DI-97).</b> A Decision Record owned by the Secure Record Owners team (a secure child, D-33) is shared inline, before the
/// route returns, by uac-r2's REAL <see cref="SecureChildShareSynchronizer"/> over <see cref="Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld"/>
/// (an evaluating Dataverse) and its strict POA double. "The same grants the reconcile computes" is proven by running the scheduled reconcile
/// afterwards: it finds the record already in line and writes nothing to it.</item>
/// <item><b>D-212 (#2630, DI-143).</b> The record's <c>sprk_privilegeflagged</c> is derived from the Signal's subject and stored evidence
/// communications, read as the caller through <see cref="WorkItemPrivilege"/>, failing closed; the Signal's own column is never the source.</item>
/// </list>
/// </summary>
public sealed partial class DecisionCommitServiceTests
{
    private const int Collaborate = 262167;
    private static readonly Guid Sharee = Guid.Parse("c0430000-0000-4000-8000-0000000002a1");
    private static readonly Guid OtherSecureRecord = Guid.Parse("c0430000-0000-4000-8000-0000000002a2");
    private static readonly Guid EvidenceMail = Guid.Parse("c0430000-0000-4000-8000-0000000002a3");
    private static readonly Guid SecondEvidenceMail = Guid.Parse("c0430000-0000-4000-8000-0000000002a4");
    private static readonly Guid SubjectMail = Guid.Parse("c0430000-0000-4000-8000-0000000002a5");

    private static readonly DataversePrincipalRef ShareePrincipal = DataversePrincipalRef.User(Sharee);

    /// <summary>A rig whose Decision Record the resolver places under the Secure Record Owners team, on a secure matter shared with
    /// <see cref="Sharee"/>, beside another secure Decision Record on the same matter that is out of line (it has no share).</summary>
    private static Rig SecureRecordRig()
    {
        var rig = new Rig { Owner = RecordOwnerResolution.Owned(SecureTeam) with { IsSecureOwner = true } };
        rig.ShareWorld.SecureRoot("sprk_matter", Matter)
            .Add("sprk_recordtype_ref", TypeMatter, ("sprk_recordlogicalname", "sprk_matter"))
            .SecureChild("sprk_decisionrecord", OtherSecureRecord, ("sprk_matter", "sprk_matter", Matter));
        rig.ShareTable.Seed("sprk_matter", Matter, ShareePrincipal, Collaborate);
        return rig;
    }

    // ── D-229: the inline share ─────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "225 D-229: a secure-child Decision Record is shared inline, with exactly the grants the reconcile computes for it")]
    public async Task ASecureChildRecord_IsSharedBeforeTheRouteReturns_WithTheReconcilesGrants()
    {
        var rig = SecureRecordRig();

        var result = await rig.CommitAsync(Dismissal("not-material"));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        result.DecisionRecordId.Should().Be(RecordId);
        rig.Record.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(SecureTeam, "the precondition: a secure child");
        rig.ShareTable.MaskOf("sprk_decisionrecord", RecordId, ShareePrincipal).Should().NotBeNull(
            "the matter's sharee reads the new record when the route returns, not after the two-minute reconcile");
        rig.ShareTable.WriteLog.Should().NotBeEmpty().And.OnlyContain(w => w.RecordId == RecordId, "only the new record is shared");
        rig.ShareTable.MaskOf("sprk_decisionrecord", OtherSecureRecord, ShareePrincipal).Should().BeNull(
            "another out-of-line secure child is the reconcile's, never the commit's");

        // The reconcile, run now, finds the new record already in line: the inline share granted what it grants, no more, no less.
        var inline = rig.ShareTable.WriteLog.Count;
        var mask = rig.ShareTable.MaskOf("sprk_decisionrecord", RecordId, ShareePrincipal);
        var reconcile = await rig.Synchronizer().ReconcileAllAsync(CancellationToken.None);

        reconcile.Status.Should().Be(SecureChildShareSyncStatus.Completed);
        rig.ShareTable.WriteLog.Skip(inline).Should().NotContain(w => w.RecordId == RecordId, "the reconcile has nothing to change on it");
        rig.ShareTable.MaskOf("sprk_decisionrecord", RecordId, ShareePrincipal).Should().Be(mask);
        rig.ShareTable.MaskOf("sprk_decisionrecord", OtherSecureRecord, ShareePrincipal).Should().Be(mask,
            "control: the reconcile does mirror an out-of-line child, so its silence on the new record is meaningful");
    }

    [Fact(DisplayName = "225 D-229 must-not-fire: a Decision Record that is not a secure child triggers no share call")]
    public async Task AnOrdinaryRecord_TriggersNoShareCall()
    {
        var rig = SecureRecordRig();
        rig.Owner = RecordOwnerResolution.Owned(OrdinaryTeam);

        var result = await rig.CommitAsync(Dismissal("not-material"));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.ShareWorld.Queries.Should().BeEmpty("the synchronizer is never asked about an ordinary record");
        rig.ShareTable.WriteLog.Should().BeEmpty();
    }

    [Fact(DisplayName = "225 D-229 must-not-fire: a share that fails leaves the decision recorded, logs it, and adds no warning")]
    public async Task AShareFault_LeavesTheDecisionRecorded_AndIsLogged()
    {
        var rig = SecureRecordRig();
        var log = new ListLogger();
        rig.CommitLogger = log;
        rig.ShareWorld.FailingQueriesOf("sprk_decisionrecord");

        var result = await rig.CommitAsync(Dismissal("not-material"));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, "a share fault never fails or undoes a committed decision (the route answers 200)");
        result.DecisionRecordId.Should().Be(RecordId);
        rig.Status(Primary).Should().Be(Resolved, "the Signal is still closed with the record");
        rig.LinkedRecord(Primary).Should().Be(RecordId);
        result.Warnings.Should().BeEmpty("the reconcile repairs the share; the person is not told about it");
        rig.ShareTable.WriteLog.Should().BeEmpty();
        log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.EventId == DecisionCommitEvents.AfterRecordFailed
            && e.Message.Contains(RecordId.ToString("D"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "225 D-229 must-not-fire: a share write that fails leaves the decision recorded and is logged")]
    public async Task AShareWriteFault_LeavesTheDecisionRecorded_AndIsLogged()
    {
        var rig = SecureRecordRig();
        var log = new ListLogger();
        rig.CommitLogger = log;
        rig.ShareTable.WriteFailure = new InvalidOperationException("the POA write failed");

        var result = await rig.CommitAsync(Dismissal("not-material"));

        result.Status.Should().Be(DecisionCommitStatus.Recorded);
        rig.Status(Primary).Should().Be(Resolved);
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.EventId == DecisionCommitEvents.AfterRecordFailed);
    }

    // ── D-212: the privilege flag ────────────────────────────────────────────────────────────────────────────────

    private static string Evidence(params Guid[] communications) => System.Text.Json.JsonSerializer.Serialize(
        communications.Select(c => new { kind = "communication", @ref = $"sprk_communication:{c:D}", tier = "Fact" })
            .Prepend(new { kind = "snapshot", @ref = "sprk_matter:" + Matter.ToString("D"), tier = "Fact" }));

    private static async Task<bool> RecordedFlagAsync(Rig rig)
    {
        var result = await rig.CommitAsync(Dismissal("not-material"));
        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        return rig.Record.GetAttributeValue<bool>("sprk_privilegeflagged");
    }

    [Theory(DisplayName = "225 D-212: an evidence communication classified Privileged or Potentially Privileged flags the record")]
    [InlineData((int)CommunicationPrivilegeClassification.Privileged)]
    [InlineData((int)CommunicationPrivilegeClassification.PotentiallyPrivileged)]
    public async Task AFlaggingEvidenceCommunication_FlagsTheRecord(int classification)
    {
        var rig = new Rig();
        rig.Signals[Primary]["sprk_evidencerefs"] = Evidence(EvidenceMail, SecondEvidenceMail);
        rig.CommunicationClassifications[EvidenceMail] = (int)CommunicationPrivilegeClassification.None;
        rig.CommunicationClassifications[SecondEvidenceMail] = classification;

        (await RecordedFlagAsync(rig)).Should().BeTrue();
        rig.CommunicationReads.Should().ContainSingle("one batched read, as the caller");
    }

    [Theory(DisplayName = "225 D-212: None or no classification gives false, even when the Signal's own column says true")]
    [InlineData((int)CommunicationPrivilegeClassification.None)]
    [InlineData(null)]
    public async Task ANotPrivilegedCommunication_DoesNotFlag_AndTheSignalColumnIsNotRead(int? classification)
    {
        var rig = new Rig();
        rig.Signals[Primary]["sprk_privilegeflagged"] = true; // the column nothing writes (DI-64): never the source
        rig.Signals[Primary]["sprk_evidencerefs"] = Evidence(EvidenceMail);
        rig.CommunicationClassifications[EvidenceMail] = classification;

        (await RecordedFlagAsync(rig)).Should().BeFalse();
        rig.CommunicationReads.Should().ContainSingle();
    }

    [Fact(DisplayName = "225 D-212 must-not-fire: a Signal about no communication is not flagged and reads nothing, whatever its own column says")]
    public async Task ASignalAboutNoCommunication_IsNotFlagged_AndReadsNothing()
    {
        var rig = new Rig();
        rig.Signals[Primary]["sprk_privilegeflagged"] = true;

        (await RecordedFlagAsync(rig)).Should().BeFalse();
        rig.CommunicationReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "225 D-212: a fault of the caller's classification read flags the record (fail closed)")]
    public async Task AReadFault_FlagsTheRecord()
    {
        var rig = new Rig { CommunicationReadFails = DataverseUserResponse.Fail(503, "ServiceUnavailable", "throttled") };
        rig.Signals[Primary]["sprk_evidencerefs"] = Evidence(EvidenceMail);
        rig.CommunicationClassifications[EvidenceMail] = (int)CommunicationPrivilegeClassification.None;

        (await RecordedFlagAsync(rig)).Should().BeTrue();
    }

    [Fact(DisplayName = "225 D-212: a communication the caller cannot read flags the record (fail closed)")]
    public async Task AnUnreadableCommunication_FlagsTheRecord()
    {
        var rig = new Rig();
        rig.Signals[Primary]["sprk_evidencerefs"] = Evidence(EvidenceMail); // not in the caller's answer

        (await RecordedFlagAsync(rig)).Should().BeTrue();
    }

    [Fact(DisplayName = "225 D-212: stored evidence that cannot be interpreted flags the record without a read (fail closed)")]
    public async Task UninterpretableEvidence_FlagsTheRecord()
    {
        var rig = new Rig();
        rig.Signals[Primary]["sprk_evidencerefs"] = "{\"not\":\"an array\"}";

        (await RecordedFlagAsync(rig)).Should().BeTrue();
        rig.CommunicationReads.Should().BeEmpty();
    }

    [Theory(DisplayName = "225 D-212: the Signal's communication SUBJECT decides the flag")]
    [InlineData((int)CommunicationPrivilegeClassification.Privileged, true)]
    [InlineData((int)CommunicationPrivilegeClassification.None, false)]
    public async Task ACommunicationSubject_DecidesTheFlag(int classification, bool flagged)
    {
        var rig = new Rig();
        rig.Signals.Remove(Primary);
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_communication", subjectId: SubjectMail);
        rig.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_communication", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_communications");
        // The record is filed under its subject: the communication sits under the matter (the core-ancestor derivation reads it app-only).
        rig.Entities.Setup(e => e.RetrieveAsync("sprk_communication", SubjectMail, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communication", SubjectMail) { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Matter) });
        rig.CommunicationClassifications[SubjectMail] = classification;

        (await RecordedFlagAsync(rig)).Should().Be(flagged);
        rig.CommunicationReads.Should().ContainSingle().Which.Should().Contain(SubjectMail.ToString("D"));
    }

    /// <summary>The commit's log, for the share-fault tests.</summary>
    private sealed class ListLogger : ILogger<DecisionCommitService>
    {
        public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
                Entries.Add((logLevel, eventId, formatter(state, exception)));
        }
    }
}

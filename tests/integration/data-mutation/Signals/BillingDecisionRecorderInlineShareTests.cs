using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// #3276 (DI-178; ontology D-229, the rule PR #3270 gave the decision commit route): <see cref="BillingDecisionRecorder"/> shares a Decision
/// Record it wrote under the Secure Record Owners team (a secure child), or found already written, with the secure family's sharees before
/// <see cref="BillingDecisionRecorder.RecordAsync"/> / <see cref="BillingDecisionRecorder.RecordProcedureAsync"/> return. Through the REAL
/// <see cref="DecisionRecordWriter"/> (the SDK boundary mocked), the REAL <see cref="BillingDecisionRecorder"/> and uac-r2's REAL
/// <see cref="SecureChildShareSynchronizer"/> over <see cref="SecureChildShareWorld"/> (an evaluating Dataverse) and its strict POA double,
/// exactly as <c>DecisionCommitServiceTests.InlineShareAndPrivilege</c> does for the commit route. "The same grants the reconcile computes" is
/// proven by running the scheduled reconcile afterwards: it writes nothing more to the new record.
/// <para>The row is stored in the share world INSIDE the writer's create (the synchronizer reads the stored row), so the share runs only after
/// the record and its owner are committed: a recorder that shared before the writer returned would find no row and share nothing.</para>
/// </summary>
public sealed class BillingDecisionRecorderInlineShareTests
{
    private const int Collaborate = 262167;
    private static readonly Guid SecureTeam = Guid.Parse("33760000-0000-4000-8000-000000000001");
    private static readonly Guid OrdinaryTeam = Guid.Parse("33760000-0000-4000-8000-000000000002");
    private static readonly Guid Matter = Guid.Parse("33760000-0000-4000-8000-000000000011");
    private static readonly Guid Invoice = Guid.Parse("33760000-0000-4000-8000-000000000012");
    private static readonly Guid Reviewer = Guid.Parse("33760000-0000-4000-8000-000000000021");
    private static readonly Guid ReviewerContact = Guid.Parse("33760000-0000-4000-8000-000000000022");
    private static readonly Guid Sharee = Guid.Parse("33760000-0000-4000-8000-0000000000a1");
    private static readonly Guid OtherSecureRecord = Guid.Parse("33760000-0000-4000-8000-0000000000a2");
    private static readonly DataversePrincipalRef ShareePrincipal = DataversePrincipalRef.User(Sharee);
    private static readonly DateTimeOffset DecidedOn = new(2026, 11, 26, 9, 30, 0, TimeSpan.Zero);

    private sealed class Rig
    {
        public DecisionRecordWriterTests.Harness H { get; }
        public SecureChildShareWorld World { get; } = SecureChildShareWorld.Standard(SecureChildShareWorld.SecureBu, SecureTeam);
        public FakeRecordShareTable Shares { get; } = new();
        public ListLogger Log { get; } = new();
        public ILogger<SecureChildShareSynchronizer>? SynchronizerLogger { get; set; }
        public CountingScopes? Scopes { get; }
        public BillingDecisionRecorder Recorder { get; }

        /// <summary>How many synchronizers the host's scopes built (each share must get a fresh one).</summary>
        public int SynchronizersBuilt { get; private set; }

        public Rig(bool secure = true, bool withScopes = true)
        {
            var owner = RecordOwnerResolution.Owned(secure ? SecureTeam : OrdinaryTeam) with { IsSecureOwner = secure };
            H = DecisionRecordWriterTests.WithBillingLookups(new DecisionRecordWriterTests.Harness(ownership: owner));
            H.Rows[("sprk_invoice", Invoice)] = new Entity("sprk_invoice", Invoice) { ["sprk_matter"] = new EntityReference("sprk_matter", Matter) };
            // A secure matter shared with Sharee, beside another out-of-line secure Decision Record (the reconcile's, never the recorder's).
            World.SecureRoot("sprk_matter", Matter)
                .SecureChild("sprk_invoice", Invoice, ("sprk_matter", "sprk_matter", Matter))
                .SecureChild("sprk_decisionrecord", OtherSecureRecord, ("sprk_matter", "sprk_matter", Matter));
            Shares.Seed("sprk_matter", Matter, ShareePrincipal, Collaborate);
            // The synchronizer reads the record-type refs the writer stamped on the row (its core and regarding pair), by the harness catalog's ids.
            foreach (var (table, typeId) in Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub.SpaarkeDev1)
                World.Add("sprk_recordtype_ref", typeId, ("sprk_recordlogicalname", table));
            // The row lands in the world inside the writer's create: the synchronizer reads the STORED row, as the real one does.
            H.BeforeCreate = created =>
            {
                var id = created.Id != Guid.Empty ? created.Id : H.NewId;
                var columns = created.Attributes.Where(a => a.Key != "ownerid").Select(a => (a.Key, a.Value)).ToList();
                if (created.GetAttributeValue<EntityReference>("ownerid") is { } ownerRef)
                    columns.Add((ownerRef.LogicalName == "team" ? "owningteam" : "owninguser", (object)ownerRef));
                World.Add(created.LogicalName, id, columns.ToArray());
            };

            var identity = new Mock<IIdentityNormalizationService>();
            identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid user, CancellationToken _) => new PersonIdentity(user, ContactId: ReviewerContact));

            if (withScopes)
            {
                var services = new ServiceCollection();
                services.AddScoped(_ =>
                {
                    SynchronizersBuilt++;
                    return Synchronizer();
                });
                Scopes = new CountingScopes(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
            }

            Recorder = new BillingDecisionRecorder(H.Writer, identity.Object, Log, Scopes);
        }

        public SecureChildShareSynchronizer Synchronizer() => World.Synchronizer(Shares, logger: SynchronizerLogger);

        public Entity Record => H.Created.Single();

        public Guid RecordIdOf() => Record.Id != Guid.Empty ? Record.Id : H.NewId;
    }

    private static BillingDecision Billing(Guid? id = null) => new(
        id ?? Guid.NewGuid(), "approve-invoice", Invoice, BillingDecider.Human(Reviewer, "Billing reviewer"),
        new Dictionary<string, object?> { ["billedTotal"] = 1200m }, "Approved");

    private static RecordProcedureDecision Procedure(Guid? id = null) => new(
        id ?? Guid.NewGuid(), "exclude-rule", "sprk_matter", Matter, Reviewer, "procedure-editor",
        new Dictionary<string, object?>
        {
            ["rule"] = Guid.Parse("33760000-0000-4000-8000-0000000000c1"),
            ["firm"] = Guid.Parse("33760000-0000-4000-8000-0000000000c2"),
            ["override"] = Guid.Parse("33760000-0000-4000-8000-0000000000f1"),
        },
        "Excluded", "client agreed", DecidedOn,
        [new DecisionRecordRef("sprk_procedureassignment", Guid.Parse("33760000-0000-4000-8000-0000000000f1"))]);

    /// <summary>The scheduled reconcile, run now, writes nothing more to the new record, and it DOES share a control out-of-line child.</summary>
    private static async Task TheReconcileHasNothingLeftForTheRecord(Rig rig, Guid recordId)
    {
        var inline = rig.Shares.WriteLog.Count;
        var mask = rig.Shares.MaskOf("sprk_decisionrecord", recordId, ShareePrincipal);
        mask.Should().NotBeNull();

        var reconcile = await rig.Synchronizer().ReconcileAllAsync(CancellationToken.None);

        reconcile.Status.Should().Be(SecureChildShareSyncStatus.Completed);
        rig.Shares.WriteLog.Skip(inline).Should().NotContain(w => w.RecordId == recordId, "the reconcile has nothing to change on it");
        rig.Shares.MaskOf("sprk_decisionrecord", recordId, ShareePrincipal).Should().Be(mask);
        rig.Shares.MaskOf("sprk_decisionrecord", OtherSecureRecord, ShareePrincipal).Should().Be(mask,
            "control: the reconcile does mirror an out-of-line child, so its silence on the new record is meaningful");
    }

    // ── T1 / T2: must fire ───────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "3276 T1: a billing decision's secure Decision Record is shared inline, with exactly the reconcile's grants")]
    public async Task ABillingDecisionRecord_OnASecureMatter_IsSharedBeforeRecordAsyncReturns()
    {
        var rig = new Rig();

        var result = await rig.Recorder.RecordAsync(Billing());

        result.AlreadyRecorded.Should().BeFalse();
        result.RecordId.Should().Be(rig.RecordIdOf());
        rig.Record.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(SecureTeam, "the precondition: a secure child");
        rig.Shares.MaskOf("sprk_decisionrecord", result.RecordId, ShareePrincipal).Should().NotBeNull(
            "the matter's sharee reads the new record when the call returns, not after the two-minute reconcile");
        rig.Shares.WriteLog.Should().NotBeEmpty().And.OnlyContain(w => w.RecordId == result.RecordId, "only the new record is shared");
        rig.Shares.MaskOf("sprk_decisionrecord", OtherSecureRecord, ShareePrincipal).Should().BeNull(
            "another out-of-line secure child is the reconcile's, never the recorder's");
        await TheReconcileHasNothingLeftForTheRecord(rig, result.RecordId);
    }

    [Fact(DisplayName = "3276 T2: a per-record Procedure change's secure Decision Record is shared inline, with exactly the reconcile's grants")]
    public async Task AProcedureDecisionRecord_OnASecureMatter_IsSharedBeforeRecordProcedureAsyncReturns()
    {
        var rig = new Rig();

        var result = await rig.Recorder.RecordProcedureAsync(Procedure());

        result.AlreadyRecorded.Should().BeFalse();
        rig.Record.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(SecureTeam, "the precondition: a secure child");
        rig.Shares.MaskOf("sprk_decisionrecord", result.RecordId, ShareePrincipal).Should().NotBeNull();
        rig.Shares.WriteLog.Should().NotBeEmpty().And.OnlyContain(w => w.RecordId == result.RecordId);
        rig.Shares.MaskOf("sprk_decisionrecord", OtherSecureRecord, ShareePrincipal).Should().BeNull();
        await TheReconcileHasNothingLeftForTheRecord(rig, result.RecordId);
    }

    [Fact(DisplayName = "3276 r1: every share resolves the synchronizer from a fresh scope and disposes it; the id is the writer's")]
    public async Task EachShare_UsesAFreshScope_ThatIsDisposed_AndTheWritersRecordId()
    {
        var rig = new Rig();
        rig.SynchronizerLogger = new SummaryLogger(); // the synchronizer's own log; not asserted on

        var first = await rig.Recorder.RecordAsync(Billing());
        var second = await rig.Recorder.RecordProcedureAsync(Procedure());

        rig.SynchronizersBuilt.Should().Be(2, "one synchronizer per share, never one held by the singleton");
        rig.Scopes!.Created.Should().Be(2);
        rig.Scopes.Disposed.Should().Be(2, "each scope is disposed after its share");
        rig.Shares.MaskOf("sprk_decisionrecord", first.RecordId, ShareePrincipal).Should().NotBeNull();
        rig.Shares.MaskOf("sprk_decisionrecord", second.RecordId, ShareePrincipal).Should().NotBeNull();
        first.RecordId.Should().NotBe(second.RecordId);
        rig.Shares.WriteLog.Should().OnlyContain(w => w.RecordId == first.RecordId || w.RecordId == second.RecordId);
    }

    // ── T3: must not fire ────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "3276 T3 must-not-fire: an ordinary Decision Record written now triggers no share call")]
    public async Task AnOrdinaryRecord_WrittenNow_TriggersNoShareCall()
    {
        var rig = new Rig(secure: false);

        await rig.Recorder.RecordAsync(Billing());
        await rig.Recorder.RecordProcedureAsync(Procedure());

        rig.H.Created.Should().HaveCount(2);
        rig.World.Queries.Should().BeEmpty("the synchronizer is never asked about an ordinary record");
        rig.Shares.WriteLog.Should().BeEmpty();
        rig.Scopes!.Created.Should().Be(0, "no scope is opened for an ordinary record");
    }

    // ── T4: retry ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "3276 T4: a retry of a decision whose secure record was not shared (the first attempt's share never happened) shares it")]
    public async Task ARetry_OfASecureRecordThatWasNotShared_SharesIt()
    {
        var rig = new Rig();
        var decision = Billing();
        rig.World.FailingQueriesOf("sprk_decisionrecord"); // the first attempt's share does not happen (a crash right after the write)
        var first = await rig.Recorder.RecordAsync(decision);
        rig.Shares.MaskOf("sprk_decisionrecord", first.RecordId, ShareePrincipal).Should().BeNull("the precondition: not shared yet");
        rig.World.ClearQueryFaults();

        var retry = await rig.Recorder.RecordAsync(decision);

        retry.AlreadyRecorded.Should().BeTrue();
        retry.RecordId.Should().Be(first.RecordId);
        rig.H.Created.Should().ContainSingle("the retry writes no second record");
        rig.Shares.MaskOf("sprk_decisionrecord", first.RecordId, ShareePrincipal).Should().NotBeNull();
        rig.Shares.WriteLog.Should().OnlyContain(w => w.RecordId == first.RecordId);
        await TheReconcileHasNothingLeftForTheRecord(rig, first.RecordId);
    }

    [Fact(DisplayName = "3276 T4b: a Procedure retry whose secure record was not shared shares it")]
    public async Task AProcedureRetry_OfASecureRecordThatWasNotShared_SharesIt()
    {
        var rig = new Rig();
        var decision = Procedure();
        rig.World.FailingQueriesOf("sprk_decisionrecord");
        var first = await rig.Recorder.RecordProcedureAsync(decision);
        rig.World.ClearQueryFaults();

        var retry = await rig.Recorder.RecordProcedureAsync(decision);

        retry.AlreadyRecorded.Should().BeTrue();
        rig.Shares.MaskOf("sprk_decisionrecord", first.RecordId, ShareePrincipal).Should().NotBeNull();
        rig.Shares.WriteLog.Should().OnlyContain(w => w.RecordId == first.RecordId);
    }

    [Fact(DisplayName = "3276 T4c must-not-fire: a retry of an ordinary record shares nothing (the synchronizer answers NotApplicable)")]
    public async Task ARetry_OfAnOrdinaryRecord_SharesNothing()
    {
        var rig = new Rig(secure: false);
        var decision = Billing();
        await rig.Recorder.RecordAsync(decision);

        var retry = await rig.Recorder.RecordAsync(decision);

        retry.AlreadyRecorded.Should().BeTrue();
        rig.Shares.WriteLog.Should().BeEmpty("SyncChildAsync answers NotApplicable for a row the Secure team does not own");
        rig.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning && e.EventId == BillingDecisionRecorderEvents.ShareIncomplete);
    }

    // ── T5: faults ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "3276 T5: a share that cannot decide leaves the decision recorded and logs a Warning naming the record")]
    public async Task AShareQueryFault_LeavesTheDecisionRecorded_AndIsLogged()
    {
        var rig = new Rig();
        rig.World.FailingQueriesOf("sprk_decisionrecord");

        var billing = await rig.Recorder.RecordAsync(Billing());
        var procedure = await rig.Recorder.RecordProcedureAsync(Procedure());

        rig.H.Created.Should().HaveCount(2, "a share fault never fails or undoes a committed decision");
        rig.Shares.WriteLog.Should().BeEmpty();
        foreach (var id in new[] { billing.RecordId, procedure.RecordId })
            rig.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.EventId == BillingDecisionRecorderEvents.ShareIncomplete
                && e.Message.Contains(id.ToString("D"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "3276 T5b: a share write that fails leaves the decision recorded and logs a Warning naming the record")]
    public async Task AShareWriteFault_LeavesTheDecisionRecorded_AndIsLogged()
    {
        var rig = new Rig();
        rig.Shares.WriteFailure = new InvalidOperationException("the POA write failed");

        var result = await rig.Recorder.RecordAsync(Billing());

        rig.H.Created.Should().ContainSingle();
        rig.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.EventId == BillingDecisionRecorderEvents.ShareIncomplete
            && e.Message.Contains(result.RecordId.ToString("D"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "3276 T5c: a share that throws out of the synchronizer is logged with its exception and the decision is still recorded")]
    public async Task AShareThatThrows_IsLogged_AndTheDecisionIsRecorded()
    {
        var rig = new Rig();
        var sink = new SummaryLogger { ThrowOnce = true };
        rig.SynchronizerLogger = sink;

        var result = await rig.Recorder.RecordAsync(Billing());

        sink.Thrown.Should().Be(1, "the precondition: the share threw out of SyncChildAsync");
        result.RecordId.Should().Be(rig.RecordIdOf());
        rig.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.EventId == BillingDecisionRecorderEvents.ShareIncomplete
            && e.Exception is InvalidOperationException && e.Message.Contains(result.RecordId.ToString("D"), StringComparison.OrdinalIgnoreCase));
    }

    // ── T6: no scope factory ─────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "3276 T6: with no scope factory no share is attempted and nothing throws (the behaviour before #3276)")]
    public async Task WithNoScopeFactory_NoShareIsAttempted()
    {
        var rig = new Rig(withScopes: false);

        var billing = await rig.Recorder.RecordAsync(Billing());
        var procedure = await rig.Recorder.RecordProcedureAsync(Procedure());

        rig.H.Created.Should().HaveCount(2);
        billing.AlreadyRecorded.Should().BeFalse();
        procedure.AlreadyRecorded.Should().BeFalse();
        rig.World.Queries.Should().BeEmpty();
        rig.Shares.WriteLog.Should().BeEmpty();
        rig.Log.Entries.Should().BeEmpty();
    }

    // ── doubles ──────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class ListLogger : ILogger<BillingDecisionRecorder>
    {
        public List<(LogLevel Level, EventId EventId, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
                Entries.Add((logLevel, eventId, formatter(state, exception), exception));
        }
    }

    /// <summary>The synchronizer's logger; when <see cref="ThrowOnce"/> is set it throws the first time it logs its run summary: the one fault
    /// that escapes <c>SyncChildAsync</c> (the shape uac-r2's own refile tests use).</summary>
    private sealed class SummaryLogger : ILogger<SecureChildShareSynchronizer>
    {
        public bool ThrowOnce { get; init; }

        public int Thrown { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (ThrowOnce && Thrown == 0 && formatter(state, exception).StartsWith("[SECURE-CHILD-SHARES] scope=", StringComparison.Ordinal))
            {
                Thrown++;
                throw new InvalidOperationException("Test: the run summary's log sink faulted.");
            }
        }
    }

    /// <summary>A scope factory that counts the scopes it opened and the ones disposed.</summary>
    private sealed class CountingScopes(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        public int Created { get; private set; }

        public int Disposed { get; private set; }

        public IServiceScope CreateScope()
        {
            Created++;
            return new CountedScope(inner.CreateScope(), () => Disposed++);
        }

        private sealed class CountedScope(IServiceScope scope, Action onDispose) : IServiceScope
        {
            public IServiceProvider ServiceProvider => scope.ServiceProvider;

            public void Dispose()
            {
                onDispose();
                scope.Dispose();
            }
        }
    }
}


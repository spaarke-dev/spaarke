using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// GitHub #2091 — the playbook update node (<see cref="UpdateRecordActionCore"/>) and the output orchestrator's generic update
/// (<see cref="DataverseUpdateHandler"/>) are re-file writers: a write that moves a filing lookup of a lineage table runs the
/// ONE step after a re-file, <see cref="Sprk.Bff.Api.Services.Access.SecureChildReconciler.AfterRefileAsync"/>, with whether
/// the row was isolated read BEFORE the write — as the browser routes and the chat update tool do.
/// </summary>
/// <remarks>
/// Driven over the REAL reconciler (its REAL synchronizer and ownership resolver) on an in-memory Dataverse
/// (<see cref="SecureChildShareWorld"/>): an isolated communication filed under a secure matter, with an isolated to-do
/// filed only under it, both carrying the matter sharee's mirrored share. The write moves the communication to an
/// ordinary matter, and the owner assignment that follows a reparent lands with it (the resolver double assigns nothing).
/// So the communication losing its mirror and the to-do being released prove the step ran for THAT row with
/// <c>wasIsolated = true</c> — read after the write it would be false, and nothing would be released. The thread
/// reconcile (the step's own, for a communication) counts the calls.
/// </remarks>
public sealed class RefileWriterAfterRefileHookTests
{
    private static readonly Guid SecureMatter = Guid.Parse("20910000-0000-4000-8000-000000000001");
    private static readonly Guid OrdinaryMatter = Guid.Parse("20910000-0000-4000-8000-000000000002");
    private static readonly Guid Communication = Guid.Parse("20910000-0000-4000-8000-0000000000c1");
    private static readonly Guid TodoUnderCommunication = Guid.Parse("20910000-0000-4000-8000-0000000000d1");
    private static readonly Guid Sharee = Guid.Parse("20910000-0000-4000-8000-0000000000b1");

    /// <summary>Collaborate on the record, as a Manage Access share writes it (Read|Write|Append|AppendTo, no Share).</summary>
    private const int CollaborateMask = 1 | 2 | 4 | 16;

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .SecureRoot("sprk_matter", SecureMatter)
        .OrdinaryRoot("sprk_matter", OrdinaryMatter)
        .SecureChild("sprk_communication", Communication, ("sprk_regardingmatter", "sprk_matter", SecureMatter))
        .SecureChild("sprk_todo", TodoUnderCommunication, ("sprk_regardingcommunication", "sprk_communication", Communication));

    private readonly FakeRecordShareTable _shares = new();
    private readonly Mock<IThreadResolver> _threads = new();
    private readonly Mock<IFieldMappingDataverseService> _write = new(MockBehavior.Strict);
    private readonly List<Dictionary<string, object?>> _writes = new();
    private bool _failWrite;

    public RefileWriterAfterRefileHookTests()
    {
        _shares.Seed("sprk_communication", Communication, DataversePrincipalRef.User(Sharee), CollaborateMask);
        _shares.Seed("sprk_todo", TodoUnderCommunication, DataversePrincipalRef.User(Sharee), CollaborateMask);

        _write
            .Setup(w => w.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(),
                It.IsAny<Guid?>()))
            .Returns((string table, Guid id, Dictionary<string, object?> fields, CancellationToken _, Guid? _) =>
            {
                if (_failWrite)
                    throw new HttpRequestException("0x80040220 the caller lacks Write on the record", null, System.Net.HttpStatusCode.Forbidden);

                _writes.Add(fields);
                // A transient read fault (the timeout tests) has cleared by the time the write lands.
                _world.ClearQueryFaults();
                // Dataverse as it stands once the re-file landed: the lookup moved, and the owner the reparent decided
                // (the ordinary matter's business-unit team) is assigned.
                if (fields.Keys.Any(k => SecureRootInheritanceColumn(k) == "sprk_regardingmatter"))
                {
                    _world.Set(table, id, "sprk_regardingmatter", new EntityReference("sprk_matter", OrdinaryMatter));
                    _world.MoveOwner(table, id, DataversePrincipalRef.Team(SecureChildShareWorld.GeneralTeam));
                }

                return Task.CompletedTask;
            });
    }

    // ── UpdateRecordActionCore (the playbook update node and the action seam) ────────────────────────────────────

    [Fact(DisplayName = "#2091: an UpdateRecord node moving a communication out of a secure matter runs AfterRefileAsync once, isolated-before read before the write")]
    public async Task UpdateRecordActionCore_MovingAFilingLookup_RunsTheAfterRefileStepOnce_WithIsolationReadBeforeTheWrite()
    {
        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_communication", Communication, FieldMappings: null, LegacyFields: null,
                Lookups: [new RenderedLookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter.ToString())]),
            CancellationToken.None);

        _writes.Should().ContainSingle();
        AssertTheStepRanOnceForTheCommunication();
    }

    [Fact(DisplayName = "#2091: an UpdateRecord node writing no filing lookup does not run AfterRefileAsync (and reads no owner)")]
    public async Task UpdateRecordActionCore_AWriteThatMovesNoFilingLookup_DoesNotRunTheStep()
    {
        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_communication", Communication, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_subject"] = "Re: invoice" }, Lookups: null),
            CancellationToken.None);

        _writes.Should().ContainSingle();
        AssertTheStepDidNotRun(readsAllowed: false);
    }

    [Fact(DisplayName = "#2091: an UpdateRecord node whose write fails does not run AfterRefileAsync")]
    public async Task UpdateRecordActionCore_AFailedWrite_DoesNotRunTheStep()
    {
        _failWrite = true;

        var act = () => Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_communication", Communication, FieldMappings: null, LegacyFields: null,
                Lookups: [new RenderedLookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter.ToString())]),
            CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>("the caller's own failure surfaces unchanged");
        AssertTheStepDidNotRun(readsAllowed: true);
    }

    // ── DataverseUpdateHandler (the output orchestrator's generic update) ────────────────────────────────────────

    [Fact(DisplayName = "#2091: the output orchestrator's update moving a communication out of a secure matter runs AfterRefileAsync once, isolated-before read before the write")]
    public async Task DataverseUpdateHandler_MovingAFilingLookup_RunsTheAfterRefileStepOnce_WithIsolationReadBeforeTheWrite()
    {
        await Handler().UpdateAsync(
            "sprk_communication", Communication,
            new Dictionary<string, object?> { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OrdinaryMatter) },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        _writes.Should().ContainSingle();
        AssertTheStepRanOnceForTheCommunication();
    }

    [Fact(DisplayName = "#2091: the output orchestrator's update writing no filing lookup does not run AfterRefileAsync (and reads no owner)")]
    public async Task DataverseUpdateHandler_AWriteThatMovesNoFilingLookup_DoesNotRunTheStep()
    {
        await Handler().UpdateAsync(
            "sprk_communication", Communication,
            new Dictionary<string, object?> { ["sprk_subject"] = "Re: invoice" },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        _writes.Should().ContainSingle();
        AssertTheStepDidNotRun(readsAllowed: false);
    }

    [Fact(DisplayName = "#2091: the output orchestrator's update whose write fails does not run AfterRefileAsync")]
    public async Task DataverseUpdateHandler_AFailedWrite_DoesNotRunTheStep()
    {
        _failWrite = true;

        var act = () => Handler().UpdateAsync(
            "sprk_communication", Communication,
            new Dictionary<string, object?> { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OrdinaryMatter) },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>("the write's own failure surfaces unchanged");
        AssertTheStepDidNotRun(readsAllowed: true);
    }

    // ── #2091 round 3: a timeout on the read before the write is a FAULTED wasIsolated, never a cancellation ────────

    [Fact(DisplayName = "#2091 r3: an UpdateRecord node whose isolation read times out (not the caller's cancellation) still writes, and the step releases nothing")]
    public async Task UpdateRecordActionCore_AnIsolationReadTimeout_IsAFault_TheWriteStands()
    {
        TheIsolationReadTimesOut();

        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_communication", Communication, FieldMappings: null, LegacyFields: null,
                Lookups: [new RenderedLookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter.ToString())]),
            CancellationToken.None);

        AssertTheWriteStoodAndNothingWasReleased();
    }

    [Fact(DisplayName = "#2091 r3: the output orchestrator's update whose isolation read times out (not the caller's cancellation) still writes, and the step releases nothing")]
    public async Task DataverseUpdateHandler_AnIsolationReadTimeout_IsAFault_TheWriteStands()
    {
        TheIsolationReadTimesOut();

        await Handler().UpdateAsync(
            "sprk_communication", Communication,
            new Dictionary<string, object?> { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OrdinaryMatter) },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        AssertTheWriteStoodAndNothingWasReleased();
    }

    // ── #2091 round 4: one transient fault on the isolation read is retried, so a move OUT still releases ────────────

    [Fact(DisplayName = "#2091 r4: an UpdateRecord node whose isolation read faults ONCE retries it, and the move out releases the row and what is filed under it")]
    public async Task UpdateRecordActionCore_OneTransientIsolationReadFault_IsRetried_AndTheMoveOutReleases()
    {
        TheIsolationReadFaultsOnce();

        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_communication", Communication, FieldMappings: null, LegacyFields: null,
                Lookups: [new RenderedLookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter.ToString())]),
            CancellationToken.None);

        _writes.Should().ContainSingle();
        AssertTheStepRanOnceForTheCommunication();
    }

    [Fact(DisplayName = "#2091 r4: the output orchestrator's update whose isolation read faults ONCE retries it, and the move out releases the row and what is filed under it")]
    public async Task DataverseUpdateHandler_OneTransientIsolationReadFault_IsRetried_AndTheMoveOutReleases()
    {
        TheIsolationReadFaultsOnce();

        await Handler().UpdateAsync(
            "sprk_communication", Communication,
            new Dictionary<string, object?> { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OrdinaryMatter) },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        _writes.Should().ContainSingle();
        AssertTheStepRanOnceForTheCommunication();
    }

    // ── #2091 round 5: the retry faults too — a FAULTED wasIsolated, and nothing is released ─────────────────────────

    [Fact(DisplayName = "#2091 r5: an UpdateRecord node whose isolation read faults on the retry too still writes, and the step releases nothing")]
    public async Task UpdateRecordActionCore_AnIsolationReadThatFaultsTwice_IsAFault_NothingIsReleased()
    {
        var faults = TheIsolationReadFaultsTwice();

        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_communication", Communication, FieldMappings: null, LegacyFields: null,
                Lookups: [new RenderedLookup("sprk_regardingmatter", "sprk_matter", OrdinaryMatter.ToString())]),
            CancellationToken.None);

        faults().Should().Be(2, "the read, then its one retry");
        AssertTheWriteStoodAndNothingWasReleased();
    }

    [Fact(DisplayName = "#2091 r5: the output orchestrator's update whose isolation read faults on the retry too still writes, and the step releases nothing")]
    public async Task DataverseUpdateHandler_AnIsolationReadThatFaultsTwice_IsAFault_NothingIsReleased()
    {
        var faults = TheIsolationReadFaultsTwice();

        await Handler().UpdateAsync(
            "sprk_communication", Communication,
            new Dictionary<string, object?> { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OrdinaryMatter) },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        faults().Should().Be(2, "the read, then its one retry");
        AssertTheWriteStoodAndNothingWasReleased();
    }

    /// <summary>The isolation read faults on the read and on its retry, then heals; answers how many faults were thrown.</summary>
    private Func<int> TheIsolationReadFaultsTwice()
    {
        var faults = 0;
        _world.QueryFault = () =>
        {
            if (++faults == 2)
                _world.ClearQueryFaults();
            return new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
        };
        _world.FailingQueriesOf("sprk_communication");
        return () => faults;
    }

    private void TheIsolationReadFaultsOnce()
    {
        _world.QueryFault = () =>
        {
            _world.ClearQueryFaults();
            return new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
        };
        _world.FailingQueriesOf("sprk_communication");
    }

    // ── #2091 round 4: a cleared TEXT column is no re-file; a cleared filing lookup is ───────────────────────────────

    [Fact(DisplayName = "#2091 r4: the output orchestrator's update clearing a text column runs no step after a re-file (and reads no owner)")]
    public async Task DataverseUpdateHandler_ANullTextColumn_RunsNoStep()
    {
        await Handler().UpdateAsync(
            "sprk_communication", Communication, new Dictionary<string, object?> { ["sprk_subject"] = null },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        _writes.Should().ContainSingle();
        AssertTheStepDidNotRun(readsAllowed: false);
    }

    [Fact(DisplayName = "#2091 r4: an UpdateRecord node clearing a text column runs no step after a re-file (and reads no owner)")]
    public async Task UpdateRecordActionCore_ANullTextColumn_RunsNoStep()
    {
        await Core().UpdateAsync(
            new UpdateRecordActionInput(
                "sprk_communication", Communication, FieldMappings: null,
                LegacyFields: new Dictionary<string, string?> { ["sprk_subject"] = null }, Lookups: null),
            CancellationToken.None);

        _writes.Should().ContainSingle();
        AssertTheStepDidNotRun(readsAllowed: false);
    }

    [Fact(DisplayName = "#2091 r4: the output orchestrator's update clearing a filing lookup runs the step after a re-file")]
    public async Task DataverseUpdateHandler_ANullFilingLookup_RunsTheStep()
    {
        await Handler().UpdateAsync(
            "sprk_communication", Communication, new Dictionary<string, object?> { ["sprk_regardingmatter"] = null },
            ConcurrencyMode.None, maxRetries: 1, CancellationToken.None);

        _writes.Should().ContainSingle();
        AssertTheStepRanOnceForTheCommunication();
    }

    private void TheIsolationReadTimesOut()
    {
        _world.QueryFault = () => new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
        _world.FailingQueriesOf("sprk_communication");
    }

    private void AssertTheWriteStoodAndNothingWasReleased()
    {
        _writes.Should().ContainSingle("an HttpClient timeout is not the caller cancelling the update");
        ThreadReconciles().Where(i => (Guid)i.Arguments[0] == Communication).Should().ContainSingle(
            "the step after the re-file still ran, handed the faulted read (any overload)");
        _world.OwnerOf("sprk_todo", TodoUnderCommunication).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam),
            "whether the row WAS isolated is unknown, so nothing is released");
    }

    // ── assertions ───────────────────────────────────────────────────────────────────────────────────────────────

    private void AssertTheStepRanOnceForTheCommunication()
    {
        _shares.MaskOf("sprk_communication", Communication, DataversePrincipalRef.User(Sharee)).Should().BeNull(
            "the step's mirror ran for the re-filed communication, which WAS isolated (read before the write), so the " +
            "secure matter's sharee is taken off it now (owner round 22)");
        _world.OwnerOf("sprk_todo", TodoUnderCommunication).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.GeneralTeam),
            "the step's pass released the to-do isolated only through the communication — it may only when the row WAS isolated");
        _shares.MaskOf("sprk_todo", TodoUnderCommunication, DataversePrincipalRef.User(Sharee)).Should().BeNull(
            "the released to-do loses its mirror with it");
        ThreadReconciles().Should().ContainSingle(
                "the step ran exactly once (it reconciles a communication's thread), and the writer did not reconcile it "
                + "again, through any overload")
            .Which.Arguments[0].Should().Be(Communication);
    }

    /// <summary>#2143 round 8: every thread reconcile the writer or its step asked for, through ANY overload.</summary>
    private IReadOnlyList<Moq.IInvocation> ThreadReconciles() => _threads.Invocations
        .Where(i => i.Method.Name == nameof(Sprk.Bff.Api.Services.Communication.IThreadResolver.ReconcileThreadForFilingAsync))
        .ToList();

    private void AssertTheStepDidNotRun(bool readsAllowed)
    {
        ThreadReconciles().Should().BeEmpty("no thread reconcile, through any overload");
        _world.OwnerWrites.Should().BeEmpty("no pass ran over what is filed under the communication");
        _shares.MaskOf("sprk_communication", Communication, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
        _shares.MaskOf("sprk_todo", TodoUnderCommunication, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
        _world.OwnerOf("sprk_todo", TodoUnderCommunication).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
        if (!readsAllowed)
            _world.QueriedTables.Should().BeEmpty("a write that moves no filing lookup costs no isolation read");
    }

    // ── composition ──────────────────────────────────────────────────────────────────────────────────────────────

    private static string SecureRootInheritanceColumn(string key) =>
        Sprk.Bff.Api.Services.Access.SecureRootInheritance.NormalizeColumn(key);

    /// <summary>The REAL reconciler over this test's world, with the thread seam it calls for a communication.</summary>
    private Sprk.Bff.Api.Services.Access.SecureChildReconciler Reconciler() =>
        SecureChildShareWorld.ReconcilerOver(() => _world, _shares, null!, threads: _threads.Object);

    /// <summary>The restamper over a world holding the communication (the re-file moves no stamp it can find below it).</summary>
    private static CoreAncestorRestamper Restamper() =>
        new StampWorld().Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)]).Restamper;

    /// <summary>The node's core, with a scope that resolves the reconciler as the host does (scoped).</summary>
    private UpdateRecordActionCore Core() =>
        new(
            _write.Object,
            new ServiceCollection()
                .AddSingleton(Restamper())
                .AddSingleton<IRecordOwnershipResolver>(new RecordOwnershipResolverDouble())
                .AddSingleton(_threads.Object)
                .AddSingleton(Sprk.Bff.Api.Tests.TestInfrastructure.EntitySetNameFixtures.Service())
                .AddScoped(_ => Reconciler())
                .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true })
                .GetRequiredService<IServiceScopeFactory>(),
            NullLogger.Instance);

    private DataverseUpdateHandler Handler() =>
        new(
            _write.Object, new Mock<IGenericEntityService>().Object, Restamper(), new RecordOwnershipResolverDouble(),
            SecureRootFilingGateFixtures.NothingSecure(), NullLogger<DataverseUpdateHandler>.Instance,
            Reconciler());
}

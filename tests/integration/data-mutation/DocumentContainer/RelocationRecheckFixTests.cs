// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/DocumentContainer/**`
//   - Justification: #2159 / PR #2161 security re-check — three write contracts of the relocator and its release retry:
//     1 a pointer write whose outcome is UNKNOWN (a timeout, a cancellation — wrapped by the entity service as an
//       InvalidOperationException — a transport fault) keeps its copy whatever an immediate re-read says, because the
//       update may still commit; only a DEFINITE refusal whose row, read again, names something else removes it (the
//       client retries internally, so a refusal may follow an earlier attempt that committed). And the settle never deletes a source unless the row's
//       CURRENT file exists — the backstop that makes any pointer race survivable;
//     2 a queued F3 release retry is bound to the source container, the secure record left and the target — never to the
//       item, which the relocator itself re-points inside that container (an edited source closed in place);
//     3 a fault escaping the relocator is a failed delivery of the release retry, logged with the one recovery.
//
// Doubles are module boundaries only (ADR-038 §4): the REAL RecordContainerResolver and relocator over task 166's
// document-pointer world; the entity service's write is scripted at the Dataverse boundary.

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Documents;
using Sprk.Bff.Api.Services.Jobs;
using Spaarke.Dataverse;
using Xunit;
using Ledger = Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator.RelocationLedger;
using R = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.RefileFileRelocationTests;
using T = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.DocumentContainerRelocatorTests;
using World = TestRecordContainerResolver.DocumentPointerWorld;

namespace Sprk.Bff.Api.Tests.DataMutation.DocumentContainer;

public class RelocationRecheckFixTests
{
    private const string Item = R.Item;

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // 1 — a pointer write that lands late never costs the document its file
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>A document in customer B's container (it moves into customer A1's).</summary>
    private static (World World, T.Rig Rig) InCustomerB()
    {
        var world = T.Environment();
        world.Rows[("sprk_document", T.DocumentId)] = T.Doc(drive: T.CustomerBContainer, item: Item);
        return (world, new T.Rig(world));
    }

    /// <summary>
    /// The pointer write (the update that names the copy) is SENT but answers with <paramref name="fault"/> before it
    /// commits; <see cref="LateCommit"/> then lands it, as Dataverse does after the caller stopped waiting. Every other
    /// write applies at once. <paramref name="cancel"/> is cancelled as the write is sent (Make Secure's request ending).
    /// <paramref name="committedFirst"/>: the update COMMITS and only then answers with <paramref name="fault"/> — the
    /// Dataverse client's internal retry (502/503, transient SQL, throttling) surfacing its LAST attempt's refusal after an
    /// earlier attempt landed.
    /// </summary>
    private sealed class LatePointerWrite
    {
        private readonly World _world;
        private Dictionary<string, object>? _pending;

        /// <summary>The pointer write has been sent.</summary>
        public bool Sent => _pending is not null;

        public LatePointerWrite(World world, Exception fault, CancellationTokenSource? cancel = null, bool committedFirst = false)
        {
            _world = world;
            Mock.Get(world.EntityService!)
                .Setup(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Returns((string entity, Guid id, Dictionary<string, object> fields, CancellationToken _) =>
                {
                    var pointerWrite = fields.TryGetValue("sprk_graphitemid", out var item) && Equals(item, T.Rig.CopyItem);
                    if (pointerWrite && _pending is null)
                    {
                        _pending = fields;
                        if (committedFirst)
                            Apply(entity, id, fields);
                        cancel?.Cancel();
                        return Task.FromException(fault);
                    }

                    Apply(entity, id, fields);
                    return Task.CompletedTask;
                });
        }

        /// <summary>The sent update commits now.</summary>
        public void LateCommit() => Apply("sprk_document", T.DocumentId, _pending!);

        private void Apply(string entity, Guid id, Dictionary<string, object> fields)
        {
            _world.Updates.Add((entity, id, fields));
            foreach (var (column, value) in fields)
            {
                if (value is DBNull)
                    _world.Rows[(entity, id)].Attributes.Remove(column);
                else
                    _world.Rows[(entity, id)][column] = value;
            }
        }
    }

    /// <summary>The production shape of a cancelled write: <c>DataverseServiceClientImpl.UpdateAsync</c> wraps every failure.</summary>
    private static Exception WrappedCancellation()
        => new InvalidOperationException("Failed to update sprk_document", new TaskCanceledException("A task was canceled."));

    [Theory(DisplayName = "1 must fire: a re-point cancelled (production shape) whose update commits AFTER the re-read keeps the copy, and the next pass leaves the document a file")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ALateCommittingRePoint_KeepsTheCopy_AndTheDocumentKeepsAFile(bool requestCancelled)
    {
        var (world, rig) = InCustomerB();
        using var cts = new CancellationTokenSource();
        var write = new LatePointerWrite(world, WrappedCancellation(), requestCancelled ? cts : null);

        var first = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.LegacyMigration, cts.Token);

        first.State.Should().Be(RelocationState.Failed, first.Detail);
        first.Detail.Should().Contain("may still land");
        rig.Spe.Verify(s => s.DeleteFileAsync(T.CustomerA1Container, T.Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Never,
            "the re-read said 'not yet', which proves nothing for a write that did not answer");

        write.LateCommit();
        rig.ItemOf(T.DocumentId).Should().Be(T.Rig.CopyItem, "the update committed after the re-read");

        var second = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.LegacyMigration);

        world.ItemFacts(rig.DriveOf(T.DocumentId)!, rig.ItemOf(T.DocumentId)!).Should().NotBeNull(
            $"the file the row names exists ({second.State}: {second.Detail})");
    }

    [Fact(DisplayName = "1 must not fire: a re-point Dataverse REFUSED (a fault, production shape) removes the copy and leaves the row and the source")]
    public async Task ARefusedRePoint_RemovesTheCopy()
    {
        var (world, rig) = InCustomerB();
        _ = new LatePointerWrite(world, new InvalidOperationException("Failed to update sprk_document",
            new System.ServiceModel.FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = -2147220891 })));

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.LegacyMigration);

        outcome.State.Should().Be(RelocationState.Failed);
        outcome.Detail.Should().Be("the row could not be re-pointed");
        rig.Spe.Verify(s => s.DeleteFileAsync(T.CustomerA1Container, T.Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Once);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerBContainer);
        world.ItemFacts(T.CustomerBContainer, Item).Should().NotBeNull();
    }

    /// <summary>
    /// The production shape of a throttled update: <c>DataverseServiceClientImpl.UpdateAsync</c> wraps the client's final
    /// <see cref="System.ServiceModel.FaultException{TDetail}"/> (service protection limit, -2147015902).
    /// </summary>
    private static Exception WrappedThrottlingFault()
        => new InvalidOperationException(
            $"Failed to update sprk_document record {T.DocumentId}: Number of requests exceeded the limit of 6000 over time window of 300 seconds.",
            new System.ServiceModel.FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = -2147015902, Message = "Number of requests exceeded the limit of 6000 over time window of 300 seconds." },
                new System.ServiceModel.FaultReason("Number of requests exceeded the limit of 6000 over time window of 300 seconds.")));

    [Fact(DisplayName = "1 must fire: a re-point REFUSED on the client's last retry (throttling, production shape) after an earlier attempt committed keeps the copy, and the move is committed")]
    public async Task ARefusalAfterAnEarlierAttemptCommitted_KeepsTheCopy_AndCommits()
    {
        var (world, rig) = InCustomerB();
        _ = new LatePointerWrite(world, WrappedThrottlingFault(), committedFirst: true);

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.LegacyMigration);

        rig.Spe.Verify(s => s.DeleteFileAsync(T.CustomerA1Container, T.Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Never,
            "the row names the copy: deleting it would leave the row pointing at a file that no longer exists");
        rig.ItemOf(T.DocumentId).Should().Be(T.Rig.CopyItem);
        world.ItemFacts(T.CustomerA1Container, T.Rig.CopyItem).Should().NotBeNull("the file the row names exists");
        outcome.State.Should().NotBe(RelocationState.Failed, outcome.Detail);
        outcome.TargetItem.Should().Be(T.Rig.CopyItem, "the landed re-point is the move's commit");
    }

    [Fact(DisplayName = "1 must fire: a re-point REFUSED whose row cannot be read again keeps the copy (an earlier retry may have committed)")]
    public async Task ARefusedRePointWithAnUnreadableRow_KeepsTheCopy()
    {
        var (world, rig) = InCustomerB();
        var write = new LatePointerWrite(world, WrappedThrottlingFault());
        world.RetrieveFault = (entity, _) => write.Sent && entity == "sprk_document"
            ? new HttpRequestException("Dataverse is unreachable")
            : null;

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.LegacyMigration);

        outcome.State.Should().Be(RelocationState.Failed);
        outcome.Detail.Should().Contain("could not be read again").And.Contain("the copy is kept");
        rig.Spe.Verify(s => s.DeleteFileAsync(T.CustomerA1Container, T.Rig.CopyItem, It.IsAny<CancellationToken>()), Times.Never,
            "the row may name the copy");
        world.ItemFacts(T.CustomerBContainer, Item).Should().NotBeNull("the source is never touched");
    }

    public static TheoryData<string, Exception, bool> WriteFailures => new()
    {
        { "a cancellation, wrapped (production)", WrappedCancellation(), false },
        { "an OperationCanceledException, wrapped", new InvalidOperationException("x", new OperationCanceledException()), false },
        { "a raw cancellation", new OperationCanceledException(), false },
        { "a timeout", new TimeoutException("no answer"), false },
        { "a transport fault, wrapped", new InvalidOperationException("x", new HttpRequestException("reset")), false },
        { "a 5xx", new InvalidOperationException("x", new Microsoft.PowerPlatform.Dataverse.Client.Utils.DataverseOperationException("x",
            new Microsoft.Rest.HttpOperationException("503") { Response = new Microsoft.Rest.HttpResponseMessageWrapper(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), string.Empty) })), false },
        { "a service fault, wrapped (production)", new InvalidOperationException("x",
            new System.ServiceModel.FaultException<OrganizationServiceFault>(new OrganizationServiceFault())), true },
        { "a 4xx", new InvalidOperationException("x", new Microsoft.PowerPlatform.Dataverse.Client.Utils.DataverseOperationException("x",
            new Microsoft.Rest.HttpOperationException("400") { Response = new Microsoft.Rest.HttpResponseMessageWrapper(new HttpResponseMessage(HttpStatusCode.BadRequest), string.Empty) })), true },
    };

    [Theory(DisplayName = "1: only a DEFINITE refusal of the write (a Dataverse fault or 4xx) counts as a refusal")]
    [MemberData(nameof(WriteFailures))]
    public void OnlyADefiniteRefusal_ProvesTheWriteNeverCommits(string what, Exception failure, bool definite)
        => DocumentContainerRelocator.IsDefiniteWriteRejection(failure).Should().Be(definite, what);

    /// <summary>
    /// A document moved into customer A1's container whose source delete failed — its ledger still owes the source, with
    /// the witness recorded at the move — and whose CURRENT file (the copy) is then gone.
    /// </summary>
    private static (World World, T.Rig Rig) OwedSourceAndCurrentFileGone()
    {
        var (world, rig) = InCustomerB();
        rig.DeleteFails = (drive, item) => drive == T.CustomerBContainer && item == Item;
        rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.LegacyMigration).GetAwaiter().GetResult()
            .State.Should().Be(RelocationState.RelocationPending);
        rig.DeleteFails = (_, _) => false;
        rig.ItemOf(T.DocumentId).Should().Be(T.Rig.CopyItem);
        world.Items[(T.CustomerA1Container, T.Rig.CopyItem)] = null; // the row names a file that is gone
        return (world, rig);
    }

    [Fact(DisplayName = "1 must fire: the settle never deletes a source while the row's CURRENT file is missing — the entry stays owed, an Error names the document")]
    public async Task TheSettle_KeepsTheSource_WhenTheCurrentFileIsMissing()
    {
        var (world, rig) = OwedSourceAndCurrentFileGone();
        var logger = new CapturingLogger<DocumentContainerRelocator>();

        var outcome = await rig.NewRelocator(logger: logger)
            .RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.LegacyMigration);

        world.ItemFacts(T.CustomerBContainer, Item).Should().NotBeNull($"the source is the document's last copy ({outcome.State}: {outcome.Detail})");
        rig.Spe.Verify(s => s.DeleteFileAsync(T.CustomerBContainer, Item, It.IsAny<CancellationToken>()), Times.Once,
            "only the first move's failed delete — never one on the strength of a row naming a missing file");
        Ledger.Parse(world.Rows[("sprk_document", T.DocumentId)].GetAttributeValue<string>(DocumentContainerRelocator.RelocationLedgerColumn))!
            .Entries.Should().ContainSingle(e => e.SourceItem == Item, "the source stays owed");
        logger.Lines.Should().Contain(l => l.Level == LogLevel.Error && l.Message.Contains(T.DocumentId.ToString())
                                            && l.Message.Contains("current file is missing"));
    }

    [Fact(DisplayName = "1 must fire: the settle never deletes a source while the row's CURRENT file cannot be read")]
    public async Task TheSettle_KeepsTheSource_WhenTheCurrentFileIsUnreadable()
    {
        var (world, rig) = OwedSourceAndCurrentFileGone();
        world.Items[(T.CustomerA1Container, T.Rig.CopyItem)] = new SpeItemCreator("memo.docx", null, T.BffApplication, 1234, "hash-1234");
        var reads = 0;
        rig.Spe.Setup(s => s.GetItemCreatorAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string drive, string item, CancellationToken _) =>
                drive == T.CustomerA1Container && item == T.Rig.CopyItem && reads++ == 0
                    ? Task.FromException<SpeItemCreator?>(new HttpRequestException("Graph unavailable"))
                    : Task.FromResult(world.ItemFacts(drive, item)));

        await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.LegacyMigration);

        world.ItemFacts(T.CustomerBContainer, Item).Should().NotBeNull();
        rig.Spe.Verify(s => s.DeleteFileAsync(T.CustomerBContainer, Item, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "1 must not fire: the settle deletes the matching source once the row's current file is there")]
    public async Task TheSettle_DeletesTheSource_WhenTheCurrentFileExists()
    {
        var (world, rig) = OwedSourceAndCurrentFileGone();
        world.Items[(T.CustomerA1Container, T.Rig.CopyItem)] = new SpeItemCreator("memo.docx", null, T.BffApplication, 1234, "hash-1234");

        var outcome = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.LegacyMigration);

        outcome.State.Should().Be(RelocationState.InPlace, outcome.Detail);
        world.ItemFacts(T.CustomerBContainer, Item).Should().BeNull("the source is settled");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // 2 — the queued release survives a same-container re-copy
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "2 must fire: a queued release whose row a pass re-copies INSIDE S1 (an edited source closed in place) is still recorded, and the file moves into S2")]
    public async Task AQueuedRelease_SurvivesASameContainerReCopy()
    {
        const string current = "01CURRENTFILEINS1";
        var world = R.Env();
        world.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, R.SecondSecureMatter, T.SecureContainer, current);
        R.Own(world, T.DocumentId, true);
        var rig = new T.Rig(world);
        var row = world.Rows[("sprk_document", T.DocumentId)];
        row["sprk_graphitemid"] = current;
        world.Items[(T.SecureContainer, current)] = new SpeItemCreator("memo.docx", null, T.BffApplication, 1234, "hash-current");
        world.Items[(T.CustomerAContainer, Item)] = new SpeItemCreator("memo.docx", null, T.BffApplication, 2000, "source-edited");
        rig.Versions[(T.CustomerAContainer, Item)] =
        [
            new VersionInfoDto("1.0", null, T.Rig.Written, 1234, T.Rig.CreatorName) { LastModifiedByUserId = T.Creator },
            new VersionInfoDto("2.0", null, new DateTimeOffset(2026, 10, 6, 8, 0, 0, TimeSpan.Zero), 2000, "Late Editor") { LastModifiedByUserId = T.Creator },
        ];
        row[DocumentContainerRelocator.RelocationLedgerColumn] = new Ledger([new DocumentContainerRelocator.RelocationLedgerEntry
        {
            SourceDrive = T.CustomerAContainer,
            SourceItem = Item,
            RekeyPending = true,
            At = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero),
            Witness = new DocumentContainerRelocator.RelocationWitness(1234, "hash-1234", "1.0"),
        }]).Serialize();

        // The re-file's inline release meets the lock of a running relocation: queued, bound at the item read then.
        var key = DocumentContainerRelocator.RelocationLockKey(T.DocumentId);
        rig.Locks.Held.Add(key);
        var permit = new SecureExitPermit([new RecordOwnershipParent("sprk_matter", T.SecureMatter)],
            new SecureRemovalDecision(SecureRemovalOutcome.Permitted, SecureRemovalBasis.FullAccess, Guid.NewGuid()),
            [new RecordOwnershipParent("sprk_matter", R.SecondSecureMatter)]);
        (await rig.Relocator.RecordReleaseAsync(T.DocumentId, permit)).RetryQueued.Should().BeTrue();
        rig.Locks.Held.Remove(key);

        // The lock holder, a pass, closes the edited source in place: the same-container re-copy re-points the row in S1.
        var pass = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.Refile);
        rig.DriveOf(T.DocumentId).Should().Be(T.SecureContainer);
        var recopied = rig.ItemOf(T.DocumentId);
        recopied.Should().NotBe(current, $"re-copied in S1 ({pass.State}: {pass.Detail})");

        var processed = await new RelocationReleaseRetryJobHandler(rig.Relocator, NullLogger<RelocationReleaseRetryJobHandler>.Instance) { LockWait = TimeSpan.Zero }
            .ProcessAsync(rig.Submitted.Single(), CancellationToken.None);

        processed.Status.Should().Be(JobStatus.Completed);
        var release = Ledger.Parse(row.GetAttributeValue<string>(DocumentContainerRelocator.RelocationLedgerColumn))!.Release;
        release.Should().NotBeNull("the release is recorded");
        release!.SourceItem.Should().Be(recopied, "it names the item read under the lock, so the guard's exact match holds");
        release.TargetContainer.Should().Be(R.SecondSecureContainer);

        var after = await rig.Relocator.RelocateIfMisplacedAsync(T.DocumentId, apply: true, RelocationPurpose.Refile);
        after.State.Should().NotBe(RelocationState.HeldIsolated, after.Detail);
        rig.DriveOf(T.DocumentId).Should().Be(R.SecondSecureContainer, "the released move ran");
    }

    [Fact(DisplayName = "2 must not fire: a queued release whose file is in another container now records nothing, and an Error names the recovery")]
    public async Task AQueuedRelease_ForAnotherContainer_IsRefused_WithTheRecovery()
    {
        var world = R.Env();
        world.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, R.SecondSecureMatter, T.SecureContainer);
        R.Own(world, T.DocumentId, true);
        var rig = new T.Rig(world);
        var logger = new CapturingLogger<DocumentContainerRelocator>();
        var payload = RelocationFixRoundTests.APayload(drive: T.CustomerAContainer);

        var processed = await new RelocationReleaseRetryJobHandler(rig.NewRelocator(logger: logger), NullLogger<RelocationReleaseRetryJobHandler>.Instance)
            { LockWait = TimeSpan.Zero }.ProcessAsync(RelocationReleaseRetryJobHandler.NewJob(payload), CancellationToken.None);

        processed.Status.Should().Be(JobStatus.Completed, "a retry would find the same");
        world.Updates.Should().BeEmpty();
        logger.Lines.Should().Contain(l => l.Level == LogLevel.Error && l.Message.Contains(T.DocumentId.ToString())
                                            && l.Message.Contains(DocumentContainerRelocator.UnreleasedRecovery));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // 3 — a fault escaping the relocator is a failed delivery, logged with the recovery
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>A clock that faults — any fault the relocator does not classify itself.</summary>
    private sealed class FaultingClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("clock unavailable");
    }

    [Fact(DisplayName = "3 must fire: a fault escaping the relocator is retried, then dead-lettered, and every delivery logs an Error with the recovery")]
    public async Task AnEscapingFault_IsAFailedDelivery_WithTheRecovery()
    {
        var world = R.Env();
        world.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, R.SecondSecureMatter, T.SecureContainer);
        R.Own(world, T.DocumentId, true);
        var rig = new T.Rig(world);
        var logger = new CapturingLogger<RelocationReleaseRetryJobHandler>();
        var handler = new RelocationReleaseRetryJobHandler(rig.NewRelocator(time: new FaultingClock()), logger) { LockWait = TimeSpan.Zero };
        var job = RelocationReleaseRetryJobHandler.NewJob(RelocationFixRoundTests.APayload());

        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Failed, "a redelivery may succeed");
        job.DeliveryCount = JobContract.MaxDeliveryCount;
        (await handler.ProcessAsync(job, CancellationToken.None)).Status.Should().Be(JobStatus.Poisoned);

        logger.Lines.Count(l => l.Level == LogLevel.Error && l.Message.Contains(T.DocumentId.ToString())
                                && l.Message.Contains(DocumentContainerRelocator.UnreleasedRecovery))
            .Should().BeGreaterThanOrEqualTo(2);
        world.Updates.Should().BeEmpty();
    }

    [Fact(DisplayName = "3 must not fire: a cancelled delivery (host shutdown) is not swallowed")]
    public async Task ACancelledDelivery_IsNotSwallowed()
    {
        var world = R.Env();
        world.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, R.SecondSecureMatter, T.SecureContainer);
        R.Own(world, T.DocumentId, true);
        var rig = new T.Rig(world);
        rig.Locks.Held.Add(DocumentContainerRelocator.RelocationLockKey(T.DocumentId));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new RelocationReleaseRetryJobHandler(rig.Relocator, NullLogger<RelocationReleaseRetryJobHandler>.Instance)
        {
            LockWait = TimeSpan.FromMinutes(1),
        };

        var act = () => handler.ProcessAsync(RelocationReleaseRetryJobHandler.NewJob(RelocationFixRoundTests.APayload()), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class CapturingLogger<TCategory> : ILogger<TCategory>
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Lines)
                Lines.Add((logLevel, formatter(state, exception)));
        }
    }
}

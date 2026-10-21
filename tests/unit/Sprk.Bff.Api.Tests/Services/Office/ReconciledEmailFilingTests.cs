using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Engine.Rungs;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.Domain.Signals;
using Sprk.Bff.Api.Tests.Services.Signals;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Office;

/// <summary>
/// spaarkeai-word-add-in-r1 task 121, owner decision 2026-10-09 ("file it if unfiled"): an Office email save that
/// reconciles to an EXISTING communication files it to the save's record when it is filed to nothing — never when it is
/// already filed, never for an unfiled save, never when the caller may not change it, and never by failing the save.
/// </summary>
/// <remarks>
/// The REAL <see cref="EmailUploadCaptureService"/> and <see cref="IncomingAssociationResolver"/> (real rungs and status
/// ladder) produce the decision exactly as the save does; only the Dataverse boundary, the caller's rights
/// (<see cref="CallerRecordAccessProbe"/>'s virtual seam) and the ownership resolver are doubled (ADR-038 §4).
/// </remarks>
[Trait("status", "task-121-word-add-in-r1")]
public class ReconciledEmailFilingTests
{
    private static readonly Guid CanonicalId = Guid.NewGuid();
    private static readonly Guid MatterId = Guid.NewGuid();

    private sealed class World
    {
        public Mock<IDataverseService> Dataverse { get; } = new();
        public List<Dictionary<string, object>> CommunicationWrites { get; } = new();
        public RecordOwnershipResolverDouble Ownership { get; } = new();

        /// <summary>Task 129: the thread resolver every step of the save reaches (capture, association, after-re-file).</summary>
        public Mock<IThreadResolver> Threads { get; } = new();

        /// <summary>#2143 round 8: every thread reconcile asked for, through ANY overload.</summary>
        public IReadOnlyList<Moq.IInvocation> ThreadReconciles() => Threads.Invocations
            .Where(i => i.Method.Name == nameof(IThreadResolver.ReconcileThreadForFilingAsync))
            .ToList();

        /// <summary>Task 129: the #1629 grant the capture issues for the thread its join returned.</summary>
        public Mock<Sprk.Bff.Api.Services.Communication.Access.IDirectThreadAccessService> Grants { get; } = new();
        public Mock<CallerRecordAccessProbe> Probe { get; } = new(
            new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!, null!)
        { CallBase = false };

        public World(bool reconciled, params DataverseEntity[] filingReads)
        {
            Dataverse
                .Setup(d => d.CreateCommunicationRaceProofAsync(It.IsAny<DataverseEntity>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CanonicalId, reconciled));
            var reads = new Queue<DataverseEntity>(filingReads);
            Dataverse
                .Setup(d => d.RetrieveAsync("sprk_communication", CanonicalId,
                    It.Is<string[]>(c => c.Contains("sprk_regardingmatter")), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => reads.Count > 1 ? reads.Dequeue() : reads.Peek());
            Dataverse
                .Setup(d => d.UpdateAsync("sprk_communication", CanonicalId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, f, _) => CommunicationWrites.Add(new(f)))
                .Returns(Task.CompletedTask);
            CallerMay(AccessRights.Read | AccessRights.Write | AccessRights.AppendTo);
        }

        public void CallerMay(AccessRights rights) =>
            Probe.Setup(p => p.GetCallerRightsAsync(It.IsAny<string?>(), "sprk_communications", CanonicalId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(rights);

        public async Task<(EmailCaptureOutcome? Capture, ReconciledFilingResult Result)> SaveAsync(SaveRequest request)
        {
            var options = new AutoFileOptions { Enabled = true, Threshold = 0.85 };
            var mapper = new AssociationStatusMapper(
                new AutoFileGate(Mock.Of<IOptionsMonitor<AutoFileOptions>>(m => m.CurrentValue == options)),
                NullLogger<AssociationStatusMapper>.Instance);
            var resolver = new IncomingAssociationResolver(
                new IAssociationRung[] { new ExplicitReferenceRung(Dataverse.Object) },
                Dataverse.Object, Dataverse.Object, mapper, CoreAncestorResolverFixtures.Inert(), Ownership,
                NullLogger<IncomingAssociationResolver>.Instance, Threads.Object);
            var capture = new EmailUploadCaptureService(
                Dataverse.Object, resolver, Mock.Of<ICommunicationEnrichmentService>(), Ownership,
                NullLogger<EmailUploadCaptureService>.Instance, Threads.Object, Grants.Object);

            // The reconciler's share mirror and descendant pass run over loose doubles; it never throws by contract.
            var entities = new Mock<IGenericEntityService>();
            var configuration = new ConfigurationBuilder().Build();
            var children = Isolation is { } isolation
                ? SecureChildShareWorld.ReconcilerOver(() => isolation, new FakeRecordShareTable(), null!, gateDecisionRecords: GateRecords)
                : new SecureChildReconciler(
                entities.Object, Ownership,
                new SecureChildShareSynchronizer(entities.Object, Mock.Of<IDataverseRecordShareService>(), null!, configuration,
                    NullLogger<SecureChildShareSynchronizer>.Instance),
                null!, configuration, NullLogger<SecureChildReconciler>.Instance, GateRecords ?? new GateSharingWorld().Sharing,
                threads: Threads.Object);
            var filing = new ReconciledEmailFiling(
                resolver, Dataverse.Object, Probe.Object, children,
                Isolation is { } stamped && !RestampOverStamps
                    ? SecureChildShareWorld.RestamperOver(() => stamped)
                    : RestamperLogger is { } restamperLogger
                        ? new CoreAncestorRestamper(Stamps.Service, Stamps.Resolver, restamperLogger)
                        : Stamps.Restamper,
                NullLogger<ReconciledEmailFiling>.Instance,
                Trigger?.Object);

            var outcome = await capture.CaptureWithOutcomeAsync(request, "user-oid", CancellationToken.None);
            if (outcome is not null && Rewrite is { } rewrite)
                outcome = outcome with { Decision = rewrite(outcome.Decision) };
            return (outcome, await filing.FileIfUnfiledAsync(request, outcome, "caller-token", CancellationToken.None));
        }

        /// <summary>Optionally changes the evaluated decision before the filing sees it (a second engine proposal).</summary>
        public Func<AssociationDecision, AssociationDecision>? Rewrite { get; set; }

        /// <summary>Ontology task 144: the rows filed under the communication, as the core-ancestor re-stamp reads and writes them
        /// (the REAL <see cref="CoreAncestorRestamper"/> over an in-memory Dataverse; empty unless a test seeds it).</summary>
        public StampWorld Stamps { get; } = new();

        /// <summary>Ontology task 144: when set, the re-stamp AND the ownership pass run over this one secure-child world (the
        /// REAL restamper and the REAL reconciler, ownership resolver and share synchronizer), so the re-own reads the new stamp.</summary>
        public SecureChildShareWorld? Isolation { get; set; }

        /// <summary>Ontology task 144 (uac-r2 K3): with <see cref="Isolation"/> set, keep the re-stamp over <see cref="Stamps"/> (so a
        /// test can make it fail or throw) while the ownership pass still runs over the secure-child world.</summary>
        public bool RestampOverStamps { get; set; }

        /// <summary>Ontology D-48 (#1659): the association-confirmed Signal trigger the filing queues; null = none registered.</summary>
        public Mock<SignalEvaluationTrigger>? Trigger { get; set; }

        /// <summary>Ontology task 042b (D-169): the gate Decision Record helper the reconciler's after-re-file step calls; null = a double over an
        /// empty world.</summary>
        public GateDecisionRecordSharing? GateRecords { get; set; }

        /// <summary>Ontology task 144: logs the real restamper writes to; null = none. A throwing logger breaks the
        /// restamper's never-throws contract, the one way a fault can escape it.</summary>
        public Microsoft.Extensions.Logging.ILogger<CoreAncestorRestamper>? RestamperLogger { get; set; }
    }

    /// <summary>A logger that throws on every write (a fault inside the restamper's own reporting).</summary>
    private sealed class ThrowingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            throw new InvalidOperationException("Test: the restamper's logger faulted.");
    }

    /// <summary>The communication as it reads after the filing: on the matter, with or without a triage category.</summary>
    private static DataverseEntity FiledToMatter(bool classified) => FilingSignalDoubles.Communication(CanonicalId, MatterId, classified);

    private static DataverseEntity Unfiled() => new("sprk_communication", CanonicalId);

    private static DataverseEntity FiledTo(string column, string table) => new("sprk_communication", CanonicalId)
    {
        [column] = new EntityReference(table, Guid.NewGuid()),
    };

    private static SaveRequest PaneSave(SaveEntityReference? target) => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = target,
        Email = new EmailMetadata
        {
            Subject = "RE: Discovery schedule",
            SenderEmail = "counsel@contoso.com",
            InternetMessageId = "<CAF0a1b2c3@mail.example.com>",
            ExchangeItemId = "AAMkAGI2TG93AAA=",
            Body = "<p>text</p>",
            IsBodyHtml = true,
        },
    };

    private static SaveEntityReference Matter() => new() { EntityType = "Matter", EntityId = MatterId, DisplayName = "Acme v Beta" };

    [Fact]
    public async Task FileIfUnfiled_SaveReconciledToAnUnfiledCommunication_FilesItToTheSavesRecord_ThroughTheReparent()
    {
        var world = new World(reconciled: true, Unfiled());

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Filed);
        var write = world.CommunicationWrites.Should().ContainSingle().Subject;
        ((EntityReference)write["sprk_regardingmatter"]).Id.Should().Be(MatterId);
        world.Ownership.Reparents.Should().ContainSingle("the owner is re-derived from the record it is now filed under")
            .Which.EntityLogicalName.Should().Be("sprk_communication");
    }

    [Fact(DisplayName = "Ontology task 144: filing the reconciled communication re-stamps what is filed under it in the same operation — a stale copy left by an out-of-BFF unfiling does not wait for the 5-minute job")]
    public async Task FileIfUnfiled_RestampsTheRowsFiledUnderTheCommunication()
    {
        // The communication was on another matter and was unfiled on its form (no BFF path ran), so its Signal, Decision
        // Record and to-do still carry that matter's copy. The save files it to MatterId; the stamp double holds the
        // communication as the filing leaves it.
        var staleMatter = Guid.NewGuid();
        var signal = Guid.NewGuid();
        var decisionRecord = Guid.NewGuid();
        var todo = Guid.NewGuid();
        var world = new World(reconciled: true, Unfiled());
        world.Stamps
            .Row("sprk_communication", CanonicalId, [("sprk_regardingmatter", "sprk_matter", MatterId)], pairId: MatterId.ToString())
            .Row("sprk_signal", signal,
                [("sprk_regardingcommunication", "sprk_communication", CanonicalId), ("sprk_regardingmatter", "sprk_matter", staleMatter)],
                pairId: CanonicalId.ToString())
            .Row("sprk_decisionrecord", decisionRecord,
                [("sprk_regardingcommunication", "sprk_communication", CanonicalId), ("sprk_regardingmatter", "sprk_matter", staleMatter)],
                pairId: CanonicalId.ToString())
            .Row("sprk_todo", todo,
                [("sprk_regardingcommunication", "sprk_communication", CanonicalId), ("sprk_regardingmatter", "sprk_matter", staleMatter)],
                pairId: CanonicalId.ToString());

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Filed);
        world.Stamps.Lookup("sprk_signal", signal, "sprk_regardingmatter").Should().Be(MatterId);
        world.Stamps.Lookup("sprk_decisionrecord", decisionRecord, "sprk_regardingmatter").Should().Be(MatterId);
        world.Stamps.Lookup("sprk_todo", todo, "sprk_regardingmatter").Should().Be(MatterId);
        world.Stamps.Patches.Should().NotContain(p => p.Entity == "sprk_communication",
            "the communication's own filing is the association engine's write, never the restamper's");
    }

    [Fact(DisplayName = "Ontology task 144: filing the reconciled communication under a SECURE matter re-owns its Signal and Decision Record to the Secure Record owner team and re-stamps them, in the same operation")]
    public async Task FileIfUnfiled_UnderASecureMatter_ReownsAndRestampsTheSignalAndDecisionRecordFiledUnderIt()
    {
        // The window case of the 144 trace: the communication was on an ORDINARY matter and was unfiled outside the BFF, so
        // its Signal and Decision Record still say that matter; the Office save files it under the secure MatterId.
        var staleMatter = Guid.NewGuid();
        var signal = Guid.NewGuid();
        var decisionRecord = Guid.NewGuid();
        var isolation = SecureChildShareWorld.Standard()
            .SecureRoot("sprk_matter", MatterId)
            .OrdinaryRoot("sprk_matter", staleMatter)
            // As the filing leaves it: under the secure matter, re-owned by the reparent to the named team.
            .SecureChild("sprk_communication", CanonicalId, ("sprk_regardingmatter", "sprk_matter", MatterId))
            .OrdinaryChild("sprk_signal", signal,
                ("sprk_regardingcommunication", "sprk_communication", CanonicalId), ("sprk_regardingmatter", "sprk_matter", staleMatter))
            .OrdinaryChild("sprk_decisionrecord", decisionRecord,
                ("sprk_regardingcommunication", "sprk_communication", CanonicalId), ("sprk_regardingmatter", "sprk_matter", staleMatter));
        var world = new World(reconciled: true, Unfiled()) { Isolation = isolation };

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Filed);
        foreach (var (table, id) in new[] { ("sprk_signal", signal), ("sprk_decisionrecord", decisionRecord) })
        {
            isolation.ValueOf<EntityReference>(table, id, "sprk_regardingmatter")!.Id.Should().Be(MatterId);
            isolation.OwnerOf(table, id).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam),
                $"the {table} follows the communication into isolation in the same operation");
        }
    }

    [Fact(DisplayName = "Ontology task 144: a filing that is refused re-stamps nothing")]
    public async Task FileIfUnfiled_NotPermitted_RestampsNothing()
    {
        var world = new World(reconciled: true, Unfiled());
        world.CallerMay(AccessRights.Read);
        world.Stamps
            .Row("sprk_communication", CanonicalId, [("sprk_regardingmatter", "sprk_matter", MatterId)])
            .Row("sprk_signal", Guid.NewGuid(),
                [("sprk_regardingcommunication", "sprk_communication", CanonicalId), ("sprk_regardingmatter", "sprk_matter", Guid.NewGuid())],
                pairId: CanonicalId.ToString());

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.NotPermitted);
        world.Stamps.Patches.Should().BeEmpty();
    }

    [Fact(DisplayName = "Ontology task 144: a filing whose write fails re-stamps nothing")]
    public async Task FileIfUnfiled_WhenTheWriteFails_RestampsNothing()
    {
        var world = new World(reconciled: true, Unfiled());
        world.Dataverse
            .Setup(d => d.UpdateAsync("sprk_communication", CanonicalId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse down"));
        world.Stamps
            .Row("sprk_communication", CanonicalId, [("sprk_regardingmatter", "sprk_matter", MatterId)])
            .Row("sprk_signal", Guid.NewGuid(),
                [("sprk_regardingcommunication", "sprk_communication", CanonicalId), ("sprk_regardingmatter", "sprk_matter", Guid.NewGuid())],
                pairId: CanonicalId.ToString());

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Failed);
        world.Stamps.Patches.Should().BeEmpty("nothing was filed, so nothing under it moved");
    }

    /// <summary>
    /// Ontology task 144 (uac-r2 K3): a secure-child world in which the filing leaves the communication under the secure matter and
    /// a Signal filed under it still owned by the ordinary owner. Only <see cref="SecureChildReconciler.AfterRefileAsync"/> moves
    /// that Signal to the Secure Record owner team — the filing's own reparent goes to the ownership double, never to this world.
    /// </summary>
    private static (SecureChildShareWorld Isolation, Guid Signal) SecureWorldWithAnOrdinarySignal()
    {
        var signal = Guid.NewGuid();
        var isolation = SecureChildShareWorld.Standard()
            .SecureRoot("sprk_matter", MatterId)
            .SecureChild("sprk_communication", CanonicalId, ("sprk_regardingmatter", "sprk_matter", MatterId))
            .OrdinaryChild("sprk_signal", signal,
                ("sprk_regardingcommunication", "sprk_communication", CanonicalId), ("sprk_regardingmatter", "sprk_matter", MatterId));
        return (isolation, signal);
    }

    private static void AssertAfterRefileReownedTheSignal(SecureChildShareWorld isolation, Guid signal)
    {
        isolation.OwnerWrites.Should().ContainSingle(w => w.Table == "sprk_signal" && w.Id == signal,
                "AfterRefileAsync (and nothing else) re-owns the Signal filed under the re-filed communication")
            .Which.Owner.Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
        isolation.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.SecureTeam));
    }

    [Fact(DisplayName = "Ontology task 144: a re-stamp that THROWS never undoes the filing, skips the ownership step or the Signal trigger")]
    public async Task FileIfUnfiled_WhenTheRestampThrows_TheFilingStands_AndTheTriggerStillRuns()
    {
        var trigger = FilingSignalDoubles.Trigger();
        var (isolation, signal) = SecureWorldWithAnOrdinarySignal();
        var world = new World(reconciled: true, Unfiled(), Unfiled(), FiledToMatter(classified: true))
        {
            Trigger = trigger,
            RestamperLogger = new ThrowingLogger<CoreAncestorRestamper>(),
            Isolation = isolation,
            RestampOverStamps = true,
        };
        world.Stamps
            .Row("sprk_communication", CanonicalId, [("sprk_regardingmatter", "sprk_matter", MatterId)])
            .FailScan("sprk_signal");

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Filed, "the filing landed before the re-stamp; it is not reported as failed");
        world.Ownership.Reparents.Should().ContainSingle("the filing's own reparent");
        AssertAfterRefileReownedTheSignal(isolation, signal);
        FilingSignalDoubles.VerifyEnqueuedOnce(trigger, MatterId, CanonicalId);
    }

    [Fact(DisplayName = "Ontology task 144: a re-stamp that does not finish never skips the ownership step or the Signal trigger")]
    public async Task FileIfUnfiled_WhenTheRestampFails_TheFilingStands_AndTheTriggerStillRuns()
    {
        var trigger = FilingSignalDoubles.Trigger();
        var (isolation, signal) = SecureWorldWithAnOrdinarySignal();
        var world = new World(reconciled: true, Unfiled(), Unfiled(), FiledToMatter(classified: true))
        {
            Trigger = trigger,
            Isolation = isolation,
            RestampOverStamps = true,
        };
        world.Stamps
            .Row("sprk_communication", CanonicalId, [("sprk_regardingmatter", "sprk_matter", MatterId)])
            .FailScan("sprk_signal");

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Filed);
        world.Ownership.Reparents.Should().ContainSingle("the filing's own reparent");
        AssertAfterRefileReownedTheSignal(isolation, signal);
        FilingSignalDoubles.VerifyEnqueuedOnce(trigger, MatterId, CanonicalId);
    }

    [Fact]
    public async Task FileIfUnfiled_EngineAlsoProposedAnotherRecord_FilesOnlyTheSavesRecord_AndMarksTheOtherNotWritten()
    {
        // The participant rung can propose the sender's contact beside the save's matter. The caller filed to the matter
        // only: the contact is neither written nor recorded as written in the provenance the review surface reads.
        var contactId = Guid.NewGuid();
        var world = new World(reconciled: true, Unfiled())
        {
            Rewrite = decision => decision with
            {
                RegardingWrites = new Dictionary<string, EntityReference>(decision.RegardingWrites)
                {
                    ["sprk_regardingperson"] = new EntityReference("contact", contactId),
                },
                Provenance = decision.Provenance with
                {
                    Candidates = decision.Provenance.Candidates.Append(new CandidateTrace
                    {
                        Field = "sprk_regardingperson",
                        TargetEntity = "contact",
                        TargetId = contactId.ToString("D"),
                        ReinforcedConfidence = 0.9,
                        DeterministicConfidence = 0.9,
                        Written = true,
                        Conflict = false,
                        Contributors = new[] { new ContributorTrace { Rung = "ParticipantCorrelation", Confidence = 0.9, Provenance = "sender" } },
                    }).ToList(),
                },
            },
        };

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Filed);
        var write = world.CommunicationWrites.Should().ContainSingle().Subject;
        write.Should().ContainKey("sprk_regardingmatter").And.NotContainKey("sprk_regardingperson");
        var candidates = JsonDocument.Parse((string)write["sprk_associationprovenance"]).RootElement
            .GetProperty("candidates").EnumerateArray().ToList();
        candidates.Single(c => c.GetProperty("field").GetString() == "sprk_regardingperson")
            .GetProperty("written").GetBoolean().Should().BeFalse("a dropped proposal must not read as filed");
        candidates.Single(c => c.GetProperty("field").GetString() == "sprk_regardingmatter")
            .GetProperty("written").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task FileIfUnfiled_CommunicationAlreadyFiledElsewhere_IsNeverMoved()
    {
        var world = new World(reconciled: true, FiledTo("sprk_regardingproject", "sprk_project"));

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.AlreadyFiled);
        world.CommunicationWrites.Should().BeEmpty();
        world.Ownership.Reparents.Should().BeEmpty();
    }

    [Fact]
    public async Task FileIfUnfiled_CommunicationWithOnlyTheRegardingPair_CountsAsFiled()
    {
        var pairOnly = new DataverseEntity("sprk_communication", CanonicalId) { ["sprk_regardingrecordid"] = Guid.NewGuid().ToString("D") };
        var world = new World(reconciled: true, pairOnly);

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.AlreadyFiled);
        world.CommunicationWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task FileIfUnfiled_UnfiledSave_FilesNothing()
    {
        var world = new World(reconciled: true, Unfiled());

        var (_, result) = await world.SaveAsync(PaneSave(target: null));

        result.Should().Be(ReconciledFilingResult.NotApplicable);
        world.CommunicationWrites.Should().BeEmpty();
        world.Probe.Verify(p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FileIfUnfiled_CallerMayNotChangeTheCommunication_LeavesItUnfiled_AndTheSaveProceeds()
    {
        var world = new World(reconciled: true, Unfiled());
        world.CallerMay(AccessRights.Read);

        var (capture, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.NotPermitted);
        capture!.CommunicationId.Should().Be(CanonicalId, "the save still links its document to the communication");
        world.CommunicationWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task FileIfUnfiled_FiledBySomeoneElseBetweenTheReadAndTheWrite_IsNotOverwritten()
    {
        var world = new World(reconciled: true, Unfiled(), FiledTo("sprk_regardingproject", "sprk_project"));

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.AlreadyFiled);
        world.CommunicationWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task FileIfUnfiled_WhenTheWriteFails_ReportsFailed_AndNeverThrows()
    {
        var world = new World(reconciled: true, Unfiled());
        world.Dataverse
            .Setup(d => d.UpdateAsync("sprk_communication", CanonicalId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse down"));

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Failed);
        world.CommunicationWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task FileIfUnfiled_SaveCreatedItsOwnCommunication_IsNotAReconcile()
    {
        var world = new World(reconciled: false, Unfiled());

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.NotApplicable, "a new capture is created WITH its filing");
    }

    // ── Ontology D-48 / #1659: a classified communication filed to a matter queues the association-confirmed trigger ──

    [Fact]
    public async Task FileIfUnfiled_FiledToAMatter_WhenClassified_QueuesTheAssociationConfirmedTrigger_Once()
    {
        var trigger = FilingSignalDoubles.Trigger();
        var world = new World(reconciled: true, Unfiled(), Unfiled(), FiledToMatter(classified: true)) { Trigger = trigger };

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Filed);
        FilingSignalDoubles.VerifyEnqueuedOnce(trigger, MatterId, CanonicalId);
        trigger.Verify(t => t.EnqueueAsync(It.IsAny<SignalTriggerKind>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FileIfUnfiled_FiledToAMatter_WhenNotClassified_QueuesNothing()
    {
        var trigger = FilingSignalDoubles.Trigger();
        var world = new World(reconciled: true, Unfiled(), Unfiled(), FiledToMatter(classified: false)) { Trigger = trigger };

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Filed);
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
    }

    [Fact]
    public async Task FileIfUnfiled_AlreadyFiled_QueuesNothing()
    {
        var trigger = FilingSignalDoubles.Trigger();
        var world = new World(reconciled: true, FiledToMatter(classified: true)) { Trigger = trigger };

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.AlreadyFiled);
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
    }

    [Fact]
    public async Task FileIfUnfiled_CallerMayNotChangeIt_QueuesNothing()
    {
        var trigger = FilingSignalDoubles.Trigger();
        var world = new World(reconciled: true, Unfiled(), Unfiled(), FiledToMatter(classified: true)) { Trigger = trigger };
        world.CallerMay(AccessRights.Read);

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.NotPermitted);
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
    }

    [Fact]
    public async Task FileIfUnfiled_WhenTheWriteFails_QueuesNothing()
    {
        var trigger = FilingSignalDoubles.Trigger();
        var world = new World(reconciled: true, Unfiled(), Unfiled(), FiledToMatter(classified: true)) { Trigger = trigger };
        world.Dataverse
            .Setup(d => d.UpdateAsync("sprk_communication", CanonicalId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse down"));

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Failed);
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
    }

    [Fact]
    public async Task FileIfUnfiled_UnfiledSave_QueuesNothing()
    {
        var trigger = FilingSignalDoubles.Trigger();
        var world = new World(reconciled: true, Unfiled(), Unfiled(), FiledToMatter(classified: true)) { Trigger = trigger };

        var (_, result) = await world.SaveAsync(PaneSave(target: null));

        result.Should().Be(ReconciledFilingResult.NotApplicable);
        FilingSignalDoubles.VerifyNothingEnqueued(trigger);
    }

    [Fact]
    public async Task FileIfUnfiled_WhenTheTriggerThrows_IsStillFiled()
    {
        var trigger = FilingSignalDoubles.Trigger();
        trigger.Setup(t => t.EnqueueAsync(It.IsAny<SignalTriggerKind>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bus down"));
        var world = new World(reconciled: true, Unfiled(), Unfiled(), FiledToMatter(classified: true)) { Trigger = trigger };

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Filed, "the filing stands; a Signal that could not be refreshed live never fails the save");
        world.CommunicationWrites.Should().ContainSingle();
    }

    [Fact]
    public async Task FileIfUnfiled_WithNoTriggerRegistered_IsStillFiled()
    {
        var world = new World(reconciled: true, Unfiled(), Unfiled(), FiledToMatter(classified: true));

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Filed);
    }

    [Fact]
    public async Task FileIfUnfiled_QuickSaveRequest_FilesTheReconciledCommunicationToThePredictedRecord()
    {
        // The exact wire shape the ribbon Quick Save posts (quickSaveHelpers.buildEmailSaveRequest) — same route, same rule.
        var json = $$"""
        {
          "contentType": "Email",
          "triggerAiProcessing": true,
          "targetEntity": { "entityType": "Matter", "entityId": "{{MatterId}}", "displayName": "Acme v Beta" },
          "email": {
            "subject": "RE: Discovery schedule", "senderEmail": "counsel@contoso.com", "recipients": [],
            "isNameSystemDerived": true, "body": "<p>text</p>", "isBodyHtml": true,
            "internetMessageId": "<CAF0a1b2c3@mail.example.com>", "exchangeItemId": "AAMkAGI2TG93AAA="
          },
          "idempotencyKey": "qs-key"
        }
        """;
        var request = JsonSerializer.Deserialize<SaveRequest>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } })!;
        var world = new World(reconciled: true, Unfiled());

        var (_, result) = await world.SaveAsync(request);

        result.Should().Be(ReconciledFilingResult.Filed);
        ((EntityReference)world.CommunicationWrites.Should().ContainSingle().Subject["sprk_regardingmatter"]).Id.Should().Be(MatterId);
    }

    // ── spaarkeai-word-add-in-r1 task 129, behaviour review of 48a2f8687 ────────────────────────────────────────────

    [Fact]
    public async Task FileIfUnfiled_AWrittenFiling_ReconcilesTheThreadOnce_ThroughTheAfterRefileStep()
    {
        // F3: the reconcile runs in SecureChildReconciler.AfterRefileAsync, the step this filing (like every re-file writer)
        // runs — once, not also inside the association write it calls.
        var world = new World(reconciled: true, Unfiled());

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Filed);
        world.ThreadReconciles().Should().ContainSingle("once, through any overload")
            .Which.Arguments[0].Should().Be(CanonicalId);
        world.Threads.Verify(t => t.ResolveAndAssignAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()), Times.Never,
            "a save that reconciled to an existing row keeps that row's thread");
    }

    [Fact]
    public async Task FileIfUnfiled_AFilingNotWritten_NeverReconcilesTheThread()
    {
        var world = new World(reconciled: true, Unfiled());
        world.CallerMay(AccessRights.Read);

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.NotPermitted);
        world.ThreadReconciles().Should().BeEmpty("through any overload");
    }

    [Fact]
    public async Task Capture_ANewlyCapturedEmail_JoinsItsThread_AndIsGrantedTheAudienceTheJoinFixed()
    {
        // F2(a): an Office add-in save created a filed communication with NO thread, so it never showed on the record's
        // Messages panel. The capture now resolves a thread through the same path as the mailbox and send paths, then
        // grants it.
        var world = new World(reconciled: false, Unfiled());
        var assignment = ThreadAssignment.Anchored(Guid.NewGuid(), "sprk_matter", MatterId);
        ThreadResolutionRequest? asked = null;
        world.Threads
            .Setup(t => t.ResolveAndAssignAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ThreadResolutionRequest, CancellationToken>((r, _) => asked = r)
            .ReturnsAsync(assignment);

        var (capture, _) = await world.SaveAsync(PaneSave(Matter()));

        capture!.ReconciledToExisting.Should().BeFalse();
        asked.Should().NotBeNull();
        asked!.CommunicationId.Should().Be(CanonicalId);
        asked.ChannelType.Should().Be(Sprk.Bff.Api.Services.Communication.Models.CommunicationType.Email);
        asked.Message.InternetMessageId.Should().Be("<CAF0a1b2c3@mail.example.com>", "the resolution receives the email's own envelope");
        world.Grants.Verify(g => g.GrantMessageAccessAsync(CanonicalId, assignment, It.IsAny<CancellationToken>()), Times.Once);
        world.ThreadReconciles().Should().BeEmpty("a new row is not a re-file, through any overload");
    }

    [Fact]
    public async Task Capture_WhenTheThreadResolutionThrows_TheCaptureStillSucceeds()
    {
        var world = new World(reconciled: false, Unfiled());
        world.Threads
            .Setup(t => t.ResolveAndAssignAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated thread fault"));

        var (capture, _) = await world.SaveAsync(PaneSave(Matter()));

        capture.Should().NotBeNull("thread resolution is best-effort (NFR-02/NFR-04)");
        world.Grants.Verify(g => g.GrantMessageAccessAsync(It.IsAny<Guid>(), It.IsAny<ThreadAssignment>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Ontology task 042b (D-169): the filed communication's gate Decision Record takes the team-owned path ─────

    private static readonly Guid GateRecord = Guid.Parse("d0d0d0d0-0000-4000-8000-0000000000d1");
    private static readonly Guid GateOwner = Guid.Parse("a0000000-0000-4000-8000-0000000000d2");
    private static readonly Guid GateSharee = Guid.Parse("a0000000-0000-4000-8000-0000000000d3");
    private static readonly Guid MatterTeam = Guid.Parse("e0000000-0000-4000-8000-0000000000d4");

    /// <summary>The reconciled communication's user-owned gate record, shared with its participant, and the resolver's ordinary answer.</summary>
    private static GateSharingWorld GateRecordOfTheCommunication()
    {
        var gate = new GateSharingWorld()
            .WithGateRecord(GateRecord, CanonicalId, GateOwner)
            .Share(GateSharingWorld.Record, GateRecord, GateSharee);
        gate.OwnerAnswers(RecordOwnerResolution.Owned(MatterTeam));
        return gate;
    }

    [Fact(DisplayName = "Ontology task 042b (D-169): filing the reconciled communication under an ordinary record re-owns its gate Decision Record to the resolver's team, reads it back, then revokes its participant shares")]
    public async Task FileIfUnfiled_ReownsTheGateDecisionRecord_ThenRevokesItsShares()
    {
        var gate = GateRecordOfTheCommunication();
        var world = new World(reconciled: true, Unfiled()) { GateRecords = gate.Sharing };

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Filed);
        gate.Rows[(GateSharingWorld.Record, GateRecord)].GetAttributeValue<EntityReference>("owningteam").Id.Should().Be(MatterTeam);
        gate.RecordSharees(GateRecord).Should().BeEmpty();
    }

    [Fact(DisplayName = "Ontology task 042b (D-169): a gate Decision Record re-own that fails keeps the user owner and the shares, and the filing still succeeds")]
    public async Task FileIfUnfiled_AFailedGateRecordReown_KeepsItsSharesAndOwner_AndTheFilingStands()
    {
        var gate = GateRecordOfTheCommunication();
        gate.AssignLands = false;
        gate.AssignFault = new InvalidOperationException("Test: Dataverse refused the Assign.");
        var world = new World(reconciled: true, Unfiled()) { GateRecords = gate.Sharing };

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Filed);
        gate.Rows[(GateSharingWorld.Record, GateRecord)].GetAttributeValue<EntityReference>("owninguser").Id.Should().Be(GateOwner);
        gate.RecordSharees(GateRecord).Should().Equal(GateSharee);
    }
    [Fact(DisplayName = "Ontology task 042b round 2 (D-169): a gate Decision Record helper fault after the filing leaves the filing standing")]
    public async Task FileIfUnfiled_AGateHelperFault_LeavesTheFilingStanding()
    {
        var world = new World(reconciled: true, Unfiled()) { GateRecords = GateSharingWorld.Faulting() };

        var (_, result) = await world.SaveAsync(PaneSave(Matter()));

        result.Should().Be(ReconciledFilingResult.Filed, "the reconciler's after-re-file step isolates the gate helper's fault");
    }
}

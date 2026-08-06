using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Vertical-slice seam (ADR-038 <c>tests/integration/seam/**</c> KEEP path / spec FR-11 / NFR-05, FR-04 /
/// task 024) for the <c>communication_assessed</c> producer wired into <see cref="CommunicationEnrichmentService"/>
/// step 5. Drives the REAL <see cref="CommunicationEnrichmentService.EnrichAsync"/> orchestration (all steps,
/// each wrapped in the real non-fatal <c>RunStepAsync</c>) and doubles only the module boundaries: the
/// producer seam (<see cref="ICommunicationAssessedProducer"/>), the Dataverse read
/// (<see cref="IGenericEntityService"/>), and the triage facade (<see cref="ICommunicationTriageAi"/>).
/// Proves (a) step 5 invokes the producer with the expected signal shape on success, (b) a producer that
/// throws still lets enrichment complete without propagating (NFR-05), (c) task 024's FR-04 RI-confidence is
/// now a REAL computed score — not the pre-024 hardcoded <c>0</c> — for a high-urgency, well-associated
/// email (clears the shipped default gate threshold) vs. a low-urgency, weakly-associated one (does not),
/// and (d) a RI-confidence scoring failure degrades to confidence <c>0</c> without propagating (NFR-04).
/// </summary>
public sealed class CommsAssessedProducerSeamTests
{
    private const string CommunicationEntity = "sprk_communication";

    /// <summary>The shipped default gate threshold — used to prove the wired confidence actually
    /// clears/misses the REAL threshold <see cref="CommunicationRuleGate"/> evaluates against.</summary>
    private static readonly double DefaultThreshold = new CommsPolicyOptions().DefaultConfidenceThreshold;

    /// <summary>A high-deterministic-agreement provenance (rung 0, TopDeterministicConfidence 0.95) carrying
    /// a persisted AI-classify signal, so the "email-triage" step actually invokes the triage facade.</summary>
    private const string HighAgreementProvenanceJson =
        """
        {"version":1,"direction":"Incoming","decision":{"status":"Resolved","autoFiled":true,"killSwitchEnabled":true,"autoFileThreshold":0.85,"topDeterministicConfidence":0.95,"topConfidence":0.95,"aiInvolved":false,"reason":"test"},"rungsFired":["ExplicitReference"],"candidates":[],"signals":[{"category":"court-notice","confidence":0.6,"provenance":"ai-classify:category=court-notice:urgency=urgent:types=[sprk_matter]:actions=[calendar-deadline]:Court deadline.","obligations":["respond-by-deadline"]}]}
        """;

    /// <summary>A weak-deterministic-agreement provenance (TopDeterministicConfidence 0.2), no persisted
    /// classification signal — no triage facade call, urgency falls back to the neutral default.</summary>
    private const string WeakAgreementProvenanceJson =
        """
        {"version":1,"direction":"Incoming","decision":{"status":"PendingReview","autoFiled":false,"killSwitchEnabled":true,"autoFileThreshold":0.85,"topDeterministicConfidence":0.2,"topConfidence":0.2,"aiInvolved":false,"reason":"test"},"rungsFired":[],"candidates":[],"signals":[]}
        """;

    private static Entity RecordWithProvenance(string? provenanceJson)
    {
        var entity = new Entity(CommunicationEntity, Guid.NewGuid());
        if (provenanceJson is not null)
        {
            entity["sprk_associationprovenance"] = provenanceJson;
        }
        return entity;
    }

    /// <summary>Records every published signal.</summary>
    private sealed class CapturingAssessedProducer : ICommunicationAssessedProducer
    {
        public readonly List<CommunicationAssessedSignal> Published = new();

        public Task PublishAsync(CommunicationAssessedSignal signal, CancellationToken ct = default)
        {
            Published.Add(signal);
            return Task.CompletedTask;
        }
    }

    /// <summary>Throws on publish — the NFR-05 non-fatality probe.</summary>
    private sealed class ThrowingAssessedProducer : ICommunicationAssessedProducer
    {
        public int Calls { get; private set; }

        public Task PublishAsync(CommunicationAssessedSignal signal, CancellationToken ct = default)
        {
            Calls++;
            throw new InvalidOperationException("assessed producer boom");
        }
    }

    private static CommunicationEnrichmentService CreateService(ICommunicationAssessedProducer producer)
    {
        // The other steps run through the real RunStepAsync (non-fatal); loose mocks let them no-op/
        // fail-safe so this seam isolates the assessment-event step's producer behavior. The triage facade
        // is a loose mock too — GenericEntityService returns a default (empty) Entity, so
        // RunEmailTriageAsync's provenance read finds no signal and no-ops before ever calling the facade,
        // and ComputeRiConfidenceAsync's own read finds no provenance either (deterministic agreement 0).
        var entity = new Mock<IGenericEntityService>(MockBehavior.Loose);
        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Loose);
        return CreateService(producer, entity.Object, triageAi.Object);
    }

    private static CommunicationEnrichmentService CreateService(
        ICommunicationAssessedProducer producer,
        IGenericEntityService entityService,
        ICommunicationTriageAi triageAi)
    {
        var enqueuer = new Mock<IPostUploadIndexingEnqueuer>(MockBehavior.Loose);
        var config = new ConfigurationBuilder().Build();

        return new CommunicationEnrichmentService(
            enqueuer.Object,
            entityService,
            config,
            producer,
            triageAi,
            new NullCommunicationProposeAi(),
            new NullCommunicationCreateTaskAi(),
            new Mock<IActionSeam>(MockBehavior.Loose).Object,
            NullLogger<CommunicationEnrichmentService>.Instance);
    }

    private static NormalizedMessage Message() => new()
    {
        Direction = CommunicationDirection.Incoming,
        From = "sender@example.com",
        To = new[] { "a@example.com", "b@example.com" },
        Subject = "Quarterly filing",
    };

    [Fact]
    public async Task EnrichAsync_OnSuccess_InvokesAssessedProducerWithExpectedSignal()
    {
        var producer = new CapturingAssessedProducer();
        var sut = CreateService(producer);
        var communicationId = Guid.NewGuid();

        await sut.EnrichAsync(communicationId, CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        producer.Published.Should().ContainSingle("enrichment step 5 must invoke the producer, not just log");
        var signal = producer.Published[0];
        signal.CommunicationId.Should().Be(communicationId);
        signal.Direction.Should().Be(CommunicationDirection.Incoming);
        signal.Subject.Should().Be("Quarterly filing");
        signal.From.Should().Be("sender@example.com");
        signal.RecipientCount.Should().Be(2);
        signal.Confidence.Should().Be(0,
            "no persisted association/triage signal exists yet for this communication — the FR-04 scorer's " +
            "deterministic-agreement factor is 0, preserving the pre-024 conservative outcome for an " +
            "unassessed communication (no noise introduced)");
    }

    [Fact]
    public async Task EnrichAsync_WhenAssessedProducerThrows_CompletesWithoutPropagating()
    {
        var producer = new ThrowingAssessedProducer();
        var sut = CreateService(producer);

        Func<Task> act = () => sut.EnrichAsync(
            Guid.NewGuid(), CommunicationDirection.Outgoing, Message(), archivedDocumentId: null, CancellationToken.None);

        await act.Should().NotThrowAsync(
            "NFR-05: a communication_assessed producer failure must never fail enrichment");
        producer.Calls.Should().Be(1, "the producer was actually invoked before it threw");
    }

    // ── FR-04 (task 024): the signal now carries a REAL computed RI-confidence ──────────────────

    [Fact]
    public async Task EnrichAsync_HighUrgencyWellAssociatedEmail_EmitsConfidenceThatClearsDefaultGateThreshold()
    {
        var producer = new CapturingAssessedProducer();
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entityService
            .Setup(s => s.RetrieveAsync(CommunicationEntity, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecordWithProvenance(HighAgreementProvenanceJson));

        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Loose);
        triageAi
            .Setup(t => t.TriageAsync(It.IsAny<CommunicationTriageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommunicationTriageResult("Court / Filing", "Two-line summary.", new[] { "Respond by Friday" }, "Urgent", "Route"));

        var sut = CreateService(producer, entityService.Object, triageAi.Object);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        producer.Published.Should().ContainSingle();
        var confidence = producer.Published[0].Confidence;
        confidence.Should().BeApproximately(0.95, 1e-9, "Urgent (weight 1.0) x TopDeterministicConfidence 0.95");
        confidence.Should().BeGreaterThanOrEqualTo(DefaultThreshold,
            "a high-urgency, well-associated email must clear the shipped default rule-gate threshold " +
            "(CommunicationRuleGate would AUTHORIZE — see CommunicationRuleGateSeamTests for the gate's own proof)");
    }

    [Fact]
    public async Task EnrichAsync_LowUrgencyWeaklyAssociatedEmail_EmitsConfidenceBelowDefaultGateThreshold()
    {
        var producer = new CapturingAssessedProducer();
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entityService
            .Setup(s => s.RetrieveAsync(CommunicationEntity, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecordWithProvenance(WeakAgreementProvenanceJson));

        // No persisted AI-classify signal in this provenance ⇒ the "email-triage" step no-ops before ever
        // calling the facade (matches EmailTriageSeamTests' "no persisted signal" behavior); urgency falls
        // back to the neutral default inside ComputeRiConfidenceAsync.
        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Strict);

        var sut = CreateService(producer, entityService.Object, triageAi.Object);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        producer.Published.Should().ContainSingle();
        producer.Published[0].Confidence.Should().BeLessThan(DefaultThreshold,
            "noise (weak deterministic agreement, no urgency signal) must NOT clear the shipped default " +
            "rule-gate threshold (CommunicationRuleGate would DENY)");
    }

    [Fact]
    public async Task EnrichAsync_WhenRiConfidenceReadThrows_DegradesToZeroConfidence_WithoutPropagating()
    {
        var producer = new CapturingAssessedProducer();
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entityService
            .Setup(s => s.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse read boom"));
        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Loose);

        var sut = CreateService(producer, entityService.Object, triageAi.Object);

        Func<Task> act = () => sut.EnrichAsync(
            Guid.NewGuid(), CommunicationDirection.Incoming, Message(), archivedDocumentId: null, CancellationToken.None);

        await act.Should().NotThrowAsync(
            "NFR-04: a RI-confidence scoring failure (the Dataverse read) must never fail enrichment");
        producer.Published.Should().ContainSingle("the assessment-event step must still complete and publish");
        producer.Published[0].Confidence.Should().Be(0,
            "a scoring failure degrades to the safe/conservative default confidence (0), never throws");
    }
}

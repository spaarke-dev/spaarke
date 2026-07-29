using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Vertical-slice seam (ADR-038 <c>tests/integration/seam/**</c> KEEP path / spec FR-11 / NFR-05) for the
/// <c>communication_assessed</c> producer wired into <see cref="CommunicationEnrichmentService"/> step 5.
/// Drives the REAL <see cref="CommunicationEnrichmentService.EnrichAsync"/> orchestration (all five steps,
/// each wrapped in the real non-fatal <c>RunStepAsync</c>) and doubles only the module boundaries: the
/// producer seam (<see cref="ICommunicationAssessedProducer"/>) and the other steps' Dataverse/indexing
/// deps. Proves (a) step 5 invokes the producer with the expected signal shape on success, and (b) a
/// producer that throws still lets enrichment complete without propagating (NFR-05).
/// </summary>
public sealed class CommsAssessedProducerSeamTests
{
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
        // RunEmailTriageAsync's provenance read finds no signal and no-ops before ever calling the facade.
        var enqueuer = new Mock<IPostUploadIndexingEnqueuer>(MockBehavior.Loose);
        var entity = new Mock<IGenericEntityService>(MockBehavior.Loose);
        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Loose);
        var config = new ConfigurationBuilder().Build();

        return new CommunicationEnrichmentService(
            enqueuer.Object,
            entity.Object,
            config,
            producer,
            triageAi.Object,
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
}

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// FR-A4 affinity confirmation-write orchestration (R-1). Each test protects a concrete contract of
/// <see cref="AffinityConfirmationRecorder"/>: a human confirmation of a mapped regarding target records affinity
/// (one row per signal), while an unmapped/invalid target, a tenant with affinity disabled, and a reconstruction
/// failure are all best-effort NO-OPS that never throw (NFR-04) — so the user's confirmation is never failed.
/// The Dataverse boundary is the real <see cref="AffinityStore"/> over a mocked <see cref="IGenericEntityService"/>;
/// the envelope reconstruction is the <see cref="ICommunicationEnvelopeReader"/> test-seam.
/// </summary>
public class AffinityConfirmationRecorderTests
{
    private readonly Mock<ICommunicationEnvelopeReader> _reader = new();
    private readonly Mock<IGenericEntityService> _dataverse = new();
    private readonly Mock<IOptionsMonitor<AffinityOptions>> _options = new();

    private AffinityConfirmationRecorder CreateSut(AffinityOptions opts)
    {
        _options.Setup(o => o.CurrentValue).Returns(opts);
        var store = new AffinityStore(_dataverse.Object, NullLogger<AffinityStore>.Instance);
        return new AffinityConfirmationRecorder(_reader.Object, store, _options.Object, NullLogger<AffinityConfirmationRecorder>.Instance);
    }

    private static (NormalizedMessage, AssociationContext) Envelope(string? tenantKey = "tenant-a4") =>
        (new NormalizedMessage
        {
            Direction = CommunicationDirection.Incoming,
            From = "alice@acme.com",
            Subject = "matter update meeting",
            To = new[] { "bob@acme.com" },
        },
        new AssociationContext { TenantKey = tenantKey });

    private void ArrangeEnvelope() =>
        _reader.Setup(r => r.ReconstructEnvelopeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Envelope());

    private void ArrangeStoreCreatesNewRows()
    {
        // FindByNameAsync → no existing row → RecordConfirmationAsync CREATEs a fresh sprk_affinity row per signal.
        _dataverse.Setup(g => g.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        _dataverse.Setup(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task RecordAsync_EnabledMappedTarget_RecordsOneAffinityRowPerSignal()
    {
        ArrangeEnvelope();
        ArrangeStoreCreatesNewRows();
        var sut = CreateSut(new AffinityOptions { Enabled = true });

        var recorded = await sut.RecordAsync(Guid.NewGuid(), "sprk_matter", Guid.NewGuid().ToString(), CancellationToken.None);

        recorded.Should().BeGreaterThan(0, "sender/domain/subject-keyword/participant-set signals are extracted + recorded");
        _dataverse.Verify(g => g.CreateAsync(It.Is<Entity>(e => e.LogicalName == "sprk_affinity"), It.IsAny<CancellationToken>()),
            Times.Exactly(recorded), "each extracted signal increments (here creates) exactly one affinity row");
    }

    [Fact]
    public async Task RecordAsync_UnmappedTarget_IsNoOp_DoesNotReconstructOrWrite()
    {
        var sut = CreateSut(new AffinityOptions { Enabled = true });

        var recorded = await sut.RecordAsync(Guid.NewGuid(), "not_a_regarding_entity", Guid.NewGuid().ToString(), CancellationToken.None);

        recorded.Should().Be(0, "affinity only learns for ADR-024 regarding targets (mirrors the read rung's guard)");
        _reader.Verify(r => r.ReconstructEnvelopeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "an unmapped target short-circuits before the envelope round-trip");
        _dataverse.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordAsync_InvalidGuid_IsNoOp()
    {
        var sut = CreateSut(new AffinityOptions { Enabled = true });

        var recorded = await sut.RecordAsync(Guid.NewGuid(), "sprk_matter", "not-a-guid", CancellationToken.None);

        recorded.Should().Be(0);
        _dataverse.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordAsync_AffinityDisabledForTenant_IsNoOp()
    {
        ArrangeEnvelope();
        var sut = CreateSut(new AffinityOptions { Enabled = false });

        var recorded = await sut.RecordAsync(Guid.NewGuid(), "sprk_matter", Guid.NewGuid().ToString(), CancellationToken.None);

        recorded.Should().Be(0, "a tenant with affinity disabled records nothing (no Dataverse cost)");
        _dataverse.Verify(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordAsync_EnvelopeReconstructionThrows_IsNonFatalReturnsZero()
    {
        _reader.Setup(r => r.ReconstructEnvelopeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse blip"));
        var sut = CreateSut(new AffinityOptions { Enabled = true });

        var act = async () => await sut.RecordAsync(Guid.NewGuid(), "sprk_matter", Guid.NewGuid().ToString(), CancellationToken.None);

        var recorded = await act.Should().NotThrowAsync("affinity learning must NEVER fail the user's confirmation (NFR-04)");
        recorded.Subject.Should().Be(0);
    }

    [Fact]
    public async Task RecordAsync_EmptyTarget_IsNoOp()
    {
        var sut = CreateSut(new AffinityOptions { Enabled = true });

        var recorded = await sut.RecordAsync(Guid.NewGuid(), "", "", CancellationToken.None);

        recorded.Should().Be(0);
    }
}

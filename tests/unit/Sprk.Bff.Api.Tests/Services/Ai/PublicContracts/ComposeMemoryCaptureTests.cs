// spaarkeai-compose-r2 (FR-30, #629) — unit tests for the durable memory-CAPTURE facade.
//
// ComposeMemoryCapture maps each Compose-agnostic ComposeInsight onto a governed Record-scope MemoryItem
// and persists it via the SHARED IMemoryItemStore (no forked store — ADR-013 / §11 reuse). These are
// BEHAVIOR tests of that facade→store boundary:
//   • Does it build a full Record-scope envelope (scope, subject, fact, provenance threaded, TrustLevel
//     null, Tier-3 deletion/retention) and persist it?
//   • Does it SKIP a single store-rejected (DataverseFieldMirrorGuard) insight and continue the batch?
//   • Is it best-effort — an unexpected store failure degrades to Failed and never throws?
//   • Does an empty batch skip without touching the store?
//
// Mocking boundary (ADR-038 §4 module-boundary mocks): IMemoryItemStore is a genuine persistence seam.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PublicContracts;

public sealed class ComposeMemoryCaptureTests
{
    private const string Tenant = "tenant-compose-fr30";
    private const string SubjectType = "sprk_document";

    private readonly Mock<IMemoryItemStore> _store = new(MockBehavior.Strict);

    private ComposeMemoryCapture CreateSut() =>
        new(_store.Object, NullLogger<ComposeMemoryCapture>.Instance);

    private static ComposeMemoryProvenance Provenance(string? createdBy = null) => new()
    {
        TenantId = Tenant,
        SessionId = "session-1",
        CreatedBy = createdBy,
    };

    // ── Happy path: persists a full Record-scope envelope for every insight ──────────────────────
    [Fact]
    public async Task CaptureRecordInsightsAsync_PersistsRecordScopeMemoryItem_WithFullEnvelope()
    {
        var subjectId = Guid.NewGuid().ToString();
        var captured = new List<MemoryItem>();
        _store
            .Setup(s => s.UpsertAsync(It.IsAny<MemoryItem>(), Tenant, It.IsAny<CancellationToken>()))
            .Callback<MemoryItem, string, CancellationToken>((item, _, _) => captured.Add(item))
            .ReturnsAsync((MemoryItem item, string _, CancellationToken _) => item);

        var insights = new List<ComposeInsight>
        {
            new("defined-term", "Confidential Information", "info marked confidential",
                MemoryOrigin.AiDerived, Confidence: null, BindingId: "compose-defined-terms", LedgerRef: "compose-defined-terms@t2"),
            new("defined-term", "Effective Date", "the date the agreement takes effect",
                MemoryOrigin.User, Confidence: 0.9, BindingId: null, LedgerRef: null),
        };

        var outcome = await CreateSut().CaptureRecordInsightsAsync(SubjectType, subjectId, insights, Provenance(createdBy: "user-42"), CancellationToken.None);

        outcome.Status.Should().Be(ComposeMemoryCaptureStatus.Captured);
        outcome.Count.Should().Be(2);

        _store.Verify(s => s.UpsertAsync(It.IsAny<MemoryItem>(), Tenant, It.IsAny<CancellationToken>()), Times.Exactly(2));
        captured.Should().HaveCount(2);

        var first = captured[0];
        first.Scope.Should().Be(MemoryScope.Record);
        first.Version.Should().Be(MemoryItemContract.SchemaVersion);
        first.SubjectType.Should().Be(SubjectType);
        first.SubjectId.Should().Be(subjectId);

        // Fact — the "defined-term" category maps to the coarse KeyFact bucket; key/value carried through.
        first.Fact.Type.Should().Be(MemoryFactType.KeyFact);
        first.Fact.Key.Should().Be("Confidential Information");
        first.Fact.Value.Should().Be("info marked confidential");
        first.Fact.Source.Should().Be(MemoryOrigin.AiDerived);
        first.Fact.ConfirmedByUser.Should().BeFalse();

        // Provenance envelope threaded; TrustLevel carried INERT (null) — gate deferred (#629).
        first.Source.Should().Be(MemoryOrigin.AiDerived);
        first.BindingId.Should().Be("compose-defined-terms");
        first.LedgerRef.Should().Be("compose-defined-terms@t2");
        first.SessionId.Should().Be("session-1");
        first.TrustLevel.Should().BeNull();
        first.CreatedBy.Should().Be("user-42");

        // Tier-3 erasure envelope (ADR-015).
        first.DeletionPolicy.Should().Be("user-erasable");
        first.RetentionClass.Should().Be("tier-3-user-owned");
    }

    // ── One insight rejected by the store (DataverseFieldMirrorGuard) is skipped; batch continues ─
    [Fact]
    public async Task CaptureRecordInsightsAsync_WhenStoreRejectsFieldMirror_SkipsItemAndContinues()
    {
        var subjectId = Guid.NewGuid().ToString();
        var persisted = new List<MemoryItem>();

        // General setup: persist. Specific setup (defined last → wins) for the mirror key: reject.
        _store
            .Setup(s => s.UpsertAsync(It.IsAny<MemoryItem>(), Tenant, It.IsAny<CancellationToken>()))
            .Callback<MemoryItem, string, CancellationToken>((item, _, _) => persisted.Add(item))
            .ReturnsAsync((MemoryItem item, string _, CancellationToken _) => item);
        _store
            .Setup(s => s.UpsertAsync(It.Is<MemoryItem>(m => m.Fact.Key == "Matter Name"), Tenant, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Record-scope fact mirrors a live Dataverse field."));

        var insights = new List<ComposeInsight>
        {
            new("defined-term", "Matter Name", "mirrors sprk_name", MemoryOrigin.AiDerived, null, null, null),
            new("defined-term", "Good Term", "a genuinely durable definition", MemoryOrigin.AiDerived, null, null, null),
        };

        var act = async () => await CreateSut().CaptureRecordInsightsAsync(SubjectType, subjectId, insights, Provenance(), CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.Status.Should().Be(ComposeMemoryCaptureStatus.Captured);
        result.Subject.Count.Should().Be(1);

        persisted.Should().ContainSingle(i => i.Fact.Key == "Good Term");
        persisted.Should().NotContain(i => i.Fact.Key == "Matter Name");
    }

    // ── Best-effort: an unexpected store failure degrades to Failed and never throws ─────────────
    [Fact]
    public async Task CaptureRecordInsightsAsync_WhenStoreThrowsUnexpected_ReturnsFailed_DoesNotThrow()
    {
        var subjectId = Guid.NewGuid().ToString();
        _store
            .Setup(s => s.UpsertAsync(It.IsAny<MemoryItem>(), Tenant, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cosmos exploded"));

        var insights = new List<ComposeInsight>
        {
            new("defined-term", "Term", "definition", MemoryOrigin.AiDerived, null, null, null),
        };

        var act = async () => await CreateSut().CaptureRecordInsightsAsync(SubjectType, subjectId, insights, Provenance(), CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.Status.Should().Be(ComposeMemoryCaptureStatus.Failed);
        result.Subject.Reason.Should().NotBeNullOrEmpty();
    }

    // ── Empty batch skips cleanly without touching the store ─────────────────────────────────────
    [Fact]
    public async Task CaptureRecordInsightsAsync_EmptyInsights_SkipsWithoutTouchingStore()
    {
        var outcome = await CreateSut().CaptureRecordInsightsAsync(
            SubjectType, Guid.NewGuid().ToString(), Array.Empty<ComposeInsight>(), Provenance(), CancellationToken.None);

        outcome.Status.Should().Be(ComposeMemoryCaptureStatus.Skipped);
        outcome.Reason.Should().NotBeNullOrEmpty();
        _store.Verify(s => s.UpsertAsync(It.IsAny<MemoryItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

// spaarkeai-compose-r2 (FR-30, #629) — CAPTURE→RECALL seam test for the durable memory facade.
//
// This is a facade→store ROUND-TRIP over the REAL FakeMemoryItemStore (this repo has NO live-Cosmos CI
// tier — the sibling MemoryWriteCaptureRecallEvalTests uses the same fake). The fake replicates the
// PRODUCTION MemoryItemStore's deterministic upsert-by-(scope, fact.Type, fact.Key) supersession + the
// DataverseFieldMirrorGuard rejection, so this proves the CAPTURE→RECALL logic end-to-end at the
// facade+store CONTRACT level: ComposeMemoryCapture.CaptureRecordInsightsAsync(...) → store.UpsertAsync
// → store.GetForRecordAsync reads the same facts back.
//
// The true through-the-wire Cosmos + HTTP E2E (capture on Save, recall via
// GET /api/memory/records/sprk_document/{id}) is dev/UAT-verified (compose E2E DoD ✅◐ E2E-pending:
// needs Cosmos) — not runnable in CI without a Cosmos emulator tier.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Seam.Ai.Memory;

public sealed class ComposeMemoryCaptureRecallTests
{
    private const string Tenant = "tenant-compose-recall-fr30";
    private const string SubjectType = "sprk_document";

    [Fact]
    public async Task Capture_ThenRecall_RoundTripsRecordScopeInsightsThroughTheStore()
    {
        var store = new FakeMemoryItemStore();
        var sut = new ComposeMemoryCapture(store, NullLogger<ComposeMemoryCapture>.Instance);
        var documentId = Guid.NewGuid().ToString();

        var insights = new List<ComposeInsight>
        {
            new("defined-term", "Confidential Information", "information marked confidential by either party",
                MemoryOrigin.AiDerived, Confidence: null, BindingId: "compose-defined-terms", LedgerRef: "compose-defined-terms@t1"),
            new("defined-term", "Effective Date", "the date on which this agreement takes effect",
                MemoryOrigin.User, Confidence: null, BindingId: null, LedgerRef: null),
        };

        // CAPTURE
        var outcome = await sut.CaptureRecordInsightsAsync(
            SubjectType, documentId, insights,
            new ComposeMemoryProvenance { TenantId = Tenant, SessionId = "session-recall-1", CreatedBy = "user-7" },
            CancellationToken.None);

        outcome.Status.Should().Be(ComposeMemoryCaptureStatus.Captured);
        outcome.Count.Should().Be(2);

        // RECALL — read the SAME facts back through the store's Record-scope query.
        var recalled = await store.GetForRecordAsync(SubjectType, documentId, CancellationToken.None);

        recalled.Should().HaveCount(2);
        recalled.Should().OnlyContain(i => i.Scope == MemoryScope.Record && i.SubjectId == documentId);

        var confidential = recalled.Single(i => i.Fact.Key == "Confidential Information");
        confidential.Fact.Value.Should().Be("information marked confidential by either party");
        confidential.Fact.Type.Should().Be(MemoryFactType.KeyFact);
        confidential.Source.Should().Be(MemoryOrigin.AiDerived);
        confidential.BindingId.Should().Be("compose-defined-terms");
        confidential.LedgerRef.Should().Be("compose-defined-terms@t1");
        confidential.SessionId.Should().Be("session-recall-1");
        confidential.TrustLevel.Should().BeNull();               // gate deferred (#629) — carried inert
        confidential.DeletionPolicy.Should().Be("user-erasable"); // Tier-3 (ADR-015)
    }

    [Fact]
    public async Task Capture_RepeatedSameKey_SupersedesRatherThanDuplicates()
    {
        var store = new FakeMemoryItemStore();
        var sut = new ComposeMemoryCapture(store, NullLogger<ComposeMemoryCapture>.Instance);
        var documentId = Guid.NewGuid().ToString();

        static ComposeInsight Term(string value) =>
            new("defined-term", "Governing Law", value, MemoryOrigin.AiDerived, null, null, null);

        await sut.CaptureRecordInsightsAsync(SubjectType, documentId, new[] { Term("Delaware") },
            new ComposeMemoryProvenance { TenantId = Tenant }, CancellationToken.None);
        await sut.CaptureRecordInsightsAsync(SubjectType, documentId, new[] { Term("New York") },
            new ComposeMemoryProvenance { TenantId = Tenant }, CancellationToken.None);

        // Deterministic upsert-by-(scope, Type, Key) supersession — one document, latest value wins.
        var recalled = await store.GetForRecordAsync(SubjectType, documentId, CancellationToken.None);
        recalled.Should().ContainSingle();
        recalled[0].Fact.Value.Should().Be("New York");
    }
}

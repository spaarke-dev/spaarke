using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Memory;

/// <summary>
/// Governance-envelope contract tests (task AIR2-051, FR-B-02). The envelope itself shipped with
/// MemoryItem v1 (task 016) and its Cosmos shape <see cref="MemoryItemDocument"/> (task 050) —
/// this suite pins the envelope's MIGRATION-SAFETY and INERTNESS contracts that no prior test covered:
///
/// (a) TOLERANT READER, missing fields — a document persisted WITHOUT the optional envelope fields
///     deserializes with the documented defaults (no exception, no data loss); the field-by-field
///     default table lives in <c>projects/spaarke-ai-architecture-redesign-r2/notes/051-envelope-field-mapping.md</c>.
/// (b) TOLERANT READER, extra fields — a document carrying UNKNOWN future-additive fields still
///     deserializes (a v1 reader survives a future additive v1.x).
/// (c) LEGACY MIGRATION SEAM — <see cref="MemoryItemMigration.FromLegacyMatterFact"/> reads a
///     pre-envelope matter-keyed <see cref="MemoryFact"/> as a Record-scope item with Tier-3
///     defaults and zero fact-field loss (seam retained by ruling even though no docs were migrated).
/// (d) NEGATIVE — <c>TrustLevel</c> is INERT: any value (including "untrusted") passes the write
///     path unchanged; enforcement is DEFERRED to the governance project (FR-B-08).
/// (e) The envelope admits a <c>source: insights-engine</c> item with no special-casing (FR-B-02).
/// </summary>
public class MemoryItemEnvelopeTests
{
    private const string ProjectId = "3f6a4bc2-0001-4c4e-9d2a-aaaaaaaaaaaa";

    // =========================================================================
    // (a) Tolerant reader — pre-envelope document applies documented defaults
    // =========================================================================

    [Fact]
    public void DeserializeDocument_MissingAllOptionalEnvelopeFields_AppliesDocumentedDefaults()
    {
        // A document carrying ONLY the four required members + the fact — i.e. the minimal shape a
        // pre-envelope writer could have persisted. Every optional envelope field is absent.
        const string json = """
        {
          "id": "mem_record_3_0123456789abcdef",
          "subjectId": "3f6a4bc2-0001-4c4e-9d2a-aaaaaaaaaaaa",
          "scope": "record",
          "subjectType": "project",
          "fact": {
            "id": "fact-1",
            "type": "KeyFact",
            "key": "Scope Risk",
            "value": "Fixed-fee overrun risk on phase 2",
            "source": "user",
            "confirmedByUser": true,
            "confidence": 1.0,
            "recordedAt": "2026-07-01T00:00:00Z"
          }
        }
        """;

        var document = JsonSerializer.Deserialize<MemoryItemDocument>(json, MemoryItemContract.SerializerOptions);

        // No exception, no data loss on the fact.
        document.Should().NotBeNull();
        document!.Fact.Key.Should().Be("Scope Risk");
        document.Fact.Value.Should().Be("Fixed-fee overrun risk on phase 2");
        document.Fact.ConfirmedByUser.Should().BeTrue();

        // Documented defaults (see notes/051-envelope-field-mapping.md):
        document.DocumentType.Should().Be(MemoryItemStore.DocumentTypeValue, "discriminator defaults to memory-item");
        document.Version.Should().Be(MemoryItemContract.SchemaVersion, "unstamped documents read as current v1");
        document.Source.Should().Be(MemoryOrigin.User, "absent provenance origin defaults to the trusted 'user' class");
        document.TenantId.Should().BeNull();
        document.UserId.Should().BeNull();
        document.BindingId.Should().BeNull();
        document.LedgerRef.Should().BeNull();
        document.SessionId.Should().BeNull();
        document.TurnId.Should().BeNull();
        document.TrustLevel.Should().BeNull();
        document.Sensitivity.Should().BeNull();
        document.Expiration.Should().BeNull();
        document.DeletionPolicy.Should().BeNull();
        document.RetentionClass.Should().BeNull();
        document.UpdatedAt.Should().BeNull();
        document.CreatedBy.Should().BeNull();
        document.Ttl.Should().BeNull("absent ttl means no per-item expiry (container default is -1)");

        // The consumer-facing MemoryItem v1 projection still works over the defaulted document.
        var item = document.ToItem();
        item.Scope.Should().Be(MemoryScope.Record);
        item.SubjectType.Should().Be("project");
        item.SubjectId.Should().Be(ProjectId);
        item.Fact.Value.Should().Be("Fixed-fee overrun risk on phase 2");
    }

    // =========================================================================
    // (b) Tolerant reader — unknown future-additive fields are ignored
    // =========================================================================

    [Fact]
    public void DeserializeDocument_WithUnknownFutureAdditiveFields_IgnoresThemWithoutError()
    {
        const string json = """
        {
          "id": "mem_user_3_0123456789abcdef",
          "subjectId": "9d81c0de-0003-4c4e-9d2a-cccccccccccc",
          "scope": "user",
          "userId": "9d81c0de-0003-4c4e-9d2a-cccccccccccc",
          "fact": {
            "type": "KeyFact",
            "key": "Drafting Style",
            "value": "Plain-English, short sentences"
          },
          "trustLevel": "session-derived",
          "futureAdditiveScalar": 42,
          "futureAdditiveObject": { "nested": true, "list": [1, 2, 3] }
        }
        """;

        var document = JsonSerializer.Deserialize<MemoryItemDocument>(json, MemoryItemContract.SerializerOptions);

        document.Should().NotBeNull("a v1 reader must survive a future additive v1.x document");
        document!.Scope.Should().Be(MemoryScope.User);
        document.UserId.Should().Be("9d81c0de-0003-4c4e-9d2a-cccccccccccc");
        document.TrustLevel.Should().Be("session-derived", "known fields deserialize normally alongside unknown ones");
        document.Fact.Key.Should().Be("Drafting Style");
    }

    // =========================================================================
    // (c) Legacy migration seam — pre-envelope MemoryFact reads forward losslessly
    // =========================================================================

    [Fact]
    public void FromLegacyMatterFact_PreEnvelopeFact_AppliesTier3DefaultsWithoutFactLoss()
    {
        var legacyFact = new MemoryFact
        {
            Type = MemoryFactType.KeyDate,
            Key = "Markman Hearing",
            Value = "July 15, 2026",
            Source = "ai-extraction",
            ConfirmedByUser = false,
            Confidence = 0.85,
            RecordedAt = DateTimeOffset.Parse("2026-03-01T10:00:00Z"),
        };

        var item = MemoryItemMigration.FromLegacyMatterFact(legacyFact, matterId: "matter-123", createdBy: "user-1");

        item.Scope.Should().Be(MemoryScope.Record);
        item.SubjectType.Should().Be(MemoryItemMigration.LegacySubjectType);
        item.SubjectId.Should().Be("matter-123");
        item.Fact.Should().BeSameAs(legacyFact, "the fact is reused verbatim — no field is copied lossily");
        item.Source.Should().Be(MemoryOrigin.AiDerived, "legacy 'ai-extraction' maps to the v1 ai-derived origin");
        item.CreatedAt.Should().Be(legacyFact.RecordedAt, "RecordedAt is preserved as the audit creation stamp");
        item.CreatedBy.Should().Be("user-1");
        item.DeletionPolicy.Should().Be("user-erasable", "Tier-3 default (ADR-015)");
        item.RetentionClass.Should().Be("tier-3-user-owned", "Tier-3 default (ADR-015)");
    }

    // =========================================================================
    // (d) NEGATIVE — TrustLevel is INERT (carried, never a deny path; FR-B-08)
    // =========================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("untrusted")]
    [InlineData("external-content")]
    [InlineData("session-derived")]
    public void Write_AnyTrustLevelValue_IsAcceptedAndCarriedVerbatim(string? trustLevel)
    {
        var item = BuildRecordItem() with { TrustLevel = trustLevel };
        MemoryItem? persisted = null;

        var result = MemoryItemWriter.Write(item, i => persisted = i);

        persisted.Should().NotBeNull("trustLevel must never gate the write — enforcement is deferred (FR-B-08)");
        persisted!.TrustLevel.Should().Be(trustLevel, "the value is carried as metadata, not normalized or acted on");
        result.TrustLevel.Should().Be(trustLevel);
    }

    // =========================================================================
    // (e) source: insights-engine admitted with no special-casing (FR-B-02)
    // =========================================================================

    [Fact]
    public void Write_InsightsEngineSourcedItem_PersistsThroughTheSamePathAsAnyOrigin()
    {
        var item = BuildRecordItem() with { Source = MemoryOrigin.InsightsEngine };
        MemoryItem? persisted = null;

        var result = MemoryItemWriter.Write(item, i => persisted = i);

        persisted.Should().NotBeNull();
        persisted!.Source.Should().Be(MemoryOrigin.InsightsEngine);
        result.Version.Should().Be(MemoryItemContract.SchemaVersion);

        // The Cosmos document shape carries the origin 1:1 — no insights-engine special case exists.
        var document = MemoryItemDocument.FromItem(result, id: "mem_record_3_feedfeedfeedfeed", subjectKey: ProjectId, tenantId: null);
        document.Source.Should().Be(MemoryOrigin.InsightsEngine);
        document.ToItem().Source.Should().Be(MemoryOrigin.InsightsEngine);
    }

    private static MemoryItem BuildRecordItem() => new()
    {
        Version = MemoryItemContract.SchemaVersion,
        Scope = MemoryScope.Record,
        SubjectType = "project",
        SubjectId = ProjectId,
        Fact = new MemoryFact { Type = MemoryFactType.KeyFact, Key = "Scope Risk", Value = "Overrun risk", ConfirmedByUser = true },
    };
}

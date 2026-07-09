using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// <b>MemoryItem v1 contract test</b> (spaarke-ai-architecture-redesign-r2 task 016 — the LAST
/// Phase-A0 seam; spec FR-A0-03 / FR-B-02). Locks the structured-memory OBJECT + governance envelope
/// that the Memory Service generalization (050), the envelope (051), governance (052), the
/// ContextEnvelope Memory slice (015), and <c>memory.write</c> (057) all bind to — so a schema drift
/// (dropped envelope field, matter-only regression, opened version, an embedding leaking in, or a
/// broken tolerant-reader migration of a legacy fact) fails HERE, not downstream. Unblocks Compose r2
/// FR-30 (persist AI-derived insights).
/// </summary>
/// <remarks>
/// <para>
/// <b>Self-contained by design</b>: the walking-skeleton producer (<see cref="MemoryItemWriter.Write"/>),
/// consumer (<see cref="MemoryItemReader"/>), and migration (<see cref="MemoryItemMigration"/>) are pure
/// over an in-memory list — no DI, no <c>WebApplicationFactory</c>, no <c>Mock&lt;HttpMessageHandler&gt;</c>,
/// no DI-registration assertions (ADR-038 KEEP-path contract test).
/// </para>
/// <para>
/// <b>KEEP rationale (maintain-class, contract path)</b>: this is a published cross-project seam. The
/// object + envelope + generic <c>(entityType, entityId)</c> keying are the contract every Memory /
/// governance task builds on; this round-trip is its regression anchor.
/// </para>
/// </remarks>
public class MemoryItemContractTests
{
    // ─── Shape + envelope + version pin ────────────────────────────────────────────────────────

    [Fact]
    public void ForRecordItem_CarriesReusedFact_GovernanceEnvelope_AndVersionStamp()
    {
        var item = BuildRecordItem("project", "proj-42");

        // Version stamp (tolerant-reader gate).
        item.Version.Should().Be(MemoryItemContract.SchemaVersion);
        MemoryItemContract.SchemaVersion.Should().Be("memory-item/v1");

        // Reused MemoryFact members (Type/Key/Value/Source/ConfirmedByUser/Confidence/RecordedAt).
        item.Fact.Type.Should().Be(MemoryFactType.KeyFact);
        item.Fact.Key.Should().Be("Contract Value");
        item.Fact.Value.Should().Be("$2.4M");
        item.Fact.Confidence.Should().Be(0.9);

        // Governance / provenance envelope present.
        item.Scope.Should().Be(MemoryScope.Record);
        item.SubjectType.Should().Be("project");
        item.SubjectId.Should().Be("proj-42");
        item.Source.Should().Be(MemoryOrigin.AiDerived);
        item.BindingId.Should().Be("bind-1");
        item.LedgerRef.Should().Be("bind-1@t3");
        item.TrustLevel.Should().Be("unverified", "trustLevel is carried as metadata (enforcement deferred)");
        item.Sensitivity.Should().Be("normal");
        item.DeletionPolicy.Should().Be("user-erasable");
        item.RetentionClass.Should().Be("tier-3-user-owned");
        item.CreatedBy.Should().Be("agent");
    }

    [Fact]
    public void Item_PartitionKey_IsSubjectId_ForRecord_AndUserId_ForUser()
    {
        BuildRecordItem("invoice", "inv-7").PartitionKey.Should().Be("inv-7",
            "record memory is partitioned by SUBJECT (entityId), not tenantId");
        BuildUserItem("user-9").PartitionKey.Should().Be("user-9",
            "user memory is partitioned by userId");
    }

    // ─── Generic (entityType, entityId) keying — NOT matter-only ───────────────────────────────

    [Fact]
    public void RecordMemory_IsKeyedGenerically_ByEntityTypeAndEntityId_NotMatterOnly()
    {
        var store = new List<MemoryItem>();
        // Three different NON-matter entity types + a same-id-different-type collision guard.
        MemoryItemWriter.Write(BuildRecordItem("project", "shared-id"), store.Add);
        MemoryItemWriter.Write(BuildRecordItem("invoice", "shared-id"), store.Add);
        MemoryItemWriter.Write(BuildRecordItem("work-assignment", "wa-1"), store.Add);

        MemoryItemReader.ForRecord(store, "project", "shared-id").Should().ContainSingle(
            "a non-matter entity (project) keys correctly — Record scope is generic (entityType, entityId)");
        MemoryItemReader.ForRecord(store, "invoice", "shared-id").Should().ContainSingle(
            "the SAME id under a different entityType is a DISTINCT subject — keying is the (type, id) pair");
        MemoryItemReader.ForRecord(store, "work-assignment", "wa-1").Should().ContainSingle();
    }

    // ─── Producer → consumer round-trip honoring scope ─────────────────────────────────────────

    [Fact]
    public void Producer_WritesItem_Consumer_ReadsBack_HonoringScope()
    {
        var store = new List<MemoryItem>();

        var recordItem = MemoryItemWriter.Write(BuildRecordItem("project", "proj-42"), store.Add);
        var userItem = MemoryItemWriter.Write(BuildUserItem("user-9"), store.Add);

        var recordReadBack = MemoryItemReader.ForRecord(store, "project", "proj-42");
        recordReadBack.Should().ContainSingle().Which.Id.Should().Be(recordItem.Id);

        var userReadBack = MemoryItemReader.ForUser(store, "user-9");
        userReadBack.Should().ContainSingle().Which.Id.Should().Be(userItem.Id);

        // Scope isolation: a record read never returns the user item and vice-versa.
        MemoryItemReader.ForRecord(store, "project", "proj-42")
            .Should().NotContain(i => i.Scope == MemoryScope.User);
        MemoryItemReader.ForUser(store, "user-9")
            .Should().NotContain(i => i.Scope == MemoryScope.Record);
    }

    // ─── Tolerant-reader MIGRATION of a legacy matter-keyed fact ───────────────────────────────

    [Fact]
    public void FromLegacyMatterFact_MigratesToRecordScope_WithMatterDefaults_NoDataLoss()
    {
        var legacyFact = new MemoryFact
        {
            Type = MemoryFactType.Party,
            Key = "Plaintiff",
            Value = "Company X",
            Source = "ai-extraction",
            ConfirmedByUser = true,
            Confidence = 0.83,
            RecordedAt = DateTimeOffset.Parse("2026-05-12T09:00:00Z"),
        };

        var migrated = MemoryItemMigration.FromLegacyMatterFact(legacyFact, matterId: "matter-77");

        migrated.Version.Should().Be(MemoryItemContract.SchemaVersion);
        migrated.Scope.Should().Be(MemoryScope.Record);
        migrated.SubjectType.Should().Be("matter", "legacy matter-keyed facts default to subjectType=matter");
        migrated.SubjectId.Should().Be("matter-77");
        migrated.Source.Should().Be(MemoryOrigin.AiDerived, "'ai-extraction' maps to the ai-derived origin");
        migrated.RetentionClass.Should().Be("tier-3-user-owned");
        migrated.CreatedAt.Should().Be(legacyFact.RecordedAt, "RecordedAt is preserved — no data loss");

        // The fact itself is reused verbatim (no field dropped).
        migrated.Fact.Should().BeSameAs(legacyFact);
        migrated.Fact.Key.Should().Be("Plaintiff");
        migrated.Fact.Value.Should().Be("Company X");
        migrated.Fact.ConfirmedByUser.Should().BeTrue();
        migrated.Fact.Confidence.Should().Be(0.83);
    }

    // ─── Structured object, NOT an embedding ───────────────────────────────────────────────────

    [Fact]
    public void SerializedItem_IsStructuredObject_NotAnEmbeddingVector()
    {
        var item = BuildRecordItem("project", "proj-42");

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(item, MemoryItemContract.SerializerOptions));
        var root = doc.RootElement;

        MemoryItemContract.IsStructuredObjectNotEmbedding(root).Should().BeTrue(
            "a MemoryItem is a structured object — it carries no embedding/vector field");

        // The structured fact is a plain string value, not a numeric vector.
        root.GetProperty("fact").GetProperty("value").ValueKind.Should().Be(JsonValueKind.String);
        foreach (var forbidden in new[] { "embedding", "embeddings", "vector", "vectors" })
        {
            root.TryGetProperty(forbidden, out _).Should().BeFalse($"'{forbidden}' must NOT appear on a MemoryItem");
        }
    }

    // ─── Tolerant reader: unknown additive field ignored ───────────────────────────────────────

    [Fact]
    public void Deserialize_ItemWithUnknownAdditiveField_IgnoresUnknown_AndKeepsKnownMembers()
    {
        var wireWithUnknown =
            """
            {
              "version": "memory-item/v1",
              "id": "mi-1",
              "scope": "record",
              "subjectType": "project",
              "subjectId": "proj-42",
              "source": "ai-derived",
              "fact": { "type": "KeyFact", "key": "Governing Law", "value": "NY", "confidence": 1.0 },
              "futureV1xField": { "nested": true }
            }
            """;

        var item = JsonSerializer.Deserialize<MemoryItem>(wireWithUnknown, MemoryItemContract.SerializerOptions);

        item.Should().NotBeNull("an unknown additive field must NEVER fail v1 deserialization (tolerant reader)");
        item!.Version.Should().Be("memory-item/v1");
        item.SubjectType.Should().Be("project");
        item.SubjectId.Should().Be("proj-42");
        item.Fact.Key.Should().Be("Governing Law");
        item.Fact.Value.Should().Be("NY");
    }

    // ─── NEGATIVE: Conversation scope is rejected (ADR-040 ledger facade) ───────────────────────

    [Fact]
    public void Write_ToConversationScope_IsRejected_MemoryItemIsRecordOrUserOnly()
    {
        var conversationScoped = BuildRecordItem("project", "proj-42") with { Scope = MemoryScope.Conversation };

        var act = () => MemoryItemWriter.Write(conversationScoped, _ => { });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*conversation*",
                "MemoryItem is Record/User scope only — conversation memory stays the ADR-040 ledger facade");
    }

    [Fact]
    public void Write_ToUnknownScope_IsRejected()
    {
        var badScope = BuildRecordItem("project", "proj-42") with { Scope = "workspace" };

        var act = () => MemoryItemWriter.Write(badScope, _ => { });

        act.Should().Throw<ArgumentException>(
            "only 'record' and 'user' are valid scopes; 'workspace' terminology maps to Record scope");
    }

    // ─── Provenance envelope is METADATA, not a write-gate ─────────────────────────────────────

    [Fact]
    public void Write_WithMinimalProvenance_IsNotGated_EnvelopeIsMetadataOnly()
    {
        var store = new List<MemoryItem>();

        // An AI-initiated write carrying source only — NO trustLevel, NO bindingId. It MUST still write
        // (memory.write is AI-initiated + silent per FR-B-08; the envelope describes, it does not gate).
        var minimal = new MemoryItem
        {
            Version = MemoryItemContract.SchemaVersion,
            Scope = MemoryScope.User,
            UserId = "user-9",
            Source = MemoryOrigin.AiDerived,
            Fact = new MemoryFact { Type = MemoryFactType.KeyFact, Key = "Preference", Value = "concise" },
        };

        var act = () => MemoryItemWriter.Write(minimal, store.Add);

        act.Should().NotThrow("the provenance envelope is metadata — a missing trustLevel/bindingId does NOT gate the write");
        store.Should().ContainSingle();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────────────────────

    private static MemoryItem BuildRecordItem(string subjectType, string subjectId) => new()
    {
        Version = MemoryItemContract.SchemaVersion,
        Scope = MemoryScope.Record,
        SubjectType = subjectType,
        SubjectId = subjectId,
        Source = MemoryOrigin.AiDerived,
        BindingId = "bind-1",
        LedgerRef = "bind-1@t3",
        SessionId = "sess-1",
        TurnId = 3,
        TrustLevel = "unverified",
        Sensitivity = "normal",
        DeletionPolicy = "user-erasable",
        RetentionClass = "tier-3-user-owned",
        CreatedBy = "agent",
        CreatedAt = DateTimeOffset.Parse("2026-07-08T12:00:00Z"),
        Fact = new MemoryFact
        {
            Type = MemoryFactType.KeyFact,
            Key = "Contract Value",
            Value = "$2.4M",
            Source = "ai-extraction",
            Confidence = 0.9,
        },
    };

    private static MemoryItem BuildUserItem(string userId) => new()
    {
        Version = MemoryItemContract.SchemaVersion,
        Scope = MemoryScope.User,
        UserId = userId,
        Source = MemoryOrigin.User,
        DeletionPolicy = "user-erasable",
        RetentionClass = "tier-3-user-owned",
        Fact = new MemoryFact
        {
            Type = MemoryFactType.KeyFact,
            Key = "Preferred Tone",
            Value = "concise",
        },
    };
}

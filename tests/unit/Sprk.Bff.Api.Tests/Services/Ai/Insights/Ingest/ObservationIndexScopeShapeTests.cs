using System;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.Insights.Ingest;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Insights.Ingest;

/// <summary>
/// Unit tests for Wave D6 (task 035) hybrid scope shape projection in
/// <see cref="ObservationIndexUpserter"/>. Covers the writer-behavior table from design-a6 §4.4
/// + NFR-08 backward-compat invariant for Phase 1 matter-subject Observations.
/// </summary>
/// <remarks>
/// The actual <c>UpsertAsync</c> path requires <see cref="Azure.Search.Documents.Indexes.SearchIndexClient"/>
/// (sealed, hard to mock) — these tests target the deterministic helpers
/// <see cref="ObservationIndexUpserter.BuildScopeEntry"/> + <see cref="ObservationIndexUpserter.ParseSubject"/>
/// which is where the design-a6 §4 contract lives.
/// </remarks>
public sealed class ObservationIndexScopeShapeTests
{
    private static readonly DateTimeOffset FixedAsOf = new(2026, 6, 3, 12, 0, 0, TimeSpan.Zero);

    private static JsonElement Raw(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static ObservationArtifact BuildObservation(
        string subject,
        string? matterId = null,
        string? practiceArea = null,
        string tenantId = "tenant-acme")
    {
        return new ObservationArtifact
        {
            Id = $"obs:{subject}:outcomeCategory:doc-1",
            Subject = subject,
            Predicate = "outcomeCategory",
            Value = new Value
            {
                Raw = Raw("\"favorable\""),
                DisplayHint = "enum"
            },
            Evidence = Array.Empty<EvidenceRef>(),
            AsOf = FixedAsOf,
            ProducedBy = new ProducedBy
            {
                Kind = "playbook",
                Id = "playbook://universal-ingest@v1",
                Version = "v1"
            },
            Scope = new Scope
            {
                TenantId = tenantId,
                MatterId = matterId,
                PracticeArea = practiceArea
            },
            TenantId = tenantId,
            Confidence = 0.92
        };
    }

    // ─── ParseSubject ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("matter:M-2024-0341", "matter", "M-2024-0341")]
    [InlineData("project:p-abc-123", "project", "p-abc-123")]
    [InlineData("invoice:i-xyz-456", "invoice", "i-xyz-456")]
    [InlineData("MATTER:M-XYZ", "matter", "M-XYZ")] // scheme normalized to lowercase
    public void ParseSubject_Valid_ReturnsSchemeAndEntityId(
        string subject, string expectedScheme, string expectedEntityId)
    {
        var (scheme, entityId) = ObservationIndexUpserter.ParseSubject(subject);
        scheme.Should().Be(expectedScheme);
        entityId.Should().Be(expectedEntityId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("no-colon")]
    [InlineData(":no-scheme")]
    [InlineData("no-id:")]
    public void ParseSubject_Invalid_ReturnsNullPair(string? subject)
    {
        var (scheme, entityId) = ObservationIndexUpserter.ParseSubject(subject);
        scheme.Should().BeNull();
        entityId.Should().BeNull();
    }

    // ─── BuildScopeEntry — matter subjects (NFR-08 + design-a6 §4.4 row 1) ─────

    [Fact]
    public void BuildScopeEntry_MatterSubject_DualWriteOn_PopulatesAllScopeFields()
    {
        // Per design-a6 §4.4: matter subjects with DualWriteScopeMatterId=true should populate
        // BOTH scope.matterId (Phase 1 compat) AND scope.entityType/entityId (canonical).
        var obs = BuildObservation("matter:M-2024-0341", practiceArea: "ip-licensing");

        var scope = ObservationIndexUpserter.BuildScopeEntry(obs, dualWriteMatterId: true);

        scope.MatterId.Should().Be("M-2024-0341", because: "NFR-08 backward-compat dual-write");
        scope.EntityType.Should().Be("matter");
        scope.EntityId.Should().Be("M-2024-0341");
        scope.TenantId.Should().Be("tenant-acme");
        scope.PracticeArea.Should().Be("ip-licensing");
    }

    [Fact]
    public void BuildScopeEntry_MatterSubject_DualWriteOff_OmitsMatterIdWhenObservationScopeAlsoNull()
    {
        // With DualWriteScopeMatterId=false AND no Observation.Scope.MatterId already set,
        // the projection should leave scope.matterId null (canonical-only) and populate
        // scope.entityType/entityId.
        var obs = BuildObservation("matter:M-2024-0341");

        var scope = ObservationIndexUpserter.BuildScopeEntry(obs, dualWriteMatterId: false);

        scope.MatterId.Should().BeNull();
        scope.EntityType.Should().Be("matter");
        scope.EntityId.Should().Be("M-2024-0341");
    }

    [Fact]
    public void BuildScopeEntry_MatterSubject_DualWriteOff_PassesThroughObservationMatterId()
    {
        // Phase 1 legacy producer pattern: Observation.Scope.MatterId already set explicitly.
        // Should NOT be dropped — pass through even with DualWrite=false.
        var obs = BuildObservation("matter:M-2024-0341", matterId: "M-legacy-1");

        var scope = ObservationIndexUpserter.BuildScopeEntry(obs, dualWriteMatterId: false);

        scope.MatterId.Should().Be("M-legacy-1");
        scope.EntityType.Should().Be("matter");
        scope.EntityId.Should().Be("M-2024-0341");
    }

    // ─── BuildScopeEntry — project / invoice (design-a6 §4.4 rows 2, 3) ────────

    [Fact]
    public void BuildScopeEntry_ProjectSubject_LeavesMatterIdNull()
    {
        var obs = BuildObservation("project:p-abc-123");

        var scope = ObservationIndexUpserter.BuildScopeEntry(obs, dualWriteMatterId: true);

        scope.MatterId.Should().BeNull(because: "project Observations have null matterId per design-a6 §4.4");
        scope.EntityType.Should().Be("project");
        scope.EntityId.Should().Be("p-abc-123");
    }

    [Fact]
    public void BuildScopeEntry_InvoiceSubject_LeavesMatterIdNull()
    {
        var obs = BuildObservation("invoice:i-xyz-456");

        var scope = ObservationIndexUpserter.BuildScopeEntry(obs, dualWriteMatterId: true);

        scope.MatterId.Should().BeNull();
        scope.EntityType.Should().Be("invoice");
        scope.EntityId.Should().Be("i-xyz-456");
    }

    // ─── BuildScopeEntry — malformed subjects (defense in depth) ───────────────

    [Fact]
    public void BuildScopeEntry_MalformedSubject_NullSchemeAndEntityId()
    {
        var obs = BuildObservation("malformed-no-colon");

        var scope = ObservationIndexUpserter.BuildScopeEntry(obs, dualWriteMatterId: true);

        scope.EntityType.Should().BeNull();
        scope.EntityId.Should().BeNull();
        scope.MatterId.Should().BeNull();
        scope.TenantId.Should().Be("tenant-acme", because: "tenantId comes from Observation.Scope, not subject parse");
    }

    [Fact]
    public void BuildScopeEntry_MalformedSubject_PreservesExistingMatterId()
    {
        // Even if subject parse fails, an explicit Observation.Scope.MatterId is preserved.
        var obs = BuildObservation("malformed", matterId: "M-explicit");

        var scope = ObservationIndexUpserter.BuildScopeEntry(obs, dualWriteMatterId: true);

        scope.MatterId.Should().Be("M-explicit");
        scope.EntityType.Should().BeNull();
        scope.EntityId.Should().BeNull();
    }
}

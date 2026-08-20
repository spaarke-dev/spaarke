using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.DI;

/// <summary>
/// Regression guard for the R-5 Cosmos write-stoppage (dev 2026-08-07 → 2026-08-18): the CosmosClient
/// was serializing optional <c>int? Ttl</c> fields as <c>"ttl": null</c>, which a TTL-enabled container
/// rejects with HTTP 400 — silently swallowed, freezing History + memory writes for 11 days.
///
/// These tests assert the EXACT production serializer options
/// (<see cref="AiPersistenceModule.CosmosJsonSerializerOptions"/>) rather than a raw
/// <see cref="JsonSerializer"/> call. That distinction is the whole point: the pre-fix
/// <c>ChatSessionManagerTests</c> ttl-omission assertion passed because it used raw System.Text.Json
/// (which honors <c>[JsonIgnore(WhenWritingNull)]</c>), while PRODUCTION used the Cosmos SDK default
/// Newtonsoft serializer (which does NOT) and wrote the null. Binding these tests to the production
/// options closes that test-vs-runtime gap.
/// </summary>
public sealed class CosmosPersistenceSerializerTests
{
    private static readonly JsonSerializerOptions Prod = AiPersistenceModule.CosmosJsonSerializerOptions;

    [Fact]
    public void StoredSession_UnfiledTtlNull_OmitsTtlProperty_NoNullWritten()
    {
        var unfiled = new StoredSession { Id = "s1", SessionId = "s1", TenantId = "t1", Ttl = null };

        var json = JsonSerializer.Serialize(unfiled, Prod);

        // The exact 400-inducing token must never appear; the property must be OMITTED so the doc
        // rides the container's default TTL (FR-D10 intent).
        json.Should().NotContain("\"ttl\"",
            "an unfiled session must omit ttl (writing \"ttl\": null is rejected by a TTL-enabled container with HTTP 400 — the R-5 outage)");
        json.Should().NotContain("null");
    }

    [Fact]
    public void StoredSession_FiledTtlMinusOne_WritesNeverExpireSentinel()
    {
        var filed = new StoredSession { Id = "s2", SessionId = "s2", TenantId = "t1", Ttl = StoredSession.NeverExpireTtl };

        var json = JsonSerializer.Serialize(filed, Prod);

        json.Should().Contain("\"ttl\":-1", "a filed session must persist ttl=-1 (never expire) — a VALID Cosmos per-item TTL");
    }

    [Fact]
    public void MemoryItemDocument_TtlNull_OmitsTtlProperty_EvenWithoutJsonIgnoreAttribute()
    {
        // MemoryItemDocument.Ttl carries NO [JsonIgnore(WhenWritingNull)] attribute — so ONLY the
        // options' DefaultIgnoreCondition can omit it. This is the case that proves the fix covers the
        // memory-items container (TTL-enabled, defaultTtl=-1), not just sessions.
        var doc = new MemoryItemDocument
        {
            Id = "mem_user_pref_abc",
            SubjectId = "user-1",
            Scope = MemoryScope.User,
            Fact = new MemoryFact { Type = MemoryFactType.Preference, Key = "tone", Value = "concise" },
            Ttl = null,
        };

        var json = JsonSerializer.Serialize(doc, Prod);

        json.Should().NotContain("\"ttl\"",
            "a memory item with no retention class must omit ttl — DefaultIgnoreCondition.WhenWritingNull is the only thing that can omit it (no attribute exists)");
        json.Should().NotContain("null");
    }

    [Fact]
    public void StoredSession_RoundTrips_ThroughProductionOptions()
    {
        var original = new StoredSession
        {
            Id = "s3",
            SessionId = "s3",
            TenantId = "t1",
            LastActivity = DateTimeOffset.Parse("2026-08-18T12:00:00.0000000+00:00"),
            Ttl = StoredSession.NeverExpireTtl,
        };

        var json = JsonSerializer.Serialize(original, Prod);
        var restored = JsonSerializer.Deserialize<StoredSession>(json, Prod)!;

        restored.SessionId.Should().Be("s3");
        restored.TenantId.Should().Be("t1");
        restored.Ttl.Should().Be(StoredSession.NeverExpireTtl);
        // camelCase field names match the on-disk documents written since May 2026.
        json.Should().Contain("\"lastActivity\"");
        json.Should().Contain("\"tenantId\"");
    }
}

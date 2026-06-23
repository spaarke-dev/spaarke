// R3 Part 1 Phase 2 — Task 072 (2026-06-22)
// Wire-contract tests for `MembershipChangedEvent`. Locks:
//   - Round-trip preservation of every payload field (Serialize_RoundTrip).
//   - Enum-as-string serialization for both MutationType + PersonIdType
//     (Serialize_EnumAsString, Serialize_PersonIdType_*).
//   - Q4 owner-clarification coverage: PersonIdentityType includes
//     `Organization` and round-trips correctly.
//   - NFR-08: CorrelationId is required — System.Text.Json throws when
//     the property is missing from the source JSON.
//   - Forward compatibility: schemaVersion defaults to 1 when an older
//     payload omits it (Deserialize_OlderSchemaVersion_BackwardCompat).
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.2, NFR-08;
//            sibling MembershipResponseTests.cs (style + fixture conventions).

using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Membership.Events;

[Trait("status", "new")]
public class MembershipChangedEventTests
{
    private static readonly Guid PersonIdFixture =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EntityRecordIdFixture =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string CorrelationIdFixture = "corr-abc-123";

    private static MembershipChangedEvent BuildSampleEvent(
        MembershipMutationType mutation = MembershipMutationType.Added,
        PersonIdentityType idType = PersonIdentityType.User,
        DateTime? occurredOnUtc = null) =>
        new()
        {
            PersonId = PersonIdFixture,
            PersonIdType = idType,
            EntityLogicalName = "sprk_matter",
            EntityRecordId = EntityRecordIdFixture,
            SourceField = "sprk_assignedattorney1",
            Role = "assignedAttorney",
            MutationType = mutation,
            CorrelationId = CorrelationIdFixture,
            OccurredOnUtc = occurredOnUtc,
        };

    [Fact]
    public void Serialize_RoundTrip_PreservesAllFields()
    {
        // Arrange — full payload incl. optional OccurredOnUtc so the
        // roundtrip exercises every field exactly once.
        var occurred = new DateTime(2026, 6, 22, 14, 30, 0, DateTimeKind.Utc);
        var original = BuildSampleEvent(occurredOnUtc: occurred);

        // Act
        var json = JsonSerializer.Serialize(original, MembershipChangedEvent.SerializerOptions);
        var deserialized = JsonSerializer.Deserialize<MembershipChangedEvent>(
            json, MembershipChangedEvent.SerializerOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.PersonId.Should().Be(PersonIdFixture);
        deserialized.PersonIdType.Should().Be(PersonIdentityType.User);
        deserialized.EntityLogicalName.Should().Be("sprk_matter");
        deserialized.EntityRecordId.Should().Be(EntityRecordIdFixture);
        deserialized.SourceField.Should().Be("sprk_assignedattorney1");
        deserialized.Role.Should().Be("assignedAttorney");
        deserialized.MutationType.Should().Be(MembershipMutationType.Added);
        deserialized.CorrelationId.Should().Be(CorrelationIdFixture);
        deserialized.SchemaVersion.Should().Be(1);
        deserialized.OccurredOnUtc.Should().Be(occurred);
    }

    [Fact]
    public void Serialize_EnumAsString_NotInteger()
    {
        // Arrange
        var evt = BuildSampleEvent(mutation: MembershipMutationType.Removed);

        // Act
        var json = JsonSerializer.Serialize(evt, MembershipChangedEvent.SerializerOptions);

        // Assert — wire-stability: MutationType MUST be the string "Removed",
        // NOT integer 2. This is the schemaVersion-bump-safety guarantee.
        json.Should().Contain("\"mutationType\":\"Removed\"");
        json.Should().NotContain("\"mutationType\":2");
    }

    [Theory]
    [InlineData(PersonIdentityType.User, "User")]
    [InlineData(PersonIdentityType.Contact, "Contact")]
    [InlineData(PersonIdentityType.Team, "Team")]
    [InlineData(PersonIdentityType.Organization, "Organization")] // Q4 binding
    public void Serialize_PersonIdType_IncludesAllFourTypesAsString(
        PersonIdentityType type,
        string expectedLabel)
    {
        // Arrange
        var evt = BuildSampleEvent(idType: type);

        // Act
        var json = JsonSerializer.Serialize(evt, MembershipChangedEvent.SerializerOptions);

        // Assert — both serialization-as-string AND Q4 coverage that
        // `Organization` is a legal value (per owner clarification 2026-06-20).
        json.Should().Contain($"\"personIdType\":\"{expectedLabel}\"");
    }

    [Fact]
    public void Deserialize_MissingCorrelationId_ThrowsJsonException()
    {
        // Arrange — payload omits the required correlationId field. Per
        // NFR-08 the contract is "non-null, non-empty" and there is no
        // silent-default path. System.Text.Json (.NET 7+) honors the
        // `required` keyword by throwing on deserialize.
        const string json = """
            {
              "personId": "11111111-1111-1111-1111-111111111111",
              "personIdType": "User",
              "entityLogicalName": "sprk_matter",
              "entityRecordId": "22222222-2222-2222-2222-222222222222",
              "sourceField": "sprk_assignedattorney1",
              "role": "assignedAttorney",
              "mutationType": "Added"
            }
            """;

        // Act
        var act = () => JsonSerializer.Deserialize<MembershipChangedEvent>(
            json, MembershipChangedEvent.SerializerOptions);

        // Assert
        act.Should().Throw<JsonException>(
            "NFR-08 requires correlationId on every membership event payload");
    }

    [Fact]
    public void Deserialize_OlderSchemaVersion_DefaultsToOne()
    {
        // Arrange — a payload from a (hypothetical) earlier publisher that
        // omits the schemaVersion field. Forward-compatibility contract:
        // consumers MUST be able to deserialize without exception and the
        // resulting record's SchemaVersion MUST default to 1.
        const string json = """
            {
              "personId": "11111111-1111-1111-1111-111111111111",
              "personIdType": "User",
              "entityLogicalName": "sprk_matter",
              "entityRecordId": "22222222-2222-2222-2222-222222222222",
              "sourceField": "sprk_assignedattorney1",
              "role": "assignedAttorney",
              "mutationType": "Added",
              "correlationId": "corr-abc-123"
            }
            """;

        // Act
        var evt = JsonSerializer.Deserialize<MembershipChangedEvent>(
            json, MembershipChangedEvent.SerializerOptions);

        // Assert
        evt.Should().NotBeNull();
        evt!.SchemaVersion.Should().Be(1, "default value when field is absent");
        evt.OccurredOnUtc.Should().BeNull("optional field is null when absent");
        evt.CorrelationId.Should().Be("corr-abc-123");
    }

    [Fact]
    public void Serialize_OmitsOccurredOnUtc_WhenNull()
    {
        // Arrange — publisher that does not yet capture mutation timestamp.
        // The optional field should not appear in the serialized JSON at
        // all (cleaner wire payload + smaller bytes for high-volume
        // consumers).
        var evt = BuildSampleEvent(occurredOnUtc: null);

        // Act
        var json = JsonSerializer.Serialize(evt, MembershipChangedEvent.SerializerOptions);

        // Assert
        json.Should().NotContain("occurredOnUtc");
    }
}

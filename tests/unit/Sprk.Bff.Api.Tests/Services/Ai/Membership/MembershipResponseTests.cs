// R3 Part 1 — Task 034 (2026-06-21): MembershipResponse DTO contract tests.
// Verifies JSON serialization shape matches design.md Part 1 §
// "Endpoint contract" exactly: camelCase keys, GUID string form, ISO 8601
// DateTimeOffset, byRole as a map of role->id-list, nullable
// continuationToken emitted explicitly as null. Includes a roundtrip test
// proving every field on both MembershipResponse AND the nested
// PersonIdentity survives serialize → deserialize without loss.

using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Membership;

[Trait("status", "new")]
public class MembershipResponseTests
{
    // Test fixture matching the design.md example payload as closely as
    // possible. Using fixed GUIDs / timestamps so JSON-shape assertions are
    // deterministic across runs.
    private static readonly Guid SystemUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ContactId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TeamId1 = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TeamId2 = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid BusinessUnitId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid AccountId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid OrganizationId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid MatterId1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid MatterId2 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid MatterId3 = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTimeOffset CacheExpiresAt =
        new(2026, 06, 20, 15, 34, 00, TimeSpan.Zero);

    private static MembershipResponse BuildSampleResponse() => new(
        EntityType: "sprk_matter",
        PersonIdentity: new PersonIdentity(
            SystemUserId: SystemUserId,
            ContactId: ContactId,
            PrimaryEmail: "user@example.com",
            TeamIds: new[] { TeamId1, TeamId2 },
            BusinessUnitId: BusinessUnitId,
            AccountId: AccountId,
            OrganizationIds: new[] { OrganizationId }),
        Ids: new[] { MatterId1, MatterId2, MatterId3 },
        ByRole: new Dictionary<string, IReadOnlyList<Guid>>
        {
            ["owner"] = new[] { MatterId1 },
            ["owningTeam"] = new[] { MatterId1, MatterId2 },
            ["assignedAttorney"] = new[] { MatterId3 },
            ["assignedLawFirm"] = Array.Empty<Guid>(),
        },
        Count: 3,
        CacheExpiresAt: CacheExpiresAt,
        ContinuationToken: null);

    [Fact]
    public void Serialize_UsesCamelCaseKeys_ForAllTopLevelFields()
    {
        var response = BuildSampleResponse();

        var json = JsonSerializer.Serialize(response);

        // Top-level property names — locked per design.md §"Endpoint contract"
        json.Should().Contain("\"entityType\":");
        json.Should().Contain("\"personIdentity\":");
        json.Should().Contain("\"ids\":");
        json.Should().Contain("\"byRole\":");
        json.Should().Contain("\"count\":");
        json.Should().Contain("\"cacheExpiresAt\":");
        json.Should().Contain("\"continuationToken\":");

        // Negative: PascalCase MUST NOT leak through
        json.Should().NotContain("\"EntityType\"");
        json.Should().NotContain("\"PersonIdentity\"");
        json.Should().NotContain("\"ByRole\"");
        json.Should().NotContain("\"CacheExpiresAt\"");
    }

    [Fact]
    public void Serialize_NestedPersonIdentity_UsesCamelCaseKeys()
    {
        var response = BuildSampleResponse();

        var json = JsonSerializer.Serialize(response);

        json.Should().Contain("\"systemUserId\":");
        json.Should().Contain("\"contactId\":");
        json.Should().Contain("\"primaryEmail\":");
        json.Should().Contain("\"teamIds\":");
        json.Should().Contain("\"businessUnitId\":");
        json.Should().Contain("\"accountId\":");
        json.Should().Contain("\"organizationIds\":");
    }

    [Fact]
    public void Serialize_Guid_UsesStringDForm_NoBraces()
    {
        var response = BuildSampleResponse();

        var json = JsonSerializer.Serialize(response);

        // GUIDs MUST serialize as 32-hex-with-hyphens strings (the System.Text.Json default
        // "D" format). Verified against MatterId1.
        json.Should().Contain("\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"");
        json.Should().NotContain("{aaaaaaaa-"); // No braced GUID form
    }

    [Fact]
    public void Serialize_DateTimeOffset_UsesIso8601WithTimezone()
    {
        var response = BuildSampleResponse();

        var json = JsonSerializer.Serialize(response);

        // System.Text.Json default DateTimeOffset format is ISO 8601 round-trip ("O").
        // For UTC, the suffix is "+00:00" (NOT "Z" — System.Text.Json preserves the offset).
        // Either suffix is spec-compliant ISO 8601, but we lock the runtime behavior here.
        json.Should().Contain("\"cacheExpiresAt\":\"2026-06-20T15:34:00+00:00\"");
    }

    [Fact]
    public void Serialize_ContinuationToken_EmitsExplicitNull()
    {
        var response = BuildSampleResponse();

        var json = JsonSerializer.Serialize(response);

        // Design example shows "continuationToken": null explicitly.
        // We MUST NOT apply JsonIgnore(WhenWritingNull) to this field.
        json.Should().Contain("\"continuationToken\":null");
    }

    [Fact]
    public void Serialize_ByRole_PreservesEmptyArrays()
    {
        var response = BuildSampleResponse();

        var json = JsonSerializer.Serialize(response);

        // assignedLawFirm has zero matches — must serialize as [] not be omitted
        json.Should().Contain("\"assignedLawFirm\":[]");
    }

    [Fact]
    public void Serialize_Roundtrip_PreservesAllTopLevelFields()
    {
        var original = BuildSampleResponse();

        var json = JsonSerializer.Serialize(original);
        var roundtripped = JsonSerializer.Deserialize<MembershipResponse>(json);

        roundtripped.Should().NotBeNull();
        roundtripped!.EntityType.Should().Be("sprk_matter");
        roundtripped.Count.Should().Be(3);
        roundtripped.CacheExpiresAt.Should().Be(CacheExpiresAt);
        roundtripped.ContinuationToken.Should().BeNull();
        roundtripped.Ids.Should().BeEquivalentTo(new[] { MatterId1, MatterId2, MatterId3 });
        roundtripped.ByRole.Should().HaveCount(4);
        roundtripped.ByRole["owner"].Should().BeEquivalentTo(new[] { MatterId1 });
        roundtripped.ByRole["owningTeam"].Should().BeEquivalentTo(new[] { MatterId1, MatterId2 });
        roundtripped.ByRole["assignedAttorney"].Should().BeEquivalentTo(new[] { MatterId3 });
        roundtripped.ByRole["assignedLawFirm"].Should().BeEmpty();
    }

    [Fact]
    public void Serialize_Roundtrip_PreservesNestedPersonIdentity()
    {
        var original = BuildSampleResponse();

        var json = JsonSerializer.Serialize(original);
        var roundtripped = JsonSerializer.Deserialize<MembershipResponse>(json);

        roundtripped.Should().NotBeNull();
        var identity = roundtripped!.PersonIdentity;
        identity.SystemUserId.Should().Be(SystemUserId);
        identity.ContactId.Should().Be(ContactId);
        identity.PrimaryEmail.Should().Be("user@example.com");
        identity.TeamIds.Should().BeEquivalentTo(new[] { TeamId1, TeamId2 });
        identity.BusinessUnitId.Should().Be(BusinessUnitId);
        identity.AccountId.Should().Be(AccountId);
        identity.OrganizationIds.Should().BeEquivalentTo(new[] { OrganizationId });
    }

    [Fact]
    public void Serialize_ContinuationTokenPresent_IsString()
    {
        // Pagination case: when there's a continuation token, it serializes as a string value.
        var response = BuildSampleResponse() with { ContinuationToken = "next-page-cursor-xyz" };

        var json = JsonSerializer.Serialize(response);

        json.Should().Contain("\"continuationToken\":\"next-page-cursor-xyz\"");
    }

    [Fact]
    public void Deserialize_RoundtripWithContinuationToken_PreservesToken()
    {
        var original = BuildSampleResponse() with { ContinuationToken = "page-2-cursor" };

        var json = JsonSerializer.Serialize(original);
        var roundtripped = JsonSerializer.Deserialize<MembershipResponse>(json);

        roundtripped!.ContinuationToken.Should().Be("page-2-cursor");
    }
}

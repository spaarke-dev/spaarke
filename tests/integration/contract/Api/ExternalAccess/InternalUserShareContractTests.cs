// Task 063 — the wire contract of the internal system-user share routes (spec FR-29), consumed by the Manage Access
// "+ User" picker (task 065). KEEP path: endpoint-contract.
//
// What is asserted here is what a client depends on: the routes, the status codes, the JSON property names and the
// reason codes, as LITERALS. The behaviour behind them — a level change replaces the rights, a failed read is a
// refusal, every write is confirmed, the user's cached root set is cleared — is owned by
// tests/integration/auth/UnifiedAccessControl/InternalUserShareTests; the delegation gate by
// DelegationRuleCharacterizationTests. This fixture's caller holds Write (EntitledCallerRecordAccessProbe).

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

public sealed class InternalUserShareContractTests : IClassFixture<ExternalAccessContractFixture>
{
    private const string ShareUserPath = "/api/v1/external-access/share-user";
    private const string UnshareUserPath = "/api/v1/external-access/unshare-user";
    private const string UserSharesPath = "/api/v1/external-access/user-shares";

    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("abababab-abab-abab-abab-abababababab");
    private static readonly Guid OtherUserId = Guid.Parse("cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd");

    private readonly ExternalAccessContractFixture _fixture;

    public InternalUserShareContractTests(ExternalAccessContractFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public async Task PostShareUser_ForAnInternalPerson_Returns200WithTheLevelMaskAndOutcome()
    {
        _fixture.Dataverse.ContactQueryResult = SystemUserRow(UserId, isExternal: false);
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync(ShareUserPath, new
        {
            recordType = "project",
            recordId = ProjectId,
            systemUserId = UserId,
            accessLevel = 100000001
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = document.RootElement;
        body.GetProperty("systemUserId").GetGuid().Should().Be(UserId);
        body.GetProperty("accessLevel").GetInt32().Should().Be(100000001, "the level is the same number a contact grant carries");
        body.GetProperty("accessRightsMask").GetInt32().Should().Be(23, "Read 1 + Write 2 + Append 4 + AppendTo 16");
        body.GetProperty("outcome").GetString().Should().Be("created");
        body.GetProperty("narrowed").GetBoolean().Should().BeFalse(
            "the fixture's caller holds a full working set, so their own rights did not narrow the grant");
    }

    [Fact]
    public async Task PostShareUser_WithALevelOutsideTheThree_Returns400WithTheReasonCode()
    {
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync(ShareUserPath, new
        {
            recordType = "project",
            recordId = ProjectId,
            systemUserId = UserId,
            accessLevel = 7777
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReasonCode(response)).Should().Be("sdap.access.user_share.level_invalid");
    }

    [Fact]
    public async Task PostShareUser_ForAnExternalUser_Returns422WithTheReasonCode()
    {
        _fixture.Dataverse.ContactQueryResult = SystemUserRow(UserId, isExternal: true);
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync(ShareUserPath, new
        {
            recordType = "project",
            recordId = ProjectId,
            systemUserId = UserId,
            accessLevel = 100000000
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReasonCode(response)).Should().Be("sdap.access.user_share.user_not_internal");
    }

    [Fact]
    public async Task PostUnshareUser_ForAUserWithNoShare_Returns200RemovedFalse()
    {
        _fixture.Dataverse.ContactQueryResult = SystemUserRow(UserId, isExternal: false);
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync(UnshareUserPath, new
        {
            recordType = "project",
            recordId = ProjectId,
            systemUserId = UserId
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("systemUserId").GetGuid().Should().Be(UserId);
        document.RootElement.GetProperty("removed").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task PostUnshareUser_WithoutAUser_Returns400WithTheReasonCode()
    {
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync(UnshareUserPath, new { recordType = "project", recordId = ProjectId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReasonCode(response)).Should().Be("sdap.access.user_share.user_required");
    }

    [Fact]
    public async Task GetUserShares_Returns200WithEachUsersNameMaskAndLevel()
    {
        var modifiedOn = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
        _fixture.RecordShares.Seed("sprk_project", ProjectId, DataversePrincipalRef.User(UserId), 23, modifiedOn);
        _fixture.RecordShares.Seed("sprk_project", ProjectId, DataversePrincipalRef.User(OtherUserId), 262167, modifiedOn);
        _fixture.Dataverse.ContactQueryResult =
            $$"""[{"systemuserid":"{{UserId}}","fullname":"Ada Lovelace"},{"systemuserid":"{{OtherUserId}}","fullname":"Brook Okafor"}]""";
        using var client = _fixture.CreateAdminClient();

        var response = await client.GetAsync($"{UserSharesPath}?recordType=project&recordId={ProjectId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var shares = document.RootElement.GetProperty("shares").EnumerateArray().ToList();
        shares.Should().HaveCount(2);

        shares[0].GetProperty("systemUserId").GetGuid().Should().Be(UserId);
        shares[0].GetProperty("fullName").GetString().Should().Be("Ada Lovelace");
        shares[0].GetProperty("accessRightsMask").GetInt32().Should().Be(23);
        shares[0].GetProperty("accessLevel").GetInt32().Should().Be(100000001);
        shares[0].GetProperty("modifiedOn").GetDateTimeOffset().Should().Be(modifiedOn);

        shares[1].GetProperty("systemUserId").GetGuid().Should().Be(OtherUserId);
        shares[1].GetProperty("accessRightsMask").GetInt32().Should().Be(262167);
        shares[1].GetProperty("accessLevel").ValueKind.Should().Be(JsonValueKind.Null,
            "Collaborate plus Share is no level — the picker shows the rights rather than naming a level");
    }

    private static string SystemUserRow(Guid id, bool isExternal) =>
        $$"""[{"systemuserid":"{{id}}","fullname":"Ada Lovelace","isdisabled":false,"accessmode":0,"applicationid":null,"sprk_isexternal":{{(isExternal ? "true" : "false")}}}]""";

    private static async Task<string?> ReasonCode(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("reasonCode", out var code) ? code.GetString() : null;
    }
}

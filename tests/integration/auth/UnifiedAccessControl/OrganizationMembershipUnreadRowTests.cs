using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// ebilling-r1 task 130-0 fix round 3e: a <c>sprk_contactorganization</c> row that does not carry the lookup column a read selected
/// is UNRESOLVED (<see cref="OrganizationMemberSet.UnreadableRows"/>), not "no member" and not "no organization". An explicit JSON
/// null names nobody and is not counted. The selects are pinned: the column each reader depends on is in its <c>$select</c>.
/// The callers' handling (counted into the sweep's own undecidable counter) is asserted where each caller is tested:
/// <c>PersonOrganizationGrantSpeCleanupTests</c>, <c>ProjectClosureCascadeTests</c>, <c>SpeRevokeMatcherTests</c> and
/// <c>RevokeGrantOverlapAndOwnContainerTests</c>. KEEP path: <c>tests/integration/auth/**</c>.
/// </summary>
public class OrganizationMembershipUnreadRowTests
{
    private static readonly Guid Contact = Guid.Parse("11111111-3e00-4000-8000-00000000000a");
    private static readonly Guid Organization = Guid.Parse("33333333-3e00-4000-8000-000000000001");

    private static ExternalOrganizationMembership.ContactOrganizationRow Row(string json)
        => JsonSerializer.Deserialize<ExternalOrganizationMembership.ContactOrganizationRow>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static Mock<DataverseWebApiClient> Dataverse(string?[] captureSelect, params string[] rowsJson)
    {
        var mock = new Mock<DataverseWebApiClient>(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Graph:ManagedIdentity:Enabled"] = "true",
                ["API_APP_ID"] = "00000000-0000-0000-0000-0000000001ab",
                ["API_CLIENT_SECRET"] = "test-secret",
                ["TENANT_ID"] = "00000000-0000-0000-0000-0000000001aa",
            }).Build(),
            NullLogger<DataverseWebApiClient>.Instance, null!, null!);

        mock.Setup(c => c.QueryAsync<ExternalOrganizationMembership.ContactOrganizationRow>(
                ExternalOrganizationMembership.EntitySet, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string? _, string? select, int? _, int? _, CancellationToken _) =>
            {
                captureSelect[0] = select;
                return rowsJson.Select(Row).ToList();
            });
        return mock;
    }

    private static string ContactJson(Guid? id) => id is null ? "{\"_sprk_contact_value\":null}" : $"{{\"_sprk_contact_value\":\"{id}\"}}";

    private static string OrganizationJson(Guid? id)
        => id is null ? "{\"_sprk_organization_value\":null}" : $"{{\"_sprk_organization_value\":\"{id}\"}}";

    // ── Members of an organization (the contact column) ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Members_ARowWithoutTheContactColumn_IsUnreadable_NotAMemberAndNotDropped()
    {
        var select = new string?[1];
        var dataverse = Dataverse(select, ContactJson(Contact), "{}");

        var members = await ExternalOrganizationMembership.QueryActiveMembersAsync(dataverse.Object, Organization, CancellationToken.None);

        members.UnreadableRows.Should().Be(1);
        members.ContactIds.Should().Equal(Contact);
        members.ExceededBound.Should().BeFalse();
    }

    [Fact]
    public async Task Members_AnExplicitNullContact_NamesNobody_AndIsNotUnreadable()
    {
        var select = new string?[1];
        var dataverse = Dataverse(select, ContactJson(Contact), ContactJson(null));

        var members = await ExternalOrganizationMembership.QueryActiveMembersAsync(dataverse.Object, Organization, CancellationToken.None);

        members.UnreadableRows.Should().Be(0);
        members.ContactIds.Should().Equal(Contact);
    }

    [Fact]
    public async Task Members_EveryRowComplete_HasNoUnreadableRows()
    {
        var other = Guid.NewGuid();
        var dataverse = Dataverse(new string?[1], ContactJson(Contact), ContactJson(other));

        var members = await ExternalOrganizationMembership.QueryActiveMembersAsync(dataverse.Object, Organization, CancellationToken.None);

        members.UnreadableRows.Should().Be(0);
        members.ContactIds.Should().BeEquivalentTo(new[] { Contact, other });
    }

    [Fact]
    public async Task Members_TheSelect_CarriesTheContactColumn()
    {
        var select = new string?[1];
        var dataverse = Dataverse(select);

        await ExternalOrganizationMembership.QueryActiveMembersAsync(dataverse.Object, Organization, CancellationToken.None);

        select[0]!.Split(',').Should().Contain("_sprk_contact_value");
        ExternalOrganizationMembership.MemberSelect.Split(',').Should().Contain("_sprk_contact_value");
    }

    // ── Organizations of a contact (the organization column) ──────────────────────────────────────────────────────

    [Fact]
    public async Task Organizations_ARowWithoutTheOrganizationColumn_IsUnreadable_NotNoOrganization()
    {
        var dataverse = Dataverse(new string?[1], OrganizationJson(Organization), "{}");

        var organizations = await ExternalOrganizationMembership.QueryActiveOrganizationsOfContactAsync(
            dataverse.Object, Contact, CancellationToken.None);

        organizations.UnreadableRows.Should().Be(1);
        organizations.ContactIds.Should().Equal(Organization);
    }

    [Fact]
    public async Task Organizations_AnExplicitNullOrganization_NamesNoOrganization_AndIsNotUnreadable()
    {
        var dataverse = Dataverse(new string?[1], OrganizationJson(Organization), OrganizationJson(null));

        var organizations = await ExternalOrganizationMembership.QueryActiveOrganizationsOfContactAsync(
            dataverse.Object, Contact, CancellationToken.None);

        organizations.UnreadableRows.Should().Be(0);
        organizations.ContactIds.Should().Equal(Organization);
    }

    [Fact]
    public async Task Organizations_TheSelect_CarriesTheOrganizationColumn()
    {
        var select = new string?[1];
        var dataverse = Dataverse(select);

        await ExternalOrganizationMembership.QueryActiveOrganizationsOfContactAsync(dataverse.Object, Contact, CancellationToken.None);

        select[0]!.Split(',').Should().Contain("_sprk_organization_value");
        ExternalOrganizationMembership.OrganizationSelect.Split(',').Should().Contain("_sprk_organization_value");
    }

    [Fact]
    public void TheRow_RecordsWhetherEachColumnWasRead()
    {
        var absent = Row("{}");
        var nulls = Row("{\"_sprk_contact_value\":null,\"_sprk_organization_value\":null}");

        (absent.ContactRead, absent.OrganizationRead).Should().Be((false, false));
        (nulls.ContactRead, nulls.OrganizationRead).Should().Be((true, true));
    }
}

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Fix round 3g, item 1: <see cref="ExternalGrantRow"/> records which lookups the read returned. A lookup the row did not carry
/// (the Web API omitted the column) is not the same as an explicit null ("no record named"). These tests pin the row itself, the
/// two selects that must return every lookup, and the two shared readers (<see cref="ExternalGrantLifecycle.DeriveKey"/> and
/// <see cref="ExternalGrantLifecycle.ReadContactHeldGrantsAsync"/>). Each consumer's answer is pinned in its own harness:
/// RevokeGrantOverlapAndOwnContainerTests, PersonOrganizationGrantSpeCleanupTests, PersonAccessAdministrationTests,
/// RemovalSweepTests, ContactGrantAuthorizationTests, RecordShareExpiryTests and RecordGrantsEndpointTests.
/// </summary>
public class ExternalGrantRowPresenceTests
{
    private static readonly Guid RowId = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000f001");
    private static readonly Guid Project = Guid.Parse("22222222-0000-0000-0000-00000000f001");
    private static readonly Guid Contact = Guid.Parse("11111111-0000-0000-0000-00000000f001");

    private static readonly string[] KeyColumns =
    {
        "_sprk_project_value", "_sprk_matter_value", "_sprk_workassignment_value", "_sprk_contact_value", "_sprk_organization_value",
    };

    private static readonly string[] IssuerColumns = { "_sprk_grantedby_value", "_sprk_grantedbycontact_value" };

    private static string Json(string? omit, IDictionary<string, string>? overrides = null)
    {
        var columns = new Dictionary<string, string>
        {
            ["_sprk_contact_value"] = $"\"{Contact}\"",
            ["_sprk_organization_value"] = "null",
            ["_sprk_project_value"] = $"\"{Project}\"",
            ["_sprk_matter_value"] = "null",
            ["_sprk_workassignment_value"] = "null",
            ["_sprk_grantedby_value"] = "null",
            ["_sprk_grantedbycontact_value"] = "null",
        };
        if (overrides is not null)
            foreach (var kv in overrides) columns[kv.Key] = kv.Value;
        if (omit is not null) columns.Remove(omit);
        return "{\"sprk_externalrecordaccessid\":\"" + RowId + "\",\"statecode\":0,\"sprk_accesslevel\":" + (int)ExternalAccessLevel.ViewOnly + "," +
               string.Join(",", columns.Select(kv => $"\"{kv.Key}\":{kv.Value}")) + "}";
    }

    internal static ExternalGrantRow Read(string json) => JsonSerializer.Deserialize<ExternalGrantRow>(json)!;

    // ── The row ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ARowWithEveryLookupReturned_AsValuesOrExplicitNulls_IsRead()
    {
        var row = Read(Json(omit: null));

        row.KeyLookupsRead.Should().BeTrue();
        row.IssuerRead.Should().BeTrue();
        row.AllLookupsRead.Should().BeTrue();
        row.ContactRead.Should().BeTrue();
        row.OrganizationRead.Should().BeTrue();
        row.ContactId.Should().Be(Contact);
        row.OrganizationId.Should().BeNull("an explicit null is 'none filed', not 'not read'");
    }

    [Theory]
    [InlineData("_sprk_project_value")]
    [InlineData("_sprk_matter_value")]
    [InlineData("_sprk_workassignment_value")]
    [InlineData("_sprk_contact_value")]
    [InlineData("_sprk_organization_value")]
    public void ARowMissingAKeyColumn_IsNotKeyRead_AndHasNoDerivableKey(string omitted)
    {
        var row = Read(Json(omitted));

        row.KeyLookupsRead.Should().BeFalse();
        row.AllLookupsRead.Should().BeFalse();
        ExternalGrantLifecycle.DeriveKey(row).Should().BeNull("a row that did not carry a root, contact or organization column has no key");
    }

    [Theory]
    [InlineData("_sprk_grantedby_value")]
    [InlineData("_sprk_grantedbycontact_value")]
    public void ARowMissingAnIssuerColumn_IsNotIssuerRead_ButStillHasItsKey(string omitted)
    {
        var row = Read(Json(omitted));

        row.IssuerRead.Should().Be(omitted != "_sprk_grantedbycontact_value",
            "the issuer check reads the contact-issuer lookup; the system-user lookup is read on its own");
        row.AllLookupsRead.Should().BeFalse();
        row.KeyLookupsRead.Should().BeTrue();
        ExternalGrantLifecycle.DeriveKey(row).Should().NotBeNull();
    }

    [Fact]
    public void ARowWithExplicitNullsForEveryOtherLookup_HasItsKey_ExactlyAsBefore()
    {
        var key = ExternalGrantLifecycle.DeriveKey(Read(Json(omit: null)));

        key.Should().NotBeNull();
        key!.Value.RootId.Should().Be(Project);
        key.Value.ContactId.Should().Be(Contact);
        key.Value.IsOrganizationGrant.Should().BeFalse();
    }

    [Fact]
    public void ARowBuiltInCode_RecordsAssignedLookupsAsRead_EvenWhenNull()
    {
        var row = new ExternalGrantRow { ContactId = null };

        row.ContactRead.Should().BeTrue("assigning the property is a read, as the typed rows of the other classes do");
        row.OrganizationRead.Should().BeFalse("a lookup never assigned is not read");
        row.KeyLookupsRead.Should().BeFalse();
    }

    // ── The selects ───────────────────────────────────────────────────────────────────────────────────────────────

    private static IEnumerable<string> LookupColumns() => typeof(ExternalGrantRow)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
        .Where(n => n is not null && Regex.IsMatch(n, "^_sprk_[a-z]+_value$"))
        .Select(n => n!)
        .Distinct();

    [Fact]
    public void TheRowsLookupColumns_AreTheSevenTheSelectsMustReturn()
    {
        LookupColumns().Should().BeEquivalentTo(KeyColumns.Concat(IssuerColumns),
            "a new lookup on the row is a new column to select and to track, so this list moves only by review");
    }

    [Fact]
    public void RowSelect_AndListingSelect_ReturnEveryLookupColumnOfTheRow()
    {
        foreach (var column in LookupColumns())
        {
            ExternalGrantLifecycle.RowSelect.Split(',').Should().Contain(column, "the row-level select must return it");
            ExternalGrantLifecycle.ListingSelect.Split(',').Should().Contain(column, "the listing select must return it");
        }
    }

    // ── ReadContactHeldGrantsAsync ────────────────────────────────────────────────────────────────────────────────

    private static Mock<DataverseWebApiClient> Client(params ExternalGrantRow[] rows)
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
        mock.Setup(c => c.QueryAsync<ExternalGrantRow>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows.ToList());
        return mock;
    }

    private static Task<ContactHeldGrants> Held(DataverseWebApiClient client)
        => ExternalGrantLifecycle.ReadContactHeldGrantsAsync(
            Contact, Array.Empty<Guid>(), ExternalGrantRootType.Project, Project, ExternalAccessLevel.ViewOnly,
            _ => true, client,
            new GrantPolicyTestDoubles.FlagStubParticipationService(RootRecordFlags.None), CancellationToken.None);

    [Theory]
    [InlineData("_sprk_contact_value")]
    [InlineData("_sprk_organization_value")]
    public async Task ReadContactHeldGrants_WhenARowDidNotCarryItsContactOrOrganizationColumn_Faults(string omitted)
    {
        var client = Client(Read(Json(omitted)));

        var act = () => Held(client.Object);

        await act.Should().ThrowAsync<InvalidOperationException>("the row cannot be judged, so the read could not be completed");
    }

    [Fact]
    public async Task ReadContactHeldGrants_WhenEveryColumnIsReturned_CountsTheRow()
    {
        var client = Client(Read(Json(omit: null)));

        var held = await Held(client.Object);

        held.Rows.Should().ContainSingle().Which.Id.Should().Be(RowId);
    }
}

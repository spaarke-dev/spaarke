// -----------------------------------------------------------------------------
// H7bContactLocksPackagedTests.cs
//
// #2317. H7b S19 verifies the locks on contact.sprk_standinggrant and contact.sprk_accesspermissiongrant, and S22 the
// locks on the BFF-only columns (sprk_externalinvitedemailhash, sprk_externalinvitedtenantid) and on
// sprk_externalaccessremovedon. A new stamp receives those locks only through the SpaarkeMaster import (H6), so the
// committed package must carry exactly the locks H7b requires. This structural test reads the committed export
// (Entity.xml IsSecured, FieldSecurityProfiles.xml rights per profile) and:
//   - pins each column secured and the exact read/create/update of every profile that has a row on it;
//   - runs the real procedure against an environment whose rows for these columns are the packaged ones, and requires
//     it to pass — so a package and an H7b that disagree fail here, not in a customer's provisioning run;
//   - requires the package version to be at least the minimum release H7b names in its S18/S19 refusals.
//
// Controls (tests/CLAUDE.md): the rights scan runs on seeded XML with and without a row; the agreement run is shown
// to refuse a package that deviates from the lock.
// -----------------------------------------------------------------------------

using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;
using Sprk.Provisioning.ControlPlane.Tests.Handlers.SecureRecordSetup;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H7bContactLocksPackagedTests
{
    private const string Contact = "contact";
    private const string StandingAdmins = SecureRecordSetupProcedure.StandingGrantProfileName;
    private const string Readers = SecureRecordSetupProcedure.ReaderProfileName;
    private const string Writers = SecureRecordSetupProcedure.WriterProfileName;

    /// <summary>The S19/S22 contact columns and the exact rights each profile holds on them, as H7b requires.</summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, (int Read, int Create, int Update)>> Required =
        new Dictionary<string, IReadOnlyDictionary<string, (int, int, int)>>
        {
            [SecureRecordSetupProcedure.StandingGrantColumn] = new Dictionary<string, (int, int, int)>
            {
                [StandingAdmins] = (4, 4, 4),
            },
            [SecureRecordSetupProcedure.AccessLevelColumn] = new Dictionary<string, (int, int, int)>
            {
                [StandingAdmins] = (4, 4, 4),
                [Readers] = (4, 0, 0),
                [Writers] = (4, 0, 4),
            },
            [SecureRecordSetupProcedure.InvitedEmailHashColumn] = new Dictionary<string, (int, int, int)>
            {
                [Writers] = (4, 4, 4),
            },
            [SecureRecordSetupProcedure.InvitedTenantIdColumn] = new Dictionary<string, (int, int, int)>
            {
                [Writers] = (4, 4, 4),
            },
            [SecureRecordSetupProcedure.AccessRemovedOnColumn] = new Dictionary<string, (int, int, int)>
            {
                [Readers] = (4, 0, 0),
                [Writers] = (4, 4, 4),
            },
        };

    [Fact]
    public void ThePackageSecuresEveryS19AndS22Column()
    {
        var contact = XDocument.Load(PackagePath("Entities", "Contact", "Entity.xml"));

        Required.Keys.Should().BeEquivalentTo(
            new[] { SecureRecordSetupProcedure.StandingGrantColumn, SecureRecordSetupProcedure.AccessLevelColumn,
                    SecureRecordSetupProcedure.AccessRemovedOnColumn }.Concat(SecureRecordSetupProcedure.BffOnlyColumns),
            "this test covers every contact column whose lock S19 and S22 verify");
        foreach (var column in Required.Keys)
        {
            IsSecured(contact, column).Should().BeTrue($"H7b refuses an environment where contact.{column} is not secured");
        }
    }

    [Fact]
    public void ThePackageGrantsExactlyTheRightsH7bRequires_PerProfile()
    {
        var profiles = XDocument.Load(PackagePath("Other", "FieldSecurityProfiles.xml"));

        foreach (var (column, rights) in Required)
        {
            Rights(profiles, Contact, column).Should().BeEquivalentTo(rights,
                $"the package's profiles on contact.{column} are exactly those H7b's lock check requires, with those rights");
        }
    }

    [Fact]
    public async Task H7bAcceptsThePackagedLocks()
    {
        var dv = EnvironmentWithPackagedLocks(_ => { });

        var applied = (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Applied>(
            "an environment carrying exactly the packaged locks passes S19 and S22").Subject;

        foreach (var column in SecureRecordSetupProcedure.BffOnlyColumns.Append(SecureRecordSetupProcedure.AccessRemovedOnColumn))
        {
            applied.State.Notes.Should().ContainSingle(n => n.Contains(column, StringComparison.Ordinal)
                                                           && n.Contains("present, secured", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ThePackageVersion_IsAtLeastTheMinimumH7bNames()
    {
        var solution = XDocument.Load(PackagePath("Other", "Solution.xml"));
        var version = Version.Parse(solution.Descendants("Version").First().Value);

        version.Should().BeGreaterThanOrEqualTo(Version.Parse(SecureRecordSetupProcedure.ContactLocksMinimumSpaarkeMasterVersion),
            "H7b's S18/S19 refusals send an operator to this release for the contact locks; the package must be it or later");
    }

    // ---------- controls ----------

    [Fact]
    public void RightsScan_FindsWhatIsThere_AndNotWhatIsNot()
    {
        var profiles = XDocument.Parse("""
            <FieldSecurityProfiles>
              <FieldSecurityProfile name="P"><FieldPermissions>
                <FieldPermission><EntityName>contact</EntityName><AttributeName>sprk_a</AttributeName>
                  <CanRead>4</CanRead><CanUpdate>4</CanUpdate><CanCreate>0</CanCreate></FieldPermission>
                <FieldPermission><EntityName>account</EntityName><AttributeName>sprk_a</AttributeName>
                  <CanRead>4</CanRead><CanUpdate>4</CanUpdate><CanCreate>4</CanCreate></FieldPermission>
              </FieldPermissions></FieldSecurityProfile>
              <FieldSecurityProfile name="Q"><FieldPermissions /></FieldSecurityProfile>
            </FieldSecurityProfiles>
            """);

        Rights(profiles, "contact", "sprk_a").Should().BeEquivalentTo(new Dictionary<string, (int, int, int)> { ["P"] = (4, 0, 4) });
        Rights(profiles, "contact", "sprk_b").Should().BeEmpty();
    }

    public static TheoryData<string, Action<FakeSecureRecordSetupDataverse>, string> DeviatingPackages() => new()
    {
        { "Standing Grant Administrators lose Create on the standing grant",
            dv => Set(dv, SecureRecordSetupProcedure.StandingGrantColumn, dv.StandingGrantProfileId, 4, 0, 4),
            SecureRecordSetupRejectionCodes.StandingGrantLockIncomplete },
        { "Readers gain Update on the standing grant",
            dv => Set(dv, SecureRecordSetupProcedure.StandingGrantColumn, dv.ReaderProfileId, 4, 0, 4),
            SecureRecordSetupRejectionCodes.StandingGrantLockOtherWriter },
        { "Readers gain Update on the access level",
            dv => Set(dv, SecureRecordSetupProcedure.AccessLevelColumn, dv.ReaderProfileId, 4, 0, 4),
            SecureRecordSetupRejectionCodes.AccessLevelLockOtherWriter },
        { "Readers gain Read on the invited email hash",
            dv => Set(dv, SecureRecordSetupProcedure.InvitedEmailHashColumn, dv.ReaderProfileId, 4, 0, 0),
            SecureRecordSetupRejectionCodes.BffOnlyColumnLockOtherAccess },
        { "Readers gain Update on the removed-on marker",
            dv => Set(dv, SecureRecordSetupProcedure.AccessRemovedOnColumn, dv.ReaderProfileId, 4, 0, 4),
            SecureRecordSetupRejectionCodes.AccessRemovedOnLockOtherWriter },
        { "the access level not secured",
            dv => dv.SecuredColumns[FakeSecureRecordSetupDataverse.ColumnKey(Contact, SecureRecordSetupProcedure.AccessLevelColumn)] = false,
            SecureRecordSetupRejectionCodes.AccessLevelLockIncomplete },
    };

    [Theory]
    [MemberData(nameof(DeviatingPackages))]
    public async Task H7bRefusesAPackageThatDeviatesFromTheLock(string because, Action<FakeSecureRecordSetupDataverse> deviate, string code)
    {
        var dv = EnvironmentWithPackagedLocks(deviate);

        var refused = (await RunAsync(dv)).Should().BeOfType<SecureRecordSetupOutcome.Refused>(because).Subject;

        refused.RejectionCode.Should().Be(code, because);
    }

    // ---------- the agreement environment ----------

    private static readonly SecureRecordOwnerRoleSet RoleSet = SecureRecordOwnerRoleSet.Embedded;

    private static Task<SecureRecordSetupOutcome> RunAsync(FakeSecureRecordSetupDataverse dv)
        => new SecureRecordSetupProcedure(dv, NullLogger.Instance)
            .RunAsync(new SecureRecordSetupRequest(new SecureRecordSetupTarget("https://spaarke-acme.crm.dynamics.com/", "tenant", "bff-app"),
                RoleSet, [dv.BffAppUser, dv.MiAppUser], dv.CustomerUnitId, DryRun: false), CancellationToken.None);

    /// <summary>
    /// A fake environment whose S19/S22 contact columns carry exactly the packaged IsSecured flags and profile rows (plus
    /// the platform's System Administrator profile at 4/4/4, which no package carries), then <paramref name="deviate"/>.
    /// </summary>
    private static FakeSecureRecordSetupDataverse EnvironmentWithPackagedLocks(Action<FakeSecureRecordSetupDataverse> deviate)
    {
        var contact = XDocument.Load(PackagePath("Entities", "Contact", "Entity.xml"));
        var profiles = XDocument.Load(PackagePath("Other", "FieldSecurityProfiles.xml"));
        var dv = FakeSecureRecordSetupDataverse.NewEnvironment(RoleSet);

        foreach (var column in Required.Keys)
        {
            dv.SecuredColumns[FakeSecureRecordSetupDataverse.ColumnKey(Contact, column)] = IsSecured(contact, column);
            var permissions = dv.ColumnPermissions(column);
            permissions.RemoveAll(p => string.Equals(p.EntityName, Contact, StringComparison.OrdinalIgnoreCase));
            foreach (var (profile, (read, create, update)) in Rights(profiles, Contact, column))
            {
                permissions.Add(new SecureSetupFieldPermission(dv.Profiles[profile].Single(), Contact, read, create, update));
            }
            permissions.Add(new SecureSetupFieldPermission(dv.SystemAdministratorProfileId, Contact, 4, 4, 4));
        }

        deviate(dv);
        return dv;
    }

    /// <summary>Sets <paramref name="profileId"/>'s row on contact.<paramref name="column"/>, adding it when there is none.</summary>
    private static void Set(FakeSecureRecordSetupDataverse dv, string column, Guid profileId, int read, int create, int update)
    {
        var permissions = dv.ColumnPermissions(column);
        permissions.RemoveAll(p => p.ProfileId == profileId && string.Equals(p.EntityName, Contact, StringComparison.OrdinalIgnoreCase));
        permissions.Add(new SecureSetupFieldPermission(profileId, Contact, read, create, update));
    }

    // ---------- scans ----------

    /// <summary>The attribute's IsSecured flag, or null when the package does not carry the attribute.</summary>
    private static bool? IsSecured(XDocument entity, string logicalName)
    {
        var attribute = entity.Descendants("attribute")
            .FirstOrDefault(a => string.Equals((string?)a.Element("LogicalName"), logicalName, StringComparison.Ordinal));
        return attribute is null ? null : (string?)attribute.Element("IsSecured") == "1";
    }

    /// <summary>Every profile with a row on <paramref name="entity"/>.<paramref name="attribute"/>, and its read/create/update.</summary>
    private static Dictionary<string, (int Read, int Create, int Update)> Rights(XDocument profiles, string entity, string attribute)
        => profiles.Descendants("FieldSecurityProfile")
            .SelectMany(p => p.Descendants("FieldPermission").Select(f => (Profile: (string)p.Attribute("name")!, Row: f)))
            .Where(x => string.Equals((string?)x.Row.Element("EntityName"), entity, StringComparison.OrdinalIgnoreCase)
                        && string.Equals((string?)x.Row.Element("AttributeName"), attribute, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(x => x.Profile, x => (
                (int?)x.Row.Element("CanRead") ?? 0,
                (int?)x.Row.Element("CanCreate") ?? 0,
                (int?)x.Row.Element("CanUpdate") ?? 0));

    private static string PackagePath(params string[] parts)
        => Path.Combine([RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", .. parts]);

    private static string RepoRoot()
    {
        // A worktree's .git is a FILE; a regular checkout's is a directory (parity with ContactIdentityBindingSchemaPackagedTests).
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException($"No repository root above {AppContext.BaseDirectory}.");
    }
}

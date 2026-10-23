using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;

/// <summary>
/// The contact's access-level and identity bookkeeping columns are field-secured; only the BFF and Standing Grant
/// Administrators write them. Three sources must say the same thing, and this test ties them together:
/// <list type="number">
///   <item>the schema script <c>scripts/Set-ExternalAccessContactSecuritySchema.ps1</c> — its <c>$Grants</c> (every
///   profile x column pair, which is also its column list) and <c>$NewColumns</c>;</item>
///   <item>the SpaarkeMaster package's <c>Entities/Contact/Entity.xml</c> — <c>IsSecured</c> and the new columns' shape;</item>
///   <item>the package's <c>Other/FieldSecurityProfiles.xml</c> — exactly the script's grants, and no other profile with
///   access to these columns.</item>
/// </list>
/// The expected lock is written here once, from the BFF's own column constants.
/// </summary>
/// <remarks>
/// Read as text and XML (no PowerShell host, no network). The parsers are exercised on seeded snippets, so a regression in
/// the guard reddens too. Mirrors <see cref="ExternalInviteMarkerSchemaAgreementTests"/> and
/// <see cref="ExternalInviteCodeHashSchemaAgreementTests"/>.
/// </remarks>
[Trait("Category", "Security")]
public class ExternalAccessContactSecuritySchemaAgreementTests
{
    private const string Readers = "Spaarke BFF-Managed Field Readers";
    private const string Writers = "Spaarke BFF-Managed Field Writers";
    private const string LinkReaders = "Spaarke Identity Link Readers";
    private const string LinkWriters = "Spaarke Identity Link Writers";
    private const string StandingGrantAdministrators = "Standing Grant Administrators";

    /// <summary>The profiles whose only members are the BFF application user(s).</summary>
    private static readonly string[] BffProfiles = [Writers, LinkWriters];

    private static readonly string[] PlaneAndCollisionColumns =
    [
        ContactBindingDecision.IdentityPlaneColumn,
        DataverseContactIdentityStore.FlagOnColumn,
        DataverseContactIdentityStore.FlagOidColumn,
        DataverseContactIdentityStore.FlagPlaneColumn,
        DataverseContactIdentityStore.FlagReasonColumn,
        DataverseContactIdentityStore.FlagPartiesColumn,
    ];

    private static readonly IReadOnlySet<Grant> ExpectedGrants = new HashSet<Grant>(
        new[]
        {
            new Grant(SubjectStandingGrantReader.BaselineAttribute, StandingGrantAdministrators, 4, 4, 4),
            new Grant(SubjectStandingGrantReader.BaselineAttribute, Readers, 4, 0, 0),
            new Grant(SubjectStandingGrantReader.BaselineAttribute, Writers, 4, 0, 4),
            new Grant(ContactBindingDecision.ExternalInvitedEmailHashColumn, Writers, 4, 4, 4),
            new Grant(ContactBindingDecision.ExternalInvitedTenantIdColumn, Writers, 4, 4, 4),
            new Grant(ContactBindingDecision.ExternalAccessRemovedOnColumn, Readers, 4, 0, 0),
            new Grant(ContactBindingDecision.ExternalAccessRemovedOnColumn, Writers, 4, 4, 4),
        }
        .Concat(PlaneAndCollisionColumns.SelectMany(c => new[]
        {
            new Grant(c, LinkReaders, 4, 0, 0),
            new Grant(c, LinkWriters, 4, 4, 4),
        })));

    private static readonly IReadOnlyList<NewColumn> ExpectedNewColumns =
    [
        new(ContactBindingDecision.ExternalInvitedEmailHashColumn, "String", 100, Audit: false),
        new(ContactBindingDecision.ExternalInvitedTenantIdColumn, "String", 36, Audit: true),
        new(ContactBindingDecision.ExternalAccessRemovedOnColumn, "DateTime", 0, Audit: true),
    ];

    // ── The three sources agree ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheScript_LocksExactlyTheExpectedColumns()
    {
        var script = Analyse(File.ReadAllText(ScriptPath()));

        script.Table.Should().Be("contact");
        script.Secured.Should().BeTrue();
        script.Grants.Should().BeEquivalentTo(ExpectedGrants, "every profile x column pair, exactly");
        script.NewColumns.Should().BeEquivalentTo(ExpectedNewColumns);
        script.Solutions.Should().Contain(new[] { "SpaarkeCore", "SpaarkeMaster" },
            "stamps import only SpaarkeMaster, where contact carries no subcomponents");
        Agrees(script).Should().BeTrue();
    }

    [Fact]
    public void ThePackage_SecuresAndGrantsExactlyWhatTheScriptDoes()
    {
        var script = Analyse(File.ReadAllText(ScriptPath()));
        var entity = XDocument.Load(PackagePath("Entities", "Contact", "Entity.xml"));
        var profiles = XDocument.Load(PackagePath("Other", "FieldSecurityProfiles.xml"));

        PackageGaps(script, entity, profiles).Should().BeEmpty(
            "Entity.xml IsSecured, FieldSecurityProfiles.xml and the schema script's column list must agree");
    }

    [Fact]
    public void TheBffApplicationUser_KeepsReadingEveryLockedColumn()
    {
        // The BFF reads every one of these columns app-only; its application users are the members of the BFF profiles.
        var script = Analyse(File.ReadAllText(ScriptPath()));

        foreach (var column in script.Grants.Select(g => g.Column).Distinct())
        {
            script.Grants.Should().Contain(g => g.Column == column && BffProfiles.Contains(g.Profile) && g.Read == 4,
                $"the BFF must keep reading contact.{column}");
        }
    }

    [Fact]
    public void OnlyTheBffAndStandingGrantAdministrators_CanWriteAnyLockedColumn()
    {
        var script = Analyse(File.ReadAllText(ScriptPath()));

        script.Grants.Where(g => g.Create == 4 || g.Update == 4).Select(g => g.Profile).Distinct()
            .Should().BeSubsetOf(BffProfiles.Append(StandingGrantAdministrators));
        script.Grants.Where(g => g.Profile == StandingGrantAdministrators).Select(g => g.Column)
            .Should().Equal(SubjectStandingGrantReader.BaselineAttribute);
    }

    [Fact]
    public void TheScript_NeverCreatesAFieldSecurityProfile()
        => File.ReadAllText(ScriptPath()).Should().NotMatchRegex(@"POST\s+'fieldsecurityprofiles'",
            "the script reuses the existing profiles only");

    [Fact]
    public void TheScriptsVerify_ChecksEveryPair_AndEveryOtherProfile()
    {
        var text = File.ReadAllText(ScriptPath());

        text.Should().Contain("foreach ($g in $rows)", "every listed profile x column pair is read and compared");
        text.Should().MatchRegex(@"attributelogicalname eq '\$c' and \(canread eq 4 or canupdate eq 4 or cancreate eq 4\)",
            "any other profile that can read, create or update a locked column is FAIL");
        text.Should().Contain("IsSecured              = $Secured", "a new column is secured in its create request");
    }

    [Fact]
    public void TheScriptsVerify_ReadsEveryTableOfTheReadabilityProof_AsTheBffApplicationUser()
    {
        // The BFF trusts an EMPTY secured value only after proving its read (SecuredColumnReadProof); if its application
        // user cannot read the proof's tables, every such read is refused. -Verify runs the proof's reads as that user.
        var text = File.ReadAllText(ScriptPath());
        var listed = System.Text.RegularExpressions.Regex.Match(text, @"\$ProofTables = @\(([^)]*)\)");

        listed.Success.Should().BeTrue("the script lists the proof's tables");
        listed.Groups[1].Value.Split(',').Select(t => t.Trim().Trim('\'')).Should()
            .BeEquivalentTo(SecuredColumnReadProof.PlatformTablesRead, "the script verifies exactly the tables the proof reads");
        foreach (var table in SecuredColumnReadProof.PlatformTablesRead.Where(t => t != "systemuser"))
        {
            text.Should().Contain($"name=`\"{table}`\"", $"a proof read joins {table}");
        }

        text.Should().Contain("$h['MSCRMCallerID'] = $SystemUserId", "the reads are made as the BFF application user");
        text.Should().Contain("Path = 'WhoAmI'", "the proof's first read is the user's own systemuser");
    }

    // ── The guard bites: the script parser on a seeded script ─────────────────────────────────────────

    private const string Seed = """
        [string[]]$SolutionUniqueNames = @('SpaarkeCore', 'SpaarkeMaster'),
        $Table = 'contact'
        $ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
        $WriterProfileName = 'Spaarke BFF-Managed Field Writers'
        $LinkReaderProfileName = 'Spaarke Identity Link Readers'
        $LinkWriterProfileName = 'Spaarke Identity Link Writers'
        $StandingGrantProfileName = 'Standing Grant Administrators'
        $Secured = $true
        $NewColumns = @(
            @{ Column = 'sprk_externalinvitedemailhash'; SchemaName = 'sprk_ExternalInvitedEmailHash'; AttributeType = 'String'; MaxLength = 100; AuditEnabled = $false }
            @{ Column = 'sprk_externalinvitedtenantid'; SchemaName = 'sprk_ExternalInvitedTenantId'; AttributeType = 'String'; MaxLength = 36; AuditEnabled = $true }
            @{ Column = 'sprk_externalaccessremovedon'; SchemaName = 'sprk_ExternalAccessRemovedOn'; AttributeType = 'DateTime'; MaxLength = 0; AuditEnabled = $true }
        )
        $Grants = @(
            @{ Column = 'sprk_accesspermissiongrant'; Profile = $StandingGrantProfileName; Read = 4; Create = 4; Update = 4 }
            @{ Column = 'sprk_accesspermissiongrant'; Profile = $ReaderProfileName; Read = 4; Create = 0; Update = 0 }
            @{ Column = 'sprk_accesspermissiongrant'; Profile = $WriterProfileName; Read = 4; Create = 0; Update = 4 }
            @{ Column = 'sprk_identityplane'; Profile = $LinkReaderProfileName; Read = 4; Create = 0; Update = 0 }
            @{ Column = 'sprk_identityplane'; Profile = $LinkWriterProfileName; Read = 4; Create = 4; Update = 4 }
            @{ Column = 'sprk_identitycollisionon'; Profile = $LinkReaderProfileName; Read = 4; Create = 0; Update = 0 }
            @{ Column = 'sprk_identitycollisionon'; Profile = $LinkWriterProfileName; Read = 4; Create = 4; Update = 4 }
            @{ Column = 'sprk_identitycollisionoid'; Profile = $LinkReaderProfileName; Read = 4; Create = 0; Update = 0 }
            @{ Column = 'sprk_identitycollisionoid'; Profile = $LinkWriterProfileName; Read = 4; Create = 4; Update = 4 }
            @{ Column = 'sprk_identitycollisionplane'; Profile = $LinkReaderProfileName; Read = 4; Create = 0; Update = 0 }
            @{ Column = 'sprk_identitycollisionplane'; Profile = $LinkWriterProfileName; Read = 4; Create = 4; Update = 4 }
            @{ Column = 'sprk_identitycollisionreason'; Profile = $LinkReaderProfileName; Read = 4; Create = 0; Update = 0 }
            @{ Column = 'sprk_identitycollisionreason'; Profile = $LinkWriterProfileName; Read = 4; Create = 4; Update = 4 }
            @{ Column = 'sprk_identitycollisionparties'; Profile = $LinkReaderProfileName; Read = 4; Create = 0; Update = 0 }
            @{ Column = 'sprk_identitycollisionparties'; Profile = $LinkWriterProfileName; Read = 4; Create = 4; Update = 4 }
            @{ Column = 'sprk_externalinvitedemailhash'; Profile = $WriterProfileName; Read = 4; Create = 4; Update = 4 }
            @{ Column = 'sprk_externalinvitedtenantid'; Profile = $WriterProfileName; Read = 4; Create = 4; Update = 4 }
            @{ Column = 'sprk_externalaccessremovedon'; Profile = $ReaderProfileName; Read = 4; Create = 0; Update = 0 }
            @{ Column = 'sprk_externalaccessremovedon'; Profile = $WriterProfileName; Read = 4; Create = 4; Update = 4 }
        )
        """;

    [Fact]
    public void TheParser_ReadsTheSeed()
        => Agrees(Analyse(Seed)).Should().BeTrue();

    /// <summary>Each drift below changes who can read or write a locked column, or which column is locked; each fails.</summary>
    [Theory]
    [InlineData("$Secured = $true", "$Secured = $false")]
    [InlineData("'sprk_accesspermissiongrant'; Profile = $ReaderProfileName; Read = 4; Create = 0; Update = 0", "'sprk_accesspermissiongrant'; Profile = $ReaderProfileName; Read = 4; Create = 0; Update = 4")]
    [InlineData("'sprk_accesspermissiongrant'; Profile = $WriterProfileName; Read = 4;", "'sprk_accesspermissiongrant'; Profile = $WriterProfileName; Read = 0;")]
    [InlineData("'sprk_accesspermissiongrant'; Profile = $StandingGrantProfileName;", "'sprk_accesspermissiongrant'; Profile = $ReaderProfileName;")]
    [InlineData("'sprk_identityplane'; Profile = $LinkReaderProfileName; Read = 4; Create = 0", "'sprk_identityplane'; Profile = $LinkReaderProfileName; Read = 4; Create = 4")]
    [InlineData("'sprk_identitycollisionparties'; Profile = $LinkWriterProfileName", "'sprk_identitycollisionparties'; Profile = $WriterProfileName")]
    [InlineData("'sprk_externalinvitedemailhash'; Profile = $WriterProfileName", "'sprk_externalinvitedemailhash'; Profile = $ReaderProfileName")]
    [InlineData("'sprk_externalaccessremovedon'; Profile = $ReaderProfileName; Read = 4", "'sprk_externalaccessremovedon'; Profile = $ReaderProfileName; Read = 0")]
    [InlineData("'Spaarke Identity Link Writers'", "'Spaarke Contact Editors'")]
    [InlineData("'Standing Grant Administrators'", "'Standing Grant Editors'")]
    [InlineData("$Table = 'contact'", "$Table = 'account'")]
    [InlineData("MaxLength = 100; AuditEnabled = $false", "MaxLength = 100; AuditEnabled = $true")]
    [InlineData("MaxLength = 100;", "MaxLength = 64;")]
    [InlineData("AttributeType = 'DateTime'", "AttributeType = 'String'")]
    [InlineData("'SpaarkeCore', 'SpaarkeMaster'", "'SpaarkeCore'")]
    public void TheParser_SeesEachDrift(string from, string to)
    {
        Agrees(Analyse(Seed)).Should().BeTrue("the faithful seed agrees with the BFF");
        Seed.Should().Contain(from, "each drift must replace text the seed actually has");
        Agrees(Analyse(Seed.Replace(from, to))).Should().BeFalse("each seeded drift must fail the agreement");
    }

    [Fact]
    public void TheParser_SeesAGrantRowRemoved()
    {
        var removed = string.Join('\n', Seed.Split('\n')
            .Where(l => !l.Contains("'sprk_identitycollisionoid'; Profile = $LinkReaderProfileName", StringComparison.Ordinal)));

        Agrees(Analyse(removed)).Should().BeFalse("a column every user must read lost its reader row");
    }

    // ── The guard bites: the package scan on seeded XML ───────────────────────────────────────────────

    private static readonly ScriptShape OneColumn = new(
        "contact", true,
        new HashSet<Grant> { new("sprk_a", Readers, 4, 0, 0), new("sprk_a", Writers, 4, 4, 4) },
        [new NewColumn("sprk_a", "String", 100, Audit: false)],
        ["SpaarkeCore", "SpaarkeMaster"]);

    private const string SeedEntity = """
        <Entity><EntityInfo><entity Name="Contact"><attributes>
          <attribute PhysicalName="sprk_A"><Type>nvarchar</Type><LogicalName>sprk_a</LogicalName><IsAuditEnabled>0</IsAuditEnabled><IsSecured>1</IsSecured><MaxLength>100</MaxLength></attribute>
        </attributes></entity></EntityInfo></Entity>
        """;

    private const string SeedProfiles = """
        <FieldSecurityProfiles>
          <FieldSecurityProfile name="Spaarke BFF-Managed Field Readers"><FieldPermissions>
            <FieldPermission><EntityName>contact</EntityName><AttributeName>sprk_a</AttributeName><CanRead>4</CanRead><CanUpdate>0</CanUpdate><CanCreate>0</CanCreate></FieldPermission>
          </FieldPermissions></FieldSecurityProfile>
          <FieldSecurityProfile name="Spaarke BFF-Managed Field Writers"><FieldPermissions>
            <FieldPermission><EntityName>contact</EntityName><AttributeName>sprk_a</AttributeName><CanRead>4</CanRead><CanUpdate>4</CanUpdate><CanCreate>4</CanCreate></FieldPermission>
          </FieldPermissions></FieldSecurityProfile>
          <FieldSecurityProfile name="Other"><FieldPermissions>
            <FieldPermission><EntityName>contact</EntityName><AttributeName>sprk_b</AttributeName><CanRead>4</CanRead><CanUpdate>4</CanUpdate><CanCreate>4</CanCreate></FieldPermission>
          </FieldPermissions></FieldSecurityProfile>
        </FieldSecurityProfiles>
        """;

    [Fact]
    public void ThePackageScan_PassesTheSeed()
        => PackageGaps(OneColumn, XDocument.Parse(SeedEntity), XDocument.Parse(SeedProfiles)).Should().BeEmpty();

    [Theory]
    [InlineData("<IsSecured>1</IsSecured>", "<IsSecured>0</IsSecured>", true)]                              // not secured
    [InlineData("<IsAuditEnabled>0</IsAuditEnabled>", "<IsAuditEnabled>1</IsAuditEnabled>", true)]          // audited
    [InlineData("<MaxLength>100</MaxLength>", "<MaxLength>64</MaxLength>", true)]                            // too short
    [InlineData("<LogicalName>sprk_a</LogicalName>", "<LogicalName>sprk_c</LogicalName>", true)]            // missing
    [InlineData("<CanRead>4</CanRead><CanUpdate>0</CanUpdate>", "<CanRead>4</CanRead><CanUpdate>4</CanUpdate>", false)] // reader writes
    [InlineData("<AttributeName>sprk_b</AttributeName>", "<AttributeName>sprk_a</AttributeName>", false)]   // another profile
    [InlineData("name=\"Spaarke BFF-Managed Field Writers\"", "name=\"Spaarke Contact Editors\"", false)]   // grant missing
    public void ThePackageScan_SeesEachDrift(string from, string to, bool inEntity)
    {
        var entity = inEntity ? SeedEntity.Replace(from, to) : SeedEntity;
        var profiles = inEntity ? SeedProfiles : SeedProfiles.Replace(from, to);
        (inEntity ? SeedEntity : SeedProfiles).Should().Contain(from);

        PackageGaps(OneColumn, XDocument.Parse(entity), XDocument.Parse(profiles)).Should().NotBeEmpty();
    }

    // ── Script parser ──────────────────────────────────────────────────────────────────────────────────

    private sealed record Grant(string Column, string Profile, int Read, int Create, int Update);

    private sealed record NewColumn(string Column, string AttributeType, int MaxLength, bool Audit);

    private sealed record ScriptShape(
        string? Table, bool Secured, IReadOnlySet<Grant> Grants, IReadOnlyList<NewColumn> NewColumns,
        IReadOnlyList<string> Solutions);

    private static bool Agrees(ScriptShape s) =>
        s.Table == "contact"
        && s.Secured
        && s.Grants.SetEquals(ExpectedGrants)
        && s.NewColumns.SequenceEqual(ExpectedNewColumns)
        && s.Solutions.Contains("SpaarkeCore") && s.Solutions.Contains("SpaarkeMaster");

    private static ScriptShape Analyse(string script)
    {
        static string? Single(string text, string pattern) =>
            Regex.Match(text, pattern, RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

        // $SomethingProfileName = '...' → the variable each grant row names.
        var profileVariables = Regex.Matches(script, @"^\s*\$(\w+ProfileName)\s*=\s*'([^']+)'", RegexOptions.Multiline)
            .GroupBy(m => m.Groups[1].Value)
            .ToDictionary(g => g.Key, g => g.First().Groups[2].Value);

        var grants = Regex.Matches(script,
                @"@\{\s*Column\s*=\s*'([^']+)'\s*;\s*Profile\s*=\s*\$(\w+)\s*;\s*Read\s*=\s*(\d+)\s*;\s*Create\s*=\s*(\d+)\s*;\s*Update\s*=\s*(\d+)\s*\}")
            .Select(m => new Grant(
                m.Groups[1].Value,
                profileVariables.TryGetValue(m.Groups[2].Value, out var name) ? name : "$" + m.Groups[2].Value,
                int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value)))
            .ToHashSet();

        var newColumns = Regex.Matches(script,
                @"@\{\s*Column\s*=\s*'([^']+)'\s*;\s*SchemaName\s*=\s*'([^']+)'\s*;\s*AttributeType\s*=\s*'(\w+)'\s*;\s*MaxLength\s*=\s*(\d+)\s*;\s*AuditEnabled\s*=\s*\$(\w+)")
            .Where(m => string.Equals(m.Groups[2].Value, m.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
            .Select(m => new NewColumn(m.Groups[1].Value, m.Groups[3].Value, int.Parse(m.Groups[4].Value),
                // Anything but an explicit $false counts as auditing on (fail closed).
                !string.Equals(m.Groups[5].Value, "false", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var solutionsLine = Single(script, @"\$SolutionUniqueNames\s*=\s*@\(([^)]*)\)");
        var solutions = solutionsLine is null
            ? Array.Empty<string>()
            : Regex.Matches(solutionsLine, @"'([^']+)'").Select(m => m.Groups[1].Value).ToArray();

        return new ScriptShape(
            Single(script, @"^\s*\$Table\s*=\s*'([^']+)'"),
            string.Equals(Single(script, @"^\s*\$Secured\s*=\s*\$(\w+)"), "true", StringComparison.OrdinalIgnoreCase),
            grants, newColumns, solutions);
    }

    // ── Package scan ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Every way the package disagrees with the script, as one line each (empty = they agree).</summary>
    private static List<string> PackageGaps(ScriptShape script, XDocument entity, XDocument profiles)
    {
        var gaps = new List<string>();
        var attributes = entity.Descendants("attribute")
            .GroupBy(a => (string?)a.Element("LogicalName") ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var column in script.Grants.Select(g => g.Column).Distinct())
        {
            if (!attributes.TryGetValue(column, out var attribute))
            {
                gaps.Add($"Entity.xml does not carry contact.{column}");
                continue;
            }

            if ((string?)attribute.Element("IsSecured") != "1")
                gaps.Add($"Entity.xml: contact.{column} IsSecured is not 1");
        }

        foreach (var column in script.NewColumns)
        {
            if (!attributes.TryGetValue(column.Column, out var attribute))
                continue; // reported above
            var type = (string?)attribute.Element("Type");
            var expectedType = column.AttributeType == "DateTime" ? "datetime" : "nvarchar";
            if (type != expectedType)
                gaps.Add($"Entity.xml: contact.{column.Column} is {type}, expected {expectedType}");
            if (column.AttributeType == "String" && (string?)attribute.Element("MaxLength") != column.MaxLength.ToString())
                gaps.Add($"Entity.xml: contact.{column.Column} MaxLength is not {column.MaxLength}");
            if (((string?)attribute.Element("IsAuditEnabled") == "1") != column.Audit)
                gaps.Add($"Entity.xml: contact.{column.Column} auditing is not {column.Audit}");
        }

        var columns = script.Grants.Select(g => g.Column).ToHashSet(StringComparer.Ordinal);
        var packaged = profiles.Descendants("FieldSecurityProfile")
            .SelectMany(p => p.Descendants("FieldPermission").Select(f => new
            {
                Profile = (string?)p.Attribute("name") ?? string.Empty,
                Entity = (string?)f.Element("EntityName"),
                Column = (string?)f.Element("AttributeName") ?? string.Empty,
                Read = (string?)f.Element("CanRead") == "4" ? 4 : 0,
                Create = (string?)f.Element("CanCreate") == "4" ? 4 : 0,
                Update = (string?)f.Element("CanUpdate") == "4" ? 4 : 0,
            }))
            .Where(f => f.Entity == "contact" && columns.Contains(f.Column))
            .Select(f => new Grant(f.Column, f.Profile, f.Read, f.Create, f.Update))
            .Where(g => g.Read == 4 || g.Create == 4 || g.Update == 4)
            .ToHashSet();

        foreach (var missing in script.Grants.Except(packaged))
            gaps.Add($"FieldSecurityProfiles.xml lacks {missing}");
        foreach (var extra in packaged.Except(script.Grants))
            gaps.Add($"FieldSecurityProfiles.xml grants {extra}, which the script does not");

        return gaps;
    }

    private static string ScriptPath() => Path.Combine(RepoRoot(), "scripts", "Set-ExternalAccessContactSecuritySchema.ps1");

    private static string PackagePath(params string[] parts)
        => Path.Combine([RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", .. parts]);

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            // `.git` is a FILE in a git worktree, which is how this repo is normally developed.
            var git = Path.Combine(dir, ".git");
            if (System.IO.Directory.Exists(git) || File.Exists(git))
                return dir;

            dir = System.IO.Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}

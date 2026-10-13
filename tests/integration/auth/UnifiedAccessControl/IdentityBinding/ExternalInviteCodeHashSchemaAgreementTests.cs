using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;

/// <summary>
/// unified-access-control-r2 task 185 (owner round 98) — the invite-code hash's schema script
/// (<c>scripts/Set-ExternalInviteCodeHashSchema.ps1</c>) creates the column the invite writes and E11's marker leg reads,
/// and locks it so ONLY the BFF can read or write it: a single line of text (max 100), field-secured, the reader profile
/// with NO read, create or update, the writer profile (the BFF application user(s) only) read/create/update, auditing
/// off, existing profiles only, the column in BOTH SpaarkeCore and SpaarkeMaster, and task 184's marker column checked
/// first.
/// </summary>
/// <remarks>
/// <para><b>Why a source guard.</b> The column does not exist until the main session runs <c>-Apply</c>, so every
/// behavioural test runs against a double that admits whatever name the code uses. A script that named another column, or
/// left it too short for the stored hash, would leave every test green while every invite failed in the environment. A
/// script that let the reader profile READ the hash would expose it to every business unit's default team; one that let
/// it write, or left the column unsecured, would let a user set a code of their own and bind an invited contact to any
/// CIAM account; one that left auditing on would keep consumed hashes in audit rows. Read as text (no PowerShell host, no
/// network); the parser is exercised on seeded snippets, so a regression in the guard reddens too. Mirrors
/// <see cref="ExternalInviteMarkerSchemaAgreementTests"/>.</para>
/// <para><b>The column name.</b> Slice A (schema) lands before the BFF code that names the column, so the expected name is
/// pinned here from the design (§3); the slice that adds the BFF constant points <see cref="ExpectedColumn"/> at it.</para>
/// </remarks>
[Trait("Category", "Security")]
public class ExternalInviteCodeHashSchemaAgreementTests
{
    private const string ExpectedColumn = "sprk_externalinvitecodehash";
    private const int ExpectedMaxLength = 100;
    private const string ReaderProfile = "Spaarke BFF-Managed Field Readers";
    private const string WriterProfile = "Spaarke BFF-Managed Field Writers";

    [Fact]
    public void TheScript_CreatesAndSecures_TheHashColumn()
    {
        var schema = Analyse(File.ReadAllText(ScriptPath()));

        schema.Table.Should().Be("contact");
        schema.Column.Should().Be(ExpectedColumn);
        schema.ColumnSchemaName!.ToLowerInvariant().Should().Be(ExpectedColumn,
            "Dataverse derives the logical name from the schema name");
        (schema.AttributeType, schema.Format).Should().Be(("String", "Text"), "a single line of text");
        schema.MaxLength.Should().Be(ExpectedMaxLength, "the stored value is 69 characters; the room is for a v2");
        schema.AuditEnabled.Should().BeFalse("an audit row would keep old hashes after the bind clears them");
        schema.MarkerColumn.Should().Be(ContactBindingDecision.ExternalInvitedOnColumn,
            "the script checks task 184's marker column first; the code means nothing without it");
        schema.Solutions.Should().Contain(new[] { "SpaarkeCore", "SpaarkeMaster" },
            "stamps import only SpaarkeMaster, where contact carries no subcomponents");
        Agrees(schema).Should().BeTrue("only the BFF may read or write the invite-code hash");
    }

    // ── The guard bites: the parser on seeded scripts ───────────────────────────────────────────────

    private const string Seed = """
        [string[]]$SolutionUniqueNames = @('SpaarkeCore', 'SpaarkeMaster'),
        $Table = 'contact'
        $Column = 'sprk_externalinvitecodehash'
        $ColumnSchemaName = 'sprk_ExternalInviteCodeHash'
        $AttributeType = 'String'
        $Format = 'Text'
        $MaxLength = 100
        $AuditEnabled = $false
        $MarkerColumn = 'sprk_externalinvitedon'
        $ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
        $WriterProfileName = 'Spaarke BFF-Managed Field Writers'
        $Secured = $true
        foreach ($spec in @(@{ Id = $readerId; Name = $ReaderProfileName; Read = 0; Create = 0; Update = 0 }, @{ Id = $writerId; Name = $WriterProfileName; Read = 4; Create = 4; Update = 4 })) {
        """;

    [Fact]
    public void TheParser_ReadsTheSeed()
        => Agrees(Analyse(Seed)).Should().BeTrue();

    /// <summary>
    /// MUTATION PROOF (task 185): "a code hash readable or settable by anyone but the BFF" is impossible only while this
    /// agreement holds. Each drift below would expose the hash, let someone else set it, keep it in audit, or put it on the
    /// wrong column, and each fails.
    /// </summary>
    [Theory]
    [InlineData("$Secured = $true", "$Secured = $false")]                                                   // anyone with Read/Write
    [InlineData("Name = $ReaderProfileName; Read = 0;", "Name = $ReaderProfileName; Read = 4;")]           // the marker's reader grant, copied
    [InlineData("Name = $ReaderProfileName; Read = 0; Create = 0;", "Name = $ReaderProfileName; Read = 0; Create = 4;")]
    [InlineData("Name = $ReaderProfileName; Read = 0; Create = 0; Update = 0", "Name = $ReaderProfileName; Read = 0; Create = 0; Update = 4")]
    [InlineData("Name = $WriterProfileName; Read = 4;", "Name = $WriterProfileName; Read = 0;")]           // the BFF cannot compare
    [InlineData("Name = $WriterProfileName; Read = 4; Create = 4; Update = 4", "Name = $WriterProfileName; Read = 4; Create = 4; Update = 0")]
    [InlineData("'Spaarke BFF-Managed Field Writers'", "'Spaarke Contact Editors'")]                          // another writer profile
    [InlineData("$AuditEnabled = $false", "$AuditEnabled = $true")]                                          // old hashes kept
    [InlineData("$MaxLength = 100", "$MaxLength = 64")]                                                      // the 69-char value does not fit
    [InlineData("$AttributeType = 'String'", "$AttributeType = 'Memo'")]
    [InlineData("$Column = 'sprk_externalinvitecodehash'", "$Column = 'sprk_externalinvitecode'")]
    [InlineData("$MarkerColumn = 'sprk_externalinvitedon'", "$MarkerColumn = 'sprk_externalinvitedate'")]
    [InlineData("$Table = 'contact'", "$Table = 'account'")]
    [InlineData("'SpaarkeCore', 'SpaarkeMaster'", "'SpaarkeCore'")]                                          // stamps never get it
    public void TheParser_SeesEachDrift(string from, string to)
    {
        Agrees(Analyse(Seed)).Should().BeTrue("the faithful seed agrees with the BFF");
        Seed.Should().Contain(from, "each drift must replace text the seed actually has");
        Agrees(Analyse(Seed.Replace(from, to))).Should().BeFalse("each seeded drift must fail the agreement");
    }

    [Fact]
    public void TheScript_NeverCreatesAFieldSecurityProfile()
        // Provisioning's H7b creates and populates the profiles on a new stamp; a profile created here would be a second,
        // unpopulated lock with the same name.
        => File.ReadAllText(ScriptPath()).Should().NotMatchRegex(@"POST\s+'fieldsecurityprofiles'",
            "the script reuses the existing BFF-Managed profiles only");

    [Fact]
    public void TheScript_FailsAnyOtherProfileThatCanRead_NotOnlyWrite()
        // The marker script fails only other WRITERS; for the hash a reader is as bad (§3: "Any other profile that can read,
        // create or update it is FAIL").
        => File.ReadAllText(ScriptPath()).Should().MatchRegex(
            @"attributelogicalname eq '\$Column' and \(canread eq 4 or canupdate eq 4 or cancreate eq 4\)",
            "the other-profile sweep must include read");

    // ── Parser ───────────────────────────────────────────────────────────────────────────────────────

    private sealed record SchemaShape(
        string? Table, string? Column, string? ColumnSchemaName, string? AttributeType, string? Format, int? MaxLength,
        bool AuditEnabled, string? MarkerColumn, string? ReaderProfile, string? WriterProfile, bool Secured,
        (int Read, int Create, int Update)? ReaderGrant, (int Read, int Create, int Update)? WriterGrant,
        IReadOnlyList<string> Solutions);

    private static bool Agrees(SchemaShape s) =>
        s.Table == "contact"
        && s.Column == ExpectedColumn
        && s.ColumnSchemaName?.ToLowerInvariant() == ExpectedColumn
        && s.AttributeType == "String"
        && s.Format == "Text"
        && s.MaxLength == ExpectedMaxLength
        && !s.AuditEnabled
        && s.MarkerColumn == ContactBindingDecision.ExternalInvitedOnColumn
        && s.ReaderProfile == ReaderProfile
        && s.WriterProfile == WriterProfile
        && s.Secured
        && s.ReaderGrant == (0, 0, 0)
        && s.WriterGrant == (4, 4, 4)
        && s.Solutions.Contains("SpaarkeCore") && s.Solutions.Contains("SpaarkeMaster");

    private static SchemaShape Analyse(string script)
    {
        static string? Single(string text, string pattern) =>
            Regex.Match(text, pattern, RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

        static (int, int, int)? Grant(string text, string profileVariable)
        {
            var m = Regex.Match(text,
                @"Name\s*=\s*\$" + profileVariable
                + @"\s*;\s*Read\s*=\s*(\d+)\s*;\s*Create\s*=\s*(\d+)\s*;\s*Update\s*=\s*(\d+)");
            return m.Success
                ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value))
                : null;
        }

        var solutionsLine = Single(script, @"\$SolutionUniqueNames\s*=\s*@\(([^)]*)\)");
        var solutions = solutionsLine is null
            ? Array.Empty<string>()
            : Regex.Matches(solutionsLine, @"'([^']+)'").Select(m => m.Groups[1].Value).ToArray();
        var maxLength = Single(script, @"^\s*\$MaxLength\s*=\s*(\d+)\s*$");
        // Anything but an explicit $false counts as auditing on (fail closed).
        var audit = Single(script, @"^\s*\$AuditEnabled\s*=\s*\$(\w+)");

        return new SchemaShape(
            Single(script, @"^\s*\$Table\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$Column\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$ColumnSchemaName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$AttributeType\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$Format\s*=\s*'([^']+)'"),
            maxLength is null ? null : int.Parse(maxLength),
            !string.Equals(audit, "false", StringComparison.OrdinalIgnoreCase),
            Single(script, @"^\s*\$MarkerColumn\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$ReaderProfileName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$WriterProfileName\s*=\s*'([^']+)'"),
            string.Equals(Single(script, @"^\s*\$Secured\s*=\s*\$(\w+)"), "true", StringComparison.OrdinalIgnoreCase),
            Grant(script, "ReaderProfileName"),
            Grant(script, "WriterProfileName"),
            solutions);
    }

    private static string ScriptPath() => Path.Combine(RepoRoot(), "scripts", "Set-ExternalInviteCodeHashSchema.ps1");

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

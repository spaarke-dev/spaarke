using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;

/// <summary>
/// unified-access-control-r2 task 184 (owner round 96) — the pending-invite marker's schema script
/// (<c>scripts/Set-ExternalInviteMarkerSchema.ps1</c>) and the BFF agree on the column the invite writes and E11 reads, and
/// the script locks it so ONLY the BFF can write it: field-secured, the reader profile read-only, the writer profile (the
/// BFF application user(s) only) read/create/update, existing profiles only, and the column in BOTH SpaarkeCore and
/// SpaarkeMaster.
/// </summary>
/// <remarks>
/// <para><b>Why a source guard.</b> The column does not exist until the main session runs <c>-Apply</c>, so every
/// behavioural test runs against a double that admits whatever name the code uses. A script that named another column
/// would leave every test green while, in the environment, every invite logged "column missing" and E11 never saw an
/// invite. A script that left the column unsecured — or let the reader profile write it — would let any user with contact
/// Write mark a contact invited and open it to an email bind by whichever CIAM user signs in with its email: the
/// cross-customer gap this task closes. Read as text (no PowerShell host, no network); the parser is exercised on seeded
/// snippets, so a regression in the guard reddens too. Mirrors <c>ChildRecordCreatorPersonSchemaAgreementTests</c>.</para>
/// </remarks>
[Trait("Category", "Security")]
public class ExternalInviteMarkerSchemaAgreementTests
{
    private const string ReaderProfile = "Spaarke BFF-Managed Field Readers";
    private const string WriterProfile = "Spaarke BFF-Managed Field Writers";

    [Fact]
    public void TheScript_CreatesAndSecures_TheColumnTheBffWritesAndReads()
    {
        var schema = Analyse(File.ReadAllText(ScriptPath()));

        schema.Table.Should().Be("contact");
        schema.Column.Should().Be(ContactBindingDecision.ExternalInvitedOnColumn);
        schema.ColumnSchemaName!.ToLowerInvariant().Should().Be(ContactBindingDecision.ExternalInvitedOnColumn,
            "Dataverse derives the logical name from the schema name");
        (schema.AttributeType, schema.Format).Should().Be(("DateTime", "DateAndTime"), "a UTC date and time");
        schema.Solutions.Should().Contain(new[] { "SpaarkeCore", "SpaarkeMaster" },
            "stamps import only SpaarkeMaster, where contact carries no subcomponents");
        Agrees(schema).Should().BeTrue("only the BFF may mark a contact invited");
    }

    // ── The guard bites: the parser on seeded scripts ───────────────────────────────────────────────

    private const string Seed = """
        [string[]]$SolutionUniqueNames = @('SpaarkeCore', 'SpaarkeMaster'),
        $Table = 'contact'
        $Column = 'sprk_externalinvitedon'
        $ColumnSchemaName = 'sprk_ExternalInvitedOn'
        $AttributeType = 'DateTime'
        $Format = 'DateAndTime'
        $ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
        $WriterProfileName = 'Spaarke BFF-Managed Field Writers'
        $Secured = $true
        foreach ($spec in @(@{ Id = $readerId; Name = $ReaderProfileName; Create = 0; Update = 0 }, @{ Id = $writerId; Name = $WriterProfileName; Create = 4; Update = 4 })) {
        """;

    [Fact]
    public void TheParser_ReadsTheSeed()
        => Agrees(Analyse(Seed)).Should().BeTrue();

    /// <summary>
    /// MUTATION PROOF (task 184): "a marker set but not BFF-written" is impossible only while this agreement holds. Each
    /// drift below would let someone other than the BFF set the marker (or put it on the wrong column), and each fails.
    /// </summary>
    [Theory]
    [InlineData("$Secured = $true", "$Secured = $false")]                                         // anyone with Write
    [InlineData("Name = $ReaderProfileName; Create = 0; Update = 0", "Name = $ReaderProfileName; Create = 4; Update = 0")]
    [InlineData("Name = $ReaderProfileName; Create = 0; Update = 0", "Name = $ReaderProfileName; Create = 0; Update = 4")]
    [InlineData("Name = $WriterProfileName; Create = 4; Update = 4", "Name = $WriterProfileName; Create = 4; Update = 0")]
    [InlineData("'Spaarke BFF-Managed Field Writers'", "'Spaarke Contact Editors'")]                // another writer profile
    [InlineData("$Column = 'sprk_externalinvitedon'", "$Column = 'sprk_externalinvitedate'")]
    [InlineData("$Table = 'contact'", "$Table = 'account'")]
    [InlineData("'SpaarkeCore', 'SpaarkeMaster'", "'SpaarkeCore'")]                                // stamps never get it
    public void TheParser_SeesEachDrift(string from, string to)
    {
        Agrees(Analyse(Seed)).Should().BeTrue("the faithful seed agrees with the BFF");
        Agrees(Analyse(Seed.Replace(from, to))).Should().BeFalse("each seeded drift must fail the agreement");
    }

    [Fact]
    public void TheScript_NeverCreatesAFieldSecurityProfile()
        // Provisioning's H7b creates and populates the profiles on a new stamp; a profile created here would be a second,
        // unpopulated lock with the same name.
        => File.ReadAllText(ScriptPath()).Should().NotMatchRegex(@"POST\s+'fieldsecurityprofiles'",
            "the script reuses the existing BFF-Managed profiles only");

    // ── Parser ───────────────────────────────────────────────────────────────────────────────────────

    private sealed record SchemaShape(
        string? Table, string? Column, string? ColumnSchemaName, string? AttributeType, string? Format,
        string? ReaderProfile, string? WriterProfile, bool Secured,
        (int Create, int Update)? ReaderGrant, (int Create, int Update)? WriterGrant, IReadOnlyList<string> Solutions);

    private static bool Agrees(SchemaShape s) =>
        s.Table == "contact"
        && s.Column == ContactBindingDecision.ExternalInvitedOnColumn
        && s.AttributeType == "DateTime"
        && s.Format == "DateAndTime"
        && s.ReaderProfile == ReaderProfile
        && s.WriterProfile == WriterProfile
        && s.Secured
        && s.ReaderGrant == (0, 0)
        && s.WriterGrant == (4, 4)
        && s.Solutions.Contains("SpaarkeCore") && s.Solutions.Contains("SpaarkeMaster");

    private static SchemaShape Analyse(string script)
    {
        static string? Single(string text, string pattern) =>
            Regex.Match(text, pattern, RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

        static (int, int)? Grant(string text, string profileVariable)
        {
            var m = Regex.Match(text,
                @"Name\s*=\s*\$" + profileVariable + @"\s*;\s*Create\s*=\s*(\d+)\s*;\s*Update\s*=\s*(\d+)");
            return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : null;
        }

        var solutionsLine = Single(script, @"\$SolutionUniqueNames\s*=\s*@\(([^)]*)\)");
        var solutions = solutionsLine is null
            ? Array.Empty<string>()
            : Regex.Matches(solutionsLine, @"'([^']+)'").Select(m => m.Groups[1].Value).ToArray();

        return new SchemaShape(
            Single(script, @"^\s*\$Table\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$Column\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$ColumnSchemaName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$AttributeType\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$Format\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$ReaderProfileName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$WriterProfileName\s*=\s*'([^']+)'"),
            string.Equals(Single(script, @"^\s*\$Secured\s*=\s*\$(\w+)"), "true", StringComparison.OrdinalIgnoreCase),
            Grant(script, "ReaderProfileName"),
            Grant(script, "WriterProfileName"),
            solutions);
    }

    private static string ScriptPath() => Path.Combine(RepoRoot(), "scripts", "Set-ExternalInviteMarkerSchema.ps1");

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

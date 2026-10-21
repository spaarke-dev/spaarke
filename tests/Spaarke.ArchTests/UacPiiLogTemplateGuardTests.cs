using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// #2111 / #2130 (CODE-QUALITY-RUBRIC D9, "no PII in logs"): the unified-access-control log lines and census findings
/// name people by id, never by email, UPN or domain name.
/// </summary>
/// <remarks>
/// <para><b>What it found.</b> <c>[ID-LINK-RECON]</c> logged <c>email={Email}</c> for every enabled interactive
/// systemuser every five minutes (12,082 dev lines in three days); the SPE membership service logged <c>{Email}</c> and a
/// key whose text was the address; and the <c>[SECURE-CENSUS]</c> findings, logged at Critical every fifteen minutes and
/// returned in ResultJson and by the acceptance route, carried each principal's domain name (24,192 dev lines).</para>
///
/// <para><b>Two rules.</b></para>
/// <list type="number">
///   <item>No placeholder or interpolation hole whose name contains <c>Email</c>, <c>Mail</c>, <c>Upn</c>/<c>UPN</c>,
///   <c>UserPrincipalName</c>, <c>DomainName</c> or <c>PrincipalName</c> (<c>{Email}</c>, <c>{UserEmail}</c>,
///   <c>{ContactEmail}</c>, <c>{Mail}</c>, <c>{DomainName}</c>…, with or without a format suffix) in any code line under
///   <c>Services/ExternalAccess</c> or <c>Infrastructure/ExternalAccess</c>. Booleans and counts (<c>{HasEmail}</c>,
///   <c>{IsMailbox}</c>, <c>{EmailCount}</c>) carry no address and are allowed. The <c>[ID-LINK-RECON]</c> and
///   SPE-membership templates, and most <c>[SECURE-CENSUS]</c> and <c>[ID-BIND]</c> ones, live there; but
///   <c>KeylessProofEndpoints.cs</c> and <c>RegistrationDataverseService.cs</c> carry such lines outside this scope.
///   Widening the scope to them is #2134.</item>
///   <item>No interpolation hole in <c>SecureBuRoleDepthAssertion.cs</c> (the census evaluator, which builds the finding
///   messages) that reads a principal's domain name or display name.</item>
/// </list>
///
/// <para><b>Scope, and why there is no ratchet baseline.</b> Both directories are clean after the fix, so the guard
/// holds them at zero. Other BFF folders still log <c>{Email}</c> (the invite endpoints, registration, communication);
/// they are outside this guard's scope rather than baselined, and are reported separately.</para>
///
/// <para><b>Crude by design</b> (<see cref="SourceScan"/>): full-line comments are skipped, nothing else is parsed.
/// The controls prove each rule fires on the shape it bans and stays quiet on the sanctioned one.</para>
///
/// <para><b>Enforced by:</b> this class. MAINTAIN-class structural fitness function (<c>tests/CLAUDE.md</c>).</para>
/// </remarks>
public sealed class UacPiiLogTemplateGuardTests
{
    private static readonly string BffRoot =
        Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");

    /// <summary>The scanned folders, relative to the BFF project.</summary>
    internal static readonly string[] ScannedFolders =
    {
        "Services/ExternalAccess",
        "Infrastructure/ExternalAccess",
    };

    /// <summary>The census evaluator whose finding messages are logged and returned.</summary>
    internal const string CensusEvaluatorFile = "Infrastructure/Dataverse/SecureBuRoleDepthAssertion.cs";

    /// <summary>
    /// A placeholder or hole for an email-shaped value: <c>{Email}</c>, <c>{UserEmail:l}</c>, <c>{ContactEmail,20}</c>,
    /// <c>{Mail}</c>, <c>{Upn}</c>, <c>{DomainName}</c>, <c>{PrincipalName}</c>… A <c>Has*</c> or <c>Is*</c> boolean and a
    /// <c>*Count</c> carry no address and are excluded.
    /// </summary>
    private static readonly Regex PiiPlaceholder = new(
        @"\{(?!(?:Has|Is)[A-Z])(?!\w*Count[,:}])\w*(?:Email|Mail|Upn|UPN|UserPrincipalName|DomainName|PrincipalName)\w*(?:[,:][^}]*)?\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// An interpolation hole reading a principal's domain name or display name: <c>{grant.PrincipalDomainName …}</c>,
    /// <c>{user.DomainName …}</c>, <c>{grant.PrincipalName}</c>, <c>{user.Name}</c>.
    /// </summary>
    private static readonly Regex PrincipalNameHole = new(
        @"\{\s*[\w.]*(?:DomainName|PrincipalName)\b|\{\s*(?:user|member|principal)\.Name\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // ── Pure logic ────────────────────────────────────────────────────────────────────────────

    /// <summary>The 1-based lines of <paramref name="lines"/> matching <paramref name="rule"/>, full-line comments skipped.</summary>
    internal static IReadOnlyList<int> Violations(IReadOnlyList<string> lines, Regex rule)
    {
        var hits = new List<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (rule.IsMatch(lines[i]))
            {
                hits.Add(i + 1);
            }
        }

        return hits;
    }

    private static IEnumerable<string> ScannedFiles() =>
        ScannedFolders
            .Select(folder => Path.Combine(BffRoot, folder.Replace('/', Path.DirectorySeparatorChar)))
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    // ── Real tree ─────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "No UAC log template (Services/ExternalAccess, Infrastructure/ExternalAccess) has an email, mail, UPN, domain-name or principal-name placeholder")]
    public void RealTree_NoPiiPlaceholderInUacLogTemplates()
    {
        var violations = ScannedFiles()
            .SelectMany(file => Violations(File.ReadAllLines(file), PiiPlaceholder)
                .Select(line => $"{Path.GetRelativePath(SourceScan.RepoRoot, file)}:{line}"))
            .ToList();

        Assert.True(violations.Count == 0,
            "Log the systemuser, contact or Entra object id instead of an email/UPN (CODE-QUALITY-RUBRIC D9; #2111, "
            + "#2130): " + string.Join(", ", violations));
    }

    [Fact(DisplayName = "No census finding message interpolates a principal's domain name or display name")]
    public void RealTree_NoPrincipalNameInCensusFindings()
    {
        var path = Path.Combine(BffRoot, CensusEvaluatorFile.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{CensusEvaluatorFile} not found");

        var violations = Violations(File.ReadAllLines(path), PrincipalNameHole);

        Assert.True(violations.Count == 0,
            "Census findings are logged at Critical and returned by the acceptance route: name a user by systemuser id "
            + $"(#2130). {CensusEvaluatorFile} lines: " + string.Join(", ", violations));
    }

    [Fact(DisplayName = "The scan sees the tagged UAC templates (the scanned folders are not empty)")]
    public void RealTree_ScanSeesTheTaggedTemplates()
    {
        var text = string.Join("\n", ScannedFiles().Select(File.ReadAllText));

        Assert.Contains("[ID-LINK-RECON]", text, StringComparison.Ordinal);
        Assert.Contains("[SECURE-CENSUS]", text, StringComparison.Ordinal);
        Assert.Contains("class SpeContainerMembershipService", text, StringComparison.Ordinal);
    }

    // ── Controls ──────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "Control (must fire): an email/UPN placeholder in a log template")]
    [InlineData("\"[ID-LINK-RECON] decision systemuser={SystemUserId} email={Email} outcome={Outcome}\",")]
    [InlineData("\"Granting SPE container membership: containerId={ContainerId}, email={Email:l}\",")]
    [InlineData("\"[EXT] user {Upn} signed in\"")]
    [InlineData("\"[EXT] user {UserPrincipalName,30} signed in\"")]
    [InlineData("\"[SECURE-CENSUS] principal {PrincipalDomainName}\"")]
    [InlineData("\"[EXT] user {UserEmail} signed in\"")]
    [InlineData("\"[ID-BIND] contact {ContactEmail:l} bound\"")]
    [InlineData("\"[EXT] invited {InternalEmail,20}\"")]
    [InlineData("\"[EXT] mailbox {Mail}\"")]
    [InlineData("\"[SECURE-CENSUS] user {DomainName}\"")]
    [InlineData("\"[SECURE-CENSUS] user {PrincipalName}\"")]
    public void Control_PiiPlaceholder_Fires(string sample)
    {
        Assert.Equal([1], Violations([sample], PiiPlaceholder));
    }

    [Theory(DisplayName = "Control (must not fire): id placeholders, look-alike names and comments")]
    [InlineData("\"[ID-LINK-RECON] decision systemuser={SystemUserId} contact={ContactId} oid={Oid}\",")]
    [InlineData("\"sent {EmailCount} message(s) to {EmailAddressCount} recipient(s)\"")]
    [InlineData("\"email present: {HasEmail}\"")]
    [InlineData("\"counted {EmailCount:N0}\"")]
    [InlineData("\"shared mailbox: {IsMailbox}\"")]
    [InlineData("    /// The job used to log email={Email}; it logs ids now.")]
    [InlineData("    // email={Email}")]
    public void Control_PiiPlaceholder_DoesNotFire(string sample)
    {
        Assert.Empty(Violations([sample], PiiPlaceholder));
    }

    [Theory(DisplayName = "Control (must fire): a principal's domain or display name in a census message")]
    [InlineData("$\"'{grant.PrincipalName}' ({grant.PrincipalDomainName ?? \"no domain\"}) reaches the \"")]
    [InlineData("$\"user '{user.Name}' ({user.DomainName ?? \"no domain\"})\"")]
    [InlineData("return $\"{user.Name} ({kind})\";")]
    public void Control_PrincipalNameHole_Fires(string sample)
    {
        Assert.Equal([1], Violations([sample], PrincipalNameHole));
    }

    [Theory(DisplayName = "Control (must not fire): ids, team and business-unit names, and identity matching")]
    [InlineData("$\"SECURE PROJECTS ARE NOT ISOLATED: systemuser {grant.PrincipalId} \"")]
    [InlineData("$\"holds it via team '{grant.HeldViaTeam}', which REACHES '{secureBu.Name}' ({secureBu.Id})\"")]
    [InlineData("$\"team '{team.Name}' (the business unit's DEFAULT team)\"")]
    [InlineData("a.PrincipalDomainName ?? a.PrincipalName,")]
    public void Control_PrincipalNameHole_DoesNotFire(string sample)
    {
        Assert.Empty(Violations([sample], PrincipalNameHole));
    }
}

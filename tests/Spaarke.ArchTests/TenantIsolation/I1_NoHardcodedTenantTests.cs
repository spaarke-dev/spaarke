using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests.TenantIsolation;

/// <summary>
/// Tenant-isolation invariant <b>I1</b> (spec.md FR-28 / design.md §4D I1):
/// no hardcoded default tenant in provisioning scripts. Every script that
/// provisions per-customer must require <c>-TenantId</c> explicitly; no
/// fallback default.
///
/// <para>
/// <b>Severity of the invariant</b>: HIGH. An operator running the
/// provisioning script for a new customer without passing <c>-TenantId</c>
/// would provision the customer's Entra app-reg in the <b>Spaarke tenant</b>,
/// granting Spaarke's users access to the customer's Dataverse env — a
/// cross-tenant identity leak (§4D I1 rationale).
/// </para>
///
/// <para>
/// <b>Scan shape (per POML constraint)</b>: file-level regex over every
/// <c>*.ps1</c> under <c>scripts/</c>. For each script we extract
/// <c>Param(...)</c> parameter blocks and look for a parameter of the shape
/// <c>[string]$TenantId = '&lt;GUID-shape&gt;'</c> — i.e. a string parameter
/// whose DEFAULT value matches
/// <c>^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$</c>.
/// Variable initializers inside function bodies are NOT scanned (per POML
/// constraint — this yields too many false positives from local test
/// variables and script-example strings).
/// </para>
///
/// <para>
/// <b>Compliant shape</b>: <c>[Parameter(Mandatory=$true)][string]$TenantId</c>
/// (no default value), or <c>[string]$TenantId = ''</c> (empty default that
/// forces the operator to pass one).
/// </para>
///
/// <para>
/// Reference fix baseline: commit <c>1834b77bc</c> removed the hardcoded
/// Spaarke default from <c>scripts/Register-EntraAppRegistrations.ps1:63</c>
/// (previously <c>[string]$TenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"</c>).
/// </para>
/// </summary>
public class I1_NoHardcodedTenantTests
{
    private static readonly string RepoRoot = ResolveRepoRoot();

    /// <summary>
    /// Root of the PowerShell scan. All <c>*.ps1</c> under this directory
    /// (recursively) are subject to the I1 predicate.
    /// </summary>
    private const string ScriptsRelDir = "scripts";

    /// <summary>
    /// Directories under <see cref="ScriptsRelDir"/> excluded from the scan:
    /// archived scripts are historical artifacts (not part of the live
    /// provisioning surface) and generated files reflect their template's
    /// shape (whose non-generated source is the authoritative scan target).
    /// </summary>
    private static readonly string[] ExcludedRelDirs =
    {
        "scripts/_archive/",
        "scripts/canonical-secret-catalog/generated/",
    };

    /// <summary>
    /// GUID shape as it would appear in a PowerShell string literal default
    /// value. Case-insensitive so upper- and lower-case hex both match.
    /// Anchored WITHIN quotes below; the outer scanner supplies the quotes
    /// as part of the Param match.
    /// </summary>
    private const string GuidPattern = @"[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}";

    /// <summary>
    /// Matches a single tenant-shaped parameter DEFAULT inside a PowerShell
    /// Param block. Captured groups:
    /// <list type="number">
    ///   <item>Parameter name (drops the leading <c>$</c>).</item>
    ///   <item>GUID literal that was assigned as the default.</item>
    /// </list>
    ///
    /// <para>
    /// Deliberately targets ONLY <c>[string]</c>-typed parameters whose name
    /// contains <c>Tenant</c> case-insensitively — narrower than "any string
    /// parameter with a GUID default" to avoid false positives on non-tenant
    /// IDs (e.g., subscription IDs, resource group IDs, principal IDs) that
    /// legitimately appear as environment-per-env defaults in some ops
    /// scripts.
    /// </para>
    ///
    /// <para>
    /// Multiline / DOTALL so the parameter attribute prefix
    /// (<c>[Parameter(...)]</c>) may span lines and the assignment may be
    /// indented across lines.
    /// </para>
    /// </summary>
    private static readonly Regex TenantIdDefaultInParam = new(
        @"\[string\]\s*\$(\w*[Tt]enant\w*)\s*=\s*['""](" + GuidPattern + @")['""]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>
    /// Matches the boundary of a PowerShell <c>Param(...)</c> block — we only
    /// scan for tenant defaults inside these blocks. Function-body variable
    /// assignments outside Param() are ignored (per POML constraint).
    /// </summary>
    private static readonly Regex ParamBlock = new(
        @"(?ms)\bparam\s*\(",
        RegexOptions.Compiled);

    [Fact(DisplayName = "FR-28/§4D I1: no PowerShell script has a tenant-shaped GUID default on a [string]$*Tenant* Param")]
    public void ProvisioningScripts_HaveNoHardcodedTenantDefault()
    {
        var scriptsDir = Path.Combine(RepoRoot, ScriptsRelDir);
        Assert.True(
            Directory.Exists(scriptsDir),
            $"scripts/ directory not found at '{scriptsDir}'. The I1 ArchTest cannot run without it.");

        var offenders = ScanForI1Offenders(scriptsDir, ExcludedRelDirs);

        Assert.True(
            offenders.Count == 0,
            "§4D I1 violation: PowerShell provisioning script(s) have a hardcoded tenant-shaped GUID " +
            "default on a Param(). Running such a script without an explicit -TenantId would provision " +
            "against the wrong tenant — a cross-tenant identity leak (severity HIGH). Remove the default; " +
            "mark the parameter [Parameter(Mandatory=$true)].\n" +
            $"Offenders:\n{string.Join("\n", offenders.OrderBy(x => x, StringComparer.Ordinal))}");
    }

    /// <summary>
    /// Scans every <c>*.ps1</c> under <paramref name="scanRoot"/> (recursive, skipping
    /// any file whose repo-relative path starts with a prefix in
    /// <paramref name="excludedRelPrefixes"/>) and returns a list of offender strings
    /// in the shape <c>{relPath}:{line} — Param [string]${name} = '{guid}' …</c>.
    ///
    /// <para>
    /// Extracted from <see cref="ProvisioningScripts_HaveNoHardcodedTenantDefault"/>
    /// (task 066) so the scanner can be exercised against arbitrary scan roots — the
    /// regression-seed test <see cref="ScanForI1Offenders_TempScriptWithTenantDefault_ReportsFileAndLine"/>
    /// authors a temporary script under <see cref="Path.GetTempPath"/> and asserts the
    /// scanner catches it end-to-end (proves the predicate catches what it claims —
    /// closes the "does the ArchTest actually see a real offender file, or only a
    /// regex-in-a-string?" negative-control gap task 064 left open).
    /// </para>
    /// </summary>
    private static List<string> ScanForI1Offenders(string scanRoot, string[] excludedRelPrefixes)
    {
        var offenders = new List<string>();
        foreach (var file in EnumerateProvisioningScripts(scanRoot, excludedRelPrefixes))
        {
            var text = File.ReadAllText(file);
            var paramMatches = ParamBlock.Matches(text);
            if (paramMatches.Count == 0) continue;

            foreach (Match paramMatch in paramMatches)
            {
                var paramBlockText = ExtractBalancedParamBlock(text, paramMatch.Index);
                if (paramBlockText is null) continue;

                foreach (Match hit in TenantIdDefaultInParam.Matches(paramBlockText))
                {
                    // Line-number = 1-based line of hit inside the original file.
                    var lineNumber = LineNumberFor(text, paramMatch.Index + hit.Index);
                    var rel = RelPath(file);
                    var paramName = hit.Groups[1].Value;
                    var guid = hit.Groups[2].Value;

                    offenders.Add(
                        $"{rel}:{lineNumber} — Param [string]${paramName} = '{guid}' " +
                        $"(GUID-shaped tenant DEFAULT). Fix: remove the default and mark the parameter " +
                        $"[Parameter(Mandatory=$true)] (or set the default to an empty string). " +
                        $"Reference: spec.md FR-28 / design.md §4D I1; baseline fix commit 1834b77bc.");
                }
            }
        }
        return offenders;
    }

    // -----------------------------------------------------------------------
    // Negative controls — prove the predicate would flag known violations,
    // and prove it does NOT over-match legitimate shapes.
    // (Mirrors the CosmosProvisioningSecretGuardTests convention: coverage
    // of both directions to make silent-weakening regressions visible.)
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR-28 negative control: the tenant-default regex flags a real GUID default")]
    public void TenantIdDefaultRegex_FlagsHardcodedGuidDefault()
    {
        // Simulates the pre-1834b77bc line 63 of Register-EntraAppRegistrations.ps1.
        var badParamBlock =
            "param(\n" +
            "    [Parameter()][string]$TenantId = 'a221a95e-6abc-4434-aecc-e48338a1b2f2',\n" +
            "    [Parameter(Mandatory=$true)][string]$CustomerId\n" +
            ")";
        Assert.True(
            TenantIdDefaultInParam.IsMatch(badParamBlock),
            "Regex should have flagged a Param with a GUID-shaped default on a $Tenant* parameter.");
    }

    [Fact(DisplayName = "FR-28 negative control: regex does NOT flag a Mandatory parameter or a non-tenant GUID default")]
    public void TenantIdDefaultRegex_PermitsCompliantShapes()
    {
        // Compliant: Mandatory=$true with no default.
        var goodParamBlock1 =
            "param(\n" +
            "    [Parameter(Mandatory=$true)][string]$TenantId,\n" +
            "    [Parameter(Mandatory=$true)][string]$CustomerId\n" +
            ")";
        Assert.False(
            TenantIdDefaultInParam.IsMatch(goodParamBlock1),
            "Regex should NOT flag a Mandatory=$true tenant parameter with no default.");

        // Compliant: empty-string default (forces caller to supply explicitly).
        var goodParamBlock2 = "param([string]$TenantId = '')";
        Assert.False(
            TenantIdDefaultInParam.IsMatch(goodParamBlock2),
            "Regex should NOT flag an empty-string default (non-GUID).");

        // Compliant: a subscription-ID default on a non-tenant parameter name
        // is out of scope (regex narrows to $*Tenant* names to avoid false positives).
        var goodParamBlock3 = "param([string]$SubscriptionId = '11111111-2222-3333-4444-555555555555')";
        Assert.False(
            TenantIdDefaultInParam.IsMatch(goodParamBlock3),
            "Regex should NOT flag a non-tenant parameter with a GUID default (out of I1 scope).");
    }

    /// <summary>
    /// Regression-seed / end-to-end negative control (task 066, POML step 6): authors
    /// a temporary <c>.ps1</c> file under <see cref="Path.GetTempPath"/> with a
    /// tenant-shaped GUID default on a <c>[string]$TenantId</c> Param(), runs the
    /// full scanner against JUST the temp directory, asserts the offender is reported
    /// with the correct file name AND line number, and deletes the temp file.
    ///
    /// <para>
    /// Why this exists: the two regex-only negative controls above prove the REGEX
    /// catches the shape in-memory, but they do NOT prove the scanner's end-to-end
    /// wiring (file enumeration, Param() extraction, line-number arithmetic, offender
    /// formatting) actually catches a real file. This test closes that gap by feeding
    /// a real file into <see cref="ScanForI1Offenders"/> — the same code path the main
    /// test uses — and asserting on the offender string the main test would emit if
    /// this file lived under <c>scripts/</c>.
    /// </para>
    ///
    /// <para>
    /// Deliberately placed under <see cref="Path.GetTempPath"/> (not under
    /// <c>scripts/</c>) so the file is invisible to the main test's scan and cannot
    /// pollute the real repo tree if the test process is killed mid-run — the temp
    /// dir carries a unique <see cref="Guid"/> suffix and is deleted in <c>finally</c>.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "FR-28 regression seed: temp .ps1 with tenant-shaped default is detected end-to-end by ScanForI1Offenders (proves the scanner catches a real file, not only an in-memory regex)")]
    public void ScanForI1Offenders_TempScriptWithTenantDefault_ReportsFileAndLine()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "spaarke-i1-regression-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var scriptPath = Path.Combine(tempDir, "SeedRegression.ps1");

            // File layout (line numbers 1-based, matching what LineNumberFor reports):
            //   1: # Seed regression control — NOT a real provisioning script.
            //   2: param(
            //   3:     [Parameter()][string]$TenantId = 'a221a95e-6abc-4434-aecc-e48338a1b2f2'
            //   4: )
            // The tenant-shaped default sits on line 3. This matches the shape the
            // pre-1834b77bc Register-EntraAppRegistrations.ps1:63 offender had.
            File.WriteAllText(
                scriptPath,
                "# Seed regression control - NOT a real provisioning script.\n" +
                "param(\n" +
                "    [Parameter()][string]$TenantId = 'a221a95e-6abc-4434-aecc-e48338a1b2f2'\n" +
                ")\n");

            var offenders = ScanForI1Offenders(tempDir, Array.Empty<string>());

            Assert.Single(offenders);
            var offender = offenders[0];
            Assert.Contains("SeedRegression.ps1", offender);
            Assert.Contains("$TenantId", offender);
            Assert.Contains("a221a95e-6abc-4434-aecc-e48338a1b2f2", offender);
            Assert.Contains(":3 —", offender);
            Assert.Contains("[Parameter(Mandatory=$true)]", offender);
            Assert.Contains("1834b77bc", offender);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IEnumerable<string> EnumerateProvisioningScripts(string scriptsDir, string[] excludedRelPrefixes)
    {
        foreach (var file in Directory.EnumerateFiles(scriptsDir, "*.ps1", SearchOption.AllDirectories))
        {
            var rel = RelPath(file).Replace('\\', '/');
            var excluded = false;
            foreach (var ex in excludedRelPrefixes)
            {
                if (rel.StartsWith(ex, StringComparison.OrdinalIgnoreCase))
                {
                    excluded = true;
                    break;
                }
            }
            if (!excluded) yield return file;
        }
    }

    /// <summary>
    /// Extracts the text between the opening <c>(</c> of a <c>param(...)</c>
    /// block and its matching closing <c>)</c>, tracking paren depth so
    /// nested parentheses (e.g. inside default expressions or attribute
    /// arguments) do not terminate the block prematurely. Returns <c>null</c>
    /// if no balancing paren is found (malformed script).
    /// </summary>
    private static string? ExtractBalancedParamBlock(string source, int paramStart)
    {
        var openParen = source.IndexOf('(', paramStart);
        if (openParen < 0) return null;

        int depth = 1;
        for (int i = openParen + 1; i < source.Length; i++)
        {
            var c = source[i];
            switch (c)
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth == 0)
                    {
                        return source[(openParen + 1)..i];
                    }
                    break;
            }
        }
        return null;
    }

    private static int LineNumberFor(string source, int charIndex)
    {
        if (charIndex < 0 || charIndex > source.Length) return 1;
        int line = 1;
        for (int i = 0; i < charIndex; i++)
        {
            if (source[i] == '\n') line++;
        }
        return line;
    }

    private static string RelPath(string fullPath)
        => fullPath.Replace(RepoRoot + Path.DirectorySeparatorChar, string.Empty)
                   .Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>
    /// Walk from the test binary directory up to the repo root (contains
    /// both <c>src/</c> and <c>tests/</c>). Same convention as
    /// <c>GodClassGuardTests</c> and <c>CosmosProvisioningSecretGuardTests</c>.
    /// </summary>
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                Directory.Exists(Path.Combine(dir.FullName, "tests")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }
}

using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// #2833: a script that reads Dataverse attribute metadata must ask for a strongly consistent read.
///
/// <para>Dataverse caches attribute metadata per server, so a plain Web API read of <c>EntityDefinitions(...)/Attributes(...)</c>
/// soon after a change can return a stale <c>IsSecured</c> (measured on dev 2026-11-14: 5 of 36 plain reads stale, 0 of 36 with
/// <c>Consistency: Strong</c>). A stale read makes <c>-Verify</c> report a secured column as MISSING, or lets a re-run
/// re-apply a change that already landed.</para>
///
/// <para><b>The rule.</b> Every <c>*.ps1</c> / <c>*.psm1</c> under <c>scripts/</c> whose text contains <c>IsSecured</c> and
/// <c>EntityDefinitions(</c> must also contain <c>Consistency</c> (the request header), or load a module beside it
/// (<c>using module .\X.psm1</c> / <c>Import-Module</c>) that does: a script that sends its requests through a shared helper module
/// (e.g. <c>scripts/ebilling/EbillingSchema.psm1</c>) gets the header from there. Textual and file-level on purpose: a script
/// that mentions all three has at least decided about the header; one that does not has not.</para>
///
/// <para><b>Ratchet baseline.</b> <see cref="KnownOffenders"/> lists files that violate the rule and are not fixed yet. It is
/// empty now (the <c>scripts/ebilling/</c> slice landed with #2839). A new violation fails; a baseline entry that no longer
/// violates also fails, so the list only shrinks.</para>
///
/// <para><b>Controls.</b> <see cref="Violates_MustFireAndMustNotFire"/> runs the predicate on in-memory text: it fires on
/// <c>IsSecured</c> + <c>EntityDefinitions(</c> without the header, and does not fire with the header, without <c>IsSecured</c>,
/// or when a referenced module sends the header.</para>
/// </summary>
public class ScriptMetadataStrongConsistencyGuardTests
{
    /// <summary>Files that violate the rule and are not fixed yet. Remove an entry when its script is fixed (the test demands it).</summary>
    private static readonly IReadOnlySet<string> KnownOffenders = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>A module the script loads from its own folder: <c>using module .\X.psm1</c> or <c>Import-Module (Join-Path $PSScriptRoot 'X.psm1')</c> and similar.</summary>
    private static readonly Regex ModuleReference = new(@"(?:using\s+module|Import-Module)[^\r\n]*?([A-Za-z0-9_.-]+\.psm1)", RegexOptions.Compiled);

    /// <summary>
    /// True when the script reads attribute security metadata and neither it nor any module it loads names the Consistency
    /// header. <paramref name="loadModule"/> returns the text of a module referenced by file name, or null when it does not exist. Pure.
    /// </summary>
    internal static bool ViolatesStrongConsistencyRule(string scriptText, Func<string, string?> loadModule)
    {
        if (!scriptText.Contains("IsSecured", StringComparison.Ordinal)
            || !scriptText.Contains("EntityDefinitions(", StringComparison.Ordinal)
            || scriptText.Contains("Consistency", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (Match m in ModuleReference.Matches(scriptText))
        {
            var module = loadModule(m.Groups[1].Value);
            if (module is not null && module.Contains("Consistency", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> Violators()
    {
        var root = SourceScan.RepoRoot;
        var scriptsRoot = Path.Combine(root, "scripts");
        return Directory.EnumerateFiles(scriptsRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".psm1", StringComparison.OrdinalIgnoreCase))
            .Where(f => ViolatesStrongConsistencyRule(File.ReadAllText(f), name =>
            {
                var path = Path.Combine(Path.GetDirectoryName(f)!, name);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    [Fact(DisplayName = "#2833: scripts that read attribute IsSecured via EntityDefinitions send Consistency: Strong (known offenders ratcheted)")]
    public void ScriptsReadingAttributeMetadataSendStrongConsistency()
    {
        var violators = Violators();

        // Non-vacuity: the shared engine is a script that does read IsSecured through EntityDefinitions and passes the rule.
        var engine = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "scripts", "Set-DocumentPointerFieldSecurity.ps1"));
        Assert.Contains("EntityDefinitions(", engine);
        Assert.Contains("IsSecured", engine);
        Assert.False(ViolatesStrongConsistencyRule(engine, _ => null));

        var newViolations = violators.Where(v => !KnownOffenders.Contains(v)).ToList();
        Assert.True(newViolations.Count == 0,
            "These scripts read attribute metadata (IsSecured via EntityDefinitions(...)) without a Consistency header. " +
            "Dataverse caches attribute metadata per server, so a plain read can return a stale IsSecured (#2833). Add " +
            "Consistency = 'Strong' to the Dataverse request headers (see scripts/Set-DocumentPointerFieldSecurity.ps1), or " +
            "send the requests through a helper module beside the script that does:\n  " +
            string.Join("\n  ", newViolations));

        var fixedEntries = KnownOffenders.Where(k => !violators.Contains(k, StringComparer.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(fixedEntries.Count == 0,
            "These baseline entries no longer violate the rule (or no longer exist): remove them from KnownOffenders so the " +
            "ratchet only shrinks:\n  " + string.Join("\n  ", fixedEntries));
    }

    [Fact(DisplayName = "#2833 guard control: the predicate fires without the header and does not fire with it, without IsSecured, or via a module that sends it")]
    public void Violates_MustFireAndMustNotFire()
    {
        const string Read = "Invoke-RestMethod -Uri \"$Api/EntityDefinitions(LogicalName='sprk_x')/Attributes(LogicalName='sprk_y')?$select=IsSecured\" -Headers $headers";
        static string? NoModules(string _) => null;

        // Must fire: reads IsSecured through EntityDefinitions( with no Consistency header anywhere.
        Assert.True(ViolatesStrongConsistencyRule(Read, NoModules));

        // Must not fire: the same script with the header.
        Assert.False(ViolatesStrongConsistencyRule("$headers = @{ Consistency = 'Strong' }\n" + Read, NoModules));

        // Must not fire: EntityDefinitions( without IsSecured (the rule is about the secured flag only).
        Assert.False(ViolatesStrongConsistencyRule("Invoke-RestMethod -Uri \"$Api/EntityDefinitions(LogicalName='sprk_x')?$select=LogicalName\"", NoModules));

        // Must not fire: IsSecured without EntityDefinitions( (not a metadata read).
        Assert.False(ViolatesStrongConsistencyRule("$col.IsSecured = $true", NoModules));

        // Module references: a helper module that sends the header satisfies the rule; one that does not, or a missing one, does not.
        const string ViaModule = "using module .\\Helper.psm1\n" + Read;
        Assert.False(ViolatesStrongConsistencyRule(ViaModule, n => n == "Helper.psm1" ? "$headers = @{ Consistency = 'Strong' }" : null));
        Assert.True(ViolatesStrongConsistencyRule(ViaModule, n => n == "Helper.psm1" ? "$headers = @{ Accept = 'application/json' }" : null));
        Assert.True(ViolatesStrongConsistencyRule(ViaModule, NoModules));
        Assert.False(ViolatesStrongConsistencyRule(
            "Import-Module (Join-Path $PSScriptRoot 'Helper.psm1') -Force\n" + Read,
            n => n == "Helper.psm1" ? "Consistency = 'Strong'" : null));
    }
}

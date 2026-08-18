using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests.TenantIsolation;

/// <summary>
/// Tenant-isolation invariant <b>I4</b> (spec.md FR-31 / design.md §4D I4):
/// SPE container IDs are always tenant-scoped-derived (from the current
/// tenant's env-var <c>sprk_SharePointEmbeddedContainerId</c> or KV secret
/// <c>customer-{customerId}-spe-container-id</c>) — NEVER a fallback default
/// or hard-coded string literal.
///
/// <para>
/// <b>Severity</b>: CATASTROPHIC. A fallback-default SPE container ID would
/// route a customer's file uploads into another customer's SPE container —
/// privileged documents into wrong hands (§4D I4 rationale).
/// </para>
///
/// <para>
/// <b>Scan shape</b>: file-level regex over <c>src/server/api/Sprk.Bff.Api/Services/**/*.cs</c>
/// for any string literal matching the SPE container-ID shape:
/// <c>b!</c> followed by 20+ URL-safe base64 characters (canonical Graph SPE
/// container-ID format, see
/// <see href="https://learn.microsoft.com/graph/api/resources/filestoragecontainer"/>).
/// Constants derived from <c>IOptions</c> bags or the
/// <c>ITenantContainerResolver</c> service ARE compliant — those are
/// per-tenant / per-request lookups; only inline string LITERALS are
/// forbidden.
/// </para>
///
/// <para>
/// <b>Compliant lookup pattern</b>:
/// <code>
/// var containerId = await _tenantContainerResolver.ResolveAsync(tenantId, ct);
/// // or:
/// var containerId = _speOptions.ContainerId;   // bound from KV / env at boot
/// </code>
/// </para>
/// </summary>
public class I4_SpeContainerIdLiteralTests
{
    private static readonly string RepoRoot = ResolveRepoRoot();
    private const string ScanRelDir = "src/server/api/Sprk.Bff.Api/Services";

    /// <summary>
    /// Matches a suspected SPE container-ID literal (Graph
    /// <c>filestoragecontainer</c> ID format):
    /// <list type="bullet">
    ///   <item>Prefix: <c>b!</c> (canonical Graph resource-ID prefix)</item>
    ///   <item>Body: 20 or more URL-safe base64 characters
    ///     (<c>[A-Za-z0-9_-]</c>) — real container IDs are ~72 chars but 20+
    ///     is a safe lower bound that still avoids matching short prefix-like
    ///     substrings.</item>
    ///   <item>Anchored inside a C# string literal (<c>"..."</c>) with word
    ///     boundaries on the shape to avoid matching e.g. a base64 blob body
    ///     that happens to start with <c>b!</c> — the surrounding quote
    ///     characters supply the anchoring below.</item>
    /// </list>
    /// </summary>
    private static readonly Regex SpeContainerIdLiteral = new(
        "\"b![A-Za-z0-9_-]{20,}\"",
        RegexOptions.Compiled);

    /// <summary>
    /// Files under <see cref="ScanRelDir"/> excluded from the I4 predicate.
    /// The list is intentionally kept minimal — an SPE literal should NEVER
    /// be committed to production code.
    /// </summary>
    private static readonly HashSet<string> ExcludedFileRelPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        // Currently empty. Any future exception requires an inline rationale.
    };

    [Fact(DisplayName = "FR-31/§4D I4: no SPE container-ID string literal ('b!...') in BFF Services/**")]
    public void BffServices_HaveNoSpeContainerIdLiteral()
    {
        var scanRoot = Path.Combine(RepoRoot, ScanRelDir);
        Assert.True(
            Directory.Exists(scanRoot),
            $"{ScanRelDir} directory not found at '{scanRoot}'. The I4 ArchTest cannot run without it.");

        var offenders = new List<string>();
        foreach (var file in EnumerateProductionCsFiles(scanRoot))
        {
            var rel = RelPath(file);
            if (ExcludedFileRelPaths.Contains(rel)) continue;

            var text = File.ReadAllText(file);
            foreach (Match m in SpeContainerIdLiteral.Matches(text))
            {
                var lineNumber = LineNumberFor(text, m.Index);
                var literal = m.Value;
                // Truncate to protect an eventual real container-ID from being
                // fully echoed into CI logs (defense-in-depth on the failure
                // message itself; the real value should never appear here).
                var displayed = literal.Length <= 30
                    ? literal
                    : $"{literal.Substring(0, 20)}...{literal[^6..]}";
                offenders.Add(
                    $"{rel}:{lineNumber} — SPE container-ID literal {displayed} " +
                    $"(shape 'b!' + 20+ url-safe chars) in BFF service. Fix: resolve the container ID " +
                    $"per-tenant via `ITenantContainerResolver` (preferred) or read it from an IOptions bag " +
                    $"whose value is bound from the customer's KV secret / Dataverse env-var at boot. " +
                    $"Reference: spec.md FR-31 / design.md §4D I4.");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "§4D I4 violation: SPE container-ID string literal(s) in BFF Services/**. A fallback-default " +
            "container ID routes a customer's SPE uploads to another customer's container — CATASTROPHIC " +
            "(privileged docs in wrong hands). Resolve container IDs per-tenant via ITenantContainerResolver.\n" +
            $"Offenders:\n{string.Join("\n", offenders.OrderBy(x => x, StringComparer.Ordinal))}");
    }

    // -----------------------------------------------------------------------
    // Negative controls
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR-31 negative control: regex flags a realistic SPE container-ID literal shape")]
    public void SpeContainerIdRegex_FlagsRealisticLiteral()
    {
        // Realistic Graph SPE container-ID shape (fabricated for the test —
        // not a real container). ~68 chars of url-safe base64.
        var sample = "\"b!" + new string('a', 20) + "-_" + new string('B', 20) + "\"";
        Assert.True(
            SpeContainerIdLiteral.IsMatch(sample),
            $"Regex should have flagged the SPE container-ID literal shape (sample length {sample.Length}).");
    }

    [Fact(DisplayName = "FR-31 negative control: regex does NOT over-match short 'b!' prefixes or non-string mentions")]
    public void SpeContainerIdRegex_PermitsBenignShapes()
    {
        // Too short — 15 chars after b! is below the 20-char lower bound.
        Assert.False(
            SpeContainerIdLiteral.IsMatch("\"b!abcdef1234567890\""),
            "Regex should NOT flag a short pseudo-ID (below the 20-char length threshold).");

        // Not inside a string literal — a comment or identifier reference.
        Assert.False(
            SpeContainerIdLiteral.IsMatch("// b!ABCDEFGHIJKLMNOPQRSTUVWXYZ (an SPE container id shape mention in a comment)"),
            "Regex should NOT flag a bare 'b!...' mention outside of C# string quotes.");

        // Prefix in a longer literal that isn't the SPE shape.
        Assert.False(
            SpeContainerIdLiteral.IsMatch("\"the container id starts with b!...\""),
            "Regex should NOT flag an English-language string that describes the SPE shape.");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IEnumerable<string> EnumerateProductionCsFiles(string root)
        => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

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

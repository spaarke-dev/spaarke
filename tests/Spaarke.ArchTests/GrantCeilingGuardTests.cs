using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 139 (WP-1, owner Q1): no grant is persisted without a stated grantor ceiling.
/// </summary>
/// <remarks>
/// <para>The grantor ceiling, the never-lower rule and the write-time No Access / access-policy refusals are enforced
/// INSIDE the one grant-writing core, <c>GrantExternalAccessEndpoint.CreateGrantAsync</c>, which takes the ceiling as
/// a REQUIRED parameter. These guards keep it that way:</para>
/// <list type="number">
/// <item>Exactly one server file writes a grant's access level — the core. A second writer would persist grants
/// around every check.</item>
/// <item>The core's <c>GrantCeiling ceiling</c> parameter is non-nullable with no default, so "no ceiling" cannot be
/// expressed by omission.</item>
/// <item>Every call of the core supplies one.</item>
/// <item><c>GrantCeiling</c> has no non-private constructor: a new writer adds a NAMED, documented factory (task
/// 140's contact grantor, task 142's Assigned-To Collaborate) instead of inventing a ceiling inline.</item>
/// </list>
/// <para><b>Crude by design</b> (see <see cref="SourceScan"/>): text, not syntax. Each rule was proven to bite by
/// seeding its violation and watching it go red before the guard was committed (task 139 notes).</para>
/// </remarks>
public class GrantCeilingGuardTests
{
    private const string CoreFile = "src/server/api/Sprk.Bff.Api/Api/ExternalAccess/GrantExternalAccessEndpoint.cs";
    private const string CeilingTypeFile = "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalGrantLifecycle.cs";

    private static string RelativePath(string fullPath) =>
        Path.GetRelativePath(SourceScan.RepoRoot, fullPath).Replace('\\', '/');

    private static string CodeOf(string relativePath) =>
        SourceScan.CodeText(File.ReadAllLines(Path.Combine(SourceScan.RepoRoot, relativePath)));

    /// <summary>An assignment or initializer of the grant's level column: <c>["sprk_accesslevel"] = …</c>.</summary>
    private static readonly Regex AccessLevelWrite = new(@"\[\s*""sprk_accesslevel""\s*\]\s*=(?!=)", RegexOptions.CultureInvariant);

    [Fact(DisplayName = "Task 139 (WP-1): only the grant core writes a grant's access level")]
    public void OnlyTheCoreWritesTheGrantAccessLevel()
    {
        var writers = SourceScan.ServerSourceFiles()
            .Where(f => AccessLevelWrite.IsMatch(SourceScan.CodeText(File.ReadAllLines(f))))
            .Select(RelativePath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            writers.Count == 1 && writers[0] == CoreFile,
            "every grant must be written through GrantExternalAccessEndpoint.CreateGrantAsync, which applies the "
            + "grantor ceiling, never-lower and the No Access / access-policy refusals (task 139, WP-1). Call the core "
            + "instead of writing sprk_accesslevel."
            + $"{Environment.NewLine}  expected: {CoreFile}"
            + $"{Environment.NewLine}  found   : {string.Join(", ", writers)}");
    }

    [Fact(DisplayName = "Task 139 (WP-1): the grant core takes a REQUIRED, non-nullable GrantCeiling")]
    public void TheCoreRequiresACeiling()
    {
        var code = CodeOf(CoreFile);
        var declaration = Regex.Match(
            code, @"Task<GrantUpsertOutcome>\s+CreateGrantAsync\s*\((?<params>[^)]*)\)", RegexOptions.Singleline);

        Assert.True(declaration.Success, "CreateGrantAsync's declaration was not found in " + CoreFile);

        var parameters = declaration.Groups["params"].Value;
        Assert.Matches(new Regex(@"\bGrantCeiling\s+ceiling\s*,"), parameters);
        Assert.DoesNotMatch(new Regex(@"GrantCeiling\s*\?"), parameters);
        Assert.DoesNotMatch(new Regex(@"\bceiling\s*="), parameters);
    }

    [Fact(DisplayName = "Task 139 (WP-1): every call of the grant core supplies a ceiling")]
    public void EveryCallOfTheCoreSuppliesACeiling()
    {
        var missing = new List<string>();
        var calls = 0;

        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var code = SourceScan.CodeText(File.ReadAllLines(file));
            foreach (Match call in Regex.Matches(code, @"\bCreateGrantAsync\s*\(", RegexOptions.CultureInvariant))
            {
                // The declaration is the one occurrence preceded by its return type; everything else is a call.
                var before = code[..call.Index];
                if (Regex.IsMatch(before, @"Task<GrantUpsertOutcome>\s+$"))
                    continue;

                calls++;
                var arguments = ArgumentsAt(code, call.Index + call.Length);
                if (!Regex.IsMatch(arguments, @"\bceiling\b|\bGrantCeiling\.", RegexOptions.IgnoreCase))
                    missing.Add($"{RelativePath(file)}:{SourceScan.LineOf(code, call.Index)}");
            }
        }

        Assert.True(calls > 0, "no call of CreateGrantAsync was found — the scan is not seeing the source");
        Assert.True(
            missing.Count == 0,
            "a grant written without a stated ceiling bypasses the owner's 'cap every grant at the grantor's own level' "
            + "(task 139, WP-1). Pass GrantCeiling.FromGrantorRights(...) or a named factory for your writer."
            + $"{Environment.NewLine}  calls without a ceiling: {string.Join(", ", missing)}");
    }

    [Fact(DisplayName = "Task 139 (WP-1): a GrantCeiling is built only through named factories")]
    public void GrantCeilingHasNoNonPrivateConstructor()
    {
        var code = CodeOf(CeilingTypeFile);

        Assert.Contains("sealed class GrantCeiling", code, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"\bprivate\s+GrantCeiling\s*\("), code);
        Assert.DoesNotMatch(new Regex(@"\b(public|internal|protected)\s+GrantCeiling\s*\("), code);
        Assert.DoesNotMatch(new Regex(@"\b(record|struct)\s+GrantCeiling\b"), code);
    }

    /// <summary>The text of a call's argument list, from just after its opening parenthesis to the matching one.</summary>
    private static string ArgumentsAt(string code, int start)
    {
        var depth = 1;
        for (var i = start; i < code.Length; i++)
        {
            if (code[i] == '(') depth++;
            else if (code[i] == ')' && --depth == 0) return code[start..i];
        }

        return code[start..];
    }
}

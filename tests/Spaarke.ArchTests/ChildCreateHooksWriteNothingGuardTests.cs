using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ISS-010 part C (K2): the decision commit's access window keeps record-access answers across a <c>ChildCreateExecutorBase</c>
/// step, which is sound only because every ask comes before the step's ONE write (<c>CreateChildAsync</c> in the sealed
/// <c>WriteAsync</c>). The two hooks that still run inside that step, <c>ExtendColumnsAsync</c> and <c>FileUnderAsync</c>, are
/// virtual and their subclasses hold <c>IGenericEntityService</c> (which has write members), so a hook that wrote would put a
/// write between the asks and the window's kept answers. This guard: the overrides of those two hooks in
/// <c>ChildCreateExecutorBase</c> subclasses call no create, update, upsert, delete, associate, disassociate, bulk, post, patch,
/// execute or write member.
/// </summary>
/// <remarks>
/// <b>Signal and limits.</b> A source scan of the code text (line comments stripped) of every <c>src/server/**</c> class that
/// derives from <c>ChildCreateExecutorBase</c>, over the body of each such override. A write reached through a helper the hook calls
/// (a method on another type) is not seen: the hooks read through <c>Entities</c> and the option-source collaborator only, and a
/// helper that wrote would itself be reviewed as a write path. Enforced by: this guard (ArchTests in CI and the Stop hook).
/// </remarks>
public class ChildCreateHooksWriteNothingGuardTests
{
    private static readonly Regex SubclassDeclaration = new(
        @"\bclass\s+(?<name>\w+)\b[^{;]*:\s*(?:[\w.]+\s*,\s*)*ChildCreateExecutorBase\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HookOverride = new(
        @"\boverride\s+[^(;{=]*?\b(?<hook>ExtendColumnsAsync|FileUnderAsync)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A member CALL whose name starts like a write (a constant such as DecisionActionReasons.WriteFailed is not a call): IGenericEntityService / IDataverseUserClient / the route cores.</summary>
    private static readonly Regex WriteMember = new(
        @"\.\s*(?<member>(?:Create|Update|Upsert|Delete|Associate|Disassociate|BulkUpdate|Post|Patch|ExecuteTransaction|Execute|Write)\w*)\s*(?:<[^>()]*>)?\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The hooks scanned and every write member called inside them, over <paramref name="source"/>.</summary>
    internal static (int HooksScanned, List<string> Findings) Scan(string source)
    {
        var code = SourceScan.CodeText(source.Split('\n'));
        var hooks = 0;
        var findings = new List<string>();
        foreach (Match declaration in SubclassDeclaration.Matches(code))
        {
            var bodyStart = code.IndexOf('{', declaration.Index + declaration.Length - 1);
            if (bodyStart < 0)
                continue;

            var classBody = code[bodyStart..(MatchingBrace(code, bodyStart) + 1)];
            foreach (Match hook in HookOverride.Matches(classBody))
            {
                hooks++;
                var body = HookBody(classBody, hook.Index + hook.Length - 1);
                foreach (Match write in WriteMember.Matches(body))
                    findings.Add($"{declaration.Groups["name"].Value}.{hook.Groups["hook"].Value} calls .{write.Groups["member"].Value}");
            }
        }

        return (hooks, findings);
    }

    /// <summary>The body of the method whose parameter list opens at <paramref name="openParen"/>: a block, or an expression body to its <c>;</c>.</summary>
    private static string HookBody(string text, int openParen)
    {
        var depth = 0;
        var i = openParen;
        for (; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) break;
        }

        for (i++; i < text.Length; i++)
        {
            if (text[i] == '{')
                return text[i..(MatchingBrace(text, i) + 1)];
            if (text[i] == '=' && i + 1 < text.Length && text[i + 1] == '>')
                return text[i..(text.IndexOf(';', i) + 1)];
            if (text[i] == ';')
                return string.Empty; // abstract or partial declaration
        }

        return string.Empty;
    }

    private static int MatchingBrace(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return i;
        }

        return text.Length - 1;
    }

    [Fact(DisplayName = "ISS-010 K2: no ChildCreateExecutorBase hook override writes (must not fire on today's code, and scans the real hooks)")]
    public void TheHooksOfTheChildCreateExecutors_WriteNothing()
    {
        var hooks = 0;
        var findings = new List<string>();
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var source = File.ReadAllText(file);
            if (!source.Contains("ChildCreateExecutorBase", StringComparison.Ordinal))
                continue;

            var (scanned, found) = Scan(source);
            hooks += scanned;
            findings.AddRange(found.Select(f => $"{SourceScan.Relative(file).Replace('\\', '/')}: {f}"));
        }

        Assert.True(hooks >= 2, $"The scan saw {hooks} hook override(s); the budget executor alone has two, so the scan lost its signal.");
        Assert.True(findings.Count == 0,
            "A ChildCreateExecutorBase hook (ExtendColumnsAsync, FileUnderAsync) runs inside the step whose record-access answers the "
            + "decision commit's access window keeps; it must read only, because the step's one write is the sealed WriteAsync's "
            + "CreateChildAsync (ISS-010 part C):\n" + string.Join("\n", findings));
    }

    [Fact(DisplayName = "ISS-010 K2 control (must fire): a hook that writes through Entities, the user client or the route cores is found")]
    public void Control_AHookThatWrites_IsFound()
    {
        const string violation = """
            public sealed class SneakyExecutor : ChildCreateExecutorBase
            {
                protected override async Task<DecisionActionOutcome?> ExtendColumnsAsync(
                    DecisionActionRequest request, IDictionary<string, object?> payload, CancellationToken ct)
                {
                    await Entities.UpdateAsync("sprk_matter", Guid.Empty, new Dictionary<string, object>(), ct);
                    return null;
                }

                protected override Task<DecisionActionOutcome?> FileUnderAsync(DecisionCoreRecord core, IDictionary<string, object?> payload, CancellationToken ct) =>
                    _cores.CreateChildAsync(null, "sprk_todo", "{}", ct);
            }
            """;

        var (hooks, findings) = Scan(violation);

        Assert.Equal(2, hooks);
        Assert.Equal(["SneakyExecutor.ExtendColumnsAsync calls .UpdateAsync", "SneakyExecutor.FileUnderAsync calls .CreateChildAsync"], findings);
    }

    [Fact(DisplayName = "ISS-010 K2 control (must not fire): reads in a hook, and a write outside the hooks, are not findings")]
    public void Control_AReadOnlyHook_AndAWriteElsewhere_AreNotFindings()
    {
        const string fine = """
            public sealed class ReadOnlyExecutor : ChildCreateExecutorBase
            {
                protected override async Task<DecisionActionOutcome?> ExtendColumnsAsync(
                    DecisionActionRequest request, IDictionary<string, object?> payload, CancellationToken ct)
                {
                    var set = await Entities.GetEntitySetNameAsync("sprk_matter", ct);
                    payload["a@odata.bind"] = $"/{set}({Guid.Empty:D})";
                    if (set is null) return DecisionActionOutcome.Failed(Code, DecisionActionReasons.WriteFailed, "x");
                    return null;
                }

                protected override BuildColumnsSomething Other() { Entities.UpdateAsync("x", Guid.Empty, null!, default); }
            }
            """;

        var (hooks, findings) = Scan(fine);

        Assert.Equal(1, hooks);
        Assert.Empty(findings);
    }
}

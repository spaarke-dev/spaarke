using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// #2658: no client component renders a modal as <c>{cond &amp;&amp; &lt;XModal open /&gt;}</c> and closes it by
/// clearing <c>cond</c>.
///
/// <para><b>The defect.</b> Fluent's focus manager (tabster) hides everything outside an open modal <c>Dialog</c> with
/// <c>aria-hidden</c>. Closing the Dialog with <c>open={false}</c> exposes the page again. Unmounting it while it is open,
/// or changing its <c>key</c> while it is open, does not: the page stays hidden from assistive technology until reload.
/// <c>SprkModal</c> wraps that Dialog, so every Spaarke modal inherits this. The mechanism is proven in
/// <c>src/client/shared/Spaarke.UI.Components/src/components/SprkModal/__tests__/closeNotUnmount.test.tsx</c>.
/// Nine sites had the shape. The first (FamilyPage) surfaced as quality alert #1906; #2658 fixed the other eight, one
/// of which (ComposeCommentGutter, a bare Dialog behind a ternary) only this guard found.</para>
///
/// <para><b>What the guard matches.</b> A JSX element rendered behind <c>&amp;&amp;</c> or a ternary <c>?</c> whose
/// <c>open</c> attribute is literally true (bare <c>open</c> or <c>open={true}</c>). That is the shape of "open while
/// mounted, closed by unmounting". It cannot see <c>{cond &amp;&amp; &lt;X open={isOpen} /&gt;}</c> where <c>cond</c>
/// can fall while <c>isOpen</c> is true; review catches that.</para>
///
/// <para><b>The sanctioned shape</b> (every fixed site): keep the modal's content in state after it closes, render
/// <c>&lt;X key={seq} open={isOpen} /&gt;</c>, close with <c>open={false}</c>, and change <c>seq</c> only when opening
/// from closed.</para>
///
/// <para>Enforcement ladder (root CLAUDE.md §16): a lint rule would be the stronger rung, but only 2 of the affected
/// packages have an ESLint config. This source scan covers every <c>.tsx</c> under <c>src/</c>. Baseline: zero; there is
/// no allowlist.</para>
/// </summary>
public class ModalUnmountedWhileOpenGuardTests
{
    // =============================================================================================
    // MAINTENANCE PROCEDURE
    //
    //   1. A failure names a file and line. Do not suppress it. Rewrite the site in the sanctioned shape above; the
    //      eight #2658 sites are worked examples (e.g. src/client/external-spa/src/components/detail/RecordOpener.tsx).
    //
    //   2. If the element is not a modal at all (a component with an unrelated boolean prop named `open`), rename the
    //      prop rather than add an exemption. There is deliberately no allowlist: the baseline is zero.
    //
    //   3. If closeNotUnmount.test.tsx's "unmounted while open leaves the page hidden" case starts failing, a
    //      Fluent/tabster upgrade fixed the leak. This guard can then be retired, in the same PR as that test.
    // =============================================================================================

    private static readonly string[] PrunedDirectories =
    {
        "node_modules", "dist", "build", "out", "obj", "bin", "coverage", ".vite", "__tests__", "__mocks__",
    };

    /// <summary>The start of a JSX element rendered conditionally: after <c>&amp;&amp;</c> or a ternary <c>?</c>.</summary>
    private static readonly Regex ConditionalElement = new(
        @"(?:&&|(?<![?.])\?(?![?.]))\s*\(?\s*<(?<name>[A-Z][\w.]*)",
        RegexOptions.Compiled);

    /// <summary>An <c>open</c> attribute that is literally true, in an opening tag whose braced values are collapsed.</summary>
    private static readonly Regex LiteralTrueOpen = new(
        @"(?<![\w-])open(?:=\{true\})?(?=\s|/|$)",
        RegexOptions.Compiled);

    [Fact(DisplayName = "#2658: no modal in src/ is rendered behind a condition with a literal-true open")]
    public void NoModalIsClosedByUnmountingItWhileOpen()
    {
        var violations = ClientTsxFiles()
            .SelectMany(file => FindViolations(File.ReadAllText(file))
                .Select(v => $"{SourceScan.Relative(file)}:{v}"))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "A modal is rendered as {cond && <X open />} (or behind a ternary) and closed by unmounting it. A Fluent "
            + "Dialog unmounted while open leaves the rest of the page aria-hidden until reload (#2658). Keep the modal "
            + "mounted: <X key={seq} open={isOpen} />, close it with open={false}, and change seq only when opening "
            + "from closed. See the MAINTENANCE PROCEDURE in ModalUnmountedWhileOpenGuardTests.cs.\n\n"
            + string.Join("\n", violations));
    }

    // =============================================================================================
    // Controls
    // =============================================================================================

    [Fact(DisplayName = "#2658: negative control — every conditional literal-true open shape is reported")]
    public void Detector_NegativeControl_FiresOnEveryShape()
    {
        Assert.Single(FindViolations("{doc && <VersionHistoryModal open={true} onClose={() => setDoc(null)} />}"));
        Assert.Single(FindViolations(
            "{detail && (\n  <RecordDetailModal\n    open\n    onClose={handleClose}\n    ids={detail.ids}\n  />\n)}"));
        Assert.Single(FindViolations("{ActiveWizard && (\n  <ActiveWizard open onClose={() => setType(null)} />\n)}"));
        Assert.Single(FindViolations("{showIt ? <ConfirmModal open title=\"x\" /> : null}"));
        // The shape ComposeCommentGutter had: a bare Fluent Dialog behind a ternary.
        Assert.Single(FindViolations(
            "{pending ? (\n  <Dialog open onOpenChange={(_e, data) => (!data.open ? cancel() : undefined)}>\n) : null}"));
        // An arrow function's `=>` inside an attribute must not end the tag before `open` is seen.
        Assert.Single(FindViolations("{x && <Pane onClose={() => close()} open />}"));
    }

    [Fact(DisplayName = "#2658: positive control — the sanctioned shape and look-alikes are not reported")]
    public void Detector_PositiveControl_StaysSilentOnTheSanctionedShape()
    {
        // The sanctioned shape: mounted behind the content condition, open bound to state.
        Assert.Empty(FindViolations("{shown && <RecordDetailModal key={shown.seq} open={shown.open} onClose={close} />}"));
        // Not conditional at all.
        Assert.Empty(FindViolations("<SprkModal open onClose={noop} title=\"Probe\" />"));
        // `open` used inside another attribute's value, and attributes that merely contain the word.
        Assert.Empty(FindViolations(
            "{(total > 0 || row) && (\n  <Button icon={open ? <Down /> : <Right />} aria-expanded={open}>x</Button>\n)}"));
        Assert.Empty(FindViolations("{c && <Chip data-open aria-open=\"true\" />}"));
        // The word inside a string attribute (ConsumerChips' "More suggestions — open the capability library").
        Assert.Empty(FindViolations("{onMore && (\n  <Button aria-label=\"More suggestions — open the library\" onClick={onMore}>\n)}"));
        // A bare Fluent Dialog kept mounted and bound to state (the ComposeCommentGutter fix).
        Assert.Empty(FindViolations("<Dialog\n  open={pending !== null}\n  onOpenChange={(_e, d) => (!d.open ? cancel() : undefined)}\n>"));
        // Commented-out code.
        Assert.Empty(FindViolations("// {c && <XModal open />}\n/* {c && <YModal open={true} />} */"));
        // Optional chaining and nullish coalescing are not ternaries.
        Assert.Empty(FindViolations("{a?.b ?? <Fallback open />}"));
    }

    [Fact(DisplayName = "#2658: negative control — the scan reaches the client trees and skips build output")]
    public void Scan_NegativeControl_ReachesTheClientSource()
    {
        var files = ClientTsxFiles().Select(SourceScan.Relative).ToList();

        Assert.Contains(files, f => f.EndsWith("RecordOpener.tsx", StringComparison.Ordinal));
        Assert.Contains(files, f => f.EndsWith("DailyBriefingApp.tsx", StringComparison.Ordinal));
        Assert.Contains(files, f => f.EndsWith("DocumentUploadWizardDialog.tsx", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.Contains("node_modules", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.Contains("__tests__", StringComparison.Ordinal));
    }

    // =============================================================================================
    // Machinery: source analysis only
    // =============================================================================================

    /// <summary>Every <c>.tsx</c> under <c>src/</c>, excluding tests, stories and build or dependency folders.</summary>
    private static IEnumerable<string> ClientTsxFiles()
    {
        var pending = new Stack<string>();
        pending.Push(Path.Combine(SourceScan.RepoRoot, "src"));

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                if (!PrunedDirectories.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
                {
                    pending.Push(sub);
                }
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.tsx"))
            {
                var name = Path.GetFileName(file);
                if (!name.EndsWith(".test.tsx", StringComparison.OrdinalIgnoreCase)
                    && !name.EndsWith(".spec.tsx", StringComparison.OrdinalIgnoreCase)
                    && !name.EndsWith(".stories.tsx", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    /// <summary>Violations in one file's text, as <c>line: &lt;Name&gt;</c> strings.</summary>
    private static List<string> FindViolations(string source)
    {
        var text = StripComments(source);
        var found = new List<string>();

        foreach (Match match in ConditionalElement.Matches(text))
        {
            var attributes = TopLevelAttributes(text, match.Index + match.Length);
            if (LiteralTrueOpen.IsMatch(attributes))
            {
                found.Add($"{SourceScan.LineOf(text, match.Index)}: <{match.Groups["name"].Value} open> rendered behind a condition");
            }
        }

        return found;
    }

    /// <summary>
    /// The opening tag's attribute text from <paramref name="start"/> to its closing <c>&gt;</c>, with each top-level
    /// <c>{…}</c> value collapsed to <c>{}</c>, except <c>{true}</c>, which is kept. Braces and quotes are tracked, so a
    /// <c>&gt;</c> inside <c>{() =&gt; x}</c> or a string does not end the tag.
    /// </summary>
    private static string TopLevelAttributes(string text, int start)
    {
        var result = new StringBuilder();
        var braced = new StringBuilder();
        var depth = 0;
        char quote = '\0';

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            // String contents are dropped (an aria-label "… open the library" is not an `open` attribute); the quotes are
            // kept so the attribute's shape survives.
            if (quote != '\0')
            {
                if (c == quote)
                {
                    (depth > 0 ? braced : result).Append(c);
                    quote = '\0';
                }
                continue;
            }

            if (c is '"' or '\'' or '`')
            {
                quote = c;
                (depth > 0 ? braced : result).Append(c);
                continue;
            }

            if (c == '{')
            {
                depth++;
                if (depth > 1) braced.Append(c);
                continue;
            }

            if (c == '}' && depth > 0)
            {
                depth--;
                if (depth == 0)
                {
                    result.Append(braced.ToString().Trim() == "true" ? "{true}" : "{}");
                    braced.Clear();
                }
                else
                {
                    braced.Append(c);
                }
                continue;
            }

            if (depth > 0)
            {
                braced.Append(c);
                continue;
            }

            if (c == '>')
            {
                break;
            }

            result.Append(c);
        }

        return result.ToString();
    }

    /// <summary>Removes <c>//</c> and <c>/* */</c> comments. Naive about comment markers inside strings, which can only hide a match, never add one.</summary>
    private static string StripComments(string source)
    {
        var withoutBlocks = Regex.Replace(source, @"/\*.*?\*/", m => new string('\n', m.Value.Count(ch => ch == '\n')), RegexOptions.Singleline);
        return Regex.Replace(withoutBlocks, @"(?<!:)//[^\n]*", string.Empty);
    }
}

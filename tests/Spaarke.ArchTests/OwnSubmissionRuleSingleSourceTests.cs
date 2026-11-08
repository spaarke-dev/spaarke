using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 165c: whether a submission is its invoice's own submission has ONE definition,
/// <c>InvoiceBlockedHold.IsOwnSubmission(invoiceId, submissionId)</c> (the submission's immutable id derives the invoice's,
/// <c>InvoiceSubmissionPersister.InvoiceIdFor</c>). A reader that compares a derived id itself is a second copy of the rule, and two copies
/// drift (one treats <see cref="Guid.Empty"/> differently, or a later change to the rule reaches only one).
/// </summary>
/// <remarks>
/// Enforced by: this source scan (CI and the Stop hook). The rule is a comparison any file can spell, so no type can stop a copy; a scan
/// of <c>src/server/**</c> is the strongest mechanism that works. Outside the rule's file (<c>InvoiceBlockedHold.cs</c>) and the file that
/// defines <c>InvoiceIdFor</c> and uses it to write the link (<c>InvoiceSubmissionPersister.cs</c>), no source
/// <list type="bullet">
/// <item>uses an <c>InvoiceIdFor(...)</c> call as an operand of <c>==</c>, <c>!=</c> or <c>Equals</c> (either side, either spelling:
/// <c>a == InvoiceIdFor(x)</c>, <c>InvoiceIdFor(x).Equals(a)</c>, <c>a.Equals(InvoiceIdFor(x))</c>, <c>Equals(a, InvoiceIdFor(x))</c>), or</item>
/// <item>compares a local assigned from one (<c>var invoiceId = InvoiceIdFor(s);</c> ... <c>link != invoiceId</c>, <c>link.Equals(invoiceId)</c>)
/// within that local's block, other than against <c>Guid.Empty</c>, <c>default</c> or <c>null</c>.</item>
/// </list>
/// Deriving an id (an assignment, an argument, a write target) is not a comparison. Not caught, by design of a scan: a derived id passed
/// to another method that compares it, a comparison through a field or property, a comparison through a second local copied from the
/// first, a pattern (<c>is var</c>), <c>.Contains</c> on a set of derived ids, <c>CompareTo</c>, a derivation re-spelled through
/// <c>InvoiceSubmissionPersister.IdOf</c> literals, and the exempt files. Grouping parentheses and whitespace around an operand are skipped
/// (task 165f fix round 1). A full fix needs a Roslyn analyzer. The baseline is empty. Controls: <see cref="TheDetector_FiresOnAComparison"/>, <see cref="TheDetector_FiresOnALocalAndEquals"/>,
/// <see cref="TheDetector_StaysQuietOnADerivation"/> and <see cref="TheDetector_StaysQuietOnALocalThatIsNotCompared"/>.
/// <para>Task 165f: the same scan, with the same mechanics and blind spots, holds the own-document rule
/// (<c>InvoiceSubmissionService.IsExistingDocumentSubmissionIdOf(documentId, submissionId)</c>, a document is a source only for the
/// submission derived from it): outside <c>InvoiceSubmissionService.cs</c>, which defines the rule and the derivation, no source compares
/// <c>ExistingDocumentSubmissionIdFor(...)</c> or a local assigned from it. The baseline is empty. Controls:
/// <see cref="TheDetector_FiresOnADocumentComparison"/>, <see cref="TheDetector_StaysQuietOnADocumentDerivation"/> and
/// <see cref="TheDocumentRulesFile_Exists_AndDefinesTheRule"/>.</para>
/// </remarks>
public class OwnSubmissionRuleSingleSourceTests
{
    private static readonly string[] Exempt =
    [
        "Services/Finance/Submission/InvoiceBlockedHold.cs",
        "Services/Finance/Submission/InvoiceSubmissionPersister.cs",
    ];

    /// <summary>The own-submission rule's derivation (<c>InvoiceSubmissionPersister.InvoiceIdFor</c>).</summary>
    internal const string InvoiceDerivation = "InvoiceIdFor";

    /// <summary>Task 165f: the own-document rule's derivation (<c>InvoiceSubmissionService.ExistingDocumentSubmissionIdFor</c>), compared
    /// only by <c>InvoiceSubmissionService.IsExistingDocumentSubmissionIdOf</c>, in the file that defines both.</summary>
    internal const string DocumentDerivation = "ExistingDocumentSubmissionIdFor";

    private static readonly string[] DocumentExempt = ["Services/Finance/Submission/InvoiceSubmissionService.cs"];

    // `InvoiceIdFor(` (or the derivation named): a call of it.
    private static Regex CallOf(string derivation) => new(@"\b" + Regex.Escape(derivation) + @"\s*\(");

    // `x = InvoiceIdFor(` / `var x = Qualifier.InvoiceIdFor(` / `Guid x = ...`: the identifier a derived id is held in.
    private static Regex AssignedFrom(string derivation) =>
        new(@"\b(?<name>[A-Za-z_]\w*)\s*=\s*[\w.]*\b" + Regex.Escape(derivation) + @"\s*\(");

    // String and character literals, blanked before scanning so a brace or a name inside one cannot move the block depth or match.
    private static readonly Regex Literal = new("\"(?:[^\"\\\\\\n]|\\\\.)*\"|'(?:[^'\\\\\\n]|\\\\.)+'", RegexOptions.Compiled);

    // A comparison with an empty value is a guard, not the own-submission rule.
    private static readonly Regex EmptyAfter = new(@"^\s*(?:Guid\.Empty|default|null)\b", RegexOptions.Compiled);
    private static readonly Regex EmptyBefore = new(@"\b(?:Guid\.Empty|default|null)\s*$", RegexOptions.Compiled);
    private static readonly Regex EqualsCallAfter = new(@"^\s*\??\.\s*Equals\s*\(", RegexOptions.Compiled);
    private static readonly Regex EqualsNameBefore = new(@"\bEquals\s*$", RegexOptions.Compiled);

    /// <summary>The 1-based lines of <paramref name="code"/> where the own-submission rule is spelled outside the shared method: an
    /// <c>InvoiceIdFor(...)</c> call as an operand of <c>==</c>, <c>!=</c> or <c>Equals</c>, or a local assigned from one and, within its
    /// block, an operand of the same (other than against <c>Guid.Empty</c>, <c>default</c> or <c>null</c>). Line comments and literals are
    /// ignored. A local that is only written, passed on or returned is not a comparison.</summary>
    internal static IReadOnlyList<int> ComparisonLines(string code) => ComparisonLines(code, InvoiceDerivation);

    /// <summary>Task 165f: <see cref="ComparisonLines(string)"/> for the derivation named (<see cref="DocumentDerivation"/>), with the same
    /// mechanics and the same blind spots.</summary>
    internal static IReadOnlyList<int> ComparisonLines(string code, string derivation)
    {
        var call = CallOf(derivation);
        var assignedFrom = AssignedFrom(derivation);
        var text = SourceScan.CodeText(code.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'));
        text = Literal.Replace(text, m => Regex.Replace(m.Value, @"[^\n]", " "));
        var found = new SortedSet<int>();
        foreach (Match match in call.Matches(text))
        {
            var open = match.Index + match.Length - 1;
            if (IsComparedBefore(text, match.Index, open) || IsComparedAfter(text, match.Index, open) || IsEqualsArgument(text, match.Index)
                || IsEqualsReceiver(text, open))
                found.Add(SourceScan.LineOf(text, match.Index));
        }

        foreach (Match assigned in assignedFrom.Matches(text))
        {
            var name = assigned.Groups["name"].Value;
            var scopeStart = assigned.Index + assigned.Length;
            var scopeEnd = EndOfBlock(text, assigned.Index);
            var use = new Regex(@"(?<![\w.])" + Regex.Escape(name) + @"\b");
            for (var m = use.Match(text, scopeStart); m.Success && m.Index < scopeEnd; m = m.NextMatch())
            {
                if (IsLocalCompared(text, m.Index, m.Index + m.Length))
                    found.Add(SourceScan.LineOf(text, m.Index));
            }
        }

        return found.ToList();
    }

    // The start of the call's operand: the qualifier (InvoiceSubmissionPersister., Sprk.Bff.Api.…) is part of it.
    private static int OperandStart(string text, int callStart)
    {
        var i = callStart - 1;
        while (i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '.'))
            i--;
        return i + 1;
    }

    // A `(` that opens a call or a keyword's condition (`Foo(`, `if (`), not a grouping around an operand. `return (x)` groups.
    private static bool IsCallParen(string text, int open)
    {
        var i = open - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i]))
            i--;
        if (i < 0 || !(char.IsLetterOrDigit(text[i]) || text[i] is '_' or '>' or ']'))
            return false;
        var end = i + 1;
        while (i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
            i--;
        return text[(i + 1)..end] != "return";
    }

    // What lies either side of the operand text[start..end] once the grouping parentheses that wrap it (and only it) are skipped,
    // with the whitespace beside them: `((x))` and `( x )` are the operand x.
    private static (string Before, string After) Around(string text, int start, int end)
    {
        var before = start;
        var after = end;
        while (true)
        {
            var b = before - 1;
            while (b >= 0 && char.IsWhiteSpace(text[b]))
                b--;
            var a = after;
            while (a < text.Length && char.IsWhiteSpace(text[a]))
                a++;
            if (b < 0 || a >= text.Length || text[b] != '(' || text[a] != ')' || IsCallParen(text, b))
                break;
            before = b;
            after = a + 1;
        }

        return (text[..before].TrimEnd(), text[after..].TrimStart());
    }

    // Left operand: skip the qualifier, the grouping parentheses and whitespace, then look for the operator.
    private static bool IsComparedBefore(string text, int callStart, int openParen)
    {
        var close = CloseOf(text, openParen);
        if (close < 0)
            return false;
        var before = Around(text, OperandStart(text, callStart), close + 1).Before;
        return before.EndsWith("==", StringComparison.Ordinal) || before.EndsWith("!=", StringComparison.Ordinal);
    }

    // Right operand: find the call's closing parenthesis, skip the grouping parentheses, then look for the operator after it.
    private static bool IsComparedAfter(string text, int callStart, int openParen)
    {
        var close = CloseOf(text, openParen);
        if (close < 0)
            return false;
        var rest = Around(text, OperandStart(text, callStart), close + 1).After;
        return rest.StartsWith("==", StringComparison.Ordinal) || rest.StartsWith("!=", StringComparison.Ordinal);
    }

    // `Equals(InvoiceIdFor(a), x)`, `Equals(x, InvoiceIdFor(a))`, `x.Equals(InvoiceIdFor(a))`: the call is a direct argument of Equals.
    private static bool IsEqualsArgument(string text, int start)
    {
        var open = EnclosingOpenParen(text, start);
        return open >= 0 && EqualsNameBefore.IsMatch(text[..open]);
    }

    // `InvoiceIdFor(a).Equals(x)`
    private static bool IsEqualsReceiver(string text, int openParen)
    {
        var close = CloseOf(text, openParen);
        return close >= 0 && EqualsCallAfter.IsMatch(text[(close + 1)..]);
    }

    // A local holding a derived id, as an operand: `x == a`, `a != x`, `a.Equals(x)`, `x.Equals(a)`, `Equals(a, x)`.
    private static bool IsLocalCompared(string text, int start, int end)
    {
        var (before, after) = Around(text, start, end);
        if ((before.EndsWith("==", StringComparison.Ordinal) || before.EndsWith("!=", StringComparison.Ordinal))
            && !EmptyBefore.IsMatch(before[..^2].TrimEnd()))
            return true;
        if ((after.StartsWith("==", StringComparison.Ordinal) || after.StartsWith("!=", StringComparison.Ordinal))
            && !EmptyAfter.IsMatch(after[2..]))
            return true;
        if (EqualsCallAfter.IsMatch(after))
            return true;
        var open = EnclosingOpenParen(text, start);
        return open >= 0 && EqualsNameBefore.IsMatch(text[..open]);
    }

    // The `(` that directly encloses <paramref name="index"/> (not a group already closed before it), or -1.
    private static int EnclosingOpenParen(string text, int index)
    {
        var depth = 0;
        for (var i = index - 1; i >= 0; i--)
        {
            if (text[i] == ')')
                depth++;
            else if (text[i] == '(')
            {
                if (depth == 0)
                    return i;
                depth--;
            }
            else if (depth == 0 && text[i] is ';' or '{' or '}')
                return -1;
        }

        return -1;
    }

    private static int CloseOf(string text, int openParen)
    {
        var depth = 0;
        for (var i = openParen; i < text.Length; i++)
        {
            if (text[i] == '(')
                depth++;
            else if (text[i] == ')' && --depth == 0)
                return i;
        }

        return -1;
    }

    // The index where the block holding <paramref name="index"/> closes (the end of the text for a top-level statement).
    private static int EndOfBlock(string text, int index)
    {
        var depth = 0;
        for (var i = index; i < text.Length; i++)
        {
            if (text[i] == '{')
                depth++;
            else if (text[i] == '}' && --depth < 0)
                return i;
        }

        return text.Length;
    }

    private static string RelativeToBff(string file) =>
        Path.GetRelativePath(Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api"), file).Replace('\\', '/');

    [Fact(DisplayName = "165c: no server source outside InvoiceBlockedHold and InvoiceSubmissionPersister compares InvoiceIdFor(...), or a local assigned from it, with ==, != or Equals (use InvoiceBlockedHold.IsOwnSubmission)")]
    public void NoServerSource_ComparesADerivedInvoiceId_OutsideTheRule()
    {
        var copies = SourceScan.ServerSourceFiles()
            .Where(f => !Exempt.Contains(RelativeToBff(f), StringComparer.Ordinal))
            .SelectMany(f => ComparisonLines(File.ReadAllText(f)).Select(line => $"{SourceScan.Relative(f)}:{line}"))
            .ToList();

        Assert.True(copies.Count == 0,
            "compares InvoiceIdFor(...) or a local holding it (a second copy of the own-submission rule); call InvoiceBlockedHold.IsOwnSubmission(invoiceId, submissionId): "
            + string.Join(", ", copies));
    }

    [Fact(DisplayName = "165c control: the exempt files exist (a rename would silently widen the scan's blind spot)")]
    public void TheExemptFiles_Exist()
    {
        foreach (var file in Exempt)
            Assert.True(File.Exists(Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", file)), file);
    }

    [Fact(DisplayName = "165f: no server source outside InvoiceSubmissionService compares ExistingDocumentSubmissionIdFor(...), or a local assigned from it, with ==, != or Equals (use InvoiceSubmissionService.IsExistingDocumentSubmissionIdOf)")]
    public void NoServerSource_ComparesADerivedDocumentSubmissionId_OutsideTheRule()
    {
        var copies = SourceScan.ServerSourceFiles()
            .Where(f => !DocumentExempt.Contains(RelativeToBff(f), StringComparer.Ordinal))
            .SelectMany(f => ComparisonLines(File.ReadAllText(f), DocumentDerivation).Select(line => $"{SourceScan.Relative(f)}:{line}"))
            .ToList();

        Assert.True(copies.Count == 0,
            "compares ExistingDocumentSubmissionIdFor(...) or a local holding it (a second copy of the own-document rule); call "
            + "InvoiceSubmissionService.IsExistingDocumentSubmissionIdOf(documentId, submissionId): " + string.Join(", ", copies));
    }

    [Fact(DisplayName = "165f control: the own-document rule's file exists and still defines the rule and the derivation")]
    public void TheDocumentRulesFile_Exists_AndDefinesTheRule()
    {
        var file = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", DocumentExempt[0]);
        Assert.True(File.Exists(file), DocumentExempt[0]);
        var code = File.ReadAllText(file);
        Assert.Contains("static bool IsExistingDocumentSubmissionIdOf(Guid documentId, Guid submissionId)", code, StringComparison.Ordinal);
        Assert.Contains($"static Guid {DocumentDerivation}(Guid documentId, int generation)", code, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "165f control (must fire): ExistingDocumentSubmissionIdFor compared directly, through a local, or with Equals")]
    public void TheDetector_FiresOnADocumentComparison()
    {
        const string code = """
            void A(Guid documentId, Guid submissionId, Guid other)
            {
                if (submissionId != InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(documentId)) return;
                var own = ExistingDocumentSubmissionIdFor(documentId, 2) == submissionId;
                var expected = InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(documentId);
                var same = other == expected;
                var eq = submissionId.Equals(ExistingDocumentSubmissionIdFor(documentId));
            }
            """;

        Assert.Equal(new[] { 3, 4, 6, 7 }, ComparisonLines(code, DocumentDerivation));
        Assert.Empty(ComparisonLines(code));
    }

    [Fact(DisplayName = "165f control (must not fire): deriving, passing or guarding a document submission id, the shared rule, and a comment")]
    public void TheDetector_StaysQuietOnADocumentDerivation()
    {
        const string code = """
            void A(Guid documentId, Guid submissionId)
            {
                var confirmSubmissionId = InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(documentId);
                var confirmed = Get(confirmSubmissionId);
                if (confirmSubmissionId == Guid.Empty) return;
                var row = Get(ExistingDocumentSubmissionIdFor(documentId, 2));
                var own = InvoiceSubmissionService.IsExistingDocumentSubmissionIdOf(documentId, submissionId);
                // a note: ExistingDocumentSubmissionIdFor(documentId) == submissionId
            }
            """;

        Assert.Empty(ComparisonLines(code, DocumentDerivation));
    }

    [Fact(DisplayName = "165f fix round 1 control (must fire): a derived id or a local wrapped in grouping parentheses, with whitespace, on either side")]
    public void TheDetector_FiresOnAParenthesisedOperand()
    {
        const string call = """
            void A(Guid documentId, Guid s)
            {
                var a = (ExistingDocumentSubmissionIdFor(documentId)) == s;
                var b = s != (InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(documentId));
                var c = ( ( ExistingDocumentSubmissionIdFor(documentId) ) ) != s;
                if (s == ( ExistingDocumentSubmissionIdFor(documentId) )) return;
            }
            """;
        Assert.Equal(new[] { 3, 4, 5, 6 }, ComparisonLines(call, DocumentDerivation));

        const string local = """
            void A(Guid documentId, Guid s)
            {
                var e = ExistingDocumentSubmissionIdFor(documentId);
                var a = ((e) == s);
                var b = s != ( e );
                var c = (e).Equals(s);
            }
            """;
        Assert.Equal(new[] { 4, 5, 6 }, ComparisonLines(local, DocumentDerivation));

        // The invoice derivation is scanned the same way.
        Assert.Equal(new[] { 1 }, ComparisonLines("var a = (InvoiceSubmissionPersister.InvoiceIdFor(s)) == link;"));
    }

    [Fact(DisplayName = "165f fix round 1 control (must not fire): a parenthesised derivation that is passed on, returned or wrapped in a call is not a comparison")]
    public void TheDetector_StaysQuietOnAParenthesisedDerivation()
    {
        const string code = """
            Guid A(Guid documentId, Guid s)
            {
                var e = ExistingDocumentSubmissionIdFor(documentId);
                Use((ExistingDocumentSubmissionIdFor(documentId)), s == Guid.Empty);
                var wrapped = Get((ExistingDocumentSubmissionIdFor(documentId))) == s;
                var copy = (e);
                if ((e) == Guid.Empty) return (e);
                return (ExistingDocumentSubmissionIdFor(documentId));
            }
            """;

        Assert.Empty(ComparisonLines(code, DocumentDerivation));
    }

    [Fact(DisplayName = "165c control (must fire): a comparison on either side, == or !=, qualified or not, across a line break")]
    public void TheDetector_FiresOnAComparison()
    {
        const string code = """
            if (invoiceId != InvoiceSubmissionPersister.InvoiceIdFor(submissionId)) return;
            var own = InvoiceIdFor(s.Id) == invoiceId;
            var same = id == Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionPersister.InvoiceIdFor(Pick(earlier, other));
            var wrapped = InvoiceSubmissionPersister.InvoiceIdFor(target)
                != invoiceId;
            """;

        Assert.Equal(new[] { 1, 2, 3, 4 }, ComparisonLines(code));
    }

    [Fact(DisplayName = "165c pass 2 control (must fire): a local assigned from InvoiceIdFor and compared (== / != / Equals), and Equals on the call itself, both forms")]
    public void TheDetector_FiresOnALocalAndEquals()
    {
        const string local = """
            void A(Entity submission, Guid submissionId)
            {
                var invoiceId = InvoiceSubmissionPersister.InvoiceIdFor(submissionId);
                Use(invoiceId);
                if (submission.GetAttributeValue<EntityReference>("sprk_invoice")?.Id != invoiceId) return;
            }
            """;
        Assert.Equal(new[] { 5 }, ComparisonLines(local));

        const string reversed = """
            void A(Guid link, Guid submissionId)
            {
                Guid expected = InvoiceIdFor(submissionId);
                var own = expected == link;
            }
            """;
        Assert.Equal(new[] { 4 }, ComparisonLines(reversed));

        const string equalsOnLocal = """
            void A(Guid link, Guid submissionId)
            {
                var invoiceId = InvoiceIdFor(submissionId);
                var a = link.Equals(invoiceId);
                var b = invoiceId.Equals(link);
                var c = Guid.Equals(link, invoiceId);
            }
            """;
        Assert.Equal(new[] { 4, 5, 6 }, ComparisonLines(equalsOnLocal));

        const string equalsOnCall = """
            var a = Equals(InvoiceIdFor(s), link);
            var b = link.Equals(InvoiceSubmissionPersister.InvoiceIdFor(s));
            var c = Guid.Equals(link, InvoiceIdFor(s));
            var d = InvoiceIdFor(s).Equals(link);
            var e = Foo(Equals(Bar(), InvoiceIdFor(s)));
            """;
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, ComparisonLines(equalsOnCall));
    }

    [Fact(DisplayName = "165c control (must not fire): deriving an id, passing it on, the shared rule, and a comment")]
    public void TheDetector_StaysQuietOnADerivation()
    {
        const string code = """
            var invoiceId = InvoiceSubmissionPersister.InvoiceIdFor(submissionId);
            var parent = new StoredFileParent(StoredFileParentKind.Invoice, InvoiceIdFor(submission.SubmissionId));
            yield return InvoiceSubmissionPersister.InvoiceIdFor(s.SubmissionId);
            var own = InvoiceBlockedHold.IsOwnSubmission(invoiceId, submissionId);
            var later = invoiceId >= InvoiceIdFor(x) ? 1 : 0;
            // a note: InvoiceIdFor(s.Id) == invoiceId
            var text = Describe(Equals(Pick(), 1), InvoiceIdFor(s));
            """;

        Assert.Empty(ComparisonLines(code));
    }

    [Fact(DisplayName = "165c pass 2 control (must not fire): a local from InvoiceIdFor that is only written, passed or guarded; a same-named local elsewhere")]
    public void TheDetector_StaysQuietOnALocalThatIsNotCompared()
    {
        const string writeOnly = """
            void A(Entity invoice, Guid submissionId)
            {
                var invoiceId = InvoiceSubmissionPersister.InvoiceIdFor(submissionId);
                invoice.Id = invoiceId;
                invoice["sprk_invoice"] = new EntityReference("sprk_invoice", invoiceId);
                Store(invoiceId, $"invoice {invoiceId} of {submissionId}");
                if (invoiceId == Guid.Empty || Guid.Empty != invoiceId || invoiceId == default || null == invoiceId) return;
                var same = invoiceId; // a plain copy
            }
            """;
        Assert.Empty(ComparisonLines(writeOnly));

        // The same name in another method of the same file: a parameter, not the derived local.
        const string otherMethod = """
            void A(Guid submissionId)
            {
                var invoiceId = InvoiceIdFor(submissionId);
                Use(invoiceId);
            }

            bool B(Guid invoiceId, Guid link)
            {
                return link != invoiceId;
            }
            """;
        Assert.Empty(ComparisonLines(otherMethod));

        // The same name in another file: a file is scanned on its own.
        Assert.Empty(ComparisonLines("void A(Guid submissionId) { Use(InvoiceIdFor(submissionId)); }"));
        Assert.Empty(ComparisonLines("void B(Guid link) { var invoiceId = Guid.NewGuid(); if (link != invoiceId) return; }"));

        // A name used only inside a string is not a use.
        Assert.Empty(ComparisonLines("""
            void A(Guid submissionId)
            {
                var invoiceId = InvoiceIdFor(submissionId);
                Log("invoiceId == x and Equals(InvoiceIdFor(a), b)");
            }
            """));
    }
}

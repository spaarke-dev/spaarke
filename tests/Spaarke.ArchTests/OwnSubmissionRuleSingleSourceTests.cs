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
/// submission derived from it): outside the body of that method, no source compares
/// <c>ExistingDocumentSubmissionIdFor(...)</c> or a local assigned from it. The method is in <c>InvoiceSubmissionService.cs</c>, which is
/// scanned like any other file with only the method's span (found by its signature and matched braces) left out. The baseline is empty. Controls:
/// <see cref="TheDetector_FiresOnADocumentComparison"/>, <see cref="TheDetector_StaysQuietOnADocumentDerivation"/> and
/// <see cref="TheDocumentRulesFile_Exists_AndDefinesTheRule"/>.</para>
/// <para>Task 165f follow-up: the guard now also catches a grouped operand after a lambda arrow (<c>x =&gt; (D(d)) == s</c>), a derivation in
/// extra parentheses as an <c>Equals</c> argument or receiver, and an operand that is a cast of the derivation (<c>(Guid?)D(d) == s</c>) or of
/// a local holding it. Known limit: a local assigned from a cast of the derivation (<c>var a = (Guid?)D(d);</c>) is not tracked as holding
/// it, because the local's assignment pattern is a plain call.</para>
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

    /// <summary>The file that defines the own-document rule and its derivation. Only the rule method's span is exempt in it, not the file.</summary>
    private const string DocumentRuleFile = "Services/Finance/Submission/InvoiceSubmissionService.cs";

    /// <summary>The signature of the own-document rule method; its span (found by matching braces) is the one place the derivation is compared.</summary>
    internal const string DocumentRuleSignature = "static bool IsExistingDocumentSubmissionIdOf(Guid documentId, Guid submissionId)";

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
            var close = CloseOf(text, open);
            if (IsComparedBefore(text, match.Index, open) || IsComparedAfter(text, match.Index, open)
                || IsEqualsArgument(text, match.Index, close < 0 ? open + 1 : close + 1) || IsEqualsReceiver(text, match.Index, open))
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

    /// <summary>Task 165f follow-up: <see cref="ComparisonLines(string, string)"/> for the file that defines the rule, less the span of the rule
    /// method (<paramref name="ruleSignature"/>, located by its signature and its matched braces). A comparison anywhere else in that file is
    /// reported. Throws when the method cannot be found, so a rename cannot silently leave the whole file unchecked.</summary>
    internal static IReadOnlyList<int> ComparisonLinesOutsideRule(string code, string derivation, string ruleSignature)
    {
        var (first, last) = RuleSpan(code, ruleSignature);
        return ComparisonLines(code, derivation).Where(line => line < first || line > last).ToList();
    }

    /// <summary>The 1-based first and last line of the method declared with <paramref name="signature"/> (whitespace-insensitive), from the
    /// signature to the brace that closes its body (an expression body ends at its <c>;</c>).</summary>
    internal static (int FirstLine, int LastLine) RuleSpan(string code, string signature)
    {
        var text = SourceScan.CodeText(code.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'));
        text = Literal.Replace(text, m => Regex.Replace(m.Value, @"[^\n]", " "));
        var found = Regex.Matches(text, Regex.Escape(signature).Replace(@"\ ", @"\s+"));
        if (found.Count != 1)
            throw new InvalidOperationException($"expected exactly one declaration of '{signature}', found {found.Count}");

        var afterSignature = found[0].Index + found[0].Length;
        var bodyStart = text.IndexOfAny(['{', ';', '='], afterSignature);
        if (bodyStart < 0)
            throw new InvalidOperationException($"'{signature}' has no body");
        if (text[bodyStart] != '{')
            return (SourceScan.LineOf(text, found[0].Index), SourceScan.LineOf(text, text.IndexOf(';', bodyStart)));

        var depth = 0;
        for (var i = bodyStart; i < text.Length; i++)
        {
            if (text[i] == '{')
                depth++;
            else if (text[i] == '}' && --depth == 0)
                return (SourceScan.LineOf(text, found[0].Index), SourceScan.LineOf(text, i));
        }

        throw new InvalidOperationException($"the body of '{signature}' is not closed");
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
        // `x => (…)`: the lambda arrow is not a generic's closing `>`, so the `(` after it groups the body's expression.
        if (text[i] == '>' && i > 0 && text[i - 1] == '=')
            return false;
        var end = i + 1;
        while (i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
            i--;
        return text[(i + 1)..end] != "return";
    }

    // A cast written in front of an operand: `(Guid?)`, `(System.Guid)`. A parenthesised type name directly before an operand (no operator
    // between) can only be a cast, so a cast of the derived id is the derived id.
    private static readonly Regex CastBefore = new(@"\(\s*[A-Za-z_][\w.]*(?:<[\w.,\s?<>]*>)?\??\s*\)\s*$", RegexOptions.Compiled);

    // The operand text[start..end] widened over the grouping parentheses that wrap it (and only it) and over a cast in front of it, with
    // the whitespace beside them: `((x))`, `( x )` and `(Guid?)x` are the operand x.
    private static (int Start, int End) Widen(string text, int start, int end)
    {
        while (true)
        {
            var b = start - 1;
            while (b >= 0 && char.IsWhiteSpace(text[b]))
                b--;
            var a = end;
            while (a < text.Length && char.IsWhiteSpace(text[a]))
                a++;
            if (b >= 0 && a < text.Length && text[b] == '(' && text[a] == ')' && !IsCallParen(text, b))
            {
                start = b;
                end = a + 1;
                continue;
            }

            var from = Math.Max(0, start - 200);
            var cast = CastBefore.Match(text[from..start]);
            if (!cast.Success)
                return (start, end);
            start = from + cast.Index;
        }
    }

    // What lies either side of the widened operand text[start..end].
    private static (string Before, string After) Around(string text, int start, int end)
    {
        var (wideStart, wideEnd) = Widen(text, start, end);
        return (text[..wideStart].TrimEnd(), text[wideEnd..].TrimStart());
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

    // `Equals(InvoiceIdFor(a), x)`, `Equals(x, InvoiceIdFor(a))`, `x.Equals(InvoiceIdFor(a))`, also `Equals((InvoiceIdFor(a)), x)`: the call (once
    // its grouping parentheses and a cast are skipped) is a direct argument of Equals.
    private static bool IsEqualsArgument(string text, int start, int end)
    {
        var open = EnclosingOpenParen(text, Widen(text, OperandStart(text, start), end).Start);
        return open >= 0 && EqualsNameBefore.IsMatch(text[..open]);
    }

    // `InvoiceIdFor(a).Equals(x)`, also `(InvoiceIdFor(a)).Equals(x)`
    private static bool IsEqualsReceiver(string text, int callStart, int openParen)
    {
        var close = CloseOf(text, openParen);
        return close >= 0 && EqualsCallAfter.IsMatch(Around(text, OperandStart(text, callStart), close + 1).After);
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
        var open = EnclosingOpenParen(text, Widen(text, start, end).Start);
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

    [Fact(DisplayName = "165f: no server source outside the IsExistingDocumentSubmissionIdOf method compares ExistingDocumentSubmissionIdFor(...), or a local assigned from it, with ==, != or Equals (use InvoiceSubmissionService.IsExistingDocumentSubmissionIdOf)")]
    public void NoServerSource_ComparesADerivedDocumentSubmissionId_OutsideTheRule()
    {
        var copies = SourceScan.ServerSourceFiles()
            .SelectMany(f =>
            {
                var code = File.ReadAllText(f);
                var lines = RelativeToBff(f) == DocumentRuleFile
                    ? ComparisonLinesOutsideRule(code, DocumentDerivation, DocumentRuleSignature)
                    : ComparisonLines(code, DocumentDerivation);
                return lines.Select(line => $"{SourceScan.Relative(f)}:{line}");
            })
            .ToList();

        Assert.True(copies.Count == 0,
            "compares ExistingDocumentSubmissionIdFor(...) or a local holding it (a second copy of the own-document rule); call "
            + "InvoiceSubmissionService.IsExistingDocumentSubmissionIdOf(documentId, submissionId): " + string.Join(", ", copies));
    }

    [Fact(DisplayName = "165f control: the own-document rule's file exists and still defines the rule and the derivation")]
    public void TheDocumentRulesFile_Exists_AndDefinesTheRule()
    {
        var file = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", DocumentRuleFile);
        Assert.True(File.Exists(file), DocumentRuleFile);
        var code = File.ReadAllText(file);
        Assert.Contains(DocumentRuleSignature, code, StringComparison.Ordinal);
        var (first, last) = RuleSpan(code, DocumentRuleSignature);
        Assert.True(last > first, "the rule method's span is a real body");
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

    [Fact(DisplayName = "165f follow-up control (must fire): a comparison in a lambda body or an expression-bodied member that starts with a grouping parenthesis")]
    public void TheDetector_FiresOnAGroupedOperandAfterALambdaArrow()
    {
        const string code = """
            void A(Guid documentId, Guid s, IEnumerable<Guid> ids)
            {
                var a = ids.Any(x => (ExistingDocumentSubmissionIdFor(documentId)) == s);
                Func<Guid, bool> b = x => ( InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(documentId) ) != x;
            }

            bool Own(Guid documentId, Guid s) => (ExistingDocumentSubmissionIdFor(documentId)) == s;
            """;

        Assert.Equal(new[] { 3, 4, 7 }, ComparisonLines(code, DocumentDerivation));
    }

    [Fact(DisplayName = "165f follow-up control (must not fire): a lambda or generic call whose parenthesis wraps the derivation as an argument, and a grouped lambda body that is not compared")]
    public void TheDetector_StaysQuietOnACallAfterALambdaArrowOrGeneric()
    {
        const string code = """
            void A(Guid documentId, Guid s, IEnumerable<Guid> ids)
            {
                var a = ids.Select(x => Get(ExistingDocumentSubmissionIdFor(documentId))) == s;
                var b = Get<Guid>(ExistingDocumentSubmissionIdFor(documentId)) == s;
                var c = ids.Select(x => (ExistingDocumentSubmissionIdFor(documentId)));
            }
            """;

        Assert.Empty(ComparisonLines(code, DocumentDerivation));
    }

    [Fact(DisplayName = "165f follow-up control (must fire): Equals with the derivation in extra parentheses, as either argument or as the receiver")]
    public void TheDetector_FiresOnEqualsWithAWrappedDerivation()
    {
        const string code = """
            void A(Guid documentId, Guid s)
            {
                var a = Equals((ExistingDocumentSubmissionIdFor(documentId)), s);
                var b = s.Equals((ExistingDocumentSubmissionIdFor(documentId)));
                var c = Guid.Equals(s, ( ( InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(documentId) ) ));
                var d = (ExistingDocumentSubmissionIdFor(documentId)).Equals(s);
            }
            """;

        Assert.Equal(new[] { 3, 4, 5, 6 }, ComparisonLines(code, DocumentDerivation));
    }

    [Fact(DisplayName = "165f follow-up control (must not fire): Equals on unrelated values in the same shape, and the derivation nested in another call")]
    public void TheDetector_StaysQuietOnEqualsOfUnrelatedValues()
    {
        const string code = """
            void A(Guid documentId, Guid s)
            {
                var a = Equals((Pick(documentId)), s);
                var b = s.Equals((Other(documentId)));
                var c = Equals((Get(ExistingDocumentSubmissionIdFor(documentId))), s);
                var d = s.Equals((Get(ExistingDocumentSubmissionIdFor(documentId))));
                var e = Use((ExistingDocumentSubmissionIdFor(documentId)), s.Equals(documentId));
            }
            """;

        Assert.Empty(ComparisonLines(code, DocumentDerivation));
    }

    [Fact(DisplayName = "165f follow-up control (must fire): a comparison or Equals whose operand is a cast of the derivation (or of a local holding it)")]
    public void TheDetector_FiresOnACastOperand()
    {
        const string code = """
            void A(Guid documentId, Guid? s)
            {
                var a = (Guid?)ExistingDocumentSubmissionIdFor(documentId) == s;
                var b = s != (Guid?)InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(documentId);
                var c = ((System.Guid?)(ExistingDocumentSubmissionIdFor(documentId))) == s;
                var d = Equals(s, (Guid?)ExistingDocumentSubmissionIdFor(documentId));
                var e = ExistingDocumentSubmissionIdFor(documentId);
                var f = (Guid?)e == s;
            }
            """;

        Assert.Equal(new[] { 3, 4, 5, 6, 8 }, ComparisonLines(code, DocumentDerivation));
    }

    [Fact(DisplayName = "165f follow-up control (must not fire): a cast of the derivation that is only assigned, passed on, returned or chosen")]
    public void TheDetector_StaysQuietOnACastThatIsNotCompared()
    {
        const string code = """
            Guid? A(Guid documentId, Guid? s)
            {
                var a = (Guid?)ExistingDocumentSubmissionIdFor(documentId);
                var b = Get((Guid?)ExistingDocumentSubmissionIdFor(documentId)) == s;
                if (s == (Guid?)Guid.Empty) return (Guid?)ExistingDocumentSubmissionIdFor(documentId);
                return Pick(a) == s ? (Guid?)ExistingDocumentSubmissionIdFor(documentId) : null;
            }
            """;

        Assert.Empty(ComparisonLines(code, DocumentDerivation));
    }

    [Fact(DisplayName = "165f follow-up control: only the rule method's span of the rule file is exempt; a comparison elsewhere in it is reported, and a missing rule method fails loudly")]
    public void TheDetector_ReportsAComparisonOutsideTheRuleMethodInTheRuleFile()
    {
        const string code = """
            class InvoiceSubmissionService
            {
                public static Guid ExistingDocumentSubmissionIdFor(Guid documentId, int generation) => generation <= 1
                    ? ExistingDocumentSubmissionIdFor(documentId)
                    : Guid.Empty;

                internal static bool IsExistingDocumentSubmissionIdOf(Guid documentId, Guid submissionId)
                {
                    for (var generation = 1; generation <= 3; generation++)
                    {
                        if (ExistingDocumentSubmissionIdFor(documentId, generation) == submissionId)
                        {
                            return true;
                        }
                    }

                    return false;
                }

                bool Elsewhere(Guid documentId, Guid x)
                {
                    return ExistingDocumentSubmissionIdFor(documentId) == x;
                }
            }
            """;

        // The whole-file scan sees both; only the rule method's span is exempt.
        Assert.Equal(new[] { 11, 22 }, ComparisonLines(code, DocumentDerivation));
        Assert.Equal(new[] { 22 }, ComparisonLinesOutsideRule(code, DocumentDerivation, DocumentRuleSignature));
        Assert.Equal((7, 18), RuleSpan(code, DocumentRuleSignature));

        // A rule file that holds only the rule method reports nothing; one without it throws rather than exempting nothing silently.
        var ruleOnly = string.Join("\n", code.Split('\n').Take(19)) + "\n}";
        Assert.Empty(ComparisonLinesOutsideRule(ruleOnly, DocumentDerivation, DocumentRuleSignature));
        Assert.Throws<InvalidOperationException>(() =>
            ComparisonLinesOutsideRule("class C { bool Other() => true; }", DocumentDerivation, DocumentRuleSignature));
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

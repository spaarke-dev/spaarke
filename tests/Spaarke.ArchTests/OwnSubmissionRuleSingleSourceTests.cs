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
/// defines <c>InvoiceIdFor</c> and uses it to write the link (<c>InvoiceSubmissionPersister.cs</c>), no source compares <c>InvoiceIdFor(...)</c>
/// with <c>==</c> or <c>!=</c>. Deriving an id (an assignment, an argument) is not a comparison. The baseline is empty. Controls:
/// <see cref="TheDetector_FiresOnAComparison"/> and <see cref="TheDetector_StaysQuietOnADerivation"/>.
/// </remarks>
public class OwnSubmissionRuleSingleSourceTests
{
    private static readonly string[] Exempt =
    [
        "Services/Finance/Submission/InvoiceBlockedHold.cs",
        "Services/Finance/Submission/InvoiceSubmissionPersister.cs",
    ];

    private static readonly Regex Call = new(@"\bInvoiceIdFor\s*\(", RegexOptions.Compiled);

    /// <summary>The 1-based lines of <paramref name="code"/> where an <c>InvoiceIdFor(...)</c> call is an operand of <c>==</c> or <c>!=</c>.
    /// Line comments are ignored.</summary>
    internal static IReadOnlyList<int> ComparisonLines(string code)
    {
        var text = SourceScan.CodeText(code.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'));
        var found = new List<int>();
        foreach (Match match in Call.Matches(text))
        {
            if (IsComparedBefore(text, match.Index) || IsComparedAfter(text, match.Index + match.Length - 1))
                found.Add(SourceScan.LineOf(text, match.Index));
        }

        return found;
    }

    // Left operand: skip the qualifier (InvoiceSubmissionPersister., Sprk.Bff.Api.…) and whitespace, then look for the operator.
    private static bool IsComparedBefore(string text, int callStart)
    {
        var i = callStart - 1;
        while (i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '.'))
            i--;
        while (i >= 0 && char.IsWhiteSpace(text[i]))
            i--;
        return i >= 1 && text[i] == '=' && text[i - 1] is '=' or '!';
    }

    // Right operand: find the call's closing parenthesis, then look for the operator after it.
    private static bool IsComparedAfter(string text, int openParen)
    {
        var depth = 0;
        for (var i = openParen; i < text.Length; i++)
        {
            if (text[i] == '(')
                depth++;
            else if (text[i] == ')' && --depth == 0)
            {
                var rest = text[(i + 1)..].TrimStart();
                return rest.StartsWith("==", StringComparison.Ordinal) || rest.StartsWith("!=", StringComparison.Ordinal);
            }
        }

        return false;
    }

    private static string RelativeToBff(string file) =>
        Path.GetRelativePath(Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api"), file).Replace('\\', '/');

    [Fact(DisplayName = "165c: no server source outside InvoiceBlockedHold and InvoiceSubmissionPersister compares InvoiceIdFor(...) with == or != (use InvoiceBlockedHold.IsOwnSubmission)")]
    public void NoServerSource_ComparesADerivedInvoiceId_OutsideTheRule()
    {
        var copies = SourceScan.ServerSourceFiles()
            .Where(f => !Exempt.Contains(RelativeToBff(f), StringComparer.Ordinal))
            .SelectMany(f => ComparisonLines(File.ReadAllText(f)).Select(line => $"{SourceScan.Relative(f)}:{line}"))
            .ToList();

        Assert.True(copies.Count == 0,
            "compares InvoiceIdFor(...) itself (a second copy of the own-submission rule); call InvoiceBlockedHold.IsOwnSubmission(invoiceId, submissionId): "
            + string.Join(", ", copies));
    }

    [Fact(DisplayName = "165c control: the exempt files exist (a rename would silently widen the scan's blind spot)")]
    public void TheExemptFiles_Exist()
    {
        foreach (var file in Exempt)
            Assert.True(File.Exists(Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", file)), file);
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
            """;

        Assert.Empty(ComparisonLines(code));
    }
}

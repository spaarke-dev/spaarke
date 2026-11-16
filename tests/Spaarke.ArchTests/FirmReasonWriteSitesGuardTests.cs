using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 130e-1 (DEF-060; uac-r2 O3, design 3.3 (a)(b)(c)): the partner-visible firm reason on a billing Signal has exactly one
/// source and one write.
/// </summary>
/// <remarks>
/// <para><b>Rule 1 (O3).</b> <c>SignalWriteRequest.FirmReason</c> is assigned only where the text is the stored policy version's, after
/// <c>FirmDescriptionRules</c>: <c>BillingFindingSignalMapper</c> (the evaluation's mapping) and <c>BillingFirmReasonReplay</c> (the plan replay,
/// which re-reads the version). Any other assignment (<c>FirmReason = ...</c> in an object initializer or a <c>with</c> expression) could take
/// the text from a request body, a fact value, a sentence or AI output.</para>
/// <para><b>Rule 2 (3.3 (c)).</b> In <c>SignalWriter.cs</c> the column name <c>"sprk_firmreason"</c> appears in code only inside
/// <c>BuildEntity</c>, the create payload: no update, refresh or reconcile path can send it.</para>
/// <para><b>Rule 3 (3.3 (a)(b)).</b> Nowhere else in the BFF is <c>"sprk_firmreason"</c> written: no indexer assignment
/// (<c>row["sprk_firmreason"] = ...</c>, a dictionary initializer) and no <c>Add("sprk_firmreason", ...)</c>. Reading it (a column list,
/// <c>GetAttributeValue</c>) is allowed.</para>
/// <para><b>Maintenance.</b> If this fails, do not widen the allowlist to make a new path pass: a new source of the firm reason needs the
/// same review as DEF-060 (the design note's C1-C4 and O1-O6). A file that moves keeps its entry only if it keeps the same role; update
/// <see cref="FirmReasonAssigners"/> with the new path and its reason. Rules 2 and 3 have no allowlist.</para>
/// <para>Enforced by this test (CLAUDE.md section 16 enforcement ladder: a source scan; the type system cannot tell which code sets an
/// init-only property). No ratchet baseline: the scope has no violation today.</para>
/// </remarks>
public sealed class FirmReasonWriteSitesGuardTests
{
    /// <summary>The files that may assign <c>FirmReason</c>, each with its reason (ADR citation: design note 130e / DEF-060 O3).</summary>
    private static readonly IReadOnlyDictionary<string, string> FirmReasonAssigners = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["BillingFindingSignalMapper.cs"] = "DEF-060 O3: the mapper copies the cited version's description after FirmDescriptionRules (C3, C4).",
        ["BillingFirmReasonReplay.cs"] = "DEF-060 C1: the plan replay re-reads the version's description and validates it; the plan never carries it.",
    };

    private const string Column = "\"sprk_firmreason\"";

    /// <summary>An assignment to a member named FirmReason (not <c>==</c>, not <c>=&gt;</c>).</summary>
    private static readonly Regex FirmReasonAssignment = new(@"\bFirmReason\s*=(?![=>])", RegexOptions.CultureInvariant);

    /// <summary>A write of the column: an indexer assignment or an <c>Add("sprk_firmreason", ...)</c>.</summary>
    private static readonly Regex ColumnWrite = new(
        @"\[\s*""sprk_firmreason""\s*\]\s*=(?![=>])|\bAdd\s*\(\s*""sprk_firmreason""\s*,", RegexOptions.CultureInvariant);

    private static readonly Regex BuildEntityDeclaration = new(@"\bEntity\s+BuildEntity\s*\(", RegexOptions.CultureInvariant);

    internal sealed record Finding(string File, int Line, string Rule);

    // ── The scans ──

    /// <summary>Rule 1: assignments of FirmReason in code (comments and strings are not code) in a file not on the allowlist.</summary>
    internal static IReadOnlyList<Finding> ScanAssignments(string file, string source)
    {
        if (FirmReasonAssigners.ContainsKey(Path.GetFileName(file)))
        {
            return [];
        }

        var masked = DraftPolicyVersionStatecodeGuardTests.Mask(source);
        return FirmReasonAssignment.Matches(masked).Select(m => new Finding(file, LineOf(source, m.Index), "FirmReason assigned outside the mapper and the replay")).ToList();
    }

    /// <summary>Rule 2: every code occurrence of the column name in SignalWriter.cs lies inside BuildEntity.</summary>
    internal static IReadOnlyList<Finding> ScanSignalWriter(string file, string source)
    {
        var code = MaskCommentsOnly(source);
        var masked = DraftPolicyVersionStatecodeGuardTests.Mask(source);
        var declaration = BuildEntityDeclaration.Match(masked);
        var (start, end) = (-1, -1);
        if (declaration.Success)
        {
            start = masked.IndexOf('{', declaration.Index);
            end = start < 0 ? -1 : MatchingBrace(masked, start);
        }

        return Occurrences(code, Column)
            .Where(i => i < start || i > end)
            .Select(i => new Finding(file, LineOf(source, i), "sprk_firmreason named outside SignalWriter.BuildEntity"))
            .ToList();
    }

    /// <summary>Rule 3: a write of the column anywhere else in the BFF.</summary>
    internal static IReadOnlyList<Finding> ScanColumnWrites(string file, string source)
    {
        var code = MaskCommentsOnly(source);
        return ColumnWrite.Matches(code).Select(m => new Finding(file, LineOf(source, m.Index), "sprk_firmreason written outside SignalWriter.BuildEntity")).ToList();
    }

    // ── The tree ──

    private static IReadOnlyList<(string File, string Source)> BffSources()
    {
        var bff = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");
        return SourceScan.ServerSourceFiles()
            .Where(f => f.StartsWith(bff, StringComparison.OrdinalIgnoreCase))
            .Select(f => (SourceScan.Relative(f), File.ReadAllText(f)))
            .ToList();
    }

    [Fact(DisplayName = "130e-1 O3: FirmReason is assigned only by the billing mapper and the plan replay")]
    public void FirmReason_IsAssignedOnlyByTheMapperAndTheReplay()
    {
        var sources = BffSources();
        Assert.NotEmpty(sources);

        var findings = sources.SelectMany(s => ScanAssignments(s.File, s.Source)).ToList();
        Assert.True(findings.Count == 0,
            "SignalWriteRequest.FirmReason is partner-visible text; it may come only from the stored policy version after FirmDescriptionRules "
            + "(BillingFindingSignalMapper, BillingFirmReasonReplay). See the maintenance note in this file:\n"
            + string.Join("\n", findings.Select(f => $"  {f.File}:{f.Line}  {f.Rule}")));

        // Non-vacuity: both allowed sites exist and assign it.
        foreach (var allowed in FirmReasonAssigners.Keys)
        {
            var site = sources.Single(s => Path.GetFileName(s.File) == allowed);
            Assert.Matches(FirmReasonAssignment, DraftPolicyVersionStatecodeGuardTests.Mask(site.Source));
        }
    }

    [Fact(DisplayName = "130e-1 3.3(c): in SignalWriter.cs sprk_firmreason is named only inside BuildEntity (the create payload)")]
    public void SignalWriter_NamesTheColumnOnlyInBuildEntity()
    {
        var writer = BffSources().Single(s => s.File.EndsWith(Path.Combine("Services", "Signals", "SignalWriter.cs"), StringComparison.Ordinal));

        var findings = ScanSignalWriter(writer.File, writer.Source);
        Assert.True(findings.Count == 0,
            "sprk_firmreason is written on create only (design 3.3 (c)): the dedupe refresh and every update path must not name it:\n"
            + string.Join("\n", findings.Select(f => $"  {f.File}:{f.Line}  {f.Rule}")));

        // Non-vacuity: BuildEntity is found and does name it.
        Assert.Single(Occurrences(MaskCommentsOnly(writer.Source), Column));
        Assert.Matches(BuildEntityDeclaration, writer.Source);
    }

    [Fact(DisplayName = "130e-1 3.3(a)(b): no other BFF file writes sprk_firmreason (reading it is allowed)")]
    public void NoOtherFile_WritesTheColumn()
    {
        var findings = BffSources()
            .Where(s => !s.File.EndsWith(Path.Combine("Services", "Signals", "SignalWriter.cs"), StringComparison.Ordinal))
            .SelectMany(s => ScanColumnWrites(s.File, s.Source))
            .ToList();
        Assert.True(findings.Count == 0,
            "sprk_firmreason is written only by SignalWriter.BuildEntity, through the one writer client (design 3.3 (a)(b)):\n"
            + string.Join("\n", findings.Select(f => $"  {f.File}:{f.Line}  {f.Rule}")));
    }

    // ── Controls ──

    [Fact(DisplayName = "130e-1 guard control (must-fire): an initializer, a with expression and a compound line assigning FirmReason elsewhere are reported")]
    public void Control_AnAssignmentElsewhere_IsReported()
    {
        const string source = """
            var a = new SignalWriteRequest(p, c, v, e, s, n, h, l, sev, t, f) { FirmReason = body.Text };
            var b = a with { FirmReason = facts["x"] };
            var c = b with { Episode = 2, FirmReason=sentence };
            """;

        Assert.Equal(3, ScanAssignments("Services/Finance/SomethingElse.cs", source).Count);
    }

    [Fact(DisplayName = "130e-1 guard control (must-not-fire): a comparison, a declaration, a comment, a string and the allowed files are not reported")]
    public void Control_AllowedShapes_AreNotReported()
    {
        const string source = """
            public string? FirmReason { get; init; }
            if (request.FirmReason == null || request.FirmReason is { Length: 0 }) { }
            Func<SignalWriteRequest, string?> f = r => r.FirmReason;
            // a comment: request with { FirmReason = x }
            /// <summary>FirmReason = the text</summary>
            var s = "FirmReason = x";
            """;

        Assert.Empty(ScanAssignments("Services/Finance/SomethingElse.cs", source));
        Assert.Empty(ScanAssignments("Services/Signals/Billing/BillingFindingSignalMapper.cs", "request = request with { FirmReason = firmReason };"));
        Assert.Empty(ScanAssignments("Services/Signals/Billing/BillingFirmReasonReplay.cs", "return planned with { FirmReason = text };"));
    }

    [Fact(DisplayName = "130e-1 guard control (must-fire): sprk_firmreason in an update or refresh method of SignalWriter is reported")]
    public void Control_TheColumnOutsideBuildEntity_IsReported()
    {
        const string source = """
            private static Entity BuildEntity(SignalWriteRequest request)
            {
                var entity = new Entity("sprk_signal");
                if (request.FirmReason is not null) { entity["sprk_firmreason"] = request.FirmReason; }
                return entity;
            }

            private async Task RefreshAsync(Guid id, SignalWriteRequest request)
            {
                var fields = new Dictionary<string, object> { ["sprk_lastevaluated"] = now, ["sprk_firmreason"] = request.FirmReason! };
                await _client.UpdateAsync("sprk_signal", id, fields);
            }
            """;

        var findings = ScanSignalWriter("Services/Signals/SignalWriter.cs", source);

        Assert.Single(findings);
        Assert.Equal(10, findings[0].Line);
    }

    [Fact(DisplayName = "130e-1 guard control (must-not-fire): sprk_firmreason inside BuildEntity, in a comment or in a doc comment is not reported")]
    public void Control_TheColumnInBuildEntityOrAComment_IsNotReported()
    {
        const string source = """
            /// <summary>Writes <c>sprk_firmreason</c> on create; "sprk_firmreason" never on update.</summary>
            private static Entity BuildEntity(SignalWriteRequest request)
            {
                var entity = new Entity("sprk_signal");
                // the column "sprk_firmreason" rides this create only
                if (request.FirmReason is not null) { entity["sprk_firmreason"] = request.FirmReason; }
                return entity;
            }

            private async Task RefreshAsync(Guid id) // never "sprk_firmreason" here
            {
                await _client.UpdateAsync("sprk_signal", id, new() { ["sprk_lastevaluated"] = now });
            }
            """;

        Assert.Empty(ScanSignalWriter("Services/Signals/SignalWriter.cs", source));
    }

    [Fact(DisplayName = "130e-1 guard control (must-fire / must-not-fire): a write of sprk_firmreason elsewhere is reported; a read or a column list is not")]
    public void Control_ColumnWritesElsewhere()
    {
        const string writes = """
            row["sprk_firmreason"] = text;
            var fields = new Dictionary<string, object> { [ "sprk_firmreason" ] = text };
            fields.Add("sprk_firmreason", text);
            """;
        const string reads = """
            private static readonly string[] Columns = { "sprk_policyversion", "sprk_firmreason", "createdon" };
            var text = signal.GetAttributeValue<string>("sprk_firmreason");
            if (signal["sprk_firmreason"] == null) { }
            // row["sprk_firmreason"] = text;  (a comment)
            """;

        Assert.Equal(3, ScanColumnWrites("Services/Finance/SomethingElse.cs", writes).Count);
        Assert.Empty(ScanColumnWrites("Services/Finance/SomethingElse.cs", reads));
    }

    // ── Helpers ──

    private static int LineOf(string source, int index) => source[..index].Count(c => c == '\n') + 1;

    private static IEnumerable<int> Occurrences(string text, string token)
    {
        for (var i = text.IndexOf(token, StringComparison.Ordinal); i >= 0; i = text.IndexOf(token, i + token.Length, StringComparison.Ordinal))
        {
            yield return i;
        }
    }

    private static int MatchingBrace(string masked, int open)
    {
        var depth = 0;
        for (var i = open; i < masked.Length; i++)
        {
            if (masked[i] == '{')
            {
                depth++;
            }
            else if (masked[i] == '}' && --depth == 0)
            {
                return i;
            }
        }

        return masked.Length;
    }

    /// <summary>The source with every comment blanked (same length, line breaks kept); string literals are kept, so a column name in a string
    /// is still seen. A <c>//</c> inside a string is not a comment.</summary>
    internal static string MaskCommentsOnly(string source)
    {
        var sb = new StringBuilder(source);
        void Blank(int from, int to)
        {
            for (var k = from; k < to && k < sb.Length; k++)
            {
                if (sb[k] is not ('\n' or '\r'))
                {
                    sb[k] = ' ';
                }
            }
        }

        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                var end = source.IndexOf('\n', i);
                end = end < 0 ? source.Length : end;
                Blank(i, end);
                i = end;
            }
            else if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 2;
                Blank(i, end);
                i = end;
            }
            else if (c == '"' && i + 2 < source.Length && source[i + 1] == '"' && source[i + 2] == '"')
            {
                var quotes = 3;
                while (i + quotes < source.Length && source[i + quotes] == '"')
                {
                    quotes++;
                }

                var end = source.IndexOf(new string('"', quotes), i + quotes, StringComparison.Ordinal);
                i = end < 0 ? source.Length : end + quotes;
            }
            else if (c == '"')
            {
                var verbatim = i > 0 && source[i - 1] == '@';
                var j = i + 1;
                while (j < source.Length)
                {
                    if (!verbatim && source[j] == '\\')
                    {
                        j += 2;
                        continue;
                    }

                    if (source[j] == '"')
                    {
                        if (verbatim && j + 1 < source.Length && source[j + 1] == '"')
                        {
                            j += 2;
                            continue;
                        }

                        break;
                    }

                    j++;
                }

                i = j + 1;
            }
            else if (c == '\'')
            {
                var j = i + 1;
                while (j < source.Length && source[j] != '\'')
                {
                    j += source[j] == '\\' ? 2 : 1;
                }

                i = j + 1;
            }
            else
            {
                i++;
            }
        }

        return sb.ToString();
    }
}

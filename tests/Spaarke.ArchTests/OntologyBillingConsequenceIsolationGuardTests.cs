using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 073, ontology-r1 review K1: the billing consequence (<c>sprk_consequence</c>) never reaches an Ontology save or
/// evaluation path. Task 073 gave <c>PolicyVersionSnapshot</c> an optional sixth parameter, <c>Consequence</c>, and
/// <c>PolicyVersionValidator.ValidateForSave</c> an optional fifth, <c>billingConsequence</c>; both are billing-only. Inside
/// <c>Services/Ontology/**</c>, <c>Services/Signals/SignalEvaluator.cs</c> and <c>Services/Signals/SignalReevaluationJob.cs</c> and <c>Services/Signals/RuleBodyDescriber.cs</c> this guard
/// fails on: a <c>new PolicyVersionSnapshot(...)</c> with more than five arguments; a <c>ValidateForSave(...)</c> call with more than
/// four; and any <c>billingConsequence</c> token or <c>Consequence</c> passed as a named argument, an initializer or a <c>with</c>
/// member.
/// </summary>
/// <remarks>
/// Enforced by: this source scan (the optional parameters compile to the same IL whether a caller passes <c>null</c> or nothing, so an
/// IL scan cannot tell them apart). Crude by design (<see cref="SourceScan"/>): line comments are stripped, strings are not parsed; a
/// target-typed <c>new(...)</c> is not recognised as a snapshot (a code-review concern). Must-fire and must-not-fire controls below run
/// the same detector over synthetic snippets.
/// </remarks>
public sealed class OntologyBillingConsequenceIsolationGuardTests
{
    private const int SnapshotOntologyArgs = 5;
    private const int ValidateForSaveOntologyArgs = 4;

    private static readonly Regex SnapshotStart = new(@"\bnew\s+PolicyVersionSnapshot\s*\(", RegexOptions.Compiled);
    private static readonly Regex ValidateForSaveStart = new(@"\bValidateForSave\s*\(", RegexOptions.Compiled);
    private static readonly Regex BillingConsequenceToken = new(@"\bbillingConsequence\b", RegexOptions.Compiled);

    // `Consequence:` (named argument) or `Consequence =` (initializer / with), but not `x.Consequence`, `Consequence ==` or `=>`.
    private static readonly Regex ConsequencePassed = new(@"(?<![\w.])Consequence\s*(:(?!:)|=(?![=>]))", RegexOptions.Compiled);

    [Fact(DisplayName = "073 / ontology-r1 K1: no Ontology path builds a snapshot or a save validation with the billing consequence")]
    public void OntologyPaths_NeverPassTheBillingConsequence()
    {
        var files = ScopedFiles().ToList();
        Assert.True(files.Count >= 3, $"The scan found only {files.Count} file(s); repo root resolved to '{SourceScan.RepoRoot}'.");

        var evaluator = files.Single(f => f.EndsWith("SignalEvaluator.cs", StringComparison.Ordinal));
        Assert.True(SnapshotStart.IsMatch(SourceScan.CodeText(File.ReadAllLines(evaluator))),
            "Positive control: SignalEvaluator.cs builds a PolicyVersionSnapshot, so the scan sees the shape it guards.");

        var violations = files
            .SelectMany(f => Detect(SourceScan.CodeText(File.ReadAllLines(f))).Select(v => $"{SourceScan.Relative(f)}:{v}"))
            .ToList();
        Assert.True(violations.Count == 0,
            "The billing consequence reaches an Ontology path (it is billing-only, task 073): " + string.Join("; ", violations) +
            ". Build PolicyVersionSnapshot with five arguments and call ValidateForSave without billingConsequence on Ontology paths.");
    }

    [Theory(DisplayName = "073 / ontology-r1 K1: the detector fires on each forbidden shape (must-fire controls)")]
    [InlineData("var s = new PolicyVersionSnapshot(id, code, type, body, template, 100000000);")]
    [InlineData("var s = new PolicyVersionSnapshot(\n    id, code, type,\n    body, template, consequence);")]
    [InlineData("var s = new PolicyVersionSnapshot(id, code, type, body, template, Consequence: c);")]
    [InlineData("var s = new PolicyVersionSnapshot(id, code, type, body, template) { Consequence = 100000000 };")]
    [InlineData("var t = s with { Consequence = 100000000 };")]
    [InlineData("var r = _validator.ValidateForSave(type, body, template, RuleDomain.Ontology, 100000000);")]
    [InlineData("var r = _validator.ValidateForSave(type, body, template, billingConsequence: c);")]
    [InlineData("int? billingConsequence = row.Consequence;")]
    public void TheDetectorFires_OnEveryForbiddenShape(string snippet)
    {
        Assert.NotEmpty(Detect(snippet));
    }

    [Theory(DisplayName = "073 / ontology-r1 K1: the detector stays quiet on the sanctioned shapes (must-not-fire controls)")]
    [InlineData("var s = new PolicyVersionSnapshot(policy.PolicyVersionId, policy.PolicyCode, policy.RuleType, F(a, b), policy.MessageTemplate);")]
    [InlineData("var r = _validator.ValidateForSave(type, body, template, RuleDomain.Ontology);")]
    [InlineData("if (pv.Consequence == null) { return; }")]
    [InlineData("var x = pv.Consequence;")]
    [InlineData("Func<int?> f = () => Consequence => 1;")]
    public void TheDetectorDoesNotFire_OnTheSanctionedShapes(string snippet)
    {
        Assert.Empty(Detect(snippet));
    }

    private static IEnumerable<string> ScopedFiles()
    {
        var api = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", "Services");
        var ontology = Path.Combine(api, "Ontology");
        var files = Directory.Exists(ontology)
            ? Directory.EnumerateFiles(ontology, "*.cs", SearchOption.AllDirectories).ToList()
            : new List<string>();
        files.Add(Path.Combine(api, "Signals", "SignalEvaluator.cs"));
        files.Add(Path.Combine(api, "Signals", "SignalReevaluationJob.cs"));
        files.Add(Path.Combine(api, "Signals", "RuleBodyDescriber.cs")); // the other non-billing ValidateForSave caller (ontology-r1 K2)
        return files.Where(File.Exists);
    }

    /// <summary>Every violation in <paramref name="code"/> (comments already stripped), as "line: what".</summary>
    internal static List<string> Detect(string code)
    {
        var found = new List<string>();
        foreach (Match m in SnapshotStart.Matches(code))
        {
            var count = TopLevelArgumentCount(code, m.Index + m.Length);
            if (count > SnapshotOntologyArgs)
            {
                found.Add($"{SourceScan.LineOf(code, m.Index)}: new PolicyVersionSnapshot with {count} arguments");
            }
        }

        foreach (Match m in ValidateForSaveStart.Matches(code))
        {
            var count = TopLevelArgumentCount(code, m.Index + m.Length);
            if (count > ValidateForSaveOntologyArgs)
            {
                found.Add($"{SourceScan.LineOf(code, m.Index)}: ValidateForSave with {count} arguments");
            }
        }

        found.AddRange(BillingConsequenceToken.Matches(code).Select(m => $"{SourceScan.LineOf(code, m.Index)}: billingConsequence"));
        found.AddRange(ConsequencePassed.Matches(code).Select(m => $"{SourceScan.LineOf(code, m.Index)}: Consequence passed"));
        return found;
    }

    /// <summary>The number of top-level arguments of the call whose argument list starts at <paramref name="start"/> (just after its '(').</summary>
    private static int TopLevelArgumentCount(string code, int start)
    {
        var depth = 0;
        var commas = 0;
        var any = false;
        for (var i = start; i < code.Length; i++)
        {
            var c = code[i];
            switch (c)
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    if (depth == 0)
                    {
                        return any ? commas + 1 : 0;
                    }

                    depth--;
                    break;
                case ',' when depth == 0:
                    commas++;
                    break;
            }

            if (!char.IsWhiteSpace(c))
            {
                any = true;
            }
        }

        return any ? commas + 1 : 0;
    }
}

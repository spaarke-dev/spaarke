using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Issue #1295 — every JSON Schema meta-schema evaluation in the BFF goes through
/// <c>Draft202012MetaSchemaValidator</c>.
/// </summary>
/// <remarks>
/// <para>Json.Schema.Net 7.3.4 <c>JsonSchema.Evaluate</c> is not thread-safe on a shared schema
/// instance, and the <c>MetaSchemas.*</c> members are process-wide statics. Two call sites evaluated
/// <c>MetaSchemas.Draft202012</c> without a lock and, under concurrency, accepted invalid tool schemas
/// (fail-open). The fix is ONE gate in <c>Services/Ai/Draft202012MetaSchemaValidator.cs</c> — a lock per
/// call site is measurably not enough, because the sites race each other on the same instance. So a
/// second direct evaluation is not a style issue: it re-opens the race even if it carries its own
/// lock.</para>
///
/// <para>Two shapes reach the shared meta-schemas: naming a <c>MetaSchemas.</c> member, and
/// <c>EvaluationOptions.ValidateAgainstMetaSchema = true</c>, which makes the library evaluate the
/// meta-schema internally. Evaluating a schema the caller owns with default options (e.g. the ontology
/// branch's <c>RuleBodySchemaValidator</c> over its own embedded schema) touches neither and is not
/// flagged.</para>
///
/// <para><b>Crude by design</b> (see <see cref="SourceScan"/>): a text scan with <c>//</c> comments
/// stripped, paired with a negative control proving it fires and a positive control proving it does not
/// fire on the sanctioned shapes or on the real tree.</para>
/// </remarks>
public class MetaSchemaEvaluationGuardTests
{
    private const string BffRoot = "src/server/api/Sprk.Bff.Api/";

    /// <summary>The one file allowed to evaluate the shared meta-schemas.</summary>
    private const string CanonicalGate = "src/server/api/Sprk.Bff.Api/Services/Ai/Draft202012MetaSchemaValidator.cs";

    private static readonly Regex NamesAMetaSchema = new(@"\bMetaSchemas\.", RegexOptions.Compiled);

    private static readonly Regex AsksForMetaSchemaValidation = new(
        @"\bValidateAgainstMetaSchema\s*=\s*true\b",
        RegexOptions.Compiled);

    [Fact(DisplayName = "#1295: only Draft202012MetaSchemaValidator evaluates the shared JSON Schema meta-schemas")]
    public void OnlyTheGateEvaluatesTheSharedMetaSchemas()
    {
        var violations = ScanTree().Where(v => !v.StartsWith(CanonicalGate + ":", StringComparison.Ordinal)).ToList();

        Assert.True(
            violations.Count == 0,
            "JsonSchema.Net's Evaluate is not thread-safe on a shared schema instance, and MetaSchemas.* are "
            + "process-wide statics (issue #1295: under concurrency invalid tool schemas evaluated as valid). "
            + "Every meta-schema evaluation must go through Draft202012MetaSchemaValidator.Evaluate — a lock "
            + "at the new call site is NOT enough, because it would still race the existing gate on the same "
            + "instance.\n\nOffending sites:\n  " + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "#1295 negative control: the detector fires on a direct MetaSchemas evaluation and on ValidateAgainstMetaSchema = true")]
    public void Detector_NegativeControl_FiresOnBothShapes()
    {
        Assert.NotEmpty(ScanText(BffRoot + "Services/Fake/A.cs", """
            var results = MetaSchemas.Draft202012.Evaluate(node, options);
            """));

        Assert.NotEmpty(ScanText(BffRoot + "Services/Fake/B.cs", """
            var results = Json.Schema.MetaSchemas.Draft7.Evaluate(node);
            """));

        Assert.NotEmpty(ScanText(BffRoot + "Services/Fake/C.cs", """
            var results = schema.Evaluate(node, new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                ValidateAgainstMetaSchema = true
            });
            """));

        // The real gate file is found too — proves the scan reads the tree and the pattern still
        // matches the canonical shape, so the main rule's pass is not vacuous.
        Assert.Contains(ScanTree(), v => v.StartsWith(CanonicalGate + ":", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "#1295 positive control: the detector does not fire on doc comments, on false, on the adapter's method name, on a caller-owned schema, or on the real tree")]
    public void Detector_PositiveControl_DoesNotFireOnTheSanctionedShapes()
    {
        // (1) Prose in a doc comment naming the member (ToolHandlerToAIFunctionAdapter has exactly this).
        Assert.Empty(ScanText(BffRoot + "Services/Fake/D.cs", """
            /// <item>Uses <see cref="MetaSchemas.Draft202012"/> through the shared validator.</item>
            // ValidateAgainstMetaSchema = true would evaluate the meta-schema internally.
            """));

        // (2) The explicit false the gate itself passes, and the adapter's private method of a similar name.
        Assert.Empty(ScanText(BffRoot + "Services/Fake/E.cs", """
            var options = new EvaluationOptions { ValidateAgainstMetaSchema = false };
            ValidateAgainstMetaSchema(tool);
            """));

        // (3) A caller-owned schema evaluated with default options — the shape of the ontology branch's
        //     RuleBodySchemaValidator (its own static instance, under its own lock).
        Assert.Empty(ScanText(BffRoot + "Services/Signals/Fake/F.cs", """
            lock (EvaluationGate)
            {
                results = schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });
            }
            """));

        // (4) THE REAL PRODUCTION TREE, minus the gate itself.
        Assert.Empty(ScanTree().Where(v => !v.StartsWith(CanonicalGate + ":", StringComparison.Ordinal)));
    }

    // =================================================================================================
    // MACHINERY
    // =================================================================================================

    private static List<string> ScanTree()
        => SourceScan.ServerSourceFiles()
            .Select(f => (Full: f, Relative: Path.GetRelativePath(SourceScan.RepoRoot, f).Replace('\\', '/')))
            .Where(f => f.Relative.StartsWith(BffRoot, StringComparison.Ordinal))
            .SelectMany(f => ScanText(f.Relative, File.ReadAllText(f.Full)))
            .ToList();

    private static List<string> ScanText(string relativeFile, string rawText)
    {
        var violations = new List<string>();
        var lines = rawText.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var code = SourceScan.StripLineComment(lines[i]);
            if (NamesAMetaSchema.IsMatch(code) || AsksForMetaSchemaValidation.IsMatch(code))
            {
                violations.Add($"{relativeFile}:{i + 1}: {code.Trim()}");
            }
        }

        return violations;
    }
}

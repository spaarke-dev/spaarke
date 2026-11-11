using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 081 (procedure model 15 answer 2, "preview = sweep"): the Procedure retire confirmation counts from the SAME open-Signal
/// selection the close acts on, so the number a billing admin confirms can never describe a different set from the one that closes.
/// </summary>
/// <remarks>
/// <para><b>Enforced</b> (source scan of <c>Sprk.Bff.Api</c>, line comments stripped):</para>
/// <list type="number">
/// <item><c>ProcedureService.PreviewRetireAsync</c> selects through <c>SignalClosureService.SelectOpenSignalsOfPolicyAsync</c>.</item>
/// <item><c>ProcedureService.RetireAsync</c> closes through <c>SignalClosureService.ClosePolicySignalsRetiredAsync</c>.</item>
/// <item><c>ClosePolicySignalsRetiredAsync</c> itself selects through <c>SelectOpenSignalsOfPolicyAsync</c> and builds no query of its own.</item>
/// <item>No file under <c>Services/Finance/Procedures/</c> queries <c>sprk_signal</c> directly (no second selection to drift).</item>
/// </list>
/// <para><b>Not enforced:</b> what the shared selection selects (that is the domain tests' job:
/// <c>SignalReevaluationJobTests.ProcedureRetire</c>), reflection, and other assemblies.</para>
/// <para><b>Controls.</b> <see cref="Detectors_FireOnTheForbiddenShapes"/> (must-fire) and <see cref="Detectors_DoNotFireOnTheSanctionedShapes"/>
/// (must-not-fire) run each detector on a synthetic source, so a detector that stops matching fails instead of passing vacuously.</para>
/// </remarks>
public class ProcedureRetireSelectionGuardTests
{
    private static readonly string ProceduresDir =
        Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", "Services", "Finance", "Procedures");

    private static readonly string ProcedureServicePath = Path.Combine(ProceduresDir, "ProcedureService.cs");

    private static readonly string ClosurePath =
        Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", "Services", "Signals", "SignalClosureService.cs");

    private const string Select = "SelectOpenSignalsOfPolicyAsync";
    private const string Close = "ClosePolicySignalsRetiredAsync";

    [Fact(DisplayName = "081: the Procedure retire preview and close share the close's own open-Signal selection")]
    public void PreviewAndClose_ShareTheSelection()
    {
        var procedure = Code(File.ReadAllText(ProcedureServicePath));
        var closure = Code(File.ReadAllText(ClosurePath));

        Assert.True(Calls(MethodBody(procedure, "PreviewRetireAsync"), Select),
            $"ProcedureService.PreviewRetireAsync must count from SignalClosureService.{Select} (model 15 answer 2: preview = sweep).");
        Assert.True(Calls(MethodBody(procedure, "RetireAsync"), Close),
            $"ProcedureService.RetireAsync must close through SignalClosureService.{Close}.");

        var closeBody = MethodBody(closure, Close);
        Assert.True(Calls(closeBody, Select), $"SignalClosureService.{Close} must select through {Select}.");
        Assert.False(BuildsAQuery(closeBody), $"SignalClosureService.{Close} must not build a selection of its own.");
    }

    [Fact(DisplayName = "081: nothing under Services/Finance/Procedures queries sprk_signal directly")]
    public void NoProcedureFile_QueriesSignalsDirectly()
    {
        var files = Directory.EnumerateFiles(ProceduresDir, "*.cs", SearchOption.AllDirectories).ToList();
        Assert.Contains(files, f => f.EndsWith("ProcedureService.cs", StringComparison.Ordinal)); // non-vacuity

        var offenders = files.Where(f => NamesTheSignalTable(Code(File.ReadAllText(f)))).Select(Path.GetFileName).ToList();

        Assert.True(offenders.Count == 0,
            $"A Procedure file names the sprk_signal table: {string.Join(", ", offenders)}. Select open Signals through " +
            $"SignalClosureService.{Select} so the retire preview and the close cannot disagree (task 081).");
    }

    [Fact(DisplayName = "081 must-fire: each detector reports its forbidden shape")]
    public void Detectors_FireOnTheForbiddenShapes()
    {
        const string OwnQuery = """
            public async Task<int> PreviewRetireAsync(Guid id, string? token, CancellationToken ct)
            {
                var query = new QueryExpression("sprk_signal");
                return (await _entities.RetrieveMultipleAsync(query, ct)).Entities.Count;
            }
            """;
        Assert.False(Calls(MethodBody(Code(OwnQuery), "PreviewRetireAsync"), Select));
        Assert.True(NamesTheSignalTable(Code(OwnQuery)));
        Assert.True(NamesTheSignalTable(Code("var e = SignalWriter.SignalEntity;")));
        Assert.True(BuildsAQuery(MethodBody(Code(OwnQuery), "PreviewRetireAsync")));
    }

    [Fact(DisplayName = "081 must-not-fire: the sanctioned call and a comment that names the table pass")]
    public void Detectors_DoNotFireOnTheSanctionedShapes()
    {
        const string Sanctioned = """
            // the close selects open "sprk_signal" rows through the shared selection
            public async Task<int> PreviewRetireAsync(Guid id, string? token, CancellationToken ct)
            {
                if (id == Guid.Empty) { return 0; }
                var open = await _closure.SelectOpenSignalsOfPolicyAsync(id, ct);
                return open.Count;
            }

            public async Task<int> RetireAsync(Guid id, CancellationToken ct) => 0;
            """;
        var code = Code(Sanctioned);
        Assert.True(Calls(MethodBody(code, "PreviewRetireAsync"), Select));
        Assert.False(NamesTheSignalTable(code));
        Assert.False(BuildsAQuery(MethodBody(code, "PreviewRetireAsync")));
        Assert.DoesNotContain(Select, MethodBody(code, "RetireAsync")); // bodies are not run together
    }

    // ── Detectors ────────────────────────────────────────────────────────────────────────────────────

    private static string Code(string source) =>
        string.Join('\n', source.Split('\n').Select(SourceScan.StripLineComment));

    private static bool Calls(string body, string method) => Regex.IsMatch(body, $@"\b{method}\s*\(");

    private static bool BuildsAQuery(string body) => Regex.IsMatch(body, @"new\s+(QueryExpression|FetchExpression)\s*\(");

    private static bool NamesTheSignalTable(string code) =>
        code.Contains("\"sprk_signal\"", StringComparison.Ordinal) || Regex.IsMatch(code, @"\bSignalWriter\.SignalEntity\b");

    /// <summary>The body of the method DECLARED with this name (a declaration, not a call: a return type precedes it), brace-matched.</summary>
    private static string MethodBody(string code, string name)
    {
        var declaration = Regex.Match(code, $@"\b(Task|void|int)(<[^\n(]*>)?\s+{name}\s*\(");
        Assert.True(declaration.Success, $"No declaration of {name} was found; the guard needs updating.");
        var parens = 1; // skip the parameter list (a "= default" in it is not an expression body)
        var i0 = declaration.Index + declaration.Length;
        for (; parens > 0; i0++)
        {
            parens += code[i0] switch { '(' => 1, ')' => -1, _ => 0 };
        }

        var open = code.IndexOfAny(new[] { '{', '=' }, i0);
        if (code[open] == '=')
        {
            var end = code.IndexOf(';', open);
            return code[open..end];
        }

        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            depth += code[i] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                return code[open..(i + 1)];
            }
        }

        return code[open..];
    }
}

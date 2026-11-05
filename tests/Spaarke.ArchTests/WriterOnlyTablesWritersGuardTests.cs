using NetArchTest.Rules;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 042c fix round 4 (owner decision 2026-11-04, ontology D-204): a Signal, a Decision
/// Record and a Budget Revision are created only by their writers, and the general create refuses those tables
/// (<see cref="RecordTableRule.CreatedOnlyByItsWriter"/>). So the writers themselves must never reach the general create: if one
/// did, the refusal would stop it. This guard names each writer and proves none of them depends on the general create.
/// </summary>
/// <remarks>
/// <para><b>The writers, named.</b> <c>SignalWriter</c> (Signals), <c>DecisionRecordWriter</c> (Decision Records),
/// <c>ReviseBudgetExecutor</c> (the decision action executor that creates a Budget Revision) and
/// <c>OntologyWriterDataverseClient</c> (the writer identity the Budget Revision is created with).</para>
/// <para><b>The general create.</b> <c>RecordCreationService</c> and its request, the in-process child-records route
/// (<c>ChildRecordEndpoints</c>, and <c>DecisionRouteCores</c> whose <c>CreateChildAsync</c> calls it), and the chat
/// <c>dataverse.create_record</c> tool (<c>DataverseCreateRecordHandler</c>).</para>
/// <para><b>Signal.</b> Two scans, so a miss in one is caught by the other. (1) IL: NetArchTest's dependency search over the
/// compiled BFF, for each writer type AND every type nested in it (an async method's body compiles into a nested state
/// machine), so a call through a local, a cast or <c>GetRequiredService&lt;RecordCreationService&gt;()</c> is seen. (2) Source:
/// the writers' files (the Budget Revision executor's class span only, since the other executors in its file do use the
/// general create on purpose) hold none of the general create's names in code text (line comments stripped).</para>
/// <para><b>Limits.</b> A dependency reached only through ANOTHER type the writer calls (a helper that itself creates through
/// the service) is not followed; the writers' behaviour tests (the Budget Revision commit in
/// <c>SecureChildOwnershipAiToolTests.DecisionCommit</c>) cover that path. Enforced by: this guard (ArchTests in CI and the
/// Stop hook). No baseline: there are no violations today.</para>
/// <para><b>Controls.</b> Must fire: a seeded fixture whose async method calls <c>RecordCreationService.CreateAsync</c> (only
/// inside its state machine) is found by the IL scan, and seeded source lines are found by the source scan. Must not fire: a
/// fixture with an async method that never names the general create, and source that only mentions it in a comment.</para>
/// </remarks>
public class WriterOnlyTablesWritersGuardTests
{
    /// <summary>The writers of the tables only their writers create, by full type name.</summary>
    internal static readonly string[] Writers =
    [
        "Sprk.Bff.Api.Services.Signals.SignalWriter",
        "Sprk.Bff.Api.Services.Signals.DecisionRecordWriter",
        "Sprk.Bff.Api.Services.Signals.Actions.ReviseBudgetExecutor",
        "Sprk.Bff.Api.Services.Signals.OntologyWriterDataverseClient",
    ];

    /// <summary>The general create, by full type name (a dependency on any of these is a route into it).</summary>
    internal static readonly string[] GeneralCreate =
    [
        "Sprk.Bff.Api.Services.RecordCreation.RecordCreationService",
        "Sprk.Bff.Api.Services.RecordCreation.RecordCreationRequest",
        "Sprk.Bff.Api.Api.ChildRecordEndpoints",
        "Sprk.Bff.Api.Services.Signals.Actions.DecisionRouteCores",
        "Sprk.Bff.Api.Services.Ai.Handlers.DataverseCreateRecordHandler",
    ];

    /// <summary>The writers' source: the file, and the class whose span is scanned (null: the whole file).</summary>
    internal static readonly (string File, string? Class)[] WriterSources =
    [
        ("src/server/api/Sprk.Bff.Api/Services/Signals/SignalWriter.cs", null),
        ("src/server/api/Sprk.Bff.Api/Services/Signals/DecisionRecordWriter.cs", null),
        ("src/server/api/Sprk.Bff.Api/Services/Signals/OntologyWriterDataverseClient.cs", null),
        ("src/server/api/Sprk.Bff.Api/Services/Signals/Actions/DecisionActionExecutors.cs", "ReviseBudgetExecutor"),
    ];

    private static readonly string[] GeneralCreateNames =
        ["RecordCreationService", "RecordCreationRequest", "ChildRecordEndpoints", "DecisionRouteCores", "CreateChildAsync",
         "DataverseCreateRecordHandler"];

    /// <summary>IL: the types in <paramref name="assembly"/> named in <paramref name="writers"/> (or nested in one of them) that
    /// depend on the general create, by the writer they belong to.</summary>
    internal static IReadOnlyList<string> WritersThatDependOnTheGeneralCreate(
        System.Reflection.Assembly assembly, IReadOnlyCollection<string> writers)
    {
        var dependents = Types.InAssembly(assembly).That().HaveDependencyOnAny(GeneralCreate).GetTypes();
        return dependents
            .Select(OutermostOf)
            .Where(t => t.FullName is { } name && writers.Contains(name, StringComparer.Ordinal))
            .Select(t => t.FullName!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        static Type OutermostOf(Type type)
        {
            while (type.DeclaringType is { } outer)
                type = outer;
            return type;
        }
    }

    /// <summary>Source: each line of <paramref name="source"/> (comments stripped; the span of class <paramref name="className"/>
    /// when given) that names the general create.</summary>
    internal static IReadOnlyList<string> GeneralCreateNamesIn(string source, string? className)
    {
        var code = SourceScan.CodeText(source.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'));
        var start = 0;
        var end = code.Length;
        if (className is not null)
        {
            var declaration = System.Text.RegularExpressions.Regex.Match(code, @"\bclass\s+" + className + @"\b");
            if (!declaration.Success)
                return [$"class {className} not found: the scan lost its signal"];
            start = declaration.Index;
            var next = System.Text.RegularExpressions.Regex.Match(code[(start + declaration.Length)..],
                @"^(?:public|internal)\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+)*(?:class|record|enum|interface)\s",
                System.Text.RegularExpressions.RegexOptions.Multiline);
            end = next.Success ? start + declaration.Length + next.Index : code.Length;
        }

        var span = code[start..end];
        var hits = new List<string>();
        foreach (var name in GeneralCreateNames)
        {
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(span, @"\b" + name + @"\b"))
                hits.Add($"line {SourceScan.LineOf(code, start + m.Index)}: names {name}");
        }

        return hits;
    }

    [Fact(DisplayName = "042c fix round 4 (D-204): SignalWriter, DecisionRecordWriter, ReviseBudgetExecutor and the writer identity never depend on the general create (IL)")]
    public void TheWriters_NeverDependOnTheGeneralCreate_IL()
    {
        var bff = typeof(RecordCreationService).Assembly;

        // Non-vacuity: every named writer is a type of the BFF.
        foreach (var writer in Writers)
            Assert.True(bff.GetType(writer) is not null, $"{writer} is not a type of the BFF: the guard lost its signal.");

        var found = WritersThatDependOnTheGeneralCreate(bff, Writers);
        Assert.True(found.Count == 0,
            "A Signal, Decision Record or Budget Revision is created only by its writer, and the general create refuses those "
            + "tables (RecordTableRule.CreatedOnlyByItsWriter; owner decision D-204), so a writer may not reach it: "
            + string.Join(", ", found));
    }

    [Fact(DisplayName = "042c fix round 4 (D-204): the writers' source never names the general create")]
    public void TheWriters_NeverNameTheGeneralCreate_Source()
    {
        var problems = new List<string>();
        foreach (var (file, className) in WriterSources)
        {
            var path = Path.Combine(SourceScan.RepoRoot, file.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"{file} does not exist: the guard lost its signal.");
            problems.AddRange(GeneralCreateNamesIn(File.ReadAllText(path), className).Select(p => $"{file}: {p}"));
        }

        Assert.True(problems.Count == 0,
            "The writers of the tables only their writers create may not name the general create:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "042c fix round 4 control (must fire): a seeded call of RecordCreationService inside an async method is found")]
    public void Control_ASeededCallOfTheGeneralCreate_IsFound()
    {
        var tests = typeof(WriterOnlyTablesWritersGuardTests).Assembly;

        Assert.Equal([typeof(WriterGuardSeededFixture).FullName!],
            WritersThatDependOnTheGeneralCreate(tests, [typeof(WriterGuardSeededFixture).FullName!, typeof(WriterGuardCleanFixture).FullName!]));

        Assert.Single(GeneralCreateNamesIn("""
            public sealed class W
            {
                private async Task M(object s) => await ((RecordCreationService)s).CreateAsync(null!, null!, default);
            }
            """, null));
        Assert.Single(GeneralCreateNamesIn("""
            public sealed class ReviseBudgetExecutor
            {
                private Task M(HttpContext h, DecisionRouteCores c) => Task.CompletedTask;
            }
            public sealed class CreateBudgetExecutor { }
            """, "ReviseBudgetExecutor"));
    }

    [Fact(DisplayName = "042c fix round 4 control (must not fire): a writer that never names the general create, and a comment, are not flagged")]
    public void Control_AWriterThatNeverNamesTheGeneralCreate_IsNotFound()
    {
        var tests = typeof(WriterOnlyTablesWritersGuardTests).Assembly;

        Assert.Empty(WritersThatDependOnTheGeneralCreate(tests, [typeof(WriterGuardCleanFixture).FullName!]));

        Assert.Empty(GeneralCreateNamesIn("""
            public sealed class ReviseBudgetExecutor
            {
                // never through RecordCreationService
                private Task M() => Task.CompletedTask;
            }
            public sealed class CreateBudgetExecutor
            {
                private Task N(DecisionRouteCores c) => c.CreateChildAsync(null!, "sprk_budget", default, default);
            }
            """, "ReviseBudgetExecutor"));
    }
}

/// <summary>Must-fire fixture: the general create is named only inside an async method's body (its state machine).</summary>
internal sealed class WriterGuardSeededFixture
{
    private readonly object _service = new();

    internal async Task<bool> WriteAsync() =>
        (await ((RecordCreationService)_service).CreateAsync(null!, null!, CancellationToken.None)).Succeeded;
}

/// <summary>Must-not-fire fixture: an async writer that never names the general create.</summary>
internal sealed class WriterGuardCleanFixture
{
    internal async Task<int> WriteAsync()
    {
        await Task.Yield();
        return 1;
    }
}

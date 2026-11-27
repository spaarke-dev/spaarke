using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ISS-010 part C (uac-r2 design §6 and amendment A3): nothing that runs AFTER the record create asks a caller-access
/// question. The decision commit's access window keeps a caller's access answers between the writes of one executor step
/// and clears them after each step; that is sound only because a step's access questions all come before its one write.
/// A post-create service (or the tail of <c>RecordCreationService.CreateAsync</c>) that asked "may the caller ...?" would
/// get an answer from before the write, so this guard fails it.
/// </summary>
/// <remarks>
/// <para><b>Part 1, the step-12 services.</b> The files of <c>CoreAncestorRestamper</c>, <c>SecureChildShareSynchronizer</c>,
/// <c>AssignedAccessMaterializer</c>, <c>SecureRootFilingGate</c> and <c>SecureRootInheritance</c> (every partial) must name
/// none of <c>IDataverseUserClient</c>, <c>CallerRecordAccessProbe</c> or <c>CallerWriteAuthorization</c>. The scan is over
/// identifier tokens, so comments, XML documentation and string text are not seen.</para>
/// <para><b>Part 2, <c>RecordCreationService</c> after step 11.</b> Roslyn, over every partial of the class. The "tail" is every
/// top-level statement of <c>CreateAsync</c> after the one that holds the step 11 app-only create (the statement that calls
/// <c>UpdateRecordFieldsAsync</c>). The scan follows the tail into every method of the class that it names (by simple name,
/// transitively: step 12's <c>AfterCreateAsync</c> and what it calls) and fails when any of that code names one of the three
/// types above. In the tail itself it also fails on <c>principal</c> and <c>user</c>, the two ways <c>CreateAsync</c> holds
/// the caller's client. If the step 11 statement is not found exactly once, the guard fails (it never passes without a tail).</para>
/// <para><b>Limits.</b> A name match, not a type-resolved one: a call into another class is not followed (the services above
/// are covered by part 1, and any new class a post-create step reaches must be added to <c>StepTwelveServices</c>); a method
/// the tail names is followed by name, so two methods with one name are both checked (over-approximation, never under). A
/// caller-access question asked through a type this guard does not list is not seen; the three types are the whole of the
/// caller-access surface the part C window serves.</para>
/// <para>Enforced by: this guard (ArchTests, CI and the Stop hook). Must-fire and must-not-fire controls below.</para>
/// </remarks>
public class PostCreateNoCallerAccessGuardTests
{
    private static readonly string[] CallerAccessTypes =
        ["IDataverseUserClient", "CallerRecordAccessProbe", "CallerWriteAuthorization"];

    /// <summary>In the tail of <c>CreateAsync</c> only: the names under which it holds the caller's client.</summary>
    private static readonly string[] CallerHolders = ["principal", "user"];

    /// <summary>The step 12 services, by class name; each is scanned in every file named <c>Name.cs</c> or <c>Name.*.cs</c>.</summary>
    private static readonly string[] StepTwelveServices =
    [
        "CoreAncestorRestamper",
        "SecureChildShareSynchronizer",
        "AssignedAccessMaterializer",
        "SecureRootFilingGate",
        "SecureRootInheritance",
    ];

    private const string CreateMethod = "CreateAsync";
    private const string StepElevenCall = "UpdateRecordFieldsAsync";

    private static readonly string BffRoot = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");

    // ── Part 1: the step 12 services ──────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "ISS-010 C: the step-12 post-create services reference no caller-access type")]
    public void StepTwelveServices_ReferenceNoCallerAccessType()
    {
        var violations = new List<string>();
        foreach (var service in StepTwelveServices)
        {
            var files = ServiceFiles(service).ToList();
            Assert.True(files.Count > 0, $"No source file found for the step 12 service {service}; rename it here too.");
            foreach (var file in files)
                violations.AddRange(ServiceViolations(SourceScan.Relative(file), File.ReadAllText(file)));
        }

        Assert.True(violations.Count == 0,
            "A step 12 post-create service asks a caller-access question. It runs after the record's create, so the decision "
            + "commit's access window (ISS-010 part C) could serve it an answer from before that write:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "ISS-010 C: the step-12 scan fires on each caller-access type and not on a comment or string")]
    public void ServiceScan_MustFireAndMustNotFire()
    {
        foreach (var type in CallerAccessTypes)
        {
            var fired = ServiceViolations("fixture.cs", $"class Svc {{ private readonly {type} _x; }}");
            Assert.Single(fired);
            Assert.Contains(type, fired[0], StringComparison.Ordinal);
        }

        Assert.Single(ServiceViolations("fixture.cs", "class Svc { async Task Go() { await CallerWriteAuthorization.WhoAmIAsync(c, ct); } }"));

        const string clean = """
            /// <summary>Not <see cref="CallerWriteAuthorization"/> and not IDataverseUserClient.</summary>
            class Svc
            {
                // CallerRecordAccessProbe is mentioned in a comment only.
                private const string Text = "IDataverseUserClient CallerWriteAuthorization";
                private readonly IDataverseWebApiClient _webApi;
            }
            """;
        Assert.Empty(ServiceViolations("fixture.cs", clean));
    }

    // ── Part 2: RecordCreationService after step 11 ───────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "ISS-010 C: RecordCreationService after the step-11 create asks no caller-access question")]
    public void RecordCreationService_AfterStepEleven_AsksNoCallerAccessQuestion()
    {
        var files = Directory.EnumerateFiles(Path.Combine(BffRoot, "Services", "RecordCreation"), "RecordCreationService*.cs").ToList();
        Assert.NotEmpty(files);
        var result = TailViolations(files.Select(f => (SourceScan.Relative(f), File.ReadAllText(f))).ToList());

        Assert.True(result.AnchorFound,
            $"RecordCreationService.{CreateMethod} no longer has exactly one top-level statement calling {StepElevenCall} (the step 11 "
            + "app-only create), so the post-create tail cannot be found. Update this guard to the new shape; do not skip it.");

        // A pass must not be vacuous: the tail must reach step 12's method and what it calls.
        Assert.Contains("AfterCreateAsync", result.MethodsChecked);
        Assert.Contains("GrantAssignedAccessAsync", result.MethodsChecked);

        Assert.True(result.Violations.Count == 0,
            "RecordCreationService's code after the step 11 create asks (or can reach) a caller-access question. The decision "
            + "commit's access window (ISS-010 part C) could serve it an answer from before that write:\n  "
            + string.Join("\n  ", result.Violations));
    }

    [Fact(DisplayName = "ISS-010 C: the tail scan fires directly, through a callee and on the caller's client, and not before the create")]
    public void TailScan_MustFireAndMustNotFire()
    {
        // Must fire: a direct reference in the tail.
        Assert.Single(Tail(Fixture(after: "await CallerWriteAuthorization.WhoAmIAsync(null, ct);")).Violations);

        // Must fire: the tail names the caller's client by its holder name.
        Assert.NotEmpty(Tail(Fixture(after: "await principal.Client.GetAsync(ct);")).Violations);
        Assert.NotEmpty(Tail(Fixture(after: "await user.GetAsync(ct);")).Violations);

        // Must fire: through a callee, two hops down, and through a method group.
        const string hops = """
            private async Task Post() { await Deeper(); }
            private async Task Deeper() { var p = CallerRecordAccessProbe.Create(); await Task.CompletedTask; }
            """;
        var transitive = Tail(Fixture(after: "await Post();", members: hops));
        Assert.Single(transitive.Violations);
        Assert.Contains("Deeper", transitive.Violations[0], StringComparison.Ordinal);
        Assert.Contains("Post", transitive.MethodsChecked);

        const string group = "private Task Grab(IDataverseUserClient c) => Task.CompletedTask;";
        Assert.NotEmpty(Tail(Fixture(after: "Run(Grab);", members: group)).Violations);

        // Must NOT fire: the same references before the create, and in a method only the pre-create code calls.
        const string before = "await CallerWriteAuthorization.WhoAmIAsync(null, ct); await user.GetAsync(ct); await Decide();";
        const string preOnly = "private async Task Decide(IDataverseUserClient user) { await CallerRecordAccessProbe.Ask(user); }";
        var clean = Tail(Fixture(before: before, after: "await AfterOnly();", members: preOnly + " private Task AfterOnly() => Task.CompletedTask;"));
        Assert.True(clean.AnchorFound);
        Assert.Empty(clean.Violations);
        Assert.Contains("AfterOnly", clean.MethodsChecked);
        Assert.DoesNotContain("Decide", clean.MethodsChecked);

        // Must NOT fire: comments and strings in the tail.
        Assert.Empty(Tail(Fixture(after: "var s = \"CallerWriteAuthorization user\"; // principal user CallerRecordAccessProbe")).Violations);

        // The anchor is required: no step 11 statement, or two of them, is reported, never passed.
        Assert.False(Tail("class RecordCreationService { Task<int> CreateAsync() { return Task.FromResult(1); } }").AnchorFound);
        Assert.False(Tail(Fixture(after: "await _appOnly.UpdateRecordFieldsAsync(t, id, f, ct);")).AnchorFound);
    }

    // ── The scanners ──────────────────────────────────────────────────────────────────────────────────────────────────

    private static IEnumerable<string> ServiceFiles(string service) =>
        Directory.EnumerateFiles(BffRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return name.Equals(service + ".cs", StringComparison.Ordinal)
                       || (name.StartsWith(service + ".", StringComparison.Ordinal) && name.EndsWith(".cs", StringComparison.Ordinal));
            });

    internal static IReadOnlyList<string> ServiceViolations(string file, string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        return tree.GetRoot().DescendantTokens()
            .Where(t => t.IsKind(SyntaxKind.IdentifierToken) && CallerAccessTypes.Contains(t.ValueText, StringComparer.Ordinal))
            .Select(t => $"{file}:{t.GetLocation().GetLineSpan().StartLinePosition.Line + 1} names {t.ValueText}")
            .ToList();
    }

    internal sealed record TailResult(bool AnchorFound, IReadOnlyList<string> Violations, IReadOnlyCollection<string> MethodsChecked);

    internal static TailResult TailViolations(IReadOnlyList<(string File, string Source)> partials)
    {
        var methods = new Dictionary<string, List<(string File, MethodDeclarationSyntax Method)>>(StringComparer.Ordinal);
        MethodDeclarationSyntax? create = null;
        var createFile = string.Empty;
        foreach (var (file, source) in partials)
        {
            var root = CSharpSyntaxTree.ParseText(source).GetRoot();
            foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                         .Where(c => c.Identifier.ValueText == "RecordCreationService"))
            {
                foreach (var method in cls.Members.OfType<MethodDeclarationSyntax>())
                {
                    var name = method.Identifier.ValueText;
                    if (!methods.TryGetValue(name, out var list))
                        methods[name] = list = [];
                    list.Add((file, method));
                    if (name == CreateMethod && method.Body is not null)
                    {
                        // The one CreateAsync with a step 11 statement is the pipeline; any other overload is not it.
                        if (CreateStatements(method).Any(s => CallsStepEleven(s)))
                        {
                            create = method;
                            createFile = file;
                        }
                    }
                }
            }
        }

        if (create is null)
            return new TailResult(false, [], []);

        var statements = CreateStatements(create);
        var anchors = statements.Where(CallsStepEleven).ToList();
        if (anchors.Count != 1)
            return new TailResult(false, [], []);

        var tail = statements.SkipWhile(s => s != anchors[0]).Skip(1).ToList();
        var violations = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(string Where, SyntaxNode Node, bool IsTail)>();
        foreach (var s in tail)
            queue.Enqueue(($"{createFile} (CreateAsync after step 11)", s, true));

        while (queue.Count > 0)
        {
            var (where, node, isTail) = queue.Dequeue();
            foreach (var token in node.DescendantTokens().Where(t => t.IsKind(SyntaxKind.IdentifierToken)))
            {
                var text = token.ValueText;
                var line = token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                if (CallerAccessTypes.Contains(text, StringComparer.Ordinal) || (isTail && CallerHolders.Contains(text, StringComparer.Ordinal)))
                    violations.Add($"{where} line {line}: names {text}");

                if (methods.TryGetValue(text, out var targets) && text != CreateMethod && visited.Add(text))
                {
                    foreach (var (file, method) in targets)
                        queue.Enqueue(($"{file} method {text} (reached from the post-create tail)", method, false));
                }
            }
        }

        return new TailResult(true, violations, visited.ToList());
    }

    private static IReadOnlyList<StatementSyntax> CreateStatements(MethodDeclarationSyntax create) =>
        create.Body?.Statements.ToList() ?? [];

    private static bool CallsStepEleven(StatementSyntax statement) =>
        statement.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(i =>
            i.Expression switch
            {
                MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText == StepElevenCall,
                IdentifierNameSyntax n => n.Identifier.ValueText == StepElevenCall,
                _ => false,
            });

    // ── Fixtures ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private static TailResult Tail(string source) => TailViolations([("fixture.cs", source)]);

    /// <summary>A <c>RecordCreationService</c> whose <c>CreateAsync</c> has one step 11 create between <paramref name="before"/> and <paramref name="after"/>.</summary>
    private static string Fixture(string after, string before = "", string members = "") =>
        $$"""
        partial class RecordCreationService
        {
            internal async Task<int> CreateAsync(Request request, Principal principal, CancellationToken ct)
            {
                {{before}}
                for (var attempt = 1; ; attempt++)
                {
                    await _appOnly.UpdateRecordFieldsAsync(table, id, fields, ct);
                    break;
                }
                {{after}}
                return 1;
            }
            {{members}}
        }
        """;
}

using System.Linq.Expressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// spaarke-ontology-platform-r1 task 022 (rework round 2 finding F11, extended round 3 finding L1): within
/// <c>Sprk.Bff.Api</c>, a <see cref="CompiledPredicate"/> can only be obtained through
/// <c>PolicyVersionValidator.TryPrepareForEvaluation</c>.
/// </summary>
/// <remarks>
/// <para><b>Why this is a boundary.</b> <c>TryPrepareForEvaluation</c> is the fail-closed evaluation-time gate (owner
/// decision 2026-10-03: admins can author policy rows directly in the Spaarke Platform app, bypassing BFF save-time
/// validation). It also checks what <c>Compile</c> alone does not — the message template's tokens against the
/// predicate's template-eligible fields (§0.3) — and it logs + meters every refusal (EventId 50301,
/// <c>ontology.policy.invalid</c>). An evaluator (task 031) that compiled directly, or FORGED a predicate, would
/// bypass all of that. <c>internal</c> cannot express this (the evaluator lives in the same assembly).</para>
/// <para><b>Exactly what is enforced</b> (IL scan of <c>Sprk.Bff.Api</c>; a lambda or async state machine is
/// attributed to the type that declares it):</para>
/// <list type="number">
/// <item>Outside <c>PolicyVersionValidator</c> and <c>PredicateCompiler</c>: no <c>call</c>/<c>callvirt</c>, no
/// delegate creation (<c>ldftn</c>/<c>ldvirtftn</c>) and no expression tree (<c>ldtoken</c>) of any
/// <c>PredicateCompiler.Compile*</c> method.</item>
/// <item>Outside <c>PredicateCompiler</c> (and the record itself): no <c>newobj</c> of a <see cref="CompiledPredicate"/>
/// constructor, and no call to its compiler-generated <c>&lt;Clone&gt;$</c> — which is what <c>with</c> compiles to —
/// so a predicate can be neither forged nor altered (e.g. <c>validated with { FetchXml = ... }</c>).</item>
/// </list>
/// <para><b>Not enforced</b>: reflection (<c>MethodInfo.Invoke</c>, <c>Activator.CreateInstance</c>), <c>dynamic</c>
/// dispatch, and other assemblies. Those stay a code-review concern.</para>
/// <para><b>Why IL, not only NetArchTest.</b> NetArchTest's rules are TYPE-granular: <c>SignalWriter</c> (reads
/// <c>PredicateCompiler.EvaluatorGlobalReadableEntities</c>) and <c>SignalsModule</c> (DI) legitimately depend on the
/// type without compiling anything, and the evaluator will legitimately CONSUME a <see cref="CompiledPredicate"/>. The
/// rules are about specific members, so call sites are scanned with Mono.Cecil — the library NetArchTest is built on
/// (a transitive dependency, no new package). NetArchTest selects the subject type for the non-vacuity check.</para>
/// <para><b>Controls.</b> Positive: the sanctioned <c>PolicyVersionValidator</c> call IS detected. Negative:
/// <see cref="PredicateCompilerGuardControlFixtures"/> (compiled into THIS assembly) commits every forbidden shape,
/// and <see cref="EveryDetectorFiresOnItsControlFixture"/> proves each detector reports it — so a detector that
/// silently stops matching (an IL shape change, a rename) fails here instead of passing vacuously.</para>
/// </remarks>
public class PredicateCompilerCallerGuardTests
{
    private const string CompilerTypeName = "Sprk.Bff.Api.Services.Signals.PredicateCompiler";
    private const string PredicateTypeName = "Sprk.Bff.Api.Services.Signals.CompiledPredicate";
    private const string ValidatorTypeName = "Sprk.Bff.Api.Services.Signals.PolicyVersionValidator";

    private const string CompileReference = "Compile* reference";
    private const string PredicateConstruction = "CompiledPredicate construction";
    private const string PredicateClone = "CompiledPredicate clone (with)";

    private static readonly IReadOnlyDictionary<string, HashSet<string>> AllowedByKind = new Dictionary<string, HashSet<string>>
    {
        [CompileReference] = new(StringComparer.Ordinal) { ValidatorTypeName, CompilerTypeName },
        [PredicateConstruction] = new(StringComparer.Ordinal) { CompilerTypeName, PredicateTypeName },
        [PredicateClone] = new(StringComparer.Ordinal) { CompilerTypeName, PredicateTypeName },
    };

    [Fact(DisplayName = "Task 022 F11/L1: only the validator compiles, and only the compiler constructs or clones a CompiledPredicate")]
    public void OnlyTheSanctionedPathProducesACompiledPredicate()
    {
        // Non-vacuity, type side: the compiler still exists under the name this guard matches on.
        var compilerTypes = Types.InAssembly(typeof(Program).Assembly)
            .That().HaveNameMatching("^PredicateCompiler$")
            .GetTypes()
            .Select(t => t.FullName)
            .ToList();
        Assert.Equal(new[] { CompilerTypeName }, compilerTypes);

        var findings = ScanBff();

        // Positive control: the sanctioned caller's call IS detected.
        Assert.Contains(findings, f => f.Kind == CompileReference && f.CallerType == ValidatorTypeName);

        var violations = findings
            .Where(f => !AllowedByKind[f.Kind].Contains(f.CallerType))
            .Select(f => $"{f.Kind}: {f.CallerMethod} -> {f.Target}")
            .Distinct()
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Task 022 F11/L1 violation: a Sprk.Bff.Api method compiles, constructs or clones a CompiledPredicate " +
            "outside the sanctioned path. Obtain a predicate via PolicyVersionValidator.TryPrepareForEvaluation: it " +
            "is the fail-closed gate that also checks the message template against the predicate's " +
            "template-eligible fields (section 0.3) and logs + meters every refusal. If another producer is " +
            "genuinely needed, follow CLAUDE.md section 6.5 before adding it to AllowedByKind. " +
            $"Violations: {string.Join("; ", violations)}");
    }

    [Fact(DisplayName = "Task 022 F2: PolicyVersionValidator compiles via CompileSchemaValidated only (schema evaluated once)")]
    public void PolicyVersionValidatorNeverCallsTheSchemaEvaluatingCompile()
    {
        // PolicyVersionValidator evaluates the rule-body schema itself (so it can report schema_invalid with
        // field-level errors). Calling PredicateCompiler.Compile afterwards would evaluate the schema a SECOND
        // time under RuleBodySchemaValidator's process-wide lock -- the duplicate finding F2 removed.
        var callees = ScanBff()
            .Where(f => f.Kind == CompileReference && f.CallerType == ValidatorTypeName)
            .Select(f => f.Target)
            .Distinct()
            .ToList();

        Assert.Equal(new[] { "PredicateCompiler::CompileSchemaValidated" }, callees);
    }

    [Theory(DisplayName = "Task 022 L1: every detector fires on its negative-control fixture")]
    [InlineData(nameof(PredicateCompilerGuardControlFixtures.DirectCall), CompileReference)]
    [InlineData(nameof(PredicateCompilerGuardControlFixtures.DelegateToCompile), CompileReference)]
    [InlineData(nameof(PredicateCompilerGuardControlFixtures.ExpressionTreeOverCompile), CompileReference)]
    [InlineData(nameof(PredicateCompilerGuardControlFixtures.Forge), PredicateConstruction)]
    [InlineData(nameof(PredicateCompilerGuardControlFixtures.AlterWithWith), PredicateClone)]
    public void EveryDetectorFiresOnItsControlFixture(string fixtureMethod, string expectedKind)
    {
        using var module = ModuleDefinition.ReadModule(typeof(PredicateCompilerGuardControlFixtures).Assembly.Location);
        var fixtureType = typeof(PredicateCompilerGuardControlFixtures).FullName!;

        var findings = Scan(module)
            .Where(f => f.CallerType == fixtureType && f.CallerMethod.EndsWith("::" + fixtureMethod, StringComparison.Ordinal))
            .Select(f => f.Kind)
            .ToList();

        Assert.Contains(expectedKind, findings);
    }

    private static List<Finding> ScanBff()
    {
        using var module = ModuleDefinition.ReadModule(typeof(Program).Assembly.Location);
        return Scan(module);
    }

    private static List<Finding> Scan(ModuleDefinition module)
    {
        var found = new List<Finding>();

        foreach (var type in module.GetTypes()) // GetTypes() includes nested + compiler-generated types
        {
            foreach (var method in type.Methods.Where(m => m.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is not MethodReference target)
                    {
                        continue;
                    }

                    var kind = Classify(instruction.OpCode.Code, target);
                    if (kind is not null)
                    {
                        found.Add(new Finding(
                            kind,
                            OutermostType(type).FullName,
                            $"{type.FullName}::{method.Name}",
                            $"{target.DeclaringType.Name}::{target.Name}"));
                    }
                }
            }
        }

        return found;
    }

    private static string? Classify(Code code, MethodReference target)
    {
        var declaring = target.DeclaringType.FullName;

        if (declaring == CompilerTypeName
            && target.Name.StartsWith("Compile", StringComparison.Ordinal)
            && code is Code.Call or Code.Callvirt or Code.Ldftn or Code.Ldvirtftn or Code.Ldtoken)
        {
            return CompileReference;
        }

        if (declaring == PredicateTypeName && code == Code.Newobj && target.Name == ".ctor")
        {
            return PredicateConstruction;
        }

        if (declaring == PredicateTypeName && code is Code.Call or Code.Callvirt && target.Name == "<Clone>$")
        {
            return PredicateClone;
        }

        return null;
    }

    /// <summary>A lambda or async state machine is a nested compiler-generated type; attribute its IL to the
    /// type that declared it.</summary>
    private static TypeDefinition OutermostType(TypeDefinition type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }

        return type;
    }

    private sealed record Finding(string Kind, string CallerType, string CallerMethod, string Target);
}

/// <summary>
/// NEGATIVE CONTROLS for <see cref="PredicateCompilerCallerGuardTests"/> — never called. Each method commits one
/// shape the guard forbids inside <c>Sprk.Bff.Api</c>; the guard's control theory proves its detector reports it.
/// Lives in the test assembly, so it is not itself a violation of the rule (which scans <c>Sprk.Bff.Api</c> only).
/// Same convention as <c>Adr001ControlFixtures</c>.
/// </summary>
internal static class PredicateCompilerGuardControlFixtures
{
    internal static CompiledPredicate DirectCall(PredicateCompiler compiler) => compiler.Compile("{}");

    internal static Func<string, Guid?, CompiledPredicate> DelegateToCompile(PredicateCompiler compiler) => compiler.Compile;

    internal static Expression<Func<PredicateCompiler, CompiledPredicate>> ExpressionTreeOverCompile() =>
        compiler => compiler.Compile("{}", null);

    internal static CompiledPredicate Forge() =>
        new("sprk_matter", "sprk_matterid", "<fetch/>", DateTimeOffset.UnixEpoch,
            new HashSet<string>(), new HashSet<string>(), new HashSet<string>(),
            PredicateCompiler.DefaultQuietWindowDays, new HashSet<string>());

    internal static CompiledPredicate AlterWithWith(CompiledPredicate validated) => validated with { FetchXml = "<fetch/>" };
}

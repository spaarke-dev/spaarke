using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// spaarke-ontology-platform-r1 task 022 (rework round 2, finding F11): within <c>Sprk.Bff.Api</c>, only
/// <c>PolicyVersionValidator</c> — and <c>PredicateCompiler</c> itself — may call a <c>PredicateCompiler.Compile*</c>
/// method.
/// </summary>
/// <remarks>
/// <para><b>Why this is a boundary.</b> <c>PolicyVersionValidator.TryPrepareForEvaluation</c> is the fail-closed
/// evaluation-time gate (owner decision 2026-10-03: admins can author policy rows directly in the Spaarke Platform
/// app, bypassing BFF save-time validation). It also checks what <c>Compile</c> alone does not — the message
/// template's tokens against the predicate's template-eligible fields (§0.3), and it logs + meters every refusal
/// (EventId 50301, <c>ontology.policy.invalid</c>). An evaluator (task 031) that called <c>Compile</c> directly
/// would get a working predicate from a policy version whose template asserts something the predicate never
/// read — and an invalid one would fail silently. <c>internal</c> cannot express this (the evaluator lives in the
/// same assembly), so the rule is enforced here.</para>
/// <para><b>Why IL, not only NetArchTest.</b> NetArchTest's dependency rules work at TYPE granularity, and a
/// type-level rule cannot be the boundary: <c>SignalWriter</c> legitimately reads
/// <c>PredicateCompiler.EvaluatorGlobalReadableEntities</c> and <c>SignalsModule</c> registers the type, so both
/// depend on <c>PredicateCompiler</c> without compiling anything. The rule is about one METHOD family, so this
/// scans call sites with Mono.Cecil — the library NetArchTest itself is built on (a transitive dependency of
/// <c>NetArchTest.Rules</c>, no new package). NetArchTest selects the subject type for the non-vacuity check.</para>
/// <para><b>Positive control.</b> The detector must find <c>PolicyVersionValidator</c>'s own call, or the guard is
/// examining nothing (a rename, or an IL shape this scan does not recognise) and fails rather than passing.</para>
/// </remarks>
public class PredicateCompilerCallerGuardTests
{
    private const string CompilerTypeName = "Sprk.Bff.Api.Services.Signals.PredicateCompiler";

    private static readonly HashSet<string> AllowedCallers = new(StringComparer.Ordinal)
    {
        "Sprk.Bff.Api.Services.Signals.PolicyVersionValidator",
        CompilerTypeName,
    };

    [Fact(DisplayName = "Task 022 F11: only PolicyVersionValidator (and the compiler itself) calls PredicateCompiler.Compile*")]
    public void OnlyPolicyVersionValidatorCallsPredicateCompilerCompile()
    {
        // Non-vacuity, type side: the compiler still exists under the name this guard matches on.
        var compilerTypes = Types.InAssembly(typeof(Program).Assembly)
            .That().HaveNameMatching("^PredicateCompiler$")
            .GetTypes()
            .Select(t => t.FullName)
            .ToList();
        Assert.Equal(new[] { CompilerTypeName }, compilerTypes);

        var callSites = FindCompileCallSites();

        // Positive control, call side: the sanctioned caller's call IS detected.
        Assert.Contains(callSites, c => c.CallerType == "Sprk.Bff.Api.Services.Signals.PolicyVersionValidator");

        var violations = callSites
            .Where(c => !AllowedCallers.Contains(c.CallerType))
            .Select(c => $"{c.CallerMethod} -> PredicateCompiler.{c.Callee}")
            .Distinct()
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Task 022 F11 violation: a Sprk.Bff.Api method calls PredicateCompiler.Compile* directly. Obtain a " +
            "predicate via PolicyVersionValidator.TryPrepareForEvaluation instead: it is the fail-closed gate that " +
            "also checks the message template against the predicate's template-eligible fields (section 0.3) and " +
            "logs + meters every refusal. If a second caller is genuinely needed, follow CLAUDE.md section 6.5 " +
            $"before adding it to AllowedCallers. Violations: {string.Join("; ", violations)}");
    }

    [Fact(DisplayName = "Task 022 F2: PolicyVersionValidator compiles via CompileSchemaValidated only (schema evaluated once)")]
    public void PolicyVersionValidatorNeverCallsTheSchemaEvaluatingCompile()
    {
        // PolicyVersionValidator evaluates the rule-body schema itself (so it can report schema_invalid with
        // field-level errors). Calling PredicateCompiler.Compile afterwards would evaluate the schema a SECOND
        // time under RuleBodySchemaValidator's process-wide lock -- the duplicate finding F2 removed.
        var callees = FindCompileCallSites()
            .Where(c => c.CallerType == "Sprk.Bff.Api.Services.Signals.PolicyVersionValidator")
            .Select(c => c.Callee)
            .Distinct()
            .ToList();

        Assert.Equal(new[] { "CompileSchemaValidated" }, callees);
    }

    private static List<(string CallerType, string CallerMethod, string Callee)> FindCompileCallSites()
    {
        var found = new List<(string, string, string)>();
        using var module = ModuleDefinition.ReadModule(typeof(Program).Assembly.Location);

        foreach (var type in module.GetTypes()) // GetTypes() includes nested + compiler-generated types
        {
            foreach (var method in type.Methods.Where(m => m.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode.Code is not (Code.Call or Code.Callvirt or Code.Ldftn or Code.Ldvirtftn)
                        || instruction.Operand is not MethodReference callee
                        || callee.DeclaringType.FullName != CompilerTypeName
                        || !callee.Name.StartsWith("Compile", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    found.Add((OutermostType(type).FullName, $"{type.FullName}::{method.Name}", callee.Name));
                }
            }
        }

        return found;
    }

    /// <summary>A lambda or async state machine is a nested compiler-generated type; attribute its calls to the
    /// type that declared it.</summary>
    private static TypeDefinition OutermostType(TypeDefinition type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }

        return type;
    }
}

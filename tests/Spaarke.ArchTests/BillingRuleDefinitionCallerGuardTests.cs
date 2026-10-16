using System.Linq.Expressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 070 (spec FR-24, POML constraint "the billing gate is the only way to obtain a parsed billing rule"):
/// inside <c>Sprk.Bff.Api</c>, only <see cref="PolicyVersionValidator"/> (its <see cref="PolicyVersionValidator.TryPrepareBillingRule"/>
/// gate) may call, take a delegate to, or build an expression tree over <c>BillingRuleDefinition.FromValidatedBody</c>. The
/// constructor is private, so <c>newobj</c> outside the class does not compile. Same IL scan as
/// <see cref="PredicateCompilerCallerGuardTests"/>. Not covered: reflection and other assemblies (a code-review concern).
/// </summary>
public sealed class BillingRuleDefinitionCallerGuardTests
{
    private const string DefinitionTypeName = "Sprk.Bff.Api.Services.Signals.Billing.BillingRuleDefinition";
    private const string FactoryName = "FromValidatedBody";

    private static readonly HashSet<string> AllowedCallers = new(StringComparer.Ordinal)
    {
        "Sprk.Bff.Api.Services.Signals.PolicyVersionValidator",
        DefinitionTypeName,
    };

    [Fact(DisplayName = "Task 070: only the billing gate (PolicyVersionValidator) obtains a BillingRuleDefinition")]
    public void OnlyTheBillingGateProducesABillingRuleDefinition()
    {
        using var module = ModuleDefinition.ReadModule(typeof(Program).Assembly.Location);
        var findings = Scan(module);

        Assert.Contains(findings, f => f.CallerType == "Sprk.Bff.Api.Services.Signals.PolicyVersionValidator");
        var violations = findings.Where(f => !AllowedCallers.Contains(f.CallerType)).ToList();
        Assert.True(violations.Count == 0,
            "BillingRuleDefinition.FromValidatedBody is referenced outside the billing gate: " +
            string.Join("; ", violations.Select(v => $"{v.CallerMethod} ({v.Opcode})")) +
            ". Obtain a billing rule through PolicyVersionValidator.TryPrepareBillingRule.");
    }

    [Theory(DisplayName = "Task 070: the billing-definition guard fires on each forbidden shape (must-fire controls)")]
    [InlineData(nameof(BillingRuleDefinitionGuardControlFixtures.DirectCall), "Call")]
    [InlineData(nameof(BillingRuleDefinitionGuardControlFixtures.DelegateToFactory), "Ldftn")]
    [InlineData(nameof(BillingRuleDefinitionGuardControlFixtures.ExpressionTreeOverFactory), "Ldtoken")]
    public void EveryDetectorFiresOnItsControlFixture(string fixtureMethod, string expectedOpcode)
    {
        using var module = ModuleDefinition.ReadModule(typeof(BillingRuleDefinitionGuardControlFixtures).Assembly.Location);
        var findings = Scan(module)
            .Where(f => f.CallerType == typeof(BillingRuleDefinitionGuardControlFixtures).FullName
                        && f.CallerMethod.Contains(fixtureMethod, StringComparison.Ordinal))
            .Select(f => f.Opcode)
            .ToList();

        Assert.Contains(expectedOpcode, findings);
    }

    private static List<Finding> Scan(ModuleDefinition module)
    {
        var found = new List<Finding>();
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods.Where(m => m.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is MethodReference target
                        && target.DeclaringType.FullName == DefinitionTypeName
                        && target.Name == FactoryName
                        && instruction.OpCode.Code is Code.Call or Code.Callvirt or Code.Ldftn or Code.Ldvirtftn or Code.Ldtoken)
                    {
                        var outer = type;
                        while (outer.DeclaringType is not null) outer = outer.DeclaringType;
                        found.Add(new Finding(outer.FullName, $"{type.FullName}::{method.Name}", instruction.OpCode.Code.ToString()));
                    }
                }
            }
        }

        return found;
    }

    private sealed record Finding(string CallerType, string CallerMethod, string Opcode);
}

/// <summary>NEGATIVE CONTROLS for <see cref="BillingRuleDefinitionCallerGuardTests"/> — never called. Each commits one shape the
/// guard forbids; it lives in the test assembly, so it is not itself a violation (the rule scans <c>Sprk.Bff.Api</c>).</summary>
internal static class BillingRuleDefinitionGuardControlFixtures
{
    internal static BillingRuleDefinition DirectCall() =>
        BillingRuleDefinition.FromValidatedBody(Guid.Empty, "BILL-X", RuleType.ApprovedTimekeeper, "{\"type\":\"ApprovedTimekeeper\"}");

    internal static Func<Guid, string, RuleType, string, BillingRuleDefinition> DelegateToFactory() =>
        BillingRuleDefinition.FromValidatedBody;

    internal static Expression<Func<BillingRuleDefinition>> ExpressionTreeOverFactory() =>
        () => BillingRuleDefinition.FromValidatedBody(Guid.Empty, "BILL-X", RuleType.ApprovedTimekeeper, "{}");
}

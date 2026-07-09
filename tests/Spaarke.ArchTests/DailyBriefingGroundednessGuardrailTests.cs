using NetArchTest.Rules;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// R5 task 015 (FR-A6) — locks the operator ruling (2026-07-08): the Daily Briefing is accurate
/// BY CONSTRUCTION (deterministic item rows + deterministic-fact TL;DR + binary anchor
/// resolution). Existence is never probabilistic, so NO briefing code path may warn or withhold
/// user-facing content based on a groundedness score. <c>GroundednessCheckService</c> is a
/// Chat-safety + eval/telemetry signal ONLY.
///
/// A score gate on the briefing would mechanically require a briefing type to take a dependency
/// on the groundedness service. These guardrails fail the moment any DailyBriefing type (by name)
/// or any type in the briefing's coded-composite home namespace (Services.Ai.Narrators) acquires
/// that dependency — turning the operator ruling into an enforced architectural boundary rather
/// than a convention that a future change could silently cross.
/// </summary>
public class DailyBriefingGroundednessGuardrailTests
{
    private static readonly string[] GroundednessTypes =
    {
        "Sprk.Bff.Api.Services.Ai.Safety.IGroundednessCheckService",
        "Sprk.Bff.Api.Services.Ai.Safety.GroundednessCheckService",
    };

    [Fact(DisplayName = "FR-A6: DailyBriefing* types must not depend on GroundednessCheckService")]
    public void DailyBriefingTypesMustNotDependOnGroundednessCheck()
    {
        var assembly = typeof(Program).Assembly;

        var result = Types.InAssembly(assembly)
            .That()
            .HaveNameStartingWith("DailyBriefing")
            .ShouldNot()
            .HaveDependencyOnAny(GroundednessTypes)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "FR-A6 guardrail violated: the Daily Briefing is accurate by construction — no briefing " +
            "type may depend on GroundednessCheckService (no groundedness score gates user-facing " +
            "content). Failing types: " +
            string.Join(", ", result.FailingTypeNames ?? System.Array.Empty<string>()));
    }

    [Fact(DisplayName = "FR-A6: the Narrators namespace must not depend on GroundednessCheckService")]
    public void NarratorsNamespaceMustNotDependOnGroundednessCheck()
    {
        var assembly = typeof(Program).Assembly;

        var result = Types.InAssembly(assembly)
            .That()
            .ResideInNamespace("Sprk.Bff.Api.Services.Ai.Narrators")
            .ShouldNot()
            .HaveDependencyOnAny(GroundednessTypes)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "FR-A6 guardrail violated: the DailyBriefing coded-composite home " +
            "(Sprk.Bff.Api.Services.Ai.Narrators) must not route through a groundedness score gate. " +
            "Failing types: " +
            string.Join(", ", result.FailingTypeNames ?? System.Array.Empty<string>()));
    }
}

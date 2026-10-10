using NetArchTest.Rules;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-013 / FR-C6: AI facade boundary (sdap-bff-api-remediation-fix, Outcome E).
/// CRUD (non-AI) code in Sprk.Bff.Api must NOT depend on AI-internal types
/// (<c>IOpenAiClient</c>, <c>IPlaybookService</c>). AI capability is consumed
/// through the <c>Services/Ai/PublicContracts/</c> facade interfaces
/// (IBriefingAi, IInvoiceAi, IInsightsAi, IWorkspacePrefillAi, IRecordMatchingAi, ...).
///
/// Without this guard the 2026-05 facade migration (148 occurrences / 59 files
/// reduced by 92%) decays as new code drifts back to direct injection — the
/// path of least resistance. See:
///   - .claude/constraints/bff-extensions.md (pre-merge checklist item 4)
///   - projects/sdap-bff-api-remediation-fix/spec.md FR-C6 / FR-E2 / SC-28
///   - projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md tasks 050 + 053
///     (documented exception inventory grandfathered below)
/// </summary>
public class ADR013_AiBoundaryTests
{
    /// <summary>
    /// AI-internal boundary types per FR-C6. The spec also named four
    /// facade-internal peers (IBriefingService, IInvoiceService,
    /// IRecordMatchingService, IWorkspacePrefillService); those types no longer
    /// exist post-facade (their replacements are the PublicContracts facades,
    /// which are the SANCTIONED consumption path and therefore not listed here).
    /// </summary>
    private static readonly string[] ForbiddenAiInternalTypes =
    {
        "Sprk.Bff.Api.Services.Ai.IOpenAiClient",
        "Sprk.Bff.Api.Services.Ai.IPlaybookService",
    };

    /// <summary>
    /// Grandfathered exceptions. Namespace prefixes:
    ///  - Services.Ai — the AI domain itself (the boundary marker per FR-C6);
    ///  - Infrastructure.DI — composition root; DI modules register the types
    ///    (FR-E2 acceptance scope: "zero outside Services/Ai/ and Infrastructure/DI/").
    /// Type prefixes — the DOCUMENTED AI-API-surface deferrals per
    /// projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md task 053
    /// ("Deferred (5 files, all in AI API surface — boundary exception per task 050)").
    /// Note: Api/Ai/AiPlaybookBuilderEndpoints.cs from that list has since been
    /// retired and needs no entry. Prefix matching also covers compiler-generated
    /// nested closure types (e.g. lambdas inside endpoint filter extensions).
    /// Do NOT extend this list without an ADR-013 exception documented per
    /// CLAUDE.md §6.5 (path A) — that is the whole point of this test.
    /// </summary>
    private static readonly string[] GrandfatheredPrefixes =
    {
        // Namespace-level exclusions (sanctioned zones)
        "Sprk.Bff.Api.Services.Ai.",
        "Sprk.Bff.Api.Infrastructure.DI.",

        // Documented AI-API-surface exceptions (EXECUTION-LOG task 050/053)
        "Sprk.Bff.Api.Api.Filters.PlaybookAuthorizationFilter",           // ADR-008 ownership-check filter (IPlaybookService.GetPlaybookAsync not exposed via facade)
        "Sprk.Bff.Api.Api.Filters.PlaybookAuthorizationFilterExtensions", // same file — endpoint-filter factory lambdas
        "Sprk.Bff.Api.Api.Ai.ChatEndpoints",                              // IS the Chat API surface (raw AI exposure)
        "Sprk.Bff.Api.Api.Ai.PlaybookEndpoints",                          // IS the Playbook CRUD API (handlers wrap IPlaybookService 1:1)
        "Sprk.Bff.Api.Api.Agent.AgentEndpoints",                          // M365 Copilot agent gateway (playbook-discovery pattern)
    };

    [Fact(DisplayName = "ADR-013/FR-C6: CRUD code must not depend on AI-internal types — use Services/Ai/PublicContracts facade")]
    public void CrudCodeMustNotDependOnAiInternalTypes()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act — pure assembly inspection (deterministic; no source scans, no ceilings)
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(ForbiddenAiInternalTypes)
            .GetResult();

        var violations = (result.FailingTypeNames ?? new List<string>())
            .Where(typeName => !GrandfatheredPrefixes.Any(prefix =>
                typeName.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(typeName => typeName, StringComparer.Ordinal)
            .ToList();

        // Assert
        Assert.True(
            violations.Count == 0,
            "ADR-013 / FR-C6 violation: direct dependency on AI-internal types " +
            "(IOpenAiClient / IPlaybookService) outside Services/Ai/. " +
            "Consume AI capability via the Services/Ai/PublicContracts/ facade instead " +
            "(IBriefingAi, IInvoiceAi, IInsightsAi, IWorkspacePrefillAi, IRecordMatchingAi). " +
            "If this is a legitimate AI-API-surface exception, follow CLAUDE.md §6.5 path A " +
            "(document in design.md/spec.md + PR description) before extending the " +
            "grandfathered list in this test. " +
            $"Violating types: {string.Join(", ", violations)}");
    }

    // ---------------------------------------------------------------------------------------------
    // D-66 (spaarke-ontology-platform-r1 task 048): the shared Dataverse write core is NOT an AI type.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The shared write core, by full name. Nested and compiler-generated types match by prefix.</summary>
    private static readonly string[] DataverseWriteCoreTypes =
    {
        "Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite",
        "Sprk.Bff.Api.Services.Dataverse.DataverseWriteItemMapper",
    };

    /// <summary>The namespace the write core used to live in (it still holds the AI-tool-only helpers). The trailing dot is
    /// for the <c>StartsWith</c> / <see cref="IsInNamespace"/> checks, so <c>...Handlers.DataverseUpdateRecordHandler</c> (an AI
    /// handler in a different namespace) does not match. NetArchTest's <c>HaveDependencyOn</c> matches whole namespace
    /// segments and needs the name WITHOUT the dot (<c>TrimEnd('.')</c> at the call sites): with the dot it matches nothing.</summary>
    private const string AiDataverseToolNamespace = "Sprk.Bff.Api.Services.Ai.Handlers.Dataverse.";

    private const string AiNamespace = "Sprk.Bff.Api.Services.Ai.";

    [Fact(DisplayName = "D-66: the Dataverse write core (OwnedChildWrite, DataverseWriteItemMapper) lives in Services/Dataverse and non-AI code never reaches into Services/Ai for it")]
    public void DataverseWriteCoreIsNotAnAiType()
    {
        var assembly = typeof(Program).Assembly;

        // 1. The core sits where the direction says it must (AI -> core, never core -> AI).
        foreach (var fullName in DataverseWriteCoreTypes)
        {
            Assert.True(
                assembly.GetType(fullName) is not null,
                $"D-66: {fullName} is missing. The shared Dataverse write core belongs in Sprk.Bff.Api.Services.Dataverse, not under Services/Ai.");
        }

        // 2. The core depends on nothing in Services/Ai (otherwise "moved out" is cosmetic).
        //    NetArchTest sees type-level dependencies (fields, signatures, base types); IlCallScan adds the call sites
        //    in method bodies (a static call such as DataverseRecordCitations.RecordPath is invisible to NetArchTest).
        var coreResult = Types.InAssembly(assembly)
            .That().HaveNameStartingWith("OwnedChildWrite").Or().HaveNameStartingWith("DataverseWriteItemMapper")
            .ShouldNot().HaveDependencyOn(AiNamespace.TrimEnd('.'))
            .GetResult();
        var coreViolations = (coreResult.FailingTypeNames ?? new List<string>())
            .Where(n => DataverseWriteCoreTypes.Any(c => n.StartsWith(c, StringComparison.Ordinal)))
            .ToList();
        coreViolations.AddRange(
            IlCallScan.MethodReferences(DataverseWriteCoreTypes.SelectMany(n => IlCallScan.WithNested(assembly.GetType(n)!)))
                .Where(r => IsInNamespace(r.Target.DeclaringType, AiNamespace))
                .Select(r => $"{IlCallScan.Describe(r.CallerMethod)} -> {r.Target.DeclaringType!.FullName}.{r.Target.Name}"));

        // 3. No non-AI code (Api/*, Services/Dataverse, Services/Signals, Infrastructure/*, ...) reaches into the
        //    AI-tool Dataverse namespace — the route a caller would take to reach a write-core type that moved back.
        var callerResult = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOn(AiDataverseToolNamespace.TrimEnd('.'))
            .GetResult();
        var callerViolations = (callerResult.FailingTypeNames ?? new List<string>())
            .Where(n => !n.StartsWith(AiNamespace, StringComparison.Ordinal))
            .ToList();
        callerViolations.AddRange(
            IlCallScan.MethodReferences(assembly.GetTypes().Where(t => !IsInNamespace(IlCallScan.Outermost(t), AiNamespace)))
                .Where(r => IsInNamespace(r.Target.DeclaringType, AiDataverseToolNamespace))
                .Select(r => $"{IlCallScan.Describe(r.CallerMethod)} -> {r.Target.DeclaringType!.FullName}.{r.Target.Name}"));
        coreViolations.Sort(StringComparer.Ordinal);
        callerViolations.Sort(StringComparer.Ordinal);

        Assert.True(
            coreViolations.Count == 0 && callerViolations.Count == 0,
            "D-66 violation: the Dataverse write core must not depend on Sprk.Bff.Api.Services.Ai.*, and non-AI code must not " +
            "depend on Sprk.Bff.Api.Services.Ai.Handlers.Dataverse.*. Move the shared type to Services/Dataverse. " +
            $"Write-core types depending on Services/Ai: [{string.Join(", ", coreViolations)}]. " +
            $"Non-AI types depending on Ai.Handlers.Dataverse: [{string.Join(", ", callerViolations)}]");
    }

    private static bool IsInNamespace(Type? type, string namespacePrefixWithTrailingDot) =>
        type?.Namespace is { } ns
        && (ns + ".").StartsWith(namespacePrefixWithTrailingDot, StringComparison.Ordinal);
}

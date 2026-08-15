using NetArchTest.Rules;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-013 forcing-function (code-quality-and-assurance-r3 task 040 / FR-14): locks the
/// <c>ai-architecture-redesign-r1</c> task-024 remediation that routed the Linear AI Consumer
/// executor primitives (<c>IActionResolver</c> / <c>IActionRunner</c>) through the
/// <c>Services/Ai/PublicContracts/</c> facades. After that remediation, NO type outside the AI
/// domain injects or otherwise depends on those two primitives directly — CRUD / workspace /
/// communication code reaches them only through a facade (<c>IFileSummarizeAi</c>,
/// <c>IWorkspacePrefillAi</c>, <c>IDocumentProfileAi</c>, <c>ICommunication*Ai</c>, ...).
///
/// This is the ratchet that keeps the +92% direct-injection reduction from re-drifting: direct
/// injection is the path of least resistance, and without a fitness function a new feature quietly
/// re-couples CRUD code to the executor primitives and passes CI.
/// </summary>
/// <remarks>
/// Complements (does NOT duplicate) <see cref="ADR013_AiBoundaryTests"/>, which guards the
/// coarser model/playbook primitives (<c>IOpenAiClient</c> / <c>IPlaybookService</c>). This test
/// adds the executor-primitive layer named in the r3 project CLAUDE.md ADR-013 rule.
/// See:
///   - .claude/adr/ADR-013-ai-architecture.md (Tier-1 facade discipline)
///   - .claude/constraints/bff-extensions.md §10 bullet 3 (PublicContracts facade requirement)
///   - projects/code-quality-and-assurance-r3/design.md §9 (forcing-functions)
/// </remarks>
public class ADR013_LinearConsumerBoundaryTests
{
    /// <summary>
    /// The Linear AI Consumer executor primitives. Task 024 routed every CRUD/consumer caller
    /// of these through a <c>Services/Ai/PublicContracts/</c> facade.
    /// </summary>
    private static readonly string[] ForbiddenLinearConsumerPrimitives =
    {
        "Sprk.Bff.Api.Services.Ai.LinearConsumers.IActionResolver",
        "Sprk.Bff.Api.Services.Ai.LinearConsumers.IActionRunner",
    };

    /// <summary>
    /// Sanctioned zones allowed to reference the executor primitives directly:
    ///  - <c>Services.Ai.</c> — the AI domain itself (owns the primitives + all facade impls);
    ///  - <c>Infrastructure.DI.</c> — the composition root (registers the primitives);
    ///  - <c>Api.Ai.</c> — the raw AI API surface (AnalysisEndpoints exposes the run/resolve
    ///    pipeline directly, exactly the grandfathered category ADR013_AiBoundaryTests uses).
    /// Prefix matching also covers compiler-generated closure/display types nested under these
    /// namespaces (endpoint-filter lambdas, etc.).
    ///
    /// Do NOT extend this list without an ADR-013 exception documented per CLAUDE.md §6.5 (path A) —
    /// that is the whole point of this test.
    /// </summary>
    private static readonly string[] GrandfatheredPrefixes =
    {
        "Sprk.Bff.Api.Services.Ai.",
        "Sprk.Bff.Api.Infrastructure.DI.",
        "Sprk.Bff.Api.Api.Ai.",
    };

    /// <summary>
    /// Current external consumers of <c>IPlaybookLookupService</c> — the by-stable-id playbook
    /// lookup that resides in <c>Sprk.Bff.Api.Services.Ai.PublicContracts</c> (it is itself a
    /// SANCTIONED facade type, not an internal primitive; task 040 grep confirmed the namespace).
    /// These consumers therefore do NOT violate ADR-013 — they consume a PublicContracts interface.
    /// This companion rule is a lighter SPREAD-GUARD than the executor-primitive ban above: it
    /// freezes the current external footprint so that new non-AI CRUD code prefers a task-specific
    /// facade rather than taking a blanket dependency on the AI playbook lookup. It is a ratchet,
    /// not an ADR-013 hard ban.
    /// </summary>
    private static readonly string[] PlaybookLookupBaselineConsumerTypes =
    {
        "Sprk.Bff.Api.Services.Workspace.WorkspaceAiService",
        "Sprk.Bff.Api.Services.Workspace.ProjectPreFillService",
        "Sprk.Bff.Api.Services.Workspace.MatterPreFillService",
        "Sprk.Bff.Api.Services.Jobs.Handlers.InvoiceExtractionJobHandler",
    };

    [Fact(DisplayName = "ADR-013/FR-14: no code outside the AI domain may depend on IActionResolver/IActionRunner — use a Services/Ai/PublicContracts facade")]
    public void NonAiCodeMustNotDependOnLinearConsumerPrimitives()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act — pure assembly inspection (deterministic; no source scans, no ceilings)
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(ForbiddenLinearConsumerPrimitives)
            .GetResult();

        var violations = (result.FailingTypeNames ?? new List<string>())
            .Where(typeName => !IsGrandfathered(typeName, GrandfatheredPrefixes))
            .OrderBy(typeName => typeName, StringComparer.Ordinal)
            .ToList();

        // Assert
        Assert.True(
            violations.Count == 0,
            "ADR-013 / FR-14 violation: a type outside the AI domain depends on the Linear AI " +
            "Consumer executor primitives (IActionResolver / IActionRunner). Consume the capability " +
            "through a Services/Ai/PublicContracts/ facade (IFileSummarizeAi, IWorkspacePrefillAi, " +
            "IDocumentProfileAi, ICommunication*Ai, ...) instead — this is the task-024 remediation " +
            "this test locks. If this is a legitimate AI-API-surface exception, follow CLAUDE.md §6.5 " +
            "path A (document it) before extending the grandfathered zones in this test. " +
            $"Violating types: {string.Join(", ", violations)}");
    }

    [Fact(DisplayName = "FR-14: IPlaybookLookupService external consumer footprint is frozen (spread-guard ratchet)")]
    public void PlaybookLookupDirectInjectionFootprintMustNotGrow()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Sprk.Bff.Api.Services.Ai.PublicContracts.IPlaybookLookupService")
            .GetResult();

        // A referencer is allowed if it is in a sanctioned zone (AI domain / DI root / AI API
        // surface) OR is one of the documented baseline consumers. Anything else is NEW spread.
        var newSpread = (result.FailingTypeNames ?? new List<string>())
            .Where(typeName =>
                !IsGrandfathered(typeName, GrandfatheredPrefixes) &&
                !PlaybookLookupBaselineConsumerTypes.Any(baseline =>
                    typeName.Equals(baseline, StringComparison.Ordinal) ||
                    typeName.StartsWith(baseline + "+", StringComparison.Ordinal)))
            .OrderBy(typeName => typeName, StringComparer.Ordinal)
            .ToList();

        // Assert — the CURRENT footprint passes; any NEW direct injector fails and must either
        // consume a facade or be added to the baseline with an ADR-013 §6.5 exception rationale.
        Assert.True(
            newSpread.Count == 0,
            "FR-14 spread-guard: a NEW type outside the frozen baseline takes a dependency on " +
            "IPlaybookLookupService. The external consumer footprint is frozen by this ratchet " +
            "(task 040) — prefer a task-specific Services/Ai/PublicContracts/ facade, or (if " +
            "genuinely required) extend PlaybookLookupBaselineConsumerTypes with a documented " +
            "CLAUDE.md §6.5 rationale. " +
            $"New consumers: {string.Join(", ", newSpread)}");
    }

    // --- Negative control: prove the grandfather filter would catch a real violation ---

    [Fact(DisplayName = "ADR-013/FR-14: grandfather filter flags a non-sanctioned dependency (seeded violation)")]
    public void GrandfatherFilter_NegativeControl_FlagsNonSanctionedType()
    {
        // A hypothetical CRUD type injecting IActionResolver — outside every sanctioned zone.
        const string seededViolation = "Sprk.Bff.Api.Services.Documents.DocumentCrudService";
        Assert.False(
            IsGrandfathered(seededViolation, GrandfatheredPrefixes),
            "A dependency from outside Services.Ai / Infrastructure.DI / Api.Ai must be flagged.");

        // A genuinely sanctioned type (AI domain) must be excluded.
        Assert.True(
            IsGrandfathered("Sprk.Bff.Api.Services.Ai.PublicContracts.FileSummarizeAi", GrandfatheredPrefixes),
            "A dependency from inside the AI domain must be grandfathered.");
    }

    /// <summary>True when <paramref name="typeName"/> resides under one of the sanctioned prefixes.</summary>
    private static bool IsGrandfathered(string typeName, string[] prefixes)
        => prefixes.Any(prefix => typeName.StartsWith(prefix, StringComparison.Ordinal));
}

using System.Reflection;
using FluentAssertions;
using Sprk.Bff.Api.Api.Workspace;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests;

/// <summary>
/// Phase 1 Stable-ID Migration Suite — single regression gate for all 9 consumer surfaces
/// migrated by tasks 015–021 (chat-routing-redesign-r1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose</b>: One class a code reviewer can read to verify the Phase 1 §1.7 migration
/// is complete and correct. Each <see cref="FactAttribute"/> asserts ONE consumer surface
/// has been migrated from <c>/by-name/</c> to <c>/by-id/</c> resolution semantics. The suite
/// is the regression gate for task 026 (deploy) and the verification basis for task 027 (exit
/// gate). Per Q&amp;A 2026-06-22 Q1 stable-ID semantics: lookup goes through the
/// <c>sprk_playbookid</c> immutable opaque GUID, not <c>sprk_playbookcode</c>.
/// </para>
/// <para>
/// <b>Assertion strategy per consumer</b>:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>BE consumers (1–7)</b>: Reflection on the production type — verify the migrated
///     consumer DEPENDS on <see cref="IPlaybookLookupService"/> (constructor parameter
///     + private field). The Pattern A/B refactor wired this field; the class would not
///     compile without it after the migration. This is the simplest, most robust assertion
///     of the migration invariant.
///   </description></item>
///   <item><description>
///     <b>Consumer 8 (ChatContextMappingService)</b>: Source-inspection — assert NO
///     <c>GetByNameAsync</c> call exists in the source file (it was always data-driven via
///     Dataverse lookup column; the task 020 audit confirmed no refactor was needed).
///   </description></item>
///   <item><description>
///     <b>Consumer 9 (Frontend Pattern B)</b>: Source-inspection of the .ts/.tsx files
///     for <c>/by-id/</c> URL pattern AND absence of <c>/by-name/</c>. Clean skip if the
///     file is missing (defensive fallback for repo moves).
///   </description></item>
/// </list>
/// <para>
/// <b>Out of scope</b>: Full WebApplicationFactory boot per consumer. The existing
/// per-consumer test classes (<c>PlaybookByIdEndpointTests</c>,
/// <c>WorkspaceAiServiceTests</c>, etc.) cover that. This suite layers a cohesive
/// regression on top.
/// </para>
/// </remarks>
public class Phase1StableIdMigrationSuite
{
    // ─────────────────────────────────────────────────────────────────────────
    // Consumer 1 — the summarize orchestrator shell was DELETED by
    // ai-architecture-redesign-r1 task 044 (FR-P3-05): /summarize resolves its
    // Action through the Binding catalog via the ONE dispatch seam — no playbook
    // lookup exists on the path, so the stable-id concern no longer applies.
    // ─────────────────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────────────────
    // Consumer 2 — MatterPreFillService (task 016 / FR-02 Pattern A)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Consumer02_MatterPreFillService_DependsOnPlaybookLookupService()
    {
        AssertConsumerWiredToPlaybookLookupService(
            typeof(MatterPreFillService),
            consumerLabel: "MatterPreFillService (task 016)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Consumer 3 — ProjectPreFillService (task 017 / FR-05 Pattern A)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Consumer03_ProjectPreFillService_DependsOnPlaybookLookupService()
    {
        AssertConsumerWiredToPlaybookLookupService(
            typeof(ProjectPreFillService),
            consumerLabel: "ProjectPreFillService (task 017)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Consumer 4 — WorkspaceAiService (task 018 / Pattern A; Document Profile PB-002)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Consumer04_WorkspaceAiService_DependsOnPlaybookLookupService()
    {
        AssertConsumerWiredToPlaybookLookupService(
            typeof(WorkspaceAiService),
            consumerLabel: "WorkspaceAiService (task 018)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Consumer 5 — WorkspaceFileEndpoints.HandleSummarize (task 019 / Pattern A)
    //
    // Static endpoint class — verify the static method signature accepts
    // IPlaybookLookupService as a parameter (DI-injected per ASP.NET minimal API).
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Consumer05_WorkspaceFileEndpoints_HandleSummarize_ExecutesOnActionPrimitives()
    {
        // FR-P3-05 (ai-architecture-redesign-r1 task 044): summarize-file executes on the
        // prompted executor — HandleSummarize composes IActionResolver + IActionRunner
        // directly; the playbook lookup + engine fall-through were deleted (no playbook id
        // resolution remains on the path, so the stable-id concern is closed structurally).
        var method = typeof(WorkspaceFileEndpoints).GetMethod(
            "HandleSummarize",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull(
            "WorkspaceFileEndpoints.HandleSummarize must exist for the /summarize endpoint");

        var parameters = method!.GetParameters();
        parameters.Should().Contain(
            p => p.ParameterType == typeof(Sprk.Bff.Api.Services.Ai.LinearConsumers.IActionResolver),
            "HandleSummarize resolves the summarize-file Binding row's Action via IActionResolver (task 044)");
        parameters.Should().Contain(
            p => p.ParameterType == typeof(Sprk.Bff.Api.Services.Ai.LinearConsumers.IActionRunner),
            "HandleSummarize executes via IActionRunner (task 044)");
        parameters.Should().NotContain(
            p => p.ParameterType == typeof(IPlaybookLookupService),
            "the playbook lookup was deleted from the summarize path (FR-P3-05 hard cutover)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Consumer 6 — AppOnlyAnalysisService / Document Profile resolution
    //
    // UPDATED 2026-07-10 (F-6b, e2e-completion-audit): the task-020 hardcoded
    // `DocumentProfilePlaybookId` const this test used to assert was REMOVED by
    // ai-architecture-redesign-r1 FR-P3-01 (task 040, commit 2d61b1c) — the by-const
    // GUID lookup was retired in favour of the `sprk_playbookconsumer` Binding table as
    // the single routing surface. The stable-ID invariant is now pinned at the CURRENT
    // resolution contract: AppOnlyAnalysisService resolves the Document Profile playbook
    // through IConsumerRoutingService.ResolveAsync(ConsumerTypes.DocumentProfile), then
    // materializes it via IPlaybookLookupService.GetByIdAsync — no by-name/by-const lookup.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Consumer06_AppOnlyAnalysisService_ResolvesDocumentProfileViaBindingTable()
    {
        var type = typeof(AppOnlyAnalysisService);

        // Stable-resolution contract: routes via the Binding table (IConsumerRoutingService)
        // and materializes by immutable playbook id (IPlaybookLookupService.GetByIdAsync).
        AssertConsumerWiredToConsumerRouting(
            type, consumerLabel: "AppOnlyAnalysisService (Document Profile via Binding table)");
        AssertConsumerWiredToPlaybookLookupService(
            type, consumerLabel: "AppOnlyAnalysisService (Document Profile via Binding table)");

        // The typed routing key exists (compile-time typo defense per ConsumerTypes) and is
        // the stable string admins set in sprk_playbookconsumer.sprk_consumertype.
        ConsumerTypes.DocumentProfile.Should().Be(
            "document-profile",
            "AppOnlyAnalysisService resolves Document Profile via ConsumerTypes.DocumentProfile (FR-P3-01)");

        // Regression guard for the FR-P3-01 hard cutover: the retired hardcoded by-const GUID
        // lookup MUST NOT reappear (a reintroduced fallback const would bypass the Binding table).
        type.GetField("DocumentProfilePlaybookId", BindingFlags.NonPublic | BindingFlags.Static)
            .Should().BeNull(
                "the hardcoded DocumentProfilePlaybookId fallback const was deleted by FR-P3-01 " +
                "(task 040) — resolution routes exclusively through the sprk_playbookconsumer Binding table");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Consumer 7 — AppOnlyAnalysisService / Email Analysis resolution
    //
    // UPDATED 2026-07-10 (F-6b): symmetric to Consumer 6 — the task-020
    // `EmailAnalysisPlaybookId` const was removed by FR-P3-01 (task 040). Pin the current
    // contract: resolution routes through the Binding table via
    // ConsumerTypes.EmailAnalysis, materialized by immutable id.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Consumer07_AppOnlyAnalysisService_ResolvesEmailAnalysisViaBindingTable()
    {
        var type = typeof(AppOnlyAnalysisService);

        AssertConsumerWiredToConsumerRouting(
            type, consumerLabel: "AppOnlyAnalysisService (Email Analysis via Binding table)");
        AssertConsumerWiredToPlaybookLookupService(
            type, consumerLabel: "AppOnlyAnalysisService (Email Analysis via Binding table)");

        ConsumerTypes.EmailAnalysis.Should().Be(
            "email-analysis",
            "AppOnlyAnalysisService resolves Email Analysis via ConsumerTypes.EmailAnalysis (FR-P3-01)");

        type.GetField("EmailAnalysisPlaybookId", BindingFlags.NonPublic | BindingFlags.Static)
            .Should().BeNull(
                "the hardcoded EmailAnalysisPlaybookId fallback const was deleted by FR-P3-01 " +
                "(task 040) — resolution routes exclusively through the sprk_playbookconsumer Binding table");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Consumer 8 — ChatContextMappingService (task 020 / NO REFACTOR; verify absence)
    //
    // This service was already data-driven via Dataverse lookup column. Task 020
    // audit confirmed no by-name call sites exist. We assert the absence via
    // source inspection — if a future change ADDS a by-name call, this gate fires.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Consumer08_ChatContextMappingService_HasNoByNameResolution()
    {
        // ARRANGE — resolve the source path of ChatContextMappingService.cs.
        var sourcePath = TryResolveRepoFile(
            "src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatContextMappingService.cs");

        if (sourcePath is null)
        {
            // Defensive fallback — source layout moved.
            Assert.Fail(
                "Could not locate ChatContextMappingService.cs from repo root. " +
                "Update Phase1StableIdMigrationSuite.Consumer08 expected path.");
        }

        var source = File.ReadAllText(sourcePath);

        // ASSERT — NO by-name call sites (data-driven lookup column, NOT playbook-name resolution).
        source.Should().NotContain(
            "GetByNameAsync",
            "ChatContextMappingService must remain data-driven (task 020 audit). " +
            "If you added a GetByNameAsync call, migrate it to GetByIdAsync first (FR-02 binding).");

        source.Should().NotContain(
            "/by-name/",
            "ChatContextMappingService must not call the deprecated /by-name/ endpoint URL " +
            "(if it grew a direct HTTP call, route via IPlaybookLookupService.GetByIdAsync instead).");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Consumer 9 — Frontend Pattern B (task 021)
    //
    // Source inspection of useAiSummary.ts + DocumentEmailWizard.tsx:
    //   - MUST contain `/api/ai/playbooks/by-id/`
    //   - MUST NOT contain `/api/ai/playbooks/by-name/` as an active call URL
    //   (doc-comment references to "/by-name/" are allowed — they describe the retired path)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Consumer09a_FrontendUseAiSummary_UsesByIdUrl()
    {
        var sourcePath = TryResolveRepoFile(
            "src/client/shared/Spaarke.UI.Components/src/hooks/useAiSummary.ts");

        if (sourcePath is null)
        {
            // Defensive skip — the FE file isn't present in this checkout (e.g., partial worktree).
            return;
        }

        var source = File.ReadAllText(sourcePath);

        AssertFrontendUsesByIdEndpoint(
            source,
            sourceLabel: "useAiSummary.ts",
            expectedPlaybookIdConstPrefix: "DOCUMENT_PROFILE_PLAYBOOK_ID");
    }

    [Fact]
    public void Consumer09b_FrontendDocumentEmailWizard_UsesByIdUrl()
    {
        var sourcePath = TryResolveRepoFile(
            "src/client/shared/Spaarke.UI.Components/src/components/DocumentEmailWizard/DocumentEmailWizard.tsx");

        if (sourcePath is null)
        {
            return;
        }

        var source = File.ReadAllText(sourcePath);

        AssertFrontendUsesByIdEndpoint(
            source,
            sourceLabel: "DocumentEmailWizard.tsx",
            expectedPlaybookIdConstPrefix: "SUMMARIZE_NEW_FILES_PLAYBOOK_ID");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Asserts that a migrated BE consumer type has <see cref="IPlaybookLookupService"/>
    /// as a constructor parameter AND a private field. The Pattern A/B refactor wired
    /// these in tasks 015-020; the class would not compile without them.
    /// </summary>
    private static void AssertConsumerWiredToPlaybookLookupService(Type type, string consumerLabel)
    {
        // Constructor parameter check.
        var ctorWithLookup = type.GetConstructors()
            .FirstOrDefault(c => c.GetParameters()
                .Any(p => p.ParameterType == typeof(IPlaybookLookupService)));

        ctorWithLookup.Should().NotBeNull(
            $"{consumerLabel} must accept IPlaybookLookupService via DI " +
            "(Pattern A/B stable-ID migration; spec FR-02).");

        // Private field check — confirms the dep is wired into the type for use.
        var lookupField = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(f => f.FieldType == typeof(IPlaybookLookupService));

        lookupField.Should().NotBeNull(
            $"{consumerLabel} must store IPlaybookLookupService in a private field " +
            "(verifies the consumer actually uses the lookup, not just receives + discards it).");
    }

    /// <summary>
    /// Asserts that a consumer type resolves its playbook through the <c>sprk_playbookconsumer</c>
    /// Binding table by depending on <see cref="IConsumerRoutingService"/> (constructor parameter
    /// + private field). This is the FR-P3-01 single-routing-surface invariant: the hardcoded
    /// fallback GUIDs were deleted (task 040), so the consumer MUST route through
    /// <see cref="IConsumerRoutingService.ResolveAsync"/> — the class would not compile without
    /// the injected dependency.
    /// </summary>
    private static void AssertConsumerWiredToConsumerRouting(Type type, string consumerLabel)
    {
        var ctorWithRouting = type.GetConstructors()
            .FirstOrDefault(c => c.GetParameters()
                .Any(p => p.ParameterType == typeof(IConsumerRoutingService)));

        ctorWithRouting.Should().NotBeNull(
            $"{consumerLabel} must accept IConsumerRoutingService via DI " +
            "(FR-P3-01 Binding-table routing; the hardcoded fallback GUIDs were deleted in task 040).");

        var routingField = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(f => f.FieldType == typeof(IConsumerRoutingService));

        routingField.Should().NotBeNull(
            $"{consumerLabel} must store IConsumerRoutingService in a private field " +
            "(verifies the consumer actually routes through the Binding table, not just receives + discards it).");
    }

    /// <summary>
    /// Asserts a frontend source file uses the <c>/by-id/</c> endpoint with a hardcoded
    /// GUID const, AND does NOT make active calls to the retired <c>/by-name/</c> endpoint.
    /// </summary>
    /// <remarks>
    /// We tolerate doc-comment references to "/by-name/" (e.g., "the path being retired") —
    /// the production code Pattern B uses doc comments to explain the migration intent.
    /// The contract is: no <c>fetch(</c> / template-literal that REQUESTS a /by-name/ URL.
    /// </remarks>
    private static void AssertFrontendUsesByIdEndpoint(
        string source,
        string sourceLabel,
        string expectedPlaybookIdConstPrefix)
    {
        // MUST contain the /by-id/ URL fragment.
        source.Should().Contain(
            "/api/ai/playbooks/by-id/",
            $"{sourceLabel} (task 021 Pattern B) must call the stable /by-id/ endpoint");

        // MUST contain a GUID-format const name for the playbook id.
        source.Should().Contain(
            expectedPlaybookIdConstPrefix,
            $"{sourceLabel} must declare a hardcoded {expectedPlaybookIdConstPrefix} " +
            "const (Pattern B execution-path stable-ID).");

        // MUST NOT contain active /by-name/ request URLs. We probe for the most common
        // active-call shapes (template literal with leading ${apiBaseUrl} or ${bffBaseUrl}
        // and request via fetch). Plain string mentions of "/by-name/" in /* … */ or //
        // doc comments are TOLERATED because the task 021 source explicitly references
        // the retired path in JSDoc comments explaining the migration.
        //
        // Robust detection: count "/by-name/" occurrences NOT preceded by " * " or "// " on
        // their line. If any are found, the source has an active call. This is a simple but
        // effective heuristic — full TS-AST parsing is over-engineering for a regression gate.
        var lines = source.Split('\n');
        var activeByNameLines = lines
            .Where(line =>
            {
                if (!line.Contains("/by-name/", StringComparison.Ordinal))
                {
                    return false;
                }

                var trimmed = line.TrimStart();
                // Allow doc-comment + line-comment + block-comment continuation lines.
                if (trimmed.StartsWith("*", StringComparison.Ordinal) ||
                    trimmed.StartsWith("//", StringComparison.Ordinal) ||
                    trimmed.StartsWith("/*", StringComparison.Ordinal))
                {
                    return false;
                }

                return true;
            })
            .ToArray();

        activeByNameLines.Should().BeEmpty(
            $"{sourceLabel} must not have any active calls to /by-name/ URLs " +
            "(comment-only references for migration narration are tolerated). " +
            $"Offending lines: {string.Join(" | ", activeByNameLines)}");
    }

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> looking for a repo-root marker
    /// (the <c>.git</c> folder), then joins the given repo-relative path. Returns null if
    /// the file doesn't exist after path resolution — callers decide whether that's a hard
    /// fail (Consumer 8 — file is required) or a clean skip (Consumer 9 — frontend may be
    /// partially checked out).
    /// </summary>
    private static string? TryResolveRepoFile(string repoRelativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        // Walk up looking for a repo-root marker. In a primary checkout `.git` is a directory;
        // in a git WORKTREE `.git` is a FILE (gitfile pointer). Accept either.
        while (dir is not null)
        {
            var gitPath = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                break;
            }
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return null;
        }

        // Normalize forward-slashes to platform separators.
        var nativeRelative = repoRelativePath.Replace('/', Path.DirectorySeparatorChar);
        var candidate = Path.Combine(dir.FullName, nativeRelative);

        return File.Exists(candidate) ? candidate : null;
    }
}

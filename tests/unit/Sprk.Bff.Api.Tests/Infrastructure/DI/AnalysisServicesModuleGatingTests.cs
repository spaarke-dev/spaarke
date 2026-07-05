// ai-architecture-redesign-r1 — Task 006 (FR-P0-05, 2026-07-05)
// Registration-hygiene behavior tests: PlaybookLookupService + OutputOrchestratorService
// moved OUT of FinanceModule into AnalysisServicesModule.AddPlaybookServices (compound
// Analysis:Enabled && DocumentIntelligence:Enabled gate) with ADR-032 P3 Null-Object peers;
// LinearConsumers stack moved under the same compound gate.
//
// ADR-038 note: these are NOT container-wiring assertions ("service X is registered") —
// they prove the OBSERVABLE kill-switch contract per the task acceptance criteria:
//   (a) with the compound AI gate OFF, the REAL implementations are unresolvable
//       (resolution failure — GetService returns null / resolves the Null peer instead), and
//   (b) the Null peers fail fast with FeatureDisabledException carrying a STABLE errorCode —
//       the exact exception endpoint catch sites convert to the canonical 503 ProblemDetails
//       via FeatureDisabledResults.AsFeatureDisabled503 (ADR-018 + ADR-019).
// Pattern precedent: CacheModuleTests (branch matrix) + NullInsightsIntentClassifierTests
// (P3 fail-fast contract).

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.DI;

public class AnalysisServicesModuleGatingTests
{
    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static IConfiguration BuildConfiguration(bool analysisEnabled, bool documentIntelligenceEnabled)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Analysis:Enabled"] = analysisEnabled ? "true" : "false",
                ["DocumentIntelligence:Enabled"] = documentIntelligenceEnabled ? "true" : "false",
            })
            .Build();
    }

    /// <summary>
    /// Builds the AnalysisServicesModule service graph for a compound-OFF combination.
    /// Only logging is added on top — the Null peers MUST be constructible from
    /// logger-only deps per ADR-032 (that constraint is itself part of what these
    /// tests exercise: resolution succeeds without any AI dependency present).
    /// </summary>
    private static ServiceProvider BuildCompoundOffProvider(bool analysisEnabled, bool documentIntelligenceEnabled)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAnalysisServicesModule(BuildConfiguration(analysisEnabled, documentIntelligenceEnabled));
        return services.BuildServiceProvider();
    }

    // Both compound-OFF combinations (Analysis off / DocIntel off) — the gate is compound,
    // so BOTH branches must register the Null peers (mirrors the §F.1-runtime fixture shape).
    public static TheoryData<bool, bool> CompoundOffCombinations => new()
    {
        { false, true },   // Analysis:Enabled=false  (acceptance-criterion combination)
        { true, false },   // DocumentIntelligence:Enabled=false
        { false, false },  // both off
    };

    // ===================================================================
    // IPlaybookLookupService — real unresolvable + Null peer 503 pattern
    // ===================================================================

    [Theory]
    [MemberData(nameof(CompoundOffCombinations))]
    public async Task PlaybookLookup_WhenCompoundGateOff_GetByIdAsync_FailsFastWithStable503ErrorCode(
        bool analysisEnabled, bool documentIntelligenceEnabled)
    {
        using var provider = BuildCompoundOffProvider(analysisEnabled, documentIntelligenceEnabled);
        using var scope = provider.CreateScope();

        var lookup = scope.ServiceProvider.GetRequiredService<IPlaybookLookupService>();

        // Real implementation must be unresolvable — the Null peer answers instead.
        lookup.Should().NotBeOfType<PlaybookLookupService>(
            "the real PlaybookLookupService must be unresolvable when the compound AI gate is off (FR-P0-05)");

        var ex = await Assert.ThrowsAsync<FeatureDisabledException>(
            () => lookup.GetByIdAsync("1e657651-9308-f111-8407-7c1e520aa4df", CancellationToken.None));

        ex.ErrorCode.Should().Be(NullPlaybookLookupService.ErrorCode);
        ex.ErrorCode.Should().Be("ai.playbook.lookup.disabled",
            "errorCode must be stable across releases — endpoint catch sites surface it in the 503 ProblemDetails");
    }

    [Fact]
    public void PlaybookLookup_WhenAnalysisDisabled_CacheClears_AreQuietNoOps()
    {
        using var provider = BuildCompoundOffProvider(analysisEnabled: false, documentIntelligenceEnabled: true);
        using var scope = provider.CreateScope();

        var lookup = scope.ServiceProvider.GetRequiredService<IPlaybookLookupService>();

        // Admin cache-flush flows must NOT fail under the kill switch — nothing is cached,
        // so "nothing happened" is the truthful outcome (P2 semantics for side effects).
        var clearOne = () => lookup.ClearCache("1e657651-9308-f111-8407-7c1e520aa4df");
        var clearAll = () => lookup.ClearAllCache();

        clearOne.Should().NotThrow();
        clearAll.Should().NotThrow();
    }

    // ===================================================================
    // IOutputOrchestratorService — real unresolvable + Null peer fail-fast
    // ===================================================================

    [Theory]
    [MemberData(nameof(CompoundOffCombinations))]
    public async Task OutputOrchestrator_WhenCompoundGateOff_ApplyOutputMapping_FailsFastWithStable503ErrorCode(
        bool analysisEnabled, bool documentIntelligenceEnabled)
    {
        using var provider = BuildCompoundOffProvider(analysisEnabled, documentIntelligenceEnabled);
        using var scope = provider.CreateScope();

        var orchestrator = scope.ServiceProvider.GetRequiredService<IOutputOrchestratorService>();

        orchestrator.Should().NotBeOfType<OutputOrchestratorService>(
            "the real OutputOrchestratorService must be unresolvable when the compound AI gate is off (FR-P0-05)");

        var ex = await Assert.ThrowsAsync<FeatureDisabledException>(
            () => orchestrator.ApplyOutputMappingAsync(
                Guid.NewGuid(), new PlaybookExecutionContext(), CancellationToken.None));

        ex.ErrorCode.Should().Be(NullOutputOrchestratorService.ErrorCode);
        ex.ErrorCode.Should().Be("ai.output-orchestrator.disabled",
            "errorCode must be stable across releases");
    }

    // ===================================================================
    // LinearConsumers stack — gated as one unit under the compound AI gate
    // ===================================================================

    [Theory]
    [MemberData(nameof(CompoundOffCombinations))]
    public async Task FileSummarize_WhenCompoundGateOff_ThrowsFeatureDisabledOnFirstMoveNext(
        bool analysisEnabled, bool documentIntelligenceEnabled)
    {
        using var provider = BuildCompoundOffProvider(analysisEnabled, documentIntelligenceEnabled);
        using var scope = provider.CreateScope();

        // WorkspaceFileEndpoints.HandleSummarize injects the CONCRETE type and is mapped
        // unconditionally — resolution must succeed (Null subclass) and fail fast on use.
        var summarize = scope.ServiceProvider.GetRequiredService<FileSummarizeService>();
        summarize.Should().BeOfType<NullFileSummarizeService>(
            "the real FileSummarizeService (AI ctor deps) must be unresolvable when the compound AI gate is off");

        var act = async () =>
        {
            await foreach (var _ in summarize.ExecuteAsync(
                "some extracted text", "file.pdf", "summarize-file", new DefaultHttpContext(), CancellationToken.None))
            {
            }
        };

        var ex = (await act.Should().ThrowAsync<FeatureDisabledException>(
            "the endpoint's catch (FeatureDisabledException) converts this to the 503 kill-switch pattern")).Which;
        ex.ErrorCode.Should().Be(NullFileSummarizeService.ErrorCode);
        ex.ErrorCode.Should().Be("ai.linear-consumers.disabled",
            "errorCode must be stable across releases");
    }

    [Fact]
    public void LinearConsumersPrimitives_WhenAnalysisDisabled_AreUnresolvable()
    {
        using var provider = BuildCompoundOffProvider(analysisEnabled: false, documentIntelligenceEnabled: true);
        using var scope = provider.CreateScope();

        // The prompted-executor stack toggles as ONE unit (FR-P0-05): none of the
        // LinearConsumers primitives/consumer services may resolve when the compound AI
        // gate is off. Consumers are either gated with them (DocumentProfileService via
        // MapAnalysisEndpoints), tolerate null (Matter/ProjectPreFillService optional ctor
        // params), or have a Null peer (FileSummarizeService, asserted above).
        scope.ServiceProvider.GetService<IActionResolver>().Should().BeNull();
        scope.ServiceProvider.GetService<IActionRunner>().Should().BeNull();
        scope.ServiceProvider.GetService<IDocumentTextSource>().Should().BeNull();
        scope.ServiceProvider.GetService<ISessionFileTextSource>().Should().BeNull();
        scope.ServiceProvider.GetService<DocumentProfileService>().Should().BeNull();
    }

    // ===================================================================
    // FinanceModule — the two playbook services have EXITED (FR-P0-05)
    // ===================================================================

    [Fact]
    public void FinanceModule_NoLongerRegisters_PlaybookLookup_Or_OutputOrchestrator()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFinanceModule(BuildConfiguration(analysisEnabled: true, documentIntelligenceEnabled: false));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Resolution failure proves the FinanceModule exit — with ONLY FinanceModule loaded,
        // neither playbook service exists. Their home is AnalysisServicesModule (compound gate).
        scope.ServiceProvider.GetService<IPlaybookLookupService>().Should().BeNull(
            "IPlaybookLookupService moved to AnalysisServicesModule.AddPlaybookServices (FR-P0-05)");
        scope.ServiceProvider.GetService<IOutputOrchestratorService>().Should().BeNull(
            "IOutputOrchestratorService moved to AnalysisServicesModule.AddPlaybookServices (FR-P0-05)");
    }
}

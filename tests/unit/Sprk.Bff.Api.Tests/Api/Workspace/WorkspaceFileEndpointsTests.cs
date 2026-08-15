using System.Reflection;
using FluentAssertions;
using Sprk.Bff.Api.Api.Workspace;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Workspace;

/// <summary>
/// Unit tests for <see cref="WorkspaceFileEndpoints"/> — contract invariants for the
/// <c>/api/workspace/files/summarize</c> endpoint after the FR-P3-05 hard cutover
/// (ai-architecture-redesign-r1 task 044: wrapper absorption + engine fall-through deletion).
/// </summary>
/// <remarks>
/// <para>
/// <b>Approach</b>: reflection-based contract tests in the style of
/// <c>AnalysisEndpointsTests</c>. The endpoint handlers are <see langword="static"/>
/// route delegates with no DI seams, so we pin behavior via signature and member
/// inspection rather than spinning up a full in-process WebApplication.
/// Integration coverage for the full SSE pipeline lives separately (the live PCF
/// + workspace UI exercise the path end-to-end against the deployed BFF).
/// </para>
/// <para>
/// <b>FR-P3-05</b>: summarize-file executes EXCLUSIVELY on the prompted executor —
/// <see cref="IActionResolver"/> resolves the summarize-file Binding row's Action
/// (sprk_playbookconsumer → sprk_analysisaction, ADR-039 single routing surface) and
/// <see cref="IActionRunner"/> renders + runs it. The consumer-specific wrapper class,
/// the Playbook Engine fall-through, and the playbook lookup were all DELETED per NFR-08.
/// </para>
/// </remarks>
public class WorkspaceFileEndpointsTests
{
    #region Facade Boundary Execution Contract (ADR-013 / BFF §10 bullet 3 — task 024)

    [Fact]
    public void HandleSummarize_AcceptsFileSummarizeFacade_ADR013()
    {
        // Task 024 (ADR-013 / BFF §10 bullet 3): the non-AI endpoint composes the prompted
        // executor through the IFileSummarizeAi PublicContracts facade — it MUST NOT inject the
        // Linear AI Consumer primitives (IActionResolver / IActionRunner) directly (that was the
        // A-1 violation). The facade wraps those primitives and preserves the SSE chunk + 503
        // contract byte-for-byte.
        var handler = GetPrivateStaticMethod("HandleSummarize");
        handler.Should().NotBeNull(
            "the workspace summarize handler must exist on WorkspaceFileEndpoints");

        var parameterTypes = handler!.GetParameters().Select(p => p.ParameterType).ToList();
        parameterTypes.Should().Contain(typeof(IFileSummarizeAi),
            "ADR-013 — HandleSummarize MUST resolve + run the summarize-file Action via the " +
            "IFileSummarizeAi PublicContracts facade, not the AI-internal primitives");
        parameterTypes.Should().NotContain(typeof(IActionResolver),
            "task 024 A-1 — IActionResolver MUST NOT be injected into the non-AI endpoint " +
            "(it now lives behind the IFileSummarizeAi facade)");
        parameterTypes.Should().NotContain(typeof(IActionRunner),
            "task 024 A-1 — IActionRunner MUST NOT be injected into the non-AI endpoint " +
            "(it now lives behind the IFileSummarizeAi facade)");
    }

    [Fact]
    public void HandleSummarize_HasNoEngineOrLookupOrConfigParameter()
    {
        // FR-P3-05 hard cutover (still binding) — the engine fall-through and the playbook lookup
        // were DELETED (NFR-08: no shims). The handler may not carry the frozen-engine facade,
        // a playbook lookup, or a config-options dependency; the Action catalog is the only
        // execution source.
        var handler = GetPrivateStaticMethod("HandleSummarize");
        handler.Should().NotBeNull();
        handler!.GetParameters().Should().NotContain(
            p => p.ParameterType == typeof(IPlaybookOrchestrationService),
            "FR-P3-05 — the engine fall-through was deleted; the executor is the only path");
        handler.GetParameters().Should().NotContain(
            p => p.ParameterType == typeof(IPlaybookLookupService),
            "FR-P3-05 — no playbook resolution remains on the summarize path");
        handler.GetParameters().Should().NotContain(
            p => p.ParameterType.FullName!.Contains("WorkspaceOptions"),
            "FR-P3-01 — the WorkspaceOptions config fallback stays deleted");
    }

    [Fact]
    public void WorkspaceFileEndpoints_Source_DelegatesToFileSummarizeFacade_ADR013()
    {
        // Task 024: the endpoint MUST delegate to IFileSummarizeAi.SummarizeAsync and MUST NOT
        // itself resolve/run the primitives or carry any engine/config surface (NFR-08).
        var source = File.ReadAllText(LocateWorkspaceFileEndpointsSource());
        source.Should().Contain("fileSummarizeAi.SummarizeAsync(",
            "ADR-013 — endpoint MUST stream via the IFileSummarizeAi facade");
        source.Should().NotContain("actionResolver.ResolveAsync(",
            "task 024 A-1 — resolve moved behind the facade; the endpoint no longer calls it");
        source.Should().NotContain("actionRunner.RunAsync(",
            "task 024 A-1 — run moved behind the facade; the endpoint no longer calls it");
        source.Should().NotContain("SummarizePlaybookId",
            "FR-P3-01 — no reference to the deleted config property may remain");
        source.Should().NotContain("WorkspaceOptions",
            "FR-P3-01 — the WorkspaceOptions config surface was deleted entirely");
        source.Should().NotContain("PlaybookRunRequest",
            "FR-P3-05 — no engine dispatch may remain on the workspace summarize path");
    }

    [Fact]
    public void FileSummarizeAi_Source_ExecutesOnActionResolverWithConstant_ADR013()
    {
        // Task 024 + FR-P3-05 + code-review S-5: the facade MUST resolve via
        // IActionResolver.ResolveAsync with the ConsumerTypes.SummarizeFile compile-time constant
        // (never a literal string) and run via IActionRunner.RunAsync.
        var source = File.ReadAllText(LocateFileSummarizeAiSource());
        source.Should().Contain("ConsumerTypes.SummarizeFile",
            "code-review S-5 — facade MUST use the ConsumerTypes.SummarizeFile constant, " +
            "not a literal string");
        source.Should().Contain("_actionResolver.ResolveAsync(",
            "FR-P3-05 — facade MUST resolve the Action via IActionResolver");
        source.Should().Contain("_actionRunner.RunAsync(",
            "FR-P3-05 — facade MUST execute via IActionRunner");
    }

    [Fact]
    public void Summarize_SurfacesResolutionFailureAsErrorChunk_ADR032()
    {
        // FR-04 / NFR-02 fail-fast contract preserved through the facade relocation: when the
        // Action cannot be resolved (missing Binding row / no Action target), the facade MUST
        // surface an SSE error chunk (not a silent no-op) — and the kill-switch
        // FeatureDisabledException MUST propagate to the endpoint's 503 pattern.
        var facadeSource = File.ReadAllText(LocateFileSummarizeAiSource());
        facadeSource.Should().Contain("Failed to resolve action",
            "FR-04 — a resolution miss MUST surface as an actionable SSE error chunk");

        var endpointSource = File.ReadAllText(LocateWorkspaceFileEndpointsSource());
        endpointSource.Should().Contain("catch (FeatureDisabledException",
            "ADR-032 — the kill-switch exception must propagate to the endpoint's 503 pattern");
    }

    #endregion

    private static MethodInfo? GetPrivateStaticMethod(string name) =>
        typeof(WorkspaceFileEndpoints).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static);

    private static string LocateWorkspaceFileEndpointsSource() =>
        LocateRepoSource("Api", "Workspace", "WorkspaceFileEndpoints.cs");

    private static string LocateFileSummarizeAiSource() =>
        LocateRepoSource("Services", "Ai", "PublicContracts", "FileSummarizeAi.cs");

    private static string LocateRepoSource(params string[] relativeUnderProject)
    {
        var assemblyPath = typeof(WorkspaceFileEndpointsTests).Assembly.Location;
        var dir = new DirectoryInfo(Path.GetDirectoryName(assemblyPath)!);

        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "server")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("repo root must be locatable from the test assembly path");
        var segments = new[] { dir!.FullName, "src", "server", "api", "Sprk.Bff.Api" }
            .Concat(relativeUnderProject)
            .ToArray();
        var source = Path.Combine(segments);
        File.Exists(source).Should().BeTrue($"source must exist at '{source}'");
        return source;
    }
}

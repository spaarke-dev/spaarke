using System.Reflection;
using FluentAssertions;
using Sprk.Bff.Api.Api.Workspace;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
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
    #region FR-P3-05 — Executor-Only Execution Contract

    [Fact]
    public void HandleSummarize_AcceptsExecutorPrimitives_FRP305()
    {
        // FR-P3-05: the endpoint composes the prompted-executor primitives directly (the
        // wrapper class was absorbed). Their presence on the delegate signature pins the
        // binding — no wrapper, no engine, no playbook lookup.
        var handler = GetPrivateStaticMethod("HandleSummarize");
        handler.Should().NotBeNull(
            "the workspace summarize handler must exist on WorkspaceFileEndpoints");

        var parameterTypes = handler!.GetParameters().Select(p => p.ParameterType).ToList();
        parameterTypes.Should().Contain(typeof(IActionResolver),
            "FR-P3-05 — HandleSummarize MUST resolve the summarize-file Binding row's Action " +
            "via IActionResolver (the ONLY routing surface, ADR-039)");
        parameterTypes.Should().Contain(typeof(IActionRunner),
            "FR-P3-05 — HandleSummarize MUST execute via IActionRunner (the prompted executor)");
    }

    [Fact]
    public void HandleSummarize_HasNoEngineOrLookupOrConfigParameter_FRP305()
    {
        // FR-P3-05 hard cutover — the engine fall-through and the playbook lookup were
        // DELETED (NFR-08: no shims). The handler may not carry the frozen-engine facade,
        // a playbook lookup, or a config-options dependency; the Action catalog is the
        // only execution source.
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
    public void WorkspaceFileEndpoints_Source_ExecutesOnActionResolverWithConstant_FRP305()
    {
        // FR-P3-05 + code-review S-5: the endpoint MUST resolve via
        // IActionResolver.ResolveAsync with the ConsumerTypes.SummarizeFile compile-time
        // constant (never a literal string), and no engine dispatch or config fallback may
        // remain, comments included (NFR-08 hard cutover).
        var source = File.ReadAllText(LocateWorkspaceFileEndpointsSource());
        source.Should().Contain("ConsumerTypes.SummarizeFile",
            "code-review S-5 — endpoint MUST use the ConsumerTypes.SummarizeFile constant, " +
            "not a literal string");
        source.Should().Contain("actionResolver.ResolveAsync(",
            "FR-P3-05 — endpoint MUST resolve the Action via IActionResolver");
        source.Should().Contain("actionRunner.RunAsync(",
            "FR-P3-05 — endpoint MUST execute via IActionRunner");
        source.Should().NotContain("SummarizePlaybookId",
            "FR-P3-01 — no reference to the deleted config property may remain, " +
            "comments included (NFR-08 hard cutover)");
        source.Should().NotContain("WorkspaceOptions",
            "FR-P3-01 — the WorkspaceOptions config surface was deleted entirely");
        source.Should().NotContain("PlaybookRunRequest",
            "FR-P3-05 — no engine dispatch may remain on the workspace summarize path");
    }

    [Fact]
    public void WorkspaceFileEndpoints_Source_SurfacesResolutionFailureAsErrorChunk_FRP305()
    {
        // FR-04 / NFR-02 fail-fast contract preserved through the cutover: when the Action
        // cannot be resolved (missing Binding row / no Action target), the endpoint MUST
        // surface an SSE error chunk (not a silent no-op) — and the kill-switch
        // FeatureDisabledException MUST propagate to the endpoint's 503 pattern.
        var source = File.ReadAllText(LocateWorkspaceFileEndpointsSource());
        source.Should().Contain("Failed to resolve action",
            "FR-04 — a resolution miss MUST surface as an actionable SSE error chunk");
        source.Should().Contain("catch (FeatureDisabledException)",
            "ADR-032 — the kill-switch exception must propagate to the endpoint's 503 pattern");
    }

    #endregion

    private static MethodInfo? GetPrivateStaticMethod(string name) =>
        typeof(WorkspaceFileEndpoints).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static);

    private static string LocateWorkspaceFileEndpointsSource()
    {
        var assemblyPath = typeof(WorkspaceFileEndpointsTests).Assembly.Location;
        var dir = new DirectoryInfo(Path.GetDirectoryName(assemblyPath)!);

        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "server")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("repo root must be locatable from the test assembly path");
        var source = Path.Combine(
            dir!.FullName,
            "src", "server", "api", "Sprk.Bff.Api",
            "Api", "Workspace", "WorkspaceFileEndpoints.cs");
        File.Exists(source).Should().BeTrue($"WorkspaceFileEndpoints.cs must exist at '{source}'");
        return source;
    }
}

using System.Reflection;
using FluentAssertions;
using Sprk.Bff.Api.Api.Workspace;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Workspace;

/// <summary>
/// Unit tests for <see cref="WorkspaceFileEndpoints"/> — contract invariants for the
/// <c>/api/workspace/files/summarize</c> endpoint after the FR-P3-01 hard cutover
/// (ai-architecture-redesign-r1 task 040).
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
/// <b>FR-P3-01</b>: the playbook is resolved EXCLUSIVELY via
/// <see cref="IConsumerRoutingService"/> (sprk_playbookconsumer Binding table,
/// consumerType <c>summarize-file</c>, MIME type in the routing context). The legacy
/// WorkspaceOptions typed-options fallback was DELETED per NFR-08. Fail-fast on a
/// routing miss — InvalidOperationException so the SSE stream surfaces an error chunk.
/// </para>
/// </remarks>
public class WorkspaceFileEndpointsTests
{
    #region FR-P3-01 — Routing-Only Resolution Contract

    [Fact]
    public void HandleSummarize_AcceptsIPlaybookLookupServiceParameter_FR04()
    {
        // The endpoint MUST load the routed playbook via IPlaybookLookupService.GetByIdAsync
        // at runtime — not via a hardcoded GUID. The service is wired into the static
        // handler's parameter list, so its presence on the delegate signature pins the binding.
        var handler = GetPrivateStaticMethod("HandleSummarize");
        handler.Should().NotBeNull(
            "the workspace summarize handler must exist on WorkspaceFileEndpoints");

        var parameterTypes = handler!.GetParameters().Select(p => p.ParameterType).ToList();
        parameterTypes.Should().Contain(typeof(IPlaybookLookupService),
            "HandleSummarize MUST accept IPlaybookLookupService so the endpoint can load " +
            "the routed playbook by stable-ID at runtime (no hardcoded GUID fallback)");
    }

    [Fact]
    public void HandleSummarize_AcceptsIConsumerRoutingServiceParameter_FRP301()
    {
        // FR-P3-01: the workspace /summarize endpoint MUST resolve its playbook EXCLUSIVELY
        // via IConsumerRoutingService.ResolveAsync(ConsumerTypes.SummarizeFile, …) querying
        // sprk_playbookconsumer. The routing service is wired into the static handler's
        // parameter list, so its presence on the delegate signature pins the binding.
        var handler = GetPrivateStaticMethod("HandleSummarize");
        handler.Should().NotBeNull(
            "the workspace summarize handler must exist on WorkspaceFileEndpoints");

        var parameterTypes = handler!.GetParameters().Select(p => p.ParameterType).ToList();
        parameterTypes.Should().Contain(typeof(IConsumerRoutingService),
            "FR-P3-01 — HandleSummarize MUST accept IConsumerRoutingService so the " +
            "endpoint can resolve the summarize playbook via the sprk_playbookconsumer routing " +
            "table (with the MIME type passed through RoutingContext for content-aware routing).");
    }

    [Fact]
    public void HandleSummarize_HasNoWorkspaceOptionsParameter_FRP301()
    {
        // FR-P3-01 hard cutover — the WorkspaceOptions typed-options fallback surface was
        // DELETED (NFR-08: no shims). Neither the handler nor its SSE helper may carry a
        // WorkspaceOptions dependency; routing is the only source.
        var handler = GetPrivateStaticMethod("HandleSummarize");
        handler.Should().NotBeNull();
        handler!.GetParameters().Should().NotContain(
            p => p.ParameterType.FullName!.Contains("WorkspaceOptions"),
            "FR-P3-01 — the WorkspaceOptions config fallback was deleted; " +
            "the routing table is the ONLY playbook-resolution source");

        var helper = GetPrivateStaticMethod("RunSummarizePlaybookAsSSEAsync");
        helper.Should().NotBeNull();
        helper!.GetParameters().Should().NotContain(
            p => p.ParameterType.FullName!.Contains("WorkspaceOptions"),
            "FR-P3-01 — the SSE helper owns the resolution call and MUST NOT read config");
    }

    [Fact]
    public void WorkspaceFileEndpoints_Source_CallsConsumerRoutingResolveAsyncWithMimeType_FRP301()
    {
        // FR-P3-01 + FR-1R-04: the endpoint MUST call IConsumerRoutingService.ResolveAsync
        // with the ConsumerTypes.SummarizeFile compile-time constant AND pass a
        // RoutingContext carrying the uploaded file's MIME type so sprk_matchconditions
        // JSON predicates can route per content type (NDA PDF → specialized playbook, etc.).
        // Hardening per code-review S-5: ConsumerTypes constant rather than literal string.
        // No config fallback may remain, comments included (NFR-08 hard cutover).
        var source = File.ReadAllText(LocateWorkspaceFileEndpointsSource());
        source.Should().Contain("ConsumerTypes.SummarizeFile",
            "code-review S-5 — endpoint MUST use the ConsumerTypes.SummarizeFile constant, " +
            "not a literal string");
        source.Should().Contain(".ResolveAsync(",
            "FR-P3-01 — endpoint MUST call IConsumerRoutingService.ResolveAsync");
        source.Should().Contain("RoutingContext",
            "FR-1R-04 — endpoint MUST construct a RoutingContext so MIME-aware routing works");
        source.Should().Contain("MimeType",
            "FR-1R-04 — RoutingContext.MimeType MUST be populated for content-aware routing");
        source.Should().NotContain("SummarizePlaybookId",
            "FR-P3-01 — no reference to the deleted config property may remain, " +
            "comments included (NFR-08 hard cutover)");
        source.Should().NotContain("WorkspaceOptions",
            "FR-P3-01 — the WorkspaceOptions config surface was deleted entirely");
    }

    [Fact]
    public void WorkspaceFileEndpoints_Source_FailsFastOnRoutingMiss_FRP301()
    {
        // FR-04 / NFR-02 fail-fast contract preserved through the cutover: when the routing
        // table has no enabled row, the endpoint MUST throw InvalidOperationException so
        // the SSE stream surfaces an error chunk (not a silent no-op). The message tells
        // the operator to seed the Binding row — no config fallback exists.
        var source = File.ReadAllText(LocateWorkspaceFileEndpointsSource());
        source.Should().Contain("throw new InvalidOperationException(",
            "FR-04 — fail-fast on a routing miss MUST be preserved");
        source.Should().Contain(
            "No enabled sprk_playbookconsumer row resolves consumerType 'summarize-file'",
            "FR-P3-01 — the fail-fast message MUST name the missing Binding row");
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

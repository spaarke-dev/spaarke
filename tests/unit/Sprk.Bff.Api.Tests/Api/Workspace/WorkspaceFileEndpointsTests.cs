using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Api.Workspace;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Workspace;

/// <summary>
/// Unit tests for <see cref="WorkspaceFileEndpoints"/> — contract and FR-04 stable-ID
/// resolution invariants for the <c>/api/workspace/files/summarize</c> endpoint.
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
/// <b>FR-04 (chat-routing-redesign-r1 task 019)</b>: the prior hardcoded
/// <c>4a72f99c-a119-f111-8343-7ced8d1dc988</c> GUID fallback (<c>DefaultSummarizePlaybookId</c>)
/// has been removed. The playbook is now resolved at runtime via
/// <see cref="IPlaybookLookupService.GetByIdAsync"/> using
/// <see cref="WorkspaceOptions.SummarizePlaybookId"/> (typed-options per ADR-018, set by
/// task 012). Fail-fast on missing config — no hardcoded fallback at the convergence point.
/// Mirrors the <c>SessionSummarizeOrchestrator</c> pattern proven by Wave 1-D task 015.
/// </para>
/// </remarks>
public class WorkspaceFileEndpointsTests
{
    #region Endpoint Mapping Surface

    [Fact]
    public void MapWorkspaceFileEndpoints_IsPublicStaticExtension()
    {
        var method = typeof(WorkspaceFileEndpoints).GetMethod(
            nameof(WorkspaceFileEndpoints.MapWorkspaceFileEndpoints));

        method.Should().NotBeNull();
        method!.IsStatic.Should().BeTrue();
        method.ReturnType.Should().Be(typeof(IEndpointRouteBuilder));
    }

    #endregion

    #region FR-04 Task 019 — Stable-ID Resolution Contract

    [Fact]
    public void HandleSummarize_AcceptsIPlaybookLookupServiceParameter_FR04()
    {
        // FR-04 task 019: the workspace /summarize endpoint MUST resolve its playbook via
        // IPlaybookLookupService.GetByIdAsync at runtime — not via a hardcoded GUID. The
        // service is wired into the static handler's parameter list, so its presence on the
        // delegate signature pins the binding.
        var handler = GetPrivateStaticMethod("HandleSummarize");
        handler.Should().NotBeNull(
            "the workspace summarize handler must exist on WorkspaceFileEndpoints");

        var parameterTypes = handler!.GetParameters().Select(p => p.ParameterType).ToList();
        parameterTypes.Should().Contain(typeof(IPlaybookLookupService),
            "FR-04 task 019 — HandleSummarize MUST accept IPlaybookLookupService so the " +
            "endpoint can resolve the summarize playbook by stable-ID at runtime (replacing " +
            "the prior hardcoded GUID fallback). Mirrors the SessionSummarizeOrchestrator " +
            "pattern proven by Wave 1-D task 015.");
    }

    [Fact]
    public void HandleSummarize_AcceptsIOptionsWorkspaceOptionsParameter_ADR018()
    {
        // ADR-018 / FR-04 task 012: typed-options surface — the endpoint reads
        // SummarizePlaybookId from WorkspaceOptions, not from raw IConfiguration[..]
        // indexer. Task 012 added the property; task 019 keeps the typed read intact and
        // simply forwards the value into the lookup service.
        var handler = GetPrivateStaticMethod("HandleSummarize");
        handler.Should().NotBeNull();

        var parameterTypes = handler!.GetParameters().Select(p => p.ParameterType).ToList();
        parameterTypes.Should().Contain(typeof(IOptions<WorkspaceOptions>),
            "ADR-018 — HandleSummarize MUST receive IOptions<WorkspaceOptions> (typed options) " +
            "rather than reading IConfiguration[\"Workspace:SummarizePlaybookId\"] via the indexer");
    }

    [Fact]
    public void WorkspaceFileEndpoints_HasNoHardcodedSummarizePlaybookIdConstant_FR04()
    {
        // FR-04 task 019: the prior internal `private static readonly Guid DefaultSummarizePlaybookId`
        // constant (value 4a72f99c-a119-f111-8343-7ced8d1dc988) was removed. Resolution now
        // flows through WorkspaceOptions.SummarizePlaybookId + IPlaybookLookupService.GetByIdAsync.
        // Reflection assert: the constant no longer exists on the static class.
        var members = typeof(WorkspaceFileEndpoints)
            .GetMembers(BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Static
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToArray();

        members.Should().NotContain("DefaultSummarizePlaybookId",
            "FR-04 task 019 — hardcoded DefaultSummarizePlaybookId Guid constant removed; " +
            "playbook resolved at runtime via WorkspaceOptions.SummarizePlaybookId + " +
            "IPlaybookLookupService.GetByIdAsync per ADR-018 typed options + Pattern A stable-ID");
    }

    [Fact]
    public void WorkspaceFileEndpoints_HasNoLegacyHardcodedGuidLiteral_FR04()
    {
        // Defense in depth: scan all static field/constant values for the legacy hardcoded
        // GUID literal. Any remaining occurrence indicates the fallback was preserved
        // somewhere else on the type.
        var legacyGuid = Guid.Parse("4a72f99c-a119-f111-8343-7ced8d1dc988");

        var staticFields = typeof(WorkspaceFileEndpoints)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(Guid))
            .Select(f => (Guid?)f.GetValue(null))
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        staticFields.Should().NotContain(legacyGuid,
            "FR-04 task 019 — the legacy 4a72f99c-a119-f111-8343-7ced8d1dc988 hardcoded " +
            "summarize playbook fallback was removed in favor of fail-fast on missing config + " +
            "IPlaybookLookupService.GetByIdAsync(SummarizePlaybookId, ct) resolution");
    }

    #endregion

    #region WorkspaceOptions Binding (regression — task 012 invariant preserved)

    [Fact]
    public void WorkspaceOptions_SummarizePlaybookIdProperty_RemainsTypedString()
    {
        // Task 012 invariant — the typed-options property carries the per-env GUID value as
        // a string (deferred GUID parse) and is the ONLY config surface for the workspace
        // summarize playbook. Task 019 consumes this value via the lookup service.
        var prop = typeof(WorkspaceOptions)
            .GetProperty(nameof(WorkspaceOptions.SummarizePlaybookId));

        prop.Should().NotBeNull(
            "WorkspaceOptions.SummarizePlaybookId is the typed-options surface for the workspace " +
            "summarize playbook stable-ID per task 012 (preserved across task 019 migration)");
        prop!.PropertyType.Should().Be(typeof(string),
            "the property holds the stable-ID string value (queried via sprk_playbookid alt key), " +
            "not a Guid — parsing happens inside IPlaybookLookupService on the Dataverse side");
    }

    #endregion

    private static MethodInfo? GetPrivateStaticMethod(string name) =>
        typeof(WorkspaceFileEndpoints).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static);
}

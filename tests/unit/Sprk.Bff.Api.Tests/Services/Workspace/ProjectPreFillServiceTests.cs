using System.Reflection;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Workspace;

/// <summary>
/// Unit tests for <see cref="ProjectPreFillService"/> — focused on the FR-P3-01 hard-cutover
/// routing contract (ai-architecture-redesign-r1 task 040).
///
/// <para>
/// Mirrors the test surface of <see cref="MatterPreFillServiceTests"/>:
/// </para>
/// <list type="bullet">
///   <item>Constructor injects <see cref="IConsumerRoutingService"/> — the ONLY
///         playbook-resolution source per FR-P3-01 — plus <see cref="IPlaybookLookupService"/>
///         for the downstream record load.</item>
///   <item>The legacy typed-options config surface (WorkspaceOptions) is GONE from the
///         constructor and the service body (FR-P3-01 / NFR-08: no shims).</item>
///   <item>Source body calls <c>_consumerRouting.ResolveAsync(ConsumerTypes.ProjectPreFill, …)</c>
///         using the compile-time constant (code-review S-5 hardening).</item>
///   <item>Routing null yields the clean empty response — no fallback.</item>
///   <item>The 45-second timeout invariant (NFR-07 binding) remains in the source.</item>
/// </list>
///
/// <para>
/// Full pipeline coverage (text extraction, SpeFileStore staging, playbook event consumption)
/// is intentionally OUT OF SCOPE — <see cref="Sprk.Bff.Api.Infrastructure.Graph.SpeFileStore"/>
/// is a concrete non-virtual facade that cannot be cleanly mocked without a wider refactor,
/// and the NFR-07-binding pre-fill flow is exercised end-to-end by existing integration tests.
/// The routing contract is pinned via constructor reflection + source-text invariants
/// (established pattern in this file since task 028c).
/// </para>
/// </summary>
public class ProjectPreFillServiceTests
{
    // ─── (a) FR-P3-01 — IConsumerRoutingService is the ONLY resolution source ────────────

    [Fact]
    public void ProjectPreFillService_Constructor_RequiresConsumerRoutingService_FRP301()
    {
        // FR-P3-01 — the sprk_playbookconsumer Binding routing table is the ONLY
        // playbook-resolution source, so IConsumerRoutingService MUST be a constructor
        // dependency (ADR-010 DI minimalism). The constant ConsumerTypes.ProjectPreFill
        // (compile-time typo defense per code-review S-5) is passed at the call site.
        var ctor = typeof(ProjectPreFillService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single();

        var parameters = ctor.GetParameters();
        parameters.Should().Contain(p => p.ParameterType == typeof(IConsumerRoutingService),
            "FR-P3-01 — IConsumerRoutingService MUST be a constructor dependency " +
            "for sprk_playbookconsumer routing-table resolution");
    }

    [Fact]
    public void ProjectPreFillService_Constructor_RequiresPlaybookLookupService()
    {
        // Pattern A baseline: IPlaybookLookupService MUST remain a ctor dependency — the
        // routing service resolves WHICH playbook (consumer→playbookId), the lookup
        // service then loads the playbook record itself.
        var ctor = typeof(ProjectPreFillService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single();

        var parameters = ctor.GetParameters();
        parameters.Should().Contain(p => p.ParameterType == typeof(IPlaybookLookupService),
            "Pattern A — IPlaybookLookupService MUST remain a constructor dependency");
    }

    [Fact]
    public void ProjectPreFillService_Constructor_HasNoWorkspaceOptionsDependency_FRP301()
    {
        // FR-P3-01 hard cutover — the WorkspaceOptions typed-options fallback surface was
        // DELETED (NFR-08: no shims). The constructor MUST NOT carry any WorkspaceOptions
        // dependency; routing is the only source.
        var ctor = typeof(ProjectPreFillService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single();

        ctor.GetParameters().Should().NotContain(
            p => p.ParameterType.FullName!.Contains("WorkspaceOptions"),
            "FR-P3-01 — the WorkspaceOptions config fallback was deleted; " +
            "the routing table is the ONLY playbook-resolution source");
    }

    // ─── (b) NFR-07 binding — 45s timeout invariant pinned in source ─────────────────────

    [Fact]
    public void ProjectPreFillService_PreservesFortyFiveSecondTimeout_NFR07()
    {
        // NFR-07 BINDING: the pre-fill flow's 45-second timeout MUST NOT change. The
        // cutover only touches the internal routing/lookup mechanism. Source-text check.
        var sourcePath = LocateProjectPreFillServiceSource();
        var source = File.ReadAllText(sourcePath);
        source.Should().Contain("TimeSpan.FromSeconds(45)",
            "NFR-07 BINDING — pre-fill flow 45s timeout invariant MUST be preserved");
    }

    // ─── (c) Source-text invariants — routing-only resolution ─────────────────────────────

    [Fact]
    public void ProjectPreFillService_Source_ResolvesViaConsumerRoutingOnly_FRP301()
    {
        // The service body MUST call IConsumerRoutingService.ResolveAsync with the
        // ConsumerTypes.ProjectPreFill compile-time constant (NOT a literal string —
        // code-review S-5 hardening), and MUST NOT read any config fallback (FR-P3-01).
        var source = File.ReadAllText(LocateProjectPreFillServiceSource());
        source.Should().Contain("_consumerRouting",
            "service MUST hold an IConsumerRoutingService field");
        source.Should().Contain("ConsumerTypes.ProjectPreFill",
            "code-review S-5 — service MUST use the ConsumerTypes.ProjectPreFill constant, " +
            "not a literal string");
        source.Should().Contain(".ResolveAsync(",
            "service MUST call IConsumerRoutingService.ResolveAsync");
        source.Should().NotContain("_workspaceOptions",
            "FR-P3-01 — the WorkspaceOptions field was deleted with the config fallback");
        source.Should().NotContain("ProjectPreFillPlaybookId",
            "FR-P3-01 — no reference to the deleted config property may remain, " +
            "comments included (NFR-08 hard cutover)");
    }

    [Fact]
    public void ProjectPreFillService_Source_RoutingNull_YieldsCleanEmptyResponse_FRP301()
    {
        // FR-P3-01 clean-error contract: when the routing table has no enabled
        // sprk_playbookconsumer row, the service LogErrors (seed-the-row remedy) and
        // returns ProjectPreFillResponse.Empty() — no config fallback, no exception.
        // (ProjectPreFillResponse.Empty() carries no message parameter — the LogError is
        // the operator-facing signal.)
        var source = File.ReadAllText(LocateProjectPreFillServiceSource());
        source.Should().Contain("no config fallback exists per FR-P3-01",
            "FR-P3-01 — the routing-miss LogError MUST tell the operator to seed the row");
        source.Should().Contain("ProjectPreFillResponse.Empty()",
            "routing null MUST return the clean empty response");
    }

    [Fact]
    public void ProjectPreFillService_Source_StillCallsPlaybookLookupGetByIdAsync()
    {
        // The routed GUID MUST still flow through IPlaybookLookupService.GetByIdAsync so
        // 1-hour playbook caching (ADR-014) and stable-ID semantics are preserved.
        var source = File.ReadAllText(LocateProjectPreFillServiceSource());
        source.Should().Contain("_playbookLookup",
            "service MUST still hold an IPlaybookLookupService field");
        source.Should().Contain(".GetByIdAsync(",
            "service MUST still call IPlaybookLookupService.GetByIdAsync");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────────────

    private static string LocateProjectPreFillServiceSource()
    {
        var assemblyPath = typeof(ProjectPreFillServiceTests).Assembly.Location;
        var dir = new DirectoryInfo(Path.GetDirectoryName(assemblyPath)!);

        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "server")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("repo root must be locatable from the test assembly path");
        var source = Path.Combine(
            dir!.FullName,
            "src", "server", "api", "Sprk.Bff.Api",
            "Services", "Workspace", "ProjectPreFillService.cs");
        File.Exists(source).Should().BeTrue($"ProjectPreFillService.cs must exist at '{source}'");
        return source;
    }
}

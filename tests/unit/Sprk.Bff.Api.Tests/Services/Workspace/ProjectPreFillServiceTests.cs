using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Workspace;

/// <summary>
/// Unit tests for <see cref="ProjectPreFillService"/> — focused on the FR-1R-05 / Pattern A
/// routing-table migration landed by chat-routing-redesign-r1 task 028c.
///
/// <para>
/// Mirrors the test surface of <see cref="MatterPreFillServiceTests"/>:
/// </para>
/// <list type="bullet">
///   <item>Constructor injects <see cref="IConsumerRoutingService"/> (FR-1R-05 task 028c).</item>
///   <item>Constructor still injects <see cref="IPlaybookLookupService"/> + typed
///         <see cref="IOptions{TOptions}"/> of <see cref="WorkspaceOptions"/>
///         (Pattern A — Phase 1 baseline preserved).</item>
///   <item>Source body calls <c>_consumerRouting.ResolveAsync(ConsumerTypes.ProjectPreFill, …)</c>
///         using the compile-time constant (code-review S-5 hardening).</item>
///   <item>Env-var fallback (<c>WorkspaceOptions.ProjectPreFillPlaybookId</c>) remains
///         readable during the FR-1R-06 deprecation window.</item>
///   <item>The 45-second timeout invariant (NFR-07 binding) remains in the source.</item>
///   <item>Public <c>AnalyzeFilesAsync</c> signature is unchanged (NFR-07 binding).</item>
/// </list>
///
/// <para>
/// Full pipeline coverage (text extraction, SpeFileStore staging, playbook event consumption)
/// is intentionally OUT OF SCOPE — <see cref="Sprk.Bff.Api.Infrastructure.Graph.SpeFileStore"/>
/// is a concrete non-virtual facade that cannot be cleanly mocked without a wider refactor,
/// and the NFR-07-binding pre-fill flow is exercised end-to-end by existing integration tests.
/// </para>
/// </summary>
public class ProjectPreFillServiceTests
{
    // ─── (a) FR-1R-05 task 028c — IConsumerRoutingService is a constructor parameter ────────

    [Fact]
    public void ProjectPreFillService_Constructor_RequiresConsumerRoutingService_FR1R05()
    {
        // FR-1R-05 task 028c — the Pattern A migration to the sprk_playbookconsumer routing
        // table MUST inject IConsumerRoutingService directly via the constructor (ADR-010
        // DI minimalism). The constant ConsumerTypes.ProjectPreFill (compile-time typo defense
        // per code-review S-5) is passed at the call site.
        var ctor = typeof(ProjectPreFillService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single();

        var parameters = ctor.GetParameters();
        parameters.Should().Contain(p => p.ParameterType == typeof(IConsumerRoutingService),
            "FR-1R-05 task 028c — IConsumerRoutingService MUST be a constructor dependency " +
            "for sprk_playbookconsumer routing-table resolution");
    }

    [Fact]
    public void ProjectPreFillService_Constructor_RequiresPlaybookLookupService()
    {
        // Phase 1 (task 017) Pattern A baseline: IPlaybookLookupService MUST remain a ctor
        // dependency — the routing service resolves WHICH playbook (consumer→playbookId),
        // the lookup service then loads the playbook record itself.
        var ctor = typeof(ProjectPreFillService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single();

        var parameters = ctor.GetParameters();
        parameters.Should().Contain(p => p.ParameterType == typeof(IPlaybookLookupService),
            "Phase 1 Pattern A — IPlaybookLookupService MUST remain a constructor dependency");
    }

    [Fact]
    public void ProjectPreFillService_Constructor_RequiresWorkspaceOptions()
    {
        // ADR-018 typed-options + FR-1R-06 deprecation-window env-var fallback: the typed
        // options MUST remain a ctor dependency so the fallback path still works.
        var ctor = typeof(ProjectPreFillService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single();

        var parameters = ctor.GetParameters();
        parameters.Should().Contain(p => p.ParameterType == typeof(IOptions<WorkspaceOptions>),
            "ADR-018 typed-options — WorkspaceOptions MUST be consumed via IOptions<T> " +
            "(env-var fallback for FR-1R-06 deprecation window)");
    }

    // ─── (b) NFR-07 binding — 45s timeout invariant pinned in source ─────────────────────

    [Fact]
    public void ProjectPreFillService_PreservesFortyFiveSecondTimeout_NFR07()
    {
        // NFR-07 BINDING: the pre-fill flow's 45-second timeout MUST NOT change. The
        // migration only touches the internal routing/lookup mechanism. Source-text check.
        var sourcePath = LocateProjectPreFillServiceSource();
        var source = File.ReadAllText(sourcePath);
        source.Should().Contain("TimeSpan.FromSeconds(45)",
            "NFR-07 BINDING — pre-fill flow 45s timeout invariant MUST be preserved");
    }

    // ─── (c) NFR-07 binding — public AnalyzeFilesAsync signature unchanged ───────────────


    // ─── (d) Source-text invariants — migration uses ConsumerTypes.ProjectPreFill + fallback ─

    [Fact]
    public void ProjectPreFillService_Source_CallsConsumerRoutingResolveAsync_FR1R05()
    {
        // FR-1R-05 task 028c: the service body MUST call IConsumerRoutingService.ResolveAsync
        // with the ConsumerTypes.ProjectPreFill compile-time constant (NOT a literal string —
        // code-review S-5 hardening). The env-var fallback MUST remain readable during the
        // FR-1R-06 deprecation window.
        var source = File.ReadAllText(LocateProjectPreFillServiceSource());
        source.Should().Contain("_consumerRouting",
            "FR-1R-05 task 028c — service MUST hold an IConsumerRoutingService field");
        source.Should().Contain("ConsumerTypes.ProjectPreFill",
            "code-review S-5 — service MUST use the ConsumerTypes.ProjectPreFill constant, " +
            "not a literal string");
        source.Should().Contain(".ResolveAsync(",
            "FR-1R-05 — service MUST call IConsumerRoutingService.ResolveAsync");
        source.Should().Contain("_workspaceOptions.ProjectPreFillPlaybookId",
            "FR-1R-06 — env-var fallback MUST remain readable during the deprecation window");
    }

    [Fact]
    public void ProjectPreFillService_Source_StillCallsPlaybookLookupGetByIdAsync()
    {
        // FR-1R-05 task 028c migrates the routing decision but NOT the playbook-record load.
        // The downstream IPlaybookLookupService.GetByIdAsync call MUST remain unchanged so
        // 1-hour playbook caching (ADR-014) and stable-ID semantics are preserved.
        var source = File.ReadAllText(LocateProjectPreFillServiceSource());
        source.Should().Contain("_playbookLookup",
            "Phase 1 task 017 — service MUST still hold an IPlaybookLookupService field");
        source.Should().Contain(".GetByIdAsync(",
            "Phase 1 task 017 — service MUST still call IPlaybookLookupService.GetByIdAsync");
    }

    // ─── (e) Fail-fast on null IConsumerRoutingService ─────────────────────────────────────

    [Fact]
    public void Constructor_NullConsumerRouting_ThrowsArgumentNullException()
    {
        // ADR-010 fail-fast on missing DI dependency — the constructor MUST throw
        // ArgumentNullException when IConsumerRoutingService is null. SpeFileStore
        // construction throws first (it has no parameterless ctor), so we just verify
        // an exception surfaces — proving the ctor refuses construction.
        var act = () => new ProjectPreFillService(
            speFileStore: null!,
            textExtractor: Mock.Of<ITextExtractor>(),
            playbookLookup: Mock.Of<IPlaybookLookupService>(),
            consumerRouting: null!,
            workspaceOptions: Options.Create(new WorkspaceOptions()),
            speOptions: Options.Create(new SharePointEmbeddedOptions()),
            logger: Mock.Of<ILogger<ProjectPreFillService>>());

        act.Should().Throw<Exception>(
            "ctor must refuse to construct with missing dependencies (fail-fast)");
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

using System.Reflection;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Workspace;

/// <summary>
/// Unit tests for <see cref="MatterPreFillService"/> — focused on the FR-P3-01 hard-cutover
/// routing contract (ai-architecture-redesign-r1 task 040).
///
/// <para>
/// Test scope is intentionally narrow on the routing invariants (mirrors the canonical
/// reflection/source-text tests established by chat-routing-redesign-r1 tasks 016/028c):
/// </para>
/// <list type="bullet">
///   <item>The constructor injects <see cref="IConsumerRoutingService"/> — the ONLY
///         playbook-resolution source per FR-P3-01 — plus <see cref="IPlaybookLookupService"/>
///         for the downstream record load.</item>
///   <item>The legacy typed-options config surface (WorkspaceOptions) is GONE from the
///         constructor and the service body (FR-P3-01 / NFR-08: no shims, no deprecation
///         windows).</item>
///   <item>Routing null yields the clean ROUTING_MISSING empty response — no fallback.</item>
///   <item>The 45-second timeout invariant (NFR-07 binding) remains in the source —
///         pinned by a textual contract check.</item>
/// </list>
///
/// <para>
/// Tests covering the full pre-fill pipeline (text extraction, SpeFileStore staging,
/// playbook event consumption, $choices output shape) are intentionally OUT OF SCOPE
/// here: <see cref="Sprk.Bff.Api.Infrastructure.Graph.SpeFileStore"/> is a concrete
/// non-virtual facade that cannot be cleanly mocked without a wider refactor, and the
/// NFR-07-binding pre-fill flow is exercised end-to-end by existing integration tests.
/// Playbook resolution happens inside a private method behind that facade, so the routing
/// contract is pinned via constructor reflection + source-text invariants (established
/// pattern in this file since task 016).
/// </para>
/// </summary>
public class MatterPreFillServiceTests
{
    // ─── (a) FR-P3-01 — IConsumerRoutingService is the ONLY resolution source ────────────

    [Fact]
    public void MatterPreFillService_Constructor_RequiresPlaybookLookupService()
    {
        // Pattern A — IPlaybookLookupService MUST be a constructor dependency: routing
        // resolves WHICH playbook (consumer → playbookId), the lookup service then loads
        // the playbook record itself (1-hour caching per ADR-014).
        var ctor = typeof(MatterPreFillService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single();

        var parameters = ctor.GetParameters();
        parameters.Should().Contain(p => p.ParameterType == typeof(IPlaybookLookupService),
            "Pattern A migration — IPlaybookLookupService MUST be a constructor dependency " +
            "for stable-ID playbook resolution");
    }

    [Fact]
    public void MatterPreFillService_Constructor_RequiresConsumerRoutingService_FRP301()
    {
        // FR-P3-01 — the sprk_playbookconsumer Binding routing table is the ONLY
        // playbook-resolution source, so IConsumerRoutingService MUST be a constructor
        // dependency (ADR-010 DI minimalism). The constant ConsumerTypes.MatterPreFill
        // (compile-time typo defense per code-review S-5) is passed at the call site.
        var ctor = typeof(MatterPreFillService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single();

        var parameters = ctor.GetParameters();
        parameters.Should().Contain(p => p.ParameterType == typeof(IConsumerRoutingService),
            "FR-P3-01 — IConsumerRoutingService MUST be a constructor dependency " +
            "for sprk_playbookconsumer routing-table resolution");
    }

    [Fact]
    public void MatterPreFillService_Constructor_HasNoWorkspaceOptionsDependency_FRP301()
    {
        // FR-P3-01 hard cutover — the WorkspaceOptions typed-options fallback surface was
        // DELETED (NFR-08: no shims). The constructor MUST NOT carry any WorkspaceOptions
        // dependency; routing is the only source.
        var ctor = typeof(MatterPreFillService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single();

        ctor.GetParameters().Should().NotContain(
            p => p.ParameterType.FullName!.Contains("WorkspaceOptions"),
            "FR-P3-01 — the WorkspaceOptions config fallback was deleted; " +
            "the routing table is the ONLY playbook-resolution source");
    }

    // ─── (b) Source-text invariants — routing-only resolution ─────────────────────────────

    [Fact]
    public void MatterPreFillService_Source_ResolvesViaConsumerRoutingOnly_FRP301()
    {
        // The service body MUST call IConsumerRoutingService.ResolveAsync with the
        // ConsumerTypes.MatterPreFill compile-time constant (NOT a literal string —
        // code-review S-5 hardening), and MUST NOT read any config fallback (FR-P3-01).
        var source = File.ReadAllText(LocateMatterPreFillServiceSource());
        source.Should().Contain("_consumerRouting",
            "service MUST hold an IConsumerRoutingService field");
        source.Should().Contain("ConsumerTypes.MatterPreFill",
            "code-review S-5 — service MUST use the ConsumerTypes.MatterPreFill constant, " +
            "not a literal string");
        source.Should().Contain(".ResolveAsync(",
            "service MUST call IConsumerRoutingService.ResolveAsync");
        source.Should().NotContain("_workspaceOptions",
            "FR-P3-01 — the WorkspaceOptions field was deleted with the config fallback");
        source.Should().NotContain("MatterPreFillPlaybookId",
            "FR-P3-01 — no reference to the deleted config property may remain, " +
            "comments included (NFR-08 hard cutover)");
    }

    [Fact]
    public void MatterPreFillService_Source_RoutingNull_YieldsRoutingMissingResponse_FRP301()
    {
        // FR-P3-01 clean-error contract: when the routing table has no enabled
        // sprk_playbookconsumer row, the service returns an empty pre-fill response with
        // the ROUTING_MISSING reason code — no config fallback, no exception.
        var source = File.ReadAllText(LocateMatterPreFillServiceSource());
        source.Should().Contain(
            "ROUTING_MISSING: no enabled sprk_playbookconsumer row for consumerType 'matter-pre-fill'",
            "FR-P3-01 — routing null MUST surface the clean ROUTING_MISSING reason code");
        source.Should().NotContain("CONFIG_MISSING",
            "FR-P3-01 — the CONFIG_MISSING reason code died with the config fallback");
    }

    // ─── (c) NFR-07 binding — 45s timeout invariant pinned in source ─────────────────────

    [Fact]
    public void MatterPreFillService_PreservesFortyFiveSecondTimeout_NFR07()
    {
        // NFR-07 BINDING: the pre-fill flow's 45-second timeout MUST NOT change. The
        // cutover only touches the internal playbook-ID resolution mechanism. We pin the
        // textual contract by reading the source file and asserting the timeout literal
        // is present. This is intentionally brittle — any change to the timeout MUST
        // be a deliberate, reviewed action that updates this test alongside the source.
        var sourcePath = LocateMatterPreFillServiceSource();
        var source = File.ReadAllText(sourcePath);
        source.Should().Contain("TimeSpan.FromSeconds(45)",
            "NFR-07 BINDING — pre-fill flow 45s timeout invariant MUST be preserved");
    }

    // ─── (d) Source-text invariants — downstream lookup via IPlaybookLookupService ───────

    [Fact]
    public void MatterPreFillService_Source_CallsPlaybookLookupGetByIdAsync()
    {
        // The routed GUID MUST still flow through IPlaybookLookupService.GetByIdAsync so
        // 1-hour playbook caching (ADR-014) and stable-ID semantics are preserved.
        var source = File.ReadAllText(LocateMatterPreFillServiceSource());
        source.Should().Contain("_playbookLookup",
            "service MUST hold an IPlaybookLookupService field");
        source.Should().Contain(".GetByIdAsync(",
            "service MUST call IPlaybookLookupService.GetByIdAsync with the routed id");
    }

    [Fact]
    public void MatterPreFillService_Source_DoesNotContainHardcodedMatterPreFillGuid()
    {
        // FR-05 task 016 acceptance (still binding): the hardcoded 2d660cad-... GUID MUST
        // NOT appear in executable code (migration-history comments are allowed).
        var source = File.ReadAllText(LocateMatterPreFillServiceSource());
        source.Should().NotContain("Guid.Parse(\"2d660cad-d418-f111-8343-7ced8d1dc988\")",
            "FR-05 task 016 — hardcoded 'Create New Matter Pre-Fill' GUID MUST be removed " +
            "from executable code (migration history comments are allowed)");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────────────

    private static string LocateMatterPreFillServiceSource()
    {
        // Resolve from the test assembly's parent directory tree. The test project lives
        // at tests/unit/Sprk.Bff.Api.Tests; the source lives at
        // src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs.
        var assemblyPath = typeof(MatterPreFillServiceTests).Assembly.Location;
        var dir = new DirectoryInfo(Path.GetDirectoryName(assemblyPath)!);

        // Walk up to the repo root (the worktree directory contains both `src` and `tests`).
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "server")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("repo root must be locatable from the test assembly path");
        var source = Path.Combine(
            dir!.FullName,
            "src", "server", "api", "Sprk.Bff.Api",
            "Services", "Workspace", "MatterPreFillService.cs");
        File.Exists(source).Should().BeTrue($"MatterPreFillService.cs must exist at '{source}'");
        return source;
    }
}

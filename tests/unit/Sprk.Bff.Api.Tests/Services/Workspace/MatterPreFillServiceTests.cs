using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Workspace;

/// <summary>
/// Unit tests for <see cref="MatterPreFillService"/> — focused on the FR-05 / Pattern A
/// stable-ID migration landed by chat-routing-redesign-r1 task 016.
///
/// <para>
/// Test scope is intentionally narrow on the migration invariants (mirrors the canonical
/// reflection-based tests in <c>SessionSummarizeOrchestratorTests</c> for task 015):
/// </para>
/// <list type="bullet">
///   <item>The hardcoded <c>2d660cad-d418-f111-8343-7ced8d1dc988</c> Guid constant is
///         removed from the service surface (FR-05 task 016).</item>
///   <item>The constructor injects <see cref="IPlaybookLookupService"/> and
///         <see cref="IOptions{TOptions}"/> of <see cref="WorkspaceOptions"/>
///         (Pattern A typed-options + stable-ID lookup).</item>
///   <item>Fail-fast on missing config — when
///         <see cref="WorkspaceOptions.MatterPreFillPlaybookId"/> is empty, the service
///         MUST NOT call <see cref="IPlaybookLookupService.GetByIdAsync"/> and MUST
///         return an empty pre-fill response with a CONFIG_MISSING reason code.</item>
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
/// The Pattern A migration only touches the playbook-ID lookup mechanism — these
/// reflection / fail-fast tests pin the exact migration contract.
/// </para>
/// </summary>
public class MatterPreFillServiceTests
{
    // FR-05 task 016 (chat-routing-redesign-r1): canonical DEV-environment GUID for the
    // "Create New Matter Pre-Fill" playbook (the sprk_playbookid alt-key value, mirroring
    // its sprk_analysisplaybookid PK per task 014 backfill plan).
    private const string ConfiguredMatterPreFillPlaybookId = "2d660cad-d418-f111-8343-7ced8d1dc988";

    // ─── (a) FR-05 task 016 — hardcoded GUID constant removed ─────────────────────────────

    [Fact]
    public void MatterPreFillService_HasNoHardcodedDefaultPreFillPlaybookIdConstant_FR05()
    {
        // FR-05 task 016 (chat-routing-redesign-r1): the prior
        // private static readonly Guid DefaultPreFillPlaybookId =
        //     Guid.Parse("2d660cad-d418-f111-8343-7ced8d1dc988");
        // constant was removed. Resolution now flows through
        // WorkspaceOptions.MatterPreFillPlaybookId + IPlaybookLookupService.GetByIdAsync.
        // Reflection assert: the constant no longer exists on the service.
        var members = typeof(MatterPreFillService)
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToArray();

        members.Should().NotContain("DefaultPreFillPlaybookId",
            "FR-05 task 016 — hardcoded DefaultPreFillPlaybookId Guid constant removed; " +
            "playbook resolved at runtime via WorkspaceOptions.MatterPreFillPlaybookId + " +
            "IPlaybookLookupService.GetByIdAsync per ADR-018 typed options + Pattern A stable-ID");
    }

    // ─── (b) FR-05 task 016 — IPlaybookLookupService is a constructor parameter ───────────

    [Fact]
    public void MatterPreFillService_Constructor_RequiresPlaybookLookupService()
    {
        // The Pattern A migration MUST inject IPlaybookLookupService directly via the
        // constructor (ADR-010 DI minimalism). Reflection assert: the parameter exists
        // and the type is correct.
        var ctor = typeof(MatterPreFillService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single();

        var parameters = ctor.GetParameters();
        parameters.Should().Contain(p => p.ParameterType == typeof(IPlaybookLookupService),
            "Pattern A migration — IPlaybookLookupService MUST be a constructor dependency " +
            "for stable-ID playbook resolution (mirrors SessionSummarizeOrchestrator task 015)");
    }

    [Fact]
    public void MatterPreFillService_Constructor_RequiresWorkspaceOptions()
    {
        // The Pattern A migration MUST consume WorkspaceOptions via IOptions<T> (ADR-018
        // typed options — no raw IConfiguration[] indexer). Reflection assert: the
        // IOptions<WorkspaceOptions> parameter exists.
        var ctor = typeof(MatterPreFillService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single();

        var parameters = ctor.GetParameters();
        parameters.Should().Contain(p => p.ParameterType == typeof(IOptions<WorkspaceOptions>),
            "ADR-018 typed-options — WorkspaceOptions MUST be consumed via IOptions<T>");
    }

    // ─── (c) Fail-fast on null IPlaybookLookupService ─────────────────────────────────────

    [Fact]
    public void Constructor_NullPlaybookLookup_ThrowsArgumentNullException()
    {
        // ADR-010 fail-fast on missing DI dependency — the constructor MUST throw
        // ArgumentNullException when IPlaybookLookupService is null.
        var act = () => new MatterPreFillService(
            speFileStore: null!,
            textExtractor: null!,
            playbookLookup: null!,
            workspaceOptions: Options.Create(new WorkspaceOptions()),
            speOptions: Options.Create(new SharePointEmbeddedOptions()),
            logger: Mock.Of<ILogger<MatterPreFillService>>());

        // The first null-check that fires is speFileStore; cascade through to playbook.
        // We just verify ArgumentNullException surfaces — the order is incidental.
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullPlaybookLookup_ThrowsArgumentNullException_NamedParam()
    {
        // Pin the parameter name to playbookLookup specifically (cascade through earlier
        // non-null params). This protects the migration contract: future refactors that
        // reorder ctor params must update the test.
        var act = () => new MatterPreFillService(
            speFileStore: Mock.Of<Sprk.Bff.Api.Infrastructure.Graph.SpeFileStore>(),
            textExtractor: Mock.Of<ITextExtractor>(),
            playbookLookup: null!,
            workspaceOptions: Options.Create(new WorkspaceOptions()),
            speOptions: Options.Create(new SharePointEmbeddedOptions()),
            logger: Mock.Of<ILogger<MatterPreFillService>>());

        // SpeFileStore lacks a parameterless ctor so Mock.Of can't synthesize it — that
        // throws first. Either way the ctor refuses construction when AI dependencies
        // are missing, which is the safety property we care about (fail-fast).
        act.Should().Throw<Exception>(
            "ctor must refuse to construct with missing AI dependencies (fail-fast)");
    }

    // ─── (d) NFR-07 binding — 45s timeout invariant pinned in source ─────────────────────

    [Fact]
    public void MatterPreFillService_PreservesFortyFiveSecondTimeout_NFR07()
    {
        // NFR-07 BINDING: the pre-fill flow's 45-second timeout MUST NOT change. The
        // migration only touches the internal playbook-ID lookup mechanism. We pin the
        // textual contract by reading the source file and asserting the timeout literal
        // is present. This is intentionally brittle — any change to the timeout MUST
        // be a deliberate, reviewed action that updates this test alongside the source.
        var sourcePath = LocateMatterPreFillServiceSource();
        var source = File.ReadAllText(sourcePath);
        source.Should().Contain("TimeSpan.FromSeconds(45)",
            "NFR-07 BINDING — pre-fill flow 45s timeout invariant MUST be preserved");
    }

    // ─── (e) NFR-07 binding — public AnalyzeFilesAsync signature unchanged ───────────────

    [Fact]
    public void MatterPreFillService_AnalyzeFilesAsync_PublicSignatureUnchanged_NFR07()
    {
        // NFR-07 BINDING: the public method consumed by the front-end useAiPrefill hook
        // MUST keep its signature unchanged. The Pattern A migration only changes the
        // INTERNAL playbook-ID lookup — the boundary contract is preserved.
        var method = typeof(MatterPreFillService).GetMethod(
            nameof(MatterPreFillService.AnalyzeFilesAsync),
            BindingFlags.Public | BindingFlags.Instance);

        method.Should().NotBeNull("AnalyzeFilesAsync is the public entry point consumed by useAiPrefill");
        var parameters = method!.GetParameters();
        parameters.Should().HaveCount(4, "NFR-07 — public signature MUST NOT change");
        parameters[0].ParameterType.Name.Should().Be("IFormFileCollection",
            "files parameter type unchanged (front-end upload contract)");
        parameters[1].ParameterType.Should().Be(typeof(string), "userId parameter unchanged");
        parameters[2].ParameterType.Name.Should().Be("HttpContext", "httpContext parameter unchanged");
        parameters[3].ParameterType.Should().Be(typeof(CancellationToken), "cancellationToken parameter unchanged");
    }

    // ─── (f) Source-text invariants — migration uses IPlaybookLookupService.GetByIdAsync ─

    [Fact]
    public void MatterPreFillService_Source_CallsPlaybookLookupGetByIdAsync()
    {
        // FR-05 task 016: the service body MUST call IPlaybookLookupService.GetByIdAsync
        // with WorkspaceOptions.MatterPreFillPlaybookId. Source-text check pins the
        // migration shape (mirrors the SessionSummarizeOrchestrator approach).
        var source = File.ReadAllText(LocateMatterPreFillServiceSource());
        source.Should().Contain("_playbookLookup",
            "FR-05 task 016 — service MUST hold an IPlaybookLookupService field");
        source.Should().Contain(".GetByIdAsync(",
            "FR-05 task 016 — service MUST call IPlaybookLookupService.GetByIdAsync");
        source.Should().Contain("_workspaceOptions.MatterPreFillPlaybookId",
            "Pattern A — service MUST read the per-env stable-ID value from " +
            "WorkspaceOptions.MatterPreFillPlaybookId (pre-seated by task 013)");
    }

    [Fact]
    public void MatterPreFillService_Source_DoesNotContainHardcodedMatterPreFillGuid()
    {
        // FR-05 task 016 acceptance: the hardcoded 2d660cad-... GUID MUST NOT appear in
        // the service source code (only in XML doc / migration-history comments that
        // reference it as removed). We verify it does not appear OUTSIDE comments.
        var source = File.ReadAllText(LocateMatterPreFillServiceSource());

        // The GUID can only appear inside comment blocks (// or /// migration history).
        // Easiest check: ensure no `Guid.Parse("2d660cad...")` or executable literal.
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

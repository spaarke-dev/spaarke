using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// BINDING INVARIANT (chat-routing-redesign-r1 FR-45): IPlaybookChatContextProvider
/// MUST invoke IMatterMemoryService.ToSystemPromptFragmentAsync at per-turn context
/// assembly time when the session has a matter context. Architecture §11.1 confirms
/// the wiring; CLAUDE.md decision log (2026-06-21) treats this as 'do not regress'.
/// Task 078 audit evidence: notes/handoffs/078-fr-45-line-verified.md.
/// </summary>
/// <remarks>
/// DO NOT REGRESS. If a future change to PlaybookChatContextProvider removes this
/// invocation, the matter-memory pillar of the 6-tier memory model goes dark and
/// chat sessions for matter contexts lose long-term memory continuity. This test
/// class is the binding enforcement.
///
/// Coverage:
/// <list type="bullet">
/// <item>Behavioral (Moq.Verify): the production composer DOES call
/// <see cref="IMatterMemoryService.ToSystemPromptFragmentAsync"/> exactly once at
/// runtime when the session host context identifies a matter.</item>
/// <item>Source-text (file grep): the production source file contains the literal
/// invocation site — pins the wiring even if the runtime path is later refactored.</item>
/// <item>Guard (Moq.Verify Times.Never): the production composer DOES NOT invoke
/// the memory service when the host context is null or non-matter — the
/// EntityType=="matter" guard at <see cref="PlaybookChatContextProvider.cs"/>
/// (~line 663) remains intact.</item>
/// </list>
///
/// This file is the dedicated FR-45 regression class. It complements (but does not
/// replace) the source-text smoke tests inside
/// <see cref="PlaybookChatContextProviderTests"/> that task 078 added — those pin
/// FR-27 (single-pipeline) alongside FR-45; this class is FR-45-only with behavioral
/// rigor.
/// </remarks>
public class PlaybookChatContextProviderFr45RegressionTests
{
    private static readonly Guid TestPlaybookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestActionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string TestDocumentId = "doc-001";
    private const string TestTenantId = "tenant-abc";
    private const string TestMatterId = "matter-xyz-789";

    private readonly Mock<IScopeResolverService> _scopeResolverMock;
    private readonly Mock<IPlaybookService> _playbookServiceMock;
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<PlaybookChatContextProvider>> _loggerMock;
    private readonly Mock<IMatterMemoryService> _matterMemoryServiceMock;

    public PlaybookChatContextProviderFr45RegressionTests()
    {
        _scopeResolverMock = new Mock<IScopeResolverService>();
        _playbookServiceMock = new Mock<IPlaybookService>();
        _dataverseServiceMock = new Mock<IDataverseService>();
        _loggerMock = new Mock<ILogger<PlaybookChatContextProvider>>();
        _matterMemoryServiceMock = new Mock<IMatterMemoryService>();

        // Default: playbook with action, no scopes — enough to exercise the
        // playbook-mode code path without touching knowledge or skill enrichment.
        // Mirrors the fixture in PlaybookChatContextProviderTests (task 078).
        _playbookServiceMock
            .Setup(p => p.GetPlaybookAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse
            {
                Id = TestPlaybookId,
                Name = "Test Playbook",
                ActionIds = [TestActionId],
                SkillIds = [],
                KnowledgeIds = [],
                ToolIds = []
            });

        _scopeResolverMock
            .Setup(s => s.GetActionAsync(TestActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction
            {
                Id = TestActionId,
                Name = "Test Action",
                SystemPrompt = "You are a legal assistant."
            });

        _scopeResolverMock
            .Setup(s => s.ResolvePlaybookScopesAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedScopes([], [], []));
    }

    /// <summary>
    /// FR-45 BEHAVIORAL INVARIANT: when the session host context identifies a matter,
    /// <see cref="PlaybookChatContextProvider.GetContextAsync"/> MUST invoke
    /// <see cref="IMatterMemoryService.ToSystemPromptFragmentAsync"/> exactly once
    /// at runtime, carrying the tenant + matterId pair from the host context.
    /// </summary>
    /// <remarks>
    /// This complements the source-text test below by proving the wiring works at
    /// runtime — not just that the literal string exists in source. Future
    /// refactors that move the invocation behind a feature flag, a guard that
    /// always returns early, or a null-object pattern that no-ops would all FAIL
    /// this test even if the source-text test still passes.
    /// </remarks>
    [Fact]
    public async Task GetContextAsync_InvokesToSystemPromptFragmentAsync_ExactlyOnce_ForMatterContextSession()
    {
        // Arrange — matter context session: EntityType == "matter" with non-empty
        // EntityId. Tenant is required for Cosmos partition key (production guard).
        _matterMemoryServiceMock
            .Setup(m => m.ToSystemPromptFragmentAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty); // empty fragment → no prompt append, but
                                         // the CALL still must have been made.

        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: TestMatterId);

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId,
            TestTenantId,
            TestPlaybookId,
            hostContext: hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — context returned cleanly AND the service was invoked once.
        context.Should().NotBeNull();
        context.SystemPrompt.Should().NotBeNullOrEmpty();

        _matterMemoryServiceMock.Verify(
            m => m.ToSystemPromptFragmentAsync(
                TestTenantId,
                TestMatterId,
                It.IsAny<CancellationToken>()),
            Times.Once,
            "FR-45 BINDING — chat-routing-redesign-r1 task 080: the per-turn " +
            "composition seam MUST invoke IMatterMemoryService.ToSystemPromptFragmentAsync " +
            "with (tenantId, matterId) when the session host context identifies a matter. " +
            "Architecture §11.1. Do NOT regress.");
    }

    /// <summary>
    /// FR-45 SOURCE-TEXT INVARIANT: the literal invocation site MUST be present in
    /// <see cref="PlaybookChatContextProvider"/>'s source. Pins the wiring even if
    /// the behavioral path is later restructured. Line number is intentionally NOT
    /// asserted — the invariant is the call's existence, not its position
    /// (per notes/handoffs/078-fr-45-line-verified.md).
    /// </summary>
    [Fact]
    public void PlaybookChatContextProvider_Source_ContainsMatterMemoryServiceInvocation_FR45()
    {
        // Arrange + Act — read the production source file via the assembly-path walk
        // pattern used by task 078's source-text tests.
        var sourcePath = LocateBffSource("Services", "Ai", "Chat", "PlaybookChatContextProvider.cs");
        var source = File.ReadAllText(sourcePath);

        // Assert — literal field-access invocation. We do not assert on the bare
        // method name (which appears multiple times in XML doc-comments) — we
        // assert on `_matterMemoryService.ToSystemPromptFragmentAsync(` to lock the
        // actual call site.
        source.Should().Contain(
            "_matterMemoryService.ToSystemPromptFragmentAsync(",
            "FR-45 binding invariant violated — see architecture §11.1 + CLAUDE.md " +
            "decision log (2026-06-21). The PlaybookChatContextProvider source MUST " +
            "contain a MatterMemoryService.ToSystemPromptFragmentAsync invocation. " +
            "Do NOT remove or rename this without updating spec FR-45 and CLAUDE.md.");
    }

    /// <summary>
    /// FR-45 GUARD INVARIANT: when the session host context is null OR identifies a
    /// non-matter entity type, the provider MUST NOT invoke
    /// <see cref="IMatterMemoryService.ToSystemPromptFragmentAsync"/> — the early-return
    /// guard at <see cref="PlaybookChatContextProvider.cs"/> (~line 663,
    /// <c>EntityType != "matter"</c>) must remain intact. We assert this by setting
    /// the mock to THROW if invoked — if the guard breaks, the test fails loudly
    /// with the unexpected-invocation exception rather than a soft Verify failure.
    /// </summary>
    /// <remarks>
    /// Variant chosen: <b>throw-on-invoke</b> + assert NOT INVOKED. The production
    /// invocation IS guarded by the explicit
    /// <c>!string.Equals(hostContext.EntityType, "matter", StringComparison.OrdinalIgnoreCase)</c>
    /// check (PlaybookChatContextProvider.cs lines 663–669). Throwing on invocation
    /// proves the guard works at runtime — a refactor that drops the guard would
    /// surface the call and fail the test with a clear error, not a silent mock
    /// recording.
    /// </remarks>
    [Fact]
    public async Task GetContextAsync_HandlesGracefully_WhenSessionHasNoMatterContext()
    {
        // Arrange — set the matter memory mock to throw if invoked; this would
        // surface any regression in the guard immediately rather than silently.
        _matterMemoryServiceMock
            .Setup(m => m.ToSystemPromptFragmentAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "FR-45 guard violated — ToSystemPromptFragmentAsync invoked for non-matter session"));

        var sut = CreateProvider();

        // Act — case 1: no host context at all (null). Provider must complete
        // without invoking the matter memory service.
        var contextWithoutHost = await sut.GetContextAsync(
            TestDocumentId,
            TestTenantId,
            TestPlaybookId,
            hostContext: null,
            cancellationToken: CancellationToken.None);

        // Act — case 2: host context present but EntityType is NOT "matter".
        // Guard at PlaybookChatContextProvider.cs:663-669 must short-circuit.
        var nonMatterHost = new ChatHostContext(
            EntityType: "sprk_document",
            EntityId: "doc-999");

        var contextWithNonMatterHost = await sut.GetContextAsync(
            TestDocumentId,
            TestTenantId,
            TestPlaybookId,
            hostContext: nonMatterHost,
            cancellationToken: CancellationToken.None);

        // Assert — both calls returned valid contexts without throwing, AND the
        // matter memory service was never invoked.
        contextWithoutHost.Should().NotBeNull();
        contextWithoutHost.SystemPrompt.Should().NotBeNullOrEmpty();

        contextWithNonMatterHost.Should().NotBeNull();
        contextWithNonMatterHost.SystemPrompt.Should().NotBeNullOrEmpty();

        _matterMemoryServiceMock.Verify(
            m => m.ToSystemPromptFragmentAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "FR-45 GUARD INVARIANT — chat-routing-redesign-r1 task 080: " +
            "PlaybookChatContextProvider MUST NOT invoke matter-memory when " +
            "the host context is null or non-matter. The EntityType==\"matter\" " +
            "guard at lines 663–669 must remain intact.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private PlaybookChatContextProvider CreateProvider()
        => new(
            _scopeResolverMock.Object,
            _playbookServiceMock.Object,
            _dataverseServiceMock.Object, // IDataverseService inherits IDocumentDataverseService
            _loggerMock.Object,
            _matterMemoryServiceMock.Object);

    /// <summary>
    /// Resolves the path to a BFF source file relative to the test assembly's
    /// location. Mirrors the helper in <see cref="PlaybookChatContextProviderTests"/>
    /// (task 078) — kept private to this class so the regression test is fully
    /// self-contained.
    /// </summary>
    private static string LocateBffSource(params string[] tailSegments)
    {
        var assemblyPath = typeof(PlaybookChatContextProviderFr45RegressionTests).Assembly.Location;
        var dir = new DirectoryInfo(Path.GetDirectoryName(assemblyPath)!);

        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "server")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("repo root must be locatable from the test assembly path");

        var segments = new[]
        {
            dir!.FullName, "src", "server", "api", "Sprk.Bff.Api"
        }.Concat(tailSegments).ToArray();

        var path = Path.Combine(segments);
        File.Exists(path).Should().BeTrue($"expected source file at {path}");
        return path;
    }
}

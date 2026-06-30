using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Regression tests for R7 Wave 12 task 151 (audit 120 Gap B): server-side EntityName
/// lazy-fetch in <see cref="PlaybookChatContextProvider.AppendEntityEnrichmentAsync"/>.
///
/// Audit 120 Gap B background: the SpaarkeAi client does NOT populate
/// <see cref="ChatHostContext.EntityName"/> when launching chat from a matter form. Before
/// T151, this caused <see cref="PlaybookChatContextProvider"/>'s entity-enrichment guard at
/// the EntityName check to short-circuit even after T150 normalised <see cref="ChatHostContext.EntityType"/>.
/// Operator-visible effect: "what matter am I in?" returned a generic refusal.
///
/// T151 fix: when EntityName is missing but EntityType + EntityId are present, the provider
/// retrieves the entity's primary-name attribute (<c>sprk_name</c>) from Dataverse via
/// <see cref="IGenericEntityService.RetrieveAsync"/>. Result is memoised per-request. Soft-fails
/// gracefully on any exception path (no chat-request failure).
///
/// These tests live alongside the existing <c>PlaybookChatContextProviderEnrichmentTests</c>
/// (which assert the original guard behaviour) — they complement, not replace, those tests.
/// </summary>
public class PlaybookChatContextProviderEntityNameLazyFetchTests
{
    private static readonly Guid TestPlaybookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestActionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string TestDocumentId = "doc-001";
    private const string TestTenantId = "tenant-abc";
    private const string BaseSystemPrompt = "You are a legal assistant.";
    private const string TestMatterIdString = "491b1efe-e562-f111-ab0c-000d3a4d8152";
    private static readonly Guid TestMatterId = Guid.Parse(TestMatterIdString);

    private readonly Mock<IScopeResolverService> _scopeResolverMock;
    private readonly Mock<IPlaybookService> _playbookServiceMock;
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<PlaybookChatContextProvider>> _loggerMock;

    public PlaybookChatContextProviderEntityNameLazyFetchTests()
    {
        _scopeResolverMock = new Mock<IScopeResolverService>();
        _playbookServiceMock = new Mock<IPlaybookService>();
        _dataverseServiceMock = new Mock<IDataverseService>();
        _loggerMock = new Mock<ILogger<PlaybookChatContextProvider>>();

        // Default: playbook with action, no scopes
        _playbookServiceMock
            .Setup(p => p.GetPlaybookAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sprk.Bff.Api.Models.Ai.PlaybookResponse
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
            .ReturnsAsync(new Sprk.Bff.Api.Services.Ai.AnalysisAction
            {
                Id = TestActionId,
                Name = "Test Action",
                SystemPrompt = BaseSystemPrompt
            });

        _scopeResolverMock
            .Setup(s => s.ResolvePlaybookScopesAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sprk.Bff.Api.Services.Ai.ResolvedScopes([], [], []));
    }

    /// <summary>
    /// Audit 120 Scenario A (with T150 already applied): client sends normalised EntityType
    /// "matter" + EntityId but no EntityName + valid PageType. T151 contract:
    /// lazy-fetch the entity's sprk_name from Dataverse and enrich the system prompt.
    /// </summary>
    [Fact]
    public async Task GetContextAsync_MissingEntityName_LazyFetchesNameFromDataverseAndEnriches()
    {
        // Arrange — Dataverse returns sprk_name="Smith v. Jones" for sprk_matter lookup
        var matterEntity = new Entity("sprk_matter", TestMatterId);
        matterEntity["sprk_name"] = "Smith v. Jones";

        _dataverseServiceMock
            .Setup(d => d.RetrieveAsync(
                "sprk_matter",
                TestMatterId,
                It.Is<string[]>(cols => cols.Length == 1 && cols[0] == "sprk_name"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(matterEntity);

        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: TestMatterIdString,
            EntityName: null,
            PageType: "entityrecord");

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — enrichment fires using the fetched name
        context.SystemPrompt.Should()
            .Contain("Context: You are assisting with matter record 'Smith v. Jones'.");
        context.SystemPrompt.Should()
            .Contain("The user is viewing the main form view.");
    }

    /// <summary>
    /// Soft-fail invariant: when the Dataverse retrieve throws (outage / 404 / network),
    /// enrichment is skipped (pre-T151 guard behaviour preserved) and the chat request
    /// still succeeds with a non-enriched prompt. Chat request MUST NOT fail.
    /// </summary>
    [Fact]
    public async Task GetContextAsync_MissingEntityName_LazyFetchFails_SkipsEnrichmentAndContinues()
    {
        // Arrange — Dataverse throws on retrieve
        _dataverseServiceMock
            .Setup(d => d.RetrieveAsync(
                "sprk_matter",
                TestMatterId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated Dataverse outage"));

        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: TestMatterIdString,
            EntityName: null,
            PageType: "entityrecord");

        var sut = CreateProvider();

        // Act — MUST NOT throw
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — chat request succeeded; enrichment skipped (no "Context: You are assisting" marker);
        // base prompt still intact
        context.SystemPrompt.Should().NotContain("Context: You are assisting with");
        context.SystemPrompt.Should().Contain(BaseSystemPrompt);
    }

    /// <summary>
    /// Per-request cache invariant: when both code paths in <see cref="PlaybookChatContextProvider.GetContextAsync"/>
    /// (generic mode and playbook mode) hit the same Scoped provider instance for the same
    /// entity, only ONE Dataverse round-trip occurs.
    ///
    /// Test pattern: call <see cref="PlaybookChatContextProvider.GetContextAsync"/> twice
    /// on the same Scoped instance (simulating a request that asks for both generic and
    /// playbook context for the same matter), assert the underlying RetrieveAsync was
    /// invoked exactly once.
    /// </summary>
    [Fact]
    public async Task GetContextAsync_TwoCallsSameInstance_LazyFetchCachedAcrossCalls()
    {
        // Arrange
        var matterEntity = new Entity("sprk_matter", TestMatterId);
        matterEntity["sprk_name"] = "Smith v. Jones";

        _dataverseServiceMock
            .Setup(d => d.RetrieveAsync(
                "sprk_matter",
                TestMatterId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(matterEntity);

        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: TestMatterIdString,
            EntityName: null,
            PageType: "entityrecord");

        var sut = CreateProvider();

        // Act — two calls on the SAME provider instance (mimics one request that needs context
        // assembled twice; or two messages in same request scope hitting the cache).
        await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — Dataverse retrieve was called exactly ONCE despite two GetContextAsync calls
        _dataverseServiceMock.Verify(
            d => d.RetrieveAsync(
                "sprk_matter",
                TestMatterId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "per-request cache must prevent duplicate Dataverse round-trips for the same entity within the same Scoped provider lifetime");
    }

    /// <summary>
    /// EntityName supplied by the client — lazy-fetch MUST NOT run (zero Dataverse round-trips).
    /// Protects against regressions where the fetch path runs unconditionally and wastes calls.
    /// </summary>
    [Fact]
    public async Task GetContextAsync_EntityNameAlreadyPresent_DoesNotInvokeLazyFetch()
    {
        // Arrange — client supplies EntityName + EntityType + EntityId
        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: TestMatterIdString,
            EntityName: "Client-Supplied Name",
            PageType: "entityrecord");

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — enrichment used the client-supplied name; Dataverse was NOT consulted
        context.SystemPrompt.Should()
            .Contain("Context: You are assisting with matter record 'Client-Supplied Name'.");
        _dataverseServiceMock.Verify(
            d => d.RetrieveAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "lazy-fetch MUST NOT run when EntityName is already populated by the client");
    }

    /// <summary>
    /// Boundary case: EntityId is not a parseable GUID. Lazy-fetch SHOULD return null
    /// (no Dataverse call); enrichment is skipped; chat request succeeds.
    /// </summary>
    [Fact]
    public async Task GetContextAsync_MissingEntityName_InvalidGuidId_SkipsLazyFetchAndEnrichment()
    {
        // Arrange — non-GUID EntityId
        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: "not-a-guid",
            EntityName: null,
            PageType: "entityrecord");

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — no Dataverse call attempted; enrichment skipped
        _dataverseServiceMock.Verify(
            d => d.RetrieveAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        context.SystemPrompt.Should().NotContain("Context: You are assisting with");
        context.SystemPrompt.Should().Contain(BaseSystemPrompt);
    }

    /// <summary>
    /// Raw Dataverse logical name (<c>sprk_matter</c>) also resolves correctly — T151's
    /// <c>ToDataverseLogicalName</c> mapper is bidirectional for canonical AND raw inputs.
    /// This protects against accidental coupling to T150 boundary normalisation order.
    /// </summary>
    [Fact]
    public async Task GetContextAsync_MissingEntityName_RawLogicalNameType_LazyFetchUsesSameLogicalName()
    {
        // Arrange — raw logical name; same matter
        var matterEntity = new Entity("sprk_matter", TestMatterId);
        matterEntity["sprk_name"] = "Smith v. Jones";

        _dataverseServiceMock
            .Setup(d => d.RetrieveAsync(
                "sprk_matter",
                TestMatterId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(matterEntity);

        // T150 will normalize "sprk_matter" → "matter" at the ChatHostContext boundary,
        // but T151's ToDataverseLogicalName helper is bidirectional, so both forms resolve.
        var hostContext = new ChatHostContext(
            EntityType: "sprk_matter",
            EntityId: TestMatterIdString,
            EntityName: null,
            PageType: "entityrecord");

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — enrichment fires; EntityType in the prompt is the post-T150 canonical form
        context.SystemPrompt.Should()
            .Contain("Context: You are assisting with matter record 'Smith v. Jones'.");
    }

    #region Setup helpers

    private PlaybookChatContextProvider CreateProvider()
        => new(
            _scopeResolverMock.Object,
            _playbookServiceMock.Object,
            _dataverseServiceMock.Object,
            _loggerMock.Object,
            new Mock<IMatterMemoryService>().Object,
            promptBudgetTracker: null,
            // R7 Wave 12 task 151 — IGenericEntityService satisfied by the same IDataverseService
            // composite mock (IDataverseService inherits IGenericEntityService per
            // Spaarke.Dataverse.IDataverseService.cs:9-19).
            entityService: _dataverseServiceMock.Object);

    #endregion
}

using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// FR-P0-03 Business-slice determinism check (spaarke-ai-architecture-redesign-r2 task 003,
/// D-M2, NFR-04). Repeat-render pinning tests proving the two render sites that will feed the
/// future ContextEnvelope's Business slice render byte-identically for the same input — the
/// precondition for D-M2 placing Business in the cache-stable prefix.
/// </summary>
/// <remarks>
/// <para>
/// <b>Render sites verified (existing assembly path — ADR-040: no second render path
/// introduced to obtain a "clean" result)</b>:
/// </para>
/// <list type="bullet">
/// <item><description>Host-record identity block —
/// <see cref="PlaybookChatContextProvider.AppendEntityEnrichmentAsync"/>
/// (PlaybookChatContextProvider.cs:582). Dynamic per host context; the most likely place a
/// timestamp, per-request GUID, or unordered field would sneak in.</description></item>
/// <item><description>Per-table write-contract description hand-mirrored into
/// <see cref="DataverseCreateRecordHandler.Metadata"/> (DataverseCreateRecordHandler.cs:111) —
/// the "Host-record schema card" design.md §D-M2 names as a Business-slice source, "currently
/// hand-mirrored across tool descriptions".</description></item>
/// </list>
/// <para>
/// <b>Verdict (see <c>notes/business-slice-determinism.md</c>)</b>: CONFIRMED DETERMINISTIC.
/// Both sites are pure functions of their inputs — no <c>DateTime.UtcNow</c>/
/// <c>DateTimeOffset.UtcNow</c>, no <c>Guid.NewGuid()</c>, and no unordered-collection
/// iteration in the rendered text. The Business slice stays in the ContextEnvelope's
/// cache-stable prefix per D-M2; NFR-04 is NOT re-scoped.
/// </para>
/// </remarks>
public class BusinessSliceDeterminismContractTests
{
    private static readonly Guid TestPlaybookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestActionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string TestDocumentId = "doc-001";
    private const string TestTenantId = "tenant-abc";
    private const string BaseSystemPrompt = "You are a legal assistant.";
    private const string TestMatterIdString = "491b1efe-e562-f111-ab0c-000d3a4d8152";
    private static readonly Guid TestMatterId = Guid.Parse(TestMatterIdString);

    private static readonly Regex GuidPattern = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.Compiled);

    private static readonly Regex TimestampPattern = new(
        @"\b\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+\-]\d{2}:\d{2})?\b",
        RegexOptions.Compiled);

    // =========================================================================
    // Site 1 — host-record identity block (PlaybookChatContextProvider.cs:582)
    // =========================================================================

    [Fact]
    public async Task HostIdentityBlock_RenderedTwiceForSameHostContext_IsByteIdentical()
    {
        // Two INDEPENDENT provider instances — mirrors two separate Scoped-per-request
        // renders of the SAME host record (the shape production actually uses; the provider
        // is registered Scoped, so no two real requests ever share an instance).
        var mocksA = BuildCollaboratorMocks();
        var mocksB = BuildCollaboratorMocks();

        var matterEntity = new Entity("sprk_matter", TestMatterId);
        matterEntity["sprk_name"] = "Smith v. Jones";
        mocksA.Dataverse
            .Setup(d => d.RetrieveAsync("sprk_matter", TestMatterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matterEntity);
        mocksB.Dataverse
            .Setup(d => d.RetrieveAsync("sprk_matter", TestMatterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matterEntity);

        // EntityName is null — forces the server-side lazy-fetch path (T151), the most
        // dynamic shape of this render (a live Dataverse round-trip feeds the rendered text).
        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: TestMatterIdString,
            EntityName: null,
            PageType: "entityrecord");

        var sut1 = CreateProvider(mocksA);
        var sut2 = CreateProvider(mocksB);

        var context1 = await sut1.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext, cancellationToken: CancellationToken.None);
        var context2 = await sut2.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext, cancellationToken: CancellationToken.None);

        context1.SystemPrompt.Should().Be(context2.SystemPrompt,
            "the Business slice's host-identity render must be byte-identical across repeated turns for prompt-cache stability (NFR-04)");

        var enrichmentBlock = ExtractEnrichmentBlock(context1.SystemPrompt);
        enrichmentBlock.Should().NotBeNullOrEmpty("the enrichment block must be present for this host context");

        TimestampPattern.Matches(enrichmentBlock!).Should().BeEmpty(
            "the host-identity block must carry no embedded timestamp (NFR-04)");

        var guidsInBlock = GuidPattern.Matches(enrichmentBlock!).Select(m => m.Value).Distinct().ToList();
        guidsInBlock.Should().BeEquivalentTo(new[] { TestMatterIdString },
            "the ONLY GUID-shaped substring in the block must be the caller-supplied host record id — no per-request GUID is minted by the render");
    }

    [Fact]
    public async Task HostIdentityBlock_IdOnlyShape_RenderedTwice_IsByteIdentical()
    {
        // Id-only shape (EntityName unresolvable) exercises the OTHER branch of the
        // recordPhrase ternary — same byte-identity guarantee must hold there too.
        var mocksA = BuildCollaboratorMocks();
        var mocksB = BuildCollaboratorMocks();

        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: "entity-123",
            EntityName: null,
            PageType: "entityrecord");

        var sut1 = CreateProvider(mocksA);
        var sut2 = CreateProvider(mocksB);

        var context1 = await sut1.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext, cancellationToken: CancellationToken.None);
        var context2 = await sut2.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext, cancellationToken: CancellationToken.None);

        context1.SystemPrompt.Should().Be(context2.SystemPrompt,
            "the id-only identity block must also be byte-identical across repeated renders");
    }

    // =========================================================================
    // Site 2 — per-table write-contract description
    // (DataverseCreateRecordHandler.cs:111 — the "hand-mirrored" schema card design.md
    // §D-M2 names as a Business-slice source)
    // =========================================================================

    [Fact]
    public void WriteContractDescription_RenderedFromTwoIndependentInstances_IsByteIdentical()
    {
        var handler1 = new DataverseCreateRecordHandler(
            new Mock<IDataverseUserClient>().Object, new Mock<ILogger<DataverseCreateRecordHandler>>().Object);
        var handler2 = new DataverseCreateRecordHandler(
            new Mock<IDataverseUserClient>().Object, new Mock<ILogger<DataverseCreateRecordHandler>>().Object);

        var description1 = handler1.Metadata.Description;
        var description2 = handler2.Metadata.Description;

        description1.Should().Be(description2,
            "the sprk_matter write-contract text hand-mirrored into the tool description (D-M2 Business-slice source) must render identically across instances/turns");

        TimestampPattern.Matches(description1).Should().BeEmpty("write-contract text must carry no embedded timestamp");
        GuidPattern.Matches(description1).Should().BeEmpty("write-contract text must carry no embedded per-instance GUID");
    }

    // =========================================================================
    // NEGATIVE control (task 003 AC5) — proves the byte-identity assertion shape used
    // above actually DETECTS non-determinism rather than passing by construction.
    // =========================================================================

    [Fact]
    public void RepeatRenderEqualityCheck_WhenGuidInjectedPerCall_FailsToMatch()
    {
        // Simulates the regression a future edit would introduce by adding Guid.NewGuid()
        // to either render site above. The SAME byte-identity assertion shape used by the
        // real pinning tests fails here — proving those tests would catch this class of bug.
        string RenderWithInjectedGuid() =>
            $"Context: This chat is hosted on the matter record (trace {Guid.NewGuid()}).";

        var renderA = RenderWithInjectedGuid();
        var renderB = RenderWithInjectedGuid();

        renderA.Should().NotBe(renderB,
            "a per-call Guid.NewGuid() in the render breaks byte-identity — exactly what the real pinning tests above would catch");
    }

    [Fact]
    public void RepeatRenderEqualityCheck_WhenTimestampVariesAcrossCalls_FailsToMatch()
    {
        // Simulates the regression a future edit would introduce by embedding a per-call
        // clock read (DateTimeOffset.UtcNow) in either render site. Two renders captured at
        // different instants necessarily disagree; the byte-identity assertion used above
        // would fail on any such variance, proving it detects this class of bug too.
        const string renderedAtOne = "Context: This chat is hosted on the matter record (rendered-at 2026-07-08T10:00:00.000Z).";
        const string renderedAtTwo = "Context: This chat is hosted on the matter record (rendered-at 2026-07-08T10:00:00.001Z).";

        renderedAtOne.Should().NotBe(renderedAtTwo,
            "an embedded per-call timestamp breaks byte-identity — exactly what the real pinning tests above would catch");
    }

    [Fact]
    public void RepeatRenderEqualityCheck_WhenSourceOrderingUnstable_FailsToMatch()
    {
        // Simulates the "unordered-dictionary" failure mode NFR-04 names: two logically
        // equal property sets serialized in different insertion order produce different text.
        var columnsInDeclaredOrder = new[] { "sprk_mattername", "sprk_matternumber", "sprk_mattertype" };
        var columnsReordered = new[] { "sprk_mattertype", "sprk_mattername", "sprk_matternumber" };

        static string Render(IEnumerable<string> columns) => string.Join(", ", columns);

        Render(columnsInDeclaredOrder).Should().NotBe(Render(columnsReordered),
            "unstable source ordering changes the rendered text — exactly the class of bug the pinning tests above are designed to catch");
    }

    #region Helpers

    private sealed record CollaboratorMocks(
        Mock<IScopeResolverService> ScopeResolver,
        Mock<IPlaybookService> Playbook,
        Mock<IDataverseService> Dataverse,
        Mock<ILogger<PlaybookChatContextProvider>> Logger);

    private static CollaboratorMocks BuildCollaboratorMocks()
    {
        var scopeResolverMock = new Mock<IScopeResolverService>();
        var playbookServiceMock = new Mock<IPlaybookService>();
        var dataverseServiceMock = new Mock<IDataverseService>();
        var loggerMock = new Mock<ILogger<PlaybookChatContextProvider>>();

        playbookServiceMock
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

        scopeResolverMock
            .Setup(s => s.GetActionAsync(TestActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction
            {
                Id = TestActionId,
                Name = "Test Action",
                SystemPrompt = BaseSystemPrompt
            });

        scopeResolverMock
            .Setup(s => s.ResolvePlaybookScopesAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedScopes([], [], []));

        return new CollaboratorMocks(scopeResolverMock, playbookServiceMock, dataverseServiceMock, loggerMock);
    }

    private static PlaybookChatContextProvider CreateProvider(CollaboratorMocks mocks)
        => new(
            mocks.ScopeResolver.Object,
            mocks.Playbook.Object,
            mocks.Dataverse.Object,
            mocks.Logger.Object,
            new Mock<IMatterMemoryService>().Object,
            promptBudgetTracker: null,
            // IGenericEntityService satisfied by the same IDataverseService composite mock
            // (IDataverseService inherits IGenericEntityService — Spaarke.Dataverse.IDataverseService.cs).
            entityService: mocks.Dataverse.Object);

    private static string? ExtractEnrichmentBlock(string systemPrompt)
    {
        var idx = systemPrompt.IndexOf("Context: This chat is hosted on", StringComparison.Ordinal);
        return idx < 0 ? null : systemPrompt[idx..];
    }

    #endregion
}

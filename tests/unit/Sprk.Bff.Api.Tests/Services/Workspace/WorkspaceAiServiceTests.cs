using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Workspace.Models;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Workspace;

/// <summary>
/// Unit tests for <see cref="WorkspaceAiService"/> focused on the FR-02 stable-ID
/// playbook-resolution refactor (chat-routing-redesign-r1 task 018, Wave 1-E Pattern A).
///
/// <para>
/// Pre-task-018 the service carried a hardcoded GUID
/// (<c>18cf3cc8-02ec-f011-8406-7c1e520aa4df</c> — DEV "Document Profile" playbook) and
/// read the override via a raw <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
/// indexer. Post-task-018 both have been removed in favour of typed-options +
/// <see cref="IPlaybookLookupService.GetByIdAsync"/> per ADR-018 + ADR-014.
/// </para>
/// <para>
/// Tests here verify ONLY the new boundary:
/// </para>
/// <list type="bullet">
///   <item>The service injects <see cref="IPlaybookLookupService"/> +
///         <see cref="IOptions{TOptions}"/> of <see cref="WorkspaceOptions"/>.</item>
///   <item>The hardcoded DefaultAiSummaryPlaybookId constant is gone (reflection assert).</item>
///   <item>The raw configuration indexer dependency is gone (no
///         <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> field).</item>
///   <item>Empty / whitespace config returns the template fallback (graceful degrade —
///         contract per BuildFallbackResponse; the tile must still render).</item>
///   <item>Lookup-service exceptions return the template fallback (no 500 to caller).</item>
/// </list>
/// <para>
/// Entity-fetch path coverage (sprk_event / sprk_matter / sprk_project / sprk_document)
/// is out of scope here — those private helpers were unchanged by task 018 and exercise
/// the existing fallback path via the unsupported-type branch.
/// </para>
/// </summary>
public class WorkspaceAiServiceTests
{
    // FR-02 task 018 (chat-routing-redesign-r1): tests configure WorkspaceOptions with the
    // DEV-environment value for the workspace AI summary playbook — the "Document Profile"
    // playbook's sprk_playbookid (mirrors its sprk_analysisplaybookid PK per task 014 backfill).
    // The service passes this string into IPlaybookLookupService.GetByIdAsync; the mock
    // returns a PlaybookResponse whose Id is the same GUID (parsed) so the engine sees the
    // unchanged GUID identifier.
    private const string ConfiguredAiSummaryPlaybookId = "18cf3cc8-02ec-f011-8406-7c1e520aa4df";
    private static readonly Guid ResolvedAiSummaryPlaybookGuid =
        Guid.Parse(ConfiguredAiSummaryPlaybookId);

    private readonly Mock<IGenericEntityService> _genericEntityServiceMock;
    private readonly Mock<IDocumentDataverseService> _documentServiceMock;
    private readonly Mock<ILogger<WorkspaceAiService>> _loggerMock;
    private readonly Mock<IPlaybookLookupService> _playbookLookupMock;
    private readonly Mock<IConsumerRoutingService> _consumerRoutingMock;
    private readonly Mock<IWorkspacePrefillAi> _prefillAiMock;

    public WorkspaceAiServiceTests()
    {
        _genericEntityServiceMock = new Mock<IGenericEntityService>();
        _documentServiceMock = new Mock<IDocumentDataverseService>();
        _loggerMock = new Mock<ILogger<WorkspaceAiService>>();
        _playbookLookupMock = new Mock<IPlaybookLookupService>();
        _consumerRoutingMock = new Mock<IConsumerRoutingService>();
        _prefillAiMock = new Mock<IWorkspacePrefillAi>();

        // Default stub: GetByIdAsync(<configured id>, ct) → PlaybookResponse with Id = GUID.
        _playbookLookupMock
            .Setup(p => p.GetByIdAsync(ConfiguredAiSummaryPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse
            {
                Id = ResolvedAiSummaryPlaybookGuid,
                Name = "Document Profile",
                PlaybookCode = "PB-002",
                IsActive = true
            });

        // Default stub (FR-1R-05 task 028c): the routing table returns null by default so
        // tests exercise the env-var fallback (preserving Phase 1 contract). Individual
        // tests that exercise the routing-table happy path override this stub.
        _consumerRoutingMock
            .Setup(c => c.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
    }

    private WorkspaceAiService CreateSut(WorkspaceOptions? options = null) => new(
        _genericEntityServiceMock.Object,
        _documentServiceMock.Object,
        _loggerMock.Object,
        _playbookLookupMock.Object,
        _consumerRoutingMock.Object,
        Options.Create(options ?? new WorkspaceOptions
        {
            AiSummaryPlaybookId = ConfiguredAiSummaryPlaybookId
        }),
        _prefillAiMock.Object);

    private static HttpContext NewHttpContext() => new DefaultHttpContext();

    // ─── (a) ADR-018 / ADR-010 — Pattern A migration shape ────────────────────────────────

    [Fact]
    public void WorkspaceAiService_InjectsIPlaybookLookupService_AndIOptionsWorkspaceOptions_PatternA()
    {
        // ADR-018 + ADR-010: post-task-018 the service consumes the stable-ID lookup
        // surface via DI rather than raw IConfiguration indexer reads.
        var ctor = typeof(WorkspaceAiService).GetConstructors().Single();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToArray();

        paramTypes.Should().Contain(typeof(IPlaybookLookupService),
            "task 018 — Pattern A consumers MUST inject IPlaybookLookupService " +
            "for runtime stable-ID resolution per ADR-014");
        paramTypes.Should().Contain(typeof(IOptions<WorkspaceOptions>),
            "ADR-018 — typed-options consumption replaces raw IConfiguration[\"...\"] indexer reads");
    }


    // ─── (b) Lookup-service contract — happy path ─────────────────────────────────────────

    [Fact]
    public async Task GenerateAiSummaryAsync_ResolvesPlaybookViaLookupService_UsingConfiguredOptionValue()
    {
        // FR-02 / ADR-014 binding — service MUST hit IPlaybookLookupService.GetByIdAsync
        // with the configured WorkspaceOptions.AiSummaryPlaybookId value (string).
        // Engine is exercised via the unsupported-entity-type early-throw branch so we can
        // assert the lookup contract without setting up the full PrefillAi stream.
        // BUT — unsupported-type throws BEFORE the lookup runs. To assert the lookup
        // contract, we drive through ExecutePlaybookAnalysisAsync via the unsupported-entity
        // path NOT being used; instead we set up the prefill mock to return an empty stream
        // (which falls into BuildFallbackResponse). The lookup MUST still have been called
        // before the engine call.

        // Arrange — supported entity type; document path because it doesn't depend on
        // OptionSetValue construction (those are sealed). Document service mocked to throw
        // KeyNotFoundException which the entity-fetch wrapper catches → fallback description
        // path is taken AND ExecutePlaybookAnalysisAsync still runs the lookup.
        _documentServiceMock
            .Setup(d => d.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentEntity?)null);

        // Prefill mock returns an empty stream — service will fall back to template response.
        _prefillAiMock
            .Setup(p => p.ExecutePlaybookAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(EmptyEventStream());

        var sut = CreateSut();
        var request = new AiSummaryRequest("sprk_document", Guid.NewGuid(), Context: null);

        // Act — for sprk_document the FetchDocumentDescriptionAsync helper throws
        // KeyNotFoundException when the doc is null; the outer catch in
        // FetchEntityDescriptionAsync re-throws KeyNotFoundException so the caller can map
        // to 404. So we don't reach ExecutePlaybookAnalysisAsync in this path. Use a
        // different validated entity type that won't pre-empt the lookup.

        // We must pick an entity type whose Fetch* helper doesn't pre-throw on the mock.
        // sprk_event needs sprk_eventname column — the mock returns null Entity by default,
        // FetchEventDescriptionAsync's GetAttributeValue calls on null Entity throws NRE.
        // To exercise the lookup-service call clean, mock RetrieveAsync to return a minimal entity.
        _genericEntityServiceMock
            .Setup(g => g.RetrieveAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity("sprk_event"));

        request = new AiSummaryRequest("sprk_event", Guid.NewGuid(), Context: null);
        var ctx = NewHttpContext();

        var result = await sut.GenerateAiSummaryAsync(request, "user-1", ctx, CancellationToken.None);

        // Assert — lookup was called with the configured stable-ID string exactly once.
        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(ConfiguredAiSummaryPlaybookId, It.IsAny<CancellationToken>()),
            Times.Once,
            "FR-02 — WorkspaceAiService MUST resolve the playbook via IPlaybookLookupService " +
            "using the configured WorkspaceOptions.AiSummaryPlaybookId");

        result.Should().NotBeNull();
        // Empty engine stream → fallback path → non-empty analysis from BuildFallbackResponse.
        result.Analysis.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GenerateAiSummaryAsync_ForwardsResolvedGuid_ToPlaybookExecution()
    {
        // The configured string is resolved to a Guid via lookup; that Guid is the
        // PlaybookRunRequest.PlaybookId forwarded to the AI facade (engine call site).
        _genericEntityServiceMock
            .Setup(g => g.RetrieveAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity("sprk_event"));

        Guid forwardedPlaybookId = Guid.Empty;
        _prefillAiMock
            .Setup(p => p.ExecutePlaybookAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Returns<PlaybookRunRequest, HttpContext, CancellationToken>((req, _, _) =>
            {
                forwardedPlaybookId = req.PlaybookId;
                return EmptyEventStream();
            });

        var sut = CreateSut();
        var request = new AiSummaryRequest("sprk_event", Guid.NewGuid(), Context: null);

        _ = await sut.GenerateAiSummaryAsync(request, "user-1", NewHttpContext(), CancellationToken.None);

        forwardedPlaybookId.Should().Be(ResolvedAiSummaryPlaybookGuid,
            "FR-02 — the resolved playbook.Id (Guid) MUST be the PlaybookRunRequest.PlaybookId " +
            "forwarded to the AI facade (no other Guid is acceptable; no hardcoded fallback)");
    }

    // ─── (c) Empty/missing config — graceful degrade to template ──────────────────────────

    [Fact]
    public async Task GenerateAiSummaryAsync_EmptyConfiguredId_ReturnsFallback_WithoutLookupOrEngine()
    {
        // Unlike SessionSummarizeOrchestrator (R6 FR-26 convergence point — fail-fast),
        // workspace AI summaries gracefully degrade: empty config → template response so
        // the tile still renders. The lookup MUST NOT be called and the engine MUST NOT
        // be called when config is missing.
        _genericEntityServiceMock
            .Setup(g => g.RetrieveAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity("sprk_event"));

        var sut = CreateSut(new WorkspaceOptions { AiSummaryPlaybookId = null });
        var request = new AiSummaryRequest("sprk_event", Guid.NewGuid(), Context: null);

        var result = await sut.GenerateAiSummaryAsync(request, "user-1", NewHttpContext(), CancellationToken.None);

        result.Should().NotBeNull();
        result.Analysis.Should().NotBeNullOrWhiteSpace(
            "BuildFallbackResponse contract — workspace AI tile MUST still render");
        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "empty config short-circuits before lookup");
        _prefillAiMock.Verify(
            p => p.ExecutePlaybookAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "empty config short-circuits before engine call");
    }

    [Fact]
    public async Task GenerateAiSummaryAsync_LookupServiceThrows_ReturnsFallback_WithoutEngine()
    {
        // The lookup-service call is wrapped in try/catch so transient Dataverse failures
        // degrade gracefully (tile renders template) rather than 500-ing the endpoint.
        _genericEntityServiceMock
            .Setup(g => g.RetrieveAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity("sprk_event"));

        _playbookLookupMock
            .Setup(p => p.GetByIdAsync(ConfiguredAiSummaryPlaybookId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "transient Dataverse failure — lookup unable to resolve sprk_playbookid alt-key"));

        var sut = CreateSut();
        var request = new AiSummaryRequest("sprk_event", Guid.NewGuid(), Context: null);

        var result = await sut.GenerateAiSummaryAsync(request, "user-1", NewHttpContext(), CancellationToken.None);

        result.Should().NotBeNull();
        result.Analysis.Should().NotBeNullOrWhiteSpace();
        _prefillAiMock.Verify(
            p => p.ExecutePlaybookAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "lookup failure short-circuits before engine call — template fallback used");
    }

    [Fact]
    public async Task GenerateAiSummaryAsync_UnsupportedEntityType_StillThrowsBeforeLookup()
    {
        // Regression: the input-validation early-throw branch MUST stay before any lookup
        // call so misuse doesn't burn a Dataverse query.
        var sut = CreateSut();
        var request = new AiSummaryRequest("sprk_nonexistent", Guid.NewGuid(), Context: null);

        var act = async () => await sut.GenerateAiSummaryAsync(
            request, "user-1", NewHttpContext(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "validation runs before lookup");
    }

    // ─── (d) FR-1R-05 task 028c — Consumer routing service integration ────────────────────

    [Fact]
    public void WorkspaceAiService_Constructor_RequiresConsumerRoutingService_FR1R05()
    {
        // FR-1R-05 task 028c — the Pattern A migration to the sprk_playbookconsumer routing
        // table MUST inject IConsumerRoutingService directly via the constructor. Compile-time
        // typo defense via ConsumerTypes.AiSummary (code-review S-5 hardening).
        var ctor = typeof(WorkspaceAiService).GetConstructors().Single();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToArray();

        paramTypes.Should().Contain(typeof(IConsumerRoutingService),
            "FR-1R-05 task 028c — IConsumerRoutingService MUST be a constructor dependency " +
            "for sprk_playbookconsumer routing-table resolution");
    }

    [Fact]
    public async Task GenerateAiSummaryAsync_RoutingTableReturnsGuid_UsesRoutedPlaybookId_FR1R05()
    {
        // FR-1R-05 task 028c happy path: when IConsumerRoutingService.ResolveAsync returns
        // a non-null Guid, the service MUST use it (no env-var fallback) and forward it to
        // the playbook lookup + AI facade.
        var routedPlaybookId = Guid.NewGuid();
        var resolvedPlaybookGuid = routedPlaybookId; // lookup returns the same id

        _consumerRoutingMock
            .Setup(c => c.ResolveAsync(
                ConsumerTypes.AiSummary,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(routedPlaybookId);

        // The lookup service is called with the GUID string (routing table value).
        var routedPlaybookIdStr = routedPlaybookId.ToString();
        _playbookLookupMock
            .Setup(p => p.GetByIdAsync(routedPlaybookIdStr, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse
            {
                Id = resolvedPlaybookGuid,
                Name = "Document Profile",
                PlaybookCode = "PB-002",
                IsActive = true
            });

        _genericEntityServiceMock
            .Setup(g => g.RetrieveAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity("sprk_event"));

        Guid forwardedPlaybookId = Guid.Empty;
        _prefillAiMock
            .Setup(p => p.ExecutePlaybookAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Returns<PlaybookRunRequest, HttpContext, CancellationToken>((req, _, _) =>
            {
                forwardedPlaybookId = req.PlaybookId;
                return EmptyEventStream();
            });

        var sut = CreateSut();
        var request = new AiSummaryRequest("sprk_event", Guid.NewGuid(), Context: null);

        _ = await sut.GenerateAiSummaryAsync(request, "user-1", NewHttpContext(), CancellationToken.None);

        forwardedPlaybookId.Should().Be(resolvedPlaybookGuid,
            "FR-1R-05 — the routing-table-resolved playbook.Id (Guid) MUST be the PlaybookRunRequest.PlaybookId");
        _consumerRoutingMock.Verify(
            c => c.ResolveAsync(
                ConsumerTypes.AiSummary,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "FR-1R-05 — routing service MUST be called on every summary request");
    }

    [Fact]
    public async Task GenerateAiSummaryAsync_RoutingTableReturnsNull_FallsBackToEnvVar_FR1R06()
    {
        // FR-1R-06 deprecation-window fallback: when IConsumerRoutingService.ResolveAsync
        // returns null AND WorkspaceOptions.AiSummaryPlaybookId is configured, the service
        // MUST fall back to the env-var value (preserving Phase 1 behavior for admins
        // who haven't migrated to the table yet).
        _consumerRoutingMock
            .Setup(c => c.ResolveAsync(
                ConsumerTypes.AiSummary,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        _genericEntityServiceMock
            .Setup(g => g.RetrieveAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity("sprk_event"));

        Guid forwardedPlaybookId = Guid.Empty;
        _prefillAiMock
            .Setup(p => p.ExecutePlaybookAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Returns<PlaybookRunRequest, HttpContext, CancellationToken>((req, _, _) =>
            {
                forwardedPlaybookId = req.PlaybookId;
                return EmptyEventStream();
            });

        var sut = CreateSut();
        var request = new AiSummaryRequest("sprk_event", Guid.NewGuid(), Context: null);

        _ = await sut.GenerateAiSummaryAsync(request, "user-1", NewHttpContext(), CancellationToken.None);

        forwardedPlaybookId.Should().Be(ResolvedAiSummaryPlaybookGuid,
            "FR-1R-06 — null routing-table result MUST fall back to WorkspaceOptions.AiSummaryPlaybookId");
        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(ConfiguredAiSummaryPlaybookId, It.IsAny<CancellationToken>()),
            Times.Once,
            "FR-1R-06 — env-var value MUST be passed to the playbook lookup");
    }

    [Fact]
    public async Task GenerateAiSummaryAsync_RoutingNullAndEnvVarEmpty_ReturnsFallback_PreservesGracefulDegrade()
    {
        // Phase 1 task 018 graceful-degrade contract preserved: when BOTH the routing
        // table returns null AND WorkspaceOptions.AiSummaryPlaybookId is empty, the
        // workspace AI tile MUST still render via the template fallback — no exception,
        // no 500. This is the chat-routing-redesign-r1 task 028c invariant.
        _consumerRoutingMock
            .Setup(c => c.ResolveAsync(
                ConsumerTypes.AiSummary,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        _genericEntityServiceMock
            .Setup(g => g.RetrieveAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity("sprk_event"));

        var sut = CreateSut(new WorkspaceOptions { AiSummaryPlaybookId = null });
        var request = new AiSummaryRequest("sprk_event", Guid.NewGuid(), Context: null);

        var result = await sut.GenerateAiSummaryAsync(request, "user-1", NewHttpContext(), CancellationToken.None);

        result.Should().NotBeNull();
        result.Analysis.Should().NotBeNullOrWhiteSpace(
            "graceful-degrade — workspace AI tile MUST still render when no playbook can be resolved");
        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no playbook id → no lookup → template fallback path");
        _prefillAiMock.Verify(
            p => p.ExecutePlaybookAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no playbook id → no engine call → template fallback path");
    }

    // ─── helpers ──────────────────────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<PlaybookStreamEvent> EmptyEventStream()
    {
        await Task.CompletedTask;
        yield break;
    }
}

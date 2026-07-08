using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Workspace.Models;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Workspace;

/// <summary>
/// Unit tests for <see cref="WorkspaceAiService"/> focused on the FR-P3-01 hard-cutover
/// routing contract (ai-architecture-redesign-r1 task 040).
///
/// <para>
/// The playbook is resolved EXCLUSIVELY via <see cref="IConsumerRoutingService"/>
/// (sprk_playbookconsumer Binding table, consumerType <c>ai-summary</c>); the legacy
/// WorkspaceOptions typed-options fallback was DELETED per NFR-08. Tests verify:
/// </para>
/// <list type="bullet">
///   <item>Resolution comes from the (mocked) routing service — the routed GUID flows
///         through <see cref="IPlaybookLookupService.GetByIdAsync"/> into the
///         <c>PlaybookRunRequest.PlaybookId</c> forwarded to the AI facade.</item>
///   <item>Routing null yields the clean template-fallback response (graceful degrade —
///         the workspace tile must still render; no exception, no 500, no config
///         fallback).</item>
///   <item>Lookup-service exceptions return the template fallback (no 500 to caller).</item>
/// </list>
/// <para>
/// Entity-fetch path coverage (sprk_event / sprk_matter / sprk_project / sprk_document)
/// is out of scope here — those private helpers were unchanged by the cutover and exercise
/// the existing fallback path via the unsupported-type branch.
/// </para>
/// </summary>
public class WorkspaceAiServiceTests
{
    // FR-P3-01: tests stub the routing service (sprk_playbookconsumer Binding table) with
    // the DEV-environment value for the workspace AI summary playbook — the "Document
    // Profile" playbook's sprk_playbookid (mirrors its sprk_analysisplaybookid PK). The
    // service passes this GUID's string form into IPlaybookLookupService.GetByIdAsync; the
    // mock returns a PlaybookResponse whose Id is the same GUID (parsed) so the engine sees
    // the unchanged GUID identifier.
    private const string RoutedAiSummaryPlaybookId = "18cf3cc8-02ec-f011-8406-7c1e520aa4df";
    private static readonly Guid RoutedAiSummaryPlaybookGuid =
        Guid.Parse(RoutedAiSummaryPlaybookId);

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

        // Default stub (FR-P3-01): the routing table resolves the ai-summary consumer to
        // the Document Profile playbook GUID. Individual tests that exercise the
        // routing-miss path override this stub with null.
        _consumerRoutingMock
            .Setup(c => c.ResolveAsync(
                ConsumerTypes.AiSummary,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(RoutedAiSummaryPlaybookGuid);

        // Default stub: GetByIdAsync(<routed id string>, ct) → PlaybookResponse with Id = GUID.
        _playbookLookupMock
            .Setup(p => p.GetByIdAsync(RoutedAiSummaryPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse
            {
                Id = RoutedAiSummaryPlaybookGuid,
                Name = "Document Profile",
                PlaybookCode = "PB-002",
                IsActive = true
            });
    }

    private WorkspaceAiService CreateSut() => new(
        _genericEntityServiceMock.Object,
        _documentServiceMock.Object,
        _loggerMock.Object,
        _playbookLookupMock.Object,
        _consumerRoutingMock.Object,
        _prefillAiMock.Object);

    private static HttpContext NewHttpContext() => new DefaultHttpContext();

    // ─── (a) FR-P3-01 — constructor shape: routing is the only resolution source ─────────

    [Fact]
    public void WorkspaceAiService_Constructor_RequiresConsumerRoutingService_FRP301()
    {
        // FR-P3-01 — the sprk_playbookconsumer Binding routing table is the ONLY
        // playbook-resolution source, so IConsumerRoutingService MUST be a constructor
        // dependency. Compile-time typo defense via ConsumerTypes.AiSummary (code-review
        // S-5 hardening).
        var ctor = typeof(WorkspaceAiService).GetConstructors().Single();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToArray();

        paramTypes.Should().Contain(typeof(IConsumerRoutingService),
            "FR-P3-01 — IConsumerRoutingService MUST be a constructor dependency " +
            "for sprk_playbookconsumer routing-table resolution");
        paramTypes.Should().Contain(typeof(IPlaybookLookupService),
            "Pattern A — IPlaybookLookupService MUST remain a constructor dependency " +
            "for the downstream playbook-record load (ADR-014 caching)");
    }

    [Fact]
    public void WorkspaceAiService_Constructor_HasNoWorkspaceOptionsDependency_FRP301()
    {
        // FR-P3-01 hard cutover — the WorkspaceOptions typed-options fallback surface was
        // DELETED (NFR-08: no shims). The constructor MUST NOT carry any WorkspaceOptions
        // dependency; routing is the only source.
        var ctor = typeof(WorkspaceAiService).GetConstructors().Single();

        ctor.GetParameters().Should().NotContain(
            p => p.ParameterType.FullName!.Contains("WorkspaceOptions"),
            "FR-P3-01 — the WorkspaceOptions config fallback was deleted; " +
            "the routing table is the ONLY playbook-resolution source");
    }

    // ─── (b) FR-P3-01 — resolution comes from the routing service ─────────────────────────

    [Fact]
    public async Task GenerateAiSummaryAsync_RoutingTableReturnsGuid_UsesRoutedPlaybookId_FRP301()
    {
        // FR-P3-01 happy path: the routed GUID (sprk_playbookconsumer Binding row) is the
        // ONLY resolution source. It MUST flow through IPlaybookLookupService.GetByIdAsync
        // and become the PlaybookRunRequest.PlaybookId forwarded to the AI facade.
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

        var result = await sut.GenerateAiSummaryAsync(request, "user-1", NewHttpContext(), CancellationToken.None);

        _consumerRoutingMock.Verify(
            c => c.ResolveAsync(
                ConsumerTypes.AiSummary,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "FR-P3-01 — the routing service MUST be consulted on every summary request");
        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(RoutedAiSummaryPlaybookId, It.IsAny<CancellationToken>()),
            Times.Once,
            "FR-P3-01 — the routed GUID (string form) MUST be passed to the playbook lookup");
        forwardedPlaybookId.Should().Be(RoutedAiSummaryPlaybookGuid,
            "FR-P3-01 — the routing-table-resolved playbook.Id (Guid) MUST be the " +
            "PlaybookRunRequest.PlaybookId (no other Guid is acceptable; no config fallback)");

        result.Should().NotBeNull();
        // Empty engine stream → fallback path → non-empty analysis from BuildFallbackResponse.
        result.Analysis.Should().NotBeNullOrWhiteSpace();
    }

    // ─── (c) FR-P3-01 — routing null yields the clean template fallback (no config) ──────

    [Fact]
    public async Task GenerateAiSummaryAsync_RoutingTableReturnsNull_ReturnsTemplateFallback_NoConfigFallback_FRP301()
    {
        // FR-P3-01 clean-error contract: when no enabled sprk_playbookconsumer row resolves
        // consumerType 'ai-summary', the service MUST degrade gracefully to the template
        // response (the workspace AI tile must still render — no exception, no 500) WITHOUT
        // consulting any config fallback: the lookup and the engine are never touched.
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

        var sut = CreateSut();
        var request = new AiSummaryRequest("sprk_event", Guid.NewGuid(), Context: null);

        var result = await sut.GenerateAiSummaryAsync(request, "user-1", NewHttpContext(), CancellationToken.None);

        result.Should().NotBeNull();
        result.Analysis.Should().NotBeNullOrWhiteSpace(
            "graceful-degrade — workspace AI tile MUST still render when the routing table " +
            "has no enabled row (BuildFallbackResponse contract)");
        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "FR-P3-01 — routing null is terminal: no lookup, no config fallback");
        _prefillAiMock.Verify(
            p => p.ExecutePlaybookAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "FR-P3-01 — routing null is terminal: no engine call, no config fallback");
    }

    // ─── (d) Lookup failure — graceful degrade preserved ─────────────────────────────────

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
            .Setup(p => p.GetByIdAsync(RoutedAiSummaryPlaybookId, It.IsAny<CancellationToken>()))
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

    // ─── helpers ──────────────────────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<PlaybookStreamEvent> EmptyEventStream()
    {
        await Task.CompletedTask;
        yield break;
    }
}

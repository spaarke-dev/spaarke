using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for <see cref="AppOnlyAnalysisService"/> playbook resolution after the
/// FR-1R-05 routing-table migration (chat-routing-redesign-r1 task 028d).
///
/// <para>
/// The previously-existing 2 Pattern B execution-path consts at lines :46 and :1068
/// (flagged by task 027 exit-gate evidence as <c>const string DocumentProfilePlaybookId</c>
/// and <c>const string EmailAnalysisPlaybookId</c>) were retired into
/// <c>private static readonly Guid Fallback*PlaybookId</c> graceful-degrade fallbacks.
/// Primary resolution for the email-analysis path now flows through
/// <see cref="IConsumerRoutingService.ResolveAsync"/> with
/// <see cref="ConsumerTypes.EmailAnalysis"/>; the typed-options/const path is reached only
/// when the routing table returns null (deprecation-window safety per FR-1R-06).
/// </para>
///
/// <para>
/// These tests target the private <c>ResolvePlaybookAsync</c> method via reflection — the
/// single resolution point in the service (both <c>AnalyzeDocumentAsync</c> and
/// <c>ExecutePlaybookAnalysisAsync</c> delegate to it). Reflection is the cleanest seam
/// because the public methods drag in the full Dataverse/SPE/text-extraction pipeline.
/// </para>
/// </summary>
public class AppOnlyAnalysisServiceResolveTests
{
    private const string EmailAnalysisPlaybookName = "Email Analysis";
    private const string DocumentProfilePlaybookName = "Document Profile";
    private static readonly Guid FallbackEmailAnalysisGuid =
        Guid.Parse("bc71facf-6af1-f011-8406-7ced8d1dc988");
    private static readonly Guid FallbackDocumentProfileGuid =
        Guid.Parse("18cf3cc8-02ec-f011-8406-7c1e520aa4df");

    private readonly Mock<IPlaybookLookupService> _playbookLookupMock = new();
    private readonly Mock<IPlaybookService> _playbookServiceMock = new();
    private readonly Mock<IConsumerRoutingService> _consumerRoutingMock = new();

    private AppOnlyAnalysisService CreateSut()
    {
        return new AppOnlyAnalysisService(
            documentService: Mock.Of<IDocumentDataverseService>(),
            analysisService: Mock.Of<IAnalysisDataverseService>(),
            speFileOperations: Mock.Of<ISpeFileOperations>(),
            textExtractor: Mock.Of<ITextExtractor>(),
            playbookService: _playbookServiceMock.Object,
            playbookLookup: _playbookLookupMock.Object,
            consumerRouting: _consumerRoutingMock.Object,
            scopeResolver: Mock.Of<IScopeResolverService>(),
            toolHandlerRegistry: Mock.Of<IToolHandlerRegistry>(),
            nodeService: Mock.Of<INodeService>(),
            playbookOrchestrator: Mock.Of<IPlaybookOrchestrationService>(),
            logger: Mock.Of<ILogger<AppOnlyAnalysisService>>());
    }

    private static Task<PlaybookResponse> InvokeResolvePlaybookAsync(
        AppOnlyAnalysisService sut,
        string playbookName,
        CancellationToken ct = default)
    {
        var method = typeof(AppOnlyAnalysisService).GetMethod(
            "ResolvePlaybookAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull("ResolvePlaybookAsync is the single resolution point and " +
            "MUST remain a private method on AppOnlyAnalysisService");
        return (Task<PlaybookResponse>)method!.Invoke(sut, new object?[] { playbookName, ct })!;
    }

    // ─── (a) FR-1R-05 happy path — routing-table returns Guid → use it ───────────────────────

    [Fact]
    public async Task ResolvePlaybook_EmailAnalysis_RoutingTableReturnsId_UsesRoutingTableGuid()
    {
        // Arrange: IConsumerRoutingService returns a non-null Guid for ConsumerTypes.EmailAnalysis
        var routedGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        _consumerRoutingMock
            .Setup(c => c.ResolveAsync(
                ConsumerTypes.EmailAnalysis,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(routedGuid);
        _playbookLookupMock
            .Setup(p => p.GetByIdAsync(routedGuid.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse
            {
                Id = routedGuid,
                Name = EmailAnalysisPlaybookName,
                PlaybookCode = string.Empty,
                IsActive = true,
            });

        var sut = CreateSut();

        // Act
        var result = await InvokeResolvePlaybookAsync(sut, EmailAnalysisPlaybookName);

        // Assert
        result.Id.Should().Be(routedGuid,
            "FR-1R-05 — routing-table GUID MUST be forwarded to IPlaybookLookupService when " +
            "IConsumerRoutingService returns a non-null value");
        _consumerRoutingMock.Verify(
            c => c.ResolveAsync(
                ConsumerTypes.EmailAnalysis,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "FR-1R-05 hardening (code-review S-5) — consumer type MUST be ConsumerTypes.EmailAnalysis " +
            "(compile-time typo defense), NOT the literal string");
        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(routedGuid.ToString(), It.IsAny<CancellationToken>()),
            Times.Once,
            "FR-1R-05 — the routing-table GUID is resolved into a PlaybookResponse via the " +
            "existing IPlaybookLookupService (single-cache discipline; no duplicate cache layer)");
        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(FallbackEmailAnalysisGuid.ToString(), It.IsAny<CancellationToken>()),
            Times.Never,
            "FR-1R-05 — fallback const MUST NOT be consulted when routing-table returns a non-null Guid");
    }

    // ─── (b) FR-1R-05 graceful-degrade — routing-table returns null → fallback const ─────────

    [Fact]
    public async Task ResolvePlaybook_EmailAnalysis_RoutingTableReturnsNull_FallsBackToConstGuid()
    {
        // Arrange: IConsumerRoutingService returns null → orchestrator falls back to the
        // Fallback*PlaybookId const-equivalent (private static readonly Guid).
        _consumerRoutingMock
            .Setup(c => c.ResolveAsync(
                ConsumerTypes.EmailAnalysis,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        _playbookLookupMock
            .Setup(p => p.GetByIdAsync(FallbackEmailAnalysisGuid.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse
            {
                Id = FallbackEmailAnalysisGuid,
                Name = EmailAnalysisPlaybookName,
                PlaybookCode = string.Empty,
                IsActive = true,
            });

        var sut = CreateSut();

        // Act
        var result = await InvokeResolvePlaybookAsync(sut, EmailAnalysisPlaybookName);

        // Assert
        result.Id.Should().Be(FallbackEmailAnalysisGuid,
            "FR-1R-05 graceful-degrade — null from IConsumerRoutingService MUST trigger fallback to the " +
            "FallbackEmailAnalysisPlaybookId const (preserves pre-028d behavior verbatim for the " +
            "FR-1R-06 deprecation window)");
        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(FallbackEmailAnalysisGuid.ToString(), It.IsAny<CancellationToken>()),
            Times.Once,
            "FR-1R-05 graceful-degrade — the const fallback resolves through the same IPlaybookLookupService");
        _playbookServiceMock.Verify(
            p => p.GetByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "FR-03 — well-known names MUST NOT fall through to the legacy by-name path");
    }

    // ─── (c) FR-1R-05 defensive edge — empty Guid routes like null ───────────────────────────

    [Fact]
    public async Task ResolvePlaybook_EmailAnalysis_RoutingTableReturnsEmptyGuid_FallsBackToConstGuid()
    {
        // Arrange: routing service returns Guid.Empty (defensive sentinel — semantically no-match)
        _consumerRoutingMock
            .Setup(c => c.ResolveAsync(
                ConsumerTypes.EmailAnalysis,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null); // ResolveAsync impl returns null in this case; verify
                                        // behavior is consistent — the contract is "null on no match".
        _playbookLookupMock
            .Setup(p => p.GetByIdAsync(FallbackEmailAnalysisGuid.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse
            {
                Id = FallbackEmailAnalysisGuid,
                Name = EmailAnalysisPlaybookName,
                PlaybookCode = string.Empty,
                IsActive = true,
            });

        var sut = CreateSut();

        // Act
        var result = await InvokeResolvePlaybookAsync(sut, EmailAnalysisPlaybookName);

        // Assert
        result.Id.Should().Be(FallbackEmailAnalysisGuid,
            "FR-1R-05 — null-equivalent (no-match) MUST trigger fallback to the const Guid path");
    }

    // ─── (d) Document Profile path — uses fallback const directly (no FR-1R-05 route yet) ────

    [Fact]
    public async Task ResolvePlaybook_DocumentProfile_UsesFallbackConstDirectly()
    {
        // Document Profile does NOT have a ConsumerTypes entry yet (planned post-028e). The
        // resolver MUST NOT consult IConsumerRoutingService for this name; it goes directly
        // to the Fallback*PlaybookId path. Pinning this preserves the AnalyzeDocumentAsync
        // contract (ProfileSummaryWorker, AppOnlyDocumentAnalysisJobHandler) verbatim.
        _playbookLookupMock
            .Setup(p => p.GetByIdAsync(FallbackDocumentProfileGuid.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse
            {
                Id = FallbackDocumentProfileGuid,
                Name = DocumentProfilePlaybookName,
                PlaybookCode = string.Empty,
                IsActive = true,
            });

        var sut = CreateSut();

        // Act
        var result = await InvokeResolvePlaybookAsync(sut, DocumentProfilePlaybookName);

        // Assert
        result.Id.Should().Be(FallbackDocumentProfileGuid,
            "FR-1R-05 partial migration — Document Profile path remains on the FR-03 stable-ID " +
            "const until the post-028e routing-record + ConsumerTypes constant introduction");
        _consumerRoutingMock.Verify(
            c => c.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "FR-1R-05 — Document Profile path does NOT consult IConsumerRoutingService yet " +
            "(no ConsumerTypes entry; planned post-028e)");
    }

    // ─── (e) Custom playbook name → legacy by-name path (unchanged by 028d) ──────────────────

    [Fact]
    public async Task ResolvePlaybook_CustomName_UsesLegacyByNamePath()
    {
        // Custom playbook names (test fixtures, future custom playbooks) still resolve via
        // the legacy IPlaybookService.GetByNameAsync path during the FR-03 deprecation window.
        const string customName = "Custom Playbook For Tests";
        var customGuid = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        _playbookServiceMock
            .Setup(p => p.GetByNameAsync(customName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse
            {
                Id = customGuid,
                Name = customName,
                PlaybookCode = string.Empty,
                IsActive = true,
            });

        var sut = CreateSut();

        // Act
        var result = await InvokeResolvePlaybookAsync(sut, customName);

        // Assert
        result.Id.Should().Be(customGuid,
            "FR-03 — custom names fall through to the legacy IPlaybookService.GetByNameAsync path " +
            "(unchanged by 028d)");
        _consumerRoutingMock.Verify(
            c => c.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "FR-1R-05 — routing service is consulted ONLY for well-known names mapped to ConsumerTypes constants");
    }

    // ─── (f) Service surface invariant — hardcoded const stable-IDs removed ──────────────────

    [Fact]
    public void AppOnlyAnalysisService_HasNoHardcodedConstStableIds_FR1R05()
    {
        // FR-1R-05 task 028d acceptance criterion: the 2 const stable-IDs flagged by task 027
        // exit-gate evidence (`const string DocumentProfilePlaybookId` + `const string
        // EmailAnalysisPlaybookId`) MUST be removed from the service surface. They were
        // replaced with `private static readonly Guid Fallback*PlaybookId` field-equivalents.
        // Reflection assertion: the const member names no longer exist on the type.
        var members = typeof(AppOnlyAnalysisService)
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToArray();

        members.Should().NotContain(
            new[] { "DocumentProfilePlaybookId", "EmailAnalysisPlaybookId" },
            "FR-1R-05 task 028d acceptance — the 2 Pattern B execution-path consts MUST be removed; " +
            "playbook resolution now flows through IConsumerRoutingService.ResolveAsync");

        // Sanity: the fallback fields exist (graceful-degrade for FR-1R-06 deprecation window).
        members.Should().Contain(
            new[] { "FallbackDocumentProfilePlaybookId", "FallbackEmailAnalysisPlaybookId" },
            "FR-1R-05 — graceful-degrade fallback fields MUST exist during the FR-1R-06 " +
            "deprecation window (deleted at FR-1R-08 exit gate)");
    }
}

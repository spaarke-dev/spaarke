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
/// FR-P3-01 hard cutover (spaarke-ai-architecture-redesign-r1 task 040).
///
/// <para>
/// The FR-1R-05 graceful-degrade fallback GUIDs (<c>FallbackDocumentProfilePlaybookId</c> /
/// <c>FallbackEmailAnalysisPlaybookId</c>) were DELETED. Both well-known playbook names
/// ("Document Profile", "Email Analysis") now resolve exclusively through
/// <see cref="IConsumerRoutingService.ResolveAsync"/> against the <c>sprk_playbookconsumer</c>
/// Binding table; a null routing result is a hard <see cref="InvalidOperationException"/>
/// (single routing surface per ADR-039 / NFR-08 — no shims, no fallback).
/// </para>
///
/// <para>
/// These tests target the private <c>ResolvePlaybookAsync</c> method via reflection — the
/// single resolution point in the service (both <c>AnalyzeDocumentAsync</c> and
/// <c>ExecutePlaybookAnalysisAsync</c> delegate to it). Reflection is the pre-existing seam
/// (kept from the 028d suite) because the public methods drag in the full
/// Dataverse/SPE/text-extraction pipeline.
/// </para>
/// </summary>
public class AppOnlyAnalysisServiceResolveTests
{
    private const string EmailAnalysisPlaybookName = "Email Analysis";
    private const string DocumentProfilePlaybookName = "Document Profile";

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

    private static async Task<PlaybookResponse> InvokeResolvePlaybookAsync(
        AppOnlyAnalysisService sut,
        string playbookName,
        CancellationToken ct = default)
    {
        var method = typeof(AppOnlyAnalysisService).GetMethod(
            "ResolvePlaybookAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull("ResolvePlaybookAsync is the single resolution point and " +
            "MUST remain a private method on AppOnlyAnalysisService");
        try
        {
            return await (Task<PlaybookResponse>)method!.Invoke(sut, new object?[] { playbookName, ct })!;
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            // Unwrap so tests can assert on the service's own exception type.
            throw tie.InnerException;
        }
    }

    private void SetupRouting(string consumerType, Guid? result)
    {
        _consumerRoutingMock
            .Setup(c => c.ResolveAsync(
                consumerType,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }

    private void SetupLookup(Guid playbookId, string name)
    {
        _playbookLookupMock
            .Setup(p => p.GetByIdAsync(playbookId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse
            {
                Id = playbookId,
                Name = name,
                PlaybookCode = string.Empty,
                IsActive = true,
            });
    }

    // ─── (a) FR-P3-01 happy path — routing table resolves → routed GUID used ────────────────

    [Fact]
    public async Task ResolvePlaybook_EmailAnalysis_RoutingResolves_UsesRoutedGuid()
    {
        var routedGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        SetupRouting(ConsumerTypes.EmailAnalysis, routedGuid);
        SetupLookup(routedGuid, EmailAnalysisPlaybookName);

        var sut = CreateSut();

        var result = await InvokeResolvePlaybookAsync(sut, EmailAnalysisPlaybookName);

        result.Id.Should().Be(routedGuid,
            "FR-P3-01 — the Binding-routed GUID MUST be forwarded to IPlaybookLookupService");
        _consumerRoutingMock.Verify(
            c => c.ResolveAsync(
                ConsumerTypes.EmailAnalysis,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "consumer type MUST be the ConsumerTypes.EmailAnalysis constant " +
            "(compile-time typo defense, code-review S-5), NOT a literal string");
        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(routedGuid.ToString(), It.IsAny<CancellationToken>()),
            Times.Once,
            "the routed GUID is materialized via the existing IPlaybookLookupService " +
            "(single-cache discipline; no duplicate cache layer)");
        _playbookServiceMock.Verify(
            p => p.GetByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "well-known names MUST NOT fall through to the legacy by-name path");
    }

    [Fact]
    public async Task ResolvePlaybook_DocumentProfile_RoutingResolves_UsesRoutedGuid()
    {
        // FR-P3-01: Document Profile now routes through the Binding table too — the FR-1R-05
        // "no ConsumerTypes entry yet" carve-out (which read a hardcoded fallback GUID) is gone.
        var routedGuid = Guid.Parse("18cf3cc8-02ec-f011-8406-7c1e520aa4df");
        SetupRouting(ConsumerTypes.DocumentProfile, routedGuid);
        SetupLookup(routedGuid, DocumentProfilePlaybookName);

        var sut = CreateSut();

        var result = await InvokeResolvePlaybookAsync(sut, DocumentProfilePlaybookName);

        result.Id.Should().Be(routedGuid,
            "FR-P3-01 — 'Document Profile' resolves via ResolveAsync(ConsumerTypes.DocumentProfile), " +
            "not via a hardcoded stable-ID const");
        _consumerRoutingMock.Verify(
            c => c.ResolveAsync(
                ConsumerTypes.DocumentProfile,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(routedGuid.ToString(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── (b) FR-P3-01 hard cutover — routing null → InvalidOperationException ───────────────

    [Fact]
    public async Task ResolvePlaybook_EmailAnalysis_RoutingReturnsNull_ThrowsInvalidOperation()
    {
        SetupRouting(ConsumerTypes.EmailAnalysis, null);

        var sut = CreateSut();

        var act = () => InvokeResolvePlaybookAsync(sut, EmailAnalysisPlaybookName);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>(
            "FR-P3-01 hard cutover — a missing Binding row is a hard error, not a graceful " +
            "degrade to a hardcoded GUID");
        ex.Which.Message.Should().Contain("email-analysis")
            .And.Contain("sprk_playbookconsumer");
        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no playbook lookup may occur when routing does not resolve — the fallback GUID is gone");
    }

    [Fact]
    public async Task ResolvePlaybook_DocumentProfile_RoutingReturnsNull_ThrowsInvalidOperation()
    {
        SetupRouting(ConsumerTypes.DocumentProfile, null);

        var sut = CreateSut();

        var act = () => InvokeResolvePlaybookAsync(sut, DocumentProfilePlaybookName);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("document-profile")
            .And.Contain("sprk_playbookconsumer");
        _playbookLookupMock.Verify(
            p => p.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── (c) Defensive edge — Guid.Empty from routing treated as no-match ───────────────────

    [Fact]
    public async Task ResolvePlaybook_EmailAnalysis_RoutingReturnsEmptyGuid_ThrowsInvalidOperation()
    {
        SetupRouting(ConsumerTypes.EmailAnalysis, Guid.Empty);

        var sut = CreateSut();

        var act = () => InvokeResolvePlaybookAsync(sut, EmailAnalysisPlaybookName);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "Guid.Empty is a no-match sentinel and MUST be treated like null (hard error)");
    }

    // ─── (d) Custom playbook name → legacy by-name path (unchanged) ─────────────────────────

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

        var result = await InvokeResolvePlaybookAsync(sut, customName);

        result.Id.Should().Be(customGuid,
            "FR-03 — custom names fall through to the legacy IPlaybookService.GetByNameAsync path");
        _consumerRoutingMock.Verify(
            c => c.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "the routing service is consulted ONLY for well-known names mapped to ConsumerTypes constants");
    }
}

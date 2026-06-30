using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Unit tests for <see cref="ComposeDocumentService"/> — the SPE plumbing seam for Compose
/// (load + save DOCX bytes via Microsoft Graph against a SPE drive-item).
///
/// <para>
/// <b>ADR-038 KEEP category</b>: <c>domain-logic</c> (argument validation + R1 stub
/// behavior). The wider SPE round-trip (Graph SDK chain → drive-item read/write) is
/// deliberately NOT unit-tested per ADR-038 §4 mock-boundary rule:
/// <list type="bullet">
///   <item>The only mockable seam is <see cref="IGraphClientFactory.ForUserAsync"/>, which
///   returns the concrete <c>GraphServiceClient</c>. Mocking the subsequent
///   <c>graphClient.Drives[].Items[].GetAsync()</c> / <c>.Content.GetAsync()</c> /
///   <c>.Content.PutAsync()</c> chain requires either
///   <see cref="System.Net.Http.HttpMessageHandler"/> mocks (B1, BANNED) or
///   <c>IRequestAdapter</c> mocks (B2, BANNED — same wire-format antipattern hidden).</item>
///   <item>The honest replacement per <c>docs/standards/TEST-ARCHITECTURE.md</c> §5 row 1
///   "Banned 1 alternative" is an endpoint integration test (task 027 +
///   <c>tests/integration/contract/Api/ComposeEndpointsContractTests.cs</c>) that exercises
///   the full SPE round-trip through <c>WebApplicationFactory&lt;Program&gt;</c> with the
///   Graph boundary stubbed at <see cref="IGraphClientFactory"/>. That test owns the
///   Load/Save behavioral coverage; this unit test owns only argument validation +
///   R1-stub semantics.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Path A documented deferral</b> (per CLAUDE.md §6.5): the POML asked for full
/// behavioral coverage of Load/Save. Strict compliance would require B1/B2-banned mocks.
/// We pivot to Path A: documented project-scoped narrowing — argument-validation behavior
/// tests here, full SPE round-trip coverage in <c>ComposeEndpointsContractTests</c>
/// (task 027, KEEP category <c>endpoint-contract</c>). The same architectural-justified
/// narrowing was applied at the W2 dispatch (per <c>current-task.md</c> archive entry for
/// task 022: "R1 stubs for checkout methods (Phase 5 wires)" + "pure Graph-SDK wrappers
/// fail 'what production behavior breaks if deleted'"). Documented here for traceability.
/// </para>
///
/// <para>
/// <b>Banned-pattern audit</b>: no <c>Mock&lt;HttpMessageHandler&gt;</c> (B1). No
/// <c>Mock&lt;IServiceClient&gt;</c> (B2). No DI-registration tests (B3). No constructor
/// null-check tests (B4) — argument validation tests assert the BEHAVIOR a caller observes
/// (an explicit <see cref="ArgumentException"/> with the parameter name), not the
/// <c>ThrowIfNull</c> guard pattern itself. No mirror tests (B6). No all-mocks +
/// trivial-assertion (B7). No private-method tests (B8). All test names follow
/// <c>{Method}_{Scenario}_{ExpectedResult}</c> (B13 compliant).
/// </para>
/// </summary>
public class ComposeDocumentServiceTests
{
    private const string DriveId = "drive-aaa";
    private const string ItemId = "item-bbb";
    private const string CorrelationId = "corr-test-doc-svc";

    private readonly Mock<IGraphClientFactory> _graphFactoryMock;
    private readonly Mock<ILogger<ComposeDocumentService>> _loggerMock;
    private readonly ComposeDocumentService _sut;

    public ComposeDocumentServiceTests()
    {
        _graphFactoryMock = new Mock<IGraphClientFactory>();
        _loggerMock = new Mock<ILogger<ComposeDocumentService>>();

        _sut = new ComposeDocumentService(_graphFactoryMock.Object, _loggerMock.Object);
    }

    // =========================================================================
    // LoadDocxAsync — argument-validation behavior contract
    // =========================================================================

    [Fact]
    public async Task LoadDocxAsync_WhenDriveIdIsEmpty_ThrowsArgumentExceptionNamingTheParameter()
    {
        // Arrange — caller mistakenly passes empty drive id; SUT MUST surface a clear error
        // BEFORE making any Graph call (Graph 400 would be misleading and rate-limit-able).
        var httpContext = new DefaultHttpContext();

        // Act
        var act = () => _sut.LoadDocxAsync(httpContext, driveId: "", itemId: ItemId);

        // Assert — observable behavior: explicit ArgumentException with the parameter name
        var ex = await act.Should().ThrowAsync<ArgumentException>();
        ex.Which.ParamName.Should().Be("driveId");

        _graphFactoryMock.Verify(
            f => f.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "validation MUST short-circuit BEFORE the OBO exchange — failed validation is not a Graph problem");
    }

    [Fact]
    public async Task LoadDocxAsync_WhenItemIdIsEmpty_ThrowsArgumentExceptionNamingTheParameter()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();

        // Act
        var act = () => _sut.LoadDocxAsync(httpContext, driveId: DriveId, itemId: "");

        // Assert
        var ex = await act.Should().ThrowAsync<ArgumentException>();
        ex.Which.ParamName.Should().Be("itemId");

        _graphFactoryMock.Verify(
            f => f.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LoadDocxAsync_WhenItemIdIsWhitespace_ThrowsArgumentExceptionNamingTheParameter()
    {
        // Arrange — whitespace-only id MUST be rejected (otherwise SPE returns a confusing 404
        // and the user sees "not found" for a problem that's actually input validation).
        var httpContext = new DefaultHttpContext();

        // Act
        var act = () => _sut.LoadDocxAsync(httpContext, driveId: DriveId, itemId: "   ");

        // Assert
        var ex = await act.Should().ThrowAsync<ArgumentException>();
        ex.Which.ParamName.Should().Be("itemId");
    }

    // =========================================================================
    // SaveDocxAsync — argument-validation behavior contract
    // =========================================================================

    [Fact]
    public async Task SaveDocxAsync_WhenDriveIdIsEmpty_ThrowsArgumentExceptionNamingTheParameter()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        using var content = new MemoryStream(new byte[] { 0x50, 0x4B });
        var user = BuildPrincipal();

        // Act
        var act = () => _sut.SaveDocxAsync(httpContext, driveId: "", itemId: ItemId,
            content: content, user: user, correlationId: CorrelationId);

        // Assert
        var ex = await act.Should().ThrowAsync<ArgumentException>();
        ex.Which.ParamName.Should().Be("driveId");

        _graphFactoryMock.Verify(
            f => f.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "FR-04 + writer-identity rule: invalid driveId MUST NOT consume an OBO token");
    }

    [Fact]
    public async Task SaveDocxAsync_WhenCorrelationIdIsEmpty_ThrowsArgumentExceptionNamingTheParameter()
    {
        // Arrange — correlationId is binding per logging contract (the same id stitches Graph
        // + ChatSession + Dataverse logs together). Missing correlation = unobservable race
        // conditions in production. Validation MUST trip BEFORE we burn an OBO token.
        var httpContext = new DefaultHttpContext();
        using var content = new MemoryStream(new byte[] { 0x50, 0x4B });
        var user = BuildPrincipal();

        // Act
        var act = () => _sut.SaveDocxAsync(httpContext, driveId: DriveId, itemId: ItemId,
            content: content, user: user, correlationId: "");

        // Assert
        var ex = await act.Should().ThrowAsync<ArgumentException>();
        ex.Which.ParamName.Should().Be("correlationId");
    }

    // =========================================================================
    // R1 stub semantics — Phase 5 reuse-pivot scaffolding (per spike #3 §2.4)
    // =========================================================================
    //
    // The check-out methods are intentionally R1 stubs (per spike #3 §2.4 + the
    // IComposeDocumentService XML doc). Phase 5 task 050 wires the React layer directly to
    // the existing DocumentCheckoutService endpoints. Tests below assert the R1 STUB
    // CONTRACT (NotImplementedException with a message that points the implementer at the
    // right Phase 5 task) — this is observable behavior: a Phase 5 implementer who removes
    // the stub WITHOUT replacing the surface will break these tests, signaling the contract
    // change.
    //
    // These tests survive the "what production behavior breaks if deleted?" question (ADR-038
    // §7) — without them, a Phase 5 refactor could silently turn the stubs into no-ops,
    // letting a caller mis-believe a checkout was acquired. The tests pin the loud failure.

    [Fact]
    public async Task AcquireCheckOutAsync_InR1_ThrowsNotImplementedExceptionPointingToPhase5()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var user = BuildPrincipal();

        // Act
        var act = () => _sut.AcquireCheckOutAsync(documentId, user);

        // Assert — Phase 5 task 050 is the documented reuse path
        var ex = await act.Should().ThrowAsync<NotImplementedException>();
        ex.Which.Message.Should().Contain("Phase 5", "the stub message MUST direct the Phase 5 implementer");
        ex.Which.Message.Should().Contain("DocumentCheckoutService",
            "the stub message MUST name the existing service to reuse (per spike #3 §2.4)");
    }

    [Fact]
    public async Task ReleaseCheckOutAsync_InR1_ThrowsNotImplementedExceptionPointingToPhase5()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var user = BuildPrincipal();

        // Act
        var act = () => _sut.ReleaseCheckOutAsync(documentId, user);

        // Assert — symmetric contract to Acquire (R1 stub → Phase 5 reuse pivot)
        var ex = await act.Should().ThrowAsync<NotImplementedException>();
        ex.Which.Message.Should().Contain("Phase 5");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static System.Security.Claims.ClaimsPrincipal BuildPrincipal()
    {
        var identity = new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("sub", "user-test"),
            new System.Security.Claims.Claim("tid", "tenant-test"),
        }, authenticationType: "Bearer");
        return new System.Security.Claims.ClaimsPrincipal(identity);
    }
}

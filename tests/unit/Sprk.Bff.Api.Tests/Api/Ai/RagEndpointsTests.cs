using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Endpoint-level integration-style tests for <see cref="RagEndpoints"/> covering the
/// FR-BFF-07 (multi-container-multi-index-r1) wiring of <c>RagSearchRequest.SearchIndexName</c>
/// from the request DTO into <see cref="RagSearchOptions.SearchIndexName"/> before the
/// service call, plus ProblemDetails 400 propagation for <see cref="SdapProblemException"/>
/// per ADR-019 / NFR-08.
/// </summary>
/// <remarks>
/// <para>
/// Invokes the private static <c>Search</c> handler directly via reflection (mirroring
/// the ASP.NET Minimal API invocation contract). Mocks <see cref="IRagService"/> and
/// captures the <see cref="RagSearchOptions"/> arg passed to <c>SearchAsync</c> so the
/// thread-through is asserted at the boundary that actually matters — what reaches
/// the service.
/// </para>
/// <para>
/// Scope: keeps the assertion surface small (3 tests) because service-level tests in
/// <c>tests/unit/Sprk.Bff.Api.Tests/Services/Ai/RagServiceTests.cs</c> already cover
/// the resolver routing (task 014). These tests cover ONLY the endpoint-boundary
/// behavior tasks 010/013/014/015 did not exercise: the DTO → options merge.
/// </para>
/// </remarks>
public sealed class RagEndpointsTests
{
    private const string MethodName = "Search";
    private const string TenantId = "00000000-0000-0000-0000-000000000001";

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Invokes the private static <c>RagEndpoints.Search</c> handler via reflection,
    /// matching the parameter order <c>(RagSearchRequest, IRagService, CancellationToken)</c>.
    /// </summary>
    private static async Task<IResult> InvokeSearchAsync(
        RagSearchRequest request,
        IRagService ragService,
        CancellationToken cancellationToken = default)
    {
        var method = typeof(RagEndpoints)
            .GetMethod(MethodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"Handler '{MethodName}' not found on RagEndpoints via reflection — signature change?");

        var task = (Task<IResult>)method.Invoke(null, new object[] { request, ragService, cancellationToken })!;
        return await task;
    }

    private static RagSearchRequest MakeRequest(string? searchIndexName)
        => new RagSearchRequest
        {
            Query = "what is a matter",
            Options = new RagSearchOptions
            {
                TenantId = TenantId,
                TopK = 5
            },
            SearchIndexName = searchIndexName
        };

    private static RagSearchResponse EmptyResponse()
        => new RagSearchResponse
        {
            Query = "what is a matter",
            Results = Array.Empty<RagSearchResult>(),
            TotalCount = 0
        };

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// FR-BFF-07: when the client includes <c>searchIndexName</c> at the top level of
    /// <see cref="RagSearchRequest"/>, the endpoint MUST copy that value into
    /// <see cref="RagSearchOptions.SearchIndexName"/> before invoking
    /// <c>IRagService.SearchAsync(query, options, ct)</c>. The service-side conditional
    /// dispatch (task 014) then routes through the resolver overload.
    /// </summary>
    [Fact]
    public async Task Search_WithSearchIndexNameOnRequest_PropagatesValueIntoOptionsBeforeServiceCall()
    {
        // Arrange
        const string explicitIndex = "spaarke-file-index";
        RagSearchOptions? captured = null;
        var ragServiceMock = new Mock<IRagService>(MockBehavior.Strict);
        ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagSearchOptions, CancellationToken>((_, opts, _) => captured = opts)
            .ReturnsAsync(EmptyResponse());

        // Act
        var result = await InvokeSearchAsync(MakeRequest(searchIndexName: explicitIndex), ragServiceMock.Object);

        // Assert
        result.Should().BeOfType<Ok<RagSearchResponse>>();
        captured.Should().NotBeNull();
        captured!.SearchIndexName.Should().Be(explicitIndex,
            "the endpoint MUST copy request.SearchIndexName into options.SearchIndexName so " +
            "RagService routes via the 3-arg resolver overload (FR-BFF-07 task 014 + 016).");
        captured.TenantId.Should().Be(TenantId, "other fields on the supplied options object MUST be preserved.");
    }

    /// <summary>
    /// NFR-02 backward-compat: when the client does NOT include <c>searchIndexName</c>
    /// (null or whitespace), the endpoint MUST pass the original <see cref="RagSearchOptions"/>
    /// instance through unchanged. The 'with' expression branch MUST NOT execute. All
    /// existing callers continue to work byte-for-byte.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_WithoutSearchIndexNameOnRequest_PassesOriginalOptionsVerbatim(string? input)
    {
        // Arrange
        RagSearchOptions? captured = null;
        var ragServiceMock = new Mock<IRagService>(MockBehavior.Strict);
        ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagSearchOptions, CancellationToken>((_, opts, _) => captured = opts)
            .ReturnsAsync(EmptyResponse());

        var request = MakeRequest(searchIndexName: input);

        // Act
        var result = await InvokeSearchAsync(request, ragServiceMock.Object);

        // Assert
        result.Should().BeOfType<Ok<RagSearchResponse>>();
        captured.Should().NotBeNull();
        captured!.SearchIndexName.Should().BeNullOrWhiteSpace(
            "when request.SearchIndexName is null/whitespace, the endpoint MUST NOT introduce a non-empty value " +
            "into the options (NFR-02 backward-compat — existing 2-tier resolver chain must remain unchanged).");
        ReferenceEquals(captured, request.Options).Should().BeTrue(
            "when SearchIndexName is null/whitespace, the endpoint should pass the caller's options reference verbatim " +
            "(no defensive copy) to keep the hot path allocation-free.");
    }

    /// <summary>
    /// NFR-08 / ADR-019: when the resolver throws <see cref="SdapProblemException"/> with
    /// <c>INDEX_NOT_ALLOWED</c> (statusCode 400), the endpoint MUST rethrow rather than
    /// converting it into a generic 500. The global <c>UseExceptionHandler</c> middleware
    /// (MiddlewarePipelineExtensions) then renders the canonical ProblemDetails 400 JSON.
    /// </summary>
    [Fact]
    public async Task Search_WhenResolverThrowsIndexNotAllowed_RethrowsForGlobalProblemDetailsMiddleware()
    {
        // Arrange
        var rejected = new SdapProblemException(
            code: "INDEX_NOT_ALLOWED",
            title: "AI Search index not allowed",
            detail: "The requested AI Search index 'evil-index' is not in the configured allow-list.",
            statusCode: 400,
            extensions: new Dictionary<string, object> { ["rejectedIndexName"] = "evil-index" });

        var ragServiceMock = new Mock<IRagService>(MockBehavior.Strict);
        ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(rejected);

        // Act
        var act = () => InvokeSearchAsync(MakeRequest(searchIndexName: "evil-index"), ragServiceMock.Object);

        // Assert — the SdapProblemException MUST escape the handler verbatim so the global
        // exception middleware (UseExceptionHandler) can render the canonical 400 ProblemDetails.
        var thrown = await act.Should().ThrowAsync<SdapProblemException>();
        thrown.Which.Code.Should().Be("INDEX_NOT_ALLOWED");
        thrown.Which.StatusCode.Should().Be(400, "ADR-019 + NFR-08: invalid index name MUST surface as ProblemDetails 400.");
        thrown.Which.Extensions.Should().ContainKey("rejectedIndexName");
    }
}

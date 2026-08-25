using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Errors;

/// <summary>
/// Pure-logic tests (ADR-038 §2 path #6 — no mocks, no DI, no I/O) for the SPE Admin error surface
/// introduced by <c>sdap-SPE-admin-app-r2</c> task 001 (spec FR-A01).
/// </summary>
/// <remarks>
/// <para>
/// What breaks if these are deleted: (1) a Graph or Dataverse message containing a token-shaped substring
/// would reach an admin's browser unredacted — the surface only became reachable because task 001 started
/// putting real upstream text in responses; (2) the Graph request id would silently stop flowing, and its
/// absence is invisible until an operator needs it for a Microsoft support case; (3) an upstream Graph 401
/// would start propagating verbatim again, where the client's <c>authenticatedFetch</c> retry loop swallows
/// it and replaces the real error with a generic auth failure — re-creating the exact defect the project
/// exists to fix.
/// </para>
/// <para>
/// These assert observable payload content and mapping behavior, not implementation shape.
/// </para>
/// </remarks>
public class ErrorSurfaceTests
{
    private static async Task<(int Status, JsonElement Body)> ExecuteAsync(IResult result)
    {
        var ctx = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;
        await result.ExecuteAsync(ctx);

        var json = Encoding.UTF8.GetString(buffer.ToArray());
        return (ctx.Response.StatusCode, JsonDocument.Parse(json).RootElement.Clone());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Redact — secret VALUES never reach a payload; secret NAMES survive
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Redact_WhenMessageContainsBearerToken_RemovesTheTokenValue()
    {
        const string message = "Request failed. Authorization: Bearer abc123DEF456ghi789JKL.mno-pqr_stu";

        var redacted = ProblemDetailsHelper.Redact(message);

        redacted.Should().NotContain("abc123DEF456ghi789JKL");
        redacted.Should().Contain("[redacted]");
    }

    [Fact]
    public void Redact_WhenMessageContainsJwt_RemovesTheTokenValue()
    {
        const string message =
            "token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U was rejected";

        var redacted = ProblemDetailsHelper.Redact(message);

        redacted.Should().NotContain("eyJhbGciOiJIUzI1NiJ9");
        redacted.Should().Contain("[redacted-token]");
    }

    [Theory]
    [InlineData("client_secret=s3cr3tV4lu3", "s3cr3tV4lu3")]
    [InlineData("\"access_token\":\"ya29.A0ARrdaM-value\"", "ya29.A0ARrdaM-value")]
    [InlineData("password = hunter2", "hunter2")]
    public void Redact_WhenMessageAssignsASecret_RemovesTheValueButKeepsTheKey(string message, string secretValue)
    {
        var redacted = ProblemDetailsHelper.Redact(message);

        redacted.Should().NotContain(secretValue);
        redacted.Should().Contain("[redacted]");
    }

    [Fact]
    public void Redact_WhenMessageNamesAKeyVaultSecret_KeepsTheNameSoItStaysDiagnostic()
    {
        // The NAME is exactly what an admin needs in order to fix a misconfiguration.
        const string message = "Secret 'spe-owning-app-secret' was not found in vault 'kv-spaarke-dev'.";

        var redacted = ProblemDetailsHelper.Redact(message);

        redacted.Should().Contain("spe-owning-app-secret");
        redacted.Should().Contain("kv-spaarke-dev");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Redact_WhenMessageIsAbsent_ReturnsItUnchanged(string? message)
    {
        ProblemDetailsHelper.Redact(message).Should().Be(message);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Explain — the summary keeps its wording, the real cause is appended
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Explain_WhenExceptionHasAMessage_AppendsTypeAndMessageToTheSummary()
    {
        var result = ProblemDetailsHelper.Explain(
            "An unexpected error occurred while listing containers.",
            new InvalidOperationException("Key Vault reference could not be resolved."));

        result.Should().StartWith("An unexpected error occurred while listing containers.");
        result.Should().Contain("InvalidOperationException");
        result.Should().Contain("Key Vault reference could not be resolved.");
    }

    [Fact]
    public void Explain_WhenExceptionMessageContainsASecret_RedactsBeforeAppending()
    {
        var result = ProblemDetailsHelper.Explain(
            "Sign-in failed.",
            new InvalidOperationException("used client_secret=leakMeIfYouCan"));

        result.Should().NotContain("leakMeIfYouCan");
    }

    [Fact]
    public void Explain_WhenExceptionMessageIsEmpty_StillNamesTheExceptionType()
    {
        var result = ProblemDetailsHelper.Explain("Could not list environments.", new TimeoutException(""));

        result.Should().Contain("TimeoutException");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ExtractRequestId — the value an operator quotes to Microsoft support
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractRequestId_WhenInnerErrorCarriesRequestId_ReturnsIt()
    {
        var error = new ODataError
        {
            Error = new MainError
            {
                Code = "itemNotFound",
                InnerError = new InnerError { RequestId = "req-1111-2222" }
            }
        };

        GraphErrorTranslator.ExtractRequestId(error).Should().Be("req-1111-2222");
    }

    [Fact]
    public void ExtractRequestId_WhenOnlyClientRequestIdIsPresent_FallsBackToIt()
    {
        var error = new ODataError
        {
            Error = new MainError
            {
                Code = "itemNotFound",
                InnerError = new InnerError { ClientRequestId = "client-3333" }
            }
        };

        GraphErrorTranslator.ExtractRequestId(error).Should().Be("client-3333");
    }

    [Fact]
    public void ExtractRequestId_WhenBodyHasNoInnerError_FallsBackToTheResponseHeader()
    {
        var error = new ODataError { Error = new MainError { Code = "serviceNotAvailable" } };
        error.ResponseHeaders["request-id"] = new List<string> { "hdr-4444" };

        GraphErrorTranslator.ExtractRequestId(error).Should().Be("hdr-4444");
    }

    [Fact]
    public void ExtractRequestId_WhenGraphReportsNoIdAnywhere_ReturnsNull()
    {
        var error = new ODataError { Error = new MainError { Code = "unknownError" } };

        GraphErrorTranslator.ExtractRequestId(error).Should().BeNull();
    }

    [Fact]
    public void ToSpaarkeStorageException_WhenGraphReportsARequestId_CarriesItOntoTheDomainException()
    {
        var error = new ODataError
        {
            ResponseStatusCode = 403,
            Error = new MainError
            {
                Code = "accessDenied",
                Message = "Access denied",
                InnerError = new InnerError { RequestId = "req-5555" }
            }
        };

        var translated = error.ToSpaarkeStorageException("ListContainerTypes");

        translated.GraphRequestId.Should().Be("req-5555");
        translated.ErrorCode.Should().Be("accessDenied");
        translated.StatusCode.Should().Be(403);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ClientStatusFor — an upstream 401 must not look like the caller's 401
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ClientStatusFor_WhenGraphReturns401_Returns502SoTheClientRetryLoopCannotSwallowIt()
    {
        var ex = new SpaarkeStorageException("unauthorized", statusCode: 401);

        ex.ClientStatusFor().Should().Be(StatusCodes.Status502BadGateway);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(503)]
    public void ClientStatusFor_WhenGraphReturnsAnyOther4xxOr5xx_PassesItThrough(int upstream)
    {
        var ex = new SpaarkeStorageException("failed", statusCode: upstream);

        ex.ClientStatusFor().Should().Be(upstream);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(200)]
    public void ClientStatusFor_WhenUpstreamStatusIsUnknown_Returns502(int? upstream)
    {
        var ex = new SpaarkeStorageException("failed", statusCode: upstream);

        ex.ClientStatusFor().Should().Be(StatusCodes.Status502BadGateway);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ToProblemDetails — reports what Graph said, asserts no cause of its own
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ToProblemDetails_WhenGraphFails_PayloadCarriesCodeMessageAndRequestId()
    {
        var ex = new SpaarkeStorageException(
            "ListContainerTypes: Application permissions are not supported on this API.",
            statusCode: 403,
            errorCode: "accessDenied",
            graphRequestId: "req-6666");

        var (status, body) = await ExecuteAsync(ex.ToProblemDetails(
            summary: "Could not retrieve container types.",
            errorCode: "spe.containertypes.graph_error",
            statusCode: StatusCodes.Status500InternalServerError,
            traceId: "trace-7777"));

        status.Should().Be(500);
        body.GetProperty("detail").GetString().Should()
            .Contain("Could not retrieve container types.")
            .And.Contain("accessDenied")
            .And.Contain("Application permissions are not supported on this API.");
        body.GetProperty("graphErrorCode").GetString().Should().Be("accessDenied");
        body.GetProperty("graphRequestId").GetString().Should().Be("req-6666");
        body.GetProperty("graphStatusCode").GetInt32().Should().Be(403);
        body.GetProperty("traceId").GetString().Should().Be("trace-7777");
        body.GetProperty("errorCode").GetString().Should().Be("spe.containertypes.graph_error");
    }

    [Fact]
    public async Task ToProblemDetails_WhenGraphFails_DoesNotTellTheAdminToCheckCredentials()
    {
        // The regression this whole project exists to prevent: the Container Types screen told admins to
        // check credentials that the Containers screen was using successfully at the same moment.
        var ex = new SpaarkeStorageException(
            "Application permissions are not supported on this API.",
            statusCode: 403,
            errorCode: "accessDenied");

        var (_, body) = await ExecuteAsync(ex.ToProblemDetails(
            summary: "Could not retrieve container types.",
            errorCode: "spe.containertypes.graph_error"));

        body.GetProperty("detail").GetString().Should()
            .NotContain("Check the app registration credentials");
    }

    [Fact]
    public async Task ToProblemDetails_WhenGraphMessageContainsASecret_RedactsItFromThePayload()
    {
        var ex = new SpaarkeStorageException(
            "auth failed for Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.signature",
            statusCode: 401,
            errorCode: "InvalidAuthenticationToken");

        var (_, body) = await ExecuteAsync(ex.ToProblemDetails(
            summary: "Could not list containers.",
            errorCode: "spe.containers.graph_error"));

        body.GetProperty("detail").GetString().Should().NotContain("eyJhbGciOiJIUzI1NiJ9");
    }

    [Fact]
    public async Task ToProblemDetails_WhenGraphReportsNothingUseful_StillReturnsTheSummaryAlone()
    {
        var ex = new SpaarkeStorageException(string.Empty, statusCode: 500);

        var (_, body) = await ExecuteAsync(ex.ToProblemDetails(
            summary: "Could not retrieve container types.",
            errorCode: "spe.containertypes.graph_error"));

        body.GetProperty("detail").GetString().Should().Be("Could not retrieve container types.");
    }
}

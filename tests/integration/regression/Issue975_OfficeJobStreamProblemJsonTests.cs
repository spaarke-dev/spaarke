// Regression anchor for GitHub issue #975 (spaarkeai-word-add-in-r1 task 052 — two of the three sites
// task 050's blast-radius survey found but was scoped out of; see
// projects/spaarkeai-word-add-in-r1/notes/050-problem-json-content-type.md §5). Same defect class as
// Issue975_ProblemJsonContentTypeTests.cs (task 050) and the sibling
// Issue975_ChatAttachmentValidationProblemJsonTests.cs (task 052): both sites inside
// OfficeEndpoints.GetJobStatusStreamAsync set `context.Response.ContentType =
// "application/problem+json"` and then call `context.Response.WriteAsJsonAsync(new {...},
// cancellationToken)` with NO explicit content-type argument. WriteAsJsonAsync's convenience overload
// unconditionally overwrites ContentType with "application/json; charset=utf-8", contrary to ADR-019.
//
// SSE JUDGMENT CALL (task 052's first escalation trigger — reducing scope on evidence is a correct
// outcome, but the evidence here says BOTH sites stay in scope): reading GetJobStatusStreamAsync's full
// body, both `return` well before the line that sets `context.Response.ContentType =
// "text/event-stream"` and before any write to context.Response.Body — HasStarted is false at both
// points. The mid-stream case is a WHOLLY SEPARATE code path further down: the generic catch block
// checks `context.Response.HasStarted` and, only when true, uses
// Services.Office.SseHelper.FormatError to write a `data:` frame — that frame is never touched by this
// change. Both OFFICE_009 (401) and OFFICE_008 (404) sites here are plain pre-stream error responses,
// not frames inside a started stream — in scope for the same fix task 050 established.
//
// REACHABILITY NOTE (recorded for the record; does not change the in-scope classification above): on
// the LIVE route (GET /api/office/jobs/{jobId}/stream), both inline checks are defense-in-depth
// duplicates. AddOfficeAuthFilter() (an endpoint filter that runs BEFORE this handler) already 401s an
// unauthenticated/unresolvable caller via ASP.NET Core's own Results.Problem (errorCode
// OFFICE_AUTH_002) — the already-correct framework path per task 050's consumer survey.
// AddJobOwnershipFilter() (also before this handler) already 404s an unknown job, ALSO via
// Results.Problem, using the SAME errorCode "OFFICE_008" as this handler's own duplicate check. So on
// the real HTTP surface, this handler's inline OFFICE_009/OFFICE_008 branches fire only if it is ever
// invoked without those filters running first — which is exactly why this test drives them directly:
// it is the only way to exercise the lines at all. Still correct to fix: defense-in-depth code that is
// itself broken defeats the point of having it, and any future filter-chain reordering or reuse of this
// handler would immediately re-expose the header bug on the live path.
//
// TECHNIQUE: GetJobStatusStreamAsync is private static with a short, fully-mockable parameter list
// (Guid jobId, IOfficeService officeService, ILogger<Program> logger, HttpContext context,
// CancellationToken cancellationToken). Reflection matches the same technique the sibling
// Issue975_ChatAttachmentValidationProblemJsonTests.cs uses, originally established by
// ChatEndpointsAttachmentsTests.cs. No new fixture (CLAUDE.md §11): IOfficeService is an existing
// public interface, mocked the same way OfficeEndpointsContractTests.cs's own OfficeTestWebAppFactory
// mocks its collaborators.
//
// MAINTAIN-class (regression-protector; ADR-038 §1 KEEP path, tests/integration/regression/**,
// Issue{N}_*Tests.cs naming). NO Mock<HttpMessageHandler>, NO DI-registration test, NO ctor-null test,
// NO Stopwatch/Task.Delay.

using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.Office;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

public class Issue975_OfficeJobStreamProblemJsonTests
{
    private static readonly MethodInfo GetJobStatusStreamAsyncMethod =
        typeof(OfficeEndpoints).GetMethod("GetJobStatusStreamAsync", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(OfficeEndpoints), "GetJobStatusStreamAsync");

    private static async Task InvokeAsync(IOfficeService officeService, DefaultHttpContext httpContext, Guid jobId)
    {
        // Positional order MUST match GetJobStatusStreamAsync's declared parameter list exactly.
        var args = new object?[]
        {
            jobId,
            officeService,
            NullLogger<Program>.Instance,
            httpContext,
            CancellationToken.None
        };

        await (Task)GetJobStatusStreamAsyncMethod.Invoke(null, args)!;
    }

    private static async IAsyncEnumerable<byte[]> EmptyStream()
    {
        await Task.CompletedTask;
        yield break;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // 1. THE DEFECT (401 site): userId cannot be determined -> the OFFICE_009 rejection must carry
    //    application/problem+json. Before this task's fix, this failed — MediaType was
    //    "application/json" instead.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetJobStatusStream_WhenUserIdCannotBeDetermined_401Response_CarriesProblemJsonContentType()
    {
        // Arrange — a principal with no oid-shaped claim reproduces the handler's OWN defensive
        // branch directly. See the REACHABILITY NOTE above for why a real HTTP round trip cannot
        // reach this line: OfficeAuthFilter would 401 first, upstream, via its own already-correct
        // Results.Problem path.
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        httpContext.Response.Body = new MemoryStream();
        var officeServiceMock = new Mock<IOfficeService>(MockBehavior.Strict); // never called — proves the early return

        // Act
        await InvokeAsync(officeServiceMock.Object, httpContext, Guid.NewGuid());

        // Assert
        httpContext.Response.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);
        httpContext.Response.ContentType.Should().Be("application/problem+json",
            "RFC 7807 (ADR-019) — GetJobStatusStreamAsync's own WriteAsJsonAsync call was overwriting " +
            "this header with application/json before task 052's fix");

        httpContext.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(httpContext.Response.Body);
        body.GetProperty("status").GetInt32().Should().Be(401);
        body.GetProperty("errorCode").GetString().Should().Be("OFFICE_009");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // 2. THE DEFECT (404 site): the job does not exist -> the OFFICE_008 rejection must carry
    //    application/problem+json. Before this task's fix, this failed — MediaType was
    //    "application/json" instead.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetJobStatusStream_WhenJobDoesNotExist_404Response_CarriesProblemJsonContentType()
    {
        // Arrange
        var httpContext = TestHttpContexts.Authenticated();
        httpContext.Response.Body = new MemoryStream();
        var jobId = Guid.NewGuid();
        var officeServiceMock = new Mock<IOfficeService>(MockBehavior.Strict);
        officeServiceMock
            .Setup(s => s.GetJobStatusAsync(jobId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobStatusResponse?)null);

        // Act
        await InvokeAsync(officeServiceMock.Object, httpContext, jobId);

        // Assert
        httpContext.Response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
        httpContext.Response.ContentType.Should().Be("application/problem+json",
            "RFC 7807 (ADR-019) — GetJobStatusStreamAsync's own WriteAsJsonAsync call was overwriting " +
            "this header with application/json before task 052's fix");

        httpContext.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(httpContext.Response.Body);
        body.GetProperty("status").GetInt32().Should().Be(404);
        body.GetProperty("errorCode").GetString().Should().Be("OFFICE_008");
        body.GetProperty("jobId").GetString().Should().Be(jobId.ToString());
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // 3. NEGATIVE CONTROL: a successful stream start is untouched by this fix — the SSE content
    //    type this handler itself sets stays "text/event-stream" (POML acceptance criterion 5 / the
    //    "never change the stream's own content type" constraint).
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetJobStatusStream_WhenJobExists_StreamContentTypeRemainsEventStream()
    {
        // Arrange
        var httpContext = TestHttpContexts.Authenticated();
        httpContext.Response.Body = new MemoryStream();
        var jobId = Guid.NewGuid();
        var officeServiceMock = new Mock<IOfficeService>(MockBehavior.Strict);
        officeServiceMock
            .Setup(s => s.GetJobStatusAsync(jobId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobStatusResponse
            {
                JobId = jobId,
                Status = JobStatus.Running,
                JobType = JobType.DocumentSave,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        officeServiceMock
            .Setup(s => s.StreamJobStatusAsync(jobId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyStream());

        // Act
        await InvokeAsync(officeServiceMock.Object, httpContext, jobId);

        // Assert — untouched by this task's fix: the SSE content type this handler sets for a KNOWN
        // job is the ORIGINAL "text/event-stream", never "application/problem+json".
        httpContext.Response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        httpContext.Response.ContentType.Should().Be("text/event-stream",
            "a successful stream start must be unaffected by the problem+json fix at the sibling " +
            "401/404 branches in the same handler");
    }
}

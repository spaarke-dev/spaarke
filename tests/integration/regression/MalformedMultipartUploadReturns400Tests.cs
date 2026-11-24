// Regression anchor: a MALFORMED multipart body on the two BFF upload routes that read their form by hand
// answered 500 (server_error) instead of 400.
//
// THE DEFECT (found by sse-streaming-tests-r1 while building RateLimiterPipelineContractTests)
//   POST /api/ai/visualization/related-from-content (VisualizationEndpoints.IndexTemporaryContent) and
//   POST /api/ai/chat/sessions/{sessionId}/documents (ChatDocumentEndpoints.UploadDocumentAsync) checked
//   HasFormContentType and then called HttpRequest.ReadFormAsync unguarded. A body that DECLARES
//   multipart/form-data but is not valid multipart (an empty MultipartFormDataContent from .NET, or a section
//   without Content-Disposition) makes FormFeature throw InvalidDataException; nothing mapped it, so the global
//   exception handler answered 500 — a client error reported as a server fault (ADR-019), paging on a bad
//   request. ContainerItemEndpoints already guards its ReadFormAsync and was not affected.
//
// THE FIX: each handler wraps ONLY its ReadFormAsync in a catch for InvalidDataException / BadHttpRequestException
// and answers 400 ProblemDetails "Request body is not valid multipart/form-data." (no global mapping:
// Compose's OpenXml services throw InvalidDataException for corrupt STORED documents, a genuine 5xx).
//
// Drives the REAL routes through WebApplicationFactory<Program> as an authenticated caller. Each case uses a
// fresh caller, so the routes' "ai-upload" rate limit (5/min per caller) never interferes.
//
// MAINTAIN-class regression protector (ADR-038 §1 KEEP path). No Mock<HttpMessageHandler>, no timing.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Tests.Api.Finance;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

public sealed class MalformedMultipartUploadReturns400Tests : IClassFixture<MalformedMultipartFixture>
{
    private const string VisualizationRoute = "visualization";
    private const string ChatDocumentRoute = "chat-document";
    private const string EmptyMultipart = "empty-multipart";
    private const string SectionWithoutContentDisposition = "section-without-content-disposition";

    private readonly MalformedMultipartFixture _fixture;

    public MalformedMultipartUploadReturns400Tests(MalformedMultipartFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(VisualizationRoute, EmptyMultipart)]
    [InlineData(VisualizationRoute, SectionWithoutContentDisposition)]
    [InlineData(ChatDocumentRoute, EmptyMultipart)]
    [InlineData(ChatDocumentRoute, SectionWithoutContentDisposition)]
    public async Task UploadRoute_MalformedMultipartBody_Returns400ProblemDetailsWithoutExceptionDetail(string route, string body)
    {
        var caller = Guid.NewGuid().ToString();
        var path = route == VisualizationRoute
            ? "/api/ai/visualization/related-from-content"
            : $"/api/ai/chat/sessions/{await _fixture.CreateSessionAsync(caller)}/documents";

        using var client = _fixture.CreateClientFor(caller);
        using var content = BuildMalformedBody(body);

        using var response = await client.PostAsync(path, content);
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"a malformed multipart body is a client error, not a server fault (route={route}, body={body}): {text}");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json", "ADR-019 / RFC 7807");
        text.Should().Contain("Request body is not valid multipart/form-data.");
        text.Should().NotContain(nameof(InvalidDataException), "the parser's exception type must not reach the client");
        text.Should().NotContain("Content-Disposition", "the parser's message must not reach the client");
        text.Should().NotContain("   at ", "no stack frame may reach the client");
    }

    private const string FaultHeader = MalformedMultipartFixture.FaultHeader;

    private static string PathFor(string route, string session) =>
        route == VisualizationRoute
            ? "/api/ai/visualization/related-from-content"
            : $"/api/ai/chat/sessions/{session}/documents";

    // ISS-049: the ISS-043 handling on the chat document and visualization routes.
    private async Task<(HttpResponseMessage Response, string Text)> PostAsync(string route, HttpContent content, string? fault = null)
    {
        var caller = Guid.NewGuid().ToString();
        var path = PathFor(route, route == VisualizationRoute ? string.Empty : await _fixture.CreateSessionAsync(caller));
        using var client = _fixture.CreateClientFor(caller);
        if (fault is not null)
        {
            client.DefaultRequestHeaders.Add(FaultHeader, fault);
        }

        _fixture.Warnings.Entries.Clear();
        var response = await client.PostAsync(path, content);
        return (response, await response.Content.ReadAsStringAsync());
    }

    private IEnumerable<(LogLevel Level, string Message, Exception? Exception)> ReadFailureLogs() =>
        _fixture.Warnings.Entries.Where(e => e.Message.Contains("multipart request body could not be read"));

    [Theory(DisplayName = "ISS-049 must-fire: a multipart body with no closing boundary is a 400 (not a 500) and is logged at Warning without the message")]
    [InlineData(VisualizationRoute)]
    [InlineData(ChatDocumentRoute)]
    public async Task UploadRoute_NoClosingBoundary_Is400_AndLogsAWarning(string route)
    {
        using var content = MalformedMultipartBodies.NoClosingBoundary();

        var (response, text) = await PostAsync(route, content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
        text.Should().Contain("Request body is not valid multipart/form-data.");
        var entry = ReadFailureLogs().Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain(nameof(IOException)).And.Contain("HResult");
        entry.Message.Should().NotContain("x.pdf", "the file name is not logged");
        entry.Exception.Should().BeNull("the exception object (and so its message) is not logged");
    }

    [Theory(DisplayName = "ISS-049 must-fire: a body stream that fails with an IOException (token not cancelled) is a 400 logged at Warning without the message")]
    [InlineData(VisualizationRoute)]
    [InlineData(ChatDocumentRoute)]
    public async Task UploadRoute_BodyStreamFault_Is400_AndLogsAWarning(string route)
    {
        using var content = MalformedMultipartBodies.NoClosingBoundary();

        var (response, text) = await PostAsync(route, content, fault: "io");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
        var entry = ReadFailureLogs().Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain(nameof(IOException)).And.Contain("HResult");
        entry.Message.Should().NotContain(MalformedMultipartBodies.FaultMessage, "the parser's message can echo request content");
        entry.Exception.Should().BeNull();
    }

    [Theory(DisplayName = "ISS-049 fix round 1 (F1) must-fire: a body that fails because the client abandoned the request, or reset the connection, is answered 499 (not 400, not 500) and logs no Warning or Error")]
    [InlineData(VisualizationRoute, "abandoned")]
    [InlineData(VisualizationRoute, "reset")]
    [InlineData(ChatDocumentRoute, "abandoned")]
    [InlineData(ChatDocumentRoute, "reset")]
    public async Task UploadRoute_AbandonedOrReset_Is499_AndLogsNoFault(string route, string fault)
    {
        using var content = MalformedMultipartBodies.NoClosingBoundary();

        // An abandoned request may surface to the test client as the cancellation itself (the test server reports "The application aborted
        // the request"); any answer it does give must be the route's 499, never the global handler's 500.
        try
        {
            var (response, text) = await PostAsync(route, content, fault);
            ((int)response.StatusCode).Should().Be(499, text);
        }
        catch (OperationCanceledException) when (fault == "abandoned")
        {
        }

        _fixture.Warnings.Entries.Where(e => e.Level >= LogLevel.Warning && !e.Message.StartsWith("Bearer token present but unreadable")).Select(e => e.Level + ": " + e.Message)
            .Should().BeEmpty("an abandoned upload is not a fault and must not reach the global handler (the fake-auth host's own bearer-token warning is unrelated)");
        // The route itself handled it (the test server can report an abandoned request to the client as a cancellation, so the status alone cannot show that).
        _fixture.Warnings.Entries.Should().Contain(e => e.Level == LogLevel.Debug && e.Message.Contains("answered 499"));
    }

    /// <summary>A well-formed upload still passes the read (here it stops at the extension check, a 422 / 400 of the route, not the body fault).</summary>
    [Theory(DisplayName = "ISS-049 must-not-fire: a well-formed multipart upload is read as before (answered by the route's own validation, no multipart Warning)")]
    [InlineData(VisualizationRoute)]
    [InlineData(ChatDocumentRoute)]
    public async Task UploadRoute_WellFormedBody_IsReadAsBefore(string route)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("not an allowed type")), "file", "notes.exe");

        var (response, text) = await PostAsync(route, content);

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, text);
        text.Should().NotContain("Request body is not valid multipart/form-data.");
        ReadFailureLogs().Should().BeEmpty();
    }

    private static HttpContent BuildMalformedBody(string kind)
    {
        if (kind == EmptyMultipart)
        {
            // .NET writes an opening boundary followed by an empty, header-less section.
            return new MultipartFormDataContent();
        }

        const string boundary = "regression-boundary";
        var raw = $"--{boundary}\r\nContent-Type: text/plain\r\n\r\nno disposition here\r\n--{boundary}--\r\n";
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(raw));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={boundary}");
        return content;
    }
}

/// <summary>
/// The full BFF host (<see cref="CustomWebAppFactory"/> config and boundary doubles) with a header-selected caller
/// and the chat session cold/warm tiers doubled, so a session minted in the test lives in the in-memory hot tier.
/// </summary>
public sealed class MalformedMultipartFixture : CustomWebAppFactory
{
    public const string TenantId = "tenant-malformed-multipart";

    /// <summary>ISS-049: a request header naming a body fault the host simulates before the route runs: io, abandoned (token cancelled first) or reset.</summary>
    public const string FaultHeader = "X-Test-Body-Fault";

    /// <summary>ISS-049: what the host logged (level, formatted message, exception).</summary>
    internal MalformedMultipartBodies.LogCapture Warnings { get; } = new();

    public async Task<string> CreateSessionAsync(string ownerOid)
    {
        using var scope = Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
        var session = await sessions.CreateSessionAsync(TenantId, ownerOid, documentId: null, playbookId: null, hostContext: null);
        return session.SessionId;
    }

    public HttpClient CreateClientFor(string callerOid)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        client.DefaultRequestHeaders.Add(MalformedMultipartFakeAuthHandler.CallerHeader, callerOid);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(Warnings);
            services.AddLogging(logging => logging.AddFilter("Program", LogLevel.Debug).AddFilter("Sprk.Bff.Api.Api.Ai.ChatDocumentEndpoints", LogLevel.Debug)); // the routes log under ILogger<Program> and the chat route under its own category; ISS-049 asserts a Debug line
            services.AddTransient<IStartupFilter>(_ => new BodyFaultStartupFilter());

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, MalformedMultipartFakeAuthHandler>(MalformedMultipartFakeAuthHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = MalformedMultipartFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = MalformedMultipartFakeAuthHandler.SchemeName;
            });

            // Session cold tier (Dataverse) and warm tier (Cosmos) are external stores.
            services.RemoveAll<IChatDataverseRepository>();
            services.AddSingleton(new Mock<IChatDataverseRepository>(MockBehavior.Loose).Object);
            services.RemoveAll<ISessionPersistenceService>();
            services.AddSingleton(new Mock<ISessionPersistenceService>(MockBehavior.Loose).Object);
        });
    }
}

/// <summary>Authenticates any request with a bearer header as the oid named in <see cref="CallerHeader"/>.</summary>
internal sealed class MalformedMultipartFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "MalformedMultipartFakeAuth";
    public const string CallerHeader = "X-Test-Caller-Oid";

    public MalformedMultipartFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var oid = Request.Headers[CallerHeader].FirstOrDefault();
        if (!Request.Headers.ContainsKey("Authorization") || string.IsNullOrEmpty(oid))
        {
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header or caller"));
        }

        var claims = new List<Claim>
        {
            new("oid", oid),
            new("sub", oid),
            new("tid", MalformedMultipartFixture.TenantId),
            new(ClaimTypes.NameIdentifier, oid),
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

/// <summary>
/// ISS-049: first in the pipeline, replaces the request body (and token) with a faulting one when a test asks for it with
/// <see cref="MalformedMultipartFixture.FaultHeader"/>. Requests without the header are untouched.
/// </summary>
internal sealed class BodyFaultStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            switch (context.Request.Headers[MalformedMultipartFixture.FaultHeader].ToString())
            {
                case "io":
                    MalformedMultipartBodies.Apply(abandoned: false)(context);
                    break;
                case "abandoned":
                    MalformedMultipartBodies.Apply(abandoned: true)(context);
                    break;
                case "reset":
                    MalformedMultipartBodies.ApplyConnectionReset()(context);
                    break;
            }

            await nextMiddleware(context);
        });
        next(app);
    };
}

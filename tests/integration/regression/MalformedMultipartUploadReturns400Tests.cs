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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
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

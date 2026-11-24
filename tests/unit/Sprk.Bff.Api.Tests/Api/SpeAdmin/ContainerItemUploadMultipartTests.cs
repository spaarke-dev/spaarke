using System.Net;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Api.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.SpeAdmin;
using Sprk.Bff.Api.Tests.Api.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.SpeAdmin;

/// <summary>
/// ebilling-r1 ISS-049: the SPE admin upload route <c>POST /api/spe/containers/{containerId}/items/upload</c> reads its multipart form with a
/// catch-all (everything but <c>SdapProblemException</c>). A truncated body must answer 400 and log a Warning with the exception type and HResult
/// only; an abandoned upload (cancellation) must not be answered 400 or logged as a fault. The route is mapped on a bare <c>/api/spe</c> group:
/// the paths under test read the form before they touch the Graph or audit services, so those are never called.
/// </summary>
public sealed class ContainerItemUploadMultipartTests
{
    private const string UploadUrl = "/api/spe/containers/b!TESTCONTAINER/items/upload?configId=3f2504e0-4f89-11d3-9a0c-0305e82c3301";
    private const string ReadFailureText = "multipart request body could not be read";

    private sealed class Host : IAsyncDisposable
    {
        private WebApplication _app = null!;
        public HttpClient Client { get; private set; } = null!;
        public MalformedMultipartBodies.LogCapture Logs { get; } = new();
        public Action<HttpContext>? OnRequest { get; set; }

        public static async Task<Host> StartAsync()
        {
            var host = new Host();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Debug);
            builder.Logging.AddProvider(host.Logs);
            // Never reached on the paths under test (the form is read first); the handler needs only a bound instance.
            builder.Services.AddSingleton((SpeAdminGraphService)RuntimeHelpers.GetUninitializedObject(typeof(SpeAdminGraphService)));
            builder.Services.AddSingleton((SpeAuditService)RuntimeHelpers.GetUninitializedObject(typeof(SpeAuditService)));
            host._app = builder.Build();
            // ISS-049 fix round 1: like the production pipeline, an exception that escapes the route reaches a global handler that answers 500 and
            // logs an Error (Infrastructure/DI/MiddlewarePipelineExtensions.cs), so a test can tell an escape from a handled abandonment.
            host._app.UseExceptionHandler(handler => handler.Run(context =>
            {
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalHandler")
                    .LogError(context.Features.Get<IExceptionHandlerFeature>()?.Error, "Unhandled exception escaped the route");
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return Task.CompletedTask;
            }));
            host._app.Use(async (context, next) =>
            {
                host.OnRequest?.Invoke(context);
                await next(context);
            });
            ContainerItemEndpoints.MapContainerItemEndpoints(host._app.MapGroup("/api/spe"));
            await host._app.StartAsync();
            host.Client = host._app.GetTestClient();
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }

        public IEnumerable<(LogLevel Level, string Message, Exception? Exception)> ReadFailures() =>
            Logs.Entries.Where(e => e.Message.Contains(ReadFailureText));
    }

    private static HttpRequestMessage Upload(HttpContent content) => new(HttpMethod.Post, UploadUrl) { Content = content };

    [Fact(DisplayName = "ISS-049 must-fire: a multipart body with no closing boundary is a 400 and logs a Warning with the exception type and HResult only")]
    public async Task NoClosingBoundary_Is400_AndLogsAWarning()
    {
        await using var host = await Host.StartAsync();

        var response = await host.Client.SendAsync(Upload(MalformedMultipartBodies.NoClosingBoundary(fileName: "secret-name.pdf")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var entry = host.ReadFailures().Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain(nameof(IOException)).And.Contain("HResult");
        entry.Message.Should().NotContain("secret-name.pdf", "the file name is not logged");
        entry.Exception.Should().BeNull("the exception object (its message and stack trace) is not logged");
    }

    [Fact(DisplayName = "ISS-049 must-fire: a body stream that fails with an IOException (token not cancelled) is a 400 logged without the message")]
    public async Task BodyStreamFault_Is400_AndLogsAWarning_WithoutTheMessage()
    {
        await using var host = await Host.StartAsync();
        host.OnRequest = MalformedMultipartBodies.Apply(abandoned: false);

        var response = await host.Client.SendAsync(Upload(MalformedMultipartBodies.NoClosingBoundary()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var entry = host.ReadFailures().Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().NotContain(MalformedMultipartBodies.FaultMessage, "the parser's message can echo request content");
        entry.Exception.Should().BeNull();
    }

    [Fact(DisplayName = "ISS-049 fix round 1 (F1) must-fire: a body that fails because the client abandoned the request (token cancelled, IOException) is answered 499, nothing escapes, no Warning or Error is logged")]
    public async Task AbandonedRequest_IoException_Is499_AndLogsNoFault()
    {
        await using var host = await Host.StartAsync();
        host.OnRequest = MalformedMultipartBodies.Apply(abandoned: true);

        // The test server may surface the abandonment to the client as the cancellation itself; either way nothing may escape the route.
        try
        {
            var response = await host.Client.SendAsync(Upload(MalformedMultipartBodies.NoClosingBoundary()));
            ((int)response.StatusCode).Should().Be(499);
        }
        catch (OperationCanceledException)
        {
        }

        host.Logs.Entries.Where(e => e.Level >= LogLevel.Warning).Select(e => e.Level + ": " + e.Message).Should().BeEmpty("an abandoned upload is not a fault and must not reach the global handler");
        host.Logs.Entries.Should().Contain(e => e.Level == LogLevel.Debug && e.Message.Contains("answered 499"), "the route handled it");
    }

    [Fact(DisplayName = "ISS-049 must-not-fire: a body read that is cancelled (OperationCanceledException) is not a 400 and logs no fault")]
    public async Task CancelledRead_IsNotA400_AndLogsNothing()
    {
        await using var host = await Host.StartAsync();
        host.OnRequest = context =>
        {
            var source = new CancellationTokenSource();
            context.RequestAborted = source.Token;
            context.Request.Body = new CancelledBody(source);
        };

        // The route no longer catches the cancellation (it passes through to the global handler, ISS-049 notes K2): not a 400, and the route
        // logs no "Failed to read multipart form" Warning with a stack trace.
        try
        {
            var response = await host.Client.SendAsync(Upload(MalformedMultipartBodies.NoClosingBoundary()));
            response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
        }
        catch (OperationCanceledException)
        {
        }

        host.Logs.Entries.Should().NotContain(e => e.Message.Contains("Failed to read multipart form") || e.Message.Contains(ReadFailureText));
    }

    [Fact(DisplayName = "ISS-049 fix round 1 (K3) must-fire: a BadHttpRequestException is a 400 and its StatusCode is logged, so a 413 and a truncated body can be told apart")]
    public async Task BadHttpRequest_Is400_AndLogsItsStatusCode()
    {
        await using var host = await Host.StartAsync();
        host.OnRequest = context => context.Request.Body = new BadRequestBody();

        var response = await host.Client.SendAsync(Upload(MalformedMultipartBodies.NoClosingBoundary()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var entry = host.ReadFailures().Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("HttpStatusCode 413").And.Contain(nameof(BadHttpRequestException));
    }

    /// <summary>A body whose read throws Kestrel's "request body too large" (413).</summary>
    private sealed class BadRequestBody : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw Fault();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(Fault());
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromException<int>(Fault());
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private static BadHttpRequestException Fault() => new("Request body too large.", StatusCodes.Status413PayloadTooLarge);
    }

    [Fact(DisplayName = "ISS-049 fix round 1 (F1) must-fire: a connection reset (token not cancelled) is answered 499, nothing escapes, no Warning or Error is logged")]
    public async Task ConnectionReset_Is499_AndLogsNoFault()
    {
        await using var host = await Host.StartAsync();
        host.OnRequest = MalformedMultipartBodies.ApplyConnectionReset();

        var response = await host.Client.SendAsync(Upload(MalformedMultipartBodies.NoClosingBoundary()));

        ((int)response.StatusCode).Should().Be(499);
        host.Logs.Entries.Where(e => e.Level >= LogLevel.Warning).Select(e => e.Level + ": " + e.Message).Should().BeEmpty("a reset is not a fault and must not reach the global handler");
    }

    [Fact(DisplayName = "ISS-049 must-not-fire: a well-formed multipart body is read as before (the route's own empty-file refusal, not the read failure)")]
    public async Task WellFormedBody_IsRead_AsBefore()
    {
        await using var host = await Host.StartAsync();
        using var content = new MultipartFormDataContent { { new ByteArrayContent(Array.Empty<byte>()), "file", "empty.txt" } };

        var response = await host.Client.SendAsync(Upload(content));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("The 'file' form field is required and must not be empty.");
        host.ReadFailures().Should().BeEmpty();
    }

    /// <summary>A body whose first read cancels the request's token and throws the cancellation, as a client that abandons the upload does.</summary>
    private sealed class CancelledBody(CancellationTokenSource source) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw Cancel();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(Cancel());
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromException<int>(Cancel());
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private OperationCanceledException Cancel()
        {
            source.Cancel();
            return new OperationCanceledException(source.Token);
        }
    }
}

using System.Net;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
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
            builder.Logging.AddProvider(host.Logs);
            // Never reached on the paths under test (the form is read first); the handler needs only a bound instance.
            builder.Services.AddSingleton((SpeAdminGraphService)RuntimeHelpers.GetUninitializedObject(typeof(SpeAdminGraphService)));
            builder.Services.AddSingleton((SpeAuditService)RuntimeHelpers.GetUninitializedObject(typeof(SpeAuditService)));
            host._app = builder.Build();
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

    [Fact(DisplayName = "ISS-049 must-not-fire: a body that fails because the client abandoned the request (token cancelled, IOException) is not a 400 and logs no fault")]
    public async Task AbandonedRequest_IoException_IsNotA400_AndLogsNothing()
    {
        await using var host = await Host.StartAsync();
        host.OnRequest = MalformedMultipartBodies.Apply(abandoned: true);

        var act = async () => await host.Client.SendAsync(Upload(MalformedMultipartBodies.NoClosingBoundary()));

        (await act.Should().ThrowAsync<Exception>()).Which.Should().Match(e => e is IOException || e is OperationCanceledException);
        host.Logs.Entries.Where(e => e.Level >= LogLevel.Warning).Should().BeEmpty("an abandoned upload is not a fault");
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

        var act = async () => await host.Client.SendAsync(Upload(MalformedMultipartBodies.NoClosingBoundary()));

        (await act.Should().ThrowAsync<Exception>()).Which.Should().BeAssignableTo<OperationCanceledException>();
        host.Logs.Entries.Where(e => e.Level >= LogLevel.Warning).Should().BeEmpty("an abandoned upload is not a fault");
    }

    [Fact(DisplayName = "ISS-049 must-not-fire: a connection reset (token not cancelled) is not a 400 and logs no fault")]
    public async Task ConnectionReset_IsNotA400_AndLogsNothing()
    {
        await using var host = await Host.StartAsync();
        host.OnRequest = MalformedMultipartBodies.ApplyConnectionReset();

        var act = async () => await host.Client.SendAsync(Upload(MalformedMultipartBodies.NoClosingBoundary()));

        await act.Should().ThrowAsync<Microsoft.AspNetCore.Connections.ConnectionResetException>();
        host.Logs.Entries.Where(e => e.Level >= LogLevel.Warning).Should().BeEmpty();
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

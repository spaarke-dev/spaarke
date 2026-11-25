using System.Net.Http.Headers;
using System.Text;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// ebilling-r1 ISS-043: request bodies the multipart routes' <c>ReadFormAsync</c> answers with a <see cref="IOException"/> (not the
/// <see cref="InvalidDataException"/> or <see cref="BadHttpRequestException"/> the routes already mapped to their 400), and the pieces
/// a host needs to simulate a client that abandons the request mid-body. Shared by the Procedure, rate card and invoice submission route tests.
/// </summary>
internal static class MalformedMultipartBodies
{
    /// <summary>A multipart body whose last part never gets its closing boundary (the stream ends inside the part): the parser throws IOException.</summary>
    internal static HttpContent NoClosingBoundary(string partName = "file", string fileName = "x.pdf")
    {
        const string boundary = "iss043-boundary";
        var raw = $"--{boundary}\r\nContent-Disposition: form-data; name=\"{partName}\"; filename=\"{fileName}\"\r\nContent-Type: application/pdf\r\n\r\n%PDF-1.4 truncated";
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(raw));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={boundary}");
        return content;
    }

    /// <summary>
    /// A request whose body cannot be read: the first read throws an <see cref="IOException"/> (a dropped connection or a stream cut short).
    /// With <paramref name="abandoned"/> the request's own token is cancelled first (<see cref="Apply"/>): the client left, which is not a
    /// malformed body and must not be answered as one.
    /// </summary>
    internal static Action<HttpContext> Apply(bool abandoned)
    {
        return context =>
        {
            var source = new CancellationTokenSource();
            context.RequestAborted = source.Token;
            context.Request.Body = new IoFaultBody(abandoned ? source : null, reset: false);
        };
    }

    /// <summary>
    /// ISS-043 fix round 1 (K2): the client reset the connection mid-body. The first read throws Kestrel's
    /// <see cref="ConnectionResetException"/> (an <see cref="IOException"/>) while the request's token is NOT cancelled; it is not a malformed
    /// body and must reach the server as before, never a 400.
    /// </summary>
    internal static Action<HttpContext> ApplyConnectionReset()
    {
        return context =>
        {
            context.RequestAborted = new CancellationTokenSource().Token;
            context.Request.Body = new IoFaultBody(null, reset: true);
        };
    }

    /// <summary>The message of the IOException the fault body throws: a test that it never reaches a log or a response looks for this text.</summary>
    internal const string FaultMessage = "Unexpected end of Stream, the content may have already been read by another component.";

    /// <summary>ISS-043 fix round 1 (F1): records what a host logs (level, formatted message, exception), to assert the Warning a swallowed body fault leaves.</summary>
    internal sealed class LogCapture : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(LogCapture owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Entries.Enqueue((logLevel, formatter(state, exception), exception));
        }
    }

    private sealed class IoFaultBody(CancellationTokenSource? cancelOnRead, bool reset) : Stream
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

        private IOException Fault()
        {
            cancelOnRead?.Cancel();
            return reset ? new ConnectionResetException("The client reset the connection.") : new IOException(FaultMessage);
        }
    }
}

using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Http;

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
            context.Request.Body = new IoFaultBody(abandoned ? source : null);
        };
    }

    private sealed class IoFaultBody(CancellationTokenSource? cancelOnRead) : Stream
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
            return new IOException("Unexpected end of Stream, the content may have already been read by another component.");
        }
    }
}

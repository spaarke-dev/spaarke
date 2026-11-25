using System.Security.Cryptography;
using MimeKit;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// ebilling-r1 task 095 (model §9.1 steps 2 and 3): the attachment parts of an <c>.eml</c>, the ONE definition of a part's ordinal,
/// name and SHA-256 that the from-email submission records at save time and the submission job checks when it reads the saved
/// <c>.eml</c> back from SPE. The ordinal is the 0-based position in <see cref="MimeMessage.Attachments"/> (MIME parts whose
/// Content-Disposition is <c>attachment</c>, in document order: what <see cref="OfficeEmailEnricher.BuildEmlFromMetadata"/> adds as a
/// regular attachment). Inline linked resources and embedded messages are not attachment parts here.
/// </summary>
internal static class EmlMimeParts
{
    /// <summary>The attachment parts of the <c>.eml</c> in <paramref name="eml"/> (read from its current position; the caller resets it).</summary>
    /// <exception cref="FormatException">The stream is not a parseable MIME message.</exception>
    public static IReadOnlyList<EmlPart> Read(Stream eml, CancellationToken ct = default) => ReadWithContent(eml, ct).Select(p => p.Part).ToList();

    /// <summary>
    /// As <see cref="Read"/>, with each part's decoded bytes (for the submission job's staging). DEF-052: when <paramref name="maxPartBytes"/>
    /// is given, a part whose decoded size is over it is not held: its decode stops there and it comes back with <c>Oversize</c> true, empty
    /// content and an empty <see cref="EmlPart.Sha256"/> (the default, no limit, is exactly the read the save has always made), so a large
    /// part cannot fill memory. A part of exactly <paramref name="maxPartBytes"/> bytes is read.
    /// </summary>
    public static IReadOnlyList<(EmlPart Part, byte[] Content, bool Oversize)> ReadWithContent(
        Stream eml, CancellationToken ct = default, long? maxPartBytes = null)
    {
        ArgumentNullException.ThrowIfNull(eml);
        // MimeKit throws FormatException (ParseException derives from it) for a stream that is not a MIME message.
        var message = MimeMessage.Load(eml, ct);

        var parts = new List<(EmlPart, byte[], bool)>();
        var ordinal = 0;
        foreach (var entity in message.Attachments)
        {
            if (entity is not MimePart part)
            {
                continue; // an attached message (message/rfc822) is not a file part
            }

            using var decoded = new LimitedMemoryStream(maxPartBytes);
            try
            {
                part.Content?.DecodeTo(decoded, ct);
            }
            catch (PartOverLimitException)
            {
                parts.Add((new EmlPart(ordinal, part.FileName, string.Empty), Array.Empty<byte>(), true));
                ordinal++;
                continue;
            }

            var bytes = decoded.ToArray();
            parts.Add((new EmlPart(ordinal, part.FileName, Sha256Hex(bytes)), bytes, false));
            ordinal++;
        }

        return parts;
    }

    /// <summary>Raised by <see cref="LimitedMemoryStream"/> at the first byte past its limit; never leaves <see cref="ReadWithContent"/>.</summary>
    private sealed class PartOverLimitException : IOException;

    /// <summary>A <see cref="MemoryStream"/> that refuses the first byte past <c>limit</c> (null: no limit).</summary>
    private sealed class LimitedMemoryStream(long? limit) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            Check(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Check(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            Check(1);
            base.WriteByte(value);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Check(count);
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Check(buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
        }

        private void Check(int adding)
        {
            if (limit is { } max && Length + adding > max)
            {
                throw new PartOverLimitException();
            }
        }
    }

    /// <summary>The lower-case hex SHA-256 of <paramref name="bytes"/>.</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// Whether <see cref="OfficeEmailEnricher.BuildEmlFromMetadata"/> carries an attachment entry into the <c>.eml</c> as a part: it has
    /// <c>ContentBase64</c> that decodes and a content type MimeKit parses (the builder skips anything else silently). The decoded bytes
    /// when it does.
    /// </summary>
    public static bool TryCarriedBytes(string? contentBase64, string? contentType, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrEmpty(contentBase64))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(contentBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        return ContentType.TryParse(contentType ?? "application/octet-stream", out _);
    }
}

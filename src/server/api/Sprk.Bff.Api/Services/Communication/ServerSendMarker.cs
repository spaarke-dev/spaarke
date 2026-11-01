using System.Security.Cryptography;
using System.Text;
using Sprk.Bff.Api.Services.Communication.Tracking;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// The header a server-only send (<see cref="Models.ServerSendOptions"/>, ebilling-r1 task 151c-1a) carries, so the inbound mailbox path
/// (<see cref="IncomingCommunicationProcessor"/>) does not file its attachments as documents or archive its <c>.eml</c> when the same email
/// reaches a monitored mailbox (by a distribution list, a forward rule or a missed Internet-Message-Id echo match).
/// </summary>
/// <remarks>
/// <para><b>What the value is.</b> A token from the existing tracking-token signer (<see cref="ITrackingTokenSigner"/>: HMAC-SHA256 with the
/// Key Vault key, verified in constant time). Its record type is <see cref="RecordTypePrefix"/> followed by a SHA-256 digest of the
/// attachments that were sent (each file's name and the SHA-256 of its bytes, sorted). Only the server holds the key, so the header
/// cannot be forged.</para>
/// <para><b>Why the digest.</b> Every recipient sees the header, so a recipient can copy it onto another email. The inbound check recomputes
/// the digest from the attachments the inbound message actually carries and honours the marker only when it is equal, so a copied header
/// can only ever skip filing files byte-identical to the ones this server sent, which are already stored on their own record. A message
/// with no attachments, a non-file attachment, an attachment whose bytes cannot be read, a second marker header, a token for any other
/// record type (a tracking-footer token, for example) or no signing key is processed as before.</para>
/// <para><b>No key, no marker.</b> When the signing key is not configured the signer returns null, no header is sent and the inbound path is
/// unchanged; the sender-side recipient check (the AP export route refuses a monitored recipient) still applies.</para>
/// </remarks>
internal static class ServerSendMarker
{
    /// <summary>The header name (Graph accepts custom headers that start with <c>x-</c>).</summary>
    public const string HeaderName = "x-spaarke-server-send";

    /// <summary>The token's record type is this prefix and the attachment digest. No regarding map holds it, so the tracking rung ignores it.</summary>
    public const string RecordTypePrefix = "spaarke-server-send:";

    /// <summary>
    /// The digest of a set of files: SHA-256 over each file's name and the SHA-256 of its bytes, sorted by name then content hash.
    /// Null when there are no files or a file has no bytes (nothing to bind the marker to).
    /// </summary>
    public static string? AttachmentDigest(IEnumerable<(string? Name, byte[]? Content)> files)
    {
        var lines = new List<string>();
        foreach (var (name, content) in files)
        {
            if (content is null)
            {
                return null;
            }

            lines.Add((name ?? string.Empty) + "\n" + Convert.ToHexString(SHA256.HashData(content)));
        }

        if (lines.Count == 0)
        {
            return null;
        }

        lines.Sort(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines))));
    }

    /// <summary>The header value for a send of these files, or null (no files, or no signing key).</summary>
    public static async Task<string?> SignAsync(
        ITrackingTokenSigner signer, IEnumerable<(string? Name, byte[]? Content)> files, Guid recordId, DateTimeOffset issued, CancellationToken ct)
    {
        var digest = AttachmentDigest(files);
        return digest is null
            ? null
            : await signer.SignAsync(RecordTypePrefix + digest, recordId, null, issued, ct).ConfigureAwait(false);
    }

    /// <summary>The marker header's values on a message (any case of the name).</summary>
    public static List<string> MarkerValues(IEnumerable<(string? Name, string? Value)>? headers) =>
        headers?
            .Where(h => string.Equals(h.Name?.Trim(), HeaderName, StringComparison.OrdinalIgnoreCase))
            .Select(h => h.Value ?? string.Empty)
            .ToList() ?? [];

    /// <summary>
    /// True only when the message carries exactly one marker header, its token verifies with the server's key, and its digest equals
    /// the digest of <paramref name="files"/> (the attachments the message actually carries). Never throws; anything else is false.
    /// </summary>
    public static async Task<bool> IsVerifiedAsync(
        ITrackingTokenSigner? signer, IReadOnlyList<string> markerValues, IEnumerable<(string? Name, byte[]? Content)> files, CancellationToken ct)
    {
        if (signer is null || markerValues.Count != 1)
        {
            return false;
        }

        // A long header may arrive folded; base64url carries no whitespace, so whitespace is folding.
        var token = new string(markerValues[0].Where(c => !char.IsWhiteSpace(c)).ToArray());
        var digest = AttachmentDigest(files);
        if (digest is null || token.Length == 0)
        {
            return false;
        }

        try
        {
            var verification = await signer.VerifyAsync(token, ct).ConfigureAwait(false);
            return verification is { IsValid: true, Payload: { } payload }
                && string.Equals(payload.RecordType, RecordTypePrefix + digest, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}

using System.Text;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Builds Microsoft Graph sharing tokens (<c>u!{base64url}</c>) for <c>GET /shares/{token}/driveItem</c>,
/// which resolves an absolute document URL to the drive item it names (FR-01, spaarkeai-word-add-in-r1 task 012).
/// </summary>
/// <remarks>
/// <para>Graph's documented encoding: base64-encode the URL, strip the <c>=</c> padding, replace <c>/</c> with
/// <c>_</c> and <c>+</c> with <c>-</c>, prefix <c>u!</c>. The token encodes the URL's BYTES, so two spellings of one
/// URL are two different tokens, and which spelling Graph accepts over an SPE <c>contentstorage</c> path is
/// undocumented. Until the resolver's logs record the answer, each plausible spelling is tried in turn. A 400/404 on
/// one of them is an ENCODING finding, not evidence that SPE is unsupported.</para>
/// <para><b>The spellings, in order.</b></para>
/// <list type="number">
/// <item><see cref="EncodedForm"/> — every path segment of the URL exactly as the caller sent it, percent-encoded.
/// <c>Office.context.document.url</c> returns the path RAW (spike-1 §19: <c>…/Document Library/Examiner report
/// draft.docx</c>), so this is right for what the add-in sends — including a <c>#</c> or <c>%</c> in a file name, both
/// legal in SharePoint. Parsed as a URI instead, <c>#</c> would start a fragment and a literal <c>%20</c> would read as
/// an escaped space: Graph would be handed a different file.</item>
/// <item><see cref="NormalizedForm"/> — <see cref="Uri.AbsoluteUri"/>, right when the caller had ALREADY
/// percent-encoded the URL (the form the BFF's own open-links emits, spike-1 §21). Skipped when identical to 1.</item>
/// <item><see cref="RawForm"/> — the unescaped URL, the literal spelling the host returned.</item>
/// </list>
/// <para>Public: a unit with its own contract, tested through it (ADR-038 B8).</para>
/// </remarks>
public static class SharingUrlToken
{
    public const string EncodedForm = "encoded";
    public const string NormalizedForm = "normalized";
    public const string RawForm = "raw";

    public static string Encode(string absoluteUrl)
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(absoluteUrl));
        return "u!" + base64.TrimEnd('=').Replace('/', '_').Replace('+', '-');
    }

    /// <summary>The spellings to try, in order, without duplicates. See the type's remarks.</summary>
    public static IReadOnlyList<(string Form, string Url)> BuildCandidates(Uri documentUrl)
    {
        var candidates = new List<(string Form, string Url)>(3);

        void Add(string form, string url)
        {
            if (!candidates.Exists(c => string.Equals(c.Url, url, StringComparison.Ordinal)))
                candidates.Add((form, url));
        }

        Add(EncodedForm, EncodeRawPath(documentUrl));
        Add(NormalizedForm, documentUrl.AbsoluteUri);
        Add(RawForm, Uri.UnescapeDataString(documentUrl.AbsoluteUri));
        return candidates;
    }

    /// <summary>
    /// Percent-encodes each path segment of the URL as the caller sent it (<see cref="Uri.OriginalString"/>). A
    /// SharePoint name cannot contain <c>?</c>, so a query, if one is present, is kept verbatim.
    /// </summary>
    private static string EncodeRawPath(Uri documentUrl)
    {
        var original = documentUrl.OriginalString.Trim();
        var schemeEnd = original.IndexOf("://", StringComparison.Ordinal);
        var pathStart = schemeEnd < 0 ? -1 : original.IndexOf('/', schemeEnd + 3);
        if (pathStart < 0)
            return documentUrl.AbsoluteUri;

        var path = original[pathStart..];
        var query = string.Empty;
        var queryStart = path.IndexOf('?');
        if (queryStart >= 0)
        {
            query = path[queryStart..];
            path = path[..queryStart];
        }

        return original[..pathStart]
            + string.Join('/', path.Split('/').Select(Uri.EscapeDataString))
            + query;
    }
}

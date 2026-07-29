using System.Text;
using System.Text.RegularExpressions;
using Ganss.Xss;
using MimeKit;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Renders an archived RFC 2822 <c>.eml</c> as sanitized, ready-to-display HTML for the reading pane.
///
/// <para>This is the HTML-PRESERVING reverse of <see cref="GraphMessageToEmlConverter"/> (which writes the
/// self-contained <c>multipart/related</c> body with <c>Content-Id</c> inline parts). It:
/// <list type="number">
///   <item>parses the MIME with MimeKit, taking <see cref="MimeMessage.HtmlBody"/> (NOT the text-stripped
///     path <c>TextExtractorService.ExtractFromEmlAsync</c> uses for AI — the opposite intent);</item>
///   <item>rewrites every inline <c>cid:</c> reference to a <c>data:</c> URI resolved from the
///     <c>multipart/related</c> image parts;</item>
///   <item>SANITIZES the resulting HTML server-side with a hardened allow-list — this is the AUTHORITATIVE
///     XSS boundary for the untrusted-email-HTML path (spec NFR-03). The client renders the returned HTML in
///     a sandboxed iframe as defense-in-depth (task 033), NOT as the primary control.</item>
/// </list></para>
///
/// <para><b>Pure transformation</b> — no I/O, no Dataverse, no Graph, no SPE (the caller supplies the already
/// downloaded <c>.eml</c> stream). Mirrors <see cref="GraphMessageToEmlConverter"/>'s shape so it is directly
/// unit-testable with real <c>.eml</c> fixtures (ADR-038 — no <c>Mock&lt;HttpMessageHandler&gt;</c>).</para>
///
/// <para><b>Registered Singleton</b> (CommunicationModule): the underlying <see cref="HtmlSanitizer"/> is
/// configured once in the constructor and only <c>Sanitize</c> is called thereafter, which is thread-safe.</para>
/// </summary>
public sealed partial class EmlToHtmlRenderer
{
    /// <summary>
    /// Long-lived cache horizon for the archived (immutable) <c>.eml</c> render (~1 year). Surfaced so the
    /// endpoint's cache header and its tests share one source of truth.
    /// </summary>
    internal const int ImmutableMaxAgeSeconds = 31_536_000;

    private readonly HtmlSanitizer _sanitizer;

    public EmlToHtmlRenderer()
    {
        _sanitizer = BuildSanitizer();
    }

    /// <summary>
    /// Parses the <c>.eml</c> stream and returns sanitized, safe-to-render HTML with inline <c>cid:</c> images
    /// resolved to <c>data:</c> URIs. Never returns unsanitized markup.
    /// </summary>
    public async Task<string> RenderSanitizedHtmlAsync(Stream emlStream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(emlStream);

        var message = await MimeMessage.LoadAsync(emlStream, ct);

        var html = message.HtmlBody;
        if (string.IsNullOrEmpty(html))
        {
            // No HTML part: synthesize minimal HTML from the plain-text body so the pane still renders,
            // preserving line breaks. Do NOT strip — this is the opposite of TextExtractorService.
            html = SynthesizeHtmlFromText(message.TextBody);
        }
        else
        {
            // Resolve inline images (cid:) to data: URIs before sanitizing (so the data: image survives the
            // allow-list). Unresolved cid: refs are left in place and dropped by the sanitizer (cid is not an
            // allowed scheme) — safe by construction.
            html = RewriteCidReferences(html, message, ct);
        }

        return _sanitizer.Sanitize(html);
    }

    /// <summary>
    /// Builds the hardened sanitizer: default Ganss allow-list (already excludes <c>script</c>/<c>iframe</c>/
    /// <c>object</c>/<c>embed</c> and every <c>on*</c> handler) tightened further, with URL schemes restricted
    /// to http/https/mailto and image-only <c>data:</c> URIs.
    /// </summary>
    private static HtmlSanitizer BuildSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        // Defense-in-depth: be explicit about the dangerous tag set (idempotent if already absent by default).
        foreach (var tag in new[] { "script", "iframe", "object", "embed", "form", "base", "link", "meta", "frame", "frameset" })
        {
            sanitizer.AllowedTags.Remove(tag);
        }

        // Authoritative scheme allow-list (spec NFR-03): http/https/mailto + data (data further restricted to
        // images by the FilterUrl guard below). javascript:, vbscript:, file:, etc. are excluded by omission.
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("mailto");
        sanitizer.AllowedSchemes.Add("data");

        // Guard: permit ONLY inline-image data: URIs (data:image/...). Strip any other data: URI (e.g.
        // data:text/html, which could carry script). This keeps the cid:->data: image inlining working while
        // preventing a malicious .eml from smuggling an executable data: payload past the scheme allow-list.
        sanitizer.FilterUrl += (_, e) =>
        {
            var url = e.SanitizedUrl ?? e.OriginalUrl;
            if (!string.IsNullOrEmpty(url)
                && url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                e.SanitizedUrl = null; // remove the attribute entirely
            }
        };

        return sanitizer;
    }

    /// <summary>
    /// Rewrites <c>cid:</c> references in the HTML to <c>data:</c> URIs built from the message's inline image
    /// parts. Non-image or unmatched <c>cid:</c> references are left untouched (the sanitizer drops them).
    /// </summary>
    private static string RewriteCidReferences(string html, MimeMessage message, CancellationToken ct)
    {
        var map = BuildContentIdToDataUriMap(message, ct);
        if (map.Count == 0)
        {
            return html;
        }

        return CidReferenceRegex().Replace(html, match =>
        {
            var key = NormalizeContentId(match.Groups["id"].Value);

            if (map.TryGetValue(key, out var dataUri))
            {
                return dataUri;
            }

            // Case-insensitive fallback (Content-Id casing varies across mail clients).
            foreach (var kvp in map)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            // Unresolved: leave the cid: token in place — the sanitizer strips it (cid is not an allowed scheme).
            return match.Value;
        });
    }

    /// <summary>
    /// Enumerates the message's leaf parts and builds a <c>Content-Id → data: URI</c> map for inline IMAGE
    /// parts only. Restricting to <c>image/*</c> is a defense: an inline <c>cid:</c> reference in an
    /// <c>&lt;img src&gt;</c> is always an image; non-image parts are never inlined as data: URIs.
    /// </summary>
    private static Dictionary<string, string> BuildContentIdToDataUriMap(MimeMessage message, CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var part in message.BodyParts.OfType<MimePart>())
        {
            if (string.IsNullOrEmpty(part.ContentId))
            {
                continue;
            }

            if (part.Content is null)
            {
                continue;
            }

            var mimeType = part.ContentType?.MimeType ?? "application/octet-stream";
            if (!mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var ms = new MemoryStream();
            part.Content.DecodeTo(ms, ct);
            var base64 = Convert.ToBase64String(ms.ToArray());

            var key = NormalizeContentId(part.ContentId);
            map[key] = $"data:{mimeType};base64,{base64}";
        }

        return map;
    }

    private static string NormalizeContentId(string contentId)
        => contentId.Trim().Trim('<', '>').Trim();

    /// <summary>
    /// Synthesizes minimal, HTML-encoded markup from a plain-text body, preserving line breaks. Returns an
    /// empty <c>&lt;div&gt;</c> when there is no text so the pane renders an empty (not broken) message.
    /// </summary>
    private static string SynthesizeHtmlFromText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "<div></div>";
        }

        var encoded = System.Net.WebUtility.HtmlEncode(text)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal);

        return $"<div>{encoded}</div>";
    }

    // Matches a cid: reference and captures the Content-Id token (up to the next quote, paren, whitespace or >).
    [GeneratedRegex("cid:(?<id>[^\"'()\\s>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CidReferenceRegex();
}

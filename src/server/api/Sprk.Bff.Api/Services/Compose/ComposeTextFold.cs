namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Server-side twin of the client's <c>MATCH_FOLD</c> (usePendingRedline.ts) — a STRICTLY 1:1
/// per-character typographic fold used to match editor-origin text against retained-original OOXML
/// text. Word/DOCX carries typographic characters (curly quotes, non-breaking/thin/figure/narrow
/// spaces, en/em/figure/non-breaking dashes) that the model routinely straightens when it echoes a
/// clause back as <c>target_text</c>, and that the editor may fold on paste; a raw ordinal match then
/// misses. Folding BOTH sides through the SAME map closes that gap.
/// </summary>
/// <remarks>
/// <para>
/// Added 2026-07-20 (UAT round 2, C1/C2). C1: <see cref="DocxAnnotationWriter"/> located annotation
/// targets by an EXACT ordinal search while the client had already folded — a single curly apostrophe
/// or NBSP drift produced a 422 "tracked change could not be located". C2: the save-time paraId stamp
/// (<see cref="ComposeBaselineParaIdStamper"/>) verifies a baseline paragraph's text against the
/// client's load-time snapshot text before stamping a minted id, and must fold identically.
/// </para>
/// <para>
/// The per-character map mirrors the client's <c>MATCH_FOLD</c> code-point-for-code-point (verified
/// against usePendingRedline.ts). <see cref="Fold"/> is 1:1 (one input char in → one out,
/// position-preserving). <see cref="Normalize"/> additionally collapses whitespace runs and trims — a
/// NON-1:1 pass used only for whole-paragraph equality checks (never for position-preserving index math).
/// </para>
/// </remarks>
public static class ComposeTextFold
{
    /// <summary>1:1 typographic fold — curly quotes / typographic spaces / dashes → canonical ASCII.
    /// One input char → one output char (position-preserving). Mirrors client <c>MATCH_FOLD</c>.</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        // Fast path: strings with no fold-eligible char are returned as-is (the common case).
        var needsFold = false;
        foreach (var ch in text)
        {
            if (ch > 0x7F || ch == '`')
            {
                needsFold = true;
                break;
            }
        }

        if (!needsFold)
        {
            return text;
        }

        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = FoldChar(chars[i]);
        }

        return new string(chars);
    }

    /// <summary>
    /// Whole-string comparison form: <see cref="Fold"/> + whitespace-run collapse + trim. Used for
    /// paragraph-level equality (e.g. "does this baseline paragraph carry the text the client's snapshot
    /// says this paraId belongs to?"). NOT 1:1 — never use where a char-position index must stay aligned.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var folded = Fold(text);
        var sb = new System.Text.StringBuilder(folded.Length);
        var pendingSpace = false;
        foreach (var ch in folded)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    // Per-character 1:1 fold. Code points verified against client MATCH_FOLD (usePendingRedline.ts).
    private static char FoldChar(char ch) => ch switch
    {
        // single quotes / apostrophes / prime / grave / acute -> straight apostrophe (U+0027)
        '‘' or '’' or '‚' or '‛' or '′' or 'ʼ' or '`' or '´' => '\'',
        // double quotes / double prime -> straight quote (U+0022)
        '“' or '”' or '„' or '‟' or '″' => '"',
        // non-breaking / figure / thin / narrow-no-break spaces -> regular space (U+0020)
        ' ' or ' ' or ' ' or ' ' => ' ',
        // hyphen / non-breaking hyphen / figure / en / em / horizontal-bar / minus -> hyphen-minus (U+002D)
        '‐' or '‑' or '‒' or '–' or '—' or '―' or '−' => '-',
        _ => ch,
    };
}

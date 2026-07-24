namespace Sprk.Bff.Api.Services.Communication.Engine.Rungs;

/// <summary>
/// Shared formatting for the deterministic name/number-match rungs' provenance mini-format
/// (<c>record-name-match:…:name="…":number="…":reason="…"</c> and the analogous
/// <c>contact-name-match:…</c>) emitted by <see cref="RecordNameMatchRung"/> and
/// <see cref="ContactNameMatchRung"/> and parsed by the CommunicationConnections review UI
/// (<c>provenance.ts → parseNameMatch</c>).
/// </summary>
/// <remarks>
/// <para>
/// The review-UI parser extracts each quote-delimited value with the key-anchored regex
/// <c>key="([^"]*)"</c> and has NO unescape step. A raw double-quote inside an interpolated
/// VALUE would therefore prematurely close its segment or inject a spurious key. The
/// engine-generated <c>reason</c> and the record's <c>number</c> never contain quotes, but the
/// user-controlled <c>name</c> (a record or contact display name) can — e.g.
/// <c>Smith "Slippery" Co</c>.
/// </para>
/// <para>
/// We neutralize the delimiter by replacing an embedded <c>"</c> with a single quote before
/// interpolation. This is deliberately SERVER-SIDE ONLY (no backslash escaping) so the existing
/// shipped PCF parser keeps working unchanged — a backslash escape would need a matching
/// client-side unescape + PCF rebundle. The provenance <c>name=</c> value is a diagnostic trace
/// (the review UI renders the resolved <c>targetName</c>, not the provenance name), so the
/// substitution has no user-visible effect; the real display name still rides on
/// <c>RungMatch.Target.Name</c> untouched.
/// </para>
/// </remarks>
internal static class RungProvenanceFormat
{
    /// <summary>
    /// Makes <paramref name="value"/> safe to embed inside a double-quote-delimited provenance
    /// segment by replacing embedded double-quotes with single quotes. Null/empty → empty string.
    /// </summary>
    public static string EscapeValue(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Replace('"', '\'');
}

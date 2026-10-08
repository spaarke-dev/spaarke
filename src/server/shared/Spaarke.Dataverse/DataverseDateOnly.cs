using System.Globalization;
using System.Text.Json;

namespace Spaarke.Dataverse;

/// <summary>
/// Reads and writes Dataverse calendar-date columns (the six <c>sprk_event</c> date columns: due, final due, base,
/// completed, approved, meeting) without any time-zone conversion.
/// </summary>
/// <remarks>
/// <para><b>Why (spaarke-ontology-platform-r1 task 098).</b> These columns were Format DateOnly / Behavior UserLocal:
/// Dataverse stored an instant and the Web API returned <c>"2026-10-02T00:00:00Z"</c>. <c>DateTime.Parse</c> turned
/// that into the server's local time (10/1 20:00 on an Eastern machine) and the DTO then carried a timestamp that clients
/// read as an instant, so the day moved by one wherever the reader was west of UTC. The columns are now Behavior DateOnly
/// in spaarkedev1, and the Web API returns and accepts <b>only</b> <c>"yyyy-MM-dd"</c> (a timestamp write is HTTP 400
/// "Cannot convert the literal … to the expected type 'Edm.Date'", verified live 2026-10-05).</para>
/// <para><b>Both shapes are read</b>, because other environments keep the old behaviour until
/// <c>docs/data-model/sprk_event-date-columns.md</c> §4 is run there. A timestamp's calendar date is its leading ten
/// characters. The Web API renders a UserLocal value in UTC (<c>…Z</c>), so for Dataverse's own output that is the UTC
/// date — exactly what the conversion (rule SpecificTimeZone, UTC) keeps, so a value reads as the same day before and
/// after an environment is converted. For a timestamp carrying another offset it is the date as written, never a
/// converted instant.</para>
/// <para>§11: <i>Existing</i> — <c>DataverseDateOnlyJsonConverter</c> in <c>Sprk.Bff.Api</c> applies the same
/// leading-ten-characters rule to <c>sprk_externalrecordaccess</c> dates, but it is <c>internal</c> to the BFF and works on a
/// <c>Utf8JsonReader</c>; this library cannot reference it, and the event read path maps a
/// <see cref="JsonElement"/> dictionary. <i>Extension</i> — the converter could delegate here later; it is owned by another
/// project's active work, so it is left untouched. <i>Cost of doing nothing</i> — the event read path keeps converting
/// dates to the machine's local time and the BFF writes the date in the server's culture.</para>
/// </remarks>
public static class DataverseDateOnly
{
    /// <summary>The only shape the Web API accepts for a DateOnly-behaviour column.</summary>
    public const string WireFormat = "yyyy-MM-dd";

    /// <summary>Formats <paramref name="value"/> for a Web API write: <c>yyyy-MM-dd</c>, invariant culture.</summary>
    public static string Format(DateOnly value) => value.ToString(WireFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a Web API date value: <c>yyyy-MM-dd</c> (DateOnly behaviour), or a timestamp <c>yyyy-MM-ddT…</c> from a
    /// column not yet converted, whose calendar date is its leading ten characters. Never converts to local time.
    /// </summary>
    /// <exception cref="FormatException"><paramref name="text"/> is neither shape.</exception>
    public static DateOnly Parse(string text)
    {
        if (TryParse(text, out var date))
            return date;

        throw new FormatException($"'{text}' is not a Dataverse date (yyyy-MM-dd, or yyyy-MM-ddT… from an unconverted column).");
    }

    /// <inheritdoc cref="Parse(string)"/>
    public static bool TryParse(string? text, out DateOnly date)
    {
        date = default;
        return text is { Length: >= 10 }
            && (text.Length == 10 || text[10] == 'T')
            && DateOnly.TryParseExact(text.AsSpan(0, 10), WireFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>
    /// Reads column <paramref name="column"/> from a Web API row: null when absent or JSON null, otherwise
    /// <see cref="Parse(string)"/>.
    /// </summary>
    public static DateOnly? Read(IReadOnlyDictionary<string, JsonElement> row, string column) =>
        row.TryGetValue(column, out var value) && value.ValueKind != JsonValueKind.Null
            ? Parse(value.GetString()!)
            : null;
}

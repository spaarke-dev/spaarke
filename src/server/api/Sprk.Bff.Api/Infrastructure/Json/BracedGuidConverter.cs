using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Infrastructure.Json;

/// <summary>
/// Provides pre-configured JsonSerializerOptions for Dataverse JSON payloads.
/// Dataverse webhooks send GUIDs with braces and dates in WCF format that System.Text.Json can't parse by default.
/// </summary>
public static class DataverseJsonOptions
{
    /// <summary>
    /// JSON serializer options for Dataverse webhook payloads and RemoteExecutionContext.
    /// Handles braced GUIDs "{guid}" and WCF dates "/Date(123)/" format automatically.
    /// </summary>
    public static JsonSerializerOptions Default { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new BracedGuidConverter(),
            new NullableBracedGuidConverter(),
            new WcfDateTimeConverter(),
            new NullableWcfDateTimeConverter()
        }
    };
}

/// <summary>
/// JSON converter that handles GUIDs with optional curly braces.
/// Dataverse sends GUIDs in format "{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}"
/// but System.Text.Json only accepts "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx".
/// This converter strips braces on read and writes standard format.
/// </summary>
public class BracedGuidConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrEmpty(value))
            return Guid.Empty;

        // Strip braces if present: "{guid}" -> "guid"
        if (value.StartsWith('{') && value.EndsWith('}'))
            value = value[1..^1];

        return Guid.Parse(value);
    }

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
    {
        // Write standard format without braces (D format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)
        writer.WriteStringValue(value.ToString("D"));
    }
}

/// <summary>
/// JSON converter that handles nullable GUIDs with optional curly braces.
/// Same as BracedGuidConverter but for Guid? properties.
/// </summary>
public class NullableBracedGuidConverter : JsonConverter<Guid?>
{
    public override Guid? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        var value = reader.GetString();
        if (string.IsNullOrEmpty(value))
            return null;

        // Strip braces if present: "{guid}" -> "guid"
        if (value.StartsWith('{') && value.EndsWith('}'))
            value = value[1..^1];

        if (Guid.TryParse(value, out var guid))
            return guid;

        return null;
    }

    public override void Write(Utf8JsonWriter writer, Guid? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value.ToString("D"));
        else
            writer.WriteNullValue();
    }
}

/// <summary>
/// JSON converter that handles WCF date format from Dataverse.
/// Dataverse sends dates in format "/Date(1234567890000)/" or "/Date(1234567890000+0000)/"
/// where the number is milliseconds since Unix epoch.
/// </summary>
public partial class WcfDateTimeConverter : JsonConverter<DateTime>
{
    // Pattern: /Date(1234567890000)/ or /Date(1234567890000+0000)/
    [GeneratedRegex(@"^/Date\((-?\d+)([+-]\d{4})?\)/$")]
    private static partial Regex WcfDatePattern();

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrEmpty(value))
            return default;

        // Try WCF format first: /Date(1234567890000)/
        var match = WcfDatePattern().Match(value);
        if (match.Success)
        {
            var milliseconds = long.Parse(match.Groups[1].Value);
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
        }

        // Fallback to standard ISO 8601 parsing
        if (DateTime.TryParse(value, out var dateTime))
            return dateTime;

        return default;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // Write ISO 8601 format
        writer.WriteStringValue(value.ToString("O"));
    }
}

/// <summary>
/// JSON converter that handles nullable WCF date format from Dataverse.
/// Same as WcfDateTimeConverter but for DateTime? properties.
/// </summary>
public partial class NullableWcfDateTimeConverter : JsonConverter<DateTime?>
{
    [GeneratedRegex(@"^/Date\((-?\d+)([+-]\d{4})?\)/$")]
    private static partial Regex WcfDatePattern();

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        var value = reader.GetString();
        if (string.IsNullOrEmpty(value))
            return null;

        // Try WCF format first: /Date(1234567890000)/
        var match = WcfDatePattern().Match(value);
        if (match.Success)
        {
            var milliseconds = long.Parse(match.Groups[1].Value);
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
        }

        // Fallback to standard ISO 8601 parsing
        if (DateTime.TryParse(value, out var dateTime))
            return dateTime;

        return null;
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value.ToString("O"));
        else
            writer.WriteNullValue();
    }
}

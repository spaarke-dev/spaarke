// -----------------------------------------------------------------------------
// NewtonsoftVerbatimKeysDictionaryConverter.cs
//
// unified-access-control-r2 task 165 — scope discovered by the owner-round-49
// production-serializer test (ProvisioningRunProductionSerializerTests) and added
// to the task under the standing directive (round 15; the POML's "scope discovered
// during execution is added to this task").
//
// WHY: CosmosModule.BuildCosmosClient sets CosmosPropertyNamingPolicy.CamelCase.
// The SDK implements that with Newtonsoft's CamelCasePropertyNamesContractResolver,
// whose naming strategy ALSO camel-cases DICTIONARY KEYS (ProcessDictionaryKeys =
// true). So a key is persisted with its first letter lowered: the reconciler's
// per-handler retry counter run.HandlerRetryAttempts["H9"] came back as "h9" — the
// next lookup of "H9" missed, every retry re-used attempt 1 and the same Service Bus
// MessageId, and duplicate detection dropped the second retry (task 107's counter
// never counted). RunParameters.NonSecret documents "persisted verbatim in Cosmos";
// it was not, for any key that starts upper-case.
//
// WHAT: the run's dictionaries are written with their keys VERBATIM and read back
// into a dictionary with the run model's comparer (values still go through the
// serializer's own contract, so camelCase property names, the enum converters and
// the evidence converter all still apply). The retry counter's dictionary is
// case-insensitive so a run persisted before this converter ("h9") still counts.
// -----------------------------------------------------------------------------

using Newtonsoft.Json;

namespace Sprk.Provisioning.ControlPlane.Models;

/// <summary>
/// Newtonsoft.Json converter for an <see cref="IDictionary{TKey,TValue}"/> keyed by string whose keys must persist
/// VERBATIM — the Cosmos SDK's camelCase option otherwise lowers each key's first letter.
/// </summary>
/// <typeparam name="TValue">The dictionary's value type (serialized through the serializer's own contract).</typeparam>
internal sealed class NewtonsoftVerbatimKeysDictionaryConverter<TValue> : Newtonsoft.Json.JsonConverter
{
    private readonly StringComparer _comparer;

    /// <summary>Keys compared ordinally (the run model's default).</summary>
    public NewtonsoftVerbatimKeysDictionaryConverter()
        : this(ignoreCase: false)
    {
    }

    /// <summary>Keys compared ordinally, or ignoring case when <paramref name="ignoreCase"/> (for keys persisted lowered before this converter).</summary>
    public NewtonsoftVerbatimKeysDictionaryConverter(bool ignoreCase)
    {
        _comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    /// <inheritdoc/>
    public override bool CanConvert(Type objectType) =>
        typeof(IDictionary<string, TValue>).IsAssignableFrom(objectType);

    /// <inheritdoc/>
    public override void WriteJson(JsonWriter writer, object? value, Newtonsoft.Json.JsonSerializer serializer)
    {
        if (value is not IDictionary<string, TValue> dictionary)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteStartObject();
        foreach (var (key, item) in dictionary)
        {
            writer.WritePropertyName(key); // verbatim — no naming strategy
            serializer.Serialize(writer, item, typeof(TValue));
        }

        writer.WriteEndObject();
    }

    /// <inheritdoc/>
    public override object? ReadJson(
        JsonReader reader, Type objectType, object? existingValue, Newtonsoft.Json.JsonSerializer serializer)
    {
        var result = new Dictionary<string, TValue>(_comparer);
        if (reader.TokenType == JsonToken.Null)
        {
            return result;
        }

        if (reader.TokenType != JsonToken.StartObject)
        {
            throw new JsonSerializationException(
                $"Expected a JSON object for a dictionary, found {reader.TokenType} at '{reader.Path}'.");
        }

        while (reader.Read() && reader.TokenType != JsonToken.EndObject)
        {
            if (reader.TokenType != JsonToken.PropertyName)
            {
                throw new JsonSerializationException($"Expected a property name at '{reader.Path}', found {reader.TokenType}.");
            }

            var key = (string)reader.Value!;
            reader.Read();
            result[key] = serializer.Deserialize<TValue>(reader)!;
        }

        return result;
    }
}

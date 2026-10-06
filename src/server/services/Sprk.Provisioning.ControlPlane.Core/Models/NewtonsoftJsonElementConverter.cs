// -----------------------------------------------------------------------------
// NewtonsoftJsonElementConverter.cs
//
// unified-access-control-r2 task 165, owner round 49 item 2 (the H8 resume must
// survive the production Cosmos serializer).
//
// WHY: the Cosmos SDK's DEFAULT serializer — the one CosmosModule.BuildCosmosClient
// configures, no custom STJ CosmosSerializer — is Newtonsoft.Json, and Newtonsoft
// has no notion of System.Text.Json.JsonElement. It serialized GateEntry.Evidence
// as the struct's one public property, {"valueKind":1}, and read that back as a
// DEFAULT JsonElement (ValueKind Undefined). Measured through the SDK's own
// serializer (CosmosClient.ClientOptions.Serializer): every gate's evidence was
// LOST on the first write, and a run read back from Cosmos then threw
// InvalidOperationException when GET /api/runs/{id} serialized it (STJ cannot write
// an Undefined JsonElement) — so the operator's run view failed for every run that
// had passed a gate with evidence.
//
// WHAT: the evidence is written as the raw JSON it holds and read back as a
// JsonElement of that JSON. Date-shaped strings stay strings (DateParseHandling is
// switched off while the value is copied), so the round trip is lossless for the
// shapes the handlers write (objects of strings, numbers, booleans, nulls, nested
// objects/arrays). A document written before this converter ({"valueKind":1}) reads
// back as that object — valid JSON, serializable — instead of an Undefined element.
//
// The H8 resume record does NOT live in evidence (owner round 49: typed fields,
// InterStepState.SpeContainerCreation); this converter makes evidence an honest
// operator record, which live gate (d) reads.
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using Newtonsoft.Json;

namespace Sprk.Provisioning.ControlPlane.Models;

/// <summary>
/// Newtonsoft.Json converter for <see cref="JsonElement"/> / <see cref="Nullable{JsonElement}"/> — the Cosmos SDK's
/// default serializer otherwise persists a <see cref="JsonElement"/> as <c>{"valueKind":1}</c> and loses it.
/// </summary>
internal sealed class NewtonsoftJsonElementConverter : Newtonsoft.Json.JsonConverter
{
    /// <inheritdoc/>
    public override bool CanConvert(Type objectType) =>
        objectType == typeof(JsonElement) || objectType == typeof(JsonElement?);

    /// <inheritdoc/>
    public override void WriteJson(JsonWriter writer, object? value, Newtonsoft.Json.JsonSerializer serializer)
    {
        if (value is JsonElement element && element.ValueKind != JsonValueKind.Undefined)
        {
            writer.WriteRawValue(element.GetRawText());
            return;
        }

        writer.WriteNull();
    }

    /// <inheritdoc/>
    public override object? ReadJson(
        JsonReader reader, Type objectType, object? existingValue, Newtonsoft.Json.JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return objectType == typeof(JsonElement?) ? null : default(JsonElement);
        }

        // Copy the token as JSON text. Dates must stay the strings they were written as: turn the reader's date
        // parsing off for the tokens still to be read (the current token is a StartObject / StartArray / primitive).
        var dateParseHandling = reader.DateParseHandling;
        reader.DateParseHandling = DateParseHandling.None;
        try
        {
            using var text = new StringWriter(CultureInfo.InvariantCulture);
            using (var copy = new JsonTextWriter(text) { DateFormatHandling = DateFormatHandling.IsoDateFormat })
            {
                copy.WriteToken(reader);
            }

            using var document = JsonDocument.Parse(text.ToString());
            return document.RootElement.Clone();
        }
        finally
        {
            reader.DateParseHandling = dateParseHandling;
        }
    }
}

using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Email body format. Maps to Dataverse sprk_bodyformat.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BodyFormat
{
    PlainText = 100000000,
    HTML = 100000001
}

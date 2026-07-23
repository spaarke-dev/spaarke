using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine.Detectors;

/// <summary>
/// Shared helper: the searchable text of an envelope (subject + plain body, with the HTML body as a
/// fallback) used by keyword/pattern-based structural detectors.
/// </summary>
internal static class DetectorText
{
    public static string Of(NormalizedMessage message)
    {
        var parts = new[] { message.Subject, message.BodyText, message.BodyHtml };
        return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}

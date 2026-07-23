using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine.Detectors;

/// <summary>
/// Detects a calendar invite — an iCalendar attachment (<c>text/calendar</c> content-type or a
/// <c>.ics</c> file). Resolves the <c>event</c> category + a <c>calendar-response</c> obligation.
/// </summary>
/// <remarks>
/// Resolving the specific <c>sprk_event</c> record (by iCal UID) is a follow-up needing a UID lookup
/// query + schema; task 014 resolves the category + obligation deterministically from structure.
/// </remarks>
public sealed class CalendarInviteDetector : IStructuralDetector
{
    public string Name => "calendar-invite";

    public StructuralMatch? Detect(NormalizedMessage message)
    {
        var hasIcs = message.Attachments.Any(a =>
            string.Equals(a.ContentType, "text/calendar", StringComparison.OrdinalIgnoreCase)
            || (a.Name?.EndsWith(".ics", StringComparison.OrdinalIgnoreCase) ?? false));

        if (!hasIcs)
            return null;

        return new StructuralMatch
        {
            Category = "event",
            Obligations = new[] { "calendar-response" },
            Confidence = 0.90,
            Provenance = "structural:calendar-invite:ics-attachment",
        };
    }
}

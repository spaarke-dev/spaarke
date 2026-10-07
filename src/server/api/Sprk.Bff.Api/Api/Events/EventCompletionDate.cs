using System.Security.Claims;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Context;

namespace Sprk.Bff.Api.Api.Events;

/// <summary>
/// The date <c>POST /api/v1/events/{id}/complete</c> writes to <c>sprk_completeddate</c>: today's date in the
/// completing user's own Dataverse time zone.
/// </summary>
/// <remarks>
/// <para><b>Why (spaarke-ontology-platform-r1 task 098).</b> The route wrote <c>DateTime.UtcNow</c>'s date, so a task
/// completed at 23:49 Eastern on June 3 was recorded as June 4 (live: event a0cb27af, stored 2026-06-04T03:49:16Z,
/// hand-corrected to 2026-06-03 during the Date Only conversion). <c>sprk_completeddate</c> is now a calendar date
/// (Behavior DateOnly), and the date has to be the one the user would have picked.</para>
/// <para><b>The choice: the caller's time zone, not a client-supplied date.</b> The source is the caller's Dataverse
/// personal time zone (<c>usersettings.timezonecode</c>, read by <see cref="DataverseUserTimeZone"/>). The model-driven app
/// uses that same setting when the user picks a date in a form, so a completion through the API lands on the day the same
/// user would have entered by hand. The only caller of this route today is the Copilot agent (<c>completeEvent</c>), which
/// has no reliable notion of the user's local date. Deriving the date on the server also keeps it a server-owned value
/// (DATAVERSE-WRITE-PATH-ARCHITECTURE WP-1) that a client cannot back-date.</para>
/// <para><b>Fallback: the UTC date</b> (the former behaviour), logged as a warning, when the caller or their zone cannot
/// be resolved. Completing a task never fails because its date could not be localised.</para>
/// <para>§11: <i>Existing</i> — <see cref="DataverseUserTimeZone"/> owns the time-zone read; this class adds only the
/// caller resolution. <i>Extension</i> — it reuses <see cref="ICallerSystemUserResolver"/> and
/// <see cref="IGenericEntityService"/>, both already injected into the event routes; no new interface, service or DI
/// registration. <i>Cost of doing nothing</i> — every completion after about 20:00 Eastern is dated the next day.</para>
/// </remarks>
internal static class EventCompletionDate
{
    /// <summary>The calendar date of <paramref name="utcNow"/> in <paramref name="zone"/>; the UTC date when the zone is
    /// unknown.</summary>
    internal static DateOnly LocalDate(DateTimeOffset utcNow, TimeZoneInfo? zone) => DataverseUserTimeZone.LocalDate(utcNow, zone);

    /// <summary>
    /// Today's date for <paramref name="caller"/> in their Dataverse time zone, or the UTC date (logged) when the caller
    /// or their zone cannot be resolved.
    /// </summary>
    internal static async Task<DateOnly> ForCallerAsync(
        ClaimsPrincipal caller,
        ICallerSystemUserResolver callerResolver,
        IGenericEntityService entities,
        TimeProvider clock,
        ILogger logger,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        string? unresolved;
        DateOnly today;

        try
        {
            var resolution = await callerResolver.ResolveAsync(caller, ct);
            if (!resolution.IsResolved || !Guid.TryParse(resolution.SystemUserId, out var systemUserId) || systemUserId == Guid.Empty)
            {
                (today, unresolved) = (LocalDate(now, null), "caller-" + (resolution.UnresolvedReason ?? "unresolved"));
            }
            else
            {
                (today, unresolved) = await DataverseUserTimeZone.TodayForUserAsync(entities, systemUserId, now, ct);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "[EVENTS] Completion date falls back to the UTC date ({Reason}).", "lookup-failed");
            return LocalDate(now, null);
        }

        if (unresolved is not null)
            logger.LogWarning("[EVENTS] Completion date falls back to the UTC date ({Reason}).", unresolved);

        return today;
    }
}

using System.Collections.Concurrent;
using Microsoft.Xrm.Sdk.Query;

namespace Spaarke.Dataverse;

/// <summary>
/// "Today" for a Dataverse user, in that user's own Dataverse time zone (<c>usersettings.timezonecode</c>) — the setting
/// the model-driven app uses when that user picks a date. Used wherever the server writes a calendar date FOR a user into
/// a Date Only column (spaarke-ontology-platform-r1 task 098: the sprk_event completion date, the RI follow-up task's due
/// dates).
/// </summary>
/// <remarks>
/// <para>Dataverse names its zones with Windows ids (<c>timezonedefinition.standardname</c>, e.g. "Eastern Standard
/// Time"), which <see cref="TimeZoneInfo.TryFindSystemTimeZoneById"/> resolves on Windows and, via ICU, on Linux. A zone
/// that cannot be read yields the UTC date and an identifier-only reason for the caller to log — never an exception,
/// because a missing time zone must not fail the write it dates.</para>
/// <para>Read app-only through <see cref="IGenericEntityService"/>; the BFF app user holds <c>prvReadUserSettings</c> at
/// Global depth in spaarkedev1 (checked 2026-10-05, docs/data-model/sprk_event-date-columns.md §1).</para>
/// <para>§11: <i>Existing</i> — none (no other code reads a user's time zone; searched <c>timezonecode</c>,
/// <c>usersettings</c>, <c>TimeZoneInfo</c>). <i>Extension</i> — task 098 first wrote this inside the BFF's
/// <c>EventCompletionDate</c>; when the RI action service (a different BFF feature, same rule) needed it, it moved here
/// beside <see cref="DataverseDateOnly"/> rather than being copied. <i>Cost of doing nothing</i> — each writer would date
/// "today" in UTC: after ~20:00 Eastern, tomorrow.</para>
/// </remarks>
public static class DataverseUserTimeZone
{
    /// <summary>Dataverse time-zone code → zone. Platform reference data: successes are cached for the process
    /// lifetime; failures are not. Tests share this process-wide cache, so a test that seeds a code with a DIFFERENT
    /// standard name than real data uses a code no other test uses (e.g. 9035).</summary>
    private static readonly ConcurrentDictionary<int, TimeZoneInfo> ZonesByCode = new();

    /// <summary>The calendar date of <paramref name="utcNow"/> in <paramref name="zone"/>; the UTC date when the zone is
    /// unknown.</summary>
    public static DateOnly LocalDate(DateTimeOffset utcNow, TimeZoneInfo? zone) =>
        DateOnly.FromDateTime(zone is null ? utcNow.UtcDateTime : TimeZoneInfo.ConvertTime(utcNow, zone).DateTime);

    /// <summary>
    /// Today for <paramref name="systemUserId"/>, read app-only through <paramref name="entities"/>.
    /// <c>FallbackReason</c> is null when the user's zone was used, else an identifier (<c>no-timezonecode</c>,
    /// <c>unknown-timezonecode-N</c>, <c>lookup-failed</c>) and the date is the UTC date.
    /// </summary>
    public static async Task<(DateOnly Today, string? FallbackReason)> TodayForUserAsync(
        IGenericEntityService entities, Guid systemUserId, DateTimeOffset utcNow, CancellationToken ct)
    {
        var day = await UserDayAsync(
            async c => (await entities.RetrieveAsync("usersettings", systemUserId, ["timezonecode"], c).ConfigureAwait(false))
                .GetAttributeValue<int?>("timezonecode"),
            async (code, c) =>
            {
                var query = new QueryExpression("timezonedefinition") { ColumnSet = new ColumnSet("standardname"), TopCount = 1 };
                query.Criteria.AddCondition("timezonecode", ConditionOperator.Equal, code);
                var rows = await entities.RetrieveMultipleAsync(query, c).ConfigureAwait(false);
                return rows.Entities.FirstOrDefault()?.GetAttributeValue<string>("standardname");
            },
            utcNow,
            ct).ConfigureAwait(false);
        return (day.Today, day.FallbackReason);
    }

    /// <summary>
    /// The core: the user's zone and today, from two reads the caller supplies — the user's
    /// <c>usersettings.timezonecode</c> and a code's <c>timezonedefinition.standardname</c>. Lets a caller that must
    /// not hold an app-only client (the Daily Briefing collector reads only AS the user) use the same rule.
    /// <c>Zone</c> is null exactly when <c>FallbackReason</c> is set (the UTC date is then returned).
    /// </summary>
    public static async Task<UserDay> UserDayAsync(
        Func<CancellationToken, Task<int?>> readTimeZoneCode,
        Func<int, CancellationToken, Task<string?>> readStandardName,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        try
        {
            if (await readTimeZoneCode(ct).ConfigureAwait(false) is not { } code)
                return new UserDay(LocalDate(utcNow, null), null, "no-timezonecode");

            var zone = ZonesByCode.TryGetValue(code, out var cached) ? cached : null;
            if (zone is null)
            {
                var name = await readStandardName(code, ct).ConfigureAwait(false);
                if (name is not null && TimeZoneInfo.TryFindSystemTimeZoneById(name, out var found))
                    ZonesByCode[code] = zone = found;
            }

            return zone is null
                ? new UserDay(LocalDate(utcNow, null), null, $"unknown-timezonecode-{code}")
                : new UserDay(LocalDate(utcNow, zone), zone, null);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Includes an HttpClient timeout (a TaskCanceledException the CALLER did not request): a time zone that
            // cannot be read never fails the work it dates — only the caller's own cancellation propagates.
            return new UserDay(LocalDate(utcNow, null), null, "lookup-failed");
        }
    }
}

/// <summary>A user's "today" and the zone it came from (null when <paramref name="FallbackReason"/> says why the UTC date
/// was used instead).</summary>
public sealed record UserDay(DateOnly Today, TimeZoneInfo? Zone, string? FallbackReason);

using System.Collections.Concurrent;
using Microsoft.Xrm.Sdk;
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
        var day = await UserDayForUserAsync(entities, systemUserId, utcNow, ct).ConfigureAwait(false);
        return (day.Today, day.FallbackReason);
    }

    /// <summary>
    /// <see cref="TodayForUserAsync"/> with the zone it came from, which <see cref="DataverseRecipientDays"/> needs to tell
    /// "the user's zone" apart from "the UTC fallback".
    /// </summary>
    public static Task<UserDay> UserDayForUserAsync(
        IGenericEntityService entities, Guid systemUserId, DateTimeOffset utcNow, CancellationToken ct) =>
        UserDayAsync(
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
            ct);

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

/// <summary>Whose zone a <see cref="RecipientDay"/> came from.</summary>
public enum RecipientDaySource
{
    /// <summary>The assignee's zone (the systemuser the assignee contact represents).</summary>
    Assignee,

    /// <summary>The owner's zone (the owner is a systemuser and the assignee gave no zone).</summary>
    Owner,

    /// <summary>Neither gave a zone: the UTC date. The caller logs a warning with the reason.</summary>
    Utc,
}

/// <summary>"Today" for the person an item is FOR, where it came from, and, when it is not the assignee's, why not.</summary>
public sealed record RecipientDay(DateOnly Today, RecipientDaySource Source, Guid? SystemUserId, string? FallbackReason);

/// <summary>
/// The "today" of the person an item is FOR: the <b>assignee's</b> time zone, else the <b>owner's</b>, else <b>UTC</b>
/// (owner decision D-25, 2026-10-06; spec FR-47, where the nightly Do-lane evaluator, task 031, uses this same helper).
/// One instance per run: each distinct contact is mapped to a user once, and each distinct user's zone is read once.
/// </summary>
/// <remarks>
/// <para><b>The assignee is a contact.</b> <c>sprk_assignedto</c> and the responsible-contact columns are contact
/// lookups, but a time zone belongs to a <c>systemuser</c> (<c>usersettings.timezonecode</c>). The contact's user is the
/// one task 141 links it to: <c>systemuser.sprk_primarycontact</c>, else the user whose Entra oid the contact's
/// <c>sprk_externalobjectid</c> carries. That is the reverse of the identity rule in <c>PersonIdentity.ContactId</c>.
/// Only an ENABLED user counts, and only when there is exactly one. A contact that represents no user (an external
/// person) or several (a collision) gives no zone, and the owner is tried.</para>
/// <para><b>The owner</b> gives a zone only when it is a <c>systemuser</c>; a team has no time zone.</para>
/// <para>Reads are app-only through <see cref="IGenericEntityService"/>. A read that fails is a fallback, never an
/// exception (as in <see cref="DataverseUserTimeZone"/>); only the caller's own cancellation propagates.</para>
/// <para>§11: <i>Existing</i>: <see cref="DataverseUserTimeZone"/> (one user's today), reused for every zone read here.
/// <i>Extension</i>: the per-run caches and the contact-to-user step need state, so they cannot be static members of it.
/// <i>Cost of doing nothing</i>: To Do generation judges "overdue" and "due within N days" by the UTC date, so after
/// 20:00 Eastern an item due today is reported overdue a day early (D-25).</para>
/// </remarks>
public sealed class DataverseRecipientDays
{
    private readonly IGenericEntityService _entities;
    private readonly DateTimeOffset _utcNow;
    private readonly Dictionary<Guid, (Guid? User, string? Reason)> _userOfContact = new();
    private readonly Dictionary<Guid, UserDay> _dayOfUser = new();

    /// <param name="entities">App-only reader.</param>
    /// <param name="utcNow">The run's instant. Every "today" this instance returns is that instant's date somewhere.</param>
    public DataverseRecipientDays(IGenericEntityService entities, DateTimeOffset utcNow)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _utcNow = utcNow;
    }

    /// <summary>The run's UTC date, which is the last fallback.</summary>
    public DateOnly UtcToday => DataverseUserTimeZone.LocalDate(_utcNow, null);

    /// <summary>
    /// Every date "today" can be for anyone at this instant: the UTC date and the day either side (zones run from
    /// UTC-12 to UTC+14). A judgement that comes out the same for all three needs no lookup.
    /// </summary>
    public IReadOnlyList<DateOnly> PossibleTodays => [UtcToday.AddDays(-1), UtcToday, UtcToday.AddDays(1)];

    /// <summary>Today for the item whose assignee is <paramref name="assigneeContactId"/> and owner is <paramref name="owner"/>.</summary>
    public async Task<RecipientDay> ForAsync(Guid? assigneeContactId, EntityReference? owner, CancellationToken ct)
    {
        string assigneeReason;
        if (assigneeContactId is { } contact && contact != Guid.Empty)
        {
            var (user, reason) = await UserOfContactAsync(contact, ct).ConfigureAwait(false);
            if (user is { } assigneeUser)
            {
                var day = await DayOfUserAsync(assigneeUser, ct).ConfigureAwait(false);
                if (day.Zone is not null)
                    return new RecipientDay(day.Today, RecipientDaySource.Assignee, assigneeUser, null);
                assigneeReason = $"assignee-{day.FallbackReason}";
            }
            else
            {
                assigneeReason = $"assignee-{reason}";
            }
        }
        else
        {
            assigneeReason = "no-assignee";
        }

        string ownerReason;
        if (owner is { } o && o.Id != Guid.Empty && string.Equals(o.LogicalName, "systemuser", StringComparison.OrdinalIgnoreCase))
        {
            var day = await DayOfUserAsync(o.Id, ct).ConfigureAwait(false);
            if (day.Zone is not null)
                return new RecipientDay(day.Today, RecipientDaySource.Owner, o.Id, assigneeReason);
            ownerReason = $"owner-{day.FallbackReason}";
        }
        else
        {
            ownerReason = owner is null || owner.Id == Guid.Empty ? "no-owner" : $"owner-is-{owner.LogicalName}";
        }

        return new RecipientDay(UtcToday, RecipientDaySource.Utc, null, $"{assigneeReason};{ownerReason}");
    }

    private async Task<UserDay> DayOfUserAsync(Guid user, CancellationToken ct)
    {
        if (!_dayOfUser.TryGetValue(user, out var day))
        {
            day = await DataverseUserTimeZone.UserDayForUserAsync(_entities, user, _utcNow, ct).ConfigureAwait(false);
            _dayOfUser[user] = day;
        }

        return day;
    }

    private async Task<(Guid? User, string? Reason)> UserOfContactAsync(Guid contact, CancellationToken ct)
    {
        if (_userOfContact.TryGetValue(contact, out var known))
            return known;

        (Guid? User, string? Reason) result;
        try
        {
            var linked = await EnabledUsersWhereAsync("sprk_primarycontact", contact, ct).ConfigureAwait(false);
            if (linked.Count == 0
                && (await _entities.RetrieveAsync("contact", contact, ["sprk_externalobjectid"], ct).ConfigureAwait(false))
                    ?.GetAttributeValue<string>("sprk_externalobjectid") is { } rawOid
                && Guid.TryParse(rawOid, out var oid) && oid != Guid.Empty)
            {
                linked = await EnabledUsersWhereAsync("azureactivedirectoryobjectid", oid, ct).ConfigureAwait(false);
            }

            result = linked.Count switch
            {
                1 => (linked[0], null),
                0 => (null, "not-a-user"),
                _ => (null, "represents-several-users"),
            };
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            result = (null, "contact-lookup-failed");
        }

        _userOfContact[contact] = result;
        return result;
    }

    private async Task<IReadOnlyList<Guid>> EnabledUsersWhereAsync(string column, Guid value, CancellationToken ct)
    {
        var query = new QueryExpression("systemuser") { ColumnSet = new ColumnSet("systemuserid"), TopCount = 2 };
        query.Criteria.AddCondition(column, ConditionOperator.Equal, value);
        query.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);
        var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return rows.Entities.Select(e => e.Id).Where(id => id != Guid.Empty).ToList();
    }
}

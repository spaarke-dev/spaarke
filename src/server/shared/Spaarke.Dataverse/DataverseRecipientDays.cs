using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Spaarke.Dataverse;

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
/// <param name="Error">The first read failure behind a fallback (a time-zone or contact read), for the caller to log.</param>
public sealed record RecipientDay(
    DateOnly Today, RecipientDaySource Source, Guid? SystemUserId, string? FallbackReason, Exception? Error = null);

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
    private readonly Dictionary<Guid, (Guid? User, string? Reason, Exception? Error)> _userOfContact = new();
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
        Exception? error = null;
        if (assigneeContactId is { } contact && contact != Guid.Empty)
        {
            var (user, reason, contactError) = await UserOfContactAsync(contact, ct).ConfigureAwait(false);
            if (user is { } assigneeUser)
            {
                var day = await DayOfUserAsync(assigneeUser, ct).ConfigureAwait(false);
                if (day.Zone is not null)
                    return new RecipientDay(day.Today, RecipientDaySource.Assignee, assigneeUser, null);
                assigneeReason = $"assignee-{day.FallbackReason}";
                error = day.Error;
            }
            else
            {
                assigneeReason = $"assignee-{reason}";
                error = contactError;
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
                return new RecipientDay(day.Today, RecipientDaySource.Owner, o.Id, assigneeReason, error);
            ownerReason = $"owner-{day.FallbackReason}";
            error ??= day.Error;
        }
        else
        {
            ownerReason = owner is null || owner.Id == Guid.Empty ? "no-owner" : $"owner-is-{owner.LogicalName}";
        }

        return new RecipientDay(UtcToday, RecipientDaySource.Utc, null, $"{assigneeReason};{ownerReason}", error);
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

    private async Task<(Guid? User, string? Reason, Exception? Error)> UserOfContactAsync(Guid contact, CancellationToken ct)
    {
        if (_userOfContact.TryGetValue(contact, out var known))
            return known;

        (Guid? User, string? Reason, Exception? Error) result;
        try
        {
            var linked = await EnabledUsersWhereAsync("sprk_primarycontact", contact, ct).ConfigureAwait(false);
            if (linked.Count == 0
                && (await _entities.RetrieveAsync("contact", contact, ["sprk_externalobjectid"], ct).ConfigureAwait(false))
                    ?.GetAttributeValue<string>("sprk_externalobjectid") is { } rawOid
                && Guid.TryParse(rawOid, out var oid) && oid != Guid.Empty)
            {
                linked = await EnabledSystemUsersWithEntraOidAsync(oid, ct).ConfigureAwait(false);
            }

            result = linked.Count switch
            {
                1 => (linked[0], null, null),
                0 => (null, "not-a-user", null),
                _ => (null, "represents-several-users", null),
            };
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            result = (null, "contact-lookup-failed", ex);
        }

        _userOfContact[contact] = result;
        return result;
    }

    // The Entra oid column lives on systemuser, never on contact (ContactAadObjectIdColumnGuardTests).
    private Task<IReadOnlyList<Guid>> EnabledSystemUsersWithEntraOidAsync(Guid oid, CancellationToken ct) =>
        EnabledUsersWhereAsync("azureactivedirectoryobjectid", oid, ct);

    private async Task<IReadOnlyList<Guid>> EnabledUsersWhereAsync(string column, Guid value, CancellationToken ct)
    {
        var query = new QueryExpression("systemuser") { ColumnSet = new ColumnSet("systemuserid"), TopCount = 2 };
        query.Criteria.AddCondition(column, ConditionOperator.Equal, value);
        query.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);
        var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return rows.Entities.Select(e => e.Id).Where(id => id != Guid.Empty).ToList();
    }
}

// unified-access-control-r2 task 152 — server-created to-dos and tasks name a PERSON.
//
// Every BFF create is app-only, so Created By is the BFF application user and can never say who a server-created
// to-do is FOR. The #1044 split agreed with word-add-in-r1 (session 27, "Peer follow-through") makes "Assigned To"
// carry the person instead, and the people-targeting surface (ADR-034 A3) finds the to-do through it. This is the one
// precedence rule the BFF's server writers apply (TodoGenerationService, TaskActionCore). The external portal's
// to-do create applies the same rule with the calling contact as the triggering person and needs no parent read.
// The Office CreateTodoAsync is word-add-in-r1 task 083's and does not use this helper.

using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Fills <c>sprk_assignedto</c> (a CONTACT lookup on <c>sprk_todo</c> and on <c>sprk_event</c>) on a server-created
/// record when the request named no assignee: (1) the TRIGGERING person's contact, else (2) the regarding parent's
/// responsible internal contact (<c>sprk_assignedtointernal</c>, then <c>sprk_assignedattorney1</c>), else blank with a
/// structured <c>todo_unassigned</c> warning.
/// </summary>
/// <remarks>
/// <para>Never a team (a team-owned to-do must not fan out — owner round 2 item 9). Never an email or name match (the
/// C7 hijack path): the triggering person's contact comes only from task 141's link
/// (<c>PersonIdentity.ContactId</c>) or from a resolved calling contact. A supplied assignee is NEVER overwritten.</para>
/// <para>A parent read that fails leaves the column blank (logged) rather than failing the create — the to-do still
/// exists and is still reachable by its owner team; only its attention target is missing, and the log says so.</para>
/// </remarks>
internal static class AssignedToDefaults
{
    /// <summary>The person column the people-targeting surface matches (registry Contact-typed on both tables).</summary>
    internal const string AssignedToAttribute = "sprk_assignedto";

    /// <summary>The parent's responsible internal contact columns, in precedence order.</summary>
    internal static readonly IReadOnlyList<string> ResponsibleContactColumns =
        new[] { "sprk_assignedtointernal", "sprk_assignedattorney1" };

    /// <summary>
    /// Parents that carry both responsible columns (registry-listed Contact columns on each — MembershipOptions'
    /// canonical registry; verified live on sprk_matter 2026-10-02). Any other parent (invoice, communication, …)
    /// defers to the core record stamped on the new row (FR-26), which is always one of the first three.
    /// </summary>
    private static readonly HashSet<string> ParentsWithResponsibleColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_matter", "sprk_project", "sprk_workassignment", "sprk_event",
    };

    /// <summary>The core-ancestor lookups FR-26 stamps on a child row, in the order they are consulted.</summary>
    private static readonly IReadOnlyList<(string Attribute, string Entity)> CoreLookups = new[]
    {
        ("sprk_regardingmatter", "sprk_matter"),
        ("sprk_regardingproject", "sprk_project"),
        ("sprk_regardingworkassignment", "sprk_workassignment"),
    };

    /// <summary>How <c>sprk_assignedto</c> was decided — logged, and asserted by tests.</summary>
    internal enum Outcome
    {
        /// <summary>The request supplied an assignee; it was kept.</summary>
        Supplied,
        /// <summary>The triggering person's contact.</summary>
        TriggeringPerson,
        /// <summary>The regarding parent's responsible internal contact.</summary>
        ParentResponsibleContact,
        /// <summary>Nobody could be named; the column is blank and <c>todo_unassigned</c> was logged.</summary>
        Unassigned,
    }

    /// <summary>
    /// Applies the precedence rule to <paramref name="record"/> (which must already carry its regarding lookups and
    /// core-ancestor stamps when it has a parent).
    /// </summary>
    /// <param name="dataverse">App-only reader for the parent's responsible contact (a read of a lookup id, never shown).</param>
    /// <param name="record">The <c>sprk_todo</c> / <c>sprk_event</c> about to be created.</param>
    /// <param name="triggeringContactId">The triggering person's contact, when there is one.</param>
    /// <param name="parentEntity">The regarding parent's logical name, when there is one.</param>
    /// <param name="parentId">The regarding parent's id.</param>
    /// <param name="logger">Logger for the structured outcome.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<Outcome> ApplyAsync(
        IGenericEntityService dataverse,
        Entity record,
        Guid? triggeringContactId,
        string? parentEntity,
        Guid? parentId,
        ILogger logger,
        CancellationToken ct)
    {
        if (record.Contains(AssignedToAttribute)
            && record[AssignedToAttribute] is EntityReference supplied
            && supplied.Id != Guid.Empty)
        {
            return Outcome.Supplied;
        }

        if (triggeringContactId is { } contactId && contactId != Guid.Empty)
        {
            record[AssignedToAttribute] = new EntityReference("contact", contactId);
            return Outcome.TriggeringPerson;
        }

        var (responsible, readFailed) = await ReadResponsibleContactAsync(
            dataverse, record, parentEntity, parentId, logger, ct).ConfigureAwait(false);
        if (responsible is { } responsibleId)
        {
            record[AssignedToAttribute] = new EntityReference("contact", responsibleId);
            return Outcome.ParentResponsibleContact;
        }

        logger.LogWarning(
            "todo_unassigned: entity={Entity} parentEntity={ParentEntity} parentId={ParentId} reason={Reason} — no triggering "
            + "person and no responsible internal contact on the parent; sprk_assignedto left blank (never a team, never an "
            + "email/name match)",
            record.LogicalName,
            parentEntity ?? "none",
            parentId,
            readFailed ? "parent_read_failed" : "no_person_to_name");
        return Outcome.Unassigned;
    }

    private static async Task<(Guid? ContactId, bool ReadFailed)> ReadResponsibleContactAsync(
        IGenericEntityService dataverse,
        Entity record,
        string? parentEntity,
        Guid? parentId,
        ILogger logger,
        CancellationToken ct)
    {
        // The direct parent when it carries the columns; otherwise the core record FR-26 stamped on this row.
        var candidates = new List<(string Entity, Guid Id)>();
        if (!string.IsNullOrWhiteSpace(parentEntity)
            && parentId is { } pid && pid != Guid.Empty
            && ParentsWithResponsibleColumns.Contains(parentEntity))
        {
            candidates.Add((parentEntity, pid));
        }
        else
        {
            foreach (var (attribute, entity) in CoreLookups)
            {
                if (record.Contains(attribute) && record[attribute] is EntityReference core && core.Id != Guid.Empty)
                {
                    candidates.Add((entity, core.Id));
                    break;
                }
            }
        }

        var readFailed = false;
        foreach (var (entity, id) in candidates)
        {
            try
            {
                var row = await dataverse
                    .RetrieveAsync(entity, id, ResponsibleContactColumns.ToArray(), ct)
                    .ConfigureAwait(false);
                foreach (var column in ResponsibleContactColumns)
                {
                    if (row is not null && row.Contains(column) && row[column] is EntityReference contact && contact.Id != Guid.Empty)
                    {
                        return (contact.Id, false);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                readFailed = true;
                logger.LogWarning(ex,
                    "AssignedToDefaults: reading the responsible contact of {Entity}({Id}) failed", entity, id);
            }
        }

        return (null, readFailed);
    }
}

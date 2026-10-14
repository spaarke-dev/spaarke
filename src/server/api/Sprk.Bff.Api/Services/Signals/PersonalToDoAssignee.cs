using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Services.Signals;

// =====================================================================================================================
// D-134 / D-135 (owner, 2026-10-12): a row about a PERSONAL To Do (a sprk_todo filed under nothing) is owned by the
// business-unit team of the To Do's Assigned To user, not the To Do's own team. This is the ONE place that answers "who is
// that user": sprk_todo.sprk_assignedto (a contact) -> the systemuser whose sprk_primarycontact names it (task 141's link),
// enabled only. Shared by the Decision Record (task 043, DecisionCommitService) and, next, the Signal (task 143, SignalWriter).
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: AssignedAccessStore.ReadLinkCandidatesAsync (uac-r2 task 142) already reads "the systemusers a contact
//     represents" with isdisabled; IContactIdentityStore.FindSystemUsersLinkingAsync reads the same link but returns no
//     isdisabled; AssignedToDefaults WRITES sprk_assignedto and never reads it back as a person.
//   Extension: this adds no Dataverse plumbing: it is a static rule over those two existing injected reads (the To Do
//     through IGenericEntityService, the link through AssignedAccessStore). Not a DI service: it holds no state.
//   Cost of doing nothing: D-134 cannot be applied; the Decision Record (and later the Signal) about a personal To Do stays
//     owned by the To Do creator's team, which the assignee in another business unit cannot read.
// =====================================================================================================================

/// <summary>
/// The ONE enabled systemuser a personal To Do's Assigned To names (D-134), or <c>null</c> for "no single user" — the caller
/// then keeps its ordinary answer (the To Do's own team).
/// </summary>
/// <remarks>
/// <para><b>No single user</b> (D-135 items 1, 5, 6): the To Do does not exist, is filed under a record (a core filing takes
/// precedence), or has no Assigned To; or no systemuser links to that contact, several enabled ones do, or only disabled ones
/// do. A contact that does not exist simply has no linked user.</para>
/// <para><b>A fault is not that answer.</b> A Dataverse fault reading the To Do or the systemusers PROPAGATES: the caller fails
/// closed (it does not know whether a user exists, so it must not fall back).</para>
/// <para>Item 7 (a secure parent wins) is the caller's: this answers only who the assignee is.</para>
/// </remarks>
internal static class PersonalToDoAssignee
{
    internal const string Table = "sprk_todo";

    /// <summary>The To Do's filing lookups. "Filed" is any regarding at all (owner K2: only a To Do filed under NOTHING is
    /// personal): every lookup in <see cref="TodoRegardingBuilder.AllRegardingLookups"/> — the single list both clients write,
    /// identity parents (<c>sprk_regardingorganization</c>, <c>sprk_regardingcontact</c>) included — UNION uac-r2's lineage entry
    /// for <c>sprk_todo</c> and the four core-ancestor lookups (<see cref="CoreAncestorResolver.CoreAncestorLookups"/>), which
    /// carry the columns that list lacks (<c>sprk_regardingagreement</c>, <c>sprk_regardingreportcard</c>,
    /// <c>sprk_regardingservicerequest</c>). Any value set means it is filed.</summary>
    internal static readonly IReadOnlyList<string> FilingColumns = TodoRegardingBuilder.AllRegardingLookups
        .Concat(SecureChildLineage.Children[Table].Lookups.Keys)
        .Concat(CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>The ADR-024 polymorphic pair: a To Do whose pair names a record is filed even with no typed lookup set.</summary>
    private static readonly string[] PairColumns =
        [TodoRegardingBuilder.FieldRegardingRecordType, TodoRegardingBuilder.FieldRegardingRecordId];

    private static readonly string[] Columns = [AssignedToDefaults.AssignedToAttribute, .. FilingColumns, .. PairColumns];

    /// <summary>Reads the To Do app-only and answers its Assigned To's one enabled systemuser; <c>null</c> for no single user.</summary>
    /// <exception cref="Exception">A Dataverse fault (other than the row not existing) propagates.</exception>
    internal static async Task<Guid?> ResolveAsync(
        IGenericEntityService entities, AssignedAccessStore links, Guid todoId, CancellationToken ct) =>
        (await ClassifyAsync(entities, links, todoId, ct).ConfigureAwait(false)).UserId;

    /// <summary>
    /// <see cref="ResolveAsync"/> with WHY there is no single user (task 148: the follow-the-assignee job counts each fallback
    /// separately). The same reads and the same rule; <see cref="Answer.UserId"/> is <see cref="ResolveAsync"/>'s answer.
    /// </summary>
    /// <exception cref="Exception">A Dataverse fault (other than the row not existing) propagates.</exception>
    internal static async Task<Answer> ClassifyAsync(
        IGenericEntityService entities, AssignedAccessStore links, Guid todoId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(links);

        Entity? todo;
        try
        {
            todo = await entities.RetrieveAsync(Table, todoId, Columns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && IsNotFound(ex))
        {
            return new Answer(null, Outcome.ToDoNotFound);
        }

        if (todo is null)
            return new Answer(null, Outcome.ToDoNotFound);
        if (IsFiled(todo))
            return new Answer(null, Outcome.Filed);
        if (todo.GetAttributeValue<EntityReference>(AssignedToDefaults.AssignedToAttribute) is not { } assignedTo || assignedTo.Id == Guid.Empty)
            return new Answer(null, Outcome.NoAssignedTo);

        return await ClassifyLinkedUsersAsync(links, assignedTo.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Why <see cref="ClassifyAsync"/> named a user or none.</summary>
    internal enum Outcome
    {
        /// <summary>Exactly one eligible user: <see cref="Answer.UserId"/> is set.</summary>
        SingleUser,

        /// <summary>The To Do does not exist.</summary>
        ToDoNotFound,

        /// <summary>The To Do is filed under something (any regarding lookup, or the pair): not personal (owner K2).</summary>
        Filed,

        /// <summary>The To Do has no Assigned To.</summary>
        NoAssignedTo,

        /// <summary>No systemuser links to the Assigned To contact.</summary>
        NoLinkedUser,

        /// <summary>Several eligible systemusers link to the contact (D-135 item 5).</summary>
        SeveralUsers,

        /// <summary>Only ineligible users link to it: disabled, application, non-interactive or external (D-135 item 6).</summary>
        OnlyIneligibleUsers,
    }

    /// <summary>The assignee answer: the one eligible user (or <c>null</c>) and why.</summary>
    internal sealed record Answer(Guid? UserId, Outcome Outcome);

    /// <summary>True when the To Do is filed under anything: a regarding lookup is set, or the ADR-024 pair names a record.</summary>
    private static bool IsFiled(Entity todo) =>
        FilingColumns.Any(c => todo.GetAttributeValue<EntityReference>(c) is { } r && r.Id != Guid.Empty)
        || PairColumns.Any(c => todo.Contains(c) && todo[c] is { } v && !string.IsNullOrWhiteSpace(v.ToString()));

    /// <summary>The ONE eligible (enabled, interactive, non-application) systemuser whose <c>sprk_primarycontact</c> is
    /// <paramref name="contactId"/>; <c>null</c> when there is none, several, or only ineligible ones. A fault propagates.</summary>
    internal static async Task<Guid?> SingleEnabledUserAsync(AssignedAccessStore links, Guid contactId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(links);
        if (contactId == Guid.Empty)
            return null;

        return (await ClassifyLinkedUsersAsync(links, contactId, ct).ConfigureAwait(false)).UserId;
    }

    /// <summary><see cref="SingleEnabledUserAsync"/> with why there is no single user. A fault propagates.</summary>
    private static async Task<Answer> ClassifyLinkedUsersAsync(AssignedAccessStore links, Guid contactId, CancellationToken ct)
    {
        var linked = (await links.ReadLinkCandidatesAsync(contactId, boundOid: null, ct).ConfigureAwait(false))
            .Where(c => c.PrimaryContactId == contactId)
            .ToList();
        // Only a person who can be shared with counts (the ONE rule AssignedAccessMaterializer applies, InternalShareEndpoints
        // .ClassifyEligibility): an application user, a non-interactive account, an external-flagged one or a disabled one never
        // counts toward "exactly one". rootIsRestricted is null (not read here), which ClassifyEligibility documents as fail
        // closed: an external-flagged user is refused. (false would admit one, as it does for a share on an ordinary record.)
        var enabled = linked
            .Where(c => InternalShareEndpoints.ClassifyEligibility(c.IsDisabled, c.AccessMode, c.ApplicationId, c.IsExternal, rootIsRestricted: null)
                    == InternalShareEndpoints.ShareEligibility.Eligible)
            .Select(c => c.SystemUserId)
            .Distinct()
            .ToList();

        return enabled.Count switch
        {
            1 => new Answer(enabled[0], Outcome.SingleUser),
            > 1 => new Answer(null, Outcome.SeveralUsers),
            _ => new Answer(null, linked.Count == 0 ? Outcome.NoLinkedUser : Outcome.OnlyIneligibleUsers),
        };
    }

    private static bool IsNotFound(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (RecordContainerResolver.IsRecordNotFound(e))
                return true;
        }

        return false;
    }
}

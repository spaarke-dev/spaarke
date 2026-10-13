using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;

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

    /// <summary>The To Do's filing lookups (uac-r2's lineage entry for <c>sprk_todo</c>): any value set means it is filed.</summary>
    internal static readonly IReadOnlyList<string> FilingColumns = SecureChildLineage.Children[Table].Lookups.Keys.ToArray();

    private static readonly string[] Columns = [AssignedToDefaults.AssignedToAttribute, .. FilingColumns];

    /// <summary>Reads the To Do app-only and answers its Assigned To's one enabled systemuser; <c>null</c> for no single user.</summary>
    /// <exception cref="Exception">A Dataverse fault (other than the row not existing) propagates.</exception>
    internal static async Task<Guid?> ResolveAsync(
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
            return null;
        }

        if (todo is null || FilingColumns.Any(c => todo.GetAttributeValue<EntityReference>(c) is { } r && r.Id != Guid.Empty))
            return null;

        return todo.GetAttributeValue<EntityReference>(AssignedToDefaults.AssignedToAttribute) is { } assignedTo && assignedTo.Id != Guid.Empty
            ? await SingleEnabledUserAsync(links, assignedTo.Id, ct).ConfigureAwait(false)
            : null;
    }

    /// <summary>The ONE enabled systemuser whose <c>sprk_primarycontact</c> is <paramref name="contactId"/>; <c>null</c> when there
    /// is none, several, or only disabled ones. A fault propagates.</summary>
    internal static async Task<Guid?> SingleEnabledUserAsync(AssignedAccessStore links, Guid contactId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(links);
        if (contactId == Guid.Empty)
            return null;

        var candidates = await links.ReadLinkCandidatesAsync(contactId, boundOid: null, ct).ConfigureAwait(false);
        var enabled = candidates
            .Where(c => c.PrimaryContactId == contactId && c.IsDisabled == false)
            .Select(c => c.SystemUserId)
            .Distinct()
            .ToList();
        return enabled.Count == 1 ? enabled[0] : null;
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

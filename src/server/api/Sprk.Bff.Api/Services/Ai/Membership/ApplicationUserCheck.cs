// unified-access-control-r2 task 152 (ADR-034 Amendment A3) — "is this systemuser a person?"
//
// One answer for the three places that must agree on it:
//   1. MembershipResolverService's people-targeting surface — Created By binds only for a HUMAN caller.
//   2. The MembershipChangedEvent publishers — an application-user owner produces no event.
//   3. MembershipReconciliationJob — an application-user identity produces no junction row.
// If (2) and (3) disagreed, the two writers would key the junction differently again — the defect this task fixes.

using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Reads <c>systemuser.applicationid</c>: set for an application user (an Entra app registration's Dataverse
/// identity, e.g. <c># mi-bff-api-dev</c>), null for a human. An application user is not a person, so it is never a
/// people-targeting term and never a membership-junction subject.
/// </summary>
internal static class ApplicationUserCheck
{
    /// <summary>The systemuser column that distinguishes an application user from a human.</summary>
    internal const string ApplicationIdAttribute = "applicationid";

    /// <summary>
    /// <see langword="true"/> = application user; <see langword="false"/> = human; <see langword="null"/> = the row
    /// could not be read. Callers decide how to treat "unknown", and every caller in this codebase treats it as
    /// "not a person" for SELECTION (the people surface binds nothing) — never as a human.
    /// </summary>
    public static async Task<bool?> IsApplicationUserAsync(
        IGenericEntityService dataverse,
        Guid systemUserId,
        ILogger logger,
        CancellationToken ct)
    {
        if (systemUserId == Guid.Empty)
        {
            return null;
        }

        try
        {
            var row = await dataverse
                .RetrieveAsync("systemuser", systemUserId, new[] { ApplicationIdAttribute }, ct)
                .ConfigureAwait(false);

            if (row is null)
            {
                return null;
            }

            if (!row.Contains(ApplicationIdAttribute) || row[ApplicationIdAttribute] is null)
            {
                return false;
            }

            return row[ApplicationIdAttribute] switch
            {
                Guid g => g != Guid.Empty,
                string s => Guid.TryParse(s, out var parsed) && parsed != Guid.Empty,
                _ => true,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "application_user_check_failed: systemUserId={SystemUserId} — treated as unknown (not a person for selection)",
                systemUserId);
            return null;
        }
    }

    /// <summary>
    /// Maps a lookup's target logical name to the closed membership identity type. The polymorphic Owner column is
    /// typed from the VALUE (<see cref="EntityReference.LogicalName"/>), never from the descriptor — discovery always
    /// binds Owner as SystemUser (ADR-034 A1.1), so a team-owned row's team id would otherwise be recorded as a User.
    /// </summary>
    public static bool TryMapLogicalName(string? logicalName, out Events.PersonIdentityType type)
    {
        switch (logicalName?.Trim().ToLowerInvariant())
        {
            case "systemuser":
                type = Events.PersonIdentityType.User;
                return true;
            case "team":
                type = Events.PersonIdentityType.Team;
                return true;
            case "contact":
                type = Events.PersonIdentityType.Contact;
                return true;
            case "sprk_organization":
                type = Events.PersonIdentityType.Organization;
                return true;
            default:
                type = default;
                return false;
        }
    }
}

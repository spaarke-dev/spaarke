using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>
/// What an internal (POA) share of one level carries: the rights literal GrantAccess and ModifyAccess send, and the
/// <c>accessrightsmask</c> Dataverse stores for those rights.
/// </summary>
/// <param name="AccessRightsCsv">The Web API <c>AccessMask</c> literal, e.g. <c>"ReadAccess,WriteAccess"</c>.</param>
/// <param name="AccessRightsMask">The same rights as the bitmask Dataverse stores on the share row.</param>
internal readonly record struct RecordShareRights(string AccessRightsCsv, int AccessRightsMask);

/// <summary>
/// The ONE mapping from a share level to the Dataverse rights an internal system-user (POA) share of that level
/// carries — unified-access-control-r2 task 063, spec FR-29. The levels were set by the owner on 2026-09-15.
/// </summary>
/// <remarks>
/// <para><b>The levels.</b> View Only = Read. Collaborate = Read, Write, Append and AppendTo — the working access a
/// colleague already receives at secure-project provisioning, whose constant now points here. Full Access =
/// Collaborate + Delete. <b>Share and Assign are at no level</b> (owner, "No re-share at any level"): a person given
/// access through the "+ User" picker cannot pass it on, or take the record over.</para>
///
/// <para><b>The masks are Dataverse's numbers, not <see cref="Spaarke.Dataverse.AccessRights"/>.</b> That enum is
/// Spaarke's evaluation vocabulary with its own bit layout — its Delete is 4, which on a share row means Append. A
/// share row stores Dataverse's <c>AccessRights</c> values, read by reflection from <c>Microsoft.Crm.Sdk.Proxy</c>
/// 1.2.26: Read 1, Write 2, Append 4, AppendTo 16, Create 32, Delete 65536, Share 262144, Assign 524288. The share
/// endpoint confirms each write by comparing the stored mask with these numbers, so a mask built from the other
/// vocabulary would fail every confirmation — or pass one for the wrong rights.</para>
///
/// <para><b>Not <see cref="ExternalAccessLevels.ToAccessRights"/>.</b> That table decides what an EXTERNAL contact's
/// grant lets the evaluator admit, and it includes Create, which means nothing on a share of an existing record.
/// The two tables use the same three level names to answer different questions.</para>
/// </remarks>
internal static class RecordShareLevels
{
    // Dataverse AccessRights values (see the remarks). Private: a mask is built here and nowhere else.
    private const int Read = 1;
    private const int Write = 2;
    private const int Append = 4;
    private const int AppendTo = 16;
    private const int Delete = 65536;

    private const int ViewOnlyMask = Read;
    private const int CollaborateMask = Read | Write | Append | AppendTo;
    private const int FullAccessMask = CollaborateMask | Delete;

    /// <summary>View Only: read the record.</summary>
    internal const string ViewOnlyRights = "ReadAccess";

    /// <summary>Collaborate: work the record. Also the colleague share at secure-project provisioning.</summary>
    internal const string CollaborateRights = "ReadAccess,WriteAccess,AppendAccess,AppendToAccess";

    /// <summary>Full Access: Collaborate, plus delete the record.</summary>
    internal const string FullAccessRights = CollaborateRights + ",DeleteAccess";

    /// <summary>
    /// The rights for <paramref name="level"/>. <c>false</c> for <c>null</c> or any value outside the three levels —
    /// an unrecognised level never becomes a share.
    /// </summary>
    internal static bool TryGetRights(ExternalAccessLevel? level, out RecordShareRights rights)
    {
        switch (level)
        {
            case ExternalAccessLevel.ViewOnly:
                rights = new RecordShareRights(ViewOnlyRights, ViewOnlyMask);
                return true;
            case ExternalAccessLevel.Collaborate:
                rights = new RecordShareRights(CollaborateRights, CollaborateMask);
                return true;
            case ExternalAccessLevel.FullAccess:
                rights = new RecordShareRights(FullAccessRights, FullAccessMask);
                return true;
            default:
                rights = default;
                return false;
        }
    }

    /// <summary>
    /// The level whose rights are EXACTLY <paramref name="accessRightsMask"/>, or <c>null</c>. A share holding rights
    /// outside the three levels — the provisioning creator's Share right, a share made in the Dataverse UI — has no
    /// level; naming one would under- or over-state what it grants.
    /// </summary>
    internal static ExternalAccessLevel? LevelForMask(int accessRightsMask) => accessRightsMask switch
    {
        ViewOnlyMask => ExternalAccessLevel.ViewOnly,
        CollaborateMask => ExternalAccessLevel.Collaborate,
        FullAccessMask => ExternalAccessLevel.FullAccess,
        _ => null
    };
}

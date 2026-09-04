using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// Construction helpers for the task-032 <c>(recordId → rights)</c> shapes.
/// </summary>
/// <remarks>
/// <para>
/// Before task 032, <c>AccessibleRecordSet.RecordIds</c> and <c>ExternalGrantSet.Matters</c> /
/// <c>.WorkAssignments</c> were settable id collections, so a test said "these records are accessible"
/// by assigning a <c>HashSet&lt;Guid&gt;</c>. They are now DERIVED VIEWS over the rights map / the
/// level-carrying grant lists, so they cannot be assigned. These helpers keep the tests reading the
/// same way rather than scattering dictionary-building over five files.
/// </para>
/// <para>
/// <b>Why <see cref="MembershipRights"/> is the default for a bare id list.</b> A bare id set carried no
/// level at all; downstream, the workforce strategy blanket-stamped Collaborate over everything it
/// contained (register A-8). So Read|Write|Create is the FAITHFUL translation of what those ids meant
/// before this task — not a new grant. Tests that care about level fidelity build the map explicitly
/// instead of using these helpers.
/// </para>
/// </remarks>
internal static class AccessibleRecordSetTestFactory
{
    /// <summary>The membership term level — the rights a bare id previously resolved to. </summary>
    public const AccessRights MembershipRights =
        AccessRights.Read | AccessRights.Write | AccessRights.Create;

    /// <summary>A rights map over <paramref name="ids"/>, all at <see cref="MembershipRights"/>.</summary>
    public static IReadOnlyDictionary<Guid, AccessRights> RightsOf(params Guid[] ids) =>
        ids.ToDictionary(id => id, _ => MembershipRights);

    /// <summary>A rights map over <paramref name="ids"/>, all at <see cref="MembershipRights"/>.</summary>
    public static IReadOnlyDictionary<Guid, AccessRights> RightsOf(IEnumerable<Guid> ids) =>
        ids.ToDictionary(id => id, _ => MembershipRights);

    /// <summary>Level-carrying root grants at one level (default <c>Collaborate</c>).</summary>
    public static IReadOnlyList<ExternalRootGrant> RootGrants(
        ExternalAccessLevel level, params Guid[] ids) =>
        ids.Select(id => new ExternalRootGrant { RecordId = id, AccessLevel = level }).ToList();

    /// <summary>Level-carrying root grants at <c>Collaborate</c> — the neutral default for tests that predate levels.</summary>
    public static IReadOnlyList<ExternalRootGrant> RootGrants(params Guid[] ids) =>
        RootGrants(ExternalAccessLevel.Collaborate, ids);

    /// <summary>The empty root-grant list (replaces <c>new HashSet&lt;Guid&gt;()</c> at grant-set sites).</summary>
    public static IReadOnlyList<ExternalRootGrant> NoRootGrants { get; } = Array.Empty<ExternalRootGrant>();
}

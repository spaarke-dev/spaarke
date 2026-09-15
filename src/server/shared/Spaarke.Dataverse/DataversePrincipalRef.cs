namespace Spaarke.Dataverse;

/// <summary>
/// The kind of Dataverse security principal a <c>principalobjectaccess</c> (POA) share can name.
/// </summary>
/// <remarks>
/// The values are Dataverse's own <c>principaltypecode</c> / <c>ObjectTypeCode</c> numbers, so the enum
/// converts directly to and from what the POA table stores — no lookup table, nothing to keep in sync.
///
/// <para><b>Extension point (unified-access-control-r2 task 060)</b>: additional principal kinds
/// (e.g. access teams, which Dataverse also stores as <c>team</c>=9 but distinguishes by team type)
/// are added HERE plus a case in <see cref="DataversePrincipalRefExtensions.ToEntitySet"/>. Two kinds
/// are defined deliberately — the two that Spaarke actually shares to today. Do not add speculative
/// kinds.</para>
/// </remarks>
public enum DataversePrincipalKind
{
    /// <summary>A <c>systemuser</c> (Dataverse ObjectTypeCode 8).</summary>
    SystemUser = 8,

    /// <summary>A <c>team</c> (Dataverse ObjectTypeCode 9).</summary>
    Team = 9,
}

/// <summary>
/// Identifies the principal on one side of a POA share: its kind plus its id. This is the shape that
/// makes the share seam <i>parameterized</i> rather than one copy of every method per principal kind.
/// </summary>
/// <param name="Kind">Whether <paramref name="Id"/> names a systemuser or a team.</param>
/// <param name="Id">The principal's Dataverse row id.</param>
public readonly record struct DataversePrincipalRef(DataversePrincipalKind Kind, Guid Id)
{
    /// <summary>A <c>systemuser</c> principal.</summary>
    public static DataversePrincipalRef User(Guid systemUserId) => new(DataversePrincipalKind.SystemUser, systemUserId);

    /// <summary>A <c>team</c> principal.</summary>
    public static DataversePrincipalRef Team(Guid teamId) => new(DataversePrincipalKind.Team, teamId);
}

/// <summary>
/// One POA row as read back from Dataverse: who has a share on the record, and with what rights.
/// </summary>
/// <param name="Principal">The principal holding the share.</param>
/// <param name="AccessRightsMask">Dataverse's <c>accessrightsmask</c> bitfield for the share.</param>
/// <param name="ModifiedOn">When the share row last changed.</param>
public readonly record struct DataversePrincipalAccess(
    DataversePrincipalRef Principal,
    int AccessRightsMask,
    DateTimeOffset ModifiedOn);

/// <summary>Mapping helpers between <see cref="DataversePrincipalKind"/> and the Web API's URL vocabulary.</summary>
public static class DataversePrincipalRefExtensions
{
    /// <summary>
    /// The entity set a principal reference addresses in a <c>@odata.id</c> binding
    /// (<c>systemusers(...)</c> / <c>teams(...)</c>).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not one this seam can address.</exception>
    public static string ToEntitySet(this DataversePrincipalKind kind) => kind switch
    {
        DataversePrincipalKind.SystemUser => "systemusers",
        DataversePrincipalKind.Team => "teams",
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, "Unsupported Dataverse principal kind for a POA share."),
    };

    /// <summary>
    /// Maps a POA <c>principaltypecode</c> to a <see cref="DataversePrincipalKind"/>, or <c>null</c>
    /// when the code is one this seam does not model (the caller skips such rows rather than guessing).
    /// </summary>
    public static DataversePrincipalKind? FromPrincipalTypeCode(int principalTypeCode) => principalTypeCode switch
    {
        (int)DataversePrincipalKind.SystemUser => DataversePrincipalKind.SystemUser,
        (int)DataversePrincipalKind.Team => DataversePrincipalKind.Team,
        _ => null,
    };
}

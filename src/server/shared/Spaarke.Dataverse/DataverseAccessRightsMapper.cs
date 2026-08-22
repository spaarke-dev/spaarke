namespace Spaarke.Dataverse;

/// <summary>
/// Translates Dataverse's <c>RetrievePrincipalAccess</c> rights string into <see cref="AccessRights"/>.
/// </summary>
/// <remarks>
/// <para>Dataverse answers <c>RetrievePrincipalAccess</c> with a comma-separated list of right names —
/// <c>"ReadAccess,WriteAccess,AppendAccess,AppendToAccess,ShareAccess"</c>. This is the single place
/// that turns those names into flags.</para>
///
/// <para><b>Why this is its own type</b> (unified-access-control-r2 task 005, spec FR-04). The logic
/// previously lived as a private method on <see cref="DataverseAccessDataSource"/>, where it was
/// unreachable by any test: the only way in is an HTTP call, and mocking that transport is banned
/// (ADR-038 §7 ban B1), while reflecting into a private member is banned too (ban B8). FR-04's
/// acceptance criterion — <i>"the snapshot never exceeds the caller's actual Dataverse rights, asserted
/// by test with a mocked Dataverse answer"</i> — is not satisfiable without a seam. Extracting the pure
/// function gives one, at the layer ADR-038 calls domain logic (no I/O, no DI, no time). It is
/// <c>internal</c>, exposed via <c>InternalsVisibleTo</c> per the convention already used across
/// <c>Sprk.Bff.Api</c>.</para>
///
/// <para><b>Why it matters that this is tested.</b> Transposing <c>AppendToAccess</c> and
/// <c>AppendAccess</c> — adjacent names, one character apart in the enum — would leave
/// <c>POST /api/office/save</c> permanently 403 while looking fixed, because
/// <c>entity.associate_document</c> resolves and the denial reads as a legitimate
/// <c>insufficient_rights</c>. Task 003 recorded exactly that failure mode as a binding obligation on
/// task 005.</para>
/// </remarks>
internal static class DataverseAccessRightsMapper
{
    /// <summary>
    /// Maps a comma-separated Dataverse rights string to flags.
    /// </summary>
    /// <param name="accessRightsString">
    /// e.g. <c>"ReadAccess,WriteAccess,DeleteAccess"</c>. Null, empty or whitespace yields
    /// <see cref="AccessRights.None"/> — an authoritative "no rights", which is the fail-closed answer.
    /// </param>
    /// <returns>The bitwise combination of every recognised right. Unrecognised names contribute nothing.</returns>
    internal static AccessRights FromAccessRightsString(string? accessRightsString)
    {
        if (string.IsNullOrWhiteSpace(accessRightsString))
        {
            return AccessRights.None;
        }

        var rights = accessRightsString.Split(',', StringSplitOptions.TrimEntries);
        var accessRights = AccessRights.None;

        foreach (var right in rights)
        {
            // Unknown names map to None rather than throwing: Dataverse may add rights we do not model,
            // and a new right must never be able to widen — or crash — an authorization decision.
            accessRights |= right switch
            {
                "ReadAccess" => AccessRights.Read,
                "WriteAccess" => AccessRights.Write,
                "DeleteAccess" => AccessRights.Delete,
                "CreateAccess" => AccessRights.Create,
                "AppendAccess" => AccessRights.Append,
                "AppendToAccess" => AccessRights.AppendTo,
                "ShareAccess" => AccessRights.Share,
                _ => AccessRights.None
            };
        }

        return accessRights;
    }
}

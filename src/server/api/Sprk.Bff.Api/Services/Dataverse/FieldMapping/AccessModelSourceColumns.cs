namespace Sprk.Bff.Api.Services.Dataverse.FieldMapping;

/// <summary>
/// field-mapping-server-write-path-r1 task 067 (UAC-r2 condition C3): the closed list of ACCESS-MODEL source columns. Field
/// mapping never reads one of these past the caller's column view, whatever a rule says. On create and at email send a rule
/// that reads one keeps the masked read (<see cref="MappingSourceCallerMask.ApplyAsync"/>); on push it keeps the as-caller
/// read; on Repair it keeps the rule that a field-secured source column is not copied. In each case a warning names the
/// column the rule targets.
/// </summary>
/// <remarks>
/// <para><b>Why a closed list.</b> Task 067 (owner 2026-11-27) copies a mapped value from the row's own filing parent whatever
/// the caller can read on that parent's columns. These columns hold identity proofs, invitation state, standing grants and the
/// secure flags that decide who sees a record, so they stay behind the caller's view even from the filing parent.</para>
/// <para><b><see cref="Exact"/> is matched by name on every table</b> (trimmed, ordinal ignoring case), so a column of the same
/// name on another table is denied too, and <see cref="IdentityCollisionPrefix"/> denies every column that starts with it. A
/// column the access-column registry lists is denied only on the table it lists it for (<see cref="IsRegistryColumn"/>), so
/// every check names the source table.</para>
/// <para><b>One source of truth.</b> No field-security profile in the repo names these sets, so this constant (with the
/// registry it derives from) is the authority. <c>tests/Spaarke.ArchTests/AccessModelSourceColumnsGuardTests.cs</c> fails when
/// a column the repo's solution XML marks field-secured on contact, systemuser, sprk_document, sprk_communication or any table
/// that carries <c>sprk_issecure</c> (in any solution file) is neither on this list nor in that guard's pinned list of business
/// columns. Consulted by <see cref="MappingSourceCallerMask"/> (create and send), the
/// field-mapping push and Repair.</para>
/// <para>ADR-010: static, no DI registration.</para>
/// </remarks>
internal static class AccessModelSourceColumns
{
    /// <summary>Every column starting with this is an access-model column (contact identity-collision state).</summary>
    internal const string IdentityCollisionPrefix = "sprk_identitycollision";

    /// <summary>The columns denied by exact name.</summary>
    internal static readonly IReadOnlySet<string> Exact = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // contact: identity, invitation and external-access state
        "adx_identity_passwordhash",
        "adx_identity_logonenabled",
        "sprk_externalinvitecodehash",
        "sprk_externalinvitedemailhash",
        "sprk_externalinvitedtenantid",
        "sprk_externalinvitedon",
        "sprk_externalobjectid",
        "sprk_externaltenantid",
        "sprk_identityplane",
        "sprk_standinggrant",
        "sprk_accesspermissiongrant",
        "sprk_externalaccessremovedon",

        // every table: the secure flag and the access inheritance
        "sprk_issecure",
        "sprk_accessinheritance",

        // systemuser: the home tenant and object
        "sprk_hometenantid",
        "sprk_homeobjectid",

        // sprk_document: the submission marker that narrows who sees it, and its file binding (drive, item, bound item)
        "sprk_restrictedbysubmission",
        "sprk_graphdriveid",
        "sprk_graphitemid",
        "sprk_graphitemidbound",
    };

    /// <summary>
    /// Whether the access-column registry (<see cref="InheritedAccessPermission.AccessColumns"/>: the columns only the BFF's typed
    /// writers set) lists <paramref name="column"/> for <paramref name="table"/> itself (both trimmed, ordinal ignoring case).
    /// Read from that registry, so a column added there is denied here too, on the table it is listed for and no other.
    /// </summary>
    internal static bool IsRegistryColumn(string? table, string? column) =>
        !string.IsNullOrWhiteSpace(table)
        && !string.IsNullOrWhiteSpace(column)
        && InheritedAccessPermission.AccessColumns.TryGetValue(table.Trim(), out var columns)
        && columns.Contains(column.Trim());

    /// <summary>Whether <paramref name="column"/> of <paramref name="sourceTable"/> is an access-model column: by exact name or
    /// the identity-collision prefix (on any table), or listed for that table in the access-column registry
    /// (<see cref="IsRegistryColumn"/>).</summary>
    internal static bool Contains(string? sourceTable, string? column)
    {
        if (string.IsNullOrWhiteSpace(column))
            return false;

        var name = column.Trim();
        return Exact.Contains(name)
               || name.StartsWith(IdentityCollisionPrefix, StringComparison.OrdinalIgnoreCase)
               || IsRegistryColumn(sourceTable, name);
    }

    /// <summary>Whether any of <paramref name="columns"/> of <paramref name="sourceTable"/> is an access-model column.</summary>
    internal static bool AnyIn(string? sourceTable, IEnumerable<string> columns) => columns.Any(c => Contains(sourceTable, c));

    /// <summary>The target columns of the <paramref name="rules"/> that read an access-model column of
    /// <paramref name="sourceTable"/> (by the engine's own source columns, so a Concat or Template placeholder counts), trimmed,
    /// distinct and in ordinal order.</summary>
    internal static IReadOnlyList<string> TargetsOfRulesReadingOne(
        string? sourceTable, IEnumerable<Spaarke.Dataverse.FieldMappingRuleEntity> rules) =>
        rules
            .Where(r => AnyIn(sourceTable, CreateTimeFieldMapping.CollectSourceColumns([r])))
            .Select(r => r.TargetField?.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .Select(t => t!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>The warning text for a rule whose source is an access-model column, naming the column it targets; the same
    /// words whatever the source holds (it names no value and says nothing about whether one exists).</summary>
    internal static string ReadAsTheCallerWarning(string targetColumn) =>
        $"Field-mapping rule targeting \"{targetColumn}\" reads an access-model column, so it copies only what you can read.";

    /// <summary>The Repair warning for a rule whose field-secured source is an access-model column, naming its target.</summary>
    internal static string NotCopiedByRepairWarning(string targetColumn) =>
        $"Field-mapping rule targeting \"{targetColumn}\" was not applied: it reads a field-secured access-model column.";
}

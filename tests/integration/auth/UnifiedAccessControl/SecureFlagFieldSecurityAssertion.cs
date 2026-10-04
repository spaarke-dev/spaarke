namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// The task 150 STANDING assertion on the field-level security of <c>sprk_issecure</c>: every reader can read it, and
/// only the expected principals can write it. A sibling of <c>SecureBuRoleDepthAssertion</c> (which checks ROLES, not
/// field permissions), kept separate so the two evolve independently.
/// </summary>
/// <remarks>
/// <para><b>The failure it guards is silent and fail-open.</b> Dataverse returns a field-secured column the reading
/// identity has no Read on as EMPTY — no error. The BFF now refuses on an empty flag (task 150's ABSENT-branch decision),
/// but every OTHER reader — the external plane's Secure suppression, the client container resolver, the PCF access
/// gate — maps empty to "not secure". The repo's one earlier field-secured column (<c>contact.sprk_standinggrant</c>,
/// teams-app-r1 task 050) shows how this goes: the BFF's Read grant was left as an operator step, never applied, and
/// the feature read "no grant" for every contact for months. So the grants are a checked invariant, not a runbook
/// line.</para>
///
/// <para><b>Clauses</b> (each gap is its own finding, naming the principal or table):
/// <list type="number">
///   <item>The column is field-secured on <see cref="Tables"/> — the lock exists at all.</item>
///   <item>The READER profile (<see cref="ReaderProfileName"/>) grants Read on each table and is associated with EVERY
///   business unit's default team — every current and future user of an existing business unit reads the true value
///   (membership of a default team is automatic). A business unit created later is a finding until its default team is
///   added (the new-BU step).</item>
///   <item>The WRITER profile (<see cref="WriterProfileName"/>) grants Read, Create and Update on each table, and its
///   members are EXACTLY the BFF application user(s): each one present (explicitly — never relying on System
///   Administrator), and no other user and no team.</item>
///   <item>No profile other than the writer and the platform's System Administrator profile can create or update the
///   column. The System Administrator profile's full permission is created by the platform and cannot be narrowed;
///   owner decision F4 accepts it as the administrator boundary, so it is not a finding — and every holder of the
///   System Administrator role is LISTED in the summary, so the residual writer set stays visible.</item>
///   <item>No CONFIGURATION row names the column as a write target (<see cref="ConfiguredWriterChannels"/>): a Field
///   Mapping Framework rule, an AI topic-registry row or an email update-field row. Each is maker-authored data through
///   which a client or BFF path writes a column outside the secure/unsecure endpoints. Once the column is locked, such a
///   row makes every write it drives fail (a Copy rule onto a child record would refuse every secure create); before it
///   is locked, it writes the flag behind the endpoints' back (task 150 r2, verifier F5 — live 2026-10-03: 0 rows on
///   all three channels).</item>
/// </list></para>
///
/// <para><b>Names.</b> The two profiles are the ones task 133 created for the class of column only the BFF writes
/// (<c>scripts/Set-RecordCreatorPersonSchema.ps1</c>); task 150 adds <c>sprk_issecure</c> to them
/// (<c>scripts/Set-SecureFlagFieldSecurity.ps1</c>). <c>SecureFlagFieldSecurityScriptAgreementTests</c> pins these
/// constants against both scripts.</para>
/// </remarks>
public static class SecureFlagFieldSecurityAssertion
{
    /// <summary>The field-secured column.</summary>
    public const string Column = "sprk_issecure";

    /// <summary>
    /// The tables whose copy of the column is locked — the three secure roots, and <c>sprk_invoice</c> (owner round 10
    /// item 11: an invoice follows its matter, so its flag is not a security input and nothing writes it; it is locked
    /// like the roots so no user sets a value that looks meaningful).
    /// </summary>
    public static readonly IReadOnlyList<string> Tables =
        new[] { "sprk_project", "sprk_matter", "sprk_workassignment", "sprk_invoice" };

    /// <summary>The reader profile (task 133's name, shared by every BFF-managed column).</summary>
    public const string ReaderProfileName = "Spaarke BFF-Managed Field Readers";

    /// <summary>The writer profile (task 133's name, shared by every BFF-managed column).</summary>
    public const string WriterProfileName = "Spaarke BFF-Managed Field Writers";

    /// <summary>The platform's own profile, whose full permission on every secured column is created automatically.</summary>
    public const string SystemAdministratorProfileName = "System Administrator";

    /// <summary>
    /// The configuration tables whose rows name a column the platform then WRITES, as (table logical name, the column
    /// holding the target column's logical name). Clause 5.
    /// </summary>
    public static readonly IReadOnlyList<(string Table, string TargetColumn)> ConfiguredWriterChannels = new[]
    {
        ("sprk_fieldmappingrule", "sprk_targetfield"),
        ("sprk_aitopicregistry", "sprk_targetfield"),
        ("sprk_emailupdatefield", "sprk_targetfieldlogicalname")
    };

    /// <summary>Dataverse's "allowed" value for <c>canread</c> / <c>cancreate</c> / <c>canupdate</c>.</summary>
    public const int Allowed = 4;

    /// <summary>Evaluates every clause against a census read from a live environment.</summary>
    public static SecureFlagFieldSecurityOutcome Evaluate(SecureFlagFieldSecurityCensus census)
    {
        ArgumentNullException.ThrowIfNull(census);
        var findings = new List<string>();

        // ── Clause 1 — the lock exists ─────────────────────────────────────────────
        foreach (var table in Tables)
        {
            if (!census.SecuredTables.Contains(table))
                findings.Add($"NOT LOCKED: {table}.{Column} is not field-secured — any user with Write can change it.");
        }

        var reader = census.Profiles.Where(p => NameIs(p, ReaderProfileName)).ToArray();
        var writer = census.Profiles.Where(p => NameIs(p, WriterProfileName)).ToArray();

        // ── Clause 2 — every reader reads ─────────────────────────────────────────
        if (reader.Length != 1)
        {
            findings.Add($"READER PROFILE: {reader.Length} profile(s) named '{ReaderProfileName}' — exactly one is required.");
        }
        else
        {
            RequirePermissions(census, reader[0], create: false, findings);

            var associated = reader[0].TeamIds.ToHashSet();
            foreach (var team in census.DefaultTeams.Where(t => !associated.Contains(t.Id)))
            {
                findings.Add(
                    $"MASKED READERS: business unit '{team.BusinessUnitName}' — its default team '{team.Name}' ({team.Id}) is " +
                    $"not associated with '{ReaderProfileName}', so every user in it reads {Column} EMPTY, and every " +
                    "reader that maps empty to 'not secure' treats a secure record as an ordinary one. Add the team " +
                    "(re-run scripts/Set-RecordCreatorPersonSchema.ps1 -Apply — the new-business-unit step).");
            }

            if (census.DefaultTeams.Count == 0)
            {
                findings.Add("VACUOUS: the census found no business-unit default teams — every environment has at least " +
                             "the root business unit's. The query is wrong; this is not a pass.");
            }
        }

        // ── Clause 3 — the BFF application user(s) read AND write, explicitly, and nobody else does ──
        foreach (var unresolved in census.UnresolvedBffApplicationIds)
        {
            findings.Add($"BFF IDENTITY: no application user exists for appId {unresolved}, so it cannot be in " +
                         $"'{WriterProfileName}'.");
        }

        if (census.BffApplicationUsers.Count == 0 && census.UnresolvedBffApplicationIds.Count == 0)
        {
            findings.Add("BFF IDENTITY: no BFF application id was supplied, so the BFF's own Read cannot be verified. " +
                         "This is not a pass.");
        }

        if (writer.Length != 1)
        {
            findings.Add($"WRITER PROFILE: {writer.Length} profile(s) named '{WriterProfileName}' — exactly one is required.");
        }
        else
        {
            RequirePermissions(census, writer[0], create: true, findings);

            var members = writer[0].UserIds.ToHashSet();
            foreach (var bff in census.BffApplicationUsers.Where(u => !members.Contains(u.Id)))
            {
                findings.Add(
                    $"BFF READ: application user '{bff.Name}' ({bff.Id}) is not a member of '{WriterProfileName}'. Without " +
                    "it the BFF reads the flag EMPTY wherever it does not hold System Administrator, and refuses every " +
                    "upload to a securable record (secure_flag_unreadable) and every provisioning (secure_flag_not_set).");
            }

            var expected = census.BffApplicationUsers.Select(u => u.Id).ToHashSet();
            foreach (var extra in writer[0].Users.Where(u => !expected.Contains(u.Id)))
            {
                findings.Add(
                    $"EXTRA WRITER: '{extra.Name}' ({extra.Id}, {(extra.IsApplicationUser ? "an application user not named as the BFF" : "a HUMAN user")}) " +
                    $"is a member of '{WriterProfileName}' and can set or clear {Column} outside the secure/unsecure endpoints.");
            }

            foreach (var team in writer[0].Teams)
            {
                findings.Add(
                    $"EXTRA WRITER: team '{team.Name}' ({team.Id}) is a member of '{WriterProfileName}' — every member of it " +
                    $"can set or clear {Column}.");
            }
        }

        // ── Clause 4 — no other profile writes ────────────────────────────────────
        foreach (var permission in census.Permissions.Where(p => p.CanCreate == Allowed || p.CanUpdate == Allowed))
        {
            var profile = census.Profiles.FirstOrDefault(p => p.Id == permission.ProfileId);
            var name = profile?.Name ?? $"(unknown profile {permission.ProfileId})";
            if (string.Equals(name, WriterProfileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, SystemAdministratorProfileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            findings.Add(
                $"EXTRA WRITER: profile '{name}' can {(permission.CanCreate == Allowed ? "create" : "")}" +
                $"{(permission.CanCreate == Allowed && permission.CanUpdate == Allowed ? "/" : "")}" +
                $"{(permission.CanUpdate == Allowed ? "update" : "")} {permission.Table}.{Column} — only the BFF may.");
        }

        // ── Clause 5 — no configuration row writes it ─────────────────────────────
        foreach (var row in census.ConfiguredWriters)
        {
            findings.Add(
                $"CONFIGURED WRITER: {row.Table} row {row.RowId} names {Column} as its target, so the path it drives " +
                "writes the flag outside the secure/unsecure endpoints — and, once the column is locked, fails every time " +
                "it runs for a user. Remove the row (or retarget it); only the BFF endpoints set or clear the flag.");
        }

        return new SecureFlagFieldSecurityOutcome(findings, census.SystemAdministratorHolders);
    }

    private static bool NameIs(CensusFieldSecurityProfile profile, string name)
        => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase);

    private static void RequirePermissions(
        SecureFlagFieldSecurityCensus census, CensusFieldSecurityProfile profile, bool create, List<string> findings)
    {
        foreach (var table in Tables)
        {
            var rows = census.Permissions
                .Where(p => p.ProfileId == profile.Id && string.Equals(p.Table, table, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (rows.Length == 0)
            {
                findings.Add($"MISSING PERMISSION: '{profile.Name}' has no field permission on {table}.{Column}" +
                             (create ? " — the BFF cannot read or write it." : " — its members read it EMPTY."));
                continue;
            }

            var row = rows[0];
            var wantCreate = create ? Allowed : 0;
            if (row.CanRead != Allowed || row.CanCreate != wantCreate || row.CanUpdate != wantCreate)
            {
                findings.Add(
                    $"WRONG PERMISSION: '{profile.Name}' on {table}.{Column} is read={row.CanRead} create={row.CanCreate} " +
                    $"update={row.CanUpdate}; expected read={Allowed} create={wantCreate} update={wantCreate}.");
            }
        }
    }
}

/// <summary>What a live environment says about <c>sprk_issecure</c>'s field security.</summary>
/// <param name="SecuredTables">The tables on which the column reports <c>IsSecured = true</c>.</param>
/// <param name="Profiles">Every field security profile, with its team and user members.</param>
/// <param name="Permissions">Every <c>fieldpermission</c> row on the column.</param>
/// <param name="DefaultTeams">Every business unit's default team.</param>
/// <param name="BffApplicationUsers">The application users of the supplied BFF application ids.</param>
/// <param name="UnresolvedBffApplicationIds">Supplied BFF application ids with no application user.</param>
/// <param name="SystemAdministratorHolders">
/// Every principal holding the System Administrator role (users, and teams by name) — the residual writer set owner
/// decision F4 accepted; listed, never graded.
/// </param>
/// <param name="ConfiguredWriters">
/// Every row of a <see cref="SecureFlagFieldSecurityAssertion.ConfiguredWriterChannels"/> table whose target column is
/// the flag (clause 5). Empty is the pass.
/// </param>
public sealed record SecureFlagFieldSecurityCensus(
    IReadOnlySet<string> SecuredTables,
    IReadOnlyList<CensusFieldSecurityProfile> Profiles,
    IReadOnlyList<CensusFieldPermission> Permissions,
    IReadOnlyList<CensusDefaultTeam> DefaultTeams,
    IReadOnlyList<CensusPrincipal> BffApplicationUsers,
    IReadOnlyList<string> UnresolvedBffApplicationIds,
    IReadOnlyList<string> SystemAdministratorHolders,
    IReadOnlyList<CensusConfiguredWriter> ConfiguredWriters);

/// <summary>A configuration row (clause 5) naming the flag as the column it writes.</summary>
public sealed record CensusConfiguredWriter(string Table, Guid RowId);

/// <summary>One field security profile and its members.</summary>
public sealed record CensusFieldSecurityProfile(
    Guid Id, string Name, IReadOnlyList<CensusPrincipal> Users, IReadOnlyList<CensusPrincipal> Teams)
{
    public IEnumerable<Guid> UserIds => Users.Select(u => u.Id);

    public IEnumerable<Guid> TeamIds => Teams.Select(t => t.Id);
}

/// <summary>A user or team; <paramref name="IsApplicationUser"/> is false for a team.</summary>
public sealed record CensusPrincipal(Guid Id, string Name, bool IsApplicationUser = false);

/// <summary>One <c>fieldpermission</c> row on the column.</summary>
public sealed record CensusFieldPermission(string Table, Guid ProfileId, int CanRead, int CanCreate, int CanUpdate);

/// <summary>A business unit's default team.</summary>
public sealed record CensusDefaultTeam(Guid Id, string Name, string BusinessUnitName);

/// <summary>The verdict: <see cref="Passed"/> only with no findings.</summary>
public sealed record SecureFlagFieldSecurityOutcome(
    IReadOnlyList<string> Findings, IReadOnlyList<string> SystemAdministratorHolders)
{
    public bool Passed => Findings.Count == 0;

    public string Message =>
        (Passed
            ? $"sprk_issecure field security: PASS — every business unit's default team reads it, and only the BFF " +
              "application user(s) and the platform's System Administrator profile can write it."
            : $"sprk_issecure field security: FAIL ({Findings.Count}):{Environment.NewLine}  - " +
              string.Join(Environment.NewLine + "  - ", Findings))
        + Environment.NewLine
        + $"Residual writers through the System Administrator role (owner decision F4 — accepted, listed): " +
          (SystemAdministratorHolders.Count == 0 ? "(none read)" : string.Join("; ", SystemAdministratorHolders));
}

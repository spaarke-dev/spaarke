// -----------------------------------------------------------------------------
// CustomerOwnerTeamsProcedure.cs
//
// #2429 (H7c) — the customer owner teams, step by step: a faithful port of scripts/Set-CustomerOwnerTeams.ps1 -Apply
// (unified-access-control-r2 task 194b, merged 2cd0f62e2c; Pester tests/scripts/Set-CustomerOwnerTeams.Tests.ps1) over
// H7b's Dataverse seam (ISecureRecordSetupDataverse — same environment, same identity). For the customer's TOP business
// unit (H10's CustomerBusinessUnitId) it sets up each declared role of CustomerOwnerRoleSet: a memberless Owner team of
// the declared name in that unit, the declared role created ONCE in the root unit (Dataverse copies a root role into
// every child unit; a team holds only its own unit's copy), holding Read at Basic on exactly the file's tables, and the
// customer unit's copy assigned to the team.
//
// THREE PHASES (the script's order)
//   1. Read and refuse, for EVERY declared team before anything is written: the root (exactly one), the Secure Record
//      unit by name (exactly one — H7b made it), the customer unit (exists; not the root; not Secure Record; a DIRECT
//      child of the root), each table's Read privilege from metadata (exactly one, its name ordinally the file's), then
//      each team's state and drift. Two teams of a declared name in the unit, or two roles of a declared name in the root,
//      stop the run. Drift that is an owner decision (TEAM TYPE, TEAM DEFAULT, TEAM MEMBERS, TEAM ROLE, ROLE ASSIGNED
//      ELSEWHERE) refuses QuarantineRequired with nothing written.
//   2. Apply, per team with drift: create the team (teamtype 0) in the customer unit if missing; create the role in the
//      root if missing; re-read; take off any member the CREATE itself added (only for a team this run created); narrow a
//      declared privilege held at another depth to Basic (remove, re-add) and add each missing one at Basic; then take
//      off EVERY privilege of that role outside the file — always after the add, because the platform injects privileges
//      on create and re-injects some on every AddPrivilegesRole (setup guide SECURE-PROJECT-ENVIRONMENT-SETUP.md §5.4, the
//      same strip as H7b's S6); assign the customer unit's copy to the team.
//   3. Verify: re-read every declared team; any drift left is a refusal (Resumable).
//
// FAIL CLOSED: a read that fails throws SecureRecordSetupDataverseException and the handler fails the run — nothing is
// decided on a partial read (list reads follow every @odata.nextLink).
//
// WHAT IT NEVER DOES: remove a member it did not add, move a team, take a role off another principal or another role off
// a team, widen a privilege beyond the file, or fall back to the root unit or the caller's own unit.
//
// DEVIATION (no behaviour change): the script binds administratorid to WhoAmI on the team create; H7b's create (reused
// here) leaves it unset, and Dataverse then makes the caller — the same identity — the administrator.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;

namespace Sprk.Provisioning.ControlPlane.Handlers.CustomerOwnerTeams;

/// <summary>What one H7c run works on.</summary>
/// <param name="Target">The environment and the identity that signs in to it (H7b's).</param>
/// <param name="RoleSet">The declared roles (embedded config files).</param>
/// <param name="SecureBusinessUnitName">The Secure Record unit's name, refused as a customer unit (config/secure-record-owner-role.json).</param>
/// <param name="CustomerBusinessUnitId">The customer's top business unit (H10's InterStepState.CustomerBusinessUnitId).</param>
public sealed record CustomerOwnerTeamsRequest(
    SecureRecordSetupTarget Target,
    CustomerOwnerRoleSet RoleSet,
    string SecureBusinessUnitName,
    Guid CustomerBusinessUnitId);

/// <summary>One declared team as verified: the team, the root role, the customer unit's copy, and the privilege count.</summary>
public sealed record CustomerOwnerTeamState(
    string File, string TeamName, string RoleName, Guid TeamId, Guid RoleId, Guid RoleCopyId, int PrivilegeCount);

/// <summary>How a run ended. <see cref="Actions"/> lists the writes performed.</summary>
public abstract record CustomerOwnerTeamsOutcome(IReadOnlyList<string> Actions)
{
    /// <summary>Every declared team is in the declared state (verified). <paramref name="Notes"/> are information only.</summary>
    public sealed record Applied(IReadOnlyList<string> Actions, IReadOnlyList<CustomerOwnerTeamState> Teams, IReadOnlyList<string> Notes)
        : CustomerOwnerTeamsOutcome(Actions);

    /// <summary>A step refused. <see cref="CustomerOwnerTeamsOutcome.Actions"/> lists what was written before it.</summary>
    public sealed record Refused(IReadOnlyList<string> Actions, FailureClass Class, string RejectionCode, string Diagnostic)
        : CustomerOwnerTeamsOutcome(Actions);
}

/// <summary>The read-only check (the script's <c>-Verify</c>): no drift, drift (each named), or a refusal.</summary>
public abstract record CustomerOwnerTeamsVerification
{
    private CustomerOwnerTeamsVerification() { }

    /// <summary>Every declared team is in the declared state.</summary>
    public sealed record Passed(IReadOnlyList<CustomerOwnerTeamState> Teams) : CustomerOwnerTeamsVerification;

    /// <summary>The check ran and found drift, or refused the environment — every finding listed.</summary>
    public sealed record Drifted(IReadOnlyList<string> Findings) : CustomerOwnerTeamsVerification;
}

/// <summary>Runs H7c against one environment through <see cref="ISecureRecordSetupDataverse"/>.</summary>
public sealed class CustomerOwnerTeamsProcedure
{
    /// <summary>How many members a read lists (a refusal names up to this many).</summary>
    internal const int NamedPrincipalLimit = 5;

    /// <summary>How many members of a team this run created are read for removal at once.</summary>
    internal const int CreatedTeamMemberPage = 50;

    /// <summary>The Owner team type.</summary>
    internal const int OwnerTeamType = 0;

    private readonly ISecureRecordSetupDataverse _dataverse;
    private readonly ILogger _logger;

    /// <summary>Creates the procedure over H7b's Dataverse seam.</summary>
    public CustomerOwnerTeamsProcedure(ISecureRecordSetupDataverse dataverse, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(logger);
        _dataverse = dataverse;
        _logger = logger;
    }

    // ------------------------------------------------------------------ the drift vocabulary (the script's)

    private enum DriftKind
    {
        TeamMissing,
        TeamType,
        TeamDefault,
        TeamMembers,
        TeamRole,
        RoleMissing,
        RoleCopyMissing,
        RoleAssignedElsewhere,
        RoleNotAssigned,
        PrivilegeMissing,
        PrivilegeDepth,
        PrivilegeOutsideFile,
    }

    private sealed record Finding(DriftKind Kind, string Text)
    {
        /// <summary>The script's OWNER DECISION NEEDED set: -Apply never changes these.</summary>
        public bool OwnerDecision => Kind is DriftKind.TeamType or DriftKind.TeamDefault or DriftKind.TeamMembers
            or DriftKind.TeamRole or DriftKind.RoleAssignedElsewhere;
    }

    private sealed record Topology(SecureSetupBusinessUnit Root, Guid SecureId, SecureSetupBusinessUnit Customer,
        IReadOnlyList<SecureSetupBusinessUnit> CustomerTops);

    private sealed record Declared(CustomerOwnerRoleDefinition Definition, IReadOnlyList<(string Table, SecureSetupPrivilege Privilege)> Wanted)
    {
        public HashSet<Guid> WantedIds { get; } = Wanted.Select(w => w.Privilege.Id).ToHashSet();
    }

    private sealed record Assignment(bool IsTeam, Guid PrincipalId, Guid RoleId);

    private sealed record TeamState(
        IReadOnlyList<SecureSetupTypedTeam> AllTeams,
        SecureSetupTypedTeam? Team,
        IReadOnlyList<Guid> Members,
        IReadOnlyList<SecureSetupRole> TeamRoles,
        SecureSetupRole? Role,
        IReadOnlyList<SecureSetupRole> Copies,
        SecureSetupRole? Copy,
        IReadOnlyList<Assignment> Assigned,
        IReadOnlyList<SecureSetupHeldPrivilege> Held);

    /// <summary>A refusal raised while reading (two teams in the unit, two roles in the root …).</summary>
    private sealed class RefusalException(FailureClass failureClass, string code, string diagnostic) : Exception(diagnostic)
    {
        public FailureClass Class { get; } = failureClass;
        public string Code { get; } = code;
        public string Diagnostic { get; } = diagnostic;
    }

    // ------------------------------------------------------------------ apply (-Apply)

    /// <summary>
    /// Reads, refuses or writes what is missing, then verifies. Returns <see cref="CustomerOwnerTeamsOutcome.Applied"/> or
    /// <see cref="CustomerOwnerTeamsOutcome.Refused"/>; throws <see cref="SecureRecordSetupDataverseException"/> for a
    /// Dataverse fault and lets cancellation propagate.
    /// </summary>
    public async Task<CustomerOwnerTeamsOutcome> RunAsync(CustomerOwnerTeamsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var t = request.Target;
        var dv = _dataverse;
        var actions = new List<string>();

        CustomerOwnerTeamsOutcome Refuse(FailureClass failureClass, string code, string diagnostic)
        {
            _logger.LogWarning("H7c refused: {Code} — {Diagnostic}", code, diagnostic);
            return new CustomerOwnerTeamsOutcome.Refused(actions.ToArray(), failureClass, code, diagnostic);
        }

        async Task<T> Write<T>(string description, Func<Task<T>> write)
        {
            actions.Add(description);
            return await write().ConfigureAwait(false);
        }

        async Task Do(string description, Func<Task> write)
        {
            actions.Add(description);
            await write().ConfigureAwait(false);
        }

        // ============================ PHASE 1 — read and refuse (no writes) ============================
        Topology topology;
        List<Declared> declared;
        var states = new List<(Declared Declared, TeamState State, IReadOnlyList<Finding> Drift)>();
        try
        {
            topology = await ReadTopologyAsync(request, ct).ConfigureAwait(false);
            declared = await ReadDeclaredAsync(request, ct).ConfigureAwait(false);
            foreach (var d in declared)
            {
                var state = await ReadStateAsync(t, d, topology, ct).ConfigureAwait(false);
                states.Add((d, state, Drift(d, state, topology)));
            }
        }
        catch (RefusalException refusal)
        {
            return Refuse(refusal.Class, refusal.Code, refusal.Diagnostic + " Nothing was written.");
        }

        // Owner decisions, never changed here: refuse before anything is written, for every declared team.
        var ownerOnly = states.SelectMany(s => s.Drift.Where(f => f.OwnerDecision)).ToArray();
        if (ownerOnly.Length > 0)
        {
            return Refuse(FailureClass.QuarantineRequired, OwnerDecisionCode(ownerOnly[0].Kind),
                "OWNER DECISION NEEDED: H7c never changes these and refuses while they stand: " +
                string.Join(" | ", ownerOnly.Select(f => f.Text)) +
                " Settle each by hand (an owner decision), then resume. Nothing was written.");
        }

        // ============================ PHASE 2 — apply, per declared team with drift ============================
        foreach (var (d, before, drift) in states.Where(s => s.Drift.Count > 0))
        {
            var def = d.Definition;
            var createdTeam = false;
            if (before.Team is null)
            {
                await Write($"create the Owner team '{def.TeamName}' in '{topology.Customer.Name}' ({topology.Customer.Id}), with no members",
                    () => dv.CreateOwnerTeamAsync(t, topology.Customer.Id, def.TeamName, TeamDescription(def), ct)).ConfigureAwait(false);
                createdTeam = true;
            }
            if (before.Role is null)
            {
                await Write($"create the role '{def.RoleName}' in the root business unit (Dataverse copies it into every child unit)",
                    () => dv.CreateRoleAsync(t, topology.Root.Id, def.RoleName, RoleDescription(def), ct)).ConfigureAwait(false);
            }

            TeamState s;
            try
            {
                s = await ReadStateAsync(t, d, topology, ct).ConfigureAwait(false);
            }
            catch (RefusalException refusal)
            {
                return Refuse(refusal.Class, refusal.Code, refusal.Diagnostic);
            }
            if (s.Team is null || s.Role is null)
            {
                return Refuse(FailureClass.Resumable, CustomerOwnerTeamsRejectionCodes.VerifyFailed,
                    $"'{def.TeamName}' or '{def.RoleName}' could not be read back after it was created. Resume re-runs H7c " +
                    "(every step reads first).");
            }
            if (s.Copy is null)
            {
                return Refuse(FailureClass.Resumable, CustomerOwnerTeamsRejectionCodes.RoleCopyMissing,
                    $"'{def.RoleName}' ({s.Role.Id}) has no copy in '{topology.Customer.Name}' ({topology.Customer.Id}) to assign. " +
                    $"Nothing further was written for it. Dataverse copies a root role into every child unit; resume once the copy exists.");
            }
            var roleId = s.Role.Id;
            var team = s.Team;
            var copy = s.Copy;

            if (createdTeam)
            {
                // Only a team THIS run created: a member it has now was added by the create itself, never by a person.
                var added = await dv.ListTeamMembersAsync(t, team.Id, CreatedTeamMemberPage, ct).ConfigureAwait(false);
                if (added.Count > 0)
                {
                    await Do($"remove the {added.Count} member(s) the create added to '{def.TeamName}'",
                        () => dv.RemoveTeamMembersAsync(t, team.Id, added, ct)).ConfigureAwait(false);
                }
            }

            var heldById = s.Held.GroupBy(h => h.PrivilegeId).ToDictionary(g => g.Key, g => g.First());
            var toAdd = d.Wanted
                .Where(w => !heldById.TryGetValue(w.Privilege.Id, out var h) || !Same(h.Depth, SecureRecordSetupProcedure.BasicDepth))
                .ToArray();
            if (toAdd.Length > 0)
            {
                foreach (var (table, privilege) in toAdd.Where(w => heldById.ContainsKey(w.Privilege.Id)))
                {
                    // Narrowing a declared privilege to Basic: take it off, then add it at Basic below (never widened).
                    await Do($"remove {privilege.Name} ({heldById[privilege.Id].Depth}) from '{def.RoleName}' to re-add it at Basic",
                        () => dv.RemovePrivilegeAsync(t, roleId, privilege.Id, ct)).ConfigureAwait(false);
                }
                await Do($"add Read at Basic to '{def.RoleName}': {string.Join(", ", toAdd.Select(w => w.Privilege.Name))}",
                    () => dv.AddBasicPrivilegesAsync(t, roleId, topology.Root.Id, toAdd.Select(w => w.Privilege).ToArray(), ct))
                    .ConfigureAwait(false);
            }

            // After AddPrivilegesRole, which re-injects platform privileges: everything outside the file comes off THIS role.
            var held = await dv.GetRolePrivilegesAsync(t, roleId, ct).ConfigureAwait(false);
            foreach (var outside in held.Where(h => !d.WantedIds.Contains(h.PrivilegeId)).ToArray())
            {
                await Do($"remove {outside.Name ?? outside.PrivilegeId.ToString()} ({outside.Depth}) from '{def.RoleName}' — outside {def.File}",
                    () => dv.RemovePrivilegeAsync(t, roleId, outside.PrivilegeId, ct)).ConfigureAwait(false);
            }

            if (!s.Assigned.Any(a => a.IsTeam && a.PrincipalId == team.Id && a.RoleId == copy.Id))
            {
                await Do($"assign the '{topology.Customer.Name}' copy of '{def.RoleName}' ({copy.Id}) to '{def.TeamName}' ({team.Id})",
                    () => dv.AssociateTeamRoleAsync(t, team.Id, copy.Id, ct)).ConfigureAwait(false);
            }
        }

        // ============================ PHASE 3 — read back and verify ============================
        (IReadOnlyList<string> Findings, IReadOnlyList<CustomerOwnerTeamState> Teams, IReadOnlyList<string> Notes) readBack;
        try
        {
            readBack = await VerifyCoreAsync(request, topology, declared, ct).ConfigureAwait(false);
        }
        catch (RefusalException refusal)
        {
            return Refuse(refusal.Class, refusal.Code, refusal.Diagnostic);
        }
        if (readBack.Findings.Count > 0)
        {
            return Refuse(FailureClass.Resumable, CustomerOwnerTeamsRejectionCodes.VerifyFailed,
                $"Read-back still shows {readBack.Findings.Count} drift finding(s); the setup is not complete: " +
                string.Join(" | ", readBack.Findings) +
                " Dataverse may still be applying a write; resume re-runs every step (each reads first).");
        }
        return new CustomerOwnerTeamsOutcome.Applied(actions.ToArray(), readBack.Teams, readBack.Notes);
    }

    // ------------------------------------------------------------------ verify (-Verify, read-only; H13)

    /// <summary>
    /// Read-only (the script's <c>-Verify</c>): never writes. Returns <see cref="CustomerOwnerTeamsVerification.Passed"/> or
    /// <see cref="CustomerOwnerTeamsVerification.Drifted"/> (a refusal of the environment is a finding too); throws
    /// <see cref="SecureRecordSetupDataverseException"/> for a Dataverse fault and lets cancellation propagate.
    /// </summary>
    public async Task<CustomerOwnerTeamsVerification> VerifyAsync(CustomerOwnerTeamsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var topology = await ReadTopologyAsync(request, ct).ConfigureAwait(false);
            var declared = await ReadDeclaredAsync(request, ct).ConfigureAwait(false);
            var (findings, teams, _) = await VerifyCoreAsync(request, topology, declared, ct).ConfigureAwait(false);
            return findings.Count == 0
                ? new CustomerOwnerTeamsVerification.Passed(teams)
                : new CustomerOwnerTeamsVerification.Drifted(findings);
        }
        catch (RefusalException refusal)
        {
            return new CustomerOwnerTeamsVerification.Drifted([$"[{refusal.Code}] {refusal.Diagnostic}"]);
        }
    }

    /// <summary>Every declared team's drift (the script's Get-State + Get-Drift). Throws <see cref="RefusalException"/>.</summary>
    private async Task<(IReadOnlyList<string> Findings, IReadOnlyList<CustomerOwnerTeamState> Teams, IReadOnlyList<string> Notes)>
        VerifyCoreAsync(CustomerOwnerTeamsRequest request, Topology topology, IReadOnlyList<Declared> declared, CancellationToken ct)
    {
        var findings = new List<string>();
        var teams = new List<CustomerOwnerTeamState>();
        var notes = new List<string>();
        foreach (var d in declared)
        {
            var s = await ReadStateAsync(request.Target, d, topology, ct).ConfigureAwait(false);
            var drift = Drift(d, s, topology);
            findings.AddRange(drift.Select(f => f.Text));
            if (drift.Count == 0)
            {
                teams.Add(new CustomerOwnerTeamState(d.Definition.File, d.Definition.TeamName, d.Definition.RoleName,
                    s.Team!.Id, s.Role!.Id, s.Copy!.Id, s.Held.Count));
            }

            // Information only: the other customer top units that do not have the team yet (each is set up by its own run).
            foreach (var unit in topology.CustomerTops.Where(u => u.Id != topology.Customer.Id).OrderBy(u => u.Name, StringComparer.Ordinal))
            {
                if (!s.AllTeams.Any(x => x.BusinessUnitId == unit.Id && x.TeamType == OwnerTeamType && !x.IsDefault))
                {
                    notes.Add($"customer top unit '{unit.Name}' ({unit.Id}) has no '{d.Definition.TeamName}' team (not this run's unit; not a failure).");
                }
            }
        }
        return (findings, teams, notes);
    }

    // ------------------------------------------------------------------ reads

    private async Task<Topology> ReadTopologyAsync(CustomerOwnerTeamsRequest request, CancellationToken ct)
    {
        var t = request.Target;
        var dv = _dataverse;

        var roots = await dv.FindRootBusinessUnitsAsync(t, ct).ConfigureAwait(false);
        if (roots.Count != 1)
        {
            throw new RefusalException(FailureClass.Resumable, CustomerOwnerTeamsRejectionCodes.RootBusinessUnitUnresolved,
                $"Expected exactly one root business unit; found {roots.Count}.");
        }
        var root = roots[0];

        // FAIL CLOSED (R4b-4): the Secure Record unit exists by this step (H7b), so a name that matches no unit is a wrong
        // name and would let the Secure Record unit pass as a customer unit.
        var secureUnits = await dv.FindBusinessUnitsByNameAsync(t, request.SecureBusinessUnitName, ct).ConfigureAwait(false);
        if (secureUnits.Count == 0)
        {
            throw new RefusalException(FailureClass.Resumable, CustomerOwnerTeamsRejectionCodes.SecureBusinessUnitUnresolved,
                $"No business unit is named '{request.SecureBusinessUnitName}'. The Secure Record unit exists by this step " +
                "(H7b creates it); check H7b's output.");
        }
        if (secureUnits.Count > 1)
        {
            throw new RefusalException(FailureClass.Resumable, CustomerOwnerTeamsRejectionCodes.SecureBusinessUnitUnresolved,
                $"More than one business unit is named '{request.SecureBusinessUnitName}'; the Secure Record unit cannot be told apart.");
        }
        var secureId = secureUnits[0].Id;

        var customer = await dv.GetBusinessUnitAsync(t, request.CustomerBusinessUnitId, ct).ConfigureAwait(false);
        if (customer is null)
        {
            throw new RefusalException(FailureClass.QuarantineRequired, CustomerOwnerTeamsRejectionCodes.CustomerBusinessUnitMissing,
                $"Expected exactly one business unit id {request.CustomerBusinessUnitId} (H10's customer unit); found none. " +
                "Find out what removed it (an owner decision).");
        }
        if (customer.Id == root.Id)
        {
            throw new RefusalException(FailureClass.QuarantineRequired, CustomerOwnerTeamsRejectionCodes.CustomerBusinessUnitIsRoot,
                $"Refused: business unit id {customer.Id} is the ROOT business unit. The root holds only Spaarke product data; " +
                "customer records go to the customer's own top unit (owner round 136).");
        }
        if (customer.Id == secureId)
        {
            throw new RefusalException(FailureClass.QuarantineRequired, CustomerOwnerTeamsRejectionCodes.CustomerBusinessUnitIsSecure,
                $"Refused: business unit id {customer.Id} is the Secure Record business unit, never a customer unit.");
        }
        if (customer.ParentId != root.Id)
        {
            throw new RefusalException(FailureClass.QuarantineRequired, CustomerOwnerTeamsRejectionCodes.CustomerBusinessUnitWrongParent,
                $"Refused: business unit '{customer.Name}' ({customer.Id}) is not a direct child of the root (its parent is " +
                $"{customer.ParentId?.ToString() ?? "(none)"}). The teams live in the customer's TOP unit; the BFF walks up to it " +
                "from any unit below. Re-parenting a unit is an owner decision.");
        }

        // Every customer top unit: a direct child of the root that is not the Secure Record unit.
        var tops = (await dv.ListChildBusinessUnitsAsync(t, root.Id, ct).ConfigureAwait(false))
            .Where(u => u.Id != secureId)
            .ToArray();
        return new Topology(root, secureId, customer, tops);
    }

    private async Task<List<Declared>> ReadDeclaredAsync(CustomerOwnerTeamsRequest request, CancellationToken ct)
    {
        // Each table's Read privilege, from METADATA: exactly one, named ordinally as the file names it.
        var declared = new List<Declared>();
        foreach (var def in request.RoleSet.Definitions)
        {
            var wanted = new List<(string, SecureSetupPrivilege)>();
            foreach (var table in def.Tables)
            {
                var privileges = await _dataverse.GetReadPrivilegesAsync(request.Target, table.LogicalName, ct).ConfigureAwait(false);
                if (privileges.Count != 1)
                {
                    throw new RefusalException(FailureClass.Resumable, CustomerOwnerTeamsRejectionCodes.PrivilegeNameMismatch,
                        $"Table '{table.LogicalName}' ({def.File}): expected one Read privilege in metadata, found {privileges.Count}. " +
                        "Is the package imported (H6)?");
                }
                if (!string.Equals(privileges[0].Name, table.PrivilegeName, StringComparison.Ordinal))
                {
                    throw new RefusalException(FailureClass.Resumable, CustomerOwnerTeamsRejectionCodes.PrivilegeNameMismatch,
                        $"Table '{table.LogicalName}' ({def.File}): metadata names its Read privilege '{privileges[0].Name}', the " +
                        $"config says '{table.PrivilegeName}'. Fix the config (casing follows the schema name).");
                }
                wanted.Add((table.LogicalName, privileges[0]));
            }
            declared.Add(new Declared(def, wanted));
        }
        return declared;
    }

    private async Task<TeamState> ReadStateAsync(SecureRecordSetupTarget t, Declared d, Topology topology, CancellationToken ct)
    {
        var dv = _dataverse;
        var def = d.Definition;

        // Every team of this name, wherever it is: the customer unit's is checked; the others decide which assignments are allowed.
        var allTeams = await dv.FindTeamsByNameAsync(t, def.TeamName, ct).ConfigureAwait(false);
        var inUnit = allTeams.Where(x => x.BusinessUnitId == topology.Customer.Id).ToArray();
        if (inUnit.Length > 1)
        {
            throw new RefusalException(FailureClass.QuarantineRequired, CustomerOwnerTeamsRejectionCodes.TeamAmbiguous,
                $"Expected at most one team named '{def.TeamName}' in '{topology.Customer.Name}'; found {inUnit.Length} " +
                $"({Ids(inUnit.Select(x => x.Id))}). Resolve it by hand (owner decision).");
        }
        var team = inUnit.SingleOrDefault();
        IReadOnlyList<Guid> members = [];
        IReadOnlyList<SecureSetupRole> teamRoles = [];
        if (team is not null)
        {
            members = await dv.ListTeamMembersAsync(t, team.Id, NamedPrincipalLimit, ct).ConfigureAwait(false);
            teamRoles = await dv.ListTeamRolesAsync(t, team.Id, ct).ConfigureAwait(false);
        }

        var roles = await dv.FindRolesAsync(t, def.RoleName, topology.Root.Id, ct).ConfigureAwait(false);
        if (roles.Count > 1)
        {
            throw new RefusalException(FailureClass.Resumable, CustomerOwnerTeamsRejectionCodes.RoleAmbiguous,
                $"Expected at most one role named '{def.RoleName}' in the root business unit; found {roles.Count} " +
                $"({Ids(roles.Select(r => r.Id))}).");
        }
        var role = roles.SingleOrDefault();
        IReadOnlyList<SecureSetupRole> copies = [];
        SecureSetupRole? copy = null;
        var assigned = new List<Assignment>();
        IReadOnlyList<SecureSetupHeldPrivilege> held = [];
        if (role is not null)
        {
            // A role created in the root business unit is copied into every child unit; a team holds its own unit's copy.
            // Every copy's assignments count.
            copies = await dv.ListRoleCopiesAsync(t, role.Id, ct).ConfigureAwait(false);
            copy = copies.FirstOrDefault(c => c.BusinessUnitId == topology.Customer.Id && c.Id != role.Id);
            foreach (var id in new[] { role.Id }.Concat(copies.Select(c => c.Id).Where(c => c != role.Id)).Distinct())
            {
                foreach (var teamId in await dv.ListRoleTeamHoldersAsync(t, id, ct).ConfigureAwait(false))
                {
                    assigned.Add(new Assignment(true, teamId, id));
                }
                foreach (var userId in await dv.ListRoleUserHoldersAsync(t, id, ct).ConfigureAwait(false))
                {
                    assigned.Add(new Assignment(false, userId, id));
                }
            }
            held = await dv.GetRolePrivilegesAsync(t, role.Id, ct).ConfigureAwait(false);
        }

        return new TeamState(allTeams, team, members, teamRoles, role, copies, copy, assigned, held);
    }

    // ------------------------------------------------------------------ drift (the script's Get-Drift)

    private static IReadOnlyList<Finding> Drift(Declared d, TeamState s, Topology topology)
    {
        var def = d.Definition;
        var customerName = topology.Customer.Name;
        var r = new List<Finding>();
        if (s.Team is null)
        {
            r.Add(new(DriftKind.TeamMissing, $"TEAM MISSING: no team named '{def.TeamName}' in {customerName}."));
        }
        else
        {
            if (s.Team.TeamType != OwnerTeamType)
            {
                r.Add(new(DriftKind.TeamType, $"TEAM TYPE: '{def.TeamName}' is teamtype {s.Team.TeamType}, expected 0 (Owner)."));
            }
            if (s.Team.IsDefault)
            {
                r.Add(new(DriftKind.TeamDefault, $"TEAM DEFAULT: '{def.TeamName}' is a business unit's default team."));
            }
            if (s.Members.Count > 0)
            {
                r.Add(new(DriftKind.TeamMembers,
                    $"TEAM MEMBERS: '{def.TeamName}' has {s.Members.Count}{(s.Members.Count >= NamedPrincipalLimit ? "+" : string.Empty)} " +
                    $"member(s) ({Ids(s.Members)}); it must have none."));
            }
            var roleIds = new HashSet<Guid>(s.Copies.Select(c => c.Id));
            if (s.Role is not null)
            {
                roleIds.Add(s.Role.Id);
            }
            foreach (var other in s.TeamRoles.Where(x => !roleIds.Contains(x.Id)))
            {
                r.Add(new(DriftKind.TeamRole, $"TEAM ROLE: '{def.TeamName}' holds another role '{other.Name}' ({other.Id})."));
            }
        }

        if (s.Role is null)
        {
            r.Add(new(DriftKind.RoleMissing, $"ROLE MISSING: no role named '{def.RoleName}' in the root business unit."));
            return r;
        }

        if (s.Copy is null)
        {
            r.Add(new(DriftKind.RoleCopyMissing, $"ROLE COPY MISSING: '{def.RoleName}' has no copy in {customerName}."));
        }

        // Allowed: a team of this name, an Owner non-default team in a customer top unit, holding THAT unit's copy.
        var copyByUnit = new Dictionary<Guid, Guid>();
        foreach (var c in s.Copies)
        {
            copyByUnit.TryAdd(c.BusinessUnitId, c.Id);
        }
        var tops = topology.CustomerTops.Select(u => u.Id).ToHashSet();
        var allowed = s.AllTeams
            .Where(x => x.TeamType == OwnerTeamType && !x.IsDefault && tops.Contains(x.BusinessUnitId)
                        && copyByUnit.ContainsKey(x.BusinessUnitId))
            .Select(x => (x.Id, copyByUnit[x.BusinessUnitId]))
            .ToHashSet();
        foreach (var a in s.Assigned.Where(a => !(a.IsTeam && allowed.Contains((a.PrincipalId, a.RoleId)))))
        {
            r.Add(new(DriftKind.RoleAssignedElsewhere,
                $"ROLE ASSIGNED ELSEWHERE: '{def.RoleName}' ({a.RoleId}) is assigned to {(a.IsTeam ? "team" : "user")} {a.PrincipalId}."));
        }
        if (s.Team is not null && s.Copy is not null
            && !s.Assigned.Any(a => a.IsTeam && a.PrincipalId == s.Team.Id && a.RoleId == s.Copy.Id))
        {
            r.Add(new(DriftKind.RoleNotAssigned,
                $"ROLE NOT ASSIGNED: the {customerName} copy of '{def.RoleName}' is not assigned to '{def.TeamName}'."));
        }

        var heldById = s.Held.GroupBy(h => h.PrivilegeId).ToDictionary(g => g.Key, g => g.First());
        foreach (var (table, privilege) in d.Wanted)
        {
            if (!heldById.TryGetValue(privilege.Id, out var h))
            {
                r.Add(new(DriftKind.PrivilegeMissing, $"PRIVILEGE MISSING: {privilege.Name} ({table})."));
            }
            else if (!Same(h.Depth, SecureRecordSetupProcedure.BasicDepth))
            {
                r.Add(new(DriftKind.PrivilegeDepth, $"PRIVILEGE DEPTH: {privilege.Name} ({table}) at {h.Depth}, expected Basic."));
            }
        }
        foreach (var o in s.Held.Where(h => !d.WantedIds.Contains(h.PrivilegeId)).OrderBy(h => h.Name, StringComparer.Ordinal))
        {
            r.Add(new(DriftKind.PrivilegeOutsideFile,
                $"PRIVILEGE OUTSIDE THE FILE: '{def.RoleName}' holds {o.Name ?? o.PrivilegeId.ToString()} at {o.Depth}."));
        }
        return r;
    }

    private static string OwnerDecisionCode(DriftKind kind) => kind switch
    {
        DriftKind.TeamMembers => CustomerOwnerTeamsRejectionCodes.TeamHasMembers,
        DriftKind.TeamType or DriftKind.TeamDefault => CustomerOwnerTeamsRejectionCodes.TeamNotOwnerTeam,
        DriftKind.TeamRole => CustomerOwnerTeamsRejectionCodes.TeamHasOtherRole,
        _ => CustomerOwnerTeamsRejectionCodes.RoleAssignedElsewhere,
    };

    internal static string TeamDescription(CustomerOwnerRoleDefinition def) =>
        $"Memberless owner team for the tables of config/{def.File} in this customer business unit. MUST NEVER have a member " +
        "or any other role. Created by provisioning (H7c).";

    internal static string RoleDescription(CustomerOwnerRoleDefinition def) =>
        $"Read at Basic on the tables of config/{def.File} only, so each customer unit's memberless {def.TeamName} team can own " +
        "their rows. Its copies are assigned to those teams only. MUST NOT be granted to any user or any other team. Created " +
        "by provisioning (H7c).";

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Ids(IEnumerable<Guid> ids)
    {
        var list = ids.ToArray();
        var shown = string.Join(", ", list.Take(NamedPrincipalLimit));
        return list.Length > NamedPrincipalLimit ? $"{shown}, +{list.Length - NamedPrincipalLimit} more" : shown;
    }
}

# Task 104: Ontology admin capability probe and the gated tab

Lane `task/ontology-104`, base `eb10d114b` (docs/ontology-platform-design). FULL rigor, Opus at high effort.

## 1. Pre-start checks

### 1.1 Reconciliation B.6 (HANDOFF @ `ae1cc9f`)

Task 104 has no B.6 row of its own; the admin rows (100/102/103) point at H§1.3 `RuleWizard` (L97) and H§8.

- **H§8** lists seven admin sections: Rules, the Rule wizard (same shell), Classification, Actions, Suppression, Health,
  and Access & audit. R1 builds **Rules** (task 100, with 102's wizard opened from it) and **Classification** (task
  103) only. The tab shell has those two areas and no others. Actions, Suppression, Health and Access (M-11..M-14) are
  not built (POML constraint; spec FR-57..FR-59). The rest of the reconciliation is C-16, undecided.
- **v4 location.** v4 reaches admin through "Top bar → Admin". The Console has no top bar and no full-page mode
  (#47), so D-22 option (a) applies: a workspace tab, shown only when the BFF probe says yes. This is already the
  owner's decision (D-22), not a new inconsistency to raise.
- **H§3 data needs**: none for the shell; the probe reads only the caller's own privilege set.
- **H§4.1 wizard host**: not used by the shell (102's wizard uses the 058 host).

### 1.2 uac-r2 re-read (owner 2026-10-07 coordination constraint)

`origin/master` at **`a8c3182ecc0c8fe8e27b56ba50e98fe60a658d37`**, fetched 2026-10-16. I re-read these files and
compared each with the base `eb10d114b`:

| File | Change since base |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Access/SecureChildLineage.cs` | none |
| `config/secure-record-owner-role.json` | none |
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordOwnershipResolver.cs` (I-6) | none |
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/CoreAncestorResolver.cs` | none |
| `src/server/api/Sprk.Bff.Api/Api/Filters/RecordRouteAccessAuthorizationFilter.cs` | none |
| `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/CallerRecordAccessProbe.cs` | none |
| `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalCallerContext.cs` | none |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` | `720200e64`: re-annotates the existing ontology rows (D-141/D-145a "mine" notes and line refs); no rule, list shape or count change |

None of these changes invalidates the plan. The task reuses uac-r2's mechanisms and builds no parallel one:
- the census (one GovernedFile, the count, one Permanent `CallerScopedOnly` waiver, the `/me` and
  `GET /api/reporting/status` precedent);
- the caller-identity client `IDataverseUserClient`;
- the single 403 reason code (`sdap.access.deny.caller_unresolved`).

`CallerRecordAccessProbe` is record-level (`RetrievePrincipalAccess`) and cannot answer a privilege question, so it is
not used. The ledger is a uac-r2-owned file. This task only adds entries to it (the pattern 036/038/043/071 followed);
the main session routes the PR through uac-r2's review (#1355) per D-115.

## 2. Escalation trigger: checked, did not fire

The trigger: "the caller's privilege cannot be read as the caller without a privilege Console Users lack". Verified on
spaarkedev1, 2026-10-16, read-only, using the operator `az` token with `MSCRMCallerID` (the security-roles §11.1
method):

- `prvCreatesprk_PolicyVersion` exists (privilegeid `286d6b3e-7395-46f5-aac9-921da76552b1`).
- **Owner** (Ralph Schroeder, `1d02f31c-…`): `RetrieveUserPrivilegeByPrivilegeName` returns one row, Depth Global.
- **Chelsea Friez** (`c46d44ca-…`): Core User, Console User and Office Add In User directly, plus Console, Basic and
  Office Add In through the default `Spaarke` team. The same call, run as her about herself, returns **200 with
  `RolePrivileges: []`**: she can ask, and the answer is "not held". She holds `prvReadUser` directly and through the
  team.
- **Impersonation took effect.** In the same session, `GET solutions` as Chelsea was refused with 403 `0x80040220`
  naming her (she lacks `prvReadSolution`). So the function call really ran with her privileges. `WhoAmI` ignores
  `MSCRMCallerID`, as the task 038 live test notes.
- **Team roles count.** Three privileges Chelsea holds only through a team role came back from the function, so an
  administrator who holds the role through a team is answered correctly.

Through the BFF route, the live leg `OntologyAdminCapabilityLiveTests` returned:
- the owner: `200 {"canAuthorPolicies":true}`;
- Chelsea: `200 {"canAuthorPolicies":false}`.

## 3. Design decisions (stated in the PR)

- **Not a `sprk_workspacelayout` row.** The worklist reached the Console through a LegalWorkspace section and a
  system layout row (task 156). That route cannot meet D-22's "registered only when the probe says yes":
  - a system layout row is listed in every user's Manage Workspaces pane;
  - the section would also be offered in the layout wizard's catalog, so a non-admin could compose it into their own
  layout.
  So the tab is a **gated direct widget** (`ontology-admin` in `WorkspaceWidgetRegistry`), registered only by
  `ontologyAdminGate.ts` after the probe answers true. Its one opener is an "Administration" entry in the Manage
  Workspaces pane, listed only while the gate is true. No layout seed is needed, so none was prepared.
- **Tab restore.** `resolveWorkspaceWidget` falls back to a generic widget for an unregistered type, so a persisted admin
  tab would come back as a dead tab. Because the type is single-instance, reopening it from Manage Workspaces would only
  focus that dead tab. Two changes prevent this:
  - The workspace pane starts `whenOntologyAdminGateSettled()` (bounded at 3 s) when restore begins, in parallel with the
    tabs GET, and awaits it only before restoring a snapshot (server or local) that holds an `ontology-admin` tab, so an
    administrator's reloaded tab is the real widget and no other restore, default-layout install or pinned-tab auto-open
    waits on the probe (fix round F1; the first version awaited it before the GET for every user).
  - Restore resolves through `resolveRestorableWorkspaceWidget`, which returns null for `ontology-admin` while the gate
    says no. The tab manager then skips the tab. This covers a revoked role, and a probe slower than 3 s; in the slow case
    the admin reopens the tab from the pane once the gate answers.
  The second change came from this task's code review (step 9.5).
- **Probe outcomes.**
  - 200 with the boolean: a clean answer.
  - 401: no token.
  - 403 (the single #1312 answer): WhoAmI cannot name the caller (no user context, OBO refused or not configured,
    Dataverse 401/403, or no id).
  - 503: any other fault, including a refusal of the privilege read.
  - The client treats anything but `200 true` as "no tab".
  - A transient OBO failure (`OboExchangeFailed`) is in the "caller cannot be named" set, so it returns the uniform 403,
    not a 503; the Console treats both as "no tab" (fix round K1).
- **Cache.** `ITenantCache` (Redis) per caller (`oid`), with TTL `min(token exp - now, 5 min)`.
  - ADR-009 rules: "cache data only" and "short TTLs for security data". The value is display data; nothing
    authorizes on it.
  - Never cached: a fault, or a token without `tid`, `oid` or `exp`.
  - A role change takes effect after a reload plus up to 5 min of server cache. This is acceptable under D-22 because the
    answer only decides what is shown; every write is authorized as the caller, so a stale answer can show a tab but
    never grant a write (fix round K1). `OntologyAdminCallerBoundaryGuardTests` (ArchTests) enforces that nothing reads
    the answer or writes through an app-identity client (fix round K2).
- **No new service or DI registration.** The handler uses `IDataverseUserClient` (unconditional in `AddSpaarkeCore`),
  `ITenantCache` and `TimeProvider`.

## 4. Extension points for tasks 100, 101 and 103

- **BFF.** `OntologyAdminEndpoints.MapOntologyAdminEndpoints` creates the `/api/v1/ontology/admin` group (authorization
  and rate limit already on it); 100 and 101 map their routes on that group. 103's POML names its own
  `OntologyAdminClassificationEndpoints.cs`. Every write authorizes itself as the caller; **no route may read the
  capability answer**.
  The probe, its cache and `OntologyAdminCapabilities` live in `OntologyAdminCapabilityProbe.cs`, the only file rule A of
  `OntologyAdminCallerBoundaryGuardTests` exempts: a route file reaches it through the one
  `OntologyAdminCapabilityProbe.ResolveAsync` call in `GetCapabilitiesAsync` and nothing else, so a new handler in
  `OntologyAdminEndpoints.cs` cannot call the probe or read its cache without failing the guard.
- **UI.**
  - `OntologyAdminTab` (`@spaarke/ui-components`) has two slots: `rules` (100) and `classification` (103).
  - `OntologyAdminWorkspaceWidget` (`@spaarke/ai-widgets`) is where those areas are built from `useAiSession()` and
    passed in, the way `WorklistWorkspaceWidget` builds the worklist service.
  - An empty slot shows a neutral empty state.

## 5. Deviations from the POML

- **Step 4 (TASK-INDEX + POML status) not done here.** The lane brief makes the main session the owner of
  `TASK-INDEX.md` and of the status. Both must be set together at merge (`check-task-status-drift.ps1`).
- **"Three sub-areas (Rules, Classification, and nothing else in R1)"**: read as the two named areas plus the "nothing
  else" clause. 102's rule wizard opens in-app from Rules, not as an area.
- **Relevant-files list.** Beyond the four files the POML names, the change touches:
  - `ontologyAdminCapability.ts` and `OntologyAdmin/index.ts`;
  - the ai-widgets gate and widget host;
  - three Console wiring edits: `main.tsx` (start the gate), `WorkspacePane.tsx` (restore wait) and
    `ManageWorkspacesPane.tsx` (the entry);
  - the BFF mapping line.
  Without the Console wiring, a registered widget has no opener, the gap task 156 found for the worklist.

## 6. Tests beyond the stated contract (one line each)

- Cache tests (per caller, no sharing, no `exp` means no cache, TTL bound): required by the D-22 owner constraint
  "caches per caller for at most the token lifetime".
- Fault-is-503-and-not-cached cases: needed so a fault never reads as "not an admin" and is never stuck in the cache.
- Gate restore-wait and restore-resolver tests: needed for the restore fixes in §3. Without them those paths are unproven.

## 7. Verification

- **Tests.**
  - BFF `OntologyAdminCapabilityEndpointTests`: 24 of 24 pass.
  - ArchTests: 87 of 87 pass.
  - Live leg: 2 of 2 pass (owner true, Chelsea false).
  - ui-components `OntologyAdmin.test`: 15 of 15 pass.
  - ai-widgets `ontologyAdminGate.test`: 13 of 13 pass.
  - SpaarkeAi `src/components/workspace`: 213 of 213 pass, including 5 new ones.
- **Seeding** (each test failed before the change or under a mutation):
  - BFF route unmapped: 16 fail.
  - BFF cache keyed by tenant only: 8 fail.
  - Census with the original ledger: 3 fail.
  - Pane test against the base pane: 4 fail.
  - Pane gate condition forced true: 1 fails.
  - Gate ignoring the answer: 1 fails.
  - Gate registering at module load: 3 fail.
  - Restore resolver not filtering: 2 fail.
- **Publish** (Release, from a short path, Compress-Archive): base `origin/docs/ontology-platform-design` has 192 files
  and 38,949,291 bytes. This branch has 192 files and 38,955,090 bytes, a **+5,799 B** delta. There are no package
  changes.
- **Lint.** The client-lint ratchet passes for ui-components, ai-widgets and spaarkeai. One `no-unnecessary-condition`
  hit on the new restore-cancel check is a false positive (TS cannot see the cleanup's write across the await), so it is
  justified inline.

## 8. Found in passing (not fixed; D-106)

- **Registration test failures on the base.** `Spaarke.AI.Widgets/src/widgets/workspace/__tests__/register-workspace-widgets.test.ts:436`
  and `:775` fail on the base `eb10d114b` too (3 tests): the communications-list and worklist lazy imports resolve to the
  fallback in this worktree. This was not caused by this task.
- **rowMenu crash.** `SpaarkeAi/.../__tests__/ManageWorkspacesPane.rowMenu.test.tsx:39` crashes the jest worker on the
  base (`navigateToWebResourceSurfaceAsync` is not stubbed). It is already fixed on master in `1c9a88b30` (#1898/#1906)
  and reaches this branch with the next master merge.
- **Duplicated reason-code literal.** `"sdap.access.deny.caller_unresolved"` is declared twice, at
  `Api/Events/EventEndpoints.cs:67` and `Api/Signals/DecisionPlanEndpoints.cs:25`. The values are equal, so this is not a
  defect.

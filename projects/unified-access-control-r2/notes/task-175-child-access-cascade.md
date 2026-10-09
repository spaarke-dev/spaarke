# Task 175: a filed work assignment / project follows its parent both ways, locked while filed

Owner round 84 (2026-10-08, binding): "Child access should always follow parent; if parent changes, then child changes. A
protection is that if a child has a parent then the access cannot be changed manually (e.g. its locked)." Follow-ups the
same day: un-securing cascades ("Child follows"); it applies to work assignments and projects ("all children").
Absorbs #1478 (five readers that decided Restricted from the record's own stored column). Carries task 067's verifier
amendment (the Manage Access lock).

Branch `task/uac-r2-175`, built on `task/uac-r2-174` and rebased onto `origin/master` `e78c47149` (#1481 merged).

## Superseded (for `notes/decisions.md`, on the work branch)

> **2026-10-09 — round 84 / task 175 supersedes round 6 item 4 and task 158's "no unsecure cascade" constraint.** A work
> assignment or project filed under a matter or project follows it OUT of secure when no other ancestor is secure, through
> `/unsecure-project`'s own steps (task 158's order and stops), from the parent's unsecure, a re-file and the 5-minute job.
> A record WITH a parent cannot be made secure or un-secured, and its Access Permission cannot be set, by hand (409
> `sdap.access.access_follows_parent`); F3 applies only to a parentless record. Nothing else about task 158 changed.

(`notes/decisions.md` exists only on `work/unified-access-control-r2`; this PR is against master, so the main session adds
the entry there, as for task 173.)

## Inventory (step 1): every path that sets `sprk_issecure` / `sprk_accesspermission` on a work assignment or project

| Path | Before | Now |
|---|---|---|
| `POST /provision-project` (Make Secure; the wizards' create-then-secure) | sets `sprk_issecure` | refused 409 `access_follows_parent` when the record has a parent; unchanged otherwise |
| `ProvisionProjectEndpoint.ProvisionInheritedAsync` (task 158: parent secured, create into isolation, re-file, job) | sets `sprk_issecure` | unchanged (the securing direction stays task 158's) |
| `POST /unsecure-project` | clears `sprk_issecure` (F3) | refused 409 when the record has a parent; on a parent, its filed records follow it (step 6) |
| `UnsecureProjectEndpoint.UnsecureInheritedAsync` (new) | — | the cascade's un-secure: the endpoint's own steps, no F3, a team owner |
| `OwnedChildWrite` isolated create (AI tool) | sets `sprk_issecure = true` in the create | unchanged (create into isolation) |
| BFF generic updates through `SecureRootFilingGate.CheckAsync` (`DataverseUpdateHandler`, the AI update tool, the playbook update node, field-mapping push) | could write either column | refused `access_follows_parent` when the record will have a parent after the write; allowed on a parentless one; creates not refused |
| Model-driven forms, grid edit, import, flow, direct Web API | write freely | the form lock (`sprk_accesspermission_inherited.js` 1.1.0) disables both columns while filed; anything that slips through is reverted by the job (≤ 5 min) |
| A parent's `sprk_accesspermission` / `sprk_issecure` change outside the BFF | no cascade | the job (≤ 5 min); enforcement already follows the parent (task 174) |

No BFF route wrote `sprk_accesspermission` on a work assignment or project explicitly.

## What was built (goal items)

1. **Downward cascade on every change** (`Services/Access/SecureRootInheritance.Cascade.cs`, a partial of the ONE owner):
   - `FollowParentsAsync(table, id)`: the 174 walk (`ReadSecureParentsAsync`, `MaxFilingDepth`) decides the target —
     secure if any ancestor is; Access Permission = the most restrictive EFFECTIVE rank of the DIRECT parents. Parentless:
     nothing. Unreadable filing / parent / own flag: nothing loosened, reported.
   - Secure: unchanged (task 158's `SecureIfFiledUnderSecureAsync`, bounded by its own callers).
   - Un-secure (new): `UnsecureProjectEndpoint.UnsecureInheritedAsync` with the owner the ownership rule gives a record filed
     under its now-ordinary parents (`RecordOwnershipResolver.ResolveOwnerAsync(ForParents(...))` → their business unit's
     team, D-11). Refused when that owner is the Secure Record Owners team or unresolvable (stays secure).
   - Access Permission: one-column write, only when it differs; a stricter value first, a looser one only after an
     un-secure completed.
   - Another secure or more restrictive ancestor keeps the most restrictive value (the walk's fold).
2. **Re-file.** `SecureAfterWriteAsync` (every BFF re-file writer) now also runs `FollowParentsAsync` (and cascades below a
   project that changed). Out-of-BFF re-files: the job. A record left with no parent keeps its values and is editable (the
   gate and the form lock both treat it as parentless).
3. **The cascade runs in the existing jobs.** `SecureRootInheritanceJob` (5 min) adds `FollowParentsPassAsync` after its
   securing loop: every filed work assignment / project listed (one paged query per table), decided over ONE batched walk,
   only differing rows acted on (a fresh single-record walk each), a project changed in the run sends what is filed under it
   round again in the same run (≤ `MaxFilingDepth` rounds), 25 un-secures (cursor) + 500 permission writes per run, the rest
   deferred (run not a success, next run continues). No new job, timer or registration. Idempotent; resumable.
   `SecureChildReconciliationJob` was not needed: task 173's reconcile already descends from a changed work assignment to its
   To Do / Event / Communication / Document (a write changes `modifiedon`).
4. **Lock.**
   - 409 `sdap.access.access_follows_parent` (`AccessFollowsParent`, extensions `parentRecordType`, `parentRecordId`,
     `parentName`) from `/provision-project`, `/unsecure-project` and the gate; 500 `*.parent_unverifiable` when the
     filing cannot be read.
   - `GET /can-manage-access` reports `followsParents` (DIRECT parents only — task 174 F1-d) and `parentUnverifiable`.
   - Ribbon `access_ribbon.js` 1.7.0: Make Secure / Remove Secure hidden when `followsParents` is non-empty or
     `parentUnverifiable`; the 409 has a designed message. Update Access unchanged.
   - Forms: `sprk_accesspermission_inherited.js` 1.1.0 locks `sprk_accesspermission` (and `sprk_issecure` when on the form)
     on the work assignment and project main forms with "Access permission and Secure are inherited from {parent}" — typed
     lookups and the regarding pair (its type read from `sprk_recordtype_ref`); `Set-InheritedAccessPermissionFormLock.ps1`
     registers it on those two Main forms; the root map is pinned to the server by
     `ParentLineageTests.FormLibraryRootParentLookups_MatchTheServerFiling`.
   - Manage Access (067 amendment): `AccessGrantModal` takes `followsParents` / `onOpenParent`; hides + Contact /
     + Organization / + User, candidate approve, level dropdowns, Suggested Grant/Dismiss and Revoke; banner "Access follows
     the parent {type} {name}; manage it there" linking the parent; `classifyAccessFailure` maps the 409 (`followsParent`);
     inherited shares (`/user-shares` `inheritedFrom`, from task 158's provenance rows) are a read-only `'inherited'` row kind
     with the veto and cancellation markers still applied; `describeCoverage` names a direct parent only. TrackingFieldTrio
     1.0.44 passes `followsParents`, opens the parent form, and disables the Access Permission pill on a child.
   - Parentless records unchanged (F3 still applies).
5. **Safety.** The un-secure IS `/unsecure-project`'s sequence (owner moved and read back → related records out of
   isolation → provenance ended → shares revoked → flag cleared last). A step that does not complete leaves the record
   flagged secure (and its permission not loosened), reported; the next call or run completes it. S5 holds (the record lands
   on its business unit's team, whose members can open it); round 76 is untouched (Restricted remover unchanged).

### #1478 — the five stored-column readers

All five now also consult `EffectiveRootFlags.RestrictedThroughFilingAsync` (the 174 walk; one helper):
`ProvisionProjectEndpoint` creator rule + colleague filter (unknown → Restricted: bars only an external-flagged person),
`SecureChildShareSynchronizer.RestrictedExternalPrincipalsOrThrowAsync` (unknown → throws, its contract),
`SpeContainerMembershipSync.IsRestrictedAsync` (unknown → null, as before), `OfficeEditAccessService` (unknown → Restricted),
and the Assigned-To job's Restricted sweep (also visits the work assignments / projects filed below a Restricted matter or
project via `ListFiledRootsBelowAsync`). So none fails open between a parent's change and the cascade's write.

### Component justification (CLAUDE.md §11)

- `SecureRootInheritance.Cascade.cs` (`FollowParentsAsync`, `CascadeBelowAsync`, `FollowParentsPassAsync`) — (1) overlaps
  task 158's securing and task 173's child reconciler; (2) it extends the ONE owner (a partial of `SecureRootInheritance`),
  and its un-secure is the endpoint's own steps; (3) without it a matter's unsecure leaves its filed records isolated for good
  and their stored Access Permission stale (#1478).
- `UnsecureProjectEndpoint.UnsecureInheritedAsync` + `UnsecureActor` — (1) the endpoint's private core; (2) the core gains a
  caller/cascade actor and a team owner, no second sequence; (3) the cascade could not un-secure without a caller.
- `AccessFollowsParent` — (1) none; (2) one code shared by three surfaces; (3) the POML's named 409.
- `RecordAccessGateResponse.FollowsParents` / `ParentUnverifiable`, `RecordAccessParent`, `RecordUserShare.InheritedFrom` —
  additive fields on existing responses (no new endpoint); without them the ribbon and Manage Access cannot lock or label.
- `EffectiveRootFlags.RestrictedThroughFilingAsync` — (1) the 174 fold; (2) a thin read over it; (3) #1478's fail-open.
- `ReasonParentUnverifiable` (provisioning), three `sdap.inherit.*` codes — named outcomes of new refusals.
- No package, endpoint, column, DI registration (the optional `IRecordOwnershipResolver` ctor parameter resolves the
  existing singleton), job or timer.

### Placement (bff-extensions.md)

BFF: the transition runs inside the parent's unsecure request and the re-file writers; the safety net is the existing
in-process job (ADR-036/052). BFF identity, the same invariant owner for L1 and L4, low volume.

## Decisions and interpretations

- **Target values.** Secure = any ancestor secure (task 174's effective rule, middle records included). Access Permission =
  the most restrictive EFFECTIVE value through the direct parents (each folded with what is above it), so a stale middle
  project keeps its children at the stricter value until it is itself in step — processed top-down, a chain converges in one
  run / call.
- **The securing direction stays task 158's** (bounded provisioning); `FollowParentsAsync` does un-secure and permission only.
- **Owner on un-secure:** the business-unit team of the parents (D-11: team ownership, record-first), not a user — the
  endpoint's user owner applies only to a caller's own unsecure. Never the Secure Record Owners team (a parent still owned by
  it — flag cleared outside the BFF — leaves the child secure and reported).
- **Lock check = direct parents that exist.** A lookup to a deleted record confers nothing (parentless), as the 174 walk.
- **Creates are not refused** for setting the locked columns (wizards create filed work assignments with a value); the job
  sets the parents' value within 5 minutes and enforcement already follows the parent.
- **`alsoUnsecure`** is still accepted and validated but asks for nothing more; every filed record follows. `relatedSecureRecords`
  now lists what is STILL secure after the cascade (another secure parent, a step not completed, beyond the inline bound).
- **No F3 per related record** (round 84: F3 applies only to parentless records). A consequence to note: filing a secure
  parentless work assignment under an ordinary matter (by anyone with Write on it) un-secures it at the next run — the rule as
  the owner stated it.

## POML premise corrections

- **"Documents back to the business-unit container."** `/unsecure-project` does not move documents (task 158/171): a record's
  existing files stay in its former secure container and are read broker-only; new files go to the BU container. The cascade
  does exactly what `/unsecure-project` does. Escalation trigger 1 therefore cannot fire (no move). Open question O-1.
- **"Through the secure-child reconcile where it fits."** Not needed for work assignments / projects (above).

## Tests

- `tests/integration/data-mutation/ExternalAccess/ChildAccessCascadeTests.cs` (10, real routes / inheritance / job over the
  provisioning fixture): another secure parent keeps the child secure (AC 1); chain matter → project → work assignment in one
  call (AC 1); permission Standard → Restricted → Standard in one run each, in-step rows not written, parentless never written
  (AC 2); re-file secure M → ordinary N un-secures in one run (AC 3); parent removed keeps values and makes them editable, the
  gate refusing while filed (AC 3/4); Make Secure refused on a child, allowed on a parentless record (AC 4); fault mid-cascade
  (owner PATCH not applied) leaves it flagged secure, Secure-team-owned, shares intact AND Restricted, run fails naming it, next
  run completes (AC 6); a parent still owned by the Secure team never hands the child to it; an unreadable parent loosens
  nothing; #1478 helper + the synchronizer's barred set (one test; the other three readers call the same helper).
- `SecureRootInheritanceTests` "the way back" rewritten to round 84 (4 tests): the matter's unsecure un-secures the work
  assignment (owned by the BU team, shares revoked; decoys untouched); `alsoUnsecure` subsumed (no F3; unrelated reported);
  unsecure of a filed record 409 `access_follows_parent` before and after the parent's unsecure; unreadable parent 500.
- `RecordAccessGateTests.GetCanManageAccess_NamesTheParentsAChildFollows_AndSaysWhenThatCannotBeRead`.
- `ParentLineageTests.FormLibraryRootParentLookups_MatchTheServerFiling` (form/server parity).
- Jest: `AccessGrantModal.followsParent.test.tsx` (21), `accessRibbon.followsParent.test.ts` (22),
  `accessPermissionInherited.roots.test.ts` (23); existing suites updated (version pins, cache shape).
  `Set-InheritedAccessPermissionFormLock.ps1 -SelfTest`: 47 checks pass.
- Beyond the AC list, one line each: the gate test is the only guard on the ribbon/modal contract; the parity test is the only
  guard on the form map; the "Secure-team owner" test pins the one owner refusal that prevents a record reachable by nobody.

### Seeding proof (the one the AC asks for)

In `FollowParentsAsync`, writing ANY differing Access Permission before the un-secure (dropping the "stricter first"
condition) turned `AFaultMidCascade_LeavesTheChildSecureAndRestricted_Reports_AndTheNextRunCompletes` red (the child read
Standard while still secure); restored, green. Run 2026-10-09.

### Suite results

See the PR description (filled after the full runs).

## Known limits

- **K2** — a parent's change outside the BFF reaches the stored values within one job run (≤ 5 min); enforcement follows
  the parent meanwhile (task 174), and the #1478 readers read the chain.
- **K2** — the inline cascade after a parent's unsecure handles at most 50 filed records and chains up to `MaxFilingDepth`;
  the rest are listed as still secure and completed by the job.
- **K2** — a BFF field-mapping push that maps a parent's `sprk_accesspermission` onto a filed work assignment is refused for
  that row (`access_follows_parent`); the cascade writes the same value.
- **K2** — the work assignment / project Quick Create forms are not registered for the form lock; a value set there is
  reverted by the job.
- **K2** — the external SPA's project "secure" label reads the stored flag (`ExternalDataService.MapProject`); it catches up
  within one run.

## Deploy order (main session)

1. Merge; BFF deploy from a fresh short-path worktree of `origin/master`:
   `pwsh -File scripts/Deploy-BffApi.ps1 -Environment dev -AppServiceName spaarke-bff-dev -ResourceGroupName rg-spaarke-dev`.
2. Web resources (existing names, so no `AddSolutionComponent` is needed; read back and compare the hash):
   `pwsh scripts/Deploy-WebResourceInline.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -WebResourceName sprk_/scripts/access_ribbon.js -FilePath src/client/webresources/js/sprk_access_ribbon.js -WebResourceType 3`
   `pwsh scripts/Deploy-WebResourceInline.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -WebResourceName sprk_accesspermission_inherited -FilePath src/client/webresources/js/sprk_accesspermission_inherited.js -WebResourceType 3`
3. Form lock: `pwsh scripts/Set-InheritedAccessPermissionFormLock.ps1 -SelfTest`, then
   `-EnvironmentUrl https://spaarkedev1.crm.dynamics.com` (dry run: +handler +library on the work assignment and project main
   forms), then `-Apply`, then `-Verify`.
4. Ribbon: no XML change (the enable-rule names are unchanged); `Set-AccessRibbon.ps1 ... -SecureTransitionDeployed -Verify`
   confirms. `-Apply` only if `-Verify` fails.
5. PCF TrackingFieldTrio 1.0.44 through the `pcf-deploy` skill (solution import; never `pac pcf push`).
6. Watch the next two `secure-root-inheritance` runs: `ResultJson.followParents` (`permissionsChanged` > 0 on the first run —
   the backfill of stale children — then ~0; `undetermined` / `notCompleted` 0).

## Live gate (AC 8, main session)

Secure a throwaway matter with a filed work assignment; unsecure the matter; confirm the work assignment follows (flag false,
owner = the BU team, its container unchanged — see O-1) and that Make Secure / Remove Secure are hidden on it and Access
Permission is read-only with the matter named; clean up.

## Open questions

- **O-1 (owner):** should un-securing (a parent or a child) move existing documents back to the business-unit container?
  `/unsecure-project` does not today; the POML assumed it does. Recommendation: leave as is (reads are broker-only; moving
  widens Office reach and is a new relocation purpose), or a follow-up task that adds it to `/unsecure-project` for both.
- **O-2:** round 84's text also lists "Update Access" among the commands hidden on a child; the POML names only Make Secure /
  Remove Secure, which is what was built (Update Access only re-runs the server's own sync). Confirm.
- **O-3:** the server still accepts grants and shares on a child with a parent (`/grant`, `/share-user`); only the Manage
  Access UI is locked (the amendment). Confirm whether the server should refuse them too.

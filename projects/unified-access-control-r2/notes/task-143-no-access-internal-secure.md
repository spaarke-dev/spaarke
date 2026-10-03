# Task 143 — No Access List for INTERNAL users on secure records (#1066)

> Branch `task/uac-r2-143` (from `task/uac-r2-133-b2-r2` + `work/unified-access-control-r2`, base `7128776b7`).
> Server side complete; the form-registered steps are task 154's. Live writes: none (all pending manual gates below).

## 1. Owner answers applied (all escalations answered — none fired as a stop)

| POML trigger | Owner question | Answer (source) | Applied as |
|---|---|---|---|
| (a) | N1 — how is an internal user named | (a) systemuser column + honour linked contact (round 3: accepted as recommended) | `sprk_subjectsystemuser`; the guard also matches every contact that represents the user (141 link `sprk_primarycontact`, and contacts bound to the user's oid) and their organizations |
| (b) | N4 — organization subject binds members' internal users | (a) yes (round 3) | Guard, veto and enforcer all expand organization subjects; the enforce report carries `subjectKind` + `coveredUsers` (blast radius). The explicit "this is the customer's own firm" warning is **067's confirm dialog** (no own-firm marker exists server-side) |
| (c) | N6 — walled creator provisioning | as recommended (round 3b) | Refused BEFORE the owner move (403 `sdap.provision.creator_no_access`), walled colleagues skipped with a per-person entry in `skippedPrincipals`; a resume refuses to share to a walled creator (409 `resume_creator_no_access`) |
| (d) | N2 — team/role/BU access | hide in Teams/SPA (round 3b) — §6.5 path A | Never revoked; reported `NoAccessNotEnforceable` (team-share / team-ownership / role-or-business-unit); the veto removes secure records whole. Recorded in `design.md` §9 |
| (e) | R4 — job posture | enabled, writes on, ≤5 min (round 3 R3/R4) | `NoAccessShareReconciliationJob`, `*/5 * * * *`, enabled, removes |
| (g) | N5 — who may cause a removal | as recommended (round 3b) | A record is enforced only if the entry's `modifiedby` is an enabled person holding Write on it (RetrievePrincipalAccess for that principal, app token); else `author-lacks-write`, nothing removed |
| (h) | N3 — non-secure systemuser veto | (a) remove contact-sourced only (round 3: accepted) | Systemuser plane: secure → remove whole; non-secure → user-subject entry nothing, contact/org entry only the linked contact's contribution. A fault removes the candidate |
| S5 | last person | round 3 S5 | Enforcer keeps the last enabled person's share and reports it; the endpoint answers 409 naming the record |
| O2 | who reads/writes the table | accepted (UX note): access-administrator role | Not this task's to apply (154/064); the enforce route requires Write on the ENTRY, which under O2 is that role |

## 2. Census (step 1)

| Site | Writes | Secure-record site? | Guarded |
|---|---|---|---|
| `InternalShareEndpoints.ShareAsync` (`/share-user`) | Grant/Modify to a systemuser | yes (any root) | ✅ before any share read/write; 403 `subject_no_access`, 500 `no_access_unverifiable` |
| `ProvisionProjectEndpoint` `EnsureCreatorShareAsync` / unverified-move fallback grant | creator / resume person | yes | ✅ creator checked before the first write (forward), resume person checked before the share |
| `ProvisionProjectEndpoint.RestoreCreatorShareAsync` | back to the PRE-CALL mask | compensation only; the creator was already checked | n/a (never widens) |
| `ProvisionProjectEndpoint.ShareToColleaguesAsync` | Collaborate to named colleagues | yes | ✅ walled → skipped + reported |
| `PlaybookSharingService.GrantAccessToTeamAsync` (`:321`) | team share on `sprk_playbook` | **no** — playbooks are not secure roots | not guarded (evidence: entity set `sprk_playbooks`, principal Team) |
| `DirectThreadAccessService` (`:93`, `:195`) | Read on `sprk_communicationthread` / `sprk_communication` | **not yet** — Direct threads carry no regarding anchor; Open/record-anchored messages are children, which become secure children only with C10 part 2 (146/149, not landed) | **HOOK for 146/149**: before `GrantReadAccessToPrincipalsAsync` grants on a message whose thread is anchored to a secure root, call `SecureShareNoAccessGuard.CheckAsync(rootLogicalName, rootId, principal)` and skip refusals. Not an escalation (f): it can consult the guard internally, no API change |
| Task 142 auto-share | — | not landed | **HOOK for 142**: call `CheckAsync` before every Assigned-To POA share; a share this enforcer removed is ledgered `Skipped(no-access)`, so a lifted wall restores it; the "Update Access" command calls `NoAccessShareEnforcer.EnforceForRecordAsync` |
| Task 149 child-share fan-out | — | not landed | **HOOK for 149**: call `CheckAsync` with the ROOT before any child share; extend the enforcer to remove a walled user's child shares (today it removes root shares; a child-record object is reported `not-a-secure-root`) |
| Task 064 add endpoint | — | not landed | **HOOK for 064**: accept `sprk_subjectsystemuser`; after the add, call `EnforceEntryAsync`. ⚠️ N5 reads `modifiedby` — if 064 creates the entry APP-ONLY, `modifiedby` is the app user and nothing is enforced; 064 must create it as the caller (CallerObjectId impersonation) |

Live privilege census (read-only, 2026-10-02): Create/Write on `sprk_noaccessentry` — System Administrator, System
Customizer, Service Writer (Global). Read — those plus Spaarke Core User (Global), Service Reader, Support User (Basic).
0 entries exist in dev.

## 3. What was built (placement + §11 justification)

All in the BFF (CLAUDE.md §10): BFF domain code over BFF-owned tables, BFF identity, low volume; no AI dependency
(ADR-013), no new package, no new `.WithClientSecret`, no plugin (ADR-002), `GrantMembershipAsync` untouched.

| New component | Existing (grep) | Why not extend | Cost of doing nothing |
|---|---|---|---|
| Column `sprk_subjectsystemuser` | only `sprk_subjectcontact` / `sprk_subjectorganization` (`entity-schema.md`); `_sprk_subject` hits only the reader | linked-contact-only fails OPEN for unlinked users (1 of 8 linked) | an internal user cannot be walled directly |
| `SecureShareNoAccessGuard` (scoped) | `IsGranteeDeniedOnRecordAsync` (contacts/orgs only), `ResolveDenyVetoAsync` (read-time, private) | used by 4+ share sites and needs a status-bearing link read that the evaluator's contact path lacks | `/share-user` and provisioning hand a walled user a share |
| `NoAccessEnforcementStore` (typed HttpClient) | `DataverseWebApiClient` cannot call `RetrievePrincipalAccess`; `CallerRecordAccessProbe` is OBO-only by design | N5 and criterion 7 need RPA for a principal other than the caller | no author check, no residual report |
| `NoAccessShareEnforcer` (scoped) | `InternalShareEndpoints.UnshareAsync` removes one share per request | needs entry→records×users, N2/N5/S5, a report, job reuse | an entry added after a share exists changes nothing |
| `POST /api/v1/external-access/no-access/enforce` + `DelegationRuleFilter` case | 064 (pending) cannot see MDA-authored entries | joins the existing group | save-time enforcement (R3 "minutes") impossible |
| `NoAccessShareReconciliationJob` (`AddScheduledJob`, ADR-036/052) | `ExternalAccessReconciliationJob` (row lifecycle, ships disabled) | different reason to change; must ship enabled | OOB MDA shares after an entry stay forever |
| `ExternalParticipationService.FindSecureRootsReferencingOrganizationAsync` / `FindWallMemberContactsAsync` + `BuildOrganizationMembershipFilterForOrganization` | extension of the class that owns the org-lookup registry and junction filter | — (extension) | — |
| `ImpersonatedRootSetSource.DeploymentCacheTenant` / `InvalidateForTenantAsync` | extension | — | the job would invalidate the "anonymous" key nobody reads |
| `sprk_noaccessentry_postsave.js` | `sprk_kpiassessment_quickcreate.js` is KPI-specific | — | no save-time call |

Reader: three-subject `NoAccessSubjects` overload (the contact overload delegates), exactly-one-of-three malformed rule,
`DenyingSubjectKinds` provenance. Reason codes: `sdap.access.user_share.subject_no_access`,
`…user_share.no_access_unverifiable`, `sdap.provision.creator_no_access(_unverifiable)`,
`sdap.provision.resume_creator_no_access`, `sdap.provision.principal_no_access(_unverifiable)`,
`sdap.access.no_access.{entry_required|entry_not_found|entry_inactive|entry_malformed|entry_unreadable|last_person_on_secure_record|enforcement_incomplete}`.

Not built (stated): by-USER enforcement (the job covers links that appear); the own-firm warning (067); the form
registration, ui-tests and live gate 14 (154).

## 4. Tests

New: `SecureShareNoAccessGuardTests` (16), `NoAccessShareEnforcerTests` (19), `NoAccessEnforceEndpointTests` (8, real
filter pipeline + real DI-composed enforcer), `NoAccessShareReconciliationJobTests` (8), `ProvisionNoAccessTests` (5),
reader tests (+9), evaluator tests (4: the N3 split, the user subject, the fault — one of them a rewrite, see below),
`/share-user` tests (+10).
Beyond the contract (one line each): `TheJobShipsEnabled_EveryFiveMinutes` pins the owner's R4 posture (the
identity-link job precedent; not a DI-registration test); `EnforceForRecord_*` pin the 142 hook.

Rewritten, not deleted: `ComposeAsync_SystemUserRecordThatWouldSurviveRestricted_IsStillRemovedWhenAlsoDenyListed`
asserted the pre-N3 behaviour (any record removed whole). It is now the SECURE half
(`…OnASecureRecord_ALinkedContactEntryRemovesTheWholeRecord_MembershipIncluded`) plus the non-secure half.

Fixture changes: the seam/grant-policy deny-reader doubles now populate the SUBJECT column a real row carries (the new
malformed rule would otherwise reject their subject-less rows); the provisioning fixture registers the production guard
over its own deny list; `GrantPolicyContractTests` seeds the share target's link row.

Bite proof: ten violations seeded at once (share guard, malformed rule, secure split, S5, N5, creator guard,
colleague skip, filter case, job cache key → "anonymous", resume guard) — 27 tests failed, each seed reddening its
own tests; restored and touched.

## 5. Pending manual gates (main session; live writes)

| Gate | Command | Order |
|---|---|---|
| G-1 schema | `pwsh scripts/Set-NoAccessSystemUserSubjectSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply` then `… -Verify` (exit 0) and `mcp__dataverse__describe('tables/sprk_noaccessentry')` | **BEFORE any BFF deploy carrying this branch** — the reader selects `_sprk_subjectsystemuser_value`; without the column every No Access read fails closed (deny-all, both planes). Dry run done read-only 2026-10-02 |
| G-2 deploy | BFF deploy (bff-deploy skill) | after G-1 |
| G-3 form steps | task 154: field on the entry form; register `sprk_/scripts/bff_auth.js` then `sprk_/scripts/noaccessentry_postsave.js` (`Spaarke.NoAccessEntry.onLoad`) | after G-2 |
| G-4 live gate 14 (i)–(v) | spaarkedev1, existing non-admin users in their BUs, an author who can create entries (O2 role) AND holds Write on the project | after G-3; ⚠️ O2 is answered (accepted) but not yet APPLIED (Core User still holds Global Read — 154). The POML's O2 trigger asks to stop before sign-off while every core user reads every Reason |

## 6. Residuals (stated, not hidden)

- **Performance**: the residual check is one RetrievePrincipalAccess per (covered user × covered secure record) — bounded
  by 200 × 500 per entry, every 5 minutes. Fine for dev (0 entries); a large organization subject should be watched.
- The job scans the newest 500 active entries; beyond that it reports TRUNCATED (Success=false) every run.
- An entry whose org subject has >200 members reports Truncated every run (not clean) until the ceiling is raised.
- The read-time veto takes the linked contact from `IIdentityNormalizationService` (a faulted link read is "no contact"
  there — task 141's contract), while the guard reads the link status-first; the systemuser subject is unaffected.

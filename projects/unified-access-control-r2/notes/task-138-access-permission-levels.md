# Task 138 (#1061) — the Access Permission levels made real

> **Date**: 2026-10-02 · **Branch**: `task/uac-r2-138` (from `integ/uac-r2-batch2` @ `2a9b4b26c`)
> **Status**: code complete. Live steps (BFF + PCF deploy, the retirement script's `-Apply`, the manual gate
> of criterion 16) are PENDING MANUAL GATES — this run was read-only on Dataverse/Azure. Publish size is
> measured by the main session.

## 1. Preconditions (step 0)

| Check | Result |
|---|---|
| Task 135 merged; CIAM composes through the shared contact plane + veto pipeline | ✅ `ComposeForCiamContactAsync` → `ComposeContactPlaneAsync` (one predicate site) — escalation 1 does NOT fire |
| Task 131 (C3, cache round-trips `DirectAccessLevel`) | ✅ merged (`CacheVersion = 5`) — escalation 2 does NOT fire |
| Owner rules | round 2 item 3 + Q6; O1 FINAL (pill "Secure" red for secure and secure+Restricted; modal bar "Secure – Restricted") |

## 2. What changed

### Read time — one predicate, widened (criteria 1–5)
- `RootRecordFlags` gained `IsLimited` and an explicit `IsUnreadable` marker (both optional, so every pre-138
  construction means what it meant). `IsDirectOnly => IsSecure || IsLimited` is the ONE pre-max predicate.
- `ExternalParticipationService.FlagsFrom(isSecure, accessPermission)` maps a read row (null permission =
  Standard, null secure = not secure — unchanged); `Unreadable` = secure + limited + restricted + marker.
- `AccessibleRecordSetService.DirectOnlyPredicate(flags)` replaces the two local `IsSecure` lambdas; every
  term that consulted Secure consults it (grant term → direct level only; standing term; org expansion).
  The systemuser membership term never consults it. Restricted stays the post-max veto and wins.
- CIAM inherits Limited with **no CIAM-specific code** (task 135's shared composition).

### Write time — one decision, shared by every writer (criteria 5–10)
- `ExternalGrantLifecycle.DecideGrantPolicy(flags, rootId, granteeKind)` — pure; order: unreadable/absent
  → 503 `sdap.access.grant.policy_unreadable`; Restricted → 422 `sdap.access.grant.record_restricted` (both
  grantee kinds); organization grant on Secure/Limited → 422 `sdap.access.grant.org_grant_direct_only_record`.
  **Absent key = unreadable** (never the read path's "no veto").
- `EvaluateGrantPolicyAsync` reads through the existing `GetRootRecordFlagsAsync` by LOGICAL name; never throws.
- `GrantExternalAccessEndpoint.CreateGrantAsync` (the shared core) runs it FIRST and returns
  `GrantUpsertOutcome.Refused(...)` — a typed value, never an exception — so /grant answers 422/503, not the
  catch-all 500, and any future writer (140, 142) cannot bypass it.
- `/invite-and-grant` and `/invite` check BEFORE any Contact lookup/create, CIAM account or email.
  `/invite-and-grant` deliberately reads twice (pre-check + the core's own); the second read is the core's
  guarantee and catches a record that became Restricted during provisioning (then 422 + `contactId`).
- The check runs inside the handlers, after `DelegationRuleFilter` — a non-Write caller gets the 403 and the
  policy is never read (pinned).
- `RecordAccessGateEndpoint` remarks record why the gate ignores the flags (delegation vs grant types).

### Client (criteria 11–15)
- `AccessGrantModal`: Restricted hides "+ Contact", "+ Organization" and candidate contact rows; "+ User",
  user rows, their dropdowns and Add stay enabled; revoke unchanged. Limited/Secure hides "+ Organization".
  One bar per state via the pure `describeAccessPermission` (Restricted Access / Secure – Restricted /
  Secure / Limited Access). A server policy refusal shows the server's `detail`. Dead `onSetStandingGrant`
  prop removed; module doc corrected. New prop `isSecureRecord` (semantic boolean — banner copy only).
- Pure `resolveAccessPermissionState(value, isSecure, {limited, restricted})` (shared lib; the HOST supplies
  its integers, so the lib stays entity-agnostic): Restricted → restricted; Limited → limited; Standard/null
  with secure true OR unreadable (null/undefined) → limited; Standard/null with secure false → standard.
- `TrackingFieldTrio` (shared): `disabled` (all three controls), `accessPermissionDisabled` (pill only; menu
  pinned shut), `showAccessPermission` (unbound → hidden, grid kept aligned), `secureAccessPermission`
  (closed pill "Secure" in red for every value — a closed-LABEL change only; the menu is the unchanged
  Standard / Limited / Restricted list and each item writes its own value — r1, see §10 item 5).
- PCF v1.0.32 (manifest, solution.xml, extracted manifest, pack.ps1, footer + rebuilt bundle):
  `context.mode.isControlDisabled` + bound column `security.editable` read on every render; a fail-closed
  `sprk_issecure` GATING read (`ensureSecureFlag`, once per record id); display read unchanged (fail-soft);
  `accessPermission` property `required="false"`; unbound → no pill and never written back;
  `onSetStandingGrant` handler removed; root-value comments corrected.
- Communication components: `EMAIL_TRACKING_FIELDS.accessPermission`, `DEFAULT_ACCESS_PERMISSION_OPTIONS`,
  `updateAccessPermission`, the record-state field, `EmailWorkspaceProps.accessPermissionOptions` and the
  `EmailTrackingPanel` access props are removed; the panel renders the trio with `showAccessPermission={false}`.

### Decisions worth recording
1. ~~"Secure" in the pill menu writes Limited~~ — **REVERTED in r1** (verifier finding 5). O1 FINAL specifies
   only the closed label ("Secure" in red for secure and secure+Restricted) and the Manage Access bar; the
   "only those two choices" menu came from the SUPERSEDED O1 block. The menu is now the record's own
   Standard / Limited / Restricted list under the "Secure" closed label, so Standard stays selectable, clicking
   an item never stores a value other than its own, and no Limited is silently left behind for a later unsecure
   (task 150).
2. **NULL `sprk_issecure` → Limited in the dialog** (fail closed, criterion 12 "unreadable"). Field-level
   security also returns null, so the two cannot be told apart. The server treats NULL as not secure. Until
   task 153's Q1 cleanup (NULL → No) runs, Standard records with NULL secure (dev: 9 projects, 18 matters, 11
   WAs) do not offer "+ Organization" in the dialog. Fail-closed, intended.
3. **Criterion 12's "pure-function test"** lives in `Spaarke.UI.Components` jest (the PCF has no jest harness);
   the host passes its integers in, so only `index.ts` knows them.

## 3. Communications — live scan (READ-ONLY, spaarkedev1, 2026-10-01/02)

- `sprk_communication` forms: 4 scanned (Information ×3 types, Message main form) — **none** reference the
  column or the TrackingFieldTrio. Views: 11 system + 4 personal — none. Workflows/business rules: 0. Plugin
  steps on the table: system steps only, no filtering attributes.
- Root forms (project/matter/work assignment main forms) bind the trio with
  `<accessPermission type="OptionSet">sprk_accesspermission</accessPermission>` — unchanged.
- `RetrieveDependenciesForDelete`: ONE dependency — `aiskillconfig`
  `ais_FormFillFieldOptOut_sprk_communication_sprk_accesspermission` (unmanaged, component type 10314). The
  script deletes it first. Escalation 4 does NOT fire (nothing managed, no workflow/plugin dependency).
- Data: 99 rows hold Standard (100000000); nothing else. Escalation 5 does NOT fire (no communication surface
  displays the parent permission; the control is simply removed).

### Retirement script dry run (no `-Apply`)
```
Retire sprk_communication.sprk_accesspermission  —  DRY RUN (read-only; pass -Apply to perform the writes)
== 1. Column            Found: MetadataId bceb6389-fb8a-f111-8077-7ced8ddc4a05, type Picklist, managed=False
== 2. Workflows         None reference the column (0 scanned).
== 3. Forms             No form references (4 forms scanned).
== 4. System views      No system-view references (11 views scanned).
== 5. Personal views    No personal-view references visible to this principal (4 scanned).
== 6. aiskillconfig     PLAN delete 'ais_FormFillFieldOptOut_sprk_communication_sprk_accesspermission' — b0b76690-fb8a-f111-8077-7ced8ddc4cc6
== 7. Dependencies      component type 10314, object b0b76690-… — removed by this script
== 8. Delete            PLAN delete column sprk_communication.sprk_accesspermission, then publish sprk_communication
DRY RUN complete — no writes were made.   EXIT 0
```
The form/view transforms were verified offline against synthetic XML (a cell bound to the column, a trio
`accessPermission` parameter, a layout cell, a fetch attribute and order) — all removed, nothing else touched.

## 4. Tests (ADR-038: no `Mock<HttpMessageHandler>`, no DI-registration or ctor null-check tests)

| Criterion | Where |
|---|---|
| 1 Limited × grant/standing/org-expansion, workforce contact; systemuser contact-grants term, membership unaffected | `UnifiedEvaluatorSeamTests.Limited_*` (×3 roots + systemuser); `AccessibleRecordSetServiceTests` Limited cases |
| 2 CIAM: direct / org / both on a Limited root | `CallerPrincipalResolverTests.CiamStrategy_OnALimitedRoot_*` (real evaluator); seam `Ciam_Limited_*` ×3 |
| 3 Secure unchanged; Secure+Limited ≡ Secure | all pre-existing FR-22 tests green; `ComposeAsync_RecordBothSecureAndLimited_BehavesExactlyAsSecure` |
| 4 Restricted wins on every plane, membership survives | seam `RestrictedWins_*` ×4; unit `ComposeAsync_RecordRestrictedAndLimited_*` |
| 5 fail closed; null = Standard; absent key → 503; logical-name table | `Unreadable_IsTheMostRestrictiveCombination…`, `ComposeAsync_WhenFlagReadFaults_TheStandingTermIsSuppressedToo` (ThrowingFlag); `FlagsFrom_MapsTheRootColumns`; `DecideGrantPolicy_RootIdAbsent…`; `EveryGrantRootType_*`; contract `…CannotBeRead_Returns503…(absent/unreadable)` |
| 6 Restricted write refusals (/grant contact+org, /invite-and-grant, /invite, no side effect) | `GrantPolicyContractTests` |
| 7 org grant on Secure/Limited refused; named contact twin succeeds | contract + `GrantLifecycleCharacterizationTests.CreateGrantAsync_OnADirectOnlyRoot_*` |
| 8 shared core: typed refusal, no throw, no row; route → 422 not 500 | `GrantLifecycleCharacterizationTests.CreateGrantAsync_OnARestrictedRoot_*` |
| 9 flag fault → 503, twin real Secure+Restricted → 422 | core + contract twins |
| 10 non-Write → 403 (policy never read); `/share-user` on Restricted/Limited/Secure succeeds | `GrantPolicyOrderingTests` (+ positive twins); contract `PostShareUser_ByAWriteHolder_*` ×3 |
| 11 dialog state matrix, banners, 422 detail, doc comment | `AccessGrantModal.gating.test.tsx` |
| 12 PCF mapping (pure) | `resolveAccessPermissionState` table in the gating test file |
| 13 read-only pill/switches, enabled unchanged | `TrackingFieldTrio.test.tsx` "Read-only honoured" |
| 14/15 communications code + unbound pill | `EmailWorkspace.mapping.test.ts` negative; `EmailAssociationsAndTracking.test.tsx`; `TrackingFieldTrio.test.tsx` "Unbound" |

Beyond the closed set (one line each): the secure-pill tests pin owner O1 FINAL (named in the task context);
`CreateGrantAsync_OnARestrictedRoot_LeavesAnExistingGrantRowUntouched` pins that a refused RE-grant cannot
move a level either.

### Suite results (2026-10-02)
- **BFF unit suite** (`dotnet test tests/unit/Sprk.Bff.Api.Tests`, includes the seam/contract/auth KEEP paths):
  **13,639 passed, 0 failed, 54 skipped** (13,693 total).
- **NetArchTest** (`tests/Spaarke.ArchTests`): **341 passed, 0 failed**.
- **jest — Spaarke.UI.Components**, AccessGrantModal + TrackingFieldTrio: **108 / 108 passed**. Full package run:
  3,349 passed, 14 failed in 9 suites — all outside this task's files (ConversationView, TimelineComposeBox,
  EmailComposer template picker, RichFilePreview, RecordHeader/WorkspaceShell static scans, surfaceLaunchRegistry,
  todoScoring sha256 — the hash/static-scan ones are line-ending artefacts of this fresh worktree checkout).
- **jest — Spaarke.Communication.Components**: **280 passed, 2 failed** — `triageColumnRenderers` (fetch order) and
  `ReconciliationWorkspace` (PanelSplitter separator); neither imports a file this task changed. (The package's
  `testMatch` cannot resolve under a `.claude` worktree path; run with
  `--testMatch "**/Spaarke.Communication.Components/src/**/*.test.{ts,tsx}"`.)
- **Consumers**: `Spaarke.Communication.Components` `tsc --noEmit` clean; `src/solutions/EmailPage` `npm run build`
  succeeded; `Spaarke.AI.Widgets` `tsc --noEmit` clean (after building its sibling `ai-context` / `ai-outputs`).
- **PCF** `npm run build:prod` succeeded. **CVE**: `dotnet list package --vulnerable --include-transitive` — no
  vulnerable packages.

### Perturbation (each guard seeded, watched red, restored + touched)
- `IsDirectOnly => IsSecure` (Limited predicate disabled): **16 red** (seam Limited ×7 incl. parity
  "limited", CIAM unit, 3 evaluator unit, policy matrix, FlagsFrom, core + contract org-on-Limited).
- `DecideGrantPolicy` returns Allowed: **32 red** (matrix, absent key, unreadable, core, contract, ordering 422 twins).
- Core check removed from `CreateGrantAsync` only: **17 red** (all /grant + core tests; the invite routes'
  own pre-checks keep theirs green, as designed).
- Modal gating (`contactGrantsOffered = true`, org offered on Limited): **3 red** in the gating suite.

## 5. ADR-003 amendment (path B) — concise text for the MAIN SESSION (`.claude/` is outside the sub-agent boundary)

Replace in `.claude/adr/ADR-003-authorization-seams.md`:

```
- **MUST** let **Secure** (`sprk_issecure`) suppress derived-member + org-expansion **BEFORE** the max,
  for **every** principal kind
```
with
```
- **MUST** let a **direct-only** record — **Secure** (`sprk_issecure`) **OR Limited**
  (`sprk_accesspermission` = Limited) — suppress derived-member + org-expansion + org-inherited grants
  **BEFORE** the max, for **every** principal kind, through ONE predicate (`RootRecordFlags.IsDirectOnly`);
  internal ADR-034 membership is never suppressed, and Restricted (a post-max veto) wins (task 138, 2026-10-02)
```
and
```
- **MUST NOT** model `"No Access"` as a level, or apply Secure suppression after the max
```
with
```
- **MUST NOT** model `"No Access"` as a level, or apply Secure/Limited suppression after the max, or add a
  second suppression predicate
```
The full ADR (`docs/adr/ADR-003-lean-authorization-seams.md`) is amended in this branch. **Owner approval of
the path-B amendment must be recorded in the PR before merge** (criterion 17).

## 6. Placement + §11 justification (CLAUDE.md §10/§11)

- **Placement: in the BFF** — the evaluator and the grant core live there; `.claude/constraints/bff-extensions.md`
  decision criteria: authorization of BFF-owned write routes, no new service, no new DI registration, no new
  endpoint, no package. `ExternalParticipationService` was already injectable (typed client).
- **New surface:** methods only (`DecideGrantPolicy`, `EvaluateGrantPolicyAsync` on the existing
  `ExternalGrantLifecycle`; `FlagsFrom`, `IsFlagBearingRootType` on the existing participation service); two small
  types (`GrantGranteeKind`, `GrantPolicyDecision`). Existing: no write-time policy existed (grant validated
  shape only); Extension: the existing lifecycle home + existing flag reader; Cost of doing nothing: a Write-holder
  mints contact/org grants on Restricted records and org grants on Secure ones, and `/invite-and-grant` provisions
  a CIAM account for someone who can never get access.
- **New script** `scripts/Retire-CommunicationAccessPermission.ps1`: Existing — no column-delete/form-strip script
  (nearest pattern `Deploy-AccessEventEntity.ps1`); Extension — not possible, different operation; Cost — the dead
  column stays and keeps a Copilot dependency.
- **Shared lib:** `accessPermissionState.ts` (pure function) — Existing: the PCF's private switch, untestable
  (no PCF jest); Extension: moved the rules into a testable pure function, the host keeps the integers.
- Publish size: not measured here (main session). CVE scan: no package changes.

## 7. Pending manual gates (exact steps for the main session / operator)

1. **Deploy the BFF** to dev (`bff-deploy` skill / `scripts/Deploy-BffApi.ps1`).
2. **Import the PCF** v1.0.32: `src/client/pcf/TrackingFieldTrio/Solution/pack.ps1` → import
   `TrackingFieldTrioSolution_v1.0.32.zip` (`pcf-deploy` skill); verify the footer reads v1.0.32.
3. **Retire the column** (after 1–2): `scripts/Retire-CommunicationAccessPermission.ps1` (dry run), then `-Apply`;
   re-run → "nothing to do".
4. **Live gate (criterion 16)** on spaarkedev1 with existing non-admin users (owner round 4: ask the owner for a
   user at a specific level rather than reusing the root-BU test users):
   (a) Restricted project: "+ User" shares a colleague; contact/org options absent; a forced
   `POST /api/v1/external-access/grant` returns 422 `record_restricted`; a contact with a prior grant loses access on
   Teams and the external SPA; an internal user with role access keeps it.
   (b) Limited project: an org-inherited-only contact loses access; a directly granted contact keeps its level;
   "+ Organization" absent. Candidate fixture (task 135 heads-up): matter `2444af6d-e1f2-f011-8406-7ced8d1dc988`
   is Limited and carries contact grants (52bb55e7 Collaborate, 8e9918a9 View Only, 394fda9f via a contact+org row)
   — the first CIAM narrowing will show there.
   (c) Secure non-restricted project (65a3fab2, provisioned per round 4): behaves as (b).
   (d) Standard project unchanged. **Plus (r1, verifier finding 7): on the project, matter AND work-assignment
   main forms under v1.0.32, the access-permission pill still RENDERS (footer reads v1.0.32) and still WRITES —
   change Standard → Limited → Standard, save, and confirm `sprk_accesspermission` round-trips on each form
   (`isAccessPermissionBound()` decides both visibility and `getOutputs` write-back). On a secure record the
   closed pill reads "Secure" in red and the menu lists Standard / Limited / Restricted.**
   (e) The communication form shows no access-permission control after step 3.

## 8. Step 9.5 quality gates

**code-review** (coverage-first; no Critical):
| # | Sev | Conf | Finding | Disposition |
|---|---|---|---|---|
| 1 | Warning | med | `TrackingFieldTrio` pins the Menu shut with `open={false}` only while disabled, so the Menu flips controlled ↔ uncontrolled when a form becomes editable again. Fluent's `useControllableState` tolerates it; worst case is a dev-console warning. | Accepted — the alternative (always-controlled open state) adds state for no behaviour gain; the disabled trigger is the primary guard, `open={false}` + the click guard are defence-in-depth. |
| 2 | Suggestion | high | `AccessGrantModal.tsx` grew 1,498 → 1,625 lines. Still one responsibility (the dialog); the new copy/mapping logic is extracted as pure top-level functions (`describeAccessPermission`, `resolveAccessPermissionState` in its own file). | Accepted (cohesive); decomposition seed for task 066/067, which also edit this file. |
| 3 | Suggestion | high | `/invite-and-grant` reads the flags twice (pre-check + core). | By design — the core's check is the guarantee for every writer; the pre-check prevents onboarding. One extra GET on a low-volume admin route. |
| 4 | Suggestion | med | `GrantPolicyDecision` (Infrastructure) carries an HTTP status code. | Accepted — BFF-internal, and it keeps the ONE decision and its status from drifting apart; the ProblemDetails shaping stays in the Api layer (`PolicyRefusalProblem`). |
| 5 | Info | high | `/invite` now answers 400 for a body with no resolvable root — unreachable through the route (the delegation filter 403s first). | Fail-closed; noted. |
| 6 | Info | high | Read path keeps "absent key = no suppression" (safe: the reader returns every asked id for a flag-bearing type); the write path treats absent as unreadable. | Documented on both `DirectOnlyPredicate` and `DecideGrantPolicy`. |
Security: the policy (and its 422 detail) is reachable only after `DelegationRuleFilter` established Write (pinned by `GrantPolicyOrderingTests`); no secrets/PII added to logs (record ids + reason codes only). AI-smell scan: no single-impl interfaces, no log-rethrow, no null checks on non-nullables, no god methods added (`CreateGrantAsync` gained one guard clause).

**adr-check:** ADR-001 (Minimal API, no new endpoints) ✓ · ADR-002 (no plugins; invariant owned server-side, client is preview only — WP-1/WP-2; fail closed — WP-6) ✓ · ADR-003 item 8 → **path B amendment** documented in the full ADR, concise text handed off (§5); **owner approval required in the PR** (Warning until recorded) · ADR-008 (authorization stays in the group filter; the policy is a business rule inside the handler, after the filter) ✓ · ADR-010 (no new DI registrations, no interfaces) ✓ · ADR-012 (shared lib entity-agnostic: integers and `sprk_issecure` live only in the PCF `index.ts`; modal receives semantic state + a semantic `isSecureRecord`) ✓ · ADR-019 (ProblemDetails + reasonCode + traceId) ✓ · ADR-021 (tokens only; `colorPaletteRed*` tokens for the secure pill) ✓ · ADR-022 (PCF stays React 16 `ReactDOM.render`) ✓ · ADR-028 (`authenticatedFetch` only) ✓ · ADR-038 (KEEP paths: unit, seam, contract, auth; no banned patterns; every new guard perturbation-checked) ✓ · ADR-052 (no background work) ✓.

**Lint/build:** `dotnet build` 0 warnings / 0 errors; `dotnet format whitespace` clean on changed files (the contract-test block's pre-existing 8-space indent fixed); PCF `npm run lint` clean; ui-components eslint: 1 pre-existing warning (`defaultAccessLevel` unused); PCF `build:prod` succeeded (966 KiB bundle, v1.0.32 strings verified present, `onSetStandingGrant` absent).

## 9. Hand-offs (main session)

- **066 + 064**: amend to include **Limited** alongside Secure/Restricted (suppressed rendering of org/standing
  rows). The signal they consume: `RootRecordFlags.IsDirectOnly` / modal `accessPermissionState === 'limited'`.
- **056**: cross-link the inherit-from-parent rule now in `docs/data-model/sprk_communication.md`.
- **142** (Assigned-To auto-grants through the core): a `record_restricted` refusal is a typed
  `GrantUpsertOutcome.Refusal` — treat as "skip and log", not an error. Escalation 3 is SURFACED, not fired:
  the core ADMITS named contact auto-grants on Limited and Secure (the task's recommendation — they are named,
  direct rows an operator can remove); if the owner instead wants auto-grants suppressed on Limited, that is a
  product decision for 142.
- **139 / 140**: reuse the reason codes and `PolicyRefusalProblem`; `CreateGrantAsync` now takes
  `ExternalParticipationService` after the Dataverse client.
- **153**: the modal's single bar already renders "Secure" / "Secure – Restricted" (O1 FINAL item 3) and the pill
  shows "Secure" in red (item 2) — 153 should not add a second bar.
- **Concise ADR-003** edit (§5 above).

## 10. Verifier round 1 (2026-10-02, branch `task/uac-r2-138-r1`)

| # | Finding | Disposition |
|---|---|---|
| 1–4 | Read path, write path, re-run, perturbation | Verified — no change. |
| 5 | Two-item secure menu ("Secure" → Limited) went beyond O1 FINAL | **Fixed (revert, verifier option a).** `secureAccessPermission` is now `{ label }` only; the shared trio always lists the host's `accessPermissionOptions`; PCF `SECURE_ACCESS_PERMISSION_OPTIONS` deleted; bundle rebuilt (still v1.0.32 — never deployed). New jest: the secure menu is exactly Standard/Limited/Restricted, and each of the three writes exactly its own value. Perturbation: dropping Standard on secure → 2 red; making a secure item write Limited → 2 red; restored → 20/20. |
| 6 | NULL `sprk_issecure` → Limited in the dialog vs server "not secure" | **Kept (fail closed), flagged to the owner** alongside task 153's Q1 cleanup: the same mapping applies in PRODUCTION to any legacy NULL rows, so Q1's cleanup must also run (or the column default + cleanup ship) in every environment, not only dev. No code change. |
| 7 | `isAccessPermissionBound()` relies on `attributes.LogicalName` only | **Hardened + gated.** A numeric `raw` value is now also proof of a binding (an unbound optional property never holds one), so a host that omits metadata still shows and writes the pill whenever the record has a value. Live gate step 16(d) now explicitly checks render + write on the project, matter and work-assignment forms under v1.0.32 (§7 above; POML criterion 16(d)). The PCF still has no jest harness, so the live gate is the check. |
| 8 | Personal views: filtering view only logged; non-filtering left without `-IncludeUserViews` | **Fixed.** Personal views now follow the system-view rule: a FILTERING personal view refuses the run (exit 2) with or without the switch; a display/sort reference refuses the run (exit 2) unless `-IncludeUserViews` strips it. The refusal is raised outside the scan's try/catch so it cannot be swallowed. Proven offline with a harness that fakes `Invoke-Dv` (any non-GET throws): none → 0; filtering → 2 (both modes); displaying → 2 without the switch, 0 + PLAN with it. Seeded the pre-fix script into the same harness: filtering → 0, displaying → 0 (the old behaviour, so the scenarios bite). The live scan found no personal-view references, so the real run is unaffected. |
| 9 | Stale `GrantPolicyWriteTimeTests` doc reference | **Fixed** — now names `PolymorphicGrantWriteTests` (the `DecideGrantPolicy_*` matrix), `GrantPolicyContractTests` and `GrantPolicyOrderingTests`. |
| 10 | Merge sequencing with `task/uac-r2-141-f3` | **Recorded for the main session:** conflicts in `InviteAndGrantExternalUserEndpoint.cs`, `InviteExternalUserEndpoint.cs` and `ExternalAccessContractTests.cs`. Whichever lands second keeps BOTH the 138 policy pre-checks (before any Contact lookup / CIAM call / email) AND the 141 changes; re-run `GrantPolicyContractTests` + `ExternalAccessContractTests` after the resolution. |
| 11–12 | Info / correctly stopped escalations | No change. |
| 13 | Criterion 16 pending | Still a manual gate (not attempted; live writes are out of scope). Step (d) now carries the root-form pill check. |
| 14 | Criterion 17: owner approval of the ADR-003 path-B amendment + concise `.claude/adr` edit | **Not closable here** — the owner's approval is recorded in the PR (no PR from this branch), and `.claude/` is outside the sub-agent boundary (concise text in §5). |
| 15 | Criterion 19: publish size | **Not closable here** — main session measures (fresh-master short-path Compress-Archive). r1 changes no BFF production code (one test doc comment). |
| 16 | Criterion 11/13 qualified by finding 5 | Closed by the finding-5 revert: the pill now does exactly what O1 FINAL specifies and nothing more. |

**r1 test results (2026-10-02):** BFF unit suite 13,639 passed / 0 failed / 54 skipped (13,693); NetArchTest 341 / 341; jest `Spaarke.UI.Components` TrackingFieldTrio + AccessGrantModal 111 / 111 (was 108: one secure-menu test replaced by one menu test + three write cases); PCF `npm run lint` clean and `npm run build:prod` succeeded (bundle copied into `Solution/Controls/...`, v1.0.32 footer present, no two-item secure menu in the bundle). Publish size: main session.

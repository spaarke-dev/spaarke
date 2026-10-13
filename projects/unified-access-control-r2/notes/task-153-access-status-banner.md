# Task 153: access-status banner and TrackingFieldTrio indicator

> Executor notes, 2026-10-08. Branch `task/uac-r2-153` (worktree `C:\wt153`, from `origin/master` fc639d2c1e, which
> carries 064 / PR #1411 and 067 / PR #1434). Rigor FULL. Nothing was deployed or imported: the main session runs the
> deploy and the live gates.

## Scope as built, and the owner answers it follows

| Source | Decision | Applied as |
|---|---|---|
| Round 59 item 3 | 153 covers project, matter and work assignment only. Organization/Contact banners cut (walls there are visible in 154's subgrids) | Three root forms. No org/contact route, no §6.5 path-A organization exception, no escalation (f) |
| Round 83 item 11 (supersedes O1-final's "no banner in the PCF") | O1 = BOTH: a non-clickable red form banner, plus a clickable TrackingFieldTrio indicator that opens Manage Access. No app-wide notification | `sprk_accessstatus_banner.js` (setFormNotification only) + the indicator in TrackingFieldTrio 1.0.41 |
| O2 (research note, 2026-10-01) | **Confirmed still ACCEPTED** at step 0 (`session27-ux-research-ethical-wall-secure.md`, "Owner answers, 2026-10-01") | The banner reads only `secure`/`noAccess`; entries stay behind Manage Access (Write) |
| Q1 / escalation (g) | Backfill NULL `sprk_issecure` before the banner publishes | **Done in dev** by task 150's G-0 (2026-10-03). Re-counted live today: 0 / 0 / 0 NULL rows on project / matter / work assignment |
| Rounds 82 + 84 | A filed child's Secure should be the parent-derived value | NOT implemented here (task 174). Known limit below; one read point per client |

## Step 1: what 064 landed, and why there is no server change

064 shipped ONE Read-gated route per root type, `GET /api/v1/records/{sprk_project|sprk_matter|sprk_workassignment}/{id}/no-access`
(`RecordNoAccessEndpoint.cs`, contract `notes/phase4-access-report-contract.md`). It already carries everything this
task's server step was to add:

- the closed signal block for the root types: `secure` and `noAccess`, each `applies` / `doesNotApply` / `unknown`;
- the Secure tri-state from the shared flag read (`RootRecordFlags.IsUnreadable` → `unknown`; never "not secure");
- the record-id echo (`recordId`);
- Read gate through `RecordRouteAccessAuthorizationFilter` (the existing `read` key), caller over OBO, with the uniform
  404 for no Read / no record / probe fault / no bearer (the contract's choice; this task's clients treat ANY non-200 as
  unavailable, so a 404 and the POML's 403 render identically);
- no entry ids, names or reasons for a Read-only caller; no Reason for anyone.

The POML's organization/contact SUMMARY route is not needed: round 59 cut those banners. So **no BFF change**: no
endpoint, filter, operation key, DTO field or reader method was added, and no publish-size delta applies. Criteria 1-6
are 064's (its live gates G1-G8 PASS, `notes/batch5-live-gates-2026-10-08.md`). Grep for `sprk_noaccessentries` in
`src/server` is unchanged by this PR (no server file touched).

Live, read-only (spaarkedev1, 2026-10-08):

| Check | Result |
|---|---|
| TrackingFieldTrio on the main form (escalation e) | Project main form `5aa00242-…`, Matter main form `4fa382f2-…`, Work Assignment main form `7e578eef-…`: all carry it. Escalation (e) does not fire |
| Libraries already on those forms | all three: `sprk_/scripts/bff_auth.js`, `sprk_/scripts/assignedaccess_postsave.js` (matter also KPI/insight libraries) |
| NULL `sprk_issecure` (escalation g) | 0 / 0 / 0 |

## What was built

1. **Form banner** `src/solutions/webresources/sprk_accessstatus_banner.js` (web resource `sprk_/scripts/accessstatus_banner.js`,
   v1.0.0). OnLoad registers data OnLoad + OnPostSave once (remove-then-add) and evaluates. Closed copy and ids from the
   POML (`sprk_access_secure`, `sprk_access_noaccess` ERROR; `sprk_access_unavailable` INFO). Fail closed: any `unknown`,
   missing/unrecognised signal, non-200, unparseable body, another record's answer, missing `Spaarke.BffAuth`, no BFF
   URL, null token/Response, a throw or a 20 s timeout → ONLY the unavailable notice. Ordering and "keep until
   replaced" are keyed on the RECORD, never on the form-context object (verifier pass 2): a per-record sequence renders
   only the newest evaluation's answer, into every form that asked about the record and still shows it; while a record
   is re-evaluated the form first shows that record's last rendered state again (in place, no gap); a record never
   rendered yet, or no record, clears the three ids first. `render()` sets the ids it shows and clears the others.
   Create form: no call.
2. **TrackingFieldTrio indicator** (shared core): one opt-in prop `accessStatus` (the two signals). Since owner round 85
   it shows only red "No Access" (`INDICATOR_SHOWS_SECURE = false`: the red pill already says Secure, so a secure-only
   record draws nothing); neutral "Access status unavailable" on any unknown; nothing when No Access does not apply;
   nothing when the prop is omitted (email reading pane unchanged). Clickable only with `onOpenGrantModal` AND
   `canGrantAccess === true`: → `onOpenGrantModal('noAccess')`. (With the flag `true` it would also show "Secure" /
   "Secure · No Access", a Secure-only click opening `onOpenGrantModal()`; that path is tested through the explicit
   parameter of `resolveAccessIndicator`.) Otherwise a focusable span with no handler and a "You cannot manage access on this record."
   tooltip. The person icon now calls `onOpenGrantModal()` explicitly (the click event is never passed as a section).
3. **Section plumbing** (067 did not build it; round 59 cut it): `onOpenGrantModal` gains an optional `section` argument
   (no second callback), and `AccessGrantModal` gains `initialSection?: 'noAccess'`, which scrolls to and focuses the No
   Access List once THAT open's load finishes (state armed in the same batch as the open's loading state, so the
   previous open's list is never used; once per open; nothing when the section is hidden).
4. **PCF host** (`src/client/pcf/TrackingFieldTrio/index.ts`, 1.0.41): `ensureAccessStatus()` / `evaluateAccessStatus()`
   with `evaluateGrantGate`'s discipline (three-state "asked for", revoke on rebind, stale answers dropped). Only the
   three root tables are asked (`GRANT_ROOT_BY_ENTITY` exact match, not `resolveGrantRoot`'s project fallback). The read
   is the shared `readAccessStatus` (reuses 067's `buildNoAccessPath`, now exported).

## Component justification (CLAUDE.md §11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `sprk_accessstatus_banner.js` | `sprk_assignedaccess_postsave.js` is on the same three forms; no authored `setFormNotification` banner of access status exists | Folding it into 142's library would couple a fail-SOFT post-save sync (skips on no token, by design) with a fail-CLOSED banner (must show "unavailable"); one more form library entry is the cheaper cost | The owner's round-3b banner does not exist; a Read-only viewer gets no signal on the form |
| `TrackingFieldTrio/accessStatus.ts` (`parseAccessStatusResponse`, `readAccessStatus`, `resolveAccessIndicator`) | `AccessGrantModal/noAccess.ts` `parseNoAccessResponse` parses the same route for the ENTRIES and returns a section state; it discards the two signals | Its output is the list state (hidden/error/list); the indicator needs the signals tri-state. It reuses `buildNoAccessPath` and `cleanGuid` | The PCF's fail-closed read would be untested inline code |
| Prop `accessStatus`; optional `section` arg on `onOpenGrantModal`; prop `initialSection` | None (067's deep-link was cut) | Extends the existing callback and modal props; no second open callback | The owner's "clickable indicator that opens Manage Access" (round 83) is not built, or opens at the wrong place |
| Export of `buildNoAccessPath` | — | Reuse of 067's route builder | A second route-table map |

No endpoint, column, package or DI registration.

## ADR notes

- ADR-003 / owner fail-closed directive: every non-answer is "unavailable" in both clients; `doesNotApply` renders nothing.
- ADR-006: the banner is a thin form event handler; the server decides every state.
- ADR-021: tokens only; classes combined with `mergeClasses` (a jest console check caught a string-concatenated class);
  dark-theme render test.
- ADR-022: React 16-safe primitives only.
- ADR-028: PCF calls through the host's `authenticatedFetch`; the banner through `Spaarke.BffAuth.authenticatedFetch`.
- ADR-044 (§6.5 path C): the banner canonicalises ids with a local one-liner because a classic web resource cannot
  import `cleanGuid`; the same spelling as `sprk_assignedaccess_postsave.js`.
- `.claude/constraints/webresource.md` still says web-resource-called endpoints must be `.AllowAnonymous()`; superseded
  by `Spaarke.BffAuth` and CLAUDE.md §9 (the main session owns `.claude/`).

## Tests

| Suite | Tests |
|---|---|
| `src/__tests__/accessStatusBanner.test.ts` (the real script in jsdom) | 38 (28 + 6 in pass 1 + 4 net in pass 2) |
| `TrackingFieldTrio/__tests__/TrackingFieldTrio.accessStatus.test.tsx` | 53 (43 + 8 in pass 1 + 2 in round 85) |
| `AccessGrantModal/__tests__/AccessGrantModal.initialSection.test.tsx` | 4 |
| Existing TrackingFieldTrio + AccessGrantModal suites | unchanged, green |

Full `@spaarke/ui-components` jest: 4179 passed, 1 failed (`buildDynamicWorkspaceConfig.test.ts`, pre-existing, #1345).
Seeded mutations of the banner (unknown rendered as nothing; no supersession drop; no record-left drop; null Response
as "clear"; unknown state as doesNotApply; one global sequence for every form) each fail at least one test.
PCF `scripts/Invoke-PcfBuildProd.ps1` succeeded; `bundle.js` 1,024,345 bytes after owner round 85 (1,024,348 after
verifier pass 1; 1,023,627 at first pass; 1,017,260 at 1.0.40, +7.1 KB);
`pcf-scripts lint` clean. Every new test asserts rendered output or the request sent, not a mock echo.

## Known limits

- **K4 (task 174 dependency)** For a filed work assignment or project whose stored `sprk_issecure` lags its secure
  parent, the SECURE banner and the indicator's "Secure" follow the record's OWN flag (064's `secure`). No Access is
  already parent-aware (064 counts entries through a secure parent). One read point per client
  (`signalsOf` in the banner, `parseAccessStatusResponse` in the shared core); if 174 makes `secure` the effective value
  nothing here changes, and if it adds a separate field only those two functions change.
- **K2** Two forms showing the same record share its state (verifier pass 2): both show the newest answer, which is
  the correct state of that one record.
- **K2** While the PCF's status request is in flight (at most `ACCESS_STATUS_TIMEOUT_MS`, 20 s) the indicator is not
  drawn; after that it shows "Access status unavailable".

## Verifier pass 1 fixes (2026-10-08)

Pass 1 found nothing for security, leakage or fail-open. Fixed:

- **F4-1, the banner vanished on every save.** (The per-form `WeakMap` below was replaced in pass 2 by record-keyed state.) `evaluate()` cleared the notifications before the request, so after a
  save or a data refresh the SECURE / NO ACCESS banner disappeared for the round trip (a false "no restriction"
  moment). Now a per-FORM state (`WeakMap` keyed by the form context: evaluation number + the record last rendered)
  keeps the current notifications while the SAME record is re-evaluated on that form; no record, another record, or no
  per-form state (no `WeakMap`) clears up front as before. `render()` replaces ids in place and clears only the ones it
  does not show. The evaluation counter is per form (it was per record), so one record open in two forms is correct in
  both. Tests: a pending re-evaluation keeps the ERROR notifications; another record clears first; the same record in
  two forms with overlapping evaluations; the no-`WeakMap` fallback. Seeded (always clear; per-record counter) → fail.
- **F4-2, the red indicator lost its colour on hover and press.** Fluent's transparent Button sets its own `:hover` /
  `:hover:active` background and colour. `accessIndicatorRestricted` now sets both to `colorPaletteRedBackground3` /
  `colorNeutralForeground1`. Test: the button's own classes carry those rules (Griffel's generated CSS); seeded → fail.
- **Hardening, hung requests.** The banner's `fetchSignals` (`Config.timeoutMs`, 20 s) and the PCF's
  `readAccessStatus` (`ACCESS_STATUS_TIMEOUT_MS`, 20 s) bound the whole call (token acquisition included) and abort the
  request; a timeout is "unavailable". Tests for both.

**Owner change points:** both flipped in owner round 85 (below).

## Verifier pass 2 fixes (2026-10-08)

Pass 2 (full) confirmed the pass-1 fixes and the timeout. Fixed:

- **K2-a (treated as must-fix): ordering depended on the form-context wrapper's identity.** Pass 1 keyed the keep logic
  and the sequence on the `getFormContext()` object through a `WeakMap`. Unified Interface does not promise the same
  object for OnLoad, OnPostSave and data OnLoad; with a fresh wrapper per event an older OnLoad answer ("not secure")
  landing after a newer Make Secure refresh answer cleared the SECURE banner (verifier T7/T8). Now everything is keyed
  on the record ("table:id"), module-level: `_seqByRecord` (newest evaluation wins), `_lastByRecord` (re-shown while
  the record is re-evaluated) and `_waitingByRecord` (every form that asked gets the newest answer if it still shows the
  record, so a form whose older evaluation was dropped is not left blank or stale). The `WeakMap` is removed. Two forms
  on the same record share state and both show the newest answer. Tests: T7 and T8 with a fresh wrapper per event; a
  dropped older evaluation's form updated by the newer answer; a form switching to a record rendered before shows that
  record's state, never the previous record's. The pass-1 code fails T7 and T8; seeded mutations (no sequence check, no
  re-show, render only into the asking form, abort logged as an error) each fail.
- **K1-b: an aborted request logged an error.** After a timeout the aborted fetch's `AbortError` is no longer logged;
  the timeout's single warning stands. Test: one warning, no error.
- **F4-a: docs** (this file, "What was built" item 1, and the ids comment in the script) now match the code.

**Cost of flipping each owner change point** (measured by flipping it and running the suites):
- (a) `Spaarke.AccessStatus.ManageAccessFrom` re-pins **3 tests** (the `TEXT` constant in
  `accessStatusBanner.test.ts` holds the closed copy verbatim; the secure-only, no-access-only and both cases).
- (b) `INDICATOR_SHOWS_SECURE = false` re-pins **9 tests** (the Secure and both cases of the indicator suite).
- **They are coupled.** If (b) flips, the indicator draws nothing for a Secure-only record, so (a)'s SECURE wording must
  not send the reader to a marker that is no longer drawn (keep "the person icon", or say "the person icon, or the red
  No Access marker when there is one").

## Owner round 85 (2026-10-08): both change points accepted

- **(b)** `INDICATOR_SHOWS_SECURE = false` (`TrackingFieldTrio/accessStatus.ts`): the indicator shows only No Access; a
  secure-only record draws nothing (the red pill says Secure). The 9 tests re-pinned to the new default (secure-only
  draws nothing and offers no click; both → "No Access" opening at the No Access List; the dark-theme and header
  renders use a No Access record), plus 2 added (secure-only draws nothing; the flag's value). The Secure path stays
  covered through `resolveAccessIndicator(status, true)`.
- **(a)** `ManageAccessFrom` split into two one-line constants, because the two banners now point at different places:
  `Spaarke.AccessStatus.ManageAccessFromSecure` and `Spaarke.AccessStatus.ManageAccessFromNoAccess`. Neither banner
  says "(the person icon in the tracking panel)" any more as the only pointer. The final text:
  - **SECURE** (ERROR, `sprk_access_secure`): "SECURE RECORD — only people given access explicitly can see this record.
    People who can manage access see and change who has access in Manage Access, opened from the person icon in the
    tracking panel."
  - **NO ACCESS** (ERROR, `sprk_access_noaccess`): "NO ACCESS RESTRICTION — named people or organizations are blocked
    from this record. People who can manage access see the list from the red "No Access" marker in the tracking panel,
    which opens Manage Access at the No Access List."
  - The unavailable notice is unchanged. The 3 banner text tests re-pinned (the `TEXT` constant).
- Coupling kept: the SECURE text points at the person icon, not at a marker that is no longer drawn. Flipping (b) back
  needs the SECURE text re-checked (noted at both constants).

## Filed (out of scope)

- **#1449** The access-permission pill, Manage Access's secure state and (inherited) the new indicator stay stale after
  the Access ribbon's `formContext.data.refresh(false)` until the page is reloaded: the PCF re-reads only on a record
  change. Pre-existing for the pill (task 138). The form banner does re-evaluate on data OnLoad.

## Deploy (main session)

1. Web resource: `scripts/Deploy-WebResourceInline.ps1 -DataverseUrl <env> -WebResourceName sprk_/scripts/accessstatus_banner.js
   -FilePath src/solutions/webresources/sprk_accessstatus_banner.js -WebResourceType 3` (JScript; the script publishes).
2. Form registration: `pwsh -File scripts/Register-AccessStatusBannerOnForms.ps1 -EnvironmentUrl <env>` (dry run), then
   `-Apply` (snapshot of each form's XML in `scripts/logs/`, PATCH, publish the three tables, read back), then `-Verify`
   (exit 0). Undo: `-RestoreFrom <snapshot>`.
3. PCF: TrackingFieldTrio 1.0.41 via the pcf-deploy skill (solution import of the committed bundle; never `pac pcf push`).
4. No BFF deploy (no server change).

## Form registration script (coordinator request on PR #1450)

`scripts/Register-AccessStatusBannerOnForms.ps1`, modelled on `Add-RegardingFilingPickerToForms.ps1` and
`Deploy-NoAccessEntryForms.ps1` (Web API, no pac):

- Targets ONLY the active main forms named `Project main form`, `Matter main form` and `Work Assignment main form`
  (one each, by table and name; `-ProjectFormName` etc. override). Refuses a managed form (`MANAGED_FORM`).
- Additive only: appends `sprk_/scripts/accessstatus_banner.js` at the END of `<formLibraries>` (so after
  `sprk_/scripts/bff_auth.js`, which must already be registered: `AUTH_LIBRARY_MISSING` otherwise) and one
  `Spaarke.AccessStatus.onLoad` handler (enabled, pass execution context) at the end of the form-level OnLoad. Never
  removes, changes or reorders another library or handler, and never touches control-level events. A parsed
  comparison (the result minus exactly the added nodes equals the original, node by node) runs on every transform,
  including the dry run against the live XML. Stable ids from the form id: a re-run writes identical XML; a complete
  form is left alone.
- Refuses (never "fixes") a banner library registered before `bff_auth.js` (`LIBRARY_ORDER`) and a duplicated,
  disabled, foreign-library or context-less banner handler (`HANDLER_MISCONFIGURED`); `-Verify` names each as a gap.
- `-Apply` refuses while either web resource is missing (`PREREQ_MISSING`) or if a form changed between the scan and
  its PATCH (`FORM_CHANGED`). `-RestoreFrom` refuses another environment's snapshot or a form changed since the apply.
- No solution components are touched (forms are edited in place), so `DataverseSolutionMembership.ps1` does not apply;
  the ArchTests' `SchemaScriptSolutionMembershipGuardTests` pass.

Run 2026-10-08 (read-only; NOT applied, the main session's gate):

- `-SelfTest`: **SELF-TEST PASS**, 44 checks. Fixtures follow the live spaarkedev1 form shapes. The checks cover:
  - the project shape (`events` before `formLibraries`) and the matter shape (several handlers, JSON `parameters`);
  - no form `events`, and an empty form OnLoad;
  - libraries kept in order with the banner appended last; existing handlers byte-identical and in order;
  - control-level events untouched; idempotent and deterministic;
  - the refusals: no `bff_auth.js`, banner library first, duplicate / disabled / context-less handler;
  - a form that has the library but no handler gains only the handler.
- Dry run against spaarkedev1 (exit 0):

```
Register-AccessStatusBannerOnForms (task 153)  env: https://spaarkedev1.crm.dynamics.com (org 'spaarkedev1')  mode: DRY RUN (no writes)

== Prerequisites
   ok    web resource sprk_/scripts/bff_auth.js present
   GAP   web resource sprk_/scripts/accessstatus_banner.js is not in the environment (deploy it first: scripts/Deploy-WebResourceInline.ps1)

== sprk_project: 'Project main form'
   PLAN  library sprk_/scripts/accessstatus_banner.js is not registered
   PLAN  no form OnLoad handler Spaarke.AccessStatus.onLoad
   PLAN  add 2 node(s); every existing library and handler kept, in order

== sprk_matter: 'Matter main form'
   PLAN  library sprk_/scripts/accessstatus_banner.js is not registered
   PLAN  no form OnLoad handler Spaarke.AccessStatus.onLoad
   PLAN  add 2 node(s); every existing library and handler kept, in order

== sprk_workassignment: 'Work Assignment main form'
   PLAN  library sprk_/scripts/accessstatus_banner.js is not registered
   PLAN  no form OnLoad handler Spaarke.AccessStatus.onLoad
   PLAN  add 2 node(s); every existing library and handler kept, in order

DRY RUN: 3 form(s) would change: sprk_project, sprk_matter, sprk_workassignment. 1 prerequisite gap(s) must be closed before -Apply. Re-run with -Apply.
```

- `-Verify` against spaarkedev1 (exit 1, as expected before the web resource deploy and `-Apply`):

```
Register-AccessStatusBannerOnForms (task 153)  env: https://spaarkedev1.crm.dynamics.com (org 'spaarkedev1')  mode: VERIFY (read-only)

== Prerequisites
   ok    web resource sprk_/scripts/bff_auth.js present
   GAP   web resource sprk_/scripts/accessstatus_banner.js is not in the environment (deploy it first: scripts/Deploy-WebResourceInline.ps1)

== sprk_project: 'Project main form'
   GAP   sprk_project 'Project main form': library sprk_/scripts/accessstatus_banner.js is not registered
   GAP   sprk_project 'Project main form': no form OnLoad handler Spaarke.AccessStatus.onLoad

== sprk_matter: 'Matter main form'
   GAP   sprk_matter 'Matter main form': library sprk_/scripts/accessstatus_banner.js is not registered
   GAP   sprk_matter 'Matter main form': no form OnLoad handler Spaarke.AccessStatus.onLoad

== sprk_workassignment: 'Work Assignment main form'
   GAP   sprk_workassignment 'Work Assignment main form': library sprk_/scripts/accessstatus_banner.js is not registered
   GAP   sprk_workassignment 'Work Assignment main form': no form OnLoad handler Spaarke.AccessStatus.onLoad

VERIFY FAIL: 7 gap(s).
```

## Live gates (main session / owner)

See the PR description (adapted from POML criterion 12 and the ui-tests, three root forms only).

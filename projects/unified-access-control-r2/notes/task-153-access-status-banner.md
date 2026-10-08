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
   URL, null token/Response, or a throw → ONLY the unavailable notice. Each evaluation clears its three ids first; a
   superseded answer (per record) or one for a record the form has left is dropped. Create form: no call.
2. **TrackingFieldTrio indicator** (shared core): one opt-in prop `accessStatus` (the two signals). Red "Secure" /
   "No Access" / "Secure · No Access"; neutral "Access status unavailable" on any unknown; nothing when both
   `doesNotApply`; nothing when the prop is omitted (email reading pane unchanged). Clickable only with
   `onOpenGrantModal` AND `canGrantAccess === true`: No Access (or both) → `onOpenGrantModal('noAccess')`; Secure only →
   `onOpenGrantModal()`. Otherwise a focusable span with no handler and a "You cannot manage access on this record."
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
| `src/__tests__/accessStatusBanner.test.ts` (the real script in jsdom) | 28 |
| `TrackingFieldTrio/__tests__/TrackingFieldTrio.accessStatus.test.tsx` | 43 |
| `AccessGrantModal/__tests__/AccessGrantModal.initialSection.test.tsx` | 4 |
| Existing TrackingFieldTrio + AccessGrantModal suites | unchanged, green |

Full `@spaarke/ui-components` jest: 4179 passed, 1 failed (`buildDynamicWorkspaceConfig.test.ts`, pre-existing, #1345).
Seeded mutations of the banner (unknown rendered as nothing; no supersession drop; no record-left drop; null Response
as "clear"; unknown state as doesNotApply; one global sequence for every form) each fail at least one test.
PCF `scripts/Invoke-PcfBuildProd.ps1` succeeded; `bundle.js` 1,023,627 bytes (was 1,017,260 at 1.0.40, +6.4 KB);
`pcf-scripts lint` clean. Every new test asserts rendered output or the request sent, not a mock echo.

## Known limits

- **K4 (task 174 dependency)** For a filed work assignment or project whose stored `sprk_issecure` lags its secure
  parent, the SECURE banner and the indicator's "Secure" follow the record's OWN flag (064's `secure`). No Access is
  already parent-aware (064 counts entries through a secure parent). One read point per client
  (`signalsOf` in the banner, `parseAccessStatusResponse` in the shared core); if 174 makes `secure` the effective value
  nothing here changes, and if it adds a separate field only those two functions change.
- **K2** While the PCF's status request is in flight the indicator is not drawn (the form banner shows the result; an
  answer that never arrives leaves the banner's own unavailable notice when the call fails).

## Filed (out of scope)

- **#1449** The access-permission pill, Manage Access's secure state and (inherited) the new indicator stay stale after
  the Access ribbon's `formContext.data.refresh(false)` until the page is reloaded: the PCF re-reads only on a record
  change. Pre-existing for the pill (task 138). The form banner does re-evaluate on data OnLoad.

## Deploy (main session)

1. Web resource: `scripts/Deploy-WebResourceInline.ps1 -DataverseUrl <env> -WebResourceName sprk_/scripts/accessstatus_banner.js
   -FilePath src/solutions/webresources/sprk_accessstatus_banner.js -WebResourceType 3` (JScript), publish.
2. Form registration, on each MAIN form (Project main form, Matter main form, Work Assignment main form; not
   "Information", not quick-create): Form libraries — `sprk_/scripts/bff_auth.js` stays FIRST (already present), add
   `sprk_/scripts/accessstatus_banner.js` after it. Event handler: OnLoad → library `sprk_/scripts/accessstatus_banner.js`,
   function `Spaarke.AccessStatus.onLoad`, enabled, **pass execution context**. Do NOT add data-OnLoad or OnSave handlers
   in the form editor (the library registers its own). Save, publish.
3. PCF: TrackingFieldTrio 1.0.41 via the pcf-deploy skill (solution import of the committed bundle; never `pac pcf push`).
4. No BFF deploy (no server change).

## Live gates (main session / owner)

See the PR description (adapted from POML criterion 12 and the ui-tests, three root forms only).

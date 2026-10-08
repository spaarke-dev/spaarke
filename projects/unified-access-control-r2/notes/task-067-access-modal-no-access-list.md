# Task 067: Manage Access, read-only No Access List, walled-off and cancelled rows

> Executor notes, 2026-10-08. Branch `task/uac-r2-067` (worktree `C:\wt067`, from `origin/master` c5f71487f, which
> carries 064 / PR #1411). Rigor FULL. Scope as narrowed by owner round 59 item 3 (066 folded in). Nothing was deployed
> or imported: the PCF ships at live session D with 099.

## Scope as built (round 59) and what was cut

Built:

1. **No Access List section** in Manage Access, read-only, over 064's `GET /api/v1/records/{table}/{id}/no-access`
   (contract `notes/phase4-access-report-contract.md`). Each entry shows who (contact / organization / user), the scope
   (this record, every record referencing organization X, through the secure parent, "and again through a secure
   parent"), its in-force state from the server (`No Access` / `Not in force` / `Undetermined`) with the reason
   (`malformed`, `userWallOnNonSecureRecord`, `secureStateUnknown`), and the last change (modifier + date).
   `notShown` hides the section; `truncated` adds a "more entries exist" line; `unavailable`, any non-200 (including
   the route's uniform 404), an unparseable or off-contract body, and an answer about another record render an error
   state, never "no entries". The section has no buttons: walls are authored in No Access Entries (task 154).
2. **Veto marker** on Current Access rows that an IN-FORCE entry walls off: a red `No Access` badge, the level badge
   struck through, and a reason line. Matching follows the contract: a contact row by contact, a user share by
   system user, an organization grant by organization, and a contact row also by the contact's organization (new host
   callback `fetchContactOrganizationMemberships`, called only when an organization wall is in force). Revoke stays
   available. The share sentence does not claim the share is inert in the model-driven app (a Dataverse share still
   opens the record there until 143 removes it).
3. **Cancelled rows (066)**: `No effect` marker plus a neutral italic reason, from `accessPermissionState` /
   `isSecureRecord`: Restricted cancels every contact-based row (contact, organization, standing); Limited and Secure
   cancel organization-wide and standing rows. Internal user shares are never cancelled. A wall wins over cancellation.

Cut by round 59 (not built): add/remove UI, the deep-link initial-section plumbing (no caller under O1-final; 153 no
longer depends on 067), standing-level editing (FR-25 UI), per-entry enforcement labels, 066's server-report rebase.
The POML's escalation triggers (O1, O2, standing-level write) therefore do not fire.

## Component justification (CLAUDE.md §11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `AccessGrantModal/noAccess.ts` (pure helpers: parse, veto, cancel) | `accessPermissionState.ts` is the same pattern for a different rule; no client read 064's route before this task (`git grep no-access` on master: under `src/client` nothing; under `src/solutions` the 154 and 142 form libraries, which call `/no-access/enforce`, plus docs) | The component file could hold them, but the rules need tests without rendering, as `accessPermissionState.ts` does | The fail-closed parse rules (another record's answer, `unavailable`, off-contract entries) would have no direct tests |
| Prop `fetchContactOrganizationMemberships` + host method | The host reads `sprk_externalrecordaccess` and contacts, never `sprk_contactorganization`; the server's equivalent is `ExternalParticipationService.BuildOrganizationMembershipFilter` | Extending `fetchExistingGrants` would read memberships on every open, though organization walls are rare | A contact whose organization is walled keeps reading as active in Current Access, which is the defect round 59 named |
| Types `IRecordNoAccessEntry`, `IContactOrganizationMembership` | 064's C# DTO `RecordNoAccessEntry` | Client mirror of the frozen contract | No typed parse of the route |

No new endpoint, column, package or DI registration. No BFF change.

## ADR notes

- ADR-012 (entity-agnostic shared core), §6.5 path C: the modal maps its semantic `recordType` to 064's route segment
  (`sprk_project` / `sprk_matter` / `sprk_workassignment`). That segment is a BFF route contract, like the endpoint
  paths and the fixed access-level values the modal already holds; the modal reads no Dataverse schema.
- ADR-021: tokens only (`colorStatusDangerForeground1` for the veto reason, `colorNeutralForeground3` italic for the
  cancel reason, Fluent Badge colours `danger` vs `subtle`); dark-theme render test included.
- ADR-028: every call through the host's `authenticatedFetch`.
- ADR-044: the record id is `cleanGuid`-normalized in the route and in the echo check; subject and membership ids are
  compared canonically.

## Tests

- New `AccessGrantModal.noAccess.test.tsx`: 46 tests (40 at first pass, +5 for verifier pass 1, +1 for pass 2) (route and canonical id; full entry rendering; read-only; truncated;
  empty; `notShown` hidden; no request without Write; 10 error shapes; contact / user / organization / org-membership
  veto; not-in-force, undetermined and malformed entries never veto; membership read failure and absence; Restricted,
  Secure, Limited, Standard cancellation; veto beats cancellation and looks different; every level dropdown offers only
  View Only / Collaborate / Full Access; dark theme; helper unit tests).
- Modal + TrackingFieldTrio suites: 10 suites, 195 tests, all pass.
- Full `@spaarke/ui-components` jest: 4097 passed, 1 failed (`buildDynamicWorkspaceConfig.test.ts`, pre-existing, #1345).
- PCF `npm run build:prod` via `scripts/Invoke-PcfBuildProd.ps1`: succeeded; `bundle.js` 1,017,260 bytes (993 KiB),
  was 1,006,789 (+10.5 KB). `pcf-scripts lint` clean.

## Known limits

- **K4** A row reached through a secure parent is labelled with the parent's type ("the secure matter this record is
  filed under"), not its name: 064's contract returns only `coveredRecordType` / `coveredRecordId`.
- **K2** When the host cannot read the Secure flag, and the Access Permission value does not say Limited or Restricted
  (it is Standard, unset, or the pill is unbound), the host passes `limited` (fail closed) and cancelled rows say "this
  record is Limited". For a record that is really Standard this OVERSTATES the cancellation (organization and standing
  rows shown as "No effect"). For a record that is really Restricted but whose value the host does not have, it
  UNDERSTATES it: named contact grants show as active. Display only; the server decides access in both cases.
- **K4** Cancellation is styled from the record's OWN Secure flag. A filed child whose secure parent governs it (round
  82) but whose own flag is still false shows organization/standing rows as active; the walls of that parent are
  shown correctly (064's `inForce`).
- **K4** A user wall marks the user's share row, not a contact row of that user's work contact (the contract matches
  by subject id only).

## Verifier pass 1 fixes (2026-10-08)

- **F4-a, overlapping loads.** The modal stays mounted while the host form rebinds, and every grant or revoke reloads,
  so an older load could resolve last and show another record's No Access List (e.g. "No one is on this record's No
  Access List" on a walled record) or its grants. `loadData` now numbers its loads: only the latest writes state,
  an older one is dropped whole and cannot clear `loading`, and closing the modal retires any load in flight. The
  No Access echo is checked against the record shown NOW (a ref), not the one the request was sent for. This also
  fixes the same pre-existing race for grants, shares, candidates and suggestions. Tests: rebind between opens with
  either answer landing first; rebind while open; a wrong echo.
- **F4-b, truncated list.** Markers come only from the listed entries, so with more than 100 entries a row walled by
  an unlisted one would read as active. The truncation line is now a warning that says Current Access rows are marked
  from the listed entries only.

## Verifier pass 2 fix (2026-10-08)

- **F4-c, a write that finishes after a rebind.** Write handlers (grant, revoke, unshare, suggestion grant/dismiss)
  run in the closure of the render where the user clicked. If the host rebound the modal to record B while the write
  on A was pending, the write's `await loadData()` took a NEW load number (so it won) but read A's shares, Assigned-To
  entries and No Access List, and its notice named A's person on B. `loadData` now returns at once unless its closure's
  record is the record shown now (`currentRecordIdRef`), and every notice and deny banner set after a write goes
  through the same check (`setNoticeIfCurrent` / `setDenyIfCurrent`); a finished grant batch also leaves B's staged
  picks alone. An unmount effect retires loads in flight. Test: the verifier's real host sequence (revoke pending on
  A, gate false then true on B, B loads, A's revoke answers) asserts that no request for A is sent and that neither
  "Share user of A", A's contact nor the revoke notice appears.
- **How a rebind reaches the modal.** The real host (`TrackingFieldTrio/index.ts`) revokes the Manage Access gate on
  rebind and answers it again for the new record, so `canGrantAccess` goes false then true and the modal's load effect
  re-runs for the new record. That is the path the F4-a and F4-c fixes cover. (Pass 1's note recorded a K4 "the modal
  does not reload on a rebind while open"; that is wrong for the real host and is withdrawn.) A host that changed
  `recordId` without touching the gate would get the No Access List's error state (the echo no longer matches) and
  stale-closure loads are refused, but nothing reloads until the gate or `open` changes; no such host exists.
- **K1 hardening** in the host: `fetchContactOrganizationMemberships` refuses any id that is not a canonical GUID before
  it reaches the OData filter (the modal then shows "could not be checked").

## Live gates (session D, after the 1.0.40 import)

See the PR description; the main session runs them.

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

- New `AccessGrantModal.noAccess.test.tsx`: 40 tests (route and canonical id; full entry rendering; read-only; truncated;
  empty; `notShown` hidden; no request without Write; 10 error shapes; contact / user / organization / org-membership
  veto; not-in-force, undetermined and malformed entries never veto; membership read failure and absence; Restricted,
  Secure, Limited, Standard cancellation; veto beats cancellation and looks different; every level dropdown offers only
  View Only / Collaborate / Full Access; dark theme; helper unit tests).
- Modal + TrackingFieldTrio suites: 9 + 1 suites, 189 tests, all pass.
- Full `@spaarke/ui-components` jest: 4097 passed, 1 failed (`buildDynamicWorkspaceConfig.test.ts`, pre-existing, #1345).
- PCF `npm run build:prod` via `scripts/Invoke-PcfBuildProd.ps1`: succeeded; `bundle.js` 1,017,260 bytes (993 KiB),
  was 1,006,789 (+10.5 KB). `pcf-scripts lint` clean.

## Known limits

- **K4** A row reached through a secure parent is labelled with the parent's type ("the secure matter this record is
  filed under"), not its name: 064's contract returns only `coveredRecordType` / `coveredRecordId`.
- **K2** When the host could not read the Secure flag it passes `limited` (fail closed), so cancelled rows say "this
  record is Limited" though the record may be Standard and unread. Display only; it overstates the cancellation, and
  the server decides.
- **K4** Cancellation is styled from the record's OWN Secure flag. A filed child whose secure parent governs it (round
  82) but whose own flag is still false shows organization/standing rows as active; the walls of that parent are
  shown correctly (064's `inForce`).
- **K4** A user wall marks the user's share row, not a contact row of that user's work contact (the contract matches
  by subject id only).

## Live gates (session D, after the 1.0.40 import)

See the PR description; the main session runs them.

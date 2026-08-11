# Task 052 — Related-to card-picker — COMPLETE (2026-08-07)

**Rigor**: FULL · sonnet·high · directional. **Model**: Opus 4.8.
**Result**: shared-lib code + tests green (tsc 0; jest 27 suites / 195 tests, +6 for 052). Step 9.5 clean. Conflict-check clean (email-communication-solution-r5 no overlap on the touched files).

## What shipped (reuse EmailConnectionsReview — no forked picker, no second write path, ADR-024/045)

- **`components/ReconciliationGrid/RelatedToCell.tsx`** (NEW): the grid "Related to" cell. Unconfirmed → blank + "Requires review" + an open-picker icon; confirmed → the linked-record chip. Clicking the icon opens the reused **`EmailConnectionsReview`** in a canonical **`SprkModal`** (ADR-050) — same candidate cards + manual lookup + create-new intent the email-form review uses. The cell contains **NO regarding-write logic** — Confirm writes through `EmailConnectionsReview`'s existing `applyRegardingSelection`/`RegardingFieldMap` path (`writeContext.hostRecordId` === `communicationId`, asserted in the test via `updateRecord('sprk_communication', HOST_ID, …)`). `onConfirmed` is the **NFR-10 handshake** (Fields/Tasks tabs 055/056 scope on the confirmed record; re-scope on override).
- **`ReconciliationGrid.tsx`** (MODIFIED): new optional `relatedTo?: RelatedToGridBinding` prop — `{ resolveReview(record) → EmailConnectionsReviewProps, onConfirmed?, columnField? }`. When wired, the association column (`sprk_associationstatus` default) becomes the interactive `RelatedToCell` (injected last so it wins over the static badge). Left unset, the column keeps its 050 status badge — backward-compatible.

## §6.5 reuse-by-extension — r5-owned `EmailConnectionsReview` (COORDINATION for task 058)

The POML assumed the create-new "new-vs-related" intent already renders inside `EmailConnectionsReview`. It did NOT: the `onCreateNewRecord` prop **existed on the component's public type but was never destructured/rendered**. The manual-lookup seam ("Link another record") and the single write path (`applyRegardingSelection`) were both already present.

Rather than fork a second picker (forbidden) or hard-stop, I applied the escalation trigger's sanctioned path — **extend the shared component additively**: wired the already-declared `onCreateNewRecord` prop into a gated **"Create new record" tile** (`data-testid="create-new-record"`), rendered ONLY when a host passes `onCreateNewRecord` **and** `!readOnly`. Existing consumers (`EmailWorkspace`) omit `onCreateNewRecord`, so the tile never appears there — **provably backward-compatible** (all 18 existing `EmailConnectionsReview` tests pass unchanged). This keeps ONE picker + ONE write path.

- **File touched (r5-owned)**: `components/EmailAssociationsAndTracking/EmailConnectionsReview.tsx` — +`DocumentAdd20Regular` import, +`onCreateNewRecord` destructure, +the gated create-new tile. No behavior change for any consumer that doesn't wire the prop.
- **Coordination obligation (task 058 / FR-E6)**: record R2's additive ownership of the `onCreateNewRecord` render in `EmailConnectionsReview` in the r5 coordination contract. Conflict-check clean (r5 has zero edits to `EmailAssociationsAndTracking` on its branch or working tree); no merge risk. Re-run `/conflict-check` before the shared-lib PR.

## NFR-10 handshake for 055/056
`RelatedToCell.onConfirmed` (and the grid's `relatedTo.onConfirmed(record)`) fire when the association is confirmed — the precondition the Fields (055) and Tasks (056) reconcile tabs require before they become actionable, and the signal to re-scope on a later override.

## Notes
- Host wiring (`resolveReview` mapping a grid row → `EmailConnectionsReview` props incl. `writeContext`/pickerWebApi/catalog) is a task-059 (code-page + widget) concern; 052 ships the component + the grid binding + the r5 extension.
- Live visual dark-mode + the MDA `Xrm.Utility.lookupObjects` manual-lookup path are jsdom-verified/host-gated; browser pass at 059.

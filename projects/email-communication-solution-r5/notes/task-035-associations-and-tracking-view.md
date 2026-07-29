# Task 035 — Reading-pane ASSOCIATIONS + TRACKING sub-views

**Status:** Complete. Build green (`tsc`), jest green (12/12 new tests; 90/90 minus a pre-existing, unrelated
sibling-task failure — see "Pre-existing build/test issue" below).

**Spec:** FR-13 / FR-14 / FR-19 · ADR-045 (communication/association) · ADR-021 (Fluent v9/dark) · NFR-05 (no
`React.ComponentType` cast)

---

## What shipped

Fills the task-032 reading-pane shell's `renderConnections(selectedId)` and `renderTracking(selectedId)` slots
(see `notes/task-032-reading-pane-shell.md` for the slot contract) with two new views in
`@spaarke/communication-components`, in their own component folder:

```
src/client/shared/Spaarke.Communication.Components/src/components/EmailAssociationsAndTracking/
  EmailAssociationsAndTracking.types.ts   ← EmailConnectionsReviewProps + EmailTrackingPanelProps
  EmailConnectionsReview.tsx               ← state + write orchestration + composition (renderConnections slot)
  EmailConnectionsReview.styles.ts         ← Fluent v9 styles (split out at code-review time)
  EmailConnectionsReview.helpers.tsx       ← constants + pure helpers (split out at code-review time)
  EmailConnectionsReviewRows.tsx           ← DecisionBlock / FiledRow / SuggestedRow (split out at code-review time)
  EmailTrackingPanel.tsx                   ← thin TrackingFieldTrio wrapper (renderTracking slot)
  index.ts                                 ← component-folder barrel (NOT wired into src/components/index.ts — orchestrator's job)
  __tests__/EmailAssociationsAndTracking.test.tsx  ← 12 tests covering the full acceptance set
```

**EmailConnectionsReview** consumes the task-020 PRODUCTION `logic/connections` extraction
(`parseProvenance`, `deriveConnections`, `mergeFiledConnections`, `groupCandidatesByName`,
`groupConnectionsByAction`, `applyRegardingSelection`, `unlinkRegarding`) via the package-relative
`../../logic/connections` import — never the stale `CommunicationPage/.../ConnectionsEditor.tsx` stub. It groups
associations into three action sections (mirrors the production `CommunicationConnections` PCF's rebuilt grouped
body, task 105/UAT R2 C1):

- **"Needs your decision"** — ambiguous conflicts (FR-19), rendered as selectable candidate-group radios; a
  "Confirm" persists the chosen candidate additively.
- **"Filed automatically"** — confirmed / auto-filed rows, INCLUDING reply-chain auto-association
  (`ThreadContinuityRung`, applied server-side at ingestion). This requires NO special-casing: a `written: true`
  candidate is already marked `status: 'confirmed'` by the task-020 `deriveConnections`, regardless of which rung
  produced it — display-only, never recomputed client-side (ADR-045). Rows offer **Change** (re-file via an
  embedded `PolymorphicPicker` scoped to that one entity type) and **Dismiss** (`unlinkRegarding` — nulls
  EXACTLY that one typed lookup, siblings untouched).
- **"Suggested"** — soft matches with **Confirm** (additive write) / **Dismiss** (client-side in-session hide,
  no write — nothing was filed, so nothing needs unfiling), plus a **"Link another record…"** affordance that
  embeds the same shared `PolymorphicPicker` (catalog derived from `TODO_REGARDING_CATALOG`).

The view OWNS the write orchestration itself (calls `applyRegardingSelection`/`unlinkRegarding` directly) rather
than only bubbling callbacks to a host — this is the one deliberate exception to this package's usual
"presentational-only, host owns the fetch/write" convention (`EmailCardList`, `TrackingFieldTrio`), because the
task goal explicitly requires confirm/change/dismiss/link-another to PERSIST via the additive path, and that
needed to be directly testable.

**EmailTrackingPanel** is a thin, presentational wrapper over the task-023 `TrackingFieldTrio` core
(`@spaarke/ui-components`) — adds a "Tracking" section header + an inline error surface around the onChange
callbacks; bakes in no `sprk_communication`-specific option integers or Dataverse field names (same
entity-agnostic contract `TrackingFieldTrio` itself already has — the host supplies values, options, and
write callbacks).

## Deviations from the task POML (documented per Step N)

1. **Directory location**: the POML's `<relevant-files>`/`<outputs>` list a flat
   `src/reading-pane/EmailAssociationsAndTracking.tsx` path. The actual established convention in this project
   (tasks 030–032, all landed) is a per-component folder under `src/components/<Name>/`. Followed the orchestrator's
   explicit instruction ("its OWN new component folder, e.g. `src/components/EmailAssociations/**`") and the
   codebase's real convention over the POML's literal (stale) path — per root CLAUDE.md §2 "Code wins, docs lag."
2. **Split into multiple files**: the POML names one file (`EmailAssociationsAndTracking.tsx`). Task 032 itself
   precedents splitting a task's component folder into multiple files (`EmailReadingPaneShell.tsx` +
   `EmailToolbar.tsx`). This task additionally split at **code-review time** (Step 9.5): the first draft of
   `EmailConnectionsReview.tsx` was 663 lines, crossing this repo's own review-metric Critical threshold
   (>500 lines). Extracted styles (`.styles.ts`), constants/helpers (`.helpers.tsx`), and the three row
   sub-components (`EmailConnectionsReviewRows.tsx`) — no behavior change, all 12 tests still green after the
   split. `renderConnections`/`renderTracking` are exported as two SEPARATE named components
   (`EmailConnectionsReview` / `EmailTrackingPanel`), matching the shell's two separate slot props.
3. **No AI-suggested-"Create X" rows**: the production PCF's grouped body also renders `deriveAiSuggestedTypes`
   ("looks like a new Matter" → Create/Dismiss). Scoped OUT of this task — that affordance is a record-creation
   action, which belongs with the toolbar's Create action (task 036), not the association-review surface. FR-13/
   FR-14/FR-19 do not require it.
4. **"Change" and "Link another record…" reuse `PolymorphicPicker`** (`@spaarke/ui-components`, already built)
   rather than the production PCF's `Xrm.Utility.lookupObjects` App-level orchestration — `PolymorphicPicker`
   already bridges to `Xrm.Utility.lookupObjects` via a `window`/`window.parent` traversal that explicitly
   supports both PCF and Code Page iframe contexts (see its own file doc), so it is the correct §11-reuse choice
   for a React 19 code-page view that has no PCF `context.webAPI`/Xrm guarantee otherwise.
5. **No `<primaryField>`/"Make primary"/★ star designation.** The production PCF designates one filed
   association as PRIMARY (denormalized `sprk_regardingrecord*` fields). Not part of FR-13/FR-14/FR-19's closed
   acceptance set for this task; every filed association still persists and displays correctly without a
   primary designation. Flagged here for a future task if the reading pane needs to show/set a primary.
6. **Tracking-field Dataverse writes are host-owned** — `EmailTrackingPanel` does not itself know
   `sprk_communication`'s field logical names (mirrors `TrackingFieldTrio`'s own established entity-agnostic
   contract from task 023); the host (task 040 assembly) wires `onMonitorChange`/`onHighPriorityChange`/
   `onAccessPermissionChange` to the actual Dataverse/BFF write.

## Pre-existing build/test issue (NOT caused by this task)

`src/components/EmailBody/EmailBodyView.tsx` (a concurrent, untouched, unrelated Wave-5 sibling task's file —
untracked in git, never edited by task 035) fails both `tsc` and its own jest suite: it uses `MessageBar`/
`MessageBarBody`/`MessageBarTitle` JSX without importing them from `@fluentui/react-components`. This is outside
task 035's guardrails (other components) and is flagged here for the orchestrator/owning task to fix. Verified
via `git status --porcelain` that task 035 touched ONLY the `EmailAssociationsAndTracking/` folder.

## For the orchestrator (barrel export — NOT added by this task per guardrail)

Add to `src/client/shared/Spaarke.Communication.Components/src/components/index.ts`:

```ts
export * from './EmailAssociationsAndTracking';
```

## Quality gates (Step 9.5)

- `code-review`: found `EmailConnectionsReview.tsx` at 663 lines (Critical line-count threshold) — fixed by
  splitting into 4 files (see Deviation 2). No AI code smells (no log-rethrow, no null-checks-on-non-nullable,
  no hardcoded hex, no `React.ComponentType` cast). Re-verified clean after the split.
- `adr-check`: Clean — 0 Violations across ADR-012 / ADR-021 / ADR-022(NFR-05) / ADR-045. Grep-verified: no
  Fluent v8 imports, no `ComponentFramework`/`Xrm.*` direct usage, no sibling `@odata.bind: null` (clear-and-set)
  pattern, no new client-side association-resolution logic.
- Build: `npm run build` (tsc, decl-only) — clean for all task-035 files (the only remaining errors are the
  pre-existing, unrelated `EmailBody` issue above).
- Tests: `npx jest src/components/EmailAssociationsAndTracking` — 12/12 pass, incl. dark-mode (no console
  errors), additive-confirm-preserves-sibling, dismiss-preserves-siblings/suggestions, ambiguous-grouping,
  reply-shows-as-filed-automatically, tracking read/write, and the production-vs-stub negative check.

# Task 022 — G7 Save-Version / Save-New split-button + transient-key dedup — Deviations & Notes

> **Status**: ✅ COMPLETE · 2026-07-30 · FULL rigor · sonnet/high (run on Opus 4.8 session)
> **Approach**: Option B — durable Dataverse column `sprk_composetransientkey` + single-column alt-key
> `sprk_composetransientkey_uk` (operator-created 2026-07-30; gate cleared). Fixes the 8-duplicate defect.

## What shipped

**Server** (`ComposeService.cs` + `IComposeService.cs` + `ComposeEndpoints.cs`):
- `SaveComposeDocumentRequest` gained `TransientKey` (string?) + `ForkNew` (bool); `PromoteComposeDocumentRequest`
  gained `TransientKey`. Wire body `SaveComposeDocumentBody` gained `transientKey` + `forkNew`, threaded into
  BOTH the replace route (forwarded for symmetry) and the create-on-save route.
- `ComposeTransientKeyAttribute = "sprk_composetransientkey"` const.
- **Transient-create branch**: BEFORE minting an SPE item, if `!ForkNew && TransientKey present`,
  `TryFindDocumentByTransientKeyAsync` (RetrieveByAlternateKeyAsync on the alt-key) → a hit with a live SPE
  pointer takes the **replace-in-place** path (`ReplaceFileContentAsUserAsync`, no new mint, no new row);
  otherwise mint as before. `ForkNew` (Save New Document) skips the lookup → always mints + creates.
- **PromoteIfEphemeralAsync**: stamps `sprk_composetransientkey` onto the NEW row at create (idempotent
  existing-row branch ignores it — set once). The concurrency-race `catch` now re-resolves by graph-item-id
  THEN transient-key, so two truly-concurrent first-saves of the same draft converge to ONE record (loser's
  minted item is orphaned — a rare edge, never a duplicate ROW).
- New private helper `TryFindDocumentByTransientKeyAsync` + private record `TransientKeyMatch(RecordId, SpeId,
  DriveId)`. Mirrors `TryFindDocumentByGraphItemIdAsync` exactly.

**Client** (`compose-contracts.ts`, `ComposeWorkspace.tsx`, `ComposeWorkspace.types.ts`, `ComposeEditor.tsx`,
`ComposeFormatToolbar.tsx`):
- New `ComposeSaveMode = 'version' | 'new'` contract type; `ComposeDocumentRef.transientKey?`.
- `mintTransientKey()` helper (crypto.randomUUID()-preferring, like `mintDocumentSessionId`). Minted ONCE per
  transient mount at all 5 mount dispatch sites (Browse, assistant-upload, blank/template, inline draft,
  ledger-resolved draft) → carried on `documentRef.transientKey` (mountTransient + mountDraftHtml reducers).
- `triggerSave(saveMode = 'version')`: `forkNew = saveMode === 'new'`; `isTransientCreate = forkNew ||
  !speDriveItemId`; `effectiveTransientKey = forkNew ? mintTransientKey() : documentRef.transientKey`. The
  create-on-save body sends `transientKey` + `forkNew`.
- Toolbar Save button → Fluent v9 **SplitButton**: primary "Save Version" (`onSave('version')`) + caret-menu
  "Save New Document" (`onSave('new')`). Mirrors the blessed `ComposerActionBar` Send split-button. Theme
  tokens only (ADR-021 dark mode). `onSave` signature `(mode?) => void` threaded Workspace→Editor→Toolbar;
  Ctrl+S / cross-pane bridge default to 'version'. The Word-menu duplicate now offers both Save-Version +
  Save-New for parity.

**Tests**:
- `tests/integration/seam/Compose/ComposeTransientKeyDedupSeamTests.cs` (3 through-the-wire slices):
  (A) Save-Version repeated same-key → replace in place, ONE record; (B) Save-New (forkNew) → skips dedup,
  forks a new record even when a matching row exists; (C) **8-duplicate** → 8 saves = 1 mint + 1 record +
  7 replaces. **3/3 green.**
- `ComposeFormatToolbar.test.tsx`: rewrote the 2 Save-button tests into 5 split-button tests (render /
  primary-fires-'version' / menu-fires-'new' / disabled-state / dark-mode). **39/39 green.**

## Verification
- New seam **3/3**; full Compose C# suite **810/810** (807 baseline + 3 — R4.5 numbering/citation/projection
  seams all green, non-regression confirmed).
- Byte-diff corpus **24/24** (NFR-01) + I-7 write-path text-search audit green (dedup resolves by KEY, never
  content — NFR-02).
- Toolbar UI **39/39** (jest; the worktree `@spaarke/*` workspace-resolution limitation from tasks 020/021
  does NOT affect ComposeFormatToolbar.test.tsx — it imports no `@spaarke/*` sibling; the binding DoD remains
  the C# server seam regardless).
- Publish **48.13 MB compressed** incl PDBs (task-021 baseline 46.75; +1.38 MB build/compression variance —
  **zero new runtime package**; ≤60 ceiling, < +5 escalation threshold). BFF build 0 errors.
- ArchTests: same **3 pre-existing failures** (ADR-010 ×2 + ADR-007) proven in task 021 — zero new violations.
- Client typecheck: only the known pre-existing `@spaarke/*`-unlinked cascades remain (identical on master);
  zero new type errors from these changes.

## Decisions / deviations
1. **POML step "reuse R4.5 WS-1 transient-mount projection identity" was NOT viable** — the escalation trigger
   fired (investigation 2026-07-30 found R4.5 WS-1 built a stateless projection reader, NOT a reusable record
   identity). Operator chose **Option B (durable Dataverse column)** as the most robust fix. This note + the
   POML `<steps mode="directional">` sanction adapting the step. Schema spec + impl plan:
   `notes/g7-transient-key-schema.md`.
2. **Save-New fork content shape (bounded)**: the fork reuses the existing transient-create requestBody shape
   (`bornInEditorRender ? contentModel : content`). A fork of a LOADED+edited imported doc therefore re-authors
   via contentModel (authored render) rather than applying the op-log tracked onto the fork. This matches the
   existing transient path's established edited-doc behavior; the PRIMARY fork scenario (a transient / born-in-
   editor draft) is exact. High-fidelity op-log-onto-fork for a loaded doc is a possible G7 follow-up if UAT
   wants it — not required by the acceptance criteria.
3. **Concurrency race** on two truly-concurrent first-saves of the same key: the alt-key unique constraint
   makes the loser's create fail; the catch re-resolves by transient key → ONE record. The loser's minted SPE
   item is orphaned (rare edge; acceptance criterion is "no duplicate RECORD", satisfied).

## Step 9.5 quality gates (applied)
- **code-review**: no security issue (transient key is a client UUID, not a secret; alt-key lookup is
  parameterized; logs carry ids not content). Correctness verified via the 3 seam slices + 810 suite. No AI
  code smells introduced.
- **adr-check**: ADR-049 (engine byte contract untouched; I-7 by-key), ADR-007 (SpeFileStore facade only),
  ADR-013 (no AI type), ADR-038 (seam DoD), ADR-021 (Fluent v9 dark mode), ADR-010/§11 (no new interface),
  §10 (routing stays in Services/Compose, ≤60 MB, no new package) — all clean.

## PR obligations
- **Placement Justification (§10)**: create-vs-replace dedup is routing logic on the existing `ComposeService`
  save orchestration + one new durable Dataverse column read/write — no new service, endpoint family, or
  library (§11 N/A — extends the existing Save button + save path). Engine stays `byte[]`-in/out; no AI/Graph type.
- `/conflict-check` run before the BFF PR (soft-warn only: analysis-hub-r1 #694 shares Spaarke.Compose.Components,
  project-init, low overlap; NFR-09 non-regression covered by the 810 suite). Watch #266 OpenXml on PR.

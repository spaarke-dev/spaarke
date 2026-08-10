# Task 053 — Browse shell + one normalized reader — COMPLETE (2026-08-07)

**Rigor**: FULL · opus·xhigh · directional. **Model**: Opus 4.8 (tier satisfied).
**Result**: shared-lib code + tests green (tsc 0, jest 23 suites / 168 tests). Step 9.5 code-review + adr-check clean (one documented, owner-approved ADR-050 deviation). Conflict-check clean (email-communication-solution-r5 has zero edits to the touched files).

## What shipped

- **`ReconciliationBrowseShell`** (NEW — `components/ReconciliationBrowseShell/`): the Pillar E reconciliation browse shell. Steps **N-of-M** through the Needs-review queue and hosts a **two-pane body**: LEFT reader + RIGHT `renderTabs` slot.
- **`EmailBodyView` attachment-text folding** (EXTENDED, additive): new optional `attachments?: ReconciliationAttachmentContent[]` + `onOpenOriginal?` props. Folds each attachment's normalized extracted text into the reader **as readable text below the body** (NFR-11 — one continuous surface over body + attachment text). Backward-compatible: omitted ⇒ no fold (every existing caller, incl. `EmailWorkspace`, unaffected).
- **Barrels**: `EmailBody/index.ts` re-exports `ReconciliationAttachmentContent`; `components/index.ts` re-exports `ReconciliationBrowseShell`.
- **Tests** (NEW): `ReconciliationBrowseShell.test.tsx` — the closed acceptance set (6 tests, all pass).

## Escalation trigger #1 FIRED → owner-approved deviation (ADR-050 Path C)

The POML constraint said *"use the `BrowseModal` preset — the browse mechanism is DECIDED"* with an escalation trigger: *"if the `BrowseModal` preset cannot cleanly host a two-pane (reader + tabs) body without forking a modal, STOP and escalate."*

**Verified in code**: `BrowseModal` wraps its children in `PreviewGridBody` — a fixed `1fr` stage + `320px` label/value **metadata** grid (`presets/PreviewModal.tsx:16-45`). That is a **file-preview** layout; the 320px meta column renders `{label,value}` strings only and structurally cannot host the three interactive reconcile tabs, and is too narrow for a reconcile workspace. So the preset cannot cleanly host the required two-pane reader+tabs body.

**Resolution (owner-approved 2026-08-07 via AskUserQuestion — "SprkModal + nav directly (Recommended)")**: build the shell on **`SprkModal` + its `nav` contract** (`@spaarke/ui-components`). `SprkModal` exposes `nav?: SprkModalNav` (the identical "N of M" prev/next counter, rendered in its header) and accepts **arbitrary children** (`SprkModal.types.ts:42-55`). `BrowseModal` is *literally* `SprkModal` + `nav` + `PreviewGridBody`, so using `SprkModal` + `nav` is the **exact same canonical shell + exact same browse mechanism the preset is built on**, minus the preview grid — a proper two-pane body renders directly.

**ADR-050 stays fully satisfied**: this is the canonical modal shell + canonical nav; it is **NOT** a hand-rolled modal and **NOT** `RecordNavigationModalShell`. This is a deviation from the POML's *literal* "BrowseModal preset" wording, not from ADR-050. §6.5 Path C (pivot to comply with the governing ADR via its correct primitive).

## Reader composition (ADR-045 reuse — no second reader)

Left pane embeds the reused **`EmailReadingPaneShell` in `hideList`** (per-record form) mode, keyed by record id so navigation remounts + re-binds it. Its `renderBody` composes `EmailRecipients` + the subject + the attachment-folding `EmailBodyView`. Right pane = `renderTabs(record, index)` slot (placeholder until 052/055/056/057). **Open original**: an attachment fold's "Open original" link opens a `PreviewModal` overlay hosting the reused `AttachmentList` for that attachment; activating the row fires `onOpenOriginalActivate` (host opens the raw file).

## Contract for downstream tasks

- **054** (citation navigation) anchors proposal citations into the **`EmailBodyView` normalized text** (body + folded attachment `text`). The fold text is `pre-wrap`, stable, and addressable per `attachmentId` — keep it stable.
- **052 / 055 / 056 / 057** fill `ReconciliationBrowseShell.renderTabs` (Related to / Fields / Tasks / routing).
- **Host wiring (059 / code-page + widget)**: the task-050 grid's `onRecordOpen` override opens the shell at the clicked row's `initialIndex` over the same `queue` order the grid shows. The host resolves each `ReconciliationBrowseRecord` (subject/recipients/`emlDocumentId`/`body`/normalized attachment `text`) — the shell is presentational (ADR-012), it does not fetch.

## Deferred / operator-gated (not part of this task)

- Live dual-mount + visual dark-mode contrast is **jsdom-verified only** here — worth a browser pass at Pillar E deploy (059).
- Attachment **normalized-text resolution** (the host-side extraction feed into `ReconciliationBrowseRecord.attachments[].text`) is a host/BFF concern wired at 059; this task ships the presentational fold + the contract.

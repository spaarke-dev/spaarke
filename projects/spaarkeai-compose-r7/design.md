# Spaarke Compose R7 — Editor UX: Save/Save-As, Draft-Safe Autosave, PDF-Import Parity, Editor Hotkeys & Save-Identity Fix

> **Project**: `spaarkeai-compose-r7`
> **Created**: 2026-08-13 · **Updated**: 2026-08-13 (post owner Q&A) · **Author**: Ralph Schroeder + Claude (Opus 4.8)
> **Status**: DESIGN (hand-authored input to `/design-to-spec` → `/project-pipeline`)
> **Type**: Compose **editor UX** + save-identity bug fix + PDF-import wiring. **Not** an AI-capability project.
> **Governing ADR**: [ADR-049 Compose Shadow Document](../../.claude/adr/ADR-049-compose-shadow-document.md) (save path — R6 render-on-save amendment); [ADR-050 Canonical Modal Shell](../../.claude/adr/ADR-050-canonical-modal-shell.md) (name/save modals); surface interplay per [ASSISTANT-SURFACE-LAUNCH-MECHANISM.md](../../docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md).
> **Relationship to R6**: R6 re-architected the *save engine* (render-on-save), **PDF intake engine**, and version-history open UX. **R7 is the editor-UX layer above it** — how the user triggers save, names documents, is protected from data loss, imports PDFs, and reaches AI tools by keyboard. R7 rides on R6's engine; it does not re-open the save-engine debate.
> **Split-out**: the original UC-1 "Templates" use case moved to a dedicated project **[`spaarkeai-compose-templates-r8`](../spaarkeai-compose-templates-r8/design.md)** (template storage + merge + picker + email templates is its own subsystem, not editor UX).

---

## 1. Why this project exists

The Compose editor works but has six concrete UX/robustness gaps that make it feel unfinished versus a real
word processor, plus one data-integrity bug and one import-parity gap surfaced during R6 UAT:

1. **Save is a two-mode split-button that invites a data-integrity bug** — the "Save New Document" fork
   reuses the filename and skips dedup, so it silently versions the original *and* spawns duplicate
   `sprk_document` records (R6 defect **D1**). Even ordinary saves can duplicate when the widget re-mounts.
2. **New documents are silently named `Untitled document.docx`** — the user is never asked for a name
   before the file lands in SharePoint Embedded (SPE).
3. **No autosave and no data-loss protection** — the workspace has *zero* autosave today (a self-imposed
   invariant). A closed modal or browser navigation loses unsaved work — the owner's explicit priority:
   *losing work is far worse than an unnecessary save.*
4. **PDF cannot be imported into an editable Compose document** — you can upload a PDF to the Assistant to
   summarize, but it never becomes an editable Compose doc (no parity with `.docx`). R6 built the engine
   but wired it to only one of three doors.
5. **AI editing requires a text selection** — "Describe a change" only opens from the selection BubbleMenu;
   there is no keyboard path to invoke it at the caret.
6. **No keyboard path into the Assistant** — the user must mouse to the chat pane and click.

All are **editor-surface** concerns. None require new AI capability, playbooks, or (after the r8 split) new
template infrastructure.

---

## 2. Owner decisions (2026-08-13 Q&A) — firm

- **D1 — Save semantics.** SPE versioning is intrinsic (every save appends a version — R6's safety net
  depends on append-only). So the tool is just **Save** and **Save As** — no separate "Save new version."
- **D2 — Autosave.** Reverse the "no autosave" invariant. **Autosave ON by default** with a toggle; use a
  **lightweight draft model** so periodic autosaves do NOT explode SPE version history (see §4 UC-4).
- **D3 — Hotkeys locked (must be easy to activate):** **`Ctrl+Space`** → "Describe a change" at the caret;
  **`Ctrl+Shift+Space`** → focus the Assistant chat input.
- **D4 — Templates → out of R7**, moved to **`spaarkeai-compose-templates-r8`** (storage + merge + picker +
  email templates). R7's empty-state "Open template" stays as-is until r8 re-points it.
- **D5 — Email composer stays Lexical** (no TipTap for email). Email templates handled in r8.
- **D6 — PDF import is in R7** (parity with Word import); **PDF export/save is OUT** (deferred to a future
  project — can be done in the Word app for now).
- **D7 — Fidelity wideners are OUT** of R7 and r8 (deferred to a future project).
- **D8 — R7 is the sole project directly working on Compose.** Non-Compose SpaarkeAi code-page changes go
  to a separate code-page-focused project.

---

## 3. Scope — use cases

### UC-2 — Save tool = **Save** + **Save As** (dropdown), with Auto Save toggle
Replace the two-mode split-button (`ComposeFormatToolbar.tsx:986-1018`) with a dropdown:
- **Save** — persist the current document (appends an SPE version; same document/file).
- **Save As** — create a **copy** as a **new document** (today's `'new'`/create-on-save path) — **must
  uniquify the filename** (see UC-8 / D1 fix) so it is a real fork, not a silent re-version of the original.
- **Auto Save** — a toggle in the menu (state also shown in the toolbar per UC-4).

### UC-3 — Name prompt when saving a new document
On first save of a new document (create-on-save), show a **small modal** (`FormModal`/`SprkModal`, ADR-050)
to set **document name + file name** (as stored in SPE). Removes the silent `Untitled document.docx`
default (`ComposeWorkspace.tsx:3018/3022`). Applies to first Save and to Save As.

### UC-4 — Draft-safe autosave (ON by default) + toolbar status + data-loss protection
The owner's priority is **never lose work**. Deliver:
- **Autosave ON by default**, toggleable; persists **only when dirty**.
- **Draft model (resolves the version-explosion tension):** autosave writes a **lightweight draft**, not a
  new SPE version. Explicit **Save** (and a longer idle/interval checkpoint) promotes to a real SPE version.
  This is the standard word-processor model (autosave ≠ a named version per tick) and keeps version history
  meaningful while honoring append-only SPE.
- **`beforeunload` / modal-close guard** — warn on unsaved changes (catches the closed-modal / browser-nav
  loss the owner named).
- **Local draft recovery** — persist editor state to local/session storage between saves so a crash or
  accidental close is recoverable.
- **Save-state indicator in the toolbar** — "Saving… / Saved / Unsaved" + "Auto Save On/Off" (this absorbs
  R6 defect **D6**, "no saved indicator").
- New documents must be **named first** (UC-3) before a server draft/save fires; until then, local draft
  recovery still protects the work.

### UC-5 — `Ctrl+Space` opens "Describe a change" at the caret (no selection)
Register a Compose hotkey so **Ctrl+Space** opens the **"Describe a change"** instruction dialog **without
requiring a selection** — applies to the current cursor position / paragraph. Reuse the existing
`promptForInstruction` dialog + the collapsed-cursor `forceVisible` path already used by right-click
(`ComposeAiToolbar.tsx`). Guard against IME/composition (`event.isComposing`; Ctrl+Space is an IME toggle on
some stacks) — fall back to `Ctrl+/` if testing shows conflicts.

### UC-6 — `Ctrl+Shift+Space` focuses the Assistant chat input
Move focus **into the Assistant chat box** from the editor. Requires (a) a **focus API** on `SprkChatInput`
(today exposes only `triggerSlashMode()`), and (b) a **cross-pane signal** (editor is in a workspace tab,
chat is in `ConversationPane`) via **PaneEventBus**. Add a tooltip/shortcut hint for discoverability.

### UC-7 — **PDF import parity** (open a PDF as an editable Compose document)
R6 built the full PDF → Azure DI layout → canonical `ComposeContentModel` → synthesized docx → TipTap path
(`ComposePdfModelProjector`, `ComposeService.ProjectPdfToDocxAsync`), but wired it into **only**
`LoadAsync` (open existing SPE document). Bring the two user-facing doors to parity with `.docx`:
- **Server:** give **`ComposeService.ProjectForMount`** (`ComposeService.cs:305-323`) — the helper behind
  both the Browse (`/api/compose/project`) and Assistant-upload (`/api/compose/upload`) doors — the same
  **`IsPdfSource` → `ProjectPdfToDocxAsync`** fork `LoadAsync` already has. This makes `ProjectForMount`
  **async** (PDF intake calls Azure DI) — a contract change to note.
- **Client:** admit `.pdf` in the Browse `accept` filter (`ComposeWorkspace.tsx:3596`) and in the
  `NON_DOCX_EXTENSION` / reference-only gate (`ComposeEditor.tsx:267,278`) **for the intake doors** (still
  route un-intakeable content to reference-only).
- **Env/DI:** the PDF path requires the compound gate `Analysis:Enabled && DocumentIntelligence:Enabled`
  (else `NullComposePdfIntakeSource` → typed "PDF intake unavailable"). Verify enabled in target env.
- **Acceptance = parity:** an uploaded/browsed PDF opens as an **editable** Compose doc, can run NDA
  analysis / create a response, and saves as a docx version — same as `.docx` (loud degradation warnings
  from the projector are expected and acceptable).

### UC-8 — **Save-identity fix** (R6 defect D1 — data integrity)
Eliminate duplicate-`sprk_document` creation across **all three vectors** identified in R6 UAT + the R7
client trace:
1. **Fork reuses filename** → Graph PUT-by-path coalesces onto the existing file → new record, same file.
   Fix: **Save As uniquifies the filename** (UC-2) so a fork is a real fork.
2. **Re-mount rotates the transient key + resets `speDriveItemId`** → a normal Save becomes a fresh
   create-on-save each time. Fix: **persist document identity across re-mounts** (don't reset id/key on
   re-mount of the same logical document).
3. **Id-less/key-less mount fallback** (assistant-insert path builds `{ speDriveItemId: '' }`, no transient
   key) → dedup skipped entirely. Fix: **always carry a dedup identity** on mount.
4. **Server upsert guard** (R6 D1c) — upsert `sprk_document` on the `sprk_graphitemid` alt-key so no door
   can create a duplicate record for the same drive-item (thin BFF change; closes the hole for all paths).

### Accepted defer adds (R6 register → R7, tightly coupled)
- **D8 (R6)** — "Blank page" mounts **non-editable** while "Open template" is editable (same
  `mountBornInEditor` path, empty-seed-specific bug, `ComposeEditor.tsx:2252/2155`). Fix in R7 (UC-1's r8
  move does NOT cover Blank page).
- **D4 (R6)** — "Restore from Source" blanks the page + asks for re-upload (mount-state reset on the
  transient/upload path — same lifecycle as UC-8 vector 2).
- **D7 (R6)** — "Add Comment" toolbar affordance (comment machinery shipped + seam-proven; only the UI
  entry point is missing).
- **Data hygiene** — delete the 5 duplicate dev `sprk_document` records (keep newest).

### Candidate adds (small; confirm in `/design-to-spec`)
apply-template **ETag/If-Match** + ApiError-404 branch (same files UC touches) · version-viewer polish
(popup-blocker fallback, blob revoke, "Current" badge) · **FakeTimeProvider flake** + pre-existing jest
suite repair (R7 is the next owning project for this surface) · PDF-intake facade cause-discrimination
(LOW-10).

---

## 4. Goals / Non-goals

**Goals**
- A **Save / Save As** dropdown with an Auto Save toggle; Save As is a *real* fork (uniquified filename).
- A **name/file-name modal** on first save (no silent `Untitled document.docx`).
- **Draft-safe autosave ON by default**, with `beforeunload` guard, local draft recovery, and a toolbar
  **save-state indicator** — the owner's "never lose work" priority.
- **PDF import parity** — a PDF opens as an editable Compose document across the Browse and upload doors.
- **`Ctrl+Space`** → "Describe a change" (no selection); **`Ctrl+Shift+Space`** → focus Assistant.
- **Save-identity fix** — no duplicate `sprk_document` records from any door.
- Accepted R6 defers: Blank-page-editable (D8), Restore-from-Source (D4), Add-Comment (D7).

**Non-goals**
- Templates / template storage / picker / email templates → **`spaarkeai-compose-templates-r8`**.
- **PDF export / save-as-PDF** → deferred to a future project (do it in Word for now).
- **Fidelity wideners** (indentation/paragraph-style/section-break survival) → deferred to a future project.
- Re-architecting the save engine (R6 render-on-save) or the PDF intake engine (R6) — R7 *wires + fronts* them.
- TipTap for the email composer (stays Lexical).
- Non-Compose SpaarkeAi code-page changes (D9 viewport clipping etc.) → separate code-page project.
- AI-suggestion pipeline bugs (D2 curly-quote mangling, D3) → AI-platform/assistant surface.

---

## 5. Constraints

- **BFF Hygiene (root §10):** R7 touches the BFF for UC-7 (async `ProjectForMount` PDF fork), UC-8 (upsert
  guard + create-on-save name threading), and possibly a "Save (overwrite)"-vs-"Save As" plumbing. Each
  needs a **Placement Justification** + **publish-size verification** (≤60 MB compressed; report absolute +
  delta vs baseline ~46.94 MB per R6 task 014) + no new HIGH CVE. New work stays in `Services/Compose/` and
  reuses R6's PDF projector/intake — no new subsystem.
- **Component Justification (root §11):** reuse `SprkModal`/`FormModal` (ADR-050), the existing `triggerSave`
  path, `QuickStartModal` scaffold, `promptForInstruction`, and R6's `ComposePdfModelProjector` /
  `ProjectPdfToDocxAsync`. Do **not** add parallel modals or save/intake mechanisms.
- **Coordination — R7 is the sole Compose owner (D8).** The old "most-contested / `parallel-safe:false`"
  pressure on `Services/Compose/` + `ComposeWorkspace.tsx` largely lifts. Still: run **`/conflict-check`
  before BFF PRs**, deploy **BFF + `sprk_spaarkeai` together** (anti-clobber verify), and coordinate any
  shared **SpaarkeAi code-page** edits with the other AI-app projects. **Sequence r8 after R7** (r8's
  new-from-template depends on R7's name-on-save modal + shared `ComposeEmptyState`/`ComposeWorkspace`).
- **NEVER delete `docxBridge.ts`.**
- Commit `--no-verify`; co-author trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

---

## 6. Key files / fault lines (grounded starting map)

**Client — `src/client/shared/Spaarke.Compose.Components/src/widgets/`**
- `ComposeFormatToolbar.tsx` — Save split-button (`986-1018`). **UC-2 dropdown + UC-4 Auto Save toggle/indicator.**
- `ComposeWorkspace.tsx` — `triggerSave(mode)` (`1346`), replace-vs-create discriminator (`1355/1367/1370`),
  response persist-back (`1747-1767`), `mountBornInEditor` + `'Untitled document.docx'` (`3002/3018/3022`),
  window Ctrl+S (`2028-2036`), documented "no autosave" invariant (`34`, `2789-2791`), Browse handler
  (`2856-2963`) + `accept=".docx"` (`3594-3596`), id-less mount fallback (`199`), mount sites that mint
  fresh transient keys (`2962/3011/3289/3353`). **UC-3 name modal, UC-4 autosave/draft/indicator, UC-7
  client PDF gate, UC-8 mount-identity fixes.**
- `ComposeWorkspace.types.ts` — `saveSucceeded` persist-back (`708-715`), `mountTransient` (`559`),
  `mountDraftHtml` (`614`), `loadSucceeded` (`507-516`). **UC-8 identity persistence.**
- `ComposeEditor.tsx` — `editorProps.handleKeyDown` (`2218-2229`; Ctrl+F/Escape today), `promptForInstruction`
  dialog (`2557-2577`, `3491-3556`), `isEditableDocx` (`278-281`) + `NON_DOCX_EXTENSION` (`267`),
  empty-seed non-editable bug (`2252/2155`). **UC-5 + UC-6 hotkeys, UC-7 client gate, D8 blank-page fix.**
- `ComposeAiToolbar.tsx` — `'compose-rewrite-instruction'` = "Describe a change" (`400-414`); collapsed
  guard (`748`); `forceVisible` (`877`). **UC-5 collapsed-cursor path.**
- `types/compose-contracts.ts` — `ComposeSaveMode = 'version' | 'new'`. **UC-2 map to Save / Save As.**

**Client — SpaarkeAi + chat**
- `.../SprkChat/SprkChatInput.tsx` — `forwardRef` (`120`), `useImperativeHandle` = `triggerSlashMode()` only
  (`257-263`). **UC-6: add `focusInput()`.** · `SprkChat.tsx` `inputHandleRef` (`763`) · `types.ts`
  `ISprkChatInputHandle` (`1513`).
- `.../conversation/ConversationPane.tsx` — PaneEventBus + host→send bridge (`745-747`, `818`). **UC-6 focus event.**

**Server — `src/server/api/Sprk.Bff.Api/`**
- `Services/Compose/ComposeService.cs` — `ProjectForMount` (`305-323`, **UC-7 add PDF fork, make async**),
  `IsPdfSource` (`793`), `ProjectPdfToDocxAsync` (`819-871`), `LoadAsync` PDF branch (`502-539`), `SaveAsync`
  (`1009`) + create-on-save name + `PromoteIfEphemeralAsync` upsert (`2505`, **UC-8 upsert guard**),
  transient-key dedup (`3381-3413`).
- `Services/Compose/ComposePdfModelProjector.cs` (`50`) + `Services/Ai/ComposePdfIntakeSource.cs` — R6 PDF
  engine (reuse). DI gate `AnalysisServicesModule.cs:211-212/472-473`.
- `Api/ComposeEndpoints.cs` — `/compose/project` (`76/1092`), `/compose/upload` (`942/1036`),
  `/documents/{id}` `Load` (`87/1245`), `/{id}/save` (`98`), `/create-on-save` (`137`). **UC-7 + UC-8.**

---

## 7. ADR / architecture tensions (surface now per root §6.5)

- **"No autosave" invariant → documented reversal (Path A).** `ComposeWorkspace.tsx:34` declares no autosave
  by design; the owner has reversed this priority (data loss ≫ extra save). R7 introduces **autosave ON by
  default** with a draft model. Update the invariant comment + the `ComposeWorkspace.unmountFlush` test
  (which asserts "no POST without explicit save") to reflect the new intended behavior — not a regression.
- **Save-vs-version semantics.** Every save appends a version (R6 append-only). The **draft model** (UC-4)
  is what keeps autosave from exploding version history — this is a coordination point with R6's version
  model (drafts are Compose-internal, not SPE versions). Confirm the draft-store mechanism in the spec.
- **`ProjectForMount` becomes async (UC-7).** It was deliberately synchronous/no-I/O per its ADR-007/013
  contract. Adding the Azure DI PDF fork makes it async — note the contract change + keep the docx path
  synchronous-fast where possible.

---

## 8. Phasing sketch (to be refined by `/project-pipeline`)

0. **Coordination gate:** `/conflict-check`; confirm R6-engine assumptions (draft store vs version model;
   PDF DI gate ON in target env); publish-size baseline.
1. **Save-identity fix (UC-8)** — mount-identity persistence + id/key-carrying fallback + Save As filename
   uniquify + server upsert guard; data-hygiene cleanup. *(Do first — it's the live data-integrity bug.)*
2. **Save dropdown (UC-2)** — Save / Save As + Auto Save toggle; map `ComposeSaveMode`.
3. **Name modal (UC-3)** — FormModal for document + file name on first create-on-save; thread name to BFF.
4. **Draft-safe autosave + data-loss protection + save-state indicator (UC-4)** — draft store, 15s dirty
   autosave, `beforeunload` guard, local recovery, toolbar indicator; update invariant + `unmountFlush` test.
5. **PDF import parity (UC-7)** — async `ProjectForMount` PDF fork; client accept/reference-only gates;
   env DI verify; parity acceptance (analysis + save-as-docx-version).
6. **Hotkeys (UC-5, UC-6)** — `Ctrl+Space` collapsed "Describe a change"; `Ctrl+Shift+Space` focus chat
   via PaneEventBus; discoverability hints.
7. **Accepted defers** — D8 Blank-page-editable, D4 Restore-from-Source, D7 Add-Comment (+ confirmed
   candidate adds).
8. **Wrap-up** — anti-clobber deploy (BFF + `sprk_spaarkeai`), test-diet, docs (Compose editor UX + PDF
   import + autosave/draft model).

---

## 9. Success criteria (closed set for spec authoring)

- The Save tool is a **dropdown** with **Save**, **Save As**, and an **Auto Save toggle**; **Save As
  produces a distinct new document** (uniquified filename) and never silently re-versions the original.
- Saving a **new** document prompts for **document name + file name**; the SPE record uses that name.
- **No door produces duplicate `sprk_document` records** for the same drive-item (all three D1 vectors +
  upsert guard closed) — verified by repeated saves + re-mount.
- With **Auto Save on** (default), a dirty document is protected every ~15s via a **draft** (no
  version-per-tick); explicit **Save** creates the SPE version; a `beforeunload`/modal-close on unsaved work
  **warns**; a crash/close is **recoverable** from local draft; the toolbar shows **Saving/Saved/Unsaved +
  Auto Save On/Off**.
- A **PDF** opened via Browse or Assistant-upload becomes an **editable** Compose document (parity with
  `.docx`) — runs analysis, creates a response, saves as a docx version.
- **Ctrl+Space** (no selection) opens "Describe a change"; **Ctrl+Shift+Space** focuses the Assistant input.
- **Blank page** mounts editable (D8); **Restore from Source** no longer blanks (D4); an **Add Comment**
  affordance exists (D7).
- Publish size ≤60 MB; no new HIGH CVE; placement/component justifications recorded; `/conflict-check` clean.

---

## 10. Next steps

1. Confirm the remaining small items: draft-store mechanism (UC-4), candidate adds batch (§3), and the D1
   data-hygiene cleanup owner.
2. Run **`/design-to-spec`** on this file → `spec.md`.
3. Run **`/project-pipeline`** → `plan.md` + `tasks/`; create worktree `spaarke-wt-spaarkeai-compose-r7`.
4. **Sequence r8 (templates) after R7.**

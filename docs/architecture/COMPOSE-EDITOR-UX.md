# Compose Editor UX Layer (Save · Autosave · Hotkeys · PDF Import · Save-Identity)

> **Status**: Shipped — `spaarkeai-compose-r7` (2026-08-17)
> **Scope**: The editor-UX layer that sits ABOVE R6's shipped save engine (ADR-049 render-on-save) and PDF-intake engine. How the user triggers save, names documents, is protected from data loss, imports PDFs, and reaches AI tools by keyboard. It does NOT re-architect the R6 engines — it wires and fronts them.
> **Companion docs**: [ADR-049](../../.claude/adr/ADR-049-compose-shadow-document.md) (save path — canonical) · [COMPOSE-READ-REFERENCE-FIDELITY.md](COMPOSE-READ-REFERENCE-FIDELITY.md) (read/reference) · [MODAL-DESIGN-SYSTEM.md](../standards/MODAL-DESIGN-SYSTEM.md) (name modal shell).
> **Code roots**: `src/client/shared/Spaarke.Compose.Components/src/widgets/**` (client), `src/server/api/Sprk.Bff.Api/Services/Compose/**` + `Api/ComposeEndpoints.cs` (BFF).

---

## 1. Save / Save As / Auto Save (FR-01/FR-02)

- **Toolbar**: `ComposeFormatToolbar.tsx` exposes a **Save** dropdown — **Save** (`onSave('version')` → append an SPE version to the same document/file) and **Save As** (`onSave('new')` → create a *real* fork with a **uniquified filename**) — plus an **Auto Save** toggle. The `ComposeSaveMode` enum is unchanged (`'version' | 'new'`); only labels/UX map.
- **Name-on-first-save** (`ComposeSaveNameDialog.tsx`, a `FormModal`/`SprkModal` preset per ADR-050): on first create-on-save AND on Save As, the user sets **document name + file name**. No path lands the silent `Untitled document.docx`. The client `sanitizeComposeName`/`deriveComposeFileName` mirror the server `ResolveFileName` rules (kept in sync — see §6).
- **Fork uniqueness**: `composeIdentity.ts:uniquifyForkFileName` derives a distinct filename so Graph PUT-by-path cannot coalesce a fork onto the original file (a save-identity vector — §4).

## 2. Draft-safe autosave — CLIENT-ONLY (FR-03)

- **Store**: `composeDraftStore.ts` — a best-effort, single-slot **localStorage** draft keyed by the stable logical identity (§4). **No BFF surface**: the draft path imports nothing network-related and never calls `triggerSave`. Only an explicit **Save** appends an SPE version (NFR-03 — no version-per-tick).
- **Cadence**: a ~15s dirty-only autosave effect in `ComposeWorkspace.tsx` (ref-mirror convention, `autoSaveEnabled`-gated); cleared on successful save.
- **Data-loss guard**: a `beforeunload` handler warns only when `hasUnsavedWork`; recovery-on-mount re-seeds the draft via the existing `mountDraftHtml` reducer path.
- **Indicator**: the toolbar shows Saving… / Saved / Unsaved + Auto Save On/Off (`aria-live`, `data-save-state`).
- **Documented ADR exception (Path A)**: this **flips the R6 "no autosave" invariant** — client-only autosave now exists; there is still NO automatic SERVER save. The invariant comments + the `unmountFlush` test docblock were reconciled to the new intended behavior.
- **Accepted limitation**: drafts are per-device (localStorage); a device switch does not carry the draft. Large-document drafts can exceed the ~5 MB localStorage cap and silently no-op — the indicator reflects `isDirty`, never a false "Saved", so crash-recovery for very large docs is best-effort (UAT note).

## 3. Editor hotkeys (FR-04/FR-05)

- **Ctrl+Space** (primary) / **Ctrl+/** (fallback) — opens the shipped "Describe a change" instruction dialog at the **current caret/paragraph** (no selection). Predicates live in `composeHotkeys.ts` (`matchesDescribeChangeHotkey`), **IME-guarded** (`event.isComposing` + legacy `keyCode === 229`). Wired in `ComposeEditor.tsx`'s `editorProps.handleKeyDown`; reuses `promptForInstruction` (no parallel dialog).
- **Ctrl+Shift+Space** — moves keyboard focus into the Assistant chat input across panes. Chain: `ComposeEditor` dispatches an **additive** PaneEventBus `conversation.focus_chat_input` discriminant (ADR-030) → `ConversationPane` relays it as a `focusInputSignal` nonce bump → `SprkChat` one-shot effect calls `SprkChatInput.focusInput()` (new imperative-handle method). The nonce is baselined at mount so focus is moved only on a *bump*, never on first render.
- **Disambiguation**: `matchesDescribeChangeHotkey` has a Shift-guard so Ctrl+Shift+Space does not also fire describe-change. Discoverability = `aria-keyshortcuts` on the editor textbox (ADR-012-safe; not a visible tooltip).

## 4. Save-identity — one document per drive-item (FR-07 / R6 D1)

A stable, non-rotating **logical identity** underpins both draft recovery (FR-03) and client dedup (FR-07). `getComposeLogicalIdentity(ref)` = `sprkDocumentId ?? speDriveItemId ?? composeLogicalId` (`compose-contracts.ts`); the localStorage-backed mint helper is `composeIdentity.ts`. All four duplicate-`sprk_document` vectors are closed:

1. **Save As uniquifies** the filename (a fork is a real fork).
2. **Identity persists across re-mounts** (mint sites no longer reset id/transient-key for the same logical document).
3. **Every mount carries a dedup identity** (the id-less assistant-insert door now carries one).
4. **Server atomic upsert** — `ComposeService.PromoteIfEphemeralAsync` uses `IGenericEntityService.UpsertAsync` (atomic on `sprk_graphitemid_uk`) instead of read-then-`CreateAsync`, so no door and no concurrent first-save creates a duplicate row.

## 5. PDF import parity (FR-06/FR-11) + blank-page / reload / comments (FR-08/09/10)

- **PDF parity**: `ProjectForMount` gained the same `IsPdfSource → ProjectPdfToDocxAsync` fork `LoadAsync` already had, so a PDF opened via Browse or Assistant-upload becomes an **editable** synthesized docx (analysis + response + save-as-docx, parity with `.docx`). Client `isEditableDocx` is `sourceFormat`-aware; the Browse `accept` filter admits `.pdf`.
  - **Documented ADR exception (Path A / NFR-04)**: making `ProjectForMount` async is an ADR-007/013 contract change (was deliberately synchronous/no-I/O). The docx path stays synchronous-fast; only the PDF branch awaits I/O.
  - **Env gate**: requires `Analysis:Enabled && DocumentIntelligence:Enabled` in the target env, else `NullComposePdfIntakeSource` returns a typed "PDF intake unavailable".
- **FR-11 cause discrimination**: `ComposePdfIntakeSource` returns a discriminated `PdfIntakeParseResult` (circuit-open / timeout / corrupt) via `Services/Ai/PublicContracts/`; `ComposeService` throws the cause-specific message; status map **Corrupt→422**, else **503**.
- **Blank page mounts editable** (FR-08/D8): confirmed already satisfied by the born-in-editor mount path; a regression guard locks it.
- **Reload from Source no longer blanks** (FR-09/D4): `loadSucceeded` now stamps `driveId` onto `documentRef` (mirroring `saveSucceeded`), so reload retains the drive and repopulates.
- **Add Comment** (FR-10/D7): a toolbar toggle in `ComposeFormatToolbar` re-exposes the shipped R6 comment machinery (`useComposeCommentThreads.createThread` seam) whose UI trigger had been removed.

## 6. Invariants & extension notes

- **NEVER delete `docxBridge.ts`** (NFR-06).
- **`ComposeSaveMode` stays `'version' | 'new'`** — map labels only.
- **Deploy BFF + `sprk_spaarkeai` together** (anti-clobber, NFR-05); never deploy the BFF from a net8 tree (→503 on the net10 runtime).
- **apply-template concurrency**: apply-template rides the same server-side concurrency model as save (no client-supplied If-Match). A residual read→write TOCTOU vs a concurrent sibling-tab save is rare + recoverable via SPE version history; server-side If-Match hardening is deferred (DEF-001 / GitHub #776).
- **Client↔server name rules** (`sanitizeComposeName` ↔ `ResolveFileName`) are a documented parallel-implementation pair; keep them in sync or the "Saved as:" preview drifts.
- **Fidelity wideners** (indentation/paragraph-style/section-break survival) are OUT of R7's UX scope — tracked for the `spaarkeai-compose-fidelity-wideners-r1` fast-follow (DEF-002 / GitHub #777).

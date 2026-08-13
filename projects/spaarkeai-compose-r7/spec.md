# Spaarke Compose R7 — Editor UX: Save/Save-As, Draft-Safe Autosave, PDF-Import Parity, Editor Hotkeys & Save-Identity Fix — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-08-13
> **Source**: `projects/spaarkeai-compose-r7/design.md` (+ `notes/r6-defer-register-consolidated.md`)
> **Type**: Compose editor-UX + data-integrity bug fix + PDF-import wiring. **Not** an AI-capability project.
> **Governing ADRs**: ADR-049 (Compose Shadow Document — save path, R6 render-on-save amendment) · ADR-050 (Canonical Modal Shell — name/save modals) · ADR-032 (Null-Object kill-switch — PDF-intake gate) · ADR-007/ADR-013 (`ProjectForMount` contract). Surface interplay per `ASSISTANT-SURFACE-LAUNCH-MECHANISM.md`.

## Executive Summary

The Compose editor works but has six UX/robustness gaps, one data-integrity bug (R6 D1 — duplicate `sprk_document` records), and one import-parity gap (PDF cannot become an editable Compose doc). R7 delivers the **editor-UX layer above R6's engines**: a Save / Save As dropdown with an Auto Save toggle, a name-on-first-save modal, draft-safe autosave with data-loss protection, PDF import parity across the Browse and Assistant-upload doors, two AI hotkeys, and a save-identity fix that closes duplicate-record creation from every door. R7 **wires and fronts** R6's save engine and PDF projector; it does not re-architect them.

## Scope

### In Scope
- **UC-2** — Replace the two-mode Save split-button with a **Save / Save As dropdown** + **Auto Save toggle**; Save As is a *real* fork (uniquified filename), not a silent re-version.
- **UC-3** — **Name/file-name modal** (`FormModal`/`SprkModal`, ADR-050) on first create-on-save and on Save As; removes the silent `Untitled document.docx`.
- **UC-4** — **Draft-safe autosave ON by default** (client-only local draft), `beforeunload`/modal-close guard, local draft recovery, and a **toolbar save-state indicator** (absorbs R6 D6).
- **UC-5** — **`Ctrl+Space`** opens "Describe a change" at the caret (no selection), IME-guarded, `Ctrl+/` fallback.
- **UC-6** — **`Ctrl+Shift+Space`** focuses the Assistant chat input (new `focusInput()` API + PaneEventBus signal).
- **UC-7** — **PDF import parity**: an uploaded/browsed PDF opens as an **editable** Compose doc (async `ProjectForMount` PDF fork + client intake-door gates).
- **UC-8** — **Save-identity fix** (R6 D1): close all four duplicate-`sprk_document` vectors, including the server upsert guard on `sprk_graphitemid_uk`.
- **Accepted R6 defers** — D8 Blank-page-editable, D4 Restore-from-Source no-blank, D7 Add-Comment affordance.
- **Accepted candidate adds** — LOW-10 PDF-intake cause discrimination; apply-template ETag/If-Match + ApiError-404 branch; test-hygiene batch (FakeTimeProvider flake, pre-existing jest suites, nda fixture paraId regen).

### Out of Scope
- Templates / template storage / picker / email templates → **`spaarkeai-compose-templates-r8`** (sequence r8 after R7).
- **PDF export / save-as-PDF** → deferred future project (use Word for now).
- **Fidelity wideners** (indentation/paragraph-style/section-break survival, ×84/×85 UAT volumes) → deferred future project (named fast-follow; do NOT let them rot as ledger entries).
- Re-architecting the R6 save engine (render-on-save) or PDF intake engine — R7 wires + fronts them.
- TipTap for the email composer (stays Lexical).
- Non-Compose SpaarkeAi code-page changes (D9 viewport clipping) → separate code-page project (already handed to assistant-enhancements-r3).
- AI-suggestion pipeline bugs (D2 curly-quote mangling, D3) → AI-platform/assistant surface.
- **D1 data-hygiene** (5 duplicate dev `sprk_document` records) → **accepted dev-data debt; not a task** (leave/age out).

### Affected Areas
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeFormatToolbar.tsx` — Save SplitButton @986–1018 → **UC-2 dropdown + UC-4 Auto Save toggle + save-state indicator**.
- `.../widgets/ComposeWorkspace.tsx` — `triggerSave`@1346 (`forkNew`@1355 / `isTransientCreate`@1367 / `effectiveTransientKey`@1370), `mountBornInEditor`@3002 + `'Untitled document.docx'`@3018/3022, window Ctrl+S @2029–2034, "no autosave" invariant @34 + @2789–2791, id-less fallback @199, mint sites @2962/3011/3289/3353 (+1113/3425), `accept=".docx"`@3596 → **UC-3, UC-4, UC-7 client gate, UC-8 mount identity**.
- `.../widgets/ComposeWorkspace.types.ts` — `saveSucceeded`/`mountTransient`/`mountDraftHtml`/`loadSucceeded` persist-back → **UC-8 identity persistence**.
- `.../widgets/ComposeEditor.tsx` — `handleKeyDown`@2218–2229, `promptForInstruction`@2568 + dialog ~3491–3556, `NON_DOCX_EXTENSION`@267 + `isEditableDocx`@278, empty-seed path @2252→2276 → **UC-5, UC-6, UC-7 client gate, D8**.
- `.../widgets/ComposeAiToolbar.tsx` — `compose-rewrite-instruction`@405, collapsed guard @748, `forceVisible`@877 → **UC-5 collapsed-cursor path**; **D7 Add-Comment affordance**.
- `.../types/compose-contracts.ts` — `ComposeSaveMode='version'|'new'`@426 → **UC-2 label mapping (enum unchanged)**.
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChatInput.tsx` (`useImperativeHandle`@257–263) + `SprkChat/types.ts` (`ISprkChatInputHandle`@1513) → **UC-6 add `focusInput()`**.
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` (host→send bridge @745–747) → **UC-6 focus event via PaneEventBus**.
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs` — `ProjectForMount`@305–323 (**UC-7: PDF fork, make async**), `PromoteIfEphemeralAsync`@2505 + `CreateAsync`@2717 (**UC-8: upsert guard on `sprk_graphitemid_uk`**), create-on-save name @1346.
- `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs` — `project`@76 / `upload`@55 / `create-on-save`@137 (**UC-7 + UC-8 name threading**).
- `.../Services/Ai/ComposePdfIntakeSource.cs` — **LOW-10 discriminated facade result** (reuse; do not re-build).
- Apply-template handler in `ComposeWorkspace.tsx` — **ETag/If-Match + ApiError-404** (confirm R7 still touches this path post-r8 split; else standalone hardening).

## Requirements

### Functional Requirements

1. **FR-01 (UC-2)** — Replace the Save SplitButton with a dropdown: **Save** (`'version'` — append SPE version, same document/file), **Save As** (`'new'` — create a copy as a new document; **must uniquify the filename**), and an **Auto Save toggle**. `ComposeSaveMode` enum unchanged; only labels/UX map. — **Acceptance**: toolbar shows Save + Save As + Auto Save; Save As produces a distinct new `sprk_document` (uniquified filename), never a silent re-version of the original.
2. **FR-02 (UC-3)** — On first save of a new document (create-on-save) **and** on Save As, show a small `FormModal`/`SprkModal` to set **document name + file name**; thread name to BFF `create-on-save` (`DisplayName`→`ResolveFileName`@1346). — **Acceptance**: no path lands `Untitled document.docx`; the SPE record uses the entered name.
3. **FR-03 (UC-4)** — **Draft-safe autosave ON by default**, toggleable, persists **only when dirty**, via a **client-only local/session-storage draft** (no server write, no SPE version per tick). The draft is **keyed by the shared stable logical document id** (see FR-07(b): `sprkDocumentId ?? speDriveItemId ?? persistedLogicalId`) so a re-mount/reload rehydrates the correct draft rather than orphaning it. Add: `beforeunload`/modal-close guard on unsaved work; local draft recovery on reopen/crash; **toolbar save-state indicator** (Saving… / Saved / Unsaved + Auto Save On/Off — absorbs D6). New docs must be **named first** (FR-02) before any explicit save; local draft still protects pre-name work. Update the "no autosave" invariant comment (@34, @2789–2791) and the `unmountFlush` test (which asserts "no POST without explicit save") to reflect the **new intended behavior** — not a regression. — **Acceptance**: dirty doc auto-drafts to local storage every ~15s; explicit **Save** creates the SPE version; `beforeunload`/modal-close on unsaved **warns**; a crash/close is **recoverable** from local draft; indicator reflects state. Device-switch loss is an accepted client-only limitation.
4. **FR-04 (UC-5)** — Register a Compose hotkey (`handleKeyDown`@2218–2229) so **`Ctrl+Space`** opens the **"Describe a change"** instruction dialog **without a selection** (current caret/paragraph). Reuse `promptForInstruction`@2568 + the collapsed-cursor `forceVisible`@877 path. Guard `event.isComposing`/IME (Ctrl+Space is an IME toggle on some stacks); fall back to `Ctrl+/` if testing shows conflicts. — **Acceptance**: with a collapsed cursor (no selection), Ctrl+Space opens "Describe a change"; IME composition is not hijacked.
5. **FR-05 (UC-6)** — **`Ctrl+Shift+Space`** moves focus into the Assistant chat input. Add **`focusInput()`** to `ISprkChatInputHandle`@1513 + `SprkChatInput` `useImperativeHandle`@257–263 (today exposes only `triggerSlashMode()`). Bridge editor→chat (different panes) via **PaneEventBus** through `ConversationPane` (host→send bridge @745–747). Add a tooltip/shortcut hint. — **Acceptance**: from the editor, Ctrl+Shift+Space focuses the chat input across panes.
6. **FR-06 (UC-7)** — **PDF import parity.** **Server**: give `ProjectForMount`@305–323 the same `IsPdfSource`→`ProjectPdfToDocxAsync` fork `LoadAsync` has (@502–508) — this makes `ProjectForMount` **async** (Azure DI call); keep the docx path synchronous-fast. **Client**: admit `.pdf` in the Browse `accept` filter (@3596) and the `NON_DOCX_EXTENSION`/reference-only gate (@267/@278) **for the intake doors only** (still route un-intakeable content to reference-only). **Env**: verify the compound DI gate `Analysis:Enabled && DocumentIntelligence:Enabled` (`AnalysisServicesModule.cs:154`) is ON in the target env (else `NullComposePdfIntakeSource`@472–473 → typed "PDF intake unavailable"). — **Acceptance = parity**: a PDF opened via Browse or Assistant-upload becomes an **editable** Compose doc, runs NDA analysis / creates a response, and saves as a docx version — same as `.docx` (loud projector degradation warnings are expected + acceptable).
7. **FR-07 (UC-8)** — **Save-identity fix** — close all four duplicate-`sprk_document` vectors: (a) **Save As uniquifies the filename** (FR-01) so a fork is a real fork (Graph PUT-by-path can't coalesce onto the existing file); (b) **introduce a stable logical document id that persists across re-mounts.** Confirmed: **no non-rotating id exists today** — `transientKey` is minted fresh at every transient/draft mount door (@2962/3011/3289/3347/3420/1113 via `mintTransientKey`@240) and is never persisted, and `speDriveItemId` is reset to `''` on transient mounts (`mountTransient` types@559 / `mountDraftHtml` types@613); `sprkDocumentId` (Dataverse `sprk_documentid`, contract@646) exists only *after* first-save promotion. FR-07(b) makes the logical id **non-rotating + persisted** — persist/rehydrate the minted `transientKey` (or add a dedicated `composeLogicalId` field to `ComposeDocumentRef`, contract@642–668) — so the same logical document keeps one identity across re-mount/reload. (c) **always carry a dedup identity on mount** (the id-less/key-less assistant-insert `{ speDriveItemId: '' }`@199 currently skips dedup entirely); (d) **server upsert guard** — `PromoteIfEphemeralAsync`@2505 currently does read-then-`CreateAsync`@2717 (no atomic upsert) on `sprk_graphitemid_uk`; make it an atomic **upsert** so no door and no concurrent first-save creates a duplicate row. **Shared identity:** the draft-recovery key (FR-03) and the client dedup key are the SAME stable id — `sprkDocumentId ?? speDriveItemId ?? persistedLogicalId`. — **Acceptance**: repeated saves + widget re-mount + id-less mount produce **exactly one** `sprk_document` per drive-item, and a re-mount rehydrates the correct local draft (FR-03).
8. **FR-08 (R6 D8)** — **Blank page mounts editable.** "Blank page" (`handleBlankRequested`, empty `'<p></p>'` seed @3018) currently mounts non-editable while "Open template" is editable — same `mountBornInEditor` path, empty-seed-specific (empty `initialHtml` skips the @2252 guard, falls through to `isEditableDocx` @2276 → reference-only). Fix so Blank page is editable. — **Acceptance**: Compose tab → Blank page mounts an editable document.
9. **FR-09 (R6 D4)** — **"Restore from Source" no longer blanks the page.** Root-cause the mount-state reset on the transient/upload path (same lifecycle as FR-07 vector b) and fix. — **Acceptance**: Restore from Source restores content without asking for re-upload.
10. **FR-10 (R6 D7)** — **Add Comment toolbar affordance.** Comment round-trip machinery shipped + seam-proven in R6 (024/026); only the UI entry point is missing. Add the affordance and wire to the existing machinery. — **Acceptance**: an Add Comment control exists and creates a comment via the shipped path.
11. **FR-11 (LOW-10)** — **PDF-intake cause discrimination.** Replace the collapsed single-message null boundary in `ComposePdfIntakeSource.cs` with a discriminated facade result (circuit-open vs timeout vs corrupt). — **Acceptance**: an intake failure surfaces a cause-specific message, not one collapsed "unavailable".
12. **FR-12 (apply-template hardening)** — **If-Match/ETag** on the apply-template replace (TOCTOU vs a concurrent sibling-tab save) + an **ApiError-typed 404 branch** replacing the dead `response.ok` idiom in the apply handler. — **Acceptance**: concurrent apply/save is ETag-guarded; a 404 is handled as a typed ApiError. *(task-create note: confirm R7 still touches this file post-r8 template split; if not, treat as standalone drive-by hardening.)*
13. **FR-13 (test-hygiene batch)** — Fix the **FakeTimeProvider flake** in `ComposeServiceCreateOnSaveTests` (pollutes every Compose suite run); repair the **pre-existing failing jest suites** (4× `ComposeWorkspace` "Element type is invalid" + `stepOperationInterceptor`, proven pre-existing via stash bisect — R7 is the next owning project); regenerate spec-invalid paraIds (≥0x80000000) in the `nda-interrupted-clauses.docx` fixture. — **Acceptance**: Compose jest + xUnit suites run green and non-flaky locally + CI.

### Non-Functional Requirements
- **NFR-01 (publish size)** — BFF-touching tasks (FR-06, FR-07, possibly FR-12) MUST verify compressed publish size ≤ **60 MB** and report absolute + delta vs the **~46.94 MB** R6 baseline (R6 task 014). ≥+5 MB single-task delta → explicit justification; ≥55 MB cumulative → architecture review; ≥60 MB → HARD STOP. Per root §10.
- **NFR-02 (CVE)** — No new HIGH-severity CVE from `dotnet list package --vulnerable --include-transitive` on BFF-touching tasks.
- **NFR-03 (no version-per-tick)** — Autosave MUST NOT create an SPE version per tick. Satisfied structurally by the **client-only draft** decision (drafts never reach the BFF; only explicit Save appends a version).
- **NFR-04 (`ProjectForMount` contract)** — Making `ProjectForMount` async (FR-06) is a documented contract change (was deliberately synchronous/no-I/O per ADR-007/013). Keep the docx path synchronous-fast; note the change in code + PR.
- **NFR-05 (coordination)** — Run `/conflict-check` before BFF PRs; deploy **BFF + `sprk_spaarkeai` together** (anti-clobber verify). R7 is the sole Compose owner (D8), but coordinate shared SpaarkeAi code-page edits with sibling AI-app projects. **Sequence r8 after R7.**
- **NFR-06 (never delete `docxBridge.ts`)** — Binding regardless of the transitional-op-log-path cleanup trigger.

## Technical Constraints

### Applicable ADRs
- **ADR-049** — Compose Shadow Document (save path; R6 render-on-save amendment). R7 rides the engine; append-only SPE versioning is intrinsic (justifies Save/Save As, not "Save new version").
- **ADR-050** — Canonical Modal Shell. UC-3 name modal uses `FormModal`/`SprkModal`; no parallel modal.
- **ADR-032** — Null-Object kill-switch. PDF intake is compound-gated with `NullComposePdfIntakeSource` fallback (already implemented; FR-06 verifies env only).
- **ADR-007 / ADR-013** — `ProjectForMount` I/O-free contract (NFR-04 tension).

### MUST Rules
- ✅ MUST reuse `SprkModal`/`FormModal`, the existing `triggerSave` path, `promptForInstruction`, `forceVisible`, and R6's `ComposePdfModelProjector` / `ProjectPdfToDocxAsync`.
- ✅ MUST keep `ComposeSaveMode` = `'version' | 'new'` (map labels only).
- ✅ MUST route un-intakeable browsed/uploaded content to reference-only (only `.pdf` intake-door admission changes).
- ✅ MUST make `PromoteIfEphemeralAsync` idempotent via an atomic upsert on `sprk_graphitemid_uk`.
- ❌ MUST NOT add a parallel save/intake/modal mechanism, a server-side draft store, or a new AI capability/playbook.
- ❌ MUST NOT delete `docxBridge.ts`.

### Existing Patterns to Follow
- Save engine + dedup: `ComposeService.cs` (`SaveAsync`@1009, transient-key dedup @3381–3413, promote @2505).
- PDF fork reference impl: `LoadAsync` PDF branch @502–508 (copy the fork into `ProjectForMount`).
- Modal: ADR-050 `FormModal` (`@spaarke/ui-components`).
- Cross-pane signalling: PaneEventBus (`ConversationPane` host→send bridge @745–747).

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration
```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>Y</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
**BFF Placement Justification**: All BFF work stays in `Services/Compose/` (FR-06 async PDF fork, FR-07 upsert guard, create-on-save name threading) and reuses R6's PDF projector/intake — **no new subsystem, no new service**. The ≤60 MB publish-size ceiling (NFR-01) applies per BFF-touching task. Cite `.claude/constraints/bff-extensions.md` in each BFF PR.

### New Components (§11 three-question gate)
R7 is overwhelmingly **modify-only**. The only genuinely new surface:

| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `focusInput()` on `ISprkChatInputHandle` | `SprkChat/types.ts:1513` (only `triggerSlashMode`); `SprkChatInput.tsx:257–263` | **Extend** the existing interface/handle — not a new component | Ctrl+Shift+Space (UC-6) cannot move focus into chat; no programmatic focus API exists today (confirmed) |
| PaneEventBus chat-focus event | Existing PaneEventBus (`ConversationPane:745–747`) | **Extend** — add one event type to the existing bus | Editor→chat focus can't cross the pane boundary; UC-6 has no transport |
| Client local-draft recovery store | No autosave/draft store exists (invariant @34) | **New** small client util (localStorage keyed by the shared stable logical id — FR-07(b)) | Unsaved work is lost on crash/close today — the owner's #1 priority |
| Stable logical document id (`composeLogicalId` or persisted `transientKey`) | `ComposeDocumentRef` (contract@642–668) has speDriveItemId/sprkDocumentId/transientKey — **none non-rotating pre-save** (confirmed) | **New** field/persistence — no existing field survives re-mount for an unsaved doc | Draft recovery (FR-03) + client dedup (FR-07) have no stable key across re-mount; drafts orphan + dupes recur |
| Toolbar save-state indicator | None (D6: "no saved indicator") | **New** small UI element in `ComposeFormatToolbar` | User has no feedback whether work is saved; blocks UC-4 UAT |
| Atomic upsert on `sprk_graphitemid_uk` | `PromoteIfEphemeralAsync:2505` (read-then-`CreateAsync`@2717) | **Modify** the existing promote path — not a new service | Concurrent/repeat first-saves create duplicate `sprk_document` rows (live D1 bug) |

All other work (Save dropdown, name modal, hotkeys, PDF fork, blank-page, restore-from-source, add-comment, LOW-10, apply-template hardening, test fixes) modifies existing files/surfaces — no §11 justification required.

## ADR Tensions (per CLAUDE.md §6.5)

| ADR / invariant | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| "No autosave" invariant (`ComposeWorkspace.tsx:34`, `:2789–2791`) | "there is NO autosave/debounce/flush-on-blur in this workspace" | Owner has reversed the priority (data loss ≫ extra save); R7 introduces autosave ON by default | **A (documented exception)** | Client-only draft model keeps SPE version history clean (NFR-03) while honoring "never lose work". Update the invariant comment + the `unmountFlush` test to the new intended behavior — a deliberate change, not a regression. |
| ADR-007 / ADR-013 `ProjectForMount` contract | `ProjectForMount` is deliberately synchronous / no-I/O | UC-7 PDF fork adds an Azure DI call → method becomes async | **A (documented exception)** | Parity with `LoadAsync` (which already has the fork) requires it; keep the docx path synchronous-fast; document the contract change in code + PR. |
| ADR-049 append-only versioning | Every save appends an SPE version | Autosave must not explode version history | **C (comply)** | Resolved by the client-only draft decision — autosave never reaches the BFF; only explicit Save appends. No ADR change needed. |

## Success Criteria
1. [ ] Save tool is a **dropdown** (Save + Save As + Auto Save toggle); **Save As produces a distinct new document** (uniquified filename), never a silent re-version. — Verify: save + save-as on the same doc; assert two distinct `sprk_document` rows + files.
2. [ ] Saving a **new** doc prompts for **document name + file name**; SPE record uses that name; no `Untitled document.docx`. — Verify: create-on-save + inspect SPE record name.
3. [ ] **No door produces duplicate `sprk_document` rows** for the same drive-item (all four D1 vectors + upsert guard). — Verify: repeated saves + re-mount + id-less mount → single row.
4. [ ] With **Auto Save on** (default), a dirty doc drafts to local storage ~every 15s (no version-per-tick); explicit **Save** creates the SPE version; `beforeunload`/modal-close on unsaved **warns**; crash/close is **recoverable**; toolbar shows Saving/Saved/Unsaved + Auto Save On/Off. — Verify: dirty-edit + close/crash simulation + reopen.
5. [ ] A **PDF** via Browse or Assistant-upload becomes an **editable** Compose doc (parity with `.docx`) — runs analysis, creates a response, saves as a docx version. — Verify: intake a real PDF end-to-end in an env with the DI gate ON.
6. [ ] **Ctrl+Space** (no selection) opens "Describe a change"; **Ctrl+Shift+Space** focuses the Assistant input; IME not hijacked. — Verify: manual UAT + IME check.
7. [ ] **Blank page** mounts editable (D8); **Restore from Source** no longer blanks (D4); an **Add Comment** affordance exists (D7). — Verify: manual UAT each.
8. [ ] LOW-10 cause discrimination + apply-template ETag/404 + test-hygiene batch green. — Verify: failure-path test + suite run.
9. [ ] Publish size ≤60 MB (report delta vs ~46.94 MB); no new HIGH CVE; placement/component justifications recorded; `/conflict-check` clean; BFF + `sprk_spaarkeai` deployed together. — Verify: publish measure + CVE scan + conflict-check.

## Dependencies

### Prerequisites
- R6 engine assumptions confirmed: append-only version model; PDF DI compound gate ON in the target env; publish-size baseline (~46.94 MB).
- `/conflict-check` clean before BFF PRs.

### External
- Azure Document Intelligence enabled in the target environment (FR-06).
- Atomic-BFF + `sprk_spaarkeai` deploy window (NFR-05).

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Draft store (UC-4) | Where does the lightweight autosave draft live? | **Client-only** (local/session storage); no server write until explicit Save | UC-4 touches **no BFF surface**; NFR-03 satisfied structurally; device-switch loss accepted |
| Candidate adds | Which R7-ADD? rows pull into R7? | D6 indicator (into UC-4), LOW-10, apply-template ETag+404, test-hygiene batch | Adds FR-11, FR-12, FR-13; D6 folded into FR-03 |
| D1 hygiene | How to handle the 5 duplicate dev records? | **Leave / defer** (accepted dev-data debt) | No cleanup task; noted in Out-of-Scope |
| Draft-identity key | What key identifies a draft across re-mounts? | **Introduce a stable logical id** (FR-07(b)); draft store + client dedup share `sprkDocumentId ?? speDriveItemId ?? persistedLogicalId` | Unifies FR-03 + FR-07 on one identity; confirmed no non-rotating id exists today so FR-07(b) must add it |
| Fidelity-wideners home | Where do the deferred wideners (×84/×85) live? | **Defer to R7 wrap-up** (090) — decide the named home when the fast-follow slate is clearer | Not an R7 blocker; wrap-up MUST name a home (Idea or project) so they don't rot as ledger entries |

## Assumptions
- **Autosave interval**: ~15s dirty (per design §8 phase 4); tunable — not a hard contract.
- **Ctrl+Space**: primary binding; `Ctrl+/` is the confirmed fallback if IME conflicts surface in testing.
- **apply-template file touch (FR-12)**: assumes R7 still touches the apply path after the r8 template split; if task-create finds it does not, FR-12 becomes standalone hardening on the same file.
- **"Save As" label**: replaces R6's "Save New Document" menu item (`onSave('new')`); enum value stays `'new'`.

## Unresolved Questions
*Both design-time open questions are now RESOLVED (see Owner Clarifications). None remain blocking.*

- [x] ~~Local-draft identity key~~ — **Resolved**: introduce a stable logical id in FR-07(b); FR-03 draft store + FR-07 client dedup share `sprkDocumentId ?? speDriveItemId ?? persistedLogicalId`. Confirmed against code — no non-rotating id exists today. Implementation choice (persist `transientKey` vs new `composeLogicalId` field) is left to the FR-07(b) task as a bounded design detail.
- [x] ~~Fidelity-wideners home~~ — **Resolved**: defer to R7 wrap-up (090). The wrap-up task MUST name a home (GitHub Idea or fast-follow project) for the deferred wideners (indentation ×84 / paragraph-style ×85 / section-break etc.) so they don't rot as ledger entries — carry the R6 defer-register §C evidence forward.

---
*AI-optimized specification. Original design: `projects/spaarkeai-compose-r7/design.md`.*

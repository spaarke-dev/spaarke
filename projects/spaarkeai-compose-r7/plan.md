# Project Plan: Spaarke Compose R7 — Editor UX

> **Last Updated**: 2026-08-13
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Deliver the Compose editor-UX layer above R6's shipped save/PDF engines — Save/Save-As, draft-safe autosave, name-on-save, PDF import parity, AI hotkeys — and fix the live duplicate-`sprk_document` data-integrity bug (R6 D1).

**Scope**:
- Save / Save As dropdown + Auto Save toggle (UC-2)
- Name/file-name modal on first save (UC-3)
- Draft-safe autosave (client-only) + data-loss protection + save-state indicator (UC-4)
- PDF import parity across Browse + Assistant-upload doors (UC-7)
- `Ctrl+Space` / `Ctrl+Shift+Space` hotkeys (UC-5/UC-6)
- Save-identity fix, four vectors + server upsert guard (UC-8)
- Accepted R6 defers (D8/D4/D7) + hardening batch (LOW-10, apply-template ETag/404, test-hygiene)

**Timeline**: ~8 phases | **Estimated Effort**: ~15–22 tasks (2–4 hr chunks); refined by task-create.

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-049** — Compose Shadow Document (save path; R6 render-on-save). Append-only SPE versioning is intrinsic → Save/Save As, not "Save new version".
- **ADR-050** — Canonical Modal Shell. UC-3 name modal uses `FormModal`/`SprkModal`; no parallel modal.
- **ADR-032** — Null-Object kill-switch. PDF intake compound-gated with `NullComposePdfIntakeSource` (already implemented; FR-06 verifies env).
- **ADR-007 / ADR-013** — `ProjectForMount` I/O-free contract (NFR-04 tension: made async for PDF fork).

**From Spec**:
- Autosave is **client-only** (local/session storage); no server write until explicit Save. UC-4 touches no BFF surface.
- Draft-recovery key + client dedup share ONE stable logical id (`sprkDocumentId ?? speDriveItemId ?? persistedLogicalId`) — FR-07(b) introduces it (none exists today).
- All BFF work stays in `Services/Compose/`; reuse R6 PDF projector/intake. Publish ≤60 MB (baseline ~44.96 MB (net10)). NEVER delete `docxBridge.ts`.
- Consume `Services/Ai/PublicContracts/` for FR-11 — no fork of `Services/Ai/`.

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Client-only draft store | Never-lose-work without version-per-tick | UC-4 has zero BFF footprint |
| Unified stable logical id | One identity for draft recovery + dedup | FR-03 + FR-07 share the key |
| Async `ProjectForMount` | PDF parity with `LoadAsync` | ADR-007/013 contract change (documented) |
| Atomic upsert on `sprk_graphitemid_uk` | Close read-then-create duplicate hole | Server dedup for all doors |

### Discovered Resources

**Applicable Skills**: `task-execute`, `code-review`, `adr-check`, `conflict-check`, `pcf-deploy`/`code-page-deploy`, `bff-deploy`, `test-diet`, `ui-test`.

**Knowledge / ADRs**: [ADR-049](../../.claude/adr/ADR-049-compose-shadow-document.md), [ADR-050](../../.claude/adr/ADR-050-canonical-modal-shell.md), [ADR-032](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md), ADR-007/013; [COMPOSE-READ-REFERENCE-FIDELITY.md](../../docs/architecture/COMPOSE-READ-REFERENCE-FIDELITY.md); [bff-extensions.md](../../.claude/constraints/bff-extensions.md); [MODAL-DESIGN-SYSTEM.md](../../docs/standards/MODAL-DESIGN-SYSTEM.md); [ASSISTANT-SURFACE-LAUNCH-MECHANISM.md](../../docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md).

**Reusable Code** (reference impls to copy — do not fork):
- `ComposeService.cs` `LoadAsync` PDF branch @502–508 → copy the fork into `ProjectForMount`.
- `ComposeService.cs` `SaveAsync`@1009, transient-key dedup @3381–3413, `PromoteIfEphemeralAsync`@2505.
- `FormModal`/`SprkModal` (`@spaarke/ui-components`) for the name modal.
- `promptForInstruction`@2568 + `forceVisible`@877 (`ComposeEditor`/`ComposeAiToolbar`) for the Ctrl+Space path.
- PaneEventBus host→send bridge (`ConversationPane`@745–747) for the chat-focus signal.

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0: Coordination gate + baseline
Phase 1: Save-identity fix (UC-8)          ← DO FIRST (live data-integrity bug)
Phase 2: Save dropdown (UC-2)
Phase 3: Name modal (UC-3)
Phase 4: Draft-safe autosave + data-loss protection + indicator (UC-4)
Phase 5: PDF import parity (UC-7)
Phase 6: Hotkeys (UC-5, UC-6)
Phase 7: Accepted defers + hardening batch (D8, D4, D7, LOW-10, ETag/404, test-hygiene)
Phase 8: Wrap-up (anti-clobber deploy, test-diet, docs)
```

### Critical Path

**Blocking Dependencies:**
- Phase 1 (UC-8 stable logical id) BLOCKS Phase 4 (UC-4 draft key) — they share the identity.
- Phase 1 (Save As uniquify) BLOCKS Phase 2 (Save As dropdown fork).
- Phase 3 (name modal) precedes Phase 4 (new docs named before server save; local draft protects pre-name).
- Phase 5 (async `ProjectForMount`) is a BFF contract change — coordinate `/conflict-check`.

**High-Risk Items:**
- UC-6 shared-file collision with `spaarkeai-assistant-enhancements-r3` — Mitigation: `/conflict-check` + sequence.
- FR-11 vs `Services/Ai` sole owner — Mitigation: `PublicContracts/` seam, no fork.

---

## 4. Phase Breakdown

### Phase 0: Coordination Gate + Baseline

**Objectives:** Confirm R6-engine assumptions; establish publish-size baseline; clear conflict-check.

**Deliverables:**
- [ ] `/conflict-check` clean (esp. `ConversationPane.tsx`, `Services/Ai`, `Services/Compose`)
- [ ] Confirm PDF DI compound gate ON in target env
- [ ] Record publish-size baseline (~44.96 MB (net10))

**Inputs**: spec.md, INDEX.md · **Outputs**: coordination note in `notes/`.

### Phase 1: Save-Identity Fix (UC-8) — FR-07

**Objectives:** Close all four duplicate-`sprk_document` vectors.

**Deliverables:**
- [ ] Introduce a stable, non-rotating logical id persisted across re-mounts (FR-07b)
- [ ] Save As uniquifies filename (FR-07a; feeds Phase 2)
- [ ] Always carry a dedup identity on id-less mount (FR-07c)
- [ ] Atomic server upsert on `sprk_graphitemid_uk` in `PromoteIfEphemeralAsync` (FR-07d)

**Critical Tasks:** stable-logical-id (blocks Phase 4). **BFF task** → Placement Justification + publish-size verify.

**Inputs**: `ComposeService.cs`, `ComposeWorkspace.tsx/.types.ts` · **Outputs**: identity-persistence + upsert guard.

### Phase 2: Save Dropdown (UC-2) — FR-01

**Deliverables:**
- [ ] Replace SplitButton with Save / Save As dropdown + Auto Save toggle
- [ ] Map `ComposeSaveMode` labels (enum unchanged: `'version'|'new'`)

**Inputs**: `ComposeFormatToolbar.tsx`@986–1018 · **Outputs**: dropdown control.

### Phase 3: Name Modal (UC-3) — FR-02

**Deliverables:**
- [ ] `FormModal` for document + file name on first create-on-save + Save As
- [ ] Thread name to BFF `create-on-save` (`DisplayName`→`ResolveFileName`@1346)

**Inputs**: `FormModal`, `ComposeWorkspace.tsx`@3018/3022 · **Outputs**: name modal + threaded name.

### Phase 4: Draft-Safe Autosave (UC-4) — FR-03

**Deliverables:**
- [ ] Client-only local/session draft store (keyed by the Phase-1 stable logical id), dirty-only, ~15s
- [ ] `beforeunload`/modal-close guard on unsaved work
- [ ] Local draft recovery on reopen/crash
- [ ] Toolbar save-state indicator (Saving/Saved/Unsaved + Auto Save On/Off — absorbs D6)
- [ ] Update the "no autosave" invariant comment (@34, @2789–2791) + the `unmountFlush` test

**Depends on**: Phase 1 (stable logical id), Phase 3 (name-first). **Inputs**: `ComposeWorkspace.tsx`, `ComposeFormatToolbar.tsx` · **Outputs**: autosave + recovery + indicator.

### Phase 5: PDF Import Parity (UC-7) — FR-06

**Deliverables:**
- [ ] Async `ProjectForMount` PDF fork (`IsPdfSource`→`ProjectPdfToDocxAsync`); keep docx path sync-fast
- [ ] Client: admit `.pdf` in Browse `accept`@3596 + `NON_DOCX_EXTENSION`/reference-only gate (intake doors only)
- [ ] Verify DI compound gate ON in target env
- [ ] Parity acceptance: PDF → editable → analysis → response → save-as-docx-version

**Critical Tasks:** async `ProjectForMount` (BFF contract change — NFR-04). **BFF task** → Placement Justification + publish-size verify. **Inputs**: `ComposeService.cs`@305–323, `ComposeEndpoints.cs`, `ComposeEditor.tsx`@267/278 · **Outputs**: PDF parity.

### Phase 6: Hotkeys (UC-5, UC-6) — FR-04, FR-05

**Deliverables:**
- [ ] `Ctrl+Space` opens "Describe a change" at caret (no selection); IME guard; `Ctrl+/` fallback
- [ ] `Ctrl+Shift+Space` focuses Assistant: add `focusInput()` to `ISprkChatInputHandle` + PaneEventBus event
- [ ] Discoverability hints (tooltips/shortcut labels)

**Coordination:** `/conflict-check` on `ConversationPane.tsx`/`SprkChatInput.tsx` vs `spaarkeai-assistant-enhancements-r3`. **Inputs**: `ComposeEditor.tsx`@2218–2229, `SprkChatInput.tsx`@257–263, `ConversationPane.tsx`@745–747 · **Outputs**: two hotkeys.

### Phase 7: Accepted Defers + Hardening Batch — FR-08…FR-13

**Deliverables:**
- [ ] D8 Blank-page mounts editable (empty-seed path @2252→2276) — FR-08
- [ ] D4 Restore-from-Source no longer blanks — FR-09
- [ ] D7 Add-Comment toolbar affordance wired to shipped machinery — FR-10
- [ ] LOW-10 PDF-intake cause discrimination (`ComposePdfIntakeSource.cs`) — FR-11
- [ ] apply-template ETag/If-Match + ApiError-404 branch — FR-12 (confirm file still touched post-r8)
- [ ] Test-hygiene: FakeTimeProvider flake + pre-existing jest suites + nda fixture paraId regen — FR-13

**Inputs**: various Compose files · **Outputs**: defers closed + hardening.

### Phase 8: Wrap-up

**Deliverables:**
- [ ] Anti-clobber deploy (BFF + `sprk_spaarkeai` together)
- [ ] `/test-diet` reconciliation (`notes/test-diet-report.md`)
- [ ] Docs (Compose editor UX + PDF import + autosave/draft model)
- [ ] **Name a home for the deferred fidelity wideners** (GitHub Idea or fast-follow project) — carry R6 defer-register §C evidence
- [ ] README status → Complete; lessons-learned; archive

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Azure Document Intelligence in target env | Verify | Medium | FR-06 verifies compound gate; NullObject typed-degradation if OFF |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| R6 save + PDF-intake engines | `src/server/api/Sprk.Bff.Api/Services/Compose/**` | Production (merged) |
| `Services/Ai/PublicContracts/` (FR-11) | `Services/Ai/PublicContracts/` | Published |
| `FormModal`/`SprkModal` | `@spaarke/ui-components` | Production |

---

## 6. Testing Strategy

**Unit Tests**: save-mode discriminator, filename uniquify, upsert idempotency, draft key derivation, PDF fork routing, hotkey guards (IME).

**Integration / seam Tests**: create-on-save name threading; upsert on `sprk_graphitemid_uk` (dedup across doors); async `ProjectForMount` PDF projection; parity path (PDF → editable → save-as-docx-version). Per ADR-038 `tests/integration/seam/**` for dispatch-spine changes.

**E2E / UI Tests**: Save/Save As UX; name modal; autosave + beforeunload + recovery; Ctrl+Space / Ctrl+Shift+Space; Blank-page editable; Restore-from-Source; Add-Comment. ADR-021 dark-mode checks on new UI (dropdown, modal, indicator).

---

## 7. Acceptance Criteria

### Technical Acceptance
- [ ] All 13 FRs' closed-set acceptance criteria met (see spec.md)
- [ ] No duplicate `sprk_document` rows from any door (repeated save + re-mount + id-less mount)
- [ ] PDF parity end-to-end in a DI-enabled env
- [ ] Compose jest + xUnit suites green + non-flaky

### Business Acceptance
- [ ] Owner UAT: never-lose-work autosave, Save/Save As, name-on-save, PDF import, hotkeys all pass
- [ ] Publish ≤60 MB; no new HIGH CVE

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Shared-file collision (`ConversationPane`/`SprkChatInput`) with assistant-r3 | Med | Med | `/conflict-check`; sequence UC-6 |
| R2 | `Services/Ai` sole-owner conflict (FR-11) | Med | Med | `PublicContracts/` seam; no fork |
| R3 | `Ctrl+Space` IME conflict | Med | Low | `isComposing` guard; `Ctrl+/` fallback |
| R4 | PDF DI gate OFF in env | Low | Med | FR-06 env verify; typed NullObject degradation |
| R5 | Publish-size breach | Low | High | Per-task publish verify (≤60 MB) |
| R6 | Test-hygiene overlap with PR #690 | Med | Low | Watch/coordinate #690 |

---

## 9. Next Steps

1. **Review this plan.md** for phase/critical-path accuracy.
2. **Run** `/task-create projects/spaarkeai-compose-r7` to generate task files (done by pipeline Step 3).
3. **Begin** Phase 1 (UC-8 save-identity fix) — the live data-integrity bug.

---

**Status**: Ready for Tasks
**Next Action**: task-create decomposition (pipeline Step 3)

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*

# Spaarke Compose R7 — Editor UX

> **Last Updated**: 2026-08-18
>
> **Status**: 🔬 **In UAT (NOT closed)** — all 20 tasks shipped + deployed to dev (2026-08-17), now resolving UAT findings. The project closes only when every issue in [`notes/uat-issues.md`](notes/uat-issues.md) is Fixed or explicitly Deferred to a named follow-up.

## Overview

Compose R7 is the **editor-UX layer above R6's save/PDF engines**: a Save / Save As dropdown with an Auto Save toggle, a name-on-first-save modal, draft-safe autosave with data-loss protection, PDF import parity, two AI hotkeys, and a save-identity fix that closes duplicate `sprk_document` creation from every door. R7 **wires and fronts** R6's engines — it does not re-architect them.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan, WBS, critical path |
| [Design Spec](./design.md) | Human design document (input) |
| [AI Spec](./spec.md) | AI-optimized spec — 13 FRs, 6 NFRs, ADR tensions |
| [Task Index](./tasks/TASK-INDEX.md) | Task tracker + parallel groups (created by task-create) |
| [R6 Defer Register](./notes/r6-defer-register-consolidated.md) | Consolidated R6→R7 defer register |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | 🔬 In UAT (code 100% + deployed; resolving UAT issues) |
| **Progress** | Build 100% (20/20 tasks); UAT open — [`notes/uat-issues.md`](notes/uat-issues.md) |
| **Target Date** | — |
| **Completed Date** | — (pending UAT sign-off) |
| **Owner** | Ralph Schroeder |

## Problem Statement

The Compose editor works but has six concrete UX/robustness gaps, one data-integrity bug, and one import-parity gap surfaced during R6 UAT:

1. **Save is a two-mode split-button** that invites a data-integrity bug (duplicate `sprk_document` records — R6 defect D1).
2. **New documents are silently named `Untitled document.docx`** — no name prompt before the file lands in SharePoint Embedded.
3. **No autosave and no data-loss protection** — a closed modal or browser navigation loses unsaved work (the owner's explicit priority: *losing work is far worse than an unnecessary save*).
4. **PDF cannot be imported** into an editable Compose document (no parity with `.docx`) — R6 built the engine but wired it to only one of three doors.
5. **AI editing requires a text selection** — no keyboard path to invoke "Describe a change" at the caret.
6. **No keyboard path into the Assistant** — the user must mouse to the chat pane.

All are editor-surface concerns; none require new AI capability, playbooks, or template infrastructure (templates split to `spaarkeai-compose-templates-r8`).

## Solution Summary

Deliver seven use cases (UC-2…UC-8) on top of R6's shipped save + PDF-intake engines: a **Save / Save As dropdown** with an Auto Save toggle; a **name/file-name modal** on first save; **draft-safe autosave ON by default** (client-only local draft, `beforeunload` guard, local recovery, toolbar save-state indicator); **PDF import parity** (async `ProjectForMount` PDF fork + client intake-door gates); **`Ctrl+Space`** → "Describe a change" and **`Ctrl+Shift+Space`** → focus Assistant; and a **save-identity fix** closing all four duplicate-record vectors, including an atomic server upsert guard on `sprk_graphitemid_uk`. Plus accepted R6 defers (Blank-page-editable, Restore-from-Source, Add-Comment) and a small hardening batch (LOW-10 cause discrimination, apply-template ETag/404, test-hygiene).

## Graduation Criteria

The project is considered **complete** when (✅ = shipped + deployed; ⚙️ = code+automated-tests complete, live interactive UAT is the operator's post-deploy check):

- [x] Save is a **dropdown** (Save + Save As + Auto Save toggle); **Save As produces a distinct new document** (uniquified filename), never a silent re-version.
- [x] Saving a **new** document prompts for **document name + file name**; the SPE record uses that name (no `Untitled document.docx`).
- [x] **No door produces duplicate `sprk_document` rows** for the same drive-item (all four D1 vectors + atomic upsert guard on `sprk_graphitemid_uk`).
- [x] With **Auto Save on** (default), a dirty doc drafts to local storage every ~15s (no version-per-tick); explicit **Save** creates the SPE version; `beforeunload`/modal-close warns; crash/close is recoverable; the toolbar shows Saving/Saved/Unsaved + Auto Save On/Off.
- [x] ⚙️ A **PDF** opened via Browse or Assistant-upload becomes an **editable** Compose document (parity with `.docx`). Code + client gates + seam tests shipped; end-to-end analysis→response→docx-save UAT is operator-run in a DI-enabled env (`Analysis:Enabled && DocumentIntelligence:Enabled`).
- [x] ⚙️ **Ctrl+Space** (no selection) opens "Describe a change"; **Ctrl+Shift+Space** focuses the Assistant input. IME guard (`isComposing`/keyCode 229) shipped + unit-tested; manual IME-not-hijacked check is operator UAT.
- [x] **Blank page** mounts editable (D8); **Restore from Source** no longer blanks (D4); an **Add Comment** affordance exists (D7).
- [x] Publish size **44.95 MB** ≤ 60 MB; no new HIGH CVE; placement/component justifications recorded; `/conflict-check` clean; **BFF + `sprk_spaarkeai` deployed together** to dev (2026-08-17), hash-verified, healthy, Compose routes 401.
- [x] Test suites green — the wrap-up holistic review caught + fixed a cross-project CS0535 build break (`Sprk.Bff.Api.IntegrationTests`) and a task-061 focus-steal before deploy.

## Scope

### In Scope
- UC-2 Save / Save As dropdown + Auto Save toggle
- UC-3 Name/file-name modal on first save + Save As
- UC-4 Draft-safe autosave (client-only) + data-loss protection + save-state indicator
- UC-5 `Ctrl+Space` "Describe a change" at caret
- UC-6 `Ctrl+Shift+Space` focus Assistant (new `focusInput()` + PaneEventBus)
- UC-7 PDF import parity (async `ProjectForMount` + client gates)
- UC-8 Save-identity fix (four vectors + server upsert guard)
- Accepted R6 defers: D8 Blank-page-editable, D4 Restore-from-Source, D7 Add-Comment
- Candidate adds: LOW-10 PDF-intake cause discrimination, apply-template ETag/404, test-hygiene batch

### Out of Scope
- Templates / storage / picker / email templates → `spaarkeai-compose-templates-r8`
- PDF export / save-as-PDF → deferred future project
- Fidelity wideners (indentation ×84 / paragraph-style ×85 / section-break) → deferred; **home named at R7 wrap-up**
- Re-architecting the R6 save/PDF-intake engines
- TipTap for the email composer (stays Lexical)
- Non-Compose SpaarkeAi code-page changes (D9) → separate code-page project
- AI-suggestion pipeline bugs (D2/D3) → AI-platform surface
- D1 dev-data hygiene (5 duplicate records) → accepted debt, no task

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Autosave = **client-only** local/session draft | Honors "never lose work" for crash/close/nav without exploding SPE version history; UC-4 touches no BFF surface | [ADR-049](../../.claude/adr/ADR-049-compose-shadow-document.md) |
| Draft-recovery key + client dedup share one **stable logical id** | Unifies FR-03 + FR-07 on `sprkDocumentId ?? speDriveItemId ?? persistedLogicalId`; confirmed no non-rotating id exists today | — |
| `ProjectForMount` becomes **async** for the PDF fork | Parity with `LoadAsync`; documented ADR-007/013 contract change; docx path stays synchronous-fast | [ADR-007](../../.claude/adr/) / ADR-013 |
| Server upsert guard on `sprk_graphitemid_uk` | Read-then-`CreateAsync` (no atomic upsert) is the live D1 duplicate-record hole | — |
| Name modal reuses `FormModal`/`SprkModal` | §11 reuse — no parallel modal | [ADR-050](../../.claude/adr/ADR-050-canonical-modal-shell.md) |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| `ConversationPane.tsx` / `SprkChatInput.tsx` collision with `spaarkeai-assistant-enhancements-r3` (active) | Med | Med | `/conflict-check` before UC-6 PR; sequence vs r3's ConversationPane work |
| `Services/Ai/ComposePdfIntakeSource.cs` (FR-11) vs `spaarke-ai-architecture-redesign-r2` (sole `Services/Ai` owner) | Med | Med | Consume `PublicContracts/` seam — no fork; `/conflict-check` before BFF PR |
| `Ctrl+Space` IME conflict (IME toggle on some stacks) | Low | Med | `event.isComposing` guard; `Ctrl+/` fallback confirmed |
| PDF DI gate OFF in target env → intake silently unavailable | Med | Low | FR-06 verifies `Analysis:Enabled && DocumentIntelligence:Enabled`; typed "unavailable" via NullObject |
| Publish size breach (BFF touches) | High | Low | Per-task publish-size verify (≤60 MB; baseline ~44.96 MB (net10)) |
| Test-hygiene overlap with PR #690 (ci-lfs, "fixes 5 Compose seam tests") | Low | Med | Watch/coordinate #690 before landing FR-13 |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| R6 save + PDF-intake engines | Internal | Ready | Merged to master; verified in code (ProjectForMount / ProjectPdfToDocxAsync) |
| Azure Document Intelligence enabled in target env | External | Verify | Required for FR-06 PDF parity |
| `spaarke-ai-architecture-redesign-r2` `PublicContracts/` | Internal | Ready | Consume for FR-11 — no fork |
| Atomic BFF + `sprk_spaarkeai` deploy window | Internal | Planned | Anti-clobber (NFR-05) |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Ralph Schroeder | Overall accountability |
| Developer | Claude Code (Sonnet 5 execution) | Implementation per task-execute |
| Reviewer | code-review + adr-check gates | Quality gates at Step 9.5 |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-08-13 | 1.0 | Project initialized via /project-pipeline (spec + artifacts + tasks) | Ralph + Claude |
| 2026-08-17 | 2.0 | **Project complete.** All 20 tasks shipped (Save/Save-As + Auto Save, name modal, client-only draft-safe autosave, PDF import parity, Ctrl+Space/Ctrl+Shift+Space hotkeys, four-vector save-identity fix + atomic upsert, D8/D4/D7 defers, LOW-10 + apply-template + test-hygiene batch). Fast-forward merged to master; **BFF (44.95 MB, net10) + `sprk_spaarkeai` deployed together** to dev, both verified. Wrap-up gates: `/test-diet` clean (0 scaffolding), `/conflict-check` clean, holistic review fixed a CS0535 build break + a focus-steal before deploy. Documented exceptions: 074 §6.5 Path A (DEF-001/#776), 041 no-autosave invariant flip, 050 async `ProjectForMount`. Fidelity-wideners home named: `spaarkeai-compose-fidelity-wideners-r1` (DEF-002/#777). | Ralph + Claude |

---

*Template version: 1.0 | Based on Spaarke development lifecycle*

# TASK-INDEX — spaarkeai-compose-r7

> **Project**: Spaarke Compose R7 — Editor UX (Save/Save-As, draft-safe autosave, PDF-import parity, hotkeys, save-identity fix)
> **Generated**: 2026-08-13 by `/task-create` (pipeline Step 3)
> **Spec**: [spec.md](../spec.md) · **Plan**: [plan.md](../plan.md) · **Project CLAUDE.md**: [CLAUDE.md](../CLAUDE.md)
> **Total tasks**: 20 (001, 010–013, 020, 030, 040–041, 050–051, 060–061, 070–075, 090)

---

## Task Table

| ID | Title | Phase | Status | Rigor | Model | Effort | Parallel-safe | Deps | FR |
|----|-------|-------|--------|-------|-------|--------|---------------|------|----|
| 001 | Coordination gate + publish-size baseline + env verify | 0 | ✅ | MINIMAL | sonnet | high | ❌ (gate) | none | — |
| 010 | Stable, non-rotating logical document id (persisted) | 1 | ✅ | FULL | **opus** | high | ❌ spine | 001 | FR-07b |
| 011 | Always carry dedup identity on id-less mount | 1 | ✅ | FULL | sonnet | **xhigh** | ❌ spine | 010 | FR-07c |
| 012 | Save As uniquifies filename (real fork) | 1 | ✅ | FULL | sonnet | **xhigh** | ❌ spine | 010 | FR-07a |
| 013 | Atomic server upsert on `sprk_graphitemid_uk` | 1 | ✅ | FULL | **opus** | high | ❌ spine/BFF | 010 | FR-07d |
| 020 | Save / Save As dropdown + Auto Save toggle | 2 | 🔲 | FULL | sonnet | high | ❌ spine | 012 | FR-01 |
| 030 | Name / file-name modal on first save + Save As | 3 | 🔲 | FULL | sonnet | high | ❌ spine/BFF | 001 | FR-02 |
| 040 | Client-only draft store + dirty autosave + recovery | 4 | 🔲 | FULL | sonnet | high | ❌ spine | 010, 030 | FR-03 |
| 041 | Save-state indicator + beforeunload + invariant/test update | 4 | 🔲 | FULL | sonnet | high | ❌ spine | 040 | FR-03 |
| 050 | Async `ProjectForMount` PDF fork (server) | 5 | 🔲 | FULL | **opus** | high | ❌ spine/BFF | 001 | FR-06 |
| 051 | Client PDF intake-door gates + env verify + parity | 5 | 🔲 | FULL | sonnet | high | ❌ spine | 050 | FR-06 |
| 060 | Ctrl+Space "Describe a change" at caret (IME-guarded) | 6 | 🔲 | FULL | sonnet | high | ❌ spine | 001 | FR-04 |
| 061 | Ctrl+Shift+Space focus chat — focusInput() + PaneEventBus | 6 | 🔲 | FULL | sonnet | high | ❌ spine/coord | 001 | FR-05 |
| 070 | Blank page mounts editable | 7 | 🔲 | FULL | sonnet | high | ❌ spine | 001 | FR-08 |
| 071 | Restore from Source no longer blanks | 7 | 🔲 | FULL | sonnet | **xhigh** | ❌ spine | 011 | FR-09 |
| 072 | Add Comment toolbar affordance | 7 | 🔲 | FULL | sonnet | high | ❌ spine | 001 | FR-10 |
| 073 | PDF-intake cause discrimination (LOW-10) | 7 | ✅ | FULL | sonnet | high | ✅ Group B | 001 | FR-11 |
| 074 | apply-template ETag/If-Match + typed-404 | 7 | 🔲 | FULL | sonnet | high | ❌ spine | 001 | FR-12 |
| 075 | Test-hygiene batch (flake + jest suites + fixture) | 7 | ✅ | FULL | sonnet | high | ✅ Group B | 001 | FR-13 |
| 090 | Project wrap-up (deploy, test-diet, docs, archive) | 8 | 🔲 | FULL | sonnet | high | ❌ final | 010–075 | — |

**Rigor distribution**: FULL ×18, MINIMAL ×1 (001). (075 is FULL by the CLAUDE.md §8 TEST-MODIFYING override.)
**Model tier**: opus ×3 (010, 013, 050); sonnet ×17.
**Effort**: xhigh ×3 (011, 012, 071); high ×17.
**Parallel-safe: true** ×2 (073, 075 — Group B); all others false (shared Compose spine per CLAUDE.md §Parallel Task Execution).

---

## Dependency Graph (DAG)

```
001 (coordination gate)
 ├─► 010 (stable logical id) ─┬─► 011 (id-less mount dedup) ─► 071 (restore-from-source)
 │                            ├─► 012 (Save As uniquify) ─────► 020 (Save dropdown)
 │                            ├─► 013 (server upsert)
 │                            └─► 040 (draft store) ──────────► 041 (indicator/beforeunload)
 ├─► 030 (name modal) ────────────────────────────────────────► 040
 ├─► 050 (async ProjectForMount) ─► 051 (client PDF gates + parity)
 ├─► 060 (Ctrl+Space)
 ├─► 061 (Ctrl+Shift+Space)          [coord: /conflict-check ConversationPane/SprkChatInput vs assistant-r3]
 ├─► 070 (blank-page editable)
 ├─► 072 (add-comment)
 ├─► 073 (LOW-10 intake)             [coord: PublicContracts only, no Services/Ai fork — r2 sole owner]
 ├─► 074 (apply-template ETag/404)
 └─► 075 (test-hygiene)              [coord: watch PR #690]
        └────────────────────────── all ──────────────────────► 090 (wrap-up)
```

### Critical Path

`001 → 010 → 040 → 041 → 090`  (shared stable-id → draft key → indicator/invariant → wrap-up).

Key blocking dependencies (from plan.md §Critical Path):
- **010 → 040** — Phase 1 stable logical id is the Phase 4 draft-recovery key (shared identity).
- **010 → 012 → 020** — Save As uniquify (fork) is surfaced by the Save dropdown.
- **030 → 040** — new docs named-first before server save (local draft protects pre-name work).
- **050** — async `ProjectForMount` is an ADR-007/013 contract change (NFR-04); coordinate `/conflict-check` before the BFF PR.
- **011 → 071** — Restore-from-Source shares the transient-mount lifecycle root cause with the id-less mount vector.

---

## Parallel Execution Plan

**Reality**: nearly all R7 tasks touch the shared Compose spine (`Services/Compose/**`, `ComposeWorkspace.tsx`, `ComposeEditor.tsx`, `ComposeFormatToolbar.tsx`, `ConversationPane.tsx`) and are cross-worktree-contended per `projects/INDEX.md`, so they run **sequentially** (parallel-safe:false). The only genuine parallel group is **Group B** (073 + 075), whose files are disjoint from every other task and from each other.

| Wave | Tasks | Prereq | Files touched | Parallel-safe | goal-eligible |
|------|-------|--------|---------------|---------------|---------------|
| W0 | 001 | none | notes only | ❌ (gate) | NO (single-task gate) |
| W1 | 010 | 001 | ComposeWorkspace.tsx/.types.ts, compose-contracts.ts | ❌ spine | NO (architectural, blocks Phase 4) |
| W2 | 011 → 012 → 013 (sequential) | 010 | ComposeWorkspace.tsx, ComposeService.cs | ❌ spine | NO (data-integrity + BFF, irreversible) |
| W3 | 020 | 012 | ComposeFormatToolbar.tsx | ❌ spine | NO (single-task, shared toolbar) |
| W4 | 030 | 001 | ComposeWorkspace.tsx, ComposeEndpoints.cs, ComposeService.cs | ❌ spine/BFF | NO (BFF name threading) |
| W5 | 040 → 041 (sequential) | 010, 030 | draft util, ComposeWorkspace.tsx, ComposeFormatToolbar.tsx | ❌ spine | NO (data-loss stakes, invariant flip) |
| W6 | 050 → 051 (sequential) | 001 | ComposeService.cs, ComposeEndpoints.cs, ComposeEditor.tsx | ❌ spine/BFF | NO (BFF contract change, env-gated) |
| W7 | 060, 061 (sequential — both touch ComposeEditor.tsx) | 001 | ComposeEditor.tsx, SprkChatInput.tsx, ConversationPane.tsx | ❌ spine/coord | NO (IME judgment + cross-worktree coord) |
| **W8** | **073, 075 (parallel — Group B)** | 001 | 073: Services/Ai/ComposePdfIntakeSource.cs · 075: test files + nda fixture | ✅ **both true** | NO (2-task wave <3; BFF + PR#690 coordination) |
| W9 | 070, 071, 072, 074 (sequential — all touch Compose spine) | 070/072/074←001; 071←011 | ComposeWorkspace.tsx, ComposeEditor.tsx, ComposeAiToolbar.tsx, ComposeFormatToolbar.tsx | ❌ spine | NO (shared spine, no batching benefit) |
| W10 | 090 | 010–075 | README/plan/docs, deploy | ❌ final | NO (irreversible deploy) |

### Group B (the only true parallel group)

| Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|-------|-------|--------------|---------------|---------------------|
| B | 073, 075 | 001 ✅ | 073: `Services/Ai/ComposePdfIntakeSource.cs` (+test) · 075: Compose test files + nda fixture — **disjoint** | ✅ Yes |

**How to execute Group B**: confirm 001 ✅, then invoke the Task tool with two `task-execute` subagents (073, 075) in ONE message. Both carry cross-worktree coordination notes (073 → Services/Ai sole owner r2; 075 → PR #690) — resolve those via `/conflict-check` before their PRs, not by serializing execution.

### goal-eligibility — summary

**No wave is `/goal`-eligible.** Reasons: W1/W2/W5 are data-integrity + architectural (high-ambiguity, must stop for human judgment); W4/W6/W8-073 are BFF/irreversible-deploy-adjacent (touch `azure-deployment.md` scope); W7 has an IME judgment boundary + cross-worktree coordination; W3/W9/W10 are single-task or shared-spine (no batching benefit); the only parallel wave (W8, 073+075) is a 2-task wave (<3, fails Step 3.85 batching threshold) with BFF + PR-#690 coordination. Per Step 3.85, all waves record `goal-eligible: NO`. Step 9.5 gates (code-review + adr-check) run per task regardless.

---

## Coordination / Hot-Path Notes (from CLAUDE.md + INDEX.md)

- **061** — `/conflict-check` on `ConversationPane.tsx` + `SprkChatInput.tsx` vs active `spaarkeai-assistant-enhancements-r3` before the PR.
- **073** — consume `Services/Ai/PublicContracts/` ONLY; **no fork** of `Services/Ai/` (sole owner `spaarke-ai-architecture-redesign-r2`).
- **075** — watch PR **#690** (ci-lfs, "fixes 5 Compose seam tests"); do not double-fix.
- **BFF tasks (012 if server, 013, 030, 050, 073, 074 if server)** — Placement Justification in PR (cite `.claude/constraints/bff-extensions.md`); publish ≤60 MB (delta vs ~44.96 MB (net10) baseline from 001); no new HIGH CVE; `/conflict-check` before the BFF PR.
- **All Compose client tasks** — **NEVER delete `docxBridge.ts`** (NFR-06).
- **090** — deploy BFF + `sprk_spaarkeai` **together** (anti-clobber, NFR-05).

---

## Execution Order (recommended)

1. **001** (gate — must clear before anything else).
2. **010** (stable id — blocks Phase 4 + dedup).
3. **011 → 012 → 013** (remaining save-identity vectors, sequential).
4. **020** (Save dropdown), **030** (name modal).
5. **040 → 041** (autosave + indicator).
6. **050 → 051** (PDF parity).
7. **060 → 061** (hotkeys).
8. **Group B: 073 ∥ 075** (parallel); **070, 071, 072, 074** (sequential spine).
9. **090** (wrap-up — after all ✅).

Run `/task-execute 001` to begin.

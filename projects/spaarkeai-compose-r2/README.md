# Spaarke Compose R2

> **Last Updated**: 2026-07-08
>
> **Status**: In Progress (planning complete; implementation gated on core Phase A0 for AI-dependent tracks)

## Overview

R2 turns Spaarke Compose from a foundation editor into an **AI-native legal drafting workspace with Word-native interoperability and cross-session memory continuity**. It layers five one-shot AI actions, inline redline editing, three working document entry paths, Word comment/track-change round-trip, and rich session memory onto the R1 foundation — reintroducing nothing PR #544 retired.

## Quick Links

| Document | Description |
|----------|-------------|
| [Design](./design.md) | Working design (feature-first; refined + code-grounded 2026-07-08) |
| [Spec](./spec.md) | AI implementation spec (36 FRs, 9 NFRs) |
| [Plan](./plan.md) | Implementation plan + WBS (phase sequencing around the core dependency) |
| [Tasks](./tasks/TASK-INDEX.md) | Task tracker (created by task-create) |
| [CLAUDE.md](./CLAUDE.md) | AI context for this project |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Planning → ready for task decomposition |
| **Progress** | 0% (implementation not started) |
| **Target Date** | — (set after task count is known) |
| **Owner** | Ralph Schroeder |

## Problem Statement

R1 shipped the Compose workspace foundation (TipTap editor, three-pane shell, document load, single dispatch path) but left the differentiation layer un-activated: no AI actions on selections, no Word-native interop, no memory continuity, and two of three document entry paths non-functional. Uploaded/chat-drafted content cannot become a real Spaarke Document. Without these, Compose cannot replace Word add-in workflows (Harvey, Spellbook) or deliver the "highlight → explain → compare → draft → push to Word → remembered" promise.

## Solution Summary

Five AI actions (explain, compare-to-playbook, draft-alternative, summarize-word-changes, defined-terms) authored as **Action+Binding catalog rows** dispatched through the *shipped* session-dispatch seam (**zero new dispatch endpoints**, ADR-039). Inline redline editing with undo/replace via the session ledger (ADR-040). Three entry paths wired (Browse, Search, upload→transient-mount). Document creation at full ingestion parity on save (container from business unit + optional parent prompt + profile + indexing). Word-native `<w:comment>`/`<w:ins>`/`<w:del>` push+pull via Open XML SDK with return-from-Word re-anchoring. Rich session memory (ledger + workspace-scope MemoryItems + anchored annotations).

## Graduation Criteria

The project is **complete** when:

- [ ] **Flagship gate (G-R2-C)**: the assistant-driven lifecycle — open → pre-seed → draft-into-editor → AI edit rounds → save-back with provenance — runs in **one conversation**, browser-verified on spaarkedev1, create leg ending with the document **open in Compose** (not a record id)
- [ ] All three entry paths mount a file (1a Browse, 1a Search, 1b upload, 1c)
- [ ] 5 Action+Binding rows deployed with eval cases + schema validation; all dispatch through the seam (zero new endpoints)
- [ ] Create-on-save yields a full-parity Document (container + `sprk_document` + profile + indexing); no fileless orphan reported as success
- [ ] Word push renders natively in Word for Web; pull round-trips; return-from-Word re-anchors with confidence bands + banner
- [ ] Inline redline + undo/replace are ledger-durable (survive refresh)
- [ ] BFF publish ≤ 60 MB; no new HIGH CVE; NetArchTest AI-facade rule green

## Scope

### In Scope

- 5 AI actions (Action+Binding pairs) + Document Q&A stretch
- Inline AI toolbar (BubbleMenu) + custom marks + pending redline + undo/replace + serial queue
- Entry paths: 1a Browse, 1a Search, 1b upload→transient mount; create-on-save at full ingestion parity
- LLM editing patterns (validator, batch, transaction, semantic appendix, CriticMarkup read direction)
- Word interop: push/pull annotations, SPE webhook+delta, return-from-Word re-anchoring
- Session memory: anchored annotations, workspace-scope MemoryItems, ledger-query action history, always-visible provenance + D-F4 trace hosting
- Three-pane coordination (six flows + D-F3 ack)

### Out of Scope

- Clause library + cursor-position insertion toolbar (deferred to a follow-on; extensibility preserved)
- New `sprk_analysisplaybook` records (engine frozen)
- New AI dispatch endpoint (ADR-039 ban)
- Word's authoring UX at parity (tracked-changes/comments authoring, footnotes, complex formatting, redline comparison — Word-required per §1.6)
- Interactive input surfaces in the Context pane (audit-only)

## Key Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Core Phase A0 contracts (`ComposeDisposition`, `JobAwareCompletionState`, `OutcomeCard`, `TraceEvent`, `GateDecision v2`, `memory.write`, triple-twin hoist) | External (core R2) | **BLOCKED** — core R2 has no worktree yet (INDEX.md) | Gates catalog rows + draft-into-editor + completion/gate/memory legs. Spikes, Word shuttle, entry-point wiring do NOT wait. |
| R1 foundation + redesign-r1 as-built | Internal | Ready (in master) | Compose layout, chat→Compose bridge, `ComposeService`, session ledger — all merged |
| `@spaarke/legal-workspace` package extraction (dataset-grid-framework-r2) | Internal | In-flight (PR #537) | Merge-order coordination for Context-pane sections |
| Open XML SDK 3.x + Codeuctivity.OpenXmlPowerTools | External (MIT) | Ready | DOCX read/write |

---

*Project artifacts: [spec.md](./spec.md) · [plan.md](./plan.md) · [design.md](./design.md). Generated 2026-07-08 by project-pipeline.*

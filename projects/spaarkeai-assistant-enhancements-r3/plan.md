# Implementation Plan — spaarkeai-assistant-enhancements-r3

> **Source**: `spec.md` (15 FRs / 9 NFRs) · **Design**: `design.md` (The Assistant ⇄ Workspace Interaction Contract, §5.5)
> **Status**: Tasks generated — execution owner-gated (NOT auto-started)
> **Created**: 2026-08-10

---

## 1. Overview

R3 delivers **Conversational Capability Parity**: every open workspace widget gets a matching Assistant tool set (overview/query + per-item action), mounted only when its tab is open. The spine is the **active-item handle model** (§5.5) — the Assistant holds `{id,type,label}`, never content; every fact/action is a tool that fetches by id. Generalized from the shipped Compose active-document flow.

**Scope locked (owner 2026-08-10)**: overview parity across **all grids + Briefing + Calendar** (one parameterized `configId` tool); per-item cards for **Email + Documents**; auto-draft replies with a binding **thread-preservation invariant**; selection handle stays **in the prompt** (no `get_selection` tool); **Phase 0 out** (shipped on R2).

---

## 2. Architecture Context

### Discovered resources
- **ADRs (full)**: ADR-039 (grounded execution, closed catalogs — the mounting mechanism), ADR-015 (data governance — id-not-content, Path A honest exception), ADR-030 (PaneEventBus), ADR-047 (notification spine — keep distinct), ADR-049 (Compose — the generalized precedent), ADR-012 (shared component library), ADR-028 (auth/OBO), ADR-038 (testing), ADR-032 (Null-Object kill-switch — only if any parity tool ships feature-gated), ADR-013 (AI facade — `Services/Ai/PublicContracts/`).
- **Skills**: task-execute, code-review, adr-check, conflict-check (before every BFF/ConversationPane PR), bff-deploy, code-page-deploy, test-diet (wrap-up).
- **Canonical impls to reuse**: `composeActionBridge`/`registerActiveDocument`/`activeSourceDocRef` (conduit precedent); `WorkspacePane` `active-doc-follows-tab` (`:2283`); `EmailWorkspace.onVisibleEmailChange` (`:199`); `EmailComposer.reducer` `deriveReplyState`/`quotedThread` + `runAiDraft` re-append; `AgentToolProjection.PreFilter` (`:101`); `BriefingService`; `IRagService`/`DocumentSearchHandler`/`DocumentContextService`; `EmailDraftToolHandler`; `eml-render` (`FileAccessEndpoints.cs:901`).
- **Docs**: `ASSISTANT-SURFACE-LAUNCH-MECHANISM.md`, `ASSISTANT-UI-ELEMENT-CRITERIA.md`, `SPAARKEAI-WORKSPACE-ARCHITECTURE.md`, `.claude/constraints/bff-extensions.md`.

### Hot-path coordination (⚠️ HEAVY — see `projects/INDEX.md`)
R3 declares **BFF=Y, SpaarkeAi=Y**. Overlapping active worktrees on R3's exact files:

| Worktree | Shared surface | Rule |
|---|---|---|
| `spaarke-ai-architecture-redesign-r2` | **sole owner of `Services/Ai/` internals** | Consume `PublicContracts/` seams — **NO fork**. `/conflict-check` before every BFF PR. |
| `spaarkeai-assistant-enhancements-r2` (predecessor) | `SprkChatAgentFactory`, `TryDeriveVisibleState`, `ConversationPane` | Shipped/merged → in-branch; worktree live. Re-base on merged R2. |
| `spaarke-notification-spine-r1` | `ConversationPane` suggestion renderer (ADR-047) | Keep reactive card surface **distinct** (NFR-07). |
| `analysis-hub-r1` / `agreements-r1` | `ConversationPane` fork/routing | Coordinate merge order; `parallel-safe:false` on `ConversationPane`. |
| `email-communication-solution-r5` / `email-communication-intelligence-r2` | `EmailWorkspace`, `useEmailComposeActions`, `Communication.Components` | `bodyOverride` + email tools coordinate; `parallel-safe:false` on email components. |
| `spaarkeai-compose-r5/r6` | `composeActionBridge`, `Services/Compose` | Generalizing the conduit — coordinate; NEVER delete `docxBridge.ts`. |

**`ConversationPane.tsx` is a sequential spine** (multiple R3 tasks touch it → `parallel-safe:false` among themselves). **`Services/Ai/Chat` tasks** coordinate with architecture-redesign-r2. **Email-component tasks** coordinate with email-r5.

### Baseline note
Branch is **5 commits behind `origin/master`** (2026-08-10). Merge `origin/master` into the branch **before Phase 1 execution** (per owner "re-sync master before deploy"). Not a task-generation blocker.

---

## 3. Phase Breakdown (WBS)

### Phase 0 — Foundation: the active-item conduit
- **001** — Generalize the Compose active-document flow into a **widget-agnostic active-item conduit** (`{id,type,label}`, single-active-item invariant, clear-on-deselect/tab-switch). Two feed patterns: in-widget selection + tab-focus. *(FR-04 foundation — critical path; blocks all per-item work.)*

### Phase 1 — Awareness (identity + active-item handle)
- **010** — Layout-tab visibility (Daily Briefing + Calendar identity variant) + persist `visibleToAssistant`. *(FR-01, FR-02)*
- **011** — Trim prompt block to `{type,label,active}` per tab + thread the active-item handle into the prompt (server). *(FR-03, FR-04 server)*
- **012** — Email widget publishes selection as an **id handle** to the conduit (redirect `onVisibleEmailChange`). *(FR-05)*

### Phase 2 — Capability parity
- **020** — **Parameterized `configId` overview tool** (the overdue-tasks DoD driver: saved-query reuse, `today` injected, server-side predicates, record-id citations). *(FR-06)*
- **021** — Wire the overview tool across **all grids + Briefing (`BriefingService`) + Calendar (events query)**. *(FR-07)*
- **022** — widget-type ↔ context-type **mapping + Assistant-contract metadata shape** on registration. *(FR-08, FR-15 shape)*
- **023** — Email per-item tools: `draft_reply`/`draft_forward` (auto-draft, **extend `EmailDraftToolHandler`**) + `summarize_thread` (reuse file-summarize over `.eml`). *(FR-09)*
- **024** — `bodyOverride` param on `openComposer` — **thread-preserving compose** (AI draft above reducer `quotedThread`; whole-body replace = defect). *(FR-10 — BINDING invariant)*
- **025** — Email per-item **cards** (Reply/Reply All/Forward/Summarize) keyed to the active email; wire to 023/024. *(FR-09/FR-10 client)*
- **026** — Document per-item: generalize `active-doc-follows-tab` to `document-viewer`; **cards** Summarize/Draft-response/Draft-memo; backing tools reuse RAG (lane 2) + on-demand body. *(FR-11)*

### Phase 3 — Tool economy
- **030** — Wire `Binding.ContextTypeTags` into the ADR-039 `PreFilter` (`OpenTabContextTypes` predicate + hoist `tabs`); mount only open tabs' tools. *(FR-12)*

### Phase 4 — Interaction patterns + accurate follow-ons
- **040** — Respond/direct/hybrid pattern as a **registration-contract field**. *(FR-13)*
- **041** — Deterministic follow-ons from mounted tools + pattern; **card vs chip** element type (per-item→cards, query→chips; collapse card stacks). *(FR-14)*

### Cross-cutting — Registration contract
- **050** — Make Assistant-contract fields **required** registration metadata + registry enforcement across **all four** registration sites. *(FR-15 enforcement — lands after 022/040 define the fields.)*

### Deploy + wrap-up
- **080** — Deploy + verify (BFF publish ≤60 MB + SpaarkeAi code page); **owner-gated**; re-sync master first.
- **090** — Project wrap-up (README status, lessons-learned, `/test-diet`, archive).

---

## 4. Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| Foundation | 001 | — | Conduit; blocks per-item work. `parallel-safe:false` (shared conduit; coordinate compose lines). |
| Wave 1 | 010, 012 | 001 | 010 = BFF+client layout visibility; 012 = email-widget emit. Disjoint files. |
| Wave 1-serial | 011 | 001 | Touches `SprkChatAgentFactory.BuildWorkspaceStateBlock` (same file as 010) → **sequential with 010**; coordinate redesign-r2 seams. |
| Wave 2-BFF | 020 → 021, 023 | 011 | `Services/Ai` — 020 before 021 (021 wires 020 across surfaces); 023 separate handler. `parallel-safe:false` (redesign-r2 coordination). |
| Wave 2-client | 022, 024 | 001 | 022 registration metadata (shared); 024 email components (coordinate email-r5). Disjoint. |
| Wave 3 | 025, 026 | 023, 024, 022 | Both touch `ConversationPane` (per-item cards) → **sequential spine** (`parallel-safe:false`). |
| Wave 4 | 030 | 022 | PreFilter (`Services/Ai`) — coordinate redesign-r2. |
| Wave 5 | 040 → 041 → 050 | 022, 025, 026 | Interaction field → follow-ons → registry enforcement. Sequential (registration shape). |
| Deploy | 080 | all code waves | Owner-gated; re-sync master. |
| Wrap-up | 090 | 080 | `/test-diet` gate. |

**Concurrency cap**: 6 agents/wave. **Build verification** between waves (dotnet build BFF for `.cs`; `npm run build` for shared/SpaarkeAi packages).

---

## 5. Rigor & tiering

- Default execution: **Sonnet 5 @ high**.
- **opus / xhigh** escalations: **001** (shared conduit, cross-worktree blast radius), **011** (ADR-015 prompt boundary + redesign-r2 seams), **020** (the DoD; server-side query + today injection), **023** (email tools + AI facade), **024** (the thread-preservation invariant — data-loss-adjacent), **030** (PreFilter on ADR-039 boundary).
- FULL rigor (code-review + adr-check at Step 9.5) on all `.cs`/`.ts`/`.tsx` tasks.

---

## 6. Success criteria (from spec §Success Criteria)

Overview DoD (overdue tasks) · Per-item DoD (email reply auto-draft **+ preserved thread**) · Summarize-in-chat · Document per-item · Overview breadth (all surfaces) · Tool economy (open-tab scoping) · Registration enforcement · Governance (no content in prompt) · BFF hygiene (≤60 MB, no new HIGH CVE, dual-mount email parity).

---

## 7. References

- `spec.md` · `design.md` · `notes/design-review-2026-08-10.md` · `notes/R3-SESSION-CONTEXT.md`
- `projects/INDEX.md` (hot-path registry) · `.claude/constraints/bff-extensions.md` · `.claude/adr/ADR-039-*.md` · `.claude/adr/ADR-015-*.md`

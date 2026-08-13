# Task Index — spaarkeai-assistant-enhancements-r3

> **Status legend**: 🔲 pending · 🔄 in-progress / needs-retry · ✅ complete
> **Execution**: **owner-gated — NOT auto-started.** Re-sync `origin/master` (5 behind at init) before Phase 1.
> **Generated**: 2026-08-10 by project-pipeline (17 tasks).

---

## Task Registry

| # | Task | Phase | FRs | Tier / Effort | Rigor | Parallel-safe | Deps | Status |
|---|---|---|---|---|---|---|---|---|
| 001 | Active-item conduit (widget-agnostic `{id,type,label}`) | 0 Foundation | FR-04 | opus / xhigh | FULL | ❌ | — | ✅ |
| 010 | Layout-tab visibility + persist `visibleToAssistant` | 1 Awareness | FR-01,02 | sonnet / high | FULL | ❌ | 001 | ✅ |
| 011 | Trim prompt block + thread active-item handle (server) | 1 Awareness | FR-03,04 | opus / xhigh | FULL | ❌ | 001,010 | ✅ |
| 012 | Email widget publishes selection as id handle | 1 Awareness | FR-05 | sonnet / high | FULL | ✅ | 001 | ✅ |
| 020 | Parameterized `configId` overview tool (DoD driver) | 2 Parity | FR-06 | opus / xhigh | FULL | ❌ | 011 | ✅ |
| 021 | Wire overview tool: all grids + Briefing + Calendar | 2 Parity | FR-07 | sonnet / high | FULL | ❌ | 020 | ✅ |
| 022 | widget↔context-type map + contract metadata shape | 2 Parity | FR-08,15 | sonnet / high | FULL | ✅ | 001 | ✅ |
| 023 | Email per-item tools (extend `EmailDraftToolHandler`) | 2 Parity | FR-09 | opus / high | FULL | ❌ | 011 | ✅ |
| 024 | `bodyOverride` — thread-preserving compose (invariant) | 2 Parity | FR-10 | opus / xhigh | FULL | ❌ | 001 | ✅ |
| 025 | Email per-item cards (Reply/RA/Fwd/Summarize) | 2 Parity | FR-09,10 | sonnet / high | FULL | ❌ | 023,024,022 | ✅ |
| 026 | Document per-item (tab-focus + RAG cards) | 2 Parity | FR-11 | sonnet / high | FULL | ❌ | 022,024 | ✅ |
| 030 | Tool economy — `OpenTabContextTypes` PreFilter | 3 Economy | FR-12 | opus / xhigh | FULL | ❌ | 022 | ✅ |
| 040 | Interaction pattern as registration field | 4 Interaction | FR-13 | sonnet / high | FULL | ❌ | 022,025,026 | ✅ |
| 041 | Deterministic follow-ons + card/chip type | 4 Interaction | FR-14 | sonnet / high | FULL | ❌ | 040 | ✅ |
| 050 | Registration contract enforcement (4 sites) | Cross-cutting | FR-15 | opus / high | FULL | ❌ | 022,040 | ✅ |
| 080 | Deploy + verify (owner-gated; re-sync master) | Deploy | — | sonnet / high | STANDARD | ❌ | all code | ✅ deployed 2026-08-11 (BFF+code page); QW1/QW2 on master, live deploy via successor project; runtime DoD = owner UAT (notes/deploy-verify.md) |
| 090 | Project wrap-up (`/test-diet` gate) | Wrap-up | — | sonnet / med | MINIMAL | ❌ | 080 | ✅ 2026-08-13 — code-review SHIP, test-diet 33/33 MAINTAIN (notes/test-diet-report.md), portfolio Issue #766 archived |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| Foundation | 001 | — | Conduit; blocks per-item work. Solo (`parallel-safe:false`; coordinate compose lines, never delete `docxBridge.ts`). |
| Wave 1 | 010, 012 | 001 | 010 = BFF+client layout visibility; 012 = email-widget emit. Disjoint files. |
| Wave 1-serial | 011 | 001, 010 | Same `BuildWorkspaceStateBlock` file as 010 → **sequential**; coordinate redesign-r2 seams. |
| Wave 2-BFF | 020 → 021, 023 | 011 | `Services/Ai` — 020 before 021; 023 separate handler. `parallel-safe:false` (redesign-r2 coordination). |
| Wave 2-client | 022, 024 | 001 | 022 registration metadata (safe); 024 email components (coordinate email-r5). |
| Wave 3 | 025, 026 | 023,024,022 | Both touch `ConversationPane` → **sequential spine** (`parallel-safe:false`). |
| Wave 4 | 030 | 022 | PreFilter (`Services/Ai`) — coordinate redesign-r2. |
| Wave 5 | 040 → 041 → 050 | 022,025,026 | Interaction field → follow-ons → registry enforcement. Sequential (registration shape). |
| Deploy | 080 | all code | Owner-gated; re-sync master. |
| Wrap-up | 090 | 080 | `/test-diet` gate. |

**Concurrency cap**: 6 agents/wave. **Build verification between waves** (mandatory): `dotnet build src/server/api/Sprk.Bff.Api/` if any `.cs` changed; `npm run build` for touched shared/SpaarkeAi packages.

---

## Critical Path

`001 → 010 → 011 → 020 → 021` (overview DoD) and `001 → {023,024,022} → 025` (per-item DoD) → `040 → 041 → 050` → `080 → 090`.

Longest chain: **001 → 011 → 020 → 021 → 040 → 041 → 050 → 080 → 090** (≈9 links). The two DoDs (020/021 overview; 025 per-item) are the value-proving milestones.

---

## High-Risk Items

- **001** — shared conduit; cross-worktree blast radius (compose lines). opus/xhigh.
- **011** — ADR-015 prompt boundary (id-not-content); redesign-r2 seam coordination. opus/xhigh.
- **020** — the overview DoD; server-side query + `today` injection over OBO. opus/xhigh.
- **024** — the thread-preservation invariant (data-loss-adjacent); dual-mount parity (must not break `sprk_emailpage`). opus/xhigh.
- **030** — ADR-039 PreFilter boundary; redesign-r2 coordination. opus/xhigh.

---

## Coordination (hot-path — see `projects/INDEX.md`)

- `/conflict-check` before **every** BFF / `ConversationPane` PR.
- Consume `Services/Ai/PublicContracts/` seams — **no fork** (redesign-r2 sole owner).
- `ConversationPane.tsx` sequential spine (025, 026, 041 → `parallel-safe:false`).
- Email-component tasks (024) coordinate with `email-communication-solution-r5`.
- Keep the reactive card surface distinct from the ADR-047 spine (notification-spine-r1).

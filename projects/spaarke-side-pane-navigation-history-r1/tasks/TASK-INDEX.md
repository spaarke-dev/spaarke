# TASK-INDEX — spaarke-side-pane-navigation-history-r1

> **Generated**: 2026-08-13 by `/project-pipeline` Step 3 · **Tasks**: 21 · **Status**: INITIALIZED (execution owner-gated)
> Legend: 🔲 not-started · 🔄 needs-retry · ✅ completed · ⛔ blocked/deferred
> Rigor: FULL = code-review + adr-check at Step 9.5. Tier/effort dispatched by `task-execute` Step 0.5 from each POML.

## Task registry

| # | Task | Phase | Rigor | Tier/Effort | Deps | Group | Parallel-safe | Status |
|---|---|---|---|---|---|---|---|---|
| 001 | SPIKE/GATE — Path B bootstrap on current UCI | 0 Spike | FULL | opus/xhigh | — | Gate | ❌ | ✅ GO |
| 010 | Widen `xrmContext.ts` typings (getPane/select, 3-frame, webresourceName) | 1 Framework | FULL | sonnet/high | 001 | A | ✅ | ✅ |
| 011 | `SprkSidePaneHost` + `sidePaneRegistry` (wrap shell + generalize orchestrator) | 1 Framework | FULL | sonnet/high | 010 | B | ✅ | ✅ |
| 020 | `sprk_navitem` schema authoring (entity-schema.md + deploy script) | 2 Entity | STANDARD | sonnet/high | 001 | A | ✅ | ✅ |
| 021 | Deploy `sprk_navitem` + security roles | 2 Entity | STANDARD | sonnet/high | 020 | B | ❌ env | ✅ deploy* |
| 030 | Capture (Viewed) — re-adopt `contextService` poll → history upsert | 3 Capture | FULL | sonnet/high | 011,021 | C | ✅ | ✅ |
| 031 | Retention — prune-on-write, 30-day age | 3 Capture | FULL | sonnet/high | 030 | E | ✅ | ✅ |
| 040 | `NavigatorPane` code page (Vite webresource pane + tab scaffold) | 4 Navigator | FULL | sonnet/high | 011,021 | C | ✅ | ✅ |
| 041 | Recent (Viewed) tab | 4 Navigator | FULL | sonnet/high | 030,040 | D | ❌ shared body | ✅ |
| 042 | Recent (Edited) toggle — N per-entity `modifiedby=me` | 4 Navigator | FULL | sonnet/high | 040 | D | ❌ shared body | ✅ |
| 050 | Pin gesture (per-user pin; never writes `sprk_monitor`) | 5 Pinned | FULL | sonnet/high | 040 | D | ❌ shared body | ✅ |
| 051 | Bookmarks (Pin this page + Add bookmark URL parse) | 5 Pinned | FULL | sonnet/high | 050 | E | ✅ | ✅ |
| 052 | Monitored lens (shared `sprk_monitor`, scoped to me) | 5 Pinned | FULL | sonnet/high | 040 | D | ❌ shared body | ✅ owner-only* |
| 060 | Views tab (reuse `ViewService.ts`; `userquery` grouped) | 6 Views | FULL | sonnet/high | 040 | D | ✅ own file | ✅ |
| 070 | Search / quick-switcher (local fuzzy + live escalation + kbd) | 7 Search | FULL | sonnet/high | 041,050,060 | F | ✅ | 🔲 |
| 080 | Read-time security trimming | 8 Security | FULL | sonnet/high | 041,050 | F | ✅ | 🔲 |
| 081 | Retention verification | 8 Security | STANDARD | sonnet/high | 031 | F | ✅ | 🔲 |
| 085 | Stub contributor (framework-proof, FR-13) | 9 Proof | STANDARD | sonnet/high | 011 | F | ✅ | 🔲 |
| 086 | Deploy `NavigatorPane` + wire bootstrap | 9 Deploy | STANDARD | sonnet/high | 040,041,042,050,051,052,060,070,080 | Deploy | ❌ env | 🔲 |
| 087 | UI-test pass (`ui-test`, light+dark) | 9 Deploy | STANDARD | sonnet/high | 086 | Deploy | ❌ | 🔲 |
| 090 | Project wrap-up (test-diet, lessons-learned, archive) | Wrap | MINIMAL | sonnet/high | 087 | Wrap | ❌ | 🔲 |

## Parallel execution groups (waves)

| Group | Tasks | Prerequisite | Goal-eligible | Notes |
|---|---|---|---|---|
| **Gate** | 001 | — | No | Serial go/no-go spike (opus/xhigh); Path A fallback on failure |
| **A** | 010, 020 | 001 ✅ | No | `xrmContext.ts` ‖ Dataverse schema authoring — different surfaces |
| **B** | 011, 021 | A ✅ | No | Host build ‖ entity deploy (021 human/env-gated) |
| **C** | 030, 040 | B ✅ | No | Capture service ‖ Navigator code-page shell |
| **D** | 041, 042, 050, 052, 060 | C ✅ | No | 041/042/050/052 edit the shared Navigator body → run **sequentially** (parallel-safe=false); 060 owns its Views file (may parallelize) |
| **E** | 031, 051 | 030 / 050 ✅ | No | Retention ‖ bookmarks |
| **F** | 070, 080, 081, 085 | D/E ✅ | No | Search ‖ security-trim ‖ retention-verify ‖ stub |
| **Deploy** | 086 → 087 | F ✅ | No | Build+deploy (human/env gate), then UI-test pass |
| **Wrap** | 090 | 087 ✅ | No | Serial wrap-up + `/test-diet` gate |

> **Goal-eligibility (Step 3.85)**: **No** for all waves — this project contains a spike gate (001), environment-mutating deploys (021, 086), and a security-sensitive task (080). Execution is owner-gated wave-by-wave; do not run under a `/goal` auto-loop.

## Critical path

`001 → 011 → 040 → 041 → 070 → 086 → 087 → 090`
Parallel prerequisite stream feeding 030/041: `001 → 020 → 021 → 030`.
The gate (001) blocks everything; the shared-body tasks (041/042/050/052) serialize within wave D.

## High-risk items

- **001 (gate)** — Path B bootstrap reliability on current UCI. Failure → documented Path A fallback (re-baselines FR-02/FR-03). Do not commit framework code before the verdict.
- **021, 086** — environment mutations (schema deploy, code-page deploy). Human/env-gated; `parallel-safe=false`; escalate on failure.
- **080** — security-sensitive (legal-context read-time trimming). Distinguish 403/404 (drop) from transient errors (retain). FULL rigor + escalation.
- **042 / capture** — current-user OData literal (`@me`) may vary by UCI build (R2); validate in 001/030.

## Coordination

- Hot-path: BFF=N, SpaarkeAi=N, CI=N, Skill-directives=N, root-CLAUDE=N — no §10 obligations.
- Touches `@spaarke/ui-components` (`SidePane/`, `xrmContext.ts`, new host/registry, capture service). No active worktree touches these subpaths, but run `/conflict-check` before any `@spaarke/ui-components` PR.

# Task Index — spaarkeai-assistant-enhancements-r4

> **Status legend**: 🔲 pending · 🔄 in-progress / needs-retry · ✅ complete
> **Execution**: **owner-gated — NOT auto-started.** Baseline synced to `origin/master` (`033c43a91`).
> **Generated**: 2026-08-13 by project-pipeline (17 tasks / 6 phases).

---

## Task Registry

| # | Task | Phase | FRs | Tier / Effort | Rigor | Parallel-safe | Deps | Status |
|---|---|---|---|---|---|---|---|---|
| 001 | Behavior-gap register + eval-case harness | 0 Foundation | FR-12, FR-10 | sonnet / high | STANDARD | ✅ | — | ✅ |
| 010 | Per-Action bounded grounded-tool opt-in field | 1 E1 tier | FR-03 | opus / high | FULL | ❌ | — | ✅ |
| 011 | Advisory pre-filter bounded-tool scoping | 1 E1 tier | FR-02 | opus / xhigh | FULL | ❌ | 010 | ✅ (Option A projection primitive; runner+routing → 012) |
| 012 | Advisory task-agenda capability + nested-turn runner | 1 E1 tier | FR-01 | opus / high | FULL | ❌ | 010, 011 | ✅ (advisory list-tasks + AdvisoryCapabilityRunner + dispatch routing; 9 seam tests, publish 43.67 MB, CVE clean) |
| 013 | E1 eval cases (task-agenda golden utterances) | 1 E1 tier | FR-10 | sonnet / high | FULL | ✅ | 012 | ✅ (AR4-001/002/003 + AssistantEnhancementsR4EvalTests, 6 structural DoD assertions, GoldenUtteranceEval gate 154/154, negative check confirmed) |
| 020 | OBO-identity wording (user-scoped tools) | 2 E2 | FR-05 | sonnet / high | FULL | ❌ | — | ✅ |
| 021 | ~~Gate free-string `SprkChatSuggestions`~~ | 2 E2 | FR-04 | — | — | — | — | ⛔ SUPERSEDED → 021a + 021b (design delta 2026-08-17) |
| 021a | Grounded suggestion proposer (BFF) — retire ungrounded generator + typed two-kind output | 2 E2 | FR-04 | opus / high | FULL | ❌ | 012 | 🔲 |
| 021b | Render typed two-kind chip family (capability vs question) | 2 E2 | FR-04 | sonnet / high | FULL | ❌ | 021a | 🔲 |
| 022 | Briefing + Smart To Do launch-registry entries | 2 E2 | FR-06 | sonnet / med | STANDARD | ✅ | — | ✅ |
| 023 | Follow-on cards, open-tab-gated (Briefing/SmartToDo) | 2 E2 | FR-06 | sonnet / high | FULL | ❌ | 022, 012 | 🔲 |
| 024 | E2 eval cases (dead-end + card gating) | 2 E2 | FR-10 | sonnet / high | FULL | ✅ | 021b, 023 | 🔲 |
| 030 | `Preference` MemoryFactType + wire map | 3 E3 loop | FR-07 | opus / high | FULL | ❌ | — | ✅ |
| 031 | Feedback→memory pipeline | 3 E3 loop | FR-08 | opus / high | FULL | ❌ | 030 | 🔲 |
| 032 | Governed narrow-allow-list preference-producer | 3 E3 loop | FR-09 | opus / xhigh | FULL | ❌ | 030 | 🔲 |
| 033 | E3 eval + preference-producer bounds tests | 3 E3 loop | FR-09, FR-10 | sonnet / high | FULL | ✅ | 031, 032 | 🔲 |
| 040 | D9 host-proof flex-chain fix (Open-in-Compose) | 4 D9 | FR-11 | sonnet / high | FULL | ❌ | — | 🔲 |
| 080 | Deploy + verify (BFF + sprk_spaarkeai; owner-gated) | Deploy | — | sonnet / high | STANDARD | ❌ | 013, 024, 033, 040 | 🔲 |
| 090 | Project wrap-up (`/test-diet` gate) | Wrap-up | — | sonnet / med | MINIMAL | ❌ | 080 | 🔲 |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| Foundation | 001 | — | Register + eval harness (new files). `parallel-safe:true`. |
| Wave 1 — E1 spine | 010 → 011 → 012 → 013 | 001 | `Services/Ai` catalog/pre-filter spine → **sequential** (`parallel-safe:false`); 013 = eval (safe). |
| Wave 1 — independent | 020, 022, 030, 040 | — | Alongside E1: 020 OBO wording (BFF, disjoint), 022 registry entries (client, additive, safe), 030 memory enum (BFF Memory), 040 D9 (client). BFF ones `parallel-safe:false` → dispatch as slots free. |
| Wave 2 — E2 | 021a → 021b → 023 → 024 | 012, 022 | 021 SUPERSEDED → 021a (BFF grounded proposer) → 021b (client typed chips); then 023 cards, 024 eval. `SprkChat`/`ConversationPane`/`ChatEndpoints` spine → **sequential** (`parallel-safe:false`); 024 = eval (safe). See `notes/021-grounded-suggestions-design-delta.md`. |
| Wave 3 — E3 | 031, 032 → 033 | 030 | 031 feedback→memory, 032 producer (injection-defense) — coordinate BFF; 033 = eval/tests (safe). |
| Deploy | 080 | all code | Owner-gated; BFF + `sprk_spaarkeai` together. |
| Wrap-up | 090 | 080 | `/test-diet` gate. |

**Concurrency cap**: 6 agents/wave. **Build verification between waves** (mandatory): `dotnet build src/server/api/Sprk.Bff.Api/` if any `.cs` changed; `npm run build:prod` for PCF, `npm run build` for touched shared/SpaarkeAi packages.

> **🆕 Runtime = .NET 10** (dev net10 since 2026-08-14; worktree net10-ready, `dotnet build -c Release` clean). SDK ≥10.0.100 required for BFF builds (`global.json` pins 10.0.100). **Never deploy the BFF from a net8 tree (503).** Re-baseline the publish size fresh under net10 (the ~49.63 MB figure was net8).

---

## Critical Path

`001 → 010 → 011 → 012 → 013` is the E1 grounded-recommend value-proving spine. E2 depends on 012 (so "prioritize" maps to the advisory capability) + 022. E3 (030 → 031/032 → 033) is independent of E1/E2. E4 (040) is fully independent.

Longest chain: **001 → 010 → 011 → 012 → 021a → 021b → 023 → 024 → 080 → 090** (≈10 links).

---

## High-Risk Items

- **011** — ADR-039 pre-filter boundary: the bounded allow-list MUST be deterministic pre-filtering, never a second decider. opus/xhigh.
- **032** — injection-defense boundary: a preference may hint/trigger an allow-listed capability but never grant a capability or alter a fact. opus/xhigh.
- **012** — advisory Action authoring: `output_determinism: advisory`, Reasoning tier @ temp ~0.2–0.3, every narrated fact cited (no fabrication).
- **030/031** — memory governance: R4 owns the changes (redesign-r2 closed) but MUST preserve ADR-042 deferred hard-governance (#616; trustLevel inert).
- **040** — D9 may already be fixed by the merged partial fix; confirm live-DOM repro first; keep host-proof (no measured heights).

---

## Coordination (hot-path — see `projects/INDEX.md`)

- `/conflict-check` before **every** BFF / `ConversationPane` / `SprkChat` PR.
- Live overlap: **compose-r5/r6** (ConversationPane/SprkChat — D9), **assistant-r3** (SprkChatAgentFactory/AgentToolProjection). Memory files have **no live contender** (redesign-r2 closed) — R4 owns them.
- Bind CRUD-side memory consumers to `PublicContracts/MemoryItem.cs` v1; keep AI-internal types out of CRUD code (ADR-013).
- Keep the reactive card surface distinct from the ADR-047 spine (notification-spine-r1).
- Deploy BFF + `sprk_spaarkeai` together for any turn touching both.

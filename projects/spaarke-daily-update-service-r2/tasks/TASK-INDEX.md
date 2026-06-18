# Task Index — Daily Briefing R2

> **Project**: `spaarke-daily-update-service-r2`
> **Last Updated**: 2026-06-18
> **Status**: 19 / 36 complete; 1 deferred (Waves 1–4: + 014, 055 — FR-21 spec assumption falsified, acceptance relaxed per notes/runtime-config-divergence.md)
> **Branch**: `work/spaarke-daily-update-service-r2`

---

## Task Register

| ID | Title | Phase | Status | Dependencies | Parallel | Rigor |
|----|-------|-------|--------|--------------|----------|-------|
| 001 | Add `loadNotificationContext` factory option to dailyBriefingRegistration | P1 | ✅ | none | — | FULL |
| 002 | Wire `loadSpaarkeAiNotificationContext` injection in SpaarkeAi `main.tsx` | P1 | ⏸ | **018** (was 001) | — | FULL |
| 003 | P1 verification — SpaarkeAi pane renders bullets in spaarkedev1 (smoke) | P1 | ⏸ | 002 | — | STANDARD |
| 010 | Scaffold new `@spaarke/daily-briefing-components` package | P2 | ✅ | **none** (was 003) | — | FULL |
| 011 | Hoist Daily Briefing components (`DailyBriefingApp`, sections, atoms) | P2 | ✅ | 010 | A | FULL |
| 012 | Hoist `briefingService` (BFF `/narrate` client) | P2 | ✅ | 010 | A | FULL |
| 013 | Hoist existing hooks (`useInlineTodoCreate`, `useBriefingNarration`) | P2 | ✅ | 010 | A | FULL |
| 014 | Decompose `useNotificationData` → `useBriefingNotifications` + `useBriefingPreferences` + `useBriefingActions` | P2 | ✅ | 011,012,013 | — | FULL |
| 015 | Abstract dependencies — props/parameters; remove solution-local imports | P2 | 🔲 | 014 | — | FULL |
| 016 | Subpath exports contract (`./components`, `./widgets`, `./hooks`, `./services`, `./types`) | P2 | 🔲 | 015 | — | FULL |
| 017 | Shrink standalone code page to thin host shell | P2 | 🔲 | 016 | B | FULL |
| 018 | Replace LegalWorkspace `dailyBriefing` registration with thin shim | P2 | 🔲 | 016 | B | FULL |
| 019 | Unit tests for 3 split hooks + smoke test mounting `DailyBriefingApp` | P2 | 🔲 | 014,011 | — | STANDARD |
| 020 | `NarrativeBullet` renders per-item sub-list when `itemIds.length > 1` (FR-11) | P2a | 🔲 | 016 | — | FULL |
| 021 | Sub-row entity link via supplied `regardingId` + `Xrm.Navigation.navigateTo` (FR-12) | P2a | 🔲 | 020 | C | FULL |
| 022 | Sub-row Add-to-To-Do uses `useInlineTodoCreate` (FR-13) | P2a | 🔲 | 020 | C | FULL |
| 023 | Sub-row Dismiss + aggregated cascade Dismiss (FR-14, FR-14a) | P2a | 🔲 | 020 | C | FULL |
| 024 | P2a unit + visual tests + dark-mode parity check | P2a | 🔲 | 021,022,023 | — | STANDARD |
| 030 | `BuildChannelNarrationPrompt` emits `regardingId` per item + updated rule list (FR-15, FR-16) | P2b | ✅ | none | D | FULL |
| 031 | `ParseChannelBullets` validates `primaryEntityId`; nulls invalid + logs (FR-17) | P2b | ✅ | 030 | — | FULL |
| 032 | Unit tests (prompt content + validation logic) | P2b | ✅ | 031 | — | STANDARD |
| 033 | BFF publish-size delta + CVE verification (P2b) | P2b | ✅ subsumed by 042 | 032 | — | STANDARD |
| 040 | `CreateNotificationNodeExecutor` populates `data.actions[]` for visible-toasttype (FR-18) | P3 | ✅ | none | D | FULL |
| 041 | Unit tests for visible vs hidden toasttype paths | P3 | ✅ | 040 | — | STANDARD |
| 042 | BFF publish-size verification + E2E manual MDA bell test note | P3 | ✅ +0.00 MB delta | 041 | — | STANDARD |
| 050 | Hoist `MicrosoftToDoIcon` to `@spaarke/ui-components/src/icons/` (FR-19a) | DD | ✅ | none | D | FULL |
| 051 | Delete 3 solution-local `MicrosoftToDoIcon` copies; update imports (FR-19b) | DD | ✅ | 050 | — | FULL |
| 052 | Create `createCodePageAuthInitializer` factory in `@spaarke/auth` (FR-20a) | DD | ✅ | none | D | FULL |
| 053 | Migrate `DailyBriefing` solution to auth factory; delete local `authInit` | DD | ✅ | 052 | — | FULL |
| 054 | Migrate `LegalWorkspace` + `SpaarkeAi` to auth factory; delete local `authInit` | DD | ✅ (lazy-singleton pattern) | 053 | — | FULL |
| 055 | Consolidate `runtimeConfig` → `@spaarke/auth` singleton; delete 3 local copies (FR-21) | DD | ✅ (thin wrappers; FR-21 acceptance relaxed) | 054 | — | FULL |
| 060 | Deploy BFF (P2b + P3) via `bff-deploy` skill | Phase7 | 🔲 | 033,042 | E | FULL |
| 061 | Redeploy standalone Daily Briefing code page via `code-page-deploy` | Phase7 | 🔲 | 017,055 | E | FULL |
| 062 | E2E verification — SC1–SC14 in spaarkedev1 | Phase7 | 🔲 | 060,061,024,018 | — | STANDARD |
| 063 | Update architecture docs (SPAARKEAI-COMPONENT-MODEL, SPAARKEAI-WORKSPACE-ARCHITECTURE, BUILD-A-NEW-WORKSPACE-WIDGET) | Phase7 | 🔲 | 016 | — | MINIMAL |
| 090 | Project wrap-up (code-review + adr-check + repo-cleanup + README status + lessons-learned) | Phase8 | 🔲 | 062,063 | — | FULL |

---

## Re-sequencing Decisions (Wave 2a 2026-06-18)

**Task 002 deferred → depends on 018 (was 001)**. Reason: the `loadNotificationContext` factory option exists in the shared-lib `dailyBriefing.registration.ts`, but the LegalWorkspace consumer `src/solutions/LegalWorkspace/src/sections/dailyBriefing/dailyBriefing.registration.ts` is a STATIC `SectionRegistration` const that does NOT invoke the factory — so wiring `main.tsx` alone has no effect. Task 018 (LegalWorkspace shim replacement) is the natural unblocker: after 018 lands, the registration consumes the factory, and task 002 becomes a trivial `loadNotificationContext: loadSpaarkeAiNotificationContext` arg pass. Full analysis in `notes/task-002-blocker.md`. Task 003 (P1 smoke verification) is chained behind 002.

**Task 010 unblocked → deps cleared (was 003)**. Reason: scaffolding the new `@spaarke/daily-briefing-components` package has no functional dependency on P1 verification. P2 hoist can proceed in parallel with the P1 deferral.

**Task 053 acceptance interpreted, not literal**. The DailyBriefing solution's `authInit.ts` was REWRITTEN as a thin factory consumer with lazy-singleton wrapping (49 LOC) rather than deleted outright — the JSDoc canonical example pattern keeps `services/authInit.ts` as the encapsulation point. The spirit of FR-20 is met: no per-solution auth init *logic* remains; only a thin call-site wrapper. Lazy-singleton needed because DailyBriefing's runtime-config getters aren't available at module load (`setRuntimeConfig` must fire first). Task 054 should verify whether LegalWorkspace + SpaarkeAi need the same pattern (likely don't). See `notes/task-053-factory-config-timing.md`.

---

## Parallel Execution Groups

Tasks in the same group can run simultaneously once prerequisites are met. Pipeline orchestrator dispatches one agent per task in a group.

| Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize | Max Concurrency |
|-------|-------|--------------|---------------|---------------------|-----------------|
| **A** | 011, 012, 013 | 010 ✅ | Components (`components/`), service (`services/briefingService.ts`), hooks (`hooks/useInlineTodoCreate.ts` + `useBriefingNarration.ts`) — disjoint paths | ✅ Yes | 3 |
| **B** | 017, 018 | 016 ✅ | Standalone host shell (`src/solutions/DailyBriefing/`) vs LegalWorkspace shim (`src/solutions/LegalWorkspace/src/sections/dailyBriefing/`) — disjoint | ✅ Yes | 2 |
| **C** | 021, 022, 023 | 020 ✅ | All edit `NarrativeBullet.tsx` regions — **file-overlap risk**; run as Group C only if reviewer can confirm split-region edits, otherwise serialize | ⚠️ Serialize-or-careful | 1 (default) |
| **D** | 030, 040, 050, 052 | 003 ✅ | BFF `DailyBriefingEndpoints.cs`, BFF `CreateNotificationNodeExecutor.cs`, `@spaarke/ui-components/icons/MicrosoftToDoIcon.tsx`, `@spaarke/auth/` factory — fully disjoint code regions | ✅ Yes | 4 |
| **E** | 060, 061 | 033, 042, 017, 055 ✅ | Different deploy targets (BFF App Service vs code-page web resource) | ✅ Yes | 2 |

### Reconsidered Group C

Group C (021, 022, 023) is **conservatively marked serialize** because all three tasks edit `NarrativeBullet.tsx` (per-item sub-row logic lives in one component). If task 020 lands the sub-list skeleton with clearly-separated regions, the reviewer can promote C to parallel-safe; otherwise execute serially. Default: serial.

### Reconsidered Group D

Group D (030, 040, 050, 052) is the **most valuable parallel wave**: 4 independent code regions can move concurrently. P2b (030) and P3 (040) touch BFF; DD-icon (050) and DD-auth (052) touch shared UI/Auth packages. Zero file overlap.

---

## How to Execute Parallel Groups

1. Check all prerequisites are complete (✅ in Status column)
2. Send ONE message containing MULTIPLE `Skill(task-execute, ...)` invocations — one per task in the group
3. Each task-execute call runs in its own subagent with full context loading
4. Wait for all in the group to complete; verify build still passes; update statuses
5. Proceed to the next wave

**Max concurrency rule**: 6 agents per wave (per project-pipeline §5). All groups here are within that limit.

---

## Critical Path

The longest dependency chain (determines minimum project duration):

```
001 → 002 → 003 → 010 → 011 → 014 → 015 → 016 → 017 → 019 → 020 → 023 → 024 → 060 → 062 → 090
```

≈ 16 sequential tasks. P2b, P3, DD all hang off this path in parallel; they cannot extend it.

---

## High-Risk Tasks

| Task | Risk | Mitigation Reference |
|------|------|----------------------|
| 014 — `useNotificationData` 3-way split | Cache invalidation semantics may shift | plan.md R2; Option A (effect-based) at consumer layer; explicit refetch on preferences change |
| 017 — Standalone code page shrink | Visual regression in standalone | NFR-02 relaxed; smoke test mount; manual pre/post visual comparison |
| 054 — Auth factory migration in LegalWorkspace + SpaarkeAi | Breaks auth init order | One solution at a time; build green between; factory preserves call sequence |
| 040 — `data.actions[]` schema | Mismatch with Dataverse expected shape | E2E verification on first deployed notification in spaarkedev1 |
| 062 — E2E SC1–SC14 verification | Catches any integration regressions | Run after all code merged + deployed; manual checklist in spaarkedev1 |

---

## Rigor Level Distribution

- **FULL** (24 tasks): All code-implementation tasks across P1, P2, P2a, P2b, P3, DD, and deployment (per spec NFR-07)
- **STANDARD** (10 tasks): Test tasks (019, 024, 032, 041), verification (003, 033, 042, 062), publish-size measurements
- **MINIMAL** (1 task): Architecture doc updates (063)
- **FULL** (1 task): Project wrap-up (090) — includes code-review + adr-check + repo-cleanup quality gates

Per CLAUDE.md task-execute rigor decision tree.

---

## Execution Order Recommendation

**Wave 0 (serial)**: 001 → 002 → 003 (Phase P1 — unblocks all downstream work)

**Wave 1 (parallel, Group D + 010)**: 010 (P2 scaffold), 030 (P2b prompt), 040 (P3 executor), 050 (DD-icon hoist), 052 (DD-auth factory) — 5 agents

**Wave 2 (parallel, Group A)**: 011, 012, 013 — 3 agents (after 010); 031 (after 030); 041 (after 040); 051 (after 050); 053 (after 052) — 7 tasks total, dispatched in two sub-waves to stay ≤6 agents

**Wave 3 (serial — split hooks)**: 014 (after 011/012/013)

**Wave 4 (serial)**: 015 → 016, alongside 032 (P2b tests), 042 (P3 verification), 054 (DD-auth solutions 2 of 2)

**Wave 5 (parallel, Group B)**: 017, 018 — 2 agents (after 016); 055 (after 054); 033 (after 032)

**Wave 6 (serial — tests)**: 019 (after 014 + 011)

**Wave 7 (P2a serial)**: 020 → 021 → 022 → 023 → 024

**Wave 8 (parallel, Group E)**: 060, 061 — 2 agents; 063 (docs, after 016)

**Wave 9 (serial)**: 062 (SC1–SC14 verification)

**Wave 10 (serial)**: 090 (project wrap-up)

Total expected wave-count: ~10. Critical path is ~16 sequential tasks; parallelization reduces wall-clock significantly.

---

*Index maintained by task-execute (status flips on completion) and task-create (regeneration only by re-running this skill).*

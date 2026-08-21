# TASK-INDEX — sdap-SPE-admin-app-r2

> **Generated**: 2026-08-21 by `/project-pipeline` · **30 tasks** · lint: **30 clean / 0 errors / 0 warnings**
> **Branch**: `work/sdap-SPE-admin-app-r2` · **Draft PR**: [#811](https://github.com/spaarke-dev/spaarke/pull/811)
> Status legend: 🔲 not started · 🔄 in progress / needs retry · ✅ complete · ⛔ blocked

---

## ⚠️ Read before dispatching any wave

**The god-file caps concurrency.** Nearly every task modifies
`Infrastructure/Graph/SpeAdminGraphService.cs` (4,911 LOC). **At most ONE task per wave may modify it.**
Those tasks carry `parallel-safe=false` and run in the main session; `parallel-safe=true` tasks run as
agents alongside. Realistic concurrency is **2–3 agents**, not the 6-agent maximum. See [`../plan.md`](../plan.md) §3.

**Task 010 can reopen the auth ADR gate.** It is the project's highest-risk task. A `UNWORKABLE` verdict
blocks 011 and requires re-running the CLAUDE.md §6.5 block — not a silent fallback.

---

## Task Registry

| # | Task | Phase | FR | Rigor | Model | Effort | Wave | ∥-safe | Deps | Status |
|---|---|---|---|---|---|---|---|---|---|---|
| 001 | [Real error surface via ProblemDetails](001-real-error-surface.poml) | 1 A | A01 | FULL | sonnet | high | W0 | ❌ | — | ✅ |
| 002 | [Audit 70 `catch (ODataError)` sites](002-odata-error-audit.poml) | 1 A | A02 | FULL | sonnet | xhigh | W1 | ❌ | 001 | 🔲 |
| 003 | [Sync Status reflects real outcomes](003-sync-status-truth.poml) | 1 A | A03 | FULL | sonnet | high | W1 | ✅ | 001 | 🔲 |
| 005 | [Diagnose + fix Audit Log](005-fix-audit-log.poml) | 1 A | A05 | FULL | sonnet | xhigh | W1 | ✅ | 001 | 🔲 |
| 004 | [Diagnose + fix Search](004-fix-search.poml) | 1 A | A04 | FULL | sonnet | xhigh | W2 | ❌ | 001 | 🔲 |
| 010 | [🔔 SPIKE — owning-app delegated token](010-obo-spike.poml) | 2 B | B01 | FULL | **opus** | xhigh | W2 | ✅ | — | 🔲 |
| 040 | [WireMock Graph fixture infrastructure](040-wiremock-harness.poml) | 2 D | D01 | FULL | sonnet | high | W2 | ✅ | — | 🔲 |
| 011 | [Wire hybrid delegated path](011-hybrid-delegated-path.poml) | 2 B | B02 | FULL | **opus** | xhigh | W3 | ❌ | 010 | 🔲 |
| 012 | [Operator role prerequisite message](012-operator-role-message.poml) | 2 B | B03 | FULL | sonnet | high | W3 | ✅ | 010 | 🔲 |
| 013 | [Grant `SecurityEvents.Read.All`](013-security-events-grant.poml) | 2 B | B04 | STANDARD | sonnet | medium | W3 | ✅ | 001 | 🔲 |
| 020 | [`/beta` → v1.0 migration](020-beta-to-v1-migration.poml) | 3 C | C01 | FULL | sonnet | high | W4 | ❌ | 011, 040 | 🔲 |
| 030 | [Lifecycle constraints in UI](030-lifecycle-constraints-ui.poml) | 3 C | C13 | FULL | sonnet | high | W4 | ✅ | 011 | 🔲 |
| 021 | [Graph Endpoint setting — wire or delete](021-graph-endpoint-setting.poml) | 3 C | C02 | FULL | sonnet | high | W5 | ❌ | 020 | 🔲 |
| 022 | [Fix recycle-bin `$select`](022-recycle-bin-select-fix.poml) | 3 C | C03 | FULL | sonnet | medium | W6 | ❌ | 020, 040 | 🔲 |
| 023 | [Property names + quota/consumption split](023-property-names-and-quota-split.poml) | 3 C | C04, C05 | FULL | sonnet | high | W7 | ❌ | 020, 040 | 🔲 |
| 024 | [SPIKE + branch — storage consumption](024-storage-consumption-spike.poml) | 3 C | C06 | FULL | sonnet | high | W8 | ❌ | 023 | 🔲 |
| 025 | [Full 9-property settings surface](025-full-settings-surface.poml) | 3 C | C07 | FULL | sonnet | high | W9 | ❌ | 023, 040 | 🔲 |
| 026 | [Replication + override state](026-replication-and-override-state.poml) | 3 C | C08 | FULL | sonnet | high | W10 | ✅ | 025 | 🔲 |
| 029 | [Billing status surface + warning](029-billing-status-surface.poml) | 3 C | C12 | FULL | sonnet | medium | W10 | ✅ | 020 | 🔲 |
| 027 | [Container-type owner management](027-container-type-owner-management.poml) | 3 C | C09 | FULL | sonnet | high | W11 | ❌ | 011, 020, 040 | 🔲 |
| 028 | [Container URL + Purview deep-link](028-container-url-and-purview.poml) | 3 C | C10, C11 | FULL | sonnet | medium | W12 | ❌ | 020, 040 | 🔲 |
| 041 | [LiveIntegration suite + throwaway fixture](041-live-integration-suite.poml) | 4 D | D02 | FULL | sonnet | high | W13 | ✅ | 011, 040 | 🔲 |
| 042 | [Retire scaffolding tests (ADR-038)](042-retire-scaffolding-tests.poml) | 4 D | D03 | FULL | sonnet | high | W14 | ✅ | 040, 041 | 🔲 |
| 050 | [Container archival](050-container-archival.poml) | 5 E | E01 | FULL | sonnet | high | W15 | ❌ | 020, 040 | 🔲 |
| 051 | [Per-container quota ceiling](051-quota-ceiling.poml) | 5 E | E02 | FULL | sonnet | high | W16 | ❌ | 023, 024, 040 | 🔲 |
| 052 | [Item recycle bin (207 handled)](052-item-recycle-bin.poml) | 5 E | E03 | FULL | sonnet | high | W17 | ❌ | 022, 040, 041 | 🔲 |
| 060 | [Hygiene — dead stub + misfiled file](060-hygiene-stub-and-misfiled.poml) | 6 | F01, F02 | STANDARD | sonnet | low | W18 | ✅ | — | 🔲 |
| 061 | [Refresh SPE knowledge corpus](061-knowledge-corpus-refresh.poml) | 6 | X01 | MINIMAL | sonnet | medium | W18 | ✅ | 025 | 🔲 |
| 062 | [Billing-attach cross-project handoff](062-billing-attach-handoff.poml) | 6 | X02 | MINIMAL | sonnet | low | W18 | ✅ | 029 | 🔲 |
| 090 | [Project wrap-up + `/test-diet` gate](090-project-wrap-up.poml) | 7 | — | STANDARD | sonnet | high | none | ❌ | all | 🔲 |

---

## Wave Execution Plan

Each wave holds **at most one** `parallel-safe=false` GraphService task. Build verification runs between waves.

| Wave | Tasks | Prerequisite | Concurrency | Notes |
|---|---|---|---|---|
| **W0** | 001 ✅ | — | 1 (serial) | **Done 2026-08-21.** 60 error sites routed; endpoint-layer only (no GraphService change needed). ⚠️ UI verification blocked — SpeAdminApp build broken by a pre-existing missing dep; see `notes/task-001-completion.md` |
| **W1** | **002**, 003, 005 | 001 ✅ | 3 | 002 owns GraphService; 003 owns DashboardSync; 005 owns AuditService |
| **W2** | **004**, 010, 040 | 001 ✅ | 3 | 004 owns GraphService; 010 is notes-only; 040 is test-project-only. 🔔 **010 may reopen the ADR gate** |
| **W3** | **011**, 012, 013 | 010 ✅ **WORKABLE** | 3 | 011 owns TokenProvider + GraphService; 012 owns filter + client; 013 is Azure config |
| **W4** | **020**, 030 | 011, 040 ✅ | 2 | 020 owns GraphService; 030 is client-only |
| **W5** | **021** | 020 ✅ | 1 | GraphService + config + client |
| **W6** | **022** | 020, 040 ✅ | 1 | GraphService |
| **W7** | **023** | 020, 040 ✅ | 1 | GraphService + DTO. Load-bearing semantic split |
| **W8** | **024** | 023 ✅ | 1 | GraphService (4 null sites) + client |
| **W9** | **025** | 023, 040 ✅ | 1 | GraphService + DTOs + client |
| **W10** | 026, 029 | 025 / 020 ✅ | 2 | Both client + DTO only — no GraphService task this wave |
| **W11** | **027** | 011, 020, 040 ✅ | 1 | GraphService + permissions endpoints |
| **W12** | **028** | 020, 040 ✅ | 1 | GraphService ($select) + client |
| **W13** | 041 | 011, 040 ✅ | 1 | Test project only. ⚠️ Provisions a throwaway container |
| **W14** | 042 | 040, 041 ✅ | 1 | Test project only |
| **W15** | **050** | 020, 040 ✅ | 1 | GraphService + endpoints + client |
| **W16** | **051** | 023, 024, 040 ✅ | 1 | GraphService + endpoints + client |
| **W17** | **052** | 022, 040, 041 ✅ | 1 | GraphService + endpoints + client. ⚠️ Irreversible ops — throwaway container only |
| **W18** | 060, 061, 062 | 029 ✅ (062) | 3 | All independent: file moves, docs, cross-project note |
| **close** | 090 | all ✅ | 1 (serial) | 🔔 `/test-diet` is a BINDING gate |

**Bold** = the wave's single GraphService-modifying task.

### Build verification between waves (mandatory)

- Any `.cs` modified → `dotnet build src/server/api/Sprk.Bff.Api/`
- Any `.tsx`/`.ts` modified → build the SpeAdminApp code page (`npm install --legacy-peer-deps`)
- **Build fails → STOP. Do not dispatch the next wave.**

---

## Critical Path

```
001 → 004/010 → 011 → 020 → 023 → 024 → 051 → 090
      (W0)  (W2)   (W3)  (W4)  (W7)  (W8)  (W16)
```

**Longest chain: 8 tasks.** Task 010 is the highest-risk node — an `UNWORKABLE` verdict blocks 011, and
everything from 020 onward depends on 011. The auth spike is not just first; it is load-bearing.

---

## High-Risk Items

| Task | Risk | Guard |
|---|---|---|
| **010** | Owning-app OBO may be unworkable; two verified defects say the current path cannot succeed | Escalation trigger → re-run §6.5 gate. **Never fall back to BFF-identity OBO silently** |
| **011** | Auth change in a BFF hot path; ADR-028 A4 boundary | Opus tier + `xhigh`; §6.5 path-C cited in the PR |
| **004 / 005** | Uncapped — root causes not isolated; effort is provisional | Escalation triggers hand off if the cause is out of scope |
| **041 / 052** | Irreversible ops against a live tenant holding real documents | NFR-07: throwaway container provisioned by the fixture; guard refuses non-fixture container ids |
| **024** | Graph may not expose consumption at all | Two-branch FR — removal is pre-authorized by owner decision OC-04 |
| **042** | Deleting unreplaced coverage would be a regression | ADR-038 deletion-safety; escalation trigger |

---

## Goal-Eligibility (task-create Step 3.85)

| Wave | Eligible | Reason |
|---|---|---|
| W0, W2, W3 | ❌ | Security-sensitive auth work with a live ADR gate; 010 can reopen an architectural decision |
| W1 | ❌ | 004/005 are open-ended root-cause investigations — no machine-verifiable end state |
| W5–W12 | ✅ | Well-specified, low-ambiguity, machine-verifiable (build + tests + read-back). ≥3 tasks across the span |
| W13, W14 | ❌ | Live-tenant and deletion work — excluded per the eligibility rule |
| W15–W17 | ❌ | Irreversible operations against a live tenant |
| W18 | ✅ | Independent, well-specified, verifiable |
| close | ❌ | Terminal task with a binding human gate |

`goal-condition` for W5–W12: *"Every Workstream C task in the wave has its acceptance criteria met, `dotnet build` is 0 errors, the SpeAdmin test suite is green, and each settings change is confirmed by read-back against Spaarke Dev."*

> The Haiku evaluator is a **stopping-condition check, not a quality gate**. Step 9.5 and orchestrator
> authority are unchanged; tasks are never auto-completed on goal achievement.

---

## Traceability — 31 FRs → 30 tasks

| Workstream | FRs | Tasks |
|---|---|---|
| A — Make failures visible | A01–A05 | 001, 002, 003, 004, 005 |
| B — Auth (gated) | B01–B04 | 010, 011, 012, 013 |
| C — API surface | C01–C13 | 020, 021, 022, 023, 024, 025, 026, 027, 028, 029, 030 |
| D — Harness | D01–D03 | 040, 041, 042 |
| E — New capabilities | E01–E03 | 050, 051, 052 |
| F — Hygiene only | F01, F02 | 060 |
| Cross-cutting | X01, X02 | 061, 062 |
| Close | — | 090 |

FR count is 31 vs 30 tasks because tasks 023 and 028 each cover two FRs (C04+C05 and C10+C11), while
task 060 covers F01+F02.

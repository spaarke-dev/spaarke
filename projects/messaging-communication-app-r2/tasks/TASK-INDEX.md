# TASK-INDEX — messaging-communication-app-r2 (Communication Workspace)

> **Generated**: 2026-07-18 via `/project-pipeline`
> **Total tasks**: 21 (20 work + 1 wrap-up)
> **Legend**: 🔲 not-started · 🔄 in-progress/needs-retry · ✅ complete · ⛔ blocked/gated

---

## Task Registry

| # | Title | Wave | Tags | FR | Deps | Blocks | Parallel-safe | Rigor | Model/Effort | Status |
|---|---|---|---|---|---|---|---|---|---|---|
| 001 | Phase-0 audit (spike): live thread/participant schema; confirm category/tags absent (Q2); verify email-r4 `Services/Communication` merged state; plan `sprk_role` choice ints | W0 | dataverse, spike | — | — | 002,003 | true | STANDARD | sonnet/high | ✅ (note: `notes/001-phase0-schema-audit.md`; 11 RegardingFieldMap lookups captured, Q2 absent confirmed, sprk_role=100000000-3; live MCP re-verify deferred before 002/003 apply) |
| 002 | Schema: `sprk_communicationthread` typed-regarding **all-11** lookups (mirror `RegardingFieldMap.All`) + new Lookup discriminator + naming-edited marker + default-thread marker | W0 | dataverse, schema | FR-06 | 001 | 010,070,071 | true | STANDARD | sonnet/high | ✅ (script `scripts/Deploy-ThreadRegardingSchema.ps1` + note `notes/002-thread-regarding-schema.md`; discriminator=`sprk_regardingrecordtype_ref`→`sprk_recordtype_ref`; markers `sprk_nameisautoderived`/`sprk_isdefaultthread`; Text field untouched; **live apply DEFERRED — MCP down**, owner applies+verifies) |
| 003 | Schema: `sprk_communicationparticipant` junction — 6 fields (communication lookup, systemuser, contact, role {From,To,Cc,Bcc}, addresstext, isresolved) | W0 | dataverse, schema | FR-08 | 001 | 050,051 | true | STANDARD | sonnet/high | ✅ (authored: `scripts/Deploy-CommunicationParticipantSchema.ps1` + `notes/003-communicationparticipant-schema.md`; Org-owned + Cascade parent lookup mirror `sprk_communicationattachment`; sprk_role=100000000-3; **LIVE APPLY DEFERRED** — MCP down, owner runs script + records ints before 050/051) |
| 004 | Author participant-junction **schema ADR** (concise + full; ADR-034 path-C tension; INDEX → Accepted) | W0 | adr, docs | FR-08 | — | 050 | **false** (`.claude/`) | STANDARD | opus/high | ✅ (**ADR-048** authored concise `.claude/adr/` + full `docs/adr/`; both INDEXes → Accepted + CHANGELOG; ADR-034 path-C comply-with-intent argued; ADR-047 not claimed; main-session) |
| 010 | `GET /api/communications/by-regarding/{entityType}/{id}` + `ReadByRegardingAsync` on `CommunicationThreadReadService` (reuse impersonation query + access filter) | W1 | bff-api, communication | FR-01 | 002 | 020,080 | true | FULL | sonnet/high | 🔲 |
| 011 | `GET /api/communications?thread=&regarding=&channel=&from=&to=` filtered query (facets reuse read path; `participant=` stub) | W1 | bff-api, communication | FR-02 | 002 | 051,080 | true | FULL | sonnet/high | 🔲 |
| 020 | Regarding-mode extension to `CommunicationTimeline` component (threads-as-groups → per-thread interleaved timelines; calls `by-regarding`) | W2 | frontend, fluent-ui, communication | FR-03 | 010 | 021 | true | FULL | sonnet/high | 🔲 |
| 021 | Regarding-mode PCF variant (mirror R1 Timeline PCF + regarding-resolution path; **bound `anchorField`**) + solution pack | W2 | pcf, deploy, e2e-test | FR-04 | 020 | 022 | true | FULL | sonnet/high | 🔲 |
| 022 | Place the regarding-mode PCF on **all 11** entity forms + form config | W2 | dataverse, deploy, e2e-test | FR-04 | 021 | 080 | true | FULL | sonnet/high | 🔲 |
| 023 | VisualHost summary count MetricCard config (optional, config-only; drill-through; count-only per T-1) | W2 | dataverse, config | FR-05 | 010 | — | true | STANDARD | sonnet/high | 🔲 |
| 030 | New `@spaarke/communication-components` lib + rich Pattern D widget (copy `CalendarWorkspaceWidget`); upgrade `communications-list` **in place** (keep type string); **dual-deploy** | W3 | frontend, fluent-ui, spaarke-ai | FR-12 | 010 | — | true | FULL | sonnet/high | 🔲 |
| 040 | Standalone `sprk_communicationspage` shell (copy `sprk_invoicespage`; reuse config `e1826c4c-…`; **widget/launcher only — no sitemap**); register in `Deploy-AllDataGridConsumers.ps1` | W4 | frontend, deploy | FR-11 | — | — | true | STANDARD | sonnet/high | ✅ built (vite dist/index.html 1.6MB; GUID baked; deploy-script registered; live pac deploy DEFERRED to owner) |
| 041 | Curate grid views/columns on config `e1826c4c-…` so chips auto-derive (channel/person/date/regarding) | W4 | dataverse, config | FR-11 | — | — | true | STANDARD | sonnet/high | 🔲 |
| 050 | Participant-index **write** at capture/send — populate junction rows (message grain, reuse `ParticipantCorrelationRung`; unresolved → isresolved=false) | W5 | bff-api, communication | FR-08 | 003,004 | 051,060 | **false** (shared persist path) | FULL | opus/xhigh | 🔲 |
| 051 | `participant=` facet on the filtered `query` endpoint (join the junction) | W5 | bff-api, communication | FR-02 | 011,050 | 080 | true | FULL | sonnet/high | 🔲 |
| 060 | Compose enrichment: Subject/topic + Cc/Bcc + structured recipient picker (reuse `RecipientField`, emit resolved ids feeding 050); meaningful `sprk_name` | W6 | pcf, frontend, communication | FR-10 | 050 | — | true | FULL | sonnet/high | 🔲 |
| 070 | Auto-threading policy: 3-tier `IThreadResolver` (subject → per-record default → per-user master) + `default-thread` marker; characterization-test existing flows first | W7 | bff-api, communication | FR-09 | 002 | 071 | **false** (shared `ThreadResolver`) | FULL | opus/xhigh | 🔲 |
| 071 | Thread naming re-derive (`BuildTopic` re-derives unless user-edited, marker-gated) + place RegardingResolver PCF on thread form (0 code) | W7 | bff-api, pcf, communication | FR-07 | 002,070 | — | true | FULL | sonnet/high | 🔲 |
| 080 | Vertical-slice **seam tests**: `by-regarding` + `query` + `participant=` + auto-threading tiers; **11-entity pass**; access-parity (private + internal-only); preserve email/messaging characterization | W8 | bff-api, testing | NFR-03 | 010,011,022,050,051,070 | — | true | TEST-MODIFYING | sonnet/xhigh | 🔲 |
| 081 | Architecture doc: extend communication architecture with Workspace read endpoints + participant index + regarding-mode + widget | W8 | docs | — | 010,050,070 | — | true | STANDARD | sonnet/high | 🔲 |
| 090 | Project wrap-up (README Complete, lessons-learned, `/test-diet`, archive) | Wrap | wrapup | — | (all) | — | **false** | STANDARD | sonnet/high | 🔲 |

> **Note**: 20 numbered work tasks (001–004, 010–011, 020–023, 030, 040–041, 050–051, 060, 070–071, 080–081) + 090 wrap-up = **21 tasks**. All 21 POML files authored + validated (`Validate-TaskPoml.ps1` clean).

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **W0-A** | 001, 004 | — | Audit spike + ADR (004 main-session `.claude/`) |
| **W0-B** | 002, 003 | 001 | Two schema deltas (distinct entities, parallel) |
| **W1** | 010, 011 | 002 | Additive read endpoints; parallel |
| **W2** | 020 → 021 → 022; 023 (‖) | 010 | Component → PCF → 11-form placement; 023 config parallel |
| **W3** | 030 | 010 (+ PR #508 / dataset-grid-r2 merge-order) | Widget upgrade (dual-deploy) |
| **W4** | 040, 041 | — | Standalone page + grid curation (parallel) |
| **W5** | **050 (serial)** → 051 | 003 (+004) | 050 shared-path write; 051 facet after |
| **W6** | 060 | 050 | Compose enrichment feeds the index |
| **W7** | **070 (serial)** → 071 | 002 | Auto-threading shared-resolver edit; naming after |
| **W8** | 080, 081 | W1–W7 substantially complete | Seam tests + doc |

**Max concurrency**: 6 agents/wave. `.claude/`-touching (004) + wrap-up (090) run main-session, sequential. **050 + 070 are `parallel-safe: false`** (shared `Services/Communication/` edits — never concurrent; `/conflict-check` before each).

**Model tiers (per CLAUDE.md §8.5)**: default **sonnet @ high**. **opus** on 004 (ADR), 050 (participant write / shared persist path), 070 (auto-threading / shared resolver). **effort xhigh** on 050, 070, 080. All others sonnet @ high.

## Critical Path

```
001 → 002 → 010 → 020 → 021 → 022 → 080 → 090
001 → 003 → 050 → 051 → 080          (participant index → person filter)
002 → 070 → 071                       (auto-threading + naming on shared resolver)
```

## High-Risk / Watch Items

- **050 (participant write)** + **070 (auto-threading)** — shared `Services/Communication/` edits; characterization-test existing email/messaging flows green first; `parallel-safe: false`; `/conflict-check` before PR.
- **010/011 access parity (NFR-03)** — apply R1 impersonation + 2-rule filter; NO membership-union (retired 2026-07-16). Access-parity seam tests in 080.
- **022 (11-form placement)** — larger deploy/test matrix; PCF needs a bound `anchorField`.
- **030 (widget)** — dual-deploy (LegalWorkspace + SpaarkeAi); keep type string; merge-order vs dataset-grid-r2 + PR #508.
- **T-1 (VisualHost)** — 023 count-only; content via BFF Timeline.
- Every BFF-touching task: `/conflict-check` + publish-size + CVE (root §10).

## FR Coverage

FR-01→010 · FR-02→011,051 · FR-03→020 · FR-04→021,022 · FR-05→023 · FR-06→002 · FR-07→071 · FR-08→003,004,050 · FR-09→070 · FR-10→060 · FR-11→040,041 · FR-12→030. NFRs distributed (NFR-01/02 every BFF task; NFR-03→080; NFR-05 §11; NFR-06 reserve-only; NFR-07→030; NFR-08 deploy runbook).

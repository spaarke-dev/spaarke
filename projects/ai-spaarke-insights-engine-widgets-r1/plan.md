# plan.md — Insights Engine Widgets r1 — Implementation Plan

> **Source**: [`spec.md`](spec.md) (post-resolution 2026-06-10)
> **Status**: ✅ stable — generated 2026-06-10 via `/project-pipeline` constrained run
> **Phases**: 8 (Phase 0 Foundation through Phase 7 Deploy + Wrap)
> **Estimate**: 4–5 weeks with parallel execution where dependencies allow

---

## 1. Goals

1. Deliver the `InsightSummaryCard` reusable framework as defined in [`spec.md`](spec.md) FR-01 through FR-09
2. Deliver Matter Health single-mode as the first proven topic per FR-10 through FR-16
3. Wire pre-warm + caching infrastructure per FR-17 through FR-22
4. Validate via end-to-end UAT + tutorial per FR-23 through FR-27
5. Meet all 15 success criteria except SC-12 (DEFERRED to r2+ per Q-U3)

---

## 2. Phase Breakdown (WBS)

### Phase 0 — Foundation (Tasks 001–005)

**Goal**: Lock down pre-execution assumptions before committing to schema, playbook, or UI work.

| Task | Deliverable |
|---|---|
| 001 | FR-03 component-reuse investigation deliverable: `notes/insight-component-reuse-investigation.md` + `decisions/DR-001-component-reuse.md`. Ratify the plan-time pre-flight finding (no inline-expand-to-modal pattern exists; `InsightSummaryCard` ships in `@spaarke/ai-widgets` composing Popover + Dialog) with implementation-level confirmation. |
| 002 | R5 source grep + Matter form XML inspection (Assumption 2/11 OPEN INVESTIGATION): confirm whether R5 already wired a sparkle-icon to `sprk_performancesummary`; produce `notes/r5-sparkle-wiring-baseline.md`. If NO existing wiring → r1 is net-new form customization (most likely). |
| 003 | `sprk_aitopicregistry` schema design (entity diagram + field types + constraints + indexing); produce `notes/topic-registry-schema-design.md`. Verify against existing `sprk_gridconfiguration` patterns. |
| 004 | `adr-check` validation pass against spec.md (all named ADRs applicable; no NFR-09 violations); produce `notes/adr-check-2026-06-10.md`. |
| 005 | Build verification + branch health: `dotnet build`, BFF publish-size measurement (NFR-01 ≤60 MB ceiling; baseline 45.65 MB); produce `notes/baseline-2026-06-10.md`. |

### Phase 1 — Dataverse schema (Tasks 010–014)

**Goal**: `sprk_aitopicregistry` entity created + seeded; SME-editable in model-driven app.

| Task | Deliverable |
|---|---|
| 010 | Author entity definition (Web API metadata calls per `dataverse-create-schema` skill): 9 fields per FR-04 + relationships. Note: `sprk_icon` uses Fluent icon component name string convention (Q-U2 resolved). |
| 011 | Deploy entity to dev Dataverse via `dataverse-create-schema` skill; verify in MetadataService; verify SDK access. |
| 012 | Generate model-driven app form for `sprk_aitopicregistry`; verify SME can add row without code deploy (FR-09). |
| 013 | Seed `matter-health` / `single` row pointing at `matter-health-single` playbook with `Sparkle24Filled` icon, `sprk_performancesummary` target, 60-minute TTL. |
| 014 | Schema deploy verification: `mcp__dataverse__describe_table('sprk_aitopicregistry')` shows all fields; row queryable via Web API. |

### Phase 2 — Playbook authoring + deployment (Tasks 020–025)

**Goal**: `matter-health-single` JPS playbook deployed to dev; invokable via `/api/insights/ask`.

| Task | Deliverable |
|---|---|
| 020 | Author synthesis system prompt covering 7 baseline diagnostic dimensions (FR-12); persist to `sprk_analysisaction.sprk_systemprompt` via `jps-action-create` skill. Use exact KPI Performance Area names per FR-13 (Guideline Compliance / Budget Compliance / Outcomes Achievement). |
| 021 | Author playbook JSON (`matter-health-single`) following `predict-matter-cost.playbook.json` shape; use only existing node executors (QueryDataverse, IndexRetrieve, EvidenceSufficiency, AiAnalysis, GroundingVerify, UpdateRecord, ReturnInsightArtifact, DeclineToFindNode). **NO `@v1` suffix in name.** |
| 022 | UpdateRecord node config: write JSON envelope to `sprk_matter.sprk_performancesummary` per FR-14; envelope `schemaVersion: "1.0"` (string semver). |
| 023 | Deploy playbook via `scripts/Deploy-Playbook.ps1`; verify `sprk_playbook` row exists; set `Insights:Playbooks:Map.matter-health-single` config Guid in dev. |
| 024 | End-to-end smoke test: invoke `/api/insights/ask` with `topic=matter-health`, `mode=single`, `subject=matter:<GUID>`; verify response shape; verify envelope written to Matter record. |
| 025 | Document envelope schema: `notes/insight-envelope-schema.md` — JSON Schema definition + Power Fx + plugin extraction examples per FR-15. |

### Phase 3 — UI component `InsightSummaryCard` (Tasks 030–037)

**Goal**: Reusable card component in `@spaarke/ai-widgets` with 5+ states, Storybook story, dark mode.

| Task | Deliverable |
|---|---|
| 030 | Component scaffold in `src/client/shared/Spaarke.AI.Widgets/src/components/InsightSummaryCard/`; props per FR-01 (NO `onFeedback` — deferred per Q-U3); Griffel styles + Fluent v9 semantic tokens. |
| 031 | State machine implementation (5+ states per FR-06): idle / loading / loaded / error / decline / stale. Apply `AiSummaryPopover` lazy-load pattern; compose Fluent Popover + Dialog per FR-03 finding. Apply [`.claude/patterns/ui/fluent-v9-portal-gotcha.md`](../../.claude/patterns/ui/fluent-v9-portal-gotcha.md) — MANDATORY. |
| 032 | Topic registry check on mount (FR-05): no orphan sparkles. Read registry via `IDataService` adapter. |
| 033 | Citation rendering (FR-07): type-discriminated (`assessment` → in-product nav; `document` → SPE href); extensible. |
| 034 | Manual refresh button (FR-20): blocking-style invocation with spinner; on completion, card re-renders. |
| 035 | Storybook stories (SC-01): one per state; dark mode variants; props documentation. |
| 036 | Accessibility audit (NFR-04 WCAG 2.1 AA): axe DevTools clean; keyboard navigation works. |
| 037 | ADR-021 dark mode verification (NFR-05): all colors use semantic tokens; light + dark verified. |

### Phase 4 — Matter form integration (Tasks 040–044)

**Goal**: Matter form OnLoad handler + sparkle icon wired to `InsightSummaryCard`.

| Task | Deliverable |
|---|---|
| 040 | Author Matter form OnLoad handler (net-new per Phase 0 finding): read `sprk_performancesummary`, parse as JSON, check `generatedAt` timestamp per FR-17. Handle non-JSON gracefully (legacy R5 placeholder). |
| 041 | Fire-and-forget invocation (FR-18): if `generatedAt` > 1 hour stale OR absent, `Xrm.WebApi`-driven POST to `/api/insights/ask` without await. |
| 042 | Form integration: host `InsightSummaryCard` on Matter Health card; pass current Matter Guid as `subject`. Wire stored summary into card via FR-19 (renders immediately, even if stale). |
| 043 | Package as solution patch (FormXml + JS web resource); deploy via `scripts/Deploy-AllWebResources.ps1` or equivalent dataverse-deploy path. |
| 044 | End-to-end form test: load Matter form → verify TTI unblocked (NFR-03) → verify pre-warm fires → verify card renders. |

### Phase 5 — Telemetry + cache TTL plumbing (Tasks 050–053)

**Goal**: `Sprk.Bff.Api.InsightWidgets` meter live; per-topic TTL plumbed.

| Task | Deliverable |
|---|---|
| 050 | New telemetry source `InsightWidgetsTelemetry.cs` modeled on `R5SummarizeTelemetry.cs`; meter `Sprk.Bff.Api.InsightWidgets` per Q-U8 resolution; bounded dimensions per NFR-06. |
| 051 | Emit `widget.insightcard.invoked` event with tags `{topic, mode, subject, duration, outcome, cacheHit}` from BFF invocation path. |
| 052 | Extend `IInsightsPlaybookExecutionCache` TTL configuration to read from `sprk_aitopicregistry.sprk_cachettlminutes` (default 60). Do NOT add new cache abstraction (audit DR-002). |
| 053 | Concurrency dedup (FR-22): verify simultaneous invocations for same `subject`+`topic`+`mode` dedup via idempotency key; observable in BFF logs. |

### Phase 6 — UAT + documentation (Tasks 060–067)

**Goal**: All success criteria (except SC-12 DEFERRED) demonstrated; tutorial authored.

| Task | Deliverable |
|---|---|
| 060 | UAT scenario authoring: 3 personas (dev Matter ≥3 assessments per area; low-data decline Matter; kill-switch ON dev). Define expected outcomes per SC-05/SC-07/SC-08. |
| 061 | End-to-end UAT execution (SC-05): walkthrough on real dev Matter — sparkle click → narrative + citations → envelope written → second click cache hit (SC-06). |
| 062 | Decline rendering UAT (SC-07): Matter with <2 assessments OR empty Notes → card shows "Insufficient data..." per FR-24. |
| 063 | Kill-switch UAT (SC-08): `DocumentIntelligence:Enabled=false` → BFF returns 503 ProblemDetails → UI graceful error per FR-25. |
| 064 | Degraded-mode UAT (SC-14): empty `spaarke-insights-index` → narrative produced from KPI data alone; limitation documented in user help per FR-26. |
| 065 | Background pre-warm UAT (SC-10): telemetry confirms OnLoad-triggered invocation; UI not blocked. |
| 066 | Telemetry verification (SC-11): App Insights query confirms events with full metadata. |
| 067 | Author `docs/guides/BUILD-A-NEW-INSIGHT-CARD.md` (FR-27 + SC-13) — same engineer as Phase 3 (Q-U7 resolution). Walks through: author playbook → add registry row → drop component → verify. |

### Phase 7 — Deploy + project wrap (Tasks 080, 090)

| Task | Deliverable |
|---|---|
| 080 | Production-ready deploy package: BFF deploy with updated telemetry registration via `bff-deploy` skill (verify NFR-01 ≤60 MB ceiling). Dataverse solution package containing entity + form patch + web resources. |
| 090 | Project wrap-up per [`.claude/skills/task-execute/SKILL.md`](../../.claude/skills/task-execute/SKILL.md) wrap-up protocol: update README status to Complete; create `notes/lessons-learned.md`; verify all 14 active SCs passed (SC-12 carried as DEFERRED). |

---

## 3. Parallel Execution Strategy

Tasks within each phase can largely run sequentially due to dependencies, but **cross-phase parallelism** is possible:

| Wave | Parallel-eligible tasks | Prerequisite |
|---|---|---|
| **Wave 0** | 001, 002, 003, 004, 005 (5 tasks) | Project initialized (✅) |
| **Wave 1A** | 010, 020 (schema design + prompt authoring can run in parallel) | Phase 0 complete |
| **Wave 1B** | 011, 012, 021, 022, 023, 030, 031, 050 (schema deploy + playbook deploy + UI scaffold + telemetry can run in parallel) | 010, 020 complete |
| **Wave 2** | 013, 024, 032, 033, 051, 052, 053, 040 (all middle-stage work) | Wave 1B complete |
| **Wave 3** | 014, 025, 034, 035, 036, 037, 041, 042 | Wave 2 complete |
| **Wave 4** | 043, 044, 060 | Wave 3 complete |
| **Wave 5** | 061, 062, 063, 064, 065, 066 (UAT scenarios in parallel — all read-only) | Wave 4 complete |
| **Wave 6** | 067 (tutorial doc) | Wave 5 complete |
| **Wave 7** | 080 (deploy) | Wave 6 complete |
| **Wave 8** | 090 (wrap-up) | Wave 7 complete |

**Hard sequential constraints**:
- 020 → 021 (action row must exist before playbook references it)
- 010 → 011 → 013 (entity → deploy → seed)
- 030 → 031 → 032 (component scaffold → state machine → registry check)
- 040 → 041 → 042 → 043 (form handler → fire-and-forget → wiring → solution patch)

Final phase counts and exact parallel groups are owned by [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md).

---

## 4. Discovered Resources (from `/project-pipeline` Step 2)

### ADRs (load via `adr-aware` per task)

- **ADR-001** Single BFF runtime
- **ADR-006** UI Surface Architecture (Code Pages, PCF, web resources)
- **ADR-009** Caching — `DistributedCacheExtensions.GetOrCreateAsync<T>` is canonical
- **ADR-010** DI minimalism
- **ADR-012** Shared Component Library
- **ADR-013** AI Architecture (use existing facade per audit DR-003)
- **ADR-014** AI Caching and Reuse Policy — versioned tenant-scoped artifact reuse (inherited via `IInsightsPlaybookExecutionCache`; per-topic TTL extends per FR-21)
- **Audit DR-007** (canonical-architecture-decisions.md §2.7) — Playbook prompts in `sprk_analysisaction.sprk_systemprompt`, NOT `/Prompts/` `.txt` files (citation corrected 2026-06-10 per Task 004 finding; not ADR-014)
- **ADR-018** Kill switches → 503 ProblemDetails
- **ADR-019** ProblemDetails shape
- **ADR-021** Fluent UI v9 + semantic tokens + dark mode (BINDING)
- **ADR-028** Spaarke Auth v2 (reuse record authz)
- **ADR-030** PaneEventBus (NOT used — per-record widget)
- **ADR-031** Stage Lifecycle (NOT used)
- **ADR-032** Null-Object Kill-Switch (relies on existing PR #351 Null peers — no new facade in r1)

### Skills

- `task-execute` (mandatory per task)
- `adr-aware` (auto-loaded per task)
- `widget-design`, `fluent-v9-component` (UI work)
- `jps-action-create`, `jps-playbook-design` (Phase 2)
- `dataverse-create-schema` (Phase 1 schema)
- `code-page-deploy` (only if Matter form delivered as web resource bundle)
- `bff-deploy` (Phase 7)
- `ui-test` (Phase 3, Phase 4)

### Patterns (`.claude/patterns/`)

- `ui/fluent-v9-component-authoring.md`
- **`ui/fluent-v9-portal-gotcha.md`** — MANDATORY for Phase 3 (Popover + Dialog usage)
- `ui/fluent-v9-theming.md`
- `ai/public-contracts-facade.md`
- `ai/endpoint-di-symmetry.md`

### Knowledge docs (`docs/architecture/`, `docs/guides/`)

- `SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`
- `SPAARKEAI-COMPONENT-MODEL.md`
- `BUILD-A-NEW-WORKSPACE-WIDGET.md` (inspiration; r1 widget is per-record not workspace)
- `uac-access-control.md` (Q-U5 evidence)

### Constraints (`.claude/constraints/`)

- `bff-extensions.md` — verify NFR-01 publish-size ceiling on Phase 7 deploy (≤60 MB; baseline 45.65 MB)

### Reference code (mine — do NOT author from scratch)

| Path | Use |
|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/AiSummaryPopover/AiSummaryPopover.tsx` | Lazy-load + popover state machine template |
| `src/client/shared/Spaarke.AI.Widgets/` | Destination package (v0.1.0) |
| `src/client/shared/Spaarke.UI.Components/src/services/ConfigurationService.ts` | `sprk_iconname` resolution pattern |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/predict-matter-cost.playbook.json` | Canonical single-question playbook shape |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/universal-ingest.playbook.json` | Canonical multi-node playbook shape |
| `src/server/api/Sprk.Bff.Api/Telemetry/R5SummarizeTelemetry.cs` | Telemetry meter template (Phase 5) |

### Scripts

- `scripts/Deploy-Playbook.ps1` (Phase 2 task 023)
- `scripts/Deploy-AllWebResources.ps1` (Phase 4 task 043)

### Schema validation context

- Existing entity: `sprk_gridconfiguration` (precedent for `sprk_iconname` field convention)
- Existing entity: `sprk_kpiassessment` (read-only consumer in playbook)
- Existing field: `sprk_matter.sprk_performancesummary` (longtext target for JSON envelope)
- NEW entity: `sprk_aitopicregistry` (Phase 1)

---

## 5. Risk register (top 5)

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| `spaarke-insights-index` pipeline unhealthy at UAT time | Medium | UAT shows degraded mode only | FR-26 + SC-14 explicitly cover degraded path; r1 ships regardless |
| R5 sparkle wiring assumption wrong (no existing wiring) | High | r1 is net-new form customization rather than replacement | Phase 0 Task 002 confirms early; spec assumptions 2/11 already flagged as OPEN INVESTIGATION |
| `Spaarke.AI.Widgets` package v0.1.0 missing required infra (build, lint, exports) | Low | Phase 3 task scaffold delays | Verify in Phase 0 Task 001 |
| BFF cache TTL plumbing requires more than config-only changes | Medium | DI changes triggering ADR-010 + audit DR-008 review | Phase 5 task 052 has explicit "no new abstraction" constraint; if DI change needed, apply Endpoint↔DI Symmetry Rule |
| Owner walkthrough sign-off (SC-15) requires multiple iterations on narrative quality | Medium | Phase 6 timeline extends | Build buffer into Phase 6; iterate on prompt (Phase 2 task 020) early via task 024 smoke test |

---

## 6. Out of scope (this run)

- Implementation (this plan generation only)
- Any `src/` code changes
- New ADRs (NFR-09 forbids)
- Feedback UI (Q-U3 defers to r2+)
- Topics beyond `matter-health` (r2+)
- Modes beyond `single` (r2+)
- Record types beyond `sprk_matter` (r2+)

---

*Plan authored 2026-06-10 via `/project-pipeline` constrained run. Owned by main session; updates flow back as tasks execute.*

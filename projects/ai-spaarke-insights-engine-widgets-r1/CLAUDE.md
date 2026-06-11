# CLAUDE.md — Insights Engine Widgets r1 — project context

> **Project-scoped instructions.** Loads when working in `projects/ai-spaarke-insights-engine-widgets-r1/`.
> **Status**: 🚧 plan + tasks generated 2026-06-10. Ready for `task-execute 001`.

---

## What this project is

A **product surface** project that builds reusable UI components + JPS playbooks for surfacing Spaarke Insights Engine output as **topic/subject scoped Insight Summary cards** on Spaarke record pages. r1 establishes the framework with **Matter Health single-mode** as the first proven topic.

This is **NOT** a platform project. r2 (Insights Engine), the audit (BFF AI architecture), and R5 (Summarize) shipped everything needed. r1 consumes.

---

## What r1 ships

- Reusable `InsightSummaryCard` component in **`@spaarke/ai-widgets`** (FR-03 pre-flight resolved — see Resolution Decisions in spec.md)
- `matter-health-single` JPS playbook
- `sprk_aitopicregistry` Dataverse entity (NEW)
- Matter form OnLoad pre-warm handler (net-new customization — no existing handler found)
- New telemetry meter `Sprk.Bff.Api.InsightWidgets`
- `docs/guides/BUILD-A-NEW-INSIGHT-CARD.md` tutorial (same engineer writes per Q-U7)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing tasks for this project, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

The `task-execute` skill ensures:
- ✅ Knowledge files loaded (ADRs, constraints, patterns)
- ✅ Context tracked in `current-task.md`
- ✅ Proactive checkpointing every 3 steps
- ✅ Quality gates run (code-review + adr-check at Step 9.5)
- ✅ Progress recoverable after compaction
- ✅ Skills invoked correctly (`fluent-v9-component`, `dataverse-create-schema`, `code-page-deploy`, `bff-deploy`, `ui-test`)

**Auto-detection trigger phrases** (per root CLAUDE.md §4):

| User Says | Required Action |
|---|---|
| "work on task X" / "execute task X" | Invoke `task-execute` with task X POML |
| "continue" / "keep going" / "next task" | Read `tasks/TASK-INDEX.md`, find first 🔲, invoke `task-execute` |
| "pick up where we left off" | Load `current-task.md`, invoke `task-execute` |

---

## Key constraints

| Source | Binding rule |
|---|---|
| Audit DR-003 + [`.claude/patterns/ai/public-contracts-facade.md`](../../.claude/patterns/ai/public-contracts-facade.md) | Use existing `IInsightsAi.AnswerQuestionAsync`. Do NOT create new BFF facade for widget invocation — r1 is a consumer. |
| Audit DR-008 + [`.claude/patterns/ai/endpoint-di-symmetry.md`](../../.claude/patterns/ai/endpoint-di-symmetry.md) | If any DI change is needed (unlikely in r1), follow Endpoint↔DI Symmetry Rule + add Null peer if facade. |
| Audit DR-002 + [ADR-009](../../.claude/adr/ADR-009-caching.md) | Use existing `IInsightsPlaybookExecutionCache`. Extend TTL config to read from `sprk_aitopicregistry.sprk_cachettlminutes`. Do NOT add new cache abstractions. |
| Audit canonical prompt pattern (§2.7) | Playbook prompts authored per Spaarke Canonical Prompt Construction Pattern. Prompts live in `sprk_analysisaction.sprk_systemprompt` rows per ADR-014, NOT in `/Prompts/` directories or `.txt` files. |
| r2 multi-entity subject scheme | Use `matter:GUID` for r1. Framework-shape (but don't implement) `matter-collection:` and `cohort:` subjects for r2+. |
| ADR-013 | AI features extend the BFF, not separate services. All AI calls flow through `IInsightsAi.*` facade methods. |
| ADR-018 + ADR-019 | Kill-switch surfaces 503 ProblemDetails (NOT 500). |
| ADR-021 | Fluent UI v9 — all UI uses semantic tokens; dark mode REQUIRED; React 19 for code pages. |
| ADR-032 §F.1 | If r1 adds new conditional DI (unlikely), pair with Null peer + P3 Fail-Fast. |
| Spec NFR-09 | r1 introduces NO new ADR. Operate within audit-codified constraints. |
| **Owner ban on `@v1` vernacular** (Q-U1) | Do NOT use `@v1`/`@vN` identifier-suffix syntax anywhere. Versioning via `sprk_version`/`sprk_versionumber` columns OR `schemaVersion: "1.0"` string in envelope. |

---

## Discovered Resources (loaded by `/project-pipeline` 2026-06-10)

### Applicable ADRs (load via `adr-aware` skill on task start)

| ADR | Title | Why |
|---|---|---|
| ADR-001 | Single BFF runtime, no microservices | r1 extends BFF; verify ceiling |
| ADR-006 | UI Surface Architecture (Code Pages, PCF, Web Resources) | `InsightSummaryCard` is library; Matter form customization is FormXml/JS |
| ADR-009 | Caching (canonical `DistributedCacheExtensions.GetOrCreateAsync<T>`) | Per-topic TTL plumbing |
| ADR-010 | DI minimalism (no new interface seams in r1) | Telemetry meter is standalone |
| ADR-012 | Shared Component Library (`@spaarke/ui-components` as SoT) | `InsightSummaryCard` ships in `@spaarke/ai-widgets` (per FR-03 finding) |
| ADR-013 | AI Architecture (extends BFF, public facade contract) | Use existing `IInsightsAi.AnswerQuestionAsync` directly |
| ADR-014 | Playbook prompts in `sprk_analysisaction.sprk_systemprompt` | Q-U4 — registry routes only; prompt canonical in playbook |
| ADR-018 | Kill switches surface 503 ProblemDetails | FR-25 acceptance |
| ADR-019 | ProblemDetails error response shape | FR-25 |
| ADR-021 | Fluent UI v9 + semantic tokens + dark mode | All UI tasks |
| ADR-028 | Spaarke Auth v2 (managed identity, HMAC webhooks) | Reuse record authz per NFR-08 |
| ADR-030 | PaneEventBus pattern | Not used in r1 (per-record widget) |
| ADR-031 | Stage Lifecycle pattern | Not used in r1 |
| ADR-032 | BFF Null-Object Kill-Switch | FR-25 — facade Null peers throw `FeatureDisabledException` |

### Applicable skills (invoke at task-execute time)

- `task-execute` (mandatory entry point for every task)
- `task-create` (already used — won't be re-invoked during execution)
- `adr-aware` (auto-loads relevant ADRs)
- `widget-design` (architectural guidance for `InsightSummaryCard`)
- `fluent-v9-component` (Fluent v9 conventions, Griffel, semantic tokens)
- `jps-action-create` (author `sprk_analysisaction` rows + system prompt)
- `jps-playbook-design` (author `matter-health-single` playbook)
- `dataverse-create-schema` (create `sprk_aitopicregistry` entity — PAC CLI lacks `pac table create`)
- `code-page-deploy` (only if Matter form customization is delivered as web resource bundle)
- `bff-deploy` (deploy BFF if telemetry meter or cache-TTL plumbing requires config rollout)
- `ui-test` (PCF/form UI verification, ADR-021 dark mode)

### Applicable patterns

- [`.claude/patterns/ui/fluent-v9-component-authoring.md`](../../.claude/patterns/ui/fluent-v9-component-authoring.md)
- [`.claude/patterns/ui/fluent-v9-portal-gotcha.md`](../../.claude/patterns/ui/fluent-v9-portal-gotcha.md) — **MANDATORY** because `InsightSummaryCard` uses Popover + Dialog (Fluent v9 portal re-wrap rule)
- [`.claude/patterns/ui/fluent-v9-theming.md`](../../.claude/patterns/ui/fluent-v9-theming.md)
- [`.claude/patterns/ai/public-contracts-facade.md`](../../.claude/patterns/ai/public-contracts-facade.md)
- [`.claude/patterns/ai/endpoint-di-symmetry.md`](../../.claude/patterns/ai/endpoint-di-symmetry.md) (likely unused in r1 — no DI changes expected)

### Applicable knowledge docs (`docs/architecture/`, `docs/guides/`)

- [`docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md)
- [`docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`](../../docs/architecture/SPAARKEAI-COMPONENT-MODEL.md)
- [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) — archetype decision tree (Note: this card is per-record, not workspace; treat tutorial as inspiration not contract)

### Applicable constraints

- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — even though r1 has no new BFF code, the BFF-publish-size MUST be measured per NFR-01 ceiling (≤60 MB; baseline 45.65 MB)

### Reference code (canonical examples to mine, NOT author from scratch)

- `src/client/shared/Spaarke.UI.Components/src/components/AiSummaryPopover/AiSummaryPopover.tsx` — closest extant inline+popover pattern (FR-03 pre-flight)
- `src/client/shared/Spaarke.AI.Widgets/` (v0.1.0) — destination package; `FeedbackButtons`/`CitationBadge`/`ConfidenceIndicator`/`GroundednessHighlight` siblings (note: `FeedbackButtons` unused in r1 per Q-U3 defer)
- `src/client/shared/Spaarke.UI.Components/src/services/ConfigurationService.ts` — Dataverse → React `sprk_iconname` resolution pattern (Q-U2 evidence)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/predict-matter-cost.playbook.json` + `universal-ingest.playbook.json` — canonical playbook JSON shapes
- `src/server/api/Sprk.Bff.Api/Telemetry/R5SummarizeTelemetry.cs` — direct template for `InsightWidgetsTelemetry.cs` (meter naming + bounded dimensions)

### Applicable scripts

- `scripts/Deploy-Playbook.ps1` — playbook deployment (proven r2 Wave B)
- `scripts/Deploy-AllWebResources.ps1` — form web resource deployment (if Matter form JS shipped as web resource)

---

## Working artifacts

| File | Purpose |
|---|---|
| [`README.md`](README.md) | Project overview, status, dependencies |
| [`design.md`](design.md) | Framework + Matter Health design (source of truth) |
| [`spec.md`](spec.md) | Implementation spec with Resolution Decisions |
| [`plan.md`](plan.md) | 8-phase WBS + Discovered Resources |
| [`current-task.md`](current-task.md) | Active task tracker |
| [`tasks/`](tasks/) | Task POMLs + `TASK-INDEX.md` (parallel groups) |
| [`notes/`](notes/) | Spikes, handoffs, drafts (populated during execution) |
| [`decisions/`](decisions/) | Decision records (DR-001 component reuse, etc.) |

---

## Predecessor + parallel project context

| Project | Relationship to r1 |
|---|---|
| `ai-spaarke-insights-engine-r2` | Predecessor; shipped `IInsightsAi`, multi-entity subjects, SSE, citations — the substrate r1 consumes |
| `bff-ai-architecture-audit-r1` | Codified the patterns r1 follows (PublicContracts facade, Endpoint↔DI Symmetry, Cache Stack) |
| `ai-spaarke-insights-engine-r3` | PAUSED pending R6. r1 ships independently. r3's Tier 2.4 actionable citations would enhance r2+ widgets. |
| `spaarke-ai-platform-unification-r6` | In design. R6 Pillar 3 (`IInvokePlaybookAi`) and Pillar 5/6/9 may inform r2+ widget work but do NOT block r1. |
| `spaarke-ai-platform-unification-r2` (AIPU2) | In design — Cosmos `feedback` container per ADR-015. **Q-U3 defers r1 feedback affordance pending AIPU2 landing on master.** |
| `spaarke-ai-platform-unification-r5` (closed) | Possibly shipped existing sparkle-icon record-section AI. **OPEN INVESTIGATION** (Task 001 deliverable) — research found no Matter form OnLoad handler in src tree. |

---

## Methodology

1. ✅ **Design phase** (complete) — design.md + owner iteration
2. ✅ **Spec phase** (complete) — spec.md generated; 8 open questions resolved
3. ✅ **Plan phase** (complete) — plan.md generated with 8-phase WBS
4. ✅ **Task POMLs** (complete) — `tasks/` populated; `TASK-INDEX.md` lists parallel groups
5. 🔲 **Implementation** (next) — `task-execute` per task

Total: ~4-5 weeks implementation (parallel execution where dependencies allow).

---

## Quick links

- [README.md](README.md) — project overview + dependencies
- [spec.md](spec.md) — implementation specification with Resolution Decisions
- [plan.md](plan.md) — 8-phase WBS + Discovered Resources
- [design.md](design.md) — source-of-truth design
- [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) — task registry + parallel groups
- [current-task.md](current-task.md) — active task

---

*Project context. Plan + tasks generated 2026-06-10 via `/project-pipeline` constrained run. Q-U1..Q-U8 resolved; FR-08 (feedback) deferred to r2+; FR-03 pre-flight applied at plan time.*

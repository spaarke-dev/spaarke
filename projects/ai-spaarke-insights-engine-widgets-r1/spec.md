# Spaarke Insights Engine — Widgets r1 — AI Implementation Specification

> **Status**: Ready for Implementation Review
> **Created**: 2026-06-10
> **Source**: [`design.md`](design.md)
> **Predecessors**: r2 Insights Engine (substrate), bff-ai-architecture-audit-r1 (canonical patterns), R5 (placeholder fields)
> **Parallel**: r3 (paused), R6 (in design)

---

## Executive Summary

r1 delivers a **reusable, topic-scoped, subject-aware Insight Summary widget framework** with **Matter Health single-mode** as the proven first topic. Each insight invocation produces an AI-generated narrative with citations that is **persisted to the host record** for downstream consumption (reports, emails, notifications) and **pre-warmed on form load** for low-latency UX. The framework establishes the pattern; r2+ extend to additional topics, modes, and record types.

---

## Scope

### In Scope

- **Framework**:
  - Reusable `InsightSummaryCard` UI component in `@spaarke/ui-components` (or `@spaarke/ai-widgets` — investigate existing inline-expand-to-modal pattern first)
  - `sprk_aitopicregistry` Dataverse entity mapping topics → playbooks → display config
  - Subject scheme reuse (`matter:GUID`); framework-shape for `matter-collection:` / `cohort:` (NOT implemented in r1)
  - Telemetry events per invocation
  - Caching aligned to 1-hour TTL
  - Background pre-warm on form load (fire-and-forget when stored summary is stale)
  - Manual refresh button forcing invocation
- **Matter Health single-mode topic**:
  - `matter-health-single` JPS playbook authored + deployed to Dataverse
  - 7 baseline diagnostic dimensions in narrative (composite explanation, trend, themes, inflection, critical assessments, risk, evidence gaps)
  - Playbook persists output via existing `UpdateRecord` node to **existing** `sprk_performancesummary` field as a structured JSON envelope (replaces R5 placeholder text content)
- **Matter record integration**:
  - Matter form OnLoad triggers background pre-warm when stale
  - `InsightSummaryCard` wired to existing Matter Health card with sparkle icon
- **UAT**:
  - End-to-end against real dev Matter with ≥3 assessments per performance area
  - Decline rendering test with mocked low-data Matter
  - Kill-switch test with compound-AI gate OFF
- **Documentation**:
  - `BUILD-A-NEW-INSIGHT-CARD.md` tutorial in `docs/guides/`

### Out of Scope

- Workspace narrative widgets (cross-record aggregation in workspace context) → r2+
- Topics other than `matter-health` (e.g., `budget-performance`, `outcome-trends`) → r2+
- Analysis modes other than `single` (portfolio + comparative) → r2+
- Record types other than Matter → r2+
- `matter-collection:` / `cohort:` subject schemes → framework-shaped but unimplemented until r2+
- Actionable citations (`citations[].action`) — depends on r3 Tier 2.4
- Multi-turn / bidirectional clarification — depends on r3 Tier 2.1
- Real-time push-based insight updates (SSE for live UI refresh mid-invocation)
- Scheduled background batch refresh (nightly batch jobs)
- Historical summary audit trail / version comparison
- New BFF PublicContracts facade (use existing `IInsightsAi` directly)
- New node executor types (use existing `UpdateRecord`, `AiAnalysis`, `GroundingVerify`, etc.)
- Modifications to `sprk_kpiassessment` scoring or roll-up logic (owned by other components)
- Multi-tenant support (single-tenant only for r1)
- New ADRs (operate within audit-codified constraints)

### Affected Areas

| Layer | Affected | Description |
|---|---|---|
| **UI components** | `src/client/shared/Spaarke.UI.Components/` or `@spaarke/ai-widgets` | New `InsightSummaryCard` component; investigate existing inline-expand-to-modal patterns to reuse |
| **Matter form** | Power Apps Matter form (Dataverse) | Add `InsightSummaryCard` host; wire OnLoad pre-warm event handler |
| **Dataverse schema** | `sprk_matter` (read+write existing field), `sprk_aitopicregistry` (NEW entity), `sprk_kpiassessment` (read-only) | NO new fields on `sprk_matter` — playbook writes JSON envelope to existing `sprk_performancesummary` longtext; NEW `sprk_aitopicregistry` entity created; consume existing `sprk_kpiassessment` |
| **JPS playbook** | Dataverse `sprk_playbook` + `sprk_playbooknode` rows | New `matter-health-single` playbook; deploy via existing `Deploy-Playbook.ps1` |
| **BFF API** | `src/server/api/Sprk.Bff.Api/` | No new code — consumes existing `IInsightsAi.AnswerQuestionAsync`; configuration for 1-hour cache TTL (may be per-topic via topic registry) |
| **Telemetry** | `src/server/api/Sprk.Bff.Api/Telemetry/` | Reuse `R5SummarizeTelemetry` pattern or new meter for widget invocations |
| **Documentation** | `docs/guides/BUILD-A-NEW-INSIGHT-CARD.md` (NEW) | Authoring tutorial |

---

## Requirements

### Functional Requirements

#### Framework

- **FR-01**: Reusable `InsightSummaryCard` component shipped in `@spaarke/ui-components` (or `@spaarke/ai-widgets` — TBD by investigation). Component props: `{ topic: string, subject: string, mode?: string ("single" default), parameters?: object, kpiSlot?: ReactNode, onCitationClick?: function, onFeedback?: function }`. Acceptance: documented props + Storybook story (or equivalent) demonstrating all 5 states (idle / loading / loaded / error / decline / stale).

- **FR-02**: Component supports inline expand (default, in-card growth) with optional modal expansion for long/complex narratives. Acceptance: configurable threshold for auto-modal-prompt; manual "expand to modal" affordance always available.

- **FR-03**: **Pre-flight investigation**: grep `src/client/shared/Spaarke.UI.Components/` and `@spaarke/ai-widgets` for existing inline-expand-to-modal patterns BEFORE building. If a suitable pattern exists, reuse and extend; if not, build new. Acceptance: investigation note in `notes/` recording findings; design decision documented as `decisions/DR-001-component-reuse.md`.

- **FR-04**: `sprk_aitopicregistry` Dataverse entity created with fields:
  - `sprk_topicname` (text, e.g., `matter-health`) — unique
  - `sprk_mode` (text, e.g., `single`) — combined unique with topicname
  - `sprk_playbookname` (text, FK by name to `sprk_playbook`)
  - `sprk_displayname` (text)
  - `sprk_icon` (text, identifier of icon — convention TBD in spec review)
  - `sprk_hostentity` (text, e.g., `sprk_matter`)
  - `sprk_targetfield` (text, e.g., `sprk_performancesummary`) — the single longtext field on the host entity where the JSON envelope is persisted
  - `sprk_cachettlminutes` (whole number, default 60)
  - `sprk_enabled` (boolean, default true)
  
  Acceptance: entity created via existing Dataverse schema tooling; 1 row seeded for `matter-health` / `single`.

- **FR-05**: Sparkle icon renders **only when** the host entity + topic combination is registered in `sprk_aitopicregistry`. No orphan sparkles. Acceptance: component checks registry on mount; absent registration → no sparkle.

- **FR-06**: Component renders 5+ states:
  - **Idle**: KPI slot + sparkle icon, no narrative
  - **Loading**: skeleton placeholder while invocation runs
  - **Loaded**: narrative (markdown) + citations + "Last updated Nm ago" + optional refresh button
  - **Error (FeatureDisabled)**: graceful "AI summaries unavailable in this environment" + diagnostic logging for ops (per ADR-032 / FeatureDisabledException → 503 contract)
  - **Decline (insufficient evidence)**: same card, narrative replaced with owner-confirmed text: "Insufficient data is available to provide Insights Analysis" + recommended action ("add N more assessments")
  - **Stale**: visible "Refresh available" indicator with refresh button highlighted
  
  Acceptance: all states demonstrably reachable in dev environment.

- **FR-07**: Citation envelope supports multiple citation types (extensible). Initial types:
  - `assessment` → links to specific `sprk_kpiassessment` row (in-product navigation)
  - `document` → links to source document (`spe://drive/X/item/Y` or `sprk_document` Guid)
  
  Acceptance: JSON envelope schema supports `citations[].type` discriminator; UI renders type-appropriate links; owner-confirmed flexibility ("the key is that we build flexible way to surface the context/citations").

- **FR-08**: Feedback affordance: thumbs up/down with optional free-text. Persists to designated table/field for SME calibration. Acceptance: feedback events captured to Dataverse OR App Insights OR (TBD — see Unresolved Questions); minimum: visible in BFF telemetry.

- **FR-09**: Topic registry is SME-editable in Power Apps (Dataverse model-driven app form for `sprk_aitopicregistry`). Acceptance: model-driven app form generated; SME can add a new topic row without code deploy.

#### Matter Health

- **FR-10**: `matter-health-single` JPS playbook authored and deployed to dev Dataverse via existing `Deploy-Playbook.ps1`. Acceptance: playbook row exists in `sprk_playbook`; `Insights:Playbooks:Map.matter-health-single` config Guid set in dev environment.

- **FR-11**: Playbook uses **only existing node executors** (no new node executor types in r1). Specifically uses:
  - `QueryDataverseNode` (or equivalent) to read Matter + KPI assessment rows
  - `IndexRetrieveNode` (or `LiveFactNode`) to read Observations from `spaarke-insights-index` (optional — graceful empty if files-index pipeline unhealthy)
  - `EvidenceSufficiencyNode` to gate on assessment count + Notes presence
  - `AiAnalysis` (or `AiCompletion`) for LLM synthesis
  - `GroundingVerify` for citation verification
  - `UpdateRecord` for persistence to Matter (FR-14)
  - `ReturnInsightArtifact` for final response
  - `DeclineToFindNode` for insufficient-evidence path
  
  Acceptance: playbook JSON validates against r2 universal-ingest pattern; uses only registered node executors per `ActionType` enum.

- **FR-12**: Playbook narrative covers 7 baseline diagnostic dimensions (per design.md §4.2):
  1. Composite grade explanation
  2. Trend over time + inflection points
  3. Recurring themes in Assessment Notes (text mining)
  4. Inflection-point detection (when did decline/improvement start; coincident events)
  5. Most-critical assessments cited as anchors
  6. Forward-looking risk (trajectory + matter age)
  7. Honest evidence-gap acknowledgment (Decline path when insufficient)
  
  Acceptance: UAT against real Matter shows narrative addressing all 7 dimensions; SME review confirms quality bar.

- **FR-13**: KPI Performance Area canonical names per `sprk_kpiassessment.sprk_performancearea` choice field:
  - Guideline Compliance (100,000,000)
  - Budget Compliance (100,000,001)
  - Outcomes Achievement (100,000,002)
  
  Acceptance: playbook prompts use these exact terms; UI display uses these exact terms.

#### Summary Persistence

- **FR-14**: Playbook's `UpdateRecord` node writes the **structured JSON envelope** to **existing** `sprk_matter.sprk_performancesummary` (longtext, R5-era placeholder field). This REPLACES the R5 static placeholder text with the AI-generated envelope. Envelope structure:
  ```json
  {
    "schemaVersion": 1,
    "body": "<markdown narrative>",
    "citations": [
      { "type": "assessment", "id": "<sprk_kpiassessment Guid>", "label": "Q1 2026 Guideline Compliance assessment", "excerpt": "..." },
      { "type": "document", "ref": "spe://drive/X/item/Y", "label": "Engagement Letter", "chunkId": "..." }
    ],
    "generatedAt": "2026-06-10T18:45:00Z",
    "playbookName": "matter-health-single",
    "playbookVersion": "<from sprk_playbook.sprk_version>",
    "tenantId": "<Guid>",
    "dimensions": ["composite", "trend", "themes", "inflection", "critical", "risk", "evidenceGaps"]
  }
  ```
  
  Acceptance: post-invocation, `sprk_performancesummary` contains a valid JSON envelope; envelope schema documented in `notes/insight-envelope-schema.md`.

- **FR-15**: Persisted envelope is consumable by reports, emails, notifications, and other downstream surfaces. Plain-text consumers extract `.body` from the JSON envelope (via Power Fx, plugin code, view/column transformation, or other mechanism owned by downstream consumer). r1 does NOT implement downstream consumers — it ENABLES them. Acceptance: schema documentation includes `.body` extraction guidance for downstream consumers (Power Fx example, plugin example).

- **FR-16**: No new Dataverse fields created in r1 for Matter Health persistence — `sprk_performancesummary` is the single persistence sink. Acceptance: schema change set in r1 does NOT add fields to `sprk_matter`; only modifies content of existing `sprk_performancesummary` via playbook.

#### Pre-warm + Caching

- **FR-17**: Matter form OnLoad event handler reads `sprk_performancesummary`, parses as JSON, and checks `.generatedAt` timestamp. Acceptance: handler implemented in form customization; reads field via Xrm.WebApi or equivalent; gracefully handles non-JSON content (legacy R5 placeholder text) by treating as "no stored summary" (triggers refresh).

- **FR-18**: If stored summary is >1 hour stale OR absent, OnLoad handler fires fire-and-forget background invocation to `/api/insights/ask` (or equivalent endpoint). Acceptance: handler does NOT await response; UI interactivity not blocked.

- **FR-19**: While background invocation runs, UI renders the existing (stale or freshly-loaded) stored summary immediately. Acceptance: zero perceptible UI block on form load.

- **FR-20**: Manual refresh button on the card forces invocation regardless of cache age. Acceptance: clicking refresh triggers blocking-style invocation (spinner shown); on completion, card re-renders with new content.

- **FR-21**: Server-side cache TTL **per-topic, configured via `sprk_aitopicregistry.sprk_cachettlminutes`**. Default for `matter-health` is 60 minutes (deviates from r2's universal 15-minute `IInsightsPlaybookExecutionCache` default). Acceptance: cache layer reads TTL from topic registry; verified via cache hit/miss behavior in UAT.

- **FR-22**: Concurrency: simultaneous background invocations for same `subject`+`topic`+`mode` are deduplicated via idempotency key. Acceptance: second concurrent invocation observed in BFF logs as deduplicated; only one playbook execution runs.

#### UAT + Documentation

- **FR-23**: End-to-end UAT against a real dev Matter with ≥3 assessments per Performance Area. Acceptance: walkthrough demonstrates sparkle click → narrative + citations rendered → persisted to record → second click serves from cache.

- **FR-24**: Decline rendering verified with a Matter that has <2 assessments OR empty Notes. Acceptance: card shows "Insufficient data..." message; no error state.

- **FR-25**: Kill-switch test: `DocumentIntelligence:Enabled=false` in dev → card shows graceful "AI summaries unavailable" state. Acceptance: BFF returns 503 (NOT 500) per audit DR-003 / PR #351 LATENT BUG fix; UI handles gracefully.

- **FR-26**: Degraded mode UAT: with `spaarke-insights-index` empty (Phase A files-index pipeline unhealthy scenario), narrative is produced from KPI roll-up + assessment text alone; document-grounded citations are empty. Acceptance: narrative is still useful; limitation documented in user-facing help.

- **FR-27**: Tutorial documentation `docs/guides/BUILD-A-NEW-INSIGHT-CARD.md` authored. Acceptance: doc walks through: (a) author a JPS playbook, (b) add a row to `sprk_aitopicregistry`, (c) drop `InsightSummaryCard` onto a host form, (d) verify end-to-end. Targets developers + SMEs.

### Non-Functional Requirements

- **NFR-01**: Insight invocation duration p95 ≤5s (one-shot, non-streaming). Acceptance: telemetry confirms in UAT.

- **NFR-02**: Cache hit response p95 ≤100ms. Acceptance: telemetry confirms.

- **NFR-03**: Background pre-warm invocation does NOT block UI interactivity. Acceptance: form load TTI (time-to-interactive) unaffected by pre-warm.

- **NFR-04**: Component accessibility per WCAG 2.1 AA. Acceptance: axe DevTools audit clean; keyboard navigation works.

- **NFR-05**: Component honors Spaarke Fluent v9 design system theming. Acceptance: light/dark theme rendering verified.

- **NFR-06**: Telemetry events emitted per invocation: `widget.insightcard.invoked` (with `topic`, `mode`, `subject`, `duration`, `outcome`, `cacheHit`). Acceptance: events visible in App Insights.

- **NFR-07**: Single-tenant scope only for r1. Cross-tenant federation NOT supported. Acceptance: design + code reviewed for tenant-leak risk; tenantId always set.

- **NFR-08**: Reuse existing record authz (no new authz layer). Acceptance: user with Matter read access can invoke the insight; user without Matter read access gets 403 (existing behavior).

- **NFR-09**: r1 must NOT introduce any new ADR. Operates within audit-codified constraints. Acceptance: no `docs/adr/` additions in this project.

---

## Technical Constraints

### Applicable ADRs

- **ADR-013** — AI Architecture: AI features extend BFF; CRUD-side consumers route through `Services/Ai/PublicContracts/` facades. r1 consumes existing `IInsightsAi.AnswerQuestionAsync` directly — no new facade.
- **ADR-018** — Kill switches: feature gates must surface 503 ProblemDetails to clients (NOT 500).
- **ADR-019** — ProblemDetails: error response shape.
- **ADR-032** — Null-Object Fail-Fast: facade Null peers throw `FeatureDisabledException` with stable `errorCode`. r1 relies on existing PR #351 Null peers (no new facades to add Null peers for).
- **ADR-009** — Caching: use canonical `DistributedCacheExtensions.GetOrCreateAsync<T>`; in-process MemoryCache requires explicit ADR-009 exception XML doc.
- **ADR-014** — Playbook prompts in Dataverse `sprk_analysisaction.sprk_systemprompt`, NOT .txt files.
- **ADR-010** — DI minimalism (no new interface seams in r1).
- **ADR-001** — Single BFF runtime, no microservices.

### MUST Rules

- ✅ MUST use `IInsightsAi.AnswerQuestionAsync` for playbook invocation (per audit DR-003 facade pattern)
- ✅ MUST follow Spaarke Canonical Prompt Construction Pattern (per audit canonical-architecture-decisions.md §2.7)
- ✅ MUST use existing node executors only (UpdateRecord, AiAnalysis, GroundingVerify, etc.); NO new node executor types in r1
- ✅ MUST use existing `IInsightsPlaybookExecutionCache` (extend TTL configuration, do NOT add new cache abstraction; per audit DR-002)
- ✅ MUST surface kill-switch as 503 ProblemDetails (per ADR-018 + audit PR #351)
- ✅ MUST persist via `UpdateRecord` node (NOT a new BFF persistence service)
- ✅ MUST honor Endpoint↔DI Symmetry Rule for any DI changes (per audit DR-008 / `.claude/patterns/ai/endpoint-di-symmetry.md`) — r1 should not need DI changes
- ✅ MUST authorize via existing Matter record authz (no new authz layer)
- ✅ MUST persist the JSON envelope to existing `sprk_performancesummary` (single field — no new schema)
- ❌ MUST NOT add new BFF PublicContracts facade in r1
- ❌ MUST NOT modify `sprk_kpiassessment` schema or scoring logic
- ❌ MUST NOT modify roll-up calculation
- ❌ MUST NOT introduce new ADR
- ❌ MUST NOT use `@v1` or other version suffixes in playbook names (versioning via `sprk_version`/`sprk_versionumber`)
- ❌ MUST NOT bypass the topic registry (sparkle icon conditional on registration)

### Existing Patterns to Follow

- See [`.claude/patterns/ai/public-contracts-facade.md`](../../.claude/patterns/ai/public-contracts-facade.md) for facade interaction pattern
- See [`.claude/patterns/ai/endpoint-di-symmetry.md`](../../.claude/patterns/ai/endpoint-di-symmetry.md) for DI changes (unlikely in r1)
- See [`projects/bff-ai-architecture-audit-r1/notes/canonical-architecture-decisions.md`](../bff-ai-architecture-audit-r1/notes/canonical-architecture-decisions.md) §2.7 for prompt construction pattern
- See `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/universal-ingest.playbook.json` for canonical multi-node JPS playbook structure
- See `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Playbooks/predict-matter-cost.playbook.json` for canonical single-question playbook structure
- See `R5SummarizeTelemetry` (in `Sprk.Bff.Api.Telemetry`) for invocation telemetry pattern
- See `src/client/shared/Spaarke.UI.Components/` for shared Fluent v9 component conventions
- Investigate `@spaarke/ai-widgets` for existing AI widget patterns (per FR-03)

---

## Success Criteria

1. [ ] **SC-01**: `InsightSummaryCard` component shipped with documented props + Storybook (or equivalent) covering all 5+ states — Verify: component renders in dev sandbox
2. [ ] **SC-02**: `matter-health-single` playbook deployed to dev Dataverse — Verify: `Insights:Playbooks:Map.matter-health-single` Guid set; playbook invokable via `/api/insights/ask`
3. [ ] **SC-03**: `sprk_aitopicregistry` entity created with 1 seeded row — Verify: row exists; SME-editable in model-driven app
4. [ ] **SC-04**: JSON envelope schema documented + valid envelope written to `sprk_performancesummary` on UAT Matter — Verify: parse the field content as JSON, validate against documented schema in `notes/insight-envelope-schema.md`
5. [ ] **SC-05**: End-to-end UAT: real dev Matter → sparkle click → narrative + citations rendered → `sprk_performancesummary` updated with JSON envelope (R5 placeholder text overwritten) — Verify: walkthrough video or stakeholder demo
6. [ ] **SC-06**: Cache hit on second click within 1-hour TTL window — Verify: telemetry shows `cacheHit=true`; response <100ms
7. [ ] **SC-07**: Decline rendering for Matter with insufficient assessments — Verify: card shows "Insufficient data..." with no error
8. [ ] **SC-08**: Graceful FeatureDisabledException rendering when compound-AI gate is OFF — Verify: BFF returns 503; UI shows graceful error
9. [ ] **SC-09**: Manual refresh button forces re-invocation — Verify: cache miss on refresh click
10. [ ] **SC-10**: Background pre-warm fires on form load when stored summary stale — Verify: telemetry shows OnLoad-triggered invocation; UI not blocked
11. [ ] **SC-11**: Telemetry events emitted with full metadata — Verify: App Insights query confirms
12. [ ] **SC-12**: Feedback (thumbs up/down) captured — Verify: at least 1 captured event observable
13. [ ] **SC-13**: `BUILD-A-NEW-INSIGHT-CARD.md` authored — Verify: doc in `docs/guides/` walks through the full flow
14. [ ] **SC-14**: Degraded-mode UAT: when `spaarke-insights-index` is empty, narrative still produced from KPI data alone — Verify: limitation documented
15. [ ] **SC-15**: Owner walkthrough sign-off — Verify: documented owner approval of UX + narrative quality

---

## Dependencies

### Prerequisites (hard — must exist)

- r2 Insights Engine on master (✅ all PRs merged 2026-06-04)
- Audit PublicContracts pattern on master (✅ PR #351 + PR #360)
- `sprk_kpiassessment` entity + roll-up logic (✅ existing platform)
- Existing R5 placeholder field `sprk_performancesummary` on `sprk_matter` (✅ existing)
- `Deploy-Playbook.ps1` deployment tooling (✅ proven in r2 Wave B)
- `sprk_playbook` + `sprk_playbooknode` tables (✅ existing)
- 18 existing node executors including `UpdateRecord`, `AiAnalysis`, `GroundingVerify`, etc. (✅ per audit)

### Prerequisites (soft — improve UAT realism)

- `spaarke-files-index` → `spaarke-insights-index` pipeline healthy (⚠️ in current debugging stream)
- `AiProcessingOptions:InsightsIngest=true` in target env (⚠️ env config)

### Explicitly NOT dependencies (parallel work can ship freely)

- R6 Pillar 3 `IInvokePlaybookAi` facade — r1 uses existing `IInsightsAi.AnswerQuestionAsync` directly
- R6 Pillar 5 schema-aware renderers — r1 defines its own rendering
- R6 Pillar 6 workspace state model — r1 doesn't touch workspace
- R6 Pillar 9 widget visibility contract — r1 widget is per-record
- r3 Wave 2 InsightsIntentClassifier reconciliation — r1 uses playbook path directly
- r3 Tier 2.4 actionable citations — r1 ships display-only citations

### External Dependencies

- None (no third-party services beyond Azure OpenAI which is already integrated)

---

## Owner Clarifications

Captured 2026-06-10 from owner interview:

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Topic registry location | Dataverse entity or config? | Dataverse entity `sprk_aitopicregistry` | SME-editable registration; new entity schema work in r1 |
| Mode separation | Separate playbooks or parameterized? | **Separate** playbooks per mode | Cleaner per-mode prompts; matches r2 patterns |
| r1 mode scope | Which mode? | **Single only** | Drops portfolio + comparative scope; framework-shaped but unimplemented |
| Insight dimensions | Which of 14? | **§4.2 baseline 1-7 only** | Composite, trend, themes, inflection, critical, risk, evidence gaps |
| Card UX | Inline vs modal vs auto-load? | **Inline with optional modal expansion**; investigate existing component | UI investigation work pre-build |
| Streaming | SSE or one-shot? | **One-shot + background pre-warm** for freshness | Simpler component; freshness via FR-17 to FR-22 |
| Citations | Assessment row, source doc, both? | **Both — flexible** ("the key is that we build flexible way to surface the context/citations") | Citation envelope supports type discriminator |
| Decline UX | Same card or separate? | **Same card** with "Insufficient data is available to provide Insights Analysis" message | Simpler UI state model |
| Feedback affordance | r1 or defer? | **r1** | SME calibration data starts at launch |
| Cache TTL | TTL or per-action? | **1-hour TTL + manual refresh + background pre-warm** | Per-topic TTL via topic registry (FR-21) |
| Multi-tenant | r1 considerations? | **Single-tenant for r1** | Cross-tenant deferred |
| Privilege model | New layer or reuse? | **Reuse record authz** | No new authz layer |
| R5 reuse | What did R5 ship? | **R5 only added sparkle placeholder linked to `sprk_financialsummary`, `sprk_performancesummary`, `sprk_tasksummary` static text — NOT AI-generated** | r1 builds the AI pattern; can REPLACE content in `sprk_performancesummary` with AI narrative |
| **Summary persistence (NEW)** | Save summaries to record? | **Yes — JSON envelope written to existing `sprk_performancesummary` field** (no new schema); enables reports/emails/notifications | NEW: FR-14, FR-15, FR-16; consumers extract `.body` for plain-text use |
| **Pre-warm trigger (NEW)** | Background refresh on form load? | **Yes** — first-of-day OR last-hour stale triggers fire-and-forget refresh | NEW: FR-17 to FR-22 |
| **Playbook model utilization (NEW)** | Use playbook power? | **Yes — leverage existing nodes/scopes** | Persistence via existing `UpdateRecord` node; no new executor types |
| KPI Performance Area names | Canonical names? | Per `sprk_performancearea` choice field: **Guideline Compliance (100,000,000), Budget Compliance (100,000,001), Outcomes Achievement (100,000,002)** | Playbook prompts + UI use these exact terms (corrects earlier "Outcome Compliance" / "Outcomes Success") |
| Phase A degraded mode | Block on files-index health? | **Ship anyway — narrative on KPI data alone**; document limitation | FR-26 |
| Orphan sparkle behavior | Always shown or registered-only? | **Registered only** | FR-05 |

---

## Assumptions

Where owner did not explicitly answer, proceeding with these assumptions. Open to override in implementation:

1. **JSON envelope schema (FR-14)** — assumed structure documented; please confirm or amend during spec review
2. **R5 popup wiring removal** — assumed the current R5 sparkle-icon-to-popup wiring on Matter form (linked to `sprk_performancesummary`) is REPLACED by the new `InsightSummaryCard` host. Old R5 popup is decommissioned. Please confirm during implementation grep of R5 source.
3. **Pre-warm scope** — form load only; scheduled background batch refresh deferred to r2
4. **History** — always overwrite "current"; no audit trail in r1
5. **Stored summary stale threshold** — 1 hour aligned to cache TTL (configurable per-topic via registry per FR-21)
6. **Topic registry fields** (FR-04) — proposed schema; please confirm/amend during implementation
7. **Telemetry destination** — App Insights via existing `R5SummarizeTelemetry` pattern; new meter name `Spaarke.InsightWidgets` (subject to renaming)
8. **Feedback storage** — TBD: dedicated Dataverse table OR App Insights custom event OR both (see Unresolved Q-U3)
9. **UI library home** — investigate `@spaarke/ai-widgets` first per FR-03; fall back to `@spaarke/ui-components` if no existing pattern
10. **`sprk_performancesummary` backwards compat** — assumed safe to overwrite R5's static placeholder text; please confirm no other consumers depend on the placeholder content
11. **R5 sparkle icon currently wired** — assumed exists on Matter form pointing to `sprk_performancesummary`; r1 replaces this wiring with new `InsightSummaryCard`; confirm via R5 source grep during implementation
12. **Modal expansion threshold** — UX TBD: auto-prompt modal when narrative >N characters/lines, or always manual expand only? Suggest manual-only for r1 (simpler)

---

## Unresolved Questions

Need answers before or during implementation (some may resolve during spec review iteration):

- [ ] **Q-U1**: JSON envelope `schemaVersion` field — should this be `int` (1, 2, ...) or `string` (`"1.0"`, `"1.1"`)? Implementation detail; suggest `int` for r1 simplicity
- [ ] **Q-U2**: Topic registry's `sprk_icon` field convention — string ID matching a Fluent icon? Inline SVG? Icon URL? Suggest string ID matching `@fluentui/react-icons` for r1
- [ ] **Q-U3**: Feedback storage destination — dedicated Dataverse `sprk_insightfeedback` table, or App Insights custom event, or both? Recommendation: both (Dataverse for SME query + App Insights for analytics)
- [ ] **Q-U4**: Should the `sprk_aitopicregistry` row carry the `sprk_systemprompt` reference OR is the playbook the canonical source of prompt? Recommendation: playbook is canonical (per ADR-014); registry references playbook by name only
- [ ] **Q-U5**: Are there any field-level security (FLS) considerations on `sprk_performancesummary` (the persistence field)? Confirm with security team — content now carries citation refs to assessment rows which may have stricter FLS
- [ ] **Q-U6**: Should the model-driven app form (Matter) trigger pre-warm via Power Apps form OnLoad event handler, or via a Power Automate flow, or via a React Code Page wrapper? Suggest form OnLoad for simplicity
- [ ] **Q-U7**: Schedule + ownership of `BUILD-A-NEW-INSIGHT-CARD.md` tutorial (FR-27) — same engineer as component author or separate doc writer? Affects task POML decomposition
- [ ] **Q-U8**: Does `r3` Wave 1.4 telemetry maturity (`InsightsActionLookupFailed` + dashboards) need to coordinate with this project's telemetry? Recommend separate meters; can converge later

---

*AI-optimized specification. Original design: [`design.md`](design.md). Generated by `/design-to-spec` 2026-06-10.*

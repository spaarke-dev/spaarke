# Spaarke Insights Engine — Widgets r1 — Design

> **Status**: 🔄 INITIAL DRAFT FOR ITERATION (2026-06-10)
> **Author**: Initial draft by Claude Code per owner brief; owner-driven refinement to follow
> **Predecessor design context**: [`r2/PHASE-2-OUTLINE.md`](../ai-spaarke-insights-engine-r2/PHASE-2-OUTLINE.md), [`bff-ai-architecture-audit-r1/notes/canonical-architecture-decisions.md`](../bff-ai-architecture-audit-r1/notes/canonical-architecture-decisions.md)

---

## 1. Purpose

Establish a **reusable framework** for surfacing Spaarke Insights Engine output to end users via **subject-scoped, topic-themed narrative summary widgets** — and prove the framework end-to-end with **Matter Health** as the first topic on the Matter record surface.

After r1, adding new insight surfaces (different topics, different record types, different aggregation scopes) becomes "author a JPS playbook + register a topic + the framework handles UI / caching / streaming / citations for free."

---

## 2. Architectural framing

### 2.1 What this project is NOT

- NOT a new BFF AI surface — r2 + R5 + audit shipped everything needed
- NOT a new authoring tool — JPS playbook authoring happens in existing Playbook Builder
- NOT a workspace widget pattern — workspace narratives are r2+ scope
- NOT responsible for the underlying KPI scoring — `sprk_kpiassessment` + Matter roll-up fields are owned by other components; r1 consumes the existing structured data

### 2.2 What this project IS

- A **shared UI component pattern** (`InsightSummaryCard`) that any record-section can adopt
- A **subject-scoping convention** that lets the same playbook drive single-record, portfolio, and comparative narratives
- A **topic registration mechanism** that maps "what insight does this card show" to "which playbook runs"
- A **caching + telemetry harness** that ensures consistent UX across all insight surfaces (loading, error, refresh, "last updated", failure to 503)

### 2.3 Where this fits in the Spaarke Canonical AI Stack

Per the audit's [`canonical-architecture-decisions.md`](../bff-ai-architecture-audit-r1/notes/canonical-architecture-decisions.md), this project consumes:

- **Layer 3** (Spaarke Public-Contracts Facade DI Fascia) → uses `IInsightsAi.AnswerQuestionAsync`
- **Layer 5** (Spaarke Canonical Intent Classifier Pattern) → optionally uses `InsightsIntentClassifier` for routing (Phase 2 candidate)
- **Layer 6** (Spaarke Canonical Search Substrate Architecture) → reads from `spaarke-insights-index` via the existing facade
- **Layer 7** (Spaarke Canonical Prompt Construction Pattern) → playbook prompts authored per the canonical 4-element pattern

It does NOT modify any of these layers. It is a **consumer**, not a contributor.

---

## 3. The Insight Summary Framework

### 3.1 Core abstractions

#### 3.1.1 Topic

A named domain concept the insight illuminates. Examples:
- `matter-health` (r1)
- `budget-performance` (future)
- `outcome-trends` (future)
- `upcoming-tasks` (future)
- `case-load-pressure` (future)

A topic binds:
- A display name and icon
- A canonical JPS playbook (or set of playbooks for multi-mode topics)
- A default subject-scope policy
- A rendering schema (markdown body, citations list, optional structured metric block)

#### 3.1.2 Subject

What the insight is about. Reuses r2 Wave D's multi-entity subject scheme:

| Subject form | Meaning | Example |
|---|---|---|
| `matter:GUID` | A single Matter record | `matter:1a2b3c4d-...` |
| `project:GUID` | A single Project record | `project:5e6f7g8h-...` |
| `invoice:GUID` | A single Invoice record | `invoice:9i0j1k2l-...` |
| `matter-collection:{filterRef}` | A filtered collection of Matters (NEW for portfolio mode) | `matter-collection:practiceArea=BNKF,year=2026` |
| `cohort:{cohortRef}` | A peer-cohort for comparative analysis (NEW) | `cohort:practiceArea=BNKF,attorney={GUID}` |

For r1, **only `matter:GUID` ships**. Portfolio + cohort subjects are framework-shaped but unused until r2+.

#### 3.1.3 Analysis Mode

A topic can support multiple analysis modes. For Matter Health:

| Mode | Subject scope | Question pattern |
|---|---|---|
| **Single (diagnostic)** | One matter | "Why is THIS matter graded X?" |
| **Portfolio (aggregate)** | Many matters | "How is the practice doing on Guideline Compliance overall?" |
| **Comparative (cohort)** | One matter + peer cohort | "How does this matter compare to similar matters?" |

Modes can be:
- **Separate playbooks** (`matter-health-single`, `matter-health-portfolio`, `matter-health-comparative`)
- **OR one parameterized playbook** with a `mode` parameter

**Recommendation**: separate playbooks per mode. Cleaner reasoning, simpler JPS, easier to iterate prompts per mode. Pattern matches r2's `predict-matter-cost` style — one playbook per discrete question.

**Naming convention note**: Playbook names do NOT carry version suffixes (no `@v1`, `-v1`, etc.). Versioning is tracked on the `sprk_playbook` Dataverse row via the existing `sprk_version` / `sprk_versionumber` fields. Names stay stable across versions; the version field changes. Historical references to `predict-matter-cost@v1`, `universal-ingest@v1` in r2 documentation reflect the convention at the time those playbooks were authored — going forward, this project (and recommended for r3 / R6) uses bare names.

**r1 scope**: only **Single (diagnostic)** ships. Other modes are framework-shaped but unimplemented until r2+.

#### 3.1.4 InsightSummaryCard component

A reusable UI component:
- Slot for structured KPI display (provided by host)
- Sparkle icon trigger for AI narrative
- Loading state (skeleton)
- Narrative rendering (markdown)
- Citations list (clickable, expandable)
- Error state with graceful fallback (503 FeatureDisabledException → "AI summaries unavailable in this environment")
- "Last updated" indicator
- Optional refresh button
- Expand/collapse for long narratives
- Footer: feedback affordance (thumbs up/down for SME calibration — Phase 2 SC-15 data collection)

Component contract: takes a `topic` (string), a `subject` (string), an optional `mode` (string, default `single`), and renders. Host provides the KPI display slot.

### 3.2 Topic registry

A small Dataverse-side or config-side registry maps topics to playbooks:

| Topic | Mode | Playbook (canonical name) | Display name | Icon |
|---|---|---|---|---|
| `matter-health` | `single` | `matter-health-single` | "Matter Health Diagnostic" | sparkle |
| `matter-health` | `portfolio` | `matter-health-portfolio` | "Matter Health Portfolio" | sparkle |
| `matter-health` | `comparative` | `matter-health-comparative` | "Matter Health Peer Comparison" | sparkle |
| `budget-performance` | `single` | `budget-performance-single` | ... | ... |
| ... | ... | ... | ... | ... |

For r1, the registry is small (one row: `matter-health` / `single`). The shape supports easy extension.

**Open question**: registry as `sprk_insighttopic` Dataverse entity (SME-editable), or as a config block in App Service settings (engineer-editable)? Recommendation: Dataverse entity, following the r2 + R6 pattern of "registries as data, not code."

### 3.3 End-to-end flow

```
User views Matter record
         ↓
Matter Health card renders structured KPIs (existing — already shipped)
         ↓
User clicks sparkle icon on the card
         ↓
InsightSummaryCard fires invocation:
  POST /api/insights/ask
  {
    playbookId: <resolved from topic="matter-health" + mode="single">,
    subject: "matter:GUID",
    parameters: { ...optional KPI context passed by host... }
  }
         ↓
BFF (existing IInsightsAi.AnswerQuestionAsync):
  - Cache check (IInsightsPlaybookExecutionCache, 15-min TTL)
  - On miss: invoke playbook (matter-health-single)
    - Playbook reads Matter roll-up fields (Guideline/Budget/Outcome scores)
    - Playbook reads sprk_kpiassessment rows scoped to matter (text Notes + Criteria)
    - Playbook reads Observations from spaarke-insights-index scoped to matter
    - Playbook synthesizes narrative + citations
  - Returns InsightArtifact OR structured DeclineResponse
         ↓
Widget renders:
  - Narrative (markdown)
  - Citations (linked back to specific assessments / documents)
  - "Last updated 12 seconds ago"
         ↓
User can refresh (forces cache miss), expand details, link to source
```

---

## 4. First Topic: Matter Health

### 4.1 Domain model (provided by owner)

- **Entity**: `sprk_kpiassessment`
- **Performance Area choice field** (`sprk_performancearea`, canonical):
  | Label | Value |
  |---|---|
  | Guideline Compliance | 100,000,000 |
  | Budget Compliance | 100,000,001 |
  | Outcomes Achievement | 100,000,002 |
- **Scoring**: letter grades (A+, A, B+, B, etc.) mapped to numeric scores (1.0, 0.9, 0.85, 0.8, etc.) — mapping owned by other components
- **Submission cadence**: multiple assessments per metric area over time per matter
- **Matter roll-up**: matter record has roll-up fields summarizing the running scores (used by the existing Matter Health Visual Host card)
- **Per-assessment fields**: Assessment Criteria (text), Assessment Notes (text)
- **Pre-existing R5 placeholder fields on Matter** (longtext): `sprk_financialsummary`, `sprk_performancesummary`, `sprk_tasksummary` — populated by R5 as static text with simple popup display, NOT AI-generated. r1 will REPLACE the populated content for `sprk_performancesummary` with the AI-generated narrative body. See §4.5 for persistence design.

### 4.2 Insight dimensions (single-matter, diagnostic mode for r1)

The single-matter diagnostic playbook answers: **"Why is this matter graded {currentGrade}, what's driving the score, and what's changing?"**

Reasoning inputs (the playbook reads):

| Input source | What it gives the playbook |
|---|---|
| Matter roll-up fields | Current letter grade per area, current numeric scores, total assessment count per area |
| `sprk_kpiassessment` rows scoped to matter | Per-assessment grades, dates, text Criteria, text Notes — the full evaluative history |
| Matter metadata | Practice area, matter type, assigned attorney, assigned OC attorney, OC firm, open-date / age, status |
| Spaarke-insights-index Observations scoped to `matter:GUID` | Document-level evidence (e.g., grounded extractions from underlying matter documents — if files-index pipeline is healthy) |

Reasoning dimensions (the playbook synthesizes):

1. **Composite grade explanation** — "F grade driven by Guideline Compliance at 20% (D-, 3 recent assessments) despite strong Budget Compliance at 90%."
2. **Trend** — "Guideline Compliance has declined over the last 4 assessments (B → C+ → C → D-); Budget improving (B+ → A-)."
3. **Recurring themes in Notes** — extract recurring concepts (e.g., "missed deadlines", "scope creep", "client communication gaps") with citations to specific Notes.
4. **Inflection-point detection** — "Decline started Q1 2026, coincident with attorney reassignment."
5. **Most-critical assessments** — highlight 1-3 assessments with the lowest scores + most substantive Notes as anchor citations.
6. **Forward-looking risk** — given current trajectory + matter age, what's the risk profile?
7. **Honest acknowledgment of evidence gaps** — if too few assessments OR text Notes are sparse, the playbook returns a structured Decline ("insufficient evidence to diagnose; recommend X assessments to enable narrative").

### 4.3 Additional insight candidates (owner-iteration territory)

Beyond the dimensions above, the following are good candidates the playbook could surface OR future modes could highlight. Owner picks which to ship in r1 vs defer.

| Candidate | Mode it fits best | Notes |
|---|---|---|
| **Variance / consistency** — are scores consistent or erratic? | single | Erratic scoring can signal unclear assessment criteria; valuable for QA |
| **Velocity / freshness** — last assessment date, gap from previous, cadence regularity | single | Stale assessments may indicate disengagement; cadence indicates active management |
| **Cross-metric correlations** — does Budget Compliance lag Outcome Compliance? | single + portfolio | Predictor pattern (e.g., budget pressure precedes outcome decline) |
| **Outlier detection** — this matter vs peer cohort of same area/type | comparative | Requires cohort subject scheme; defer to r2+ |
| **Best-in-class benchmarking** — within practice area, top quartile score profile | portfolio + comparative | Same as outlier but anchored to a fixed reference |
| **Time-to-recover** — for matters that declined + recovered, what interventions worked? | portfolio | Requires historical analysis across many matters; defer to r2+ |
| **Assessor consistency** — multiple assessors of same matter, do they agree? | single | Detect calibration drift; relevant for QA |
| **Assessor bias** — does same attorney/role consistently score higher/lower? | portfolio | Sensitive; raises personnel-management concerns; requires owner direction |
| **Practice-area benchmarking** — does this matter type typically score this way for this area? | comparative | Useful context for whether F grade is alarming or typical |
| **Pending-duration pressure** — older matters drift in scoring; flag if score declined as age increased | single | Combines age + trend |
| **Composite risk score** — weighted combination of trend + variance + freshness + roll-up | single + portfolio | Could replace the letter grade with a forward-looking risk indicator |
| **Critical-period detection** — is this an early-matter or near-close assessment? Different baselines apply | single | Matters near close often have different score patterns than mid-life |
| **Free-text theme extraction** — what concepts recur across Notes? Tag clouds, top concerns | single + portfolio | LLM-friendly; high signal-to-noise |
| **Comparative cohort views by attorney / firm / area** | portfolio + comparative | Map to standard reporting dimensions |

**Recommendation for r1**: ship the diagnostic playbook with dimensions 1–7 in §4.2 (composite explanation, trend, themes, inflection, critical assessments, risk, evidence gaps). Defer variance / velocity / outlier / cohort to r2+. The diagnostic is the highest-signal first capability and can be authored without portfolio/cohort subject scheme work.

### 4.4 Playbook design (Matter Health Single)

JPS playbook structure (high level):

```
matter-health-single
├── parameterSchema: { matterId: GUID, tenantId: GUID, currentGrade?: string }
├── nodes (probable shape — to be finalized in spec.md):
│   ├── 1. resolveMatterContext
│   │      → reads sprk_matter (roll-up scores, metadata)
│   ├── 2. fetchAssessments
│   │      → reads sprk_kpiassessment rows scoped to matter
│   │      → ordered by assessment date
│   ├── 3. fetchObservations (optional)
│   │      → reads spaarke-insights-index Observations scoped to matter
│   │      → may return empty if files-index pipeline is unhealthy
│   ├── 4. evidenceSufficiencyGate
│   │      → if assessments < 2 OR Notes empty: emit Decline
│   │      → otherwise: continue
│   ├── 5. analyzeAndSynthesize (LLM)
│   │      → prompt assembled per Spaarke Canonical Prompt Construction Pattern
│   │      → reads Criteria + Notes for theme extraction
│   │      → reasons over trend, inflection, critical assessments
│   │      → produces structured InsightArtifact: { headline, narrative, citations[] }
│   ├── 6. groundingVerify
│   │      → mechanical citation verifier (D-P9, existing)
│   └── 7. returnInsightArtifact
│          → wraps narrative + citations + diagnostic metadata
```

Notes:
- Steps 1–4 are deterministic Dataverse / index reads.
- Step 5 is the only LLM call.
- Step 6 is existing `GroundingVerifier` (D-P9 / D-47 / LAVERN ADR 10.6).
- The decline path (Step 4 emits Decline) is the existing r2 `DeclineResponse` shape — already supported by `InsightSummaryCard` framework error rendering.

Prompt construction follows the audit's Spaarke Canonical Prompt Construction Pattern (4 elements). The actual prompt content lives in `sprk_analysisaction.sprk_systemprompt` (editable by SMEs without code deploy, per r2 retirement of `.txt` prompts).

### 4.5 Summary persistence (NEW per owner direction 2026-06-10)

Each insight invocation persists its output to the host record (Matter) so the summary is consumable by reports, emails, notifications, and downstream surfaces — not just live AI calls.

**Persistence approach** (Matter Health single mode) — owner direction 2026-06-10 simplified to **single field, no new schema**:

| Field | Type | Content | Purpose |
|---|---|---|---|
| `sprk_performancesummary` (existing R5 placeholder, longtext) | longtext | Structured JSON envelope: `{ schemaVersion, body, citations[], generatedAt, playbookName, playbookVersion, tenantId, dimensions[] }` | Single persistence sink — UI parses JSON for full-fidelity render; downstream consumers (reports/emails/notifications) extract `.body` for plain-text content |

**No new Dataverse fields created in r1**. R5's static placeholder text in `sprk_performancesummary` is REPLACED by the AI-generated JSON envelope on first invocation. R5's old sparkle-icon-to-popup wiring is decommissioned (replaced by `InsightSummaryCard`).

**Persistence mechanism**:

The playbook itself writes the artifact via the existing `UpdateRecord` node executor (per owner direction "ensure we are using the power of the playbook/nodes/scopes model"). No new node executor types in r1.

**Storage semantics**:
- Always overwrite "current" — no history table in r1 (future r2+ candidate)
- Both fields written in one playbook step (transactionally consistent via UpdateRecord node)
- Stored summary is the source of truth for UI display; cache is a performance layer

**Downstream consumption** (not in r1 scope, but enabled by r1):
- Reports / emails / notifications parse `sprk_matter.sprk_performancesummary` as JSON and extract `.body` for plain-text rendering
- For full-fidelity rendering with citation chips, downstream consumers parse the full envelope and render the citations array

---

### 4.6 Background pre-warm + freshness (NEW per owner direction 2026-06-10)

Owner direction: "can these be triggered to update in the background when the entity/form loads for the first time in the day or last hour? this would cut down latency and keep updated."

**Pre-warm semantics**:

| Trigger | Behavior |
|---|---|
| Matter form OnLoad | Read `sprk_performancesummary`, parse as JSON, check `.generatedAt` timestamp (gracefully handle non-JSON legacy R5 placeholder content as "no stored summary") |
| If `>1 hour` since last refresh | Fire-and-forget BFF invocation in background; UI renders existing stored summary immediately (no spinner blocking interaction) |
| If `<1 hour` since last refresh | No invocation; UI renders existing stored summary |
| User clicks manual refresh button | Force invocation regardless of timestamp; UI shows spinner until result |
| Background invocation completes | Updated summary is persisted; UI updates view (live re-fetch OR next page load picks up new content — UX TBD in spec) |

**Frontend implementation**:
- Matter form OnLoad event handler reads the stored summary timestamp
- If stale, fires async fetch to `/api/insights/ask` with `mode=background` flag (or equivalent — to be defined in spec)
- Component renders existing stored summary while background work runs

**Backend implementation**:
- Existing `IInsightsAi.AnswerQuestionAsync` endpoint handles both foreground (manual click, blocking) and background (form-load, fire-and-forget) calls — same surface
- Cache TTL aligned to 1 hour (override existing default `IInsightsPlaybookExecutionCache` 15-min if needed; topic registry may include per-topic TTL — to be confirmed in spec)
- Concurrency: if two background invocations are triggered simultaneously, the second is no-op (deduplicated via idempotency key on `subject` + `topic` + `mode`)

**What's NOT in r1**:
- Scheduled background batch refresh (deferred to r2)
- Push notifications when new summary lands (deferred)
- Server-Sent Events for live UI update mid-background-invocation (deferred)

---

### 4.7 Component design (InsightSummaryCard)

Location: `@spaarke/ai-widgets` (new package OR extension of existing pattern; depends on what R5 shipped).

Props (TypeScript):

```typescript
interface InsightSummaryCardProps {
  topic: string;              // e.g., "matter-health"
  subject: string;            // e.g., "matter:1a2b3c4d-..."
  mode?: string;              // default "single"
  parameters?: Record<string, unknown>;  // optional host-provided context
  kpiSlot?: React.ReactNode;  // structured KPI display rendered by host
  onCitationClick?: (citation: Citation) => void;
  onFeedback?: (feedback: "positive" | "negative", text?: string) => void;
}
```

States:
- **Idle** — KPI slot rendered; sparkle icon shown; no narrative
- **Loading** — skeleton placeholder for narrative; sparkle icon spinning
- **Loaded** — narrative + citations rendered; "Last updated Nm ago"
- **Error (feature disabled)** — graceful "AI summaries unavailable" + diagnostic for ops
- **Error (decline)** — structured Decline rendered ("Insufficient evidence to diagnose — N assessments recommended")
- **Stale** — "Refresh" button highlighted

Visual treatment: per the screenshot's existing card styling. The component should fit inside existing record-page card layout without redesign.

---

## 5. Implementation phases

### Phase A — Files-index pipeline healthy (PREREQUISITE, owned by current debugging stream)

Until `spaarke-files-index` is populated correctly and the D-P8 SPE-upload consumer is enabled, the Insights ingest pipeline produces no Observations. Matter Health playbook can run against KPI assessment data alone, but document-grounded citations will be empty. Not a blocker for r1 design, but UAT realism depends on this stream landing.

### Phase B — Framework + Matter Health single-mode

| Workstream | Effort | Owner | Dependencies |
|---|---|---|---|
| Author `matter-health-single` JPS playbook + deploy via `Deploy-Playbook.ps1` | 3-5 days | SME + Insights engineering | Existing playbook authoring tooling (r2 Wave B proven) |
| Build `InsightSummaryCard` component in `@spaarke/ai-widgets` | 1 week | UI engineering | None (existing Fluent v9 + shared component conventions) |
| Implement topic registry (Dataverse entity OR config — to be decided) | 2-3 days | Schema + DI engineering | Dataverse schema decision |
| Wire Matter Health card on existing Matter record page to use `InsightSummaryCard` | 3-5 days | UI engineering | `InsightSummaryCard` component complete |
| Telemetry: emit invocation events per topic + mode | 1-2 days | Free if reusing R5 telemetry pattern | `R5SummarizeTelemetry` extension OR new meter |
| UAT scenarios + dev seed data | 3-5 days | QA + Insights engineering | Files-index pipeline healthy (Phase A) |

**Total**: ~4-5 weeks if streams parallelize.

### Phase C (r2+) — Framework expansion

- Additional Matter topics (Budget Performance, Outcomes Success, Upcoming Tasks)
- Portfolio + comparative analysis modes
- Other record types (Project, Invoice)
- Adopt R6 Pillar 3 `IInvokePlaybookAi` facade if/when shipped
- Adopt r3 Tier 2.4 actionable citations if/when shipped

---

## 6. Open questions for owner iteration

Numbered for easy referencing in iteration:

1. **Topic registry as Dataverse entity or config?** Recommended Dataverse (SME-editable), but engineering preference may differ.
2. **Modes: separate playbooks or parameterized single playbook?** Recommended separate per mode.
3. **For r1, which mode ships?** Recommended single (diagnostic) only.
4. **For r1 single mode, which of the 14 insight candidates in §4.3 ship?** Recommended dimensions 1–7 in §4.2 (composite, trend, themes, inflection, critical, risk, evidence gaps). Owner picks others.
5. **Card UX: inline expand vs modal vs auto-load on view?** Recommended inline expand (matches existing card pattern). Auto-load is highest impact but also highest AI cost.
6. **Streaming via SSE or one-shot?** Recommended one-shot for r1. SSE adds engineering effort and depends on r3 Tier 2.2 if playbook-path streaming is desired.
7. **Citations: link to specific `sprk_kpiassessment` row, or to source documents, or both?** Recommended both — link to assessment for in-product navigation, link to source document (if applicable) for evidence drill-down.
8. **Decline UX**: how to present "insufficient evidence to diagnose" — same card with explanation, or separate "blocked" state? Recommended: same card with Decline narrative.
9. **Feedback affordance** (thumbs up/down): ship in r1 or defer? Recommended ship — collects SME calibration data per r2 SC-15 deferral.
10. **Refresh policy**: TTL on cache (recommended 15-min, matching r2), or per-user-action invalidation?
11. **Multi-tenant considerations**: assessments + Notes may carry sensitive client data; confirm tenant isolation expectations.
12. **Privilege model**: who can see Matter Health insight on a matter they don't own? Recommended: same authz as existing Matter Health card (no new authz layer in r1).
13. **What did R5 ship for record-section AI summaries?** If sparkle icons exist today, r1 may be enhancement, not net-new. Worth grep'ing R5 source before locking design.

---

## 7. Coordination with parallel projects

This project is designed to ship independently of r3 (paused) and R6 (in design). Cross-project touchpoints:

| Project | Touchpoint | r1 strategy |
|---|---|---|
| **r3 Insights Engine** | Uses `IInsightsAi.AnswerQuestionAsync` (same surface r3 reconciles) | r1 calls existing public API; r3 internal refactor is transparent |
| **R6 Pillar 3 `IInvokePlaybookAi`** | New facade for generic playbook invocation | r1 uses `IInsightsAi.AnswerQuestionAsync` directly in r1; switches if R6 ships and migration is low-cost |
| **R6 Pillar 5 schema-aware renderers** | Output schema as a Scope entity | r1 owns its own narrative rendering; not blocked by R6 |
| **R6 Pillar 6 workspace state model** | Workspace-context state | r1 doesn't touch workspace; r2+ workspace narratives WILL coordinate with R6 |
| **R6 Pillar 9 widget visibility contract** | `getAgentVisibleState()` per widget | r1 widget isn't agent-visible (it's per-record AI display); r2+ workspace widgets WILL implement this contract |

---

## 8. Out of scope (explicit)

- Workspace narrative widgets (cross-record / portfolio aggregation in workspace context) → r2+
- Topics other than `matter-health` → r2+
- Analysis modes other than single → r2+
- Record types other than Matter → r2+
- Actionable citations (`citations[].action`) → depends on r3 Tier 2.4; r2+
- Multi-turn / bidirectional clarification → depends on r3 Tier 2.1; r3+
- Real-time / push-based insight updates → not in framework scope
- Modifying `sprk_kpiassessment` scoring or roll-up logic → owned by other components
- ADRs — r1 operates within existing audit-codified constraints

---

## 9. Success criteria (draft — to refine in spec.md)

1. **SC-1**: `InsightSummaryCard` component exists in `@spaarke/ai-widgets` with documented props + states; rendered standalone in Storybook (or equivalent)
2. **SC-2**: `matter-health-single` JPS playbook deployed to dev Dataverse; invokable via `/api/insights/ask`
3. **SC-3**: Matter record page Matter Health card displays sparkle icon; click triggers playbook invocation; returns narrative + citations
4. **SC-4**: Cache hit on second click within 15-min TTL window (sub-100ms response)
5. **SC-5**: Graceful Decline rendering when assessment count is insufficient (mocked low-data matter)
6. **SC-6**: Graceful FeatureDisabledException rendering when compound-AI gate is OFF (dev environment toggle test)
7. **SC-7**: Telemetry event emitted per invocation with topic, mode, subject, duration, outcome
8. **SC-8**: Feedback affordance (thumbs up/down) captures SME calibration data
9. **SC-9**: At least 1 owner-walkthrough UAT against a real dev Matter with ≥3 assessments per metric area
10. **SC-10**: Documentation: `BUILD-A-NEW-INSIGHT-CARD.md` tutorial in `docs/guides/` explaining how to add a new topic + playbook

---

## 10. Methodology + ADR alignment

- All BFF interactions go through the PublicContracts facade (`IInsightsAi`) per audit DR-003 / ADR-013
- DI changes (none expected in r1) would follow Endpoint↔DI Symmetry Rule per audit DR-008
- Cache use follows audit DR-002 (existing `IInsightsPlaybookExecutionCache` only)
- Prompt construction follows Spaarke Canonical Prompt Construction Pattern (per audit §2.7)
- No new ADRs; r1 operates within existing constraints

---

*Initial draft 2026-06-10. Owner iteration expected to refine §4.3 candidate selection, §6 open questions, and §9 success criteria before spec.md is authored.*

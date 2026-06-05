# Spaarke Insights Engine — Phase 2 (r3) Design

> **Status**: 🆕 SKELETON — focus areas pending owner discussion
> **Created**: 2026-06-04
> **Predecessor design**: [`r2/design.md`](../ai-spaarke-insights-engine-r2/design.md) + 5 wave design docs (a3 / a4 / a5 / a6 / e3)
> **Primary input**: [`r2/PHASE-2-OUTLINE.md`](../ai-spaarke-insights-engine-r2/PHASE-2-OUTLINE.md) — 4 tiers, 22 candidate items
> **r2 retrospective**: [`r2/notes/lessons-learned.md`](../ai-spaarke-insights-engine-r2/notes/lessons-learned.md)

---

## 0. How this document gets filled

The skeleton sections below mirror r2's `design.md` structure for continuity. The owner-mediated discussion will fill them with r3 scope decisions based on the Phase 2 outline tiers. Once focus areas are locked, the design content drives `spec.md`, which drives `plan.md`, which drives `tasks/`.

### Provenance

- **Skeleton author**: Claude (main session) on 2026-06-04 after r2 task 090 wrap-up
- **Decisions**: owner-mediated; recorded in [`decisions/`](decisions/) as DR-### records during design discussion

---

## 1. Phase 2 framing

**TBD** — to be filled during design discussion.

Suggested framing prompts:
- What r2 outcomes are we doubling down on? (e.g., contract evolution, telemetry maturity, RAG quality)
- What r2 deferrals are blocking production? (e.g., `NullInsightsAi`, SME calibration loop)
- What new surface area is r3 expanding? (e.g., multi-area docs, bidirectional clarification, embedding classifier)
- What r3 explicitly does NOT do? (boundary against Phase 3 / r4)

---

## 2. Focus areas (owner-selected from PHASE-2-OUTLINE.md tiers)

> Pick from r2's [`PHASE-2-OUTLINE.md`](../ai-spaarke-insights-engine-r2/PHASE-2-OUTLINE.md). Suggested default: Tier 1 architectural cleanup as r3 wave 1, then selected Tier 2–3 items as waves 2+.

### 2.1 Tier 1 — Architectural cleanup (LOCKED as r3 wave 1, ~5 days total)

**Selection status**: ✅ All 5 items in scope (owner confirmed 2026-06-04; 1.5 added later same day per index-rename discussion).

| Candidate | Why it matters | Est. effort | Selected? |
|---|---|---|---|
| 1.1 `NullInsightsAi` facade | Closes asymmetric-registration latent failure on `IInsightsAi` (flagged by Wave E adr-check; deferred from Wave F to keep scope tight). Mirror `NullRagService` pattern. | ~0.5d | ✅ |
| 1.2 v1.2 contract — `spe://drive/X/item/Y` evidence-ref href | F1 spike empirically minority case; currently emits `href: null` for that subset. Promotion requires async sprk_document lookup via `DataverseObservationMirror.ResolveDocumentIdAsync`. | ~2-3d | ✅ |
| 1.3 Test-fixture hygiene | Root-cause CI flakes from PRs #337 + #339 (timing test ±0.5s, Post Cache race, FileSystemWatcher dispose NRE). Audit `IntegrationTestFixture` for further race surface; review every `BindConfiguration` for change-token subscription overhead. | ~1d | ✅ |
| 1.4 Telemetry maturity | `InsightsActionLookupFailed` event (Wave D follow-up) + App Insights dashboards for SSE completion rate, mid-stream error rate, citation href click-through (post-R5 integration), per-area Layer 1/2 routing distribution. | ~1w | ✅ |
| **1.5** Rename `playbook-embeddings` index → `spaarke-playbook-index` + supporting C# class names | The AI Search index name `playbook-embeddings` (shipped by SprkChat Platform Enhancement R2, March 2026) violates the Spaarke resource naming convention `spaarke-<resource>-index` followed by `spaarke-records-index`, `spaarke-knowledge-index-v2`, `spaarke-insights-index`. Critical to fix BEFORE multi-environment / multi-tenant deploy at production scale — renaming a deployed shared index later requires per-tenant migration coordination. **Full normalization** includes Azure resource + C# class names + folder + scripts. See §2.1.1 for breakdown. | ~1.5d | ✅ |

#### 2.1.1 Tier 1.5 — Playbook index rename breakdown

**Scope**: Azure AI Search index name + supporting C# types + scripts. Migration follows the `spaarke-knowledge-index` → `spaarke-knowledge-index-v2` precedent (create new → re-populate → cutover → drop old). Safe per-environment cutover (Dev first).

**Proposed canonical names** (subject to owner refinement):

| Today | Proposed |
|---|---|
| Index: `playbook-embeddings` | `spaarke-playbook-index` |
| File: `infrastructure/ai-search/playbook-embeddings.json` | `infrastructure/ai-search/spaarke-playbook-index.json` |
| Folder: `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/` | `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookIndex/` |
| Namespace: `Sprk.Bff.Api.Services.Ai.PlaybookEmbedding` | `Sprk.Bff.Api.Services.Ai.PlaybookIndex` |
| Class: `PlaybookEmbeddingService` (search + embedding gen) | `PlaybookIndexService` |
| Class: `PlaybookIndexingService` (populator; already well-named) | keep OR rename to `PlaybookIndexer` (cleaner distinction from `PlaybookIndexService`) |
| Class: `PlaybookEmbeddingDocument` | `PlaybookIndexDocument` |
| Class: `PlaybookEmbeddingEndpoints` | `PlaybookIndexEndpoints` |
| Script: `Create-PlaybookEmbeddingsIndex.ps1` | `Create-SpaarkePlaybookIndex.ps1` |
| Script: `Index-ExistingPlaybooks.ps1` | keep (already script-name agnostic; just update internal index-name string) |
| Script: `Seed-PlaybookTriggerMetadata.ps1` | keep (same) |

**Work items**:

| ID | Title | Effort |
|---|---|---|
| 1.5.A | Code + file + folder + namespace renames; BFF references updated; index-name string moved to config `Insights:Playbook:IndexName` (default new name) for environment override capability | 0.5d |
| 1.5.B | Script renames + content updates to reference new index name | 0.25d |
| 1.5.C | Deploy new `spaarke-playbook-index` to each environment (Dev → Staging → Prod) via renamed `Create-SpaarkePlaybookIndex.ps1` | 0.25d |
| 1.5.D | Re-populate via `Index-ExistingPlaybooks.ps1` against new index name in each environment | 0.25d |
| 1.5.E | BFF cutover per environment; smoke verify `PlaybookDispatcher` is hitting new index | 0.25d |
| 1.5.F | Delete old `playbook-embeddings` index from each environment | 0.25d |

**Per-environment safety**: feature flag / config override (`Insights:Playbook:IndexName`) makes cutover reversible. If BFF deployment with new name fails to find data, point back at old index, fix, re-deploy. Old index stays until 1.5.F confirms cutover success.

**Coordination with Tier 2.5 (reconciliation)**: Tier 1.5 ships BEFORE Tier 2.5. The reconciliation work uses the new names directly — no double-renaming.

### 2.2 Tier 2 — Capability expansion + architectural reconciliation

**Selection status**: 2.5 (revised) LOCKED. Others TBD per R5 coordination.

| Candidate | Why it matters | Est. effort | Selected? |
|---|---|---|---|
| 2.1 Bidirectional clarification (HTTP 422 + envelope) | Assistant asks user disambiguation; documented Phase 2 deferral in v1.1 contract §11. R5 coordination required. | ~1w | ☐ (R5 input pending) |
| 2.2 Full playbook-path SSE token streaming | Wave F shipped coarse `progress` events only on playbook path; full token streaming requires structured-output JSON streaming (different mechanism). R5 coordination required. | ~1w | ☐ (R5 input pending) |
| 2.3 `playbookHint` Assistant-supplied field | Documented v1.1 deferral; supports Assistant "ask via playbook X" affordances. R5 coordination required. | ~3d | ☐ (R5 input pending) |
| 2.4 Actionable citations (`citations[].action`) | Documented v1.1 deferral; current `href` is display-only-link. R5 coordination required. | ~1w | ☐ (R5 input pending) |
| **2.5 (REVISED)** Reconcile `InsightsIntentClassifier` with existing `PlaybookDispatcher` infrastructure | **Architectural reconciliation**, not net-new build. Insights Engine r2 Wave E2 built a parallel LLM-only intent classifier without leveraging the `playbook-embeddings` AI Search index + `PlaybookDispatcher` two-stage matching (vector + LLM refinement) shipped by SprkChat Platform Enhancement R2 in March 2026. See §2.2.1 for revised work breakdown. | ~1w | ✅ |

#### 2.2.1 Tier 2.5 (revised) — InsightsIntentClassifier ↔ PlaybookDispatcher reconciliation

**Existing infrastructure** (built by SprkChat Platform Enhancement R2, completed 2026-03-17 — NOT used by Insights Engine r2 Wave E2):

| Asset (post-Tier-1.5 rename) | Path | Purpose |
|---|---|---|
| `spaarke-playbook-index` AI Search index (renamed in Tier 1.5 from `playbook-embeddings`) | `infrastructure/ai-search/spaarke-playbook-index.json` (renamed) | HNSW vector search over playbook descriptions + trigger phrases (3072-dim, text-embedding-3-large) |
| `PlaybookDispatcher` (two-stage matching) | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs` | Stage 1 vector similarity (1.5s budget) + Stage 2 LLM refinement (0.5s budget). Single high-confidence candidate (≥ 0.85) skips Stage 2. |
| `PlaybookIndexService` + `PlaybookIndexer` (renamed in Tier 1.5) | `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookIndex/` (renamed folder) | Search + embedding generation + index population pipeline |
| `PlaybookIndexDocument` model (renamed in Tier 1.5) | `src/server/api/Sprk.Bff.Api/Models/Ai/PlaybookIndexDocument.cs` | Fields: `playbookId`, `playbookName`, `description`, `triggerPhrases[]`, `recordType`, `entityType`, `tags[]`, `contentVector3072` |
| Deployment scripts (post-rename) | `scripts/Create-SpaarkePlaybookIndex.ps1`, `scripts/Index-ExistingPlaybooks.ps1`, `scripts/Seed-PlaybookTriggerMetadata.ps1` | Index creation + back-fill + trigger metadata seeding |

**Insights Engine r2 Wave E2 deficiency** (deferred debt — even called out in the code comment):
- `InsightsIntentClassifier.BuildPrompt()` is **hardcoded C#** with one-line-per-playbook descriptions
- Code comment line ~226: *"As more playbooks ship in Phase 2 the few-shot block can be extracted to `sprk_analysisaction.sprk_systemprompt`..."* — Phase 2 = now (r3)
- Doesn't use `playbook-embeddings` index even though it exists and is populated
- Doesn't use `PlaybookDispatcher` two-stage matching

**r3 work items for Tier 2.5 reconciliation**:

| ID | Title | Effort | Description |
|---|---|---|---|
| F-1 | Reconciliation decision spike | 0.5d | Audit `PlaybookDispatcher` two-stage architecture; decide: (a) replace `InsightsIntentClassifier` entirely; (b) refactor Insights to share Stage 1 vector search; or (c) keep separate with documented reason. Binding output for F-3 scope. |
| F-2 | Migrate Insights classifier prompt to JPS Action | 1d | Per project's stated "no .txt prompt files; prompts live in Dataverse" principle (CLAUDE.md §4 / r2 PR-1 clarification) + the existing code comment that calls this out. The classification prompt becomes a `sprk_analysisaction.sprk_systemprompt` row; tweakable by SMEs without code change. |
| F-3 | Implement reconciliation per F-1 decision | 2-3d | Most likely outcome: Insights becomes a thin wrapper around `PlaybookDispatcher` with a "no playbook matched → RAG fallback" branch (preserves FR-05 safety). Removes the parallel LLM-only classifier code path. |
| F-4 | Index Insights playbooks | 0.5d | Ensure `predict-matter-cost@v1` (and any future Insights playbooks) are present in `playbook-embeddings` index with proper `description` + `triggerPhrases` + `tags`. Verify Seed/Index scripts cover Insights playbook entity type; gap-fill if not. |
| F-5 | JPS authoring flow: auto-populate metadata at playbook creation | 0.5-1d | When `Deploy-Playbook.ps1` (or successor) deploys a new playbook, auto-populate `description` + `triggerPhrases` (generated where reasonable, SME-reviewed) and trigger re-indexing. Closes the "playbook metadata as build-time artifact" gap. |

**Total: ~5 days** (≈1 week). Less than original Tier 2.5 estimate because foundation exists.

**Cross-team coordination**:
- SprkChat platform team — confirm `PlaybookDispatcher` semantic shape is suitable for Insights consumption; no concerns about shared use across SprkChat + Insights
- R5 (Spaarke Assistant) — no contract change (this is internal BFF reconciliation); R5 sees same `POST /api/insights/assistant/query` v1.1 surface

### 2.3 Tier 3 — Surface area expansion

**Selection status**: TBD.

| Candidate | Why it matters | Est. effort | Selected? |
|---|---|---|---|
| 3.1 Multi-area document handling | Current design assumes 1 area per matter via `sprk_practicearea` | ~1w | ☐ |
| 3.2 Subject scheme expansion (contract, communication, calendar event) | Wave D5 framework supports; add new `ILiveFactResolver` impls | ~3d each | ☐ |
| 3.3 Multi-turn conversation state on BFF | Documented Phase 2 deferral; v1.x has `conversationContext` field for telemetry only | ~2w | ☐ |
| 3.4 Cross-tenant federation | Documented Phase 2 deferral; v1.x is single-tenant | ~1w+ | ☐ |

### 2.4 Tier 4 — Long-term

**Selection status**: TBD (likely deferred to r4).

| Candidate | Selected for r3? |
|---|---|
| Cosmos NoSQL graph (originally D-P17 Phase 1; re-deferred) | ☐ |
| Per-tenant prompt overrides (Wave A4 design exists; not yet implemented) | ☐ |
| Customer playbook authoring UI | ☐ |
| Field auto-populations | ☐ |
| MCP server contract | ☐ |
| SME UI for calibration loop (supports SC-15 calibration carry-forward) | ☐ |
| AI-directed playbook authoring | ☐ |
| Per-tenant monthly cost caps | ☐ |
| Multi-tenant onboarding flow | ☐ |

---

## 3. Decisions

> Record substantive design decisions here as `DR-###` references; full decision records in [`decisions/`](decisions/).

**TBD** — populated during design discussion.

---

## 4. Architectural anchors carried from r2 (unchanged)

- **§3.5 facade boundary** (Zone A / Zone B; `IInsightsAi` is the one allowed cross-zone seam)
- **JPS as canonical playbook architecture** — `PlaybookExecutionEngine` is the single executor; no parallel orchestrators
- **`sprk_analysisaction.sprk_systemprompt` IS the prompt-bearing primitive** — no new `sprk_prompt` entity
- **Practice areas sourced from `sprk_practicearea_ref`** — never hardcoded
- **Honesty contract** (D-04, D-49) — structured `DeclineResponse` over hallucination; `GroundingVerifier` for mechanical citation check
- **Evidence-sufficiency rules** (D-06) as mandatory gates in synthesis playbooks
- **Per-tenant deployment** (D-52) — single parameter file = one full deployment unit
- **AIPU2-027 privilege filtering** at RAG retrieval + href endpoint auth (no URL signing)
- **Spaarke Assistant tool-call contract** — locked at v1.1; r3 changes require minor-version bump
- **Wave-based parallel sub-agent dispatch** (Round 1 spike → Round 2 parallel → Round 3 docs) — proven across Waves D, E, F
- **F1 spike pattern** for additive contract bumps — produces binding scope decision that unblocks parallel implementation

---

## 5. Out of scope for r3

**TBD** — explicit boundary against r4 / Phase 3.

Likely candidates for r4 deferral (subject to owner confirmation):
- Cosmos NoSQL graph
- Customer playbook authoring UI
- Multi-tenant onboarding

---

## 6. Risks

**TBD** — Phase 2 risk register to be authored once focus areas lock.

Risks carried forward from r2:
- Sub-agent stuck-hang (mitigated: output-file mtime check; held in Waves E + F)
- CI flake patterns (FileSystemWatcher dispose, timing tests, cache race) — Tier 1.3 work item
- Asymmetric DI registration — Tier 1.1 work item
- R5 Assistant-side integration timing (R5 ships when ready; out of r3 control)

---

## 7. Dependencies + coordination

- **R5** (`spaarke-ai-platform-unification-r5`) — primary consumer of Insights contract. Coordinate via existing coord doc when r3 scope includes contract changes.
- **r1 wrap-up** — NOT in r3 scope (R1-1 carried forward from r2).
- **Spaarke Dev environment** — shared App Service `spaarke-bff-dev`; deploys coordinated per `.claude/constraints/bff-extensions.md` §F.4 (still binding).

---

## 8. Open questions for owner

> Populated as design discussion progresses; each question gets a Q-### identifier.

**Initial questions for the focus-area discussion** (struck through as answered):

- ~~**Q-001**: Should r3 wave 1 = Tier 1 cleanup before any new capability work?~~ ✅ **ANSWERED 2026-06-04**: Yes — all 4 Tier 1 items locked as wave 1 (~3.5d)
- **Q-002**: Which Tier 2.1–2.4 items are R5's most-needed next contract additions? (R5 lead input pending; revised 2.5 already locked as wave 2 architectural reconciliation)
- **Q-003**: Does r3 include any Tier 3 surface expansion, or defer all to r4?
- **Q-004**: SME calibration loop (SC-15 carry-forward) — is there a Tier 4 item that becomes priority due to production data volume now available?
- ~~**Q-005**: Effort cap for r3 — fixed sprint-count budget or open-ended like r2?~~ ✅ **ANSWERED 2026-06-04**: Open-ended (r2-style)
- **Q-006** (NEW from Tier 2.5 reconciliation discussion): Should the audit be broader? If the Insights Engine r2 Wave E2 missed the existing `PlaybookDispatcher`, are there other duplications between r2 Insights work and SprkChat r2 (or other projects) work that r3 should reconcile?

---

*Skeleton authored 2026-06-04 by Claude (main session). Next action: owner-mediated focus-area discussion to fill §1, §2 selections, §3 decisions, §5 out-of-scope, §6 risks, §8 question answers.*

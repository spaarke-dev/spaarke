# Spaarke AI Platform — Chat Routing Redesign (R1) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-21
> **Source**: `design.md` v3.1 (1734 lines) + `research/research-summary.md` (synthesizes A1–A4 research + 5 audit agents, 2026-06-19/20)
> **Worktree**: `spaarke-wt-spaarke-ai-platform-chat-routing-redesign-r1`

## Executive Summary

R6 left two parallel playbook-routing mechanisms, three parallel playbook-execution paths, hardcoded GUIDs / slash dicts / capability-name strings, a stateless chat that re-asks users for already-uploaded files, and implicit streaming-code Workspace destination routing. This successor project collapses routing onto one matcher driven by `playbook-embeddings`, reforms playbook identification via stable `sprk_playbookcode` (already exists in schema; missing migration of consumers), adds file-aware classification so `/summarize` on an NDA upload routes to Summarize-NDA instead of generic SUM-CHAT, makes destination metadata explicit and data-driven via `NodeRoutingConfig`, builds a 6-tier stateful chat memory model (mostly wire + refactor of existing infrastructure — `MatterMemoryService`, `PinnedContextRepository`, `MemoryCompositionService`, Cosmos `sessions`/`memory`/`audit` containers already provisioned), authors specialized playbooks (Summarize-NDA, Summarize-Patent, Extract-Invoice), and retires `CapabilityRouter` wholesale (the layer the R6 UAT hotfixes were defensively guarding). Production-bound playbooks (per §1.5 of design — 6 bindings: chat/workspace SUM-CHAT siblings, "Summarize New File(s)", "Document Profile", Matter Pre-Fill, Project Pre-Fill) are migrated in place — not deleted, not renamed — to preserve NFR-07 and pre-fill consumer contracts.

### Canonical field naming (binding — disambiguation)

**REVISED (Q&A 2026-06-22)**: Previous table was incorrect — `sprk_playbookid` is a NVARCHAR(100) Text column ON `sprk_analysisplaybook` itself (not a lookup FK on related entities), and `sprk_playbookcode` is NOT the stable lookup field. The correct field roles per Dataverse describe + owner confirmation:

| Purpose | Canonical field | Type | Notes |
|---|---|---|---|
| **Immutable opaque ID for a playbook — used by code for lookups; environment-portable** | **`sprk_playbookid`** | **Text NVARCHAR(100) on `sprk_analysisplaybook`** | **Already exists. Locked across environments. Convention: value mirrors the row's `sprk_analysisplaybookid` PK GUID (3 of 5 production playbooks already follow this; 2 need backfill). THIS is the field code resolves by.** |
| Admin-facing descriptive slug for a playbook | `sprk_playbookcode` | Text NVARCHAR(10) (alternate key) on `sprk_analysisplaybook` | Already exists with `PB-NNN` convention on some rows (`PB-002`, `PB-008`, `PB-009`, `PB-015`). Human-readable code admins use in UI. NOT used by code for lookups. This project does NOT modify these values. |
| Database PK of `sprk_analysisplaybook` | `sprk_analysisplaybookid` | GUID (PK) | Already exists. PK regenerates on environment imports without explicit preservation; do NOT use for cross-environment references unless you've also preserved the GUID via solution import settings. |
| **Immutable opaque ID for an action — used by code for lookups** | **`sprk_actionid`** | **Text NVARCHAR(100) on `sprk_analysisaction`** | **Already exists. Parallel role to `sprk_playbookid`. Convention: value mirrors the row's `sprk_analysisactionid` PK GUID.** |
| Admin-facing descriptive slug for an action | `sprk_actioncode` | Text NVARCHAR(64) on `sprk_analysisaction` | Already exists. Admin convention; not a lookup field for code. |
| Database PK of `sprk_analysisaction` | `sprk_analysisactionid` | GUID (PK) | Already exists. |

**Implications for this project**:
- All `WorkspaceOptions.*PlaybookCode` properties shipped in Wave 1-A → rename to `*PlaybookId` and bind to GUID values
- The `/api/ai/playbooks/by-code/{code}` endpoint shipped in Wave 1-A → rename route to `/by-id/{id}`; query the `sprk_playbookid` field instead of `sprk_playbookcode`
- `PlaybookLookupService` alternate-key lookup → change key column from `sprk_playbookcode` to `sprk_playbookid`
- Task 014 backfill → simplified to "write `sprk_playbookid = <PK GUID value>` on the 2 rows where it's currently NULL"
- `sprk_playbookcode` (`PB-NNN` values) — left untouched; this project does not modify it

### MVP Scope Cut (Owner decision 2026-06-22)

The owner prioritized shipping a working end-to-end MVP over the full sophisticated subsystem. **MVP delivers the core use case in full**:

> *"User engages with the Spaarke AI via Assistant + Workspace + Context → uploads a file → selects a playbook → executes the playbook → asks questions → refines/modifies the output."*

#### MVP cuts at a glance

| Phase / WP | Original tasks | MVP tasks | Deferred (with substrate lock-ins preserved) |
|---|---|---|---|
| Phase 4 — WP5 6-tier memory | 42 | **~13** | 4a PaneEventBus `memory` channel (5 tasks), 4b enrichment pipeline classification/summarization/manifest (6 of 9), 4c LayeredContextCardBuilder + TrustFrameInjector + static-prefix (3 of 5), 4d 7 of 8 tool handlers, 4e entire promotion workflow (7), 4f audit-repo refactor (2 of 6). Total ~29 tasks. |
| Phase 5 — WP2 file-aware classification | 10 | **6** | Auto-routing engine (fingerprint + reconciliation + gpt-4o-mini decider + multi-file load test). Suggested-playbooks UX preserved via single-stage vector match. |

#### What the MVP delivers (user can do these on day 1)
- Upload file → file persisted in session memory (T2)
- Pick playbook from Library modal (Flow A) OR pick from chat-suggested cards (Flow B simplified)
- Execute playbook → output streams to Workspace (per WP3 destination wiring)
- Ask follow-up questions; agent reads uploaded file content with citations (T5 single-index recall)
- Refine output via chat (T1 + T2)

#### What the MVP defers (visible gaps)
- Cross-session matter memory ("remember about this matter") — user re-tells each session
- User-level preferences ("I always want bullet lists") — no personalization
- Promote-to-matter-memory UX (T2 → T3 workflow + Context-pane Accept/Reject cards)
- Multi-doc corpus reasoning over more than the current session's uploaded files
- Long-conversation summarization (>~30 turns hits 8K budget → truncation)

#### Future-proofing lock-ins (INCLUDED in MVP — cheap now, expensive later)
1. **Task 078** — unify `MemoryCompositionService` with `PlaybookChatContextProvider` (prevents post-MVP per-turn pipeline rewrite)
2. **Task 080** — FR-45 regression test at `PlaybookChatContextProvider.cs:627` (preserves the binding invariant)
3. **Spec artifact**: lock the 5 `MemoryPaneEvent` discriminant JSON shapes (channel exists per ADR-030 v2; payloads documented now for forward compatibility)
4. **Spec artifact**: lock the Cosmos `matter-memory-promotion` doc-type schema (prevents migration script post-MVP)
5. **`RecallSessionFileHandler`** tool-description contract: distinguishes "session" vs "matter" scope so post-MVP additions (7 more handlers) don't confuse the agent

These 5 lock-ins keep post-MVP work additive (~2-3 weeks for deferred features) instead of a substrate rewrite (~6-8 weeks).

#### Honest competitive position of MVP (June 2026)
- **Wins**: workflow flexibility (playbook authoring), enterprise auth + governance, tri-pane Workspace+Assistant+Context UX
- **Ties** (table stakes): document upload + Q&A + cite, within-session refinement, streaming output
- **Loses** to Harvey on cross-session matter memory; to Hebbia on multi-doc corpus reasoning; to M365 Copilot personal memory on user-level preferences
- Pitch: *"Spaarke ships a flexible, customizable, enterprise-grade single-matter document analysis + workflow tool. Cross-session memory is our next horizon; the architecture was designed to scale to it."*

Full deferred-feature inventory + post-MVP roadmap: see [`plan.md`](plan.md) §"Post-MVP Roadmap".

### Phase 1R — `sprk_playbookconsumer` Routing Table (Owner decision 2026-06-24)

The §1.7 Stable-ID migration (Phase 1) ships consumers that resolve playbooks BY ID — but the binding *which playbook ID maps to which consumer code* still lives in `Workspace__*PlaybookId` environment variables (set via `az webapp config appsettings set`). The 2026-06-24 UAT-2 failure (Matter pre-fill broken because `Workspace__MatterPreFillPlaybookId` was set under the legacy key on bff-dev) is the exact failure mode this anti-pattern produces. **Phase 1R replaces env-var-based consumer→playbook routing with a Dataverse-backed `sprk_playbookconsumer` table.** Phase 1R is binding and adds 8 FRs (FR-1R-01 through FR-1R-08). All existing Phase 1 FRs remain valid; this is additive.

#### `sprk_playbookconsumer` table contract (FR-1R-01)

| # | Column | Type | Required | Default | Purpose |
|---|---|---|---|---|---|
| 1 | `sprk_name` | Single Line Text (250) | Yes | (auto) | Display name `{consumertype}/{consumercode} → {playbookcode}`. |
| 2 | `sprk_consumertype` | Single Line Text (64) | **Yes** | — | Stable consumer code (`matter-pre-fill`, `project-pre-fill`, `ai-summary`, `summarize-file`, `chat-summarize`, `email-analysis`, ...). Lowercase + hyphens; no spaces. |
| 3 | `sprk_consumercode` | Single Line Text (64) | No | `default` | Sub-discriminator within a consumer type. |
| 4 | `sprk_playbook` | Lookup → `sprk_analysisplaybook` | **Yes** | — | Target playbook. (As-built name; OData accessor `_sprk_playbook_value`.) |
| 5 | `sprk_priority` | Whole Number (0–1000) | Yes | `500` | Lower wins on tie; admin-override headroom. |
| 6 | `sprk_matchconditions` | Multiple Lines of Text (4000) | No | `null` | JSON predicate (see FR-1R-04 schema). `null`/`{}` = always match. |
| 7 | `sprk_enabled` | Two Options (Yes/No) | Yes | `Yes` | Soft-disable preserves audit trail. |
| 8 | `sprk_environment` | Single Line Text (16) | Yes | `*` | Env scope (`dev`/`test`/`prod`/`*`). |

**Alternate key**: `sprk_ConsumerTypeCodeEnvironment` = (`sprk_consumertype` + `sprk_consumercode` + `sprk_environment`). [As-built name per 2026-06-24 owner table creation.]
**Ownership**: Organization. **Audit + change tracking**: Enabled (BFF cache invalidates on change-tracking notification).

#### Functional Requirements (Phase 1R)

1. **FR-1R-01**: `sprk_playbookconsumer` table created in Dev → Test → Prod with the 8-column contract above. — **Acceptance**: Dataverse describe matches the contract; alternate key present; audit + change-tracking enabled.
2. **FR-1R-02**: New BFF service `IConsumerRoutingService` (in `Services/Ai/Routing/`) — `Task<Guid?> ResolveAsync(string consumerType, string? consumerCode = "default", IRoutingContext? context = null, CancellationToken ct = default)`. Returns the matching `sprk_analysisplaybook` PK GUID (`sprk_analysisplaybookid` — the system PK; not the legacy `sprk_playbookid` custom field). 5-min TTL per-tenant cache per ADR-014. Cache invalidates on `sprk_playbookconsumer` change-tracking event (existing change-tracking subscriber pattern). — **Acceptance**: interface registered via DI per ADR-010; cache hit telemetry; cache invalidates within 30s of Dataverse update.
3. **FR-1R-03**: Resolution algorithm — query records WHERE `sprk_enabled=true AND sprk_consumertype=@type AND sprk_consumercode IN (@code, 'default') AND sprk_environment IN (@env, '*')`. Apply `sprk_matchconditions` JSON predicate against `IRoutingContext`. Order by `sprk_priority asc`, then more specific `consumercode` over `default`, then more specific `environment` over `*`. Return first match's `sprk_playbookid` lookup target ID. — **Acceptance**: unit test for each tiebreak path; integration test confirms env-specific override wins over `*`.
4. **FR-1R-04**: `sprk_matchconditions` JSON schema — flat key-value map; ALL keys must match; value-match: string = equality; array = in-list (OR). `null` or `{}` = always match. Documented at `architecture/playbookconsumer-matchconditions.schema.json`. Match keys initially supported: `mimeType` (← `attachmentMetadata.contentType`), `documentType` (← `manifest.documentType` when classification available; Phase 4 future). — **Acceptance**: schema doc landed; unit tests cover null/empty/string/array/multi-key cases.
5. **FR-1R-05**: Migrate 6 BFF consumers from env-var reads to `IConsumerRoutingService.ResolveAsync`:
   - `MatterPreFillService` (was `Workspace__MatterPreFillPlaybookId` → `ResolveAsync("matter-pre-fill")`)
   - `ProjectPreFillService` (was `Workspace__ProjectPreFillPlaybookId` → `ResolveAsync("project-pre-fill")`)
   - `WorkspaceAiService` (was `Workspace__AiSummaryPlaybookId` → `ResolveAsync("ai-summary")`)
   - `WorkspaceFileEndpoints` (was `Workspace__SummarizePlaybookId` → `ResolveAsync("summarize-file", consumerCode:..., context: { mimeType })`)
   - `SessionSummarizeOrchestrator` (was hardcoded `chat-summarize` lookup → `ResolveAsync("chat-summarize")`)
   - `AppOnlyAnalysisService` (was hardcoded email-analysis GUID at `AppOnlyAnalysisService.cs:46,1068` → `ResolveAsync("email-analysis")`)
   — **Acceptance**: grep `Workspace__.*PlaybookId` in `src/server/api/Sprk.Bff.Api/Services/` returns zero hits except the deprecation telemetry call site; integration test per consumer confirms identical behavior.
6. **FR-1R-06**: Env vars `Workspace__MatterPreFillPlaybookId`, `Workspace__ProjectPreFillPlaybookId`, `Workspace__AiSummaryPlaybookId`, `Workspace__SummarizePlaybookId` deprecated with telemetry warning when read — `WorkspaceOptionsValidator` (new) emits `WARN: Workspace__*PlaybookId env vars are deprecated; configure via sprk_playbookconsumer Dataverse table` on startup IF any are non-null. Activity tag `routing.envvar_fallback_used=true` for any read. — **Acceptance**: startup log shows warning when env var set; KQL dashboard query documented; runtime path also tagged.
7. **FR-1R-07**: New PowerShell script `scripts/dataverse/Seed-PlaybookConsumers.ps1` seeds the 6 initial routing records using the current production GUIDs from existing env vars. Idempotent (UPSERT by alternate key). — **Acceptance**: script seeds 6 records on empty table; rerun is no-op; script documented in `scripts/README.md`.
8. **FR-1R-08**: Phase 1R exit gate — grep zero `Workspace__.*PlaybookId` reads in `Services/`; 6 consumer integration tests green; cache hit ratio >70% measured during stabilization. — **Acceptance**: exit gate checklist in `notes/handoffs/`.

#### Out of scope (1R-OOS)

- ❌ PCF rule-builder for editing `sprk_matchconditions` (defer; admin uses raw JSON in Power Apps form during Phase 1R)
- ❌ Migration of remaining hardcoded playbook references discovered in Phase 6 (specialized playbook authoring — separate scope)
- ❌ Cross-tenant routing rules (single-tenant scope per existing project boundary)

#### Phase 1R wave structure → see [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md#phase-1r---sprk_playbookconsumer-routing-table-revised-2026-06-24)

### Phase 5+7 Revised Scope (Owner decision 2026-06-24 — post-MVP UAT)

The 2026-06-22 MVP cut shipped infrastructure but deferred the user-visible routing convergence and output composition behavior the project was originally designed to deliver. UAT on 2026-06-24 confirmed the gap: the `/summarize` slash + NL flows still produce different routing (slash → chat sibling; NL → "document not in session"), and Workspace tabs open blank because output is rendered to chat. **The owner authorized re-opening Phase 5+7 with a revised, more sophisticated scope** that converges slash + NL behind LLM-in-the-loop intent detection and replaces the brittle schema-aware widget model with multi-node Output composition. This subsection is binding for the remainder of the project and supersedes "Phase 5 — WP2 file-aware classification" MVP entries above and the Phase 7 entries in `plan.md` §1.7. **All other frozen spec content (Phase 0–4, NFR-A1–A7, WP3/WP4 destination wiring) is unchanged.**

#### Owner-confirmed UX flow (Phase 5R)

```
1. User uploads file(s) → persists in T2 (ChatSession.UploadedFiles[])
2. User types  /summarize  OR  "summarize this document"  OR  any natural language
3. BFF: vector-match against playbook-embeddings (~150ms; existing Phase B)
   - IF top-1 confidence ≥ 0.85 → return top 3 (or all ≥ 0.80)
   - ELSE (ambiguous) → LLM (gpt-4o-mini, structured output) picks best 3 from top 5
     (+~500-800ms; total worst-case ~1s — owner-accepted budget)
4. Chat (Assistant pane): "Which playbook would you like me to use?"
   - Inline link buttons for top-N candidates (always show — never auto-execute)
   - + "Open Library Modal" link (existing Library modal; needs bug fix)
5. User clicks link button → playbook executes against the SAME session attachments
6. Output via Output Node config → workspace widget renders per-section
   (multi-node composition; section_name-keyed streaming; not schema-position-keyed)
7. Subsequent turns: agent can read both the uploaded file AND the workspace output
   via T2 round-trip; session attachments retained across turns (no per-turn drop)
```

#### Functional Requirements (Phase 5R — additive; numbered FR-46 through FR-59)

**Intent + matching (5R-A)**

46. **FR-46**: Hybrid intent detection — vector match against playbook-embeddings is PRIMARY (existing Phase B); LLM reranker (gpt-4o-mini, structured output) fires ONLY when top-1 score below confidence threshold OR multiple candidates within `confidenceDeltaMargin` of top-1. LLM input is constrained to `(userMessage, attachmentMetadata[filename, contentType, textLength], top-5 candidate {playbookId, playbookCode, displayName, embeddingScore, jpsMatchingMetadata}) ` — NO file text content (ADR-015). — **Acceptance**: telemetry shows `llmRerankInvoked` count; all-clear vector-match case shows 0 LLM calls; ambiguous case shows exactly 1 LLM call ≤800ms.
47. **FR-47**: Confidence-based top-N return — `confidenceThreshold = 0.85`, `confidenceDeltaMargin = 0.05`, `secondaryThreshold = 0.80`. Return top 3 candidates above secondaryThreshold; if fewer than 3 match, return whatever matches; if more than 3 match secondaryThreshold, top 3 by score. — **Acceptance**: unit test for each branch; integration test confirms FE receives the right payload shape.
48. **FR-48**: User confirmation always shown — `PlaybookDispatcher.DispatchAsync` (file-aware path from FR-15) NEVER auto-executes; always emits `playbook_options` SSE event with top-N. Slash and NL paths produce identical confirmation behavior. — **Acceptance**: integration test confirms /summarize + "summarize this document" both emit `playbook_options` (no execution); FR-20 slash/NL parity test from task 115 remains green.

**Chat link-buttons UX (5R-B)**

49. **FR-49**: SSE event `playbook_options` carrying `{ candidates: [{ playbookId, playbookCode, displayName, confidence, reason }], libraryModalCta: true, sessionAttachmentIds: string[] }`. ADR-015 tier-1 safe: NO user message text, NO file content, NO candidate descriptions beyond displayName. — **Acceptance**: SSE event shape locked via integration test; tier-1 audit reviewer verifies no leaked content.
50. **FR-50**: Frontend renders `playbook_options` as inline chat-message link buttons (Fluent UI v9 `Button appearance="primary"` per option); click → `POST /api/ai/playbook-dispatch/execute` with `{ playbookId, sessionAttachmentIds, originalMessage, sessionId }`. — **Acceptance**: e2e UI test renders 3 buttons, click triggers execution path; visual regression preserved.
51. **FR-51**: "Open Library Modal" link always rendered alongside top-N buttons; click opens existing Library modal pre-filtered by `sessionAttachmentTypes` (when classification is available; otherwise unfiltered). — **Acceptance**: link click opens existing modal at expected filtered state.

**Multi-node Output composition (5R-C — THE BIG ONE)**

52. **FR-52**: New `NodeType.DeliverComposite` extension to `PlaybookExecutionEngine` — Output Node accepts N upstream Action node outputs keyed by `sectionName`; composes per consumer destination (workspace widget / form prefill / chat). Existing single-action Output Node behavior unchanged when only one upstream is wired. — **Acceptance**: engine unit test executes composite node with 3 action upstreams; SSE event sequence shows 3 `section_data` events keyed by section name.
53. **FR-53**: Per-section SSE streaming — events `section_started`, `section_data` (incremental), `section_completed` keyed by section name (NOT schema position). Backward-compat: existing schema-position playbooks continue to emit `FieldDelta` until migrated per FR-58. — **Acceptance**: streaming integration test confirms section events ordered by completion (not declaration); legacy `FieldDelta` continues for unmigrated playbooks.
54. **FR-54**: `StructuredOutputStreamWidget` rework — listens by section name, not schema position; widget hydrates a `sections: Record<string, SectionState>` map. Coordination point count drops from 5 (current: schema-on-action + schema-aware widget) to 2 (section name + section state). — **Acceptance**: widget unit test driven by `section_data` events keyed by name; UI regression test confirms backward compat for unmigrated playbooks.
55. **FR-55**: ADR for multi-node Output composition pattern — authored at `.claude/adr/ADR-NNN-multinode-output-composition.md` and `docs/adr/ADR-NNN-multinode-output-composition.md`. Captures: (a) the 5-coordination-point frailty being replaced; (b) section-name-keyed routing rationale; (c) migration path (per-playbook incremental); (d) when to NOT use composite (e.g., chat sibling stays single-action). — **Acceptance**: ADR landed; `.claude/adr/INDEX.md` updated; `.claude/CHANGELOG.md` entry.

**Session continuity + memory round-trip (5R-D)**

56. **FR-56**: `ChatSession.UploadedFiles[]` invariant — files persist across multi-turn conversation without per-turn drop; no implicit eviction inside the active session TTL. — **Acceptance**: integration test uploads file, sends 5 chat turns, asserts `ChatSession.UploadedFiles.Count` unchanged; FR-26 enriched fields preserved.
57. **FR-57**: Workspace output → AI memory — when a playbook writes to a Workspace tab, the tab content (rendered widget state, NOT raw streaming chunks) is accessible to subsequent chat turns via T2. New tool handler `get_workspace_tab_content` (read-only; reuses existing Pillar 6b `get_workspace_tab_state` plumbing) returns the composed widget state. — **Acceptance**: integration test runs `summarize-document-for-workspace`, then asks "make the summary shorter"; agent reads tab content via `get_workspace_tab_content`; subsequent turn shows modified content target.

**Migration + cleanups (5R-E)**

58. **FR-58**: Migrate `summarize-document-for-workspace@v1` to multi-node composition (proof point); chat sibling `summarize-document-for-chat@v1` STAYS single-action (no benefit from composition for chat). Migration is a Dataverse-data update (per architecture §11 migration path) — playbook node graph rewritten to N Action → 1 DeliverComposite Output. — **Acceptance**: migrated playbook executes end-to-end against bff-dev; section streaming produces correct widget render; chat sibling regression remains green.
59. **FR-59**: Library modal `Cannot read properties of null (reading 'toLowerCase')` bug fix — defensive null guard in search-filter normalization; root-cause analysis in handoff doc. — **Acceptance**: e2e regression for Library modal open + search + filter passes; no console errors.

#### Phase 5R FRs that SUPERSEDE Phase 5 MVP

| Original FR | Original status | 5R replacement | Reason |
|---|---|---|---|
| FR-16 (Phase A fingerprint <50ms) | MVP-deferred | Subsumed into FR-46 hybrid path | No standalone fingerprint stage; vector match IS phase A |
| FR-18 (Phase C reconciliation) | MVP-deferred | Replaced by FR-46 LLM rerank + FR-47 top-N return | Reconciliation logic was over-engineered; LLM rerank is simpler |
| FR-19 (decider dispatch budget) | MVP-deferred | Folded into FR-46 acceptance criteria (~1s worst case) | Budget moves with new design |
| Phase 5 task 118 (load test) | MVP-deferred | Replaced by `Phase5RoutingTelemetry` (FR-117 existing) + production traffic signals | Load test was infrastructure-only; signal comes from production |

#### Phase 7R (revised — unchanged from original plan, dependency on Phase 5R)

Phase 7 sequence stays as originally scoped — `CapabilityRouter` retirement, FE slash dict deletion, dedup test, baselines, full UAT, code review, ADR check, wrap-up. Phase 7 task 141/142 atomic deletion sequence now MUST wait until Phase 5R routing is in production (slash + NL parity verified end-to-end) — same dependency rule as original spec.

#### Out of scope (5R-OOS — owner-confirmed 2026-06-24)

- ❌ Card-style UI for playbook options (user wants link buttons only)
- ❌ Draft document playbook + WorkingDocument widget (defer to R7+)
- ❌ Edit-summary capability (defer; "next-next round")
- ❌ Cross-session matter memory beyond what R6 task 095 already wires
- ❌ Specialized playbook authoring beyond `summarize-document-for-workspace@v1` migration
- ❌ Backwards-compat for schema-position playbooks beyond the migration window (will deprecate after FR-58 lands)

#### Architectural principles preserved (NFR-A1 through NFR-A7 binding)

All 7 architectural principles continue to bind. FR-46 LLM rerank input limited to metadata-only per NFR-A7 (ADR-015 tier-1). FR-57 workspace→memory round-trip via T2 explicit promotion per NFR-A1. FR-52 multi-node composition preserves R6 wire-not-build principle per NFR-A5 (extends `PlaybookExecutionEngine`; does not replace it). FR-54 widget rework continues citation-bearing trust model per NFR-A3.

## Scope

### In Scope

**§1.7 Stable-ID consumer migration** (existing `sprk_playbookid` column; consumers must adopt it) — **REVISED 2026-06-22** (was "Stable-code", now "Stable-ID" per the corrected field-role table above)
- Add `/api/ai/playbooks/by-id/{id}` resolution endpoint (5-min TTL per ADR-014; tenant-scoped); `PlaybookLookupService` already supports alternate-key lookup — extend to query `sprk_playbookid`.
- Migrate 9 hardcoded consumers (5 GUID-based, 4 name-based) to resolve by `sprk_playbookid`; see Owner Clarifications for sequencing.
- Pattern C cleanup first (PCF UniversalQuickCreate duplicate `useAiSummary` stub + stale GUID comments — both completed; LegalWorkspace deletion was REMOVED, see Out-of-Scope) → Pattern A (typed-options `*PlaybookId` + `sprk_playbookid` lookup) → Pattern B (name-resolve → ID-resolve).
- Deprecate `/by-name/` endpoint with telemetry warnings; remove after stabilization window.
- Action-ID reform in scope: parallel pattern with playbooks — code resolves actions by `sprk_actionid`; `sprk_actioncode` remains admin slug. Drop `@v1` suffix on new action slugs.

**WP1.5 Index governance** (additive Dataverse schema + Power Apps UX)
- Add `sprk_lastindexedat`, `sprk_indexstatus`, `sprk_lastindexerror`, `sprk_indexhash`, `sprk_jpsmatchingmetadata` to `sprk_analysisplaybook` (per owner: confirmed approved).
- Expand `PlaybookEmbeddingDocument` shape to embed `documentTypes + intents + triggerPhrases` from `sprk_jpsmatchingmetadata`.
- Power Apps "Send to Index" button; validation gate (description / documentTypes / destinationHint required); nightly drift detection job comparing `sprk_indexhash` to recomputed hash.
- Admin view filtered to `sprk_indexstatus IN ('stale', 'failed', 'not-indexed')`.
- DO NOT duplicate existing `sprk_playbookcode` / `sprk_playbookid` fields.

**WP2 File-aware classification** (Hybrid (C) primary per owner clarification)
- `PlaybookDispatcher.DispatchAsync` accepts `attachments` parameter; embeds `(userMessage + filename + first ~2000 chars + manifest documentType pre-filter)`.
- Phase A (per-file fingerprint <50ms total) + Phase B (per-file parallel vector match ~150-300ms) + Phase C (reconciliation; LLM decider only on disagreement, ~400ms).
- Primary path uses precomputed `documentType` from WP5.5 upload manifest as structured pre-filter against `sprk_jpsmatchingmetadata.documentTypes`.
- Fallback to per-file parallel match (A) when manifest absent; LLM decider on disagreement.
- `commandIntent` (slash bias) becomes a vector-query bias, not a separate routing path.
- Total budget: ≤1225ms worst case (5 files disagreeing); within existing 2s `PlaybookDispatcher.TotalTimeout`.
- Stay on `text-embedding-3-large`; defer voyage-law-2 / Document Intelligence.

**WP3 Destination metadata wiring** (make Workspace destination data-driven, not implicit-streaming)
- Add `NodeDestination.Both` enum value + JSON converter line to `NodeRoutingConfig.cs:31-64,247-272`.
- Wire `NodeRoutingConfig` into `DispatchResult` — add `NodeDestination` and `WidgetType` properties (default values preserve current call sites unchanged).
- Populate `DispatchResult.NodeDestination` in `PlaybookDispatcher` by loading the primary AI/DeliverOutput node and calling `NodeRoutingConfig.Parse(node.ConfigJson)`.
- Add Workspace / Both / FormPrefill / SideEffect cases to `PlaybookOutputHandler.HandleOutputAsync`:
  - **Workspace**: emit `workspace.tab_open` SSE event; delegate streaming to `PlaybookExecutionEngine`; produce no chat tokens.
  - **Both**: same as Workspace plus templated ack ("I've added a {playbookName} result to the Workspace.").
  - **FormPrefill**: no-op (NFR-07 forbids modifying pre-fill flow).
  - **SideEffect**: emit no user-visible content; rely on telemetry/audit.
- Add JSON-schema validation gate to `Deploy-Playbook.ps1` validating `sprk_configjson` against `node-routing-config.schema.json` at deploy time.
- Eliminates today's implicit-streaming Workspace destination behavior in `sseToPaneEventBridge.ts:174-256`; destination becomes structural.

**WP4 Retire CapabilityRouter** (single-phase cutover; no backward compat per Q8 resolution)
- Single phase per Q8 resolution: no parallel-run / cutover staging.
- Delete: `CapabilityRouter.cs`, `CapabilityRouterOptions.cs`, `Layer2Options`, `DataverseCapabilityManifestLoader.cs`, `CapabilityManifest*.cs`, `ICapabilityManifest.cs`, `ICapabilityManifestLoader.cs`, `ManifestRefreshService.cs`, `CapabilityValidator.cs`, `CapabilityValidationContext.cs`.
- Frontend: remove `SoftSlashRouter.SOFT_SLASH_TO_INTENT` dict; `SoftSlashRouter.decorateBody` retains wire-format but field semantics shift to bias hint per Q5 resolution (rename to align with purpose; no back-compat needed).
- Tool filtering: per-matched-playbook scopes + always-on conversational tools.
- Preserve R6 FR-30 CapabilityRouter dedup semantics through new PlaybookDispatcher path (binding test per Q20).
- `sprk_aicapability` table NOT built (and never will be).

**WP5 Stateful chat (6-tier memory)** [v2 — reconciled with architecture doc 2026-06-21]

Architecture, component model, storage map, data-flow patterns, tool surface, and migration path are BINDING per [`architecture/stateful-chat-architecture.md`](architecture/stateful-chat-architecture.md):
- **§3** 6-tier memory model + cross-tier separation rules
- **§4** component dependency graph (existing R6 components leveraged + 8 new components introduced)
- **§4.5** Components NOT to build (binding decisions — also surfaced as MUST NOT rules below)
- **§5** Insights Engine reuse boundary — PATTERN-LEVEL ONLY. NOT used for chat memory: `spaarke-insights-index`, `MultiIndexComposer`, `InsightsOrchestrator`, `EvidenceSufficiencyNode`, `GroundingVerifyNode`, `sprk_matter.sprk_performancesummary`. Pattern-level reuse permitted: versioned envelope with citations, Redis hot-tier TTL, write-through to compliance store.
- **§6** data flow patterns (upload pipeline, per-turn assembly, recall flow, promote-to-matter workflow, workspace state read/write with Q8 conflict check)
- **§7** storage architecture (no new Cosmos containers; no new AI Search index for memory; `sprk_aichatmessage` retire to write-only audit role)
- **§8** 8-tool surface (T2: `list_session_files` / `get_file_manifest` / `recall_session_file` / `write_session_memory`; T3: `retrieve_matter_memory` / `promote_to_matter_memory`; T4 read-only: `get_user_preferences` / `get_org_templates`; plus existing Pillar 6b workspace tools always-on when any tab is agent-editable)
- **§11** migration path: extend existing R6 components, wrap with new tool handlers, retire `ChatDataverseRepository` placeholder methods

FR-45 wiring is VERIFIED at `PlaybookChatContextProvider.cs:627` per architecture §11.1 — do NOT regress. Insights Engine coordination is binding-NEGATIVE: no breaking changes AND no force-fit reuse of Insights internals.

All sub-WPs ship in this project; sequenced to allow internal milestones. The WP5 FRs below (FR-26 through FR-37) operationalize the architecture-doc bindings into testable requirements.

**WP6 Specialized playbooks + Path 3 JPS `$ref` extension** (additive only per audit correction)
- Extend `PlaybookExecutionEngine.ExecuteChatSummarizeAsync` (Path 3) to invoke `JpsRefResolver` before LLM call; streaming UX preserved (per Q7).
- Author 3 new specialized playbooks: `summarize-nda` (uses SKL-003 NDA Review + KNW-006 NDA Standards), `summarize-patent` (requires NEW SKL Patent Review + NEW KNW Patent Standards), `extract-invoice` (uses SKL-002 Invoice Processing).
- Naming: kebab-case stable codes, NO `@v1` suffix on new playbooks/actions.
- Dataverse audit required (NOT just repo grep) for PB-009 / PB-012 / PB-015 / PB-017 before any deprecation decision; per Q17: review and remediate rather than delete unless truly duplicative.
- PB-009 "Summarize NDA" verification path: inspect actual node graph in Dataverse, rewrite description for embedding-friendly text, extend via JPS `$ref` to SKL-003 + KNW-006, populate `sprk_jpsmatchingmetadata` + verify `sprk_playbookcode`.
- Bind default Persona via R6 Pillar 1 in Path 3.
- Add evidence-sufficiency precheck in Path 3 (mirror `EvidenceSufficiencyNode`).

### Out of Scope

- **R6 closeout WP1 description rewrite** for the pre-existing 5+ production summary playbooks — that text-cleanup pass lands in R6 Phase E micro-wave + tasks 089/090, NOT here. (WP1.5 index governance + new specialized playbook descriptions ARE in scope here.)
- **Modification of any of the 5 production-bound playbooks** per design §1.5: `summarize-document-for-chat@v1`, `summarize-document-for-workspace@v1`, `"Document Profile"`, `"Create New Matter Pre-Fill"`, `"Create New Project Pre-Fill"`. Migration in place via stable ID only; no delete; no rename; no output-schema change. **NOTE (Q&A 2026-06-22)**: spec previously included `"Summarize New File(s)"` as a 6th row — DROPPED. Dataverse describe shows no such record exists in DEV; closest match is `Summarize File` (PB-015). The multi-file wizard's runtime error ("An error occurred while summarizing the uploaded documents.") is filed as B-015 for separate triage; out of scope for this project.
- **NFR-07 pre-fill flow signatures + 45s timeout + `useAiPrefill` hook + `$choices` constraint** — preserved verbatim.
- **NFR-08 11 production node executors** — preserved.
- **Insights Engine `sprk_performancesummary` semantics** — DO NOT touch.
- **Voyage-law-2 embedding migration** — defer.
- **Azure Document Intelligence classifier** — wrong tool for Stage 1 routing.
- **Foundry-pattern long-term memory** (Q4) — not in scope.
- **`sprk_matterfacts` separate entity** — Cosmos `memory` container is the store; no new entity.
- **Detailed promote-to-matter-memory UX** (Q12) — Context pane approval surface direction agreed; detailed design deferred.
- **`sprk_userpreferences` field expansion** (Q13) — defer; chat agent reads via cached snapshot of existing shape.
- **Per-turn cache invalidation benchmark** (Q14) — defer; measure during stabilization.

### Affected Areas

**Backend — routing**
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs` — extend with `attachments` parameter + Phase A/B/C
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookLookupService.cs` — already supports `by-code` alternate-key lookup
- `src/server/api/Sprk.Bff.Api/Api/Ai/PlaybookEmbeddingEndpoints.cs:32` — extend with `by-code` endpoint
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/PlaybookEmbeddingService.cs:28-30` — extend embed-input composition with `sprk_jpsmatchingmetadata`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs` — DELETE
- `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/DataverseCapabilityManifestLoader.cs` — DELETE
- `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouterOptions.cs` — DELETE

**Backend — execution**
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs:185` — Path 3 extension (invoke `JpsRefResolver` before LLM call)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs:442-499` — reference for Path 1 `$ref` resolution
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionSummarizeOrchestrator.cs:78-79` — remove hardcoded GUID; migrate to stable code

**Backend — consumers to migrate (§1.7 stable codes)**
- `src/server/api/Sprk.Bff.Api/Services/Ai/MatterPreFillService.cs:43-44` — Pattern A: code lookup via typed options
- `src/server/api/Sprk.Bff.Api/Services/Ai/ProjectPreFillService.cs:42-43` — Pattern A
- `src/server/api/Sprk.Bff.Api/Services/Ai/WorkspaceAiService.cs:41-44` — Pattern A
- `src/server/api/Sprk.Bff.Api/Api/Ai/WorkspaceFileEndpoints.cs:29-32,254` — Pattern A; fix ADR-018 violation (lift `Workspace:SummarizePlaybookId` to typed `WorkspaceOptions`)
- `src/server/api/Sprk.Bff.Api/Services/Ai/AppOnlyAnalysisService.cs:46,1068` — Pattern B (name → code)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatContextMappingService.cs` — Pattern B
- `src/server/api/Sprk.Bff.Api/Options/WorkspaceOptions.cs:35` — add `Workspace:SummarizePlaybookCode`; fix stale `3f21cec1-...` GUID comment

**Backend — memory (WP5)**
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/MatterMemoryService.cs` — FR-45 wiring at `PlaybookChatContextProvider.cs:627` VERIFIED per architecture §11.1; do not regress
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/MemoryCompositionService.cs` — unify with `IPlaybookChatContextProvider`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs:907,2178` — prompt assembly + Pillar 9 closed-union (closed-union deferred to R7 backlog)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatDataverseRepository.cs` — retire placeholder methods; rename interface to `IChatAuditRepository`
- `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatSession.cs:72-91,134-140` — extend `ChatSessionFile` with summary + manifest + docType
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/PinnedContextRepository.cs` — leverage existing
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/PinnedContextRecallService.cs` — leverage existing
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/SummarizationCompressionService.cs` — leverage existing
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/PromptBudgetTracker.cs` — leverage existing

**Frontend**
- `src/solutions/SpaarkeAi/src/components/conversation/SoftSlashRouter.ts:139-144` — remove SOFT_SLASH_TO_INTENT dict; bias-hint wire format
- `src/solutions/SpaarkeAi/src/components/conversation/sseToPaneEventBridge.ts:174-256` — coordinate with WP3 Workspace destination (R6 closeout)
- `src/solutions/SpaarkeAi/src/components/conversation/CommandRouter.ts` — unchanged surface
- `src/client/shared/Spaarke.AI.Widgets/src/registry/WorkspaceWidgetRegistry.ts` — extend with `agent-editable` flag
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/StructuredOutputStreamWidget.tsx` — unchanged
- `src/client/code-pages/UniversalQuickCreate/control/services/useAiSummary.ts` — migrate or delete (duplicate of shared hook)
- ~~`src/solutions/LegalWorkspace/src/components/CreateMatter/CreateRecordStep.tsx` (+ Project / WorkAssignment siblings) — delete (dead code per OC-R4-05)~~ — **REMOVED (Q&A 2026-06-22)**: spec misread OC-R4-05. Per [`docs/architecture/LEGALWORKSPACE-RETIREMENT.md`](../../docs/architecture/LEGALWORKSPACE-RETIREMENT.md) §3, OC-R4-05 retires ONLY the `sprk_corporateworkspace` Dataverse WEB RESOURCE; component source is EXPLICITLY PRESERVED as library code ("Authors MUST NOT delete LegalWorkspace component code on the grounds of 'R3 FR-25 is superseded'; the components are the dashboard engine"). Also: 2 of 3 named files don't exist (`CreateProject/CreateRecordStep.tsx` and `CreateWorkAssignment/CreateRecordStep.tsx` — each island uses its own step-component name; the spec assumed parallel naming that never existed). Task 001 was CANCELLED with prejudice. The remaining Pattern C cleanup (PCF `useAiSummary` duplicate stub + stale `3f21cec1-` GUID comments) was legitimate and proceeded.

**Frontend shared**
- `src/client/code-pages/.../useAiSummary.ts:285` — Pattern B: name → code
- `src/solutions/SpaarkeAi/...DocumentEmailWizard.tsx:628` — Pattern B

**Specialized playbooks (new)**
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Playbooks/summarize-nda.playbook.json` (NEW)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Playbooks/summarize-patent.playbook.json` (NEW)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Playbooks/extract-invoice.playbook.json` (NEW)
- `scripts/seed-data/skills.json` — add Patent Review skill (NEW)
- `scripts/seed-data/knowledge.json` — add Patent Standards knowledge source (NEW)

**Backend — destination metadata (WP3)**
- `src/server/api/Sprk.Bff.Api/Models/Ai/NodeRoutingConfig.cs:31-64,247-272` — add `Both` enum value + converter line
- `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/DispatchResult.cs:37-46` — add `NodeDestination` + `WidgetType` properties with defaults
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs` — populate new DispatchResult properties
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookOutputHandler.cs:108-117` — add Workspace / Both / FormPrefill / SideEffect cases
- `scripts/dataverse/Deploy-Playbook.ps1` — add `node-routing-config.schema.json` validation gate
- `src/solutions/SpaarkeAi/src/components/conversation/sseToPaneEventBridge.ts:174-256` — coordinate so handler-driven Workspace dispatch replaces implicit-streaming behavior

**Dataverse schema (additive)**

**Field-role clarification (Q&A 2026-06-22)** — both entities have parallel fields for two distinct purposes:

| Entity | Field | Role | Format | This project uses for lookup? |
|---|---|---|---|---|
| `sprk_analysisplaybook` | `sprk_analysisplaybookid` (GUID PK) | Primary key | GUID | No (env-changing) |
| `sprk_analysisplaybook` | **`sprk_playbookid`** (Text 100) | **Immutable opaque ID — locked across environments** | **GUID-format string (mirrors PK)** | **YES — code resolves by this field** |
| `sprk_analysisplaybook` | `sprk_playbookcode` (Text 10) | Admin-facing descriptive slug | `PB-NNN` convention (existing) | No — admin/UI only |
| `sprk_analysisaction` | `sprk_analysisactionid` (GUID PK) | Primary key | GUID | No |
| `sprk_analysisaction` | **`sprk_actionid`** (Text 100) | **Immutable opaque ID** | **GUID-format string (mirrors PK)** | **YES (for actions)** |
| `sprk_analysisaction` | `sprk_actioncode` (Text 64) | Admin-facing descriptive slug | (admin convention) | No — admin/UI only |

Schema verification (Dataverse describe, 2026-06-21): the 4 WP1.5 index-tracking fields **already exist** on `sprk_analysisplaybook`:
- `sprk_lastindexedat` (DATETIME) ✅
- `sprk_indexstatus` (CHOICE with 5 options: Not Indexed 100000000 / Pending 100000001 / Indexed 100000002 / Stale 100000003 / Failed 100000004) ✅
- `sprk_lastindexerror` (NVARCHAR(1000)) ✅
- `sprk_indexhash` (NVARCHAR(100)) ✅
- `sprk_jpsmatchingmetadata` — **NOT YET PRESENT**; task 031 adds it.

**Project work to do**:
- ❌ DO NOT add the 4 index-tracking fields (already exist) — task 030 demoted to verification.
- ✅ ADD `sprk_jpsmatchingmetadata` (MultilineText) — task 031.
- ✅ Migration: backfill **`sprk_playbookid`** on the production-bound playbooks per §1.7.3 (target value = the row's `sprk_analysisplaybookid` GUID, per existing convention). 3 of 5 rows already have this populated; only 2 (`summarize-document-for-chat@v1`, `summarize-document-for-workspace@v1`) need backfill. Task 014 simplified.
- ❌ DO NOT modify `sprk_playbookcode` values — admin-facing field, existing `PB-NNN` convention preserved.
- ❌ DO NOT create new code/id fields — existing pair satisfies both roles.

## Requirements

### Functional Requirements

#### Stable-code consumer migration

**Field semantics correction (Q&A 2026-06-22)**: FR-01 through FR-06 below were drafted before the `sprk_playbookcode` vs `sprk_playbookid` field-role clarification. The corrected semantics: **stable-ID lookups resolve by `sprk_playbookid` (immutable opaque ID, GUID-format)**; `sprk_playbookcode` is the admin-facing descriptive slug (existing `PB-NNN` convention preserved). Where the original FRs say "`*PlaybookCode`" or "`sprk_playbookcode`" in a lookup context, read "`*PlaybookId`" / "`sprk_playbookid`". Routes named `/by-code/{code}` become `/by-id/{id}`. The two-field-role separation aligns with the matching pair on `sprk_analysisaction` (`sprk_actionid` for lookups; `sprk_actioncode` for admin slug).

1. **FR-01**: `GET /api/ai/playbooks/by-id/{id}` resolution endpoint returns the playbook by `sprk_playbookid` with 5-min ADR-014 cache TTL, tenant-scoped, 404 RFC 7807 ProblemDetails. — **Acceptance**: integration test resolves the chat-summarize playbook by its GUID within 100ms warm cache; cold path under 500ms.
2. **FR-02**: All 5 GUID-based consumers (MatterPreFill, ProjectPreFill, WorkspaceAiService, WorkspaceFileEndpoints, SessionSummarizeOrchestrator) resolve playbooks by `sprk_playbookid` via typed options (`*PlaybookId`), not raw `Guid.Parse(...)` calls. — **Acceptance**: code search for `Guid.Parse("44285d15` / `2d660cad` / `fc343e9c` / `4a72f99c` / `18cf3cc8` returns zero hits in `Services/Ai/`.
3. **FR-03**: All 4 name-based consumers (`AppOnlyAnalysisService` x2, `useAiSummary.ts:285`, `DocumentEmailWizard.tsx:628`, `ChatContextMappingService`) resolve playbooks by `sprk_playbookid`, not literal name strings. — **Acceptance**: `/by-name/` endpoint emits deprecation warning per call; calls drop to zero after migration.
4. **FR-04**: `WorkspaceOptions.cs` fixes the ADR-018 violation by adding `SummarizePlaybookId` typed property; `WorkspaceFileEndpoints.cs` reads via `IOptions<WorkspaceOptions>`, not raw `IConfiguration[]` indexer. — **Acceptance**: `IConfiguration["Workspace:SummarizePlaybookId"]` returns zero call sites.
5. **FR-05**: Migration sequencing per owner: chat-summarize (`SessionSummarizeOrchestrator`) migrates first, proves resolver infrastructure, then pre-fill flows, then name-resolve consumers. Pattern C cleanup precedes both. — **Acceptance**: PR sequence + task ordering reflects this.
6. **FR-06**: Action codes — REUSE the existing `sprk_actionid` field for lookups (per the parallel field-role pattern with playbooks); `sprk_actioncode` remains the admin-facing descriptive slug. New actions populate `sprk_actionid` with a GUID-format opaque ID and `sprk_actioncode` with a kebab-case slug per admin convention. Drop `@v1` suffix on new action slugs; existing `@v1`-suffixed values remain valid until cutover. — **Acceptance**: 3 new specialized actions populate both fields; lookups use `sprk_actionid`; no new schema columns created.
7. **FR-07**: Frontend `SoftSlashRouter` wire-format renamed to align with purpose (Q5: no back-compat needed). — **Acceptance**: field name reflects "intent hint" semantics; backend treats as vector-query bias parameter to Phase B.

#### Index governance

8. **FR-08**: `sprk_analysisplaybook` extended with 5 additive tracking fields: `sprk_lastindexedat`, `sprk_indexstatus`, `sprk_lastindexerror`, `sprk_indexhash`, `sprk_jpsmatchingmetadata`. — **Acceptance**: Power Apps form displays all 5 fields; CDS-Solution export contains them.
9. **FR-09**: `sprk_jpsmatchingmetadata` follows JSON schema with `documentTypes` (string[]), `intents` (string[]), `triggerPhrases` (string[]), `preferredOver` (string[]), `outputDestination` (enum), `scopeHints` (string[]), `exclusionHints` (string[]). — **Acceptance**: JSON-schema validation on save; deploy-time validation in `Deploy-Playbook.ps1`.
10. **FR-10**: `PlaybookEmbeddingService.cs` extends embed-input composition to include `documentTypes + intents + triggerPhrases` from `sprk_jpsmatchingmetadata`. — **Acceptance**: search for "summarize this NDA" returns Summarize-NDA as top-1 hit on 100-doc Spaarke corpus benchmark.
11. **FR-11**: Power Apps "Send to Index" button on `sprk_analysisplaybook` form sets status to `pending` → calls index endpoint → receives completion → updates `sprk_lastindexedat`, `sprk_indexhash`, status. — **Acceptance**: button visible to admin role; status flips to `indexed` within 60s of successful completion.
12. **FR-12**: Validation gate rejects send-to-index when description / `documentTypes` / `destinationHint` empty; returns structured error naming missing fields. — **Acceptance**: missing-description test returns 400 with `MissingFields: ["description"]`.
13. **FR-13**: Nightly drift-detection job iterates `sprk_analysisplaybook`, recomputes embed-input hash, sets status to `stale` on mismatch. — **Acceptance**: job logs delta count; admin view shows stale rows.
14. **FR-14**: Admin Power Apps view filters `sprk_indexstatus IN ('stale', 'failed', 'not-indexed')`. — **Acceptance**: view named "Playbook Index Drift" appears in Power Apps; ribbon includes "Send to Index" for selected rows.

#### Destination metadata wiring (WP3 — make Workspace destination data-driven)

14a. **FR-14a**: `NodeDestination.Both` enum value added to `NodeRoutingConfig.cs:31-64` plus its JSON converter entry at `:247-272`. — **Acceptance**: deserialize-then-serialize roundtrip of `"both"` configJson value preserves the enum; existing 4 destination values unaffected.

14b. **FR-14b**: `DispatchResult` extended with `NodeDestination` (default `Chat`) and `WidgetType` (default `null`) properties; default values preserve all existing call sites unchanged. — **Acceptance**: `dotnet build` of `Sprk.Bff.Api` passes without modifying any caller; existing unit tests pass without change.

14c. **FR-14c**: `PlaybookDispatcher` populates `DispatchResult.NodeDestination` and `WidgetType` by loading the primary AI/DeliverOutput node and calling `NodeRoutingConfig.Parse(node.ConfigJson)`. — **Acceptance**: integration test dispatching `summarize-document-for-workspace` returns `DispatchResult { NodeDestination: Workspace, WidgetType: "structured-output-stream" }`.

14d. **FR-14d**: `PlaybookOutputHandler.HandleOutputAsync` extended with 4 new cases — Workspace / Both / FormPrefill / SideEffect — replacing today's implicit-streaming Workspace destination. Workspace emits `workspace.tab_open` SSE event + delegates streaming to `PlaybookExecutionEngine`; Both also emits a templated chat ack ("I've added a {playbookName} result to the Workspace."); FormPrefill is no-op (NFR-07 preservation); SideEffect emits no user-visible content. — **Acceptance**: end-to-end test of `summarize-document-for-workspace` opens a Workspace tab via the handler path (NOT via `sseToPaneEventBridge` implicit-streaming behavior); destination is structural, not implicit.

14e. **FR-14e**: JSON-schema validation gate added to `Deploy-Playbook.ps1` that validates `sprk_configjson` against `node-routing-config.schema.json` at deploy time and aborts on schema violation. — **Acceptance**: deploy attempt with malformed configJson fails with structured error; deploy with valid configJson succeeds.

14f. **FR-14f**: Backward compat preserved structurally — `NodeRoutingConfig.Parse(null)` returns `{Destination = Chat}`, so existing playbooks with no `sprk_configjson` continue to render in chat. — **Acceptance**: dispatch of a playbook with empty configJson produces `DispatchResult { NodeDestination: Chat }`; no regression in default behavior.

#### File-aware classification (WP2 — Hybrid (C) primary)

15. **FR-15**: `PlaybookDispatcher.DispatchAsync` accepts `IReadOnlyList<ChatMessageAttachment>` parameter; backward-compatible default for callers passing `null` or empty. — **Acceptance**: call site grep shows new signature; existing tests pass without modification.
16. **FR-16**: Phase A computes per-file fingerprint (filename tokens + content type + textLength + textPrefix 2000 chars + sha256) in <50ms total parallel. — **Acceptance**: benchmark shows ≤5ms per file, 5 files in ≤25ms.
17. **FR-17 [v2 — cross-ref architecture §6.1]**: Phase B uses precomputed `documentType` from the WP5.5 upload manifest (produced by `SessionFileEnrichmentService` per architecture §6.1 — classify + summarize + manifest pipeline) as structured pre-filter against `sprk_jpsmatchingmetadata.documentTypes`; falls back to parallel per-file vector match when manifest absent. — **Acceptance**: manifest-present path measures ≤100ms; manifest-absent path measures ≤300ms for 3 files; cross-WP integration test confirms WP2 reads enriched `ChatSessionFile` fields (`ClassifiedDocType`, `Sections`, etc.) per architecture §11.2 extension.
18. **FR-18**: Phase C reconciliation — all-agree path bypasses LLM decider; LLM decider (gpt-4o-mini, structured output) invoked only on true disagreement with input limited to `(userMessage, per-file filename + contentType + top-3 playbook names)`, NOT file text. — **Acceptance**: telemetry logs `decidersInvoked` count; multi-file all-agree case shows 0 decider calls.
19. **FR-19**: Total dispatch budget within existing 2s `PlaybookDispatcher.TotalTimeout` for all scenarios; p95 ≤1.5s for 1-3 file case. — **Acceptance**: load test shows 0% timeouts at production traffic shape.
20. **FR-20**: `commandIntent` (slash bias) integrated as vector-query bias in Phase B query composition, NOT a separate routing layer. — **Acceptance**: `SoftSlashIntentToCapabilityName` dict removed; slash + NL flows produce identical routing for same query text.
21. **FR-21**: Embedding model stays `text-embedding-3-large`; no Azure-external API calls. — **Acceptance**: `Microsoft.Extensions.Configuration` search for `voyage` returns zero hits.

#### CapabilityRouter retirement (WP4 — single-phase cutover)

22. **FR-22**: `CapabilityRouter.cs` + all supporting infrastructure (10 files listed in Affected Areas) deleted in a single cutover; no parallel-run / phased rollout. — **Acceptance**: `git log` shows single deletion commit; CI green; `grep CapabilityRouter src/` returns zero hits.
23. **FR-23**: Tool filtering replaced — per-matched-playbook scopes + always-on conversational tools (`recall_session_file`, `get_workspace_tab_state`, `document_search`, `update_workspace_tab` when tab is agent-editable). — **Acceptance**: tool list assembly unit tests pass with new logic; LLM receives correct tool subset per playbook.
24. **FR-24**: R6 FR-30 CapabilityRouter dedup semantics preserved through new dispatcher path (chat + workspace SUM-CHAT siblings deduped to one render). — **Acceptance**: existing dedup test suite remains green; one Workspace tab + one chat ack for `summarize-document-for-workspace` invocation.
25. **FR-25 [v2 — cite architecture §7.2 + §11.4]**: `sprk_aichatmessage` repository transition per architecture §7.2 + §11.4 — `ChatDataverseRepository` placeholder methods retired; interface renamed to `IChatAuditRepository` (write-only contract); Cosmos `audit` container confirmed sole reader for compliance queries. Old read methods become extension methods that throw `NotSupportedException` (per architecture §11.2 strategy — preserves binary compat through stabilization window). — **Acceptance**: 5 `Task.CompletedTask` no-ops removed; `GetMessagesAsync` deleted (or throws `NotSupportedException` for stabilization window); integration test confirms audit writes succeed; grep confirms zero read consumers remain.

#### 6-tier stateful chat (WP5)

26. **FR-26**: Tier 2 session memory — `ChatSessionFile` shape extended with `precomputedSummary`, `manifest`, `classifiedDocType`, `classifiedConfidence`, `sections`. Persists to Redis hot tier + Cosmos `sessions` warm tier (90d TTL) via existing `SaveTabsAsync` pattern. — **Acceptance**: integration test uploads PDF, queries `ChatSession.UploadedFiles[0].PrecomputedSummary` after upload pipeline completes.
27. **FR-27 [v2 — VERIFIED per architecture §2 P5 + §11.1]**: Tier 1 working context — `MemoryCompositionService` unifies with `PlaybookChatContextProvider` (no parallel pipelines); composes static prefix from T2-T5 each turn. FR-45 wiring is VERIFIED at `PlaybookChatContextProvider.cs:627` per architecture §11.1 — this is NOT a "to verify" task, it is a "do not regress" invariant. — **Acceptance**: trace log shows single composition call per turn; matter facts appear in prompt for matter-scoped sessions; regression test asserts the FR-45 invocation point at `PlaybookChatContextProvider.cs:627` continues to fire.
28. **FR-28**: Layered context cards per file replace 1-line summary suffix; up to 10 files × ~200 tokens; overflow → `list_session_files` line. — **Acceptance**: prompt inspector shows full card structure (ID, type, uploaded, classified docType, precomputed summary with non-authoritative warning, sections, citations, recall hint).
29. **FR-29 [v2 — cite architecture §8.3]**: Trust framing in system prompt — canonical persona-instruction wording per architecture §8.3 ("When you call `recall_session_file` with `requireCitations: true`, the tool returns citations alongside content. You MUST cite these in any answer that uses the recalled content. Do not quote precomputed summaries from the file cards as if they were the source — those are upload-time approximations marked 'NOT authoritative'. For any legally-precise question (specific clauses, exact wording, dates, parties, dollar amounts), call `recall_session_file` with `requireCitations: true` and cite the source in your answer."). — **Acceptance**: prompt assembly emits the architecture §8.3 wording verbatim on every turn; integration test asserts the substring is present in assembled prompts.
30. **FR-30**: Tool surface expansion — `list_session_files`, `get_file_manifest`, `recall_session_file` (purpose enum + scope enum + maxTokens + requireCitations default true), `write_session_memory`, `retrieve_matter_memory`, `promote_to_matter_memory` (approvalRequired flag), `get_user_preferences`, `get_org_templates`. — **Acceptance**: tool registry exposes all 8 with documented schemas; integration test invokes each successfully.
31. **FR-31**: `recall_session_file` `purpose` enum biases retrieval — `answer_question` / `quote` / `compare` / `summarize` / `extract_dates` / `verify` each with documented retrieval semantics. — **Acceptance**: unit test per purpose verifies returned content shape.
32. **FR-32 [v3 — uses new ADR-030 `memory` channel per 2026-06-21 amendment]**: `promote_to_matter_memory` with `approvalRequired: true` writes pending record to Cosmos `memory` container with doc-type `matter-memory-promotion` (per architecture §7.1 discriminator pattern); emits `memory.promotion_pending` event on the **new `memory` channel** (added by ADR-030 v2 amendment 2026-06-21) with payload `{ promotionId, factSummary (80-char preview), matterId, sessionId }`; Context-pane approval UI subscribes via `usePaneEvent('memory', ...)` and renders notification with Accept/Reject buttons. On Accept → `MatterMemoryService.AppendFactAsync` (existing R6 service) + emit `memory.promotion_resolved { decision: 'approved', factId }` + emit `memory.fact_promoted { factId, matterId, source }`; on Reject → emit `memory.promotion_resolved { decision: 'rejected' }`. Both paths ALSO emit `context.decision_made` audit event for tier-6 audit. New endpoints: `POST /api/memory/promotions/{id}/approve`, `POST /api/memory/promotions/{id}/reject`. New service: `MatterMemoryPromotionService` per architecture §4.4. — **Acceptance**: end-to-end test shows pending Cosmos record → `memory.promotion_pending` event delivered to ContextPane subscriber → approval prompt → user-action → `memory.promotion_resolved` event delivered → durable T3 write via existing `MatterMemoryService.AppendFactAsync` (on accept) OR no T3 write (on reject); ADR-015 audit events emitted for both branches; channel-strings confirmed by grep against `PaneEventTypes.ts`.
33. **FR-33**: Upload pipeline (WP5.5) — classify documentType + precompute summary via gpt-4o-mini + extract structural manifest in parallel with existing chunking/embedding; ~2-3s added latency; classification cost ~$0.0001/file. — **Acceptance**: latency telemetry shows p95 ≤3.5s additive; cost telemetry shows per-file cost target.
34. **FR-34**: Per-turn prompt structure — static prefix (~6K cacheable) + dynamic suffix (~5K) per design WP5.4. — **Acceptance**: token-count instrumentation shows segments within design tolerances; prompt-cache hit rate >70% on multi-turn after warmup.
35. **FR-35 [v2 — Q8 conflict check per architecture §6.5]**: Workspace-write tools (WP5.6) — `update_workspace_tab`, `send_workspace_artifact`, `close_workspace_tab` use Harvey/Artifacts targeted-edit pattern (`{tabId, edit: {old, new}}`); always-on when any tab is `agent-editable`. Before applying agent edit, MUST check `tab.lastUserEditAt` vs the agent's last read; refuse with `{ success: false, conflictReason: string }` if user edited since agent's last read; require LLM to re-read + retry. — **Acceptance**: integration test edits a workspace tab via targeted-edit call; full state retrieved via `get_workspace_tab_state` verifies application; concurrency test shows refused write + re-read flow when user-edit-during-agent-call simulated.
36. **FR-36 [v2 — reconciled with architecture §5.2.1 + §5.5]**: Tier 5 retrieval wraps existing **chat-domain** AI Search indexes via new tool handlers: `spaarke-session-files` (primary for `recall_session_file` mode='section'), `spaarke-files-index` (matter-bound chunks), `spaarke-rag-references` (knowledge sources). MUST NOT use `spaarke-insights-index` for chat memory (Insights domain; categorical mismatch per architecture §5.2.1). MUST NOT add a new chat-memory index (architecture §4.5). — **Acceptance**: code review confirms zero `spaarke-insights-index` reads from chat-memory tools; no new index provisioned; recall integration tests pass against the 3 chat-domain indexes.
37. **FR-37 [v2 — reconciled with architecture §5.2-§5.3]**: Memory composition uses `MemoryCompositionService` (R6 task 067; time-dependent layered injection with FR-42 pinned-never-drops invariant). MUST NOT use `MultiIndexComposer` for memory composition (different semantics — knowledge-tier blending, not time-dependent layering; architecture §5.2.2). MUST NOT use `InsightsOrchestrator`, `EvidenceSufficiencyNode`, `GroundingVerifyNode`, or `sprk_matter.sprk_performancesummary` for chat memory (architecture §5.2.3-§5.2.6). Pattern-level reuse only per architecture §5.3 (versioned envelopes with citations, Redis hot-tier with TTL, write-through to compliance store). — **Acceptance**: code review confirms zero `MultiIndexComposer.Merge` / `InsightsOrchestrator` / `EvidenceSufficiencyNode` / `GroundingVerifyNode` references from chat-memory subsystem; `sprk_performancesummary` field has zero chat reads/writes; Insights regression suite passes.

#### Specialized playbooks + Path 3 extension (WP6)

38. **FR-38**: `PlaybookExecutionEngine.ExecuteChatSummarizeAsync` (Path 3) invokes `JpsRefResolver` before LLM call to resolve Skill + Knowledge `$ref` composition; streaming behavior preserved unchanged (`StreamStructuredCompletionAsync`, `IncrementalJsonParser`, `FieldDelta` SSE events). — **Acceptance**: unit test verifies `JpsRefResolver.ResolveAsync` called; streaming integration test continues to emit `FieldDelta` per top-level field.
39. **FR-39**: `summarize-nda` playbook authored — single-node, action with JPS `$ref` to SKL-003 NDA Review + KNW-006 NDA Standards + KNW-005 Defined Terms; 7-section output schema (parties + direction, definition, exclusions, term + survival, permittedUse, remedies, redFlags); destination=workspace + widgetType=structured-output-stream. — **Acceptance**: playbook indexed; vector match for "summarize this NDA" + NDA upload returns it as top-1.
40. **FR-40**: `summarize-patent` playbook authored — requires NEW Patent Review skill + NEW Patent Standards knowledge source added to seed catalog; same single-node + JPS `$ref` pattern. — **Acceptance**: skill + knowledge appear in `scripts/seed-data/skills.json` + `knowledge.json`; deploy script seeds them; playbook indexed.
41. **FR-41**: `extract-invoice` playbook authored — uses SKL-002 Invoice Processing; no knowledge source; pure extraction output schema. — **Acceptance**: playbook indexed; test invoice upload routes to it.
42. **FR-42**: PB-009 / PB-012 / PB-015 / PB-017 — Dataverse-level audit (NOT just repo grep) executed first; query `sprk_aichatcontextmapping`, Power Automate flow audit, `sprk_analysisrun` history. Per Q17: review and remediate, don't delete unless truly duplicative. — **Acceptance**: written audit findings preserved in project notes; remediation plan documented per playbook.
43. **FR-43**: PB-009 verification — inspect actual node graph in Dataverse Builder UI; rewrite description for embedding-friendly text (one sentence purpose + trigger words + output format + document type hints + no project-task refs); extend via JPS `$ref` to SKL-003 + KNW-006; populate `sprk_jpsmatchingmetadata`; verify `sprk_playbookcode = "summarize-nda"`. — **Acceptance**: PB-009 inspection report; post-rewrite vector match for "summarize this NDA" returns PB-009 as top-1.
44. **FR-44**: Path 3 binds default Persona via `IScopeResolverService.ResolvePersonaForChatAsync`. — **Acceptance**: chat-Summarize prompt includes persona content; trace shows persona lookup hit.
45. **FR-45**: Path 3 evidence-sufficiency precheck — returns structured Decline when RAG yields 0 chunks; avoids garbage-summary on empty input. — **Acceptance**: empty-corpus test returns Decline shape with reason.

### Non-Functional Requirements

- **NFR-01**: BFF publish size — measure delta vs current baseline ~45.65 MB (post-Phase 5 Outcome A per CLAUDE.md §10). Single-task delta ≥+5 MB requires explicit justification; cumulative ≥55 MB triggers architecture review; ≥60 MB is HARD STOP. WP4 deletion (CapabilityRouter + supporting infra) expected to reduce size; report per-task in notes.
- **NFR-02**: Production-bound playbook preservation — the 6 bindings in §1.5 must remain functional throughout migration; output schemas unchanged; integration tests for each consumer surface pass before and after.
- **NFR-03**: NFR-07 pre-fill flow contracts preserved — `useAiPrefill` hook, 45s timeout, `$choices` output constraint, endpoint signatures unchanged. Binding constraint.
- **NFR-04**: NFR-08 11 production node executors preserved.
- **NFR-05**: ADR-015 audit memory tier — all logged events tier-1 safe (no user message content); Cosmos `audit` container append-only immutable.
- **NFR-06 [v2 — reconciled with architecture §5]**: Insights Engine non-interference — `spaarke-insights-index`, `MultiIndexComposer`, `InsightsOrchestrator`, `EvidenceSufficiencyNode`, `GroundingVerifyNode`, and `sprk_matter.sprk_performancesummary` are Insights-domain assets and MUST NOT be reused or modified for chat memory. Pattern-level reuse only (versioned envelopes with citations, Redis hot-tier TTL, write-through to compliance store) per architecture §5.3. Regression suite for Insights flows must pass.
- **NFR-07**: `text-embedding-3-large` retained; no new external API surface introduced.
- **NFR-08**: Per-turn token budget per WP5.7 — static prefix ~6K, dynamic suffix ~5K, total ~11K typical, ~13K peak; soft per-section budgets (not hard global).
- **NFR-09**: Prompt-cache hit rate target >70% on multi-turn conversations after warmup; measured during stabilization.
- **NFR-10**: Routing budget — p95 ≤1.5s for 1-3 file scenarios; ≤2s worst case (5 files disagreeing).
- **NFR-11**: Per-file classification cost target — ~$0.0001/file via gpt-4o-mini; report cost telemetry.
- **NFR-12**: Test update obligation per CLAUDE.md §10 — PRs modifying `Sprk.Bff.Api/Services/` must add/update tests in `tests/unit/Sprk.Bff.Api.Tests/`. Asymmetric-registration anti-pattern (CLAUDE.md §10 F.1) banned; conditional services use ADR-032 Null-Object Kill-Switch.

#### Architectural principles (binding per architecture §2)

- **NFR-A1 [architecture §2 P1]**: Six-tier memory separation — cross-tier writes via explicit promotion APIs only (`promote_to_matter_memory`); no implicit side-effect writes; per architecture §3.2 binding rules. T1 composes from T2-T5 each turn; nothing else writes to T1.
- **NFR-A2 [architecture §2 P2]**: JIT retrieval over prompt stuffing — identifiers + structured context cards in prompt; full content via tool calls only. No file-text stuffing in static prefix at >2-3 small docs scale.
- **NFR-A3 [architecture §2 P3]**: Citation-bearing trust model — precomputed summaries framed as "NOT authoritative"; `recall_session_file` `requireCitations: true` default; persona text per architecture §8.3.
- **NFR-A4 [architecture §2 P4]**: Layered context cards (~150-250 tok per file) — structured cards via `LayeredContextCardBuilder` (architecture §4.4), NOT 1-line summaries.
- **NFR-A5 [architecture §2 P5]**: Wire-not-build for existing R6 infrastructure — the 12+ R6 Pillar 7 components in architecture §11.1 are leveraged, NOT rebuilt or replaced. Build scope is wrapper tools + prompt-assembly refactor + upload enrichment.
- **NFR-A6 [architecture §2 P6]**: Privacy by default — T6 audit append-only and never read by agent for memory composition; T2→T3 promotion requires explicit user approval; T4 mutations go through user-settings UI (out of chat scope).
- **NFR-A7 [architecture §2 P7]**: ADR-015 audit hygiene — telemetry / `context.*` events carry tier-1 fields only (tier, operation, tenantId, sessionId, durationMs, deterministic IDs); NEVER user message text, file contents, recall results, or memory facts.

## Technical Constraints

### Applicable ADRs

- **ADR-001**: Minimal API pattern — `/api/ai/playbooks/by-code/{code}` follows minimal API style with endpoint filters.
- **ADR-008**: Endpoint filters for authorization — `by-code` endpoint enforces tenant scoping via auth filter.
- **ADR-013**: AI Architecture / BFF AI facade boundary — `IInvokePlaybookAi` + Public Contracts boundary preserved; chat-routing migration MUST NOT inject `IOpenAiClient` / `IPlaybookService` directly into CRUD code. New code uses `Services/Ai/PublicContracts/` facade.
- **ADR-014**: AI caching — `/by-code/` resolution cached 5-min TTL per tenant; `PlaybookLookupService` already aligned.
- **ADR-015**: AI Data Governance — Tier 6 audit memory tier-1 safe; chat-Summarize tool calls + recall events logged via `AuditLogService` to Cosmos `audit` (append-only, immutable, no TTL).
- **ADR-018**: Feature flags / Typed options — Fix `Workspace:SummarizePlaybookId` violation by adding to `WorkspaceOptions.cs`; new option names follow `*PlaybookCode` convention.
- **ADR-019**: ProblemDetails error model — `by-code` endpoint 404 follows ProblemDetails shape.
- **ADR-029**: BFF publish hygiene — measure publish-size impact per-task; target net reduction from CapabilityRouter retirement.
- **ADR-030 (v2 — 2026-06-21 amendment)**: PaneEventBus — WP5.6 workspace-write tools dispatch via `workspace` channel; WP5 memory-domain events (`promotion_pending`, `promotion_resolved`, `fact_promoted`, `pin_added`, `pin_removed`) dispatch via the new `memory` channel added by this project's amendment to ADR-030. Channel union expanded from 4 → 5. Payloads carry deterministic IDs + 80-char summaries only (ADR-015 tier-1 safe).
- **ADR-032**: BFF Null-Object Kill-Switch — any new feature-gated service (e.g. memory tier toggles) registers Real + Null implementations symmetrically; no asymmetric registration anti-pattern.
- **ADR-033**: Streaming chat tool side-channel — Path 3 streaming preserved (`FieldDelta` SSE events unchanged); `JpsRefResolver` invocation happens BEFORE LLM call, not in streaming hot path.

### MUST Rules (from ADRs + design)

- ✅ MUST use `Services/Ai/PublicContracts/` facade for CRUD code that consumes AI capability (ADR-013).
- ✅ MUST preserve NFR-07 pre-fill flow signatures + 45s timeout + `useAiPrefill` hook + `$choices` constraint.
- ✅ MUST preserve NFR-08 11 production node executors.
- ✅ MUST measure BFF publish-size delta per CLAUDE.md §10; report in PR description (~45.65 MB current baseline; ceiling 60 MB).
- ✅ MUST verify no new HIGH-severity CVE via `dotnet list package --vulnerable --include-transitive`.
- ✅ MUST update tests in `tests/unit/Sprk.Bff.Api.Tests/` for modified `Sprk.Bff.Api/Services/` per CLAUDE.md §10 bullet 6.
- ✅ MUST follow `bff-extensions.md` decision criteria for any NEW endpoint / service / DI registration; state placement decision in PR.
- ✅ MUST coordinate WP5 changes with Insights Engine (additive only; no breaking changes; no `sprk_performancesummary` modification).
- ✅ MUST follow ADR-032 Null-Object Kill-Switch for feature-gated services (symmetric registration).
- ✅ MUST use Cosmos `audit` container for chat audit events (tier-1 safe; ADR-015).
- ❌ MUST NOT modify the 6 production-bound playbooks per design §1.5 (output schema, name, GUID).
- ❌ MUST NOT delete production-bound playbooks without consumer migration.
- ❌ MUST NOT touch `sprk_matter.sprk_performancesummary` field (Insights-only).
- ❌ MUST NOT create new `sprk_playbookcode`, `sprk_playbookid`, `sprk_analysisplaybookid`, `sprk_actioncode`, `sprk_actionid`, or `sprk_analysisactionid` — all already exist (see Executive Summary canonical field naming table).
- ❌ MUST NOT create a `sprk_actioncode_clean` field (invented; rejected by owner). REUSE existing `sprk_actioncode` for action-code reform.
- ❌ MUST NOT inject `IOpenAiClient` / `IPlaybookService` directly into CRUD code (ADR-013 facade boundary).
- ❌ MUST NOT add Azure-external API surface (voyage-law-2 deferred; Document Intelligence deferred).
- ❌ MUST NOT introduce asymmetric DI registration (CLAUDE.md §10 F.1 anti-pattern).
- ❌ MUST NOT use `npm ci` for Vite solutions (CLAUDE.md §11 — use `npm install --legacy-peer-deps --no-audit --no-fund`).

#### Components NOT to build (binding per architecture §4.5)

- ❌ MUST NOT create `sprk_matterfacts` Dataverse entity — `MatterMemoryService` already covers via Cosmos `memory` (doc id `{tenantId}_{matterId}`) per architecture §4.5 + §7.2.
- ❌ MUST NOT add a new chat-memory AI Search index — wrap existing chat-domain indexes (`spaarke-session-files`, `spaarke-files-index`, `spaarke-rag-references`) via new tool handlers (architecture §4.5 + §7.3).
- ❌ MUST NOT build a MultiIndexComposer-derived memory composer — use `MemoryCompositionService` (R6 task 067; different semantics — see architecture §4.5 + §5.2.2).
- ❌ MUST NOT share envelope type with Insights — pattern-level reuse only (architecture §4.5 + §5.3).
- ❌ MUST NOT add a new audit container — `AuditLogService` + Cosmos `audit` already provide immutable, append-only audit (architecture §4.5 + §7.1).
- ❌ MUST NOT fix `sprk_aichatmessage` placeholder methods as a real-impl repository — retire to write-only audit role per FR-25 (architecture §4.5 + §11.4).
- ❌ MUST NOT use `spaarke-insights-index`, `MultiIndexComposer`, `InsightsOrchestrator`, `EvidenceSufficiencyNode`, `GroundingVerifyNode`, or `sprk_matter.sprk_performancesummary` for chat memory (architecture §5.2.1-§5.2.6).

### Existing Patterns to Follow

- See `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookLookupService.cs:70-115` for `by-code` alternate-key lookup (already implemented).
- See `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs:442-499` for canonical Path 1 JPS `$ref` resolution pattern to replicate in Path 3.
- See `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/MatterMemoryService.cs` for Tier 3 store pattern (Cosmos `memory` container, doc id `{tenantId}_{matterId}`, ETag concurrency).
- See [`architecture/stateful-chat-architecture.md`](architecture/stateful-chat-architecture.md) §5 for the binding Insights-reuse boundary (pattern-level only — DO NOT reuse `MultiIndexComposer`, `InsightsOrchestrator`, `EvidenceSufficiencyNode`, `GroundingVerifyNode`, `spaarke-insights-index`, or `sprk_performancesummary` for chat memory).
- See `.claude/patterns/api/` for endpoint pattern details.
- See `.claude/constraints/bff-extensions.md` for the binding BFF additions checklist + decision criteria.

## Success Criteria

1. [ ] "Summarize this NDA" with NDA upload routes to Summarize-NDA (not generic Summarize) — verify via routing telemetry + workspace tab content.
2. [ ] `/summarize` slash command produces the same destination as NL "summarize" command (closes flagship R6 deferred bug) — verify via UAT scenario T-001.
3. [ ] No hardcoded playbook GUID in `Services/Ai/` (5 GUIDs removed) — verify via `grep`.
4. [ ] No literal playbook name lookups in production code paths (4 sites migrated) — verify via `grep`.
5. [ ] `/by-name/` endpoint deprecation warning emits per call; call count drops to zero after migration window — verify via telemetry.
6. [ ] CapabilityRouter + 10 supporting files deleted; CI green; no regression in dedup semantics — verify via FR-30 dedup test suite.
7. [ ] `text-embedding-3-large` returns Summarize-NDA top-1 for "summarize this NDA" prompt on 100-doc Spaarke corpus — verify via offline eval benchmark.
8. [ ] Routing p95 ≤1.5s for 1-3 file scenarios — verify via load test.
9. [ ] Per-turn prompt structure: ~6K static prefix + ~5K dynamic suffix; cache hit >70% multi-turn — verify via prompt-inspector + Azure OpenAI telemetry.
10. [ ] Agent responds correctly to "Do you have the document?" without re-asking for upload — verify via UAT scenario T-002.
11. [ ] Agent calls `recall_session_file` with `requireCitations: true` for any legal-precision question — verify via tool-call trace.
12. [ ] Agent never quotes precomputed summary as authoritative; quotes always cite recall output — verify via UAT scenario T-003.
13. [ ] 6 production-bound playbooks remain functional throughout migration (all consumer integration tests green pre + post) — verify via test matrix.
14. [ ] NFR-07 pre-fill flow contracts preserved (signatures, 45s timeout, `useAiPrefill`, `$choices`) — verify via integration test.
15. [ ] Insights Engine non-interference — `sprk_performancesummary`, `MultiIndexComposer`, `InsightsOrchestrator`, `EvidenceSufficiencyNode`, `GroundingVerifyNode`, and `spaarke-insights-index` are unused by chat memory and behaviorally unchanged in Insights subsystem — verify via grep + Insights regression suite (per architecture §5).
16. [ ] BFF publish size reports per-task in PR description; expected net reduction from WP4 deletion; below 60 MB ceiling — verify via `dotnet publish` measurement.
17. [ ] 3 new specialized playbooks (`summarize-nda`, `summarize-patent`, `extract-invoice`) authored, indexed, and matchable via vector + LLM dispatch — verify via routing tests.
18. [ ] Path 3 (`ExecuteChatSummarizeAsync`) resolves JPS `$ref` and includes Skill `PromptFragment` + Knowledge `Content` in prompt; streaming UX preserved (FieldDelta SSE events unchanged) — verify via streaming integration test.
19. [ ] `ChatDataverseRepository` placeholder methods retired; interface renamed to `IChatAuditRepository` (write-only); Cosmos `audit` confirmed sole reader — verify via grep + integration test.
20. [ ] PB-009 / PB-012 / PB-015 / PB-017 Dataverse-level audit completed BEFORE any deprecation; written findings + per-playbook remediation plan preserved in project notes — verify via audit document.

## Dependencies

### Prerequisites

- R6 closeout tasks 089 + 090 complete (governance + wrap-up); UAT regression of all 9 pillars complete (per Q9 reaffirmation + Q16).
- 3 R6 UAT hotfix commits (`be95dfc7d`, `35462f807`, `a74ee9fdb`) merged to master (avoid silent `toolCount=0` stall regression on auto-deploy). The hotfixes are defensive guards on the CapabilityRouter layer this project retires wholesale — keep them until WP4 cutover.
- Dataverse schema confirmation triggered + approved for the 5 additive fields on `sprk_analysisplaybook` (FR-08).
- **R6 Pillar 7 memory services in place** (architecture §11.1; do NOT rebuild): `MatterMemoryService`, `PinnedContextRepository`, `PinnedContextRecallService`, `MemoryCompositionService`, `SummarizationCompressionService`, `PromptBudgetTracker`, `ManagePinnedContextHandler`. All DI-registered + tested.
- **5 Cosmos containers provisioned via Bicep** (architecture §7.1): `sessions` (T2 warm; 90d TTL), `memory` (T3/T4 with doc-type discriminator), `audit` (T6; immutable, no TTL), `prompts`, `feedback`.
- **Pillar 6b workspace-write handlers shipped + registered** as `sprk_analysistool` rows (architecture §4.3): `GetWorkspaceTabStateHandler`, `UpdateWorkspaceTabHandler`, `SendWorkspaceArtifactHandler`, `CloseWorkspaceTabHandler`.
- **FR-45 wiring at `PlaybookChatContextProvider.cs:627`** VERIFIED per architecture §11.1 — invariant; do not regress.
- **`getAgentVisibleState` per-widget impls (Pillar 9) + server-side `TryDeriveVisibleState` at `SprkChatAgentFactory.cs:2173`** shipped — leveraged for workspace digest in static prefix (architecture §6.2 + §6.5).
- `WorkspaceStateService` with Q4 hybrid persistence (R6 task 051) — extension point per `SaveTabsAsync` pattern for new memory fields (architecture §11.2).
- Pinned Memory CRUD UI (4 endpoints + 4 React components + widget registry) — shipped per architecture §11.1.

Note: WP3 destination metadata wiring is IN THIS PROJECT (not pre-required from R6) — the design's §4 split allocated quick wins to R6 closeout, but per owner clarification 2026-06-21 those WP3 sub-items remain open and are owned here.

### External Dependencies

- Azure OpenAI access (existing) — `text-embedding-3-large` + `gpt-4o-mini` for classification + decider.
- Azure AI Search (existing) — `playbook-embeddings` (routing), `spaarke-session-files` (T5 primary for chat memory recall), `spaarke-files-index`, `spaarke-rag-references`, `spaarke-records-index`, `spaarke-knowledge-index-v2`, `discovery-index`. **NOT used for chat memory**: `spaarke-insights-index` (Insights domain — see architecture §5.2.1).
- Cosmos containers (existing) — `sessions` (T2 warm), `memory` (T3/T4), `audit` (T6), `auditevents`.
- Dataverse production access for schema additions (5 fields) + playbook code backfill.
- Power Apps form customization for "Send to Index" UX (admin role).

## Owner Clarifications

*Answers captured during design-to-spec interview 2026-06-21:*

| Topic | Question | Answer | Impact |
|---|---|---|---|
| WP2 multi-file path | A / B / C primary path? | **(C) Hybrid — precomputed manifests + filter** | Routing primary path reads WP5.5 manifest's `documentType` against `sprk_jpsmatchingmetadata.documentTypes`; falls back to (A) on disagreement. WP5.5 upload pipeline is a prerequisite for full benefit; (A) is the disagreement-resolution path. |
| Action code reform (§1.7.4) | In scope or defer? | **In scope — tackle alongside playbook codes** | Adds action-code migration to project scope. New actions kebab-case without `@v1`; existing `@v1`-suffixed codes remain valid until cutover. FR-06 added. |
| `sprk_aichatmessage` future | Fix / Retire / Defer? | **Retire — repurpose as pure audit-write target** | `ChatDataverseRepository` 5 placeholder methods removed; interface renamed `IChatAuditRepository` (write-only); Cosmos `audit` is sole reader. FR-25 added. |
| §1.7 migration sequencing | Chat first / Pre-fill first / Parallel? | **Chat-summarize first** | `SessionSummarizeOrchestrator` migrates first (no config override; lowest blast radius), proves resolver infrastructure, then pre-fill flows, then name-resolve consumers. Pattern C cleanup precedes both. FR-05 documents sequencing. |
| Schema additions | All 6 approved or partial? | **`sprk_playbookcode` ALREADY EXISTS — do not duplicate. `sprk_playbookid` also exists (separate from `sprk_analysisplaybookid` GUID PK). Indexing tracking fields ARE approved.** | 5 fields added in FR-08 (NOT 6). `sprk_playbookcode` migration is a consumer-adoption project, not a schema-additions project. Spec corrected throughout. |
| WP5 scope | All sub-WPs or stage? | **All in scope BUT CRITICAL coordination with Insights Engine — additive only, do not break existing.** | NFR-06 promoted to binding. Initial v1 interpretation assumed reuse of `spaarke-insights-index` + `MultiIndexComposer`; superseded 2026-06-21 by [`architecture/stateful-chat-architecture.md`](architecture/stateful-chat-architecture.md) §5 which applies critical scrutiny and rejects type/service-level reuse — pattern-level reuse only (envelope w/ citations, Redis TTL, write-through to compliance). FR-36/FR-37/NFR-06 updated v2 accordingly. |
| WP5 architecture diagram (audit dependency) | Done or still outstanding? | **Done — v3.2 reframing absorbed it** | Proceed to spec.md / task generation based on §WP5.1a Leverage Map + §WP5.1b architecture diagram already in design doc. No additional audit gate required. |

## Assumptions

*Proceeding with these assumptions (owner did not explicitly specify or design says "defer"):*

- **Tenant-unique vs globally unique playbook code** — assuming **tenant-unique** per design §1.7.6 recommendation (allows org-level customization without colliding with SYS- defaults). Will revisit during task design if proven wrong by Insights/Dataverse audit.
- **Promote-to-matter-memory UX surface** — assuming **Context pane** approval prompt with accept/reject buttons per Q12 resolution. Detailed UX deferred to in-project iteration.
- **`sprk_userpreferences` field shape** — assuming **existing shape sufficient** (read via cached snapshot); writingStyle / summaryLength / citationStyle fields added only if proven necessary during stabilization. Q13 deferred.
- **Per-turn cache invalidation behavior** — assuming **layered context cards do not invalidate cache on every turn** if "recently-discussed" flag is consolidated into a single header section. Will benchmark during stabilization (Q14 deferred).
- **`sprk_matterfacts` separate Dataverse entity** — assuming **not needed**; Cosmos `memory` container is the durable T3 store per audit finding. Schema decision binding.
- **Patent Skill + Patent Standards Knowledge authoring** — assuming **new entries authored in this project** per WP6 scope; estimated 1-2 days authoring effort each.
- **R6 closeout dependency** — assuming **R6 tasks 089+090 + UAT regression complete BEFORE this project starts coding** (sequencing per Q9 + Q16). If R6 is incomplete at kick-off, this project blocks on it.
- **Action-code reform mechanics** — REUSE existing `sprk_actioncode` field (no new column). New actions populate it with kebab-case codes without `@v1`; existing `@v1`-suffixed values remain valid until cutover window closes. If task design surfaces a structural need for a separate field (not anticipated), the new field name MUST be `sprk_actionid` — never `sprk_actioncode_clean`.
- **`sprk_jpsmatchingmetadata` field type** — assuming **MultilineText (Plain) with JSON content** + client-side JSON-schema validation. Confirmed during task design.
- **WP5.5 upload pipeline classification model** — assuming **gpt-4o-mini structured-output classifier**; benchmark for accuracy/cost during Phase 1 of project.

## Unresolved Questions

*Items still requiring resolution during implementation:*

- [ ] **Tenant scope confirmation for `sprk_playbookcode` uniqueness** — Blocks: §1.7 migration cutover. Resolve in early task by querying Dataverse `sprk_analysisplaybook` rows.
- ~~**`MatterMemoryService.ToSystemPromptFragmentAsync` wiring (FR-27)**~~ — RESOLVED 2026-06-21 per architecture §11.1: wired at `PlaybookChatContextProvider.cs:627`. Now a regression-prevention invariant, not an open question.
- [ ] **PB-009 / PB-012 / PB-015 / PB-017 Dataverse-level audit findings** — Blocks: FR-42 acceptance + WP6 deprecation decisions. Required artifact before any production playbook delete.
- [ ] **Patent Skill + Patent Standards Knowledge content** — Blocks: FR-40 acceptance. Requires legal-domain content authorship; assign before Phase 2.
- [ ] **Insights Engine + R6 CosmosDB cross-check (Audit 3) leverageable components inventory** — Per owner: design absorbed v3.2 reframing; proceed. But if any Phase 1 task surfaces an Insights component requiring deeper integration than design assumed, escalate per CLAUDE.md §6.
- [ ] **`commandIntent` field rename target name** — Blocks: FR-07 wire-format change. Resolve in early task with frontend team; suggested names: `intentHint`, `dispatchBias`, `routingHint`.
- [ ] **Promote-to-matter-memory UX surface design (Q12)** — Defer detailed design but Phase 3 task must produce wireframe before backend stub merges.
- [ ] **Action-code stabilization window length** — how long do existing `@v1`-suffixed `sprk_actioncode` values remain valid before cutover? Resolve in §1.7.4 design task. Default proposal: same window as `/by-name/` deprecation for playbooks.

---

*AI-optimized specification. Original design: `design.md` v3.1 (1734 lines), 2026-06-19, with v3.2 reframing per Insights+CosmosDB audit.*

# ADRs - Concise Versions (AI Context)

> **Purpose**: Concise versions of ADRs optimized for AI context loading
> **Target**: 100-150 lines per ADR
> **Full versions**: See `docs/adr/` for complete ADRs

## About This Directory

This directory contains AI-optimized versions of Architecture Decision Records. Each file focuses on:
- **Decision**: What was decided
- **Constraints**: MUST/MUST NOT rules
- **Key patterns**: Code examples
- **Rationale**: Brief why (1-2 sentences)

**Omitted from concise versions**:
- Verbose context/background
- Historical discussion
- Detailed alternatives analysis
- Long examples

## ADR Index

| ADR | Title | Key Constraint | Status |
|-----|-------|----------------|--------|
| ADR-001 | Minimal API + BackgroundService | No Azure Functions | Accepted |
| ADR-002 | Thin Dataverse plugins | No HTTP/Graph calls in plugins | Accepted |
| ADR-006 | UI Surface Architecture | Code Pages are default for new UI; PCF only for form binding | Accepted (Revised 2026-03-19) |
| ADR-007 | SpeFileStore facade | No Graph SDK types leak above facade | Accepted |
| ADR-008 | Endpoint filters for auth | No global auth middleware | Accepted |
| ADR-010 | DI minimalism | ≤15 non-framework DI registrations | Accepted |
| ADR-012 | Shared component library | `@spaarke/ui-components` (UX + abstracted-I/O `IDataService`) + `@spaarke/visuals` (presentational data-viz sibling, 2026-07-12 amendment); no ad-hoc per-project viz libs | Accepted (Amended 2026-07-12) |
| ADR-013 | AI Architecture | Extend BFF (4 extraction criteria); PublicContracts facade discipline; **canonical verb = capability invocation `invoke(bindingId, args)` (2026-07-05 amendment); `IInvokePlaybookAi` grandfathered legacy shim — no new consumers** | Accepted (amended 2026-07-05) |
| ADR-021 | Fluent UI v9 Design System | All UI uses Fluent v9; React 19 for Code Pages; dark mode required | Accepted |
| ADR-022 | PCF Platform Libraries | PCF uses React 16/17 platform-provided; Code Pages use React 19 bundled | Accepted |
| ADR-023 | ~~Choice Dialog Pattern~~ | _Superseded — demoted to pattern_ | Superseded (2026-03-19) |
| ADR-026 | Code Page Build Standard | Vite + `vite-plugin-singlefile` + React 19 for all Code Pages | Accepted (Revised 2026-03-19) |
| ADR-027 | Subscription Isolation & Dataverse Solution Mgmt | Managed solutions for prod; env-separated subscriptions | Accepted |
| ADR-028 | Spaarke Auth Architecture (v2) | Function-based contract; managed identity for outbound; named API key schemes; HMAC webhooks; audit middleware | Accepted (2026-05-19) |
| ADR-029 | BFF Publish Hygiene | Framework-dependent linux-x64, sourcemap exclusion, transitive CVE override pattern, size baseline ratchet | Accepted (2026-05-26) |
| ADR-030 | PaneEventBus pattern | Typed multi-subscriber cross-pane bus; four channels (workspace/context/conversation/safety); no `any` payloads; one provider at shell root | Accepted (2026-05-26) |
| ADR-031 | Stage Lifecycle Pattern | Four stages (`welcome`/`loading`/`active-chat`/`review`); `determineStage()` canonical; transitions driven by PaneEventBus (ADR-030); client-side recompute wins over persisted state | Accepted (2026-05-26) |
| ADR-032 | BFF Null-Object Kill-Switch Pattern | Conditional service consumed by unconditional endpoint → Null-Object in else-branch (P1/P2/P3); `FeatureDisabledException` → 503 ProblemDetails | Accepted (2026-06-01) — renumbered from ADR-030 during R4 merge per number-collision resolution |
| ADR-033 | Streaming chat-tool side channel | Chat-tool handlers emit document-stream SSE via `ChatInvocationContext.DocumentStreamWriter` delegate (not interface extension); `IToolHandler` contract unchanged; two-channel side-channel pattern with Wave 7b Metadata envelope (one-shot data) vs. context-side writer (streaming) | Accepted (2026-06-08) — R6 NFR-03 revision per "ADRs Are Defaults" operating principle |
| ADR-034 | User-Record Membership Resolution Pattern | Discovery-based `MembershipResolverService` + identity normalization (6 paths, fail-isolated) + Phase 2 junction table `sprk_userentityassociation` + Service Bus topic `sprk-membership-changes` (D3) + fire-and-forget publishing (Q2) + 1-hop transitive (Q3); `LookupUserMembership` node `ActionType=52`; uses existing `SystemAdmin` policy (Q6); naming-disambiguated from `AssociationResolver` PCF | Accepted (2026-06-21) — R3 Part 1 |
| ADR-036 | Background-Job Infrastructure (Spaarke.Scheduling) | New shared lib `Spaarke.Scheduling`: `IScheduledJob` contract + `ScheduledJobHost` + Cronos cron parsing + `sprk_backgroundjob*` Dataverse entities + `/api/admin/jobs/*` admin surface (SystemAdmin policy per Q6); two reference consumers ship in R3 (`MembershipReconciliationJob` + migrated `PlaybookSchedulerJob` with single-row fan-out per D2 + fresh per-child correlationId per Q1); 26 other BackgroundServices remain for opportunistic migration | Accepted (2026-06-21) — R3 Part 2 |
| ADR-037 | Multi-Node Output Composition | New `NodeType.DeliverComposite` (ordinal 100_000_004) + `ActionType.DeliverComposite = 42` + per-section SSE streaming (`section_started` / `section_data` / `section_completed` keyed by section NAME, not schema position) + FE widget rework (`sections: Record<string, SectionState>`); reduces 5 brittle coordination points (schema-on-action + schema-aware widget + ordinal indexing + implicit linkage) to 2 (section name + section state); legacy `FieldDelta` path preserved for unmigrated playbooks via runtime event-type detection; chat sibling playbooks stay single-action (no composition benefit). **Amended 2026-07-05**: "DeliverComposite by default for future workspace playbooks" RESCINDED (engine frozen per OQ-2); binding content re-scoped to the section-name-keyed streaming + widget contract for ANY composite executor | Accepted (2026-06-25; amended 2026-07-05) — chat-routing-redesign-r1 Phase 5R Wave 5-C (FR-52..FR-55); amendment per spaarke-ai-code-audit-r1 ADR review A-2 |
| ADR-039 | Grounded Execution & Closed Catalogs | ONE dispatch protocol (Event/Click/Text; bounded agent turn = only probabilistic decider); TWO closed catalogs (Actions+Bindings, Tools); every output grounded (capability \| cited tool-chain \| confirmation \| refusal); control-flow-is-code; **adding a second intent-detection mechanism anywhere is a violation**; no routing config outside the Binding table; side effects gate by `side_effect_class`, never name lists; golden-utterance eval suite gates catalog changes | Proposed (2026-07-05) — Accepted at migration P1 |
| ADR-040 | Session Ledger | Append-only addressable typed per-session ledger (Doc/Output/ToolChain/Turn/WidgetEvent/Gate) over the existing 3-tier store; **storage precedes rendering** (universal write before any surface); reads by `ledger_resolution` reference; disposition is the only rendering contract; ADR-015 tier mapping (ledger=Tier 3; tool chains=metadata only); no second session store | Proposed (2026-07-05) — Accepted at migration P0 |
| ADR-041 | Judgment, Confirmation & Completion Policy | Three-part judgment layer above ADR-039 dispatch: **D-F0** resourcefulness (reads free / writes gated; degradation ladder verify→act→degrade→refuse-with-affordance stays BELOW the side-effect line, never weakens a gate); **D-F1** confirmation as deterministic (risk-tier × origin × completeness) with strict overlay precedence + E-1..E-6 ruled rows — risk is catalog-declared DATA never runtime LLM judgment (ADR-039), confirmation state is a Gate-ledger property so a second ask is structurally impossible (ADR-040), gate pre-suspend validation; **D-F2** completion — OutcomeCard composed after the ledger write (store-before-render), job-aware status (only fully-completed aggregate = Succeeded), UI-action ack truthfulness; enforcement in code not directives | Proposed (2026-07-09) — Accepted at G-R2-A |
| ADR-042 | Memory Architecture & Governance | TWO active memory scopes - **Record** (generic `(entityType,entityId)`) + **User** (canonical `systemuserid`); Conversation stays the ADR-040 ledger facade (MemoryItem write to it REJECTED); structured objects never embeddings; NEW `memory-items` container partitioned by SUBJECT (`/subjectId` - **never `/tenantId`**, dedicated-per-customer rationale; legacy shared `memory` container never retired/re-keyed); per-fact docs + deterministic id -> upsert-by-(Type,Key) SUPERSESSION; governance envelope (provenance incl. carried-inert trustLevel; retentionClass->per-item Cosmos TTL, no reaper; sensitivity/deletionPolicy INERT); **`memory.write` = AI-initiated + SILENT + provenance-tagged through the REAL gate from catalog DATA (Write + low-tier/reversible) - NO confirmation floor (removed 2026-07-08)**; user review/delete + GDPR Tier-3 erasure + ids-only Tier-2 audit = the controls; Dataverse-field-mirror facts rejected; DEFERRED hard-governance boundary (untrusted-origin ban / trustLevel enforcement / litigation-hold / poisoning evals / row-level read ACL -> security project #616); Insights Engine = reserved future producer | Proposed (2026-07-10) - Accepted at G-R2-B gate |
| ADR-043 | AI Capability Execution Spine | THREE execution surfaces (completion engine / agent-loop tool spine / deterministic-transform) converging at one disposition→ledger→OutcomeCard layer; **converge the two redundant completion engines** (ActionRunner + AiCompletionNodeExecutor) onto ONE input-resolution model (**ContextBinder → ContextEnvelope**, no runtimeInput-straddle); **single-source disposition** in one DispositionRoutability registry (admit = "router can route it" — kills the 3-list drift); deterministic/interactive capabilities (compose edit, retraction) via a **deterministic ActionKind + supersession-write**, not a third spine; keep the agent-loop tool spine separate (unify = R8+); **vertical-slice `tests/integration/seam/**` KEEP test = definition-of-done** + named engine owner; reserves (does not build) the multi-step Action Engine seam (hybrid auth / closed-catalog / ledger plan / framework-agnostic) | Proposed (2026-07-09) — Accepted at Phase-E gate |
| ADR-044 | Dataverse GUID Canonicalization at Boundaries | Xrm emits registry-format GUIDs (brace-wrapped / UPPERCASE) while downstream is intolerant: OData `@odata.bind` key predicate rejects braces with HTTP 400 "Error in query syntax", and AI Search `Edm.String eq` is case-sensitive. **MUST canonicalize every Dataverse GUID to bare-lowercase at every boundary** — before any `@odata.bind`/`/entityset(guid)` URL, before an AI Search key/filter, and at Xrm ingestion — via the shared **`cleanGuid`** (`@spaarke/ui-components`; no-op on bare ids) client-side and a single-convergence-point normalize in the BFF. MUST NOT hand-roll per-file `.replace(/[{}]/g,'')` or interpolate a raw GUID into a key predicate. Codifies FAILURE-MODES AP-3 (case → AI Search) + AP-6 (braces → `@odata.bind`): two prod failures, one root cause | Accepted (2026-07-10) |
| ADR-038 | Testing Strategy — Integration-heavy pyramid | 6 KEEP path categories as MUST rules (`tests/integration/{auth,regression,data-mutation,tenant,contract}/**` + `tests/unit/domain/**`); deletion-safety: removal under KEEP paths requires same-PR replacement; coverage is observation never gate (binding ≥6 months from 2026-06-26); ban `Mock<HttpMessageHandler>` + `Mock<IServiceClient>` + DI-registration tests + ctor null-check tests; mock at module boundaries not HTTP-handler level; `TimeProvider` over `Stopwatch` for time-dependent tests; enforced at `task-execute` Step 9.5 (unconditional code-review on test PRs per spec FR-B07). **STANDALONE — does NOT supersede ADR-022 (PCF Platform Libraries — unrelated frontend scope).** | Accepted (2026-06-26) — ci-cd-unit-test-remediation-r1 Phase 1 Stream B |

---

## Usage by AI Agents

Load concise ADRs proactively when creating new components:
- Creating API → Load ADR-001, ADR-008, ADR-010, **ADR-028** (auth)
- Creating PCF → Load ADR-006, ADR-012, ADR-022 (React 16 compatibility), **ADR-028** (auth)
- Creating Code Page (dialog, wizard, full page) → Load ADR-006, ADR-026, ADR-021 (React 19), **ADR-028** (auth)
- Creating Plugin → Load ADR-002
- **Working with auth → Load ADR-028 (canonical) + ADR-003 (server seams + OBO) + ADR-008 (filters) + ADR-009 (Redis caching) + `.claude/constraints/auth.md` (operational MUST/MUST NOT)**
- Working with SPE → Load ADR-007, ADR-019
- Working with UI/UX → Load ADR-021, ADR-022
- Working with shared components → Load ADR-012 (service architecture, portability tiers)
- Working with cross-pane communication / widget mount sources → Load **ADR-030** (PaneEventBus)
- Working with SpaarkeAi shell stages, widget lifecycle, or session restore → Load **ADR-031** (stage lifecycle) + ADR-030 (PaneEventBus) + ADR-028 (session restore contract)
- Deploying to production → Load ADR-027 (subscription isolation, Dataverse solution management)
- Working with Dataverse solutions → Load ADR-027 (managed vs unmanaged, import order)

Full ADRs in `docs/adr/` should be loaded only when:
- Need historical context
- Debugging architectural decisions
- Proposing changes to architecture

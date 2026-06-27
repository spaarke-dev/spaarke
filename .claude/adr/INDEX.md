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
| ADR-012 | Shared component library | `@spaarke/ui-components` as single source of truth; abstracted services via `IDataService` | Accepted (Revised 2026-03-19) |
| ADR-013 | AI Architecture | AI Tool Framework; extend BFF | Accepted |
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
| ADR-037 | Multi-Node Output Composition | New `NodeType.DeliverComposite` (ordinal 100_000_004) + `ActionType.DeliverComposite = 42` + per-section SSE streaming (`section_started` / `section_data` / `section_completed` keyed by section NAME, not schema position) + FE widget rework (`sections: Record<string, SectionState>`); reduces 5 brittle coordination points (schema-on-action + schema-aware widget + ordinal indexing + implicit linkage) to 2 (section name + section state); legacy `FieldDelta` path preserved for unmigrated playbooks via runtime event-type detection; chat sibling playbooks stay single-action (no composition benefit) | Accepted (2026-06-25) — chat-routing-redesign-r1 Phase 5R Wave 5-C (FR-52..FR-55) |
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

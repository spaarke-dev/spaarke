# Project Plan: Legal Operations Workspace (Home Corporate) R1

> **Last Updated**: 2026-02-18
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Build a Legal Operations Workspace — a single-page React dashboard embedded as a Power Apps Custom Page in the MDA. Provides legal operations managers with portfolio health metrics, activity feed, smart to-do list with transparent scoring, portfolio browser, quick-action cards, and AI-powered summaries.

**Scope**:
- Custom Page shell with FluentProvider, responsive layout, theme toggle
- 7 build blocks (Get Started, Portfolio Health, Updates Feed, Smart To Do, My Portfolio, Create Matter, Notifications)
- 7 action cards (1 custom dialog + 6 Analysis Builder entry points)
- BFF endpoints for portfolio aggregation, scoring, and AI integration
- Priority/Effort scoring engine (server-side)

**Estimated Effort**: 25-35 days (with agent team parallelism reducing wall-clock time)

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: BFF endpoints use Minimal API pattern with ProblemDetails
- **ADR-006**: Build as PCF control, no legacy JS webresources
- **ADR-007**: File uploads route through SpeFileStore facade
- **ADR-008**: Use endpoint filters for authorization (not global middleware)
- **ADR-009**: Cache aggregation queries in Redis (IDistributedCache)
- **ADR-010**: BFF DI registrations ≤15 non-framework lines
- **ADR-012**: Import from `@spaarke/ui-components`, Fluent v9 only
- **ADR-013**: AI calls through BFF → AI Playbook, never direct from client
- **ADR-021**: All UI uses Fluent v9, makeStyles, semantic tokens, dark mode mandatory
- **ADR-022**: PCF platform-library declarations, unmanaged solutions only

**ADR Exception**:
- **React 18 in Custom Page**: Custom Pages run in their own iframe — can use React 18 APIs (`createRoot`). React 16 constraint applies only to PCF controls sharing MDA runtime.

**From Spec**:
- Hybrid data access: Xrm.WebApi for simple queries, BFF for aggregations/AI
- No new Dataverse entities (use existing only)
- All 7 action cards functional (Create Matter custom + 6 Analysis Builder)
- Agent teams mandatory for parallel execution

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Power Apps Custom Page (React 18) | Workspace needs full React app, not form-bound PCF | ADR exception for React 18 APIs |
| Hybrid data access (Xrm.WebApi + BFF) | Simple queries client-side, aggregations server-side | Two data access layers |
| 6 action cards reuse Analysis Builder | Reuse existing Playbook infrastructure | Major scope reduction vs custom dialogs |
| Fluent UI v9 only, dark mode mandatory | Platform standard, accessibility | Zero hardcoded colors |
| Agent teams for parallel execution | Independent blocks enable concurrent work | Reduced wall-clock time |

### Discovered Resources

**Applicable ADRs** (10):
- `.claude/adr/ADR-001.md` - Minimal API + BackgroundService
- `.claude/adr/ADR-006.md` - PCF over webresources
- `.claude/adr/ADR-007.md` - SpeFileStore facade
- `.claude/adr/ADR-008.md` - Endpoint filters for auth
- `.claude/adr/ADR-009.md` - Redis-first caching
- `.claude/adr/ADR-010.md` - DI minimalism
- `.claude/adr/ADR-012.md` - Shared component library
- `.claude/adr/ADR-013.md` - AI Architecture
- `.claude/adr/ADR-021.md` - Fluent UI v9 Design System
- `.claude/adr/ADR-022.md` - PCF Platform Libraries

**Applicable Skills**:
- `.claude/skills/task-execute/` - Task execution with checkpointing
- `.claude/skills/adr-aware/` - ADR constraint loading
- `.claude/skills/dataverse-deploy/` - Solution deployment
- `.claude/skills/code-review/` - Quality gate reviews
- `.claude/skills/push-to-github/` - Git operations
- `.claude/skills/context-handoff/` - State persistence

**Knowledge Articles & Constraints**:
- `.claude/constraints/api.md` - BFF API rules
- `.claude/constraints/pcf.md` - PCF control rules
- `.claude/constraints/ai.md` - AI processing rules
- `.claude/constraints/data.md` - Data access rules
- `.claude/patterns/api/endpoint-definition.md` - Endpoint patterns
- `.claude/patterns/api/error-handling.md` - ProblemDetails patterns
- `.claude/patterns/api/service-registration.md` - DI module patterns
- `.claude/patterns/pcf/control-initialization.md` - PCF init patterns
- `.claude/patterns/pcf/theme-management.md` - Theme + dark mode patterns
- `.claude/patterns/caching/distributed-cache.md` - Redis caching patterns
- `.claude/patterns/dataverse/entity-operations.md` - Dataverse CRUD patterns
- `docs/guides/SPAARKE-AI-ARCHITECTURE.md` - AI Tool Framework guide

**Canonical Code References**:
- `src/client/pcf/AnalysisWorkspace/` - Best workspace PCF reference (multi-panel, theme, MSAL)
- `src/client/pcf/DueDatesWidget/` - Cleanest React 16 template
- `src/server/api/Sprk.Bff.Api/Api/Finance/FinanceEndpoints.cs` - Aggregation endpoint pattern
- `src/server/api/Sprk.Bff.Api/Api/Scorecard/ScorecardCalculatorEndpoints.cs` - Scoring endpoint pattern
- `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` - AI streaming endpoints
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/FinanceModule.cs` - DI module pattern
- `src/server/api/Sprk.Bff.Api/Api/Filters/DocumentAuthorizationFilter.cs` - Auth filter pattern
- `src/server/api/Sprk.Bff.Api/Services/Ai/EmbeddingCache.cs` - Redis cache pattern

**Available Scripts**:
- `scripts/Deploy-PCFWebResources.ps1` - PCF deployment
- `scripts/Deploy-CustomPage.ps1` - Custom Page deployment
- `scripts/Test-SdapBffApi.ps1` - BFF API testing

**Visual References** (15 screenshots):
- `screenshots/workspace-main-page.jpg` - Full layout reference
- See spec.md Visual References section for complete mapping

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Foundation & Independent Blocks (Tasks 001-009)
├── Custom Page shell, FluentProvider, theme, layout
├── Shared interfaces and TypeScript types
├── Block 2: Portfolio Health Summary (simple, independent)
├── Block 5: My Portfolio Widget (independent)
├── Block 7: Notification Panel (independent)
└── BFF: Portfolio aggregation + health endpoints

Phase 2: Core Feature Blocks (Tasks 010-019)
├── Block 3: Updates Feed (core engagement feature)
├── Block 3D: Flag-as-ToDo (shared state with Block 4)
├── Block 4: Smart To Do (requires Block 3D interface)
└── BFF: Scoring engine (priority + effort)

Phase 3: Action Cards & Dialogs (Tasks 020-029)
├── Block 1: Get Started + Quick Summary
├── Block 6: Create New Matter Dialog (multi-step wizard)
├── Action Card integration (6 cards → Analysis Builder)
└── BFF: AI integration endpoints (summary, pre-fill, briefing)

Phase 4: Integration & Polish (Tasks 030-039)
├── Cross-block state synchronization
├── End-to-end testing
├── Dark mode audit (zero hardcoded colors)
├── Accessibility audit (WCAG 2.1 AA)
├── Bundle size optimization (< 5MB target)
└── Performance optimization (< 3s load target)

Phase 5: Deployment & Wrap-up (Tasks 040-049 + 090)
├── Solution packaging for Dataverse
├── Custom Page registration in MDA
├── Deployment verification
├── Lessons learned
└── Project wrap-up
```

### Critical Path

**Blocking Dependencies:**
- Phase 1 foundation (shell, types, FluentProvider) BLOCKS all UI blocks
- Block 3D (flag-as-to-do interface) BLOCKS Block 4 Smart To Do
- Block 1 Get Started card UI BLOCKS Action Card integration wiring
- BFF scoring engine BLOCKS full to-do sort order in Block 4

**Parallel Opportunities** (see spec.md Parallel Execution Strategy):
- Phase 1: Blocks 2, 5, 7 are fully independent → 3 parallel teammates
- Phase 1: BFF endpoints can parallel with UI blocks → separate teammate
- Phase 3: Block 6 wizard and Block 1 cards can parallel
- Cross-phase: Scoring engine (BFF) can develop alongside Phase 2 UI

**High-Risk Items:**
- Custom Page auth to BFF - Mitigation: Investigate MSAL token flow early in Phase 1
- Bundle size > 5MB - Mitigation: Code-split by block, lazy load dialogs
- Scoring edge cases - Mitigation: Comprehensive unit tests with known inputs

---

## 4. Phase Breakdown

### Phase 1: Foundation & Independent Blocks

**Objectives:**
1. Establish Custom Page shell with FluentProvider, responsive layout, and theme system
2. Define all shared TypeScript interfaces and BFF DTOs (interface-first for parallelism)
3. Implement 3 independent UI blocks (Portfolio Health, My Portfolio, Notifications)
4. Implement BFF portfolio aggregation and health metric endpoints

**Deliverables:**
- [ ] Custom Page shell component with FluentProvider (light/dark/high-contrast)
- [ ] Theme toggle with localStorage persistence + system preference detection
- [ ] Responsive layout: 50/50 grid → single column at 1024px breakpoint
- [ ] Page header with notification bell and theme toggle
- [ ] Shared TypeScript interfaces (all block data models, BFF DTOs)
- [ ] Block 2: Portfolio Health Summary (4 metric cards with color thresholds)
- [ ] Block 5: My Portfolio Widget (Matters/Projects/Documents tabs)
- [ ] Block 7: Notification Panel (Drawer slide-out)
- [ ] BFF: Portfolio aggregation endpoint (matters, spend, budget)
- [ ] BFF: Health metrics endpoint (at-risk, overdue, active counts)
- [ ] Navigation contract (postMessage to parent MDA)
- [ ] Xrm.WebApi data service for client-side queries

**Parallel Groups**: pg-foundation → then pg-phase1-ui + pg-phase1-bff in parallel

**Inputs**: spec.md, design.md, screenshots/, ADR-021, ADR-022, ADR-001, ADR-008

**Outputs**: Renderable Custom Page with 3 functional blocks, 2 BFF endpoints

### Phase 2: Core Feature Blocks

**Objectives:**
1. Implement Updates Feed (core engagement feature)
2. Implement Smart To Do with scoring display
3. Implement server-side Priority and Effort scoring engine
4. Wire flag-as-to-do shared state between Feed and To Do

**Deliverables:**
- [ ] Block 3: Updates Feed with filter bar (8 categories)
- [ ] Block 3: Feed item cards (unread dot, type icon, priority badge, timestamp, flag toggle)
- [ ] Block 3D: Flag-as-ToDo toggle (creates/removes sprk_todoflag on Event)
- [ ] Block 3E: AI Summary dialog for feed items
- [ ] Block 4: Smart To Do list with prioritized items
- [ ] Block 4: Manual add bar (plus icon + input + Add button)
- [ ] Block 4: Checkbox toggle, dismiss, collapsed dismissed section
- [ ] Block 4D: AI Summary dialog with Priority×Effort scoring grid
- [ ] BFF: Priority scoring engine (deterministic, 0-100, factor tables)
- [ ] BFF: Effort scoring engine (base + multipliers, capped at 100)
- [ ] BFF: Scoring calculation endpoint
- [ ] System-generated to-do items (overdue, budget >85%, deadlines)

**Parallel Groups**: pg-phase2-feed → pg-phase2-todo (after 3D interface); pg-scoring in parallel with both

**Inputs**: Phase 1 outputs, spec.md scoring tables, ADR-009, ADR-013

**Outputs**: Feed and To Do blocks functional with live scoring, BFF scoring endpoints

### Phase 3: Action Cards & Dialogs

**Objectives:**
1. Implement Get Started action cards and Quick Summary briefing
2. Implement Create New Matter multi-step wizard (Block 6)
3. Wire 6 action cards to existing Analysis Builder
4. Implement AI integration endpoints for summaries and pre-fill

**Deliverables:**
- [ ] Block 1: Get Started horizontal card row (7 cards)
- [ ] Block 1B: Quick Summary briefing card (280-320px fixed width)
- [ ] Block 1B: Expanded briefing dialog
- [ ] Block 6: Create Matter Step 1 — File upload (drag-drop, PDF/DOCX/XLSX)
- [ ] Block 6: Create Matter Step 2 — Create Record form with AI pre-fill
- [ ] Block 6: Create Matter Step 3 — Next Steps selection (3 checkboxes)
- [ ] Block 6: Follow-on steps (Assign Counsel, Draft Summary, Send Email)
- [ ] Action card integration: 6 cards → Analysis Builder with context/intent
- [ ] BFF: AI Summary endpoint (feed/to-do items)
- [ ] BFF: Quick Summary briefing endpoint
- [ ] BFF: Create Matter AI pre-fill endpoint (document analysis)

**Parallel Groups**: pg-phase3-actions + pg-phase3-dialog in parallel; pg-ai-integration alongside

**Inputs**: Phase 1-2 outputs, AI Playbook architecture, SpeFileStore facade

**Outputs**: All 7 blocks functional, all action cards wired, AI features working

### Phase 4: Integration & Polish

**Objectives:**
1. Verify cross-block state synchronization (flag sync, score updates)
2. Audit and fix dark mode compliance (zero hardcoded colors)
3. Audit and fix accessibility (WCAG 2.1 AA)
4. Optimize bundle size (< 5MB) and page load (< 3s)
5. End-to-end testing of all user flows

**Deliverables:**
- [ ] Cross-block state sync verification (feed ↔ to-do flag state)
- [ ] Dark mode audit — verify all components use semantic tokens
- [ ] High-contrast mode verification
- [ ] Accessibility audit (keyboard nav, ARIA labels, color contrast)
- [ ] Bundle size analysis and optimization (code splitting, tree shaking)
- [ ] Performance profiling and optimization
- [ ] Unit tests for scoring engine (priority + effort with known inputs)
- [ ] Integration tests for BFF endpoints
- [ ] E2E test scenarios for all critical user flows

**Inputs**: All Phase 1-3 outputs, ADR-021 theme requirements

**Outputs**: Production-ready workspace with all quality gates passed

### Phase 5: Deployment & Wrap-up

**Objectives:**
1. Package as Dataverse unmanaged solution
2. Register Custom Page in MDA
3. Deploy and verify in dev environment
4. Document lessons learned

**Deliverables:**
- [ ] Dataverse solution packaging (unmanaged)
- [ ] Custom Page registration in target MDA
- [ ] BFF endpoint deployment
- [ ] Post-deployment verification (all 16 success criteria)
- [ ] Lessons learned document
- [ ] README status update to Complete
- [ ] Project wrap-up (archive, cleanup)

**Inputs**: Phase 4 outputs, deployment scripts

**Outputs**: Working workspace in dev MDA environment

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Fluent UI v9 (`@fluentui/react-components`) | GA | Low | Use latest stable |
| Fluent React Icons (`@fluentui/react-icons`) | GA | Low | Use latest stable |
| Power Apps Custom Page hosting | Available | Medium | Verify iframe auth flow early |
| Azure OpenAI (via AI Playbook) | Production | Low | Deterministic fallbacks for all AI features |
| SharePoint Embedded (SpeFileStore) | Production | Low | File upload in Create Matter |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| BFF API (Sprk.Bff.Api) | `src/server/api/Sprk.Bff.Api/` | Production — extend |
| AI Playbook platform | `Services/Ai/` | Production — reuse |
| SpeFileStore facade | `Services/Spe/SpeFileStore.cs` | Production — reuse |
| Shared UI components | `@spaarke/ui-components` | Production — import |
| Dataverse entities (sprk_event, sprk_matter, etc.) | Dataverse | Production — all fields exist |
| Analysis Builder (AiToolAgent PCF) | `src/client/pcf/AiToolAgent/` | Production — launch with context |

---

## 6. Testing Strategy

**Unit Tests** (80% coverage target):
- Priority scoring engine — all factor combinations
- Effort scoring engine — all event types and multipliers
- Score capping and normalization
- Portfolio aggregation calculations
- Health metric thresholds (green/orange/red)

**Integration Tests**:
- BFF portfolio aggregation endpoint with test data
- BFF scoring calculation endpoint with known inputs
- BFF AI summary endpoint with mock AI response
- Create Matter file upload → SpeFileStore → AI pre-fill pipeline

**E2E Tests** (manual in dev environment):
- Full page load in Custom Page within MDA
- Theme toggle (light ↔ dark) with visual verification
- All 8 feed filter categories
- Flag-to-do round-trip (feed → to-do → unflag)
- Create New Matter wizard complete flow
- All 6 action cards launch Analysis Builder
- My Portfolio "View All" navigation
- Keyboard navigation through all interactive elements

**UI Tests** (per task POML definitions):
- Each block has visual verification tests
- Dark mode compliance checks per ADR-021
- Responsive layout at breakpoints (900, 1024, 1280, 1920)

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1:**
- [ ] Custom Page renders in MDA iframe at 1024px, 1280px, 1920px
- [ ] Theme toggle persists to localStorage and respects system preference
- [ ] Portfolio Health shows correct metrics with color-coded thresholds
- [ ] My Portfolio loads matters/projects/documents with correct counts
- [ ] Notification Panel opens from bell icon with type filters

**Phase 2:**
- [ ] Feed displays events with correct filters, sort, and counts
- [ ] Flag toggle syncs feed ↔ to-do (Dataverse persistence < 1s)
- [ ] To-do items sorted by priority score DESC, due date ASC
- [ ] Scoring produces correct results for all test scenarios

**Phase 3:**
- [ ] All 7 action cards functional (1 custom dialog + 6 Analysis Builder)
- [ ] Create Matter wizard completes end-to-end (upload → pre-fill → submit)
- [ ] Quick Summary shows correct aggregated metrics

**Phase 4:**
- [ ] Zero hardcoded colors (dark mode audit pass)
- [ ] WCAG 2.1 AA compliance (keyboard nav, ARIA, contrast)
- [ ] Bundle size < 5MB
- [ ] Page load < 3 seconds

### Business Acceptance

- [ ] Legal operations managers can see portfolio health at a glance
- [ ] Activity feed reduces context-switching between entity views
- [ ] Smart to-do provides transparent, trustworthy priority ordering
- [ ] Create Matter wizard streamlines matter onboarding
- [ ] AI summaries enhance decision-making without replacing deterministic data

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Custom Page iframe auth to BFF unclear | Medium | High | Investigate MSAL token flow in Phase 1 foundation; fallback to delegated auth |
| R2 | Bundle size exceeds 5MB | Low | Medium | Code-split by block, lazy load dialogs, tree-shake unused Fluent components |
| R3 | Scoring formula edge cases | Medium | Medium | Comprehensive unit tests with known inputs; factor table validation |
| R4 | Xrm.WebApi performance at 500+ matters | Low | Medium | Add pagination, virtual scrolling if needed |
| R5 | AI Playbook latency affects UX | Medium | Low | Loading states, deterministic fallbacks, cancel support |
| R6 | Agent team file conflicts during parallel work | Low | Medium | Strict file ownership per parallel group; conflict-check before each group |

---

## 9. Next Steps

1. **Generate task files** via task-create (Step 3 of project-pipeline)
2. **Create TASK-INDEX.md** with parallel groups and dependencies
3. **Begin Phase 1** foundation tasks with agent team parallelism

---

**Status**: Ready for Tasks
**Next Action**: Generate POML task files from this plan

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*

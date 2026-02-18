# Legal Operations Workspace (Home Corporate) - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-02-17
> **Source**: `projects/home-corporate-workspace-r1/design.md` (v2.0)
> **Branch**: `work/home-corporate-workspace-r1`

## Executive Summary

Build a **Legal Operations Workspace** — a single-page dashboard embedded as a **Power Apps Custom Page** in the Model-Driven App. The workspace provides legal operations managers with portfolio health metrics, a prioritized activity feed, a smart to-do list with transparent priority/effort scoring, a portfolio browser, quick-action cards with dialogs, and AI-powered summaries via the Spaarke AI Playbook platform. All 7 build blocks and all action card dialogs are in scope for R1.

## Scope

### In Scope

**Core Page Infrastructure:**
- Custom Page shell with FluentProvider (light/dark theme), responsive layout, page header
- Theme toggle with localStorage persistence + system preference detection
- Navigation contract (`postMessage` to parent MDA for entity view/form navigation)
- Responsive layout: 50/50 grid stacking to single column below 1024px

**Block 1: Get Started & Quick Summary**
- Horizontal scrollable row of 7 action cards (all functional — each opens a dialog or navigates)
- Quick Summary briefing card (280-320px fixed width) with deterministic aggregation
- Expanded briefing dialog with longer narrative
- AI-enhanced briefing when AI Playbook available

**Block 2: Portfolio Health Summary**
- 4-card metric strip: Portfolio Spend (with utilization bar), Matters at Risk, Overdue Events, Active Matters
- Color-coded thresholds (green/orange/red)
- Responsive 4-column → 2-column at 900px

**Block 3: Updates Feed**
- Chronological activity stream showing Events (emails, documents, tasks, invoices, alerts, etc.)
- Filter bar with pill-style filter cards (All, High Priority, Overdue, Alerts, Emails, Documents, Invoices, Tasks)
- Feed item cards with unread dot, type icon, priority badge, timestamp, flag toggle, AI Summary button
- Sort: Priority first (critical > high > normal), then timestamp descending

**Block 4: Smart To Do**
- Prioritized work queue (system-generated, user-flagged, manually created items)
- Manual add bar (plus icon + input + Add button)
- To-do item cards with drag handle, checkbox, source indicator, priority/effort badges, context, due label
- Dismissed section (collapsible)
- AI Summary dialog with Priority x Effort scoring grid

**Block 5: My Portfolio Widget**
- Tabbed sidebar: Matters / Projects / Documents with count badges
- Matter items with status derivation (Critical/Warning/On Track), grade pills (A-F)
- Project items with status badges
- Document items with type and matter reference
- Footer "View All" navigation to MDA entity views

**Block 6: Create New Matter Dialog**
- Multi-step wizard (3 base steps + 3 dynamic follow-on steps)
- Step 1: File upload (drag-and-drop, PDF/DOCX/XLSX, max 10MB)
- Step 2: Create Record form with AI pre-fill from uploaded documents
- Step 3: Next Steps selection (Assign Counsel, Draft Summary, Send Email)
- Follow-on step implementations for each selected action

**Block 7: Notification Panel**
- Slide-out Drawer from notification bell in page header
- Activity list with avatar, type badge, timestamp
- Filter toggle buttons: Documents, Invoices, Status, Analysis

**Action Card Sub-Projects (all in scope for R1):**
- Create New Matter → Block 6 dialog (fully specified custom wizard)
- Create New Project → Launches existing AI Playbook Analysis Builder with "New Project" context
- Assign to Counsel → Launches existing AI Playbook Analysis Builder with "Assign Counsel" context
- Analyze New Document → Launches existing AI Playbook Analysis Builder with "Document Analysis" context
- Search Document Files → Launches existing AI Playbook Analysis Builder with "Document Search" context
- Send Email Message → Launches existing AI Playbook Analysis Builder with "Email Compose" context
- Schedule New Meeting → Launches existing AI Playbook Analysis Builder with "Meeting Schedule" context

> **Architecture Note**: Only "Create New Matter" is a fully custom dialog (Block 6). The remaining 6 action cards are **entry points into the existing AI Playbook Analysis Builder** (`AiToolAgent` PCF). Each card launches the Analysis Builder with pre-configured context/intent, reusing the existing conversational AI infrastructure rather than building new standalone dialogs.

**Scoring System:**
- Priority scoring (deterministic rule-based, 0-100 scale, with AI adjustment capability)
- Effort scoring (base effort by event type + complexity multipliers, capped at 100)
- Transparent reason strings for both scores
- Scoring refresh: priority real-time + daily batch, effort on creation

**AI Integration (via AI Playbook platform):**
- Feed item AI Summary dialog
- To-do item AI Summary with scoring grid
- Portfolio Quick Summary briefing
- Create Matter document analysis and form pre-fill
- Draft Matter Summary generation
- Priority/Effort AI adjustments (future enhancement)

### Out of Scope

- New Dataverse entities (all features use existing entities)
- Page-level tab navigation (single dashboard page only)
- Calendar widget on this page (calendar stays in MDA calendar view)
- VisualHost framework components (workspace patterns are interaction-heavy, not data visualizations)
- Mobile-native app (responsive web only)
- Real-time SignalR push for feed updates (polling or manual refresh for R1)
- Drag-and-drop reorder in To Do list (drag handle is visual placeholder for future)
- AI-recommended to-do items (future — `sprk_todosource = 'AI'`)
- Record-level "Add to To Do" command bar button (future)

### Affected Areas

- `src/client/pcf/` — New PCF control: `LegalWorkspace` (Custom Page component)
- `src/client/shared/` — Shared UI components (`@spaarke/ui-components`) — reusable patterns
- `src/server/api/Sprk.Bff.Api/` — New BFF endpoints for portfolio aggregation, AI integration
- `src/solutions/` — Dataverse solution packaging for Custom Page

## Requirements

### Functional Requirements

1. **FR-01**: Page loads within Custom Page iframe in MDA and renders responsive dashboard layout — Acceptance: Page renders correctly at 1024px, 1280px, and 1920px widths
2. **FR-02**: Theme toggle switches between light and dark mode, persists to localStorage, and respects system preference on first load — Acceptance: Both modes render correctly with no hardcoded colors
3. **FR-03**: Portfolio Health Summary displays 4 metric cards with live data from user's matters — Acceptance: Spend utilization bar shows correct color thresholds (<65% green, 65-85% orange, >85% red)
4. **FR-04**: Updates Feed displays Events sorted by priority then timestamp with correct type icons, priority badges, and filter counts — Acceptance: All 8 filter categories work, counts update dynamically
5. **FR-05**: Feed item flag toggle creates/removes to-do flag on the underlying Event (`sprk_todoflag`) — Acceptance: Flag state persists to Dataverse, To Do badge count updates, item appears/disappears from To Do tab
6. **FR-06**: AI Summary dialog loads for any feed item, shows loading state, displays analysis card with suggested actions — Acceptance: Dialog renders with sparkle icon, loading spinner, analysis text, and action buttons
7. **FR-07**: Smart To Do list displays prioritized items with source indicators, priority/effort badges, and due labels — Acceptance: Items sorted by priority score DESC then due date ASC
8. **FR-08**: Manual to-do creation via add bar creates a new Event with `sprk_todoflag = true`, `sprk_todosource = 'User'` — Acceptance: New item appears in To Do list immediately
9. **FR-09**: To-do checkbox toggles `sprk_todostatus` between Open and Completed with visual feedback (strikethrough, opacity) — Acceptance: State persists to Dataverse
10. **FR-10**: To-do dismiss moves item to collapsible dismissed section, sets `sprk_todostatus = 'Dismissed'` — Acceptance: Dismissed section shows count, items recoverable
11. **FR-11**: My Portfolio Widget shows tabbed view (Matters/Projects/Documents) with correct data and count badges — Acceptance: Each tab loads correct entity data, max 5 items with "View All" footer
12. **FR-12**: Matter items display computed status (Critical/Warning/On Track) and grade pills (A-F with correct colors) — Acceptance: Status derivation logic matches spec thresholds
13. **FR-13**: "View All" footer buttons navigate to MDA entity views via navigation contract — Acceptance: postMessage sent to parent with correct view parameters
14. **FR-14**: Get Started action cards render horizontally with correct icons, labels, and click targets — Acceptance: All 7 cards render, click opens corresponding dialog or navigates
15. **FR-15**: Quick Summary card displays computed portfolio metrics (active count, spend/budget, at-risk, overdue, top priority) — Acceptance: Metrics match Dataverse data
16. **FR-16**: Create New Matter dialog implements 3-step wizard with file upload, AI pre-fill form, and next steps selection — Acceptance: Full wizard flow works end-to-end including file upload to SPE
17. **FR-17**: Create New Matter follow-on steps (Assign Counsel, Draft Summary, Send Email) execute their respective workflows — Acceptance: Each follow-on creates appropriate records/actions
18. **FR-18**: Notification Panel opens as slide-out Drawer with filtered activity list — Acceptance: Drawer opens from bell icon, shows recent/unread items with type filters
19. **FR-19**: Priority scoring calculates correct score (0-100) based on deterministic factors (overdue days, budget utilization, grades, deadlines, matter value) — Acceptance: Score matches formula for all test cases
20. **FR-20**: Effort scoring calculates correct score based on event type base + complexity multipliers, capped at 100 — Acceptance: Score matches formula for all test cases
21. **FR-21**: All 6 non-Create-Matter action cards launch the existing AI Playbook Analysis Builder with appropriate pre-configured context/intent — Acceptance: Each card opens the Analysis Builder with correct intent, user can complete the workflow through the existing Playbook UI
22. **FR-22**: System-generated to-do items are created automatically for: overdue events, budget >85%, deadline within 14 days, pending invoices, assigned tasks — Acceptance: Idempotent (no duplicates), correct title patterns

### Non-Functional Requirements

- **NFR-01**: Page initial load < 3 seconds on standard corporate network
- **NFR-02**: PCF bundle size < 5MB (per ADR-021)
- **NFR-03**: All UI passes WCAG 2.1 AA accessibility (keyboard nav, ARIA labels, color contrast)
- **NFR-04**: Dark mode compliance — zero hardcoded hex/rgb values, Fluent tokens only
- **NFR-05**: Responsive layout works at 900px, 1024px, 1280px, 1920px breakpoints
- **NFR-06**: AI operations have deterministic fallback — every AI-enhanced feature works without AI
- **NFR-07**: Feed/portfolio queries return within 2 seconds for up to 500 matters per user
- **NFR-08**: To-do flag toggle persists to Dataverse within 1 second

## Technical Constraints

### Applicable ADRs

- **ADR-001** (Minimal API): BFF endpoints use Minimal API pattern, ProblemDetails for errors
- **ADR-006** (PCF over webresources): Build as PCF control, no legacy JS webresources
- **ADR-007** (SpeFileStore): File uploads in Create Matter dialog route through SpeFileStore facade
- **ADR-008** (Endpoint filters): BFF endpoints use endpoint filters for authorization
- **ADR-009** (Redis caching): Cache expensive aggregation queries in Redis
- **ADR-010** (DI minimalism): BFF service registrations ≤15 non-framework lines, concrete types
- **ADR-012** (Shared components): Import from `@spaarke/ui-components`, Fluent v9 only
- **ADR-013** (AI Architecture): AI calls go through BFF to AI Playbook service, never direct from client
- **ADR-021** (Fluent UI v9): All UI uses Fluent v9 components, makeStyles, semantic tokens
- **ADR-022** (PCF Platform Libraries): Declare platform-library in manifest, unmanaged solutions only

### ADR Exception: React 18 in Custom Page

> **Exception to ADR-021/ADR-022**: This workspace is hosted as a **Power Apps Custom Page**, not a standard PCF virtual control bound to a form field. Custom Pages run in their own iframe context and can use React 18+ APIs (`createRoot`, concurrent features). The React 16 API constraint applies only to PCF controls that share the MDA React runtime.

### MUST Rules (from ADRs + Constraints)

- MUST use Fluent UI v9 (`@fluentui/react-components`) exclusively — no v8, no third-party UI
- MUST use `makeStyles` (Griffel) for all custom styling with Fluent `tokens`
- MUST use semantic color tokens (never hardcoded hex/rgb/hsl)
- MUST support light mode, dark mode, and high-contrast
- MUST wrap all UI in `FluentProvider` with appropriate theme
- MUST use `@fluentui/react-icons` for all icons
- MUST route all SPE file operations through `SpeFileStore` facade (Create Matter file upload)
- MUST use Minimal API pattern for all new BFF endpoints
- MUST use endpoint filters for BFF authorization (per ADR-008)
- MUST use `IDistributedCache` (Redis) for cross-request caching of aggregation queries
- MUST keep BFF DI registrations ≤15 non-framework lines
- MUST return `ProblemDetails` for all BFF API errors
- MUST call AI services only through BFF → AI Playbook pipeline (never from client)
- MUST apply rate limiting to AI endpoints
- MUST deploy as unmanaged solution to Dataverse

### MUST NOT Rules

- MUST NOT hardcode any color values (hex, rgb, hsl, named colors)
- MUST NOT mix Fluent UI versions (v9 only)
- MUST NOT use alternative UI libraries (MUI, Chakra, Bootstrap, Tailwind, shadcn)
- MUST NOT call Azure AI services directly from the client/PCF
- MUST NOT expose API keys to client
- MUST NOT create new Dataverse entities (use existing only)
- MUST NOT inject `GraphServiceClient` outside `SpeFileStore`
- MUST NOT use `IMemoryCache` for non-metadata without justification
- MUST NOT create global middleware for authorization
- MUST NOT bundle React/Fluent in PCF artifacts (use platform-library declarations)

### Data Access Pattern: Hybrid

Per owner clarification, data access uses a **hybrid approach**:

| Query Type | Source | Rationale |
|-----------|--------|-----------|
| Matter list (My Portfolio) | `Xrm.WebApi` (client-side) | Simple entity query, no aggregation needed |
| Project list | `Xrm.WebApi` (client-side) | Simple entity query |
| Document list | `Xrm.WebApi` (client-side) | Simple entity query |
| Event feed (Updates) | `Xrm.WebApi` (client-side) | Event queries with filters, client-side sorting |
| To-do CRUD operations | `Xrm.WebApi` (client-side) | Direct entity updates (flag, status, create) |
| Portfolio Health aggregation | **BFF endpoint** | Sum/count across all user matters — complex aggregation |
| Quick Summary metrics | **BFF endpoint** | Multi-entity aggregation (matters + events + financials) |
| AI Summary (feed/to-do items) | **BFF endpoint** | AI Playbook invocation requires server-side |
| AI Pre-fill (Create Matter) | **BFF endpoint** | Document analysis via AI Playbook |
| Priority/Effort calculation | **BFF endpoint** | Server-side scoring with multi-entity context |

### Existing Patterns to Follow

- See `src/server/api/Sprk.Bff.Api/` for BFF endpoint patterns
- See `src/client/pcf/` for existing PCF control structure
- See `.claude/patterns/` for detailed implementation patterns
- See AI Playbook integration in `docs/guides/SPAARKE-AI-ARCHITECTURE.md`

## Visual References (Prototype Screenshots)

15 advanced mockup screenshots are available at `screenshots/` as UX reference. These are **not pixel-perfect** but represent the target layout, component patterns, and interaction design. Tasks should reference applicable screenshots during implementation.

| Screenshot | Block(s) | Shows |
|-----------|----------|-------|
| `workspace-main-page.jpg` | **All blocks** | Full page layout — Get Started cards, Quick Summary, Portfolio Health strip, Updates feed, My Portfolio sidebar. Primary reference for overall composition. |
| `updates-list-filtered-by-alert_1.jpg` | **Block 3** | Updates feed with "Alerts" filter active. Shows filter pill styling, alert feed items with priority/type badges, flag + AI Summary buttons. |
| `updates-list-ai-summary_2.jpg` | **Block 3E** | AI Summary dialog on a feed item (email type). Shows sparkle header, Analysis card, Suggested Actions list (Reply, Create task, Open matter), "Add to To Do" footer button. |
| `to-do-list_1.jpg` | **Block 4** | Smart To Do list. Shows add bar, drag handles, checkboxes, source indicators (system bot icon), priority badges (Critical/High/Medium), effort badges (High/Med/Low with colors), context text, due labels (Overdue/3d/7d/10d), dismiss + AI buttons. |
| `to-do-list-ai-summary_2.jpg` | **Block 4D** | AI Summary dialog for to-do item with Priority x Effort scoring grid. Shows Priority=105 Critical with factor breakdown, Effort=94 High with complexity multiplier checklist (1.3x, 1.2x, 1.1x, 1.2x, 1.3x), Analysis text, Suggested Actions. |
| `my-portfolio-matter-list_1.jpg` | **Block 5B** | My Portfolio — Matters tab. Shows matter items with name, status badge (Critical/On Track), type + organization, practice area + last activity, 3 grade pills (A-F with colors), overdue indicator. "View All Matters" footer link. |
| `my-portfolio-projects-list_2.jpg` | **Block 5C** | My Portfolio — Projects tab. Shows project items with name, status badge (Active/Planning), type + owner, practice area + last activity. "View All Projects" footer. |
| `my-portfolio-documents-list_3.jpg` | **Block 5D** | My Portfolio — Documents tab. Shows document items with icon, name, description, document type + matter name + timestamp. "View All Documents" footer. |
| `my-portfolio-ai-summary_1.jpg` | **Block 1B** | Quick Summary card close-up. Shows sparkle icon, "8 active matters · $1.3M of $2.0M spent", at-risk count (red), overdue count (red), top priority matter, "Full briefing" link. |
| `my-portfolio-ai-summary_2.jpg` | **Block 1B** | Portfolio Briefing expanded dialog. Shows narrative paragraphs: active matters summary, at-risk explanation (red), overdue events breakdown (red), top priority with deadline, budget watch with utilization percentages. Close button. |
| `create-new-matter-dialog-wizard.jpg` | **Block 6C** | Create New Matter — Step 1: Add files. Shows dialog shell (sidebar steps + content), drag-and-drop zone, "Supported: PDF, DOCX, XLSX (max 10MB each)", Cancel + Next buttons. |
| `create-new-matter-dialog-wizard_2.jpg` | **Block 6D** | Create New Matter — Step 2: Create record with AI pre-fill. Shows 2-column form with sparkle "AI" tags on pre-filled fields (Matter Type, Practice Area, Matter Name, Organization, Estimated Budget, Key Parties, Summary), "AI Pre-filled" badge top-right. |
| `create-new-matter-dialog-wizard_3.jpg` | **Block 6E** | Create New Matter — Step 3: Next Steps (none selected). Shows 3 checkbox cards (Assign Counsel, Draft Matter Summary, Send Email to Client), "Finish" button. |
| `create-new-matter-dialog-wizard_4.jpg` | **Block 6E** | Create New Matter — Step 3: Next Steps with "Draft Matter Summary" selected. Shows selected card with brand border/bg, dynamic "Draft matter summary" step added to sidebar below divider, "Next" button. |
| `create-new-matter-dialog-wizard_5.jpg` | **Block 6F** | Create New Matter — Follow-on: Draft Matter Summary. Shows AI-Generated Summary card with sparkle icon + narrative text, Recipients email input field, "Finish" button. |

**Usage in tasks**: Each implementation task should reference the applicable screenshot(s) as `screenshots/{filename}`. These mockups guide layout, spacing, component selection, and interaction patterns — not exact pixel dimensions.

## Build Sequence

Per design document, recommended build order with parallel opportunities:

| Phase | Block | Rationale | Parallel |
|-------|-------|-----------|----------|
| 1 | **Block 0** — Page shell, FluentProvider, theme, layout | Foundation for all blocks | — |
| 1 | **Block 2** — Portfolio Health Summary | Simple, high visibility, independent | Yes (with 5, 7) |
| 1 | **Block 5** — My Portfolio Widget | Independent, data display only | Yes (with 2, 7) |
| 2 | **Block 3** — Updates Feed | Core feature, drives engagement | — |
| 2 | **Block 4** — Smart To Do | Requires Block 3 for flag state sharing | After 3D |
| 3 | **Block 1** — Get Started + Quick Summary | Requires briefing logic, all action cards | — |
| 3 | **Block 6** — Create New Matter Dialog | Can parallel with Block 1 | Yes (with 1) |
| 3 | **Action Card Integration** — Wire 6 cards to Analysis Builder with context/intent | After Block 1 card UI exists | Yes (with 1, 6) |
| 4 | **Block 7** — Notification Panel | Lower priority, supplements Block 3 | — |
| 5 | **BFF Endpoints** — Aggregation + AI integration | Can start in Phase 1, iterate | Ongoing |
| 5 | **Scoring Engine** — Priority + Effort calculation | Server-side, supports Blocks 3-4 | Phase 2+ |

## Parallel Execution Strategy (Agent Teams)

**MANDATORY**: This project MUST use Claude Code **agent teams** for parallel task execution wherever possible. The workspace has many independent blocks and cross-layer work (PCF + BFF) that benefit significantly from parallel implementation.

### Agent Team Parallel Groups

Task creation (`task-create`) MUST tag tasks with `parallel-group` identifiers so the team lead can dispatch them to teammates concurrently. Tasks within the same group have **no file-level dependencies** on each other.

| Parallel Group | Tasks | File Ownership | Rationale |
|---------------|-------|----------------|-----------|
| **pg-foundation** | Block 0 page shell, shared types/interfaces, theme system | `src/client/pcf/LegalWorkspace/` (shell only) | Must complete before other groups |
| **pg-phase1-ui** | Block 2 (Portfolio Health), Block 5 (My Portfolio), Block 7 (Notification Panel) | Each block owns its own component directory | 3 independent UI blocks, zero shared state |
| **pg-phase1-bff** | Portfolio aggregation endpoint, health metrics endpoint | `src/server/api/` (separate endpoint files) | BFF work parallels UI work |
| **pg-phase2-feed** | Block 3 (Updates Feed), Block 3D (Flag-as-ToDo) | `src/client/pcf/LegalWorkspace/components/ActivityFeed/` | Feed components, sequential internally |
| **pg-phase2-todo** | Block 4 (Smart To Do), To Do state management | `src/client/pcf/LegalWorkspace/components/SmartToDo/` | Can start after Block 3D flag interface is defined |
| **pg-phase3-actions** | Block 1 (Get Started + Quick Summary), Action Card integration | `src/client/pcf/LegalWorkspace/components/GetStarted/` | Action cards + Analysis Builder wiring |
| **pg-phase3-dialog** | Block 6 (Create New Matter wizard — all steps) | `src/client/pcf/LegalWorkspace/components/CreateMatter/` | Independent dialog, large scope |
| **pg-scoring** | Priority scoring engine, Effort scoring engine, scoring BFF endpoints | `src/server/api/` (scoring services) | Server-side, independent of UI |
| **pg-ai-integration** | AI Summary dialog, Quick Summary briefing, AI pre-fill | Shared AI service layer | Can run alongside UI once interfaces defined |

### Execution Rules for Agent Teams

1. **Team lead** orchestrates phases — dispatches parallel groups to teammates
2. **Each teammate** owns a file/directory boundary — no two teammates edit the same file
3. **Interface-first**: Define shared TypeScript interfaces and BFF DTOs early (in pg-foundation) so parallel groups can code against contracts
4. **Cross-layer parallelism**: UI teammates and BFF teammates work simultaneously on the same feature (e.g., Portfolio Health UI + Portfolio Health BFF endpoint)
5. **Conflict check**: Run `/conflict-check` before starting each parallel group
6. **Quality gates**: Each teammate runs code-review + adr-check on their work before merging

### Recommended Team Configuration

```
Team Lead: Orchestrator (Opus, high effort)
├── Teammate 1: PCF UI components (Sonnet, medium effort)
├── Teammate 2: BFF API endpoints (Sonnet, medium effort)
├── Teammate 3: Scoring + AI integration (Sonnet, medium effort)
└── (Optional) Teammate 4: Tests + documentation (Sonnet, low effort)
```

### Task File Requirements for Parallelism

Each POML task file MUST include:
- `parallel-group` tag matching the table above
- `file-ownership` listing which files/directories this task exclusively modifies
- `depends-on` listing task IDs that must complete before this task can start
- `parallel-with` listing task IDs that can execute concurrently with this task

## Success Criteria

1. [ ] All 7 blocks render correctly in Custom Page within MDA — Verify: Visual inspection in dev environment
2. [ ] Light and dark mode both work with zero hardcoded colors — Verify: Toggle theme, inspect all blocks
3. [ ] All 7 Get Started action cards are functional — Create New Matter opens custom dialog, other 6 launch Analysis Builder with correct context — Verify: Click each card, verify correct behavior
4. [ ] Portfolio Health shows correct aggregated metrics from live Dataverse data — Verify: Compare with manual query
5. [ ] Updates Feed shows Events with correct filtering, sorting, and flag toggle — Verify: Create test events, verify display and filters
6. [ ] Smart To Do shows prioritized items with correct scores and badges — Verify: Create items with known scores, verify ordering
7. [ ] Flag-as-To-Do syncs between Feed and To Do views — Verify: Flag in feed, appears in To Do, unflag
8. [ ] AI Summary dialog loads and displays analysis from AI Playbook — Verify: Click AI Summary, verify loading + result
9. [ ] Create New Matter wizard completes full flow including file upload and AI pre-fill — Verify: Upload file, verify AI extraction, submit form, verify Dataverse record
10. [ ] My Portfolio shows correct matters/projects/documents with working "View All" navigation — Verify: Click "View All", verify MDA navigation
11. [ ] Keyboard navigation works through all interactive elements — Verify: Tab through entire page
12. [ ] ARIA labels present on all icon-only buttons — Verify: Screen reader audit
13. [ ] Bundle size < 5MB — Verify: Build output size check
14. [ ] Page load < 3 seconds — Verify: Performance measurement in dev environment
15. [ ] Priority scoring produces correct results for test scenarios — Verify: Unit tests with known inputs/outputs
16. [ ] Effort scoring produces correct results for test scenarios — Verify: Unit tests with known inputs/outputs

## Dependencies

### Prerequisites

- Dataverse entities exist with all required fields (`sprk_event` to-do and scoring fields, `sprk_matter` financial and grade fields)
- AI Playbook platform operational for AI features
- BFF API running and accessible from Custom Page
- SharePoint Embedded container configured for document uploads

### External Dependencies

- Power Apps Custom Page hosting capability in target MDA environment
- `@fluentui/react-components` v9 (latest stable)
- `@fluentui/react-icons` (latest stable)
- Azure OpenAI (via AI Playbook) for AI features
- SharePoint Embedded API (via SpeFileStore) for file uploads

## Dataverse Entities Used

All existing — no new entities required:

| Entity | Purpose | Key Fields |
|--------|---------|------------|
| `sprk_event` | Feed items, to-do items, scoring | todoflag, todostatus, todosource, priority, priorityscore, effort, effortscore, estimatedminutes, priorityreason, effortreason |
| `sprk_matter` | Portfolio, health metrics, scoring context | name, type, practicearea, totalbudget, totalspend, utilizationpercent, budgetcontrols_grade, guidelinescompliance_grade, outcomessuccess_grade, overdueeventcount |
| `sprk_project` | Portfolio widget projects tab | name, type, practicearea, owner, status, budgetused |
| `sprk_document` | Portfolio widget documents tab, file uploads | name, type, description, matter lookup, modifiedon |
| `sprk_organization` | Organization lookups (matters, counsel) | name |
| `sprk_contact` | Lead attorney, to-do assignment, recipients | name |

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| React version | Design says React 18+ but ADR-021/022 mandate React 16 APIs. Which applies? | **Custom Page (React 18)** — This is a Power Apps Custom Page, not a standard PCF control. React 18 is acceptable. | ADR exception documented. Can use `createRoot`, modern React APIs, concurrent features. |
| Action card scope | Only Create New Matter is fully specified. Are other 6 cards in scope? | **All cards in scope for R1.** All 7 action cards should be functional. | Only Create New Matter is a custom dialog. The other 6 cards are entry points to the existing AI Playbook Analysis Builder. |
| Action card architecture | Should the 6 non-Create-Matter cards build new custom dialogs? | **No — reuse existing Playbook components.** These are entry points into the existing Analysis Builder (`AiToolAgent` PCF), not standalone dialogs. Each card launches the Builder with pre-configured context/intent. | Major scope reduction. No new dialog UX needed for 6 cards. Integration work only — passing correct context to existing Playbook system. |
| Feed data source | Should Updates feed use Xrm.WebApi or BFF endpoint? | **Hybrid approach.** Xrm.WebApi for simple queries, BFF for complex aggregations and AI. | Two data access patterns needed. Client-side for CRUD, server-side for aggregation/AI. |
| Prototype code | Prototype at `projects/2025-01-corporate-legal-home/` doesn't exist. Is prototype code available? | **Implement from design doc.** No prototype code. Use design document and 15 screenshots as reference. | Fresh implementation. No code to port. Screenshots in `projects/home-corporate-workspace-r1/screenshots/`. |

## Assumptions

*Proceeding with these assumptions (owner did not specify):*

- **Concurrent users**: Assuming < 100 concurrent users for portfolio aggregation queries. Will affect caching strategy if higher.
- **Matter volume**: Assuming < 500 matters per user for My Portfolio. Will need pagination/virtual scrolling if significantly more.
- **Notification persistence**: Assuming notification read/unread state tracked in Dataverse (on `sprk_event` entity) rather than client session only. This provides cross-session persistence.
- **Calendar integration**: Assuming calendar always navigates to MDA calendar view (out of scope per design open question #3).
- **System-generated to-do trigger**: Assuming implementation via BFF scheduled job (not Dataverse plugin), since scoring requires multi-entity context that plugins cannot access efficiently.
- **Authentication to BFF**: Assuming Custom Page can acquire tokens for BFF API via MSAL or delegated auth flow from MDA context.

## Unresolved Questions

*May need answers during implementation:*

- [ ] **Custom Page auth flow**: How does the Custom Page iframe authenticate to the BFF API? Need to confirm token acquisition pattern from MDA context. — Blocks: BFF endpoint integration
- [x] **Action card architecture**: ~~Resolved~~ — 6 non-Create-Matter cards launch existing AI Playbook Analysis Builder with pre-configured context. No new dialog specs needed.
- [ ] **Scoring refresh mechanism**: Should priority score daily batch run as a BFF BackgroundService or Power Automate flow? — Blocks: Scoring system implementation
- [ ] **Quick Summary AI availability**: Is the Portfolio Analysis playbook available in AI Playbook, or should R1 use deterministic aggregation only? — Blocks: Block 1B AI briefing

---

*AI-optimized specification. Original design: `projects/home-corporate-workspace-r1/design.md` (v2.0, 1090 lines)*

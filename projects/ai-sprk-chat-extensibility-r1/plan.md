# Project Plan: SprkChat Analysis Workspace Command Center

> **Last Updated**: 2026-03-25
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Transform SprkChat from text-only chat into a contextual command center for the Analysis Workspace with three interaction tiers (natural language, chips, slash commands), smart routing, and compound action capabilities.

**Scope**:
- Phase 0: Scope enforcement + side pane lifecycle
- Phase 1: Smart chat foundation (context enrichment, slash commands, system commands)
- Phase 2: Quick-action chips
- Phase 3: Compound actions + plan preview + email drafting
- Phase 4: Prompt templates + playbook library browser

**Estimated Effort**: ~80–100 hours across 5 phases

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: BFF Minimal API; no separate microservices for AI features
- **ADR-006**: SprkChat is a React Code Page (React 18+, bundled); NOT PCF
- **ADR-008**: Endpoint filters for authorization; no global middleware
- **ADR-012**: New shared components in `@spaarke/ui-components`; callback-based props; zero service dependencies
- **ADR-013**: AI features extend BFF; flow ChatHostContext through pipeline; use RagSearchOptions
- **ADR-021**: Fluent v9 exclusively; semantic tokens for colors; dark mode required

**From Spec**:
- MUST NOT call Azure AI services directly from client
- MUST NOT create legacy JavaScript webresources
- Context enrichment payload < 1KB (NFR-07)
- Slash menu opens within 100ms (NFR-01)
- All components support keyboard navigation + screen reader labels (NFR-05)

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Hybrid routing (client enrichment + server AI) | Client adds structured context; BFF model uses it for routing | New `contextSignals` field in chat message payload |
| Remove global ribbon entirely | SprkChat only relevant in Analysis Workspace | Delete `sprk_application_ribbon_sprkchat.xml` |
| Playbook capabilities as action source | No new Dataverse table needed | Dynamic chips + commands from existing data |
| Dual pane close mechanism | useEffect cleanup + contextService poll fallback | Robust lifecycle across navigation patterns |

### Discovered Resources

**Applicable ADRs** (6):
- `.claude/adr/ADR-001-minimal-api.md` — Minimal API patterns
- `.claude/adr/ADR-006-pcf-over-webresources.md` — Code Page surface
- `.claude/adr/ADR-008-endpoint-filters.md` — Authorization filters
- `.claude/adr/ADR-012-shared-components.md` — Shared component library
- `.claude/adr/ADR-013-ai-architecture.md` — AI architecture
- `.claude/adr/ADR-021-fluent-design-system.md` — Fluent v9 design system

**Applicable Patterns**:
- `.claude/patterns/ai/streaming-endpoints.md` — SSE endpoint pattern
- `.claude/patterns/api/endpoint-definition.md` — Minimal API endpoint pattern
- `.claude/patterns/api/send-email-integration.md` — Email via Graph API
- `.claude/patterns/webresource/` — Code Page patterns

**Existing Code (Reuse)**:
- `src/client/shared/.../SprkChat/SprkChatInput.tsx` — Input interception for `/` trigger
- `src/client/shared/.../SprkChat/QuickActionChips.tsx` — Existing chip component
- `src/client/shared/.../SprkChat/PlanPreviewCard.tsx` — Existing plan preview
- `src/client/shared/.../SprkChat/hooks/useDynamicSlashCommands.ts` — Command registry hook
- `src/client/shared/.../SprkChat/hooks/useChatPlaybooks.ts` — Playbook switching
- `src/client/code-pages/SprkChatPane/src/services/contextService.ts` — Context polling
- `src/server/api/.../Services/Ai/Chat/PlaybookChatContextProvider.cs` — Tool registration
- `src/server/api/.../Services/Ai/Chat/DynamicCommandResolver.cs` — Command resolution
- `src/server/api/.../Services/Ai/Chat/CompoundIntentDetector.cs` — Multi-step detection

**Scripts**:
- `scripts/Deploy-Playbook.ps1` — Playbook deployment

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0: Scope Enforcement + Side Pane Lifecycle (Tasks 001-004)
├─ Remove SidePaneManager from Corporate Workspace
├─ Remove global ribbon button
├─ Implement side pane close on navigation
└─ Implement auto-reopen with session restore

Phase 1: Smart Chat Foundation (Tasks 010-017)
├─ Client-side context enrichment types + hook
├─ BFF context signal processing
├─ SlashCommandMenu component (Fluent v9)
├─ System commands (/clear, /new, /export, /help)
├─ Dynamic command registry integration
├─ Playbook switching from slash menu
├─ Natural language routing enhancements
└─ Integration tests

Phase 2: Quick-Action Chips (Tasks 020-022)
├─ Enhance QuickActionChips with playbook capabilities
├─ Analysis-type-aware chip sets
└─ Responsive behavior + integration

Phase 3: Compound Actions + Plan Preview (Tasks 030-036)
├─ Enhance PlanPreviewCard for edit/cancel
├─ CompoundActionProgress component
├─ Email drafting tool + preview
├─ Email send flow (BFF → Graph API)
├─ Write-back with before/after preview
├─ BFF compound action orchestration
└─ Integration tests

Phase 4: Prompt Templates + Playbook Library (Tasks 040-043)
├─ Prompt template data model + Dataverse config
├─ Template parameter UI in slash menu
├─ Playbook library browser component
└─ /playbooks command integration

Wrap-up: Task 090
```

### Critical Path

**Blocking Dependencies:**
- Phase 0 MUST complete before Phase 1 (clean slate for new features)
- Phase 1 context enrichment BLOCKS Phase 2 chips (chips need context signals)
- Phase 1 slash menu BLOCKS Phase 3 compound actions (plan preview uses slash menu patterns)
- Phase 3 plan preview BLOCKS Phase 3 email (email uses plan preview flow)

**Parallel Opportunities:**
- Phase 0: Tasks 001+002 in parallel (independent removals), Tasks 003+004 in parallel (independent lifecycle features)
- Phase 1: Tasks 010+012+013 in parallel (context types, SlashCommandMenu, system commands are independent)
- Phase 3: Tasks 030+032 in parallel (PlanPreview enhancement + email tool are independent)
- Phase 4: Tasks 040+042 in parallel (template data model + playbook browser are independent)

**High-Risk Items:**
- Slash command namespace collisions — Mitigation: prefix with playbook name
- Side pane race conditions on fast navigation — Mitigation: dual close mechanism
- Compound action rollback on partial failure — Mitigation: preserve partial results + error state

---

## 4. Phase Breakdown

### Phase 0: Scope Enforcement + Side Pane Lifecycle

**Objectives:**
1. Remove SprkChat from non-analysis pages
2. Implement robust side pane lifecycle (close on nav away, reopen on return)

**Deliverables:**
- [ ] SidePaneManager injection removed from Corporate Workspace
- [ ] Global ribbon button removed from solution
- [ ] Side pane closes on navigation away from Analysis
- [ ] Side pane auto-reopens with session restore on return

**Critical Tasks:**
- FR-01 + FR-02 (removals) — CAN RUN IN PARALLEL
- FR-03 + FR-04 (lifecycle) — CAN RUN IN PARALLEL after removals

**Inputs**: `src/solutions/LegalWorkspace/index.html`, `src/client/webresources/ribbon/sprk_application_ribbon_sprkchat.xml`, `src/client/code-pages/AnalysisWorkspace/src/App.tsx`, `src/client/code-pages/SprkChatPane/src/services/contextService.ts`

**Outputs**: Modified files above; side pane lifecycle working

### Phase 1: Smart Chat Foundation

**Objectives:**
1. Enrich outgoing messages with structured context signals
2. Build SlashCommandMenu component with keyboard navigation
3. Implement system commands and dynamic command registry
4. Enable playbook switching from menu

**Deliverables:**
- [ ] Context enrichment types and hook (`useContextEnrichment`)
- [ ] BFF processes context signals in system prompt
- [ ] SlashCommandMenu Fluent v9 Popover component
- [ ] System commands (`/clear`, `/new`, `/export`, `/help`)
- [ ] Dynamic commands from playbook capabilities
- [ ] Playbook switching via slash menu
- [ ] Natural language routing uses enriched context
- [ ] Integration tests for slash menu + context enrichment

**Critical Tasks:**
- Context enrichment types (010) — FOUNDATIONAL, do first
- SlashCommandMenu (012), System commands (013) — CAN RUN IN PARALLEL
- BFF integration (011), Dynamic registry (014), Playbook switching (015) — AFTER foundations

**Inputs**: Existing SprkChat components, BFF Chat services, playbook hooks

**Outputs**: New `SlashCommandMenu.tsx`, enhanced `SprkChatInput.tsx`, new BFF context processing

### Phase 2: Quick-Action Chips

**Objectives:**
1. Populate chips from playbook capabilities + analysis context
2. Responsive behavior for narrow panes

**Deliverables:**
- [ ] Enhanced QuickActionChips with playbook capability source
- [ ] Analysis-type-aware chip sets
- [ ] Chips hidden when pane < 350px; max 4 chips
- [ ] Chips update on playbook switch or context change

**Inputs**: Phase 1 context enrichment, existing QuickActionChips component

**Outputs**: Enhanced `QuickActionChips.tsx`, new chip data hooks

### Phase 3: Compound Actions + Plan Preview

**Objectives:**
1. Multi-step plan preview with edit/cancel
2. Email drafting and send via sprk_communication
3. Write-back with before/after diff and confirmation
4. Progress indicators for multi-step execution

**Deliverables:**
- [ ] Enhanced PlanPreviewCard with Edit plan + Cancel actions
- [ ] CompoundActionProgress component (step-by-step status)
- [ ] Email draft tool + preview in chat
- [ ] Email send flow (BFF → Graph API via sprk_communication)
- [ ] Write-back tool with diff preview + confirmation
- [ ] BFF compound action orchestration

**Critical Tasks:**
- PlanPreviewCard enhancement (030) + Email tool (032) — CAN RUN IN PARALLEL
- CompoundActionProgress (031) — depends on PlanPreviewCard pattern
- Email send (033) — depends on email tool (032)
- Write-back (034) — independent of email, CAN RUN IN PARALLEL with 032-033

**Inputs**: Existing PlanPreviewCard, ActionConfirmationDialog, sprk_communication services

**Outputs**: Enhanced plan preview, new progress component, email flow, write-back flow

### Phase 4: Prompt Templates + Playbook Library

**Objectives:**
1. Parameterized prompt templates from Dataverse
2. Browsable playbook catalog in chat

**Deliverables:**
- [ ] Prompt template data model (Dataverse config or related table)
- [ ] Template parameter fill-in UI (slash menu integration)
- [ ] PlaybookLibraryBrowser component
- [ ] `/playbooks` command integration

**Inputs**: Existing playbook entity, slash command infrastructure from Phase 1

**Outputs**: Template parameter UI, playbook browser component, BFF template endpoints

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Azure OpenAI | Production | Low | Existing service, no changes |
| Microsoft Graph (email) | Production | Low | Existing sprk_communication module |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| Context Awareness (Project #1) | Complete | Production |
| SprkChat Workspace Companion | Complete | Production |
| SprkChat Platform Enhancement R2 | Complete | Production |
| sprk_communication module | `Sprk.Bff.Api/Services/` | Production |
| SprkChat shared components | `@spaarke/ui-components` | Production |

---

## 6. Testing Strategy

**Unit Tests** (80% coverage target):
- SlashCommandMenu component (rendering, keyboard nav, filtering)
- Context enrichment hook (signal generation)
- System command handlers
- QuickActionChips enhancement (capability source, responsive)
- PlanPreviewCard enhancement (edit, cancel flows)
- CompoundActionProgress (step states)

**Integration Tests**:
- Slash menu → system command execution
- Context enrichment → BFF routing accuracy
- Compound action → plan preview → approval → execution
- Email draft → refine → send flow
- Write-back → diff preview → confirmation → update

**E2E Tests**:
- Side pane lifecycle (navigate away → close → return → reopen)
- Full slash command flow (type `/` → filter → select → execute)
- Playbook switch → commands/chips update

---

## 7. Acceptance Criteria

### Phase 0:
- [ ] SprkChat icon absent from Corporate Workspace side pane rail
- [ ] No "SprkChat" button in global command bar on any page
- [ ] Navigating from Analysis to Corporate Workspace closes SprkChat pane
- [ ] Returning to same analysis record restores chat history

### Phase 1:
- [ ] Slash menu opens within 100ms of `/` keypress
- [ ] Full keyboard navigation (Arrow Up/Down, Enter, Esc)
- [ ] Type-ahead filtering works (`/se` → `/search`)
- [ ] System commands execute without hitting BFF AI
- [ ] Playbook switch updates commands within 1 second
- [ ] BFF receives enriched payload with context signals

### Phase 2:
- [ ] Max 4 chips displayed; update within 500ms of context change
- [ ] Chips hidden when pane width < 350px
- [ ] Tapping chip sends structured message with `source: "chip_click"`

### Phase 3:
- [ ] Multi-step actions show numbered plan before executing
- [ ] "Edit plan" allows conversational modification
- [ ] Email draft → refine → send works end-to-end
- [ ] Write-back shows before/after diff with confirmation

### Phase 4:
- [ ] Templates with fill-in parameters available from slash menu
- [ ] `/playbooks` shows browsable catalog
- [ ] "Try this playbook" activates in current session

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Slash command namespace collisions | Medium | Medium | Prefix commands with playbook name |
| R2 | Compound action partial failure | Low | High | Preserve partial results + error UI |
| R3 | Side pane race conditions | Medium | Medium | Dual mechanism (useEffect + poll) |
| R4 | Context enrichment payload bloat | Low | Low | Cap at < 1KB, validate in tests |
| R5 | Email recipient resolution ambiguity | Medium | Medium | Use matter party/role relationships |

---

## 9. Next Steps

1. **Generate task files** via `/task-create`
2. **Begin Phase 0** — scope enforcement (parallel tasks 001+002)
3. **Continue through phases** — parallel groups maximize throughput

---

**Status**: Ready for Tasks
**Next Action**: Generate POML task files

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*

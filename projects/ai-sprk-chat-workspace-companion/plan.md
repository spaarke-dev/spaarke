# Project Plan: SprkChat Analysis Workspace Companion

> **Last Updated**: 2026-03-16
> **Status**: Complete
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Reposition SprkChat as a contextual AI companion for the Analysis Workspace by (1) removing unwanted global auto-registration, (2) enriching the launch with full analysis context, (3) adding inline AI toolbar + BroadcastChannel-based actions, (4) adding slash commands, quick-action chips, rich response cards, and (5) a plan preview gate with write-back to `sprk_analysisoutput`.

**Scope**:
- Phase 2A: Contextual launch — remove global injection, extend launcher context
- Phase 2B: Inline AI toolbar in shared library + wiring to EditorPanel
- Phase 2C: New BFF context-mappings/analysis endpoint + capability resolver
- Phase 2D: Insert-to-editor via BroadcastChannel
- Phase 2E: SlashCommandMenu, QuickActionChips, SprkChatMessageRenderer, PlanPreviewCard
- Phase 2F: BFF plan_preview SSE + approval endpoint + write-back

**Estimated Effort**: 8–12 days

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: All new BFF endpoints use Minimal API (no MVC controllers)
- **ADR-006**: AnalysisWorkspace is a Code Page (React 19, bundled) — not a PCF control
- **ADR-008**: New BFF endpoint MUST use endpoint filter for resource authorization
- **ADR-009**: Context mapping MUST use `IDistributedCache` (Redis) with 30-min TTL; no hybrid L1
- **ADR-012**: All new UI components go in `@spaarke/ui-components`; no Xrm dependency
- **ADR-013**: AI features extend `Sprk.Bff.Api` only — no separate AI microservice
- **ADR-021**: All UI uses Fluent v9 tokens exclusively; dark mode required; no hard-coded colors
- **ADR-022**: AnalysisWorkspace uses React 19 `createRoot()` — not PCF platform React

**From Spec**:
- `mousedown` (not `click`) on toolbar buttons to prevent selection loss
- All inline actions route through existing SprkChat session (appear in chat history)
- Reuse existing `DiffReviewPanel` — no new diff component
- Plan preview gates all 2+ tool chains and Dataverse write-back — no stubs
- Write-back targets `sprk_analysisoutput.sprk_workingdocument` ONLY

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| `InlineAiToolbar` in shared library | Code Pages only (not PCF-safe), reusable, no Xrm dependency | 5 new files in Spaarke.UI.Components |
| Static capability→InlineAction map in BFF | `sprk_playbookcapabilities` is a multi-select option set; static dict avoids schema change | `AnalysisChatContextResolver.cs` |
| `mousedown` guard on toolbar buttons | Prevents browser `selectionchange` event firing and collapsing selection on click | `InlineAiToolbar.tsx` |
| Mandatory plan preview (no stubs) | All 5 spec phases included; plan_preview SSE must be real | Phase 2F BFF changes |

### Discovered Resources

**Applicable Skills**:
- `.claude/skills/code-page-deploy/` — Deploy AnalysisWorkspace + SprkChatPane web resources
- `.claude/skills/bff-deploy/` — Deploy Sprk.Bff.Api to Azure App Service
- `.claude/skills/adr-aware/` — Auto-load ADRs during implementation
- `.claude/skills/dataverse-deploy/` — Deploy solution if schema changes needed

**Reusable Code**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatContextMappingService.cs` — Extend for analysis resolution
- `src/server/api/Sprk.Bff.Api/Api/Ai/AiToolEndpoints.cs` — SSE streaming pattern for plan_preview
- `src/client/code-pages/AnalysisWorkspace/src/hooks/useDocumentStreaming.ts` — BroadcastChannel event handling
- `src/client/code-pages/AnalysisWorkspace/src/hooks/useDiffReview.ts` — Reuse for diff-type inline actions
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx` — Mount point for new components
- `src/client/code-pages/SprkChatPane/launcher/openSprkChatPane.ts` — Extend with SprkChatLaunchContext

---

## 3. Implementation Approach

### Phase Structure

```
Phase 2A: Contextual Launch (Days 1-2)
└─ Remove global injection + extend launcher with SprkChatLaunchContext
└─ Wire AnalysisWorkspace App.tsx to pass enriched context

Phase 2B: Inline AI Toolbar — Library (Days 1-3, PARALLEL with 2A+2C)
└─ 5 new files in InlineAiToolbar/ component group
└─ BroadcastChannel inline_action events

Phase 2C: Context-Driven Actions — BFF (Days 1-3, PARALLEL with 2A+2B)
└─ New BFF endpoint + AnalysisChatContextResolver
└─ Capability-to-InlineAction mapping

Phase 2B-Wiring: EditorPanel Integration (Days 3-4, after 2B library)
└─ useInlineAiToolbar hook in AnalysisWorkspace
└─ Mount toolbar in EditorPanel.tsx

Phase 2D: Insert-to-Editor (Days 3-4, PARALLEL with 2B-Wiring)
└─ document_insert BroadcastChannel event
└─ Insert hook in editor

Phase 2E: Enhanced Pane Components (Days 4-6, after 2C)
└─ SlashCommandMenu + QuickActionChips + SprkChatMessageRenderer + PlanPreviewCard
└─ Wire into SprkChat.tsx and SprkChatInput.tsx

Phase 2F: BFF Plan Preview + Write-Back (Days 7-9, after 2E)
└─ plan_preview SSE event type
└─ Plan approval endpoint
└─ Write-back to sprk_analysisoutput

Testing + Deploy (Days 9-12)
```

### Critical Path

**Blocking Dependencies:**
- Phase 2B-Wiring (EditorPanel) BLOCKED BY Phase 2B library (InlineAiToolbar components)
- Phase 2E wiring (SprkChat.tsx, SprkChatInput.tsx) BLOCKED BY Phase 2E components + Phase 2C endpoint
- Phase 2F BLOCKED BY Phase 2E (PlanPreviewCard must exist)
- Insert-to-editor BLOCKED BY Phase 2B (BroadcastChannel bridge pattern established)

**Parallel Execution Groups:**
```
Group A (start immediately):
  001-003: Phase 2A — Contextual Launch
  010-013: Phase 2B — InlineAiToolbar UI library components
  020-022: Phase 2C — BFF analysis context endpoint

Group B (after Group A):
  030-031: Phase 2B-Wiring — useInlineAiToolbar + EditorPanel
  040-043: Phase 2E — SlashCommandMenu + chip/renderer/preview components

Group C (after Group B):
  050-051: Phase 2D — Insert-to-Editor
  060-062: Phase 2E wiring — SprkChat.tsx + SprkChatInput.tsx

Group D (after Group C):
  070-073: Phase 2F — BFF plan preview + write-back

Group E (after Group D):
  080: Integration tests
  081-082: Deploy
  090: Wrap-up
```

**High-Risk Items:**
- Plan preview session state (Phase 2F) — Mitigation: Investigate `ChatSessionManager` before task 070
- Lexical selection event timing — Mitigation: 200ms debounce + mousedown guard

---

## 4. Phase Breakdown

### Phase 2A: Contextual Launch (Tasks 001–003)

**Objectives:**
1. Remove SprkChat auto-registration from non-AI pages
2. Enrich Analysis Workspace launch with full analysis context

**Deliverables:**
- [x] `SprkChatLaunchContext` interface added to `openSprkChatPane.ts` (analysisType, matterType, practiceArea, analysisId, sourceFileId, sourceContainerId, mode)
- [x] `buildDataParams()` updated to include all 7 context fields
- [x] `AnalysisWorkspace/App.tsx` updated to pass enriched context after record loads
- [x] `SprkChatPane/src/services/contextService.ts` updated to consume new URL params
- [x] SidePaneManager injection snippet removed from `EventsPage/index.html`
- [x] SidePaneManager injection snippet removed from `SpeAdminApp/index.html`

**Inputs**: `openSprkChatPane.ts`, `App.tsx`, `contextService.ts`, `EventsPage/index.html`, `SpeAdminApp/index.html`

**Outputs**: Modified launcher, updated context consumption, cleaned HTML pages

**Can run in parallel with**: Phase 2B, Phase 2C

---

### Phase 2B: Inline AI Toolbar — Library (Tasks 010–013)

**Objectives:**
1. Create floating InlineAiToolbar component in shared library
2. Define 5 default inline actions with BroadcastChannel dispatch

**Deliverables:**
- [x] `InlineAiToolbar/inlineAiToolbar.types.ts` — Types + 5 default action definitions (summarize, simplify, expand, fact-check, ask)
- [x] `InlineAiToolbar/InlineAiToolbar.tsx` — Floating toolbar positioned above selection
- [x] `InlineAiToolbar/InlineAiActions.tsx` — Action button list with mousedown handler
- [x] `InlineAiToolbar/useInlineAiToolbar.ts` — Position + visibility hook (200ms debounce)
- [x] `InlineAiToolbar/useInlineAiActions.ts` — BroadcastChannel `inline_action` dispatch
- [x] `@spaarke/ui-components` barrel exports updated
- [x] Unit tests for hook and component

**Inputs**: `SprkChat/types.ts` (BroadcastChannel event types), Fluent v9 components

**Outputs**: 5 new files in `Spaarke.UI.Components/src/components/InlineAiToolbar/`

**Can run in parallel with**: Phase 2A, Phase 2C
**Blocks**: Phase 2B-Wiring (EditorPanel)

---

### Phase 2B-Wiring: EditorPanel Integration (Tasks 030–031)

**Objectives:**
1. Wire InlineAiToolbar into AnalysisWorkspace EditorPanel

**Deliverables:**
- [x] `AnalysisWorkspace/src/hooks/useInlineAiToolbar.ts` — Coordinates selection position, toolbar visibility, and bridge dispatch
- [x] `EditorPanel.tsx` updated to mount `InlineAiToolbar` as overlay
- [x] SprkChat `SprkChat.tsx` updated to subscribe to `inline_action` BroadcastChannel event

**Inputs**: InlineAiToolbar components (Phase 2B), `EditorPanel.tsx`, `SprkChat.tsx`

**Outputs**: Toolbar visible on text selection; actions dispatched to SprkChat session

**Depends on**: Phase 2B (InlineAiToolbar library)

---

### Phase 2C: Context-Driven Actions — BFF (Tasks 020–022)

**Objectives:**
1. Create new BFF endpoint that resolves analysis identity → full context mapping
2. Map `sprk_playbookcapabilities` values to inline action and slash command definitions

**Deliverables:**
- [x] `AnalysisChatContextResolver.cs` — Service resolving analysis record + related matter + source document
- [x] New route registered: `GET /api/ai/chat/context-mappings/analysis/{analysisId}` with endpoint filter
- [x] Capability-to-InlineAction static mapping (options 100000000–100000006)
- [x] Response model: `defaultPlaybook`, `availablePlaybooks`, `inlineActions`, `knowledgeSources`, `analysisContext`
- [x] Redis caching with 30-min TTL
- [x] Unit tests for resolver
- [x] Seed data: patent-claims playbook with populated `sprk_playbookcapabilities`

**Inputs**: `ChatContextMappingService.cs` (extend), `ADR-008` (endpoint filter), `ADR-009` (Redis caching)

**Outputs**: New BFF endpoint, resolver service, seed data

**Can run in parallel with**: Phase 2A, Phase 2B
**Blocks**: Phase 2E (context chips + slash commands depend on this endpoint)

---

### Phase 2D: Insert-to-Editor (Tasks 050–051)

**Objectives:**
1. Add "Insert" button to SprkChat message responses
2. Implement `document_insert` BroadcastChannel event with undo support

**Deliverables:**
- [x] "Insert" button added to `SprkChatMessage.tsx` on AI responses
- [x] `document_insert` BroadcastChannel event type added to bridge
- [x] Editor hook in AnalysisWorkspace that listens for `document_insert` and inserts at cursor or replaces selection
- [x] Undo support via Lexical editor undo stack
- [x] Supports plain text and formatted HTML

**Inputs**: `SprkChatMessage.tsx`, `useDocumentStreaming.ts` (bridge pattern), `EditorPanel.tsx`

**Outputs**: Insert button + editor receives content

**Depends on**: Phase 2B-Wiring (BroadcastChannel bridge pattern)

---

### Phase 2E: Enhanced SprkChat Pane — Components (Tasks 040–043)

**Objectives:**
1. Create SlashCommandMenu with dynamic command registry
2. Create QuickActionChips populated from context mapping endpoint
3. Create SprkChatMessageRenderer for structured response cards
4. Create PlanPreviewCard with approve/edit/cancel controls

**Deliverables:**
- [x] `SlashCommandMenu/slashCommand.types.ts` — Types for command registry
- [x] `SlashCommandMenu/useSlashCommands.ts` — Registry + type-ahead filtering
- [x] `SlashCommandMenu/SlashCommandMenu.tsx` — Fluent Popover, keyboard-navigable
- [x] `SprkChat/QuickActionChips.tsx` — Up to 4 chips, hidden when pane < 350px
- [x] `SprkChatMessageRenderer.tsx` — Renders 5 card types: markdown, citations, diffs, entity cards, action confirmations
- [x] `PlanPreviewCard.tsx` — Plan preview card with Proceed/Edit Plan/Cancel and per-step progress
- [x] Unit tests for SlashCommandMenu and SprkChatMessageRenderer

**Inputs**: Phase 2C endpoint (for chips data), Fluent v9 Popover component

**Outputs**: 6 new files in Spaarke.UI.Components

**Depends on**: Phase 2C (context mapping for chip data)
**Blocks**: Phase 2E-Wiring (must exist before SprkChat.tsx can mount them)

---

### Phase 2E-Wiring: Enhanced Pane Integration (Tasks 060–062)

**Objectives:**
1. Wire SlashCommandMenu + QuickActionChips into SprkChat
2. Wire PlanPreviewCard into message rendering pipeline

**Deliverables:**
- [x] `SprkChat.tsx` updated: mount `QuickActionChips` (above input), mount `SlashCommandMenu`, handle `inline_action` event subscription
- [x] `SprkChatInput.tsx` updated: add `[/]` button, intercept `/` keystroke to open slash menu
- [x] Message rendering pipeline uses `SprkChatMessageRenderer` based on `metadata.responseType`

**Inputs**: Phase 2E components, existing `SprkChat.tsx` and `SprkChatInput.tsx`

**Outputs**: Fully wired enhanced pane

**Depends on**: Phase 2E components, Phase 2C (context data)

---

### Phase 2F: BFF Plan Preview + Write-Back (Tasks 070–073)

**Objectives:**
1. Emit `plan_preview` SSE event before compound tool chains or write-back
2. Implement plan approval endpoint with session state tracking
3. Implement write-back to `sprk_analysisoutput.sprk_workingdocument`

**Deliverables:**
- [x] `plan_preview` SSE event type added to BFF SSE pipeline
- [x] BFF compound intent detection (2+ tool chain, write-back, external action)
- [x] Plan approval endpoint: `POST /api/ai/chat/sessions/{sessionId}/plan/approve`
- [x] Session state: "pending plan" tracked between `plan_preview` emit and user approval
- [x] Conversational plan editing support ("skip step 2", "also include the deadline")
- [x] Write-back implementation: Dataverse update to `sprk_analysisoutput.sprk_workingdocument`
- [x] PlanPreviewCard per-step progress via SSE

**Inputs**: `ChatSessionManager.cs` (session model), `AiToolEndpoints.cs` (SSE pattern), PlanPreviewCard component

**Outputs**: Full plan preview gate + write-back

**Depends on**: Phase 2E (PlanPreviewCard must exist on client side)

**⚠️ Pre-condition**: Investigate `ChatSessionManager` session model before starting — see Unresolved Questions in CLAUDE.md

---

### Phase Testing (Task 080)

**Objectives:**
1. Integration tests for new BFF endpoint
2. E2E manual verification checklist from spec success criteria

**Deliverables:**
- [x] Integration test: `GET /api/ai/chat/context-mappings/analysis/{analysisId}` returns correct response
- [x] Integration test: Plan approval endpoint flow
- [x] Manual E2E: all 14 success criteria from spec verified

---

### Phase Deploy (Tasks 081–082)

**Objectives:**
1. Deploy updated code pages (AnalysisWorkspace, SprkChatPane)
2. Deploy updated BFF API

**Deliverables:**
- [x] AnalysisWorkspace web resource deployed via `code-page-deploy` skill
- [x] SprkChatPane web resource deployed via `code-page-deploy` skill
- [x] `Sprk.Bff.Api` deployed via `bff-deploy` skill

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| `sprk_analysisplaybook` records in Dataverse with `sprk_playbookcapabilities` | Seed data task | Low | Task 022 seeds patent-claims example |
| Azure Redis (IDistributedCache) | Production | Low | Already in use by ChatContextMappingService |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| Phase 1: ChatContextMappingService | `Services/Ai/Chat/ChatContextMappingService.cs` | ✅ Merged |
| DiffReviewPanel + useDiffReview | `AnalysisWorkspace/src/` | ✅ Exists |
| BroadcastChannel bridge | `AnalysisWorkspace/src/components/DocumentStreamBridge.tsx` | ✅ Exists |
| SprkChat components | `Spaarke.UI.Components/src/components/SprkChat/` | ✅ Exists |

---

## 6. Testing Strategy

**Unit Tests** (90%+ coverage on shared components per ADR-012):
- `InlineAiToolbar` position/visibility hook (200ms debounce)
- `SlashCommandMenu` filtering and keyboard navigation
- `SprkChatMessageRenderer` card type selection
- `AnalysisChatContextResolver` capability mapping

**Integration Tests**:
- `GET /api/ai/chat/context-mappings/analysis/{analysisId}` — full response validation
- Plan preview → approve flow — session state tracking

**Manual E2E** (from spec success criteria):
- Text selection triggers toolbar within 200ms
- Inline actions appear in SprkChat history
- Simplify/Expand open DiffReviewPanel
- Patent-claims playbook shows extract-claims and prior-art-search
- Plan preview appears before write-back; no execution until Proceed clicked
- SPE source file unchanged after write-back

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 2A:**
- [x] EventsPage loads without creating SprkChat side pane
- [x] SpeAdminApp loads without creating SprkChat side pane
- [x] Analysis Workspace URL params contain all 7 context fields after record load

**Phase 2B:**
- [x] InlineAiToolbar visible within 200ms of stable selection; absent when selection collapsed
- [x] All inline actions appear in SprkChat chat history pane

**Phase 2C:**
- [x] Endpoint returns defaultPlaybook, availablePlaybooks, inlineActions, knowledgeSources, analysisContext
- [x] Redis cache hit on second request (TTL 30 min)
- [x] Patent-claims playbook shows extract-claims and prior-art-search inline actions

**Phase 2D:**
- [x] Selected text replaced or content inserted at cursor after "Insert" click
- [x] Undo works (Lexical undo stack)

**Phase 2E:**
- [x] Slash menu opens on `/` keystroke; type-ahead filters; Esc dismisses
- [x] Quick-action chips update when playbook changes
- [x] All 5 structured response card types render correctly

**Phase 2F:**
- [x] Plan preview appears before any 2+ tool chain execution
- [x] No Dataverse write-back executes without user Proceed confirmation
- [x] `sprk_analysisoutput.sprk_workingdocument` updated after approve
- [x] SPE source file unchanged

### Business Acceptance
- [x] SprkChat feels context-aware in Analysis Workspace (suggestions relevant to analysis type)
- [x] Inline toolbar enables without breaking existing editor selection behavior

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Plan preview session state complexity (Phase 2F) | Medium | High | Investigate ChatSessionManager before task 070; may need new session state field |
| R2 | Lexical selection timing (mousedown vs click edge cases) | Medium | Medium | 200ms debounce, mousedown guard; write focused unit tests |
| R3 | `sprk_playbookcapabilities` field values differ from assumptions | Low | High | Values confirmed in spec Assumptions section (7 values, 100000000–100000006) |
| R4 | SSE streaming for PlanPreviewCard per-step progress | Medium | Medium | Reuse existing SSE pipeline; extend metadata.responseType |
| R5 | React 19 API compatibility in AnalysisWorkspace | Low | Low | Already using React 19; createRoot confirmed; no PCF patterns |

---

## 9. Next Steps

1. **Review this plan.md** for accuracy
2. **Work on tasks** — execute parallel Group A first: tasks 001-003, 010-013, 020-022
3. Say "work on task 001" (or "continue") to start task execution via task-execute skill

---

**Status**: Complete — 29 tasks implemented, deployed, and wrapped up. Run `/merge-to-master` to merge `work/ai-sprk-chat-workspace-companion` → master.
**Next Action**: Execute tasks starting with Group A in parallel

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks. Parallel execution groups are in CLAUDE.md.*

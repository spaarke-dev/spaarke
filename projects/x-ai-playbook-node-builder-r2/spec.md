# AI Chat Playbook Builder - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-01-16
> **Source**: ai-chat-playbook-builder.md
> **Priority**: High
> **Estimated Effort**: 6-7 weeks

---

## Executive Summary

The AI Chat Playbook Builder adds conversational AI assistance to the PlaybookBuilderHost PCF control, enabling users to build playbooks through natural language while seeing results update in real-time on the visual canvas. This establishes a unified AI agent framework where the Builder itself is a playbook (PB-BUILDER) using the same scope infrastructure it helps users compose.

---

## Scope

### In Scope

- **PCF Components**
  - Floating AI assistant modal in PlaybookBuilderHost
  - Chat history component with streaming support
  - Chat input with send functionality
  - Operation feedback UI (progress indicators)
  - Zustand store for AI assistant state

- **BFF API**
  - `/api/ai/build-playbook-canvas` SSE streaming endpoint
  - `/api/ai/test-playbook-execution` test execution endpoint
  - Canvas patch schema and operations
  - Dataverse scope record CRUD operations
  - Conversation context handling (session-scoped)

- **AI Integration**
  - Intent classification system (11 intent categories)
  - Entity resolution with confidence thresholds
  - Build plan generation (internal spec/tasks)
  - Scope selection algorithm with semantic matching
  - Clarification loops for ambiguous input

- **Test Execution**
  - Mock test mode (sample data, no storage)
  - Quick test mode (temp blob, 24hr TTL)
  - Production test mode (full flow with records)

- **Scope Management**
  - Ownership model (SYS- immutable, CUST- editable)
  - "Save As" functionality for playbooks and scopes
  - "Extend" functionality with inheritance
  - Auto-prefix CUST- and suffix (1) for duplicate names
  - GenericAnalysisHandler for configurable tools

- **Builder-Specific Scopes**
  - ACT-BUILDER-001 through ACT-BUILDER-005 (Actions)
  - SKL-BUILDER-001 through SKL-BUILDER-005 (Skills)
  - TL-BUILDER-001 through TL-BUILDER-009 (Tools)
  - KNW-BUILDER-001 through KNW-BUILDER-004 (Knowledge)

- **Unified Framework**
  - PB-BUILDER meta-playbook definition
  - Conversational execution mode for PlaybookExecutionEngine
  - Tiered AI model selection (GPT-4o-mini, GPT-4o, o1-mini)

### Out of Scope

- M365 Copilot plugin integration
- Custom tool handler code (only configuration via GenericAnalysisHandler)
- N:N batch processing (running same playbook N times on N documents)
- Conversation history persistence beyond session (ephemeral only)
- Knowledge file upload/RAG (covered by ENH-003 separately)

### Affected Areas

- `src/client/pcf/PlaybookBuilderHost/` - New AI assistant components
- `src/server/api/Sprk.Bff.Api/Api/Ai/` - New builder endpoints
- `src/server/api/Sprk.Bff.Api/Services/Ai/` - New builder service
- Dataverse `sprk_analysisscope` entity - New ownership fields
- Dataverse `sprk_analysisplaybook` entity - Builder scope links

---

## Requirements

### Functional Requirements

1. **FR-01: Natural Language Canvas Operations**
   - Users can add/remove/modify nodes via natural language
   - Users can create/delete edges via natural language
   - Users can configure nodes via natural language
   - Acceptance: 11 intent categories correctly classified with >75% confidence

2. **FR-02: Real-Time Canvas Updates**
   - Canvas updates stream via SSE as operations execute
   - UI reflects changes immediately without page refresh
   - Acceptance: <500ms latency from operation to visual update

3. **FR-03: Scope Record Management**
   - AI can create Actions, Skills, Knowledge metadata, and Outputs in Dataverse
   - AI can link existing scopes to nodes
   - AI can search scopes by semantic matching
   - Acceptance: CRUD operations complete successfully, N:N links created

4. **FR-04: Chat History**
   - Conversation history maintained for session duration
   - Each message tracks associated canvas operations
   - History cleared on new session/page refresh
   - Acceptance: 10-message context window, operations trackable

5. **FR-05: Intent Classification with Clarification**
   - AI classifies user input into operation intents
   - When confidence <75%, AI asks clarifying questions
   - Multiple entity matches trigger selection prompt
   - Acceptance: Clarification triggered appropriately, no silent failures

6. **FR-06: Test Execution Modes**
   - Mock test: Canvas JSON + sample data, no storage
   - Quick test: Canvas JSON + uploaded file, temp blob (24hr TTL)
   - Production test: Saved playbook + SPE document, full records
   - Acceptance: All three modes execute correctly, results displayed

7. **FR-07: Scope Ownership Model**
   - System scopes (SYS-*) are immutable
   - Customer scopes (CUST-*) are editable/deletable
   - Ownership field on all scope entities
   - Acceptance: SYS- scopes reject edit/delete, CUST- scopes allow

8. **FR-08: Scope Customization (Save As / Extend)**
   - "Save As" creates independent copy with CUST- prefix
   - "Extend" creates child scope inheriting from parent
   - Duplicate display names get suffix (1), (2), etc.
   - Acceptance: Both operations create correct records with lineage

9. **FR-09: Builder-Specific Scopes**
   - 5 Builder Actions (intent classification, node config, scope selection, scope creation, plan generation)
   - 5 Builder Skills (lease pattern, contract pattern, risk pattern, node guide, scope matching)
   - 9 Builder Tools (addNode, removeNode, createEdge, updateNodeConfig, linkScope, createScope, searchScopes, autoLayout, validateCanvas)
   - 4 Builder Knowledge (scope catalog, reference playbooks, node schema, best practices)
   - Acceptance: All scopes created and referenced by PB-BUILDER

10. **FR-10: GenericAnalysisHandler**
    - JSON-configurable tool for custom analysis patterns
    - Output schema definition with validation
    - Strict schema validation with clear error messages
    - Acceptance: Custom tool configs execute correctly, validation errors reported

### Non-Functional Requirements

- **NFR-01: Streaming Performance**
  - SSE connection established within 1 second
  - First token streamed within 2 seconds
  - Total response within 30 seconds for typical operations

- **NFR-02: Rate Limiting**
  - Generous rate limits (~200 messages/hour per user)
  - Clear 429 ProblemDetails when limits exceeded
  - Max 10 operations per single request

- **NFR-03: Error Handling**
  - All errors return ProblemDetails (ADR-019)
  - Dataverse errors provide actionable messages
  - AI failures trigger graceful degradation

- **NFR-04: Bundle Size**
  - PCF bundle remains under 5MB
  - No React/Fluent bundled (platform libraries)

- **NFR-05: Accessibility**
  - Keyboard navigation for all modal interactions
  - ARIA labels on all interactive elements
  - Focus management when modal opens/closes

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|-----|-----------|
| **ADR-001** | Minimal API patterns for `/api/ai/build-playbook-canvas` endpoint |
| **ADR-006** | PCF control for AI assistant modal |
| **ADR-008** | Endpoint filters for authorization |
| **ADR-009** | Redis caching for scope lookups |
| **ADR-010** | DI minimalism (≤15 registrations) |
| **ADR-012** | Use `@spaarke/ui-components` for shared components |
| **ADR-013** | Extend BFF for AI (no separate microservice) |
| **ADR-016** | Rate limiting on AI endpoints |
| **ADR-019** | ProblemDetails for all errors |
| **ADR-021** | Fluent UI v9, dark mode support, tokens for styling |
| **ADR-022** | React 16 APIs only (`ReactDOM.render`, not `createRoot`) |

### MUST Rules

- ✅ MUST follow ADR-001 Minimal API patterns for all endpoints
- ✅ MUST use endpoint filters for authorization (ADR-008)
- ✅ MUST apply rate limiting to AI endpoints (ADR-016)
- ✅ MUST use `@fluentui/react-components` (Fluent v9) exclusively
- ✅ MUST use React 16 APIs in PCF (`ReactDOM.render`)
- ✅ MUST declare `platform-library` in PCF manifest
- ✅ MUST use design tokens for colors (no hard-coded values)
- ✅ MUST support light and dark modes
- ✅ MUST return ProblemDetails for all errors
- ✅ MUST access files through SpeFileStore only (ADR-007)

### MUST NOT Rules

- ❌ MUST NOT create separate AI microservice
- ❌ MUST NOT use React 18 APIs (`createRoot`, concurrent features)
- ❌ MUST NOT bundle React/Fluent in PCF output
- ❌ MUST NOT hard-code colors (hex, rgb)
- ❌ MUST NOT expose API keys to clients
- ❌ MUST NOT allow unbounded concurrent AI calls

### Existing Patterns to Follow

- See `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` for SSE streaming pattern
- See `.claude/patterns/ai/endpoint-registration.md` for AI endpoint setup
- See `src/client/pcf/PlaybookBuilderHost/` for existing canvas/store patterns

---

## Success Criteria

1. [ ] User can create a complete playbook through natural language conversation - Verify: End-to-end test with lease analysis playbook creation

2. [ ] Canvas updates in real-time as AI executes operations - Verify: Visual confirmation of immediate updates

3. [ ] All three test execution modes work correctly - Verify: Execute mock, quick, and production tests successfully

4. [ ] Scope ownership model enforced (SYS- immutable) - Verify: Attempt to edit SYS- scope returns error

5. [ ] Save As and Extend create correct scope records - Verify: Inspect Dataverse records for lineage fields

6. [ ] Builder-specific scopes (ACT/SKL/TL/KNW-BUILDER-*) created - Verify: Query Dataverse for all builder scopes

7. [ ] Rate limiting active and returns 429 appropriately - Verify: Exceed limit and confirm response

8. [ ] Dark mode supported with correct theming - Verify: Toggle dark mode, no hard-coded colors visible

9. [ ] PCF bundle under 5MB with platform libraries - Verify: Check built artifact size

10. [ ] All ADR constraints satisfied - Verify: Code review against ADR checklist

---

## Dependencies

### Prerequisites

- PlaybookBuilderHost PCF control exists and functional
- Playbook execution engine operational
- Dataverse scope entities deployed
- Azure OpenAI access configured

### External Dependencies

- Azure OpenAI (GPT-4o, GPT-4o-mini, o1-mini)
- Azure Blob Storage (for quick test temp files)
- Dataverse API access

---

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Rate limits | Per-user/session limits? | Moderate/high (~200 msgs/hr), don't block users | Configure generous rate limits |
| Conversation persistence | Persist across refresh? | Session-only, ephemeral | Client-side state only (Zustand) |
| Scope naming collision | Duplicate name handling? | Auto-prefix CUST- + suffix (1) for duplicates | Validation logic in scope creation |
| Knowledge file size | Max file size? | 50MB (same as test docs) | Consistent limits |
| Schema validation | Strict or flexible? | Strict validation | Clear error messages on failure |
| Concurrent editing | Optimistic locking? | Yes, if not overly complex | Use Dataverse versioning |

---

## Assumptions

*Proceeding with these assumptions (owner did not specify exact values):*

- **Rate limit values**: Assuming 200 messages/hour, 10 operations/request - adjustable via config
- **Clarification thresholds**: Assuming intent <75%, entity <80%, scope <70% - tunable
- **Session timeout**: Assuming standard browser session (tab close = session end)
- **Temp blob container**: Assuming `test-documents` container name

---

## Unresolved Questions

*No blocking questions remain. All critical items clarified.*

---

## Implementation Phases

| Phase | Focus | Effort |
|-------|-------|--------|
| Phase 1 | Infrastructure (BFF endpoint, Dataverse ops, conversational mode) | 1.5 weeks |
| Phase 2 | PCF Components (modal, chat, stores) | 1 week |
| Phase 3 | AI Integration + Builder Scopes | 1.5 weeks |
| Phase 4 | Test Execution (3 modes) | 1 week |
| Phase 5 | Scope Management | 1 week |
| Phase 6 | Polish | 0.5-1 week |

---

*AI-optimized specification. Original design: ai-chat-playbook-builder.md*

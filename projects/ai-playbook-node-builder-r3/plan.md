# AI Playbook Assistant Completion - Implementation Plan

> **Project**: ai-playbook-node-builder-r3
> **Created**: 2026-01-19
> **Status**: Planning Complete

---

## Executive Summary

Complete the AI Assistant implementation in the Playbook Builder by extending existing scaffolded services (`AiPlaybookBuilderService`, `ScopeResolverService`) to enable natural language playbook building with full scope CRUD, search, and test execution capabilities.

---

## Phase Breakdown

### Phase 1: Scope Management Backend (1 week)

**Objective**: Extend `IScopeResolverService` with full CRUD operations and Dataverse integration.

#### Deliverables

1. **Extend IScopeResolverService Interface**
   - Add `CreateActionAsync`, `UpdateActionAsync`, `DeleteActionAsync`
   - Add `CreateSkillAsync`, `UpdateSkillAsync`, `DeleteSkillAsync`
   - Add `CreateKnowledgeAsync`, `UpdateKnowledgeAsync`, `DeleteKnowledgeAsync`
   - Add `CreateToolAsync`, `UpdateToolAsync`, `DeleteToolAsync`
   - Add `SearchScopesAsync` for semantic search

2. **Implement Dataverse Operations in ScopeResolverService**
   - Replace stub data with real Dataverse queries
   - Implement create operations with ownership prefix logic
   - Implement update operations with immutability checks
   - Implement delete operations with ownership validation
   - Implement semantic search using existing Dataverse capabilities

3. **Add Scope Ownership Fields to Dataverse Schema**
   - Add `sprk_ownertype` (OptionSet: System=1, Customer=2)
   - Add `sprk_isimmutable` (Boolean)
   - Add `sprk_parentscope` (Lookup - self-reference for Extend)
   - Add `sprk_basedon` (Lookup - self-reference for Save As)
   - Create unmanaged solution with schema updates

4. **Implement Save As / Extend Functionality**
   - Save As: Copy scope with `basedon` reference
   - Extend: Create scope with `parentscope` reference

#### Success Criteria
- [ ] CRUD operations work for all 4 scope types
- [ ] SYS- scopes reject updates (immutability enforced)
- [ ] CUST- scopes accept updates
- [ ] Save As creates copy with lineage
- [ ] Extend creates child with parent reference

---

### Phase 2: AI Intent Enhancement (1 week)

**Objective**: Replace rule-based `ParseIntent()` with Azure OpenAI structured output.

#### Deliverables

1. **Design Intent Classification Schema**
   - Define structured output schema for intents
   - Include operation type, parameters, confidence score
   - Support clarification triggers

2. **Implement AI-Powered Intent Classification**
   - Use existing `IOpenAiClient` for AI calls
   - Implement structured output parsing
   - Add confidence thresholds (>0.8 proceed, <0.8 clarify)
   - Support intent types: build, modify, test, explain, search

3. **Add Simple Model Selection**
   - Add model parameter to AI operations
   - Support GPT-4o (complex) and GPT-4o-mini (simple)
   - Default to GPT-4o-mini for intent classification

4. **Implement Clarification Flow**
   - When confidence < threshold, generate clarification questions
   - Return options to frontend for user selection
   - Re-classify after user response

#### Success Criteria
- [ ] Intent classification uses Azure OpenAI
- [ ] Structured output parsed correctly
- [ ] Low-confidence triggers clarification
- [ ] Model selection works (4o vs 4o-mini)

---

### Phase 3: Builder Scopes & Meta-Playbook (0.5 week)

**Objective**: Create and deploy builder-specific scopes.

#### Deliverables

1. **Create Builder Scope Records**
   - 5 Actions (ACT-BUILDER-001 through 005)
   - 5 Skills (SKL-BUILDER-001 through 005)
   - 9 Tools (TL-BUILDER-001 through 009)
   - 4 Knowledge (KNW-BUILDER-001 through 004)

2. **Package as Unmanaged Solution**
   - Create solution with `Spaarke` publisher
   - Include all 23 scope records
   - Include schema updates from Phase 1

3. **Deploy to Development Environment**
   - Import unmanaged solution
   - Verify records created correctly
   - Validate ownership (all SYS-)

4. **Wire Builder to Use Own Scopes**
   - Update `AiPlaybookBuilderService` to load builder scopes
   - Use ACT-BUILDER-001 for intent classification prompt
   - Use SKL-BUILDER-004 for node type guidance

#### Success Criteria
- [ ] 23 builder scopes deployed
- [ ] All scopes have SYS- ownership
- [ ] Builder loads its own scopes
- [ ] Solution imports cleanly

---

### Phase 4: Test Execution Integration (1 week)

**Objective**: Wire test modes to `PlaybookOrchestrationService`.

#### Deliverables

1. **Create Dedicated Blob Container**
   - Create `playbook-test-documents` container
   - Configure 24-hour auto-cleanup policy
   - Add connection to BFF configuration

2. **Implement Mock Test Mode**
   - Generate sample data based on playbook inputs
   - Execute playbook without external calls
   - Return simulated outputs

3. **Implement Quick Test Mode**
   - Store test documents in dedicated container
   - Execute playbook with real processing
   - Clean up after execution

4. **Implement Production Test Mode**
   - Execute against real data
   - Full observability (logging, telemetry)
   - No cleanup (production data)

5. **Add Test Execution Endpoint**
   - Extend `AiPlaybookBuilderEndpoints.cs`
   - Add `POST /api/playbook-builder/test`
   - Accept mode parameter (mock, quick, production)

#### Success Criteria
- [ ] Mock mode returns simulated results
- [ ] Quick mode processes test documents
- [ ] Production mode executes with full observability
- [ ] Cleanup policy works for quick mode

---

### Phase 5: Frontend Enhancements (1 week)

**Objective**: Add scope browser, save as dialog, and test mode selection UI.

#### Deliverables

1. **Scope Browser Component**
   - Create `ScopeBrowser.tsx` in components/
   - Support filtering by type (Action, Skill, Knowledge, Tool)
   - Support search with debounce
   - Show scope details on selection
   - Fluent UI v9 styling

2. **Save As Dialog Component**
   - Create `SaveAsDialog.tsx` in components/
   - Accept scope to copy
   - Allow name editing
   - Show ownership (will be CUST-)
   - Confirm action

3. **Test Mode Selector Component**
   - Create `TestModeSelector.tsx` in components/
   - Show three options with descriptions
   - Mock: "Use sample data, no external calls"
   - Quick: "Upload test document, execute once"
   - Production: "Execute against real data"

4. **Enhanced Clarification UI**
   - Update clarification display with options
   - Support multiple-choice responses
   - Better formatting for complex clarifications

5. **Model Selection in AI Panel**
   - Add model dropdown (hidden by default, shown in advanced mode)
   - Options: GPT-4o (Powerful), GPT-4o-mini (Fast)
   - Save preference in store

#### Success Criteria
- [ ] Scope browser filters and searches correctly
- [ ] Save As creates CUST- copy
- [ ] Test mode selector shows all three options
- [ ] Clarification UI handles complex options
- [ ] Model selection persists

---

### Phase 6: Polish (0.5 week)

**Objective**: Error handling, performance optimization, documentation.

#### Deliverables

1. **Error Handling**
   - Add comprehensive error handling to all new code
   - User-friendly error messages
   - Retry logic for transient failures

2. **Performance Optimization**
   - Profile scope search performance
   - Add caching where appropriate
   - Optimize Dataverse queries

3. **Documentation Updates**
   - Update PLAYBOOK-BUILDER-FULLSCREEN-SETUP.md
   - Document new AI assistant capabilities
   - Add troubleshooting guide

4. **End-to-End Testing**
   - Test complete workflow: describe → build → test
   - Test scope reuse suggestions
   - Test all three test modes
   - Verify hybrid playbook creation

5. **Project Wrap-up**
   - Update README status to Complete
   - Create lessons-learned.md
   - Archive project artifacts

#### Success Criteria
- [ ] No unhandled errors in new code
- [ ] Scope search < 1s
- [ ] Documentation updated
- [ ] E2E tests pass

---

## Architecture Context

### Discovered Resources

#### Applicable ADRs

| ADR | Relevance |
|-----|-----------|
| ADR-001 | Minimal API pattern for test endpoint |
| ADR-006 | PCF over webresources |
| ADR-008 | Endpoint filters for auth |
| ADR-010 | DI minimalism - extend existing |
| ADR-013 | AI Tool Framework patterns |
| ADR-014 | AI Caching strategies |
| ADR-021 | Fluent UI v9 for all new UI |
| ADR-022 | PCF Platform Libraries, unmanaged solutions |

#### Applicable Patterns

| Pattern | Usage |
|---------|-------|
| `.claude/patterns/ai/analysis-scopes.md` | Scope management patterns |
| `.claude/patterns/ai/streaming-endpoints.md` | SSE streaming for AI |
| `.claude/patterns/api/endpoint-definition.md` | New endpoint structure |
| `.claude/patterns/dataverse/entity-operations.md` | Dataverse CRUD |
| `.claude/patterns/pcf/dialog-patterns.md` | PCF dialog components |

#### Applicable Knowledge Docs

| Document | Usage |
|----------|-------|
| `docs/guides/SPAARKE-AI-ARCHITECTURE.md` | AI framework reference |
| `docs/guides/PCF-DEPLOYMENT-GUIDE.md` | PCF deployment workflow |
| `docs/guides/PLAYBOOK-REAL-ESTATE-LEASE-ANALYSIS.md` | Example playbook |

#### Applicable Scripts

| Script | Usage |
|--------|-------|
| `Deploy-PCFWebResources.ps1` | PCF deployment |
| `Test-SdapBffApi.ps1` | API testing |

### Key Interfaces

```csharp
// Extended IScopeResolverService
public interface IScopeResolverService
{
    // Existing read operations...

    // New CRUD operations
    Task<AnalysisAction> CreateActionAsync(CreateActionRequest request, CancellationToken ct);
    Task<AnalysisAction> UpdateActionAsync(Guid id, UpdateActionRequest request, CancellationToken ct);
    Task DeleteActionAsync(Guid id, CancellationToken ct);

    // Similar for Skills, Knowledge, Tools...

    // Search
    Task<ScopeSearchResult> SearchScopesAsync(ScopeSearchQuery query, CancellationToken ct);
}
```

---

## Dependencies

### External Dependencies

| Dependency | Status | Required By |
|------------|--------|-------------|
| Dataverse schema update | Pending | Phase 1 |
| Blob container creation | Pending | Phase 4 |
| Builder scopes solution | Pending | Phase 3 |

### Internal Dependencies

| Phase | Depends On |
|-------|------------|
| Phase 2 | Phase 1 (scope CRUD for testing) |
| Phase 3 | Phase 1 (schema for ownership) |
| Phase 4 | Phase 2 (intent for test command) |
| Phase 5 | Phase 1-4 (backend complete) |
| Phase 6 | Phase 1-5 (all features) |

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Dataverse schema migration | Use additive fields only, no breaking changes |
| AI intent accuracy | Tune prompts, add examples, confidence thresholds |
| Token limits | Chunk context, summarize scope details |
| PCF bundle size | Use platform libraries per ADR-022 |

---

## References

- [spec.md](spec.md) - Full specification
- [design.md](design.md) - Design document
- [ai-chat-playbook-builder.md](../ai-playbook-node-builder-r2/ai-chat-playbook-builder.md) - Comprehensive design
- [AiPlaybookBuilderService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/AiPlaybookBuilderService.cs) - Existing service
- [ScopeResolverService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs) - Existing resolver

---

*Plan created: 2026-01-19*

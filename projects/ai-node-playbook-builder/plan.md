# AI Node-Based Playbook Builder - Implementation Plan

> **Version**: 1.0
> **Created**: 2026-01-08
> **Source**: spec.md

---

## Architecture Context

### Discovered Resources

**Applicable ADRs:**
| ADR | Summary | Key Constraint |
|-----|---------|----------------|
| [ADR-001](.claude/adr/ADR-001-minimal-api.md) | Minimal API + BackgroundService | No Azure Functions |
| [ADR-008](.claude/adr/ADR-008-endpoint-filters.md) | Endpoint filters for auth | No global auth middleware |
| [ADR-009](.claude/adr/ADR-009-redis-caching.md) | Redis-first caching | No hybrid L1 cache |
| [ADR-010](.claude/adr/ADR-010-di-minimalism.md) | DI minimalism | ≤15 non-framework registrations |
| [ADR-013](.claude/adr/ADR-013-ai-architecture.md) | AI architecture | Extend BFF, not separate service |
| [ADR-022](.claude/adr/ADR-022-pcf-platform-libraries.md) | PCF platform libraries | React 16 APIs only in PCF |

**Relevant Patterns:**
- `.claude/patterns/ai/streaming-endpoints.md` - SSE streaming for execution progress
- `.claude/patterns/api/endpoint-definition.md` - Minimal API endpoint structure
- `.claude/patterns/api/endpoint-filters.md` - Authorization filter implementation
- `.claude/patterns/api/service-registration.md` - DI module pattern

**Canonical Implementations:**
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` - Orchestration pattern
- `src/server/api/Sprk.Bff.Api/Api/Ai/PlaybookEndpoints.cs` - Existing playbook CRUD
- `src/server/api/Sprk.Bff.Api/Api/Filters/PlaybookAuthorizationFilter.cs` - Authorization

**Applicable Constraints:**
- `.claude/constraints/api.md` - API development rules
- `.claude/constraints/ai.md` - AI feature rules
- `.claude/constraints/pcf.md` - PCF development rules
- `.claude/constraints/testing.md` - Testing requirements

---

## Phase Breakdown

### Phase 1: Foundation (Tasks 001-009)

**Objective**: Multi-node playbooks via API with sequential execution

**Deliverables:**

#### 1.1 Dataverse Schema
- Extended `sprk_analysisplaybook` entity with new fields (`playbookmode`, `playbooktype`, `canvaslayoutjson`, etc.)
- New `sprk_playbooknode` entity with action, tool, output variable, dependencies
- N:N relationships: `sprk_playbooknode_skill`, `sprk_playbooknode_knowledge`
- Extended `sprk_analysisaction` with `actiontype`, `outputschemajson`
- Extended `sprk_analysistool` with `outputschemajson`
- New `sprk_aimodeldeployment` entity
- New `sprk_deliverytemplate` entity
- New `sprk_playbookrun` entity (execution tracking)
- New `sprk_playbooknoderun` entity (node execution tracking)

#### 1.2 Core Services
- `INodeService` / `NodeService` - Node CRUD operations
- Extended `IScopeResolverService.ResolveNodeScopesAsync()` - Node-level scope resolution
- `ExecutionGraph` - Topological sort, dependency resolution
- `PlaybookRunContext` - Shared state across nodes
- `NodeExecutionContext` - Single node execution context

#### 1.3 Node Executors
- `INodeExecutor` interface with `ExecuteAsync()`
- `INodeExecutorRegistry` for executor lookup by action type
- `AiAnalysisNodeExecutor` - Bridges to existing `IAnalysisToolHandler`

#### 1.4 Orchestration
- `IPlaybookOrchestrationService` / `PlaybookOrchestrationService`
- Mode check: Legacy vs NodeBased
- Sequential batch execution
- Basic validation (no cycles, required fields)

#### 1.5 API Endpoints
- `GET /api/ai/playbooks/{id}/nodes` - List nodes
- `POST /api/ai/playbooks/{id}/nodes` - Add node
- `PUT /api/ai/playbooks/{id}/nodes/{nodeId}` - Update node
- `DELETE /api/ai/playbooks/{id}/nodes/{nodeId}` - Delete node
- `PUT /api/ai/playbooks/{id}/nodes/reorder` - Reorder nodes
- `PUT /api/ai/playbooks/{id}/nodes/{nodeId}/scopes` - Update node scopes
- `POST /api/ai/playbooks/{id}/validate` - Validate playbook graph
- `POST /api/ai/playbooks/{id}/execute` - Start execution
- `GET /api/ai/playbooks/runs/{runId}` - Get run status
- `GET /api/ai/playbooks/runs/{runId}/stream` - Stream progress (SSE)

#### 1.6 Testing & Deployment
- Unit tests for services
- Integration tests for API endpoints
- Deploy to dev environment

---

### Phase 2: Visual Builder (Tasks 010-019)

**Objective**: Visual drag-and-drop playbook builder

**Deliverables:**

#### 2.1 React 18 Builder App
- Project setup: `src/client/playbook-builder/`
- React Flow integration for canvas
- Zustand state management
- API client service

#### 2.2 Builder Components
- `Canvas/` - React Flow wrapper with node/edge management
- `Nodes/` - Custom node components by action type
- `Edges/` - Custom edge components for data flow
- `Palette/` - Draggable node types
- `Properties/` - Node configuration panel
- `Toolbar/` - Actions, validation, save

#### 2.3 Scope Selector
- Query action compatibility settings
- Filter dropdowns by compatible items
- Show/hide sections based on action type

#### 2.4 PCF Host Control
- `PlaybookBuilderHost` PCF control
- Iframe embedding with React 16 APIs
- PostMessage communication protocol
- Token refresh handling

#### 2.5 Canvas Persistence
- `GET /api/ai/playbooks/{id}/canvas` - Get layout
- `PUT /api/ai/playbooks/{id}/canvas` - Save layout

#### 2.6 Testing & Deployment
- Component tests for builder
- E2E tests for builder-host communication
- Deploy builder to App Service wwwroot
- Deploy PCF to dev environment

---

### Phase 3: Parallel Execution + Delivery (Tasks 020-029)

**Objective**: Full orchestration with parallel execution and delivery outputs

**Deliverables:**

#### 3.1 Parallel Execution
- Batch execution respecting dependencies
- `maxParallelNodes` throttling
- Rate limit handling with exponential backoff

#### 3.2 Template Engine
- `ITemplateEngine` interface
- Handlebars.NET implementation
- Variable resolution from `PlaybookRunContext`

#### 3.3 Delivery Node Executors
- `CreateTaskNodeExecutor` - Create Dataverse task
- `SendEmailNodeExecutor` - Send via Microsoft Graph
- `UpdateRecordNodeExecutor` - Update Dataverse entity
- `DeliverOutputNodeExecutor` - Render and deliver final output

#### 3.4 Power Apps Integration
- Word template integration
- Email template integration
- Placeholder mapping from AI outputs

#### 3.5 Execution Visualization
- Real-time progress overlay in builder
- Node status indicators (pending, running, completed, failed)
- Token usage display

#### 3.6 Testing & Deployment
- Integration tests for parallel execution
- E2E tests for delivery
- Deploy to dev environment

---

### Phase 4: Advanced Features (Tasks 030-039)

**Objective**: Conditional branching, confidence visibility, templates

**Deliverables:**

#### 4.1 Condition Node
- `ConditionNodeExecutor` for if/else branching
- JSON-based expression syntax
- Branch path visualization in builder

#### 4.2 Model Selection
- Per-node AI model selection UI
- `GET /api/ai/model-deployments` endpoint
- Model override in `NodeExecutionContext`

#### 4.3 Confidence Scores
- Update tool handler prompts for confidence
- Confidence badges in UI (green/yellow/red)
- Source citations with click-to-highlight

#### 4.4 Playbook Templates
- Standard product playbooks
- Clone-and-customize workflow
- Template library UI

#### 4.5 Execution History
- Execution history list
- Run details with node metrics
- Token usage analytics

#### 4.6 Testing & Deployment
- Tests for conditional branching
- Tests for templates
- Deploy to dev environment

---

### Phase 5: Production Hardening (Tasks 040-049)

**Objective**: Production-ready system

**Deliverables:**

#### 5.1 Error Handling
- Comprehensive try/catch with ProblemDetails
- Retry logic with exponential backoff
- Circuit breaker integration

#### 5.2 Timeout Management
- Per-node timeout configuration
- Cancellation token propagation
- Graceful timeout handling

#### 5.3 Cancellation Support
- `POST /api/ai/playbooks/runs/{runId}/cancel`
- In-progress node cancellation
- UI cancel button

#### 5.4 Audit Logging
- Structured logging for all operations
- Execution audit trail
- Error logging with correlation IDs

#### 5.5 Performance Optimization
- Redis caching for resolved scopes
- Execution graph caching
- Document text caching (shared across nodes)

#### 5.6 Final Testing & Documentation
- Load testing
- Security review
- User documentation

---

### Phase 6: Project Wrap-up (Task 090)

**Objective**: Clean project completion

**Deliverables:**
- Update README status to Complete
- Create lessons-learned.md
- Archive ephemeral files
- Run `/repo-cleanup`

---

## Dependencies Graph

```
Phase 1 (Foundation)
    │
    ├── 001: Schema design
    │     └── 002: Schema implementation
    │           └── 003: NodeService
    │                 └── 004: ScopeResolver extension
    │                       └── 005: ExecutionGraph
    │                             └── 006: AiAnalysisNodeExecutor
    │                                   └── 007: PlaybookOrchestrationService
    │                                         └── 008: API endpoints
    │                                               └── 009: Tests + deploy
    │
Phase 2 (Visual Builder) ─── depends on Phase 1 complete
    │
    ├── 010: Builder project setup
    │     └── 011: React Flow canvas
    │           └── 012: Custom nodes
    │                 └── 013: Properties panel
    │                       └── 014: Scope selector
    │                             └── 015: PCF host control
    │                                   └── 016: Host-builder comm
    │                                         └── 017: Canvas persistence
    │                                               └── 018: Builder deploy
    │                                                     └── 019: PCF deploy + tests
    │
Phase 3 (Parallel + Delivery) ─── depends on Phase 2 complete
    │
Phase 4 (Advanced) ─── depends on Phase 3 complete
    │
Phase 5 (Hardening) ─── depends on Phase 4 complete
    │
Phase 6 (Wrap-up) ─── depends on Phase 5 complete
```

---

## Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|------------|
| React Flow + React 16 incompatibility | High | Iframe isolation pattern validated |
| DI budget exceeded | Medium | Track registrations, use modules |
| Azure OpenAI rate limits | Medium | Existing exponential backoff pattern |
| Complex dependency resolution | Medium | Topological sort with cycle detection |
| Performance with many nodes | Low | Document text shared, caching |

---

## References

- [spec.md](spec.md) - Full implementation specification
- [design.md](design.md) - Original design document
- [reference/](reference/) - Design review, wireframes, prior designs

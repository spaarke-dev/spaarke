# AI Node-Based Playbook Builder - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-01-08
> **Source**: design.md (v1.0)

## Executive Summary

Transform Spaarke's current single-action playbook model into a **multi-node orchestration platform** that enables chaining multiple AI analysis actions with data flow between them, mixing AI and deterministic actions, visual drag-and-drop playbook construction for business analysts, and flexible delivery outputs. The system extends existing analysis pipeline components without replacement, maintaining full backward compatibility with legacy playbooks.

## Scope

### In Scope

**Core Platform (P1)**
- Multi-node playbook data model in Dataverse (`sprk_playbooknode`, `sprk_aimodeldeployment`, `sprk_deliverytemplate`, `sprk_playbookrun`, `sprk_playbooknoderun`)
- Extended playbook entity with `playbookmode` (Legacy/NodeBased) for backward compatibility
- Node orchestration service with sequential execution and dependency resolution
- API endpoints for playbook/node CRUD, validation, execution, and streaming progress
- `AiAnalysisNodeExecutor` bridging to existing `IAnalysisToolHandler` pipeline
- Template engine (Handlebars.NET) for variable substitution between nodes

**Visual Builder (P2)**
- React 18 playbook-builder web app with React Flow canvas
- Iframe embedding pattern to isolate from PCF React 16 constraint
- `PlaybookBuilderHost` PCF control for model-driven app integration
- Host-builder postMessage communication protocol
- Node palette, properties panel, scope selector with filtering

**Execution & Delivery (P3)**
- Parallel node execution with throttling (`maxParallelNodes`)
- Delivery node executors: `CreateTaskNodeExecutor`, `SendEmailNodeExecutor`, `DeliverOutputNodeExecutor`
- Power Apps template integration for Word documents and emails
- Execution visualization overlay in builder

**Advanced Features (P4)**
- `ConditionNodeExecutor` for if/else branching
- Per-node AI model selection UI
- Confidence score display with color badges
- Playbook templates library (standard product playbooks, customizable per tenant)
- Execution history and metrics dashboard

**Production Hardening (P5)**
- Comprehensive error handling and retry logic
- Timeout management and cancellation support
- Audit logging
- Performance optimization

### Out of Scope

- Mobile-native applications
- Third-party marketplace for playbooks
- Playbook versioning with history/rollback (version field stubbed only)
- Cross-tenant playbook sharing (tenant isolation maintained)
- Custom template editor (Phase 1 uses Power Apps templates)
- Azure Functions or separate microservices (all within BFF API per ADR-001)

### Affected Areas

**Backend (BFF API)**
- `src/server/api/Sprk.Bff.Api/Services/Ai/` - New orchestration layer, node executors
- `src/server/api/Sprk.Bff.Api/Endpoints/` - New playbook/node API endpoints
- `src/server/api/Sprk.Bff.Api/Models/` - New DTOs for nodes, runs, execution

**Frontend (PCF)**
- `src/client/pcf/` - New `PlaybookBuilderHost` control

**Frontend (Standalone)**
- `src/client/playbook-builder/` - New React 18 application (served from App Service)

**Dataverse**
- `src/solutions/` - Schema changes, new entities, extended entities

## Requirements

### Functional Requirements

1. **FR-01**: Visual playbook builder for non-developers with drag-and-drop node placement
   - Acceptance: Business analyst can create 5-node playbook in <10 minutes without coding

2. **FR-02**: Multi-node AI analysis with data flow between nodes using template variables
   - Acceptance: Node 2 can reference `{{node1Output.parties}}` from Node 1's output

3. **FR-03**: Multiple delivery output types (Document, Email, Record, Teams)
   - Acceptance: Single playbook can generate Word doc AND send email from same execution

4. **FR-04**: Per-node AI model selection for cost/quality optimization
   - Acceptance: User can select GPT-4o for complex analysis, GPT-4o-mini for simple tasks

5. **FR-05**: Backward compatibility with existing single-action playbooks
   - Acceptance: 100% of existing playbooks execute unchanged (Legacy mode)

6. **FR-06**: Real-time execution progress streaming via SSE
   - Acceptance: UI shows node-by-node progress with <500ms latency

7. **FR-07**: Playbook validation before execution
   - Acceptance: Circular dependencies, missing required fields detected before run

8. **FR-08**: Playbook-level failure handling with clear error messages
   - Acceptance: If any node fails, execution stops and shows actionable error to fix/rerun

9. **FR-09**: Standard product playbooks customizable per tenant
   - Acceptance: Admin can deploy standard playbook template, tenant can customize nodes

### Non-Functional Requirements

- **NFR-01**: Playbook creation time <10 minutes for 5-node playbook
- **NFR-02**: Execution latency <60 seconds for 5-node sequential playbook
- **NFR-03**: UI drag/drop responsiveness <100ms
- **NFR-04**: Support concurrent playbook executions (respecting `maxParallelNodes` per playbook)
- **NFR-05**: Full test coverage: unit tests for services, integration tests for API, E2E deployment to dev environment

## Technical Constraints

### Applicable ADRs

- **ADR-001**: Minimal API + BackgroundService - All new endpoints use Minimal API pattern, no Azure Functions
- **ADR-008**: Endpoint filters for authorization - `PlaybookAuthorizationFilter` on all endpoints
- **ADR-009**: Redis-first caching - Cache resolved scopes, execution graphs
- **ADR-010**: DI minimalism (≤15 non-framework registrations) - ~10 new services within budget
- **ADR-013**: Extend BFF, not separate service - All orchestration within `Sprk.Bff.Api`
- **ADR-022**: React 16 for PCF - Iframe pattern isolates React 18 builder from PCF host

### MUST Rules

- ✅ MUST use Minimal API pattern for all new endpoints
- ✅ MUST use endpoint filters for authorization (not global middleware)
- ✅ MUST maintain ≤15 non-framework DI registrations
- ✅ MUST keep PCF host control compatible with React 16 APIs
- ✅ MUST preserve 100% backward compatibility with Legacy mode playbooks
- ✅ MUST use Handlebars.NET for template rendering (logic-less, secure)
- ✅ MUST implement playbook-level failure (any node failure stops execution)
- ❌ MUST NOT create Azure Functions or separate microservices
- ❌ MUST NOT use React 18 APIs in PCF control (use iframe isolation)
- ❌ MUST NOT leak Graph SDK types above facade layer

### Existing Patterns to Follow

- See `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` for existing orchestration pattern
- See `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/` for `IAnalysisToolHandler` implementations
- See `src/server/api/Sprk.Bff.Api/Endpoints/AiToolEndpoints.cs` for streaming SSE pattern
- See `.claude/patterns/api/` for endpoint definition patterns

## Success Criteria

1. [ ] Business analyst creates 5-node playbook in <10 minutes - Verify: Timed user test
2. [ ] 5-node playbook executes in <60 seconds - Verify: Performance test
3. [ ] Drag/drop operations respond in <100ms - Verify: UI performance profiling
4. [ ] 100% existing playbooks work unchanged - Verify: Regression test suite
5. [ ] All API endpoints have authorization filters - Verify: Code review
6. [ ] DI registrations ≤15 non-framework - Verify: Startup.cs audit
7. [ ] Full test coverage deployed to dev environment - Verify: CI/CD pipeline green

## Dependencies

### Prerequisites

- R4 Complete: Playbook Scope System (existing N:N relationships, scope resolution)
- R5 Design: RAG Pipeline (knowledge sources for nodes)
- Existing `IAnalysisToolHandler` infrastructure (8 tool handlers)
- Existing `OpenAiClient`, `ScopeResolverService`, `AnalysisContextBuilder`

### External Dependencies

- Azure OpenAI API (for AI node execution)
- Microsoft Graph API (for email delivery, Teams messages)
- Power Apps Word Templates (for document delivery - Phase 1)
- Dataverse SDK (for record operations)

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Out of Scope | What is explicitly OUT of scope? | No mobile-native, no third-party marketplace; playbooks can be standard product with custom per tenant | Added standard product playbooks to scope, excluded mobile/marketplace |
| Testing Strategy | What testing approach? | Full testing including deployment to dev environment | NFR-05 requires unit + integration + E2E deployment |
| Versioning | Version history behavior? | No versioning this release - just stub the field | Removed versioning features from scope, `sprk_version` field exists but unused |
| Failure Handling | Parallel node failure behavior? | Playbook-level failure - any node fails = show error to fix/rerun | Simplified execution engine - no partial completion, clear error UX |
| Builder Deployment | Where to deploy React app? | Same Azure App Service as BFF API | Builder served from `wwwroot/playbook-builder/`, no separate infrastructure |

## Assumptions

*Proceeding with these assumptions (owner did not specify):*

- **Authentication**: Builder iframe uses same OBO token flow as host PCF control
- **Rate Limiting**: Azure OpenAI rate limits handled with exponential backoff (existing pattern)
- **Canvas Persistence**: `sprk_canvaslayoutjson` stores React Flow viewport and node positions
- **Condition Syntax**: Condition nodes use simple JSON-based expression syntax (not full scripting)

## Unresolved Questions

*No blocking questions remaining - all critical items clarified.*

## Implementation Phases

### Phase 1: Foundation
- Dataverse schema (all entities/fields)
- `INodeService`, `NodeService`, `ExecutionGraph`
- Extended `IScopeResolverService` with node scope resolution
- `AiAnalysisNodeExecutor` bridging to existing pipeline
- `PlaybookOrchestrationService` with sequential execution
- Node management API endpoints
- Basic validation
- **Outcome**: Multi-node playbooks via API, sequential execution

### Phase 2: Visual Builder
- React 18 playbook-builder app with React Flow
- Node palette, properties panel, canvas controls
- Scope selector with filtering
- `PlaybookBuilderHost` PCF with iframe
- Host-builder postMessage communication
- Output schema validation in builder
- **Outcome**: Visual drag-and-drop builder

### Phase 3: Parallel Execution + Delivery
- Parallel node execution with throttling
- `CreateTaskNodeExecutor`, `SendEmailNodeExecutor`, `DeliverOutputNodeExecutor`
- Power Apps template integration
- `ITemplateEngine` (Handlebars.NET)
- Execution visualization overlay
- **Outcome**: Full orchestration with delivery outputs

### Phase 4: Advanced Features
- `ConditionNodeExecutor` for branching
- Per-node model selection UI
- Confidence score display
- Playbook templates library (standard product)
- Execution history and metrics
- **Outcome**: Conditional branching, confidence visibility, templates

### Phase 5: Production Hardening
- Comprehensive error handling and retry
- Timeout management
- Cancellation support
- Audit logging
- Performance optimization
- **Outcome**: Production-ready system

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| One action per node | Atomic, clear purpose |
| Single tool per node | Avoids execution ambiguity |
| Multiple skills per node | Skills are prompt modifiers that compose well |
| Multiple knowledge per node | Multiple context sources are legitimate |
| Dataverse as system of record | POML for export/import only |
| Handlebars.NET templates | Logic-less engine, no code execution, minimal attack surface |
| Iframe for builder | Isolates React 18 from PCF React 16 constraint |
| Playbook-level failure | Simpler UX - fix and rerun vs. partial completion |

---

*AI-optimized specification. Original design: design.md*

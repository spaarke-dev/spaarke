# AI Document Intelligence - Analysis Feature (R1)

> **Status**: Implementation Phase  
> **Version**: 1.0  
> **Start Date**: December 11, 2025  
> **Target Completion**: February 21, 2026 (10 weeks)

---

## Overview

The Analysis feature enables users to execute AI-driven analyses on documents with configurable actions (what to do), scopes (Skills, Knowledge, Tools for how/with what), and multi-format outputs. Users can refine analyses through conversational AI in an interactive two-column workspace.

**Key Capabilities:**
- **Configurable AI Workflows**: Actions + Skills + Knowledge + Tools
- **Interactive Workspace**: Two-column layout (editable output + source preview)
- **Conversational Refinement**: Chat-based analysis improvement
- **Playbook System**: Reusable analysis configurations
- **Multi-Format Export**: Document, Email, Teams, Workflows
- **Multi-Tenant Ready**: Full parameterization via Environment Variables

**Technology Stack:**
- Azure AI Foundry (Prompt Flow orchestration)
- Azure OpenAI (GPT-4o-mini)
- Azure AI Search (Hybrid RAG)
- ASP.NET Core 8 Minimal APIs
- Power Apps Custom Pages
- PCF Controls (React + TypeScript)
- Dataverse entities
- SharePoint Embedded (SPE)

---

## Project Structure

```
ai-document-intelligence-r1/
├── SPEC.md                     # Complete design specification
├── PLAN.md                     # Implementation plan with phases
├── README.md                   # This file
├── tasks/
│   ├── TASK-INDEX.md          # Task breakdown (178 tasks)
│   └── *.poml                 # Individual task files (to be created)
├── docs/
│   ├── deployment-guide-phase1.md
│   ├── ui-guide.md
│   ├── performance-report.md
│   └── production-runbook.md
└── notes/
    └── handoffs/               # Session handoffs (if context > 70%)
```

---

## Implementation Phases

### ✅ Phase 0: Planning (Complete)
- [x] Design specification created
- [x] Implementation plan created
- [x] Task breakdown completed (178 tasks)

### 🔄 Phase 1: Core Infrastructure (Week 1-2)
**Goal**: Multi-tenant parameterization, Azure AI Foundry, Dataverse entities, BFF API

**Status**: 🔲 Not Started (0/37 tasks)

**Key Deliverables**:
- 15 Environment Variables in Dataverse solution
- Bicep parameter templates for customer deployment
- Azure AI Foundry Hub + 2 Prompt Flows
- 8 new Dataverse entities with relationships
- BFF API: 4 endpoints with SSE streaming
- BFF API: 4 new services (Orchestration, Scope, Context, WorkingDoc)

### 🔲 Phase 2: UI Components (Week 3-4)
**Goal**: Analysis Builder, Analysis Workspace, PCF controls

**Status**: ⏸️ Blocked by Phase 1 (0/30 tasks)

**Key Deliverables**:
- Document form customization (Analysis tab)
- Analysis Builder custom page
- Analysis Workspace custom page with two-column layout
- AnalysisWorkspace PCF control (React)
- SSE streaming in custom page

### 🔲 Phase 3: Scope System & Hybrid RAG (Week 5-6)
**Goal**: Admin UI, RAG infrastructure, tool framework, seed data

**Status**: ⏸️ Blocked by Phase 1 (0/30 tasks)

**Key Deliverables**:
- Model-driven forms for Actions, Skills, Knowledge, Tools
- Azure AI Search with hybrid deployment models
- RAG service with cross-tenant auth
- Tool handler framework with 3 sample tools
- Seed data (5 Actions, 10 Skills, 5 Knowledge sources)

### 🔲 Phase 4: Playbooks & Export (Week 7-8)
**Goal**: Reusable configurations, multi-format export, integrations

**Status**: ⏸️ Blocked by Phase 3 (0/32 tasks)

**Key Deliverables**:
- Playbook management with sharing
- Export to DOCX/PDF
- Email integration (Power Apps email entity)
- Teams integration (Graph API)
- Workflow trigger infrastructure

### 🔲 Phase 5: Production Readiness (Week 9-10)
**Goal**: Optimization, monitoring, production deployment, documentation

**Status**: ⏸️ Blocked by Phase 1-4 (0/49 tasks)

**Key Deliverables**:
- Performance optimization (caching, compression)
- Azure AI Foundry evaluation pipeline
- Monitoring dashboards and alerts
- Security review and penetration test
- Production deployment
- **Customer deployment guide (validated externally)**
- User and admin documentation

---

## Quick Start for Developers

### Prerequisites
- Visual Studio 2022 or VS Code
- .NET 8 SDK
- Node.js 18+ (for PCF development)
- Power Platform CLI (`pac`)
- Azure CLI
- Access to Spaarke dev environment

### Get Started

1. **Review Documentation**
   ```bash
   # Read design spec and plan
   cat SPEC.md
   cat PLAN.md
   cat tasks/TASK-INDEX.md
   ```

2. **Load AI Execution Protocol** (for AI agents)
   ```
   Always load: docs/reference/procedures/04-ai-execution-protocol.md
   ```

3. **Execute Tasks**
   ```
   # For AI agent (Claude Code)
   Execute task: projects/ai-document-intelligence-r1/tasks/001-create-environment-variables.poml
   ```

4. **Run Tests**
   ```bash
   # Unit tests
   dotnet test
   
   # Integration tests
   dotnet test --filter Category=Integration
   ```

### Development Workflow

1. **Pick a task** from `tasks/TASK-INDEX.md`
2. **Read the .poml file** for detailed requirements
3. **Follow ADR constraints** (check `docs/adr/`)
4. **Write tests** alongside code
5. **Update TASK-INDEX.md** status when complete
6. **Create handoff** if context > 70%

---

## Key Resources

### Architecture Documentation
- [SPEC.md](SPEC.md) - Complete design specification
- [PLAN.md](PLAN.md) - Implementation plan
- [sdap-bff-api-patterns.md](../../docs/ai-knowledge/architecture/sdap-bff-api-patterns.md) - BFF API patterns
- [power-apps-custom-pages.md](../../docs/ai-knowledge/reference/power-apps-custom-pages.md) - Custom Pages guide
- [pcf-component-patterns.md](../../docs/ai-knowledge/reference/pcf-component-patterns.md) - PCF patterns

### ADRs (Must Follow)
- ADR-001: Minimal APIs
- ADR-003: Lean Authorization
- ADR-007: SpeFileStore Facade
- ADR-008: Endpoint Filters
- ADR-009: Redis Caching
- ADR-010: Static Methods
- ADR-013: AI Architecture

### Procedures
- [04-ai-execution-protocol.md](../../docs/reference/procedures/04-ai-execution-protocol.md) - AI task execution
- [05-poml-reference.md](../../docs/reference/procedures/05-poml-reference.md) - POML task format
- [06-context-engineering.md](../../docs/reference/procedures/06-context-engineering.md) - Context management

---

## Success Criteria

### Technical Success (Phase 1-5)
- [ ] All 178 tasks completed
- [ ] 80%+ unit test coverage
- [ ] All integration tests passing
- [ ] Zero hard-coded tenant-specific values
- [ ] Solution deploys to 3 external test tenants
- [ ] Load testing passes (100+ concurrent users)
- [ ] P95 latency < 2s for SSE stream start
- [ ] Security review completed (no critical issues)

### Business Success (Post-Launch)
- [ ] 50% of documents have ≥1 analysis within 30 days
- [ ] 80% of analyses reach "Completed" status
- [ ] Token costs ≤ $0.10/document
- [ ] User satisfaction > 4/5
- [ ] No critical security incidents

### Multi-Tenant Readiness
- [ ] Customer deployment guide validated by 2 external users
- [ ] Solution upgrade tested (1.0 → 1.1 managed mode)
- [ ] Bicep deploys to 2 different Azure subscriptions
- [ ] All configuration via Environment Variables

---

## Contact & Support

**Project Owner**: Spaarke Product Team  
**Tech Lead**: TBD  
**Documentation**: This folder + `docs/`  
**Issues**: Track in Azure DevOps or GitHub  

---

## Change Log

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | Dec 11, 2025 | Initial project setup, planning complete |

---

**Status**: Ready for Implementation  
**Next Action**: Begin Phase 1, Task 001 - Create Environment Variables

# AI Document Intelligence R4 - Project Context

> **For Claude Code**: Load this file when working on R4 tasks.

---

## Project Summary

Implement the Playbook scope system for no-code AI workflow composition. Domain experts assemble Actions, Skills, Knowledge, and Tools into Playbooks without engineering.

**Key Constraint**: Extend existing R3 infrastructure. DO NOT recreate existing services.

---

## Applicable ADRs

| ADR | Key Constraint |
|-----|----------------|
| **ADR-013** | Tool handlers in `Services/Ai/Tools/`, implement `IAnalysisToolHandler` |
| **ADR-001** | Minimal API pattern for scope endpoints |
| **ADR-006** | PCF over webresources - no legacy JS |
| **ADR-008** | Authorization via endpoint filters, not middleware |
| **ADR-010** | DI minimalism - focused services |
| **ADR-021** | Fluent UI v9, dark mode, semantic tokens |

---

## MUST Rules

- ✅ MUST implement tool handlers as `IAnalysisToolHandler`
- ✅ MUST store all scope configurations in Dataverse entities
- ✅ MUST use existing `ToolHandlerRegistry` for handler discovery
- ✅ MUST follow existing endpoint patterns from `PlaybookEndpoints.cs`
- ✅ MUST use Fluent UI v9 for all PCF UI changes
- ✅ MUST support dark mode in PCF enhancements

---

## MUST NOT Rules

- ❌ MUST NOT recreate `PlaybookService.cs`
- ❌ MUST NOT recreate `ScopeResolverService.cs`
- ❌ MUST NOT recreate `ToolHandlerRegistry.cs`
- ❌ MUST NOT add business logic to ribbon/command bar scripts
- ❌ MUST NOT hard-code colors in PCF controls

---

## Existing Code References

### DO NOT RECREATE

| File | Purpose | Path |
|------|---------|------|
| PlaybookService | Playbook CRUD | `Services/Ai/PlaybookService.cs` |
| ScopeResolverService | Load scopes | `Services/Ai/ScopeResolverService.cs` |
| ToolHandlerRegistry | Handler discovery | `Services/Ai/ToolHandlerRegistry.cs` |
| EntityExtractorHandler | Entity extraction | `Services/Ai/Tools/EntityExtractorHandler.cs` |
| ClauseAnalyzerHandler | Clause analysis | `Services/Ai/Tools/ClauseAnalyzerHandler.cs` |
| DocumentClassifierHandler | Classification | `Services/Ai/Tools/DocumentClassifierHandler.cs` |

### Pattern References

| Pattern | File |
|---------|------|
| Tool Handler | `Services/Ai/Tools/EntityExtractorHandler.cs` |
| API Endpoints | `Api/Ai/PlaybookEndpoints.cs` |
| Handler Tests | `Tests/Services/Ai/EntityExtractorHandlerTests.cs` |
| PCF Control | `src/client/pcf/AnalysisWorkspace/` |

---

## New Tool Handlers to Implement

| Handler | Purpose | Priority |
|---------|---------|----------|
| SummaryHandler | Document summarization | P0 |
| RiskDetectorHandler | Risk identification | P0 |
| ClauseComparisonHandler | Compare to standard terms | P0 |
| DateExtractorHandler | Date extraction/normalization | P0 |
| FinancialCalculatorHandler | Financial calculations | P0 |

---

## Key Entities

| Entity | Logical Name | Purpose |
|--------|--------------|---------|
| Playbook | sprk_analysisplaybook | Playbook definitions |
| Action | sprk_analysisaction | System prompts + handler refs |
| Skill | sprk_analysisskill | Prompt fragments |
| Knowledge | sprk_analysisknowledge | Inline/RAG content |
| Tool | sprk_analysistool | Handler configurations |

---

## API Endpoints to Add

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/ai/scopes/skills` | GET | List skills (paginated) |
| `/api/ai/scopes/knowledge` | GET | List knowledge (paginated) |
| `/api/ai/scopes/tools` | GET | List tools (paginated) |
| `/api/ai/scopes/actions` | GET | List actions (paginated) |

---

## Current Focus

Check `current-task.md` for active task and progress.

---

## Project Context

- **Lineage**: R1 → R2 → R3 → R4
- **R3 Status**: Complete (RAG, Playbooks, Export, Monitoring, Security)
- **R4 Focus**: Scope implementation for no-code composition

---

*Load this file at the start of each R4 session.*

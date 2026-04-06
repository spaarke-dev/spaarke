# Scope Architecture

> **Last Updated**: 2026-04-05
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: New
> **Purpose**: Describes the scope management subsystem — scope types, resolution chain, inheritance model, ownership validation, gap detection, and fallback catalog.

---

## Overview

Scopes are the composable building blocks of the AI analysis system. A **playbook** references one or more scopes to define what an analysis can do. The scope system provides four types (Actions, Skills, Knowledge, Tools) with a resolution chain that loads scope definitions from Dataverse, a single-level inheritance model for customization, an ownership model (SYS- immutable, CUST- editable), and gap detection for proactive scope suggestions during playbook creation.

The key design decision is the **SYS-/CUST- ownership split**: system-provided scopes are immutable and serve as templates, while customer scopes created via "Save As" or "Extend" are fully editable. This ensures platform upgrades never break customer customizations.

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| IScopeResolverService | `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs` | Resolution contracts: by explicit IDs, by playbook, by node; CRUD list operations |
| ScopeResolverService | `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` | Resolution orchestrator: delegates to focused entity services (ActionService, SkillService, KnowledgeService, ToolService) |
| IScopeManagementService | `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeManagementService.cs` | CRUD contracts for all scope types with ownership validation |
| ScopeManagementService | `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeManagementService.cs` | CRUD operations with SYS-/CUST- prefix enforcement and duplicate name handling |
| ScopeInheritanceService | `src/server/api/Sprk.Bff.Api/Services/Scopes/ScopeInheritanceService.cs` | Single-level inheritance: extend parent scopes, merge effective values, handle parent deletion |
| ScopeCopyService | `src/server/api/Sprk.Bff.Api/Services/Scopes/ScopeCopyService.cs` | "Save As" for playbooks and scopes: deep copy with CUST- prefix, duplicate name handling |
| OwnershipValidator | `src/server/api/Sprk.Bff.Api/Services/Scopes/OwnershipValidator.cs` | SYS-/CUST- prefix validation, immutability enforcement, ProblemDetails generation |
| ScopeGapDetector | `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeGapDetector.cs` | Analyzes playbook intent to suggest missing scopes; keyword-based + AI classification |
| ScopeEndpoints | `src/server/api/Sprk.Bff.Api/Api/Ai/ScopeEndpoints.cs` | HTTP API for scope management operations |

## Scope Types

| Type | Dataverse Entity | Purpose | Key Fields |
|------|-----------------|---------|------------|
| Action | `sprk_analysisaction` | System prompt and analysis configuration | SystemPrompt, ActionType, SortOrder |
| Skill | `sprk_analysisskill` | Prompt fragments injected into system prompt | PromptFragment, Name, Description |
| Knowledge | `sprk_analysisknowledge` | Knowledge sources (inline content or RAG index references) | Type (Inline/RagIndex), Content, Name |
| Tool | `sprk_analysistool` | Tool definitions registered as AI functions | ToolDefinition, Name, Description |

## Data Flow

### Scope Resolution Chain

1. **Playbook-level resolution** (`ResolvePlaybookScopesAsync`): Loads scopes from playbook N:N relationships in Dataverse (sprk_analysisplaybook -> skills, knowledge, tools)
2. **Node-level resolution** (`ResolveNodeScopesAsync`): Loads node-specific scope overrides from PlaybookNode N:N relationships plus single tool lookup
3. **Explicit resolution** (`ResolveScopesAsync`): Loads scopes by direct ID arrays (used by analysis orchestration)
4. **Knowledge partitioning**: Knowledge sources are split by type — `Inline` content is injected into the system prompt; `RagIndex` sources provide IDs for search-time filtering

### Context Provider Integration

1. **PlaybookChatContextProvider** calls `ResolvePlaybookScopesAsync` to build `ChatKnowledgeScope`
2. RAG source IDs flow into `DocumentSearchTools` and `KnowledgeRetrievalTools` for search-time scoping
3. Inline content and skill prompt fragments are composed into the system prompt (max 8,000 token budget)

### Inheritance Resolution

1. **ExtendScopeAsync**: Creates a child scope linked to parent via `parentid` lookup
2. **GetEffectiveScopeAsync**: Merges child overrides with parent values — overridden fields use child values, non-overridden fields inherit from parent
3. **Single-level constraint**: A scope that already has a parent cannot be extended further (no grandchildren)
4. **Parent deletion**: Three strategies — PromoteChildren (remove link, make standalone), DeleteChildren (cascade), PreventDeletion (block if children exist)

### Gap Detection Flow

1. **Input**: Playbook description and goals text
2. **Keyword analysis**: Fast keyword-based intent detection across 8 categories (extraction, classification, summarization, analysis, risk, dates, financial, comparison)
3. **Catalog comparison**: Detected intents compared against available scope catalog
4. **AI refinement**: OpenAI classification for ambiguous intents
5. **Output**: `ScopeGapAnalysis` with suggested scope types and descriptions

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Consumed by | Chat system | `PlaybookChatContextProvider` | Resolves knowledge scope for agent context |
| Consumed by | Analysis pipeline | `AnalysisOrchestrationService` | Loads scopes for analysis execution |
| Consumed by | Playbook builder | `AiPlaybookBuilderService` | Scope selection during playbook creation |
| Consumed by | DynamicCommandResolver | `IGenericEntityService` | Queries active scopes for capability commands |
| Depends on | Dataverse | OData Web API + `IGenericEntityService` | Scope entity CRUD and relationship queries |
| Depends on | Azure OpenAI | `IOpenAiClient` | Gap detection AI classification |

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| SYS-/CUST- ownership model | Prefix-based immutability | Platform upgrades never break customer scopes; simple naming convention | -- |
| Single-level inheritance | No grandchildren | Keeps merge logic simple; deep hierarchies add complexity without clear benefit | -- |
| Focused entity services | ActionService, SkillService, KnowledgeService, ToolService | Decomposed from ScopeResolverService to reduce constructor dependency count | ADR-010 |
| Keyword + AI gap detection | Dual-pass: fast keywords then AI refinement | Low latency for obvious gaps; AI catches ambiguous intents | -- |
| Duplicate name handling | Auto-suffix with " (N)" pattern | Prevents Dataverse unique constraint violations; max suffix 999, timestamp fallback | -- |

## Constraints

- **MUST**: SYS- prefixed scopes are immutable — update and delete operations are blocked with HTTP 403
- **MUST**: Customer-created scopes receive CUST- prefix automatically
- **MUST**: Users cannot create scopes with the SYS- prefix
- **MUST**: Scope inheritance is limited to single level (parent -> child only)
- **MUST**: Knowledge sources are partitioned by type: Inline -> system prompt, RagIndex -> search scope IDs
- **MUST NOT**: Delete a parent scope without handling children (use ParentDeletionStrategy)
- **MUST NOT**: Modify system scopes directly — use "Save As" to create an editable CUST- copy

## Known Pitfalls

- **Dataverse entity scaffolding**: Several operations in `ScopeManagementService`, `ScopeInheritanceService`, and `ScopeCopyService` return stub/in-memory data pending full Dataverse Web API integration. The service structure and contracts are production-ready, but actual persistence calls are tracked for completion.
- **Parent change propagation**: When a parent scope is updated, non-overridden child fields should reflect the new parent values. This works automatically because `GetEffectiveScopeAsync` reads parent values at resolution time, but there is no push notification to child scopes.
- **System prompt token budget**: Inline knowledge and skill prompt fragments share the 8,000-token system prompt budget. Large inline knowledge sources can crowd out other context. The `PlaybookChatContextProvider` does not currently enforce a per-section budget.
- **Scope capability option set values**: The `sprk_capabilities` multi-select option set values (100000000-100000006) are hardcoded in both `DynamicCommandResolver` and `AnalysisChatContextResolver`. Changes to the Dataverse global choice definition require code updates in both locations.

## Related

- [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) -- Four-tier AI framework: Scope Library tier covers scope types
- [playbook-architecture.md](playbook-architecture.md) -- Playbook system and how scopes are composed into playbooks

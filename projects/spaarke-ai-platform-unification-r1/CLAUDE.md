# CLAUDE.md - Spaarke AI Platform Unification R1

> **Project**: spaarke-ai-platform-unification-r1
> **Branch**: `work/spaarke-ai-platform-unification-r1`
> **Created**: 2026-05-15

## Project Summary

Standalone three-pane AI Code Page (`sprk_spaarkeai`) + shared AI libraries + Azure AI Foundry Agent Service integration. 80% reuse, 20% new code.

## Applicable ADRs

| ADR | Key Rule |
|-----|----------|
| ADR-001 | Minimal API, no Azure Functions |
| ADR-006 | Code Page for standalone UI (not PCF) |
| ADR-007 | SpeFileStore facade, no Graph SDK leak |
| ADR-008 | Endpoint filters for auth (not middleware) |
| ADR-009 | Redis-first caching for sessions/threads |
| ADR-010 | DI minimalism, <=15 non-framework registrations |
| ADR-012 | Shared component library, Fluent v9 only |
| ADR-013 | AI extends BFF, Foundry is additive |
| ADR-015 | AI data governance: minimize, don't log content |
| ADR-016 | Rate limits + backpressure for AI services |
| ADR-018 | Feature flags with kill switches + ValidateOnStart |
| ADR-019 | ProblemDetails errors, SSE terminal events |
| ADR-021 | Fluent v9, dark mode, semantic tokens only |
| ADR-026 | Vite + vite-plugin-singlefile for Code Pages |

## Constraint Files to Load

| Task Area | Files |
|-----------|-------|
| BFF endpoints | `.claude/constraints/api.md`, `ai.md`, `auth.md` |
| Code Page | `.claude/constraints/pcf.md`, `react-versioning.md`, `theme-consistency.md` |
| Shared libraries | `.claude/constraints/react-versioning.md`, `testing.md` |
| Agent Service | `.claude/constraints/ai.md`, `api.md`, `jobs.md`, `config.md` |
| Deployment | `.claude/constraints/azure-deployment.md`, `webresource.md` |

## Key Patterns

| Pattern | File | What It Shows |
|---------|------|---------------|
| Code Page bootstrap | `.claude/patterns/auth/spaarke-auth-initialization.md` | resolveRuntimeConfig -> auth -> render |
| Vite single-file build | `.claude/patterns/webresource/full-page-custom-page.md` | viteSingleFile, assetsInlineLimit |
| BFF endpoint group | `.claude/patterns/api/bff-endpoint-group.md` | MapGroup, RequireAuthorization |
| Tool registration | `.claude/patterns/ai/tool-registration.md` | AIFunctionFactory.Create, [Description] |
| Node executor | `.claude/patterns/ai/playbook-node-executor.md` | INodeExecutor, NodeOutput |
| SSE streaming | `.claude/patterns/ai/streaming-endpoints.md` | SSE format, flush, circuit breaker |

## Canonical Implementations

| What | Where | Action |
|------|-------|--------|
| Three-pane layout | `src/client/code-pages/AnalysisWorkspace/src/App.tsx` | Extract ThreePaneLayout |
| Panel layout hook | `src/client/code-pages/AnalysisWorkspace/src/hooks/usePanelLayout.ts` | Generalize |
| AI context provider | `src/client/code-pages/AnalysisWorkspace/src/context/AnalysisAiContext.tsx` | Extract to ai-context |
| Auth bootstrap | `src/solutions/LegalWorkspace/src/main.tsx` | Follow pattern |
| Vite config | `src/solutions/EventsPage/vite.config.ts` | Follow pattern |
| Context resolver | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/AnalysisChatContextResolver.cs` | Follow pattern |
| Tool class | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/WebSearchTools.cs` | Follow pattern |
| Node executor | `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs` | Implement AT 60 |
| DI module | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs` | Extend |

## Architecture Decisions

1. **ThreePaneLayout** is Code Page only (NOT PCF-safe) -- uses React 19 APIs
2. **@spaarke/ai-context** is NOT PCF-safe -- hooks depend on React 19
3. **Widget registries** use lazy `import()` by type string
4. **Bing Grounding** (LegalResearchTools) and **WebSearch** (WebSearchTools) are complementary
5. **AgentServiceNodeExecutor** = ActionType 60, auto-discovered by NodeExecutorRegistry
6. **All BFF URL construction** must use `buildBffApiUrl()` from `@spaarke/auth`
7. **npm install** (not npm ci) for `src/solutions/*` per CLAUDE.md root

## Task Execution Protocol

**MANDATORY**: All tasks MUST be executed via the `task-execute` skill. Do NOT read POML files directly and implement manually.

Trigger phrases: "work on task X", "continue", "next task", "keep going", "resume task X"

## Parallel Execution

Tasks are organized into waves for concurrent execution. See `tasks/TASK-INDEX.md` for wave assignments and file ownership boundaries. Max 4 concurrent agents per wave.

**Between-wave build verification is MANDATORY:**
- `.cs` files: `dotnet build src/server/api/Sprk.Bff.Api/`
- `.ts/.tsx` files: `npm run build` in affected package

## File Locations

| Component | Path |
|-----------|------|
| Code Page | `src/solutions/SpaarkeAi/` |
| AI Context library | `src/client/shared/Spaarke.AI.Context/` |
| AI Outputs library | `src/client/shared/Spaarke.AI.Outputs/` |
| ThreePaneLayout | `src/client/shared/Spaarke.UI.Components/src/components/ThreePaneLayout/` |
| BFF standalone endpoint | `src/server/api/Sprk.Bff.Api/Api/Ai/StandaloneChatContextEndpoints.cs` |
| BFF standalone provider | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/StandaloneChatContextProvider.cs` |
| Agent Service client | `src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/AgentServiceClient.cs` |
| AT 60 executor | `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AgentServiceNodeExecutor.cs` |
| Routing middleware | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/AgentServiceRoutingMiddleware.cs` |
| Code Interpreter tools | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/CodeInterpreterTools.cs` |
| Legal Research tools | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/LegalResearchTools.cs` |
| Agent definitions | `infrastructure/ai-foundry/agents/` |

---

*Generated by project-pipeline on 2026-05-15*

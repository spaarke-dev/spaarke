# Implementation Plan: Spaarke AI Platform Unification R1

> **Status**: Approved
> **Created**: 2026-05-15
> **Source**: spec.md
> **Approach**: Parallel wave execution with concurrent Claude Code agents

## 1. Executive Summary

Unify Spaarke's AI capabilities into a standalone three-pane Code Page (`sprk_spaarkeai`) with shared libraries, then integrate Azure AI Foundry Agent Service. 80% reuse of existing production components; 20% new code.

**35 tasks across 12 waves. ~47h wall-clock with parallel execution (vs ~100h serial).**

## 2. Architecture Context

### Applicable ADRs
| ADR | Constraint | Impact |
|-----|-----------|--------|
| ADR-001 | Minimal API, no Azure Functions | All BFF endpoints |
| ADR-004 | Async Job Contract for background work | Agent Service runs |
| ADR-006 | Code Page for standalone, PCF for form-embedded | `sprk_spaarkeai` is a Code Page |
| ADR-007 | SpeFileStore facade, no Graph SDK leak | Source pane document loading |
| ADR-008 | Endpoint filters for auth | New standalone context endpoint |
| ADR-009 | Redis-first caching | Agent sessions, Foundry thread IDs |
| ADR-010 | DI minimalism, <=15 non-framework registrations | New services in AddAiModule |
| ADR-012 | Shared component library | ThreePaneLayout, ai-context, ai-outputs |
| ADR-013 | AI extends BFF, not separate service | Foundry is additive |
| ADR-014 | AI caching/reuse: cache outcomes not streams | Agent Service results |
| ADR-015 | AI data governance: minimize, don't log content | Legal research queries, Code Interpreter data |
| ADR-016 | AI rate limits and backpressure | Agent Service concurrency gates |
| ADR-018 | Feature flags with kill switches | AgentService, CodeInterpreter, BingGrounding options |
| ADR-019 | ProblemDetails for errors, SSE terminal events | Streaming error translation |
| ADR-020 | SemVer for client packages | @spaarke/ai-context @1.0.0, @spaarke/ai-outputs @1.0.0 |
| ADR-021 | Fluent v9 only, dark mode required | All 17 widgets |
| ADR-022 | PCF = React 16/17, Code Page = React 19 | Libraries are NOT PCF-safe |
| ADR-026 | Vite + vite-plugin-singlefile for Code Pages | SpaarkeAi build config |
| ADR-027 | Managed solutions for non-dev | Deploying sprk_spaarkeai web resource |

### Discovered Resources

**Pattern Files** (10):
- `.claude/patterns/ai/streaming-endpoints.md` -- SSE format, flush, circuit breaker
- `.claude/patterns/ai/tool-registration.md` -- AIFunctionFactory.Create, [Description]
- `.claude/patterns/ai/playbook-node-executor.md` -- INodeExecutor, NodeOutput
- `.claude/patterns/auth/spaarke-auth-initialization.md` -- resolveRuntimeConfig bootstrap
- `.claude/patterns/webresource/full-page-custom-page.md` -- Vite single-file build
- `.claude/patterns/api/bff-endpoint-group.md` -- MapGroup, RequireAuthorization
- `.claude/patterns/api/endpoint-filters.md` -- AddAiAuthorizationFilter
- `.claude/patterns/shared/shared-library-build.md` -- npm run build, file: refs
- `.claude/patterns/shared/component-export.md` -- barrel exports, pcf-safe
- `.claude/patterns/auth/obo-flow.md` -- GraphClientFactory, Redis token cache

**Architecture Docs** (7):
- `docs/architecture/AI-ARCHITECTURE.md` -- Four-tier AI platform
- `docs/architecture/playbook-architecture.md` -- Node executors, ActionTypes
- `docs/architecture/chat-architecture.md` -- SprkChatAgent, dual IChatClient
- `docs/architecture/code-pages-architecture.md` -- Code Page bootstrap, themes
- `docs/architecture/shared-libraries-architecture.md` -- Spaarke.Core, Spaarke.Dataverse
- `docs/architecture/rag-architecture.md` -- RAG pipeline, hybrid search
- `docs/architecture/scope-architecture.md` -- ScopeResolverService

**Guides** (6):
- `docs/guides/AI-DEPLOYMENT-GUIDE.md` -- All config keys, deployment phases
- `docs/guides/JPS-AUTHORING-GUIDE.md` -- JPS schema, $choices, $ref
- `docs/guides/ai-assistant-theming.md` -- Fluent v9 tokens for AI components
- `docs/guides/SHARED-UI-COMPONENTS-GUIDE.md` -- Build workflow, file: refs
- `docs/guides/SCOPE-CONFIGURATION-GUIDE.md` -- Scope CRUD, scope-guided search
- `docs/guides/DATAVERSE-MCP-INTEGRATION-GUIDE.md` -- MCP tool usage

**Constraint Files** (13): ai.md, api.md, auth.md, azure-deployment.md, config.md, data.md, jobs.md, pcf.md, plugins.md, react-versioning.md, testing.md, theme-consistency.md, webresource.md

**Scripts** (12): Build-ViteSolutionsDirect.ps1, Deploy-AllWebResources.ps1, Deploy-BffApi.ps1, Deploy-DataverseSolutions.ps1, Deploy-Release.ps1, Seed-JpsActions.ps1, Seed-AnalysisSkills.ps1, Seed-KnowledgeScopes.ps1, Refresh-ScopeModelIndex.ps1, Deploy-Playbook.ps1, Diagnose-AiSummaryService.ps1, Build-AllClientComponents.ps1

### Canonical Code to Reuse
| Component | Location | Action |
|-----------|----------|--------|
| SprkChat | `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/` | Reuse as-is |
| PanelSplitter | `src/client/shared/Spaarke.UI.Components/src/components/PanelSplitter/` | Reuse as-is |
| usePanelLayout | `src/client/code-pages/AnalysisWorkspace/src/hooks/usePanelLayout.ts` | Extract, generalize |
| AnalysisAiContext | `src/client/code-pages/AnalysisWorkspace/src/context/AnalysisAiContext.tsx` | Extract to @spaarke/ai-context |
| Auth bootstrap | `src/solutions/LegalWorkspace/src/main.tsx` | Follow pattern |
| Vite config | `src/solutions/EventsPage/vite.config.ts` | Follow pattern |
| Theme resolution | `resolveCodePageTheme()` from @spaarke/ui-components | Reuse as-is |
| ChatEndpoints | `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` | Follow pattern for new endpoints |
| AnalysisChatContextResolver | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/AnalysisChatContextResolver.cs` | Follow pattern |
| WebSearchTools | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/WebSearchTools.cs` | Follow pattern for new tools |
| INodeExecutor | `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs` | Implement for AT 60 |

## 3. Key Design Decisions

1. **ThreePaneLayout is Code Page Only** -- NOT PCF-safe, can use React 19 APIs freely
2. **@spaarke/ai-context is NOT PCF-safe** -- hooks use React 19 via SprkChat internals
3. **Widget registries use lazy loading** -- dynamic `import()` by type string
4. **Bing Grounding and WebSearch are complementary** -- different products, both active
5. **AgentServiceNodeExecutor = ActionType 60** -- auto-discovered by NodeExecutorRegistry

## 4. Risk Register

| Risk | Mitigation |
|------|-----------|
| DI registration count exceeds ADR-010 ceiling | Count all registrations in AddAiModule before adding; use Options pattern |
| Bing Grounding legal query PII exposure | Document data minimization strategy per ADR-015 |
| Agent Service streaming error translation | Implement SSE terminal error events per ADR-019 |
| Feature flag proliferation | Three new Options classes with ValidateOnStart per ADR-018 |
| Parallel task file conflicts | Strict file ownership in POML tasks, build verification between waves |

## 5. Phase Breakdown (WBS)

### Phase 1: Standalone Code Page + Shared Libraries (Waves 0-5D)

#### Wave 0: Foundation Scaffolding (PARALLEL - 3 agents)
- **001** Scaffold SpaarkeAi Code Page (`src/solutions/SpaarkeAi/`)
- **002** Scaffold @spaarke/ai-context library (`src/client/shared/Spaarke.AI.Context/`)
- **003** Scaffold @spaarke/ai-outputs library (`src/client/shared/Spaarke.AI.Outputs/`)

#### Wave 1: Core Shared Components (PARALLEL - 3 agents)
- **010** ThreePaneLayout in @spaarke/ui-components
- **011** Extract context hooks to @spaarke/ai-context
- **012** StandaloneAiContext provider + entity resolver

#### Wave 2: Registries + BFF (PARALLEL - 4 agents)
- **020** Output pane registry + widgets 1-4
- **021** Output pane widgets 5-8
- **022** Source pane registry + all 6 widgets
- **023** BFF StandaloneChatContextProvider + endpoint

#### Wave 3: SSE + Chat History (PARALLEL - 3 agents)
- **030** SSE event types (output_pane, source_pane, source_highlight)
- **031** Output pane widgets 9-11 + cross-pane linking
- **032** Chat history panel component

#### Wave 4: Code Page Assembly (PARALLEL - 2 agents)
- **040** Wire SpaarkeAi Code Page with all components
- **041** Launch points (workspace, form, deep-link, M365 handoff)

#### Wave 5: Refactor + Verification (PARALLEL - 2 agents)
- **050** Refactor AnalysisWorkspace to import from @spaarke/ai-context
- **051** Dark mode verification + NFR compliance

#### Wave 5D: Phase 1 Deploy + Test (SERIAL)
- **055** Build + deploy Code Page + BFF
- **056** Phase 1 integration testing (FR-01 to FR-12)

### Phase 2: AI Foundry Agent Service Integration (Waves 6-7D)

#### Wave 6: Agent Service Foundation (PARALLEL - 3 agents)
- **060** AgentServiceClient (Azure.AI.Projects SDK wrapper)
- **061** AgentServiceNodeExecutor (ActionType 60)
- **062** Agent definition + Foundry infrastructure

#### Wave 7: Tools + Routing (PARALLEL - 3 agents)
- **070** CodeInterpreterTools.cs + CodeInterpreterBridge
- **071** LegalResearchTools.cs
- **072** AgentServiceRoutingMiddleware

#### Wave 7D: Phase 2 Integration (SERIAL)
- **075** DI registration + options classes + feature flags
- **076** Build + deploy BFF + Foundry
- **077** Phase 2 integration testing (FR-13 to FR-18)

### Phase 3: Evaluation & Quality (Waves 8-9)

#### Wave 8: Evaluation + Telemetry (PARALLEL - 2 agents)
- **080** Evaluation pipeline with legal metrics
- **081** OpenTelemetry tracing for Foundry calls

#### Wave 9: Final Integration + Wrap-up (SERIAL)
- **085** Phase 3 deploy + end-to-end testing
- **086** BYOK configuration verification
- **090** Project wrap-up

## 6. Dependencies

```
Wave 0 ──> Wave 1 ──> Wave 2 ──> Wave 3 ──> Wave 4 ──> Wave 5 ──> Wave 5D
                                                                       │
                                                                       v
                                              Wave 6 ──> Wave 7 ──> Wave 7D
                                                                       │
                                                                       v
                                                          Wave 8 ──> Wave 9
```

## 7. Affected Areas

| Area | Files | Change Type |
|------|-------|-------------|
| `src/solutions/SpaarkeAi/` | New project | Create |
| `src/client/shared/Spaarke.AI.Context/` | New library | Create |
| `src/client/shared/Spaarke.AI.Outputs/` | New library | Create |
| `src/client/shared/Spaarke.UI.Components/` | ThreePaneLayout component | Extend |
| `src/client/code-pages/AnalysisWorkspace/` | Refactor context imports | Modify |
| `src/server/api/Sprk.Bff.Api/Api/Ai/` | New endpoint | Extend |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/` | Context provider, routing | Extend |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/` | 2 new tool classes | Extend |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/` | AT 60 executor | Extend |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/` | New directory | Create |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/` | DI registrations | Modify |
| `infrastructure/ai-foundry/` | Agent definitions, eval config | Extend |

## 8. Acceptance Criteria

See README.md Graduation Criteria (15 items) and spec.md Success Criteria.

---

*Generated by project-pipeline on 2026-05-15*

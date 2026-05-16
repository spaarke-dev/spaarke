# Task Index: Spaarke AI Platform Unification R1

> **Total Tasks**: 35
> **Parallel Waves**: 12
> **Estimated Wall-Clock**: ~47h (parallel) / ~100h (serial)

## Status Legend
- :white_large_square: Not Started
- :arrows_counterclockwise: In Progress
- :white_check_mark: Complete
- :no_entry: Blocked

## Task Registry

### Phase 1: Standalone Code Page + Shared Libraries

#### Wave 0: Foundation Scaffolding (PARALLEL - 3 agents)
| # | Task | Status | Est. | Depends |
|---|------|--------|------|---------|
| 001 | Scaffold SpaarkeAi Code Page project | :white_large_square: | 2h | none |
| 002 | Scaffold @spaarke/ai-context library | :white_large_square: | 2h | none |
| 003 | Scaffold @spaarke/ai-outputs library | :white_large_square: | 2h | none |

#### Wave 1: Core Shared Components (PARALLEL - 3 agents)
| # | Task | Status | Est. | Depends |
|---|------|--------|------|---------|
| 010 | ThreePaneLayout component in @spaarke/ui-components | :white_large_square: | 3h | 001-003 |
| 011 | Extract context hooks to @spaarke/ai-context | :white_large_square: | 3h | 002 |
| 012 | StandaloneAiContext provider + entity resolver | :white_large_square: | 3h | 002 |

#### Wave 2: Registries + BFF (PARALLEL - 4 agents)
| # | Task | Status | Est. | Depends |
|---|------|--------|------|---------|
| 020 | Output pane registry + widgets 1-4 | :white_large_square: | 4h | 003 |
| 021 | Output pane widgets 5-8 | :white_check_mark: | 3h | 003, 020 |
| 022 | Source pane registry + all 6 widgets | :white_large_square: | 4h | 003 |
| 023 | BFF StandaloneChatContextProvider + endpoint | :white_large_square: | 3h | none |

#### Wave 3: SSE + Chat History (PARALLEL - 3 agents)
| # | Task | Status | Est. | Depends |
|---|------|--------|------|---------|
| 030 | SSE event types (output_pane, source_pane, source_highlight) | :white_check_mark: | 3h | 023 |
| 031 | Output pane widgets 9-11 + cross-pane linking | :white_check_mark: | 3h | 020 |
| 032 | Chat history panel component | :white_large_square: | 2h | 003 |

#### Wave 4: Code Page Assembly (PARALLEL - 2 agents)
| # | Task | Status | Est. | Depends |
|---|------|--------|------|---------|
| 040 | Wire SpaarkeAi Code Page with all components | :white_check_mark: | 4h | 010-032 |
| 041 | Launch points (workspace, form, deep-link, M365) | :white_check_mark: | 3h | 040 |

#### Wave 5: Refactor + Verification (PARALLEL - 2 agents)
| # | Task | Status | Est. | Depends |
|---|------|--------|------|---------|
| 050 | Refactor AnalysisWorkspace to import from @spaarke/ai-context | :white_check_mark: | 3h | 011, 012, 040 |
| 051 | Dark mode verification + NFR compliance | :white_check_mark: | 2h | 020-032 |

#### Wave 5D: Phase 1 Deploy + Test (SERIAL)
| # | Task | Status | Est. | Depends |
|---|------|--------|------|---------|
| 055 | Build + deploy Code Page + BFF | ✅ | 2h | 050, 051 |
| 056 | Phase 1 integration testing (FR-01 to FR-12) | :white_large_square: | 3h | 055 |

#### Wave 5E: UX Enhancements (PARALLEL)
| # | Task | Status | Est. | Depends |
|---|------|--------|------|---------|
| 057 | Welcome experience with guided prompt buttons (no-context launch) | ✅ | 4h | 040 |

### Phase 2: AI Foundry Agent Service Integration

#### Wave 6: Agent Service Foundation (PARALLEL - 3 agents)
| # | Task | Status | Est. | Depends |
|---|------|--------|------|---------|
| 060 | AgentServiceClient (Azure.AI.Projects SDK) | ✅ | 4h | 056 |
| 061 | AgentServiceNodeExecutor (AT 60) | ✅ | 3h | 056 |
| 062 | Agent definition + Foundry infrastructure | ✅ | 2h | none |

#### Wave 7: Tools + Routing (PARALLEL - 3 agents)
| # | Task | Status | Est. | Depends |
|---|------|--------|------|---------|
| 070 | CodeInterpreterTools.cs + CodeInterpreterBridge | :white_check_mark: | 3h | 060 |
| 071 | LegalResearchTools.cs (Bing Grounding) | :white_large_square: | 3h | 060 |
| 072 | AgentServiceRoutingMiddleware | :white_check_mark: | 3h | 060 |

#### Wave 7D: Phase 2 Integration (SERIAL)
| # | Task | Status | Est. | Depends |
|---|------|--------|------|---------|
| 075 | DI registration + options classes + feature flags | ✅ | 2h | 060-072 |
| 076 | Build + deploy BFF + Foundry | :white_large_square: | 2h | 075 |
| 077 | Phase 2 integration testing (FR-13 to FR-18) | :white_large_square: | 3h | 076 |

### Phase 3: Evaluation & Quality

#### Wave 8: Evaluation + Telemetry (PARALLEL - 2 agents)
| # | Task | Status | Est. | Depends |
|---|------|--------|------|---------|
| 080 | Evaluation pipeline with legal metrics | ✅ | 3h | 077 |
| 081 | OpenTelemetry tracing for Foundry calls | ✅ | 2h | 077 |

#### Wave 9: Final Integration + Wrap-up (SERIAL)
| # | Task | Status | Est. | Depends |
|---|------|--------|------|---------|
| 085 | Phase 3 deploy + end-to-end testing | :white_large_square: | 3h | 080, 081 |
| 086 | BYOK configuration verification | ✅ | 2h | 085 |
| 090 | Project wrap-up | :white_large_square: | 1h | 086 |

## Parallel Execution Groups

| Wave | Tasks | Prerequisite | Max Agents | Notes |
|------|-------|--------------|------------|-------|
| 0 | 001, 002, 003 | none | 3 | Each creates new directory |
| 1 | 010, 011, 012 | Wave 0 | 3 | Different libraries/subdirs |
| 2 | 020, 021, 022, 023 | Wave 1 | 4 | Split by widget files + BFF |
| 3 | 030, 031, 032 | Wave 2 | 3 | BFF + widget files + chat-history |
| 4 | 040, 041 | Wave 3 | 2 | App.tsx vs utilities |
| 5 | 050, 051 | Wave 4 | 2 | AnalysisWorkspace vs test files |
| 5D | 055, 056 | Wave 5 | serial | Build verification mandatory |
| 6 | 060, 061, 062 | Wave 5D | 3 | Foundry/ vs Nodes/ vs infra/ |
| 7 | 070, 071, 072 | Wave 6 | 3 | Different tool/middleware files |
| 7D | 075, 076, 077 | Wave 7 | serial | DI integration, deploy, test |
| 8 | 080, 081 | Wave 7D | 2 | Eval config vs OTEL spans |
| 9 | 085, 086, 090 | Wave 8 | serial | Final integration |

## Critical Path

```
001 -> 010 -> 020 -> 031 -> 040 -> 050 -> 055 -> 056 -> 060 -> 070 -> 075 -> 076 -> 077 -> 080 -> 085 -> 086 -> 090
```

## File Ownership Map

Each parallel task has exclusive ownership of specific files. No two tasks in the same wave touch overlapping files.

| Task | Exclusive Owned Files |
|------|----------------------|
| 001 | `src/solutions/SpaarkeAi/` |
| 002 | `src/client/shared/Spaarke.AI.Context/` |
| 003 | `src/client/shared/Spaarke.AI.Outputs/` |
| 010 | `Spaarke.UI.Components/src/components/ThreePaneLayout/` |
| 011 | `Spaarke.AI.Context/src/hooks/`, `src/services/` |
| 012 | `Spaarke.AI.Context/src/providers/`, `src/types/` |
| 020 | `Spaarke.AI.Outputs/src/output-widgets/` (1-4), `registry/output-registry.ts`, `types/widget-types.ts` |
| 021 | `Spaarke.AI.Outputs/src/output-widgets/` (5-8 only) |
| 022 | `Spaarke.AI.Outputs/src/source-widgets/`, `registry/source-registry.ts` |
| 023 | `Sprk.Bff.Api/Api/Ai/StandaloneChatContext*`, `Services/Ai/Chat/StandaloneChatContext*` |
| 030 | `Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes/` |
| 031 | `Spaarke.AI.Outputs/src/output-widgets/` (9-11), `src/cross-pane/` |
| 032 | `Spaarke.AI.Outputs/src/chat-history/` |
| 040 | `SpaarkeAi/src/` (App.tsx, components/, context/, hooks/) |
| 041 | `SpaarkeAi/src/utils/launch-resolver.ts`, ribbon scripts |
| 050 | `AnalysisWorkspace/src/context/`, `src/hooks/` |
| 051 | `Spaarke.AI.Outputs/src/**/*.test.tsx` |
| 060 | `Sprk.Bff.Api/Services/Ai/Foundry/AgentServiceClient.cs`, `AgentServiceOptions.cs`, `.csproj` |
| 061 | `Sprk.Bff.Api/Services/Ai/Nodes/AgentServiceNodeExecutor.cs` |
| 062 | `infrastructure/ai-foundry/agents/` |
| 070 | `Tools/CodeInterpreterTools.cs`, `Foundry/CodeInterpreterBridge.cs`, `Foundry/CodeInterpreterOptions.cs` |
| 071 | `Tools/LegalResearchTools.cs`, `Foundry/BingGroundingOptions.cs` |
| 072 | `Chat/AgentServiceRoutingMiddleware.cs` |

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|-----------|
| 050 | AnalysisWorkspace regression | Full regression test in 056 |
| 060 | Azure.AI.Projects SDK API surface | Early spike with test thread |
| 072 | Routing < 50ms NFR | Use keyword classifier, not LLM |
| 075 | DI registration count | Count before adding; verify <= 15 |

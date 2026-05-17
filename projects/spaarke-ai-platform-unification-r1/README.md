# Spaarke AI Platform Unification R1

> **Status**: In Progress
> **Branch**: `work/spaarke-ai-platform-unification-r1`
> **Created**: 2026-05-15
> **Owner**: Ralph Schroeder

## Purpose

Unify Spaarke's AI capabilities into a standalone three-pane Code Page (`sprk_spaarkeai`) with chat, output/work, and research/source panes. Build three shared AI libraries (`@spaarke/ai-context`, `@spaarke/ai-outputs`, `@spaarke/ui-components` extensions) to enable reuse across all surfaces. Integrate Azure AI Foundry Agent Service for enhanced capabilities (Code Interpreter, Bing Grounding).

## Scope

### In Scope
- `sprk_spaarkeai` standalone Code Page with three-pane layout
- `@spaarke/ai-context` shared library (context providers, service clients, hooks)
- `@spaarke/ai-outputs` shared library (output/source pane widget registries)
- `ThreePaneLayout` component in `@spaarke/ui-components`
- `StandaloneAiContext` provider + `StandaloneChatContextProvider` BFF service
- Output pane component registry (11 purpose-built widgets)
- Source pane component registry (6 purpose-built widgets)
- SSE event types for pane control (`output_pane`, `source_pane`, `source_highlight`)
- Chat history panel with session list and search
- Launch points (workspace button, entity form buttons, deep-link, M365 handoff)
- Azure AI Foundry Agent Service integration (`AgentServiceClient`, `AgentServiceNodeExecutor` AT 60)
- Code Interpreter bridge (data analysis, chart generation)
- Bing Grounding integration (legal research with citations)
- `CodeInterpreterTools.cs` + `LegalResearchTools.cs` tool classes
- `AgentServiceRoutingMiddleware` routing decision tree
- Evaluation pipeline with legal-specific metrics
- OpenTelemetry tracing for agent routing and Foundry calls
- Refactor `AnalysisAiContext` to `@spaarke/ai-context`
- Dark mode support for all widgets (ADR-021)
- BYOK-compatible configuration

### Out of Scope
- Multi-agent orchestration
- Custom model fine-tuning
- Semantic Kernel migration
- Agent marketplace
- Voice input/output
- Real-time collaboration
- Offline mode

## Architecture

### Three-Pane Layout
```
+------------------+---------------------+------------------+
| Chat Panel       | Output/Work Panel   | Source Panel      |
| (left, always)   | (center, dynamic)   | (right, collapse)|
|                  |                     |                  |
| SprkChat         | Widget Registry     | Widget Registry   |
| - Streaming      | - Budget Dashboard  | - Doc Viewer      |
| - Tools          | - Search Results    | - Web Sources     |
| - Commands       | - Analysis Editor   | - Legal Library   |
| - History        | - Contract Compare  | - Citations       |
| - Playbooks      | - 7 more widgets   | - 2 more widgets  |
+------------------+---------------------+------------------+
```

### Technology Stack
- **Frontend**: React 19, Vite single-file build, Fluent UI v9
- **Backend**: .NET 8 Minimal API (BFF), Azure AI Foundry Agent Service
- **Shared Libraries**: `@spaarke/ai-context`, `@spaarke/ai-outputs`, `@spaarke/ui-components`
- **Infrastructure**: Azure OpenAI, AI Foundry Hub/Project, AI Search, Redis

### Key Patterns
- Code Page bootstrap: `resolveRuntimeConfig()` + `ensureAuthInitialized()` + `createRoot()`
- Context resolution: URL params -> entity context -> scoped playbooks/tools
- Widget registries: lazy-loaded components by SSE event type
- Cross-pane linking: `source_highlight` SSE events
- Agent routing: intent classifier -> direct pipeline OR Agent Service

## Phases

| Phase | Description | Tasks |
|-------|-------------|-------|
| Phase 1 | Standalone Code Page + Shared Libraries | 001-056 |
| Phase 2 | AI Foundry Agent Service Integration | 060-077 |
| Phase 3 | Evaluation & Quality | 080-090 |

## Graduation Criteria

1. [ ] Standalone three-pane Code Page renders and functions without Analysis Workspace
2. [ ] Entity context loads from URL parameters (`?matterId=`, `?projectId=`, `?documentId=`)
3. [ ] All 7 existing tool categories work in standalone mode
4. [ ] Output pane renders purpose-built widgets (not markdown)
5. [ ] Source pane loads SPE documents and highlights cited sections
6. [ ] Cross-pane linking works (output citation -> source navigation)
7. [ ] `@spaarke/ai-context` and `@spaarke/ai-outputs` are separate installable libraries
8. [ ] Analysis Workspace works identically after `AnalysisAiContext` refactor
9. [ ] Code Interpreter generates charts from matter financial data
10. [ ] Bing Grounding returns legal research with structured citations
11. [ ] Agent Service routing is transparent to user
12. [ ] Evaluation pipeline produces quality scores
13. [ ] Works in all 3 deployment models (multi-customer, dedicated, BYOK)
14. [ ] Dark mode renders correctly across all output/source widgets
15. [ ] BYOK deployment works with customer-provisioned Foundry resources

## Related Projects

- `ai-m365-copilot-integration-r1` -- M365 surface, Declarative Agent with handoff
- `spaarke-daily-update-service` -- notification playbooks use same engine
- `ai-sprk-chat-extensibility-r1` -- context enrichment patterns inform StandaloneAiContext
- `spaarke-workspace-user-configuration-r1` -- workspace may embed standalone AI as a section

---

*Generated by project-pipeline on 2026-05-15*

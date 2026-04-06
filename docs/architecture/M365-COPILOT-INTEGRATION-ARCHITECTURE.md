# M365 Copilot Integration — Architecture Overview

> **Last Updated**: 2026-03-26
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified
> **Project**: ai-m365-copilot-integration (R1)

> **Verification note (2026-04-05)**: All 14 agent gateway files confirmed in `src/server/api/Sprk.Bff.Api/Api/Agent/`. `src/solutions/CopilotAgent/` exists. Feature is in R1 production.

## System Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│  M365 COPILOT (Microsoft's Orchestrator)                             │
│                                                                      │
│  ┌─────────────────────────────┐  ┌─────────────────────────────┐  │
│  │  Copilot in MDA             │  │  Copilot in Teams           │  │
│  │  (side pane on Dataverse    │  │  (future — R2)              │  │
│  │   forms)                    │  │                             │  │
│  └──────────┬──────────────────┘  └──────────┬──────────────────┘  │
│             │                                 │                     │
│             └──────────┬──────────────────────┘                     │
│                        │                                            │
│  ┌─────────────────────▼──────────────────────────────────────────┐│
│  │  Declarative Agent (declarativeAgent.json)                      ││
│  │  • Instructions (system prompt): Spaarke legal ops vocabulary   ││
│  │  • Conversation starters: "Find documents...", "Run a scan..."  ││
│  │  • Capabilities: API Plugin                                     ││
│  └─────────────────────┬──────────────────────────────────────────┘│
│                        │                                            │
│  ┌─────────────────────▼──────────────────────────────────────────┐│
│  │  API Plugin (spaarke-api-plugin.json)                           ││
│  │  27 functions with AI-readable descriptions                     ││
│  │  Copilot AI decides WHEN to call each function                  ││
│  └─────────────────────┬──────────────────────────────────────────┘│
│                        │                                            │
│  ┌─────────────────────▼──────────────────────────────────────────┐│
│  │  OpenAPI Spec (spaarke-bff-openapi.yaml)                        ││
│  │  35+ operations: chat, search, playbooks, analysis, events...   ││
│  └─────────────────────┬──────────────────────────────────────────┘│
└────────────────────────┼────────────────────────────────────────────┘
                         │ HTTPS (direct call, no intermediary)
                         ▼
┌─────────────────────────────────────────────────────────────────────┐
│  SPAARKE BFF API (spe-api-dev-67e2xz.azurewebsites.net)             │
│                                                                      │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │  Agent Gateway Layer (NEW — src/server/api/.../Api/Agent/)    │  │
│  │                                                               │  │
│  │  AgentEndpoints.cs         — POST /api/agent/message          │  │
│  │                              GET  /api/agent/playbooks        │  │
│  │                              POST /api/agent/run-playbook     │  │
│  │                              GET  /api/agent/playbooks/status │  │
│  │                                                               │  │
│  │  SpaarkeAgentHandler.cs    — M365 Agents SDK ActivityHandler  │  │
│  │  AgentTokenService.cs      — SSO/OBO token exchange           │  │
│  │  AgentConversationService  — Multi-turn session mapping       │  │
│  │  AdaptiveCardFormatter     — Response → Adaptive Card JSON    │  │
│  │  PlaybookInvocationService — Search → Select → Execute flow   │  │
│  │  EmailDraftService         — Matter context → AI email draft  │  │
│  │  HandoffUrlBuilder         — Deep-links to Analysis Workspace │  │
│  │  AgentPlaybookStatusSvc    — Async job tracking               │  │
│  │  AgentConfigurationSvc     — Admin controls, feature toggles  │  │
│  │  AgentErrorHandler         — User-friendly error cards        │  │
│  │  AgentTelemetry            — Interaction metrics              │  │
│  └────────────────────────────────┬─────────────────────────────┘  │
│                                   │ delegates to                    │
│  ┌────────────────────────────────▼─────────────────────────────┐  │
│  │  EXISTING BFF SERVICES (no new AI logic)                      │  │
│  │                                                               │  │
│  │  /api/ai/chat/*         — Conversational AI with RAG          │  │
│  │  /api/ai/search         — Semantic/hybrid document search     │  │
│  │  /api/ai/playbooks/*    — Playbook management + execution     │  │
│  │  /api/ai/analysis/*     — Document analysis + workspace       │  │
│  │  /api/v1/documents/*    — Document CRUD + metadata            │  │
│  │  /api/v1/events/*       — Tasks, deadlines, assignments       │  │
│  │  /api/workspace/*       — Portfolio, briefing, summaries      │  │
│  │  /api/communications/*  — Email sending                       │  │
│  └────────────────────────────────┬─────────────────────────────┘  │
│                                   │                                 │
│  ┌────────────────────────────────▼─────────────────────────────┐  │
│  │  AZURE SERVICES                                               │  │
│  │  Azure OpenAI    — LLM (our model, not Microsoft's)           │  │
│  │  AI Search       — Semantic search (tenant-isolated)          │  │
│  │  Redis           — Token + session caching                    │  │
│  │  SPE via Graph   — Document storage (discoverabilityDisabled) │  │
│  │  Dataverse       — Entity data (matters, events, contacts)    │  │
│  └──────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
```

## Key Design Principles

### 1. Agent Endpoints Are Thin Adapters
The agent gateway (`/api/agent/*`) contains zero AI orchestration logic. Every operation delegates to existing BFF services. This minimizes new code surface and reuses proven infrastructure.

### 2. SPE Documents Are Never Directly Grounded
SPE containers remain `discoverabilityDisabled = true`. M365 Copilot cannot directly access document content. All document access flows through the BFF API with per-matter/per-project authorization enforcement.

### 3. Adaptive Cards for Structured Results
All responses to Copilot use Adaptive Card JSON (schema 1.5). Cards include `Action.Submit` buttons for follow-up actions and deep-links to Analysis Workspace for complex work.

### 4. Async Pattern for Long-Running Operations
Playbooks that exceed the 30-second API response timeout return a progress indicator card with a deep-link to the Analysis Workspace code page. Status can be polled via `GET /api/agent/playbooks/status/{jobId}`.

### 5. M365 Copilot = General Chat; SprkChat = Deep Analysis
Copilot handles: document search, playbook invocation, matter queries, email drafting, navigation. SprkChat handles: streaming editor integration, inline AI toolbar, compound write-back actions (Analysis Workspace only).

## Deployment Components

| Component | Location | Deployed To |
|-----------|----------|-------------|
| Declarative Agent manifest | `src/solutions/CopilotAgent/` | M365 Org App Catalog |
| API Plugin + OpenAPI spec | `src/solutions/CopilotAgent/` | M365 Org App Catalog |
| Adaptive Card templates | `src/solutions/CopilotAgent/cards/` | Reference only (cards built in code) |
| Agent gateway services | `src/server/api/.../Api/Agent/` | Azure App Service (BFF API) |
| Bot Service | `infrastructure/bot-service/` | Azure Bot Service |
| BYOK templates | `infrastructure/byok/` | Customer Azure subscription |

## Authentication Flow

```
User on MDA Form → M365 Copilot side pane
  │
  ├── User has Entra ID session (same as MDA)
  │
  ▼
Declarative Agent → API Plugin
  │
  ├── API Plugin authenticates to BFF via OAuth 2.0
  │   (delegated permissions, user's identity)
  │
  ▼
BFF API (AgentTokenService)
  │
  ├── Validates incoming token
  ├── OBO exchange → Graph API token (Files.Read.All, FileStorageContainer.Selected)
  ├── OBO exchange → Dataverse token
  ├── Caches tokens in Redis (tenant-scoped keys per ADR-014)
  │
  ▼
Results returned with user's authorization enforced
```

## File Inventory

### Agent Gateway (11 files)
```
src/server/api/Sprk.Bff.Api/Api/Agent/
├── AgentEndpoints.cs              — 4 REST endpoints (message, playbooks, run, status)
├── AgentAuthorizationFilter.cs    — Endpoint filter for agent auth (ADR-008)
├── AgentModels.cs                 — Request/response DTOs
├── AgentTokenService.cs           — SSO/OBO token exchange
├── AgentConversationService.cs    — Multi-turn conversation session mapping
├── AdaptiveCardFormatterService.cs — BFF response → Adaptive Card JSON
├── PlaybookInvocationService.cs   — Full playbook invocation orchestration
├── PlaybookStatusEndpoints.cs     — Async job tracking service
├── EmailDraftService.cs           — Email draft generation
├── HandoffUrlBuilder.cs           — Deep-link URL generation
├── SpaarkeAgentHandler.cs         — M365 Agents SDK ActivityHandler
├── AgentConfigurationService.cs   — Admin controls and feature toggles
├── AgentErrorHandler.cs           — User-friendly error card generation
└── AgentTelemetry.cs              — Interaction metrics and logging
```

### Copilot Agent Package (6 files)
```
src/solutions/CopilotAgent/
├── declarativeAgent.json          — Agent manifest with instructions
├── spaarke-api-plugin.json        — 27 API Plugin function definitions
├── spaarke-bff-openapi.yaml       — OpenAPI spec (35+ operations)
├── appPackage/
│   ├── manifest.json              — Teams app manifest
│   └── env.dev.json               — Dev environment config
└── cards/                         — 10 Adaptive Card templates
    ├── document-list.json
    ├── matter-summary.json
    ├── task-list.json
    ├── playbook-menu.json
    ├── risk-findings.json
    ├── playbook-library.json
    ├── email-preview.json
    ├── handoff-card.json
    ├── progress-indicator.json
    └── error-card.json
```

### Infrastructure (5 files)
```
infrastructure/
├── bot-service/
│   ├── main.bicep                 — Azure Bot Service template
│   └── parameters.dev.json        — Dev parameters
└── byok/
    ├── main.bicep                 — Full BYOK stack template
    ├── parameters.template.json   — Customer parameter template
    └── README.md                  — BYOK deployment guide
```

## ADR Compliance

| ADR | How Complied |
|-----|-------------|
| ADR-001 | All agent endpoints use Minimal API pattern |
| ADR-008 | AgentAuthorizationFilter on all endpoints |
| ADR-010 | Concrete types, no unnecessary interfaces, AddAgentModule() |
| ADR-013 | Extends BFF (no separate service), reuses existing AI services |
| ADR-014 | Redis caching with tenant-scoped keys |
| ADR-015 | Never logs document content/prompts — identifiers only |
| ADR-016 | Rate limiting on all agent endpoints |
| ADR-019 | ProblemDetails for errors, stable errorCodes, correlation IDs |

---

*Architecture document for M365 Copilot Integration R1*

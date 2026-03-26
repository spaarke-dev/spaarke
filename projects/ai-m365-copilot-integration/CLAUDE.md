# CLAUDE.md — M365 Copilot Integration (R1)

> **Project**: ai-m365-copilot-integration
> **Branch**: work/ai-m365-copilot-integration-r1
> **Last Updated**: 2026-03-26

## Project Context

This project integrates Spaarke's AI capabilities into M365 Copilot within Power Apps model-driven apps. It delivers a Declarative Agent (Tier 1) and Custom Engine Agent (Tier 2) that expose the full BFF API through the Copilot side pane.

**Key principle**: Agent gateway endpoints are **thin adapter facades** over existing BFF services. No new AI orchestration logic.

## Architecture Decisions

- **Declarative Agent + API Plugin** (not Copilot Studio) — three manifest files + OpenAPI spec → direct HTTPS to BFF
- **Full BFF API exposure** — OpenAPI spec covers all (or near-all) BFF endpoints
- **SPE `discoverabilityDisabled = true`** — all document access through BFF with per-matter authorization
- **Async + deep-link for long playbooks** — API plugin has timeout limits; deep-link to Analysis code page
- **Search-by-name primary doc discovery** — file attachments silently dropped for Custom Engine Agents
- **M365 Copilot = general chat UX** — replaces SprkChat for everything except Analysis Workspace

## Applicable ADRs

| ADR | Key Constraint |
|-----|----------------|
| ADR-001 | Minimal API pattern; no Azure Functions |
| ADR-008 | Endpoint filters for auth; no global middleware |
| ADR-010 | DI minimalism; ≤15 registrations; concrete types |
| ADR-013 | Extend BFF; AI Tool Framework; flow ChatHostContext |
| ADR-014 | Redis caching; scope by tenant; no streaming cache |
| ADR-015 | Minimum text to AI; log only identifiers |
| ADR-016 | Rate limit all AI endpoints; bound concurrency |
| ADR-019 | ProblemDetails for errors; stable errorCode |

## Key Resources

**Architecture**:
- `docs/architecture/AI-ARCHITECTURE.md` — Four-tier AI architecture
- `docs/architecture/AI-CHAT-STRATEGY-M365-COPILOT-VS-SPRKCHAT.md` — Copilot vs SprkChat
- `docs/architecture/ai-implementation-reference.md` — BFF code examples
- `docs/architecture/sdap-auth-patterns.md` — OBO flow patterns
- `docs/architecture/office-outlook-teams-integration-architecture.md` — Office/Teams auth

**Patterns**:
- `.claude/patterns/api/endpoint-definition.md` — API endpoint structure
- `.claude/patterns/api/endpoint-filters.md` — Authorization filters
- `.claude/patterns/api/error-handling.md` — ProblemDetails
- `.claude/patterns/auth/obo-flow.md` — OBO token exchange
- `.claude/patterns/auth/token-caching.md` — Token caching
- `.claude/patterns/ai/streaming-endpoints.md` — SSE patterns

**Constraints**:
- `.claude/constraints/ai.md` — AI/ML constraints
- `.claude/constraints/api.md` — API/BFF constraints
- `.claude/constraints/auth.md` — Auth constraints

## Existing BFF Services to Reuse

| Service | Endpoints | Agent Usage |
|---------|-----------|-------------|
| Chat | `/api/ai/chat/*` | Message routing, conversation management |
| Search | `/api/ai/search` | Document search from Copilot |
| Playbooks | `/api/ai/playbooks/*` | Listing, invocation, results |
| Analysis | `/api/ai/analysis/*` | Creation, execution, handoff |
| Context Mappings | `/api/ai/chat/context-mappings` | Document type → playbook resolution |
| RAG | `/api/ai/rag/search` | Semantic document retrieval |
| Documents | `/api/documents` | Document metadata |
| Workspace | `/api/workspace/*` | Matter, project, file queries |
| Communications | `/api/communications` | Email drafting |

## New Components

| Component | Location | Description |
|-----------|----------|-------------|
| `declarativeAgent.json` | `src/solutions/CopilotAgent/` | Agent manifest |
| `spaarke-api-plugin.json` | `src/solutions/CopilotAgent/` | API Plugin functions |
| `spaarke-bff-openapi.yaml` | `src/solutions/CopilotAgent/` | OpenAPI spec |
| Agent gateway endpoints | `src/server/api/Sprk.Bff.Api/Features/Agent/` | Adapter endpoints |
| AdaptiveCardFormatterService | `src/server/api/Sprk.Bff.Api/Features/Agent/` | Response formatter |
| HandoffUrlBuilder | `src/server/api/Sprk.Bff.Api/Features/Agent/` | Deep-link builder |
| SpaarkeAgentHandler | `src/server/api/Sprk.Bff.Api/Features/Agent/` | M365 Agents SDK handler |
| Adaptive Card templates | `src/solutions/CopilotAgent/cards/` | JSON card templates |
| Bot Service | `infrastructure/bot-service/` | Bicep templates |

## MUST Rules

- MUST use existing BFF services — agent endpoints are adapters only
- MUST keep SPE `discoverabilityDisabled = true`
- MUST use Adaptive Card schema 1.5
- MUST use endpoint filters for agent endpoint auth (ADR-008)
- MUST rate limit all agent endpoints (ADR-016)
- MUST NOT log document content or prompts (ADR-015)
- MUST NOT create new AI orchestration logic
- MUST NOT bypass SPE authorization

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: When executing tasks in this project, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually.

When user says "work on task X", "continue", "next task", etc.:
1. Read `projects/ai-m365-copilot-integration/tasks/TASK-INDEX.md`
2. Find the specified or next pending task (🔲)
3. Invoke Skill tool with `skill="task-execute"` and task file path
4. Let task-execute orchestrate the full protocol

---

*Project-specific AI context for M365 Copilot Integration R1*

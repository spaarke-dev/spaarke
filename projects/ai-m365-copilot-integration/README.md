# M365 Copilot Integration (R1)

> **Last Updated**: 2026-03-26
>
> **Status**: In Progress

## Overview

Integrate Spaarke's AI capabilities into M365 Copilot within Power Apps model-driven apps. Delivers a Declarative Agent with API Plugin (Tier 1) and Custom Engine Agent (Tier 2) that expose the full Spaarke BFF API through the Copilot side pane — enabling document search, playbook invocation, matter queries, email drafting, and Analysis Workspace handoff.

M365 Copilot replaces SprkChat as the general-purpose chat UX across all MDA pages. SprkChat is repositioned as a special-purpose AI companion exclusively for the Analysis Workspace.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | 4-phase implementation plan with parallel execution groups |
| [Design Spec](./design.md) | Original design document |
| [AI Spec](./spec.md) | AI-optimized implementation specification |
| [Task Index](./tasks/TASK-INDEX.md) | Task breakdown, dependencies, and parallel groups |
| [Project CLAUDE.md](./CLAUDE.md) | AI context for task execution |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Development |
| **Progress** | 0% |
| **Target Date** | June 2026 |
| **Completed Date** | — |
| **Owner** | Spaarke AI Team |

## Problem Statement

Spaarke's AI capabilities are only accessible through SprkChat in the Analysis Workspace. M365 Copilot goes GA in model-driven apps (April 13, 2026). Without integration, Copilot appears alongside Spaarke with zero knowledge of SPE documents, playbooks, or domain-specific analysis — creating a confusing dual-AI experience. This project makes Copilot the gateway to Spaarke intelligence.

## Solution Summary

Build a Declarative Agent with API Plugin (Tier 1) that exposes the full BFF API surface via OpenAPI spec, plus a Custom Engine Agent (Tier 2) with agent gateway adapter endpoints, Adaptive Card formatter, and SSO token flow. Agent endpoints are thin facades over existing BFF services — no new AI orchestration logic. Long-running playbooks use async pattern or deep-link to Analysis code page.

## Graduation Criteria

The project is considered **complete** when:

- [ ] Declarative Agent deployed to org app catalog, loads in MDA Copilot side pane
- [ ] Document search returns correct results from SPE via BFF with authorization enforced
- [ ] Playbook invocation from Copilot returns Adaptive Card results or deep-links to Analysis code page
- [ ] "Open in Workspace" handoff opens Analysis Workspace with correct context
- [ ] BYOK deployment templates enable customer-hosted infrastructure
- [ ] Full BFF API exposure via OpenAPI spec

## Scope

### In Scope

- Declarative Agent manifest files (`declarativeAgent.json`, API plugin, OpenAPI spec)
- Custom Engine Agent (`SpaarkeAgentHandler`, agent gateway adapter endpoints)
- Adaptive Card templates and formatter service
- SSO token flow (M365 → OBO → BFF → Graph/Dataverse)
- Azure Bot Service registration and channel configuration
- Technical spikes (ConversationFileReference, Action.Submit, end-to-end file pipeline)
- Enterprise readiness (error handling, telemetry, admin controls, BYOK deployment)

### Out of Scope

- Teams bot, Outlook plugin, Copilot Chat standalone (R2)
- MCP server / Tier 3 (R2)
- Agent 365 governance / Tier 4 (R2)
- SprkChat modifications (separate project)
- Power Pages integration (R2)

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Agent endpoints are thin adapters over existing BFF services | Existing AI services already handle all operations; no new AI logic needed | ADR-001, ADR-013 |
| Full BFF API surface exposed via OpenAPI | Maximizes Copilot's capability set | — |
| Async pattern + deep-link for long playbooks | API plugin response timeout limits; Analysis code page provides rich UX | — |
| SPE containers remain `discoverabilityDisabled = true` | BFF enforces per-matter/per-project authorization | ADR-013 |
| Direct API Plugin path (no Copilot Studio) | Three manifest files vs. full Copilot Studio project; simpler, our BFF is the brain | — |
| Search-by-name as primary document discovery | File attachments silently dropped for Custom Engine Agents; search always works | — |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| OBO token flow complexity | High | Medium | Existing OBO pattern proven in Outlook add-in |
| Adaptive Card limitations for rich AI output | Medium | High | "Light interaction + handoff" pattern |
| Customer confusion: two AI surfaces | Medium | Medium | Clear UX: Copilot = general; SprkChat = deep analysis |
| API plugin response timeout too short | Medium | Medium | Async pattern + deep-link fallback |
| Full OpenAPI spec maintenance burden | Low | High | Generate from code annotations |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| BFF API with AI endpoints | Internal | Ready | All services exist |
| PlaybookExecutionEngine | Internal | Ready | Production |
| Azure OpenAI, AI Search | External | Ready | Provisioned |
| M365 Copilot GA in MDA | External | Ready (April 13) | Required for production deployment |
| M365 Agents SDK | External | Ready (GA) | For Custom Engine Agent |
| M365 Agents Toolkit | External | Ready | VS Code extension for packaging |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Spaarke AI Team | Overall accountability |
| Developer | Claude Code (AI) | Implementation |
| Reviewer | Product Owner | Design review, acceptance |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-03-26 | 1.0 | Initial project setup from spec.md | Claude Code |

---

*Project initialized via `/project-pipeline` skill*

# Task Index — M365 Copilot Integration (R1)

> **Last Updated**: 2026-03-26
> **Total Tasks**: 31
> **Status**: ✅ 23/31 complete

## Status Legend

| Icon | Status |
|------|--------|
| 🔲 | Not started |
| 🔄 | In progress |
| ✅ | Complete |
| 🚫 | Blocked |
| ⏭️ | Skipped |

---

## Phase 1: Foundation — Spikes + Declarative Agent MVP

| # | Task | Status | Tags | Dependencies | Parallel Group |
|---|------|--------|------|-------------|----------------|
| 001 | Project Scaffolding and Folder Structure | ✅ | foundation, setup | none | Serial |
| 002 | M365 Agents Toolkit Setup and Dev Environment | ✅ | foundation, setup, m365 | 001 | Serial |
| 003 | Spike: ConversationFileReference Validation | 🔲 | spike, m365 | 002 | **Group A** |
| 004 | Spike: Action.Submit in API Plugin Responses | 🔲 | spike, m365, adaptive-cards | 002 | **Group A** |
| 005 | Spike: End-to-End File Pipeline | 🔲 | spike, m365, documents | 003 | Serial (after Spike 1) |
| 006 | BFF API OpenAPI Specification | ✅ | api, openapi, declarative-agent | 001 | **Group B** |
| 007 | Declarative Agent Manifest | ✅ | m365, declarative-agent, manifest | 001 | **Group B** |
| 008 | API Plugin Function Definitions | ✅ | m365, api-plugin, declarative-agent | 006 | Serial (after OpenAPI) |
| 009 | Sideload and Validate Declarative Agent | 🔲 | m365, declarative-agent, validation | 006, 007, 008 | Serial |

## Phase 2: Agent Gateway + Auth

| # | Task | Status | Tags | Dependencies | Parallel Group |
|---|------|--------|------|-------------|----------------|
| 010 | Agent Gateway Adapter Endpoints | ✅ | bff-api, api, agent-gateway | 009 | **Group C** |
| 011 | SSO Token Flow for M365 Agent | ✅ | auth, sso, obo | 009 | **Group C** |
| 012 | Azure Bot Service Registration | ✅ | infrastructure, azure, bot-service | 002 | **Group C** |
| 013 | SpaarkeAgentHandler Implementation | ✅ | bff-api, m365, agents-sdk | 010, 011 | **Group D** |
| 014 | Multi-Turn Conversation Support | ✅ | bff-api, agent-gateway, chat | 010 | **Group D** |
| 015 | Agent Gateway Integration Tests | 🔲 | testing, integration-test | 010, 011, 013 | Serial |

## Phase 3: Rich Interactions + Playbook Integration

| # | Task | Status | Tags | Dependencies | Parallel Group |
|---|------|--------|------|-------------|----------------|
| 016 | Adaptive Card Templates — Documents + Matters | ✅ | adaptive-cards, m365, ui | 010 | **Group E** |
| 017 | Adaptive Card Templates — Playbooks + Analysis | ✅ | adaptive-cards, m365, ui | 010 | **Group E** |
| 018 | Adaptive Card Templates — Communication + Handoff | ✅ | adaptive-cards, m365, ui | 010 | **Group E** |
| 019 | AdaptiveCardFormatterService | ✅ | bff-api, adaptive-cards, service | 010, 016, 017, 018 | Serial |
| 020 | HandoffUrlBuilder Service | ✅ | bff-api, handoff, deep-links | 010 | **Group E** |
| 021 | Playbook Invocation Flow | ✅ | bff-api, playbooks, agent-gateway | 010, 019 | Serial |
| 022 | Email Drafting via Agent | ✅ | bff-api, communications | 010, 019 | **Group E-2** |
| 023 | Async Playbook Pattern + Status Endpoint | ✅ | bff-api, api, async, playbooks | 021 | Serial |

## Phase 4: Enterprise Readiness

| # | Task | Status | Tags | Dependencies | Parallel Group |
|---|------|--------|------|-------------|----------------|
| 024 | Error Handling and Graceful Degradation | ✅ | bff-api, error-handling, resilience | 013, 019 | **Group F** |
| 025 | Telemetry and Interaction Logging | ✅ | telemetry, logging, observability | 013 | **Group F** |
| 026 | Admin Controls and Configuration | ✅ | admin, configuration, dataverse | 010 | **Group F** |
| 027 | BYOK Deployment Templates | ✅ | infrastructure, bicep, byok | 012 | **Group F** |
| 028 | User Documentation | ✅ | documentation, user-guide | 023 | **Group F** |
| 029 | Agent Gateway Unit Tests | ✅ | testing, unit-test | 019, 020, 021 | **Group F** |
| 030 | End-to-End Integration Validation | 🔲 | testing, e2e, validation | 024, 025, 026 | Serial |
| 031 | Project Wrap-Up | 🔲 | wrap-up, documentation | 030 | Serial |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| **A** | 003, 004 | 002 complete | Phase 1 spikes — independent investigations |
| **B** | 006, 007 | 001 complete | Declarative Agent manifests — independent files |
| **C** | 010, 011, 012 | 009 complete (010, 011), 002 complete (012) | Phase 2 foundation — different layers |
| **D** | 013, 014 | 010 complete (+ 011 for 013) | Phase 2 handler + conversation — different files |
| **E** | 016, 017, 018, 020 | 010 complete | Card templates + handoff — independent outputs |
| **E-2** | 022 | 010, 019 complete | Email drafting (runs alongside Group E if 019 done) |
| **F** | 024, 025, 026, 027, 028, 029 | Various Phase 3 tasks | Enterprise readiness — all independent concerns |

## Critical Path

```
001 → 002 → [003, 004] → 005
                ↓
001 → 006 → 008 → 009 → [010, 011] → 013 → 015
       ↓                     ↓
      007 ──────────→ 009   [016, 017, 018, 020] → 019 → 021 → 023
                              ↓                                   ↓
                          [022] (after 019)              [024, 025, 026, 027, 028, 029] → 030 → 031
```

**Longest chain**: 001 → 002 → 006 → 008 → 009 → 010 → 019 → 021 → 023 → 030 → 031 (11 serial tasks)

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 003 | ConversationFileReference may not be Graph-resolvable | Search-by-name fallback |
| 004 | Action.Submit may not work in API plugin responses | Text-based fallback |
| 011 | OBO token flow multi-hop complexity | Reuse proven Outlook add-in pattern |
| 006 | Full OpenAPI spec is large and complex | Iterative; start with core endpoints |

---

*Task index for M365 Copilot Integration R1. Updated by task-execute skill.*

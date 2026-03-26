# SprkChat Analysis Workspace Command Center

> **Last Updated**: 2026-03-25
>
> **Status**: In Progress

## Overview

Transform SprkChat from a text-only chat interface into a contextual command center for the Analysis Workspace. The system provides three interaction tiers — natural language (primary), quick-action chips (discoverable), and slash commands (power users) — all backed by a smart routing layer that enriches user messages with structured context signals before the BFF AI model selects the appropriate playbook tools.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan with phase breakdown |
| [Design Spec](./design.md) | Original design specification |
| [AI Spec](./spec.md) | AI-optimized implementation specification |
| [Task Index](./tasks/TASK-INDEX.md) | Task breakdown and status |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Development |
| **Progress** | 0% |
| **Target Date** | — |
| **Completed Date** | — |
| **Owner** | Ralph Schroeder |

## Problem Statement

SprkChat currently provides basic text-only AI chat with no awareness of the user's current analysis context. Users must manually describe what they're looking at, and the chat appears globally on pages where it's not relevant (Corporate Workspace). There's no way to discover available AI capabilities without knowing the right natural language prompt.

## Solution Summary

Implement a 5-phase enhancement: First enforce scope by removing SprkChat from non-analysis pages and implementing side pane lifecycle management. Then add client-side context enrichment, a dynamic slash command menu populated from playbook capabilities, quick-action chips, compound actions with plan preview and approval gates, and finally parameterized prompt templates with a playbook library browser.

## Graduation Criteria

The project is considered **complete** when:

- [ ] SprkChat does NOT appear on Corporate Workspace or any non-analysis page
- [ ] Side pane lifecycle: navigating away closes pane; returning reopens with previous session
- [ ] Natural language routes to correct tool without slash command
- [ ] Slash menu is dynamic — commands change when playbook switches
- [ ] Context enrichment (editor selection, document type, conversation phase) included in BFF payload
- [ ] Compound actions show plan preview before executing
- [ ] Email works end-to-end: draft → refine → send via sprk_communication module

## Scope

### In Scope

- Phase 0: Remove SprkChat from Corporate Workspace; side pane lifecycle management
- Phase 1: Smart routing with context enrichment; dynamic command registry; SlashCommandMenu; system commands
- Phase 2: Quick-action chips populated from playbook capabilities
- Phase 3: Compound actions with plan preview; email drafting; write-back with confirmation
- Phase 4: Parameterized prompt templates; playbook library browser in chat

### Out of Scope

- Corporate Workspace AI interactions (M365 Copilot integration project)
- Matter-level Q&A outside analysis context
- Rich response cards with custom entity rendering (deferred)
- Admin-defined context actions via new Dataverse table (dropped — playbook capabilities suffice)
- M365 Copilot integration, Adaptive Cards, Custom Engine Agent

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| SprkChat is a Code Page (React 18+), not PCF | Standalone side pane, not field-bound | ADR-006 |
| Hybrid routing (client enrichment + server AI) | Client adds structured signals; BFF model routes | — |
| Remove global ribbon button entirely | SprkChat only launches from AnalysisWorkspace | — |
| No new Dataverse table for context actions | Playbook capabilities multi-select is sufficient | — |
| Email via BFF → Graph API (sprk_communication) | Reuse existing email infrastructure | — |
| Fluent v9 exclusively, dark mode required | Design system consistency | ADR-021 |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Slash command namespace collisions between playbooks | Med | Med | Prefix with playbook name or merge |
| Compound action partial failure rollback | High | Low | Show partial results + error state |
| Side pane lifecycle race conditions | Med | Med | Dual mechanism (useEffect + poll fallback) |
| Context enrichment payload size | Low | Low | Cap at < 1KB per NFR-07 |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Context Awareness (Project #1) | Internal | Complete | Context mappings, page type detection |
| SprkChat Workspace Companion | Internal | Complete | Analysis workspace integration |
| SprkChat Platform Enhancement R2 | Internal | Complete | Markdown, SSE, playbook dispatch |
| sprk_communication module | Internal | Production | Email composition + Graph API |
| Azure OpenAI | External | Production | AI model for routing |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Ralph Schroeder | Overall accountability |
| Developer | Claude Code (AI) | Implementation |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-03-25 | 1.0 | Initial project setup via /project-pipeline | Claude Code |

---

*Template version: 1.0 | Based on Spaarke development lifecycle*

# Legal Operations Workspace (Home Corporate) R1

> **Last Updated**: 2026-02-18
>
> **Status**: In Progress

## Overview

Build a Legal Operations Workspace — a single-page dashboard embedded as a Power Apps Custom Page in the Model-Driven App. The workspace provides legal operations managers with portfolio health metrics, a prioritized activity feed, a smart to-do list with transparent priority/effort scoring, a portfolio browser, quick-action cards, and AI-powered summaries via the Spaarke AI Playbook platform.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan with 5 phases and WBS |
| [Design Spec](./design.md) | Original UI/UX design document (v2.0) |
| [AI Spec](./spec.md) | AI-optimized implementation specification |
| [Task Index](./tasks/TASK-INDEX.md) | Task breakdown and status |
| [Screenshots](./screenshots/) | 15 prototype mockup screenshots |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Deployment |
| **Progress** | 95% |
| **Target Date** | 2026-02-18 |
| **Completed Date** | — |
| **Owner** | Spaarke Dev Team |

## Problem Statement

Legal operations managers lack a unified dashboard view of their portfolio. They must navigate between multiple entity views to understand portfolio health, track overdue items, manage priorities, and take action. This context-switching reduces efficiency and delays critical responses to at-risk matters.

## Solution Summary

A single Custom Page workspace embedded in the MDA that surfaces portfolio health metrics, an activity feed of Events, a smart to-do list with transparent priority/effort scoring, and quick-action cards that launch dialogs or the AI Playbook Analysis Builder. All UI uses Fluent UI v9 with mandatory dark mode support. Data access follows a hybrid pattern — Xrm.WebApi for simple entity queries, BFF endpoints for complex aggregations and AI integration.

## Graduation Criteria

The project is considered **complete** when:

- [x] All 7 build blocks render correctly in Custom Page within MDA
- [x] Light and dark mode both work with zero hardcoded colors
- [x] All 7 Get Started action cards are functional (Create Matter dialog + 6 Analysis Builder entry points)
- [x] Portfolio Health Summary shows correct aggregated metrics from live Dataverse data
- [x] Updates Feed displays Events with filtering, sorting, flag-to-todo, and AI Summary
- [x] Smart To Do shows prioritized items with correct priority/effort scores and badges
- [x] Create New Matter wizard completes full flow including file upload and AI pre-fill
- [x] My Portfolio Widget shows matters/projects/documents with working MDA navigation
- [x] All UI passes WCAG 2.1 AA accessibility (keyboard nav, ARIA labels, contrast)
- [x] PCF bundle size < 5MB
- [x] Page initial load < 3 seconds
- [x] Priority and effort scoring produces correct results for all test scenarios

## Scope

### In Scope

- Custom Page shell with FluentProvider, responsive layout, theme toggle
- Block 1: Get Started (7 action cards) + Quick Summary briefing
- Block 2: Portfolio Health Summary (4 metric cards)
- Block 3: Updates Feed (chronological Event stream with filters and AI Summary)
- Block 4: Smart To Do (prioritized work queue with scoring)
- Block 5: My Portfolio Widget (Matters/Projects/Documents tabs)
- Block 6: Create New Matter Dialog (multi-step wizard with AI pre-fill)
- Block 7: Notification Panel (slide-out Drawer)
- BFF endpoints for portfolio aggregation, health metrics, AI integration
- Priority and effort scoring engine (server-side)
- 6 action card integrations with existing AI Playbook Analysis Builder

### Out of Scope

- New Dataverse entities (uses existing only)
- Calendar widget (navigates to MDA calendar)
- Mobile-native app
- Real-time SignalR push (polling for R1)
- Drag-and-drop reorder in To Do
- AI-recommended to-do items (future)

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Power Apps Custom Page (React 18) | Workspace needs full React app, not a form-bound PCF | Exception to ADR-021/022 |
| Hybrid data access (Xrm.WebApi + BFF) | Simple queries client-side, aggregations server-side | ADR-001 |
| Action cards reuse Analysis Builder | 6 cards launch existing Playbook system, not new dialogs | ADR-013 |
| Fluent UI v9 only, dark mode mandatory | Platform standard, accessibility requirement | ADR-021 |
| Agent teams for parallel execution | Independent blocks enable concurrent implementation | — |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Custom Page auth to BFF unclear | High | Medium | Investigate MSAL token acquisition from Custom Page iframe early |
| Bundle size exceeds 5MB | Medium | Low | Code-split by block, lazy load dialogs |
| Scoring formula edge cases | Medium | Medium | Comprehensive unit tests with known inputs/outputs |
| 6 action card specs incomplete | Medium | Low | Cards launch existing Analysis Builder — minimal new UX |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Dataverse entities (sprk_event, sprk_matter, etc.) | Internal | Ready | All fields exist per design doc |
| AI Playbook platform | Internal | Ready | Existing Analysis Builder infrastructure |
| BFF API (Sprk.Bff.Api) | Internal | Ready | Extend with new endpoints |
| Fluent UI v9 | External | Ready | Latest stable |
| SharePoint Embedded | External | Ready | For Create Matter file uploads |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Spaarke Dev Team | Overall accountability |
| Developer | Claude Code (Agent Teams) | Implementation via parallel agents |
| Reviewer | Human Developer | Code review, design review |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-02-17 | 0.1 | Initial design document (design.md) | Owner |
| 2026-02-18 | 0.2 | AI spec (spec.md) + project initialization | Claude Code |
| 2026-02-18 | 0.95 | All 42 tasks executed, pending deployment and verification | Claude Code |

---

*Template version: 1.0 | Based on Spaarke development lifecycle*

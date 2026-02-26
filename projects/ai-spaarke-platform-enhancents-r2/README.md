# SprkChat Interactive Collaboration (R2)

> **Last Updated**: 2026-02-25
>
> **Status**: In Progress

## Overview

Transform SprkChat from an embedded, read-only AI assistant into a **platform-wide AI collaborator** deployed as a standalone Dataverse side pane. SprkChat becomes accessible on any form (Matters, Projects, Invoices, Analysis records) with playbook-governed capabilities. The Analysis Workspace migrates from PCF (React 16) to a Code Page (React 19) enabling streaming write sessions, diff compare views, and modern concurrent rendering.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan with 4 phases, 3-track parallelism |
| [Design Spec](./spec.md) | AI-optimized specification (R2) |
| [Design Doc](./design.md) | Full design document (R2) |
| [Task Index](./tasks/TASK-INDEX.md) | Task breakdown and status |
| [AI Context](./CLAUDE.md) | Project-specific AI instructions |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Development |
| **Progress** | 0% |
| **Owner** | Ralph Schroeder / Claude Code |

## Problem Statement

SprkChat is locked inside the Analysis Workspace PCF as a read-only assistant. Users expect a platform-wide AI collaborator that writes documents interactively, re-processes analysis on demand, offers structured actions via a command palette, and shows proposed changes via diff review — all governed by the existing playbook model.

## Solution Summary

Nine work packages organized for 3-track parallel agent team execution: (A) SprkChat side pane Code Page, (B) streaming write engine, (C) Analysis Workspace Code Page migration, (D) action menu/command palette, (E) re-analysis pipeline, (F) diff compare view, (G) selection-based revision, (H) suggested follow-ups + citations, (I) web search + multi-document support. All built on the existing BFF API AI tool framework with 0 additional DI registrations.

## Graduation Criteria

The project is considered **complete** when:

- [ ] SprkChat accessible as side pane on Matter, Project, and Analysis forms
- [ ] AI streams edits into editor token-by-token with <100ms latency
- [ ] Diff view shows before/after with Accept/Reject/Edit workflow
- [ ] Re-analysis reprocesses full document with progress indicator
- [ ] Action menu responds to `/` in <200ms with keyboard navigation
- [ ] Analysis Workspace runs as Code Page (React 19, no PCF dependency)
- [ ] Playbook capabilities govern available tools and actions
- [ ] All UI supports light, dark, and high-contrast modes
- [ ] Packages A, B, D executable in parallel with no file conflicts
- [ ] 0 additional DI registrations (ADR-010)
- [ ] All legacy chat code removed (`useLegacyChat`, deprecated endpoints)

## Scope

### In Scope

- **Package A**: SprkChat Side Pane — Code Page, `Xrm.App.sidePanes`, cross-pane `SprkChatBridge`
- **Package B**: Streaming Write Engine — `StreamingInsertPlugin`, `WorkingDocumentTools`, document history
- **Package C**: Analysis Workspace Code Page Migration — React 19, 2-panel layout, legacy cleanup
- **Package D**: Action Menu / Command Palette — `/` trigger, playbook-governed, keyboard navigation
- **Package E**: Re-Analysis Pipeline — `AnalysisExecutionTools`, full document reprocessing
- **Package F**: Diff Compare View — side-by-side/inline diff, Accept/Reject/Edit
- **Package G**: Selection-Based Revision — cross-pane selection flow, editor selection API
- **Package H**: Suggested Follow-Ups + Citations — contextual chips, citation popovers
- **Package I**: Web Search + Multi-Document — Azure Bing Search, multi-document context

### Out of Scope

- PlaybookBuilder AI Assistant convergence (R3+)
- Real-time collaborative editing (multiple simultaneous users)
- Voice input for chat
- Mobile/responsive layout for the workspace
- Dataverse analysis persistence (separate: R1 Task 032)
- Custom playbook creation from within SprkChat
- Office Add-in integration for SprkChat

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| SprkChat as standalone side pane Code Page | Available on any form, persists across navigation, independent deployment | ADR-006 |
| Analysis Workspace big-bang migration to Code Page | React 19 for streaming UX, full viewport control, clean separation from SprkChat | ADR-006 |
| Independent auth per Code Page pane | Security — no auth tokens via BroadcastChannel | ADR-008 |
| BroadcastChannel for cross-pane communication | Synchronous same-origin messaging; postMessage fallback | — |
| 0 additional DI registrations — factory-instantiated tools | ADR-010 budget compliance (12/15 used) | ADR-010 |
| Playbook capability declarations on Dataverse entity | Admin-configurable per playbook, filters tools + actions | ADR-013 |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Streaming write causes Lexical state corruption | High | Medium | Isolated `StreamingInsertPlugin`; extensive testing |
| Code Page migration breaks Dataverse form integrations | Medium | Medium | Feature flag; maintain PCF as fallback during transition |
| Cross-pane BroadcastChannel not available | High | Low | `window.postMessage` fallback; runtime detection |
| Re-analysis token costs exceed budget | High | Medium | CostControl middleware; confirmation prompt for expensive ops |
| React 19 bundle size increases load time | Medium | Low | Code splitting; lazy load non-critical components |
| Diff view slow for large documents | Medium | Medium | Virtual rendering; diff only visible sections |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| R1 bug fixes deployed | Internal | Ready | UseFunctionInvocation, tenantId, document context |
| Azure Bing Search API provisioned | External | Pending | Required for Package I |
| Dataverse Playbook entity schema update | Internal | Pending | Multi-select capability field for Package D |
| Lexical streaming insert API compatibility | External | Pending | Verify with latest Lexical version |
| `Xrm.App.sidePanes` API availability | External | Ready | Verified in current Dataverse environment |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Ralph Schroeder | Overall accountability |
| Developer | Claude Code (Agent Teams) | Implementation via 3-track parallel execution |
| Reviewer | Code Review Skill | Automated code review + ADR compliance |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-02-25 | 1.0 | Initial project setup via `/project-pipeline` | Claude Code |

---

*Template version: 1.0 | Based on Spaarke development lifecycle*

# UI Create Wizard Enhancements R1

> **Last Updated**: 2026-03-23
>
> **Status**: In Progress

## Overview

Delivers 18 enhancements to the Spaarke create wizard and workspace ecosystem following UAT feedback from the UI Dialog & Shell Standardization project. Covers wizard flow improvements (Associate To, reworked follow-ons), MSAL auth standardization, WorkspaceShell extraction, Code Page consolidation, theme/color token compliance, React 19 upgrade, and runtime bug fixes.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan with 6 phases |
| [Design Spec](./design.md) | Original design document (UAT feedback) |
| [AI Spec](./spec.md) | AI-optimized specification |
| [Task Index](./tasks/TASK-INDEX.md) | Task breakdown and status |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Development |
| **Progress** | 0% |
| **Owner** | Ralph Schroeder |

## Problem Statement

UAT testing of the UI Dialog & Shell Standardization project revealed UX gaps (missing Associate To step, broken AI pre-fill, inconsistent dialog sizing), broken API pipelines (Xrm.WebApi unavailable in Code Page iframe, double `/api/` prefix), and consolidation opportunities (AnalysisBuilder merge, WorkspaceShell extraction, theme token compliance). Hard-coded colors and OS-level theme detection cause inconsistent rendering across environments.

## Solution Summary

Add "Associate To" first step with Dataverse lookup side pane to CreateMatter/CreateProject wizards. Standardize MSAL auth across all Code Pages. Extract WorkspaceShell as shared component. Consolidate PlaybookLibrary + AnalysisBuilder. Move analysis creation to BFF API. Replace hard-coded colors with Fluent v9 tokens. Remove OS prefers-color-scheme fallback. Upgrade all Code Pages to React 19. Fix runtime bugs (overdue badge, SprkChat URL).

## Graduation Criteria

The project is considered **complete** when:

- [ ] "Associate To" step works with Dataverse lookup side pane in CreateMatter and CreateProject
- [ ] AI pre-fill triggers after file upload with MSAL auth across all wizard Code Pages
- [ ] All dialogs open at 60%x70% dimensions
- [ ] Lookup fields use Dataverse side pane (not inline dropdown)
- [ ] "Assign Work" and "Create Event" follow-ons create correct records with relationships
- [ ] WorkspaceShell cards maintain square aspect ratio from 768px to 2560px
- [ ] PlaybookLibrary handles all launch contexts (AnalysisBuilder retired)
- [ ] Summarize -> Analysis flow creates documents and opens document selector
- [ ] Analysis scope associations created via BFF API (no Xrm.WebApi errors)
- [ ] All Code Pages build and run on React 19
- [ ] Zero hard-coded hex/rgb/rgba colors in .ts/.tsx files (tokens only)
- [ ] All 6 duplicated ThemeProvider files replaced with shared utility
- [ ] Theme defaults to light mode (no OS prefers-color-scheme fallback)
- [ ] No Spaarke console errors in DevTools
- [ ] Quick Summary overdue badge and SprkChat context-mappings URL work correctly

## Scope

### In Scope

- E-01: Associate To step (CreateMatter, CreateProject)
- E-02: MSAL auth fix for AI pre-fill pipeline
- E-03: Dialog sizing standardization (60%x70%)
- E-04: Dataverse lookup side pane for lookup fields
- E-05: Assign Work follow-on (replaces Assign Resources)
- E-06: Create Event follow-on (replaces AI Summary)
- E-07: Send Notification Email rename
- E-08: Analysis creation via BFF API
- E-09: WorkspaceShell extraction to shared library
- E-10: Secure Project section position
- E-11: Remove duplicate title bars
- E-12: Quick Summary overdue badge fix
- E-13: SprkChat double /api/ prefix fix
- E-14: PlaybookLibrary + AnalysisBuilder consolidation
- E-15: Summarize -> Analysis document creation flow
- E-16: BFF API analysis create endpoint
- E-17: Theme cascade fix (remove OS prefers-color-scheme)
- E-18: Hard-coded color replacement with Fluent v9 tokens
- React 19 upgrade for all Code Pages

### Out of Scope

- CreateEvent, CreateTodo, CreateWorkAssignment wizard changes
- FindSimilar enhancements
- Power Pages SPA wizard integration
- New entity wizard creation
- Ribbon/command bar button changes
- Multi-select document picker

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| MSAL as single auth path | Two auth paths hide issues; MSAL is canonical | ADR-013 |
| WorkspaceShell in shared library | Reuse across workspaces; React 19 only (Code Pages) | ADR-012 |
| React 19 for Code Pages | Stable 15+ months; Fluent UI supports react <20.0.0 | ADR-022 |
| Move analysis to BFF API | Xrm.WebApi.execute unavailable in Code Page iframe | ADR-001, ADR-013 |
| No OS theme fallback | App-level theme only; default light when unset | ADR-021 |
| Fluent v9 tokens only | No hard-coded colors; ensures dark mode compliance | ADR-021 |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| React 19 breaking changes | High | Low | Fluent v9 supports react <20.0.0; incremental upgrade |
| MSAL silent flow in iframe | Medium | Medium | DocumentUploadWizard is reference implementation |
| WorkspaceShell responsive edge cases | Medium | Medium | Test from 768px to 2560px; CSS Grid with aspect-ratio |
| Hard-coded color grep may miss patterns | Medium | Low | Systematic search with acceptance criterion grep |
| Xrm.Utility.lookupObjects behavior | Medium | Medium | Test in Dataverse model-driven app context |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| UI Dialog & Shell Standardization | Internal | Complete | All wizards are standalone Code Pages |
| @spaarke/auth MSAL strategy | Internal | Ready | Reference impl in DocumentUploadWizard |
| WizardShell hideTitle prop | Internal | Ready | Already implemented |
| Fluent UI v9 + React 19 compat | External | Ready | peerDependencies react <20.0.0 |

---

*Generated by Claude Code project-pipeline*

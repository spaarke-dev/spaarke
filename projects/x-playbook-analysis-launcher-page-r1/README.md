# Playbook & Analysis Launcher Page R1

> **Status**: Closed (2026-03-09) — Core implementation complete; see [PROJECT-CLOSURE.md](PROJECT-CLOSURE.md)
> **Branch**: `work/playbook-analysis-launcher-page-r1`
> **Started**: 2026-03-04

## Overview

Replace the AnalysisBuilder PCF control (v2.9.2) with two purpose-built React 18 experiences sharing a common Playbook component library:

1. **Analysis Builder Code Page** — Standalone dialog launched from Document form subgrid ("+New Analysis"). Two-tab UI: Playbook selection vs. Custom Scope. Creates `sprk_analysis` records.

2. **Quick Start Playbook Wizards** — Multi-step wizard dialogs embedded in Corporate Workspace. Each Get Started action card launches a wizard: upload → analyze → follow-up.

## Architecture

- **Shared Playbook Library**: `src/solutions/LegalWorkspace/src/components/Playbook/` — PlaybookCardGrid, ScopeConfigurator, PlaybookService, AnalysisService, types
- **Analysis Builder Code Page**: `src/solutions/AnalysisBuilder/` — Vite + singlefile build → `sprk_analysisbuilder.html`
- **Quick Start Wizards**: `src/solutions/LegalWorkspace/src/components/QuickStart/` — Uses WizardShell, config-driven per-card
- **Command Bar**: Reuse existing `sprk_analysis_commands.js` — update one function (`openAnalysisBuilderDialog`)

## Key Decisions

- Two shells (code page + embedded workspace wizards), shared component library (Option C — same source tree)
- Reuse existing upload components (FileUploadZone, MultiFileUploadService, EntityCreationService) — zero new upload code
- App-level theming (localStorage → URL → navbar → system), NOT OS `prefers-color-scheme`
- Portable Playbook Library — QuickStartWizardDialog accepts `intent` string, config-driven

## Graduation Criteria

- [ ] Analysis Builder code page opens from Document subgrid "+New Analysis"
- [ ] Playbook selection + Custom Scope tabs functional
- [ ] Analysis records created with correct N:N relationships
- [ ] All 5 Quick Start wizard cards open their respective wizard
- [ ] Upload step works via existing SPE services
- [ ] Follow-up actions functional (email, share, assign, navigate)
- [ ] Dark mode works in both experiences (ADR-021)
- [ ] Playbook Library portable — no workspace-specific imports
- [ ] AnalysisBuilder PCF retired (PCF removed, Custom Page removed, command bar updated)
- [ ] No upload code duplication

## Quick Links

- [Design Specification](spec.md)
- [Implementation Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Project Context](CLAUDE.md)

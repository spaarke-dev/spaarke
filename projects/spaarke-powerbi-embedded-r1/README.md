# Power BI Embedded Reporting R1

> **Status**: In Progress
> **Branch**: `work/spaarke-powerbi-embedded-r1`
> **Created**: 2026-03-31
> **Module**: Reporting

## Overview

Embed Power BI reports and dashboards into Spaarke's MDA via a "Reporting" Code Page. App Owns Data with SP profiles, Import mode with scheduled Dataverse refresh, in-browser report authoring, 4-layer security, and deployment pipeline for .pbix templates.

## Graduation Criteria

- [ ] Reporting Code Page renders embedded PBI report with BU RLS
- [ ] Report dropdown shows catalog from sprk_report entity
- [ ] In-browser report authoring (create, edit, save, save-as)
- [ ] Module gated by sprk_ReportingModuleEnabled env var
- [ ] User access controlled by sprk_ReportingAccess security role
- [ ] 5 standard reports deployed via pipeline
- [ ] Works in all 3 deployment models
- [ ] No per-user PBI license required

## Quick Links

- [Implementation Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Specification](spec.md)
- [Design](design.md)

# Events and Workflow Automation R1

> **Last Updated**: 2026-02-01
>
> **Status**: Complete

## Overview

This project implements an event management system for Spaarke's legal practice management platform, enabling scheduled activities, deadlines, reminders, and notifications associated with core business entities. It delivers two key platform capabilities: the **Association Resolver Framework** (addressing Dataverse polymorphic lookup limitations) and the **Field Mapping Framework** (admin-configurable field inheritance between parent and child records).

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan with phases and WBS |
| [Task Index](./tasks/TASK-INDEX.md) | Task breakdown and status tracking |
| [Design Spec](./spec.md) | AI-optimized specification |
| [Original Design](./design.md) | Human design document |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Complete |
| **Progress** | 100% |
| **Target Date** | TBD |
| **Completed Date** | 2026-02-01 |
| **Owner** | Development Team |

## Problem Statement

Spaarke's legal practice management platform needs a centralized event management system to track deadlines, reminders, and scheduled activities across multiple entity types (Matters, Projects, Invoices, etc.). Two key limitations exist:

1. **Dataverse Polymorphic Lookup Limitation**: Native "Regarding" fields cannot be used in views, filters, or Advanced Find, making cross-entity event views impractical.

2. **Dataverse Field Mapping Limitation**: OOB relationship mappings only support 1:N relationships and only work when creating child records from the parent form, not when creating records independently.

## Solution Summary

The solution delivers two reusable platform capabilities:

1. **Association Resolver Framework**: A dual-field strategy combining entity-specific lookups (for subgrid filtering) with denormalized reference fields (for unified views), managed by the AssociationResolver PCF control.

2. **Field Mapping Framework**: Admin-configurable field-to-field mappings between parent and child entities, supporting N:1/N:N relationships with three sync modes (one-time, manual refresh, push from parent).

## Graduation Criteria

The project is considered **complete** when:

- [x] **SC-01**: AssociationResolver PCF allows selection from all 8 entity types
- [x] **SC-02**: RegardingLink PCF displays clickable links in All Events view
- [x] **SC-03**: EventFormController shows/hides fields based on Event Type
- [x] **SC-04**: Entity subgrids show only relevant Events (Matter subgrid shows only Matter events)
- [x] **SC-05**: Event Log captures state transitions (create, complete, cancel, delete)
- [x] **SC-06**: Event API endpoints pass integration tests
- [x] **SC-07**: Admin can create Field Mapping Profile and Rules
- [x] **SC-08**: Field mappings apply on child record creation
- [x] **SC-09**: "Refresh from Parent" button re-applies mappings
- [x] **SC-10**: "Update Related" button pushes mappings to all children
- [x] **SC-11**: Type compatibility validation blocks incompatible rules
- [x] **SC-12**: Cascading mappings execute correctly (two-pass)
- [x] **SC-13**: Push API returns accurate counts
- [x] **SC-14**: All PCF controls support dark mode
- [x] **SC-15**: PCF bundles use platform libraries (< 1MB each)

## Scope

### In Scope

**Event Management:**
- Event (`sprk_event`), Event Type (`sprk_eventtype`), Event Log (`sprk_eventlog`) tables
- Regarding record association with dual-field strategy
- 3 PCF controls: AssociationResolver, RegardingLink, EventFormController
- BFF API endpoints: `/api/v1/events`
- Event Log state transition tracking
- Seed Event Type records
- Model-driven app forms, views, subgrids

**Field Mapping Framework:**
- Field Mapping Profile (`sprk_fieldmappingprofile`) table with Record Type lookups
- Field Mapping Rule (`sprk_fieldmappingrule`) table as child records
- Native Dataverse forms for admin configuration (no PCF required)
- FieldMappingService shared component
- UpdateRelatedButton PCF for push operations
- BFF API endpoints: `/api/v1/field-mappings`
- Sync modes: One-time, Manual Refresh (pull), Update Related (push)
- Type compatibility validation (Strict mode)
- Cascading mappings (two-pass execution)

### Out of Scope

- Event Set implementation (deferred to Workflow Engine project)
- Workflow Automation Engine (future project)
- Reminder due calculation (deferred to Workflow Engine)
- Advanced recurring events
- External system integrations
- Court Rules Engine integration
- AI-assisted event suggestions

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| PCF over webresources | Better testability, packaging, lifecycle management | [ADR-006](../../.claude/adr/ADR-006-pcf-over-webresources.md) |
| Minimal API for BFF | Consistency with existing patterns, no Azure Functions | [ADR-001](../../.claude/adr/ADR-001-minimal-api.md) |
| React 16 APIs | Dataverse platform provides React 16/17, not 18 | [ADR-022](../../.claude/adr/ADR-022-pcf-platform-libraries.md) |
| Fluent UI v9 | Dark mode support, design token system | [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md) |
| Code-based validation | No Dataverse Business Rules per owner clarification | — |
| Dual-field strategy | Dataverse polymorphic lookup limitations | — |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| PCF complexity with 5 controls | High | Medium | Start with simplest control (RegardingLink), reuse patterns |
| Field mapping performance | Medium | Low | Limit to 500 child records per push, pagination for larger sets |
| Cascading mapping loops | High | Low | Two-pass limit prevents infinite loops |
| React version mismatch | High | Medium | Strict adherence to ADR-022, platform library declarations |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Dataverse tables (Event, Event Type, Event Log) | Internal | Created | Owner manually created |
| Dataverse tables (Field Mapping Profile, Rule) | Internal | Pending | To be created |
| Existing entity tables (Matter, Project, etc.) | Internal | Ready | Already exist in Dataverse |
| BFF API infrastructure | Internal | Ready | Authentication, authorization in place |
| Fluent UI v9 | External | Ready | Via platform-library declaration |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Development Team | Overall accountability |
| AI Assistant | Claude Code | Implementation support |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-02-01 | 1.0 | Project completed - all 46 tasks finished, graduation criteria met | Claude Code |
| 2026-02-01 | 0.1 | Initial project setup | Claude Code |

---

*Template version: 1.0 | Based on Spaarke development lifecycle*

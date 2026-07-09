# Set-Regarding and Field-Mapping Resolver — R2

> **Status**: Initialized — ready for task execution
> **Owner**: Ralph Schroeder
> **Created**: 2026-07-08 · **Initialized**: 2026-07-09

## Quick Links
- [Implementation Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Specification](spec.md)
- [Design](design.md)

## Overview

R2 restores **automatic field inheritance at child-record creation time**. When a "+" wizard creates a child record from a Matter/Project (or another Matter), mapped fields — including lookup fields like Assigned Attorney 1 — auto-populate from the parent. A context-agnostic client engine in `@spaarke/ui-components` reads the existing Dataverse-configured field-mapping profiles (via one existing BFF call) and applies all four mapping types onto the wizard's create payload.

## Problem Statement

`visual-host-create-button-r1` UAT surfaced that no field values inherit from a host Matter into wizard-created children. The Feb-2026 runtime engine that did this was deleted as collateral damage in r1 (SRFR-045). The shared-lib `FieldMappingService.ts` today is a different, never-finished stub — every Dataverse method returns empty. The two config tables + BFF API are live, but nothing invokes "get profile for this pair" at creation time.

## Proposed Solution

Rewrite the stubbed engine into a working, context-agnostic implementation that calls the existing `GET /api/v1/field-mappings/profiles/{source}/{target}`, applies all four `sprk_mapping_type` behaviors (Copy incl. lookup `@odata.bind`, Default, Concat, Template), and is wired into all 7 wizard services adjacent to `applyResolverFields`. A minimal additive BFF DTO extension + one new `sprk_expression` column close the contract/schema so the capability never needs reopening. **Client-only — no Dataverse plugins.**

## Scope

### In Scope
- Context-agnostic engine rewrite; all four mapping types; lookup `@odata.bind`.
- Additive BFF contract extension (`mappingType`/`defaultValue`/`expression`/`isRequired`/`compatibilityMode`).
- Additive schema: `sprk_expression` (`NVARCHAR(2000)`) on `sprk_fieldmappingrule`.
- Wire all 7 wizard services; seed the attorney matrix per-pair; same-entity (matter→matter) support.
- Field Mapping Framework architecture doc + admin authoring guide.

### Out of Scope
- Dataverse plugins / form scripts (owner constraint, absolute).
- Update-time cascade (stays manual `UpdateRelatedButton` → `/push`); same-entity update-time cascade; N:N; new PCF; new BFF endpoint/service/package.

## Graduation Criteria
- [ ] Wizard-created Event/Invoice/Report Card inherits every mapped field (incl. attorney lookups via `@odata.bind`) at creation, verified in Dataverse.
- [ ] All four mapping types produce correct output (unit tests + one live record each).
- [ ] No profile → graceful no-op (no error, no UI change).
- [ ] Same-entity (matter→matter) works; negative test proves no `source === target` guard.
- [ ] `UpdateRelatedButton` → `/push` unaffected after DTO extension.
- [ ] BFF additive-only (no new endpoint/service/package); publish-size delta reported; no plugin; no new PCF.
- [ ] `sprk_expression` added; engine has no `ComponentFramework` dependency; nav-prop discovery consolidated.
- [ ] Architecture doc + admin guide published; CLAUDE.md §17 updated.
- [ ] Attorney matrix seeded per verified schema; stale UAT profiles handled; orphan rule deleted.

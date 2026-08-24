# Configurable Record Header — R2

> **Portfolio**: TBD — not yet registered on [Project #2](https://github.com/users/spaarke-dev/projects/2). Parent Epic (inherited from R1): [Epic #535 — ENTITY FUNCTIONALITY](https://github.com/spaarke-dev/spaarke/issues/535)
> **Status**: 🔲 Design complete, pre-spec. Not started.
> **Worktree**: `c:/code_files/spaarke-wt-record-header-and-notepad-r2`
> **Branch**: `work/record-header-and-notepad-r2` · **PR**: none yet

## Overview

R1 shipped `MatterHeaderPcf` and — more importantly — shipped its primitives as entity-agnostic shared code. R2 **generalizes that control** so ONE deployed component (`Spaarke.Records.RecordHeader`) serves every entity's main form. The title area and toolbar (AI summary · To Do · Notepad) stay identical everywhere; the field payload and its placement come from a JSON layout on a manifest property, with sensible defaults derived from form metadata when no JSON is supplied.

**This supersedes the 2026-07-05 R2 seed**, which proposed cloning the Matter PCF four times (`ProjectHeaderPcf`, `InvoiceHeaderPcf`, `WorkAssignmentHeaderPcf`, `EventHeaderPcf`). That plan is withdrawn — see [design.md §1](./design.md).

## Quick Links

| Document | Description |
|----------|-------------|
| [Design Doc](./design.md) | **The authoritative document.** Re-scoped 2026-08-21. |
| [Discovery Checklist](./notes/discovery-checklist.md) | Blocking Dataverse verification before `/design-to-spec` |
| [Current Task](./current-task.md) | Active state (for context recovery) |
| [CLAUDE.md](./CLAUDE.md) | AI context for Claude Code |
| AI Spec | ⏳ Not generated — run `/design-to-spec` |
| Project Plan | ⏳ Not generated — run `/project-pipeline` |
| Task Index | ⏳ Not generated — run `task-create` |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | 0 — design code- and schema-verified; **ready for `/design-to-spec`** |
| **Progress** | 0% |
| **Owner** | Ralph Schroeder |
| **Next Action** | `/design-to-spec` → `/project-pipeline`. All owner decisions closed; [discovery closed](./notes/discovery-checklist.md) against `spaarkedev1` |
| **Estimate** | ~8.5–12.5 dev-days (design.md §14) |

## Problem Statement

The Matter header exists only for Matter. Every other entity that wants the same compact field card plus the standard AI-summary / To-Do / Notepad toolbar would, under the withdrawn plan, get its own PCF — five solutions to version, deploy, bind, and fix in parallel. Of `MatterHeaderView.tsx`'s 326 lines, only ~40 are genuinely configuration (field list, lookup targets, layout, summary field); the other ~180 are generic machinery — form-buffer staging, the pending-changes buffer, lookup projection and search — that the clone-per-entity approach would duplicate four times.

Separately, the existing renderer set cannot render Invoice or Event correctly at all: there is no date, datetime, currency, number, or boolean renderer, so a Money value renders as `12500` and a DateTime as `2026-08-21T00:00:00Z`.

## Solution Summary

One control, configured per form:

1. **Generalize** `MatterHeader` → `Spaarke.Records.RecordHeader`. Entity self-detected via `context.mode.contextInfo.entityTypeName`.
2. **Configure** via a `layoutJson` manifest property (schema: design.md §5.2), falling back to defaults derived from form metadata — so it never renders blank and never throws on malformed JSON.
3. **Derive, don't configure**, everything the form context already knows: labels, attribute types, option-set options, required levels. Lookup targets resolve from `EntityDefinitions/ManyToOneRelationships`, which is what removes R1's hard-coded `LOOKUP_META`.
4. **Add the missing renderers** to `@spaarke/ui-components`: date/datetime, number/currency, boolean, plus edit mode on `OptionSetField`.
5. **Hoist the generic machinery** out of the view so it exists once instead of five times.

**Rollout**: Project + Work Assignment (wave 1) → Invoice + Event (wave 2) → Matter migrated **last**, as both the final migration and the strongest regression test.

## Key Decisions (2026-08-21)

| Decision | Rationale |
|---|---|
| One configurable control, not four clones | ~180 lines of generic machinery would otherwise be duplicated 4× |
| JSON on manifest, **not** a `sprk_headerconfiguration` table | Handful of instances ever (unlike VisualHost/DataGrid); config travels in form XML so nothing to seed per environment |
| Project + Work Assignment first; **Invoice explicitly required** | Wave 1 needs no new renderers; Invoice forces the currency + date work |
| DEF-06 (`exports` migration) dropped | Rationale was amortizing across 4 PCFs; with 1 PCF the leverage is gone, the repo-wide cost isn't |
| DEF-08 (`useSprkMemoRepository` promotion) dropped | A launcher doesn't render memo content inline — no second consumer, so CLAUDE.md §11 isn't satisfied |
| New control name + one-time Matter form re-bind | `constructor=` rename creates a new control. **Re-decided 2026-08-22** after the code review showed a display-name-only rename would also fix the maker-visible name at zero cost — owner reaffirmed the clean identity |
| **2026-08-22**: main forms are solution-transported | Confirmed by owner. Makes the JSON-on-manifest portability argument real (no per-environment paste) and the Matter re-bind genuinely once |
| **2026-08-22**: metadata reuses `IDataverseClient` | Extends an existing shared contract with `targets` rather than adding a third raw-`fetch` metadata path; keeps the project inside its own "`Xrm.WebApi` only" rule and DataGrid inherits the improvement |

## Out of Scope

BFF endpoints of any kind (sparkle refresh stays unwired). Changes to the Notepad or SmartTodo code pages — both are already entity-agnostic. VisualHost `CardChrome` (DEF-03) and EventDetailSidePane `MemoSection` (DEF-04). A Dataverse config table (explicitly rejected — design.md §5.4).

## Graduation Criteria

- One control renders correctly on all five entities, configured per form
- Matter renders pixel-identically to `MatterHeaderPcf` v1.0.20, with R1's live-QA behaviors intact (form-buffer dirty state with no re-render flash, 25%×35% Notepad modal, `openTodos` SmartTodo filter, dark/high-contrast theming)
- Invoice renders currency and dates correctly
- Malformed/absent `layoutJson` degrades to derived defaults — never blank, never thrown
- Bundle ≤250 KB minified
- Authoring guide rewritten from the retired per-entity recipe to the configuration recipe

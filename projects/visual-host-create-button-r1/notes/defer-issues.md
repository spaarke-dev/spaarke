# Deferrals + Issues — visual-host-create-button-r1

> **Status**: at project wrap-up (2026-07-09).

## Deferrals filed during execution

None. This project's scope (Event/Invoice/Report Card wizards + WizardFollowOns consolidation) shipped in full; the KPI→Report Card retarget (task 040) and field-mapping inheritance exclusion were both handled as owner-approved scope decisions in `spec.md`/`TASK-INDEX.md`, not deferrals.

## Uncovered issues (not in original scope)

### ISS-01 — Invoice wizard's "Vendor Organization" lookup returns no results in live UAT

**Root cause**: Unknown. Two full investigation passes (2026-07-08/09) found no code-level defect:
- Field/entity names correct (`sprk_organization.sprk_organizationname`, confirmed live via `mcp__dataverse__describe`).
- Data exists (e.g. "Morrison Foerster LLP").
- `CreateInvoiceStep.tsx`'s `handleSearchVendorOrgs` correctly calls `searchOrganizationsAsLookup(dataService, query)` from the canonical `matterService.ts` — same implementation already fixed and verified working for `CreateEventWizard.tsx`'s equivalent field (see v1.4.32 fix in this project, commit `ced796cd7`).
- `dataService` (`createXrmDataService()`) is correctly memoized and passed through `VisualHostRoot.tsx` → `CreateInvoiceWizard` → `CreateInvoiceStep`.
- Owner confirmed **zero console errors** when using the broken lookup, which rules out a thrown/caught exception (the `LookupField.tsx` component catches and logs all search errors via `console.error`).

**Untried next steps** (need live browser access, not available to this session):
1. Confirm whether the `[MatterService] searchOrganizations query:` info-level console log appears at all when typing in this field.
2. Check the browser Network tab for the actual outgoing OData request/response for `sprk_organization`.
3. Rule out a security-role/privilege gap specific to `sprk_organization` for the interactive user (Dataverse can silently return 0 rows under certain privilege-filtering configurations without a 403).

**Recommended action**: Resolve in a follow-on session/project with live browser (`--chrome`) access, since this is purely an environment-observation gap, not a design or scope question.

**Cost-of-doing-nothing**: Invoice wizard's Vendor Organization field is unusable via typeahead (users cannot search/select a vendor org when creating an Invoice from the Visual Host "+" button) until this is fixed. Not a regression — this field never worked in this new wizard; core Invoice creation (record/email/work-assignment/file-upload) is unaffected and works correctly.

**Status**: Owner decision (2026-07-09): defer resolution to a separate session; task 090 (wrap-up) proceeds without blocking on this.
**GitHub Issue**: [#587](https://github.com/spaarke-dev/spaarke/issues/587)

## How to file NEW deferrals

This repo's `/project-defer-issue-tracking` skill is referenced by root CLAUDE.md and `push-to-github` but does not currently exist under `.claude/skills/` in this worktree (docs/skill-registry drift — not something this project introduced). Until it exists, file manually: add an entry here + a corresponding GitHub Issue, and link both ways.

## Status at wrap-up

- 17 of 17 implementation tasks complete (100%) — 001–050 + this wrap-up (090)
- `/test-diet`: 163 tests reviewed, 0 scaffolding, 163 MAINTAIN (see `notes/test-diet-report.md`)
- 1 open issue (ISS-01 above), owner-approved to resolve out-of-project

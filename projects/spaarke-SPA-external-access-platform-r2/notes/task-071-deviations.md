# Task 071 — Polymorphic grant UI (TrackingFieldTrio + AccessGrantModal + side-pane Advanced Lookup) — deviations & verification

> 2026-08-11 · FULL rigor · sonnet@high (executed on Opus 4.8) · directional steps.
> UI companion to task 028 (polymorphic read) + task 070 (polymorphic write). Branch
> `work/spaarke-SPA-external-access-platform-r2`. PCF **v1.0.12** built + imported to SPAARKE DEV 1.

## What shipped

**The "Manage Access" surface is now polymorphic across Project / Matter / Work Assignment**, and the
contact picker is the shared side-pane Advanced Lookup — the R1 project-only hardcode is gone.

### ① Shared `AccessGrantModal` (`@spaarke/ui-components`) — stayed Xrm-free (ADR-012)
- **`{recordType, recordId}` grant bodies** replace the 3 `projectId` keys (grant + invite-and-grant),
  matching task 070's DTO. `recordType` defaults to `'project'` (pre-071 baseline). Revoke drops
  `projectId` entirely (root-agnostic per 070).
- **Side-pane Advanced Lookup contact picker** via a new injected `pickContact?()` callback: when the
  host supplies it, section 2's Fluent `Combobox` is replaced by a "Select contact" button that calls
  the callback (the host wires `INavigationService.openLookup`). The inline Combobox is retained as the
  fallback for hosts that don't inject `pickContact` (SPA / no-lookup) — see **Deviation D-1**.
- **Optional `sprk_organization` picker** via a new injected `pickOrganization?()` callback → sends
  `organizationId` (the grant's `sprk_Organization` firm-scoping lookup wired in 070). Only shown when
  the host wires it.
- New types: `ExternalGrantRootType`, `IOrganizationPick`; new props `recordType`, `pickContact`,
  `pickOrganization`. No Xrm/entity literals entered the shared modal (verified by grep).

### ② `TrackingFieldTrio` PCF host — parameterized by the bound entity
- **Host entity derived at runtime** from `context.page.entityTypeName` (fallback `sprk_project`),
  replacing `GRANT_RECORD_ENTITY='sprk_project'`. A `GRANT_ROOT_BY_ENTITY` map yields `{recordType,
  rootValueField}` per root.
- **Grant-read filter fixed + made polymorphic**: `_sprk_projectid_value` (invalid field → matched zero
  rows, the root cause of the Matter/WA "Failed to load access data" error) → `_sprk_{root}_value`
  (`_sprk_project_value` / `_sprk_matter_value` / `_sprk_workassignment_value`, verified live in 070).
- `retrieveRecord` + `SendEmailDialog` regarding now use the host entity.
- **Adopted `createXrmNavigationService().openLookup`** for `pickContact` (enriched with a host-context
  `contact` email read so external-vs-internal routing stays correct) and `pickOrganization`. **No new
  lookup abstraction built (§11).**
- Candidate role fields (`sprk_assigned*`) are shared across all three roots (verified) — one shared
  `CANDIDATE_ROLE_FIELDS` set serves all three.
- Version bumped **1.0.11 → 1.0.12** in all locations (Input manifest, index.ts footer, solution.xml,
  Solution ControlManifest.xml, pack.ps1) + fresh bundle copied to the Solution folder.

## Deviations

- **D-1 — Combobox retained as fallback (not deleted).** The owner constraint said "replace section-2's
  Combobox with the side-pane Advanced Lookup." I made `pickContact` the PRODUCTION path (the PCF always
  injects it → the shared side-pane lookup is what real users get, satisfying the acceptance criterion)
  but kept the inline Combobox as a fallback for any host that does NOT inject `pickContact`. Rationale:
  the shared modal must not HARD-require an Xrm-backed lookup (openLookup is a graceful no-op in SPA/BFF
  hosts, returning `[]`); a hard requirement would break the ADR-012 "works everywhere" contract if the
  modal is ever mounted in the external SPA. Net: production uses openLookup; the modal stays portable.
  This is a Path-A-style scoped judgment, not a spec miss.

## Step 6 spike (side-pane-over-open-modal) — owner-pending live UI check

The one integration risk the task flags (does `Xrm.Utility.lookupObjects` layer correctly OVER an open
`SprkModal` and restore focus on return?) is a **browser-only** behavior that cannot be exercised
headless. The mechanism is sound by construction: `openLookup` returns a Promise that resolves with the
selection (or `[]` on cancel); the modal `await`s it in `handlePickContact`, so React state updates only
after the pane closes — no z-index/focus contention while both are open. **Confirm in the task-073
both-plane UAT** (owner opens a Matter form → person icon → Select contact → grant). Non-blocking to the
code; unit-level behavior is covered by the mock-nav `openLookup` tests.

## Verification

| Check | Result |
|---|---|
| AccessGrantModal unit suite | **25 pass / 0 fail** (+3 new: matter `recordType`, side-pane `pickContact` via `createMockNavigationService`, `organizationId`) |
| PCF `npm run build:prod` | **clean compile** across shared-lib + siblings + PCF; `v1.0.12` baked into bundle; 744 KiB (size warning pre-existing) |
| ESLint (modal) | clean (no errors) |
| ADR-021 (no hardcoded hex) | ✓ (grep clean; Fluent v9 semantic tokens; dark-mode test passes) |
| ADR-012 (modal Xrm-free) | ✓ (no Xrm/entity literals in the shared modal; injected callbacks only) |
| §11 (reuse) | ✓ adopted existing `openLookup` + `createXrmNavigationService`; no new abstraction |
| PCF import → SPAARKE DEV 1 | **Imported + published successfully** (v1.0.12 live) |
| Polymorphic read fields valid (live) | ✓ `sprk_project`/`sprk_matter`/`sprk_workassignment`/`sprk_organization` all valid columns; **1 live matter-scoped grant** (matter REAL-2026-123456.02) the Matter modal will now load (R1's `_sprk_projectid_value` returned zero) |
| BFF `{recordType,recordId}` write | already live-verified in task 070 (matter/WA/project grants 200) |

## Owner-pending (task 073 both-plane UAT)
1. Browser smoke: open a **Matter** + a **Work Assignment** form → person icon → candidates + current
   grants load (no "Failed to load access data") → **Select contact** via side-pane Advanced Lookup →
   grant → confirm the row is written to the correct root (+ optional org picker → `sprk_organization`
   set). Step-6 spike (pane layers over the open modal, focus restores) is validated here.
2. Hard-refresh (Ctrl+Shift+R) to clear the cached PCF; footer must read **v1.0.12**.

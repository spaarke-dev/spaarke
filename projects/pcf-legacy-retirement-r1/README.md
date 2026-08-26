# PCF Legacy Retirement — R1

> **Status**: Deferred / awaiting owner scope confirmation
> **Created**: 2026-08-21 by `customer-provisioning-orchestration-r1` H-3 solution-scoping
> **Predecessor**: `pcf-orphan-cleanup-r1` (2026-06-22, stalled at Task 003)
> **Purpose**: Consolidate all PCF cleanup work surfaced during r1 SpaarkeMaster assembly. Split from r1 to keep provisioning scope tight.

---

## Why this exists

While assembling `SpaarkeMaster.zip` for first-live customer provisioning (r1 H-3), the 6-layer live-dependency audit + pac export attempt surfaced 4 distinct classes of PCF debt in `spaarkedev1`. Fixing each properly is scope-appropriate for a dedicated project, not r1 (which owner directive kept focused on "get the ship out").

r1 shipped SpaarkeMaster.zip v1.1.0.0 with pragmatic decisions on all 4 classes. This project cleans them up permanently.

---

## The 4 classes of debt

### 1. 7 PCFs excluded from SpaarkeMaster + deleted UDG (8 total)

r1 kept these OUT of SpaarkeMaster.zip via `-ExcludedPCFs` (or, for UDG, by deleting its CC record entirely). All 8 remain deployed in `spaarkedev1` (in their dedicated solutions), but don't ship to customers. Should they be fully retired from `spaarkedev1`?

| PCF | Why excluded | Retirement decision needed |
|---|---|---|
| `sprk_Spaarke.Controls.AnalysisWorkspace` | Ribbon rewired to SpaarkeAi | Delete from `spaarkedev1`? |
| `sprk_Spaarke.Controls.DueDatesWidget` | Retired feature | Delete? |
| `sprk_Spaarke.Controls.EventCalendarFilter` | Superseded by `@spaarke/events-components` | Delete? |
| `sprk_Spaarke.Controls.FieldMappingAdmin` | Admin UI consolidated | Delete? |
| `sprk_Spaarke.LegalWorkspace` | Superseded by LegalWorkspace Code Page | Delete? |
| `sprk_Spaarke.Controls.UniversalDocumentUpload` (UQC) | Ribbon migrated v4.0.0 to Code Page | Delete? |
| `sprk_Spaarke.Controls.PlaybookBuilderHost` | Superseded by `sprk_playbookbuilder` Code Page | Delete? Or keep for future rewire? |
| `sprk_Spaarke.UI.Components.UniversalDatasetGrid` | **DELETED 2026-08-21** — its bundle.js + styles.css WebResources were missing; broke pac export | Already done. Restore from source if ever needed. |

**Baseline backups** exist from 2026-06-22 in `projects/pcf-orphan-cleanup-r1/backups-2026-06-22/` (5 MB, 10 solutions). Recovery = `pac solution import`.

### 2. 5 REDs in-service (must remain — or migrate off)

Live-dependency check surfaced 5 PCFs currently bound to production forms/views. These SHIP with SpaarkeMaster because customer forms depend on them. Decision: keep shipping, or migrate off + retire?

| PCF | Live binding | Migration target | CLAUDE.md drift |
|---|---|---|---|
| `sprk_Spaarke.Controls.AssociationResolver` | ~~Matter main form~~ (removed 2026-08-20 by owner) | Now zero-dep; can be excluded next release | CLAUDE.md §17 correctly says "retired" now |
| `sprk_Spaarke.Controls.RegardingLink` | 6 Active sprk_event views (cell renderer in `layoutxml`) — provides URL column functionality | Migrate 6 views to use `RegardingResolver` PCF instead, then retire RegardingLink. **NOTE: RegardingLink has no source folder** — reverse-engineer or accept perpetual dependency |
| `sprk_Spaarke.SpeDocumentViewer` | Document main form (72 appmodulecomponent registrations across MDAs) | Broad blast radius — dedicated migration project |
| `sprk_Spaarke.Controls.EventAutoAssociate` | Event quick create form (Active) | Check if still functionally needed |
| `sprk_Spaarke.Controls.EventFormController` | Event modal form + Event Assign Work main form. **`EVENT_MODAL_FORM_ID` HARDCODED in `EventDetailSidePane/src/services/sidePaneService.ts:119`** | Migration must preserve the modal-form GUID contract |

### 3. 10 N:N intersect entity add failures (cosmetic — Assemble script polish)

When Assemble tried to add 10 N:N intersect entities (`sprk_*` join tables from M:N relationships) to SpaarkeMaster via `AddSolutionComponent`, all 10 returned 400 Bad Request. This is expected Dataverse behavior — intersects auto-generate when their parent M:N EntityRelationships are included in a solution (which we DID add successfully). The 10 failures are cosmetic; SpaarkeMaster.zip exports the intersects transitively via their parent relationships.

**Cleanup**: add a script-level filter in `scripts/solution-authoring/Assemble-SpaarkeMasterSolution.ps1` that skips N:N intersect entities (heuristic: entity `logicalname` matches the intersect table naming pattern) so they don't attempt-and-fail. Small PR. Non-urgent.

### 4. 477 env-wide orphan `customcontrolresource` rows

Systematic scan of `spaarkedev1` surfaced 477 customcontrolresource records that reference non-existent WebResources. **Most are Microsoft-managed platform CustomControls** (ViewManagementControl, AutoCompleteControl, QuickForm, PowerBIPersonalDashboard, PhoneNumberControl, ContextualEmail, RadialKnobControl, etc.) — likely from platform updates that renamed/consolidated resources but left stale metadata.

**Should we clean these up?**
- Argument for: env hygiene; may cause future export issues for other solutions
- Argument against: modifying Microsoft-managed metadata may cause unexpected side effects; the auto-recreation behavior observed (delete → row reappears from CC manifestxml) means DELETE on customcontrolresource is not a stable fix
- Argument to leave alone: doesn't affect customer environments (customers get a fresh Dataverse env); only affects `spaarkedev1` which we control

**Recommendation**: leave alone unless a specific future solution export blocks on one. Track in this project as known state, don't attempt bulk cleanup.

---

## What r1 shipped (baseline for this project)

- `SpaarkeMaster` v1.1.0.0 managed ZIP (~24 MB, 412 root components, 23 CCs)
- `scripts/solution-authoring/Assemble-SpaarkeMasterSolution.ps1` with `-ExcludedPCFs` wired
- 8 PCFs kept out of SpaarkeMaster (via exclusion or CC-level deletion)
- 5 REDs continue to ship in SpaarkeMaster (form-bound)
- 200 of 210 planned components added; 10 N:N intersects failed (harmless)

---

## Suggested workstreams (WHEN this project spins up)

1. **Retire the 8 excluded PCFs from `spaarkedev1`** (§1) — decision + deletion using existing 2026-06-22 backups as safety net
2. **Migrate the 5 in-service REDs off their form bindings** (§2) — form + FormXml surgery per PCF; sequenced by blast radius
3. **Polish Assemble script for N:N intersect skip** (§3) — small PR
4. **Optional env hygiene sweep** (§4) — track only, don't proactively clean

---

## References

- Predecessor: [`projects/pcf-orphan-cleanup-r1/`](../pcf-orphan-cleanup-r1/) — 2026-06-22 audit + Task 003 stalled at Dataverse cleanup
- June inventory: [`projects/ai-procedure-quality-r1/notes/inventory/pcf-deployment-inventory-2026-06-22.md`](../ai-procedure-quality-r1/notes/inventory/pcf-deployment-inventory-2026-06-22.md)
- Backup ZIPs: [`projects/pcf-orphan-cleanup-r1/backups-2026-06-22/`](../pcf-orphan-cleanup-r1/backups-2026-06-22/) (10 baseline solution ZIPs)
- SpaarkeMaster solution architecture: [`docs/procedures/SPAARKE-SOLUTION-RELEASE-PROCESS.md`](../../docs/procedures/SPAARKE-SOLUTION-RELEASE-PROCESS.md)
- 2026-08-20 audit findings + this project's origin: `notes/audit-findings-from-r1.md` (below)

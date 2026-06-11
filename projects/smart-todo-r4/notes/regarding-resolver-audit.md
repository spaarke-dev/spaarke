# D — Regarding resolver architecture audit
> Task: R4-002 · Date: 2026-06-10 · Status: complete

**Decision (binding)**: Implement the reusable regarding resolver as a **virtual PCF control** at `src/client/pcf/RegardingResolver/`, bound to a single hidden field (`sprk_regardingrecordtype` lookup) on the host form, writing the other 4 resolver fields via `Xrm.WebApi` side effects through a wrapper around `PolymorphicResolverService.applyResolverFields`.

This audit confirms the spec's stated assumption (PCF likely winner) with evidence and gates tasks 050/051/052 to the PCF directory + build pipeline.

---

## 1. Scoring matrix

Scoring: **1 = worst, 5 = best**. Each cell carries a one-line justification.

| Criterion | PCF | Web Resource | Code Page embed |
|---|---|---|---|
| **(a) Multi-env portability** (solution export/import for multi-env deploys) | **5** — PCF binaries ship inside a managed solution; deterministic export/import; `pcf-deploy` skill is the proven pipeline (current `AssociationResolver` v1.1.0 is the existence proof). | **2** — Legacy JS web resources are solution-portable in principle but historically lose form bindings on import (no compile-time check; binding lives in FormXml). Past Spaarke rework chasing broken web-resource bindings is precisely what ADR-006 was written to stop. | **3** — Code Page HTML web resource exports cleanly, but the iframe host (the form designer wrapper) requires a second web-resource asset (loader JS) + manual FormXml. Two assets to keep in sync across envs. |
| **(b) Form-designer ergonomics** (how cleanly does it drop on a form?) | **5** — In modern form designer, PCF appears as a custom control on a bound field. Maker selects the field → "Components" → "Add component" → pick `Spaarke.Controls.RegardingResolver`. Width/height controls available. Visible by role. Identical motion to the existing `AssociationResolver` PCF that already ships. | **2** — Web resource lives as a separate form element with manual height/width pixels in FormXml; no field binding; loader script runs in form-load context with no lifecycle from the form designer. Editing requires raw FormXml on edge cases. | **2** — Same as web resource — an iframe sub-control. Form designer treats it as a generic web resource; size + scroll behavior must be tuned manually; no native field binding. |
| **(c) BFF coupling (zero preferred)** | **5** — PCF talks to `context.webAPI` (host-session Dataverse) directly. Zero BFF. Wraps `PolymorphicResolverService.applyResolverFields` which is pure client logic + `Xrm.WebApi`. | **5** — Pure client JS calling `Xrm.WebApi`. Zero BFF. (Tied with PCF on this dimension.) | **4** — Code Page can also stay BFF-free if scoped only to `Xrm.WebApi`. BUT Code Pages historically pull in `@spaarke/auth` for any future expansion, which is BFF-coupled. R4 spec line 205 says "purely client-side" — risk of drift is real once a Code Page is on the surface. |
| **(d) Maintenance burden** (one shared artifact vs. per-form duplication) | **5** — One PCF, parametrized via input properties (`entity`, `regardingTargets`, `readOnly`), used on both `sprk_todo` AND `sprk_communication` forms (FR-22). One bundle.js, one manifest. | **2** — Web resource is a single JS file but binding/configuration lives in FormXml per form — every host form needs its own wiring code; entity-specific branching tends to creep into the JS because there's no parameter contract. Two forms × maintenance × deploy cycles. | **3** — One Code Page artifact, but each host form needs its own iframe wrapper web resource (URL params for entity context). The "configure via URL params" approach works but is fragile when the surrounding form needs a value back (no native `notifyOutputChanged`). |
| **ADR-006 compliance** | **5** — ADR-006 explicitly carves out PCF for "form-embedded controls requiring bound properties" — exactly this use case. The regarding resolver IS a form-bound field control. | **1** — ADR-006 "Legacy Anti-Pattern Rule (Still Active)": *No new legacy JavaScript web resources.* This option is effectively prohibited unless overridden by audit evidence, and the audit finds no such override. | **3** — ADR-006 says Code Page is default for "standalone dialogs, wizards, pages, panels." The regarding resolver is NOT a standalone surface — it's an inline form-bound control. Code Pages are explicitly not the recommended choice when the surface needs Dataverse form binding (PCF wins per ADR-006 decision matrix). |
| **FR-22 reusability** (sprk_todo + sprk_communication, no entity-specific branching) | **5** — PCF input properties + bound lookup field make "entity" a deployment-time form configuration, not a code branch. Single PCF binary used on both forms. Implemented as `regardingTargets: string` (JSON / comma-separated logical names) input property. | **3** — Possible via global config object on the form, but every form needs a script tag + global variable wiring; the JS itself ends up with `if (entity === 'sprk_todo') {...}` because there's no contractual prop boundary. | **3** — Achievable via URL parameter `?entity=sprk_todo&regardingTargets=...`. Works, but the iframe wrapper for each host form needs its own loader. Adds duplication where PCF avoids it. |
| **FR-23 form placement** (To Do main form) | **5** — Native form-designer drag-drop on the To Do main form (`eca59df4-1364-f111-ab0c-7ced8ddc4cc6`). Bound to a single hidden `sprk_regardingrecordtype` field; sized via standard PCF properties. | **2** — Manual FormXml edit to place + size; no field-binding context; loader runs in the form's `onLoad` event with global side effects. | **2** — Same as web resource — added as an iframe sub-control with manual sizing. Often shows scroll bars inside a form section, which is poor UX for a one-line picker. |
| **NFR-03 multi-env portable** (no hardcoded env URLs/app IDs/container IDs) | **5** — PCF runs in `context.webAPI` host session; no environment URLs. App ID/client URL resolved at runtime via `Xrm.Utility.getGlobalContext()` (same as the existing `buildRecordUrl` in `PolymorphicResolverService`). Trivially portable. | **5** — Same — runs in host session. | **4** — Code Page can read `Xrm` via frame-walk, but if it ever calls the BFF it needs the BFF base URL env var. Today's design says it won't, but the door is open. Slight drift risk. |
| **TOTAL (8 criteria, max 40)** | **40 / 40** | **22 / 40** | **26 / 40** |

---

## 2. Recommendation

**Architecture: virtual PCF control.**

**Rationale**: PCF tops every criterion in the matrix, with a clean 40/40 vs. 22 (Web Resource) and 26 (Code Page embed). The decisive criteria are ADR-006 compliance (PCF is the explicit ADR-006 fit for "form-embedded controls requiring bound properties" — the regarding resolver IS one), form-designer ergonomics (native field-binding workflow), and maintenance burden (one PCF binary parametrized via input properties serves both `sprk_todo` and `sprk_communication` forms with zero entity-specific branching per FR-22). The existing `src/client/pcf/AssociationResolver/` v1.1.0 — a virtual ReactControl bound to `sprk_regardingrecordtype` writing record-id + record-name as outputs, already deployed in spaarkedev1 — is a working precedent that this PCF approach has been validated end-to-end on this exact resolver shape. Web Resource is barred by ADR-006's "no new legacy JavaScript web resources" rule; Code Page embed loses on the form-binding fit (Code Pages are the default for *standalone* surfaces, not inline form controls).

---

## 3. Implementation outline (binding for tasks 050/051/052)

**Directory location**: `src/client/pcf/RegardingResolver/`
- Modeled on `src/client/pcf/AssociationResolver/` v1.1.0 (same namespace pattern `Spaarke.Controls.RegardingResolver`)
- Virtual PCF (ReactControl) per ADR-022 — platform libraries for React 16/17 + Fluent UI v9
- Bundle target: <5 MB (per ADR-022)

**Build/deploy pipeline**: `pcf-deploy` skill (see `.claude/skills/pcf-deploy/SKILL.md` and `docs/guides/PCF-DEPLOYMENT-GUIDE.md`)
- `npm run build:prod` for production mode (per CLAUDE.md §11 build commands; *not* `npm run build`)
- Solution pack via `pac solution pack` into a managed-aware solution (spaarkedev1 first; promote via `pac solution import`)
- Version footer required in UI (see `src/client/pcf/CLAUDE.md` MANDATORY rule) — version-bump in 4 locations per the deployment-workflow checklist

**Form-binding strategy** (per spec Assumptions, hidden-field + side-effects pattern):
- Bind the PCF to a single hidden lookup field: `sprk_regardingrecordtype` (the resolver "discriminator" — already exists in R3 schema)
- Input properties on the manifest (all `usage="input"`):
  - `entity` (SingleLine.Text) — host entity logical name; either `sprk_todo` or `sprk_communication` (FR-22 reusability lever)
  - `regardingTargets` (SingleLine.Text) — comma-separated list of allowed parent entity logical names (e.g., `"sprk_matter,sprk_project,sprk_event,sprk_communication,sprk_workassignment,sprk_invoice,sprk_budget,sprk_analysis,sprk_organization,contact,sprk_document"`) — defaults to the canonical 11-entity list from `TODO_REGARDING_TARGETS`
  - `readOnly` (TwoOptions) — explicit override for FR-24 read-only mode (also auto-detected from `context.mode.isControlDisabled`)
- Output properties (so getOutputs feeds the bound field):
  - `regardingRecordType` (Lookup.Simple → `sprk_recordtype_ref`)
  - `regardingRecordId` (SingleLine.Text) — written through side-effect path
  - `regardingRecordName` (SingleLine.Text) — written through side-effect path
- All 5 resolver fields written via `Xrm.WebApi.updateRecord` on save by calling a thin wrapper around `applyResolverFields` (see §4)

**UX composition** (reuse, don't reimplement):
- Adapt `AssociateToStep` (the existing 11-entity picker UX in `@spaarke/ui-components`) into a more compact form-line variant: `RegardingResolverPicker` — single-row layout (Record Type dropdown + Select Record button + selected-record chip + clear)
- Hoist the new compact picker to `@spaarke/ui-components` per ADR-012 (so future surfaces can reuse it)
- Fluent v9 + Griffel `makeStyles` per ADR-021 (mandatory)
- Read-only rendering (FR-24): chip + clickable URL link to parent record; no edit affordances

**Test approach**:
- **Unit tests** (`__tests__/` colocated):
  - Picker state transitions (entity-type select → lookup open → record chosen → fields populated)
  - FR-21 mutual-exclusivity wrap: assert `applyResolverFields` is called exactly once per write; previous entity-specific lookup is cleared
  - Read-only mode (FR-24): no Select Record / Clear buttons rendered
  - FR-22 reusability: same component with `entity="sprk_todo"` vs `entity="sprk_communication"` — no code branch on entity
- **Integration test** (one happy-path):
  - Mounted in a harnessed PCF context; mock `Xrm.WebApi` via `IPolymorphicWebApi`; verify all 5 fields populated in the entity payload (matter selected → assert `sprk_regardingmatter@odata.bind` + 4 resolver fields)
- **A11y**: keyboard navigation (Dropdown + button focus order), ARIA labels match `AssociateToStep` pattern, contrast tokens (Fluent v9 semantic tokens, no hardcoded colors per ADR-021)
- **Smoke test in spaarkedev1**: deploy PCF, drop on To Do main form, save a record with each of the 11 entity targets, verify all 5 fields persist; flip user to read-only role, confirm FR-24

---

## 4. PolymorphicResolverService wrap pattern

The PCF MUST NOT reimplement mutual-exclusivity or 4-field-population logic (FR-21, ADR-024). The wrap pattern:

```typescript
// RegardingResolver/RegardingResolverHost.tsx (sketch)
import {
  applyResolverFields,
  findNavProp,
  type INavPropEntry,
  type IPolymorphicWebApi,
} from '@spaarke/ui-components';

// 1. Discover nav-props for the HOST entity (sprk_todo OR sprk_communication, per props.entity)
//    — cache per session (entity rarely changes during a form lifetime).
const navProps: INavPropEntry[] = await discoverNavProps(props.context, props.entity);

// 2. On record selected, build the entity payload + apply resolver fields.
const entityPayload: Record<string, unknown> = {};
await applyResolverFields(
  props.context.webAPI as unknown as IPolymorphicWebApi,
  entityPayload,
  navProps,
  parentEntityLogicalName,   // e.g., 'sprk_matter'
  parentEntitySet,           // e.g., 'sprk_matters'
  parentRecordId,
  parentRecordName,
  entityLookupHint           // e.g., 'matter'
);

// 3. Write through Xrm.WebApi.updateRecord on the host form's record.
//    (For unsaved records, defer to the form's pre-save handler so the
//    payload is included in the create transaction.)
await props.context.webAPI.updateRecord(
  props.entity,
  props.context.parameters.recordId,
  entityPayload
);
```

**FR-22 reusability — how entity becomes a prop**:
- The PCF manifest declares `entity` as an `input` property; the maker sets it once per form (`"sprk_todo"` on the To Do main form, `"sprk_communication"` on the Communication main form).
- The shared `applyResolverFields` accepts the child entity's nav-prop table as a parameter — so the PCF passes whichever nav-props were discovered for the host entity. **No entity-specific branching exists in the component code**. The only entity-aware step is the one-time `discoverNavProps(context, props.entity)` call at mount time, and that call is a pure data lookup.
- FR-13 (mutual-exclusivity) is enforced entirely inside `applyResolverFields` — the PCF is a pure wrapper.

---

## 5. Risks

- **R-01 (medium)**: Hidden-field binding pattern requires a single bound lookup field; if `sprk_regardingrecordtype` is not yet on the To Do main form, the form-customization step in task 052 must add it as a hidden field first. *Mitigation*: confirmed in R3 schema (`PolymorphicResolverService.ts` references `sprk_regardingrecordtype` directly); task 052 to verify FormXml presence as the first step.
- **R-02 (medium)**: Save-vs-create transaction boundary — for **new** To Do records, the PCF cannot call `updateRecord` until the record has a GUID. Pattern: register a pre-save handler that writes the resolver payload into the record's create transaction (the form designer's `OnSave` event runs before the Dataverse insert). *Mitigation*: well-documented PCF pattern; existing `AssociationResolver` v1.1.0 already solves this with `notifyOutputChanged` → form picks up outputs → save persists them in a single transaction.
- **R-03 (low)**: Two-form coordination — when `sprk_communication` form work happens (FR-22), the same PCF must be added there. Out of scope for R4 (R4 only adds the resolver to the To Do main form per FR-23), but the design must not foreclose it. *Mitigation*: `entity` input property makes adding to the Communication form a pure form-configuration step in a future project — zero code change.
- **R-04 (low)**: Read-only role testing requires a test user with restricted privileges in spaarkedev1. *Mitigation*: existing R3 read-only test user works for the smoke test.
- **R-05 (low)**: Adding `regardingTargets` as a string input property duplicates the canonical 11-entity list. *Mitigation*: keep `TODO_REGARDING_TARGETS` (from `@spaarke/ui-components`) as the authoritative source; the PCF's input property defaults to its serialized form. Makers only override when they explicitly want a different set.

---

## 6. Acceptance criteria checklist (from task 002 POML)

- [x] Scoring matrix complete for all 3 options against all 4 criteria (§1; also covers ADR-006, FR-22, FR-23, NFR-03 — 8 criteria total)
- [x] ONE architecture recommended with rationale (§2 — PCF)
- [x] Implementation outline includes directory + binding + build/deploy + test approach (§3)
- [x] FR-22 reuse for `sprk_communication` addressed (§3 form-binding strategy; §4 entity-as-prop)
- [x] Tasks 050/051/052 scopes gated by audit decision (§7 below)

---

## 7. Decision-gate note for tasks 050/051/052

> **Audit outcome (binding)**: PCF chosen.
>
> - **Task 050** (D implementation — PCF scaffold + shared picker hoist): scope is `src/client/pcf/RegardingResolver/` virtual PCF + new compact `RegardingResolverPicker` component hoisted into `@spaarke/ui-components` adapting `AssociateToStep` UX. Build/deploy via `pcf-deploy` skill; version footer required per `src/client/pcf/CLAUDE.md`.
> - **Task 051** (D implementation — `PolymorphicResolverService` wrap + write path): scope is the wrapper that calls `applyResolverFields` from inside the PCF — no reimplementation of FR-13 mutual-exclusivity. Includes nav-prop discovery cache and pre-save handler for new-record transactions (R-02 mitigation).
> - **Task 052** (D implementation — form-designer placement + hidden-field): scope is adding `sprk_regardingrecordtype` hidden field (if missing) + the PCF custom-control binding on the To Do main form `eca59df4-1364-f111-ab0c-7ced8ddc4cc6`. FR-24 read-only mode verified by smoke test with a restricted-role test user.
>
> **Foreclosed alternatives**:
> - Tasks 050/051/052 MUST NOT create `src/client/webresources/RegardingResolver.js` (Web Resource path foreclosed per §2 — ADR-006 violation).
> - Tasks 050/051/052 MUST NOT create `src/solutions/RegardingResolver/` (Code Page embed path foreclosed per §2 — wrong surface fit per ADR-006 decision matrix).
>
> If implementation surfaces an unforeseen blocker on the PCF path, escalate to human input per CLAUDE.md §6 (do NOT silently switch architectures).

---

*Audit complete. PCF chosen. Tasks 050/051/052 gated to `src/client/pcf/RegardingResolver/` + `pcf-deploy` pipeline.*

# Task 022 — Place `CommunicationTimelineRegarding` PCF on All 11 Regarding-Family Forms

> **Project**: messaging-communication-app-r2 · **Task**: 022 (W2, FR-04) · **Deps**: 021 · **Blocks**: 080
> **Status**: Spec + procedure + verification script authored. **Live apply DEFERRED to owner** (no live
> Dataverse/MCP access this session — consistent with every other R2 task this session: 002, 003, 010,
> 011, 020, 021, 023, 040, 041, 050, 070).

---

## 1. Contract from Task 021 (consumed here, not modified)

| Item | Value |
|---|---|
| Control | `Spaarke.Controls.CommunicationTimelineRegarding` v1.0.0 |
| Schema name (grep-verified in `Solution/solution.xml` + `Solution/customizations.xml`) | `sprk_Spaarke.Controls.CommunicationTimelineRegarding` |
| Solution | `CommunicationTimelineRegardingSolution` v1.0.0 |
| Solution ZIP | `src/client/pcf/CommunicationTimelineRegarding/Solution/bin/CommunicationTimelineRegardingSolution_v1.0.0.zip` (present, built + packed by task 021) |
| Bound property (REQUIRED) | `anchorField` — `usage="bound" required="true"`, `of-type="SingleLine.Text"`. Bind to the entity's **primary Name field**. The control does NOT read its value from the binding — it reads `(entityType, id)` from the Xrm page context. The binding exists only so the control appears in the form's component library (R1 lesson, cited verbatim in the manifest). |
| Input properties (all optional, `usage="input"`) | `apiBaseUrl`, `tenantId`, `clientAppId`, `bffAppId`, `showVersionFooter` (default `true`) |
| Platform libraries | React 16.14.0, Fluent 9.46.2 (ADR-022) |
| Behavior | Read-only. Resolves `(entityType, id)` from context, renders that record's threads-as-collapsible-groups (name + count), each expanding to the R1 interleaved timeline. No compose box. |

---

## 2. The 11-Entity Placement Table — CODE-GROUNDED (not assumed)

The 11 entities are exactly `RegardingFieldMap.All`
(`src/server/api/Sprk.Bff.Api/Services/Communication/Engine/RegardingFieldMap.cs`), in its priority order.

**⚠️ Correction to the task-022 POML's working note** ("the `sprk_` entities use `sprk_name`"): this is
only true for 6 of the 9 custom entities. The authoritative per-entity primary-name mapping already
exists server-side — `IncomingAssociationResolver.GetPrimaryNameField()`
(`src/server/api/Sprk.Bff.Api/Services/Communication/IncomingAssociationResolver.cs:469-483`) — used for
the *same* cross-entity "what's this record's display name" purpose this placement needs. Three entities
(`sprk_matter`, `sprk_project`, `sprk_event`) have their own dedicated primary-name field, not `sprk_name`.
Corroborated by grep hits in production code (`IncomingCommunicationProcessor.cs`,
`CreateProjectWizard/handoffSeedMapping.ts`, `sprk_gridconfiguration/entity-schema.md` fetchxml examples)
using `sprk_mattername` / `sprk_projectname` / `sprk_eventname` as real, live fields — not a naming
coincidence.

| # | Entity logical name | `anchorField` binding (primary Name attribute) | Regarding lookup field (ADR-024 context, not used by this PCF directly) |
|---|---|---|---|
| 1 | `sprk_matter` | **`sprk_mattername`** ⚠️ not `sprk_name` | `sprk_regardingmatter` |
| 2 | `sprk_project` | **`sprk_projectname`** ⚠️ not `sprk_name` | `sprk_regardingproject` |
| 3 | `sprk_invoice` | `sprk_name` | `sprk_regardinginvoice` |
| 4 | `sprk_servicerequest` | `sprk_name` (confirmed also in `docs/data-model/sprk_servicerequest.md`) | `sprk_regardingservicerequest` |
| 5 | `sprk_workassignment` | `sprk_name` | `sprk_regardingworkassignment` |
| 6 | `sprk_event` | **`sprk_eventname`** ⚠️ not `sprk_name` | `sprk_regardingevent` |
| 7 | `sprk_budget` | `sprk_name` | `sprk_regardingbudget` |
| 8 | `sprk_analysis` | `sprk_name` | `sprk_regardinganalysis` |
| 9 | `sprk_organization` | `sprk_name` | `sprk_regardingorganization` |
| 10 | `account` (OOB) | `name` | `sprk_regardingaccount` |
| 11 | `contact` (OOB) | `fullname` | `sprk_regardingperson` |

No entity was invented or omitted; no second regarding mechanism is introduced (ADR-024 §MUST NOT). This
list is a direct read of `RegardingFieldMap.All`, cross-checked against `IncomingAssociationResolver.cs`.

---

## 3. Import Order

1. **Import the task-021 solution ZIP FIRST** (registers the custom control in the org — a prerequisite
   for every form placement below):
   ```bash
   pac solution import \
     --path src/client/pcf/CommunicationTimelineRegarding/Solution/bin/CommunicationTimelineRegardingSolution_v1.0.0.zip \
     --publish-changes
   pac solution list | grep -i CommunicationTimelineRegarding
   ```
2. **Verify registration** — run `Verify-CommunicationTimelineRegardingPlacement.ps1` (Section 5); Step 1
   of its output confirms `customcontrols` has the schema name.
3. **Per-entity form placement** — Section 4, repeated 11×, via the Form Designer (maker checklist).
4. **Publish all customizations** after each form save (or one `pac solution publish-all` at the end).
5. **Re-run the verification script** — confirms all 11 rows.
6. **Smoke matrix + UI tests** — Sections 6–7.

---

## 4. Per-Entity Form Placement Procedure (Maker Checklist — the write path)

### Why a maker checklist, not a PATCH script, for the write side

Task 022's own `<rigor-reason>` flags this as a "host-affecting, hard-to-reverse deploy." A precedent
script exists in this repo for placing a **subgrid** across 11 parent forms
(`scripts/Deploy-TodoSubgridsToElevenParentForms.ps1`) — that's safe to script because subgrid controls
use a single, stable, widely-documented `classid` (`{E7A81278-...}`). A PCF **field-bound custom control**
uses a different FormXml region (`<controlDescriptions><controlDescription><customControl name="...">`,
confirmed structurally via `scripts/Fix-DocumentFormParams.ps1`'s `//controlDescription/customControl`
selector) with per-form-factor entries (Web/Phone/Tablet) and `forId` linkage that is **not verified/tested
end-to-end in this repo**. Authoring a blind PATCH for that schema risks corrupting a live production form
with no reliable rollback. `docs/guides/PCF-DEPLOYMENT-GUIDE.md` Step 9 ("Post-Import: Field-Based PCF")
already documents the Form Designer UI method as the **standard, tested procedure** for exactly this kind
of placement — so that is the path of record here. The scripted piece (Section 5) instead covers the safe,
well-understood **read-only verification** of the result.

### Steps (repeat for each of the 11 entities in Section 2)

1. Open the entity's **active main form** in the Form Designer (`make.powerapps.com` → Solutions → your
   solution → Tables → *{entity}* → Forms → the Main form with `formactivationstate = Active`).
2. Locate the cell bound to the entity's primary Name field (the `anchorField` value from the table above —
   e.g. `sprk_mattername` on the Matter form, `fullname` on the Contact form).
3. Select the field → **"+ Component"** (modern designer) or **"Change Properties" → Controls tab → "Add
   Control"** (classic designer) → search **"Communication Timeline (Regarding)"** → select
   `Spaarke.Controls.CommunicationTimelineRegarding` → **Add**.
4. On the newly added control's configuration row, set the input properties. **Copy the exact values
   already configured on the R1 `CommunicationTimeline` PCF placement** (on the `sprk_communication` /
   `sprk_communicationthread` forms) — do not invent new values; `apiBaseUrl` / `tenantId` / `clientAppId`
   / `bffAppId` are environment constants shared across all Timeline-family PCF placements:
   - `apiBaseUrl` — same BFF base URL as R1's placement
   - `tenantId` — same Azure AD tenant ID as R1's placement
   - `clientAppId` — same PCF MSAL client app ID as R1's placement
   - `bffAppId` — same BFF app ID (OAuth scope) as R1's placement
   - `showVersionFooter` — `true` (default)
5. Confirm the control is enabled for **Web** at minimum (Phone/Tablet optional — match whatever form
   factors the R1 placement covers, for parity; the POML does not require mobile explicitly).
6. **Save** the form.
7. **Publish** the form (or defer one bulk `pac solution publish-all` after all 11 are placed).
8. Record the row in the smoke matrix (Section 6) once verified live.

### Placement target summary (11 rows)

| # | Entity | Field to select in Form Designer | Control display name to search |
|---|---|---|---|
| 1 | `sprk_matter` | `sprk_mattername` | Communication Timeline (Regarding) |
| 2 | `sprk_project` | `sprk_projectname` | Communication Timeline (Regarding) |
| 3 | `sprk_invoice` | `sprk_name` | Communication Timeline (Regarding) |
| 4 | `sprk_servicerequest` | `sprk_name` | Communication Timeline (Regarding) |
| 5 | `sprk_workassignment` | `sprk_name` | Communication Timeline (Regarding) |
| 6 | `sprk_event` | `sprk_eventname` | Communication Timeline (Regarding) |
| 7 | `sprk_budget` | `sprk_name` | Communication Timeline (Regarding) |
| 8 | `sprk_analysis` | `sprk_name` | Communication Timeline (Regarding) |
| 9 | `sprk_organization` | `sprk_name` | Communication Timeline (Regarding) |
| 10 | `account` | `name` | Communication Timeline (Regarding) |
| 11 | `contact` | `fullname` | Communication Timeline (Regarding) |

---

## 5. Verification Script (read-only, safe to re-run)

**Path**: [`projects/messaging-communication-app-r2/scripts/Verify-CommunicationTimelineRegardingPlacement.ps1`](../scripts/Verify-CommunicationTimelineRegardingPlacement.ps1)

```bash
az login
./Verify-CommunicationTimelineRegardingPlacement.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
```

What it checks (no writes):
1. **Custom control registered** — `customcontrols` filtered by schema name (confirms the task-021
   solution ZIP was imported).
2. **Per-entity form check** — for each of the 11 entities, fetches the active main form's `formxml` and
   checks (a) whether the control's schema name appears inside the form (placed) and (b) whether a cell
   bound to that entity's confirmed primary-name attribute exists on the form (anchor field present).
3. Prints an 11-row summary table + a `Placed + verified: N / 11` count.

This is a **presence check**, not a full render/console-error validation — it is meant to be run
repeatedly during the maker checklist (Section 4) as a progress tracker, and once more at the end as a
completeness gate before the smoke pass (Section 6). It parses cleanly (`Parser]::ParseFile` verified, 0
errors) but was **not executed against a live org this session** — no Dataverse/MCP access available.

---

## 6. 11-Entity Smoke Matrix (owner fills in after live placement)

| # | Entity | Placed | Renders (grouped view) | No console errors | Notes |
|---|---|---|---|---|---|
| 1 | `sprk_matter` | ☐ | ☐ | ☐ | Deep UI-test target (Section 7) |
| 2 | `sprk_project` | ☐ | ☐ | ☐ | |
| 3 | `sprk_invoice` | ☐ | ☐ | ☐ | |
| 4 | `sprk_servicerequest` | ☐ | ☐ | ☐ | |
| 5 | `sprk_workassignment` | ☐ | ☐ | ☐ | |
| 6 | `sprk_event` | ☐ | ☐ | ☐ | |
| 7 | `sprk_budget` | ☐ | ☐ | ☐ | |
| 8 | `sprk_analysis` | ☐ | ☐ | ☐ | |
| 9 | `sprk_organization` | ☐ | ☐ | ☐ | |
| 10 | `account` | ☐ | ☐ | ☐ | |
| 11 | `contact` | ☐ | ☐ | ☐ | Deep UI-test target (Section 7) |

Do not declare the task done until all 11 rows pass (POML `<notes>`).

---

## 7. UI Tests (owner's post-placement verification checklist — verbatim from the task POML)

1. **`sprk_matter` form (deep)**: open a Matter with ≥2 threads across ≥2 months; the control renders
   threads-as-collapsible-groups; expanding a group shows the interleaved email+chat timeline; no console
   errors.
2. **`contact` form (deep, non-`sprk_` entity)**: open a Contact record; the control resolves
   `(entityType='contact', id)` from context and renders that contact's threads-as-groups — confirming the
   entity-set-agnostic path works for an OOB entity.
3. **Dark mode (ADR-021)**: on at least one of the deep forms, switch the model-driven app to dark theme;
   the grouped view, group headers, and count badges render correctly (Fluent v9 tokens pass through).
4. **11-entity smoke**: opening a record on each of the remaining 9 forms (project, invoice,
   servicerequest, workassignment, event, budget, analysis, organization, account) renders the control
   with no console errors.

**This session**: Step 9.7 (UI Testing) SKIPPED — no `--chrome` session and no live Dataverse environment
available (identical situation to tasks 020/021 this session). Substituted with the code-grounded
placement table (Section 2), the parse-verified script (Section 5), and this checklist for the owner to
execute against the live environment.

---

## 8. Live-Apply Gate

**Deferred to owner** (no live Dataverse/MCP access this session):
1. `pac solution import` the task-021 ZIP (Section 3, step 1).
2. Confirm registration (Section 3, step 2 / Section 5 script Step 1).
3. Execute the 11-row maker checklist (Section 4).
4. Re-run the verification script (Section 5) — target `Placed + verified: 11 / 11`.
5. Fill in the smoke matrix (Section 6) and execute the UI tests (Section 7).
6. Report back so task 080 (vertical-slice seam tests, which depends on 022) can proceed.

**Not deferred** (delivered this session): the authoritative 11-entity + primary-name table (Section 2,
code-grounded — corrects the task's `sprk_name`-for-all-`sprk_*` assumption), the import order (Section 3),
the placement procedure (Section 4), the parse-clean verification script (Section 5), and the smoke/UI-test
checklists (Sections 6–7).

---

## 9. ADR / Constraint Compliance

| ADR / Constraint | How satisfied |
|---|---|
| **ADR-024** (polymorphic resolver / regarding family) | Exactly the 11 `RegardingFieldMap.All` entities placed — no invention, no omission (Section 2). No second regarding mechanism: the PCF resolves `(entityType, id)` from Xrm context; the entity-specific `sprk_regarding*` lookups are ADR-024 context only, not read by this control. |
| **ADR-006** (PCF over webresources) | Form-embedded, field-bound control requiring `updateView()` lifecycle + bound property → PCF is the correct surface (not a Code Page). |
| **ADR-026** (Path-A exception, cited per task) | The surface is the OOB entity main form + a PCF control — not a Custom Page. Same exception R1's `CommunicationTimeline` placement already established; cite in the deploy PR. |
| **ADR-022** (PCF platform libraries) | Manifest already declares `React 16.14.0` / `Fluent 9.46.2` platform libraries (confirmed in `ControlManifest.Input.xml`) — no per-form change needed; the control loads under platform React on every placement. |
| **NFR-03** (access parity) | Nothing on the form re-derives access. The `by-regarding` BFF endpoint (task 010) already applies impersonation + the 2-rule access filter; this PCF is a pure client renderer of that endpoint's response. |
| **Component justification (root CLAUDE.md §11)** | Not a new component — this task places the already-built task-021 PCF; zero new code/service/DI/package. |

No ADR conflict surfaced (root CLAUDE.md §6.5 not triggered) — the maker-checklist decision above is a
scope/safety judgment within the task's own constraints, not an ADR deviation.

---

## 10. Files

| File | Purpose |
|---|---|
| `projects/messaging-communication-app-r2/notes/022-pcf-form-placement.md` | This spec (placement table, procedure, gates) |
| `projects/messaging-communication-app-r2/scripts/Verify-CommunicationTimelineRegardingPlacement.ps1` | Read-only 11-form placement verification script |
| `src/client/pcf/CommunicationTimelineRegarding/Solution/bin/CommunicationTimelineRegardingSolution_v1.0.0.zip` | The solution ZIP to import (task 021, consumed here) |
| `src/server/api/Sprk.Bff.Api/Services/Communication/IncomingAssociationResolver.cs:469-483` | Source of the authoritative primary-name-attribute mapping (read, not modified) |

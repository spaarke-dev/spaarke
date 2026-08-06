# Task 034 — Deploy Phase 4 surfaces (Matter pilot, spaarkedev1)

**Status**: deploy executed 2026-07-21 · STANDARD rigor · prescriptive · target env **spaarkedev1** (`https://spaarkedev1.crm.dynamics.com`). Surface load-verification (step 6) is user-side UAT.

## What was deployed

| Surface | Artifact | spaarkedev1 status | id |
|---|---|---|---|
| **030 PCF** | `CommunicationConversationPanelSolution_v1.0.0.zip` (unmanaged) | **PACKED — handed to operator to upload** (their choice) | — |
| **032 code page** | web resource `sprk_communicationconversationpage.html` (2047 KB) | **CREATED + published** | `4529e3ae-3c85-f111-8076-7ced8ddc4a05` |
| **033 grid page** | web resource `sprk_communicationspage.html` (1605 KB) | **CREATED + published** | `b54a859a-3c85-f111-8076-70a8a590c51c` |
| **033 onLoad** | web resource `sprk_/scripts/communications_grid_onload.js` (12 KB, JScript) | **CREATED + published** | `f8dc39d6-3c85-f111-8076-7ced8ddc4a05` |

Neither code-page web resource pre-existed in spaarkedev1 (the `Deploy-AllDataGridConsumers.ps1` PATCH path failed with "not found"), so all three were **created** via Web API POST + PublishXml (token via `az` as ralph.schroeder@spaarke.com). The reusable create/update script: `scratchpad/deploy-conv-pages.ps1`.

## PCF packaging (030)
- No `Solution/` wrapper existed (deferred from 030) — created by mirroring `CommunicationTimelineRegarding/Solution`: `solution.xml` (UniqueName `CommunicationConversationPanelSolution`, v1.0.0, RootComponent type 66 `sprk_Spaarke.Controls.CommunicationConversationPanel`), `customizations.xml`, `[Content_Types].xml`, `pack.ps1`.
- **Deploy-blocker fixed pre-build**: manifest `description-key` contained 3× `record's` (apostrophe) → Dataverse `noAposStringType` XSD rejects apostrophes in description-key. Stripped to `record`.
- `npm run build:prod` (production mode, `--buildMode production`) → bundle **2.54 MB**, in line with sibling `CommunicationTimelineRegarding` (2.47 MB) — legitimate (bundles the whole shared conversation stack), NOT a dev-mode blowup. Version footer `CONTROL_VERSION='1.0.0'` matches manifest.
- Zip: `src/client/pcf/CommunicationConversationPanel/Solution/bin/CommunicationConversationPanelSolution_v1.0.0.zip` (correct structure: lowercase root XMLs, only type-66, no web resources).

## Operator-side actions remaining (by design — operator's stated plan)
1. **Upload the PCF zip** to spaarkedev1 + place `CommunicationConversationPanel` on a Matter form's right pane.
2. **Wire the "Email & Messages" tab** on the Matter form per `src/dataverse/forms/sprk_matter/communicationsGridTab.FormXml.patch.xml`: add the tab + `WebResource_CommunicationsGrid` control (→ `sprk_communicationspage.html`) + the `<library>` `sprk_/scripts/communications_grid_onload.js` + OnLoad handler `Spaarke.CommunicationsGrid.onLoad`. (All 3 web resources are already deployed.)
3. **UAT (step 6)** on a Matter record: PCF preview + footer `1.0.0`; record-filtered modal; standalone conversation page loads; Email&Messages tab shows ONLY that Matter's comms (no cross-record leakage).

## 031 workspace widget — republish coordination (NOT done here)
The 031 `CommunicationsWorkspaceWidget` body-swap ships in the **SpaarkeAi shared-lib bundle**, not a standalone web resource. To appear in the live SpaarkeAi workspace it needs the **SpaarkeAi solution rebuilt + redeployed**. NOT done in this task: SpaarkeAi is a hot-path shared with other active worktrees (`projects/INDEX.md`) — republishing here risks clobbering concurrent SpaarkeAi work. **Coordinate the SpaarkeAi republish separately** before UAT-ing surface 2a.

## ADR tensions (for the deploy PR description)
- **ADR-026 Path-A**: right-pane conversation PCF (030) on an OOB form — same exception R2's `CommunicationTimeline*` PCFs established.
- **ADR-006 Path-C**: "Email & Messages" tab (033) uses the sanctioned DataGrid web-resource framework + within-framework form-onLoad scoping — not a bespoke web resource, not a PCF.

## Escalation
None fired. Surface load-verification (step 6) is deferred to operator UAT (no browser here); if any surface fails to load, the POML `<escalation>` says STOP the 10-entity broadening — Matter is the pilot.

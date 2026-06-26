# R4 Code Page Deploys to spaarkedev1 — UAT Readiness Report

**Date**: 2026-06-26
**Environment**: spaarkedev1 (`https://spaarkedev1.crm.dynamics.com`)
**Source commit**: `072ba99e0` (R4 merged to master via PR #456)
**Deploy operator**: ralph.schroeder@spaarke.com (az + PAC CLI session)
**Branch deployed-from**: `work/spaarke-daily-update-service-r4` (worktree; identical content to merged master)

---

## 1. Survey result — code pages requiring redeploy

R4 modified two shared / source-aliased dependency surfaces (`@spaarke/daily-briefing-components` and `src/solutions/LegalWorkspace/src/`) plus one standalone code page (`PlaybookBuilder`). Consumers identified via `grep -r "@spaarke/daily-briefing-components" src/`:

| # | Code page | Source path | Web resource | Why redeployed |
|---|-----------|-------------|--------------|----------------|
| 1 | **PlaybookBuilder** | `src/client/code-pages/PlaybookBuilder/` | `sprk_playbookbuilder` (Webpage HTML) | Direct R4 modifications: EntityNameValidatorForm.tsx + registry-integration files (types/canvas.ts, types/playbook.ts, properties/index.ts, NodePropertiesForm.tsx, NodePropertiesDialog.tsx, BaseNode.tsx, ai-assistant/commands.ts) |
| 2 | **DailyBriefing** | `src/solutions/DailyBriefing/` | `sprk_dailyupdate` (Webpage HTML) | Consumes `@spaarke/daily-briefing-components` as source (no `dist/`; Vite transpiles in-place). All seven R4 component changes (useBriefingNarration cache fix, ActivityNotesSection fallback, NarrativeBullet overflow menu, notificationService preferences, DailyBriefingApp Toaster + handleOpenRecord, PreferencesDropdown minConfidence removed, types/notifications) flow into the bundle. |
| 3 | **SpaarkeAi** | `src/solutions/SpaarkeAi/` | `sprk_spaarkeai` (Webpage HTML) | Consumes BOTH `@spaarke/daily-briefing-components` (direct npm dependency) AND `src/solutions/LegalWorkspace/src/` (Vite-aliased as `@spaarke/legal-workspace` in `vite.config.ts` lines 167–168). The R4 modification to `useDailyDigestAutoPopup.ts` (re-wired to canonical preferences) reaches the bundle through this alias. |

### Code pages NOT deployed (rationale)

- **`sprk_corporateworkspace` (LegalWorkspace standalone)** — RETIRED per OC-R4-05 (`docs/architecture/LEGALWORKSPACE-RETIREMENT.md`). The standalone code page is no longer deployed; `Deploy-CorporateWorkspace.ps1` has an early-exit guard requiring `-ForceRetiredDeploy`. LegalWorkspace components remain available as a library, consumed by SpaarkeAi via embedded mode (alias above). No gap.

---

## 2. Per-code-page deploy outcome

All deploys: SUCCESS. Sequential execution as directed.

| # | Code page | Build start | Deploy completed | Bundle size | Result | Web resource ID |
|---|-----------|-------------|------------------|-------------|--------|-----------------|
| 1 | PlaybookBuilder | 07:10:31 | 07:12 | 2,974 KB | UPDATE + Publish | `3dfd3713-9515-f111-8343-7ced8d1dc988` |
| 2 | DailyBriefing | 07:13:30 | 07:14 | 1,742 KB | UPDATE + Publish | `c34bc023-622d-f111-88b5-7ced8d1dc988` |
| 3 | SpaarkeAi | 07:15:22 | 07:16 | 3,874 KB | UPDATE + Publish | `5206a442-3451-f111-bec7-7ced8d1dc988` |

### Build / deploy notes

- **PlaybookBuilder**: Webpack production build, 3 warnings (bundle-size advisory only — 2.9 MiB; expected for self-contained code page bundling React 19 + Fluent v9 + @xyflow/react). Inlined via `build-webresource.ps1` then uploaded via `Deploy-WebResourceInline.ps1`.
- **DailyBriefing**: Required fresh `npm install --legacy-peer-deps` (node_modules absent in worktree). Build clean: surface-owned TS errors = 0; 70 pre-existing shared-lib errors (deferred to Phase B per gate config).
- **SpaarkeAi**: Required fresh `npm install --legacy-peer-deps`. Build clean: surface-owned TS errors = 0; 78 pre-existing shared-lib errors (same deferral). 3 cosmetic Rollup warnings re: `/*#__PURE__*/` annotations in applicationinsights-js (no functional impact).

All builds cleared cache (`rm -rf dist/ node_modules/.vite/ .vite/` for Vite; `rm -rf out/` for Webpack) before building, per skill mandatory-cache-clear rule.

---

## 3. URLs for UAT access

| Code page | Access path |
|-----------|-------------|
| PlaybookBuilder | Opens via `Xrm.Navigation.navigateTo({ webresourceName: 'sprk_playbookbuilder' })` — typically launched from Playbook entity ribbon or directly: `https://spaarkedev1.crm.dynamics.com/WebResources/sprk_playbookbuilder` (preview) |
| DailyBriefing | Direct: `https://spaarkedev1.crm.dynamics.com/WebResources/sprk_dailyupdate` — typically opened via Daily Briefing notification or dashboard tile |
| SpaarkeAi | Direct: `https://spaarkedev1.crm.dynamics.com/WebResources/sprk_spaarkeai` — typically opened via SpaarkeAi app launcher / sitemap |

(Replace `/WebResources/<name>` with the canonical app-launch URL once the UAT operator confirms the standard launch path — direct WebResource URL works for smoke validation but real UAT flow is via app/ribbon.)

---

## 4. Smoke check results

Each deploy returned `Updated` + `Published` from the Web API. Beyond the Dataverse-side confirmation, no in-browser smoke (Claude cannot open a browser session in this environment). The UAT operator should verify each URL renders without console errors and the bundle reflects R4 changes:

| Code page | What to verify renders | R4 marker |
|-----------|-------------------------|-----------|
| PlaybookBuilder | Canvas loads with palette | New "Entity Name Validator" tool appears in the AI Action palette; can be dropped onto canvas; properties pane opens with EntityNameValidatorForm |
| DailyBriefing | Briefing dialog renders | NarrativeBullet items have three-dot menu (overflow); ActivityNotesSection shows fallback text when empty; PreferencesDropdown lacks "minConfidence" field; Toaster appears on action triggers |
| SpaarkeAi | Workspace shell loads | Daily Briefing widget honors all-three R4 changes above; on first load with daily-digest enabled, `useDailyDigestAutoPopup` reads/writes canonical R4 preferences (no longer the pre-R4 legacy keys) |

---

## 5. Recommended UAT test scenarios

### PlaybookBuilder (`sprk_playbookbuilder`)

1. **EntityNameValidator tool present in palette** — open PlaybookBuilder, search/scroll palette for "Entity Name Validator". Drag onto canvas.
2. **EntityNameValidatorForm renders** — select the new node, properties pane opens, fields populate from node defaults, edit + save round-trips correctly.
3. **Registry integration** — verify NodePropertiesDialog dispatches to the new form via `properties/index.ts` dispatch table; BaseNode renders the new node-type marker; AI assistant `commands.ts` includes the new tool's command entry.
4. **No regression** — open an existing playbook, drag other tools (existing ones), confirm canvas + properties work as before.

### DailyBriefing widget (`sprk_dailyupdate` AND inside `sprk_spaarkeai`)

1. **Overflow menu on narrative bullets** — open a briefing with narrative content; each bullet should have a three-dot menu (overflow); menu opens with expected actions.
2. **Activity notes fallback** — open a briefing where activity has no notes; section should render the new fallback (not crash, not blank).
3. **Briefing narration cache fix** — refresh briefing twice in quick succession; second load should hit cache appropriately (no double network call / no stale narration replay).
4. **Preferences dropdown without minConfidence** — open Preferences, confirm `minConfidence` field is GONE (R4 removed it from `notifications.ts`).
5. **Toaster + handleOpenRecord** — trigger an action that opens a record (e.g., "Open in form"); Toaster confirms; record opens; no double-open.
6. **Notification service preferences** — change a preference, save, reload; preference persists per the canonical R4 preference shape.

### SpaarkeAi LegalWorkspace embedded autoPopup (`sprk_spaarkeai`)

1. **Auto-popup on first daily load** — with daily-digest preference ON and `lastShownDate < today`, opening SpaarkeAi should auto-open the Daily Briefing dialog ONCE.
2. **Suppression after first show** — close the auto-popup, reload the workspace; auto-popup should NOT fire again same day.
3. **Preference round-trip** — toggle daily-digest preference OFF in PreferencesDropdown; reload; auto-popup should NOT fire. Toggle ON; reload next day (or simulate by clearing `lastShownDate` from sessionStorage); auto-popup should fire.
4. **Standalone LegalWorkspace not deployed** — confirm `sprk_corporateworkspace` URL is NOT deployed / returns the retirement guard (smoke negative test).

---

## 6. Open items / flags

- No web-resource solution-membership audit was performed. If R4 added new web resources to a managed solution (it didn't — only updated existing three), nothing to do; if the UAT operator promotes spaarkedev1 → higher env, ensure the solution package contains these three at the deployed-bundle version.
- `useDailyDigestAutoPopup.ts` change reaches users ONLY via SpaarkeAi (LegalWorkspace standalone is retired). UAT should target SpaarkeAi for this behavior.
- Bundle size: SpaarkeAi at 3,874 KB is comfortably under the typical 5 MB code-page advisory ceiling. PlaybookBuilder at 2,974 KB likewise. DailyBriefing at 1,742 KB is small.

---

*Generated 2026-06-26 by automated UAT deploy run for R4 (PR #456 / commit `072ba99e0`).*

---

## Hotfix 2026-06-26: NODE_PALETTE entry + OptionSet value for EntityNameValidator

During UAT of PlaybookBuilder, two surfaces surfaced as missing despite R4 task 004 claiming `EntityNameValidator` was wired end-to-end:

1. **Code-page NODE_PALETTE array** — `src/client/code-pages/PlaybookBuilder/src/components/BuilderLayout.tsx` lines 72–127 listed only 9 of 10 node types. Without the 10th entry the tool was never draggable from the palette, even though `types/playbook.ts` (enum + maps), `components/nodes/BaseNode.tsx` (color scheme), and `components/properties/EntityNameValidatorForm.tsx` (form) all existed and registered correctly.
2. **Dataverse `sprk_playbooknode.sprk_nodetype` OptionSet** — the MDA "Node Properties" form's Node Type dropdown is backed by a local Picklist on `sprk_playbooknode`. Pre-hotfix values: `DeliverComposite (100000004)`, `AI Analysis (100000000)`, `Output (100000001)`, `Control (100000002)`, `Workflow (100000003)`. EntityNameValidator was not a distinct OptionSet value — `NodeTypeToDataverse` in `types/playbook.ts` aliased it to `Workflow`, so the form showed "Workflow" instead of a distinct EntityNameValidator entry.

### What was applied

| Fix | File / Surface | Detail |
|---|---|---|
| **Gap #1: palette entry** | `src/client/code-pages/PlaybookBuilder/src/components/BuilderLayout.tsx` lines 127–137 (new) | Inserted `{ type: 'entityNameValidator', label: 'Entity Name Validator', description: 'Scrub LLM-emitted entity names against allow-list', color: tokens.colorPaletteMagentaBackground2 }` between the `wait` item and the closing bracket. Color matches `BaseNode.tsx:141` magenta scheme. |
| **Gap #2a: OptionSet value** | Dataverse `sprk_playbooknode.sprk_nodetype` on spaarkedev1 | Added `EntityNameValidator = 100000005` via `scripts/dataverse/Add-EntityNameValidatorNodeTypeOption.ps1` → Dataverse Web API `InsertOptionValue` → `PublishXml`. Idempotent + reproducible (mirrors `Add-NodeTypeChoiceOption.ps1` DeliverComposite predecessor pattern). |
| **Gap #2b: enum + mapping** | `src/client/code-pages/PlaybookBuilder/src/types/playbook.ts` | Added `DataverseNodeType.DeliverComposite = 100_000_004` (was missing from prior commits despite live OptionSet value) and `DataverseNodeType.EntityNameValidator = 100_000_005`. Re-pointed `NodeTypeToDataverse[PlaybookNodeType.EntityNameValidator]` from `Workflow` → `EntityNameValidator`. |

### Redeploy timestamp

- **Build start**: 2026-06-26 08:26 (webpack production)
- **Inline + deploy completed**: 2026-06-26 08:28:48
- **Bundle size**: 2,975 KB (was 2,974 KB — +1 KB for the new palette entry)
- **Web resource ID**: `3dfd3713-9515-f111-8343-7ced8d1dc988` (UPDATE + PublishXml) — confirmed via MCP `read_query` on `webresource` (`modifiedon` = 2026-06-26T08:28:47).
- **OptionSet verification (post-publish)**: 6 values present, all confirmed via Web API `RetrieveAttributeRequest` re-read.

### How UAT operator verifies the hotfix landed

1. **Hard refresh** the PlaybookBuilder code page (Ctrl+Shift+R to bypass Dataverse static-asset cache).
2. **Open the palette** (left sidebar). Confirm a 10th entry: **Entity Name Validator** with magenta accent — same color family as Wait.
3. **Drag the Entity Name Validator** onto the canvas.
4. **Open Node Properties** for the new node. The "Node Type" dropdown should show **EntityNameValidator** as the selected/current option (alongside AI Analysis, Output, Control, Workflow, DeliverComposite).
5. **Save the playbook**. Open the underlying `sprk_playbooknode` record in the MDA and confirm `sprk_nodetype` = `EntityNameValidator (100000005)`.
6. **Regression check**: drag any other tool type (e.g., Wait or AI Analysis); confirm existing nodes still save with their original `sprk_nodetype` values.

### Notes

- The OptionSet add was a metadata mutation on spaarkedev1 ONLY. To promote to higher environments, include `sprk_playbooknode` in the next solution export — the new option will travel as part of the entity metadata. The `Add-EntityNameValidatorNodeTypeOption.ps1` script is idempotent and safe to run on any target environment.
- BFF + other code pages (DailyBriefing, SpaarkeAi) were NOT redeployed — they don't reference `entityNameValidator`.
- The errant `sprk_node_type` column created during initial MCP `update_table` investigation was deleted before the live fix (Web API `DELETE` against the EntityDefinitions attribute path); confirmed not present in the post-publish describe.

---

## UAT Hotfix #2 — Node visual parity + required-field validation (2026-06-26)

A second UAT pass surfaced two further gaps in the R4 task 004 wiring that the 08:28 hotfix did not catch:

### Gap A — Canvas node rendered as a default plain box (no icon, type label, or output preview)

**Root cause**: `nodes/index.ts` `nodeTypes` registry (consumed by @xyflow/react v12 via `<ReactFlow nodeTypes={…} />`) was missing the `entityNameValidator` entry. Without a custom node component for the type, React Flow falls back to its default plain-box renderer. Peer nodes (Start, AI Analysis, Wait, etc.) each have a dedicated `*Node.tsx` component that delegates to `BaseNode` with an icon + type label + output preview. Task 004 had wired the type into `BaseNode.tsx:141` color scheme + `NODE_TYPE_INFO` (palette command catalog) + `EntityNameValidatorForm` but never authored a node component for the canvas.

**Pieces that were missing on canvas**:
1. Icon on the left (peers: `Play20Regular`, `BrainCircuit20Regular`, `Clock20Regular` etc.)
2. Category/type label below the node name (peers: "Start" / "AI Analysis" / "Wait" — passed as `typeLabel` prop)
3. `Output: {data.outputVariable}` preview line (peers render conditionally when `data.outputVariable` is set)
4. "Configured" / "Needs configuration" indicator (BaseNode already handles this — it just doesn't run because the registry fallback bypasses BaseNode entirely)

**Fix**: Created `src/client/code-pages/PlaybookBuilder/src/components/nodes/EntityNameValidatorNode.tsx` mirroring the WaitNode / AiAnalysisNode pattern. Wired it into the `nodeTypes` registry at `nodes/index.ts` (new line under `wait`). Also updated `PlaybookCanvas.tsx` MiniMap color mapping to include `entityNameValidator` in the magenta family alongside `wait`.

**Icon chosen**: `ShieldCheckmark20Regular` — semantically signals "validation / scrubbing pass". Selected over `Broom20Regular` (too literal — implies the executor cleans rather than gates) and `ScanText20Regular` (too OCR-adjacent). The shield-with-checkmark aesthetic matches the existing Tool family's intent of "post-LLM gate".

**Type label**: `"Tool"` — per task 003/004 design EntityNameValidator IS a Tool node (distinct from AI Analysis / Output / Control / Workflow families). The label distinguishes it on the canvas without conflating it with neighbors.

### Gap B — Required-field "*" was decorative; save accepted empty values

**Root cause**: The `EntityNameValidatorForm` marked `candidateText` / `allowList` / `outputVariable` with the Fluent `Label required` prop (visual asterisk) but the canvas-time validation in `services/canvasValidation.ts` had no per-ActionType rules for the type. The save-blocking path (`playbookNodeSync.syncNodesToDataverseAndValidate` → `hasValidationErrors`) only enforces what the validator returns; without a rule, missing values pass through silently.

**Fix (full enforcement, mirroring LookupUserMembership pattern)**: Added `validateEntityNameValidatorNode(nodeId, node)` to `canvasValidation.ts` plus three new rule-id values to the `PromptSchemaValidation['rule']` discriminated union. The validator parses `node.data.configJson` for `candidateText` + `allowList` and checks `node.data.outputVariable` (the shared Basic-section field). Missing-required values surface as `'error'` severity which the existing `hasValidationErrors()` save-gate consumes — no new shared-code refactor needed.

This matches how LookupUserMembership has enforced its `entityType` + `outputVariable` required fields since R3 task 043; the pattern was the explicit template per R4 CLAUDE.md "Canonical Code Analogs".

### Files modified

| File | Change |
|---|---|
| `src/client/code-pages/PlaybookBuilder/src/components/nodes/EntityNameValidatorNode.tsx` | New — peer of `WaitNode.tsx` / `AiAnalysisNode.tsx`. |
| `src/client/code-pages/PlaybookBuilder/src/components/nodes/index.ts` | Added export + import + `nodeTypes['entityNameValidator']` entry. |
| `src/client/code-pages/PlaybookBuilder/src/components/canvas/PlaybookCanvas.tsx` | MiniMap node-color switch added `entityNameValidator` to magenta family. |
| `src/client/code-pages/PlaybookBuilder/src/services/canvasValidation.ts` | Added 3 rule-id values + `validateEntityNameValidatorNode` (~50 LOC) + per-node-type loop case. |

### Redeploy timestamp

- **Build start**: 2026-06-26 ~09:04 (webpack production)
- **Inline + deploy completed**: 2026-06-26 09:07:25
- **Bundle size**: 2,975 KB (identical to 08:28 build to within rounding — new node component + validator are < 1 KB minified)
- **Web resource ID**: `3dfd3713-9515-f111-8343-7ced8d1dc988` (UPDATE + PublishXml) — confirmed via MCP `read_query` on `webresource` (`modifiedon` = `2026-06-26T09:07:25`).

### How UAT operator verifies the hotfix landed

1. **Hard refresh** the PlaybookBuilder code page (Ctrl+Shift+R to bypass Dataverse static-asset cache).
2. **Drag an Entity Name Validator** node onto the canvas. The node should now render with: shield-checkmark icon (left), node-name title, "Tool" subtitle, an "Output: output_entityNameValidator" line, and "Needs configuration" red status (because required fields are still empty).
3. **Try to save the playbook without configuring the node** — save should be BLOCKED with three error messages (Candidate text source binding required / Allow-list source binding required / Output Variable required). Console will log `[playbookNodeSync] Sync blocked: 3 validation error(s) found.`.
4. **Open the properties pane**, fill all three required fields (e.g. `{{narrate.output.result}}` for candidate, `{{names.output.result}}` for allow-list, `scrubbedNarrative` for outputVariable), save again — should succeed and the node should flip to "Configured" green status with `Output: scrubbedNarrative` preview.
5. **Regression check**: drag other node types (Start, AI Analysis, Wait, LookupUserMembership); confirm their canvas rendering + validation continue to work as before.

### Notes

- Both fixes are PlaybookBuilder-only — no BFF or other-code-page redeploy needed.
- The localized validator function avoids any churn to shared validation infrastructure; future per-ActionType node forms can follow the same `validate<NodeType>Node` pattern alongside LookupUserMembership.
- The MiniMap color update was a small visual-parity additional fix — without it the entityNameValidator dot would appear neutral grey in the minimap instead of magenta with the rest of the Tool family.


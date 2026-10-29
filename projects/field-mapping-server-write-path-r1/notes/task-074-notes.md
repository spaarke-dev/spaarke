# Task 074 - TrackingFieldTrio Family tool (FR-33)

Branch `task/fm-074` (from the project work branch tip `30204a7c98`). Rigor FULL. Model tier sonnet, effort high.
Gate: 073 merged (RecordFamilyModal, PR #2366) - confirmed present in the tree.

## What was built

| File | Change |
|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/TrackingFieldTrio/types.ts` | New optional prop `onOpenFamily?: () => void`. |
| `.../TrackingFieldTrio/TrackingFieldTrio.tsx` | Third toolbar icon (`OrganizationRegular`, aria-label and tooltip "Family"), rendered only when `onOpenFamily` is supplied; the toolbar condition gains `\|\| onOpenFamily`. Not gated by `canGrantAccess`, not disabled by `disabled` (the Family is a read of what the caller may already read; the server filters it). Token-only styling (reuses `toolbarIconButton`). |
| `src/client/pcf/TrackingFieldTrio/index.ts` | `isFamilyModalOpen` state, `closeFamilyModal`, `onOpenFamily` callback (same shape as `onOpenGrantModal`), and `RecordFamilyModal` mounted inside the existing `WidgetErrorBoundary` fragment with `table: getHostEntity()`, `recordId`, `authenticatedFetch: authenticatedFetchGated` (ADR-028: the modal gets the fetch, never a token). No `onAction` passed, so push / Assigned To stay disabled ("Available after the push update") until task 040. |
| `.../__tests__/TrackingFieldTrio.family.test.tsx` | 17 tests (new). |
| Version files | see below. |

Decision: the icon is offered only when the form has a bound record (`getRecordId()`); with no id (harness, unsaved form) there is no family to list, and the modal is already gated on `recordId` the same way. The grant and email icons are not gated this way; this is a deliberate small difference.

## Tests

- New suite `TrackingFieldTrio.family.test.tsx`: 17 tests. Before the change (component + types reverted to HEAD): 13 failed, 4 passed (the 4 are the "onOpenFamily omitted" backward-compatibility tests, which must pass before and after). After: 17 pass.
- `src/components/TrackingFieldTrio` + `src/components/RecordFamilyModal`: 7 suites, 136 tests pass (existing 91 TrackingFieldTrio tests unchanged and green, including toolbar/accessStatus/emailMembers).
- `index.ts` is a PCF `StandardControl` class and is not loadable in the package's Jest environment (same constraint `TrackingFieldTrio.emailMembers.test.tsx` documents). The wiring suite therefore mounts the REAL toolbar and the REAL `RecordFamilyModal` behind a harness that mirrors index.ts (icon only on a bound record; click opens the modal for the entity + id; the injected fetch is called with `/api/v1/records/{table}/{canonical id}/family`, no headers), run for `sprk_matter`, `sprk_project`, `sprk_workassignment` and `sprk_event`. Also: error state (not a crash) while the family endpoint (task 072) does not answer, nothing fetched while closed, no icon without a record.
- Type-check: `tsc --noEmit` of Spaarke.UI.Components clean for the changed files; the new test file type-checked separately: clean.
- Client-lint: `node client-lint.mjs check` - all 10 packages ok (ui-components, ai-widgets, compose-components, communication-components, document-operations, daily-briefing-components, spaarkeai, legal-workspace-app, reporting, semantic-search). The PCF folder is not a client-lint package; the PCF build's own ESLint step ran clean.

## Production build and bundle delta (NFR-05)

`pwsh scripts/Invoke-PcfBuildProd.ps1 -PcfPath src/client/pcf/TrackingFieldTrio` -> `PASS TrackingFieldTrio: [build] Succeeded` (twice, identical output).

| Build | `out/controls/bundle.js` |
|---|---|
| Baseline (HEAD sources, v1.0.54 code, same toolchain) | 1,076,194 bytes (1.026 MiB) |
| With Family tool (v1.0.55) | 1,092,856 bytes (1.042 MiB) |
| Delta | +16,662 bytes (+16.3 KiB, +1.5%) |

Well under the +5 MB task threshold. The delta is `RecordFamilyModal` + its service + the Fluent `Table*`/`Checkbox`/`BrowseModal` pieces it pulls in that the bundle did not already carry, plus one icon.
Gotcha met on the way: `ensure-dist-fresh` compares file mtimes, and a source file restored from a backup copy keeps its OLD mtime, so the second build used a stale `dist` and failed with TS2353 ("onOpenFamily does not exist"). Touching the two source files fixed it. Not a product defect; it only matters to anyone restoring files by copy.
The committed `Solution/Controls/.../bundle.js` (v1.0.54) is NOT refreshed here: copying the built bundle is part of the deploy (task 076).

## Version bump: 1.0.54 -> 1.0.55 (all 5 locations of the pcf-deploy table)

1. `src/client/pcf/TrackingFieldTrio/ControlManifest.Input.xml` - `version="1.0.55"` and description `(v1.0.55)` (description also now says "grant-access modal + email-members + Family icons").
2. `src/client/pcf/TrackingFieldTrio/index.ts` - `versionText: 'v1.0.55 • Built 2026-10-28'` (the UI version footer; this control has no `.tsx` component of its own) and a v1.0.55 header entry.
3. `src/client/pcf/TrackingFieldTrio/Solution/solution.xml` - `<Version>1.0.55</Version>`.
4. `src/client/pcf/TrackingFieldTrio/Solution/Controls/sprk_Spaarke.Controls.TrackingFieldTrio/ControlManifest.xml` - `version="1.0.55"` and description (same text as 1).
5. `src/client/pcf/TrackingFieldTrio/Solution/pack.ps1` - `$version = "1.0.55"`.

Not touched (stale, owned by the deploy task): `src/dataverse/solutions/SpaarkeMaster/Controls/sprk_Spaarke.Controls.TrackingFieldTrio/` is at 1.0.47.

## Step 3 - the PCF on the four main forms (READ-ONLY, from the repo's solution form XML)

Source: `src/dataverse/solutions/SpaarkeMaster/Entities/{entity}/FormXml/main/`. No form was changed. This is the repo snapshot, not live metadata (no Dataverse call was made); the owner confirmed the Work Assignment form live earlier and it matches.

| Entity | Main form (name) | Form id | Control placed |
|---|---|---|---|
| `sprk_matter` | Matter main form | `{4fa382f2-c273-f011-b4cb-6045bdd6a665}` | Yes - cell `sprk_monitor` in tab `tab_overview`, section `overview_tracking`; custom control bound for formFactor 0, 1 and 2 |
| `sprk_project` | Project main form | `{5aa00242-5212-f111-8342-7ced8d1dc988}` | Yes - tab `tab_overview`, section `overview_tracking`; formFactor 0, 1, 2 |
| `sprk_workassignment` | Work Assignment main form | `{7e578eef-761d-f111-88b3-7c1e520aa4df}` | Yes - tab `tab_overview`, section `general_trackging`; formFactor 0, 1, 2 |
| `sprk_event` | Event main form | `{eaf22dcb-9aff-f011-8406-7c1e525abd8b}` | Yes - tab `tab_overview`, section `tab_overview_section_tracking`; formFactor 0, 1, 2 |

All four bind `monitor` -> `sprk_monitor`, `highPriority` -> `sprk_highpriority`, `accessPermission` -> `sprk_accesspermission`. The control is present on all four, so no escalation was needed.

Things to know (not changed):
- Event main form: `tab_overview` is `expanded="false"`, so the toolbar (and the Family icon) is inside a collapsed tab until the user expands it. The other three tabs are expanded.
- Other `main`-type forms of these tables do NOT carry the control and are not "the main form": Matter "Information" `{071c5a8e-bf5e-4f97-82dd-f829bde04a9c}`; Project "Information" `{d7958438-0538-438f-8734-071f198f18ca}`; Work Assignment "Information" `{5a53d61a-6e79-416d-9e50-8964086fdd10}`; Event "Information" `{f1c03a21-8422-44ed-8096-49645b961bdc}`, "Event Assign Work main form" `{835b8ee8-ba1d-f111-88b3-7ced8d1dc988}`, "Event modal form" `{90d2eff7-6703-f111-8407-7ced8d1dc988}` and five side-pane forms. The Family icon will not appear there.
- The control is ALSO placed on the To Do main form `{eca59df4-1364-f111-ab0c-7ced8ddc4cc6}` (`sprk_todo`), so the Family icon will appear there too (the PCF is shared). Out of the task's four forms; whether `sprk_todo` is a valid Family anchor is task 072's call. If it is not, the modal shows its fixed error state. Owner decision: leave it, or hide the icon for hosts that are not Family anchors (a one-line entity allow-list in index.ts).

## Review (Step 9.5, own diff)

- Scope: 3 source files + 1 test + 4 version files. No stub of the endpoint; no form, deploy or Dataverse write.
- F-class findings: none open. Checked: opt-in/backward compatibility (explicit `undefined` identical to omitted; 4 tests), fail-closed auth unaffected (Family not tied to `canGrantAccess`; the BFF/DelegationRuleFilter-style gate is for writes, the Family is a filtered read, per the modal's own contract), no token passed (fetch only), React 16 safe (no new runtime API), dark mode (tokens only, no hex; dark-theme test with zero console errors), accessible name + tooltip, version in every location, production build judged from output.
- K-class / observations (reported, not fixed): To Do form gets the icon (above); Event tab collapsed by default (above); live behaviour until task 072 ships is the modal's fixed error state ("The family could not be loaded. ...") - expected; the UI test "Family icon on each form" and the dark-mode manual check are for task 076's deploy gate, not run here (no deploy).
- Rework: none after review.

## After task 072 merged (2026-10-29, main session)

- Branch updated from master (11c3739d7e, which carries 072 / #2459). Clean merge; master's TrackingFieldTrio is still 1.0.54, so the 1.0.55 bump stands.
- `src/components/TrackingFieldTrio` + `src/components/RecordFamilyModal`: 7 suites, 136 tests pass. (In a fresh worktree the local packages `@spaarke/sdap-client` and `@spaarke/auth` must be built first, or the untouched emailMembers suite fails to load; an environment step, not a code defect.)
- `Invoke-PcfBuildProd.ps1 -PcfPath src/client/pcf/TrackingFieldTrio`: PASS; bundle 1,092,856 bytes, identical to the measurement above.
- **To Do form question closed:** `sprk_todo` is a Filing family table in `RecordFamilyEdges` (task 070), so the 072 endpoint serves a To Do anchor and the icon there is correct. No allow-list needed.
- The modal's "could not be loaded" state while 072 was absent no longer applies; the live check of the icon on each form is task 076.

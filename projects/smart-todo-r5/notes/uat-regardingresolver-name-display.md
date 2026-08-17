# UAT defect — RegardingResolver "Regarding Name" renders blank

- **Reported**: operator UAT 2026-08-17 (with screenshots), sprk_todo main form, RELATED RECORD panel, RegardingResolver PCF v1.4.8.
- **Fixed in**: v1.4.9 (this note). Built 2026-08-17.
- **Scope**: `src/client/pcf/RegardingResolver/**` only.

## Symptom

On the sprk_todo form the RELATED RECORD panel showed the **Regarding Number**
(e.g. `REAL-2026-123456.01`) but the **Regarding Name** cell was BLANK — even
though the row's `sprk_regardingrecordname` WAS populated ("Real estate
transaction analysis", verified via Web API). Data correct; PCF display wrong.
The regarding had been set via the subgrid **"+ New Task" auto-associate**
(`detectPrePopulatedParent`) path.

## Root cause

The Name cell **single-sourced** its value from the bound pass-through property
`context.parameters.regardingRecordNameField.raw`
(`RegardingResolverApp.tsx`, was line ~827 / render gate ~1402). That is exactly
how the working Number cell reads its value too — **the two reads were already
symmetric**; there was no code-level asymmetry between them.

The failure is in the *reliability of that single source* on the auto-detect
path, not in the render:

- On the subgrid "+ New" / `detectPrePopulatedParent` path the denormalized
  `sprk_regardingrecordname` is committed to the row **out-of-band** — via
  `Xrm.WebApi.updateRecord` (UPDATE mode, inside `applyRegardingSelection`) or
  `setFormTextValue` (CREATE mode, auto-detect Phase 1/2b) — **not** through the
  PCF's own bound-output channel. `index.ts` `getOutputs()` echoes these two
  fields back as pass-through outputs, so the framework does not reliably
  re-materialize `regardingRecordNameField.raw` into the control on the render
  where the name should appear. The bound prop is **empty/stale at render**, so
  `hasRecordName` is false and the cell hides.
- The Number cell reads its value the identical way and shares the identical
  fragility; it merely happened to be present in the operator's repro state.
- Crucially, the display had **no fallback** to the value the resolver already
  resolved in-session (`selectedTarget`) or that lives authoritatively on the
  row (readable via `context.webAPI`).

This is the same class of form-bound-read fragility that **SRFR-043** already
fixed for `sprk_regardingrecordurl`, where an `Xrm.Page`/bound read returned null
even though the row had the value — the fix there was to read the field directly
off the row via `context.webAPI.retrieveRecord`.

> The live-form reason the number happened to render while the name didn't in
> the exact repro cannot be pinned without the live form. The **structural**
> root cause (single-source dependence on a fragile bound pass-through with no
> fallback) is definitive and is what the fix removes.

## Fix (mirrors SRFR-043)

The Name display now resolves through a **precedence chain** instead of a single
bound read (`RegardingResolverApp.tsx`):

1. `boundRecordName` — the bound `regardingRecordNameField.raw` (normal working
   path; identical to how Number reads).
2. `selectedTarget.recordName` — the name the resolver resolved in-session,
   populated by **both** the manual-pick handler AND the auto-detect Phase 1/2b
   writes. Covers CREATE mode (no row to read).
3. `resolvedName` — read **directly off the host row** via
   `context.webAPI.retrieveRecord(hostEntity, hostRecordId, '?$select=sprk_regardingrecordname')`
   when the bound value is empty and no in-session selection exists (UPDATE /
   read-only / fresh load). Mirrors `resolveClickTarget`'s SRFR-043 pattern.

The webAPI read is a VIEW-only retrieve (safe under read-only, parity with the
click handler). It is **skipped** when the bound value is present (authoritative)
or when an in-session `selectedTarget` name exists (no redundant round-trip), and
never fires in CREATE mode (no host record id). Defensive throughout — a
rejection warns and leaves the cell blank (NFR-06 graceful-blank); never throws
to the host form.

Manual-pick and clear flows are unchanged: the bound value still wins when
present, and nothing new writes the name.

## Files changed

| File | Change |
|---|---|
| `RegardingResolver/RegardingResolverApp.tsx` | v1.4.9 docstring entry; `resolvedName` state + webAPI name-fallback effect; `displayName` precedence chain; render uses `displayName`; `BUILD_DATE` → 2026-08-17 |
| `RegardingResolver/index.ts` | `CONTROL_VERSION` 1.4.8 → 1.4.9 |
| `RegardingResolver/ControlManifest.Input.xml` | control `version` 1.4.8 → 1.4.9 |
| `Solution/solution.xml` | `<Version>` 1.4.8 → 1.4.9 |
| `Solution/Controls/sprk_Spaarke.Controls.RegardingResolver/ControlManifest.xml` | control `version` 1.4.8 → 1.4.9 |
| `__tests__/RegardingResolverApp.test.tsx` | +4 new tests (v1.4.9 fallback block); 2 pre-existing SRFR-043 assertions changed from brittle total-call-count to URL-`$select`-specific (the name-fallback now shares `retrieveRecordMock`) |

## Verification

- **jest**: 78 passed / 78 total (2 suites). 4 new tests for the fallback; 2
  pre-existing SRFR-043 tests updated for intent (not behavior regressions —
  they asserted exact `retrieveRecord` call counts on a shared mock that the
  new, legitimate name read now also uses).
- **PCF webpack build**: FAILS in this worktree, but on a **pre-existing,
  unrelated** cause — `@babel/plugin-transform-optional-chaining` chokes on the
  shared lib's transitive dep `Spaarke.UI.Components/node_modules/pdfjs-dist/build/pdf.mjs`
  (pulled via `SprkChat/useChatFileAttachment`). The RegardingResolver modules
  themselves compiled cleanly (`./RegardingResolver/ 96.7 KiB 4 modules`) before
  the bundler reached the shared-lib dep. Not caused by this change. The
  `npm run build` prebuild also fails earlier because sibling `@spaarke/auth` /
  `@spaarke/sdap-client` node_modules are absent in this worktree (environment).
- **hex/rgb**: zero introduced on changed lines (logic + docstring only; no
  new styles/colors).

## Requires operator LIVE verification

No live form available to this agent. Operator must confirm on the deployed
control:

1. Deploy v1.4.9; hard-refresh (Ctrl+Shift+R); confirm footer reads
   `v1.4.9 • Built 2026-08-17`.
2. On a sprk_todo created via subgrid "+ New Task" auto-associate: the RELATED
   RECORD panel shows **both** Regarding Number AND Regarding Name.
3. Regression: manual pick still shows the name; clearing regarding hides the
   name cell; read-only form still displays the name.

## ADR notes

- **ADR-024** (RegardingResolver canonical, no AssociationResolver): unchanged —
  no write logic added; the fallback is a read-only display resolution.
- **ADR-021** (Fluent v9 tokens, zero hex/rgb): no new styles/colors added.
- **ADR-028 / ADR-013**: fix uses the PCF's own `context.webAPI` (Xrm.WebApi) for
  a Dataverse row read — no BFF, no direct MSAL — consistent with the control's
  existing SRFR-043 data access.

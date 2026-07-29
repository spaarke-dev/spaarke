# Task 023 — Lift the `TrackingFieldTrio` generic core to `@spaarke/ui-components`

**Status:** ✅ complete · **Rigor:** FULL (sonnet/high) · **Date:** 2026-07-28
**Spec:** FR-14 (entity-agnostic tracking-flags trio) + FR-18 (two-layer extraction) · NFR-04 (regression-free) · NFR-05 (no React-18/19-only API, no `as React.ComponentType` cast) · ADR-021 (Fluent v9/dark mode) · ADR-022 (PCF platform libraries) · design Lens 4

---

## What shipped

Lifted the `TrackingFieldTrio` PCF's rendering core (Monitor toggle + High Priority toggle + Access
Permission segmented picker) from `src/client/pcf/TrackingFieldTrio/TrackingFieldTrioApp.tsx` into
`@spaarke/ui-components` at `src/client/shared/Spaarke.UI.Components/src/components/TrackingFieldTrio/`,
making it **entity-agnostic**: the shared core no longer hardcodes `sprk_communication`'s Access
Permission choice values (`100000000`/`100000001`/`100000002`) or their labels. It renders whatever
segments (`{value, label, color?}[]`) the caller injects via `accessPermissionOptions`, in the order
supplied. Field-display labels (`monitorLabel`/`highPriorityLabel`/`accessPermissionLabel`) and a
`versionText` footer string are also fully caller-supplied.

The PCF's `index.ts` is now the **only** place in the tree that knows about `sprk_communication`'s
specific Access Permission values — `getAccessPermissionOptions()` reads the real Dataverse OptionSet
metadata (value + label + color) when bound, and falls back to a hardcoded Standard/Limited/Restricted
triple (no color) when metadata is unavailable (harness/test environments), so the segmented control
always renders 3 segments exactly as it did pre-lift (NFR-04).

## Shared-lib layout (new)

```
src/client/shared/Spaarke.UI.Components/src/components/TrackingFieldTrio/
  TrackingFieldTrio.tsx   ← the lifted, entity-agnostic component
  types.ts                ← ITrackingFieldTrioProps + IAccessPermissionOption (injected-props contract)
  index.ts                ← component-folder barrel
  __tests__/
    TrackingFieldTrio.test.tsx  ← 7 tests: read/write (4), entity-agnostic (2), dark mode (1)
```

Exported from the top-level barrel via `src/components/index.ts` → `export * from './TrackingFieldTrio'`
(picked up automatically by `src/index.ts`'s `export * from './components'`).

## The entity-agnostic transformation (what actually changed vs. the pre-lift hybrid)

The PRE-lift component was a **hybrid**: `accessPermissionOptions: IAccessPermissionOption[]` was already
injected, but it was used ONLY to look up per-option **colors** — the segments themselves (which 3
buttons render, their values, their labels) were driven by a hardcoded local `ACCESS_PERMISSION_SEGMENTS`
array (`Standard=100000000`/`Limited=1`/`Restricted=2`). The lift completes the injection: the shared
core's render loop now iterates the INJECTED `accessPermissionOptions` array directly (value + label +
color), so a caller with a completely different option set (different values, labels, count) gets a
faithful render — proven by the "entity-agnostic" test using a 2-segment `Public`/`Confidential` set
with no `sprk_communication` value bleeding through.

The color-fallback logic also changed from **value-keyed** (`FALLBACK_SEGMENT_COLORS: Record<number, ...>`
keyed on the exact `sprk_communication` integers) to **position-based** (`DEFAULT_SEGMENT_FALLBACK_COLORS[idx
% length]`), which is what makes the default legitimate per FR-14's "allowed only as a prop default, not
entity-specific hardcoding" rule — and it renders byte-identical colors for `sprk_communication` today
because the PCF still injects Standard/Limited/Restricted in that order.

## The NFR-05 cast avoidance (worth flagging for task 035 + future lifts)

ADR-022's "Shared-Library React-Version Drift" section documents that PCF consumers of `@spaarke/ui-components`
JSX components often need an `as unknown as React.ComponentType<Props>` cast at the import boundary
(precedent: `CommunicationTimeline`, `ConversationView`, `AiSummaryPopover`, `TimelineComposeBox`,
`PolymorphicPicker` all do this). NFR-05 explicitly forbids that cast for this component.

**Resolution**: added a `paths` remap to `TrackingFieldTrio/tsconfig.json` that pins `react`/`react-dom`
module resolution to the PCF's own local `@types/react` (16) for the ENTIRE compilation unit — including
the shared lib's emitted `dist/*.d.ts` files:

```jsonc
"paths": {
  "react": ["./node_modules/@types/react"],
  "react/*": ["./node_modules/@types/react/*"],
  "react-dom": ["./node_modules/@types/react-dom"],
  "react-dom/*": ["./node_modules/@types/react-dom/*"]
}
```

This is not a new trick — it mirrors the existing `MatterHeader` PCF's tsconfig, which is why
`RecordHeaderShell`/`FieldGrid`/`TextField`/`TextareaField`/`LookupField` are consumed there with zero
cast while `CommunicationTimeline` et al. need one (those PCFs' tsconfigs don't have this remap). Combined
with `@spaarke/ui-components/dist/...` deep imports (not source imports — ADR-022's actual drift trigger),
this fully avoids the cast. **Recommendation for task 035** (or any future PCF lift of a JSX-returning
shared component): check the consuming PCF's `tsconfig.json` for this `paths` remap before reaching for
the cast — it may not be needed.

## PCF-side changes (`index.ts`)

- Imports the shared component **aliased** as `SharedTrackingFieldTrio` — the PCF's exported class MUST
  stay named `TrackingFieldTrio` to match `constructor="TrackingFieldTrio"` in
  `ControlManifest.Input.xml`; importing the shared component under the same bare name would have
  collided.
- `getAccessPermissionOptions()`: maps real OptionSet metadata to `{value, label, color}[]`; falls back to
  `FALLBACK_ACCESS_PERMISSION_OPTIONS` (Standard/Limited/Restricted, no color) when metadata is empty.
- Added `versionText: 'v1.0.6 • Built 2026-07-28'` prop (the shared core no longer hardcodes a version
  string — that's PCF-specific and stays in the PCF).
- `ReactDOM.render`/`unmountComponentAtNode` lifecycle, `getOutputs()`, `init()`/`updateView()` — all
  byte-identical to pre-lift (NFR-04).
- Added `@spaarke/ui-components` as a `dependencies` entry + `prebuild`/`prebuild:prod` scripts (runs
  `ensure-dist-fresh.js`), mirroring `CommunicationConnections`/`MatterHeader`.
- Bumped `ControlManifest.Input.xml` version 1.0.5 → 1.0.6 (out of the POML's literal step list but
  matches the repo's PCF-version-bump convention; did NOT touch the packaged `Solution/` folder —
  that's deployment scope, not part of this task).

## Build/test verification

- `npm run build` (shared lib, `tsc`) — clean.
- `npx jest src/components/TrackingFieldTrio` (shared lib) — 7/7 pass; coverage 91.66% stmts / 95.45%
  lines on `TrackingFieldTrio.tsx` (clears ADR-012's 90%+ MUST).
- `npm install --legacy-peer-deps --no-audit --no-fund` (PCF) — links `@spaarke/ui-components` via
  `file:../../shared/Spaarke.UI.Components`.
- `npm run build:prod` (PCF, `pcf-scripts build --buildMode production`) — clean, bundle 7.63 KiB (well
  under the 5 MB PCF cap), **zero TS2786 / cast needed**.
- `npm run lint` (PCF) + `npx eslint` (shared component folder) — clean.
- `/code-review` + `/adr-check` (Step 9.5) — 0 Critical / 0 Warning / 0 Violation on both.

## For task 035 (Phase-3 reading-pane tracking view)

Import: `import { TrackingFieldTrio, type ITrackingFieldTrioProps, type IAccessPermissionOption } from
'@spaarke/ui-components';` (React-19 code page — barrel import is safe, no deep-dist-path or cast
needed on that side either, since Code Pages use the shared lib's native React 19 types). Supply your own
`accessPermissionOptions`/labels — the component makes no assumption about which entity or option set you
pass in.

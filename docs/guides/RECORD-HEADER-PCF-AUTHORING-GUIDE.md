# Record Header PCF Authoring Guide

> **Status**: Published (this project's Phase 4 deliverable)
> **Last Updated**: 2026-07-03
> **Related project**: [`projects/record-header-and-notepad-r1/`](../../projects/record-header-and-notepad-r1/)
> **Related shared library**: [`@spaarke/ui-components`](../../src/client/shared/Spaarke.UI.Components/) — `HeaderToolbar`, `RecordHeaderShell`, `FieldGrid`, field renderers, `useRecordFieldValues`, `useRelatedCount`, `useRecordHeaderToolbarActions`

---

## Purpose

Explains how to ship a new per-entity Record Header PCF (`ProjectHeaderPcf`, `InvoiceHeaderPcf`, `EventHeaderPcf`, etc.) that composes the shared primitives added to `@spaarke/ui-components` in `record-header-and-notepad-r1`. Each new per-entity PCF is ~80 LOC of thin composition — a manifest, a ~20-LOC PCF class, and a ~40-LOC view file that wires the shared toolbar hook to the shared field grid.

This guide is the sustained value of the project. Follow it and you can ship a new entity's header card without re-reading `spec.md`.

For the design rationale (why a shared primitive set, why one PCF per entity, why the sparkle popover reads inline), see [`spec.md`](../../projects/record-header-and-notepad-r1/spec.md).

---

## Prerequisites

- Node 18+ and npm
- PAC CLI installed (`pac`) — see [`PCF-DEPLOYMENT-GUIDE.md`](PCF-DEPLOYMENT-GUIDE.md)
- Workspace-level shared library present at `src/client/shared/Spaarke.UI.Components/` and built (`npm install && npm run build` inside that folder)
- Familiarity with Fluent UI v9 semantic tokens per [ADR-021](../adr/ADR-021-fluent-ui-design-system.md) — no hex/rgb literals allowed
- Familiarity with the PCF platform-library React 16/17 boundary per [ADR-022](../adr/ADR-022-pcf-platform-libraries.md) — shared components consumed by PCFs are constrained to React 16-safe APIs
- Understanding of the ADR-024 dual-field polymorphic pattern per [ADR-024](../adr/ADR-024-polymorphic-resolver-pattern.md) — memo/todo creates go through `PolymorphicResolverService.applyResolverFields()`; consumers of the Record Header don't need to know the details, but the Notepad launch contract sits on top of that pattern

---

## Shared Primitives Overview

All primitives live under `src/client/shared/Spaarke.UI.Components/src/`. Consume them via deep-path imports (see [Bundle Optimization](#bundle-optimization--mandatory-for-consuming-pcfs) below).

| Primitive | Path | Purpose |
|---|---|---|
| `HeaderToolbar` | `components/HeaderToolbar/` | Fluent v9 flex container: title (left, ellipsis) + right-icon slots with `<CounterBadge>` overlays. Every slot is wrapped in `<Tooltip relationship="label">` for a11y. |
| `RecordHeaderShell` | `components/RecordHeader/` | Fluent v9 card container with `HeaderToolbar` on top and a body slot; renders Skeleton placeholders when `loading===true`. |
| `FieldGrid` | `components/RecordHeader/` | CSS grid (`grid-template-columns: repeat(columns, 1fr)`) supporting 2 or 3 columns; children carry a `span` prop (1..3). A `span=3` field starts a new row. |
| `TextField`, `LookupField` (top-level barrel exports it as `RecordHeaderLookupField` due to a name collision with the pre-existing top-level `LookupField`), `OptionSetField`, `TextareaField` | `components/RecordHeader/fields/` | Field renderers; each takes `label`, `value`, `span`. `LookupField` opens the target via `Xrm.Navigation.navigateTo({ pageType: "entityrecord", ... })`; `TextareaField` clamps `max-height` and offers a Fluent v9 "show more" popover for the full body. |
| `useRecordFieldValues(entity, recordId, fields)` | `hooks/useRecordFieldValues.ts` | `Xrm.WebApi.retrieveRecord` wrapper. Returns `{ values, loading, error }`. Stable dep key — refetches only when `recordId` or `fields` change. |
| `useRelatedCount(relatedEntity, filter)` | `hooks/useRelatedCount.ts` | `Xrm.WebApi.retrieveMultipleRecords` with `$count=true&$top=0`. Filter-agnostic — the consumer builds the OData filter (e.g. `_regardingobjectid_value eq {guid}` for `sprk_todo`, `_sprk_regardingmatter_value eq {guid}` for `sprk_memo` on Matter per the ADR-024 dual-field pattern). Re-queries on mount and on window-focus. |
| `useRecordHeaderToolbarActions({ entity, recordId, recordSummary })` | `hooks/useRecordHeaderToolbarActions.ts` | The linchpin hook. Returns `{ toolbarProps, sparklePopoverOpen, setSparklePopoverOpen, sparklePopoverContent }` — three wired icon slots (sparkle / checkmark / annotation) with per-slot badge counts, popover state for sparkle (renders the record summary body inline; empty-state when null), and `Xrm.Navigation.navigateTo` calls for the SmartTodo modal (85%×85%) and Notepad code page (70%×80%). Consumers pass `recordSummary` from `useRecordFieldValues` so the hook never issues a second Xrm call for the summary. |

The design constraint per [ADR-012](../adr/ADR-012-shared-component-library.md) and [ADR-011](../adr/ADR-011-dataset-pcf.md): shared primitives are typed components — no runtime schemas, no per-entity conditionals inside the hook. Per-entity thin PCFs specialize by passing an `entity` logical name and a field list.

---

## Per-Entity Thin PCF Pattern

The thin PCF has three files: a manifest, a `control/index.ts` class, and a `control/<Entity>HeaderView.tsx` view. `MatterHeader` (this project's reference) totals 90 LOC (verified in `notes/bundle-size.md` — 10 LOC of headroom under NFR-02's ≤100 ceiling). A new entity's PCF is structurally identical.

### 1. Manifest — `control/ControlManifest.Input.xml`

```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke.Records" constructor="ProjectHeader" version="1.0.0"
           display-name-key="Project Header"
           description-key="Compact summary card + 3-action toolbar for Project records."
           control-type="virtual">

    <!-- Optional recordId override; defaults to context.mode.contextInfo.entityId -->
    <property name="recordId" display-name-key="Record ID (override)"
              description-key="Optional override for the record GUID."
              of-type="SingleLine.Text" usage="input" required="false" />

    <resources>
      <code path="index.ts" order="1"/>
      <platform-library name="React" version="16.14.0" />
      <platform-library name="Fluent" version="9.46.2" />
    </resources>

    <feature-usage>
      <uses-feature name="WebAPI" required="true" />
      <uses-feature name="Utility" required="true" />
      <uses-feature name="Navigation" required="true" />
    </feature-usage>
  </control>
</manifest>
```

Key points:

- `constructor="<Entity>Header"` — one word, capitalized. Namespace is always `Spaarke.Records`.
- `version="1.0.0"` — the version number appears in **four** locations (manifest, `control/version.ts`, Solution manifest, Solution Control Manifest). Per [`PCF-DEPLOYMENT-GUIDE.md`](PCF-DEPLOYMENT-GUIDE.md), keep them in sync.
- **Exactly one** input property (`recordId`, optional override). Do NOT add `entity` or `fieldSchema` properties — the entity is compile-time-fixed in the view file. This is what makes the PCF "per-entity" and thin (per [ADR-006](../adr/ADR-006-pcf-over-webresources.md) + [ADR-011](../adr/ADR-011-dataset-pcf.md)).
- `control-type="virtual"` + `<platform-library>` entries enable platform-library modern theming — Fluent v9 auto-applies the host theme without a manual `FluentProvider` wrap.
- Manifest lives at `control/ControlManifest.Input.xml` (co-located with `index.ts`). `pcf-scripts` places `generated/` next to the manifest, so this layout is required.

### 2. PCF class — `control/index.ts` (~20 LOC)

```ts
import { IInputs, IOutputs } from './generated/ManifestTypes';
import * as React from 'react';
import { ProjectHeaderView } from './ProjectHeaderView';

export class ProjectHeader implements ComponentFramework.ReactControl<IInputs, IOutputs> {
  public init(
    _context: ComponentFramework.Context<IInputs>,
    _notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary
  ): void {
    // No async init; host-context Xrm.WebApi only (no @spaarke/auth).
  }

  public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    // context.mode.contextInfo exists at runtime but is not in @types/powerapps-component-framework.
    // Type-cast pattern mirrors SemanticSearchControl / ScopeConfigEditor.
    const contextInfo = (context.mode as unknown as { contextInfo?: { entityId?: string } }).contextInfo;
    const recordId = context.parameters.recordId?.raw || contextInfo?.entityId || '';
    return React.createElement(ProjectHeaderView, { recordId });
  }

  public getOutputs(): IOutputs {
    return {};
  }

  public destroy(): void {
    // No cleanup — no listeners, no timers, no auth handles.
  }
}
```

Use `ComponentFramework.ReactControl<IInputs, IOutputs>` (not `StandardControl`). Rationale (task 021 D-01):

1. Matches every other Spaarke PCF (`SemanticSearchControl`, `DocumentRelationshipViewer`).
2. Platform-library auto-theming with `control-type="virtual"` — no manual `FluentProvider` needed (approach 1 in `.claude/patterns/pcf/fluent-v9-modern-theming.md`).
3. React 16-API compatible per [ADR-022](../adr/ADR-022-pcf-platform-libraries.md) — no `createRoot`, no `react-dom/client`, no concurrent APIs. Grep verifies.
4. Saves ~5 LOC over the `StandardControl` + `ReactDOM.render()` pattern.

### 3. View — `control/<Entity>HeaderView.tsx` (~40 LOC)

The heart of the per-entity PCF. Below is a hypothetical `ProjectHeaderView.tsx` template modeled on the `MatterHeaderView.tsx` reference:

```tsx
import * as React from 'react';
import { Popover, PopoverSurface } from '@fluentui/react-components';

// Deep-path imports to bypass the top-level `@spaarke/ui-components` barrel
// (drags EntityCreationService → mammoth chain, ~550 KiB). This is the
// convention for ALL PCFs consuming @spaarke/ui-components until the shared
// lib grows a public `exports` field. See "Bundle optimization" section.
import {
  FieldGrid,
  RecordHeaderShell,
  TextField,
  TextareaField,
} from '@spaarke/ui-components/dist/components/RecordHeader';
import { LookupField as RecordHeaderLookupField } from '@spaarke/ui-components/dist/components/RecordHeader/fields';
import { useRecordFieldValues, useRecordHeaderToolbarActions } from '@spaarke/ui-components/dist/hooks';

const ENTITY = 'sprk_project';
const FIELDS = [
  'sprk_projectnumber',
  'sprk_projectname',
  'sprk_manager',        // lookup
  'sprk_status',         // optionset (or lookup, depending on schema)
  'sprk_description',
  'sprk_recordsummary',  // fetched inline for the sparkle popover (FR-08)
];

export interface IProjectHeaderViewProps {
  /** Record GUID (no braces). Empty string = "no record selected". */
  recordId: string;
}

export const ProjectHeaderView: React.FC<IProjectHeaderViewProps> = ({ recordId }) => {
  const { values, loading } = useRecordFieldValues(ENTITY, recordId, FIELDS);
  const { toolbarProps, sparklePopoverOpen, setSparklePopoverOpen, sparklePopoverContent } =
    useRecordHeaderToolbarActions({
      entity: ENTITY,
      recordId,
      recordSummary: (values?.sprk_recordsummary ?? null) as string | null,
    });

  return (
    <>
      <RecordHeaderShell toolbar={toolbarProps} loading={loading}>
        <FieldGrid columns={3}>
          <TextField span={1} label="Project Number" value={values?.sprk_projectnumber as string} required />
          <TextField span={2} label="Project Name" value={values?.sprk_projectname as string} />
          <RecordHeaderLookupField span={1} label="Manager" value={values?.sprk_manager as never} />
          <RecordHeaderLookupField span={1} label="Status" value={values?.sprk_status as never} />
          <TextareaField span={3} label="Description" value={values?.sprk_description as string} />
        </FieldGrid>
      </RecordHeaderShell>
      <Popover open={sparklePopoverOpen} onOpenChange={(_, d) => setSparklePopoverOpen(d.open)}>
        <PopoverSurface>{sparklePopoverContent}</PopoverSurface>
      </Popover>
    </>
  );
};
```

Notes:

- The consumer owns the `<Popover>` shell so the sparkle button rendered inside `HeaderToolbar` remains the anchor.
- The view is compile-time-fixed on `ENTITY = 'sprk_project'`. Per [ADR-011](../adr/ADR-011-dataset-pcf.md), we prefer typed per-entity components over runtime schema resolution.
- Pass `recordSummary` to `useRecordHeaderToolbarActions` so the sparkle popover reads the summary body inline — no separate `Xrm.WebApi` call. If the entity doesn't have `sprk_recordsummary` populated yet, the popover renders "No summary yet" (empty-state per FR-08).
- The refresh icon in the sparkle popover is **rendered but unwired** in R1 per FR-08a. A follow-on project will wire it to a new BFF endpoint; do NOT add BFF code here per NFR-07.
- Version footer (task 022 pattern from `MatterHeaderView.tsx`) is optional — MatterHeader renders one via `makeStyles` at bottom-right; add it to your view or skip it. In dev/harness mode it's helpful for QA; production may hide it via a build flag.

---

## Bundle Optimization — MANDATORY for Consuming PCFs

Discovered in task 024 (see [`notes/bundle-size.md`](../../projects/record-header-and-notepad-r1/notes/bundle-size.md)): the shared library's top-level barrel (`dist/index.js`) re-exports `EntityCreationService`, which imports `@spaarke/sdap-client`, which pulls in `mammoth` (docx-to-HTML converter) plus `xmldom`, `bluebird`, `xmlbuilder`, `dingbat-to-unicode`, `lop` — **~550 KiB of pre-minified source** that a header card doesn't need. Webpack cannot tree-shake through the CommonJS barrel emit.

The fix is three coordinated changes. **All three must be present** or the bundle bloats past NFR-04's 250 KB ceiling. Post-fix `MatterHeaderPcf` measures 38 KB ungzipped / 10 KB gzipped — a 43× reduction.

### Fix 1 — `featureconfig.json` at PCF root

```json
{
  "pcfReactPlatformLibraries": "on",
  "pcfAllowCustomWebpack": "on"
}
```

Without this, `<platform-library>` entries in the manifest are declared but not enforced — React + Fluent still get bundled.

### Fix 2 — `webpack.config.js` at PCF root

```javascript
module.exports = {
  optimization: {
    usedExports: true,
    sideEffects: true,
    innerGraph: true,
    providedExports: true,
  },
  module: {
    rules: [
      {
        // Mark @fluentui/react-icons as side-effect-free for tree-shaking
        test: /[\\/]node_modules[\\/]@fluentui[\\/]react-icons[\\/]/,
        sideEffects: false,
      },
    ],
  },
};
```

Marks `@fluentui/react-icons` as side-effect-free so webpack drops unused icon chunks (~6.8 MB otherwise).

### Fix 3 — deep-path imports in the view file

```typescript
// GOOD — targeted sub-barrels; no EntityCreationService drag-in
import { FieldGrid, RecordHeaderShell, TextField, TextareaField }
  from '@spaarke/ui-components/dist/components/RecordHeader';
import { LookupField as RecordHeaderLookupField }
  from '@spaarke/ui-components/dist/components/RecordHeader/fields';
import { useRecordFieldValues, useRecordHeaderToolbarActions }
  from '@spaarke/ui-components/dist/hooks';

// BAD — top-level barrel pulls the docx pipeline into your bundle
// import { RecordHeaderShell, FieldGrid, ... } from '@spaarke/ui-components';
```

`SemanticSearchControl` and `DocumentRelationshipViewer` follow this same convention. Encode it in your view file and it stays there until the shared lib grows a public `exports` field.

---

## Notepad Launch Contract

The Notepad code page ships in this project at `src/solutions/Notepad/` and deploys as the `sprk_notepad_page` webresource. Any Spaarke surface can launch it via a URL contract (spec NFR-09 — external API surface; breaking changes require a migration plan).

```typescript
Xrm.Navigation.navigateTo(
  {
    pageType: 'webresource',
    webresourceName: 'sprk_notepad_page',
    data: `regardingEntity=${entity}&regardingId=${recordId}`,
  },
  { target: 2, position: 1, width: { value: 70, unit: '%' }, height: { value: 80, unit: '%' } }
);
```

Contract:

- **`regardingEntity`** — logical name. Supported (schema-verified via Dataverse MCP in task 001): `sprk_matter`, `sprk_project`, `sprk_event`, `sprk_invoice`, `sprk_budget`, `sprk_workassignment`.
- **`regardingId`** — record GUID (with or without braces).
- **Unsupported entity** — Notepad renders a Fluent v9 `MessageBar` warning ("Notepad does not support memos for entity type '{X}'. Contact your admin.") and does not attempt CRUD.
- **Missing params** — Notepad renders a `MessageBar` error ("Missing regarding context") with a Close button that dismisses the modal.

Consumers today: `useRecordHeaderToolbarActions` (annotation icon at 70%×80%). Consumers tomorrow: ribbon buttons, workspace widgets, any surface that regards a Matter/Project/Event/Invoice/Budget/WorkAssignment.

Under the hood, Notepad memo-create uses `PolymorphicResolverService.applyResolverFields()` from `@spaarke/ui-components` per [ADR-024](../adr/ADR-024-polymorphic-resolver-pattern.md). The URL contract is entity-agnostic; memo-create is schema-limited to the 6 supported parents. This is fine — the launch surface doesn't need to know.

---

## Testing + Deploying

### Testing

Unit tests for field renderers and hooks per [ADR-038](../adr/ADR-038-testing-strategy.md); integration test composing all four field renderers in a `FieldGrid`; integration test asserting `useRecordHeaderToolbarActions` returns three wired slots. The Notepad code page uses a jsdom + `react-dom/client` harness — `@testing-library/react` is intentionally NOT in devDeps to keep the bundle lean.

Any test file that hits `Xrm.WebApi` should mock it via a Jest `jest.fn()` shim (SmartTodo + MatterHeader integration tests are the canonical references).

### Deploying

Standard PCF flow per the `/pcf-deploy` skill and [`PCF-DEPLOYMENT-GUIDE.md`](PCF-DEPLOYMENT-GUIDE.md):

1. `npm run build:prod` in the PCF folder. **NOT** `npm run build` — see `.claude/FAILURE-MODES.md#AP-1`.
2. Copy `out/controls/control/{bundle.js, ControlManifest.xml}` to `Solution/Controls/sprk_Spaarke.Records.<Entity>Header/`.
3. Run `pack.ps1` from `Solution/` — produces the zipped solution.
4. `pac solution import --path Solution/bin/<Entity>HeaderPcf_v1_0_0.zip --publish-changes`.
5. Bind the PCF to the entity form's header section via the form designer (maker task).

For the Notepad code page, use `/code-page-deploy` per [ADR-026](../adr/ADR-026-full-page-custom-page-standard.md).

---

## Troubleshooting

Gotchas caught during this project (root causes documented in `notes/task-021-deviations.md` + `notes/bundle-size.md`):

| Symptom | Root Cause | Fix |
|---|---|---|
| Bundle > 1 MB | Missing one of the three bundle-optimization fixes | Apply all three: `featureconfig.json` + `webpack.config.js` + deep-path imports. |
| `TS2339: Property 'contextInfo' does not exist on type 'Mode'` | `@types/powerapps-component-framework@1.3.18` gap | Cast: `(context.mode as unknown as { contextInfo?: { entityId?: string } }).contextInfo?.entityId`. |
| `TS5083: Cannot read file '.../tsconfig_base.json'` | Missing `tsconfig.json` at PCF root | Copy from `SemanticSearchControl/tsconfig.json`. |
| `Cannot find module 'ajv/dist/compile/codegen'` at build | `pcf-scripts` pins `ajv@6`, but `ajv-keywords@5.1.0` needs `ajv@^8` | `npm install ajv@^8.12.0 --save-dev --legacy-peer-deps` in the PCF folder. |
| `Can't resolve '@spaarke/sdap-client'` from `EntityCreationService.js` | `Spaarke.SdapClient` symlink target has no `dist/` | `npm install && npm run build` inside `src/client/shared/Spaarke.SdapClient/` first. |
| `Cannot find module './generated/ManifestTypes'` | Manifest at PCF root but code under `control/` — `pcf-scripts` puts `generated/` next to the manifest | Move `ControlManifest.Input.xml` into `control/` and use `<code path="index.ts">` relative to the manifest. |
| SmartTodo modal opens the wrong page | Wrong webresource name in `toolbarLaunchDefaults` | The name is `sprk_smarttodo` (verified via Dataverse MCP in task 020) — NOT `sprk_smarttodo_page`. |

---

## FAQ

1. **Can I add a 6th field?** Yes — increase `FieldGrid` rows or use `span=2`/`span=3` wisely. It's a CSS grid; adding rows is natural. Compact cards look best at 5–7 visible fields.
2. **Can I use a `LookupField` twice?** Yes — repeat as needed. If you need `OptionSetField` instead (e.g., for `sprk_status`), just swap the renderer. Both are exported from `components/RecordHeader/fields/`.
3. **Do I need a `webpack.config.js`?** YES. Without it, Fluent icons alone bloat the bundle past the 250 KB ceiling. Copy the one from `MatterHeader/`.
4. **Can I add a 4th toolbar action?** Only by extending `useRecordHeaderToolbarActions` in the shared lib (a separate project). Do not fork the hook per [CLAUDE.md §11 (Component Justification)](../../CLAUDE.md). The three actions (sparkle / checkmark / annotation) are the current contract; a 4th would need a spec change and a follow-on shared-lib update.
5. **What if my entity isn't in the Notepad supported list?** Notepad shows a warning `MessageBar` and no CRUD. To extend: (a) add the entity-specific lookup + resolver fields to `sprk_memo` per [ADR-024](../adr/ADR-024-polymorphic-resolver-pattern.md), (b) extend the `SUPPORTED_MEMO_PARENTS` map in `@spaarke/ui-components` `toolbarLaunchDefaults`, (c) update `PolymorphicResolverService` nav-prop discovery if needed. This is a follow-on project per §11 (extend existing, don't fork).
6. **Should I use `@spaarke/auth` for Dataverse reads?** NO — per NFR-05 and [ADR-028](../adr/ADR-028-spaarke-auth-architecture.md), Record Header PCFs are host-context surfaces. Use `Xrm.WebApi` exclusively (which the shared hooks already do). Grep at code review time verifies. Same rule applies to [ADR-032](../adr/ADR-032-bff-nullobject-kill-switch.md) — no BFF surface means no null-object kill-switch concerns here.

---

## References

- **Spec + notes**: [`projects/record-header-and-notepad-r1/spec.md`](../../projects/record-header-and-notepad-r1/spec.md); notes in `projects/record-header-and-notepad-r1/notes/`.
- **ADRs cited**: [ADR-006](../adr/ADR-006-pcf-over-webresources.md), [ADR-011](../adr/ADR-011-dataset-pcf.md), [ADR-012](../adr/ADR-012-shared-component-library.md), [ADR-021](../adr/ADR-021-fluent-ui-design-system.md), [ADR-022](../adr/ADR-022-pcf-platform-libraries.md), [ADR-024](../adr/ADR-024-polymorphic-resolver-pattern.md), [ADR-026](../adr/ADR-026-full-page-custom-page-standard.md), [ADR-028](../adr/ADR-028-spaarke-auth-architecture.md), [ADR-032](../adr/ADR-032-bff-nullobject-kill-switch.md), [ADR-038](../adr/ADR-038-testing-strategy.md).
- **Skills**: `/pcf-deploy`, `/code-page-deploy`, `/fluent-v9-component`.
- **Reference PCF**: [`src/client/pcf/MatterHeader/`](../../src/client/pcf/MatterHeader/) — the copy-and-adapt starting point for a new entity.
- **Companion guides**: [`SHARED-UI-COMPONENTS-GUIDE.md`](SHARED-UI-COMPONENTS-GUIDE.md), [`PCF-DEPLOYMENT-GUIDE.md`](PCF-DEPLOYMENT-GUIDE.md).

---

*Maintained by the `record-header-and-notepad-r1` project team. Extend this guide as new per-entity PCFs are shipped.*

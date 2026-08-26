/**
 * RecordHeader barrel — record-header-and-notepad-r1 (FR-02 / FR-03).
 *
 * Exports the outer card chrome (`RecordHeaderShell`), the CSS-grid
 * layout primitive (`FieldGrid`), and their public prop types. Field
 * renderers (`TextField`, `LookupField`, `OptionSetField`,
 * `TextareaField`) are added by tasks 005–008 in this project; R2 adds
 * `BooleanField`, `DateField`, and `NumberField` (r2 tasks 010–012, wired
 * here by r2 task 015).
 *
 * Top-level shared-lib `src/index.ts` re-exports are wired by task 013;
 * this barrel is the module-scoped entry point.
 */

export { RecordHeaderShell } from './RecordHeaderShell';
export type { IRecordHeaderShellProps } from './types';

export { FieldGrid } from './FieldGrid';
export type { IFieldGridProps } from './FieldGrid';

// Field renderers (record-header-and-notepad-r1 FR-04, tasks 005–008).
//
// Explicit-named re-exports (NOT `export * from './fields'`) because the
// field-renderer `LookupField` + `ILookupFieldProps` symbol names collide
// with the pre-existing top-level `src/components/LookupField/` component
// (a reusable search-as-you-type lookup, unrelated to record-header display).
// The RecordHeader flavor is a display-only renderer for FieldGrid cells,
// so we alias it as `RecordHeaderLookupField` at the top-level barrel to
// avoid an `export * → TS2308 ambiguous` error while preserving BOTH surfaces.
//
// TextField / OptionSetField / TextareaField names do NOT collide, so they
// re-export un-aliased. The `ILookupFieldValue` type is unique to the
// record-header flavor and re-exports un-aliased.
//
// Consumers who prefer un-aliased names can `import { LookupField } from
// '@spaarke/ui-components/components/RecordHeader/fields'` (sub-path import).
//
// record-header-and-notepad-r2 task 015: BooleanField / DateField / NumberField
// and their prop types were appended in the same explicit-named shape. A
// repo-wide grep of every `index.ts` barrel under `src/` confirmed none of
// those six symbols collides with an existing export, so all three re-export
// un-aliased (no second `RecordHeaderLookupField`-style alias was needed).
export { TextField } from './fields';
export type { ITextFieldProps } from './fields';

export { OptionSetField } from './fields';
export type { IOptionSetFieldProps } from './fields';

export { TextareaField } from './fields';
export type { ITextareaFieldProps } from './fields';

export { LookupField as RecordHeaderLookupField } from './fields';
export type { ILookupFieldProps as IRecordHeaderLookupFieldProps, ILookupFieldValue } from './fields';

// R2 renderers (record-header-and-notepad-r2 FR-06 / FR-07 / FR-08, tasks 010-012).
export { BooleanField } from './fields';
export type { IBooleanFieldProps } from './fields';

export { DateField } from './fields';
export type { IDateFieldProps } from './fields';

export { NumberField } from './fields';
export type { INumberFieldProps, NumberFieldKind } from './fields';

// Config resolver (record-header-and-notepad-r2 FR-02 / FR-04 / FR-05, task 031).
//
// `resolveHeaderConfig` is a PURE two-tier resolver (no React, no I/O, no Xrm)
// that turns the control's raw `layoutJson` manifest string + form metadata
// into a fully-resolved layout. It is exported here rather than from `types/`
// because it is header-renderer machinery, not schema. A repo-wide grep
// confirmed none of these five symbols collides with an existing export, so
// all re-export un-aliased (`src/components/index.ts` does `export * from
// './RecordHeader'`, so they surface at the top-level barrel too).
export { resolveHeaderConfig, extractConfiguredAttributeNames } from './configResolution';
export type {
  ResolvedHeaderConfig,
  ResolvedHeaderField,
  HeaderFormMetadata,
  HeaderAttributeMetadata,
  HeaderAttributeRequiredLevel,
} from './configResolution';

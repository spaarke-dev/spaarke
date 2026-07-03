/**
 * RecordHeader barrel — record-header-and-notepad-r1 (FR-02 / FR-03).
 *
 * Exports the outer card chrome (`RecordHeaderShell`), the CSS-grid
 * layout primitive (`FieldGrid`), and their public prop types. Field
 * renderers (`TextField`, `LookupField`, `OptionSetField`,
 * `TextareaField`) are added by tasks 005–008 in this project.
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
export { TextField } from './fields';
export type { ITextFieldProps } from './fields';

export { OptionSetField } from './fields';
export type { IOptionSetFieldProps } from './fields';

export { TextareaField } from './fields';
export type { ITextareaFieldProps } from './fields';

export { LookupField as RecordHeaderLookupField } from './fields';
export type {
  ILookupFieldProps as IRecordHeaderLookupFieldProps,
  ILookupFieldValue,
} from './fields';

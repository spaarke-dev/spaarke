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

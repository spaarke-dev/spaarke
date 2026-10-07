/**
 * PageChrome Components
 *
 * Components for Custom Page chrome/layout to achieve OOB Power Apps parity.
 */

// CommandBar DELETED (C-19 residual, #1113, 2026-10-03): zero consumers outside its own test
// across src/solutions, src/client/pcf and every shared package (incl. lazy/dynamic imports).
// The live command bars are DataGrid's (exported as DataGridCommandBar) and
// SemanticSearchControl's own components/CommandBar.tsx.

export { ViewToolbar } from './ViewToolbar';
export type { IViewToolbarProps } from './ViewToolbar';

/**
 * Side-pane infrastructure for the Spaarke DataGrid framework.
 *
 * Three primitives:
 *  - {@link SidePaneFilterChannel} — cross-iframe transport (`sendSidePaneFilter`
 *    + `subscribeSidePaneFilter`)
 *  - {@link useSidePaneFilter} — React hook for hosts wiring a side pane
 *    into `<DataGrid hostFilters />`
 *  - {@link DataGridSidePaneOrchestrator} — pane lifecycle (register / open /
 *    close / mutual exclusivity / visibility-driven re-registration)
 *
 * See `docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md` §5 for the end-to-end
 * pattern.
 */

export {
  sendSidePaneFilter,
  subscribeSidePaneFilter,
} from './SidePaneFilterChannel';
export type { SidePaneFilterMessage } from './SidePaneFilterChannel';

export { useSidePaneFilter } from './useSidePaneFilter';
export type { SidePaneFilterTranslator } from './useSidePaneFilter';

export { DataGridSidePaneOrchestrator } from './DataGridSidePaneOrchestrator';
export type { SidePaneSpec } from './DataGridSidePaneOrchestrator';

/**
 * DataGridExternalHost — the external-SPA rule of the shared `<DataGrid>` (unified-access-control-r2 task 157,
 * owner round 4 item 7).
 *
 * Inside the external SPA (outside counsel over CIAM, and the Teams tab over workforce SSO) a grid must offer
 * NO view picker and must never fetch the entity's saved-query list. The picker lists the entity's INTERNAL
 * MDA views, and the BFF's external column allow-lists (ExternalAccessModule.cs) admit only each grid's own
 * configured columns, so a sibling view either errors or creates pressure to widen those lists. The rule lives
 * HERE, inside the shared grid, so it holds by construction: no prop, wrapper, clone, spread or import path
 * that reaches the grid can turn the picker back on. `DataGrid` reads {@link useDataGridExternalHost} and, when
 * it is true, ignores `showViewSelector`, `externalViews` and any picked view, and never calls
 * `retrieveSavedQueriesForEntity`.
 *
 * Two independent switches, either one is enough:
 *   1. {@link DataGridExternalHostProvider} — a React context the external SPA's root component mounts above
 *      every route (`src/client/external-spa/src/App.tsx`).
 *   2. A build-time constant, `__SPAARKE_DATAGRID_EXTERNAL_HOST__`, that the external SPA's `vite.config.ts`
 *      defines as `true`. Vite replaces it in every module it bundles, this one included, so every copy of the
 *      grid in that bundle is external whatever tree, root or module instance renders it.
 *
 * Both default to OFF. No other host mounts the provider or defines the constant, so no other host changes
 * behaviour: internal surfaces keep the picker.
 *
 * ExternalSpaGridViewSelectorGuardTests (tests/Spaarke.ArchTests) asserts the provider mount and the define.
 */
import * as React from 'react';

/**
 * Defined as `true` by the external SPA's Vite build (`define`). In every other host it is never defined, so
 * the `typeof` guard below reads it as off without a ReferenceError.
 */
declare const __SPAARKE_DATAGRID_EXTERNAL_HOST__: boolean | undefined;

const DataGridExternalHostContext = React.createContext<boolean>(false);

/** True when this bundle was built by the external SPA (its `vite.config.ts` defines the constant). */
export function isDataGridExternalHostBuild(): boolean {
  return typeof __SPAARKE_DATAGRID_EXTERNAL_HOST__ !== 'undefined' && __SPAARKE_DATAGRID_EXTERNAL_HOST__ === true;
}

/** Props for {@link DataGridExternalHostProvider}. */
export interface DataGridExternalHostProviderProps {
  children?: React.ReactNode;
}

/**
 * Marks everything below it as the external SPA: every `<DataGrid>` in the subtree renders with no view picker
 * and never requests the saved-query list. Mount it once, at the external SPA's root, above every route.
 */
export const DataGridExternalHostProvider: React.FC<DataGridExternalHostProviderProps> = ({ children }) => (
  <DataGridExternalHostContext.Provider value={true}>{children}</DataGridExternalHostContext.Provider>
);

/** True when the calling grid renders inside the external SPA (the provider above it, or the external build). */
export function useDataGridExternalHost(): boolean {
  const insideProvider = React.useContext(DataGridExternalHostContext);
  return insideProvider || isDataGridExternalHostBuild();
}

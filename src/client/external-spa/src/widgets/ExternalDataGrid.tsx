/**
 * ExternalDataGrid — the ONLY way the external SPA mounts the shared Spaarke grid
 * (unified-access-control-r2 task 157, owner round 4 item 7).
 *
 * The picker rule itself lives in the shared grid (DataGridExternalHost.tsx). In this SPA the app root mounts
 * DataGridExternalHostProvider and the Vite build defines __SPAARKE_DATAGRID_EXTERNAL_HOST__, so the grid shows
 * no view picker and never requests the entity's saved-query list, whatever props reach it. Neither a call of
 * this wrapper as a plain function nor a clone of the element it returns can turn the picker on. With the picker
 * on, the grid would offer the entity's internal MDA views, whose columns the BFF's external allow-lists
 * (ExternalAccessModule.cs) do not admit.
 *
 * This wrapper is defence in depth: it also passes `showViewSelector={false}` AFTER the caller's props, and its
 * props type omits the switch. ExternalSpaGridViewSelectorGuardTests pins this file's text and refuses every other
 * import of the shared grid under src/client/external-spa/src, so a change here is a change to that guard.
 */
import * as React from 'react';
import { DataGrid, type DataGridProps } from '@spaarke/ui-components/components/DataGrid/DataGrid';

/** The shared grid's props without the view picker switch: an external grid never shows the picker. */
export type ExternalDataGridProps = Omit<DataGridProps, 'showViewSelector'>;

export const ExternalDataGrid: React.FC<ExternalDataGridProps> = props => (
  <DataGrid {...props} showViewSelector={false} />
);

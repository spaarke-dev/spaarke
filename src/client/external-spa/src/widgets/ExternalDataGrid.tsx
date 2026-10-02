/**
 * ExternalDataGrid — the ONLY way the external SPA mounts the shared Spaarke grid
 * (unified-access-control-r2 task 157, owner round 4 item 7).
 *
 * It switches the view picker off at RUNTIME: `showViewSelector={false}` comes AFTER the caller's props,
 * so no caller prop, spread or cloned element can turn the picker back on. With the picker on, the grid
 * would offer the entity's internal MDA views, whose columns the BFF's external allow-lists
 * (ExternalAccessModule.cs) do not admit.
 *
 * ExternalSpaGridViewSelectorGuardTests refuses every other import of the shared grid under
 * src/client/external-spa/src and pins this file's text, so a change here is a change to that guard.
 */
import * as React from 'react';
import { DataGrid, type DataGridProps } from '@spaarke/ui-components/components/DataGrid/DataGrid';

/** The shared grid's props without the view picker switch: an external grid never shows the picker. */
export type ExternalDataGridProps = Omit<DataGridProps, 'showViewSelector'>;

export const ExternalDataGrid: React.FC<ExternalDataGridProps> = props => (
  <DataGrid {...props} showViewSelector={false} />
);

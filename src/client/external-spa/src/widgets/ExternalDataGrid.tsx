/**
 * ExternalDataGrid — the ONLY way the external SPA mounts the shared Spaarke grid
 * (unified-access-control-r2 task 157, owner round 4 item 7).
 *
 * It switches the view picker off at RUNTIME for every JSX mount: `showViewSelector={false}` comes AFTER
 * the caller's props, so no caller prop or spread, and no clone or re-creation of an <ExternalDataGrid>
 * element, can turn the picker back on. With the picker on, the grid would offer the entity's internal MDA
 * views, whose columns the BFF's external allow-lists (ExternalAccessModule.cs) do not admit.
 *
 * It does NOT protect the element it RETURNS. Called as a plain function, it hands back the inner grid
 * element, which can be cloned, or whose `.type` can be mounted, with the picker on. So
 * ExternalSpaGridViewSelectorGuardTests refuses every other import of the shared grid under
 * src/client/external-spa/src, refuses any use of ExternalDataGrid there except as a JSX tag, and pins this
 * file's text, so a change here is a change to that guard. Walking an element tree by hand to reach the
 * inner element is a documented residual of that guard.
 */
import * as React from 'react';
import { DataGrid, type DataGridProps } from '@spaarke/ui-components/components/DataGrid/DataGrid';

/** The shared grid's props without the view picker switch: an external grid never shows the picker. */
export type ExternalDataGridProps = Omit<DataGridProps, 'showViewSelector'>;

export const ExternalDataGrid: React.FC<ExternalDataGridProps> = props => (
  <DataGrid {...props} showViewSelector={false} />
);

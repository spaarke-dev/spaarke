/**
 * GridCustomizer - builds the PAOneGridCustomizer that SpaarkeGridCustomizer hands the grid.
 *
 *  - Root-column lock (unified-access-control-r2 task 168, owner round 25 item 8): every data type
 *    gets the editor override that cancels editing of the four core-ancestor root columns, and the
 *    renderer override that shows them read-only.
 *  - Regarding links (unchanged intent from v1.0.0): Text and Lookup regarding name / id cells
 *    render as a link to the parent record when the row names it; otherwise the default renderer.
 *
 * Kept out of index.ts because a PCF entry module may export only the control class (pcf-1023).
 */

import * as React from 'react';
import {
  ALL_COLUMN_DATA_TYPES,
  CellEditorOverrides,
  CellRendererOverrides,
  CellRendererProps,
  ColumnDataType,
  GetRendererParams,
  PAOneGridCustomizer,
} from '../types/PAGridCustomizer';
import { renderRegardingLink } from './RegardingLinkRenderer';
import { columnOf, markRootCellReadOnly, rootColumnEditorOverride } from './RootColumnLock';

export const CUSTOMIZER_VERSION = '1.1.0';

type CellRenderer = (props: CellRendererProps, params: GetRendererParams) => React.ReactElement | null;

/**
 * Registry of cell renderers by column logical name (lower case).
 * Extensible: add new renderers here for additional customizations.
 */
const cellRendererRegistry: Record<string, CellRenderer> = {
  // Regarding Record columns - renders clickable links to parent records
  sprk_regardingrecordname: renderRegardingLink,
  sprk_regardingrecordid: renderRegardingLink,
};

/** Data types the regarding-link registry applies to (unchanged from v1.0.0). */
const LINK_RENDERER_TYPES: readonly ColumnDataType[] = ['Text', 'Lookup'];

/**
 * Pattern-based renderer lookup for columns matching naming conventions.
 */
export function getRendererForColumn(columnName: string): CellRenderer | null {
  const lowerName = columnName.toLowerCase();
  if (cellRendererRegistry[lowerName]) {
    return cellRendererRegistry[lowerName];
  }
  if (lowerName.includes('regarding') && (lowerName.includes('name') || lowerName.includes('id'))) {
    return renderRegardingLink;
  }
  return null;
}

/**
 * Builds the customizer the grid receives.
 */
export function createGridCustomizer(): PAOneGridCustomizer {
  const cellRendererOverrides: CellRendererOverrides = {};
  const cellEditorOverrides: CellEditorOverrides = {};
  for (const dataType of ALL_COLUMN_DATA_TYPES) {
    const linkable = LINK_RENDERER_TYPES.indexOf(dataType) >= 0;
    cellRendererOverrides[dataType] = (props, params) => {
      if (markRootCellReadOnly(props, params)) {
        return null; // the default renderer, now read-only
      }
      if (!linkable) {
        return null;
      }
      const column = columnOf(params);
      const renderer = column ? getRendererForColumn(column.name || '') : null;
      return renderer ? renderer(props, params) : null;
    };
    cellEditorOverrides[dataType] = rootColumnEditorOverride;
  }
  return { cellRendererOverrides, cellEditorOverrides };
}

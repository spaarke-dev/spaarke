/**
 * GridCustomizer - builds the PAOneGridCustomizer that SpaarkeGridCustomizer hands the grid.
 *
 *  - Filing-column lock (unified-access-control-r2 task 168; owner round 25 item 8, widened by owner
 *    round 38): every data type gets the editor override that cancels editing of a filing column
 *    (the four core-ancestor roots, the ADR-024 pair, the non-root sprk_regarding* lookups; the ONE
 *    list config/regarding-filing-columns.json) and the renderer override that shows it read-only.
 *  - Regarding links (unchanged intent from v1.0.0): Text and Lookup regarding name / id cells
 *    render as a link to the parent record when the row names it; otherwise the default renderer.
 *    A pair name / id cell is a filing column too: it keeps its link and is shown read-only.
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
import { columnOf, filingColumnEditorOverride, isLockedRootColumn, markFilingCellReadOnly } from './RootColumnLock';

export const CUSTOMIZER_VERSION = '1.1.1';

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
      // A filing column is shown read-only first, whatever renderer then draws it.
      markFilingCellReadOnly(props, params, dataType);
      const column = columnOf(params);
      if (!linkable || !column || isLockedRootColumn(column.name)) {
        return null; // the default renderer (read-only for a filing column); a root never gets a link
      }
      const renderer = getRendererForColumn(column.name || '');
      return renderer ? renderer(props, params) : null;
    };
    cellEditorOverrides[dataType] = filingColumnEditorOverride(dataType);
  }
  return { cellRendererOverrides, cellEditorOverrides };
}

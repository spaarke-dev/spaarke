/**
 * Type definitions for the Power Apps grid control customizer contract.
 *
 * Source: Microsoft Learn, "Customize the editable grid control" and the PowerApps-Samples
 * `PowerAppsGridCustomizerControl` sample (types.ts). The grid hands the customizer control an
 * `EventName`; the control answers by firing that event with a {@link PAOneGridCustomizer}, whose
 * per-data-type override functions receive `(props, rendererParams)`.
 *
 * v1.1.0 (unified-access-control-r2 task 168 f1): replaced the earlier hand-written shape
 * (`getRendererOverrides()` / `GetRendererParams.columnInfo`), which the grid never called, with
 * the documented contract so the overrides actually run.
 */

import * as React from 'react';

/** Dataverse column data types the grid keys its overrides by. */
export type ColumnDataType =
  | 'Text'
  | 'Email'
  | 'Phone'
  | 'Ticker'
  | 'URL'
  | 'TextArea'
  | 'Lookup'
  | 'Customer'
  | 'Owner'
  | 'MultiSelectPicklist'
  | 'OptionSet'
  | 'TwoOptions'
  | 'Duration'
  | 'Language'
  | 'Multiple'
  | 'TimeZone'
  | 'Integer'
  | 'Currency'
  | 'Decimal'
  | 'FloatingPoint'
  | 'AutoNumber'
  | 'DateOnly'
  | 'DateAndTime'
  | 'Image'
  | 'File'
  | 'Persona'
  | 'RichText'
  | 'UniqueIdentifier';

/** Every data type, so an override can be registered for all of them. */
export const ALL_COLUMN_DATA_TYPES: readonly ColumnDataType[] = [
  'Text',
  'Email',
  'Phone',
  'Ticker',
  'URL',
  'TextArea',
  'Lookup',
  'Customer',
  'Owner',
  'MultiSelectPicklist',
  'OptionSet',
  'TwoOptions',
  'Duration',
  'Language',
  'Multiple',
  'TimeZone',
  'Integer',
  'Currency',
  'Decimal',
  'FloatingPoint',
  'AutoNumber',
  'DateOnly',
  'DateAndTime',
  'Image',
  'File',
  'Persona',
  'RichText',
  'UniqueIdentifier',
];

/** A grid column as the grid describes it. */
export interface ColumnDefinition {
  /** Logical name of the column (for example `sprk_regardingmatter`). */
  name: string;
  displayName?: string;
  dataType?: ColumnDataType | string;
  width?: number;
  /** Whether the grid lets this column be edited. */
  editable?: boolean;
  isRequired?: boolean;
  isPrimary?: boolean;
}

/** One row of grid data, keyed by column name. */
export type RowData = Record<string, unknown>;

/** Second argument of every renderer override. */
export interface GetRendererParams {
  colDefs: ColumnDefinition[];
  columnIndex: number;
  rowData?: RowData;
  cellErrorMessage?: string;
  isRightToLeft?: boolean;
}

/** Second argument of every editor override. */
export interface GetEditorParams extends GetRendererParams {
  onCellValueChange: (newValue: unknown) => void;
  /** Ends editing; `cancel === true` discards the edit. */
  stopEditing: (cancel?: boolean) => void;
}

/** First argument of a renderer override: the default renderer's props. */
export interface CellRendererProps {
  value: unknown;
  formattedValue?: string;
  prefix?: string;
  suffix?: string;
  onCellClicked?: (event?: React.MouseEvent<HTMLElement, MouseEvent> | MouseEvent) => void;
  startEditing?: (editorInitValue?: unknown) => void;
  rowHeight?: number;
  isRTL?: boolean;
  /** Whether the cell shows as editable (the default renderer's edit affordance). */
  columnEditable?: boolean;
  columnDataType?: ColumnDataType;
  isRequired?: boolean;
}

/** First argument of an editor override: the default editor's props. */
export interface CellEditorProps {
  value: unknown;
  onChange: (newValue: unknown) => void;
  rowHeight?: number;
  isRTL?: boolean;
  isRequired?: boolean;
  columnDataType?: ColumnDataType;
}

/** A renderer override: return an element, or null / undefined to keep the default renderer. */
export type CellRendererOverride = (
  props: CellRendererProps,
  rendererParams: GetRendererParams
) => React.ReactElement | null | undefined;

/** An editor override: return an element, or null / undefined to keep the default editor. */
export type CellEditorOverride = (
  defaultProps: CellEditorProps,
  rendererParams: GetEditorParams
) => React.ReactElement | null | undefined;

export type CellRendererOverrides = Partial<Record<ColumnDataType, CellRendererOverride>>;
export type CellEditorOverrides = Partial<Record<ColumnDataType, CellEditorOverride>>;

/** What the customizer control hands the grid through `context.factory.fireEvent(EventName, …)`. */
export interface PAOneGridCustomizer {
  cellRendererOverrides?: CellRendererOverrides;
  cellEditorOverrides?: CellEditorOverrides;
}

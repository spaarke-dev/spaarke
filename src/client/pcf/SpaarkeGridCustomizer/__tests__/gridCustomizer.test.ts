/**
 * SpaarkeGridCustomizer v1.1.1 — the filing-column lock (unified-access-control-r2 task 168: owner
 * round 25 item 8, widened by owner round 38) and the control's customizer registration.
 *
 * Drives the REAL objects the Power Apps grid receives: the customizer built by
 * createGridCustomizer() and the SpaarkeGridCustomizer control's init (which fires EventName with it).
 * The column set is read from the ONE shared list, config/regarding-filing-columns.json, which the
 * two form scripts read too.
 */

import * as React from 'react';
import filingColumns from '../../../../../config/regarding-filing-columns.json';
import { SpaarkeGridCustomizer } from '../index';
import { createGridCustomizer, CUSTOMIZER_VERSION } from '../customizers/GridCustomizer';
import {
  FILING_LOOKUP_PREFIX,
  FILING_LOOKUP_TYPES,
  LOCKED_ROOT_COLUMNS,
  PAIR_COLUMNS,
} from '../customizers/RootColumnLock';
import {
  ALL_COLUMN_DATA_TYPES,
  CellEditorProps,
  CellRendererProps,
  ColumnDataType,
  ColumnDefinition,
  GetEditorParams,
  GetRendererParams,
  PAOneGridCustomizer,
} from '../types/PAGridCustomizer';

/** Non-root sprk_regarding* lookups of sprk_event / sprk_analysis (live metadata, task 168 note §12.2). */
const NON_ROOT_REGARDING_LOOKUPS = ['sprk_regardingcommunication', 'sprk_regardingaccount', 'sprk_regardingevent'];

function editorParams(
  name: string,
  extra: Partial<ColumnDefinition> = {}
): GetEditorParams & { stopEditing: jest.Mock } {
  return {
    colDefs: [
      { name: 'sprk_eventname', editable: true },
      { name, editable: true, ...extra },
    ],
    columnIndex: 1,
    rowData: {},
    onCellValueChange: jest.fn(),
    stopEditing: jest.fn(),
  };
}

function rendererParams(
  name: string,
  rowData: Record<string, unknown> = {},
  extra: Partial<ColumnDefinition> = {}
): GetRendererParams {
  return {
    colDefs: [
      { name: 'sprk_eventname', editable: true },
      { name, editable: true, ...extra },
    ],
    columnIndex: 1,
    rowData,
  };
}

const editorProps = (columnDataType?: ColumnDataType): CellEditorProps => ({
  value: null,
  onChange: jest.fn(),
  columnDataType,
});
const rendererProps = (value: unknown = 'x', columnDataType?: ColumnDataType): CellRendererProps => ({
  value,
  columnEditable: true,
  columnDataType,
});

function edit(
  customizer: PAOneGridCustomizer,
  dataType: ColumnDataType,
  params: GetEditorParams,
  props: CellEditorProps = editorProps()
) {
  const override = customizer.cellEditorOverrides?.[dataType];
  if (!override) {
    throw new Error(`no editor override for ${dataType}`);
  }
  return override(props, params);
}

describe('the ONE shared list (owner round 38): config/regarding-filing-columns.json', () => {
  it('the lock reads its columns from the shared file, not from a second copy', () => {
    const lower = (values: string[]) => values.map(v => v.toLowerCase());
    expect(LOCKED_ROOT_COLUMNS).toEqual(lower(filingColumns.rootColumns));
    expect(PAIR_COLUMNS).toEqual(lower([filingColumns.recordTypeColumn, ...filingColumns.pairTextColumns]));
    expect(FILING_LOOKUP_PREFIX).toBe(filingColumns.lookupPrefix.toLowerCase());
    expect(FILING_LOOKUP_TYPES).toEqual(filingColumns.lookupTypes);
  });

  it('names the four roots, the five pair columns, the sprk_regarding prefix and the Lookup type', () => {
    expect([...LOCKED_ROOT_COLUMNS].sort()).toEqual([
      'sprk_regardingmatter',
      'sprk_regardingproject',
      'sprk_regardingservicerequest',
      'sprk_regardingworkassignment',
    ]);
    expect([...PAIR_COLUMNS].sort()).toEqual([
      'sprk_regardingrecordid',
      'sprk_regardingrecordname',
      'sprk_regardingrecordnumber',
      'sprk_regardingrecordtype',
      'sprk_regardingrecordurl',
    ]);
    expect(FILING_LOOKUP_PREFIX).toBe('sprk_regarding');
    expect(FILING_LOOKUP_TYPES).toEqual(['Lookup']);
  });
});

describe('filing-column lock: editor override', () => {
  it.each(LOCKED_ROOT_COLUMNS.map(c => [c]))(
    'cancels editing of the root %s (stopEditing(true)) and marks the column not editable',
    column => {
      const params = editorParams(column);
      const result = edit(createGridCustomizer(), 'Lookup', params);
      expect(params.stopEditing).toHaveBeenCalledTimes(1);
      expect(params.stopEditing).toHaveBeenCalledWith(true);
      expect(params.colDefs[1].editable).toBe(false);
      expect(result).toBeNull();
    }
  );

  it.each(PAIR_COLUMNS.map(c => [c]))('cancels editing of the pair column %s, whatever its data type', column => {
    const customizer = createGridCustomizer();
    for (const dataType of ALL_COLUMN_DATA_TYPES) {
      const params = editorParams(column);
      edit(customizer, dataType, params);
      expect({ dataType, calls: params.stopEditing.mock.calls, editable: params.colDefs[1].editable }).toEqual({
        dataType,
        calls: [[true]],
        editable: false,
      });
    }
  });

  it.each(NON_ROOT_REGARDING_LOOKUPS.map(c => [c]))(
    'cancels editing of the non-root regarding lookup %s (dispatched as a Lookup)',
    column => {
      const params = editorParams(column);
      edit(createGridCustomizer(), 'Lookup', params);
      expect(params.stopEditing).toHaveBeenCalledWith(true);
      expect(params.colDefs[1].editable).toBe(false);
    }
  );

  it('recognises a regarding lookup from the column definition dataType', () => {
    const params = editorParams('sprk_regardingaccount', { dataType: 'Lookup' });
    edit(createGridCustomizer(), 'Text', params);
    expect(params.stopEditing).toHaveBeenCalledWith(true);
  });

  it('recognises a regarding lookup from the cell props columnDataType', () => {
    const params = editorParams('sprk_regardingaccount');
    edit(createGridCustomizer(), 'Text', params, editorProps('Lookup'));
    expect(params.stopEditing).toHaveBeenCalledWith(true);
  });

  it.each([['sprk_RegardingMatter'], ['SPRK_REGARDINGRECORDID'], ['sprk_RegardingAccount']])(
    'matches %s case-insensitively and as a whole value',
    column => {
      const params = editorParams(column);
      edit(createGridCustomizer(), 'Lookup', params);
      expect(params.stopEditing).toHaveBeenCalledWith(true);
    }
  );

  it.each([
    ['sprk_regardingmatterstatus', 'OptionSet'], // starts with a root name, not a lookup: NOT a filing column
    ['sprk_regardingsummary', 'Text'], // sprk_regarding* but not a lookup and not in the pair
    ['sprk_regardingrecordidx', 'Text'], // a near-miss of a pair column
    ['x_sprk_regardingmatter', 'Lookup'], // a lookup that ends with a root name: not the prefix
    ['sprk_matter', 'Lookup'], // a lookup without the sprk_regarding prefix
    ['sprk_eventname', 'Text'],
  ] as [string, ColumnDataType][])(
    'NEGATIVE: leaves %s (%s) editable (no stopEditing, editable unchanged)',
    (column, dataType) => {
      const params = editorParams(column, { dataType });
      const result = edit(createGridCustomizer(), dataType, params, editorProps(dataType));
      expect(params.stopEditing).not.toHaveBeenCalled();
      expect(params.colDefs[1].editable).toBe(true);
      expect(result).toBeNull();
    }
  );

  it('every data type the grid can key an editor by cancels a root edit', () => {
    const customizer = createGridCustomizer();
    for (const dataType of ALL_COLUMN_DATA_TYPES) {
      const params = editorParams('sprk_regardingproject');
      edit(customizer, dataType, params);
      expect({ dataType, calls: params.stopEditing.mock.calls }).toEqual({ dataType, calls: [[true]] });
    }
  });

  it('a call with no column definitions or an out-of-range index is a no-op', () => {
    const customizer = createGridCustomizer();
    const noDefs = { ...editorParams('sprk_regardingmatter'), colDefs: undefined as unknown as ColumnDefinition[] };
    expect(() => edit(customizer, 'Lookup', noDefs)).not.toThrow();
    expect(noDefs.stopEditing).not.toHaveBeenCalled();
    const outOfRange = { ...editorParams('sprk_regardingmatter'), columnIndex: 9 };
    edit(customizer, 'Lookup', outOfRange);
    expect(outOfRange.stopEditing).not.toHaveBeenCalled();
  });
});

describe('filing-column lock: renderer override', () => {
  it.each(LOCKED_ROOT_COLUMNS.map(c => [c]))(
    'shows the root %s read-only (columnEditable=false) and keeps the default renderer',
    column => {
      const props = rendererProps({ id: '1', name: 'Matter A' });
      const params = rendererParams(column);
      const result = createGridCustomizer().cellRendererOverrides?.Lookup?.(props, params);
      expect(result).toBeNull();
      expect(props.columnEditable).toBe(false);
      expect(params.colDefs[1].editable).toBe(false);
    }
  );

  it('every data type marks a root cell read-only', () => {
    const customizer = createGridCustomizer();
    for (const dataType of ALL_COLUMN_DATA_TYPES) {
      const props = rendererProps();
      customizer.cellRendererOverrides?.[dataType]?.(props, rendererParams('sprk_regardingservicerequest'));
      expect({ dataType, editable: props.columnEditable }).toEqual({ dataType, editable: false });
    }
  });

  it.each(PAIR_COLUMNS.map(c => [c]))('shows the pair column %s read-only under every data type', column => {
    const customizer = createGridCustomizer();
    for (const dataType of ALL_COLUMN_DATA_TYPES) {
      const props = rendererProps();
      const params = rendererParams(column);
      customizer.cellRendererOverrides?.[dataType]?.(props, params);
      expect({ dataType, editable: props.columnEditable, column: params.colDefs[1].editable }).toEqual({
        dataType,
        editable: false,
        column: false,
      });
    }
  });

  it.each(NON_ROOT_REGARDING_LOOKUPS.map(c => [c]))('shows the non-root regarding lookup %s read-only', column => {
    const props = rendererProps({ id: '1', name: 'Comm A' });
    const params = rendererParams(column);
    createGridCustomizer().cellRendererOverrides?.Lookup?.(props, params);
    expect(props.columnEditable).toBe(false);
    expect(params.colDefs[1].editable).toBe(false);
  });

  it.each([
    ['sprk_regardingmatterstatus', 'OptionSet'],
    ['sprk_regardingsummary', 'Text'],
    ['x_sprk_regardingmatter', 'Lookup'],
  ] as [string, ColumnDataType][])('NEGATIVE: %s (%s) keeps its edit affordance', (column, dataType) => {
    const props = rendererProps('x', dataType);
    const params = rendererParams(column, {}, { dataType });
    createGridCustomizer().cellRendererOverrides?.[dataType]?.(props, params);
    expect(props.columnEditable).toBe(true);
    expect(params.colDefs[1].editable).toBe(true);
  });
});

describe('regarding links (existing registry, ported to the documented signature)', () => {
  it('renders a link for a regarding name cell whose row names its target, and shows it read-only', () => {
    const props = rendererProps('Matter A');
    const params = rendererParams('sprk_regardingrecordname', {
      sprk_regardingrecordtype: 1,
      sprk_regardingrecordid: 'a1b2',
    });
    const element = createGridCustomizer().cellRendererOverrides?.Text?.(props, params);
    expect(React.isValidElement(element)).toBe(true);
    expect((element as React.ReactElement<{ entityName: string; recordId: string }>).props).toMatchObject({
      entityName: 'sprk_matter',
      recordId: 'a1b2',
    });
    expect(props.columnEditable).toBe(false);
    expect(params.colDefs[1].editable).toBe(false);
  });

  it('keeps the default renderer (read-only) when the row does not name its target', () => {
    const props = rendererProps('Matter A');
    const element = createGridCustomizer().cellRendererOverrides?.Text?.(
      props,
      rendererParams('sprk_regardingrecordname', {})
    );
    expect(element).toBeNull();
    expect(props.columnEditable).toBe(false);
  });

  it('a root column never gets the link renderer', () => {
    const props = rendererProps('Matter A');
    const element = createGridCustomizer().cellRendererOverrides?.Lookup?.(
      props,
      rendererParams('sprk_regardingmatter', { sprk_regardingrecordtype: 1, sprk_regardingrecordid: 'a1b2' })
    );
    expect(element).toBeNull();
    expect(props.columnEditable).toBe(false);
  });
});

describe('SpaarkeGridCustomizer control', () => {
  function context(
    eventName: string | null,
    fireEvent?: jest.Mock
  ): ComponentFramework.Context<{ EventName: ComponentFramework.PropertyTypes.StringProperty }> {
    return {
      parameters: { EventName: { raw: eventName } },
      factory: fireEvent ? { fireEvent } : {},
    } as unknown as ComponentFramework.Context<{ EventName: ComponentFramework.PropertyTypes.StringProperty }>;
  }

  beforeEach(() => {
    jest.spyOn(console, 'log').mockImplementation(() => undefined);
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    jest.spyOn(console, 'error').mockImplementation(() => undefined);
  });
  afterEach(() => jest.restoreAllMocks());

  it('init fires EventName with a customizer that cancels a root and a pair edit', () => {
    const fireEvent = jest.fn();
    new SpaarkeGridCustomizer().init(context('grid-evt-1', fireEvent), jest.fn(), {});
    expect(fireEvent).toHaveBeenCalledTimes(1);
    const [name, customizer] = fireEvent.mock.calls[0] as [string, PAOneGridCustomizer];
    expect(name).toBe('grid-evt-1');
    const params = editorParams('sprk_regardingworkassignment');
    customizer.cellEditorOverrides?.Lookup?.(editorProps(), params);
    expect(params.stopEditing).toHaveBeenCalledWith(true);
    const pair = editorParams('sprk_regardingrecordid');
    customizer.cellEditorOverrides?.Text?.(editorProps(), pair);
    expect(pair.stopEditing).toHaveBeenCalledWith(true);
    const props = rendererProps();
    customizer.cellRendererOverrides?.Lookup?.(props, rendererParams('sprk_regardingworkassignment'));
    expect(props.columnEditable).toBe(false);
  });

  it('NEGATIVE: without an EventName nothing is fired', () => {
    const fireEvent = jest.fn();
    new SpaarkeGridCustomizer().init(context(null, fireEvent), jest.fn(), {});
    expect(fireEvent).not.toHaveBeenCalled();
  });

  it('updateView renders nothing of its own (a Fragment)', () => {
    const element = new SpaarkeGridCustomizer().updateView(context('e'));
    expect(element.type).toBe(React.Fragment);
  });

  it('reports version 1.1.1', () => {
    expect(CUSTOMIZER_VERSION).toBe('1.1.1');
  });
});

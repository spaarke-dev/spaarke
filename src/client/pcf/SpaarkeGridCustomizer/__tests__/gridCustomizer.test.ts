/**
 * SpaarkeGridCustomizer v1.1.0 — the root-column lock (unified-access-control-r2 task 168 f1,
 * owner round 25 item 8) and the control's customizer registration.
 *
 * Drives the REAL objects the Power Apps grid receives: the customizer built by
 * createGridCustomizer() and the SpaarkeGridCustomizer control's init (which fires EventName with it).
 */

import * as React from 'react';
import { SpaarkeGridCustomizer } from '../index';
import { createGridCustomizer, CUSTOMIZER_VERSION } from '../customizers/GridCustomizer';
import { LOCKED_ROOT_COLUMNS } from '../customizers/RootColumnLock';
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

function rendererParams(name: string, rowData: Record<string, unknown> = {}): GetRendererParams {
  return {
    colDefs: [
      { name: 'sprk_eventname', editable: true },
      { name, editable: true },
    ],
    columnIndex: 1,
    rowData,
  };
}

const editorProps = (): CellEditorProps => ({ value: null, onChange: jest.fn() });
const rendererProps = (value: unknown = 'x'): CellRendererProps => ({ value, columnEditable: true });

function edit(customizer: PAOneGridCustomizer, dataType: ColumnDataType, params: GetEditorParams) {
  const override = customizer.cellEditorOverrides?.[dataType];
  if (!override) {
    throw new Error(`no editor override for ${dataType}`);
  }
  return override(editorProps(), params);
}

describe('root-column lock: editor override', () => {
  it.each(LOCKED_ROOT_COLUMNS.map(c => [c]))(
    'cancels editing of %s (stopEditing(true)) and marks the column not editable',
    column => {
      const params = editorParams(column);
      const result = edit(createGridCustomizer(), 'Lookup', params);
      expect(params.stopEditing).toHaveBeenCalledTimes(1);
      expect(params.stopEditing).toHaveBeenCalledWith(true);
      expect(params.colDefs[1].editable).toBe(false);
      expect(result).toBeNull();
    }
  );

  it('matches the column name case-insensitively and as a whole value (sprk_RegardingMatter)', () => {
    const params = editorParams('sprk_RegardingMatter');
    edit(createGridCustomizer(), 'Lookup', params);
    expect(params.stopEditing).toHaveBeenCalledWith(true);
  });

  it.each([
    ['sprk_regardingrecordtype'],
    ['sprk_regardingrecordid'],
    ['sprk_regardingcommunication'],
    ['sprk_regardingmatterstatus'], // starts with a locked name: NOT a match
    ['x_sprk_regardingmatter'], // ends with a locked name: NOT a match
    ['sprk_eventname'],
  ])('NEGATIVE: leaves %s editable (no stopEditing, editable unchanged)', column => {
    const params = editorParams(column);
    const result = edit(createGridCustomizer(), 'Lookup', params);
    expect(params.stopEditing).not.toHaveBeenCalled();
    expect(params.colDefs[1].editable).toBe(true);
    expect(result).toBeNull();
  });

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

describe('root-column lock: renderer override', () => {
  it.each(LOCKED_ROOT_COLUMNS.map(c => [c]))(
    'shows %s read-only (columnEditable=false) and keeps the default renderer',
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

  it('NEGATIVE: another column keeps its edit affordance', () => {
    const props = rendererProps();
    const params = rendererParams('sprk_regardingmatterstatus');
    createGridCustomizer().cellRendererOverrides?.Lookup?.(props, params);
    expect(props.columnEditable).toBe(true);
    expect(params.colDefs[1].editable).toBe(true);
  });
});

describe('regarding links (existing registry, ported to the documented signature)', () => {
  it('renders a link for a regarding name cell whose row names its target', () => {
    const element = createGridCustomizer().cellRendererOverrides?.Text?.(
      rendererProps('Matter A'),
      rendererParams('sprk_regardingrecordname', { sprk_regardingrecordtype: 1, sprk_regardingrecordid: 'a1b2' })
    );
    expect(React.isValidElement(element)).toBe(true);
    expect((element as React.ReactElement<{ entityName: string; recordId: string }>).props).toMatchObject({
      entityName: 'sprk_matter',
      recordId: 'a1b2',
    });
  });

  it('keeps the default renderer when the row does not name its target', () => {
    const element = createGridCustomizer().cellRendererOverrides?.Text?.(
      rendererProps('Matter A'),
      rendererParams('sprk_regardingrecordname', {})
    );
    expect(element).toBeNull();
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

  it('init fires EventName with a customizer whose editor override cancels a root edit', () => {
    const fireEvent = jest.fn();
    new SpaarkeGridCustomizer().init(context('grid-evt-1', fireEvent), jest.fn(), {});
    expect(fireEvent).toHaveBeenCalledTimes(1);
    const [name, customizer] = fireEvent.mock.calls[0] as [string, PAOneGridCustomizer];
    expect(name).toBe('grid-evt-1');
    const params = editorParams('sprk_regardingworkassignment');
    customizer.cellEditorOverrides?.Lookup?.(editorProps(), params);
    expect(params.stopEditing).toHaveBeenCalledWith(true);
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

  it('reports version 1.1.0', () => {
    expect(CUSTOMIZER_VERSION).toBe('1.1.0');
  });
});

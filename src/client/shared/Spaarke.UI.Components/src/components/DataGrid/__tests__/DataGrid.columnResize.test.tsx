/**
 * DataGrid.columnResize.test — column fitting and drag-resize (R4 task 028 fix round, owner E2E 2026-11-23).
 *
 * The defect: dragging a column edge did not resize the column (the drag selected header text instead). Two causes:
 *  1. The grid never measured its scroll container. The measuring effects ran once, at mount, while the grid was still
 *     showing its "Loading grid configuration" placeholder, so the container did not exist yet and the width stayed 0.
 *     The columns kept their configured widths, and Fluent's auto-fit cut them down from the right, to their minimum
 *     widths when the space was short.
 *  2. Fluent's auto-fit keeps the columns inside the container on every resize step: a column grows only by taking
 *     width from columns that are above their minimum, and with all of them at their minimum it undoes the step.
 *
 * jsdom has no layout, so the widths the grid and Fluent measure are mocked: `clientWidth` (the grid's measurement)
 * and `getBoundingClientRect().width` (Fluent's) both return the test's container width. Column widths are read from
 * the header cells' inline style, which is what the browser lays out. The drag is driven through Fluent's own resize
 * handle: mousedown on the handle, mousemove/mouseup on the document, exactly the listeners Fluent attaches.
 * Each case runs on a configuration-only grid and on a grid with the task 023a host seams (rowSource + rowActions).
 *
 * Review fix round (F1-1, F1-2): a dragged width is the user's, kept for the grid's lifetime. Before, Fluent reset it to
 * the grid's fitted width on the next change to the sizing options or the column definitions (a 1px container change, a
 * row selection, a host passing inline callbacks), and the FluentDataGrid was keyed on a 50px container-width bucket, so
 * a real container change remounted it and lost the sort and focus. The host below passes inline callbacks and an inline
 * `parentContext` on every render, as a host that does not memoise them does.
 */
import * as React from 'react';
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ArrowDownload20Regular, Delete20Regular, History20Regular } from '@fluentui/react-icons';
import { DataGrid } from '../DataGrid';
import type { DataGridRowAction } from '../index';
import type {
  IDataverseClient,
  EntityMetadata,
  FetchMultipleResult,
  SavedQueryResult,
} from '../../../services/IDataverseClient';
import type { DataGridConfiguration } from '../../../types/DataGridConfiguration';

const CONFIG_ID = '286be8b4-0000-4000-8000-000000000028';

const FETCH =
  "<fetch><entity name='sprk_doc'><attribute name='sprk_docid'/><attribute name='sprk_name'/>" +
  "<attribute name='sprk_type'/><attribute name='sprk_added'/><attribute name='sprk_summary'/></entity></fetch>";
// The record Documents grid's widths (config/external-record-documents.gridconfiguration.json): 300 / 150 / 140 / 360.
const LAYOUT =
  "<grid name='result' object='1' jump='sprk_name' select='1'><row name='result' id='sprk_docid'>" +
  "<cell name='sprk_name' width='300'/><cell name='sprk_type' width='150'/>" +
  "<cell name='sprk_added' width='140'/><cell name='sprk_summary' width='360'/></row></grid>";

const CONFIG: DataGridConfiguration = {
  _version: '1.0',
  source: { type: 'inline', fetchXml: FETCH, layoutXml: LAYOUT },
  display: { title: 'Documents' },
  behavior: { selectionMode: 'none', enableSorting: true, enableColumnResize: true },
};

const METADATA: EntityMetadata = {
  primaryIdAttribute: 'sprk_docid',
  primaryNameAttribute: 'sprk_name',
  attributes: {
    sprk_docid: { attributeType: 'String', isPrimaryId: true },
    sprk_name: { attributeType: 'String', isPrimaryName: true, displayName: 'Name' },
    sprk_type: { attributeType: 'String', displayName: 'Type' },
    sprk_added: { attributeType: 'String', displayName: 'Added' },
    sprk_summary: { attributeType: 'String', displayName: 'Summary' },
  },
};

const ROWS = [
  { sprk_docid: 'd-1', sprk_name: 'Engagement letter', sprk_type: 'Contract', sprk_added: 'Jan', sprk_summary: 'A' },
  { sprk_docid: 'd-2', sprk_name: 'Statement of work', sprk_type: 'Contract', sprk_added: 'Feb', sprk_summary: 'B' },
];

const ACTIONS: DataGridRowAction[] = [
  { id: 'download', label: 'Download', icon: <ArrowDownload20Regular /> },
  { id: 'versions', label: 'Version history', icon: <History20Regular /> },
  { id: 'delete', label: 'Delete', icon: <Delete20Regular /> },
];

// The same grid with selectable rows (case b).
const SELECTABLE_CONFIG: DataGridConfiguration = {
  ...CONFIG,
  behavior: { ...CONFIG.behavior, selectionMode: 'multi' },
};

function makeClient(config: DataGridConfiguration = CONFIG): IDataverseClient {
  return {
    retrieveRecord: jest.fn(async () => ({ sprk_configjson: JSON.stringify(config) })),
    retrieveSavedQuery: jest.fn(
      async (): Promise<SavedQueryResult> => ({
        entityName: 'sprk_doc',
        fetchXml: FETCH,
        layoutXml: LAYOUT,
        name: 'Docs',
      })
    ),
    retrieveSavedQueriesForEntity: jest.fn(async () => []),
    retrieveEntityMetadata: jest.fn(async () => METADATA),
    retrieveMultipleRecords: jest.fn(
      async (): Promise<FetchMultipleResult> => ({ entities: ROWS.map(r => ({ ...r })), moreRecords: false })
    ),
  } as unknown as IDataverseClient;
}

// ─── Layout mocks ────────────────────────────────────────────────────────────

let containerWidth = 1000;
const observers: Array<{ cb: ResizeObserverCallback; ro: ResizeObserver }> = [];

/** Tells every ResizeObserver the container changed size (the grid's own observer and Fluent's). */
function resizeContainer(width: number): void {
  containerWidth = width;
  act(() => {
    for (const { cb, ro } of observers) cb([], ro);
  });
}

const originalClientWidth = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'clientWidth');
const originalGetBoundingClientRect = Element.prototype.getBoundingClientRect;
const originalResizeObserver = globalThis.ResizeObserver;

beforeEach(() => {
  containerWidth = 1000;
  observers.length = 0;
  Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => containerWidth });
  Element.prototype.getBoundingClientRect = function () {
    return {
      x: 0,
      y: 0,
      top: 0,
      left: 0,
      right: containerWidth,
      bottom: 40,
      width: containerWidth,
      height: 40,
      toJSON() {
        return this;
      },
    } as DOMRect;
  };
  globalThis.ResizeObserver = class {
    private readonly cb: ResizeObserverCallback;
    constructor(cb: ResizeObserverCallback) {
      this.cb = cb;
      observers.push({ cb, ro: this as unknown as ResizeObserver });
    }
    observe(): void {
      /* driven by resizeContainer */
    }
    unobserve(): void {
      /* noop */
    }
    disconnect(): void {
      const i = observers.findIndex(o => o.cb === this.cb);
      if (i >= 0) observers.splice(i, 1);
    }
  } as unknown as typeof ResizeObserver;
});

afterEach(() => {
  if (originalClientWidth) Object.defineProperty(HTMLElement.prototype, 'clientWidth', originalClientWidth);
  Element.prototype.getBoundingClientRect = originalGetBoundingClientRect;
  globalThis.ResizeObserver = originalResizeObserver;
});

// ─── Helpers ─────────────────────────────────────────────────────────────────

type Variant = 'config-only' | 'host seams';

const rowSource = async () => ({ rows: ROWS.map(r => ({ ...r })), hasMore: false });

interface GridHostProps {
  variant: Variant;
  client: IDataverseClient;
  /** Changes on each `rerenderHost()`, so the host re-renders the grid with new callback identities. */
  tick: number;
}

/** A host that passes new inline callbacks and a new inline `parentContext` on every render. */
function GridHost({ variant, client, tick }: GridHostProps) {
  const hostProps =
    variant === 'host seams'
      ? {
          rowSource,
          rowSourceKey: 'k',
          rowActions: ACTIONS,
          onRecordAction: () => undefined,
          truncateWithTooltip: true,
        }
      : {};
  return (
    <FluentProvider theme={webLightTheme}>
      <DataGrid
        configId={CONFIG_ID}
        dataverseClient={client}
        showViewSelector={false}
        onRecordOpen={() => undefined}
        parentContext={{ entityType: 'sprk_matter', id: 'm-1', name: 'Matter', tick: String(tick) }}
        {...hostProps}
      />
    </FluentProvider>
  );
}

function renderGrid(variant: Variant, config: DataGridConfiguration = CONFIG) {
  const client = makeClient(config);
  let tick = 0;
  const result = render(<GridHost variant={variant} client={client} tick={tick} />);
  return {
    ...result,
    /** Re-renders the host: same grid, new inline callback and context identities. */
    rerenderHost(): void {
      tick += 1;
      result.rerender(<GridHost variant={variant} client={client} tick={tick} />);
    },
  };
}

function headerCell(label: string): HTMLElement {
  const cell = screen.getAllByRole('columnheader').find(c => within(c).queryByText(label) !== null);
  if (!cell) throw new Error(`no "${label}" column header`);
  return cell;
}

/** The width the browser lays the column out at (Fluent's inline style on the header cell). */
const widthOf = (label: string): number => parseFloat(headerCell(label).style.width);

const DATA_COLUMNS = ['Name', 'Type', 'Added', 'Summary'];
const widths = (): number[] => DATA_COLUMNS.map(widthOf);

async function flushFrames(): Promise<void> {
  await act(async () => {
    await new Promise(resolve => setTimeout(resolve, 40));
  });
}

/** Drags the right edge of `label`'s header by `dx` px in `steps` mouse moves, like a user. */
async function dragColumnEdge(label: string, dx: number, steps = 4): Promise<void> {
  const handle = headerCell(label).querySelector('.fui-TableResizeHandle');
  if (!handle) throw new Error(`"${label}" has no resize handle`);
  const x0 = 500;
  fireEvent.mouseDown(handle, { clientX: x0, clientY: 10, button: 0 });
  await flushFrames();
  for (let s = 1; s <= steps; s++) {
    fireEvent.mouseMove(document, { clientX: x0 + (dx * s) / steps, clientY: 10 });
    await flushFrames();
  }
  fireEvent.mouseUp(document, { clientX: x0 + dx, clientY: 10 });
  await flushFrames();
}

async function renderLoaded(variant: Variant, config: DataGridConfiguration = CONFIG) {
  const grid = renderGrid(variant, config);
  await screen.findByText('Engagement letter', undefined, { timeout: 10000 });
  await flushFrames();
  return grid;
}

/** Control: the widths a fresh grid shows in a container of `width` px. */
async function freshFit(variant: Variant, width: number): Promise<number[]> {
  containerWidth = width;
  const fresh = await renderLoaded(variant);
  const fit = widths();
  fresh.unmount();
  return fit;
}

const rowOf = (text: string): HTMLElement => {
  const row = screen.getByText(text).closest('[role="row"]');
  if (!row) throw new Error(`no row for "${text}"`);
  return row as HTMLElement;
};

// ─── Tests ───────────────────────────────────────────────────────────────────

describe.each<Variant>(['config-only', 'host seams'])('DataGrid column sizing (%s)', variant => {
  it('fits the columns to the measured container once the configuration has loaded', async () => {
    containerWidth = 1000;
    await renderLoaded(variant);
    // Measured: every column is scaled to the container (proportionally), so Name and Type are both below their
    // configured 300 / 150. Unmeasured (the defect), Name and Type keep 300 / 150 and only the last columns are cut.
    expect(widthOf('Name')).toBeLessThan(300);
    expect(widthOf('Type')).toBeLessThan(150);
  });

  it('widens a column the user drags, even when the columns are squeezed to their minimum widths', async () => {
    containerWidth = 600;
    await renderLoaded(variant);
    const before = widthOf('Type');
    await dragColumnEdge('Type', 100);
    // The drag is honoured (the grid may now scroll sideways): Type follows the pointer, give or take the first step.
    expect(widthOf('Type')).toBeGreaterThanOrEqual(before + 70);
  });

  it('keeps a dragged width once the drag ends, and shrinks a column no further than its minimum', async () => {
    containerWidth = 1000;
    await renderLoaded(variant);
    await dragColumnEdge('Name', 80);
    const dragged = widthOf('Name');
    await flushFrames();
    expect(widthOf('Name')).toBe(dragged);
    // Shrinking works too, down to the column's minimum and no further.
    await dragColumnEdge('Name', -1000);
    expect(widthOf('Name')).toBe(150); // max(80, 300 × 0.5)
  });

  it('(a) keeps a dragged width when the host re-renders with new inline callbacks and context', async () => {
    containerWidth = 1000;
    const grid = await renderLoaded(variant);
    const before = widthOf('Name');
    await dragColumnEdge('Name', 80);
    const dragged = widthOf('Name');
    expect(dragged).toBeGreaterThan(before + 50);
    grid.rerenderHost();
    await flushFrames();
    grid.rerenderHost();
    await flushFrames();
    expect(widthOf('Name')).toBe(dragged);
  });

  it('(b) keeps a dragged width when the row selection changes', async () => {
    containerWidth = 1000;
    await renderLoaded(variant, SELECTABLE_CONFIG);
    const before = widthOf('Name');
    await dragColumnEdge('Name', 80);
    const dragged = widthOf('Name');
    expect(dragged).toBeGreaterThan(before + 50);
    fireEvent.click(within(rowOf('Engagement letter')).getByRole('checkbox'));
    await flushFrames();
    // Control: the selection really changed.
    expect(rowOf('Engagement letter')).toHaveAttribute('aria-selected', 'true');
    expect(widthOf('Name')).toBe(dragged);
  });

  it('(c) keeps a dragged width across container changes, and re-fits the columns the user has not dragged', async () => {
    const fitAt1400 = await freshFit(variant, 1400);

    containerWidth = 1010;
    await renderLoaded(variant);
    const before = widthOf('Type');
    await dragColumnEdge('Type', 80);
    const dragged = widthOf('Type');
    expect(dragged).toBeGreaterThan(before + 50);

    // A few px within the same 50px bucket (e.g. a vertical scrollbar appears after the drag).
    resizeContainer(1002);
    await flushFrames();
    expect(widthOf('Type')).toBe(dragged);
    const summaryAt1002 = widthOf('Summary');

    // A real change across buckets.
    resizeContainer(1400);
    await flushFrames();
    expect(widthOf('Type')).toBe(dragged);
    // The undragged columns take the fresh fit for the new width (the last one may also be stretched to fill).
    expect(widthOf('Name')).toBe(fitAt1400[0]);
    expect(widthOf('Added')).toBe(fitAt1400[2]);
    expect(widthOf('Summary')).toBeGreaterThan(summaryAt1002);
  });

  it('(d) keeps the sort and the grid itself when the container changes width, and still re-fits the columns', async () => {
    const fitAt1400 = await freshFit(variant, 1400);

    containerWidth = 1000;
    await renderLoaded(variant);
    const button = headerCell('Name').querySelector('.fui-TableHeaderCell__button');
    if (!button) throw new Error('"Name" has no sort button');
    fireEvent.click(button);
    await flushFrames();
    const sort = headerCell('Name').getAttribute('aria-sort');
    // Control: the click really sorted.
    expect(sort).toMatch(/ascending|descending/);
    const gridEl = screen.getByRole('grid');
    const nameHeaderEl = headerCell('Name');

    resizeContainer(1400);
    await flushFrames();
    expect(headerCell('Name').getAttribute('aria-sort')).toBe(sort);
    // Not remounted: the same grid and header elements.
    expect(screen.getByRole('grid')).toBe(gridEl);
    expect(headerCell('Name')).toBe(nameHeaderEl);
    // And the columns re-fit to the new width without the remount.
    expect(widths()).toEqual(fitAt1400);
  });
});

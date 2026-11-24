/**
 * DataGrid.persistedSettings.test — the opt-in settings props (spaarke-SPA-external-access-platform-r4 task 032, FR-19,
 * #2489): `initialSettings` (widths + sort to start with), `onSettingsChange` (reports them) and `settingsResetKey`
 * ("Clear all": widths, sort and column filters). The shared grid stores nothing; the host does.
 *
 * Harness: the same jsdom layout mocks and Fluent resize-handle drive as DataGrid.columnResize.test.tsx (jsdom has no
 * layout, so `clientWidth` / `getBoundingClientRect` return the test's container width, and a column's laid-out width is
 * read from its header cell's inline style).
 *
 * Must-not-fire pair: with none of the three props the grid keeps Fluent's own uncontrolled sort and fits the columns to
 * the container, and nothing is reported (there is nothing to report to).
 */
import * as React from 'react';
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DataGrid } from '../DataGrid';
import type { DataGridSettings } from '../index';
import type {
  IDataverseClient,
  EntityMetadata,
  FetchMultipleResult,
  SavedQueryResult,
} from '../../../services/IDataverseClient';
import type { DataGridConfiguration } from '../../../types/DataGridConfiguration';

const CONFIG_ID = '286be8b4-0000-4000-8000-000000000032';

const FETCH =
  "<fetch><entity name='sprk_doc'><attribute name='sprk_docid'/><attribute name='sprk_name'/>" +
  "<attribute name='sprk_type'/><attribute name='sprk_added'/><attribute name='sprk_summary'/></entity></fetch>";
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
const NO_SORT_CONFIG: DataGridConfiguration = {
  ...CONFIG,
  behavior: { ...CONFIG.behavior, enableSorting: false },
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

// Served in this order: Engagement letter first (so a descending Name sort visibly moves Statement of work up).
const ROWS = [
  { sprk_docid: 'd-1', sprk_name: 'Engagement letter', sprk_type: 'Contract', sprk_added: 'Jan', sprk_summary: 'A' },
  { sprk_docid: 'd-2', sprk_name: 'Statement of work', sprk_type: 'Contract', sprk_added: 'Feb', sprk_summary: 'B' },
];

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

// ─── Layout mocks (as DataGrid.columnResize.test.tsx) ────────────────────────

let containerWidth = 1000;
const originalClientWidth = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'clientWidth');
const originalGetBoundingClientRect = Element.prototype.getBoundingClientRect;
const originalResizeObserver = globalThis.ResizeObserver;

beforeEach(() => {
  containerWidth = 1000;
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
    observe(): void {
      /* the container never changes size in these cases */
    }
    unobserve(): void {
      /* noop */
    }
    disconnect(): void {
      /* noop */
    }
  } as unknown as typeof ResizeObserver;
});

afterEach(() => {
  if (originalClientWidth) Object.defineProperty(HTMLElement.prototype, 'clientWidth', originalClientWidth);
  Element.prototype.getBoundingClientRect = originalGetBoundingClientRect;
  globalThis.ResizeObserver = originalResizeObserver;
});

// ─── Helpers ─────────────────────────────────────────────────────────────────

interface HostProps {
  client: IDataverseClient;
  /** The three opt-in props, spread as given (absent keys are not passed at all). */
  settingsProps: {
    initialSettings?: DataGridSettings;
    onSettingsChange?: (s: DataGridSettings) => void;
    settingsResetKey?: number | string;
  };
}

function GridHost({ client, settingsProps }: HostProps) {
  return (
    <FluentProvider theme={webLightTheme}>
      <DataGrid
        configId={CONFIG_ID}
        dataverseClient={client}
        showViewSelector={false}
        onRecordOpen={() => undefined}
        {...settingsProps}
      />
    </FluentProvider>
  );
}

async function flushFrames(): Promise<void> {
  await act(async () => {
    await new Promise(resolve => setTimeout(resolve, 40));
  });
}

async function renderGrid(settingsProps: HostProps['settingsProps'] = {}, config: DataGridConfiguration = CONFIG) {
  const client = makeClient(config);
  const result = render(<GridHost client={client} settingsProps={settingsProps} />);
  await screen.findByText('Engagement letter', undefined, { timeout: 10000 });
  await flushFrames();
  return {
    ...result,
    client,
    /** Re-renders the same grid with new opt-in props. */
    setSettingsProps(next: HostProps['settingsProps']): void {
      result.rerender(<GridHost client={client} settingsProps={next} />);
    },
  };
}

function headerCell(label: string): HTMLElement {
  const cell = screen.getAllByRole('columnheader').find(c => within(c).queryByText(label) !== null);
  if (!cell) throw new Error(`no "${label}" column header`);
  return cell;
}
const widthOf = (label: string): number => parseFloat(headerCell(label).style.width);
const DATA_COLUMNS = ['Name', 'Type', 'Added', 'Summary'];
const widths = (): number[] => DATA_COLUMNS.map(widthOf);

/** The names in the body rows, top to bottom. */
const bodyNames = (): string[] =>
  ['Engagement letter', 'Statement of work']
    .map(n => ({
      n,
      y: screen.getAllByRole('row').indexOf(screen.getByText(n).closest('[role="row"]') as HTMLElement),
    }))
    .sort((a, b) => a.y - b.y)
    .map(x => x.n);

/** Fluent marks an unsorted column ria-sort="none". */
const ariaSortOf = (label: string): string | null => headerCell(label).getAttribute('aria-sort');
const allUnsorted = (): boolean => DATA_COLUMNS.every(c => ariaSortOf(c) === 'none');

/** The grid's column-options trigger for a column: its name says whether the column is filtered. */
const optionsTrigger = (label: string, filtered = false): HTMLElement =>
  screen.getByRole('button', { name: `${label} column options${filtered ? ', filtered' : ''}` });

/** Applies a "Contains" text filter on a column through the header menu ("Filter by" -> text -> Apply). */
async function applyTextFilter(label: string, attribute: string, text: string): Promise<void> {
  fireEvent.click(optionsTrigger(label));
  fireEvent.click(await screen.findByTestId(`header-cell-content-menu-filter-${attribute}`));
  await act(async () => {
    await new Promise(resolve => setTimeout(resolve, 120)); // the popover opens 50 ms after the menu closes
  });
  const input = await screen.findByRole('textbox');
  fireEvent.change(input, { target: { value: text } });
  fireEvent.click(screen.getByRole('button', { name: 'Apply' }));
  await flushFrames();
}

/** The FetchXML strings the grid has run, in order. */
const fetchesOf = (client: IDataverseClient): string[] =>
  (client.retrieveMultipleRecords as jest.Mock).mock.calls.map(c => c.map(a => String(a)).join(' '));
/** The header's filter glyph: the trigger holds the dropdown chevron, plus the (aria-hidden) filter icon when filtered. */
const filterIconOf = (label: string): boolean =>
  (headerCell(label).querySelector(`button[aria-label^="${label} column options"]`)?.querySelectorAll('svg').length ??
    0) > 1;

async function clickSort(label: string): Promise<void> {
  const button = headerCell(label).querySelector('.fui-TableHeaderCell__button');
  if (!button) throw new Error(`"${label}" has no sort button`);
  fireEvent.click(button);
  await flushFrames();
}

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

/** Control: the widths a grid with none of the new props shows in this container. */
async function defaultFit(): Promise<number[]> {
  const fresh = await renderGrid();
  const fit = widths();
  fresh.unmount();
  return fit;
}

// ─── Tests ───────────────────────────────────────────────────────────────────

describe('DataGrid persisted settings (task 032)', () => {
  describe('with none of the three props (must not fire)', () => {
    it("keeps the container fit and Fluent's own uncontrolled sort", async () => {
      await renderGrid();
      // Fitted to the container (the configured 300 / 150 are scaled down at 1000px), not the configured widths.
      expect(widthOf('Name')).toBeLessThan(300);
      expect(allUnsorted()).toBe(true);
      expect(bodyNames()).toEqual(['Engagement letter', 'Statement of work']);

      await clickSort('Name');
      expect(ariaSortOf('Name')).toBe('ascending');
      await clickSort('Name');
      expect(ariaSortOf('Name')).toBe('descending');
      expect(bodyNames()).toEqual(['Statement of work', 'Engagement letter']);
    });

    it('still takes a dragged width (the existing resize contract) with no host to report to', async () => {
      await renderGrid();
      const before = widthOf('Type');
      await dragColumnEdge('Type', 80);
      expect(widthOf('Type')).toBeGreaterThan(before + 50);
    });
  });

  describe('initialSettings', () => {
    it('starts a column at the given width, leaves the others at the container fit, and reports nothing on mount', async () => {
      const fit = await defaultFit();
      const onSettingsChange = jest.fn();
      await renderGrid({ initialSettings: { widths: { sprk_name: 420 } }, onSettingsChange });
      expect(widthOf('Name')).toBe(420);
      expect(widthOf('Type')).toBe(fit[1]);
      expect(widthOf('Added')).toBe(fit[2]);
      expect(onSettingsChange).not.toHaveBeenCalled();
    });

    it('never starts a column below its minimum width', async () => {
      await renderGrid({ initialSettings: { widths: { sprk_name: 20 } } });
      expect(widthOf('Name')).toBe(150); // max(80, 300 × 0.5), as for a drag
    });

    it('drops widths it cannot use (unknown column, not a number, zero, negative) and keeps the container fit', async () => {
      const fit = await defaultFit();
      const bad = {
        no_such_column: 500,
        sprk_name: Number.NaN,
        sprk_type: -5,
        sprk_added: 0,
        sprk_summary: '300' as unknown as number,
      };
      await renderGrid({ initialSettings: { widths: bad } });
      await flushFrames();
      expect(widths()).toEqual(fit);
    });

    it('drops a huge width (a tampered entry) and keeps the container fit; 2000px, the limit, is kept', async () => {
      const fit = await defaultFit();
      const huge = await renderGrid({ initialSettings: { widths: { sprk_name: 1e9, sprk_type: 2001 } } });
      await flushFrames();
      expect(widths()).toEqual(fit);

      huge.unmount();
      await renderGrid({ initialSettings: { widths: { sprk_name: 2000 } } });
      expect(widthOf('Name')).toBe(2000);
    });

    it('starts sorted by the given column and direction', async () => {
      await renderGrid({ initialSettings: { sort: { columnId: 'sprk_name', direction: 'descending' } } });
      expect(ariaSortOf('Name')).toBe('descending');
      expect(bodyNames()).toEqual(['Statement of work', 'Engagement letter']);
    });

    it('ignores a sort on a column the grid does not have, and a sort on a grid with sorting off', async () => {
      const unknown = await renderGrid({
        initialSettings: { sort: { columnId: 'no_such_column', direction: 'descending' } },
      });
      expect(allUnsorted()).toBe(true);
      expect(bodyNames()).toEqual(['Engagement letter', 'Statement of work']);
      unknown.unmount();

      await renderGrid(
        { initialSettings: { sort: { columnId: 'sprk_name', direction: 'descending' } } },
        NO_SORT_CONFIG
      );
      expect(bodyNames()).toEqual(['Engagement letter', 'Statement of work']);
    });

    it('is read once: a later change to it does not move the grid', async () => {
      const grid = await renderGrid({ initialSettings: { widths: { sprk_name: 420 } } });
      grid.setSettingsProps({ initialSettings: { widths: { sprk_name: 500 } } });
      await flushFrames();
      expect(widthOf('Name')).toBe(420);
    });
  });

  describe('onSettingsChange', () => {
    it('reports the dragged width, with every column the user has dragged', async () => {
      const onSettingsChange = jest.fn();
      await renderGrid({ onSettingsChange });
      await dragColumnEdge('Name', 80);
      const last = onSettingsChange.mock.calls[onSettingsChange.mock.calls.length - 1][0] as DataGridSettings;
      expect(last.widths?.sprk_name).toBe(widthOf('Name'));
      expect(last.sort).toBeUndefined();

      await dragColumnEdge('Type', 60);
      const next = onSettingsChange.mock.calls[onSettingsChange.mock.calls.length - 1][0] as DataGridSettings;
      expect(Object.keys(next.widths ?? {}).sort()).toEqual(['sprk_name', 'sprk_type']);
    });

    it('reports the sort the user clicks, keeping the widths', async () => {
      const onSettingsChange = jest.fn();
      await renderGrid({ onSettingsChange });
      await dragColumnEdge('Name', 80);
      await clickSort('Type');
      const last = onSettingsChange.mock.calls[onSettingsChange.mock.calls.length - 1][0] as DataGridSettings;
      expect(last.sort).toEqual({ columnId: 'sprk_type', direction: 'ascending' });
      expect(last.widths?.sprk_name).toBe(widthOf('Name'));
      await clickSort('Type');
      const again = onSettingsChange.mock.calls[onSettingsChange.mock.calls.length - 1][0] as DataGridSettings;
      expect(again.sort).toEqual({ columnId: 'sprk_type', direction: 'descending' });
    });

    it('lets a sort made through the grid sort the rows (the sort is controlled, not just reported)', async () => {
      await renderGrid({ onSettingsChange: jest.fn() });
      await clickSort('Name');
      await clickSort('Name');
      expect(ariaSortOf('Name')).toBe('descending');
      expect(bodyNames()).toEqual(['Statement of work', 'Engagement letter']);
    });

    it('survives a remount: settings reported by one grid restore the next one', async () => {
      const onSettingsChange = jest.fn();
      const first = await renderGrid({ onSettingsChange });
      await dragColumnEdge('Name', 80);
      await clickSort('Name');
      await clickSort('Name');
      const widthBefore = widthOf('Name');
      const saved = onSettingsChange.mock.calls[onSettingsChange.mock.calls.length - 1][0] as DataGridSettings;
      first.unmount();

      await renderGrid({ initialSettings: saved });
      expect(widthOf('Name')).toBe(widthBefore);
      expect(ariaSortOf('Name')).toBe('descending');
      expect(bodyNames()).toEqual(['Statement of work', 'Engagement letter']);
    });
  });

  describe('settingsResetKey ("Clear all")', () => {
    it('returns to the configured widths and order when the key changes, and reports empty settings', async () => {
      const fit = await defaultFit();
      const onSettingsChange = jest.fn();
      const grid = await renderGrid({
        initialSettings: { widths: { sprk_name: 420 }, sort: { columnId: 'sprk_name', direction: 'descending' } },
        onSettingsChange,
        settingsResetKey: 0,
      });
      expect(widthOf('Name')).toBe(420);
      expect(ariaSortOf('Name')).toBe('descending');
      expect(onSettingsChange).not.toHaveBeenCalled();

      grid.setSettingsProps({ onSettingsChange, settingsResetKey: 1 });
      await flushFrames();
      expect(widths()).toEqual(fit); // auto-fit is back: every column at the container fit
      expect(allUnsorted()).toBe(true);
      expect(bodyNames()).toEqual(['Engagement letter', 'Statement of work']);
      const last = onSettingsChange.mock.calls[onSettingsChange.mock.calls.length - 1][0] as DataGridSettings;
      expect(last).toEqual({ widths: {} });
    });

    it('also resets a width the user dragged in this session, and lets them drag again afterwards', async () => {
      const fit = await defaultFit();
      const onSettingsChange = jest.fn();
      const grid = await renderGrid({ onSettingsChange, settingsResetKey: 'a' });
      await dragColumnEdge('Name', 80);
      expect(widthOf('Name')).toBeGreaterThan(fit[0] + 50);

      grid.setSettingsProps({ onSettingsChange, settingsResetKey: 'b' });
      await flushFrames();
      expect(widths()).toEqual(fit);

      await dragColumnEdge('Name', 40);
      expect(widthOf('Name')).toBeGreaterThan(fit[0] + 20);
    });

    it('also clears the column filters: the indicator goes, the query runs unfiltered, the column is announced as not filtered', async () => {
      const grid = await renderGrid({ settingsResetKey: 0 });
      await applyTextFilter('Name', 'sprk_name', 'Engagement');
      expect(optionsTrigger('Name', true)).toBeTruthy();
      expect(filterIconOf('Name')).toBe(true);
      const filtered = fetchesOf(grid.client);
      expect(filtered[filtered.length - 1]).toContain('Engagement');

      const callsBefore = filtered.length;
      grid.setSettingsProps({ settingsResetKey: 1 });
      await flushFrames();
      expect(optionsTrigger('Name')).toBeTruthy(); // no longer ", filtered"
      expect(screen.queryByRole('button', { name: 'Name column options, filtered' })).toBeNull();
      expect(filterIconOf('Name')).toBe(false);
      const after = fetchesOf(grid.client);
      expect(after.length).toBeGreaterThan(callsBefore); // the grid re-queried
      expect(after[after.length - 1]).not.toContain('Engagement');
    });

    it('keeps the column filters while the key stays the same', async () => {
      const grid = await renderGrid({ settingsResetKey: 5 });
      await applyTextFilter('Name', 'sprk_name', 'Engagement');
      grid.setSettingsProps({ settingsResetKey: 5 });
      await flushFrames();
      expect(optionsTrigger('Name', true)).toBeTruthy();
      expect(filterIconOf('Name')).toBe(true);
      const after = fetchesOf(grid.client);
      expect(after[after.length - 1]).toContain('Engagement');
    });

    it('does nothing while the key stays the same, and on mount', async () => {
      const onSettingsChange = jest.fn();
      const grid = await renderGrid({
        initialSettings: { widths: { sprk_name: 420 } },
        onSettingsChange,
        settingsResetKey: 7,
      });
      grid.setSettingsProps({ initialSettings: { widths: { sprk_name: 420 } }, onSettingsChange, settingsResetKey: 7 });
      await flushFrames();
      expect(widthOf('Name')).toBe(420);
      expect(onSettingsChange).not.toHaveBeenCalled();
    });
  });
});

/**
 * GridSettings.test — a user's column widths and sort on an external SPA grid survive a reload, and "Clear all"
 * restores the defaults (spaarke-SPA-external-access-platform-r4 task 032, FR-19, #2489).
 *
 * Three layers, each real where it matters:
 *  1. `gridSettingsStore`: the per-browser `localStorage` entry, one per grid configuration id, versioned, and fail-open
 *     (a throwing, full or blocked store, and malformed or foreign data, all mean "no settings", never an error).
 *  2. The REAL shared grid under the record Documents list (`DocumentLibrary` over the committed grid configuration): the
 *     saved settings come back after a remount (a reload), the "Clear all" command is in the toolbar (its tooltip equals
 *     its aria-label), and clicking it restores the defaults and clears the stored entry without a reload.
 *  3. `createGridWidgetBody` (the workspace grids) with the grid stood in: it hands the grid what is stored, stores what the
 *     grid reports, and puts "Clear all" after the host's own commands. Other grids stay stand-ins; only the Documents
 *     grid is let through to the real one.
 * The shared grid's own contract (initial / reported / reset settings, defaults untouched) is tested in the shared lib
 * (`DataGrid.persistedSettings.test.tsx`). Each must-fire check has a must-not-fire pair.
 */
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { RECORD_ACTION_ICONS } from '@spaarke/ui-components/components/RecordCommandBar';
import { DOCUMENTS_GRID_ID, documentRow } from './documentsGridHarness';

const { bffApiCall, bffApiBlob } = vi.hoisted(() => ({ bffApiCall: vi.fn(), bffApiBlob: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall, bffApiBlob };
});
vi.mock('../src/services/gridDataverseClient', async () => (await import('./documentsGridHarness')).gridClientModule());
vi.mock('@spaarke/ui-components/utils/themeStorage', () => ({
  resolveCodePageTheme: () => null,
  setupCodePageThemeListener: () => () => undefined,
}));

interface SeenGridProps {
  configId: string;
  hostCommands?: ReadonlyArray<{ id: string; label: string; icon?: unknown; onInvoke: () => void }>;
  initialSettings?: { widths?: Record<string, number>; sort?: { columnId: string; direction: string } };
  onSettingsChange?: (s: { widths?: Record<string, number>; sort?: { columnId: string; direction: string } }) => void;
  settingsResetKey?: number | string;
}
const seen: SeenGridProps[] = [];

// The Documents list's grid is the real one; every other grid (the workspace widgets) is a stand-in that records its props.
vi.mock('../src/widgets/ExternalDataGrid', async () => {
  const R = await import('react');
  const { documentsGridOr: through } = await import('./documentsGridHarness');
  return {
    ExternalDataGrid: await through((props: SeenGridProps) => {
      seen.push(props);
      return R.createElement('div', { 'data-testid': 'fake-grid' });
    }),
  };
});

import { DocumentLibrary } from '../src/components/DocumentLibrary';
import { createGridWidgetBody } from '../src/widgets/GridWidgetBody';
import { clearGridSettings, isEmpty, loadGridSettings, saveGridSettings } from '../src/services/gridSettingsStore';

const WIDGET_GRID = '3ff4102c-1092-f111-b8dc-7ced8ddc4a05';
const OTHER_GRID = '11111111-2222-4333-8444-555555555555';
const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const MATTER = guid(201);
const KEY_PREFIX = 'sprk.ext.gridSettings.v1.';
const keyOf = (configId: string): string => `${KEY_PREFIX}${configId}`;

const SLOW = { timeout: 60000 };
const LOAD = { timeout: 15000 };

const SAMPLE = {
  widths: { sprk_documentname: 333 },
  sort: { columnId: 'sprk_documentname', direction: 'descending' as const },
};

beforeEach(() => {
  window.localStorage.clear();
  seen.length = 0;
  bffApiCall.mockReset();
  bffApiBlob.mockReset();
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  window.localStorage.clear();
});

// ─── 1. The store ────────────────────────────────────────────────────────────

describe('gridSettingsStore', () => {
  it("saves and loads one grid's settings, keyed by its configuration id, and keeps grids apart", () => {
    saveGridSettings(DOCUMENTS_GRID_ID, SAMPLE);
    saveGridSettings(OTHER_GRID, { widths: { x: 90 } });

    expect(loadGridSettings(DOCUMENTS_GRID_ID)).toEqual(SAMPLE);
    expect(loadGridSettings(OTHER_GRID)).toEqual({ widths: { x: 90 } });
    expect(window.localStorage.getItem(keyOf(DOCUMENTS_GRID_ID))).not.toBeNull();
    // Must-not-fire pair: a grid nobody saved for has nothing, and the key carries no identity (only the id).
    expect(loadGridSettings('99999999-9999-4999-8999-999999999999')).toBeUndefined();
    expect(Object.keys(window.localStorage).every(k => k.startsWith(KEY_PREFIX))).toBe(true);
  });

  it('writes a versioned entry, and ignores an entry of another version', () => {
    saveGridSettings(OTHER_GRID, { widths: { x: 90 } });
    expect(JSON.parse(window.localStorage.getItem(keyOf(OTHER_GRID)) ?? '{}')).toMatchObject({ v: 1 });

    window.localStorage.setItem(keyOf(OTHER_GRID), JSON.stringify({ v: 2, widths: { x: 90 } }));
    expect(loadGridSettings(OTHER_GRID)).toBeUndefined();
  });

  it('settings with nothing in them clear the entry (a reset grid leaves no trace)', () => {
    saveGridSettings(OTHER_GRID, { widths: { x: 90 } });
    saveGridSettings(OTHER_GRID, { widths: {} });
    expect(window.localStorage.getItem(keyOf(OTHER_GRID))).toBeNull();

    saveGridSettings(OTHER_GRID, { widths: { x: 90 } });
    clearGridSettings(OTHER_GRID);
    expect(window.localStorage.getItem(keyOf(OTHER_GRID))).toBeNull();
    expect(isEmpty({ widths: {} })).toBe(true);
    expect(isEmpty({ widths: { x: 1 } })).toBe(false);
    expect(isEmpty({ sort: { columnId: 'x', direction: 'ascending' } })).toBe(false);
  });

  it.each([
    ['not JSON', '{nope'],
    ['a JSON array', '[1,2]'],
    ['a JSON string', '"x"'],
    ['null', 'null'],
    ['widths that are not an object', JSON.stringify({ v: 1, widths: [1] })],
    ['a sort with no column', JSON.stringify({ v: 1, sort: { direction: 'ascending' } })],
    ['a sort with a bad direction', JSON.stringify({ v: 1, sort: { columnId: 'x', direction: 'up' } })],
    ['a null sort', JSON.stringify({ v: 1, sort: null })],
    ['only unusable widths', JSON.stringify({ v: 1, widths: { x: -4, y: 'wide', z: 0 } })],
  ])('malformed data (%s) means no settings, with nothing thrown', (_name, raw) => {
    window.localStorage.setItem(keyOf(OTHER_GRID), raw);
    expect(() => loadGridSettings(OTHER_GRID)).not.toThrow();
    expect(loadGridSettings(OTHER_GRID)).toBeUndefined();
  });

  it('keeps the usable widths of an entry and drops the unusable ones', () => {
    window.localStorage.setItem(keyOf(OTHER_GRID), JSON.stringify({ v: 1, widths: { x: 120, y: -4, z: 'wide' } }));
    expect(loadGridSettings(OTHER_GRID)).toEqual({ widths: { x: 120 } });
  });

  it('drops a huge width (a tampered entry); 2000px, the limit, is kept; an entry of only huge widths is nothing', () => {
    window.localStorage.setItem(
      keyOf(OTHER_GRID),
      JSON.stringify({ v: 1, widths: { x: 1e9, y: 2001, z: 2000, w: 120 } })
    );
    expect(loadGridSettings(OTHER_GRID)).toEqual({ widths: { z: 2000, w: 120 } });

    window.localStorage.setItem(keyOf(OTHER_GRID), JSON.stringify({ v: 1, widths: { x: 1e9 } }));
    expect(loadGridSettings(OTHER_GRID)).toBeUndefined();

    // And a save never writes one.
    saveGridSettings(OTHER_GRID, { widths: { x: 1e9, w: 120 } });
    expect(JSON.parse(window.localStorage.getItem(keyOf(OTHER_GRID)) ?? '{}').widths).toEqual({ w: 120 });
  });

  it('a storage that throws on read, write or remove (private mode, quota, blocked) is silent', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('full', 'QuotaExceededError');
    });
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new Error('SecurityError');
    });
    expect(() => saveGridSettings(OTHER_GRID, SAMPLE)).not.toThrow();
    expect(() => clearGridSettings(OTHER_GRID)).not.toThrow();
    expect(() => loadGridSettings(OTHER_GRID)).not.toThrow();
    expect(loadGridSettings(OTHER_GRID)).toBeUndefined();
  });

  it('a window.localStorage accessor that itself throws is silent', () => {
    const spy = vi.spyOn(window, 'localStorage', 'get').mockImplementation(() => {
      throw new Error('SecurityError: access denied');
    });
    expect(() => saveGridSettings(OTHER_GRID, SAMPLE)).not.toThrow();
    expect(() => clearGridSettings(OTHER_GRID)).not.toThrow();
    expect(loadGridSettings(OTHER_GRID)).toBeUndefined();
    spy.mockRestore();
  });
});

// ─── 2. The real grid: the record Documents list ─────────────────────────────

describe('the Documents grid remembers widths and sort per browser, and Clear all restores the defaults', () => {
  const documents = [
    { sprk_documentid: guid(901), sprk_name: 'Alpha.pdf', createdon: '2026-01-05T10:00:00Z' },
    { sprk_documentid: guid(902), sprk_name: 'Zeta.pdf', createdon: '2026-01-06T10:00:00Z' },
  ];

  function listReturnsDocuments(): void {
    bffApiCall.mockImplementation(async (path: string) => {
      if (path.endsWith('/documents')) return { value: documents.map(d => ({ ...d })), canCreate: false };
      throw new Error(`unexpected path ${path}`);
    });
  }

  function mountLibrary(): void {
    render(
      <FluentProvider theme={webLightTheme}>
        <DocumentLibrary root="matters" recordId={MATTER} canDownload />
      </FluentProvider>
    );
  }

  const header = (label: string): HTMLElement => {
    const cell = screen.getAllByRole('columnheader').find(c => within(c).queryByText(label) !== null);
    if (!cell) throw new Error(`no "${label}" column header`);
    return cell;
  };
  const widthOf = (label: string): number => parseFloat(header(label).style.width);
  const ariaSort = (label: string): string | null => header(label).getAttribute('aria-sort');
  /** The document names in body order. */
  const names = (): string[] =>
    screen
      .getAllByRole('row')
      .map(r => r.textContent ?? '')
      .map(t => (t.includes('Alpha.pdf') ? 'Alpha.pdf' : t.includes('Zeta.pdf') ? 'Zeta.pdf' : ''))
      .filter(Boolean);

  async function load(): Promise<void> {
    mountLibrary();
    await documentRow('Alpha.pdf');
    await act(async () => {
      await new Promise(resolve => setTimeout(resolve, 40));
    });
  }

  async function sortByName(times: number): Promise<void> {
    const button = header('Name').querySelector('.fui-TableHeaderCell__button');
    if (!button) throw new Error('"Name" has no sort button');
    for (let i = 0; i < times; i++) {
      fireEvent.click(button);
      await act(async () => {
        await new Promise(resolve => setTimeout(resolve, 40));
      });
    }
  }

  it(
    'has Clear all in the toolbar, with a tooltip equal to its aria-label',
    async () => {
      listReturnsDocuments();
      await load();
      const reset = screen.getByRole('button', { name: 'Clear all' });
      expect(reset.getAttribute('aria-label')).toBe('Clear all');
      expect(reset.textContent).toContain('Clear all');
      // The tooltip is the command's label: the same text as its aria-label (the grid's command bar wraps it in Tooltip).
      expect(screen.queryByRole('tooltip')).toBeNull();
      fireEvent.focusIn(reset);
      const tooltip = await screen.findByRole('tooltip', undefined, LOAD);
      expect(tooltip.textContent).toBe('Clear all');
      // The icon is one from the record action icon map: an svg, drawn by the grid.
      expect(reset.querySelector('svg')).not.toBeNull();
    },
    SLOW
  );

  it(
    'a sort and a width saved in this browser come back after a reload (the grid is mounted again)',
    async () => {
      listReturnsDocuments();
      await load();
      const fitName = widthOf('Name');
      expect(ariaSort('Name')).toBe('none');
      cleanup();

      saveGridSettings(DOCUMENTS_GRID_ID, SAMPLE);
      await load();
      expect(widthOf('Name')).toBe(333);
      expect(fitName).not.toBe(333);
      expect(ariaSort('Name')).toBe('descending');
      expect(names()).toEqual(['Zeta.pdf', 'Alpha.pdf']);
    },
    SLOW
  );

  it(
    "must-not-fire pair: another grid's saved settings, and settings nobody saved, change nothing here",
    async () => {
      listReturnsDocuments();
      await load();
      const fit = ['Name', 'Type', 'Date Added', 'AI Summary'].map(widthOf);
      cleanup();

      saveGridSettings(OTHER_GRID, SAMPLE);
      await load();
      expect(['Name', 'Type', 'Date Added', 'AI Summary'].map(widthOf)).toEqual(fit);
      expect(ariaSort('Name')).toBe('none');
      expect(names()).toEqual(['Alpha.pdf', 'Zeta.pdf']);
    },
    SLOW
  );

  it(
    'a sort the user makes is stored, and is there after a reload',
    async () => {
      listReturnsDocuments();
      await load();
      expect(window.localStorage.getItem(keyOf(DOCUMENTS_GRID_ID))).toBeNull(); // nothing is stored on mount

      await sortByName(2);
      expect(ariaSort('Name')).toBe('descending');
      expect(loadGridSettings(DOCUMENTS_GRID_ID)?.sort).toEqual({
        columnId: 'sprk_documentname',
        direction: 'descending',
      });
      cleanup();

      await load();
      expect(ariaSort('Name')).toBe('descending');
      expect(names()).toEqual(['Zeta.pdf', 'Alpha.pdf']);
    },
    SLOW
  );

  it(
    'Clear all restores the configured widths and order without a reload, and clears the stored entry',
    async () => {
      listReturnsDocuments();
      await load();
      const fit = ['Name', 'Type', 'Date Added', 'AI Summary'].map(widthOf);
      cleanup();

      saveGridSettings(DOCUMENTS_GRID_ID, SAMPLE);
      await load();
      expect(widthOf('Name')).toBe(333);
      expect(ariaSort('Name')).toBe('descending');

      fireEvent.click(screen.getByRole('button', { name: 'Clear all' }));
      await waitFor(() => expect(ariaSort('Name')).toBe('none'), LOAD);
      await act(async () => {
        await new Promise(resolve => setTimeout(resolve, 40));
      });
      expect(['Name', 'Type', 'Date Added', 'AI Summary'].map(widthOf)).toEqual(fit);
      expect(names()).toEqual(['Alpha.pdf', 'Zeta.pdf']);
      expect(window.localStorage.getItem(keyOf(DOCUMENTS_GRID_ID))).toBeNull();

      // And it stays reset after a reload.
      cleanup();
      await load();
      expect(['Name', 'Type', 'Date Added', 'AI Summary'].map(widthOf)).toEqual(fit);
      expect(ariaSort('Name')).toBe('none');
    },
    SLOW
  );

  it(
    'with localStorage throwing, the grid shows its defaults, no error, and sorting still works for the visit',
    async () => {
      vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
        throw new Error('SecurityError');
      });
      vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
        throw new DOMException('full', 'QuotaExceededError');
      });
      vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
        throw new Error('SecurityError');
      });
      const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      listReturnsDocuments();

      await load();
      expect(ariaSort('Name')).toBe('none');
      expect(screen.queryByRole('alert')).toBeNull();

      await sortByName(2);
      expect(ariaSort('Name')).toBe('descending');
      expect(names()).toEqual(['Zeta.pdf', 'Alpha.pdf']);
      fireEvent.click(screen.getByRole('button', { name: 'Clear all' }));
      await waitFor(() => expect(ariaSort('Name')).toBe('none'), LOAD);
      expect(screen.queryByRole('alert')).toBeNull();
      expect(consoleError).not.toHaveBeenCalled();
    },
    SLOW
  );

  it(
    'malformed stored data shows the defaults and no error',
    async () => {
      listReturnsDocuments();
      await load();
      const fit = ['Name', 'Type', 'Date Added', 'AI Summary'].map(widthOf);
      cleanup();

      window.localStorage.setItem(keyOf(DOCUMENTS_GRID_ID), '{"v":1,"widths":');
      await load();
      expect(['Name', 'Type', 'Date Added', 'AI Summary'].map(widthOf)).toEqual(fit);
      expect(ariaSort('Name')).toBe('none');
      expect(screen.queryByRole('alert')).toBeNull();
    },
    SLOW
  );
});

// ─── 3. The workspace grids (createGridWidgetBody) ───────────────────────────

describe('createGridWidgetBody wires the grid settings', () => {
  const lastProps = (): SeenGridProps => seen[seen.length - 1];
  const widgetProps = (): SeenGridProps[] => seen.filter(p => p.configId === WIDGET_GRID);

  it("hands the grid what is stored for ITS configuration id, and nothing else's", () => {
    saveGridSettings(WIDGET_GRID, { widths: { name: 222 } });
    saveGridSettings(OTHER_GRID, { widths: { name: 999 } });
    const Body = createGridWidgetBody(WIDGET_GRID);
    render(React.createElement(Body, { title: 'Things' }));
    expect(widgetProps().every(p => p.initialSettings?.widths?.name === 222)).toBe(true);
  });

  it('must-not-fire pair: nothing stored, nothing handed; a malformed entry is nothing', () => {
    const Body = createGridWidgetBody(WIDGET_GRID);
    render(React.createElement(Body, { title: 'Things' }));
    expect(lastProps().initialSettings).toBeUndefined();
    expect(typeof lastProps().onSettingsChange).toBe('function'); // it still listens

    cleanup();
    seen.length = 0;
    window.localStorage.setItem(keyOf(WIDGET_GRID), 'garbage');
    render(React.createElement(Body, { title: 'Things' }));
    expect(lastProps().initialSettings).toBeUndefined();
  });

  it('stores what the grid reports, and a remounted grid is handed the latest (not the first) settings', () => {
    const Body = createGridWidgetBody(WIDGET_GRID);
    const first = render(React.createElement(Body, { title: 'Things' }));
    act(() => {
      lastProps().onSettingsChange?.({ widths: { name: 250 } });
    });
    expect(loadGridSettings(WIDGET_GRID)).toEqual({ widths: { name: 250 } });
    first.unmount();

    seen.length = 0;
    render(React.createElement(Body, { title: 'Things' })); // a "reload"
    expect(lastProps().initialSettings).toEqual({ widths: { name: 250 } });
  });

  it("puts Clear all after the host's own commands; invoking it clears the store and signals the grid", () => {
    saveGridSettings(WIDGET_GRID, { widths: { name: 222 } });
    const Body = createGridWidgetBody(WIDGET_GRID);
    const hostCommands = [{ id: 'do-it', label: 'Do it', onInvoke: vi.fn() }];
    render(React.createElement(Body, { title: 'Things', hostCommands }));

    const commands = lastProps().hostCommands ?? [];
    expect(commands.map(c => c.id)).toEqual(['do-it', 'clear-all']);
    expect(commands[1].label).toBe('Clear all');
    // Its own reset icon (owner 2026-11-23), not the refresh icon it first reused.
    const icon = commands[1].icon as React.ReactElement;
    expect(icon.type).toBe(RECORD_ACTION_ICONS.reset);
    expect(icon.type).not.toBe(RECORD_ACTION_ICONS.refresh);
    const keyBefore = lastProps().settingsResetKey;

    act(() => {
      commands[1].onInvoke();
    });
    expect(loadGridSettings(WIDGET_GRID)).toBeUndefined();
    expect(window.localStorage.getItem(keyOf(WIDGET_GRID))).toBeNull();
    expect(lastProps().settingsResetKey).not.toBe(keyBefore);
    expect(lastProps().initialSettings).toBeUndefined();
    // The host's own command is untouched.
    expect(hostCommands[0].onInvoke).not.toHaveBeenCalled();
  });

  it('with localStorage throwing, the grid is handed no settings and reporting or resetting throws nothing', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('full', 'QuotaExceededError');
    });
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new Error('SecurityError');
    });
    const Body = createGridWidgetBody(WIDGET_GRID);
    render(React.createElement(Body, { title: 'Things' }));
    expect(lastProps().initialSettings).toBeUndefined();
    expect(() =>
      act(() => {
        lastProps().onSettingsChange?.({ widths: { name: 250 } });
        lastProps().hostCommands?.[0].onInvoke();
      })
    ).not.toThrow();
  });
});

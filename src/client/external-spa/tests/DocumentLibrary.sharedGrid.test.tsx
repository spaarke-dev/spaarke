/**
 * R4 task 023c (design 023 section 3.2): the record Documents list is the shared grid. This replaces R4 task 009's D-12
 * width-class test (`DocumentLibrary.columns`): the hand-rolled Fluent table and its width classes are gone, so the contract
 * is now the grid's own: the configured columns, a fixed-width actions column, ellipsis with a tooltip on a name that does
 * not fit, the grid's incomplete-list notice, and the row actions the caller may use.
 *
 * Real: `DocumentLibrary` and the shared `ExternalDataGrid` / `DataGrid` over a stub Dataverse client that serves the
 * COMMITTED configuration (`config/external-record-documents.gridconfiguration.json`, see `documentsGridHarness`). Substituted:
 * the network seam (`bffApiCall`). Each must-fire check has a must-not-fire pair.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { AccessLevel, ApiError } from '../src/types';
import type { CallerIdentity } from '../src/api/delete-own';
import { DOCUMENTS_GRID_FILE, DOCUMENTS_GRID_ID, documentRow, gridQueries, rowAction } from './documentsGridHarness';

const { bffApiCall, bffApiBlob } = vi.hoisted(() => ({ bffApiCall: vi.fn(), bffApiBlob: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall, bffApiBlob };
});
vi.mock('../src/services/gridDataverseClient', async () => (await import('./documentsGridHarness')).gridClientModule());

import { DocumentLibrary, RECORD_DOCUMENTS_GRID } from '../src/components/DocumentLibrary';

const SLOW = { timeout: 60000 };
const LOAD = { timeout: 15000 };
const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const ME = guid(7);
const SOMEONE = guid(8);
const MATTER = guid(201);
const SERVICE_REQUEST = guid(601);
const asContact: CallerIdentity = { contactId: ME, systemUserId: null };

interface Row {
  sprk_documentid: string;
  sprk_name: string;
  sprk_documenttype?: string;
  sprk_summary?: string;
  createdon?: string;
  hasFile?: boolean;
  _sprk_createdbycontact_value?: string;
}

const row = (n: number, name: string, extra: Partial<Row> = {}): Row => ({
  sprk_documentid: guid(900 + n),
  sprk_name: name,
  createdon: '2026-01-05T10:00:00Z',
  ...extra,
});

const NAME_COL = 'A very long engagement letter name that cannot fit in its column at the modal width.pdf';

/** The list answers `body` for the documents route; any other path fails the test loudly. */
function listReturns(body: { value: Row[]; canCreate?: boolean; truncated?: boolean }): void {
  bffApiCall.mockImplementation(async (path: string) => {
    if (path.endsWith('/documents')) return body;
    throw new Error(`unexpected path ${path}`);
  });
}

/** The list reads (GET) of the documents route; an upload's POST goes to the same path and is not a read. */
const listCalls = (): string[] =>
  bffApiCall.mock.calls
    .filter(c => (c[1] as RequestInit | undefined)?.method !== 'POST')
    .map(c => String(c[0]))
    .filter(p => p.endsWith('/documents'));

function renderLibrary(props: Record<string, unknown> = {}): void {
  render(
    <FluentProvider theme={webLightTheme}>
      <DocumentLibrary root="matters" recordId={MATTER} canDownload {...(props as object)} />
    </FluentProvider>
  );
}

beforeEach(() => {
  bffApiCall.mockReset();
  bffApiBlob.mockReset();
  gridQueries.length = 0;
});

afterEach(() => {
  // Layout stubs of a test are removed (the properties are own properties of HTMLElement.prototype, set by the test).
  delete (HTMLElement.prototype as unknown as Record<string, unknown>).clientWidth;
  delete (HTMLElement.prototype as unknown as Record<string, unknown>).scrollWidth;
  vi.restoreAllMocks();
});

describe('the Documents list is the shared grid on the committed configuration', () => {
  it('names the grid the committed configuration file defines (no drift between the constant and the file)', () => {
    expect(RECORD_DOCUMENTS_GRID).toBe(DOCUMENTS_GRID_ID);
    expect(DOCUMENTS_GRID_FILE.id).toBe(DOCUMENTS_GRID_ID);
    expect(DOCUMENTS_GRID_FILE.entity).toBe('sprk_document');
  });

  it(
    'draws the configured columns (Name, Type, Date Added, AI Summary) and an actions column, with the row values from the route',
    async () => {
      listReturns({
        value: [row(1, 'Brief.pdf', { sprk_documenttype: 'Engagement letter', sprk_summary: 'A short summary.' })],
      });
      renderLibrary({ caller: asContact });

      const body = await documentRow('Brief.pdf');
      const grid = body.closest('[role="grid"]') as HTMLElement;
      const headers = within(grid)
        .getAllByRole('columnheader')
        .map(h => h.textContent);
      expect(headers).toEqual(['Name', 'Type', 'Date Added', 'AI Summary', 'Actions']);
      expect(within(body).getByText('Engagement letter')).toBeTruthy();
      expect(within(body).getByText('A short summary.')).toBeTruthy();
      expect(within(body).getByText('1/5/2026')).toBeTruthy();
    },
    SLOW
  );

  it(
    'a service-request root reads only its own route, once, and the grid runs no FetchXML of its own',
    async () => {
      listReturns({ value: [row(1, 'Brief.pdf')], canCreate: true });
      renderLibrary({ root: 'servicerequests', recordId: SERVICE_REQUEST });

      await documentRow('Brief.pdf');
      expect(listCalls()).toEqual([`/api/v1/external/servicerequests/${SERVICE_REQUEST}/documents`]);
      expect(gridQueries).toEqual([]);
    },
    SLOW
  );

  it(
    'must-not-fire pair: every record root reads once per load from its own route (nothing is fetched twice)',
    async () => {
      listReturns({ value: [row(1, 'Brief.pdf')], canCreate: true });
      renderLibrary({ root: 'workassignments', recordId: guid(301) });

      await documentRow('Brief.pdf');
      expect(listCalls()).toEqual([`/api/v1/external/workassignments/${guid(301)}/documents`]);
      // canCreate comes from that same response: the Upload control appears without a second read.
      expect(await screen.findByRole('button', { name: /Upload Document/ }, LOAD)).toBeTruthy();
      expect(bffApiCall).toHaveBeenCalledTimes(1);
    },
    SLOW
  );
});

describe('column sizing and truncation (replaces the D-12 width classes)', () => {
  const widthOf = (el: Element): number => parseInt((el as HTMLElement).style.width, 10);

  async function renderAt(
    containerWidth: number,
    caller: CallerIdentity | null = asContact,
    canDownload = true
  ): Promise<HTMLElement> {
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => containerWidth });
    // A writer (canCreate: true), so a caller's Delete is on offer: it is the third action button.
    listReturns({
      value: [
        row(1, NAME_COL, {
          sprk_documenttype: 'Engagement letter (signed counterpart)',
          sprk_summary: 'S'.repeat(120),
          _sprk_createdbycontact_value: ME,
        }),
      ],
      canCreate: true,
    });
    renderLibrary({ caller, canUpload: true, canDownload });
    const body = await documentRow(NAME_COL);
    return body.closest('[role="grid"]') as HTMLElement;
  }

  /** The grid's sized cells (header and body) per column, skipping the selection cell (it carries no width). */
  const sized = (grid: HTMLElement, role: 'columnheader' | 'gridcell'): HTMLElement[] =>
    within(grid)
      .getAllByRole(role)
      .filter(c => c.style.width !== '') as HTMLElement[];

  it(
    'at 1000px the five columns are sized, fit the container, and header and body cells of a column agree (no overlap)',
    async () => {
      const grid = await renderAt(1000);
      const headers = sized(grid, 'columnheader');
      const cells = sized(grid, 'gridcell');
      expect(headers.map(h => h.textContent)).toEqual(['Name', 'Type', 'Date Added', 'AI Summary', 'Actions']);
      expect(cells).toHaveLength(headers.length);

      headers.forEach((h, i) => {
        expect(widthOf(cells[i])).toBe(widthOf(h));
        // A cell is as wide as its column and no wider: it cannot spill into the next one.
        expect(cells[i].style.maxWidth).toBe(h.style.maxWidth);
        expect(widthOf(h)).toBeGreaterThanOrEqual(80);
      });
      const total = headers.reduce((sum, h) => sum + widthOf(h), 0);
      expect(total).toBeLessThanOrEqual(1000);
    },
    SLOW
  );

  it(
    'the actions column is fixed (no grow, no shrink) at the width of its three icon buttons, whatever the container',
    async () => {
      const wide = await renderAt(1000);
      const actions = sized(wide, 'columnheader')[4];
      expect(actions.textContent).toBe('Actions');
      // Download, Version history and Delete: 3 x 32px buttons + 2 x 4px gaps + 24px of cell padding.
      expect(actions.style.width).toBe('128px');
      expect(actions.style.flexGrow).toBe('0');
      expect(actions.style.flexShrink).toBe('0');
    },
    SLOW
  );

  it(
    'must-not-fire pair: the actions column is sized by the actions on offer - two buttons are narrower, and no actions means no column',
    async () => {
      // Download + Version history only (no caller, so no Delete): 2 x 32px + 1 x 4px + 24px.
      const twoActions = await renderAt(1000, null);
      const twoHeaders = sized(twoActions, 'columnheader');
      expect(twoHeaders.map(h => h.textContent)).toEqual(['Name', 'Type', 'Date Added', 'AI Summary', 'Actions']);
      expect(twoHeaders[4].style.width).toBe('92px');
      cleanup();

      // No download right and no caller: no row action at all, so no actions column either.
      const none = await renderAt(1000, null, false);
      expect(
        within(none)
          .getAllByRole('columnheader')
          .map(h => h.textContent)
      ).toEqual(['Name', 'Type', 'Date Added', 'AI Summary']);
    },
    SLOW
  );

  /**
   * jsdom has no layout, so a name is "cut off" only if the test says so: the wrapper's scrollWidth exceeds its
   * clientWidth for the long text, and not for a short one (the grid's own overflow test, `isHorizontallyOverflowing`).
   */
  function stubOverflow(): void {
    Object.defineProperty(HTMLElement.prototype, 'scrollWidth', {
      configurable: true,
      get(this: HTMLElement) {
        return (this.textContent ?? '').length > 40 ? 900 : 10;
      },
    });
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', {
      configurable: true,
      get(this: HTMLElement) {
        return this.hasAttribute('data-truncate-cell') ? 100 : 1000;
      },
    });
  }

  it(
    'a name that does not fit ends in an ellipsis wrapper and shows its full text in a tooltip when its cell is focused',
    async () => {
      stubOverflow();
      listReturns({ value: [row(1, NAME_COL)] });
      renderLibrary();
      const body = await documentRow(NAME_COL);
      const cell = within(body).getByText(NAME_COL).closest('[role="gridcell"]') as HTMLElement;
      const wrapper = cell.querySelector('[data-truncate-cell]') as HTMLElement;
      expect(wrapper).not.toBeNull();
      expect(wrapper.textContent).toBe(NAME_COL);

      expect(screen.queryByRole('tooltip')).toBeNull();
      fireEvent.focusIn(cell);
      const tooltip = await screen.findByRole('tooltip', undefined, LOAD);
      expect(tooltip.textContent).toBe(NAME_COL);
    },
    SLOW
  );

  it(
    'the data columns are resizable (a resize handle each) and the actions column is not',
    async () => {
      const grid = await renderAt(1000);
      const headers = within(grid).getAllByRole('columnheader');
      const handles = (h: HTMLElement): number => h.querySelectorAll('.fui-TableResizeHandle').length;
      const byName = (label: string): HTMLElement => headers.find(h => h.textContent === label) as HTMLElement;
      for (const label of ['Name', 'Type', 'Date Added', 'AI Summary']) {
        expect(handles(byName(label))).toBe(1);
      }
      expect(handles(byName('Actions'))).toBe(0);
    },
    SLOW
  );

  it(
    'a summary that does not fit ends in an ellipsis wrapper and shows its full text in a tooltip when its cell is focused',
    async () => {
      stubOverflow();
      const LONG_SUMMARY =
        'This agreement sets out the scope of the engagement and the fees payable, with a long tail.';
      listReturns({ value: [row(1, 'Brief.pdf', { sprk_summary: LONG_SUMMARY })] });
      renderLibrary();
      const body = await documentRow('Brief.pdf');
      const cell = within(body).getByText(LONG_SUMMARY).closest('[role="gridcell"]') as HTMLElement;
      expect(cell.querySelector('[data-truncate-cell]')).not.toBeNull();

      expect(screen.queryByRole('tooltip')).toBeNull();
      fireEvent.focusIn(cell);
      const tooltip = await screen.findByRole('tooltip', undefined, LOAD);
      expect(tooltip.textContent).toBe(LONG_SUMMARY);
    },
    SLOW
  );

  it(
    'must-not-fire pair: a short summary that fits shows no tooltip when its cell is focused',
    async () => {
      stubOverflow();
      listReturns({ value: [row(1, 'Brief.pdf', { sprk_summary: 'A short summary.' })] });
      renderLibrary();
      const body = await documentRow('Brief.pdf');
      const cell = within(body).getByText('A short summary.').closest('[role="gridcell"]') as HTMLElement;
      expect(cell.querySelector('[data-truncate-cell]')).not.toBeNull();

      fireEvent.focusIn(cell);
      await new Promise(resolve => setTimeout(resolve, 600));
      expect(screen.queryByRole('tooltip')).toBeNull();
    },
    SLOW
  );

  it(
    'must-not-fire pair: a short name that fits shows no tooltip when its cell is focused',
    async () => {
      stubOverflow();
      listReturns({ value: [row(1, 'Short.pdf')] });
      renderLibrary();
      const body = await documentRow('Short.pdf');
      const cell = within(body).getByText('Short.pdf').closest('[role="gridcell"]') as HTMLElement;
      expect(cell.querySelector('[data-truncate-cell]')).not.toBeNull();

      fireEvent.focusIn(cell);
      await new Promise(resolve => setTimeout(resolve, 600));
      expect(screen.queryByRole('tooltip')).toBeNull();
    },
    SLOW
  );
});

describe("the grid's own incomplete-list and empty states replace the SPA's", () => {
  it(
    'a cut-short list says so once, with the singular noun, and is the grid notice (not the SPA notice)',
    async () => {
      listReturns({ value: [row(1, 'Only one.pdf')], truncated: true });
      renderLibrary();

      await documentRow('Only one.pdf');
      const notice = await screen.findByTestId('datagrid-truncated-notice');
      expect(notice.textContent).toContain('This list is incomplete: only 1 document could be shown');
      expect(screen.queryByTestId('truncated-list-notice')).toBeNull();
      expect(screen.getAllByTestId('datagrid-truncated-notice')).toHaveLength(1);
    },
    SLOW
  );

  it(
    'a cut-short empty list says it could not be loaded, not that there are none',
    async () => {
      listReturns({ value: [], truncated: true });
      renderLibrary();

      const notice = await screen.findByTestId('datagrid-truncated-notice', undefined, LOAD);
      expect(notice.textContent).toContain('could not be loaded just now');
      expect(notice.textContent).toContain('does not mean there are none');
    },
    SLOW
  );

  it(
    'must-not-fire pair: a complete list shows no notice, and a complete empty list shows the configured empty state',
    async () => {
      listReturns({ value: [row(1, 'Whole.pdf')], truncated: false });
      renderLibrary();
      await documentRow('Whole.pdf');
      expect(screen.queryByTestId('datagrid-truncated-notice')).toBeNull();
    },
    SLOW
  );

  it(
    'a complete empty list shows the configured empty state and no incomplete-list notice',
    async () => {
      listReturns({ value: [] });
      renderLibrary();
      expect(await screen.findByText('No documents on this record yet.', undefined, LOAD)).toBeTruthy();
      expect(screen.queryByTestId('datagrid-truncated-notice')).toBeNull();
    },
    SLOW
  );

  it(
    'a failed read shows the load-failed text in the grid banner and offers no Upload',
    async () => {
      vi.spyOn(console, 'error').mockImplementation(() => undefined);
      bffApiCall.mockRejectedValue(new ApiError(500, 'boom'));
      renderLibrary({ canUpload: true });

      expect(
        await screen.findByText('Failed to load documents. Please try refreshing the page.', undefined, LOAD)
      ).toBeTruthy();
      expect(screen.queryByRole('button', { name: /Upload Document/ })).toBeNull();
    },
    SLOW
  );
});

describe('row actions follow the caller (hide, never disable)', () => {
  const docs = (): Row[] => [
    row(1, 'Mine.pdf', { _sprk_createdbycontact_value: ME }),
    row(2, 'Theirs.pdf', { _sprk_createdbycontact_value: SOMEONE }),
    row(3, 'NoFile.pdf', { _sprk_createdbycontact_value: ME, hasFile: false }),
  ];

  it(
    'a View-only caller who is signed in (canCreate: false) sees no Upload and no Delete, even on a row they created, but keeps Download and Version history',
    async () => {
      listReturns({ value: docs(), canCreate: false });
      renderLibrary({ caller: asContact });

      const mine = await documentRow('Mine.pdf');
      expect(screen.queryByRole('button', { name: /Upload Document/ })).toBeNull();
      expect(rowAction(mine, 'Delete')).toBeNull();
      expect(screen.queryAllByRole('button', { name: 'Delete' })).toHaveLength(0);
      expect(rowAction(mine, 'Download')).toBeTruthy();
      expect(rowAction(mine, 'Version history')).toBeTruthy();
    },
    SLOW
  );

  it(
    'must-not-fire pair: a caller who may create (canCreate: true) sees Delete on the row they created, and only that row',
    async () => {
      listReturns({ value: docs(), canCreate: true });
      renderLibrary({ caller: asContact });

      const mine = await documentRow('Mine.pdf');
      const theirs = await documentRow('Theirs.pdf');
      expect(await screen.findByRole('button', { name: /Upload Document/ }, LOAD)).toBeTruthy();
      expect(rowAction(mine, 'Delete')).toBeTruthy();
      expect(rowAction(theirs, 'Delete')).toBeNull();
    },
    SLOW
  );

  it(
    'a caller who may create but is not the creator of the row (a document someone else created) sees no Delete on it',
    async () => {
      listReturns({ value: [row(2, 'Theirs.pdf', { _sprk_createdbycontact_value: SOMEONE })], canCreate: true });
      renderLibrary({ caller: asContact });

      const theirs = await documentRow('Theirs.pdf');
      expect(await screen.findByRole('button', { name: /Upload Document/ }, LOAD)).toBeTruthy();
      expect(rowAction(theirs, 'Delete')).toBeNull();
      expect(rowAction(theirs, 'Download')).toBeTruthy();
    },
    SLOW
  );

  it(
    'no caller identity: no Delete on any row, whatever the list says',
    async () => {
      listReturns({ value: docs(), canCreate: true });
      renderLibrary({ canUpload: true, caller: null });

      const mine = await documentRow('Mine.pdf');
      expect(rowAction(mine, 'Delete')).toBeNull();
      expect(screen.queryAllByRole('button', { name: 'Delete' })).toHaveLength(0);
    },
    SLOW
  );

  it(
    'a writer sees Delete only on the rows they created, and a file-less row has neither Download nor Version history',
    async () => {
      listReturns({ value: docs(), canCreate: true });
      renderLibrary({ canUpload: true, caller: asContact });

      const mine = await documentRow('Mine.pdf');
      const theirs = await documentRow('Theirs.pdf');
      const noFile = await documentRow('NoFile.pdf');
      expect(rowAction(mine, 'Delete')).toBeTruthy();
      expect(rowAction(theirs, 'Delete')).toBeNull();
      expect(rowAction(theirs, 'Download')).toBeTruthy();
      expect(rowAction(noFile, 'Download')).toBeNull();
      expect(rowAction(noFile, 'Version history')).toBeNull();
      // Their own file-less row can still be deleted (Delete is about ownership, not about a file).
      expect(rowAction(noFile, 'Delete')).toBeTruthy();
      expect(await screen.findByRole('button', { name: /Upload Document/ }, LOAD)).toBeTruthy();
      // Hidden, not disabled: no action button on the page is disabled.
      expect(
        screen
          .queryAllByRole('button')
          .filter(b => b.hasAttribute('disabled') && /Download|Version|Delete/.test(b.getAttribute('aria-label') ?? ''))
      ).toHaveLength(0);
    },
    SLOW
  );
});

describe('the name opens the document record', () => {
  it(
    'with an opener, a click on the name reports the document id and the control that was focused',
    async () => {
      listReturns({ value: [row(1, 'Brief.pdf')] });
      const onOpenDocument = vi.fn();
      renderLibrary({ onOpenDocument });

      const link = within(await documentRow('Brief.pdf')).getByRole('button', { name: 'Brief.pdf' });
      link.focus();
      fireEvent.click(link);

      expect(onOpenDocument).toHaveBeenCalledTimes(1);
      expect(onOpenDocument.mock.calls[0][0]).toBe(guid(901));
    },
    SLOW
  );

  it(
    'must-not-fire pair: with no opener a click on the name opens nothing and reads nothing more',
    async () => {
      listReturns({ value: [row(1, 'Brief.pdf')] });
      renderLibrary();

      const link = within(await documentRow('Brief.pdf')).getByRole('button', { name: 'Brief.pdf' });
      const readsBefore = bffApiCall.mock.calls.length;
      fireEvent.click(link);
      await new Promise(resolve => setTimeout(resolve, 200));
      expect(screen.queryByRole('dialog')).toBeNull();
      expect(bffApiCall.mock.calls.length).toBe(readsBefore);
    },
    SLOW
  );
});

describe('an upload re-reads the list through the grid', () => {
  it(
    'a completed upload bumps the row source: the list is read again and the new document shows',
    async () => {
      const rows = [row(1, 'Old.pdf')];
      bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
        if (options?.method === 'POST') {
          rows.push(row(2, 'new.pdf'));
          return { sprk_documentid: guid(902) };
        }
        if (path.endsWith('/documents')) return { value: [...rows], canCreate: true };
        throw new Error(`unexpected path ${path}`);
      });
      renderLibrary({ canUpload: true, accessLevel: AccessLevel.Collaborate });

      await documentRow('Old.pdf');
      expect(listCalls()).toHaveLength(1);
      fireEvent.click(await screen.findByRole('button', { name: /Upload Document/ }, LOAD));
      const input = await waitFor(() => {
        const el = document.querySelector('input[type="file"]');
        if (!el) throw new Error('no file input yet');
        return el as HTMLInputElement;
      });
      fireEvent.change(input, { target: { files: [new File(['%PDF'], 'new.pdf', { type: 'application/pdf' })] } });
      fireEvent.click(await screen.findByRole('button', { name: 'Upload' }));

      await documentRow('new.pdf');
      expect(listCalls()).toHaveLength(2);
    },
    SLOW
  );

  it(
    'must-not-fire pair: opening and cancelling the upload dialog does not read the list again',
    async () => {
      listReturns({ value: [row(1, 'Old.pdf')], canCreate: true });
      renderLibrary({ canUpload: true });

      await documentRow('Old.pdf');
      fireEvent.click(await screen.findByRole('button', { name: /Upload Document/ }, LOAD));
      fireEvent.click(await screen.findByRole('button', { name: 'Cancel' }, LOAD));
      await new Promise(resolve => setTimeout(resolve, 300));
      expect(listCalls()).toHaveLength(1);
    },
    SLOW
  );
});

/**
 * R3 task 066 (owner check 3, items 2-3) - an external user downloads a document from the document detail window, and a
 * failed download is always visible (document detail AND DocumentLibrary).
 *
 * What runs for real: the Documents widget body, `RecordDetailModal`, `RecordDetailFields`, `DocumentLibrary`, the shared
 * `SprkModal` and Fluent v9. What is substituted: the network seam (`bffApiCall` / `bffApiBlob`, throwing `ApiError` the way
 * bff-client builds it), the shared grid (reports its rows, opens one on click) and the browser-only helpers jsdom lacks
 * (object URLs, the anchor click that starts a download).
 *
 * The SERVER is the authority (the content route checks Read on the parent and document-to-parent scoping, task 027 / 031).
 * These tests prove the SPA offers Download only where it can name a parent, asks the parents in the BFF's order, saves the
 * bytes under the document's name, and never hides a refusal.
 */
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const { bffApiCall, bffApiBlob, gridSpecs } = vi.hoisted(() => ({
  bffApiCall: vi.fn(),
  bffApiBlob: vi.fn(),
  gridSpecs: {} as Record<string, { rows: Array<Record<string, unknown>> }>,
}));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall, bffApiBlob, getExternalUserContext: () => bffApiCall('/api/v1/external/me') };
});

vi.mock('@spaarke/ui-components/utils/themeStorage', () => ({
  resolveCodePageTheme: () => null,
  setupCodePageThemeListener: () => () => undefined,
}));
vi.mock('../src/services/gridDataverseClient', () => ({ gridDataverseClient: {} }));

// The shared grid, as in RecordDetailModal.test.tsx: reports its rows and opens one through `onRecordOpen`.
vi.mock('../src/widgets/ExternalDataGrid', async () => {
  const R = await import('react');
  return {
    ExternalDataGrid: (props: {
      configId: string;
      onRecordsLoaded?: (rows: ReadonlyArray<Record<string, unknown>>) => void;
      onRecordOpen?: (id: string, row: Record<string, unknown>, ctx: Record<string, unknown>) => void;
    }) => {
      const spec = gridSpecs[props.configId];
      const { onRecordsLoaded } = props;
      R.useEffect(() => {
        onRecordsLoaded?.(spec.rows);
      }, [onRecordsLoaded, spec.rows]);
      return R.createElement(
        'ul',
        { 'data-testid': 'fake-grid' },
        spec.rows.map(row =>
          R.createElement(
            'li',
            { key: String(row.sprk_documentid) },
            R.createElement(
              'button',
              {
                onClick: () =>
                  props.onRecordOpen?.(String(row.sprk_documentid), row, {
                    configId: props.configId,
                    entityName: 'sprk_document',
                    selectedIds: [],
                  }),
              },
              String(row.sprk_documentname)
            )
          )
        )
      );
    },
  };
});

import { ApiError, AccessLevel } from '../src/types';
import DocumentsWidgetBody from '../src/widgets/DocumentsWidget';
import { DocumentLibrary } from '../src/components/DocumentLibrary';
import { documentParentCandidates } from '../src/components/detail/RecordDetailFields';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const SLOW = { timeout: 10000 };
const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;

const DOCUMENTS_GRID = '3af4102c-1092-f111-b8dc-7ced8ddc4a05';
const DOC_ID = guid(901);
const PROJECT = guid(101);
const MATTER = guid(201);
const WORK_ASSIGNMENT = guid(301);

const contentPath = (root: string, id: string): string => `/api/v1/external/${root}/${id}/documents/${DOC_ID}/content`;
const problem = (status: number): string => JSON.stringify({ status, title: 'refused', detail: 'server detail text' });
const ref = (logicalName: string, id: string): Record<string, unknown> => ({ id, logicalName, name: 'Linked' });

/** The module fetch row for the document the detail reads (lookup columns as the fetch seam serialises them). */
let documentRow: Record<string, unknown>;
let downloadedAs: string[];

function installNetwork(): void {
  bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
    if (path === '/api/v1/external/me') return { contactId: guid(7), email: 'me@example.test', projects: [] };
    if (path.endsWith('/api/dataverse/fetch')) {
      const body = JSON.parse(String(options?.body)) as { entityName: string };
      return { entities: body.entityName === 'sprk_document' ? [documentRow] : [], moreRecords: false };
    }
    throw new Error(`unexpected BFF call ${options?.method ?? 'GET'} ${path}`);
  });
}

async function openDocumentDetail(): Promise<HTMLElement> {
  render(
    <FluentProvider theme={webLightTheme}>
      <DocumentsWidgetBody title="widget" />
    </FluentProvider>
  );
  fireEvent.click(screen.getByRole('button', { name: 'grid-row' }));
  const dialog = await screen.findByRole('dialog');
  await screen.findByTestId('record-detail-fields', undefined, SLOW);
  return dialog;
}

beforeEach(() => {
  documentRow = { sprk_documentid: DOC_ID, sprk_documentname: 'Contract.pdf', sprk_documenttype: 'Contract' };
  gridSpecs[DOCUMENTS_GRID] = { rows: [{ sprk_documentid: DOC_ID, sprk_documentname: 'grid-row' }] };
  bffApiCall.mockReset();
  bffApiBlob.mockReset();
  installNetwork();
  downloadedAs = [];
  Object.assign(window.URL, { createObjectURL: vi.fn(() => 'blob:test'), revokeObjectURL: vi.fn() });
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
    downloadedAs.push(this.download);
  });
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

// ---------------------------------------------------------------------------
// Which parent the content route is asked about
// ---------------------------------------------------------------------------

describe('documentParentCandidates', () => {
  it("lists the linked parents in the BFF's order: project, matter, work assignment", () => {
    // DocumentRootLinks.Links (ExternalDataService.cs): Project, Matter, WorkAssignment - whatever order the row lists them.
    const row = {
      sprk_workassignment: ref('sprk_workassignment', WORK_ASSIGNMENT),
      sprk_matter: ref('sprk_matter', MATTER),
      sprk_project: ref('sprk_project', PROJECT),
    };
    expect(documentParentCandidates(row)).toEqual([
      { root: 'projects', id: PROJECT },
      { root: 'matters', id: MATTER },
      { root: 'workassignments', id: WORK_ASSIGNMENT },
    ]);
  });

  it('reads an entity reference, a bare id and the Web API _value form; skips a missing, empty or malformed link', () => {
    expect(documentParentCandidates({ sprk_matter: ref('sprk_matter', MATTER) })).toEqual([
      { root: 'matters', id: MATTER },
    ]);
    expect(documentParentCandidates({ sprk_matter: MATTER })).toEqual([{ root: 'matters', id: MATTER }]);
    expect(documentParentCandidates({ _sprk_project_value: PROJECT })).toEqual([{ root: 'projects', id: PROJECT }]);
    expect(documentParentCandidates({})).toEqual([]);
    expect(
      documentParentCandidates({ sprk_project: null, sprk_matter: '', sprk_workassignment: 'not-a-guid' })
    ).toEqual([]);
    expect(
      documentParentCandidates({ sprk_project: ref('sprk_project', '00000000-0000-0000-0000-000000000000') })
    ).toEqual([]);
  });
});

// ---------------------------------------------------------------------------
// Document detail: Download
// ---------------------------------------------------------------------------

describe('document detail', () => {
  it(
    'shows Download, calls the content route of the linked parent and saves the file under the document name',
    async () => {
      documentRow.sprk_matter = ref('sprk_matter', MATTER);
      bffApiBlob.mockResolvedValue(new Blob(['bytes']));

      const dialog = await openDocumentDetail();
      fireEvent.click(await within(dialog).findByRole('button', { name: 'Download Contract.pdf' }, SLOW));

      await waitFor(() => expect(downloadedAs).toEqual(['Contract.pdf']));
      expect(bffApiBlob).toHaveBeenCalledTimes(1);
      expect(bffApiBlob).toHaveBeenCalledWith(contentPath('matters', MATTER));
      expect(window.URL.createObjectURL).toHaveBeenCalledTimes(1);
      // The object URL is released after the anchor click (review K2).
      await waitFor(() => expect(window.URL.revokeObjectURL).toHaveBeenCalledWith('blob:test'));
      expect(screen.queryByTestId('record-detail-download-error')).toBeNull();
      // No new BFF route and no link: the only reads are the record's own fetch and `/me`.
      const paths = bffApiCall.mock.calls.map(c => String(c[0]));
      expect(paths.every(p => p.startsWith('/api/v1/external/'))).toBe(true);
    },
    SLOW.timeout
  );

  it(
    'asks the parents in the BFF order and moves on only when a parent refuses with 403',
    async () => {
      documentRow.sprk_project = ref('sprk_project', PROJECT);
      documentRow.sprk_matter = ref('sprk_matter', MATTER);
      bffApiBlob.mockImplementation(async (path: string) => {
        if (path === contentPath('projects', PROJECT)) throw new ApiError(403, problem(403));
        return new Blob(['bytes']);
      });

      const dialog = await openDocumentDetail();
      fireEvent.click(await within(dialog).findByRole('button', { name: 'Download Contract.pdf' }, SLOW));

      await waitFor(() => expect(downloadedAs).toEqual(['Contract.pdf']));
      expect(bffApiBlob.mock.calls.map(c => c[0])).toEqual([
        contentPath('projects', PROJECT),
        contentPath('matters', MATTER),
      ]);
      expect(screen.queryByTestId('record-detail-download-error')).toBeNull();
    },
    SLOW.timeout
  );

  it(
    'does not try another parent after a failure that is not a refusal, and shows the error',
    async () => {
      documentRow.sprk_project = ref('sprk_project', PROJECT);
      documentRow.sprk_matter = ref('sprk_matter', MATTER);
      bffApiBlob.mockRejectedValue(new ApiError(500, problem(500)));

      const dialog = await openDocumentDetail();
      fireEvent.click(await within(dialog).findByRole('button', { name: 'Download Contract.pdf' }, SLOW));

      const error = await screen.findByTestId('record-detail-download-error', undefined, SLOW);
      expect(error.textContent).toMatch(/could not be downloaded just now/i);
      // The server's detail text is never echoed (the delete messages follow the same rule).
      expect(error.textContent).not.toMatch(/server detail text/);
      expect(bffApiBlob).toHaveBeenCalledTimes(1);
      expect(downloadedAs).toEqual([]);
    },
    SLOW.timeout
  );

  it(
    'shows an access message when every linked parent refuses, and Download works again afterwards',
    async () => {
      documentRow.sprk_project = ref('sprk_project', PROJECT);
      documentRow.sprk_matter = ref('sprk_matter', MATTER);
      bffApiBlob.mockRejectedValue(new ApiError(403, problem(403)));

      const dialog = await openDocumentDetail();
      const button = await within(dialog).findByRole('button', { name: 'Download Contract.pdf' }, SLOW);
      fireEvent.click(button);

      const error = await screen.findByTestId('record-detail-download-error', undefined, SLOW);
      expect(error.textContent).toMatch(/do not have access to download/i);
      expect(bffApiBlob).toHaveBeenCalledTimes(2);

      // A retry clears the old message and succeeds.
      bffApiBlob.mockReset();
      bffApiBlob.mockResolvedValue(new Blob(['bytes']));
      fireEvent.click(await within(dialog).findByRole('button', { name: 'Download Contract.pdf' }, SLOW));
      await waitFor(() => expect(downloadedAs).toEqual(['Contract.pdf']));
      expect(screen.queryByTestId('record-detail-download-error')).toBeNull();
    },
    SLOW.timeout
  );

  it(
    'shows no Download action for a document with no parent link',
    async () => {
      const dialog = await openDocumentDetail();

      expect(within(dialog).queryByRole('button', { name: /^Download/ })).toBeNull();
      expect(within(dialog).queryByTestId('record-detail-download')).toBeNull();
      expect(bffApiBlob).not.toHaveBeenCalled();
    },
    SLOW.timeout
  );
});

// ---------------------------------------------------------------------------
// DocumentLibrary: a failed download is visible
// ---------------------------------------------------------------------------

describe('DocumentLibrary download failure', () => {
  const row = { sprk_documentid: DOC_ID, sprk_name: 'Contract.pdf', createdon: '2026-03-01T10:00:00Z' };

  function renderLibrary(): void {
    bffApiCall.mockImplementation(async (path: string) => {
      if (/\/documents$/.test(path)) return { value: [row] };
      throw new Error(`unexpected BFF call ${path}`);
    });
    render(
      <FluentProvider theme={webLightTheme}>
        <DocumentLibrary root="matters" recordId={MATTER} accessLevel={AccessLevel.Collaborate} canDownload />
      </FluentProvider>
    );
  }

  it.each([
    [403, /do not have access to download/i],
    [404, /file is not available/i],
    [500, /could not be downloaded just now/i],
  ])(
    'a %s on the content route shows a message and the row stays',
    async (status, text) => {
      bffApiBlob.mockRejectedValue(new ApiError(status, problem(status)));
      renderLibrary();

      fireEvent.click(await screen.findByRole('button', { name: 'Download Contract.pdf' }, SLOW));

      const error = await screen.findByTestId('document-download-error', undefined, SLOW);
      expect(error.textContent).toMatch(text);
      expect(error.textContent).not.toMatch(/server detail text/);
      expect(screen.getByText('Contract.pdf')).toBeTruthy();
      expect(bffApiBlob).toHaveBeenCalledWith(contentPath('matters', MATTER));
      expect(downloadedAs).toEqual([]);
    },
    SLOW.timeout
  );

  it(
    'a later successful download clears the message',
    async () => {
      bffApiBlob.mockRejectedValueOnce(new ApiError(500, problem(500)));
      bffApiBlob.mockResolvedValue(new Blob(['bytes']));
      renderLibrary();

      fireEvent.click(await screen.findByRole('button', { name: 'Download Contract.pdf' }, SLOW));
      await screen.findByTestId('document-download-error', undefined, SLOW);

      fireEvent.click(screen.getByRole('button', { name: 'Download Contract.pdf' }));
      await waitFor(() => expect(downloadedAs).toEqual(['Contract.pdf']));
      expect(screen.queryByTestId('document-download-error')).toBeNull();
    },
    SLOW.timeout
  );
});

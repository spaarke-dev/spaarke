/**
 * R3 ISS-011 (#1886, owner decision 2026-10-14) - the document detail window (opened from the Documents widget) offers
 * Download only for a document that has a file. The BFF derives `hasFile` on the documents-module row; `false` hides the
 * File section (the document stays shown, with its fields), and a row without the member (an older BFF) keeps Download.
 *
 * Same harness as DocumentDownload.test.tsx: the real widget body, RecordDetailModal, RecordDetailFields and Fluent v9; only
 * the network seam, the shared grid and the browser-only helpers are substituted.
 */
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
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

import DocumentsWidgetBody from '../src/widgets/DocumentsWidget';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const SLOW = { timeout: 10000 };
const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;

const DOCUMENTS_GRID = '3af4102c-1092-f111-b8dc-7ced8ddc4a05';
const DOC_ID = guid(901);
const MATTER = guid(201);

const ref = (logicalName: string, id: string): Record<string, unknown> => ({ id, logicalName, name: 'Linked' });

/** The module fetch row for the document the detail reads (lookup columns as the fetch seam serialises them). */
let documentRow: Record<string, unknown>;

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
  Object.assign(window.URL, { createObjectURL: vi.fn(() => 'blob:test'), revokeObjectURL: vi.fn() });
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

// ---------------------------------------------------------------------------
// Document detail: Download by hasFile
// ---------------------------------------------------------------------------

describe('document detail Download by hasFile', () => {
  it(
    'a hasFile:false document shows its fields but no Download',
    async () => {
      documentRow.sprk_matter = ref('sprk_matter', MATTER);
      documentRow.hasFile = false;

      const dialog = await openDocumentDetail();

      expect(within(dialog).getByTestId('record-detail-fields')).toBeTruthy();
      expect(within(dialog).queryByRole('button', { name: /^Download/ })).toBeNull();
      expect(within(dialog).queryByTestId('record-detail-download')).toBeNull();
      expect(bffApiBlob).not.toHaveBeenCalled();
    },
    SLOW.timeout
  );

  it(
    'a hasFile:true document shows Download',
    async () => {
      documentRow.sprk_matter = ref('sprk_matter', MATTER);
      documentRow.hasFile = true;

      const dialog = await openDocumentDetail();

      expect(await within(dialog).findByRole('button', { name: 'Download Contract.pdf' }, SLOW)).toBeTruthy();
    },
    SLOW.timeout
  );

  it(
    'a document row with no hasFile member (an older BFF) shows Download',
    async () => {
      documentRow.sprk_matter = ref('sprk_matter', MATTER);

      const dialog = await openDocumentDetail();

      expect(await within(dialog).findByRole('button', { name: 'Download Contract.pdf' }, SLOW)).toBeTruthy();
    },
    SLOW.timeout
  );
});

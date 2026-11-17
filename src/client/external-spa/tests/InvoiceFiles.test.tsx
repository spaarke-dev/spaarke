/**
 * R4 task 021 (spec FR-13; owner retest 2026-11-14 "I don't see the actual invoice document/file", and the big Upload button).
 *
 * The Invoice record's Files section is READ-ONLY and runs on ebilling's routes (task 093, read at the PR #2839 head):
 *   GET /api/v1/external/invoices/{id}/files                     -> { value: [{ id, fileName, contentType, sizeBytes, createdOn, scanStatus }] }
 *   GET /api/v1/external/invoices/{id}/files/{fileId}/content    -> the bytes (no Content-Disposition name; 409 file_scan_pending)
 * `scanStatus` is Clean | Checking | NotScanned; a malicious file is omitted by the server, so there is NO failed state.
 *
 * What runs for real: `InvoiceFilesList`, `FilesSection`, `RecordTemplateModal` (the Invoice template), the real Messages
 * section, `DocumentLibrary`'s shared helpers, SprkModal and Fluent v9. What is substituted: the network seam (`bffApiCall` /
 * `bffApiBlob`, throwing `ApiError` as bff-client builds it) and the browser-only download helpers jsdom lacks.
 *
 * Every positive case fails on the old code: the Invoice's Files section was `DocumentLibrary` on the `invoices` documents
 * root (list, Upload, versions, delete-own), `InvoiceFilesList` did not exist and nothing read `/files`.
 */
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const h = vi.hoisted(() => ({ bffApiCall: vi.fn(), bffApiBlob: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return {
    ...actual,
    bffApiCall: h.bffApiCall,
    bffApiBlob: h.bffApiBlob,
    getExternalUserContext: () => h.bffApiCall('/api/v1/external/me'),
  };
});

import { ApiError } from '../src/types';
import { InvoiceFilesList } from '../src/components/record/sections/InvoiceFilesList';
import { RECORD_TEMPLATES, recordActionIdsOf } from '../src/components/record/recordTemplates';
import { RecordTemplateModal } from '../src/components/record/RecordTemplateModal';
import { messageRootFor } from '../src/api/messages-client';
import { invoiceFileSaveName, type InvoiceFileItem } from '../src/api/web-api-client';
import { installLayout, uninstallLayout } from './jsdomLayout';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const SLOW = { timeout: 60000 };
const WAIT = { timeout: 12000 };
const INVOICE = guid(501);
const FILE_A = guid(901);
const FILE_B = guid(902);

const filesPath = `/api/v1/external/invoices/${INVOICE}/files`;
const contentPath = (fileId: string): string => `${filesPath}/${fileId}/content`;
const problem = (status: number, extra: Record<string, unknown> = {}): string =>
  JSON.stringify({ status, title: 'refused', detail: 'server detail text that must not be shown', ...extra });

function file(over: Partial<InvoiceFileItem> = {}): InvoiceFileItem {
  return {
    id: FILE_A,
    fileName: 'Invoice 501.pdf',
    contentType: 'application/pdf',
    sizeBytes: 2048,
    createdOn: '2026-03-01T12:00:00Z',
    scanStatus: 'Clean',
    ...over,
  };
}

let downloadedAs: string[];

beforeEach(() => {
  h.bffApiCall.mockReset();
  h.bffApiBlob.mockReset();
  downloadedAs = [];
  Object.assign(window.URL, { createObjectURL: vi.fn(() => 'blob:test'), revokeObjectURL: vi.fn() });
  // The anchor click that starts a browser download: record the name it was given.
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
    downloadedAs.push(this.download);
  });
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

const renderList = (): ReturnType<typeof render> =>
  render(
    <FluentProvider theme={webLightTheme}>
      <InvoiceFilesList invoiceId={INVOICE} />
    </FluentProvider>
  );

/** The files route answers `files`; the list is read once per mount (and once per Retry). */
const answerFiles = (files: InvoiceFileItem[]): void => {
  h.bffApiCall.mockImplementation(async (p: string) => {
    if (p === filesPath) return { value: files };
    throw new Error(`unexpected BFF call ${p}`);
  });
};

// ---------------------------------------------------------------------------
// The three row states
// ---------------------------------------------------------------------------

describe('Invoice files: row states', () => {
  it('reads the list from GET /invoices/{id}/files and shows name, size and date', async () => {
    answerFiles([file()]);
    renderList();

    const row = await screen.findByTestId('invoice-file-row');
    expect(within(row).getByText('Invoice 501.pdf')).toBeTruthy();
    expect(within(row).getByTestId('invoice-file-size').textContent).toBe('2.0 KB');
    expect(within(row).getByTestId('invoice-file-date').textContent).toBe('Mar 1, 2026');
    expect(h.bffApiCall.mock.calls.map(c => String(c[0]))).toEqual([filesPath]);
  });

  it('Clean: Download reads the content route and saves the bytes under the list fileName', async () => {
    answerFiles([file({ scanStatus: 'Clean', fileName: 'Acme invoice March.pdf' })]);
    h.bffApiBlob.mockResolvedValue(new Blob(['%PDF']));
    renderList();

    fireEvent.click(await screen.findByRole('button', { name: 'Download Acme invoice March.pdf' }));

    await waitFor(() => expect(downloadedAs).toEqual(['Acme invoice March.pdf']));
    expect(h.bffApiBlob).toHaveBeenCalledTimes(1);
    expect(h.bffApiBlob).toHaveBeenCalledWith(contentPath(FILE_A));
    expect(window.URL.createObjectURL).toHaveBeenCalledTimes(1);
    expect(screen.queryByText('Checking file…')).toBeNull();
  });

  it('NotScanned: Download is offered, and it downloads', async () => {
    answerFiles([file({ scanStatus: 'NotScanned' })]);
    h.bffApiBlob.mockResolvedValue(new Blob(['%PDF']));
    renderList();

    fireEvent.click(await screen.findByRole('button', { name: 'Download Invoice 501.pdf' }));
    await waitFor(() => expect(downloadedAs).toEqual(['Invoice 501.pdf']));
    expect(h.bffApiBlob).toHaveBeenCalledWith(contentPath(FILE_A));
  });

  it('Checking: shows "Checking file…" and no Download, and reads no content', async () => {
    answerFiles([file({ scanStatus: 'Checking' })]);
    renderList();

    const row = await screen.findByTestId('invoice-file-row');
    expect(within(row).getByText('Checking file…')).toBeTruthy();
    expect(within(row).queryByRole('button')).toBeNull();
    expect(h.bffApiBlob).not.toHaveBeenCalled();
  });

  it('must-not-fire: a scan status the SPA does not know is not served (no Download), never guessed clean', async () => {
    answerFiles([file({ scanStatus: 'Malicious' as unknown as InvoiceFileItem['scanStatus'] })]);
    renderList();

    const row = await screen.findByTestId('invoice-file-row');
    expect(within(row).getByText('Checking file…')).toBeTruthy();
    expect(within(row).queryByRole('button')).toBeNull();
  });

  it('serves each row by its own state: one Clean row keeps Download beside a Checking row', async () => {
    answerFiles([
      file({ id: FILE_A, fileName: 'Done.pdf', scanStatus: 'Clean' }),
      file({ id: FILE_B, fileName: 'Waiting.pdf', scanStatus: 'Checking' }),
    ]);
    renderList();

    const rows = await screen.findAllByTestId('invoice-file-row');
    expect(rows).toHaveLength(2);
    expect(within(rows[0]).getByRole('button', { name: 'Download Done.pdf' })).toBeTruthy();
    expect(within(rows[1]).queryByRole('button')).toBeNull();
    expect(within(rows[1]).getByText('Checking file…')).toBeTruthy();
  });

  it('an empty fileName shows "Invoice file", labels Download with it, and saves as invoice-file.pdf for a PDF', async () => {
    answerFiles([file({ fileName: '', contentType: 'application/pdf' })]);
    h.bffApiBlob.mockResolvedValue(new Blob(['%PDF']));
    renderList();

    const row = await screen.findByTestId('invoice-file-row');
    expect(within(row).getByText('Invoice file')).toBeTruthy();
    fireEvent.click(within(row).getByRole('button', { name: 'Download Invoice file' }));

    await waitFor(() => expect(downloadedAs).toEqual(['invoice-file.pdf']));
  });

  it.each([
    ['', null, 'invoice-file'],
    ['   ', 'image/png', 'invoice-file.png'],
    ['', 'application/octet-stream', 'invoice-file'],
    ['Real.pdf', 'application/pdf', 'Real.pdf'],
  ])('save name for fileName %j and contentType %j is %j', (fileName, contentType, expected) => {
    expect(invoiceFileSaveName({ fileName, contentType })).toBe(expected);
  });

  it('null size and null date render a dash each, not "0 B" or "Invalid Date"', async () => {
    answerFiles([file({ sizeBytes: null, createdOn: null, contentType: null })]);
    renderList();

    const row = await screen.findByTestId('invoice-file-row');
    expect(within(row).getByTestId('invoice-file-size').textContent).toBe('—');
    expect(within(row).getByTestId('invoice-file-date').textContent).toBe('—');
  });
});

// ---------------------------------------------------------------------------
// Content failures
// ---------------------------------------------------------------------------

describe('Invoice files: content failures', () => {
  it('a 409 file_scan_pending (list/content race): the row switches to "Checking file…" with no Download, and says so', async () => {
    answerFiles([file({ scanStatus: 'Clean' })]);
    h.bffApiBlob.mockRejectedValue(new ApiError(409, problem(409, { errorCode: 'file_scan_pending' })));
    renderList();

    fireEvent.click(await screen.findByRole('button', { name: 'Download Invoice 501.pdf' }));

    const row = await screen.findByTestId('invoice-file-row');
    await within(row).findByText('Checking file…');
    expect(within(row).queryByRole('button')).toBeNull();
    expect((await screen.findByTestId('invoice-files-notice')).textContent).toContain('still being checked');
    expect(downloadedAs).toEqual([]);
  });

  it('must-not-fire: a 409 that is not file_scan_pending is an ordinary download failure (the row keeps Download)', async () => {
    answerFiles([file({ scanStatus: 'Clean' })]);
    h.bffApiBlob.mockRejectedValue(new ApiError(409, problem(409, { errorCode: 'something_else' })));
    renderList();

    fireEvent.click(await screen.findByRole('button', { name: 'Download Invoice 501.pdf' }));

    expect((await screen.findByTestId('invoice-files-notice')).textContent).toBe(
      'The document could not be downloaded. Please try again.'
    );
    expect(screen.queryByText('Checking file…')).toBeNull();
    expect(screen.getByRole('button', { name: 'Download Invoice 501.pdf' })).toBeTruthy();
  });

  it.each([
    [404, "This document's file is not available."],
    [403, 'You do not have access to download this document.'],
    [503, 'The document could not be downloaded just now. Please try again later.'],
  ])('a %i on the content route reuses the document download text and keeps Download', async (status, text) => {
    answerFiles([file()]);
    h.bffApiBlob.mockRejectedValue(new ApiError(status, problem(status)));
    renderList();

    fireEvent.click(await screen.findByRole('button', { name: 'Download Invoice 501.pdf' }));

    // By status, never the server's detail text.
    expect((await screen.findByTestId('invoice-files-notice')).textContent).toBe(text);
    expect(screen.getByRole('button', { name: 'Download Invoice 501.pdf' })).toBeTruthy();
    expect(downloadedAs).toEqual([]);
  });
});

// ---------------------------------------------------------------------------
// List states
// ---------------------------------------------------------------------------

describe('Invoice files: list states', () => {
  it('an empty list shows the shared EmptyState', async () => {
    answerFiles([]);
    renderList();

    const empty = await screen.findByRole('status');
    expect(within(empty).getByText('No files')).toBeTruthy();
    expect(within(empty).getByText('No files have been submitted for this invoice.')).toBeTruthy();
    expect(screen.queryByTestId('invoice-file-row')).toBeNull();
  });

  it.each([
    [404, "This invoice's files aren't available."],
    [403, "You don't have access to this invoice's files."],
  ])('a %i says so, shows no files and offers no Retry', async (status, text) => {
    h.bffApiCall.mockRejectedValue(new ApiError(status, problem(status)));
    renderList();

    expect((await screen.findByTestId('invoice-files-error')).textContent).toBe(text);
    expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
    expect(screen.queryByTestId('invoice-file-row')).toBeNull();
    // Not "No files": a refused read is never shown as an empty invoice.
    expect(screen.queryByText('No files')).toBeNull();
  });

  it('must-fire: a 503 ebilling.disabled (the kill switch is off) is one neutral line with NO Retry', async () => {
    h.bffApiCall.mockRejectedValue(new ApiError(503, problem(503, { errorCode: 'ebilling.disabled' })));
    renderList();

    expect((await screen.findByTestId('invoice-files-error')).textContent).toBe("Invoice files aren't available.");
    expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
    expect(screen.queryByText(/couldn't be loaded/)).toBeNull();
  });

  it('must-not-fire: a 503 file_store_unavailable is still an outage with Retry', async () => {
    h.bffApiCall.mockRejectedValue(new ApiError(503, problem(503, { errorCode: 'file_store_unavailable' })));
    renderList();

    expect((await screen.findByTestId('invoice-files-error')).textContent).not.toBe("Invoice files aren't available.");
    expect(screen.getByRole('button', { name: 'Retry' })).toBeTruthy();
  });

  it('a 503 ebilling.disabled on the content route shows the same neutral line, not the generic download error', async () => {
    answerFiles([file()]);
    h.bffApiBlob.mockRejectedValue(new ApiError(503, problem(503, { errorCode: 'ebilling.disabled' })));
    renderList();

    fireEvent.click(await screen.findByRole('button', { name: 'Download Invoice 501.pdf' }));

    expect((await screen.findByTestId('invoice-files-notice')).textContent).toBe("Invoice files aren't available.");
    expect(downloadedAs).toEqual([]);
  });

  it('a 503 on the content route with another code keeps the generic download text', async () => {
    answerFiles([file()]);
    h.bffApiBlob.mockRejectedValue(new ApiError(503, problem(503, { errorCode: 'file_store_unavailable' })));
    renderList();

    fireEvent.click(await screen.findByRole('button', { name: 'Download Invoice 501.pdf' }));

    expect((await screen.findByTestId('invoice-files-notice')).textContent).toBe(
      'The document could not be downloaded just now. Please try again later.'
    );
  });

  it('a 503 is retryable: the message offers Retry, and Retry re-reads the list', async () => {
    let reads = 0;
    h.bffApiCall.mockImplementation(async (p: string) => {
      if (p !== filesPath) throw new Error(`unexpected BFF call ${p}`);
      reads += 1;
      if (reads === 1) throw new ApiError(503, problem(503, { errorCode: 'file_store_unavailable' }));
      return { value: [file()] };
    });
    renderList();

    expect((await screen.findByTestId('invoice-files-error')).textContent).toBe(
      "This invoice's files couldn't be loaded just now. Please try again."
    );
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    expect(await screen.findByRole('button', { name: 'Download Invoice 501.pdf' })).toBeTruthy();
    expect(reads).toBe(2);
    expect(screen.queryByTestId('invoice-files-error')).toBeNull();
  });

  it('any other failure is a generic error with Retry', async () => {
    h.bffApiCall.mockRejectedValue(new ApiError(500, problem(500)));
    renderList();

    expect((await screen.findByTestId('invoice-files-error')).textContent).toBe(
      "This invoice's files couldn't be loaded. Please try again."
    );
    expect(screen.getByRole('button', { name: 'Retry' })).toBeTruthy();
  });

  it('a network failure (no HTTP status) is the generic error with Retry', async () => {
    h.bffApiCall.mockRejectedValue(new TypeError('Failed to fetch'));
    renderList();

    expect(await screen.findByTestId('invoice-files-error')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeTruthy();
  });
});

// ---------------------------------------------------------------------------
// The Invoice record (the real template)
// ---------------------------------------------------------------------------

describe('the Invoice record template', () => {
  const messagePath = `/api/v1/external/invoices/${INVOICE}/messages`;

  function installRecordNet(files: InvoiceFileItem[]): void {
    h.bffApiCall.mockImplementation(async (p: string, options?: RequestInit) => {
      if (p === '/api/v1/external/me') return { contactId: guid(7), email: 'me@example.test', projects: [] };
      if (p.endsWith('/api/dataverse/fetch')) {
        const body = JSON.parse(String(options?.body)) as { entityName: string };
        return body.entityName === 'sprk_invoice'
          ? {
              entities: [
                {
                  sprk_invoiceid: INVOICE,
                  sprk_name: 'Invoice name',
                  sprk_invoicenumber: 'INV-501',
                  statuscode: 1,
                  sprk_totalamount: 100,
                },
              ],
              moreRecords: false,
            }
          : { entities: [], moreRecords: false };
      }
      if (p === filesPath) return { value: files };
      // The thread: the caller may post, so New message is offered (the only create action on an invoice).
      if (p === messagePath) return { value: [], canCreate: true };
      // Any documents read would be the retired invoice path: it answers like a broken route would.
      throw new Error(`unexpected BFF call ${options?.method ?? 'GET'} ${p}`);
    });
  }

  async function openInvoice(): Promise<HTMLElement> {
    installLayout();
    render(
      <FluentProvider theme={webLightTheme}>
        <RecordTemplateModal
          open
          onClose={() => undefined}
          config={RECORD_TEMPLATES.sprk_invoice}
          recordIds={[INVOICE]}
          index={0}
          onIndexChange={() => undefined}
        />
      </FluentProvider>
    );
    const dialog = await screen.findByRole('dialog');
    await within(dialog).findByTestId('record-template-columns', undefined, WAIT);
    return dialog;
  }

  afterEach(() => uninstallLayout());

  it(
    'shows the Files section listing the invoice files from the files route',
    async () => {
      installRecordNet([
        file({ fileName: 'Real invoice.pdf' }),
        file({ id: FILE_B, fileName: 'Pending.pdf', scanStatus: 'Checking' }),
      ]);
      const dialog = await openInvoice();

      const section = await within(dialog).findByTestId('record-section-files', undefined, WAIT);
      await within(section).findByText('Real invoice.pdf', undefined, WAIT);
      expect(within(section).getByText('Pending.pdf')).toBeTruthy();
      expect(within(section).getByText('Checking file…')).toBeTruthy();
      expect(within(section).getByRole('button', { name: 'Download Real invoice.pdf' })).toBeTruthy();
      expect(h.bffApiCall.mock.calls.map(c => String(c[0]))).toContain(filesPath);
    },
    SLOW
  );

  it(
    'offers no Upload, no Version history and no Delete anywhere on the Invoice record, even when every list says the caller may create',
    async () => {
      installRecordNet([file()]);
      const dialog = await openInvoice();
      await within(dialog).findByText('Invoice 501.pdf', undefined, WAIT);
      await within(dialog).findByTestId('record-detail-messages', undefined, WAIT);

      for (const name of [/upload/i, /version history/i, /history/i, /delete/i]) {
        expect(within(dialog).queryByRole('button', { name, hidden: true })).toBeNull();
      }
      expect(within(dialog).queryByText(/Upload Document/i)).toBeNull();
      expect(document.querySelector('input[type="file"]')).toBeNull();
      // The retired documents block is not rendered at all.
      expect(within(dialog).queryByTestId('record-detail-documents')).toBeNull();
    },
    SLOW
  );

  it('the Invoice template lists no upload, history or delete action, and no section action for Files', () => {
    const config = RECORD_TEMPLATES.sprk_invoice;
    const ids = recordActionIdsOf(config);
    expect(ids).not.toContain('upload');
    expect(ids).not.toContain('history');
    expect(ids).not.toContain('delete');
    expect(config.sectionActions?.files).toBeUndefined();
    expect(config.left).toContain('files');
    // Must-fire control for the assertion above: the Documents-bearing templates DO list an upload action.
    expect(recordActionIdsOf(RECORD_TEMPLATES.sprk_matter)).toContain('upload');
  });

  it(
    'makes no call to an invoice documents route, and the invoice message thread still loads',
    async () => {
      installRecordNet([file()]);
      const dialog = await openInvoice();
      await within(dialog).findByText('Invoice 501.pdf', undefined, WAIT);
      await within(dialog).findByTestId('record-detail-messages', undefined, WAIT);
      await waitFor(() => expect(h.bffApiCall.mock.calls.map(c => String(c[0]))).toContain(messagePath));

      const calls = h.bffApiCall.mock.calls.map(c => String(c[0]));
      expect(calls.filter(p => /\/invoices\/[^/]+\/documents/.test(p))).toEqual([]);
      expect(messageRootFor('sprk_invoice')).toBe('invoices');
      // The thread is the invoice's own: New message is offered by its `canCreate`.
      expect(within(dialog).getByRole('button', { name: /new message/i })).toBeTruthy();
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Source scan: nothing under src/ reaches the retired invoice documents path
// ---------------------------------------------------------------------------

/** Patterns of the retired invoice documents path: a built `/invoices/{id}/documents[...]`, or `invoices` as a document root. */
const RETIRED_PATTERNS: ReadonlyArray<{ name: string; re: RegExp }> = [
  { name: 'a built /invoices/{id}/documents path', re: /invoices\/(?:\$\{[^}]*\}|\{[^}]*\}|:[A-Za-z]+)\/documents/ },
  { name: "documentsRoot: 'invoices'", re: /documentsRoot\s*:\s*['"]invoices['"]/ },
  { name: "'invoices' as a DocumentRoot member", re: /type\s+DocumentRoot\s*=[^;]*['"]invoices['"]/ },
  {
    name: "a document call on the 'invoices' root",
    re: /(?:documentsPath|documentContentPath|documentVersionsPath|getRecordDocuments)\(\s*['"]invoices['"]/,
  },
  { name: "the 'invoices' noun of the document library", re: /^\s*invoices\s*:\s*['"]invoice['"]\s*,?\s*$/m },
];

export function retiredInvoiceDocumentsHits(source: string): string[] {
  return RETIRED_PATTERNS.filter(p => p.re.test(source)).map(p => p.name);
}

function sourceFiles(dir: string): string[] {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) return sourceFiles(full);
    return /\.(ts|tsx)$/.test(entry.name) ? [full] : [];
  });
}

describe('source scan: the retired invoice documents path', () => {
  it('must-fire control: the scan recognises each retired shape', () => {
    expect(retiredInvoiceDocumentsHits('const p = `/api/v1/external/invoices/${id}/documents`;')).toHaveLength(1);
    expect(retiredInvoiceDocumentsHits('const p = `${base}/invoices/${id}/documents/${doc}/versions`;')).toHaveLength(
      1
    );
    expect(retiredInvoiceDocumentsHits("export type DocumentRoot = 'projects' | 'invoices';")).toHaveLength(1);
    expect(retiredInvoiceDocumentsHits("  documentsRoot: 'invoices',")).toHaveLength(1);
    expect(retiredInvoiceDocumentsHits("getRecordDocuments('invoices', id)")).toHaveLength(1);
    expect(retiredInvoiceDocumentsHits("const RECORD_NOUN = {\n  invoices: 'invoice',\n};")).toHaveLength(1);
  });

  it('must-not-fire control: the message thread, the files routes and the other roots are not flagged', () => {
    expect(retiredInvoiceDocumentsHits("sprk_invoice: 'invoices',")).toEqual([]);
    expect(retiredInvoiceDocumentsHits('`/api/v1/external/invoices/${id}/messages`')).toEqual([]);
    expect(retiredInvoiceDocumentsHits('`/api/v1/external/invoices/${id}/files`')).toEqual([]);
    expect(retiredInvoiceDocumentsHits('`/api/v1/external/matters/${id}/documents`')).toEqual([]);
    expect(retiredInvoiceDocumentsHits("export type DocumentRoot = 'projects' | 'matters';")).toEqual([]);
  });

  it('no file under src/ builds an invoice documents path or uses `invoices` as a document root', () => {
    const root = path.resolve(__dirname, '..', 'src');
    const files = sourceFiles(root);
    expect(files.length).toBeGreaterThan(50);
    const hits = files
      .map(f => ({ file: path.relative(root, f), hits: retiredInvoiceDocumentsHits(fs.readFileSync(f, 'utf8')) }))
      .filter(r => r.hits.length > 0);
    expect(hits).toEqual([]);
  });
});

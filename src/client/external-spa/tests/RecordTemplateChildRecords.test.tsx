/**
 * R4 task 013 (owner retest 2026-11-14) — "for Invoices I don't see how to open the invoice record (should open on top of this
 * modal with invoice modal)"; a document's name opens its record the same way.
 *
 * In a Matter's Invoices section each row is a button that opens that invoice's record in a child modal over the matter; in
 * its Documents section the document NAME does the same (also for a document with no file, FR-15). The matter stays mounted
 * behind; Close or Escape returns focus to the activating row. An invoice child re-reads the Invoices section on close; a
 * document child cannot change the list, so the Documents grid is left exactly as it was (its sort is kept). Inside a child
 * record there is no further level: rows are plain text (`nested`), and no "1 of 1" browsing is drawn. The Project page's
 * `DocumentLibrary` never gets the buttons.
 *
 * What runs for real: `RecordTemplateModal` (the matter AND the child records), the real Invoices and Documents sections,
 * `DocumentLibrary`, `useChildDialog` / tabster focus handling, `SprkModal`, the record header and actions of the child.
 * What is substituted: the network seam (`bffApiCall`, `getExternalUserContext`) and jsdom's missing layout.
 *
 * Every positive case fails on the old code: an invoice row was a plain `<li>`, a document name plain text, nothing opened.
 */
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const h = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/services/gridDataverseClient', async () => (await import('./documentsGridHarness')).gridClientModule());

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return {
    ...actual,
    bffApiCall: h.bffApiCall,
    bffApiBlob: vi.fn(),
    getExternalUserContext: () => h.bffApiCall('/api/v1/external/me'),
  };
});

import { DocumentLibrary } from '../src/components/DocumentLibrary';
import { RECORD_TEMPLATES } from '../src/components/record/recordTemplates';
import { RecordTemplateModal } from '../src/components/record/RecordTemplateModal';
import { installLayout, uninstallLayout } from './jsdomLayout';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const ME = guid(7);
const MATTER = guid(201);
const INVOICE = guid(501);
const INVOICE_2 = guid(502);
const DOC_FILE = guid(901);
const DOC_NOFILE = guid(902);
const SLOW = { timeout: 60000 };
const WAIT = { timeout: 12000 };

const DOCS = [
  { sprk_documentid: DOC_FILE, sprk_name: 'Contract.pdf', createdon: '2026-03-01T10:00:00Z', hasFile: true },
  { sprk_documentid: DOC_NOFILE, sprk_name: 'Draft note', createdon: '2026-03-02T10:00:00Z', hasFile: false },
];

let invoiceListReads = 0;

const matterDocumentReads = (): number =>
  h.bffApiCall.mock.calls.filter(c => new RegExp(`/matters/${MATTER}/documents`).test(String(c[0]))).length;

function installNet(): void {
  invoiceListReads = 0;
  h.bffApiCall.mockReset();
  h.bffApiCall.mockImplementation(async (path: string, options?: RequestInit) => {
    if (path === '/api/v1/external/me') return { contactId: ME, email: 'me@example.test', projects: [] };
    if (path.endsWith('/api/dataverse/fetch')) {
      const body = JSON.parse(String(options?.body)) as { entityName: string; fetchXml: string };
      const id = /operator="eq" value="([^"]+)"/.exec(body.fetchXml)?.[1] ?? '';
      switch (body.entityName) {
        case 'sprk_matter':
          return {
            entities: [{ sprk_matterid: id, sprk_mattername: 'Matter name', sprk_matternumber: 'MAT-1' }],
            moreRecords: false,
          };
        case 'sprk_invoice':
          // The matter's Invoices list is filtered by the matter lookup; a single invoice by its own id.
          if (body.fetchXml.includes('condition attribute="sprk_matter"')) {
            invoiceListReads += 1;
            return {
              entities: [
                {
                  sprk_invoiceid: INVOICE,
                  sprk_name: 'First invoice',
                  sprk_invoicenumber: 'INV-501',
                  statuscode: 1,
                  sprk_totalamount: 100,
                },
                {
                  sprk_invoiceid: INVOICE_2,
                  sprk_name: 'Second invoice',
                  sprk_invoicenumber: 'INV-502',
                  statuscode: 1,
                  sprk_totalamount: 200,
                },
              ],
              moreRecords: false,
            };
          }
          return {
            entities: [
              {
                sprk_invoiceid: id,
                sprk_name: id === INVOICE ? 'First invoice' : 'Second invoice',
                sprk_invoicenumber: id === INVOICE ? 'INV-501' : 'INV-502',
              },
            ],
            moreRecords: false,
          };
        case 'sprk_document': {
          const doc = DOCS.find(d => d.sprk_documentid === id);
          return {
            entities: [
              {
                sprk_documentid: id,
                sprk_documentname: doc?.sprk_name ?? 'Unknown',
                hasFile: doc?.hasFile,
                sprk_matter: { id: MATTER, logicalName: 'sprk_matter', name: 'Matter name' },
              },
            ],
            moreRecords: false,
          };
        }
        default:
          return { entities: [], moreRecords: false };
      }
    }
    // The invoice child's thread: the caller may post, so New message is offered there.
    if (path === `/api/v1/external/invoices/${INVOICE}/messages`) return { value: [], canCreate: true };
    // The matter's documents; every other list of the record view (messages, events, the child's lists) is empty.
    if (new RegExp(`/matters/${MATTER}/documents(\\?|$)`).test(path)) return { value: DOCS, canCreate: false };
    return { value: [], canCreate: false };
  });
}

const settle = (ms = 60): Promise<void> =>
  act(async () => void (await new Promise(resolve => setTimeout(resolve, ms))));

beforeEach(() => {
  installLayout();
  localStorage.clear();
  installNet();
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  uninstallLayout();
});

// ---------------------------------------------------------------------------
// Harness
// ---------------------------------------------------------------------------

const Harness: React.FC<{ nested?: boolean }> = ({ nested }) => (
  <FluentProvider theme={webLightTheme}>
    <RecordTemplateModal
      open
      nested={nested}
      onClose={() => undefined}
      config={RECORD_TEMPLATES.sprk_matter}
      recordIds={[MATTER]}
      index={0}
      onIndexChange={() => undefined}
    />
  </FluentProvider>
);

/** The titles of every record dialog in the document, outermost first (read from the DOM: the layers behind are aria-hidden). */
const dialogTitles = (): string[] =>
  Array.from(document.querySelectorAll('[id^="sprk-modal-title-"]')).map(el => el.textContent ?? '');

const rowOf = (entity: string, id: string): HTMLElement | null =>
  document.querySelector<HTMLElement>(`[data-child-record="${entity}:${id}"]`);

async function openMatter(): Promise<HTMLElement> {
  render(<Harness />);
  const dialog = await screen.findByRole('dialog');
  await within(dialog).findByTestId('record-template-columns', undefined, WAIT);
  // The ~250 ms aria-hidden pass of the parent (tabster) must have run before a child is opened.
  await settle(350);
  return dialog;
}

/** Focuses a row (as a keyboard user would), activates it, and returns the child dialog it opened. */
async function openChild(row: HTMLElement, titled: string): Promise<HTMLElement> {
  row.focus();
  fireEvent.click(row);
  const child = await screen.findByRole('dialog', { name: titled }, WAIT);
  await settle(350);
  return child;
}

/** Waits for the focus to be on the (possibly re-drawn) row of this record. */
const expectFocusOnRow = (entity: string, id: string): Promise<void> =>
  waitFor(() => {
    const row = rowOf(entity, id);
    expect(row).not.toBeNull();
    expect(document.activeElement).toBe(row);
  }, WAIT);

/** Waits for the focus to be back on a document's name button (the grid's Name cell; the trigger the opener stored). */
const expectFocusOnDocumentName = (name: string): Promise<void> =>
  waitFor(() => {
    const button = screen.getByRole('button', { name });
    expect(document.activeElement).toBe(button);
  }, WAIT);

// ---------------------------------------------------------------------------
// Invoices
// ---------------------------------------------------------------------------

describe('an invoice row opens the invoice record over the matter', () => {
  it(
    'is a keyboard-operable button named "Open invoice ..." whose name carries the number, status and amount; the invoice opens on top while the matter stays mounted',
    async () => {
      const parent = await openMatter();
      const row = await within(parent).findByRole('button', { name: /^Open invoice INV-501/ }, WAIT);
      expect(row.tagName).toBe('BUTTON');
      expect(within(parent).getByRole('button', { name: /^Open invoice INV-502/ })).toBeTruthy();
      // The content names the button: number, status and amount are all in the accessible name (no aria-label hides them).
      expect(row.hasAttribute('aria-label')).toBe(false);
      expect(within(parent).getByRole('button', { name: /^Open invoice INV-501 .*·.*100/ })).toBeTruthy();
      expect(within(parent).getByRole('button', { name: /^Open invoice INV-502 .*·.*200/ })).toBeTruthy();

      const child = await openChild(row, 'Invoice INV-501');
      // The invoice's own record view: its title, its own sections (not the matter's).
      expect(within(child).getByText('Invoice INV-501')).toBeTruthy();
      expect(await within(child).findByTestId('record-template-columns', undefined, WAIT)).toBeTruthy();
      // The matter is still mounted behind it (not replaced).
      expect(dialogTitles()).toEqual(['Matter MAT-1', 'Invoice INV-501']);
      expect(parent.isConnected).toBe(true);
      expect(document.body.contains(parent)).toBe(true);
      // One record: no "1 of 1" browsing in the child (the matter behind it keeps its own).
      expect(within(child).queryByRole('group', { name: 'Record navigation' })).toBeNull();
      expect(within(child).queryByText('1 of 1')).toBeNull();
      expect(parent.querySelector('[aria-label="Record navigation"]')).not.toBeNull();
      // The matter's own record is not what the child shows: no Invoices section inside it.
      expect(within(child).queryByTestId('record-section-invoices')).toBeNull();
    },
    SLOW
  );

  it(
    'Close returns to the matter with focus on the activating row, and re-reads the Invoices section',
    async () => {
      const parent = await openMatter();
      const row = await within(parent).findByRole('button', { name: /^Open invoice INV-502/ }, WAIT);
      const readsBefore = invoiceListReads;
      const child = await openChild(row, 'Invoice INV-502');

      fireEvent.click(within(child).getAllByRole('button', { name: 'Close' })[0]);
      await waitFor(() => expect(dialogTitles()).toEqual(['Matter MAT-1']), WAIT);
      await expectFocusOnRow('sprk_invoice', INVOICE_2);
      expect(parent.isConnected).toBe(true);
      // The section re-read (something may have changed in the invoice).
      await waitFor(() => expect(invoiceListReads).toBeGreaterThan(readsBefore), WAIT);
    },
    SLOW
  );

  it(
    'Escape closes only the child (the matter stays) and focus returns to the row',
    async () => {
      const parent = await openMatter();
      const row = await within(parent).findByRole('button', { name: /^Open invoice INV-501/ }, WAIT);
      const child = await openChild(row, 'Invoice INV-501');

      // Pressed with focus inside the child, so the key really is pressed there.
      expect(child.contains(document.activeElement)).toBe(true);
      fireEvent.keyDown(document.activeElement as Element, { key: 'Escape' });
      await waitFor(() => expect(dialogTitles()).toEqual(['Matter MAT-1']), WAIT);
      await expectFocusOnRow('sprk_invoice', INVOICE);
    },
    SLOW
  );
});

describe('a child record follows the modal Esc standard', () => {
  it(
    'invoice child -> New message dialog -> Esc: the message dialog, the child and the matter all stay open',
    async () => {
      const parent = await openMatter();
      const row = await within(parent).findByRole('button', { name: /^Open invoice INV-501/ }, WAIT);
      const child = await openChild(row, 'Invoice INV-501');
      await within(child).findByTestId('record-template-columns', undefined, WAIT);

      const newMessage = await within(child).findByRole('button', { name: 'New message' }, WAIT);
      newMessage.focus();
      fireEvent.click(newMessage);
      const message = await screen.findByRole('dialog', { name: 'New message' }, WAIT);
      await settle(350);
      expect(dialogTitles()).toEqual(['Matter MAT-1', 'Invoice INV-501', 'New message']);

      // Pressed with focus inside the message dialog, so the key really is pressed there.
      expect(message.contains(document.activeElement)).toBe(true);
      fireEvent.keyDown(document.activeElement as Element, { key: 'Escape' });
      await settle(300);
      expect(dialogTitles()).toEqual(['Matter MAT-1', 'Invoice INV-501', 'New message']);
      expect(message.isConnected).toBe(true);
      expect(child.isConnected).toBe(true);
      expect(parent.isConnected).toBe(true);
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Documents
// ---------------------------------------------------------------------------

describe('a document name opens the document record over the matter', () => {
  it(
    'the name is a link-style button; the document opens on top with its own actions, and Close returns focus to it',
    async () => {
      const parent = await openMatter();
      const name = await within(parent).findByRole('button', { name: 'Contract.pdf' }, WAIT);
      expect(name.tagName).toBe('BUTTON');

      const child = await openChild(name, 'Document Contract.pdf');
      expect(dialogTitles()).toEqual(['Matter MAT-1', 'Document Contract.pdf']);
      await within(child).findByTestId('record-template-columns', undefined, WAIT);
      // Its own actions, as when opened from a grid: this document has a file and a parent link, so Download is offered.
      expect(await within(child).findByRole('button', { name: 'Download Contract.pdf' }, WAIT)).toBeTruthy();
      // Nothing of the matter's actions is inherited (Submit invoice, Upload, New message).
      expect(within(child).queryByRole('button', { name: /Submit invoice|Upload|New message/ })).toBeNull();

      fireEvent.click(within(child).getAllByRole('button', { name: 'Close' })[0]);
      await waitFor(() => expect(dialogTitles()).toEqual(['Matter MAT-1']), WAIT);
      await expectFocusOnDocumentName('Contract.pdf');
    },
    SLOW
  );

  it(
    'opening and closing a document leaves the Documents grid as it was: the sort is kept and the list is not read again',
    async () => {
      const parent = await openMatter();
      await within(parent).findByRole('button', { name: 'Contract.pdf' }, WAIT);
      const order = (): string[] =>
        within(parent)
          .getAllByRole('button', { name: /^(Contract\.pdf|Draft note)$/ })
          .map(el => el.textContent ?? '');

      // The grid starts in the list's order; sorting by Name twice (descending) puts "Draft note" first.
      expect(order()).toEqual(['Contract.pdf', 'Draft note']);
      const header = within(parent).getByRole('columnheader', { name: /Name/ });
      fireEvent.click(header);
      fireEvent.click(header);
      await waitFor(() => expect(order()).toEqual(['Draft note', 'Contract.pdf']), WAIT);
      const readsBefore = matterDocumentReads();

      const name = within(parent).getByRole('button', { name: 'Contract.pdf' });
      const child = await openChild(name, 'Document Contract.pdf');
      fireEvent.click(within(child).getAllByRole('button', { name: 'Close' })[0]);
      await waitFor(() => expect(dialogTitles()).toEqual(['Matter MAT-1']), WAIT);
      await expectFocusOnDocumentName('Contract.pdf');
      await settle(300);

      // Not remounted: the sort is kept and no second documents read happened.
      expect(order()).toEqual(['Draft note', 'Contract.pdf']);
      expect(matterDocumentReads()).toBe(readsBefore);
    },
    SLOW
  );

  it(
    'a document with no file still opens its record (FR-15): Information shown, no Download offered',
    async () => {
      const parent = await openMatter();
      const name = await within(parent).findByRole('button', { name: 'Draft note' }, WAIT);
      const child = await openChild(name, 'Document Draft note');
      await within(child).findByTestId('record-template-columns', undefined, WAIT);
      expect(within(child).getByTestId('record-preview-no-file')).toBeTruthy();
      expect(within(child).queryByRole('button', { name: /^Download/ })).toBeNull();
    },
    SLOW
  );
});

// ---------------------------------------------------------------------------
// Where it does NOT apply
// ---------------------------------------------------------------------------

describe('rows stay as they were where no child may open', () => {
  it(
    'the Project page DocumentLibrary (no opener): a name click opens nothing and no row carries an opener marker',
    async () => {
      h.bffApiCall.mockImplementation(async (path: string) => {
        if (/\/documents(\?|$)/.test(path)) return { value: DOCS };
        return { value: [] };
      });
      render(
        <FluentProvider theme={webLightTheme}>
          <DocumentLibrary projectId={guid(101)} />
        </FluentProvider>
      );
      expect(await screen.findByText('Contract.pdf', undefined, WAIT)).toBeTruthy();
      expect(screen.getByText('Draft note')).toBeTruthy();
      expect(screen.queryByRole('button', { name: /Open document/ })).toBeNull();
      expect(document.querySelector('[data-child-record]')).toBeNull();
      // The grid draws the name as its link; with no opener the click is a no-op (the grid's Xrm default must not run).
      fireEvent.click(screen.getByRole('button', { name: 'Contract.pdf' }));
      await settle(400);
      expect(screen.queryByRole('dialog')).toBeNull();
    },
    SLOW
  );

  it(
    'inside a child record (nested) the invoice and document rows do not open a further modal',
    async () => {
      render(<Harness nested />);
      const dialog = await screen.findByRole('dialog');
      await within(dialog).findByTestId('record-template-columns', undefined, WAIT);
      // The rows are drawn (text), but they are not buttons and carry no opener.
      expect(await within(dialog).findByText('INV-501', undefined, WAIT)).toBeTruthy();
      expect(await within(dialog).findByText('Contract.pdf', undefined, WAIT)).toBeTruthy();
      expect(screen.queryByRole('button', { name: /Open (invoice|document)/ })).toBeNull();
      expect(document.querySelector('[data-child-record]')).toBeNull();
      // A nested record is one record: no "1 of 1" browsing either.
      expect(within(dialog).queryByRole('group', { name: 'Record navigation' })).toBeNull();
      // Clicking the text opens nothing.
      fireEvent.click(within(dialog).getByText('INV-501'));
      fireEvent.click(within(dialog).getByText('Contract.pdf'));
      await settle(400);
      expect(dialogTitles()).toEqual(['Matter MAT-1']);
    },
    SLOW
  );

  it(
    'control: the same matter NOT nested does offer the buttons',
    async () => {
      const parent = await openMatter();
      expect(await within(parent).findByRole('button', { name: /^Open invoice INV-501/ }, WAIT)).toBeTruthy();
      expect(await within(parent).findByRole('button', { name: 'Contract.pdf' }, WAIT)).toBeTruthy();
      expect(within(parent).getByRole('group', { name: 'Record navigation' })).toBeTruthy();
    },
    SLOW
  );
});

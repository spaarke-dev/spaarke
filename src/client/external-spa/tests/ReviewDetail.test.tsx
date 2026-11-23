/**
 * R4 task 020 (FR-11) — the invoice Review detail (082c) at its four render points, behind ONE gate.
 *
 * The gate (uac-r2, binding): the value or its explanation shows ONLY while the row's raw `statuscode` is 1 (In Review).
 * Values 659490000 / 659490001 / 659490002 map to the owner-approved texts (design §7); a blank value while In Review shows
 * "The client is reviewing this invoice."; any other value, and any other status, renders blank.
 *
 * What runs for real: `reviewDetail.ts`, the grid renderer `InvoicesWidget` hands the grid, `createGridWidgetBody`,
 * `RecordDetailFields` (Information), `InvoicesSection` (the record's Invoices list) and `RecordTemplateModal` with its
 * header. What is substituted: the network seam (`bffApiCall`), `/me`, and the shared grid (it records its props).
 *
 * Every must-fire case has its must-not-fire pair (NFR-07): the same value on a Paid, Rejected and Approved invoice renders
 * blank in all four places.
 *
 * FR-12 (the held-Accepted badge) is NOT tested here: its three states are already pinned in the shared library by
 * `InvoiceSubmission/__tests__/InvoiceSubmissionOutcome.warning.test.tsx` (task 020 notes).
 */
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const h = vi.hoisted(() => ({ bffApiCall: vi.fn(), getExternalUserContext: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall: h.bffApiCall, getExternalUserContext: h.getExternalUserContext };
});
vi.mock('@spaarke/ui-components/utils/themeStorage', () => ({
  resolveCodePageTheme: () => null,
  setupCodePageThemeListener: () => () => undefined,
}));
vi.mock('../src/services/gridDataverseClient', () => ({ gridDataverseClient: {} }));

interface SeenGridProps {
  configId: string;
  overrides?: { columnRenderers?: Record<string, (value: unknown, row: Record<string, unknown>) => unknown> };
}
const seenGrids: SeenGridProps[] = [];
vi.mock('../src/widgets/ExternalDataGrid', async () => {
  const R = await import('react');
  return {
    ExternalDataGrid: (props: SeenGridProps) => {
      seenGrids.push(props);
      return R.createElement('div', { 'data-testid': 'fake-grid' });
    },
  };
});

import { RECORD_DETAIL_ENTITIES, RecordDetailFields } from '../src/components/detail/RecordDetailFields';
import { RecordTemplateModal } from '../src/components/record/RecordTemplateModal';
import { RECORD_TEMPLATES } from '../src/components/record/recordTemplates';
import type { RecordSectionContext } from '../src/components/record/recordTemplate.types';
import { InformationSection } from '../src/components/record/sections/InformationSection';
import { INVOICE_COLUMNS, InvoicesSection } from '../src/components/record/sections/InvoicesSection';
import {
  BLANK_IN_REVIEW_EXPLANATION,
  IN_REVIEW_STATUSCODE,
  REVIEW_DETAIL_COLUMN,
  reviewDetailCellText,
  reviewDetailOf,
  reviewDetailSecondaryLine,
} from '../src/components/record/reviewDetail';
import { createGridWidgetBody } from '../src/widgets/GridWidgetBody';
import { INVOICE_GRID_OVERRIDES } from '../src/widgets/InvoicesWidget';

// ---------------------------------------------------------------------------
// The approved texts, written out here on purpose (design §7, verbatim): a change to them must change this test.
// ---------------------------------------------------------------------------

const TERMS = { value: 659490000, text: 'Waiting for billing terms' };
const UNCHECKED = { value: 659490001, text: 'Billing terms could not be checked' };
const FIRM = { value: 659490002, text: 'Waiting for the firm to be confirmed' };
const OPTIONS = [TERMS, UNCHECKED, FIRM] as const;

const EXPLANATION: Record<number, string> = {
  659490000:
    "This invoice has not been rejected. It is on hold for review because no billing terms are set up for some of its work dates on this record. If the record and the work dates are right, you don't need to do anything; the client will follow up.",
  659490001:
    "This invoice has not been rejected. It is on hold for review because its billing terms could not be checked. You don't need to do anything; the client will follow up.",
  659490002:
    "This invoice has not been rejected. It is on hold for review until the client confirms which firm sent it. You don't need to do anything; the client will follow up.",
};
const REVIEWING = 'The client is reviewing this invoice.';

// Invoice statuscodes (server `InvoiceStatusCodes`).
const IN_REVIEW = 1;
const APPROVED = 659490000;
const PAID = 659490002;
const REJECTED = 659490003;
const NOT_IN_REVIEW = [0, 2, APPROVED, 659490001, PAID, REJECTED, 659490004, 659490005] as const;

const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const INVOICE_ID = guid(501);

const invoiceRow = (statuscode: number, detail: number | null | undefined, over: Record<string, unknown> = {}) => ({
  sprk_invoiceid: INVOICE_ID,
  sprk_invoicenumber: 'INV-1',
  sprk_name: 'Invoice name',
  statuscode,
  '@formattedValues': { statuscode: statuscode === IN_REVIEW ? 'In Review' : statuscode === PAID ? 'Paid' : 'Other' },
  ...(detail === undefined ? {} : { [REVIEW_DETAIL_COLUMN]: detail }),
  ...over,
});

// ---------------------------------------------------------------------------
// The gate
// ---------------------------------------------------------------------------

describe('reviewDetailOf — the one gate', () => {
  it('shows each approved text and explanation while the invoice is In Review (must-fire)', () => {
    for (const option of OPTIONS) {
      expect(reviewDetailOf(IN_REVIEW, option.value)).toEqual({
        text: option.text,
        explanation: EXPLANATION[option.value],
      });
    }
    expect(IN_REVIEW_STATUSCODE).toBe(1);
  });

  it('a blank value while In Review shows only the reviewing line', () => {
    for (const blank of [null, undefined, '']) {
      expect(reviewDetailOf(IN_REVIEW, blank)).toEqual({ text: null, explanation: REVIEWING });
    }
    expect(BLANK_IN_REVIEW_EXPLANATION).toBe(REVIEWING);
  });

  it('must-not-fire: every other status renders blank, whatever the value (Paid and Rejected included)', () => {
    for (const status of NOT_IN_REVIEW) {
      for (const value of [TERMS.value, UNCHECKED.value, FIRM.value, null, undefined, '']) {
        expect(reviewDetailOf(status, value), `status ${status}, value ${String(value)}`).toBeNull();
      }
    }
  });

  it('fails closed on a status that is missing, unreadable or only a label', () => {
    for (const status of [null, undefined, '', 'In Review', 'abc', {}, [], 1.5, NaN]) {
      expect(reviewDetailOf(status, TERMS.value), `status ${String(status)}`).toBeNull();
      expect(reviewDetailOf(status, null), `status ${String(status)}, blank`).toBeNull();
    }
  });

  it('an unknown value renders blank, even while In Review (no priority is derived, no label is guessed)', () => {
    for (const value of [0, 1, 659490003, 659489999, -1, 'Waiting for billing terms', 'abc', {}, true, 1.5]) {
      expect(reviewDetailOf(IN_REVIEW, value), `value ${String(value)}`).toBeNull();
    }
  });

  it('reads the raw option value, as a number or a string of digits', () => {
    expect(reviewDetailOf('1', '659490001')?.text).toBe(UNCHECKED.text);
    expect(reviewDetailOf(1, 659490002)?.text).toBe(FIRM.text);
  });

  it('the row forms read the same two columns (statuscode, sprk_reviewdetail) and nothing else', () => {
    const held = invoiceRow(IN_REVIEW, TERMS.value);
    expect(reviewDetailCellText(held)).toBe(TERMS.text);
    expect(reviewDetailSecondaryLine(held)).toBe(TERMS.text);
    // The display label never decides: a label that says "In Review" on a row whose raw status is not 1 shows nothing.
    const labelled = invoiceRow(PAID, TERMS.value, { '@formattedValues': { statuscode: 'In Review' } });
    expect(reviewDetailCellText(labelled)).toBe('');
    expect(reviewDetailSecondaryLine(labelled)).toBeNull();
    // A blank value has no label to show in a cell or a header line.
    expect(reviewDetailCellText(invoiceRow(IN_REVIEW, null))).toBe('');
    expect(reviewDetailSecondaryLine(invoiceRow(IN_REVIEW, undefined))).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// (a) The Invoices grid column
// ---------------------------------------------------------------------------

describe('render point (a) — the Invoices grid column', () => {
  beforeEach(() => {
    seenGrids.length = 0;
  });

  it('the Invoices widget hands the grid a sprk_reviewdetail renderer that applies the gate to the row', () => {
    const renderer = INVOICE_GRID_OVERRIDES?.columnRenderers?.[REVIEW_DETAIL_COLUMN];
    expect(typeof renderer).toBe('function');
    for (const option of OPTIONS) {
      expect(renderer?.(option.value, invoiceRow(IN_REVIEW, option.value))).toBe(option.text);
    }
    expect(renderer?.(null, invoiceRow(IN_REVIEW, null))).toBe('');
    expect(renderer?.(PAID, invoiceRow(PAID, TERMS.value))).toBe('');
  });

  it('must-not-fire: a Paid, Rejected or Approved row that carries a value renders blank in the cell', () => {
    const renderer = INVOICE_GRID_OVERRIDES?.columnRenderers?.[REVIEW_DETAIL_COLUMN];
    for (const status of [PAID, REJECTED, APPROVED]) {
      for (const option of OPTIONS) {
        expect(renderer?.(option.value, invoiceRow(status, option.value))).toBe('');
      }
    }
    // A row the grid read with no status at all (the column configured without Status) fails closed.
    const { statuscode: _omitted, ...noStatus } = invoiceRow(IN_REVIEW, TERMS.value);
    expect(renderer?.(TERMS.value, noStatus)).toBe('');
  });

  it('createGridWidgetBody forwards `overrides` to the grid; without them the grid is handed none (other grids unchanged)', () => {
    const WithOverrides = createGridWidgetBody('3ff4102c-1092-f111-b8dc-7ced8ddc4a05', INVOICE_GRID_OVERRIDES);
    render(React.createElement(WithOverrides, { title: 'Invoices' }));
    expect(seenGrids[seenGrids.length - 1].overrides).toBe(INVOICE_GRID_OVERRIDES);

    seenGrids.length = 0;
    const Plain = createGridWidgetBody('583a2a33-1092-f111-b8dc-7ced8ddc4a05');
    render(React.createElement(Plain, { title: 'Matters' }));
    expect(seenGrids.length).toBeGreaterThan(0);
    expect(seenGrids.every(p => p.overrides === undefined)).toBe(true);
  });
});

// ---------------------------------------------------------------------------
// (b) The record's Invoices section
// ---------------------------------------------------------------------------

const sectionContext = {
  detail: RECORD_DETAIL_ENTITIES.sprk_matter,
  recordId: guid(201),
  refreshSignal: 0,
  headerActions: [],
} as unknown as RecordSectionContext;

async function renderInvoicesSection(rows: Array<Record<string, unknown>>): Promise<HTMLElement> {
  h.bffApiCall.mockImplementation(async () => ({ entities: rows, moreRecords: false }));
  render(
    <FluentProvider theme={webLightTheme}>
      <InvoicesSection {...sectionContext} />
    </FluentProvider>
  );
  return screen.findByTestId('record-detail-invoices').then(async list => {
    await waitFor(() => expect(within(list).queryByText(/Loading invoices/)).toBeNull());
    return list;
  });
}

describe('render point (b) — the record Invoices section', () => {
  beforeEach(() => {
    h.bffApiCall.mockReset();
  });
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('reads the column with the list (and the status the gate needs)', async () => {
    await renderInvoicesSection([]);
    expect(INVOICE_COLUMNS).toContain(REVIEW_DETAIL_COLUMN);
    expect(INVOICE_COLUMNS).toContain('statuscode');
    const body = JSON.parse(String((h.bffApiCall.mock.calls[0][1] as RequestInit).body)) as { fetchXml: string };
    expect(body.fetchXml).toContain(`<attribute name="${REVIEW_DETAIL_COLUMN}"/>`);
    expect(body.fetchXml).toContain('<attribute name="statuscode"/>');
  });

  it('shows each approved text after the status for an In Review invoice (must-fire)', async () => {
    const rows = OPTIONS.map((option, i) =>
      invoiceRow(IN_REVIEW, option.value, { sprk_invoiceid: guid(600 + i), sprk_invoicenumber: `INV-${i + 1}` })
    );
    const list = await renderInvoicesSection(rows);
    for (const [i, option] of OPTIONS.entries()) {
      expect(within(list).getByText(new RegExp(`In Review · ${option.text}`)), `row ${i}`).toBeTruthy();
    }
  });

  it('must-not-fire: Paid, Rejected and Approved invoices that carry a value show no review detail', async () => {
    const rows = [PAID, REJECTED, APPROVED].map((status, i) =>
      invoiceRow(status, TERMS.value, { sprk_invoiceid: guid(700 + i), sprk_invoicenumber: `INV-${i + 1}` })
    );
    const list = await renderInvoicesSection(rows);
    expect(within(list).getByText('INV-1')).toBeTruthy();
    for (const option of OPTIONS) expect(list.textContent ?? '').not.toContain(option.text);
    expect(list.textContent ?? '').not.toContain(REVIEWING);
  });

  it('an unknown value and a blank value show nothing extra', async () => {
    const list = await renderInvoicesSection([
      invoiceRow(IN_REVIEW, 659490009, { sprk_invoiceid: guid(800), sprk_invoicenumber: 'INV-U' }),
      invoiceRow(IN_REVIEW, null, { sprk_invoiceid: guid(801), sprk_invoicenumber: 'INV-B' }),
    ]);
    // The meta is the status and the (absent) amount only: "In Review · —", with no review detail between them.
    expect(within(list).getAllByText('In Review · —')).toHaveLength(2);
    for (const option of OPTIONS) expect(list.textContent ?? '').not.toContain(option.text);
    expect(list.textContent ?? '').not.toContain(REVIEWING);
  });
});

// ---------------------------------------------------------------------------
// (d) The Information field
// ---------------------------------------------------------------------------

const invoiceDetail = RECORD_DETAIL_ENTITIES.sprk_invoice;

function renderFields(record: Record<string, unknown>): void {
  render(
    <FluentProvider theme={webLightTheme}>
      <RecordDetailFields cfg={invoiceDetail} record={record} />
    </FluentProvider>
  );
}

describe('render point (d) — the Information field', () => {
  it('the invoice read description reads the column and the status', () => {
    const attributes = invoiceDetail.fields.map(f => f.attribute);
    expect(attributes).toContain(REVIEW_DETAIL_COLUMN);
    expect(attributes).toContain('statuscode');
    // After Status, as the grid column is (FR-11).
    expect(attributes.indexOf(REVIEW_DETAIL_COLUMN)).toBe(attributes.indexOf('statuscode') + 1);
  });

  it.each(OPTIONS)('shows "$text" and its explanation line for an In Review invoice (must-fire)', option => {
    renderFields(invoiceRow(IN_REVIEW, option.value));
    const field = screen.getByTestId(`record-detail-field-${REVIEW_DETAIL_COLUMN}`);
    expect(field.textContent).toContain(option.text);
    expect(screen.getByTestId(`record-detail-field-${REVIEW_DETAIL_COLUMN}-note`).textContent).toBe(
      EXPLANATION[option.value]
    );
  });

  it('a blank value while In Review shows "The client is reviewing this invoice."', () => {
    renderFields(invoiceRow(IN_REVIEW, null));
    expect(screen.getByTestId(`record-detail-field-${REVIEW_DETAIL_COLUMN}-note`).textContent).toBe(REVIEWING);
    expect(screen.getByTestId(`record-detail-field-${REVIEW_DETAIL_COLUMN}`).textContent).toBe(REVIEWING);
  });

  it('a column the read did not return (the value key absent) while In Review also shows the reviewing line', () => {
    renderFields(invoiceRow(IN_REVIEW, undefined));
    expect(screen.getByTestId(`record-detail-field-${REVIEW_DETAIL_COLUMN}-note`).textContent).toBe(REVIEWING);
  });

  it('must-not-fire: a Paid, Rejected or Approved invoice that carries a value shows no row and no explanation', () => {
    for (const status of [PAID, REJECTED, APPROVED]) {
      const { unmount } = render(
        <FluentProvider theme={webLightTheme}>
          <RecordDetailFields cfg={invoiceDetail} record={invoiceRow(status, FIRM.value)} />
        </FluentProvider>
      );
      expect(screen.queryByTestId(`record-detail-field-${REVIEW_DETAIL_COLUMN}`), `status ${status}`).toBeNull();
      const text = screen.getByTestId('record-detail-fields').textContent ?? '';
      expect(text).not.toContain(FIRM.text);
      expect(text).not.toContain(EXPLANATION[FIRM.value]);
      expect(text).not.toContain(REVIEWING);
      // The rest of the invoice still shows.
      expect(text).toContain('INV-1');
      unmount();
    }
  });

  it('an unknown value shows no row', () => {
    renderFields(invoiceRow(IN_REVIEW, 659490009));
    expect(screen.queryByTestId(`record-detail-field-${REVIEW_DETAIL_COLUMN}`)).toBeNull();
  });

  it('control: no other entity gained a gated row (the Matter field list is unchanged)', () => {
    for (const entity of Object.values(RECORD_DETAIL_ENTITIES)) {
      if (entity.entity === 'sprk_invoice') continue;
      expect(
        entity.fields.some(f => f.display !== undefined),
        entity.entity
      ).toBe(false);
    }
  });
});

// ---------------------------------------------------------------------------
// (c) The Invoice header's status-fact secondary line, in the real modal (Information included)
// ---------------------------------------------------------------------------

function installInvoiceRead(row: Record<string, unknown>): void {
  h.bffApiCall.mockImplementation(async (path: string) => {
    if (!path.endsWith('/api/v1/external/api/dataverse/fetch')) throw new Error(`unexpected BFF call ${path}`);
    return { entities: [row], moreRecords: false };
  });
}

async function openInvoice(row: Record<string, unknown>): Promise<HTMLElement> {
  installInvoiceRead(row);
  h.getExternalUserContext.mockResolvedValue({ contactId: guid(7), email: 'me@example.test', projects: [] });
  render(
    <FluentProvider theme={webLightTheme}>
      <RecordTemplateModal
        open
        onClose={() => undefined}
        config={RECORD_TEMPLATES.sprk_invoice}
        recordIds={[INVOICE_ID]}
        index={0}
        onIndexChange={() => undefined}
        // Only the Information section is real here; the others read other routes and are not under test.
        renderSection={(id, ctx) => (id === 'information' ? <InformationSection {...ctx} /> : null)}
      />
    </FluentProvider>
  );
  const dialog = await screen.findByRole('dialog');
  await within(dialog).findByTestId('record-template-header');
  return dialog;
}

describe('render point (c) — the Invoice header status fact, with (d) in the same record', () => {
  beforeEach(() => {
    h.bffApiCall.mockReset();
    h.getExternalUserContext.mockReset();
  });
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it.each(OPTIONS)('shows "$text" under the status, and the Information field agrees (must-fire)', async option => {
    const dialog = await openInvoice(invoiceRow(IN_REVIEW, option.value));
    const secondary = within(dialog).getByTestId('record-template-fact-secondary');
    expect(secondary.textContent).toBe(option.text);
    // It sits in the Status fact, under the value.
    expect(secondary.closest('div')?.textContent).toContain('Status');
    expect(secondary.parentElement?.textContent).toContain('In Review');
    // The same approved text in the Information field, with its explanation.
    const field = within(dialog).getByTestId(`record-detail-field-${REVIEW_DETAIL_COLUMN}`);
    expect(field.textContent).toContain(option.text);
    expect(within(dialog).getByTestId(`record-detail-field-${REVIEW_DETAIL_COLUMN}-note`).textContent).toBe(
      EXPLANATION[option.value]
    );
  });

  it('a blank value while In Review: no header line, and the Information field shows the reviewing line', async () => {
    const dialog = await openInvoice(invoiceRow(IN_REVIEW, null));
    expect(within(dialog).queryByTestId('record-template-fact-secondary')).toBeNull();
    expect(within(dialog).getByTestId(`record-detail-field-${REVIEW_DETAIL_COLUMN}-note`).textContent).toBe(REVIEWING);
  });

  it.each([
    ['Paid', PAID],
    ['Rejected', REJECTED],
  ])(
    'must-not-fire: a %s invoice that carries a value shows nothing in the header or Information',
    async (_name, status) => {
      const dialog = await openInvoice(invoiceRow(status, TERMS.value));
      expect(within(dialog).queryByTestId('record-template-fact-secondary')).toBeNull();
      expect(within(dialog).queryByTestId(`record-detail-field-${REVIEW_DETAIL_COLUMN}`)).toBeNull();
      const text = dialog.textContent ?? '';
      expect(text).not.toContain(TERMS.text);
      expect(text).not.toContain(EXPLANATION[TERMS.value]);
      expect(text).not.toContain(REVIEWING);
    }
  );

  it('control: another record type has no secondary line on any header fact', async () => {
    h.bffApiCall.mockImplementation(async () => ({
      entities: [
        {
          sprk_matterid: guid(201),
          sprk_mattername: 'Matter name',
          sprk_matternumber: 'MAT-1',
          statuscode: IN_REVIEW,
          [REVIEW_DETAIL_COLUMN]: TERMS.value,
        },
      ],
      moreRecords: false,
    }));
    h.getExternalUserContext.mockResolvedValue({ contactId: guid(7), email: 'me@example.test', projects: [] });
    render(
      <FluentProvider theme={webLightTheme}>
        <RecordTemplateModal
          open
          onClose={() => undefined}
          config={RECORD_TEMPLATES.sprk_matter}
          recordIds={[guid(201)]}
          index={0}
          onIndexChange={() => undefined}
          renderSection={() => null}
        />
      </FluentProvider>
    );
    const dialog = await screen.findByRole('dialog');
    await within(dialog).findByTestId('record-template-header');
    expect(within(dialog).queryByTestId('record-template-fact-secondary')).toBeNull();
  });
});

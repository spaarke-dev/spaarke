/**
 * InvoiceSubmissionOutcome links (ebilling-r1 iss052, #3082): "Open invoice" and "Open {matter|project|work assignment}" on the
 * result screen and the resume view. Each shows only when the host passes its callback AND the server named the target: the
 * invoice for an Accepted view with an `invoiceId`, the record whenever `coreRecord` is a known kind and a GUID. Without the
 * callbacks the screen is unchanged (the internal CreateInvoiceWizard passes none). No id is ever shown as text.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { InvoiceSubmissionOutcome } from '../InvoiceSubmissionOutcome';
import { InvoiceSubmissionWizard } from '../InvoiceSubmissionWizard';
import { invoiceSubmissionRecordOf } from '../types';
import type { InvoiceCoreRecordOption, InvoiceSubmissionStatusView } from '../types';

const ID = '33333333-3333-4333-8333-333333333333';
const INVOICE_ID = '44444444-4444-4444-8444-444444444444';
const RECORD_ID = '11111111-1111-4111-8111-111111111111';

const view = (over: Partial<InvoiceSubmissionStatusView> = {}): InvoiceSubmissionStatusView => ({
  submissionId: ID,
  trackingNumber: 'TRK-0052',
  firmInvoiceNumber: 'INV-52',
  status: 'Accepted',
  invoiceId: INVOICE_ID,
  coreRecord: { kind: 0, recordId: RECORD_ID },
  ...over,
});

const settle = (ms = 40) =>
  act(async () => {
    await new Promise(r => setTimeout(r, ms));
  });

function renderOutcome(
  status: InvoiceSubmissionStatusView,
  callbacks: { onOpenInvoice?: jest.Mock; onOpenRecord?: jest.Mock } = {}
) {
  const getStatus = jest.fn().mockResolvedValue(status);
  renderWithProviders(
    <InvoiceSubmissionOutcome submissionId={ID} getStatus={getStatus} pollIntervalMs={10} {...callbacks} />
  );
  return getStatus;
}

const openInvoice = () => screen.queryByRole('button', { name: 'Open invoice' });

describe('InvoiceSubmissionOutcome — iss052 links', () => {
  it('Accepted with an invoice and a matter: both actions show, and each calls its callback with the server ids', async () => {
    const onOpenInvoice = jest.fn();
    const onOpenRecord = jest.fn();
    renderOutcome(view(), { onOpenInvoice, onOpenRecord });

    fireEvent.click(await screen.findByRole('button', { name: 'Open invoice' }));
    fireEvent.click(screen.getByRole('button', { name: 'Open matter' }));

    expect(onOpenInvoice).toHaveBeenCalledWith(INVOICE_ID);
    expect(onOpenRecord).toHaveBeenCalledWith({ kind: 'matter', recordId: RECORD_ID });
    // No id is shown as text.
    expect(screen.queryByText(new RegExp(INVOICE_ID))).not.toBeInTheDocument();
    expect(screen.queryByText(new RegExp(RECORD_ID))).not.toBeInTheDocument();
  });

  it.each([
    [1, 'Open project'],
    [2, 'Open work assignment'],
    ['Project', 'Open project'],
    ['workassignment', 'Open work assignment'],
  ])('a core record of kind %p reads "%s"', async (kind, label) => {
    const onOpenRecord = jest.fn();
    renderOutcome(view({ coreRecord: { kind, recordId: RECORD_ID } }), { onOpenRecord });
    fireEvent.click(await screen.findByRole('button', { name: label }));
    expect(onOpenRecord).toHaveBeenCalledWith({ kind: expect.any(String), recordId: RECORD_ID });
  });

  it.each(['Rejected', 'Failed', 'Processing', 'Received'])(
    '%s: no "Open invoice" even if an id came with it; "Open matter" still shows (the record is known)',
    async status => {
      renderOutcome(view({ status, outcomeReasonCode: null }), { onOpenInvoice: jest.fn(), onOpenRecord: jest.fn() });
      expect(await screen.findByRole('button', { name: 'Open matter' })).toBeInTheDocument();
      expect(openInvoice()).not.toBeInTheDocument();
    }
  );

  it('Accepted with no invoice id (hidden, or an older server): no "Open invoice"', async () => {
    renderOutcome(view({ invoiceId: undefined }), { onOpenInvoice: jest.fn(), onOpenRecord: jest.fn() });
    expect(await screen.findByRole('button', { name: 'Open matter' })).toBeInTheDocument();
    expect(openInvoice()).not.toBeInTheDocument();
  });

  it('an id that is not a GUID, or a core record that is not understood, offers nothing', async () => {
    renderOutcome(view({ invoiceId: 'not-a-guid', coreRecord: { kind: 7, recordId: RECORD_ID } }), {
      onOpenInvoice: jest.fn(),
      onOpenRecord: jest.fn(),
    });
    expect(await screen.findByText(/The invoice was accepted/i)).toBeInTheDocument();
    expect(screen.queryByTestId('invoice-submission-links')).not.toBeInTheDocument();
  });

  it('without the callbacks the screen is unchanged: no links, whatever the server sent', async () => {
    renderOutcome(view());
    expect(await screen.findByText(/The invoice was accepted/i)).toBeInTheDocument();
    await settle();
    expect(screen.queryByTestId('invoice-submission-links')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Open / })).not.toBeInTheDocument();
  });

  it('only the callback that is passed is offered', async () => {
    renderOutcome(view(), { onOpenRecord: jest.fn() });
    expect(await screen.findByRole('button', { name: 'Open matter' })).toBeInTheDocument();
    expect(openInvoice()).not.toBeInTheDocument();
  });
});

describe('invoiceSubmissionRecordOf', () => {
  it('reads the BFF enum number and a name; refuses anything else', () => {
    expect(invoiceSubmissionRecordOf(view())).toEqual({ kind: 'matter', recordId: RECORD_ID });
    expect(invoiceSubmissionRecordOf(view({ coreRecord: { kind: 'MATTER', recordId: RECORD_ID } }))?.kind).toBe(
      'matter'
    );
    expect(invoiceSubmissionRecordOf(view({ coreRecord: { kind: -1, recordId: RECORD_ID } }))).toBeNull();
    expect(invoiceSubmissionRecordOf(view({ coreRecord: { kind: 0.5, recordId: RECORD_ID } }))).toBeNull();
    expect(invoiceSubmissionRecordOf(view({ coreRecord: { kind: 0, recordId: 'x' } }))).toBeNull();
    expect(invoiceSubmissionRecordOf(view({ coreRecord: null }))).toBeNull();
    expect(invoiceSubmissionRecordOf(null)).toBeNull();
  });
});

describe('InvoiceSubmissionWizard — iss052 passes the links to its result screen', () => {
  const MATTER: InvoiceCoreRecordOption = { kind: 'matter', recordId: RECORD_ID, name: 'Acme v. Roadrunner' };

  async function submitWith(callbacks: { onOpenInvoice?: jest.Mock; onOpenRecord?: jest.Mock }) {
    const utils = renderWithProviders(
      <InvoiceSubmissionWizard
        embedded
        pollIntervalMs={10}
        loadRecords={jest.fn().mockResolvedValue({ records: [MATTER] })}
        submit={jest.fn().mockResolvedValue({ submissionId: ID, trackingNumber: 'TRK-0052' })}
        getStatus={jest.fn().mockResolvedValue(view())}
        onClose={jest.fn()}
        {...callbacks}
      />
    );
    fireEvent.click(await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ }));
    fireEvent.click(screen.getByRole('button', { name: /^next$/i }));
    const input = utils.container.querySelector('input[type="file"]') as HTMLInputElement;
    fireEvent.change(input, {
      target: { files: [new File(['%PDF-1.4'], 'invoice.pdf', { type: 'application/pdf' })] },
    });
    await waitFor(() => expect(screen.getByRole('button', { name: /^next$/i })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: /^next$/i }));
    fireEvent.change(await screen.findByLabelText(/Firm invoice number/i), { target: { value: 'INV-52' } });
    fireEvent.click(screen.getByRole('button', { name: /^submit invoice$/i }));
  }

  it('with the callbacks: "Open invoice" and "Open matter" on the result screen', async () => {
    const onOpenInvoice = jest.fn();
    const onOpenRecord = jest.fn();
    await submitWith({ onOpenInvoice, onOpenRecord });
    fireEvent.click(await screen.findByRole('button', { name: 'Open invoice' }));
    fireEvent.click(screen.getByRole('button', { name: 'Open matter' }));
    expect(onOpenInvoice).toHaveBeenCalledWith(INVOICE_ID);
    expect(onOpenRecord).toHaveBeenCalledWith({ kind: 'matter', recordId: RECORD_ID });
  });

  it('without them (the internal wizard): the result screen has no links', async () => {
    await submitWith({});
    expect(await screen.findByText(/The invoice was accepted/i)).toBeInTheDocument();
    expect(screen.queryByTestId('invoice-submission-links')).not.toBeInTheDocument();
  });
});

/**
 * InvoiceSubmissionWizard tests (ebilling-r1 task 051, FR-18 / FR-19, D-91).
 *
 * The wizard, WizardShell, FileUploadZone and the Processing / Result screen all run for real; only the host's three
 * callbacks (loadRecords, submit, getStatus) are fakes. Every assertion is about what the user sees and what the
 * host's callbacks receive.
 */
import * as React from 'react';
import * as fs from 'fs';
import * as path from 'path';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { InvoiceSubmissionWizard } from '../InvoiceSubmissionWizard';
import type { InvoiceSubmissionWizardProps } from '../InvoiceSubmissionWizard';
import { InvoiceSubmissionError, isEbillingDisabledError } from '../types';
import type { InvoiceCoreRecordOption, InvoiceSubmissionStatusView } from '../types';

const MATTER: InvoiceCoreRecordOption = {
  kind: 'matter',
  recordId: '11111111-1111-4111-8111-111111111111',
  name: 'Acme v. Roadrunner',
  number: 'M-1001',
};
const PROJECT: InvoiceCoreRecordOption = {
  kind: 'project',
  recordId: '22222222-2222-4222-8222-222222222222',
  name: 'Patent portfolio',
  number: 'P-77',
};
const SUBMISSION_ID = '33333333-3333-4333-8333-333333333333';

const view = (over: Partial<InvoiceSubmissionStatusView> = {}): InvoiceSubmissionStatusView => ({
  submissionId: SUBMISSION_ID,
  trackingNumber: 'TRK-0001',
  firmInvoiceNumber: 'INV-9',
  status: 'Processing',
  ...over,
});

interface Fakes {
  loadRecords: jest.Mock;
  submit: jest.Mock;
  getStatus: jest.Mock;
  onClose: jest.Mock;
  onSubmitted: jest.Mock;
}

function mount(over: Partial<InvoiceSubmissionWizardProps> = {}, fakes: Partial<Fakes> = {}) {
  const f: Fakes = {
    loadRecords: jest.fn().mockResolvedValue({ records: [MATTER, PROJECT] }),
    submit: jest.fn().mockResolvedValue({ submissionId: SUBMISSION_ID, trackingNumber: 'TRK-0001' }),
    getStatus: jest.fn().mockResolvedValue(view({ status: 'Accepted' })),
    onClose: jest.fn(),
    onSubmitted: jest.fn(),
    ...fakes,
  };
  const utils = renderWithProviders(
    <InvoiceSubmissionWizard
      embedded
      pollIntervalMs={10}
      loadRecords={f.loadRecords}
      submit={f.submit}
      getStatus={f.getStatus}
      onClose={f.onClose}
      onSubmitted={f.onSubmitted}
      {...over}
    />
  );
  return { ...utils, ...f };
}

const next = () => screen.getByRole('button', { name: /^next$/i });
const submitButton = () => screen.getByRole('button', { name: /^submit invoice$/i });

async function chooseRecord(name: RegExp) {
  fireEvent.click(await screen.findByRole('radio', { name }));
  fireEvent.click(next());
}

function pickFile(container: HTMLElement, file: File) {
  const input = container.querySelector('input[type="file"]') as HTMLInputElement;
  fireEvent.change(input, { target: { files: [file] } });
}

const pdf = (name = 'invoice.pdf', body = '%PDF-1.4') => new File([body], name, { type: 'application/pdf' });

/** Walk to the Details step with a record and a PDF chosen. */
async function toDetails(utils: ReturnType<typeof mount>) {
  await chooseRecord(/Acme v\. Roadrunner/);
  pickFile(utils.container, pdf());
  await waitFor(() => expect(next()).toBeEnabled());
  fireEvent.click(next());
  await screen.findByLabelText(/Firm invoice number/i);
}

describe('InvoiceSubmissionWizard — steps', () => {
  it('Record: lists the records the callback returns and cannot advance until one is chosen', async () => {
    mount();
    expect(await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /Patent portfolio/ })).toBeInTheDocument();
    expect(next()).toBeDisabled();
    fireEvent.click(screen.getByRole('radio', { name: /Patent portfolio/ }));
    expect(next()).toBeEnabled();
  });

  it('Record: the search box narrows the list, and a note says when the list is only a prefix', async () => {
    mount({}, { loadRecords: jest.fn().mockResolvedValue({ records: [MATTER, PROJECT], truncated: true }) });
    await screen.findByRole('radio', { name: /Acme/ });
    expect(screen.getByText(/Only part of your list is shown/i)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText(/Find a record/i), { target: { value: 'patent' } });
    expect(screen.queryByRole('radio', { name: /Acme/ })).not.toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /Patent portfolio/ })).toBeInTheDocument();
  });

  it('Record: a failed load shows its message; no records shows an empty state; neither lets the user advance', async () => {
    const { unmount } = mount(
      {},
      { loadRecords: jest.fn().mockRejectedValue(new InvoiceSubmissionError({ statusCode: 403 })) }
    );
    expect(await screen.findByText(/do not have access to submit an invoice here/i)).toBeInTheDocument();
    expect(next()).toBeDisabled();
    unmount();

    mount({}, { loadRecords: jest.fn().mockResolvedValue({ records: [] }) });
    expect(await screen.findByText(/do not have a record to submit an invoice against/i)).toBeInTheDocument();
    expect(next()).toBeDisabled();
  });

  it('File: a non-PDF is refused on the client and the step cannot advance; a PDF is accepted', async () => {
    const utils = mount();
    await chooseRecord(/Acme/);
    pickFile(
      utils.container,
      new File(['x'], 'invoice.docx', {
        type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      })
    );
    expect(await screen.findByText(/invoice\.docx: .*not supported/i)).toBeInTheDocument();
    expect(next()).toBeDisabled();

    // A Word MIME type on a .pdf name is refused too (the zone alone would accept it by MIME).
    pickFile(
      utils.container,
      new File(['x'], 'renamed.pdf', {
        type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      })
    );
    expect(await screen.findByText(/renamed\.pdf: .*not supported/i)).toBeInTheDocument();
    expect(next()).toBeDisabled();

    pickFile(utils.container, pdf('good.pdf'));
    await waitFor(() => expect(next()).toBeEnabled());
    expect(screen.getByText('good.pdf')).toBeInTheDocument();
  });

  it('File: a PDF over the size limit is refused on the client', async () => {
    const utils = mount({ maxFileSizeBytes: 10 });
    await chooseRecord(/Acme/);
    pickFile(utils.container, pdf('big.pdf', '%PDF-1.4 and a lot more bytes than ten'));
    expect(await screen.findByText(/big\.pdf: /)).toBeInTheDocument();
    expect(next()).toBeDisabled();
  });

  it('Details: the firm invoice number is required (D-91); the PO number is optional', async () => {
    const utils = mount();
    await toDetails(utils);

    expect(submitButton()).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/Firm invoice number/i), { target: { value: '   ' } });
    expect(submitButton()).toBeDisabled();

    fireEvent.change(screen.getByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
    expect(screen.getByLabelText(/PO number/i)).toHaveValue('');
    expect(submitButton()).toBeEnabled();
  });
});

describe('InvoiceSubmissionWizard — submit and result', () => {
  it('submits the chosen record, the PDF and the trimmed details; then Processing, then Accepted with the tracking number', async () => {
    const getStatus = jest
      .fn()
      .mockResolvedValueOnce(view({ status: 'Processing' }))
      .mockResolvedValue(view({ status: 'Accepted' }));
    const utils = mount({ pollIntervalMs: 200 }, { getStatus });
    await toDetails(utils);
    fireEvent.change(screen.getByLabelText(/Firm invoice number/i), { target: { value: '  INV-9  ' } });
    fireEvent.change(screen.getByLabelText(/PO number/i), { target: { value: ' PO-5 ' } });
    fireEvent.click(submitButton());

    expect(await screen.findByText('Invoice received')).toBeInTheDocument();
    expect(utils.submit).toHaveBeenCalledTimes(1);
    const input = utils.submit.mock.calls[0][0];
    expect(input.record).toEqual(MATTER);
    expect(input.file.name).toBe('invoice.pdf');
    expect(input.firmInvoiceNumber).toBe('INV-9');
    expect(input.poNumber).toBe('PO-5');
    expect(utils.onSubmitted).toHaveBeenCalledWith({ submissionId: SUBMISSION_ID, trackingNumber: 'TRK-0001' });

    // Processing first (the first read says so), tracking number from the 202 already on screen.
    expect((await screen.findAllByText('Processing')).length).toBeGreaterThan(0);
    expect(screen.getByTestId('invoice-tracking-number')).toHaveTextContent('TRK-0001');
    // Then the terminal answer; polling stops.
    expect(await screen.findByText(/The invoice was accepted/i)).toBeInTheDocument();
    expect(screen.queryByText('Processing')).not.toBeInTheDocument();
    const reads = utils.getStatus.mock.calls.length;
    await act(async () => {
      await new Promise(r => setTimeout(r, 60));
    });
    expect(utils.getStatus.mock.calls.length).toBe(reads);
    expect(utils.getStatus).toHaveBeenCalledWith(SUBMISSION_ID);
  });

  it('an empty PO number is passed as undefined', async () => {
    const utils = mount();
    await toDetails(utils);
    fireEvent.change(screen.getByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
    fireEvent.click(submitButton());
    await screen.findByText('Invoice received');
    expect(utils.submit.mock.calls[0][0].poNumber).toBeUndefined();
  });

  it('Rejected shows the reason code and the report placeholder', async () => {
    const utils = mount(
      {},
      { getStatus: jest.fn().mockResolvedValue(view({ status: 'Rejected', outcomeReasonCode: 'duplicate_invoice' })) }
    );
    await toDetails(utils);
    fireEvent.change(screen.getByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
    fireEvent.click(submitButton());
    expect(await screen.findByText(/Reason: duplicate_invoice/)).toBeInTheDocument();
    expect(screen.getByTestId('invoice-report-placeholder')).toBeInTheDocument();
  });

  it('Rejected renders the host report slot instead of the placeholder when one is given', async () => {
    const utils = mount(
      { renderReport: s => <div data-testid="host-report">report for {s.trackingNumber}</div> },
      { getStatus: jest.fn().mockResolvedValue(view({ status: 'Rejected', outcomeReasonCode: 'x' })) }
    );
    await toDetails(utils);
    fireEvent.change(screen.getByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
    fireEvent.click(submitButton());
    expect(await screen.findByTestId('host-report')).toHaveTextContent('report for TRK-0001');
    expect(screen.queryByTestId('invoice-report-placeholder')).not.toBeInTheDocument();
  });

  it('Failed shows retry guidance with the tracking number', async () => {
    const utils = mount({}, { getStatus: jest.fn().mockResolvedValue(view({ status: 'Failed' })) });
    await toDetails(utils);
    fireEvent.change(screen.getByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
    fireEvent.click(submitButton());
    expect(await screen.findByText(/We could not process this invoice/i)).toBeInTheDocument();
    expect(screen.getByText(/Submit it again/i)).toHaveTextContent('TRK-0001');
  });

  it('Done closes the wizard', async () => {
    const utils = mount();
    await toDetails(utils);
    fireEvent.change(screen.getByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
    fireEvent.click(submitButton());
    fireEvent.click(await screen.findByRole('button', { name: /^done$/i }));
    expect(utils.onClose).toHaveBeenCalledTimes(1);
  });
});

describe('InvoiceSubmissionWizard — refusals do not advance the flow', () => {
  const cases: Array<[string, InvoiceSubmissionError, RegExp]> = [
    [
      '400 not_pdf (code text when the body carried no detail)',
      new InvoiceSubmissionError({ statusCode: 400, errorCode: 'ebilling.submission.not_pdf' }),
      /The file is not a PDF/i,
    ],
    [
      '400 with the server detail (the server sentence wins)',
      new InvoiceSubmissionError({
        statusCode: 400,
        errorCode: 'ebilling.submission.not_pdf',
        detail: 'Only PDF invoices are accepted here.',
      }),
      /Only PDF invoices are accepted here\./,
    ],
    [
      '403 record_forbidden',
      new InvoiceSubmissionError({ statusCode: 403, errorCode: 'ebilling.submission.record_forbidden' }),
      /do not have access to submit an invoice against this record/i,
    ],
    [
      '403 firm_forbidden',
      new InvoiceSubmissionError({ statusCode: 403, errorCode: 'ebilling.submission.firm_forbidden' }),
      /not allowed to submit invoices for this firm/i,
    ],
    [
      '413 too_large',
      new InvoiceSubmissionError({ statusCode: 413, errorCode: 'ebilling.submission.too_large' }),
      /larger than the allowed size/i,
    ],
    ['413 with the status only', new InvoiceSubmissionError({ statusCode: 413 }), /larger than the allowed size/i],
    [
      '503 ebilling.disabled',
      new InvoiceSubmissionError({ statusCode: 503, errorCode: 'ebilling.disabled' }),
      /Invoice submission is not available/i,
    ],
    [
      'a network failure with no response',
      new InvoiceSubmissionError({ message: 'Failed to fetch' }),
      /could not be confirmed.*before submitting again/i,
    ],
  ];

  it.each(cases)(
    '%s: the message shows, the user stays on Details, nothing is marked submitted',
    async (_n, err, message) => {
      const utils = mount({}, { submit: jest.fn().mockRejectedValue(err) });
      await toDetails(utils);
      fireEvent.change(screen.getByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
      fireEvent.click(submitButton());

      expect(await screen.findByText(message)).toBeInTheDocument();
      expect(screen.queryByText('Invoice received')).not.toBeInTheDocument();
      expect(utils.onSubmitted).not.toHaveBeenCalled();
      expect(utils.getStatus).not.toHaveBeenCalled();
      // Still on Details, still editable, and submit is available again.
      expect(screen.getByLabelText(/Firm invoice number/i)).toHaveValue('INV-9');
      await waitFor(() => expect(submitButton()).toBeEnabled());
    }
  );

  it('a plain Error (not a ProblemDetails refusal) is treated as unconfirmed, never as success', async () => {
    const utils = mount({}, { submit: jest.fn().mockRejectedValue(new TypeError('boom')) });
    await toDetails(utils);
    fireEvent.change(screen.getByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
    fireEvent.click(submitButton());
    expect(await screen.findByText(/could not be confirmed/i)).toBeInTheDocument();
    expect(utils.onSubmitted).not.toHaveBeenCalled();
  });
});

describe('InvoiceSubmissionWizard — phone width', () => {
  it('hides the fixed-width step list under 600px, inside the wizard wrapper only (each step carries its own caption)', async () => {
    const { container } = mount();
    await screen.findByRole('radio', { name: /Acme/ });
    // jsdom has no layout; what can be proven is the rule Griffel emitted and that the wrapper carries its class.
    const rules: string[] = [];
    for (const sheet of Array.from(document.styleSheets)) {
      for (const rule of Array.from(sheet.cssRules)) {
        if (rule.cssText.includes('Wizard steps')) rules.push(rule.cssText);
      }
    }
    expect(rules).toHaveLength(1);
    expect(rules[0]).toMatch(/@media \(max-width: 600px\)/);
    expect(rules[0]).toMatch(/display: none/);
    const wrapperClass = /\.([\w-]+) nav\[/.exec(rules[0])?.[1];
    expect(wrapperClass).toBeTruthy();
    // The class is on an element that contains the step list, so the rule reaches it.
    const wrapper = container.querySelector(`.${wrapperClass}`);
    expect(wrapper?.querySelector('nav[aria-label="Wizard steps"]')).not.toBeNull();
    expect(screen.getByText('Step 1 of 3')).toBeInTheDocument();
  });
});

describe('isEbillingDisabledError', () => {
  it('is true only for the 503 ebilling.disabled answer', () => {
    expect(
      isEbillingDisabledError(new InvoiceSubmissionError({ statusCode: 503, errorCode: 'ebilling.disabled' }))
    ).toBe(true);
    // An outage 503 carries another code and must not hide the entry.
    expect(
      isEbillingDisabledError(
        new InvoiceSubmissionError({ statusCode: 503, errorCode: 'ebilling.submission.record_unavailable' })
      )
    ).toBe(false);
    expect(isEbillingDisabledError(new InvoiceSubmissionError({ statusCode: 503 }))).toBe(false);
    expect(
      isEbillingDisabledError(new InvoiceSubmissionError({ statusCode: 403, errorCode: 'ebilling.disabled' }))
    ).toBe(false);
    expect(isEbillingDisabledError(new Error('x'))).toBe(false);
  });
});

describe('InvoiceSubmission — host independence (ADR-012)', () => {
  it('imports no SPA auth, Xrm or BFF URL', () => {
    const dir = path.resolve(__dirname, '..');
    const files = fs.readdirSync(dir).filter(f => /\.tsx?$/.test(f));
    expect(files.length).toBeGreaterThan(0);
    for (const f of files) {
      const text = fs.readFileSync(path.join(dir, f), 'utf8');
      const imports = text.match(/^\s*(?:import|export)[^;]*?from\s+['"][^'"]+['"]/gm) ?? [];
      for (const line of imports) {
        expect(line).not.toMatch(/auth|msal|bff|xrm|external-spa/i);
      }
      expect(text).not.toMatch(/window\.Xrm|\/api\/v1\//);
    }
  });
});

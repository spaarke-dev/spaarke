/**
 * InvoiceSubmissionWizard — `correction` (ebilling-r1 task 095f-2e2b; EB-D-245; spa-r4 conditions 1 and 2).
 *
 * A corrected invoice replaces an invoice that is In Review. The host reads the invoice's correction prefill and passes it; the wizard
 * prefills the firm invoice number (read-only), fixes the record and the firm to the invoice's own, requires a note, and hands `submit`
 * `correction: { invoiceId, note }`. The wizard, WizardShell and the file zone run for real; only the host's callbacks are fakes.
 *
 * Must-not-fire (spa-r4 condition 1): without `correction` the Details step has no note box, the number is editable, and `submit`
 * receives no `correction` key. The existing suites of the wizard (including `lockedRecord`) run unchanged beside this file.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { InvoiceSubmissionWizard } from '../InvoiceSubmissionWizard';
import type { InvoiceSubmissionWizardProps } from '../InvoiceSubmissionWizard';
import { InvoiceSubmissionError } from '../types';
import type { InvoiceCorrectionTarget, InvoiceCoreRecordOption } from '../types';

const MATTER: InvoiceCoreRecordOption = {
  kind: 'matter',
  recordId: '11111111-1111-4111-8111-111111111111',
  name: 'Acme v. Roadrunner',
  number: 'M-1001',
};
const OTHER: InvoiceCoreRecordOption = {
  kind: 'project',
  recordId: '22222222-2222-4222-8222-222222222222',
  name: 'Patent portfolio',
  number: 'P-77',
};
const ALPHA = { firmId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', name: 'Alpha LLP' };
const BETA = { firmId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', name: 'Beta & Co' };
const INVOICE_ID = '44444444-4444-4444-8444-444444444444';
const SUBMISSION_ID = '33333333-3333-4333-8333-333333333333';

const TARGET: InvoiceCorrectionTarget = {
  invoiceId: INVOICE_ID,
  firmInvoiceNumber: 'INV-9',
  record: MATTER,
  firmId: ALPHA.firmId,
  firmName: ALPHA.name,
  trackingNumber: 'INV-9.1',
};

function mount(over: Partial<InvoiceSubmissionWizardProps> = {}) {
  const f = {
    loadRecords: jest.fn().mockResolvedValue({ records: [MATTER] }),
    loadFirms: jest.fn().mockResolvedValue([ALPHA, BETA]),
    submit: jest.fn().mockResolvedValue({ submissionId: SUBMISSION_ID, trackingNumber: 'INV-9.2' }),
    getStatus: jest.fn().mockResolvedValue({ submissionId: SUBMISSION_ID, status: 'Accepted' }),
    onClose: jest.fn(),
    onSubmitted: jest.fn(),
  };
  const utils = renderWithProviders(
    <InvoiceSubmissionWizard
      embedded
      pollIntervalMs={10}
      title="Submit corrected invoice"
      correction={TARGET}
      loadRecords={f.loadRecords}
      loadFirms={f.loadFirms}
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
const fileInput = (container: HTMLElement) => container.querySelector('input[type="file"]') as HTMLInputElement | null;
const pdf = () => new File(['%PDF-1.4'], 'invoice.pdf', { type: 'application/pdf' });
const noteBox = () => screen.getByLabelText('Correction note') as HTMLTextAreaElement;
const typeNote = (value: string) => fireEvent.change(noteBox(), { target: { value } });

/** File step, then Next to Details. */
async function toDetails(utils: { container: HTMLElement }) {
  await waitFor(() => expect(fileInput(utils.container)).not.toBeNull());
  fireEvent.change(fileInput(utils.container)!, { target: { files: [pdf()] } });
  await waitFor(() => expect(next()).toBeEnabled());
  fireEvent.click(next());
  await screen.findByLabelText(/Firm invoice number/i);
}

describe('InvoiceSubmissionWizard — correction: what the wizard fixes', () => {
  it('prefills the number read-only, has no Record step and no firm step, and never asks for the firms', async () => {
    const utils = mount();
    await waitFor(() => expect(fileInput(utils.container)).not.toBeNull());
    // Two firms would add a "Choose firm" step; a correction keeps the invoice's own firm.
    expect(screen.queryByText(/Choose the firm this invoice is from/i)).not.toBeInTheDocument();
    expect(utils.loadFirms).not.toHaveBeenCalled();
    expect(screen.queryByRole('radiogroup', { name: 'Records' })).not.toBeInTheDocument();
    expect(screen.queryByText('Choose the record this invoice is for')).not.toBeInTheDocument();
    expect(screen.getByText('Step 1 of 2')).toBeInTheDocument();

    await toDetails(utils);
    expect(screen.getByText('Step 2 of 2')).toBeInTheDocument();
    const number = screen.getByLabelText(/Firm invoice number/i) as HTMLInputElement;
    expect(number.value).toBe('INV-9');
    expect(number).toHaveAttribute('readonly');
    expect(screen.getByText(/Replaces invoice: INV-9/)).toBeInTheDocument();
    expect(screen.getByText(/Firm: Alpha LLP/)).toBeInTheDocument();
    expect(screen.getByText(/Record: Acme v\. Roadrunner \(M-1001\) \(Matter\)/)).toBeInTheDocument();
    // The record check asks for the invoice's own firm's records.
    expect(utils.loadRecords).toHaveBeenCalledWith({ firmId: ALPHA.firmId });
  });

  it('says the invoice cannot take a correction here when its own record is not offered (the fixed locked-record message)', async () => {
    mount({ loadRecords: jest.fn().mockResolvedValue({ records: [OTHER] }) });
    expect(await screen.findByTestId('locked-record-missing')).toBeInTheDocument();
  });
});

describe('InvoiceSubmissionWizard — correction: the required note', () => {
  it('holds Submit off until there is a note, and sends the trimmed note with the invoice, the record and the firm', async () => {
    const utils = mount();
    await toDetails(utils);
    expect(submitButton()).toBeDisabled();
    expect(screen.getByTestId('correction-note-count')).toHaveTextContent('0 / 2,000 characters');

    typeNote('   ');
    expect(submitButton()).toBeDisabled();
    typeNote('  The rates were wrong.  ');
    expect(submitButton()).toBeEnabled();
    expect(screen.getByTestId('correction-note-count')).toHaveTextContent('21 / 2,000 characters');

    fireEvent.click(submitButton());
    expect(await screen.findByText('Invoice received')).toBeInTheDocument();
    expect(utils.submit).toHaveBeenCalledTimes(1);
    const input = utils.submit.mock.calls[0][0];
    expect(input.record).toEqual(MATTER);
    expect(input.firmInvoiceNumber).toBe('INV-9');
    expect(input.firmId).toBe(ALPHA.firmId);
    expect(input.correction).toEqual({ invoiceId: INVOICE_ID, note: 'The rates were wrong.' });
    expect(input.file).toBeInstanceOf(File);
    expect(utils.onSubmitted).toHaveBeenCalledTimes(1);
  });

  it('refuses a note over 2,000 characters (counted after trimming) and a control character, with the field message; 2,000 passes', async () => {
    const utils = mount();
    await toDetails(utils);

    typeNote('x'.repeat(2001));
    expect(submitButton()).toBeDisabled();
    expect(screen.getByText('The note may be at most 2,000 characters.')).toBeInTheDocument();

    typeNote(`${' '.repeat(5)}${'x'.repeat(2000)}${' '.repeat(5)}`);
    expect(submitButton()).toBeEnabled();

    typeNote('bad\u0007note');
    expect(submitButton()).toBeDisabled();
    expect(screen.getByText(/control character that cannot be saved/i)).toBeInTheDocument();

    // A line break and a tab are allowed.
    typeNote('line one\nline two\tindented');
    expect(submitButton()).toBeEnabled();
    expect(utils.submit).not.toHaveBeenCalled();
  });

  it('does not show the required message on an untouched box', async () => {
    const utils = mount();
    await toDetails(utils);
    expect(screen.queryByText('Say why this invoice is corrected.')).not.toBeInTheDocument();
    typeNote('   ');
    expect(screen.getByText('Say why this invoice is corrected.')).toBeInTheDocument();
  });
});

describe('InvoiceSubmissionWizard — correction: one request at a time (095f-2e2a K2)', () => {
  it('a double click (both clicks before the shell re-renders) sends exactly one Correction', async () => {
    let accept!: (v: unknown) => void;
    const submit = jest.fn().mockReturnValue(new Promise(resolve => (accept = resolve)));
    const utils = mount({ submit });
    await toDetails(utils);
    typeNote('Wrong rate card');

    const button = submitButton();
    await act(async () => {
      button.click();
      button.click();
    });
    expect(submit).toHaveBeenCalledTimes(1);
    // While it is out the button is disabled and says so.
    expect(screen.getByRole('button', { name: /submitting/i })).toBeDisabled();

    await act(async () => accept({ submissionId: SUBMISSION_ID, trackingNumber: 'INV-9.2' }));
    // iss048: the result heading follows the polled status; this mount's getStatus answers Accepted within the act.
    expect(await screen.findByText('Invoice accepted')).toBeInTheDocument();
    expect(submit).toHaveBeenCalledTimes(1);
  });

  it('after a refusal the user can send again (the in-flight guard is released)', async () => {
    const submit = jest
      .fn()
      .mockRejectedValueOnce(
        new InvoiceSubmissionError({ statusCode: 503, errorCode: 'ebilling.submission.record_unavailable' })
      )
      .mockResolvedValueOnce({ submissionId: SUBMISSION_ID, trackingNumber: 'INV-9.2' });
    const utils = mount({ submit });
    await toDetails(utils);
    typeNote('Wrong rate card');
    fireEvent.click(submitButton());
    await screen.findByText(/temporarily unavailable/i);
    await waitFor(() => expect(submitButton()).toBeEnabled());
    fireEvent.click(submitButton());
    expect(await screen.findByText('Invoice received')).toBeInTheDocument();
    expect(submit).toHaveBeenCalledTimes(2);
    // The note and the number were kept for the second send.
    expect(submit.mock.calls[1][0].correction.note).toBe('Wrong rate card');
  });
});

describe('InvoiceSubmissionWizard — correction: each refusal is shown in plain words, nothing advances', () => {
  const refusals: Array<[string, InvoiceSubmissionError, RegExp]> = [
    [
      'no note (400 correction_note_required)',
      new InvoiceSubmissionError({ statusCode: 400, errorCode: 'ebilling.submission.correction_note_required' }),
      /Say why this invoice is corrected\. A correction needs a note\. Nothing was saved\./,
    ],
    [
      'invoice not available (404, no code)',
      new InvoiceSubmissionError({ statusCode: 404, errorCode: 'ebilling.submission.correction_unavailable' }),
      /This invoice is not available to you, or no longer exists, so it cannot be corrected\. Nothing was saved\./,
    ],
    [
      'invoice_status',
      new InvoiceSubmissionError({
        statusCode: 409,
        errorCode: 'ebilling.submission.correction_not_allowed',
        reason: 'invoice_status',
      }),
      /Only an invoice in review can be corrected; this one has moved on, so nothing was saved\./,
    ],
    [
      'different_firm',
      new InvoiceSubmissionError({
        statusCode: 409,
        errorCode: 'ebilling.submission.correction_not_allowed',
        reason: 'different_firm',
      }),
      /belongs to another firm, so this submission cannot replace it\. Nothing was saved\./,
    ],
    [
      'different_core_record',
      new InvoiceSubmissionError({
        statusCode: 409,
        errorCode: 'ebilling.submission.correction_not_allowed',
        reason: 'different_core_record',
      }),
      /filed under another record, so this submission cannot replace it\. Nothing was saved\./,
    ],
    [
      'different_invoice_number',
      new InvoiceSubmissionError({
        statusCode: 409,
        errorCode: 'ebilling.submission.correction_not_allowed',
        reason: 'different_invoice_number',
      }),
      /keeps the firm invoice number of the invoice it replaces\. Nothing was saved\./,
    ],
    [
      'no_submission',
      new InvoiceSubmissionError({
        statusCode: 409,
        errorCode: 'ebilling.submission.correction_not_allowed',
        reason: 'no_submission',
      }),
      /has no finished submission to correct, so nothing was saved\./,
    ],
    [
      'submission_in_progress',
      new InvoiceSubmissionError({
        statusCode: 409,
        errorCode: 'ebilling.submission.correction_not_allowed',
        reason: 'submission_in_progress',
      }),
      /still being processed\. Try again when it has finished\. Nothing was saved\./,
    ],
    [
      'a reason a newer server adds',
      new InvoiceSubmissionError({
        statusCode: 409,
        errorCode: 'ebilling.submission.correction_not_allowed',
        reason: 'something_new',
      }),
      /This invoice cannot be corrected right now\. Nothing was saved\./,
    ],
    [
      'service unavailable (503)',
      new InvoiceSubmissionError({ statusCode: 503, errorCode: 'ebilling.submission.record_unavailable' }),
      /temporarily unavailable\. Try again shortly\./,
    ],
  ];

  it.each(refusals)('%s', async (_name, error, words) => {
    const submit = jest.fn().mockRejectedValue(error);
    const utils = mount({ submit });
    await toDetails(utils);
    typeNote('Wrong rate card');
    fireEvent.click(submitButton());
    expect(await screen.findByText(words)).toBeInTheDocument();
    // Stays on Details with everything kept, and can be sent again.
    expect(screen.getByLabelText(/Firm invoice number/i)).toBeInTheDocument();
    expect(noteBox().value).toBe('Wrong rate card');
    await waitFor(() => expect(submitButton()).toBeEnabled());
    expect(utils.onSubmitted).not.toHaveBeenCalled();
    expect(screen.queryByText('Invoice received')).not.toBeInTheDocument();
  });

  it("the server's own sentence wins over the fallback", async () => {
    const submit = jest.fn().mockRejectedValue(
      new InvoiceSubmissionError({
        statusCode: 409,
        errorCode: 'ebilling.submission.correction_not_allowed',
        reason: 'invoice_status',
        detail: 'Only an invoice in review can be corrected; this one has moved on, so nothing was saved.',
      })
    );
    const utils = mount({ submit });
    await toDetails(utils);
    typeNote('Wrong rate card');
    fireEvent.click(submitButton());
    expect(await screen.findByText(/this one has moved on/)).toBeInTheDocument();
  });
});

describe('InvoiceSubmissionWizard — correction composes with lockedRecord, hideTitle and the close guard (spa-r4 condition 2)', () => {
  it('SUPPORTED with lockedRecord: lockedRecord names the record (it wins), as it wins over initialRecord; the rest is a correction', async () => {
    const utils = mount({ lockedRecord: OTHER, loadRecords: jest.fn().mockResolvedValue({ records: [OTHER] }) });
    await toDetails(utils);
    expect(screen.getByText('Step 2 of 2')).toBeInTheDocument();
    typeNote('Wrong rate card');
    fireEvent.click(submitButton());
    await screen.findByText('Invoice received');
    const input = utils.submit.mock.calls[0][0];
    expect(input.record).toEqual(OTHER);
    expect(input.correction).toEqual({ invoiceId: INVOICE_ID, note: 'Wrong rate card' });
    expect(input.firmId).toBe(ALPHA.firmId);
  });

  it('with lockedRecord equal to the invoice record the result is the same as without it', async () => {
    const utils = mount({ lockedRecord: MATTER });
    await toDetails(utils);
    typeNote('Wrong rate card');
    fireEvent.click(submitButton());
    await screen.findByText('Invoice received');
    expect(utils.submit.mock.calls[0][0].record).toEqual(MATTER);
  });

  it('hideTitle: no title bar and no second close button; the note box and the submit work', async () => {
    const utils = mount({ hideTitle: true });
    await toDetails(utils);
    expect(screen.queryByRole('heading', { name: 'Submit corrected invoice' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^close$/i })).not.toBeInTheDocument();
    typeNote('Wrong rate card');
    fireEvent.click(submitButton());
    expect(await screen.findByText('Invoice received')).toBeInTheDocument();
  });

  it('without hideTitle the title bar shows the title the host gave', async () => {
    const utils = mount();
    await waitFor(() => expect(fileInput(utils.container)).not.toBeNull());
    expect(screen.getByRole('heading', { name: 'Submit corrected invoice' })).toBeInTheDocument();
  });

  it('the title-bar close is ignored while the Correction is in flight, shows the refusal after, and works once settled', async () => {
    let refuse!: (error: unknown) => void;
    const submit = jest.fn().mockReturnValue(new Promise((_resolve, reject) => (refuse = reject)));
    const utils = mount({ submit });
    await toDetails(utils);
    typeNote('Wrong rate card');
    fireEvent.click(submitButton());
    await waitFor(() => expect(submit).toHaveBeenCalledTimes(1));

    fireEvent.click(screen.getByRole('button', { name: /^close$/i }));
    expect(utils.onClose).not.toHaveBeenCalled();

    await act(async () =>
      refuse(
        new InvoiceSubmissionError({
          statusCode: 409,
          errorCode: 'ebilling.submission.correction_not_allowed',
          reason: 'invoice_status',
        })
      )
    );
    expect(await screen.findByText(/this one has moved on/i)).toBeInTheDocument();
    expect(utils.onClose).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: /^close$/i }));
    expect(utils.onClose).toHaveBeenCalledTimes(1);
  });

  it('Done on the result screen closes (the raw onClose, as without a correction)', async () => {
    const utils = mount();
    await toDetails(utils);
    typeNote('Wrong rate card');
    fireEvent.click(submitButton());
    await screen.findByText('Invoice received');
    fireEvent.click(screen.getByRole('button', { name: /^done$/i }));
    expect(utils.onClose).toHaveBeenCalledTimes(1);
  });
});

describe('InvoiceSubmissionWizard — without correction (must-not-fire)', () => {
  it('has no note box, an editable number, asks for the firms, and submit carries no correction key', async () => {
    const loadFirms = jest.fn().mockResolvedValue([ALPHA]);
    const utils = mount({ correction: undefined, lockedRecord: MATTER, loadFirms });
    await toDetails(utils);
    expect(screen.queryByLabelText('Correction note')).not.toBeInTheDocument();
    expect(screen.queryByTestId('correction-note-field')).not.toBeInTheDocument();
    expect(screen.queryByText(/Replaces invoice/)).not.toBeInTheDocument();
    const number = screen.getByLabelText(/Firm invoice number/i) as HTMLInputElement;
    expect(number.value).toBe('');
    expect(number).not.toHaveAttribute('readonly');
    expect(screen.getByText(/The number on the invoice as your firm issued it\./)).toBeInTheDocument();
    expect(loadFirms).toHaveBeenCalled();

    fireEvent.change(number, { target: { value: 'INV-5' } });
    fireEvent.click(submitButton());
    await screen.findByText('Invoice received');
    const input = utils.submit.mock.calls[0][0];
    expect(input).not.toHaveProperty('correction');
    expect(input).not.toHaveProperty('firmId');
  });
});

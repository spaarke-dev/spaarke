/**
 * InvoiceSubmissionWizard — `lockedRecord` (external-access-r4 task 010; ebilling-r1's closed test set, review 2026-11-13).
 *
 * The host fixed the record (the SPA opens the wizard from a record): there is no Record step and no picker, the firm's
 * `loadRecords` answer still decides whether the record can take an invoice, and a failed/partial/truncated read is never
 * reported as "this record can't take invoices". The wizard, WizardShell and the file zone run for real; only the host's
 * callbacks are fakes.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { InvoiceSubmissionWizard, LOCKED_RECORD_MISSING_MESSAGE } from '../InvoiceSubmissionWizard';
import type { InvoiceSubmissionWizardProps } from '../InvoiceSubmissionWizard';
import { InvoiceSubmissionError } from '../types';
import type { InvoiceCoreRecordList, InvoiceCoreRecordOption } from '../types';

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
/** Same id as MATTER, another kind: not the same record. */
const MATTER_ID_AS_PROJECT: InvoiceCoreRecordOption = { ...MATTER, kind: 'project' };
const ALPHA = { firmId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', name: 'Alpha LLP' };
const BETA = { firmId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', name: 'Beta & Co' };
const SUBMISSION_ID = '33333333-3333-4333-8333-333333333333';

function mount(over: Partial<InvoiceSubmissionWizardProps> = {}) {
  const f = {
    loadRecords: jest.fn().mockResolvedValue({ records: [MATTER, PROJECT] }),
    submit: jest.fn().mockResolvedValue({ submissionId: SUBMISSION_ID, trackingNumber: 'TRK-0001' }),
    getStatus: jest.fn().mockResolvedValue({ submissionId: SUBMISSION_ID, status: 'Accepted' }),
    onClose: jest.fn(),
    onSubmitted: jest.fn(),
  };
  const utils = renderWithProviders(
    <InvoiceSubmissionWizard
      embedded
      pollIntervalMs={10}
      lockedRecord={MATTER}
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
const back = () => screen.getByRole('button', { name: /^back$/i });
const submitButton = () => screen.getByRole('button', { name: /^submit invoice$/i });
const fileInput = (container: HTMLElement) => container.querySelector('input[type="file"]') as HTMLInputElement | null;
const pdf = () => new File(['%PDF-1.4'], 'invoice.pdf', { type: 'application/pdf' });

/** No record picker, and no record step label, anywhere on screen. */
function expectNoRecordPicker() {
  expect(screen.queryByRole('radiogroup', { name: 'Records' })).not.toBeInTheDocument();
  expect(screen.queryByText('Choose the record this invoice is for')).not.toBeInTheDocument();
  expect(screen.queryByLabelText(/Find a record/i)).not.toBeInTheDocument();
  expect(screen.queryByRole('radio', { name: /Acme|Patent/ })).not.toBeInTheDocument();
}

function deferred<T>() {
  let resolve!: (v: T) => void;
  const promise = new Promise<T>(r => {
    resolve = r;
  });
  return { promise, resolve };
}

describe('InvoiceSubmissionWizard — lockedRecord: the record is offered', () => {
  it('has no Record step, and submit carries the locked record', async () => {
    const utils = mount();
    await waitFor(() => expect(fileInput(utils.container)).not.toBeNull());
    expectNoRecordPicker();
    expect(screen.getByText('Step 1 of 2')).toBeInTheDocument();

    fireEvent.change(fileInput(utils.container)!, { target: { files: [pdf()] } });
    await waitFor(() => expect(next()).toBeEnabled());
    fireEvent.click(next());

    await screen.findByLabelText(/Firm invoice number/i);
    expect(screen.getByText('Step 2 of 2')).toBeInTheDocument();
    expect(screen.getByText(/Record: Acme v\. Roadrunner \(M-1001\) \(Matter\)/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
    fireEvent.click(submitButton());

    expect(await screen.findByText('Invoice received')).toBeInTheDocument();
    expect(utils.submit).toHaveBeenCalledTimes(1);
    expect(utils.submit.mock.calls[0][0].record).toEqual(MATTER);
    expect(utils.submit.mock.calls[0][0]).not.toHaveProperty('firmId');
    expect(utils.onSubmitted).toHaveBeenCalledTimes(1);
    // Done closes it.
    fireEvent.click(screen.getByRole('button', { name: /^done$/i }));
    expect(utils.onClose).toHaveBeenCalledTimes(1);
  });

  it('matches on kind and id: the same id under another kind is a different record (fixed message)', async () => {
    mount({ loadRecords: jest.fn().mockResolvedValue({ records: [MATTER_ID_AS_PROJECT, PROJECT] }) });
    expect(await screen.findByTestId('locked-record-missing')).toHaveTextContent(LOCKED_RECORD_MISSING_MESSAGE);
  });

  it('the id is matched without regard to case (a GUID has no case)', async () => {
    const utils = mount({
      loadRecords: jest.fn().mockResolvedValue({ records: [{ ...MATTER, recordId: MATTER.recordId.toUpperCase() }] }),
    });
    await waitFor(() => expect(fileInput(utils.container)).not.toBeNull());
    expect(screen.queryByTestId('locked-record-missing')).not.toBeInTheDocument();
  });

  it('lockedRecord wins over initialRecord: no picker, and submit carries the locked record', async () => {
    const utils = mount({ initialRecord: PROJECT });
    await waitFor(() => expect(fileInput(utils.container)).not.toBeNull());
    expectNoRecordPicker();
    fireEvent.change(fileInput(utils.container)!, { target: { files: [pdf()] } });
    await waitFor(() => expect(next()).toBeEnabled());
    fireEvent.click(next());
    await screen.findByLabelText(/Firm invoice number/i);
    fireEvent.change(screen.getByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
    fireEvent.click(submitButton());
    await screen.findByText('Invoice received');
    expect(utils.submit.mock.calls[0][0].record).toEqual(MATTER);
  });

  it('a truncated or partial list that DOES offer the record is enough (it is offered)', async () => {
    const utils = mount({
      loadRecords: jest.fn().mockResolvedValue({ records: [MATTER], truncated: true, partial: true }),
    });
    await waitFor(() => expect(fileInput(utils.container)).not.toBeNull());
    expect(screen.queryByTestId('locked-record-missing')).not.toBeInTheDocument();
  });

  it("asks the host for the firm's records (a single-firm caller: that firm)", async () => {
    const utils = mount({ loadFirms: jest.fn().mockResolvedValue([ALPHA]) });
    await waitFor(() => expect(fileInput(utils.container)).not.toBeNull());
    expect(utils.loadRecords).toHaveBeenCalledWith({ firmId: ALPHA.firmId });
  });
});

describe('InvoiceSubmissionWizard — hideTitle (a host that supplies its own header)', () => {
  it('embedded + hideTitle draws no title bar and no second close button; without it the title bar is there', async () => {
    const hidden = mount({ hideTitle: true });
    await waitFor(() => expect(fileInput(hidden.container)).not.toBeNull());
    expect(screen.queryByRole('heading', { name: 'Submit invoice' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^close$/i })).not.toBeInTheDocument();
    hidden.unmount();

    const shown = mount();
    await waitFor(() => expect(fileInput(shown.container)).not.toBeNull());
    expect(screen.getByRole('heading', { name: 'Submit invoice' })).toBeInTheDocument();
  });
});

describe('InvoiceSubmissionWizard — lockedRecord: a COMPLETE list lacks the record', () => {
  it('shows the fixed message, no picker anywhere, no upload, and never calls submit', async () => {
    const utils = mount({ loadRecords: jest.fn().mockResolvedValue({ records: [PROJECT] }) });
    expect(await screen.findByTestId('locked-record-missing')).toHaveTextContent(LOCKED_RECORD_MISSING_MESSAGE);
    expectNoRecordPicker();
    expect(fileInput(utils.container)).toBeNull();
    expect(screen.queryByRole('button', { name: /^retry$/i })).not.toBeInTheDocument();
    expect(next()).toBeDisabled();
    expect(utils.submit).not.toHaveBeenCalled();
  });

  it('an empty complete list is the same (not a "you have no record" state, no picker)', async () => {
    mount({ loadRecords: jest.fn().mockResolvedValue({ records: [] }) });
    expect(await screen.findByTestId('locked-record-missing')).toBeInTheDocument();
    expect(screen.queryByText(/No record that you can submit/i)).not.toBeInTheDocument();
    expectNoRecordPicker();
  });

  it('Cancel still closes the wizard', async () => {
    const utils = mount({ loadRecords: jest.fn().mockResolvedValue({ records: [PROJECT] }) });
    await screen.findByTestId('locked-record-missing');
    fireEvent.click(screen.getByRole('button', { name: /^cancel$/i }));
    expect(utils.onClose).toHaveBeenCalledTimes(1);
  });
});

describe('InvoiceSubmissionWizard — lockedRecord: the read is not complete (never the fixed message)', () => {
  const cases: Array<[string, () => Promise<InvoiceCoreRecordList>, RegExp]> = [
    [
      'partial',
      () => Promise.resolve({ records: [PROJECT], partial: true }),
      /could not be loaded, so this list is incomplete/i,
    ],
    [
      'truncated',
      () => Promise.resolve({ records: [PROJECT], truncated: true }),
      /Only the first part of your list could be read/i,
    ],
    [
      'an empty partial list',
      () => Promise.resolve({ records: [], partial: true }),
      /could not be loaded, so this list is incomplete/i,
    ],
    [
      'a thrown read',
      () => Promise.reject(new InvoiceSubmissionError({ statusCode: 403 })),
      /not allowed|permission|access/i,
    ],
  ];

  it.each(cases)(
    '%s: the read message with Retry, no fixed message, no picker, no submit',
    async (_n, load, message) => {
      const utils = mount({ loadFirms: jest.fn().mockResolvedValue([ALPHA, BETA]), loadRecords: jest.fn(load) });
      fireEvent.click(await screen.findByRole('radio', { name: 'Alpha LLP' }));
      fireEvent.click(next());
      expect(await screen.findByRole('button', { name: /^retry$/i })).toBeInTheDocument();
      expect(screen.getByRole('alert')).toHaveTextContent(message);
      expect(screen.queryByTestId('locked-record-missing')).not.toBeInTheDocument();
      expect(screen.queryByText(LOCKED_RECORD_MISSING_MESSAGE)).not.toBeInTheDocument();
      expectNoRecordPicker();
      expect(fileInput(utils.container)).toBeNull();
      // Back (to the firm step) and Cancel are there; Next is not.
      expect(back()).toBeEnabled();
      expect(screen.getByRole('button', { name: /^cancel$/i })).toBeEnabled();
      expect(next()).toBeDisabled();
      expect(utils.submit).not.toHaveBeenCalled();
    }
  );

  it('Retry asks the host again, and a good answer opens the upload', async () => {
    const loadRecords = jest
      .fn()
      .mockResolvedValueOnce({ records: [PROJECT], partial: true })
      .mockResolvedValueOnce({ records: [MATTER, PROJECT] });
    const utils = mount({ loadRecords });
    fireEvent.click(await screen.findByRole('button', { name: /^retry$/i }));
    await waitFor(() => expect(fileInput(utils.container)).not.toBeNull());
    expect(loadRecords).toHaveBeenCalledTimes(2);
    expect(screen.queryByRole('button', { name: /^retry$/i })).not.toBeInTheDocument();
  });

  it('Retry that finds a complete list without the record turns into the fixed message', async () => {
    const loadRecords = jest
      .fn()
      .mockRejectedValueOnce(new InvoiceSubmissionError({ statusCode: 500 }))
      .mockResolvedValueOnce({ records: [PROJECT] });
    mount({ loadRecords });
    fireEvent.click(await screen.findByRole('button', { name: /^retry$/i }));
    expect(await screen.findByTestId('locked-record-missing')).toBeInTheDocument();
  });
});

describe('InvoiceSubmissionWizard — lockedRecord: every firm change re-checks', () => {
  /** Alpha's list has the record; Beta's does not (complete). */
  const byFirm = (query: { firmId?: string }) =>
    Promise.resolve({ records: query.firmId === ALPHA.firmId ? [MATTER] : [PROJECT] });

  it('a firm that lacks the record shows the fixed message; switching to a firm that has it clears it, and back again re-shows it', async () => {
    const loadRecords = jest.fn(byFirm);
    const utils = mount({ loadFirms: jest.fn().mockResolvedValue([ALPHA, BETA]), loadRecords });
    // Beta: the record is not offered.
    fireEvent.click(await screen.findByRole('radio', { name: 'Beta & Co' }));
    fireEvent.click(next());
    expect(await screen.findByTestId('locked-record-missing')).toBeInTheDocument();
    expect(loadRecords).toHaveBeenLastCalledWith({ firmId: BETA.firmId });

    // Back, Alpha: cleared, the upload opens.
    fireEvent.click(back());
    fireEvent.click(await screen.findByRole('radio', { name: 'Alpha LLP' }));
    fireEvent.click(next());
    await waitFor(() => expect(fileInput(utils.container)).not.toBeNull());
    expect(screen.queryByTestId('locked-record-missing')).not.toBeInTheDocument();
    expect(loadRecords).toHaveBeenLastCalledWith({ firmId: ALPHA.firmId });

    // Back, Beta again: re-checked, missing again.
    fireEvent.click(back());
    fireEvent.click(await screen.findByRole('radio', { name: 'Beta & Co' }));
    fireEvent.click(next());
    expect(await screen.findByTestId('locked-record-missing')).toBeInTheDocument();
    expect(utils.submit).not.toHaveBeenCalled();
  });

  it("another firm's list never counts while the new firm's answer is pending", async () => {
    const beta = deferred<InvoiceCoreRecordList>();
    const loadRecords = jest.fn((q: { firmId?: string }) =>
      q.firmId === ALPHA.firmId ? Promise.resolve({ records: [MATTER] }) : beta.promise
    );
    const utils = mount({ loadFirms: jest.fn().mockResolvedValue([ALPHA, BETA]), loadRecords });

    fireEvent.click(await screen.findByRole('radio', { name: 'Alpha LLP' }));
    fireEvent.click(next());
    await waitFor(() => expect(fileInput(utils.container)).not.toBeNull());

    // Switch to Beta, whose answer has not arrived: Alpha's list (which offered the record) must not unlock the upload.
    fireEvent.click(back());
    fireEvent.click(await screen.findByRole('radio', { name: 'Beta & Co' }));
    fireEvent.click(next());
    expect(await screen.findByText(/Checking this record/i)).toBeInTheDocument();
    expect(fileInput(utils.container)).toBeNull();
    expect(next()).toBeDisabled();

    // Beta answers with a complete list lacking the record.
    await act(async () => {
      beta.resolve({ records: [PROJECT] });
    });
    expect(await screen.findByTestId('locked-record-missing')).toBeInTheDocument();
  });

  it('a file chosen under one firm is kept, but a firm without the record still cannot submit it', async () => {
    const utils = mount({ loadFirms: jest.fn().mockResolvedValue([ALPHA, BETA]), loadRecords: jest.fn(byFirm) });
    fireEvent.click(await screen.findByRole('radio', { name: 'Alpha LLP' }));
    fireEvent.click(next());
    await waitFor(() => expect(fileInput(utils.container)).not.toBeNull());
    fireEvent.change(fileInput(utils.container)!, { target: { files: [pdf()] } });
    await waitFor(() => expect(next()).toBeEnabled());
    fireEvent.click(back());
    fireEvent.click(await screen.findByRole('radio', { name: 'Beta & Co' }));
    fireEvent.click(next());
    await screen.findByTestId('locked-record-missing');
    expect(next()).toBeDisabled();
    expect(utils.submit).not.toHaveBeenCalled();
  });
});

describe('InvoiceSubmissionWizard — lockedRecord: a firm_required refusal is explained (review 1, F1)', () => {
  const firmRequired = () =>
    new InvoiceSubmissionError({
      statusCode: 400,
      errorCode: 'ebilling.submission.firm_required',
      message: 'Your account belongs to more than one firm and the firm could not be chosen automatically.',
    });

  async function toDetails(utils: ReturnType<typeof mount>) {
    await waitFor(() => expect(fileInput(utils.container)).not.toBeNull());
    fireEvent.change(fileInput(utils.container)!, { target: { files: [pdf()] } });
    await waitFor(() => expect(next()).toBeEnabled());
    fireEvent.click(next());
    await screen.findByLabelText(/Firm invoice number/i);
    fireEvent.change(screen.getByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
  }

  it("shows the server's message on the File step the user lands on (there is no Record step to carry it)", async () => {
    const loadFirms = jest.fn().mockResolvedValue(null);
    const submit = jest.fn().mockRejectedValue(firmRequired());
    const utils = mount({ loadFirms, submit });
    await toDetails(utils);
    fireEvent.click(submitButton());

    const notice = await screen.findByTestId('locked-record-notice');
    expect(notice).toHaveTextContent(/more than one firm and the firm could not be chosen automatically/i);
    expect(notice).toHaveAttribute('role', 'alert');
    expect(screen.getByText('Step 1 of 2')).toBeInTheDocument();
    expect(loadFirms).toHaveBeenCalledTimes(2);
    expect(utils.onSubmitted).not.toHaveBeenCalled();
    // The locked record is still the only record; nothing re-opened a picker.
    expectNoRecordPicker();
  });

  it('must-not-fire: no refusal, no notice bar on the File step', async () => {
    const utils = mount({ loadFirms: jest.fn().mockResolvedValue(null) });
    await waitFor(() => expect(fileInput(utils.container)).not.toBeNull());
    expect(screen.queryByTestId('locked-record-notice')).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('must-not-fire: another refusal stays on Details (WizardShell shows it), never the File-step notice', async () => {
    const submit = jest
      .fn()
      .mockRejectedValue(
        new InvoiceSubmissionError({ statusCode: 403, errorCode: 'ebilling.submission.firm_forbidden' })
      );
    const utils = mount({ loadFirms: jest.fn().mockResolvedValue(null), submit });
    await toDetails(utils);
    fireEvent.click(submitButton());
    await waitFor(() => expect(submit).toHaveBeenCalledTimes(1));
    expect(screen.queryByTestId('locked-record-notice')).not.toBeInTheDocument();
    expect(screen.getByLabelText(/Firm invoice number/i)).toBeInTheDocument();
  });
});

describe('InvoiceSubmissionWizard — lockedRecord: step captions', () => {
  it('no firm step: "Step n of 2"', async () => {
    const utils = mount();
    await waitFor(() => expect(fileInput(utils.container)).not.toBeNull());
    expect(screen.getByText('Step 1 of 2')).toBeInTheDocument();
    fireEvent.change(fileInput(utils.container)!, { target: { files: [pdf()] } });
    await waitFor(() => expect(next()).toBeEnabled());
    fireEvent.click(next());
    await screen.findByLabelText(/Firm invoice number/i);
    expect(screen.getByText('Step 2 of 2')).toBeInTheDocument();
  });

  it('with the firm step: "Step n of 3"', async () => {
    const utils = mount({ loadFirms: jest.fn().mockResolvedValue([ALPHA, BETA]) });
    expect(await screen.findByText('Step 1 of 3')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('radio', { name: 'Alpha LLP' }));
    fireEvent.click(next());
    await waitFor(() => expect(fileInput(utils.container)).not.toBeNull());
    expect(screen.getByText('Step 2 of 3')).toBeInTheDocument();
    fireEvent.change(fileInput(utils.container)!, { target: { files: [pdf()] } });
    await waitFor(() => expect(next()).toBeEnabled());
    fireEvent.click(next());
    await screen.findByLabelText(/Firm invoice number/i);
    expect(screen.getByText('Step 3 of 3')).toBeInTheDocument();
    expect(screen.getByText('Firm: Alpha LLP')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
    fireEvent.click(submitButton());
    await screen.findByText('Invoice received');
    expect(utils.submit.mock.calls[0][0]).toMatchObject({ record: MATTER, firmId: ALPHA.firmId });
  });

  it('phone width: the captions are the in-step text (the step list is hidden by CSS), so they read the same', async () => {
    const original = window.innerWidth;
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 375 });
    try {
      const utils = mount();
      await waitFor(() => expect(fileInput(utils.container)).not.toBeNull());
      expect(screen.getByText('Step 1 of 2')).toBeInTheDocument();
      expect(screen.queryByText(/of 3/)).not.toBeInTheDocument();
    } finally {
      Object.defineProperty(window, 'innerWidth', { configurable: true, value: original });
    }
  });

  it('control: without lockedRecord the captions and the Record step are as before', async () => {
    mount({ lockedRecord: undefined });
    expect(await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ })).toBeInTheDocument();
    expect(screen.getByText('Step 1 of 3')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^retry$/i })).not.toBeInTheDocument();
  });
});

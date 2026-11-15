/**
 * Task 134 slice B2 (ebilling 095f, EB-D-225): the Correction Note in the Outlook "Submit Invoice" view. A correction
 * needs a note saying why the invoice is corrected (required, at most 2000 characters after trimming, no control
 * character except \n, \r, \t); a new invoice has no such field and its input carries none. The note is kept in the form
 * so a Retry resends it, and a changed note is a new attempt. Assertions are on visible elements, not the sr-only live
 * region (the pane's error text appears twice).
 */
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { InvoiceView, type InvoiceViewProps } from '../InvoiceView';
import { describeSubmissionRefusal } from '../../../utils/invoiceSubmissionMessages';
import { searchOfficeEntities } from '../../../services/officeEntitySearch';
import type { IHostAdapter } from '@shared/adapters';
import type { AttachmentInfo } from '@shared/adapters/types';
import type { EntitySearchResult } from '../../../hooks/useEntitySearch';
import type { InvoiceCorrectionPrefill } from '../../../services/invoiceSubmissionSeam';

jest.mock('../../../services/officeEntitySearch', () => ({ searchOfficeEntities: jest.fn() }));
const mockSearch = searchOfficeEntities as jest.MockedFunction<typeof searchOfficeEntities>;
jest.mock('../../../services/communicationSuggestionsService', () => ({
  fetchRelatedCandidates: jest.fn(async () => []),
}));

const TIMEOUT = 30000;
const MATTER_ID = '6f1c0e8a-2b1d-4c7e-9a52-0d3f1b7e9c41';
const FIRM_ID = '8b0c6f1e-4c2a-4f7e-9a51-2f3d1c0e9b77';
const INVOICE_ID = '3c9d2b1a-7e4f-4a6b-8c2d-1e0f9a8b7c65';

const ATTACHMENTS: AttachmentInfo[] = [
  {
    id: 'att-1',
    name: 'INV-4711.pdf',
    contentType: 'application/pdf',
    size: 182344,
    isInline: false,
    attachmentType: 'file',
  },
];

const MATTER: EntitySearchResult = {
  id: MATTER_ID,
  entityType: 'Matter',
  logicalName: 'sprk_matter',
  name: 'Smith v Jones',
  displayInfo: 'MAT-1',
  canFile: true,
};
const INVOICE: EntitySearchResult = {
  id: INVOICE_ID,
  entityType: 'Invoice',
  logicalName: 'sprk_invoice',
  name: 'INV-012',
};
const OTHER_INVOICE: EntitySearchResult = {
  id: 'aa11bb22-0000-4000-8000-000000000013',
  entityType: 'Invoice',
  logicalName: 'sprk_invoice',
  name: 'INV-013',
};
const PREFILL: InvoiceCorrectionPrefill = {
  invoiceId: INVOICE_ID,
  firm: { id: FIRM_ID, name: 'Morrison Foerster' },
  coreRecord: { entityType: 'Matter', entityId: MATTER_ID, name: 'PAT-792345' },
  invoiceStatus: 'InReview',
  trackingNumber: 'INV-012.2',
  firmInvoiceNumber: 'INV-012',
  canCorrect: true,
  reason: null,
  replacedBy: null,
};

function adapter(): IHostAdapter {
  return {
    getCapabilities: () => ({ canGetAttachments: true }),
    getAttachments: jest.fn(async () => ATTACHMENTS),
  } as unknown as IHostAdapter;
}

function renderView(props: Partial<InvoiceViewProps> = {}) {
  const onSubmit = jest.fn(async () => ({ kind: 'success' as const, trackingNumber: 'FIRMF-000123' }));
  const all = {
    hostAdapter: adapter(),
    apiBaseUrl: 'https://bff',
    getAccessToken: async () => 'token',
    onSubmit,
    loadFirms: jest.fn(async () => [{ firmId: FIRM_ID, name: 'Firm F' }]),
    correctionAvailable: true,
    loadCorrectionPrefill: jest.fn(async (id: string) => ({ ...PREFILL, invoiceId: id })),
    ...props,
  } as InvoiceViewProps;
  render(
    <FluentProvider theme={webLightTheme}>
      <InvoiceView {...all} />
    </FluentProvider>
  );
  return { onSubmit: all.onSubmit as jest.Mock };
}

const submitButton = () => screen.getByRole('button', { name: /^(Submit|Submitting…|Processing…|Submitted)$/ });
const noteBox = () => screen.getByRole('textbox', { name: /Correction note/ });
const queryNoteBox = () => screen.queryByRole('textbox', { name: /Correction note/ });

async function chooseInvoice(invoice: EntitySearchResult) {
  mockSearch.mockResolvedValue([invoice]);
  // A record already chosen is cleared first (the picker then shows its search box again).
  const clear = screen.queryByRole('button', { name: 'Clear selection' });
  if (clear) await userEvent.click(clear);
  await userEvent.clear(screen.getByLabelText('Search Invoice records'));
  await userEvent.type(screen.getByLabelText('Search Invoice records'), 'INV');
  await userEvent.click(screen.getByRole('button', { name: 'Search' }));
  await userEvent.click(await screen.findByRole('button', { name: 'Select this record' }));
}

async function chooseCorrection() {
  await screen.findByText('INV-4711.pdf');
  await userEvent.click(screen.getByRole('radio', { name: 'Existing invoice' }));
  await chooseInvoice(INVOICE);
  await userEvent.click(screen.getByRole('radio', { name: 'Submit a corrected invoice' }));
  await screen.findByTestId('invoice-correction-details');
}

/** Sets the whole note in one change (typing 2000+ characters one key at a time is needlessly slow). */
function setNote(value: string) {
  fireEvent.change(noteBox(), { target: { value } });
}

beforeEach(() => {
  mockSearch.mockReset();
  delete process.env.ORG_URL;
});
afterEach(() => jest.restoreAllMocks());

describe('InvoiceView — the Correction Note field (095f)', () => {
  it(
    'must-fire: correction mode shows a required multi-line "Correction note" field with its hint and a 0 / 2000 counter',
    async () => {
      renderView();
      await chooseCorrection();
      const box = noteBox();
      expect(box.tagName).toBe('TEXTAREA');
      expect(box).toBeRequired();
      expect(screen.getByText('Why this invoice is corrected.')).toBeInTheDocument();
      expect(screen.getByTestId('invoice-correction-note-count')).toHaveTextContent('0 / 2000');
    },
    TIMEOUT
  );

  it(
    'must-fire: Submit is disabled with an empty note and with a blank (whitespace-only) one',
    async () => {
      const { onSubmit } = renderView();
      await chooseCorrection();
      // The form is otherwise ready: the invoice's own values are loaded and the PDF is marked.
      await waitFor(() =>
        expect(within(screen.getByTestId('invoice-correction-details')).getByText(/Firm:/)).toBeVisible()
      );
      expect(submitButton()).toBeDisabled();
      setNote('   \n\t  ');
      expect(submitButton()).toBeDisabled();
      expect(screen.getByTestId('invoice-correction-note-count')).toHaveTextContent('0 / 2000');
      expect(onSubmit).not.toHaveBeenCalled();
    },
    TIMEOUT
  );

  it(
    'must-fire: with a note Submit is enabled and the input carries the TRIMMED note, mode Correction',
    async () => {
      const { onSubmit } = renderView();
      await chooseCorrection();
      setNote('  Wrong rate on line 3.  ');
      expect(screen.getByTestId('invoice-correction-note-count')).toHaveTextContent('21 / 2000');
      await waitFor(() => expect(submitButton()).toBeEnabled());
      await userEvent.click(submitButton());
      await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
      const [input] = onSubmit.mock.calls[0] as unknown as [Record<string, unknown>];
      expect(input).toMatchObject({
        mode: 'Correction',
        invoiceId: INVOICE_ID,
        correctionNote: 'Wrong rate on line 3.',
      });
    },
    TIMEOUT
  );

  it(
    'must-fire: a note of 2001 trimmed characters is blocked, with a visible message, and 2000 is allowed again',
    async () => {
      const { onSubmit } = renderView();
      await chooseCorrection();
      setNote('x'.repeat(2001));
      expect(screen.getByTestId('invoice-correction-note-count')).toHaveTextContent('2001 / 2000');
      expect(await screen.findByText('The correction note can be at most 2000 characters.')).toBeVisible();
      expect(submitButton()).toBeDisabled();
      setNote('x'.repeat(2000));
      expect(screen.queryByText('The correction note can be at most 2000 characters.')).toBeNull();
      await waitFor(() => expect(submitButton()).toBeEnabled());
      expect(onSubmit).not.toHaveBeenCalled();
    },
    TIMEOUT
  );

  it(
    'must-fire: a control character other than a line break or a tab blocks Submit with a visible message',
    async () => {
      renderView();
      await chooseCorrection();
      setNote('Wrong rate\u0007 on line 3.');
      expect(
        await screen.findByText('The correction note has a character that cannot be sent. Remove it.')
      ).toBeVisible();
      expect(submitButton()).toBeDisabled();
      setNote('Wrong rate on line 3.');
      await waitFor(() => expect(submitButton()).toBeEnabled());
    },
    TIMEOUT
  );

  it(
    'must-not-fire: whitespace around a 2000-character note does not block it (the length is the trimmed one)',
    async () => {
      const { onSubmit } = renderView();
      await chooseCorrection();
      const note = 'y'.repeat(2000);
      setNote(`   \n${note}\n\n   `);
      expect(screen.getByTestId('invoice-correction-note-count')).toHaveTextContent('2000 / 2000');
      await waitFor(() => expect(submitButton()).toBeEnabled());
      await userEvent.click(submitButton());
      await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
      expect((onSubmit.mock.calls[0] as unknown as [Record<string, unknown>])[0]).toMatchObject({
        correctionNote: note,
      });
    },
    TIMEOUT
  );

  it(
    'must-not-fire: line breaks and tabs inside the note are allowed and sent as typed',
    async () => {
      const { onSubmit } = renderView();
      await chooseCorrection();
      setNote('Line one\nLine two\tafter a tab');
      expect(screen.queryByText(/cannot be sent/)).toBeNull();
      await waitFor(() => expect(submitButton()).toBeEnabled());
      await userEvent.click(submitButton());
      await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
      expect((onSubmit.mock.calls[0] as unknown as [Record<string, unknown>])[0]).toMatchObject({
        correctionNote: 'Line one\nLine two\tafter a tab',
      });
    },
    TIMEOUT
  );

  it(
    'must-not-fire: New mode shows no note field and its input has no correctionNote key',
    async () => {
      const { onSubmit } = renderView();
      await screen.findByText('INV-4711.pdf');
      expect(queryNoteBox()).toBeNull();
      expect(screen.queryByText('Correction note')).toBeNull();
      // Submit a new invoice end to end.
      mockSearch.mockResolvedValue([MATTER]);
      await userEvent.type(screen.getByLabelText('Search Matter records'), 'Smith');
      await userEvent.click(screen.getByRole('button', { name: 'Search' }));
      await userEvent.click(await screen.findByRole('button', { name: 'Select this record' }));
      await userEvent.type(await screen.findByRole('textbox', { name: /Firm invoice number/ }), 'INV-4711');
      await waitFor(() => expect(submitButton()).toBeEnabled());
      expect(queryNoteBox()).toBeNull();
      await userEvent.click(submitButton());
      await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
      const [input] = onSubmit.mock.calls[0] as unknown as [Record<string, unknown>];
      expect(input.mode).toBe('New');
      expect(Object.prototype.hasOwnProperty.call(input, 'correctionNote')).toBe(false);
    },
    TIMEOUT
  );
});

describe('InvoiceView — the note across a Retry (095f)', () => {
  const retryable = () => jest.fn(async () => ({ kind: 'retryable' as const, message: 'Outlook is busy. Try again.' }));

  it(
    'must-fire: a Retry resends the same note under the same action id (the X-Idempotency-Key)',
    async () => {
      const onSubmit = retryable();
      renderView({ onSubmit });
      await chooseCorrection();
      setNote('The quantity was 4, not 40.');
      await waitFor(() => expect(submitButton()).toBeEnabled());
      await userEvent.click(submitButton());
      await userEvent.click(await screen.findByRole('button', { name: 'Retry' }));
      await userEvent.click(await screen.findByRole('button', { name: 'Retry' }));
      await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(3));
      const calls = onSubmit.mock.calls as unknown as Array<[Record<string, unknown>, { actionId: string }]>;
      for (const [input, attempt] of calls) {
        expect(input.correctionNote).toBe('The quantity was 4, not 40.');
        expect(attempt.actionId).toBe(calls[0]![1].actionId);
      }
      // The note is still in the form after the failures.
      expect(noteBox()).toHaveValue('The quantity was 4, not 40.');
    },
    TIMEOUT
  );

  it(
    'a changed note ends the Retry: the next Submit is a new attempt (new action id) carrying the new note',
    async () => {
      const onSubmit = retryable();
      renderView({ onSubmit });
      await chooseCorrection();
      setNote('First reason.');
      await waitFor(() => expect(submitButton()).toBeEnabled());
      await userEvent.click(submitButton());
      expect(await screen.findByRole('button', { name: 'Retry' })).toBeInTheDocument();
      setNote('Second reason.');
      expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
      await userEvent.click(submitButton());
      await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(2));
      const calls = onSubmit.mock.calls as unknown as Array<[Record<string, unknown>, { actionId: string }]>;
      expect(calls[0]![0].correctionNote).toBe('First reason.');
      expect(calls[1]![0].correctionNote).toBe('Second reason.');
      expect(calls[1]![1].actionId).not.toBe(calls[0]![1].actionId);
    },
    TIMEOUT
  );

  it(
    'a prefill reload ("Check again") keeps the note that was typed',
    async () => {
      const loadCorrectionPrefill = jest
        .fn()
        .mockResolvedValueOnce({ ...PREFILL, firm: null })
        .mockResolvedValue(PREFILL);
      renderView({ loadCorrectionPrefill });
      await chooseCorrection();
      // The invoice has no firm yet (the pane says to set it in Spaarke); the note can already be written.
      setNote('Kept across a reload.');
      await userEvent.click(await screen.findByTestId('invoice-correction-no-firm-recheck'));
      await waitFor(() => expect(loadCorrectionPrefill).toHaveBeenCalledTimes(2));
      await waitFor(() => expect(screen.queryByTestId('invoice-correction-no-firm')).toBeNull());
      expect(noteBox()).toHaveValue('Kept across a reload.');
    },
    TIMEOUT
  );
});

describe('InvoiceView — the note resets with the correction (095f)', () => {
  it(
    'must-fire: choosing another invoice, or leaving correction mode, clears the note',
    async () => {
      renderView();
      await chooseCorrection();
      setNote('A reason for INV-012.');
      expect(noteBox()).toHaveValue('A reason for INV-012.');

      // Another invoice: the note belongs to the first one.
      await chooseInvoice(OTHER_INVOICE);
      await screen.findByTestId('invoice-correction-details');
      await waitFor(() => expect(noteBox()).toHaveValue(''));

      // Back to a new invoice and again to a correction: no note carried over.
      setNote('A reason for INV-013.');
      await userEvent.click(screen.getByRole('radio', { name: 'New invoice' }));
      expect(queryNoteBox()).toBeNull();
      await userEvent.click(screen.getByRole('radio', { name: 'Existing invoice' }));
      await userEvent.click(screen.getByRole('radio', { name: 'Submit a corrected invoice' }));
      await screen.findByTestId('invoice-correction-details');
      expect(noteBox()).toHaveValue('');
    },
    TIMEOUT
  );
});

describe('InvoiceView — correction_note_required (095f)', () => {
  it(
    'must-fire: the refusal shows its sentence on screen, with the note kept for another try',
    async () => {
      const message = describeSubmissionRefusal({
        kind: 'refused',
        status: 400,
        code: 'ebilling.submission.correction_note_required',
      });
      const onSubmit = jest.fn(async () => ({ kind: 'refused' as const, message }));
      renderView({ onSubmit });
      await chooseCorrection();
      setNote('Why.');
      await waitFor(() => expect(submitButton()).toBeEnabled());
      await userEvent.click(submitButton());
      // Visible: the MessageBar (role alert); the sr-only live region says the same and is ignored here.
      const bar = (await screen.findByText('The invoice was not submitted')).closest('[role="alert"]') as HTMLElement;
      expect(bar).toHaveTextContent(
        'Enter a correction note saying why the invoice is corrected. Nothing was submitted.'
      );
      expect(noteBox()).toHaveValue('Why.');
    },
    TIMEOUT
  );
});

/**
 * SubmitInvoiceFromEmail.test.tsx (ebilling-r1 task 118)
 *
 * The one flow behind the toolbar, the flagged attachment and the Email Review "Invoice" tab. The transport is a
 * stand-in object (the flow does no I/O of its own); the marking component, the outcome screen and the Fluent
 * controls are the real ones.
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

// `@spaarke/ui-components` (mapped to source) reaches `@spaarke/auth`, which this package never initializes.
jest.mock('@spaarke/auth', () => ({ __esModule: true, authenticatedFetch: jest.fn() }));

import { InvoiceSubmissionError } from '@spaarke/ui-components';
import { SubmitInvoiceFromEmail, initialMarking, type SubmitInvoiceFromEmailProps } from '../SubmitInvoiceFromEmail';
import { hasPdfAttachment } from '../invoiceAttachments';
import { SubmitInvoiceDialog } from '../SubmitInvoiceDialog';
import { InvoiceSubmitTab } from '../../ReconcileTabs/InvoiceSubmitTab';
import { SUBMIT_NOT_FOUND_TEXT, type EmailInvoiceSubmission } from '../../../logic/invoice';
import type { MarkableAttachment } from '../../InvoiceAttachmentMarking';

if (typeof globalThis.ResizeObserver === 'undefined') {
  // jsdom has no ResizeObserver; `MessageBar` needs one to mount.
  (globalThis as unknown as { ResizeObserver: unknown }).ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}

const COMM = '11111111-1111-1111-1111-111111111111';
const MATTER = {
  kind: 'matter' as const,
  recordId: '22222222-2222-2222-2222-222222222222',
  name: 'Acme v Beta',
  number: 'M-1',
};
const FIRM = { firmId: '33333333-3333-3333-3333-333333333333', name: 'Smith LLP' };

const pdf = (id: string, extra: Partial<MarkableAttachment> = {}): MarkableAttachment => ({
  id,
  fileName: `${id}.pdf`,
  ...extra,
});

function makeSubmission(over: Partial<EmailInvoiceSubmission> = {}): jest.Mocked<EmailInvoiceSubmission> {
  return {
    loadAttachments: jest.fn().mockResolvedValue([pdf('inv'), pdf('sup'), { id: 'img', fileName: 'scan.png' }]),
    loadRecords: jest.fn().mockResolvedValue({ records: [MATTER] }),
    loadFirms: jest.fn().mockResolvedValue([FIRM]),
    submit: jest.fn().mockResolvedValue({ submissionId: 'sub-1', trackingNumber: 'TRK-100', statusUrl: null }),
    getStatus: jest.fn().mockResolvedValue({ submissionId: 'sub-1', status: 'Accepted', trackingNumber: 'TRK-100' }),
    ...over,
  } as jest.Mocked<EmailInvoiceSubmission>;
}

function renderFlow(
  submission: EmailInvoiceSubmission,
  props: Partial<SubmitInvoiceFromEmailProps> = {},
  theme = webLightTheme
) {
  return render(
    <FluentProvider theme={theme}>
      <SubmitInvoiceFromEmail communicationId={COMM} submission={submission} initialRecord={MATTER} {...props} />
    </FluentProvider>
  );
}

/** The role radio of one attachment row. */
const radio = (fileName: string, label: 'Invoice' | 'Supporting' | "Don't save") =>
  within(screen.getByRole('radiogroup', { name: fileName })).getByRole('radio', { name: label }) as HTMLInputElement;

const submitButton = () => screen.getByTestId('submit-invoice-submit') as HTMLButtonElement;

async function ready() {
  await waitFor(() => expect(screen.getByText('inv.pdf')).toBeInTheDocument());
  // record chosen (initialRecord) and its firm loaded
  await waitFor(() => expect(screen.getByTestId('submit-invoice-firm')).toBeInTheDocument());
}

function fillNumber(value = 'INV-2026-001') {
  fireEvent.change(screen.getByRole('textbox', { name: /Firm invoice number/ }), { target: { value } });
}

describe('initialMarking (pure)', () => {
  const list = [pdf('a'), pdf('b'), { id: 'c', fileName: 'c.docx' }];

  it('two PDFs: none pre-selected, every attachment Supporting', () => {
    expect(initialMarking(list)).toEqual({ a: 'supporting', b: 'supporting', c: 'supporting' });
  });

  it('a named PDF opens marked Invoice, the rest Supporting', () => {
    expect(initialMarking(list, 'b')).toEqual({ a: 'supporting', b: 'invoice', c: 'supporting' });
  });

  it('a named attachment replaces the default pick instead of adding a second Invoice', () => {
    expect(initialMarking([pdf('a', { suggestedInvoice: true }), pdf('b')], 'b')).toEqual({
      a: 'supporting',
      b: 'invoice',
    });
  });

  it('negative: a named non-PDF, or an unknown id, leaves the shared default in place', () => {
    expect(initialMarking(list, 'c')).toEqual(initialMarking(list));
    expect(initialMarking(list, 'zzz')).toEqual(initialMarking(list));
  });

  it('a named attachment that is disabled is not marked Invoice', () => {
    const locked = [pdf('a', { disabledReason: 'no document' }), pdf('b')];
    expect(initialMarking(locked, 'a').a).toBe('skip');
  });
});

describe('hasPdfAttachment (pure)', () => {
  it('is true with a PDF by file name, false with none, for a row name or a markable fileName', () => {
    expect(hasPdfAttachment([{ name: 'a.docx' }, { name: 'B.PDF' }])).toBe(true);
    expect(hasPdfAttachment([{ fileName: 'a.pdf' }])).toBe(true);
    expect(hasPdfAttachment([{ name: 'a.docx' }, { name: 'logo.png' }, { name: null }])).toBe(false);
    expect(hasPdfAttachment([])).toBe(false);
    expect(hasPdfAttachment(undefined)).toBe(false);
  });
});

describe('SubmitInvoiceFromEmail', () => {
  it('opens with the single PDF candidate marked Invoice and the rest Supporting (the shared default)', async () => {
    // inv.pdf and sup.pdf are both PDFs: two candidates, so nothing is pre-selected.
    renderFlow(makeSubmission());
    await ready();
    expect(radio('inv.pdf', 'Supporting')).toBeChecked();
    expect(radio('sup.pdf', 'Supporting')).toBeChecked();
    expect(radio('scan.png', 'Supporting')).toBeChecked();
    expect(submitButton()).toBeDisabled();
  });

  it('"Submit as invoice": opens with that attachment marked Invoice', async () => {
    renderFlow(makeSubmission(), { preMarkedInvoiceId: 'sup' });
    await ready();
    expect(radio('sup.pdf', 'Invoice')).toBeChecked();
    expect(radio('inv.pdf', 'Supporting')).toBeChecked();
  });

  describe('Submit is gated on the marking', () => {
    it('is disabled with zero Invoice marks, enabled with exactly one', async () => {
      renderFlow(makeSubmission());
      await ready();
      fillNumber();
      expect(submitButton()).toBeDisabled();
      expect(screen.getByTestId('submit-invoice-marking-hint')).toHaveTextContent('Mark one PDF as the Invoice.');

      fireEvent.click(radio('inv.pdf', 'Invoice'));
      expect(submitButton()).toBeEnabled();
      expect(screen.queryByTestId('submit-invoice-marking-hint')).not.toBeInTheDocument();
    });

    it('is disabled with two Invoice marks and says only one can be the Invoice', async () => {
      renderFlow(makeSubmission());
      await ready();
      fillNumber();
      fireEvent.click(radio('inv.pdf', 'Invoice'));
      fireEvent.click(radio('sup.pdf', 'Invoice'));
      expect(submitButton()).toBeDisabled();
      expect(screen.getByTestId('submit-invoice-marking-hint')).toHaveTextContent(
        'Only one attachment can be the Invoice.'
      );
    });

    it('a non-PDF cannot be marked Invoice: no Invoice option, and Submit stays disabled when it is the only attachment', async () => {
      const submission = makeSubmission({
        loadAttachments: jest.fn().mockResolvedValue([{ id: 'doc', fileName: 'letter.docx' }]),
      });
      renderFlow(submission, { preMarkedInvoiceId: 'doc' });
      await waitFor(() => expect(screen.getByText('letter.docx')).toBeInTheDocument());
      await waitFor(() => expect(screen.getByTestId('submit-invoice-firm')).toBeInTheDocument());
      fillNumber();
      expect(
        within(screen.getByRole('radiogroup', { name: 'letter.docx' })).queryByRole('radio', { name: 'Invoice' })
      ).toBeNull();
      expect(submitButton()).toBeDisabled();
    });

    it('is disabled until the firm invoice number is entered, and a blank (spaces) number does not count', async () => {
      renderFlow(makeSubmission(), { preMarkedInvoiceId: 'inv' });
      await ready();
      expect(submitButton()).toBeDisabled();
      fillNumber('   ');
      expect(submitButton()).toBeDisabled();
      fillNumber('INV-9');
      expect(submitButton()).toBeEnabled();
    });
  });

  it('submits the communication, the invoice document and the supporting documents only ("Don\'t save" is not sent)', async () => {
    const submission = makeSubmission();
    renderFlow(submission, { preMarkedInvoiceId: 'inv' });
    await ready();
    fireEvent.click(radio('scan.png', "Don't save"));
    fillNumber('  INV-2026-001 ');
    fireEvent.change(screen.getByRole('textbox', { name: 'PO number' }), { target: { value: ' PO-7 ' } });

    fireEvent.click(submitButton());

    await waitFor(() => expect(submission.submit).toHaveBeenCalledTimes(1));
    expect(submission.submit).toHaveBeenCalledWith({
      communicationId: COMM,
      invoiceDocumentId: 'inv',
      supportingDocumentIds: ['sup'],
      record: { kind: 'matter', recordId: MATTER.recordId },
      firmId: FIRM.firmId,
      firmInvoiceNumber: 'INV-2026-001',
      poNumber: 'PO-7',
    });
  });

  it('success shows the submission outcome (tracking number) and locks nothing it should not', async () => {
    const onClose = jest.fn();
    const submission = makeSubmission();
    renderFlow(submission, { preMarkedInvoiceId: 'inv', onClose });
    await ready();
    fillNumber();
    fireEvent.click(submitButton());

    expect(await screen.findByTestId('invoice-tracking-number')).toHaveTextContent('TRK-100');
    expect(submission.getStatus).toHaveBeenCalledWith('sub-1');
    expect(screen.queryByTestId('submit-invoice-submit')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Done' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  describe('a refusal shows its error and leaves the marks in place', () => {
    it.each([
      [
        403,
        'ebilling.submission.record_forbidden',
        undefined,
        'You do not have access to submit an invoice against this record.',
      ],
      [
        409,
        'ebilling.submission.document_core_record_mismatch',
        'This email is filed under another record.',
        'This email is filed under another record.',
      ],
      // The server's own sentences (InvoiceSubmissionService supporting-document refusals): its detail wins over the fallbacks.
      [
        413,
        'ebilling.submission.too_large',
        'A supporting document may be at most 25 MB. Nothing was saved.',
        'A supporting document may be at most 25 MB. Nothing was saved.',
      ],
      [
        422,
        'ebilling.submission.document_unavailable',
        'A supporting document has no file that can be copied, or no longer belongs to this communication. Nothing was saved.',
        'A supporting document has no file that can be copied, or no longer belongs to this communication. Nothing was saved.',
      ],
      // The route's uniform 404 (transport maps it to this wording: see emailInvoiceSubmission.test.ts).
      [404, undefined, SUBMIT_NOT_FOUND_TEXT, SUBMIT_NOT_FOUND_TEXT],
      [
        503,
        'ebilling.submission.record_unavailable',
        undefined,
        'This record is temporarily unavailable. Try again shortly.',
      ],
    ])('%i %s', async (statusCode, errorCode, detail, expected) => {
      const submission = makeSubmission({
        submit: jest.fn().mockRejectedValue(new InvoiceSubmissionError({ statusCode, errorCode, detail })),
      });
      renderFlow(submission, { preMarkedInvoiceId: 'inv' });
      await ready();
      fireEvent.click(radio('scan.png', "Don't save"));
      fillNumber('INV-5');

      fireEvent.click(submitButton());

      expect(await screen.findByTestId('submit-invoice-error')).toHaveTextContent(expected);
      // The marks, the number and the chosen record are exactly where the user left them, and Submit is available again.
      expect(radio('inv.pdf', 'Invoice')).toBeChecked();
      expect(radio('sup.pdf', 'Supporting')).toBeChecked();
      expect(radio('scan.png', "Don't save")).toBeChecked();
      expect(screen.getByRole('textbox', { name: /Firm invoice number/ })).toHaveValue('INV-5');
      expect(screen.getByRole('combobox', { name: 'Record' })).toHaveValue(`matter:${MATTER.recordId}`);
      expect(submitButton()).toBeEnabled();
      expect(screen.queryByTestId('invoice-tracking-number')).not.toBeInTheDocument();
    });

    it('onAccepted is called once when the server accepts, and never on a refusal', async () => {
      const onAccepted = jest.fn();
      const submission = makeSubmission({
        submit: jest
          .fn()
          .mockRejectedValueOnce(new InvoiceSubmissionError({ statusCode: 409, detail: 'Filed elsewhere.' }))
          .mockResolvedValueOnce({ submissionId: 'sub-1', trackingNumber: 'TRK-100' }),
      });
      renderFlow(submission, { preMarkedInvoiceId: 'inv', onAccepted });
      await ready();
      fillNumber();

      fireEvent.click(submitButton());
      await screen.findByTestId('submit-invoice-error');
      expect(onAccepted).not.toHaveBeenCalled();

      fireEvent.click(submitButton());
      expect(await screen.findByTestId('invoice-tracking-number')).toBeInTheDocument();
      expect(onAccepted).toHaveBeenCalledTimes(1);
    });

    it('a host refresh that throws does not turn an accepted submission into a shown refusal', async () => {
      const onAccepted = jest.fn(() => {
        throw new Error('refresh failed');
      });
      renderFlow(makeSubmission(), { preMarkedInvoiceId: 'inv', onAccepted });
      await ready();
      fillNumber();
      fireEvent.click(submitButton());
      expect(await screen.findByTestId('invoice-tracking-number')).toBeInTheDocument();
      expect(screen.queryByTestId('submit-invoice-error')).not.toBeInTheDocument();
    });

    it('a refusal that did not come from the server (network) says the outcome is unconfirmed', async () => {
      const submission = makeSubmission({ submit: jest.fn().mockRejectedValue(new Error('network down')) });
      renderFlow(submission, { preMarkedInvoiceId: 'inv' });
      await ready();
      fillNumber();
      fireEvent.click(submitButton());
      expect(await screen.findByTestId('submit-invoice-error')).toHaveTextContent('could not be confirmed');
    });

    it('can be submitted again after a refusal (a new attempt, same marks)', async () => {
      const submission = makeSubmission({
        submit: jest
          .fn()
          .mockRejectedValueOnce(
            new InvoiceSubmissionError({ statusCode: 503, errorCode: 'ebilling.submission.failed' })
          )
          .mockResolvedValueOnce({ submissionId: 'sub-2', trackingNumber: 'TRK-200' }),
      });
      renderFlow(submission, { preMarkedInvoiceId: 'inv' });
      await ready();
      fillNumber();
      fireEvent.click(submitButton());
      await screen.findByTestId('submit-invoice-error');
      fireEvent.click(submitButton());
      await waitFor(() => expect(submission.submit).toHaveBeenCalledTimes(2));
      expect(submission.submit.mock.calls[1][0]).toEqual(submission.submit.mock.calls[0][0]);
    });
  });

  it('does not submit twice while a request is in flight', async () => {
    let resolveSubmit: (v: { submissionId: string; trackingNumber: string }) => void = () => {};
    const submission = makeSubmission({
      submit: jest.fn().mockReturnValue(new Promise(r => (resolveSubmit = r))),
    });
    const onBusyChange = jest.fn();
    renderFlow(submission, { preMarkedInvoiceId: 'inv', onBusyChange });
    await ready();
    fillNumber();
    fireEvent.click(submitButton());
    await waitFor(() => expect(submitButton()).toBeDisabled());
    fireEvent.click(submitButton());
    expect(submission.submit).toHaveBeenCalledTimes(1);
    expect(onBusyChange).toHaveBeenLastCalledWith(true);
    resolveSubmit({ submissionId: 'sub-1', trackingNumber: 'TRK-1' });
    await screen.findByTestId('invoice-tracking-number');
    expect(onBusyChange).toHaveBeenLastCalledWith(false);
  });

  describe('the firm', () => {
    it('several firms: the user must choose one before Submit is available', async () => {
      const other = { firmId: '44444444-4444-4444-4444-444444444444', name: 'Jones LLP' };
      const submission = makeSubmission({ loadFirms: jest.fn().mockResolvedValue([FIRM, other]) });
      renderFlow(submission, { preMarkedInvoiceId: 'inv' });
      await screen.findByRole('combobox', { name: 'Firm' });
      fillNumber();
      expect(submitButton()).toBeDisabled();
      fireEvent.change(screen.getByRole('combobox', { name: 'Firm' }), { target: { value: other.firmId } });
      expect(submitButton()).toBeEnabled();
      fireEvent.click(submitButton());
      await waitFor(() => expect(submission.submit).toHaveBeenCalled());
      expect(submission.submit.mock.calls[0][0].firmId).toBe(other.firmId);
    });

    it('negative: a record with no active firm says so and Submit stays disabled', async () => {
      renderFlow(makeSubmission({ loadFirms: jest.fn().mockResolvedValue([]) }), { preMarkedInvoiceId: 'inv' });
      expect(await screen.findByTestId('submit-invoice-no-firm')).toHaveTextContent('No active firm is linked');
      fillNumber();
      expect(submitButton()).toBeDisabled();
    });
  });

  it('no record chosen (the email is not filed to a core record): the user picks one from the list', async () => {
    const submission = makeSubmission();
    renderFlow(submission, { preMarkedInvoiceId: 'inv', initialRecord: undefined });
    await waitFor(() => expect(screen.getByText('inv.pdf')).toBeInTheDocument());
    await waitFor(() => expect(screen.getByRole('option', { name: 'Acme v Beta (M-1)' })).toBeInTheDocument());
    fillNumber();
    expect(submitButton()).toBeDisabled();
    expect(submission.loadFirms).not.toHaveBeenCalled();
    fireEvent.change(screen.getByRole('combobox', { name: 'Record' }), {
      target: { value: `matter:${MATTER.recordId}` },
    });
    await screen.findByTestId('submit-invoice-firm');
    expect(submitButton()).toBeEnabled();
  });

  it('an email with no attachments says so and offers no Submit', async () => {
    renderFlow(makeSubmission({ loadAttachments: jest.fn().mockResolvedValue([]) }));
    expect(await screen.findByText('This email has no attachments to submit as an invoice.')).toBeInTheDocument();
    expect(screen.queryByTestId('submit-invoice-submit')).not.toBeInTheDocument();
  });

  it('negative: an attachment with no saved document is listed locked and is never sent', async () => {
    const submission = makeSubmission({
      loadAttachments: jest.fn().mockResolvedValue([
        pdf('inv'),
        {
          id: 'attachment:a9',
          fileName: 'orphan.pdf',
          disabledReason: 'This attachment has no saved document, so it cannot be submitted.',
        },
      ]),
    });
    renderFlow(submission);
    await ready();
    expect(screen.getByText(/has no saved document/)).toBeInTheDocument();
    fillNumber();
    fireEvent.click(submitButton());
    await waitFor(() => expect(submission.submit).toHaveBeenCalled());
    const sent = submission.submit.mock.calls[0][0];
    expect(sent.invoiceDocumentId).toBe('inv');
    expect(sent.supportingDocumentIds).toEqual([]);
  });

  it('an attachments load failure is shown, not a blank flow', async () => {
    renderFlow(makeSubmission({ loadAttachments: jest.fn().mockRejectedValue(new Error('boom')) }));
    expect(await screen.findByText('The attachments of this email could not be loaded.')).toBeInTheDocument();
  });

  it('renders under a dark FluentProvider theme with no console errors (ADR-021)', async () => {
    const consoleErrorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    renderFlow(makeSubmission(), { preMarkedInvoiceId: 'inv' }, webDarkTheme);
    await ready();
    expect(consoleErrorSpy).not.toHaveBeenCalled();
    consoleErrorSpy.mockRestore();
  });
});

describe('hosts pass onAccepted through to the flow (ebilling-r1 task 118 review)', () => {
  it('InvoiceSubmitTab (the Email Review tab): called once on Accepted, never on a refusal', async () => {
    const onAccepted = jest.fn();
    const submission = makeSubmission({
      // One PDF: it is the pre-selected Invoice (the tab has no pre-marked attachment of its own).
      loadAttachments: jest.fn().mockResolvedValue([pdf('inv')]),
      submit: jest
        .fn()
        .mockRejectedValueOnce(new InvoiceSubmissionError({ statusCode: 503, errorCode: 'ebilling.submission.failed' }))
        .mockResolvedValueOnce({ submissionId: 'sub-1', trackingNumber: 'TRK-100' }),
    });
    render(
      <FluentProvider theme={webLightTheme}>
        <InvoiceSubmitTab
          communicationId={COMM}
          submission={submission}
          regarding={{ entityType: 'sprk_matter', recordId: MATTER.recordId }}
          onAccepted={onAccepted}
        />
      </FluentProvider>
    );
    await ready();
    fillNumber();
    fireEvent.click(submitButton());
    await screen.findByTestId('submit-invoice-error');
    expect(onAccepted).not.toHaveBeenCalled();
    fireEvent.click(submitButton());
    expect(await screen.findByTestId('invoice-tracking-number')).toBeInTheDocument();
    expect(onAccepted).toHaveBeenCalledTimes(1);
  });

  it('SubmitInvoiceDialog (the toolbar, a flagged attachment, the PCF): called once on Accepted', async () => {
    const onAccepted = jest.fn();
    render(
      <FluentProvider theme={webLightTheme}>
        <SubmitInvoiceDialog
          open
          onClose={jest.fn()}
          onAccepted={onAccepted}
          communicationId={COMM}
          submission={makeSubmission()}
          initialRecord={MATTER}
          preMarkedInvoiceId="inv"
        />
      </FluentProvider>
    );
    // The modal surface is aria-hidden in jsdom (FAILURE-MODES G-21): find by test id, not by role.
    await waitFor(() => expect(screen.getByTestId('submit-invoice-firm')).toBeInTheDocument());
    fireEvent.change(screen.getByRole('textbox', { name: /Firm invoice number/, hidden: true }), {
      target: { value: 'INV-9' },
    });
    fireEvent.click(screen.getByTestId('submit-invoice-submit'));
    expect(await screen.findByTestId('invoice-tracking-number')).toBeInTheDocument();
    expect(onAccepted).toHaveBeenCalledTimes(1);
  });
});

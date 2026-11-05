/**
 * InvoiceSubmissionOutcome — the billing-terms warnings (ebilling-r1 082c-3; External Access R4 items 3 and 4).
 *
 * - The 202's provisional warning (`submittedWarning`) is a quiet caption under Processing. The terminal view is authoritative: it
 *   drops the caption, and its own `warning` (a held Accepted invoice) replaces the green Accepted bar with ONE warning bar.
 * - A missing, null or blank value renders nothing.
 * - The wording is the server's: the component shows `message` as written and never branches on `code`.
 */
import * as React from 'react';
import { act, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { InvoiceSubmissionOutcome } from '../InvoiceSubmissionOutcome';
import type { InvoiceSubmissionStatusView, InvoiceSubmissionWarning } from '../types';

const ID = '33333333-3333-4333-8333-333333333333';
const PROVISIONAL: InvoiceSubmissionWarning = {
  code: 'ebilling.submission.procedure_gap',
  message:
    "As of today, no billing terms are set up for the record you chose. If the invoice's work dates are not covered either, it will be held for review, not rejected.",
};
const HELD: InvoiceSubmissionWarning = {
  code: 'ebilling.submission.procedure_gap',
  message: 'We received this invoice. It is on hold for review: SERVER WORDING ONE.',
};

const view = (over: Partial<InvoiceSubmissionStatusView> = {}): InvoiceSubmissionStatusView => ({
  submissionId: ID,
  trackingNumber: 'TRK-0003',
  firmInvoiceNumber: 'INV-3',
  status: 'Processing',
  ...over,
});

const settle = (ms = 60) =>
  act(async () => {
    await new Promise(r => setTimeout(r, ms));
  });

const PROVISIONAL_ID = 'invoice-submission-provisional-warning';
const HELD_ID = 'invoice-submission-held-warning';

describe('InvoiceSubmissionOutcome — the 202 provisional warning (082c-3)', () => {
  it('shows the warning as a caption under Processing, in a polite status region (not an alert)', async () => {
    const getStatus = jest.fn().mockResolvedValue(view({ status: 'Processing' }));
    renderWithProviders(
      <InvoiceSubmissionOutcome
        submissionId={ID}
        submittedWarning={PROVISIONAL}
        getStatus={getStatus}
        pollIntervalMs={10}
      />
    );
    const note = await screen.findByTestId(PROVISIONAL_ID);
    expect(note).toHaveTextContent(PROVISIONAL.message);
    expect(note).toHaveAttribute('role', 'status');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    // Quiet: a caption, never a message bar.
    expect(note.closest('.fui-MessageBar')).toBeNull();
  });

  it('shows the warning before the first status read returns (the 202 is all that is known yet)', () => {
    const getStatus = jest.fn().mockReturnValue(new Promise(() => undefined));
    renderWithProviders(
      <InvoiceSubmissionOutcome
        submissionId={ID}
        submittedWarning={PROVISIONAL}
        trackingNumber="TRK-0003"
        getStatus={getStatus}
        pollIntervalMs={10}
      />
    );
    expect(screen.getByTestId(PROVISIONAL_ID)).toHaveTextContent(PROVISIONAL.message);
  });

  it('seed guard: the caption is dropped when a terminal view arrives with NO warning (the invoice is not held)', async () => {
    const getStatus = jest
      .fn()
      .mockResolvedValueOnce(view({ status: 'Processing' }))
      .mockResolvedValue(view({ status: 'Accepted' }));
    renderWithProviders(
      <InvoiceSubmissionOutcome
        submissionId={ID}
        submittedWarning={PROVISIONAL}
        getStatus={getStatus}
        pollIntervalMs={10}
      />
    );
    expect(await screen.findByTestId(PROVISIONAL_ID)).toBeInTheDocument();
    expect(await screen.findByText(/The invoice was accepted/i)).toBeInTheDocument();
    expect(screen.queryByTestId(PROVISIONAL_ID)).not.toBeInTheDocument();
    expect(screen.queryByText(PROVISIONAL.message)).not.toBeInTheDocument();
    expect(screen.queryByTestId(HELD_ID)).not.toBeInTheDocument();
  });

  it.each(['Rejected', 'Failed'])('the caption is dropped on a %s terminal view too', async terminal => {
    const getStatus = jest
      .fn()
      .mockResolvedValueOnce(view({ status: 'Received' }))
      .mockResolvedValue(view({ status: terminal, outcomeReasonCode: 'po_mismatch' }));
    renderWithProviders(
      <InvoiceSubmissionOutcome
        submissionId={ID}
        submittedWarning={PROVISIONAL}
        getStatus={getStatus}
        pollIntervalMs={10}
      />
    );
    expect(await screen.findByTestId(PROVISIONAL_ID)).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByTestId(PROVISIONAL_ID)).not.toBeInTheDocument());
  });

  it('the caption stays while the answer is Received or Processing, however many reads it takes', async () => {
    const getStatus = jest.fn().mockResolvedValue(view({ status: 'Received' }));
    renderWithProviders(
      <InvoiceSubmissionOutcome
        submissionId={ID}
        submittedWarning={PROVISIONAL}
        getStatus={getStatus}
        pollIntervalMs={10}
      />
    );
    await screen.findByTestId(PROVISIONAL_ID);
    await settle(80);
    expect(getStatus.mock.calls.length).toBeGreaterThan(2);
    expect(screen.getByTestId(PROVISIONAL_ID)).toHaveTextContent(PROVISIONAL.message);
  });

  it.each([
    ['absent', undefined],
    ['null', null],
    ['a blank message', { code: 'ebilling.submission.procedure_gap', message: '   ' }],
  ])('renders nothing for %s', async (_label, value) => {
    const getStatus = jest.fn().mockResolvedValue(view({ status: 'Processing' }));
    renderWithProviders(
      <InvoiceSubmissionOutcome submissionId={ID} submittedWarning={value} getStatus={getStatus} pollIntervalMs={10} />
    );
    await screen.findByRole('status');
    expect(screen.queryByTestId(PROVISIONAL_ID)).not.toBeInTheDocument();
    expect(screen.queryByTestId(HELD_ID)).not.toBeInTheDocument();
  });
});

describe('InvoiceSubmissionOutcome — a held Accepted invoice (082c-3, R4 item 3)', () => {
  it('shows ONE warning bar with the server sentence in place of the green Accepted bar, and keeps the tracking sentence', async () => {
    const getStatus = jest.fn().mockResolvedValue(view({ status: 'Accepted', warning: HELD }));
    renderWithProviders(<InvoiceSubmissionOutcome submissionId={ID} getStatus={getStatus} pollIntervalMs={10} />);

    const bar = await screen.findByTestId(HELD_ID);
    expect(bar).toHaveTextContent('Received: on hold for review');
    expect(bar).toHaveTextContent(HELD.message);
    expect(bar).toHaveTextContent('Quote tracking number TRK-0003 if you contact us about it.');
    expect(bar).toHaveAttribute('role', 'status');
    expect(bar.className).toContain('fui-MessageBar');
    // In place of, not stacked with, the green bar; and not an alert.
    expect(screen.queryByText(/The invoice was accepted/i)).not.toBeInTheDocument();
    expect(screen.queryByText('Accepted', { selector: '.fui-MessageBarTitle' })).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(document.querySelectorAll('.fui-MessageBar')).toHaveLength(1);
  });

  it('renders the server wording as written, whatever the code says (the client keeps no copy of the wording)', async () => {
    const getStatus = jest.fn().mockResolvedValue(
      view({
        status: 'Accepted',
        warning: { code: 'ebilling.submission.some_future_code', message: 'FUTURE SERVER SENTENCE.' },
      })
    );
    renderWithProviders(<InvoiceSubmissionOutcome submissionId={ID} getStatus={getStatus} pollIntervalMs={10} />);
    expect(await screen.findByTestId(HELD_ID)).toHaveTextContent('FUTURE SERVER SENTENCE.');
  });

  it("keeps renderAccepted content under the warning bar (the internal host's override note still shows)", async () => {
    const getStatus = jest.fn().mockResolvedValue(view({ status: 'Accepted', warning: HELD }));
    renderWithProviders(
      <InvoiceSubmissionOutcome
        submissionId={ID}
        getStatus={getStatus}
        pollIntervalMs={10}
        renderAccepted={() => <div data-testid="accepted-slot" />}
      />
    );
    await screen.findByTestId(HELD_ID);
    expect(screen.getByTestId('accepted-slot')).toBeInTheDocument();
  });

  it.each([
    ['absent', undefined],
    ['null', null],
    ['a blank message', { code: 'x', message: ' ' }],
  ])('without a usable warning (%s) the green Accepted bar shows, exactly as before', async (_label, warning) => {
    const getStatus = jest.fn().mockResolvedValue(view({ status: 'Accepted', warning }));
    renderWithProviders(<InvoiceSubmissionOutcome submissionId={ID} getStatus={getStatus} pollIntervalMs={10} />);
    expect(await screen.findByText(/The invoice was accepted/i)).toBeInTheDocument();
    expect(screen.queryByTestId(HELD_ID)).not.toBeInTheDocument();
  });

  it('a warning on a non-Accepted view is not shown as a held bar (the warning belongs to an Accepted invoice)', async () => {
    const getStatus = jest
      .fn()
      .mockResolvedValue(view({ status: 'Rejected', outcomeReasonCode: 'po_mismatch', warning: HELD }));
    renderWithProviders(<InvoiceSubmissionOutcome submissionId={ID} getStatus={getStatus} pollIntervalMs={10} />);
    await screen.findByText(/Reason: po_mismatch/);
    expect(screen.queryByTestId(HELD_ID)).not.toBeInTheDocument();
  });

  it("the provisional caption gives way to the held bar: the status view's warning is authoritative", async () => {
    const getStatus = jest
      .fn()
      .mockResolvedValueOnce(view({ status: 'Processing' }))
      .mockResolvedValue(view({ status: 'Accepted', warning: HELD }));
    renderWithProviders(
      <InvoiceSubmissionOutcome
        submissionId={ID}
        submittedWarning={PROVISIONAL}
        getStatus={getStatus}
        pollIntervalMs={10}
      />
    );
    expect(await screen.findByTestId(PROVISIONAL_ID)).toBeInTheDocument();
    expect(await screen.findByTestId(HELD_ID)).toHaveTextContent(HELD.message);
    expect(screen.queryByTestId(PROVISIONAL_ID)).not.toBeInTheDocument();
    expect(screen.queryByText(PROVISIONAL.message)).not.toBeInTheDocument();
  });
});

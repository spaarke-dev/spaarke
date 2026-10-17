/**
 * InvoiceSubmissionOutcome tests (ebilling-r1 task 051, FR-18): the screen that is shown after a submit AND when a
 * user returns to a submission later. Everything on it comes from the host's getStatus callback.
 */
import * as React from 'react';
import { act, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { InvoiceSubmissionOutcome } from '../InvoiceSubmissionOutcome';
import { InvoiceSubmissionError } from '../types';
import type { InvoiceSubmissionStatusView } from '../types';

const ID = '33333333-3333-4333-8333-333333333333';
const view = (over: Partial<InvoiceSubmissionStatusView> = {}): InvoiceSubmissionStatusView => ({
  submissionId: ID,
  trackingNumber: 'TRK-0002',
  firmInvoiceNumber: 'INV-2',
  status: 'Accepted',
  ...over,
});

const settle = (ms = 60) =>
  act(async () => {
    await new Promise(r => setTimeout(r, ms));
  });

describe('InvoiceSubmissionOutcome', () => {
  it('resume: a stored terminal result is read from the server and shown without any prior wizard state', async () => {
    const getStatus = jest.fn().mockResolvedValue(view({ status: 'Rejected', outcomeReasonCode: 'po_mismatch' }));
    renderWithProviders(<InvoiceSubmissionOutcome submissionId={ID} getStatus={getStatus} pollIntervalMs={10} />);
    expect(await screen.findByText(/Reason: po_mismatch/)).toBeInTheDocument();
    expect(screen.getByTestId('invoice-tracking-number')).toHaveTextContent('TRK-0002');
    expect(getStatus).toHaveBeenCalledWith(ID);
    // Terminal: it reads once and stops.
    await settle();
    expect(getStatus).toHaveBeenCalledTimes(1);
  });

  it('Received and Processing both read as Processing, and polling continues until a terminal status', async () => {
    const getStatus = jest
      .fn()
      .mockResolvedValueOnce(view({ status: 'Received' }))
      .mockResolvedValueOnce(view({ status: 'Processing' }))
      .mockResolvedValue(view({ status: 'Accepted' }));
    renderWithProviders(<InvoiceSubmissionOutcome submissionId={ID} getStatus={getStatus} pollIntervalMs={200} />);
    // Badge and status line both say Processing while the answer is not terminal.
    expect((await screen.findAllByText('Processing')).length).toBeGreaterThan(0);
    expect(await screen.findByText(/The invoice was accepted/i)).toBeInTheDocument();
    expect(getStatus.mock.calls.length).toBeGreaterThanOrEqual(3);
  });

  it('a read that fails in transit keeps the last state, says so, and keeps polling', async () => {
    const getStatus = jest
      .fn()
      .mockResolvedValueOnce(view({ status: 'Processing' }))
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValue(view({ status: 'Accepted' }));
    renderWithProviders(<InvoiceSubmissionOutcome submissionId={ID} getStatus={getStatus} pollIntervalMs={10} />);
    await waitFor(() => expect(screen.getByText(/latest status could not be loaded/i)).toBeInTheDocument());
    expect(await screen.findByText(/The invoice was accepted/i)).toBeInTheDocument();
    expect(screen.queryByText(/latest status could not be loaded/i)).not.toBeInTheDocument();
  });

  it('a 404 (not found, or not yours) shows the message and stops polling', async () => {
    const getStatus = jest.fn().mockRejectedValue(new InvoiceSubmissionError({ statusCode: 404 }));
    renderWithProviders(<InvoiceSubmissionOutcome submissionId={ID} getStatus={getStatus} pollIntervalMs={10} />);
    expect(await screen.findByText(/not found, or you do not have access/i)).toBeInTheDocument();
    await settle();
    expect(getStatus).toHaveBeenCalledTimes(1);
  });

  it('stops polling on unmount', async () => {
    const getStatus = jest.fn().mockResolvedValue(view({ status: 'Processing' }));
    const { unmount } = renderWithProviders(
      <InvoiceSubmissionOutcome submissionId={ID} getStatus={getStatus} pollIntervalMs={10} />
    );
    await screen.findByRole('status');
    unmount();
    const reads = getStatus.mock.calls.length;
    await settle();
    expect(getStatus.mock.calls.length).toBe(reads);
  });
});

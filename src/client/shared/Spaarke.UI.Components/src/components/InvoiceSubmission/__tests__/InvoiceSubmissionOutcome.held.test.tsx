/**
 * InvoiceSubmissionOutcome — the optional hooks the internal host uses for a held submission (ebilling-r1 task 052):
 * `keepPolling` (follow a Rejected answer that Override / Return can still move) and `renderAccepted`.
 * The default behaviour (no hook) is pinned by InvoiceSubmissionOutcome.test.tsx and, again, by the control below.
 */
import * as React from 'react';
import { act, screen } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { InvoiceSubmissionOutcome } from '../InvoiceSubmissionOutcome';
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

const HELD = view({ status: 'Rejected', outcomeReasonCode: 'ebilling.submission.blocked' });
const isHeld = (s: InvoiceSubmissionStatusView) => s.outcomeReasonCode === 'ebilling.submission.blocked';

describe('InvoiceSubmissionOutcome — held submissions (task 052)', () => {
  it('control: without keepPolling a Rejected answer stops the poll', async () => {
    const getStatus = jest.fn().mockResolvedValue(HELD);
    renderWithProviders(<InvoiceSubmissionOutcome submissionId={ID} getStatus={getStatus} pollIntervalMs={10} />);
    await screen.findByText(/Reason: ebilling.submission.blocked/);
    await settle();
    expect(getStatus).toHaveBeenCalledTimes(1);
  });

  it('with keepPolling the screen follows a Rejected answer to Accepted, drops the stale report, then stops', async () => {
    const getStatus = jest
      .fn()
      .mockResolvedValueOnce(HELD)
      .mockResolvedValue(view({ status: 'Accepted', outcomeReasonCode: 'ebilling.submission.overridden' }));
    renderWithProviders(
      <InvoiceSubmissionOutcome
        submissionId={ID}
        getStatus={getStatus}
        pollIntervalMs={10}
        keepPolling={isHeld}
        renderReport={() => <div data-testid="report-slot" />}
        renderAccepted={s => <div data-testid="accepted-slot">{s.outcomeReasonCode}</div>}
      />
    );
    expect(await screen.findByText(/The invoice was accepted/i)).toBeInTheDocument();
    expect(screen.queryByText(/Reason: ebilling.submission.blocked/)).not.toBeInTheDocument();
    expect(screen.queryByTestId('report-slot')).not.toBeInTheDocument();
    expect(screen.getByTestId('accepted-slot')).toHaveTextContent('ebilling.submission.overridden');
    const reads = getStatus.mock.calls.length;
    await settle();
    expect(getStatus.mock.calls.length).toBe(reads);
  });

  it('keepPolling is asked per answer: false for a returned submission stops the poll on Rejected', async () => {
    const getStatus = jest
      .fn()
      .mockResolvedValue(view({ status: 'Rejected', outcomeReasonCode: 'ebilling.submission.returned' }));
    renderWithProviders(
      <InvoiceSubmissionOutcome submissionId={ID} getStatus={getStatus} pollIntervalMs={10} keepPolling={isHeld} />
    );
    await screen.findByText(/Reason: ebilling.submission.returned/);
    await settle();
    expect(getStatus).toHaveBeenCalledTimes(1);
  });

  it('renderAccepted is not called for a Rejected answer', async () => {
    const renderAccepted = jest.fn(() => null);
    renderWithProviders(
      <InvoiceSubmissionOutcome
        submissionId={ID}
        getStatus={jest.fn().mockResolvedValue(HELD)}
        pollIntervalMs={10}
        renderAccepted={renderAccepted}
      />
    );
    await screen.findByText(/Reason: ebilling.submission.blocked/);
    expect(renderAccepted).not.toHaveBeenCalled();
  });
});

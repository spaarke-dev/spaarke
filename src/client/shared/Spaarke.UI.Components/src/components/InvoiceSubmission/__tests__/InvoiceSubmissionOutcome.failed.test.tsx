/**
 * InvoiceSubmissionOutcome — the Failed text and the status callback (ebilling-r1 iss048).
 *
 * A Failed submission whose reason is a `record_owner_*` code (a RecordOwnershipResolver refusal: the chosen record's setup)
 * is refused the same way on every resubmission, so the bar must not tell the firm to submit again. Every other Failed keeps
 * the old text. The reason code is never shown. `onStatusChange` is the one status source the result heading follows.
 */
import * as React from 'react';
import { screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { InvoiceSubmissionOutcome } from '../InvoiceSubmissionOutcome';
import type { InvoiceSubmissionStatusView } from '../types';

const ID = '33333333-3333-4333-8333-333333333333';
const OWNER_CODE = 'record_owner_restricted_content_team_unresolved';
const view = (over: Partial<InvoiceSubmissionStatusView> = {}): InvoiceSubmissionStatusView => ({
  submissionId: ID,
  trackingNumber: 'TRK-0002',
  firmInvoiceNumber: 'INV-2',
  status: 'Failed',
  ...over,
});

describe('InvoiceSubmissionOutcome — Failed text', () => {
  it('record_owner_ reason: says the record setup is the cause, quotes the tracking number, never says "Submit it again" or shows the code', async () => {
    const getStatus = jest.fn().mockResolvedValue(view({ outcomeReasonCode: OWNER_CODE }));
    renderWithProviders(<InvoiceSubmissionOutcome submissionId={ID} getStatus={getStatus} pollIntervalMs={10} />);
    const text = await screen.findByText(/because of how the record you chose is set up/i);
    expect(text.parentElement).toHaveTextContent(
      'We could not process this invoice because of how the record you chose is set up. Submitting it again will not help until that is fixed. Contact the team and quote tracking number TRK-0002.'
    );
    expect(screen.queryByText(/Submit it again/i)).not.toBeInTheDocument();
    expect(screen.queryByText(new RegExp(OWNER_CODE))).not.toBeInTheDocument();
  });

  it('record_owner_ reason with no tracking number: the sentence ends without the tracking clause', async () => {
    const getStatus = jest.fn().mockResolvedValue(view({ outcomeReasonCode: OWNER_CODE, trackingNumber: null }));
    renderWithProviders(<InvoiceSubmissionOutcome submissionId={ID} getStatus={getStatus} pollIntervalMs={10} />);
    const text = await screen.findByText(/because of how the record you chose is set up/i);
    const content = text.parentElement!.textContent ?? '';
    expect(content).toContain('Submitting it again will not help until that is fixed. Contact the team.');
    expect(content).not.toMatch(/tracking number/i);
  });

  it("any other Failed reason keeps today's text", async () => {
    const getStatus = jest.fn().mockResolvedValue(view({ outcomeReasonCode: 'pdf_unreadable' }));
    renderWithProviders(<InvoiceSubmissionOutcome submissionId={ID} getStatus={getStatus} pollIntervalMs={10} />);
    const text = await screen.findByText(/Submit it again/);
    expect(text.parentElement).toHaveTextContent(
      'We could not process this invoiceSubmit it again. If it keeps failing, contact the team and quote tracking number TRK-0002.'
    );
    expect(screen.queryByText(/pdf_unreadable/)).not.toBeInTheDocument();
  });
});

describe('InvoiceSubmissionOutcome — onStatusChange', () => {
  it('is called with every status read (the one poll the heading shares), and absent it nothing changes', async () => {
    const onStatusChange = jest.fn();
    const getStatus = jest
      .fn()
      .mockResolvedValueOnce(view({ status: 'Processing' }))
      .mockResolvedValue(view({ status: 'Accepted' }));
    renderWithProviders(
      <InvoiceSubmissionOutcome
        submissionId={ID}
        getStatus={getStatus}
        pollIntervalMs={10}
        onStatusChange={onStatusChange}
      />
    );
    await screen.findByText(/The invoice was accepted/i);
    await waitFor(() => expect(onStatusChange).toHaveBeenCalledTimes(2));
    expect(onStatusChange.mock.calls[0][0].status).toBe('Processing');
    expect(onStatusChange.mock.calls[1][0].status).toBe('Accepted');
    expect(getStatus).toHaveBeenCalledTimes(2);
  });
});

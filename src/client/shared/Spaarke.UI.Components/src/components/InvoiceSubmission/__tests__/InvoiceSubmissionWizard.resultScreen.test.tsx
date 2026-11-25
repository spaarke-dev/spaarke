/**
 * InvoiceSubmissionWizard result screen (ebilling-r1 iss048): the heading and icon above the outcome follow the polled
 * status instead of a fixed "Invoice received" (a Failed submission used to sit under a success icon and that title), and a
 * Failed submission that a resubmission cannot fix (a RecordOwnershipResolver refusal, reason code `record_owner_*`) no longer
 * tells the firm to "Submit it again".
 *
 * The wizard, WizardShell and the Processing / Result screen run for real; only the host callbacks are fakes.
 */
import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { InvoiceSubmissionWizard } from '../InvoiceSubmissionWizard';
import type { InvoiceCoreRecordOption, InvoiceSubmissionStatusView } from '../types';

const MATTER: InvoiceCoreRecordOption = {
  kind: 'matter',
  recordId: '11111111-1111-4111-8111-111111111111',
  name: 'Acme v. Roadrunner',
  number: 'M-1001',
};
const SUBMISSION_ID = '33333333-3333-4333-8333-333333333333';
const OWNER_CODE = 'record_owner_restricted_content_team_unresolved';

const view = (over: Partial<InvoiceSubmissionStatusView> = {}): InvoiceSubmissionStatusView => ({
  submissionId: SUBMISSION_ID,
  trackingNumber: 'TRK-0001',
  firmInvoiceNumber: 'INV-9',
  status: 'Processing',
  ...over,
});

/** Mount the wizard, walk to Submit and submit; resolves once the result screen is up. */
async function submitWith(getStatus: jest.Mock) {
  const utils = renderWithProviders(
    <InvoiceSubmissionWizard
      embedded
      pollIntervalMs={10}
      loadRecords={jest.fn().mockResolvedValue({ records: [MATTER] })}
      submit={jest.fn().mockResolvedValue({ submissionId: SUBMISSION_ID, trackingNumber: 'TRK-0001' })}
      getStatus={getStatus}
      onClose={jest.fn()}
    />
  );
  fireEvent.click(await screen.findByRole('radio', { name: /Acme v\. Roadrunner/ }));
  fireEvent.click(screen.getByRole('button', { name: /^next$/i }));
  const input = utils.container.querySelector('input[type="file"]') as HTMLInputElement;
  fireEvent.change(input, { target: { files: [new File(['%PDF-1.4'], 'invoice.pdf', { type: 'application/pdf' })] } });
  await waitFor(() => expect(screen.getByRole('button', { name: /^next$/i })).toBeEnabled());
  fireEvent.click(screen.getByRole('button', { name: /^next$/i }));
  fireEvent.change(await screen.findByLabelText(/Firm invoice number/i), { target: { value: 'INV-9' } });
  fireEvent.click(screen.getByRole('button', { name: /^submit invoice$/i }));
  return utils;
}

const heading = () => screen.getByRole('heading', { level: 2 });

describe('InvoiceSubmissionWizard result screen — heading and icon follow the outcome', () => {
  it('Failed with a record_owner_ reason: "Invoice not processed", the setup text, no "Submit it again", no reason code', async () => {
    await submitWith(jest.fn().mockResolvedValue(view({ status: 'Failed', outcomeReasonCode: OWNER_CODE })));

    await waitFor(() => expect(heading()).toHaveTextContent('Invoice not processed'));
    expect(screen.queryByText('Invoice received')).not.toBeInTheDocument();
    expect(
      screen.getByText(
        /because of how the record you chose is set up\. Submitting it again will not help until that is fixed/i
      )
    ).toBeInTheDocument();
    expect(screen.getByText(/Contact the team and quote tracking number TRK-0001\./)).toBeInTheDocument();
    expect(screen.queryByText(/Submit it again/i)).not.toBeInTheDocument();
    expect(screen.queryByText(new RegExp(OWNER_CODE))).not.toBeInTheDocument();
    // A non-success icon: the success document-checkmark is gone.
    expect(screen.getByTestId('invoice-result-icon')).toHaveAttribute('data-outcome', 'failed');
  });

  it('Failed with any other reason keeps today\'s text under the "Invoice not processed" heading', async () => {
    await submitWith(jest.fn().mockResolvedValue(view({ status: 'Failed', outcomeReasonCode: 'pdf_unreadable' })));

    await waitFor(() => expect(heading()).toHaveTextContent('Invoice not processed'));
    expect(
      screen.getByText(/Submit it again\. If it keeps failing, contact the team and quote tracking number TRK-0001\./)
    ).toBeInTheDocument();
    expect(screen.queryByText(/Submitting it again will not help/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/pdf_unreadable/)).not.toBeInTheDocument();
  });

  it("Failed with no reason code keeps today's text", async () => {
    await submitWith(jest.fn().mockResolvedValue(view({ status: 'Failed' })));
    await waitFor(() => expect(heading()).toHaveTextContent('Invoice not processed'));
    expect(screen.getByText(/Submit it again\./)).toBeInTheDocument();
  });

  it('Accepted: "Invoice accepted" with the checkmark icon', async () => {
    await submitWith(jest.fn().mockResolvedValue(view({ status: 'Accepted' })));
    await waitFor(() => expect(heading()).toHaveTextContent('Invoice accepted'));
    expect(screen.getByTestId('invoice-result-icon')).toHaveAttribute('data-outcome', 'accepted');
  });

  it('Accepted but held for review: still "Invoice received"', async () => {
    await submitWith(
      jest
        .fn()
        .mockResolvedValue(
          view({ status: 'Accepted', warning: { code: 'billing_terms', message: 'Held for review.' } })
        )
    );
    await screen.findByTestId('invoice-submission-held-warning');
    expect(heading()).toHaveTextContent('Invoice received');
    expect(screen.getByTestId('invoice-result-icon')).toHaveAttribute('data-outcome', 'received');
  });

  it('Rejected: "Invoice rejected" with an error icon', async () => {
    await submitWith(jest.fn().mockResolvedValue(view({ status: 'Rejected', outcomeReasonCode: 'po_mismatch' })));
    await waitFor(() => expect(heading()).toHaveTextContent('Invoice rejected'));
    expect(screen.getByTestId('invoice-result-icon')).toHaveAttribute('data-outcome', 'rejected');
  });

  it('Processing: "Invoice received"; then it follows the status to Accepted without a second poll', async () => {
    const getStatus = jest
      .fn()
      .mockResolvedValueOnce(view({ status: 'Processing' }))
      .mockResolvedValue(view({ status: 'Accepted' }));
    await submitWith(getStatus);
    await screen.findByRole('status');
    expect(heading()).toHaveTextContent('Invoice received');
    await waitFor(() => expect(heading()).toHaveTextContent('Invoice accepted'));
    // One reader: the heading and the body share the poll (terminal stops it), so there are two reads, not four.
    expect(getStatus).toHaveBeenCalledTimes(2);
  });
});

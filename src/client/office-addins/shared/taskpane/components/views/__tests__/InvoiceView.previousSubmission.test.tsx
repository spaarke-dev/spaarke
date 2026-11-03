/**
 * Task 133 (owner UAT 2026-11-03): an email whose invoice was already submitted says so at the top of Submit Invoice —
 * when, as what tracking number and by whom — through the view's `loadPreviousSubmission` seam (contract §11, ebilling
 * 095a). A running one is followed; an accepted one offers a correction; a failed one can be submitted again.
 */
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { InvoiceView, type InvoiceViewProps } from '../InvoiceView';
import { fetchRelatedCandidates } from '../../../services/communicationSuggestionsService';
import { searchOfficeEntities } from '../../../services/officeEntitySearch';
import type { IHostAdapter } from '@shared/adapters';
import type { AttachmentInfo } from '@shared/adapters/types';
import type { InvoiceCorrectionPrefill, InvoicePreviousSubmission } from '../../../services/invoiceSubmissionSeam';

jest.mock('../../../services/officeEntitySearch', () => ({ searchOfficeEntities: jest.fn() }));
jest.mock('../../../services/communicationSuggestionsService', () => ({ fetchRelatedCandidates: jest.fn() }));
const mockSearch = searchOfficeEntities as jest.MockedFunction<typeof searchOfficeEntities>;
const mockCandidates = fetchRelatedCandidates as jest.MockedFunction<typeof fetchRelatedCandidates>;

const TIMEOUT = 30000;
const INVOICE_ID = '3d3009f7-0000-4000-8000-000000000002';

const ATTACHMENTS: AttachmentInfo[] = [
  {
    id: 'att-1',
    name: 'INV-4711.pdf',
    contentType: 'application/pdf',
    size: 1000,
    isInline: false,
    attachmentType: 'file',
  },
];

const ACCEPTED: InvoicePreviousSubmission = {
  submissionId: 'sub-1',
  statusUrl: '/api/finance/invoice-submissions/sub-1',
  trackingNumber: 'INV-792345.3',
  status: 'Accepted',
  message: null,
  invoiceId: INVOICE_ID,
  submittedOn: '2026-11-03T14:04:38Z',
  submittedBy: 'Ana Diaz',
  isCorrection: false,
};

function adapter(): IHostAdapter {
  return {
    getCapabilities: () => ({ canGetAttachments: true }),
    getAttachments: jest.fn(async () => ATTACHMENTS),
  } as unknown as IHostAdapter;
}

function renderView(props: Partial<InvoiceViewProps> = {}) {
  const all: InvoiceViewProps = {
    hostAdapter: adapter(),
    apiBaseUrl: 'https://bff',
    getAccessToken: async () => 'token',
    onSubmit: jest.fn(async () => ({ kind: 'success' as const, trackingNumber: 'INV-1.1' })),
    loadFirms: jest.fn(async () => []),
    ...props,
  };
  return render(
    <FluentProvider theme={webLightTheme}>
      <InvoiceView {...all} />
    </FluentProvider>
  );
}

const banner = () => screen.findByTestId('invoice-previous-submission');

beforeEach(() => {
  mockSearch.mockReset();
  mockCandidates.mockReset();
});

describe('InvoiceView — the email was already submitted (§11)', () => {
  it(
    'shows at the top when, as what tracking number and by whom',
    async () => {
      renderView({ loadPreviousSubmission: jest.fn(async () => ACCEPTED) });
      const bar = await banner();
      expect(within(bar).getByText("This email's invoice was already submitted")).toBeInTheDocument();
      expect(bar).toHaveTextContent(/Submitted on .+ as INV-792345\.3 by Ana Diaz\./);
      // It comes before the form, at the top.
      const radios = screen.getByRole('radiogroup', { name: 'Invoice' });
      expect(bar.compareDocumentPosition(radios) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    },
    TIMEOUT
  );

  it(
    'must-not-fire: nothing when there is no earlier submission, or no seam',
    async () => {
      const load = jest.fn(async () => null);
      const { unmount } = renderView({ loadPreviousSubmission: load });
      await waitFor(() => expect(load).toHaveBeenCalledTimes(1));
      await screen.findByText('INV-4711.pdf');
      expect(screen.queryByTestId('invoice-previous-submission')).not.toBeInTheDocument();
      expect(screen.queryByTestId('invoice-previous-error')).not.toBeInTheDocument();
      unmount();
      renderView();
      await screen.findByText('INV-4711.pdf');
      expect(screen.queryByTestId('invoice-previous-submission')).not.toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'says it could not check (never "not submitted"), and Retry asks again',
    async () => {
      const load = jest
        .fn<Promise<InvoicePreviousSubmission | null>, [AbortSignal?]>()
        .mockRejectedValueOnce(new Error("Spaarke couldn't check whether this email's invoice was already submitted."))
        .mockResolvedValueOnce(ACCEPTED);
      renderView({ loadPreviousSubmission: load });
      const error = await screen.findByTestId('invoice-previous-error');
      expect(error).toHaveTextContent("couldn't check whether this email's invoice was already submitted");
      await userEvent.click(within(error).getByRole('button', { name: /Check again/ }));
      await banner();
      expect(load).toHaveBeenCalledTimes(2);
      expect(screen.queryByTestId('invoice-previous-error')).not.toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'follows one still processing to its final status',
    async () => {
      const running: InvoicePreviousSubmission = { ...ACCEPTED, status: 'Processing', invoiceId: null };
      let finish: (s: InvoicePreviousSubmission) => void = () => undefined;
      const follow = jest.fn(
        () =>
          new Promise<InvoicePreviousSubmission | null>(resolve => {
            finish = resolve;
          })
      );
      renderView({ loadPreviousSubmission: jest.fn(async () => running), followPreviousSubmission: follow });
      const bar = await banner();
      expect(within(bar).getByText("This email's invoice is being processed")).toBeInTheDocument();
      expect(bar).toHaveTextContent('Spaarke is checking it.');
      expect(follow).toHaveBeenCalledWith(running, expect.any(AbortSignal));
      finish({ ...running, status: 'Failed', message: 'The invoice could not be read.' });
      await waitFor(() => expect(screen.getByText("This email's invoice could not be processed")).toBeInTheDocument());
      expect(screen.getByTestId('invoice-previous-submission')).toHaveTextContent('The invoice could not be read.');
    },
    TIMEOUT
  );

  it(
    'one still processing without a follower says so, with the tracking number to follow it',
    async () => {
      renderView({ loadPreviousSubmission: jest.fn(async () => ({ ...ACCEPTED, status: 'Received' as const })) });
      expect(await banner()).toHaveTextContent('You can follow it in Spaarke with the tracking number.');
    },
    TIMEOUT
  );

  it(
    'leaving the view stops following it',
    async () => {
      let seen: AbortSignal | undefined;
      const follow = jest.fn((_s: InvoicePreviousSubmission, signal?: AbortSignal) => {
        seen = signal;
        return new Promise<InvoicePreviousSubmission | null>(() => undefined);
      });
      const { unmount } = renderView({
        loadPreviousSubmission: jest.fn(async () => ({ ...ACCEPTED, status: 'Processing' as const })),
        followPreviousSubmission: follow,
      });
      await banner();
      expect(seen?.aborted).toBe(false);
      unmount();
      expect(seen?.aborted).toBe(true);
    },
    TIMEOUT
  );

  it(
    'an accepted one offers a correction of its invoice, which opens the correction form on it',
    async () => {
      const prefill: InvoiceCorrectionPrefill = {
        invoiceId: INVOICE_ID,
        firm: { id: 'firm-1', name: 'Morrison Foerster' },
        coreRecord: { entityType: 'Matter', entityId: 'm-1', name: 'PAT-792345' },
        invoiceStatus: 'InReview',
        trackingNumber: 'INV-792345.3',
        firmInvoiceNumber: 'INV-792345',
        canCorrect: true,
        reason: null,
        replacedBy: null,
      };
      const loadCorrectionPrefill = jest.fn(async () => prefill);
      renderView({
        loadPreviousSubmission: jest.fn(async () => ACCEPTED),
        correctionAvailable: true,
        loadCorrectionPrefill,
      });
      await userEvent.click(await screen.findByTestId('invoice-previous-correct'));
      expect(await screen.findByTestId('invoice-correction-details')).toHaveTextContent('Firm: Morrison Foerster');
      expect(loadCorrectionPrefill).toHaveBeenCalledWith(INVOICE_ID);
      expect(screen.getByRole('radio', { name: 'Existing invoice' })).toBeChecked();
    },
    TIMEOUT
  );

  it(
    'must-not-fire: no correction offer while corrections are off, or without an invoice',
    async () => {
      const { unmount } = renderView({ loadPreviousSubmission: jest.fn(async () => ACCEPTED) });
      await banner();
      expect(screen.queryByTestId('invoice-previous-correct')).not.toBeInTheDocument();
      unmount();
      renderView({
        loadPreviousSubmission: jest.fn(async () => ({ ...ACCEPTED, invoiceId: null })),
        correctionAvailable: true,
      });
      await banner();
      expect(screen.queryByTestId('invoice-previous-correct')).not.toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'a failed one says why, and "Submit again" clears it for a new attempt',
    async () => {
      renderView({
        loadPreviousSubmission: jest.fn(async () => ({
          ...ACCEPTED,
          status: 'Failed' as const,
          message: 'The invoice reader is not set up.',
          invoiceId: null,
          isCorrection: true,
        })),
      });
      const bar = await banner();
      expect(bar).toHaveTextContent(/A correction was submitted on .+ as INV-792345\.3 by Ana Diaz\./);
      expect(bar).toHaveTextContent('The invoice reader is not set up.');
      await userEvent.click(within(bar).getByTestId('invoice-previous-submit-again'));
      expect(screen.queryByTestId('invoice-previous-submission')).not.toBeInTheDocument();
    },
    TIMEOUT
  );
});

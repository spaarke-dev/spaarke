/**
 * Task 133: the Outlook "Submit Invoice" view. The record searches are doubled at `searchOfficeEntities` (the
 * Save picker's own search); the submission and the firm list are the view's injected seams.
 */
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { InvoiceView, INVOICE_UPLOAD_HINT, type InvoiceViewProps } from '../InvoiceView';
import { searchOfficeEntities } from '../../../services/officeEntitySearch';
import type { IHostAdapter } from '@shared/adapters';
import type { AttachmentInfo } from '@shared/adapters/types';
import type { EntitySearchResult } from '../../../hooks/useEntitySearch';

jest.mock('../../../services/officeEntitySearch', () => ({ searchOfficeEntities: jest.fn() }));
const mockSearch = searchOfficeEntities as jest.MockedFunction<typeof searchOfficeEntities>;

const TIMEOUT = 30000;
const MATTER_ID = '6f1c0e8a-2b1d-4c7e-9a52-0d3f1b7e9c41';
const FIRM_ID = '8b0c6f1e-4c2a-4f7e-9a51-2f3d1c0e9b77';

const ATTACHMENTS: AttachmentInfo[] = [
  {
    id: 'att-1',
    name: 'INV-4711.pdf',
    contentType: 'application/pdf',
    size: 182344,
    isInline: false,
    attachmentType: 'file',
  },
  { id: 'att-2', name: 'timesheet.csv', contentType: 'text/csv', size: 40211, isInline: false, attachmentType: 'file' },
  { id: 'att-3', name: 'logo.png', contentType: 'image/png', size: 5120, isInline: true, attachmentType: 'file' },
];

const MATTER: EntitySearchResult = {
  id: MATTER_ID,
  entityType: 'Matter',
  logicalName: 'sprk_matter',
  name: 'Smith v Jones',
  displayInfo: 'MAT-1',
  canFile: true,
};

function adapter(canGetAttachments = true): IHostAdapter {
  return {
    getCapabilities: () => ({ canGetAttachments }),
    getAttachments: jest.fn(async () => ATTACHMENTS),
  } as unknown as IHostAdapter;
}

function renderView(props: { [K in keyof InvoiceViewProps]?: InvoiceViewProps[K] | undefined } = {}) {
  const onSubmit = jest.fn(async () => ({ kind: 'success' as const, trackingNumber: 'FIRMF-000123' }));
  const loadFirms = jest.fn(async () => [{ firmId: FIRM_ID, name: 'Firm F' }]);
  const all = {
    hostAdapter: adapter(),
    apiBaseUrl: 'https://bff',
    getAccessToken: async () => 'token',
    onSubmit,
    loadFirms,
    ...props,
  } as InvoiceViewProps;
  render(
    <FluentProvider theme={webLightTheme}>
      <InvoiceView {...all} />
    </FluentProvider>
  );
  return { onSubmit: all.onSubmit as jest.Mock, loadFirms: all.loadFirms as jest.Mock };
}

async function chooseMatter() {
  mockSearch.mockResolvedValue([MATTER]);
  await userEvent.type(screen.getByLabelText('Search Matter records'), 'Smith');
  await userEvent.click(screen.getByRole('button', { name: 'Search' }));
  await userEvent.click(await screen.findByRole('button', { name: 'Select this record' }));
}

const submitButton = () => screen.getByRole('button', { name: /^(Submit|Submitting…|Submitted)$/ });

beforeEach(() => {
  mockSearch.mockReset();
});

describe('InvoiceView — New invoice (task 133)', () => {
  it(
    'must-fire: Submit stays disabled without a Matter, Project or Work Assignment',
    async () => {
      renderView();
      await screen.findByRole('listitem', { name: 'INV-4711.pdf' });
      await userEvent.type(screen.getByRole('textbox', { name: /Firm invoice number/ }), 'INV-4711');
      expect(submitButton()).toBeDisabled();
      expect(screen.getByText(/Still needed: the matter, project or work assignment, the firm\./)).toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'offers Matter, Project and Work Assignment as the core record, searched with filing access',
    async () => {
      renderView();
      const pills = within(screen.getByRole('radiogroup', { name: 'Related to record type' })).getAllByRole('radio');
      expect(pills.map(p => p.textContent)).toEqual(['Matter', 'Project', 'Work Assignment']);
      await userEvent.click(screen.getByRole('radio', { name: 'Work Assignment' }));
      mockSearch.mockResolvedValue([]);
      await userEvent.type(screen.getByLabelText('Search Work Assignment records'), 'WA');
      await userEvent.click(screen.getByRole('button', { name: 'Search' }));
      await waitFor(() =>
        expect(mockSearch).toHaveBeenCalledWith('WA', 'WorkAssignment', expect.objectContaining({ access: 'file' }))
      );
      // No "+ New" here: an invoice never creates a record.
      expect(screen.queryByRole('button', { name: 'New' })).toBeNull();
    },
    TIMEOUT
  );

  it(
    'with the record, firm, number and a valid marking, Submit sends the contract input and shows the result',
    async () => {
      const { onSubmit, loadFirms } = renderView();
      await screen.findByRole('listitem', { name: 'INV-4711.pdf' });
      await chooseMatter();
      await waitFor(() => expect(loadFirms).toHaveBeenCalledWith({ entityType: 'Matter', entityId: MATTER_ID }));
      await userEvent.type(screen.getByRole('textbox', { name: /Firm invoice number/ }), ' INV-4711 ');
      await waitFor(() => expect(submitButton()).toBeEnabled());

      await userEvent.click(submitButton());

      await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
      const [input, attempt] = onSubmit.mock.calls[0]!;
      expect(input).toEqual({
        targetEntity: { entityType: 'Matter', entityId: MATTER_ID },
        marks: [
          {
            attachmentId: 'att-1',
            fileName: 'INV-4711.pdf',
            size: 182344,
            contentType: 'application/pdf',
            mark: 'Invoice',
          },
          {
            attachmentId: 'att-2',
            fileName: 'timesheet.csv',
            size: 40211,
            contentType: 'text/csv',
            mark: 'Supporting',
          },
        ],
        firmId: FIRM_ID,
        firmInvoiceNumber: 'INV-4711',
        poNumber: null,
        mode: 'New',
        invoiceId: null,
      });
      expect(typeof attempt.actionId).toBe('string');
      expect(await screen.findByText('Invoice submitted')).toBeInTheDocument();
      expect(screen.getByText(/Tracking number FIRMF-000123/)).toBeInTheDocument();
      expect(submitButton()).toBeDisabled();
    },
    TIMEOUT
  );

  it(
    'must-not-fire: an invalid marking (no Invoice) keeps Submit disabled',
    async () => {
      renderView();
      const invoiceRow = await screen.findByRole('listitem', { name: 'INV-4711.pdf' });
      await chooseMatter();
      await userEvent.type(screen.getByRole('textbox', { name: /Firm invoice number/ }), 'INV-4711');
      await waitFor(() => expect(submitButton()).toBeEnabled());
      await userEvent.click(within(invoiceRow).getByRole('radio', { name: 'Supporting' }));
      expect(submitButton()).toBeDisabled();
      expect(screen.getByText('Mark one file as the invoice.')).toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'no firm linked to the record: the empty state, and Submit stays disabled',
    async () => {
      renderView({ loadFirms: jest.fn(async () => []) });
      await screen.findByRole('listitem', { name: 'INV-4711.pdf' });
      await chooseMatter();
      expect(await screen.findByText('No firm is linked to this record')).toBeInTheDocument();
      await userEvent.type(screen.getByRole('textbox', { name: /Firm invoice number/ }), 'INV-4711');
      expect(submitButton()).toBeDisabled();
    },
    TIMEOUT
  );

  it(
    'without the submission seam, says submitting is not available and never enables Submit',
    async () => {
      renderView({ onSubmit: undefined });
      expect(screen.getByText("Submitting invoices from Outlook isn't available yet.")).toBeInTheDocument();
      await screen.findByRole('listitem', { name: 'INV-4711.pdf' });
      await chooseMatter();
      await userEvent.type(screen.getByRole('textbox', { name: /Firm invoice number/ }), 'INV-4711');
      expect(submitButton()).toBeDisabled();
    },
    TIMEOUT
  );

  it('an item with no attachment access (compose, Word) says to open a received email', async () => {
    renderView({ hostAdapter: adapter(false) });
    expect(await screen.findByText('Open a received email to submit an invoice from it.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Submit' })).toBeNull();
  });
});

describe('InvoiceView — results (task 133)', () => {
  async function fillAndSubmit() {
    await screen.findByRole('listitem', { name: 'INV-4711.pdf' });
    await chooseMatter();
    await userEvent.type(screen.getByRole('textbox', { name: /Firm invoice number/ }), 'INV-4711');
    await waitFor(() => expect(submitButton()).toBeEnabled());
    await userEvent.click(submitButton());
  }

  it(
    'retryable: Retry re-sends the SAME action; after repeated failures the upload hint appears',
    async () => {
      const onSubmit = jest.fn(async () => ({ kind: 'retryable' as const, message: 'Outlook is busy. Try again.' }));
      renderView({ onSubmit });
      await fillAndSubmit();
      expect(await screen.findByText(/Outlook is busy\. Try again\./)).toBeInTheDocument();
      expect(screen.queryByText(new RegExp(INVOICE_UPLOAD_HINT))).toBeNull();

      await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
      await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(2));
      const calls = onSubmit.mock.calls as unknown as Array<[unknown, { actionId: string }]>;
      expect(calls[1]![1].actionId).toBe(calls[0]![1].actionId);
      expect(calls[1]![0]).toEqual(calls[0]![0]);
      expect(await screen.findByText(new RegExp(INVOICE_UPLOAD_HINT))).toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'a new Submit is a new action (a new actionId)',
    async () => {
      const onSubmit = jest.fn(async () => ({ kind: 'refused' as const, message: 'That firm is not linked.' }));
      renderView({ onSubmit });
      await fillAndSubmit();
      expect(await screen.findByText('That firm is not linked.')).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
      await userEvent.click(submitButton());
      await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(2));
      const calls = onSubmit.mock.calls as unknown as Array<[unknown, { actionId: string }]>;
      expect(calls[1]![1].actionId).not.toBe(calls[0]![1].actionId);
    },
    TIMEOUT
  );

  it(
    'permanent: shows the message and suggests uploading in Spaarke',
    async () => {
      const onSubmit = jest.fn(async () => ({
        kind: 'permanent' as const,
        message: "This email can't be identified.",
      }));
      renderView({ onSubmit });
      await fillAndSubmit();
      expect(await screen.findByText("This invoice can't be submitted from Outlook")).toBeInTheDocument();
      expect(screen.getByText(new RegExp(INVOICE_UPLOAD_HINT))).toBeInTheDocument();
    },
    TIMEOUT
  );
});

describe('InvoiceView — Existing invoice (task 133)', () => {
  it('offers no "Add supporting files"; a correction is shown disabled with the note', async () => {
    renderView();
    await userEvent.click(screen.getByRole('radio', { name: 'Existing invoice' }));
    expect(screen.queryByText(/supporting files/i)).toBeNull();
    expect(screen.getByRole('radio', { name: 'Submit a corrected invoice' })).toBeDisabled();
    expect(screen.getByText('Corrections from Outlook are not available yet')).toBeInTheDocument();
    expect(screen.getByLabelText('Search Invoice records')).toBeInTheDocument();
    // The submission form is not shown for an existing invoice while corrections are off.
    expect(screen.queryByRole('button', { name: 'Submit' })).toBeNull();
  });

  it('with corrections available, the invoice searches without filing access and the correction can be chosen', async () => {
    renderView({ correctionAvailable: true });
    await userEvent.click(screen.getByRole('radio', { name: 'Existing invoice' }));
    expect(screen.getByRole('radio', { name: 'Submit a corrected invoice' })).toBeEnabled();
    mockSearch.mockResolvedValue([]);
    await userEvent.type(screen.getByLabelText('Search Invoice records'), 'INV');
    await userEvent.click(screen.getByRole('button', { name: 'Search' }));
    await waitFor(() => expect(mockSearch).toHaveBeenCalled());
    const options = mockSearch.mock.calls[0]![2];
    expect(mockSearch.mock.calls[0]![1]).toBe('Invoice');
    expect(options.access).toBeUndefined();
  });
});

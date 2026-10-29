/**
 * Task 133: the Outlook "Submit Invoice" view. The record searches are doubled at `searchOfficeEntities` (the
 * Save picker's own search); the submission and the firm list are the view's injected seams. The attachment marking
 * is the REAL shared `InvoiceAttachmentMarking` (ebilling 118a), through the jest alias.
 */
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { InvoiceView, INVOICE_UPLOAD_HINT, type InvoiceViewProps } from '../InvoiceView';
import { searchOfficeEntities } from '../../../services/officeEntitySearch';
import type { IHostAdapter } from '@shared/adapters';
import type { AttachmentInfo } from '@shared/adapters/types';
import type { EntitySearchResult } from '../../../hooks/useEntitySearch';
import type { InvoiceSubmissionOutcome, InvoiceSubmitAttempt } from '../../../services/invoiceSubmissionSeam';

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
  const { unmount } = render(
    <FluentProvider theme={webLightTheme}>
      <InvoiceView {...all} />
    </FluentProvider>
  );
  return { onSubmit: all.onSubmit as jest.Mock, loadFirms: all.loadFirms as jest.Mock, unmount };
}

async function chooseMatter() {
  mockSearch.mockResolvedValue([MATTER]);
  await userEvent.type(screen.getByLabelText('Search Matter records'), 'Smith');
  await userEvent.click(screen.getByRole('button', { name: 'Search' }));
  await userEvent.click(await screen.findByRole('button', { name: 'Select this record' }));
}

/** The marking list's row for a file (the row has no accessible name of its own; its radio group is named by it). */
async function findRow(fileName: string): Promise<HTMLElement> {
  const name = await screen.findByText(fileName);
  return name.closest('[role="listitem"]') as HTMLElement;
}

const submitButton = () => screen.getByRole('button', { name: /^(Submit|Submitting…|Processing…|Submitted)$/ });

beforeEach(() => {
  mockSearch.mockReset();
});

describe('InvoiceView — New invoice (task 133)', () => {
  it(
    'must-fire: Submit stays disabled without a Matter, Project or Work Assignment',
    async () => {
      renderView();
      await findRow('INV-4711.pdf');
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
      await findRow('INV-4711.pdf');
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
      const invoiceRow = await findRow('INV-4711.pdf');
      await chooseMatter();
      await userEvent.type(screen.getByRole('textbox', { name: /Firm invoice number/ }), 'INV-4711');
      await waitFor(() => expect(submitButton()).toBeEnabled());
      await userEvent.click(within(invoiceRow).getByRole('radio', { name: 'Supporting' }));
      expect(submitButton()).toBeDisabled();
      expect(screen.getByText('Mark the PDF that is the invoice.')).toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'must-fire: a non-PDF offers no Invoice choice; with no sendable PDF the view says the invoice must be a PDF',
    async () => {
      const csvAndCloudPdf: AttachmentInfo[] = [
        { ...ATTACHMENTS[1]! },
        {
          id: 'att-9',
          name: 'shared.pdf',
          contentType: 'application/pdf',
          size: 10,
          isInline: false,
          attachmentType: 'cloud',
        },
      ];
      renderView({
        hostAdapter: {
          getCapabilities: () => ({ canGetAttachments: true }),
          getAttachments: jest.fn(async () => csvAndCloudPdf),
        } as unknown as IHostAdapter,
      });
      const csvRow = await findRow('timesheet.csv');
      expect(within(csvRow).queryByRole('radio', { name: 'Invoice' })).toBeNull();
      expect(within(await findRow('shared.pdf')).queryByRole('radio', { name: 'Invoice' })).toBeNull();
      expect(
        screen.getByText(
          'The invoice must be a PDF attached to this email, and this email has none that can be submitted.'
        )
      ).toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'two PDFs: none is pre-selected; marking both as Invoice is refused with its own message',
    async () => {
      const twoPdfs: AttachmentInfo[] = [
        { ...ATTACHMENTS[0]! },
        {
          id: 'att-8',
          name: 'INV-4712.pdf',
          contentType: 'application/pdf',
          size: 99,
          isInline: false,
          attachmentType: 'file',
        },
      ];
      renderView({
        hostAdapter: {
          getCapabilities: () => ({ canGetAttachments: true }),
          getAttachments: jest.fn(async () => twoPdfs),
        } as unknown as IHostAdapter,
      });
      const first = await findRow('INV-4711.pdf');
      const second = await findRow('INV-4712.pdf');
      expect(screen.getByText('Mark the PDF that is the invoice.')).toBeInTheDocument();
      await userEvent.click(within(first).getByRole('radio', { name: 'Invoice' }));
      await userEvent.click(within(second).getByRole('radio', { name: 'Invoice' }));
      expect(screen.getByText('Only one file can be the invoice.')).toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'wire marks: a disabled attachment is DontSave; inline attachments get no mark',
    async () => {
      const withCloud: AttachmentInfo[] = [
        ...ATTACHMENTS,
        {
          id: 'att-9',
          name: 'shared.pdf',
          contentType: 'application/pdf',
          size: 10,
          isInline: false,
          attachmentType: 'cloud',
        },
      ];
      const { onSubmit } = renderView({
        hostAdapter: {
          getCapabilities: () => ({ canGetAttachments: true }),
          getAttachments: jest.fn(async () => withCloud),
        } as unknown as IHostAdapter,
      });
      await findRow('shared.pdf');
      await chooseMatter();
      await userEvent.type(screen.getByRole('textbox', { name: /Firm invoice number/ }), 'INV-4711');
      await waitFor(() => expect(submitButton()).toBeEnabled());
      await userEvent.click(submitButton());
      await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
      const input = (
        onSubmit.mock.calls[0] as unknown as [{ marks: Array<{ attachmentId: string; mark: string }> }]
      )[0];
      expect(input.marks.map(m => [m.attachmentId, m.mark])).toEqual([
        ['att-1', 'Invoice'],
        ['att-2', 'Supporting'],
        ['att-9', 'DontSave'],
      ]);
    },
    TIMEOUT
  );

  it(
    'no firm linked to the record: the empty state, and Submit stays disabled',
    async () => {
      renderView({ loadFirms: jest.fn(async () => []) });
      await findRow('INV-4711.pdf');
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
      await findRow('INV-4711.pdf');
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
    await findRow('INV-4711.pdf');
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
    'accepted: shows the submission processing until its final status, then the result',
    async () => {
      let finish: (outcome: InvoiceSubmissionOutcome) => void = () => undefined;
      const onSubmit = jest.fn(
        (_input: unknown, attempt: InvoiceSubmitAttempt) =>
          new Promise<InvoiceSubmissionOutcome>(resolve => {
            attempt.onAccepted?.({ trackingNumber: 'INV-4711.1' });
            finish = resolve;
          })
      );
      renderView({ onSubmit });
      await fillAndSubmit();
      expect(await screen.findByText('Processing the invoice')).toBeInTheDocument();
      expect(screen.getByText(/Tracking number INV-4711\.1/)).toBeInTheDocument();
      expect(submitButton()).toHaveTextContent('Processing…');
      expect(submitButton()).toBeDisabled();

      finish({ kind: 'success', trackingNumber: 'INV-4711.1' });
      expect(await screen.findByText('Invoice submitted')).toBeInTheDocument();
      expect(screen.queryByText('Processing the invoice')).toBeNull();
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
  it('offers "Add supporting files" and a correction, both disabled with their reasons', async () => {
    renderView();
    await userEvent.click(screen.getByRole('radio', { name: 'Existing invoice' }));
    // ebilling 123's upload route is not built yet: shown, disabled, with the reason — and it submits nothing.
    expect(screen.getByRole('radio', { name: 'Add supporting files' })).toBeDisabled();
    expect(screen.getByText('Adding supporting files from Outlook is not available yet')).toBeInTheDocument();
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
    // Supporting files stay off whatever the correction flag says: they depend on ebilling 123, not 097.
    expect(screen.getByRole('radio', { name: 'Add supporting files' })).toBeDisabled();
    mockSearch.mockResolvedValue([]);
    await userEvent.type(screen.getByLabelText('Search Invoice records'), 'INV');
    await userEvent.click(screen.getByRole('button', { name: 'Search' }));
    await waitFor(() => expect(mockSearch).toHaveBeenCalled());
    const options = mockSearch.mock.calls[0]![2];
    expect(mockSearch.mock.calls[0]![1]).toBe('Invoice');
    expect(options.access).toBeUndefined();
  });
});

describe('InvoiceView — review fixes (task 133)', () => {
  const numberBox = () => screen.getByRole('textbox', { name: /Firm invoice number/ });

  async function fillAndSubmit(number = 'INV-4711') {
    await findRow('INV-4711.pdf');
    await chooseMatter();
    await userEvent.type(numberBox(), number);
    await waitFor(() => expect(submitButton()).toBeEnabled());
    await userEvent.click(submitButton());
  }

  type Call = [
    { firmInvoiceNumber: string; poNumber: string | null; marks: Array<{ mark: string }> },
    InvoiceSubmitAttempt,
  ];
  const callsOf = (onSubmit: jest.Mock) => onSubmit.mock.calls as unknown as Call[];

  it('must-fire: a PO number over 100 characters (after trimming) shows an error and blocks Submit; must-not-fire at 100', async () => {
    renderView({ onSubmit: jest.fn() });
    await findRow('INV-4711.pdf');
    await chooseMatter();
    await userEvent.type(numberBox(), 'INV-4711');
    const poBox = screen.getByRole('textbox', { name: /PO number/ });
    await userEvent.type(poBox, ` ${'P'.repeat(100)} `);
    await waitFor(() => expect(submitButton()).toBeEnabled());
    expect(screen.queryByText('At most 100 characters.')).toBeNull();

    await userEvent.type(poBox, 'X');
    expect(await screen.findByText('At most 100 characters.')).toBeInTheDocument();
    expect(submitButton()).toBeDisabled();
  });

  describe('Retry sends what is on screen', () => {
    const retryable = () =>
      jest.fn(async () => ({ kind: 'retryable' as const, message: 'Outlook is busy. Try again.' }));

    it(
      'must-fire: after a retryable failure, editing the invoice number ends the Retry; the next Submit carries the new value',
      async () => {
        const onSubmit = retryable();
        renderView({ onSubmit });
        await fillAndSubmit('INV-4711');
        expect(await screen.findByRole('button', { name: 'Retry' })).toBeInTheDocument();

        await userEvent.type(numberBox(), '-A');

        // The stale outcome and its Retry are gone: Retry would have resent INV-4711.
        expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
        // (The screen-reader live region still holds the earlier announcement; the visible bar is what goes.)
        expect(screen.queryByText('The invoice was not submitted')).toBeNull();
        await userEvent.click(submitButton());

        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(2));
        const calls = callsOf(onSubmit);
        expect(calls[0]![0].firmInvoiceNumber).toBe('INV-4711');
        expect(calls[1]![0].firmInvoiceNumber).toBe('INV-4711-A');
        // A new attempt, not a retry of the first action.
        expect(calls[1]![1].actionId).not.toBe(calls[0]![1].actionId);
      },
      TIMEOUT
    );

    it(
      'editing the PO number or a mark also ends the Retry',
      async () => {
        const onSubmit = retryable();
        renderView({ onSubmit });
        await fillAndSubmit();
        expect(await screen.findByRole('button', { name: 'Retry' })).toBeInTheDocument();

        await userEvent.type(screen.getByRole('textbox', { name: /PO number/ }), 'PO-9');
        expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();

        await userEvent.click(submitButton());
        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(2));
        expect(callsOf(onSubmit)[1]![0].poNumber).toBe('PO-9');
        expect(await screen.findByRole('button', { name: 'Retry' })).toBeInTheDocument();

        const csvRow = await findRow('timesheet.csv');
        await userEvent.click(within(csvRow).getByRole('radio', { name: "Don't save" }));
        expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
        await userEvent.click(submitButton());
        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(3));
        expect(callsOf(onSubmit)[2]![0].marks.map(m => m.mark)).toEqual(['Invoice', 'DontSave']);
      },
      TIMEOUT
    );

    it(
      'must-not-fire: with nothing edited, Retry still resends the same request and action',
      async () => {
        const onSubmit = retryable();
        renderView({ onSubmit });
        await fillAndSubmit();
        await userEvent.click(await screen.findByRole('button', { name: 'Retry' }));
        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(2));
        const calls = callsOf(onSubmit);
        expect(calls[1]![1].actionId).toBe(calls[0]![1].actionId);
        expect(calls[1]![0]).toEqual(calls[0]![0]);
      },
      TIMEOUT
    );

    it(
      'must-not-fire: editing after a refusal keeps the refusal showing (only a retryable outcome is dropped)',
      async () => {
        renderView({
          onSubmit: jest.fn(async () => ({ kind: 'refused' as const, message: 'That firm is not linked.' })),
        });
        await fillAndSubmit();
        expect(await screen.findByText('The invoice was not submitted')).toBeInTheDocument();
        await userEvent.type(numberBox(), 'x');
        expect(screen.getByText('The invoice was not submitted')).toBeInTheDocument();
      },
      TIMEOUT
    );
  });

  describe('the size budget follows the live marking', () => {
    // Two files of 15,000,000 bytes each (base64 20,000,000): only one fits the 28,000,000 budget at a time.
    const BIG = 15_000_000;
    const twoBigFiles: AttachmentInfo[] = [
      { id: 'a-1', name: 'timesheet.csv', contentType: 'text/csv', size: BIG, isInline: false, attachmentType: 'file' },
      {
        id: 'a-2',
        name: 'INV-9.pdf',
        contentType: 'application/pdf',
        size: BIG,
        isInline: false,
        attachmentType: 'file',
      },
    ];
    const host = (list: AttachmentInfo[]) =>
      ({
        getCapabilities: () => ({ canGetAttachments: true }),
        getAttachments: jest.fn(async () => list),
      }) as unknown as IHostAdapter;

    it(
      "must-fire: the PDF the budget holds back becomes markable once the large Supporting file is set to Don't save",
      async () => {
        renderView({ hostAdapter: host(twoBigFiles) });
        const pdfRow = await findRow('INV-9.pdf');
        // The csv is marked Supporting by default and takes the budget: the PDF is locked, with the reason.
        expect(within(pdfRow).queryByRole('radio', { name: 'Invoice' })).toBeNull();
        expect(pdfRow.textContent).toMatch(/does not fit with the other marked files/);
        expect(screen.getByText(/The invoice PDF does not fit with the other marked files/)).toBeInTheDocument();

        await userEvent.click(within(await findRow('timesheet.csv')).getByRole('radio', { name: "Don't save" }));

        const unlocked = await findRow('INV-9.pdf');
        await waitFor(() => expect(within(unlocked).getByRole('radio', { name: 'Invoice' })).toBeEnabled());
        await userEvent.click(within(unlocked).getByRole('radio', { name: 'Invoice' }));
        expect(screen.queryByTestId('invoice-over-budget')).toBeNull();
      },
      TIMEOUT
    );

    it(
      'must-fire: a mark change that leaves a marked file past the budget names it, keeps its mark and blocks Submit',
      async () => {
        const { onSubmit } = renderView({ hostAdapter: host(twoBigFiles) });
        await findRow('INV-9.pdf');
        await chooseMatter();
        await userEvent.type(screen.getByRole('textbox', { name: /Firm invoice number/ }), 'INV-9');
        // csv Don't save -> the PDF unlocks -> mark it Invoice.
        await userEvent.click(within(await findRow('timesheet.csv')).getByRole('radio', { name: "Don't save" }));
        const pdfRow = await findRow('INV-9.pdf');
        await waitFor(() => expect(within(pdfRow).getByRole('radio', { name: 'Invoice' })).toBeEnabled());
        await userEvent.click(within(pdfRow).getByRole('radio', { name: 'Invoice' }));
        await waitFor(() => expect(submitButton()).toBeEnabled());

        // The csv comes BEFORE the PDF in the email, so it fits when marked again; the capture then leaves the
        // PDF out. The PDF is not unmarked behind the user's back: it is named, and Submit is blocked.
        const csvRow = await findRow('timesheet.csv');
        await waitFor(() => expect(within(csvRow).getByRole('radio', { name: 'Supporting' })).toBeEnabled());
        await userEvent.click(within(csvRow).getByRole('radio', { name: 'Supporting' }));
        const notice = await screen.findByTestId('invoice-over-budget');
        expect(notice.textContent).toMatch(/INV-9\.pdf.*doesn't fit with the other marked files/);
        expect(within(await findRow('INV-9.pdf')).getByRole('radio', { name: 'Invoice' })).toBeChecked();
        expect(submitButton()).toBeDisabled();

        // Setting the csv to Don't save makes room again: the notice goes and Submit works.
        await userEvent.click(within(await findRow('timesheet.csv')).getByRole('radio', { name: "Don't save" }));
        await waitFor(() => expect(screen.queryByTestId('invoice-over-budget')).toBeNull());
        await waitFor(() => expect(submitButton()).toBeEnabled());
        await userEvent.click(submitButton());
        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        const input = (
          onSubmit.mock.calls[0] as unknown as [{ marks: Array<{ attachmentId: string; mark: string }> }]
        )[0];
        expect(input.marks.map(m => [m.attachmentId, m.mark])).toEqual([
          ['a-1', 'DontSave'],
          ['a-2', 'Invoice'],
        ]);
      },
      TIMEOUT
    );

    it(
      'must-not-fire: files that fit together are all markable and Submit is not blocked',
      async () => {
        renderView();
        await findRow('INV-4711.pdf');
        await chooseMatter();
        await userEvent.type(screen.getByRole('textbox', { name: /Firm invoice number/ }), 'INV-4711');
        await waitFor(() => expect(submitButton()).toBeEnabled());
        expect(screen.queryByTestId('invoice-over-budget')).toBeNull();
      },
      TIMEOUT
    );

    it(
      'a cloud link stays disabled whatever else is marked, and says why',
      async () => {
        const withCloud: AttachmentInfo[] = [
          ...twoBigFiles,
          {
            id: 'a-3',
            name: 'shared.pdf',
            contentType: 'application/pdf',
            size: 10,
            isInline: false,
            attachmentType: 'cloud',
          },
        ];
        renderView({ hostAdapter: host(withCloud) });
        await findRow('shared.pdf');
        await userEvent.click(within(await findRow('timesheet.csv')).getByRole('radio', { name: "Don't save" }));
        const cloudRow = await findRow('shared.pdf');
        expect(within(cloudRow).queryByRole('radio', { name: 'Invoice' })).toBeNull();
        expect(cloudRow.textContent).toMatch(/cloud/);
      },
      TIMEOUT
    );
  });

  describe('results after the server accepted the submission', () => {
    it(
      'Rejected is titled "The invoice was rejected", keeps the tracking number, and is not "not submitted"',
      async () => {
        renderView({
          onSubmit: jest.fn(async () => ({
            kind: 'ended' as const,
            status: 'Rejected' as const,
            message: 'The invoice was rejected by a billing rule.',
            trackingNumber: 'INV-4711.1',
          })),
        });
        await fillAndSubmit();
        expect(await screen.findByText('The invoice was rejected')).toBeInTheDocument();
        expect(screen.getByText(/Tracking number INV-4711\.1\./)).toBeInTheDocument();
        expect(screen.getAllByText(/rejected by a billing rule/).length).toBeGreaterThan(0);
        expect(screen.queryByText('The invoice was not submitted')).toBeNull();
        // It is a result of a submission that exists: Submit stays available (a Failed one can be sent again).
        expect(submitButton()).toBeEnabled();
      },
      TIMEOUT
    );

    it(
      'Failed is titled "The invoice could not be processed" with the tracking number',
      async () => {
        renderView({
          onSubmit: jest.fn(async () => ({
            kind: 'ended' as const,
            status: 'Failed' as const,
            message: "The invoice couldn't be processed.",
            trackingNumber: 'INV-4711.2',
          })),
        });
        await fillAndSubmit();
        expect(await screen.findByText('The invoice could not be processed')).toBeInTheDocument();
        expect(screen.getByText(/Tracking number INV-4711\.2\./)).toBeInTheDocument();
        expect(screen.queryByText('The invoice was not submitted')).toBeNull();
      },
      TIMEOUT
    );

    it(
      'must-not-fire: a refusal before acceptance is still "not submitted"',
      async () => {
        renderView({ onSubmit: jest.fn(async () => ({ kind: 'refused' as const, message: 'Nope.' })) });
        await fillAndSubmit();
        expect(await screen.findByText('The invoice was not submitted')).toBeInTheDocument();
      },
      TIMEOUT
    );
  });

  describe('Done on a finished submission', () => {
    const endedOutcome = () =>
      jest.fn(async () => ({
        kind: 'ended' as const,
        status: 'Rejected' as const,
        message: 'The invoice was rejected by a billing rule.',
        trackingNumber: 'INV-4711.1',
      }));

    it(
      'must-fire: after an ended result, Done clears it, frees the tabs and leaves the form as it was',
      async () => {
        const onBusyChange = jest.fn();
        const onSubmit = endedOutcome();
        renderView({ onSubmit, onBusyChange });
        await fillAndSubmit('INV-4711');
        await screen.findByText('The invoice was rejected');
        expect(onBusyChange).toHaveBeenLastCalledWith(true);

        await userEvent.click(screen.getByRole('button', { name: 'Done' }));

        expect(screen.queryByText('The invoice was rejected')).toBeNull();
        await waitFor(() => expect(onBusyChange).toHaveBeenLastCalledWith(false));
        // Editable, with the values the user had.
        expect(numberBox()).toHaveValue('INV-4711');
        expect(numberBox()).toBeEnabled();
        expect(submitButton()).toBeEnabled();
        // The record and the firm are still chosen: submitting again sends the same request values.
        await userEvent.click(submitButton());
        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(2));
        const calls = onSubmit.mock.calls as unknown as Array<
          [{ firmInvoiceNumber: string; firmId: string; targetEntity: { entityId: string } }]
        >;
        expect(calls[1]![0]).toMatchObject({
          firmInvoiceNumber: 'INV-4711',
          firmId: FIRM_ID,
          targetEntity: { entityId: MATTER_ID },
        });
      },
      TIMEOUT
    );

    it(
      'must-fire: after a successful submission, Done resets the form for a new one and keeps the same email',
      async () => {
        const onBusyChange = jest.fn();
        const { onSubmit } = renderView({ onBusyChange });
        await findRow('INV-4711.pdf');
        await chooseMatter();
        await userEvent.type(numberBox(), 'INV-4711');
        await userEvent.type(screen.getByRole('textbox', { name: /PO number/ }), 'PO-1');
        await userEvent.click(within(await findRow('timesheet.csv')).getByRole('radio', { name: "Don't save" }));
        await waitFor(() => expect(submitButton()).toBeEnabled());
        await userEvent.click(submitButton());
        await screen.findByText('Invoice submitted');
        expect(onBusyChange).toHaveBeenLastCalledWith(true);

        await userEvent.click(screen.getByRole('button', { name: 'Done' }));

        expect(screen.queryByText('Invoice submitted')).toBeNull();
        await waitFor(() => expect(onBusyChange).toHaveBeenLastCalledWith(false));
        expect(numberBox()).toHaveValue('');
        expect(screen.getByRole('textbox', { name: /PO number/ })).toHaveValue('');
        expect(screen.getByLabelText('Search Matter records')).toBeInTheDocument();
        // The same email's attachments, marked as at the start (PDF is the invoice, the csv is saved again).
        expect(within(await findRow('INV-4711.pdf')).getByRole('radio', { name: 'Invoice' })).toBeChecked();
        expect(within(await findRow('timesheet.csv')).getByRole('radio', { name: 'Supporting' })).toBeChecked();
        expect(submitButton()).toBeDisabled();

        // A second submission works, as a new action.
        await chooseMatter();
        await userEvent.type(numberBox(), 'INV-4712');
        await waitFor(() => expect(submitButton()).toBeEnabled());
        await userEvent.click(submitButton());
        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(2));
        const calls = onSubmit.mock.calls as unknown as Array<[{ firmInvoiceNumber: string }, InvoiceSubmitAttempt]>;
        expect(calls[1]![0].firmInvoiceNumber).toBe('INV-4712');
        expect(calls[1]![1].actionId).not.toBe(calls[0]![1].actionId);
      },
      TIMEOUT
    );

    it(
      'must-not-fire: a result that can be retried or fixed has no Done',
      async () => {
        renderView({ onSubmit: jest.fn(async () => ({ kind: 'refused' as const, message: 'Nope.' })) });
        await fillAndSubmit();
        await screen.findByText('The invoice was not submitted');
        expect(screen.queryByRole('button', { name: 'Done' })).toBeNull();
      },
      TIMEOUT
    );
  });

  describe('attachments the capture left out', () => {
    const skippedOne = {
      attachmentId: 'att-9',
      name: 'shared.docx',
      ticked: false,
      reason: 'cloud-link' as const,
      message: '"shared.docx" was not saved with the email: it is a link to a file stored in the cloud.',
    };

    it(
      "must-fire: shows the Save view's warning, with each reason, next to a successful result",
      async () => {
        renderView({
          onSubmit: jest.fn(async () => ({
            kind: 'success' as const,
            trackingNumber: 'INV-4711.1',
            skipped: [skippedOne],
          })),
        });
        await fillAndSubmit();
        const warning = await screen.findByTestId('invoice-skipped-attachments');
        expect(within(warning).getByText('One attachment was not saved')).toBeInTheDocument();
        expect(within(warning).getByText(/"shared\.docx" was not saved with the email/)).toBeInTheDocument();
        expect(screen.getByText('Invoice submitted')).toBeInTheDocument();
      },
      TIMEOUT
    );

    it(
      'several are counted in the title',
      async () => {
        renderView({
          onSubmit: jest.fn(async () => ({
            kind: 'refused' as const,
            message: 'Nope.',
            skipped: [
              skippedOne,
              { ...skippedOne, attachmentId: 'att-10', name: 'b.docx', message: '"b.docx" was not saved.' },
            ],
          })),
        });
        await fillAndSubmit();
        expect(await screen.findByText('2 attachments were not saved')).toBeInTheDocument();
        expect(screen.getByText('"b.docx" was not saved.')).toBeInTheDocument();
      },
      TIMEOUT
    );

    it(
      'must-not-fire: nothing left out, no warning',
      async () => {
        renderView();
        await fillAndSubmit();
        await screen.findByText('Invoice submitted');
        expect(screen.queryByTestId('invoice-skipped-attachments')).toBeNull();
      },
      TIMEOUT
    );
  });

  describe('the firm list', () => {
    it(
      'must-fire: a failed firm list offers Retry, which asks again and then lists the firms',
      async () => {
        const loadFirms = jest
          .fn()
          .mockRejectedValueOnce(
            new Error("Spaarke couldn't list the firms for this record right now. Try again in a moment.")
          )
          .mockResolvedValue([{ firmId: FIRM_ID, name: 'Firm F' }]);
        renderView({ loadFirms });
        await findRow('INV-4711.pdf');
        await chooseMatter();
        expect(await screen.findByText(/couldn't list the firms/)).toBeInTheDocument();
        expect(loadFirms).toHaveBeenCalledTimes(1);

        await userEvent.click(screen.getByRole('button', { name: 'Retry loading the firms' }));

        await waitFor(() => expect(loadFirms).toHaveBeenCalledTimes(2));
        expect(loadFirms).toHaveBeenLastCalledWith({ entityType: 'Matter', entityId: MATTER_ID });
        // One firm linked: it is chosen for the user, and the error is gone.
        await waitFor(() => expect(screen.queryByText(/couldn't list the firms/)).toBeNull());
        expect(screen.queryByRole('button', { name: 'Retry loading the firms' })).toBeNull();
        expect(screen.getByRole('option', { name: 'Firm F' })).toBeInTheDocument();
      },
      TIMEOUT
    );

    it(
      'must-not-fire: a loaded firm list shows no Retry',
      async () => {
        renderView();
        await findRow('INV-4711.pdf');
        await chooseMatter();
        await screen.findByRole('option', { name: 'Firm F' });
        expect(screen.queryByRole('button', { name: 'Retry loading the firms' })).toBeNull();
      },
      TIMEOUT
    );
  });

  describe('leaving the view', () => {
    it(
      "must-fire: unmounting aborts the running submission's signal",
      async () => {
        let signal: AbortSignal | undefined;
        const onSubmit = jest.fn(
          (_input: unknown, attempt: InvoiceSubmitAttempt) =>
            new Promise<InvoiceSubmissionOutcome>(() => {
              signal = attempt.signal;
              attempt.onAccepted?.({ trackingNumber: 'INV-4711.1' });
            })
        );
        const { unmount } = renderView({ onSubmit });
        await fillAndSubmit();
        await screen.findByText('Processing the invoice');
        expect(signal).toBeDefined();
        expect(signal!.aborted).toBe(false);

        unmount();

        expect(signal!.aborted).toBe(true);
      },
      TIMEOUT
    );

    it(
      'onBusyChange: true while submitting and while an accepted result shows, false after a refusal and on unmount',
      async () => {
        const onBusyChange = jest.fn();
        const onSubmit = jest.fn(async () => ({ kind: 'success' as const, trackingNumber: 'INV-4711.1' }));
        const { unmount } = renderView({ onSubmit, onBusyChange });
        expect(onBusyChange).toHaveBeenLastCalledWith(false);
        await fillAndSubmit();
        await screen.findByText('Invoice submitted');
        expect(onBusyChange).toHaveBeenCalledWith(true);
        expect(onBusyChange).toHaveBeenLastCalledWith(true);
        unmount();
        expect(onBusyChange).toHaveBeenLastCalledWith(false);
      },
      TIMEOUT
    );

    it(
      'must-not-fire: a refusal (nothing was submitted) is not busy, so the tabs stay available',
      async () => {
        const onBusyChange = jest.fn();
        renderView({ onSubmit: jest.fn(async () => ({ kind: 'refused' as const, message: 'Nope.' })), onBusyChange });
        await fillAndSubmit();
        await screen.findByText('Nope.');
        expect(onBusyChange).toHaveBeenLastCalledWith(false);
      },
      TIMEOUT
    );
  });
});

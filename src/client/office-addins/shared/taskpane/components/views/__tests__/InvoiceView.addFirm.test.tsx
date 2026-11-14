/**
 * Task 134 slice C (ebilling 095g, EB-D-224): "Add a firm to this record" in the Outlook "Submit Invoice" view's Firm
 * field. The view runs against the REAL `listLawFirms` / `addFirmToRecord` service over a doubled `fetch`, so a test sees
 * the wire (the URL, the one-key body) and the sentence the user reads for each code. The contract is "Contract for
 * word-add-in-r1 (as built)" in `projects/ebilling-r1/notes/095g-assign-firm.md`. The sentences below are written out
 * here from that contract, not read from the pane's own table. Assertions are on visible elements, not the sr-only live
 * region (the pane's error text appears twice).
 */
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { InvoiceView, type InvoiceViewProps } from '../InvoiceView';
import { addFirmToRecord, listLawFirms } from '../../../services/invoiceSubmissionService';
import { buildOpenRecordUrl, configuredSpaarkeAppName } from '../../../services/openRecordLauncher';
import { searchOfficeEntities } from '../../../services/officeEntitySearch';
import type { IHostAdapter } from '@shared/adapters';
import type { AttachmentInfo } from '@shared/adapters/types';
import type { EntitySearchResult } from '../../../hooks/useEntitySearch';
import type { InvoiceCorrectionPrefill, InvoiceSubmissionTarget } from '../../../services/invoiceSubmissionSeam';

jest.mock('../../../services/officeEntitySearch', () => ({ searchOfficeEntities: jest.fn() }));
const mockSearch = searchOfficeEntities as jest.MockedFunction<typeof searchOfficeEntities>;
jest.mock('../../../services/communicationSuggestionsService', () => ({
  fetchRelatedCandidates: jest.fn(async () => []),
}));

const TIMEOUT = 30000;
const BASE = 'https://bff';
const ORG_URL = 'https://org.crm.dynamics.com';
const MATTER_ID = '6f1c0e8a-2b1d-4c7e-9a52-0d3f1b7e9c41';
const NEW_FIRM_ID = '8b0c6f1e-4c2a-4f7e-9a51-2f3d1c0e9b77';
const OTHER_FIRM_ID = '5d2a7c4b-1e6f-4a9b-8c3d-0f1e2a3b4c5d';
const INVOICE_ID = '3c9d2b1a-7e4f-4a6b-8c2d-1e0f9a8b7c65';
const NEW_FIRM = { firmId: NEW_FIRM_ID, name: 'Contoso LLP' };
const OTHER_FIRM = { firmId: OTHER_FIRM_ID, name: 'Other LLP' };
const NO_FIRM_LINKED = 'No firm is linked to this record. Add a firm to the record in Spaarke, then submit.';

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
const PREFILL: InvoiceCorrectionPrefill = {
  invoiceId: INVOICE_ID,
  firm: { id: NEW_FIRM_ID, name: 'Contoso LLP' },
  coreRecord: { entityType: 'Matter', entityId: MATTER_ID, name: 'PAT-792345' },
  invoiceStatus: 'InReview',
  trackingNumber: 'INV-012.2',
  firmInvoiceNumber: 'INV-012',
  canCorrect: true,
  reason: null,
  replacedBy: null,
};

// ----- the doubled wire ------------------------------------------------------------------------------------------

interface WireCall {
  url: string;
  method: string;
  body: string | undefined;
}
let wire: WireCall[] = [];
/** Answers for the next POST(s); the last one repeats. */
let postAnswers: { status: number; body: unknown }[] = [];
/** When set, a POST waits for it before answering (an add held in flight). */
let postGate: Promise<void> | null = null;
/** The law-firm list the GET returns. */
let firmPage: { items: unknown[]; page?: number; hasMore: boolean } = { items: [NEW_FIRM, OTHER_FIRM], hasMore: false };
const mockFetch = jest.fn();
global.fetch = mockFetch as unknown as typeof fetch;

function response(status: number, body: unknown) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: { get: () => null },
    json: async () => {
      if (body === undefined) throw new Error('no body');
      return body;
    },
  };
}

function installWire(): void {
  mockFetch.mockImplementation(async (url: string, init: RequestInit = {}) => {
    const call: WireCall = { url, method: init.method ?? 'GET', body: init.body as string | undefined };
    wire.push(call);
    if (call.method === 'POST') {
      if (postGate) await postGate;
      const answer = postAnswers[Math.min(wire.filter(c => c.method === 'POST').length, postAnswers.length) - 1]!;
      return response(answer.status, answer.body);
    }
    return response(200, { ...firmPage, page: firmPage.page ?? 1 });
  });
}
const posts = (): WireCall[] => wire.filter(c => c.method === 'POST');

const okBody = (over: Record<string, unknown> = {}) => ({
  changed: true,
  firms: [NEW_FIRM],
  assigned: [{ slot: 1, firmId: NEW_FIRM_ID, name: 'Contoso LLP', active: true }],
  access: { state: 'applied', granted: [{ firmId: NEW_FIRM_ID, name: 'Contoso LLP' }], revoked: [] },
  recordActive: true,
  notice: "Contoso LLP's people will get access to this record.",
  ...over,
});

// ----- the view ---------------------------------------------------------------------------------------------------

function adapter(): IHostAdapter {
  return {
    getCapabilities: () => ({ canGetAttachments: true }),
    getAttachments: jest.fn(async () => ATTACHMENTS),
  } as unknown as IHostAdapter;
}

function renderView(props: Partial<InvoiceViewProps> = {}, withSeams = true) {
  const config = { apiBaseUrl: BASE, getAccessToken: async () => 'token' };
  const loadFirms = jest.fn(async () => [] as { firmId: string; name: string }[]);
  const all = {
    hostAdapter: adapter(),
    apiBaseUrl: BASE,
    getAccessToken: async () => 'token',
    onSubmit: jest.fn(async () => ({ kind: 'success' as const })),
    loadFirms,
    correctionAvailable: true,
    loadCorrectionPrefill: jest.fn(async (id: string) => ({ ...PREFILL, invoiceId: id })),
    ...(withSeams
      ? {
          listLawFirms: (args: Parameters<typeof listLawFirms>[0], signal?: AbortSignal) =>
            listLawFirms(args, config, signal),
          addFirmToRecord: (target: InvoiceSubmissionTarget, firmId: string) => addFirmToRecord(target, firmId, config),
        }
      : {}),
    ...props,
  } as InvoiceViewProps;
  const { unmount } = render(
    <FluentProvider theme={webLightTheme}>
      <InvoiceView {...all} />
    </FluentProvider>
  );
  return { loadFirms: all.loadFirms as jest.Mock, onSubmit: all.onSubmit as jest.Mock, unmount };
}

async function chooseMatter() {
  mockSearch.mockResolvedValue([MATTER]);
  await screen.findByText('INV-4711.pdf');
  await userEvent.type(screen.getByLabelText('Search Matter records'), 'Smith');
  await userEvent.click(screen.getByRole('button', { name: 'Search' }));
  await userEvent.click(await screen.findByRole('button', { name: 'Select this record' }));
}

const entryButton = () => screen.getByTestId('invoice-add-firm');
const confirmButton = () => screen.getByRole('button', { name: 'Add firm' });
const panel = () => screen.getByTestId('add-firm-panel');

/** Chooses the matter (no firm linked yet), opens the panel and chooses a firm in it. */
async function openPanelAndChoose(firmName: string) {
  await chooseMatter();
  await screen.findByText(NO_FIRM_LINKED);
  await userEvent.click(entryButton());
  await userEvent.click(await within(panel()).findByRole('option', { name: firmName }));
}

/** The Firm field's select: its chosen value. */
const firmSelect = () => screen.getByRole('combobox', { name: /Firm/ });

const isSetSupported = (): jest.Mock => Office.context.requirements.isSetSupported as unknown as jest.Mock;
const originalOrgUrl = process.env.ORG_URL;

beforeEach(() => {
  mockSearch.mockReset();
  mockFetch.mockReset();
  wire = [];
  postGate = null;
  postAnswers = [{ status: 200, body: okBody() }];
  firmPage = { items: [NEW_FIRM, OTHER_FIRM], hasMore: false };
  installWire();
  delete process.env.ORG_URL;
  isSetSupported().mockReturnValue(false);
});

afterEach(() => {
  jest.restoreAllMocks();
  isSetSupported().mockReturnValue(true); // jest.setup.js's default
  if (originalOrgUrl === undefined) delete process.env.ORG_URL;
  else process.env.ORG_URL = originalOrgUrl;
});

// ----- the entry point --------------------------------------------------------------------------------------------

describe('InvoiceView — the "Add a firm to this record" entry point', () => {
  it(
    'must-fire: with no firm linked it shows next to the message, "Check again" and "Open in Spaarke"',
    async () => {
      process.env.ORG_URL = ORG_URL;
      renderView();
      await chooseMatter();
      expect(await screen.findByText(NO_FIRM_LINKED)).toBeInTheDocument();
      expect(entryButton()).toHaveTextContent('Add a firm to this record');
      expect(screen.getByTestId('invoice-no-firm-recheck')).toBeInTheDocument();
      expect(screen.getByTestId('invoice-no-firm-open')).toBeInTheDocument();
      // Opening the panel sends nothing, and the button gives way to the panel.
      await userEvent.click(entryButton());
      expect(await screen.findByLabelText('Search law firms')).toBeInTheDocument();
      expect(screen.queryByTestId('invoice-add-firm')).not.toBeInTheDocument();
      expect(posts()).toHaveLength(0);
    },
    TIMEOUT
  );

  it(
    'must-fire: with firms listed it reads "Add another firm"',
    async () => {
      renderView({ loadFirms: jest.fn(async () => [OTHER_FIRM]) });
      await chooseMatter();
      await waitFor(() => expect(entryButton()).toHaveTextContent('Add another firm'));
      expect(screen.queryByText('Add a firm to this record')).not.toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'must-not-fire: absent in correction mode (and present in New invoice mode, so the check can fail)',
    async () => {
      renderView({ loadFirms: jest.fn(async () => [OTHER_FIRM]) });
      await chooseMatter();
      await waitFor(() => expect(screen.getByTestId('invoice-add-firm')).toBeInTheDocument());

      mockSearch.mockResolvedValue([INVOICE]);
      await userEvent.click(screen.getByRole('radio', { name: 'Existing invoice' }));
      await userEvent.type(screen.getByLabelText('Search Invoice records'), 'INV');
      await userEvent.click(screen.getByRole('button', { name: 'Search' }));
      await userEvent.click(await screen.findByRole('button', { name: 'Select this record' }));
      await userEvent.click(screen.getByRole('radio', { name: 'Submit a corrected invoice' }));
      await screen.findByTestId('invoice-correction-details');
      expect(screen.queryByTestId('invoice-add-firm')).not.toBeInTheDocument();
      expect(screen.queryByText(/Add (a|another) firm/)).not.toBeInTheDocument();
      expect(screen.queryByTestId('add-firm-panel')).not.toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'must-not-fire: absent when the view is given no firm seams',
    async () => {
      renderView({}, false);
      await chooseMatter();
      expect(await screen.findByText(NO_FIRM_LINKED)).toBeInTheDocument();
      expect(screen.queryByTestId('invoice-add-firm')).not.toBeInTheDocument();
    },
    TIMEOUT
  );
});

// ----- adding a firm ---------------------------------------------------------------------------------------------

describe('InvoiceView — adding a firm', () => {
  it(
    'must-fire: the list request goes to GET /api/finance/firms with page 1 and pageSize 25 (no search until typed)',
    async () => {
      renderView();
      await chooseMatter();
      await screen.findByText(NO_FIRM_LINKED);
      await userEvent.click(entryButton());
      await within(panel()).findByRole('option', { name: 'Contoso LLP' });
      const gets = wire.filter(c => c.url.includes('/api/finance/firms'));
      expect(gets).toHaveLength(1);
      expect(gets[0]!.url).toBe(`${BASE}/api/finance/firms?page=1&pageSize=25`);
    },
    TIMEOUT
  );

  it(
    'must-fire: the pre-confirm notice names the firm, and nothing is POSTed until "Add firm" is clicked',
    async () => {
      renderView();
      await openPanelAndChoose('Contoso LLP');
      expect(screen.getByTestId('add-firm-access-notice')).toHaveTextContent(
        "Adding Contoso LLP can give that firm's people access to this record. Spaarke will say what it did."
      );
      expect(posts()).toHaveLength(0);
      await userEvent.click(confirmButton());
      await waitFor(() => expect(posts()).toHaveLength(1));
    },
    TIMEOUT
  );

  it(
    'must-not-fire: Cancel sends nothing and closes the panel; the entry point comes back',
    async () => {
      renderView();
      await openPanelAndChoose('Contoso LLP');
      await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
      expect(screen.queryByTestId('add-firm-panel')).not.toBeInTheDocument();
      expect(posts()).toHaveLength(0);
      expect(entryButton()).toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'must-fire: a 200 sends exactly {"add": "<guid>"}, shows the server notice, refreshes the firms from the answer and selects the new firm',
    async () => {
      const { loadFirms } = renderView();
      await openPanelAndChoose('Contoso LLP');
      await userEvent.click(confirmButton());

      const notice = await screen.findByTestId('invoice-add-firm-notice');
      expect(notice).toHaveTextContent("Contoso LLP's people will get access to this record.");
      expect(posts()).toHaveLength(1);
      expect(posts()[0]!.url).toBe(`${BASE}/api/finance/core-records/Matter/${MATTER_ID}/firms`);
      expect(posts()[0]!.body).toBe(JSON.stringify({ add: NEW_FIRM_ID }));
      expect(Object.keys(JSON.parse(posts()[0]!.body!) as object)).toEqual(['add']);
      // The panel closed; the firm list is the answer's own `firms` (no second read), and the new firm is chosen.
      expect(screen.queryByTestId('add-firm-panel')).not.toBeInTheDocument();
      expect(loadFirms).toHaveBeenCalledTimes(1);
      expect(firmSelect()).toHaveValue(NEW_FIRM_ID);
      expect(screen.queryByText(NO_FIRM_LINKED)).not.toBeInTheDocument();
      // The entry point stays, now as "Add another firm".
      expect(entryButton()).toHaveTextContent('Add another firm');
    },
    TIMEOUT
  );

  it(
    'a null notice reads "<firm> is on this record."',
    async () => {
      postAnswers = [{ status: 200, body: okBody({ notice: null }) }];
      renderView();
      await openPanelAndChoose('Contoso LLP');
      await userEvent.click(confirmButton());
      expect(await screen.findByTestId('invoice-add-firm-notice')).toHaveTextContent(
        'Contoso LLP is on this record.'
      );
    },
    TIMEOUT
  );

  it(
    'changed: false (the firm was already there) is treated as a success',
    async () => {
      postAnswers = [{ status: 200, body: okBody({ changed: false, notice: null }) }];
      renderView();
      await openPanelAndChoose('Contoso LLP');
      await userEvent.click(confirmButton());
      expect(await screen.findByTestId('invoice-add-firm-notice')).toBeInTheDocument();
      expect(screen.queryByTestId('add-firm-panel')).not.toBeInTheDocument();
      expect(firmSelect()).toHaveValue(NEW_FIRM_ID);
    },
    TIMEOUT
  );

  it(
    'must-fire: a 200 with firms: null reloads the firm list, and selects the new firm once it is listed',
    async () => {
      postAnswers = [{ status: 200, body: okBody({ firms: null }) }];
      const loadFirms = jest.fn().mockResolvedValueOnce([]).mockResolvedValue([NEW_FIRM, OTHER_FIRM]);
      renderView({ loadFirms });
      await openPanelAndChoose('Contoso LLP');
      await userEvent.click(confirmButton());
      await waitFor(() => expect(loadFirms).toHaveBeenCalledTimes(2));
      await waitFor(() => expect(firmSelect()).toHaveValue(NEW_FIRM_ID));
      expect(await screen.findByTestId('invoice-add-firm-notice')).toBeInTheDocument();
      expect(screen.queryByTestId('add-firm-panel')).not.toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'must-fire: a pending access pass shows its text, still selects the firm, and Retry resends the SAME body',
    async () => {
      postAnswers = [
        { status: 200, body: okBody({ access: { state: 'pending', granted: [], revoked: [] } }) },
        { status: 200, body: okBody({ changed: false }) },
      ];
      renderView();
      await openPanelAndChoose('Contoso LLP');
      await userEvent.click(confirmButton());

      expect(await screen.findByTestId('add-firm-pending')).toHaveTextContent(
        'Saved. Spaarke is still updating who can see this record.'
      );
      // The firm is on the record already: the list is refreshed and the firm chosen, while the panel stays for Retry.
      expect(firmSelect()).toHaveValue(NEW_FIRM_ID);
      expect(screen.queryByTestId('invoice-add-firm-notice')).not.toBeInTheDocument();

      await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
      await waitFor(() => expect(posts()).toHaveLength(2));
      expect(posts()[1]!.body).toBe(posts()[0]!.body);
      expect(posts()[1]!.url).toBe(posts()[0]!.url);
      expect(await screen.findByTestId('invoice-add-firm-notice')).toBeInTheDocument();
      expect(screen.queryByTestId('add-firm-panel')).not.toBeInTheDocument();
      expect(firmSelect()).toHaveValue(NEW_FIRM_ID);
    },
    TIMEOUT
  );

  it(
    'an inactive record: the server notice is shown as given (the pane never claims access now)',
    async () => {
      const inactive = "Contoso LLP's people will get access to this record when it is active again.";
      postAnswers = [
        {
          status: 200,
          body: okBody({
            recordActive: false,
            notice: inactive,
            access: { state: 'applied', granted: [], revoked: [] },
          }),
        },
      ];
      renderView();
      await openPanelAndChoose('Contoso LLP');
      await userEvent.click(confirmButton());
      expect(await screen.findByTestId('invoice-add-firm-notice')).toHaveTextContent(inactive);
    },
    TIMEOUT
  );
});

// ----- refusals --------------------------------------------------------------------------------------------------

const RECORD_CHANGED = 'The firms on this record changed. Check them and try again.';
const TRY_AGAIN = "Spaarke couldn't do this right now. Try again.";
const GENERIC = "Spaarke couldn't add the firm right now. Nothing was changed.";

describe('InvoiceView — a refusal', () => {
  it.each([
    [
      404,
      'sdap.access.deny.record_unavailable',
      "This record or firm isn't available to you, or you can't change this record.",
    ],
    [
      409,
      'ebilling.firm_assign.record_restricted',
      'This record is Restricted to internal users, so no firm can be given access to it.',
    ],
    [
      409,
      'ebilling.firm_assign.record_secure',
      'This record is secure, so assigning a firm would give it no access. Use Manage Access to add named people.',
    ],
    [
      409,
      'ebilling.firm_assign.record_limited',
      'This record is limited, so assigning a firm would give it no access. Use Manage Access to add named people.',
    ],
    [422, 'ebilling.firm_assign.firm_inactive', 'This firm is inactive. Choose another firm.'],
    [422, 'ebilling.firm_assign.not_a_law_firm', 'This organization is not a law firm. Choose a law firm.'],
    [409, 'ebilling.firm_assign.firm_no_access', "This firm can't be given access to this record."],
    [409, 'ebilling.firm_assign.slots_full', 'Two firms are already assigned. Replace one of them in Spaarke.'],
    [409, 'ebilling.firm_assign.firm_already_assigned', 'That firm is already assigned to this record.'],
    [409, 'ebilling.firm_assign.record_changed', RECORD_CHANGED],
    [409, 'ebilling.firm_assign.revoke_confirmation_required', RECORD_CHANGED],
    [503, 'ebilling.submission.record_unavailable', TRY_AGAIN],
    [503, 'ebilling.disabled', 'Invoice submission is turned off in Spaarke.'],
  ])(
    'must-fire: %s %s shows its sentence in the panel, which stays open',
    async (status, code, sentence) => {
      postAnswers = [{ status, body: { errorCode: code, detail: 'Server text the pane must not show.' } }];
      renderView();
      await openPanelAndChoose('Contoso LLP');
      await userEvent.click(confirmButton());
      const refusal = await screen.findByTestId('add-firm-refusal');
      expect(refusal).toHaveTextContent(sentence);
      expect(refusal).not.toHaveTextContent('Server text the pane must not show.');
      expect(panel()).toContainElement(refusal);
      // Nothing was added: no notice, no firm chosen, and the buttons are usable again.
      expect(screen.queryByTestId('invoice-add-firm-notice')).not.toBeInTheDocument();
      expect(confirmButton()).toBeEnabled();
      expect(screen.getByText(NO_FIRM_LINKED)).toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'must-fire: slots_full also shows the "Open in Spaarke" link to the record (ORG_URL set)',
    async () => {
      process.env.ORG_URL = ORG_URL;
      const openSpy = jest.spyOn(window, 'open').mockReturnValue({} as Window);
      postAnswers = [{ status: 409, body: { errorCode: 'ebilling.firm_assign.slots_full' } }];
      renderView();
      await openPanelAndChoose('Contoso LLP');
      await userEvent.click(confirmButton());
      await screen.findByTestId('add-firm-refusal');
      const link = within(panel()).getByTestId('invoice-add-firm-open');
      expect(link).toHaveTextContent('Open in Spaarke');
      await userEvent.click(link);
      expect(openSpy).toHaveBeenCalledWith(
        buildOpenRecordUrl(ORG_URL, 'sprk_matter', MATTER_ID, configuredSpaarkeAppName()),
        '_blank'
      );
    },
    TIMEOUT
  );

  it(
    'must-not-fire: no other refusal shows that link inside the panel',
    async () => {
      process.env.ORG_URL = ORG_URL;
      postAnswers = [{ status: 409, body: { errorCode: 'ebilling.firm_assign.firm_already_assigned' } }];
      renderView();
      await openPanelAndChoose('Contoso LLP');
      await userEvent.click(confirmButton());
      await screen.findByTestId('add-firm-refusal');
      expect(within(panel()).queryByTestId('invoice-add-firm-open')).not.toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'must-fire: a 400, a 404 with no code, and an unknown code all show the generic sentence',
    async () => {
      renderView();
      await openPanelAndChoose('Contoso LLP');
      for (const answer of [
        { status: 400, body: { title: 'One or more validation errors occurred.', errors: { add: ['x'] } } },
        { status: 404, body: undefined },
        { status: 409, body: { errorCode: 'ebilling.firm_assign.a_code_from_the_future', detail: 'x' } },
      ]) {
        postAnswers = [answer];
        await userEvent.click(confirmButton());
        await waitFor(() => expect(screen.getByTestId('add-firm-refusal')).toHaveTextContent(GENERIC));
        await waitFor(() => expect(confirmButton()).toBeEnabled());
      }
      expect(posts()).toHaveLength(3);
    },
    TIMEOUT
  );

  it(
    'must-not-fire: two 503 bodies with the same code and different detail show the SAME text',
    async () => {
      renderView();
      await openPanelAndChoose('Contoso LLP');
      const shown: (string | null)[] = [];
      for (const detail of ['The firm assignment could not be saved.', 'Invoice submissions are unavailable.']) {
        postAnswers = [{ status: 503, body: { errorCode: 'ebilling.submission.record_unavailable', detail } }];
        await userEvent.click(confirmButton());
        await waitFor(() => expect(screen.getByTestId('add-firm-refusal')).toHaveTextContent(TRY_AGAIN));
        shown.push(screen.getByTestId('add-firm-refusal').textContent);
        expect(screen.getByTestId('add-firm-refusal')).not.toHaveTextContent(detail);
        await waitFor(() => expect(confirmButton()).toBeEnabled());
      }
      expect(shown[0]).toBe(shown[1]);
    },
    TIMEOUT
  );

  it(
    'a list error shows its sentence with Retry (a 503 on GET /api/finance/firms)',
    async () => {
      renderView();
      await chooseMatter();
      await screen.findByText(NO_FIRM_LINKED);
      mockFetch.mockImplementationOnce(async () =>
        response(503, { errorCode: 'ebilling.submission.record_unavailable', detail: 'Law firm type not configured' })
      );
      await userEvent.click(entryButton());
      const error = await screen.findByTestId('add-firm-list-error');
      expect(error).toHaveTextContent(TRY_AGAIN);
      await userEvent.click(within(error).getByRole('button', { name: 'Retry' }));
      expect(await within(panel()).findByRole('option', { name: 'Contoso LLP' })).toBeInTheDocument();
    },
    TIMEOUT
  );
});

// ----- review fixes (task 134 slice C, round 1) -----------------------------------------------------------------------

const UNCONFIRMED = "Spaarke couldn't confirm the firm was added. Try again; adding the same firm again is safe.";
/** The elements that say, in words, that a firm's people have (or are given) access NOW. The sr-only live region is not one. */
const CLAIMS_ACCESS_NOW = /gives .* access|have access|has access/i;
const visibleAccessClaims = (): HTMLElement[] =>
  screen.queryAllByText(CLAIMS_ACCESS_NOW).filter(el => !el.closest('[aria-live]'));
const submitButton = () => screen.getByRole('button', { name: 'Submit' });
const THIRD_FIRM = { firmId: '2a4c6e8f-1b3d-4f5a-8c7e-9d0b1a2c3e4f', name: 'Third LLP' };

describe('InvoiceView — F1: the notice never promises access', () => {
  it(
    'an inactive record: no visible element claims access now, before or after the click; only the pane\'s "can give" sentence and the server\'s notice',
    async () => {
      const inactive = "Contoso LLP's people will get access to this record when it is active again.";
      postAnswers = [
        {
          status: 200,
          body: okBody({
            recordActive: false,
            notice: inactive,
            access: { state: 'applied', granted: [], revoked: [] },
          }),
        },
      ];
      renderView();
      await openPanelAndChoose('Contoso LLP');
      expect(screen.getByTestId('add-firm-access-notice')).toHaveTextContent(
        /^Adding Contoso LLP can give that firm's people access to this record\. Spaarke will say what it did\.$/
      );
      expect(visibleAccessClaims()).toHaveLength(0);
      await userEvent.click(confirmButton());
      expect(await screen.findByTestId('invoice-add-firm-notice')).toHaveTextContent(inactive);
      expect(visibleAccessClaims()).toHaveLength(0);
    },
    TIMEOUT
  );
});

describe('InvoiceView — focus after the panel opens and closes (a11y)', () => {
  it(
    'opening the panel focuses its search box',
    async () => {
      renderView();
      await chooseMatter();
      await screen.findByText(NO_FIRM_LINKED);
      await userEvent.click(entryButton());
      expect(await screen.findByLabelText('Search law firms')).toHaveFocus();
    },
    TIMEOUT
  );

  it(
    'Cancel returns focus to the entry button',
    async () => {
      renderView();
      await openPanelAndChoose('Contoso LLP');
      await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
      expect(screen.queryByTestId('add-firm-panel')).not.toBeInTheDocument();
      await waitFor(() => expect(entryButton()).toHaveFocus());
    },
    TIMEOUT
  );

  it(
    'a success focuses the notice (a focusable status bar), not the entry button',
    async () => {
      renderView();
      await openPanelAndChoose('Contoso LLP');
      await userEvent.click(confirmButton());
      const notice = await screen.findByTestId('invoice-add-firm-notice');
      expect(notice).toHaveAttribute('tabindex', '-1');
      await waitFor(() => expect(notice).toHaveFocus());
      expect(entryButton()).not.toHaveFocus();
    },
    TIMEOUT
  );

  it(
    'the pending view focuses Retry; Close then returns focus to the entry button',
    async () => {
      postAnswers = [{ status: 200, body: okBody({ access: { state: 'pending', granted: [], revoked: [] } }) }];
      renderView();
      await openPanelAndChoose('Contoso LLP');
      await userEvent.click(confirmButton());
      await screen.findByTestId('add-firm-pending');
      await waitFor(() => expect(screen.getByRole('button', { name: 'Retry' })).toHaveFocus());
      await userEvent.click(screen.getByRole('button', { name: 'Close' }));
      expect(screen.queryByTestId('add-firm-panel')).not.toBeInTheDocument();
      await waitFor(() => expect(entryButton()).toHaveFocus());
    },
    TIMEOUT
  );
});

describe('InvoiceView — an unknown 5xx and the list\'s 404', () => {
  it.each([
    [500, undefined, UNCONFIRMED],
    [502, { title: 'Bad gateway' }, UNCONFIRMED],
    [503, { errorCode: 'ebilling.something_new' }, UNCONFIRMED],
    [503, { errorCode: 'ebilling.submission.record_unavailable' }, TRY_AGAIN],
    [400, { title: 'One or more validation errors occurred.' }, GENERIC],
  ])(
    'a %s answer with body %j shows its own sentence ("Nothing was changed" only for a 4xx)',
    async (status, body, sentence) => {
      postAnswers = [{ status, body }];
      renderView();
      await openPanelAndChoose('Contoso LLP');
      await userEvent.click(confirmButton());
      const refusal = await screen.findByTestId('add-firm-refusal');
      expect(refusal).toHaveTextContent(sentence);
      if (sentence !== GENERIC) expect(refusal).not.toHaveTextContent(/Nothing was changed/);
      expect(confirmButton()).toBeEnabled();
    },
    TIMEOUT
  );

  it(
    'the law-firm list\'s uniform 404 says "You can\'t list law firms in Spaarke." (not the add sentence)',
    async () => {
      renderView();
      await chooseMatter();
      await screen.findByText(NO_FIRM_LINKED);
      mockFetch.mockImplementationOnce(async () =>
        response(404, { errorCode: 'sdap.access.deny.record_unavailable', detail: 'internal text' })
      );
      await userEvent.click(entryButton());
      const error = await screen.findByTestId('add-firm-list-error');
      expect(error).toHaveTextContent("You can't list law firms in Spaarke.");
      expect(error).not.toHaveTextContent(/isn't available to you/);
    },
    TIMEOUT
  );
});

describe('InvoiceView — wording', () => {
  it(
    'slots_full says to replace one of the firms in Spaarke',
    async () => {
      postAnswers = [{ status: 409, body: { errorCode: 'ebilling.firm_assign.slots_full' } }];
      renderView();
      await openPanelAndChoose('Contoso LLP');
      await userEvent.click(confirmButton());
      expect(await screen.findByTestId('add-firm-refusal')).toHaveTextContent(
        'Two firms are already assigned. Replace one of them in Spaarke.'
      );
    },
    TIMEOUT
  );

  it(
    'with no server notice the fallback reads "<firm> is on this record." (also for changed: false)',
    async () => {
      for (const changed of [true, false]) {
        postAnswers = [{ status: 200, body: okBody({ changed, notice: null }) }];
        const { unmount } = renderView();
        await openPanelAndChoose('Contoso LLP');
        await userEvent.click(confirmButton());
        const notice = await screen.findByTestId('invoice-add-firm-notice');
        expect(notice).toHaveTextContent('Contoso LLP is on this record.');
        expect(notice).not.toHaveTextContent(/was added/);
        unmount();
      }
    },
    TIMEOUT
  );
});

describe('InvoiceView — stale select-after-reload marker and a stale selection', () => {
  it(
    'a pending answer with firms: null, then a Retry whose answer carries firms, then Submit, Done and the reload: no firm is auto-selected',
    async () => {
      // The marker is set by the first (pending, firms: null) answer, whose reload is still in flight when Retry's answer
      // (which carries the list) arrives, so that reload is superseded and never consumes it.
      postAnswers = [
        { status: 200, body: okBody({ firms: null, access: { state: 'pending', granted: [], revoked: [] } }) },
        { status: 200, body: okBody({ changed: false, firms: [NEW_FIRM, OTHER_FIRM] }) },
      ];
      let releaseReload: (list: { firmId: string; name: string }[]) => void = () => undefined;
      const loadFirms = jest
        .fn()
        .mockResolvedValueOnce([])
        .mockImplementationOnce(() => new Promise(r => (releaseReload = r)))
        .mockResolvedValue([NEW_FIRM, OTHER_FIRM]);
      const { onSubmit } = renderView({ loadFirms, filedRecord: MATTER });
      await screen.findByText(NO_FIRM_LINKED);
      await userEvent.click(entryButton());
      await userEvent.click(await within(panel()).findByRole('option', { name: 'Contoso LLP' }));
      await userEvent.click(confirmButton());
      await screen.findByTestId('add-firm-pending');
      await waitFor(() => expect(loadFirms).toHaveBeenCalledTimes(2));

      await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
      await waitFor(() => expect(screen.queryByTestId('add-firm-panel')).not.toBeInTheDocument());
      expect(firmSelect()).toHaveValue(NEW_FIRM_ID);
      // The superseded reload finishes now; its answer is ignored.
      await act(async () => releaseReload([NEW_FIRM, OTHER_FIRM]));

      await userEvent.type(screen.getByRole('textbox', { name: /Firm invoice number/ }), 'A1');
      await waitFor(() => expect(submitButton()).toBeEnabled());
      await userEvent.click(submitButton());
      await screen.findByText('Invoice submitted');
      expect((onSubmit.mock.calls[0] as unknown as [{ firmId: string }])[0].firmId).toBe(NEW_FIRM_ID);
      await userEvent.click(screen.getByRole('button', { name: 'Done' }));

      // The form starts over: the reload lists both firms and chooses neither.
      await waitFor(() => expect(loadFirms).toHaveBeenCalledTimes(3));
      await waitFor(() => expect(within(firmSelect()).getAllByRole('option')).toHaveLength(3));
      expect(firmSelect()).toHaveValue('');
    },
    TIMEOUT
  );

  it(
    'K1: a chosen firm that the answer\'s list does not contain is cleared (Submit cannot send a firm that is not on the record)',
    async () => {
      postAnswers = [{ status: 200, body: okBody({ firms: [THIRD_FIRM] }) }];
      renderView({ loadFirms: jest.fn(async () => [OTHER_FIRM]) });
      await chooseMatter();
      await waitFor(() => expect(firmSelect()).toHaveValue(OTHER_FIRM_ID));
      await userEvent.type(screen.getByRole('textbox', { name: /Firm invoice number/ }), 'A1');
      await waitFor(() => expect(submitButton()).toBeEnabled());

      await userEvent.click(entryButton());
      await userEvent.click(await within(panel()).findByRole('option', { name: 'Contoso LLP' }));
      await userEvent.click(confirmButton());
      await screen.findByTestId('invoice-add-firm-notice');
      // The list is now [Third LLP] only; Other LLP is not on the record, so it is no longer the chosen firm.
      expect(within(firmSelect()).queryByRole('option', { name: 'Other LLP' })).not.toBeInTheDocument();
      expect(firmSelect()).toHaveValue('');
      expect(screen.getByText(/Still needed: the firm\./)).toBeInTheDocument();
      expect(submitButton()).toBeDisabled();
    },
    TIMEOUT
  );

  it(
    'must-not-fire: a chosen firm that the answer\'s list still contains stays chosen when the added firm is not listed',
    async () => {
      postAnswers = [{ status: 200, body: okBody({ firms: [OTHER_FIRM, THIRD_FIRM] }) }];
      renderView({ loadFirms: jest.fn(async () => [OTHER_FIRM]) });
      await chooseMatter();
      await waitFor(() => expect(firmSelect()).toHaveValue(OTHER_FIRM_ID));
      await userEvent.click(entryButton());
      await userEvent.click(await within(panel()).findByRole('option', { name: 'Contoso LLP' }));
      await userEvent.click(confirmButton());
      await screen.findByTestId('invoice-add-firm-notice');
      expect(firmSelect()).toHaveValue(OTHER_FIRM_ID);
    },
    TIMEOUT
  );
});

describe('InvoiceView — K2: an add in flight holds back Submit and the New / Existing switch', () => {
  it(
    'Submit and the mode switch are disabled while the add is pending, and enabled again once it resolves',
    async () => {
      let release: () => void = () => undefined;
      postGate = new Promise<void>(r => (release = r));
      renderView({ loadFirms: jest.fn(async () => [OTHER_FIRM]) });
      await chooseMatter();
      await waitFor(() => expect(firmSelect()).toHaveValue(OTHER_FIRM_ID));
      await userEvent.type(screen.getByRole('textbox', { name: /Firm invoice number/ }), 'A1');
      await waitFor(() => expect(submitButton()).toBeEnabled());
      expect(screen.getByRole('radio', { name: 'New invoice' })).toBeEnabled();
      expect(screen.getByRole('radio', { name: 'Existing invoice' })).toBeEnabled();

      await userEvent.click(entryButton());
      await userEvent.click(await within(panel()).findByRole('option', { name: 'Contoso LLP' }));
      await userEvent.click(confirmButton());
      await waitFor(() => expect(posts()).toHaveLength(1));
      await waitFor(() => expect(submitButton()).toBeDisabled());
      expect(screen.getByRole('radio', { name: 'New invoice' })).toBeDisabled();
      expect(screen.getByRole('radio', { name: 'Existing invoice' })).toBeDisabled();

      await act(async () => release());
      await screen.findByTestId('invoice-add-firm-notice');
      await waitFor(() => expect(submitButton()).toBeEnabled());
      expect(screen.getByRole('radio', { name: 'New invoice' })).toBeEnabled();
      expect(screen.getByRole('radio', { name: 'Existing invoice' })).toBeEnabled();
      expect(firmSelect()).toHaveValue(NEW_FIRM_ID);
    },
    TIMEOUT
  );
});

describe('InvoiceView — F3: stale answers are dropped', () => {
  it(
    'the record changes while an add is in flight: the panel is closed, no notice shows, and the new record\'s firms are not replaced by the old answer',
    async () => {
      let release: () => void = () => undefined;
      postGate = new Promise<void>(r => (release = r));
      const loadFirms = jest
        .fn()
        .mockResolvedValueOnce([]) // the first matter has no firm
        .mockResolvedValue([OTHER_FIRM]); // the second one has Other LLP
      renderView({ loadFirms });
      await openPanelAndChoose('Contoso LLP');
      await userEvent.click(confirmButton());
      await waitFor(() => expect(posts()).toHaveLength(1));

      // Pick another record while the add is still in flight.
      const SECOND: EntitySearchResult = { ...MATTER, id: '9d8c7b6a-5e4f-4a3b-8c2d-1e0f9a8b7c65', name: 'Brown v Green' };
      await userEvent.click(screen.getByRole('button', { name: 'Clear selection' }));
      mockSearch.mockResolvedValue([SECOND]);
      const box = screen.getByLabelText('Search Matter records');
      await userEvent.clear(box);
      await userEvent.type(box, 'Brown');
      await userEvent.click(screen.getByRole('button', { name: 'Search' }));
      await userEvent.click(await screen.findByRole('button', { name: 'Select this record' }));
      await waitFor(() => expect(firmSelect()).toHaveValue(OTHER_FIRM_ID));
      expect(screen.queryByTestId('add-firm-panel')).not.toBeInTheDocument();

      // The old record's answer arrives now.
      await act(async () => release());
      await act(async () => {
        await Promise.resolve();
      });
      expect(screen.queryByTestId('invoice-add-firm-notice')).not.toBeInTheDocument();
      expect(screen.queryByTestId('add-firm-panel')).not.toBeInTheDocument();
      expect(firmSelect()).toHaveValue(OTHER_FIRM_ID);
      expect(within(firmSelect()).queryByRole('option', { name: 'Contoso LLP' })).not.toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'a "Check again" reload held in flight does not overwrite the list an add has just answered with',
    async () => {
      let releaseReload: (list: { firmId: string; name: string }[]) => void = () => undefined;
      const loadFirms = jest
        .fn()
        .mockResolvedValueOnce([])
        .mockImplementationOnce(() => new Promise(r => (releaseReload = r)));
      renderView({ loadFirms });
      await openPanelAndChoose('Contoso LLP');
      // "Check again" asks for the firms; the answer is held.
      await userEvent.click(screen.getByTestId('invoice-no-firm-recheck'));
      await waitFor(() => expect(loadFirms).toHaveBeenCalledTimes(2));
      // The add answers with the list that includes the new firm F, and F is chosen.
      await userEvent.click(confirmButton());
      await waitFor(() => expect(firmSelect()).toHaveValue(NEW_FIRM_ID));
      // The held reload now answers with a list that lacks F: it is older than the add's answer, so it is dropped.
      await act(async () => releaseReload([]));
      expect(firmSelect()).toHaveValue(NEW_FIRM_ID);
      expect(within(firmSelect()).getByRole('option', { name: 'Contoso LLP' })).toBeInTheDocument();
      expect(screen.queryByText(NO_FIRM_LINKED)).not.toBeInTheDocument();
    },
    TIMEOUT
  );
});

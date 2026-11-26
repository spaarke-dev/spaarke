/**
 * Invoice Review — "Submit corrected invoice" (ebilling-r1 task 095f-2e2b; EB-D-245, design 095f-part2 section 16, option B').
 *
 * The action shows for an invoice that is In Review, asks the server's correction prefill first (so a refusal the server already knows is a
 * plain sentence), opens the Create Invoice wizard in Correction mode for the invoice, and reads the page again when the wizard closes. The
 * page, the action and the shared wizard's words run for real; the two API objects and the dialog launch are fakes.
 */
import '@testing-library/jest-dom';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { InvoiceReviewWithDecisions } from '../components/InvoiceReviewWithDecisions';
import type { OpenCorrectionWizard } from '../components/decision/SubmitCorrectedInvoiceAction';
import { createInvoiceDecisionApi, decisionRoutes } from '../services/decisionApi';
import {
  INVOICE_ID,
  correctionPrefill,
  failed,
  line,
  mockApi,
  mockDecisionApi,
  ok,
  problem,
  review,
  type MockDecisionApi,
} from './testKit';

function renderPage(
  opts: {
    status?: string;
    open?: OpenCorrectionWizard | null;
    decision?: MockDecisionApi;
    theme?: typeof webLightTheme;
    /** The caller's Write access to the invoice (the review's `flags.canDecide`); default: has it. */
    canDecide?: boolean;
  } = {}
) {
  const base = review([line(1), line(2)]);
  const inv = {
    ...base,
    header: { ...base.header, status: opts.status ?? 'InReview' },
    flags: { ...base.flags, canDecide: opts.canDecide ?? true },
  };
  const api = mockApi(inv);
  const decision = opts.decision ?? mockDecisionApi();
  const open = opts.open === null ? undefined : (opts.open ?? jest.fn(async () => ({ confirmed: true })));
  const view = render(
    <FluentProvider theme={opts.theme ?? webLightTheme}>
      <InvoiceReviewWithDecisions
        invoiceId={INVOICE_ID}
        api={api as never}
        decisionApi={decision as never}
        pollIntervalMs={60_000}
        openCorrectionWizard={open as OpenCorrectionWizard | undefined}
      />
    </FluentProvider>
  );
  return { api, decision, open, view };
}

const action = () => screen.findByTestId('submit-corrected-invoice');
const notice = () => screen.findByTestId('correction-notice');

beforeEach(() => {
  jest.spyOn(console, 'warn').mockImplementation(() => undefined);
});
afterEach(() => {
  jest.restoreAllMocks();
});

describe('Submit corrected invoice: when the action shows', () => {
  it('shows for an invoice that is In Review', async () => {
    renderPage();
    expect(await action()).toHaveTextContent('Submit corrected invoice');
  });

  it.each(['Approved', 'SentToAp', 'Paid', 'Rejected', 'ReturnedToFirm', 'Cancelled', 'Superseded', 'Unknown'])(
    'must-not-fire: is not shown for an invoice that is %s',
    async status => {
      const { api } = renderPage({ status });
      await waitFor(() => expect(api.getReview).toHaveBeenCalled());
      await screen.findByTestId('progress');
      expect(screen.queryByTestId('submit-corrected-invoice')).not.toBeInTheDocument();
    }
  );

  it('must-not-fire: a page given no launcher has no such action (the page as it was)', async () => {
    renderPage({ open: null });
    await screen.findByTestId('progress');
    expect(screen.queryByTestId('submit-corrected-invoice')).not.toBeInTheDocument();
    expect(screen.queryByTestId('correction-notice')).not.toBeInTheDocument();
  });

  it('must-fire: a view-only caller (no Write on the invoice) does not see the action, and the page never reads the prefill', async () => {
    const { api, decision } = renderPage({ canDecide: false });
    await waitFor(() => expect(api.getReview).toHaveBeenCalled());
    await screen.findByTestId('progress');
    expect(screen.queryByTestId('submit-corrected-invoice')).not.toBeInTheDocument();
    expect(decision.getCorrectionPrefill).not.toHaveBeenCalled();
  });

  it('must-not-fire: a caller with Write on an In Review invoice sees it (the same page, only the flag differs)', async () => {
    renderPage({ canDecide: true });
    expect(await action()).toBeInTheDocument();
  });

  it('works in dark mode: the provider carries the dark tokens and the button draws no colour of its own', async () => {
    const dark = renderPage({ theme: webDarkTheme });
    const button = await action();
    // No inline colour or background on the button or its footer: colour comes from Fluent classes and tokens only.
    expect(button.getAttribute('style')).toBeNull();
    const provider = button.closest('.fui-FluentProvider') as HTMLElement | null;
    expect(provider).not.toBeNull();
    const darkSurface = getComputedStyle(provider as HTMLElement).getPropertyValue('--colorNeutralBackground1');
    expect(darkSurface).toBe(webDarkTheme.colorNeutralBackground1);
    dark.view.unmount();

    renderPage({ theme: webLightTheme });
    const lightButton = await action();
    const lightSurface = getComputedStyle(lightButton.closest('.fui-FluentProvider') as HTMLElement).getPropertyValue(
      '--colorNeutralBackground1'
    );
    expect(lightSurface).toBe(webLightTheme.colorNeutralBackground1);
    expect(lightSurface).not.toBe(darkSurface);
  });
});

describe('Submit corrected invoice: opening the wizard and refreshing', () => {
  it('reads the correction prefill for this invoice, opens the wizard with the invoice id once, and reads the page again afterwards', async () => {
    const { api, decision, open } = renderPage();
    const reads = api.getReview.mock.calls.length;
    fireEvent.click(await action());
    await waitFor(() => expect(open).toHaveBeenCalledTimes(1));
    expect(decision.getCorrectionPrefill).toHaveBeenCalledWith(INVOICE_ID);
    expect(open).toHaveBeenCalledWith(INVOICE_ID);
    await waitFor(() => expect(api.getReview.mock.calls.length).toBeGreaterThan(reads));
    expect(screen.queryByTestId('correction-notice')).not.toBeInTheDocument();
  });

  it('a user who backed out of the dialog (confirmed: false) changes nothing, so the page is not read again', async () => {
    const open = jest.fn(async () => ({ confirmed: false }));
    const { api } = renderPage({ open });
    await action();
    const reads = api.getReview.mock.calls.length;
    fireEvent.click(await action());
    await waitFor(() => expect(open).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.getByTestId('submit-corrected-invoice')).toBeEnabled());
    expect(api.getReview.mock.calls.length).toBe(reads);
  });

  it('a dialog that resolves with nothing (the platform close button) reads the page again', async () => {
    const open = jest.fn(async () => undefined);
    const { api } = renderPage({ open });
    await action();
    const reads = api.getReview.mock.calls.length;
    fireEvent.click(await action());
    await waitFor(() => expect(api.getReview.mock.calls.length).toBeGreaterThan(reads));
  });

  it('a double click reads the prefill once and opens one wizard; the button is off while it is open', async () => {
    let close!: (v: { confirmed: boolean }) => void;
    const open = jest.fn(() => new Promise<{ confirmed: boolean }>(resolve => (close = resolve)));
    const { decision } = renderPage({ open });
    const button = await action();
    await act(async () => {
      button.click();
      button.click();
    });
    await waitFor(() => expect(open).toHaveBeenCalledTimes(1));
    expect(decision.getCorrectionPrefill).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('submit-corrected-invoice')).toBeDisabled();
    await act(async () => close({ confirmed: true }));
    await waitFor(() => expect(screen.getByTestId('submit-corrected-invoice')).toBeEnabled());
    expect(open).toHaveBeenCalledTimes(1);
  });

  it('a wizard that cannot be opened is a plain sentence, and nothing is read again', async () => {
    const open = jest.fn(async () => {
      throw new Error('Xrm is not available');
    });
    const { api } = renderPage({ open });
    await action();
    const reads = api.getReview.mock.calls.length;
    fireEvent.click(await action());
    expect(await notice()).toHaveTextContent('The corrected-invoice form could not be opened. Try again. Nothing was saved.');
    expect(api.getReview.mock.calls.length).toBe(reads);
  });
});

describe('Submit corrected invoice: each refusal is shown in plain words and the wizard is not opened', () => {
  const refusals: Array<[string, (d: MockDecisionApi) => void, RegExp]> = [
    [
      'the invoice moved on (invoice_status)',
      d => d.getCorrectionPrefill.mockResolvedValue(ok(correctionPrefill({ canCorrect: false, reason: 'invoice_status', invoiceStatus: 'Approved' }))),
      /Only an invoice in review can be corrected; this one has moved on, so nothing was saved\./,
    ],
    [
      'no finished submission (no_submission)',
      d => d.getCorrectionPrefill.mockResolvedValue(ok(correctionPrefill({ canCorrect: false, reason: 'no_submission' }))),
      /has no finished submission to correct, so nothing was saved\./,
    ],
    [
      'still being processed (submission_in_progress)',
      d => d.getCorrectionPrefill.mockResolvedValue(ok(correctionPrefill({ canCorrect: false, reason: 'submission_in_progress' }))),
      /still being processed\. Try again when it has finished\. Nothing was saved\./,
    ],
    [
      'the invoice is not available to this caller, or is gone (404)',
      // As the server answers it: the generic "not found" detail is there, and the correction's own sentence is shown anyway.
      d =>
        d.getCorrectionPrefill.mockResolvedValue(
          failed(problem(404, null, { reasonCode: 'sdap.access.deny.record_unavailable', detail: 'The requested record was not found.' }))
        ),
      /not available to you, or no longer exists, so it cannot be corrected\. Nothing was saved\./,
    ],
    [
      'the invoice could not be read (503)',
      d => d.getCorrectionPrefill.mockResolvedValue(failed(problem(503, 'ebilling.submission.record_unavailable'))),
      /could not be read right now\. Try again shortly\. Nothing was saved\./,
    ],
    [
      'no answer at all (network)',
      d => d.getCorrectionPrefill.mockResolvedValue(failed(new Error('Failed to fetch'))),
      /could not be read, so it cannot be corrected\. Try again shortly\. Nothing was saved\./,
    ],
    [
      'the invoice has no firm on file',
      d => d.getCorrectionPrefill.mockResolvedValue(ok(correctionPrefill({ firm: null }))),
      /cannot be corrected from here: its firm, number or record is not on file\. Nothing was saved\./,
    ],
    [
      "the server's own sentence wins",
      d => d.getCorrectionPrefill.mockResolvedValue(failed(problem(503, 'ebilling.submission.record_unavailable', { detail: 'The invoice store is paused.' }))),
      /The invoice store is paused\./,
    ],
  ];

  it.each(refusals)('%s', async (_name, arrange, words) => {
    const decision = mockDecisionApi();
    arrange(decision);
    const { api, open } = renderPage({ decision });
    await action();
    const reads = api.getReview.mock.calls.length;
    fireEvent.click(await action());
    expect(await notice()).toHaveTextContent(words);
    expect(open).not.toHaveBeenCalled();
    expect(api.getReview.mock.calls.length).toBe(reads);
    // The action is usable again, and the sentence can be dismissed.
    await waitFor(() => expect(screen.getByTestId('submit-corrected-invoice')).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Dismiss' }));
    expect(screen.queryByTestId('correction-notice')).not.toBeInTheDocument();
  });

  it('a new attempt clears the old sentence, and a refusal that is then gone opens the wizard', async () => {
    const decision = mockDecisionApi();
    decision.getCorrectionPrefill.mockResolvedValueOnce(ok(correctionPrefill({ canCorrect: false, reason: 'submission_in_progress' })));
    const { open } = renderPage({ decision });
    fireEvent.click(await action());
    await notice();
    fireEvent.click(await action());
    await waitFor(() => expect(open).toHaveBeenCalledTimes(1));
    expect(screen.queryByTestId('correction-notice')).not.toBeInTheDocument();
  });
});

describe('the correction prefill client (task 095f-2e2b)', () => {
  const BASE = 'https://bff.example.test/';

  it('reads GET /api/finance/invoices/{id}/correction-prefill, ids lower-cased', async () => {
    const fetchFn = jest.fn(async (_url: string, _init?: RequestInit) => ({ ok: true, status: 200, json: async () => correctionPrefill() }) as never);
    const result = await createInvoiceDecisionApi(fetchFn, BASE).getCorrectionPrefill(INVOICE_ID.toUpperCase());
    expect(result).toEqual({ ok: true, value: correctionPrefill() });
    expect(fetchFn.mock.calls[0][0]).toBe(`https://bff.example.test/api/finance/invoices/${INVOICE_ID}/correction-prefill`);
    expect(fetchFn.mock.calls[0][0]).toBe(decisionRoutes.correctionPrefill(BASE, INVOICE_ID));
    expect(fetchFn.mock.calls[0][1]?.method).toBe('GET');
  });

  it('refuses a body that is not a prefill (never a partial answer)', async () => {
    for (const body of [null, {}, { canCorrect: 'yes', coreRecord: {} }, { canCorrect: true }, { canCorrect: true, coreRecord: {}, firm: 5 }]) {
      const fetchFn = jest.fn(async () => ({ ok: true, status: 200, json: async () => body }) as never);
      expect((await createInvoiceDecisionApi(fetchFn, BASE).getCorrectionPrefill(INVOICE_ID)).ok).toBe(false);
    }
  });

  it('refuses a body without the firm invoice number (the library reader is the one validator)', async () => {
    const { firmInvoiceNumber: _omitted, ...withoutNumber } = correctionPrefill();
    for (const body of [withoutNumber, { ...withoutNumber, firmInvoiceNumber: null }, { ...withoutNumber, firmInvoiceNumber: 7 }]) {
      const fetchFn = jest.fn(async () => ({ ok: true, status: 200, json: async () => body }) as never);
      expect((await createInvoiceDecisionApi(fetchFn, BASE).getCorrectionPrefill(INVOICE_ID)).ok).toBe(false);
    }
  });

  it('on the page, a prefill body missing the firm invoice number reads as "could not be read": a sentence, no wizard, no unhandled rejection', async () => {
    const { firmInvoiceNumber: _omitted, ...withoutNumber } = correctionPrefill();
    const fetchFn = jest.fn(async () => ({ ok: true, status: 200, json: async () => withoutNumber }) as never);
    const real = createInvoiceDecisionApi(fetchFn, BASE);
    const decision = mockDecisionApi();
    decision.getCorrectionPrefill.mockImplementation(real.getCorrectionPrefill);
    const unhandled = jest.fn();
    process.on('unhandledRejection', unhandled);
    try {
      const { open } = renderPage({ decision });
      fireEvent.click(await action());
      expect(await notice()).toHaveTextContent(/could not be read, so it cannot be corrected\. Try again shortly\. Nothing was saved\./);
      expect(open).not.toHaveBeenCalled();
      await waitFor(() => expect(screen.getByTestId('submit-corrected-invoice')).toBeEnabled());
      await new Promise(resolve => setTimeout(resolve, 0));
      expect(unhandled).not.toHaveBeenCalled();
    } finally {
      process.off('unhandledRejection', unhandled);
    }
  });

  it('keeps a null firm', async () => {
    const fetchFn = jest.fn(async () => ({ ok: true, status: 200, json: async () => correctionPrefill({ firm: null }) }) as never);
    const result = await createInvoiceDecisionApi(fetchFn, BASE).getCorrectionPrefill(INVOICE_ID);
    expect(result.ok && result.value.firm).toBeNull();
  });
});

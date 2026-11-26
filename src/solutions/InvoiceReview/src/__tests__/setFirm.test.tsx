import '@testing-library/jest-dom';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { InvoiceReviewPage } from '../components/InvoiceReviewPage';
import { FIRM_FIRST_WORDS } from '../services/reviewModel';
import { INVOICE_ID, MATTER_ID, failed, flagged, line, mockApi, ok, problem, review, type MockApi } from './testKit';

/**
 * ebilling-r1 task 095d-3: the firm in the header is an editable field while the invoice has no firm, the approval state says it is needed and
 * the caller can write. The save is `POST .../invoices/{id}/firm` (the API's `setFirm`), run through the page's own `runWrite`.
 */

const FIRM_A = { firmId: 'a1a1a1a1-0000-4000-8000-0000000000a1', name: 'Alpha Law LLP' };
const FIRM_B = { firmId: 'b2b2b2b2-0000-4000-8000-0000000000b2', name: 'Beta & Partners' };

const lines = () => [line(1), flagged(2)];

/** An invoice that has no firm yet (and so no tracking number). */
function noFirm(overrides: { canDecide?: boolean } = {}) {
  const base = review(lines());
  return {
    ...base,
    header: { ...base.header, firm: null, trackingNumber: null },
    flags: { ...base.flags, canDecide: overrides.canDecide ?? true },
  };
}

/** The same invoice after the firm was set: the firm and its new tracking number show in the header. */
function withFirm() {
  const base = review(lines());
  return { ...base, header: { ...base.header, firm: { id: FIRM_A.firmId, name: FIRM_A.name }, trackingNumber: 'ALPHA-000042' } };
}

function renderPage(api: MockApi, opts: { firmNeeded?: boolean; refreshApproval?: jest.Mock; theme?: typeof webLightTheme } = {}) {
  return render(
    <FluentProvider theme={opts.theme ?? webLightTheme}>
      <InvoiceReviewPage
        invoiceId={INVOICE_ID}
        api={api as never}
        pollIntervalMs={60_000}
        firmNeeded={opts.firmNeeded ?? true}
        refreshApproval={opts.refreshApproval}
      />
    </FluentProvider>
  );
}

async function chooseFirm(name: string) {
  fireEvent.click(await screen.findByRole('combobox', { name: 'Billing firm' }));
  fireEvent.click(await screen.findByRole('option', { name }));
}

const setFirmButton = () => screen.getByRole('button', { name: 'Set firm' });

describe('InvoiceReviewPage: Set firm field (095d-3) - when it is editable', () => {
  it('must-fire: no firm + firmNeeded + canWrite shows the Billing firm field, reads the linked firms of the core record, and says what setting does', async () => {
    const api = mockApi(noFirm());
    api.listFirms.mockResolvedValue(ok([FIRM_A, FIRM_B]));
    renderPage(api);

    const combo = await screen.findByRole('combobox', { name: 'Billing firm' });
    expect(combo).toBeEnabled();
    expect(api.listFirms).toHaveBeenCalledTimes(1);
    expect(api.listFirms).toHaveBeenCalledWith('matter', MATTER_ID);
    expect(screen.getByTestId('set-firm-effect')).toHaveTextContent(
      "Setting the firm gives this invoice the firm's next tracking number and checks its billing rules again."
    );
    // Nothing is chosen, so nothing can be saved yet.
    expect(setFirmButton()).toBeDisabled();
    expect(screen.queryByText('Unknown firm', { exact: false })).not.toBeInTheDocument();

    fireEvent.click(combo);
    expect(await screen.findByRole('option', { name: FIRM_A.name })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: FIRM_B.name })).toBeInTheDocument();
    expect(api.setFirm).not.toHaveBeenCalled();
  });

  it('must-not-fire: an invoice that already has a firm stays read-only text, and nothing is requested', async () => {
    const api = mockApi(review(lines())); // the default review has a firm
    renderPage(api);
    expect(await screen.findByTestId('review-title')).toHaveTextContent('Example Firm LLP · Invoice INV-2025-78232');
    expect(screen.queryByTestId('set-firm')).not.toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Billing firm' })).not.toBeInTheDocument();
    expect(api.listFirms).not.toHaveBeenCalled();
    expect(api.setFirm).not.toHaveBeenCalled();
  });

  it('must-not-fire: the approval state does not need the firm (firmNeeded false) - read-only "Unknown firm" text, nothing requested', async () => {
    const api = mockApi(noFirm());
    renderPage(api, { firmNeeded: false });
    expect(await screen.findByTestId('review-title')).toHaveTextContent('Unknown firm · Invoice INV-2025-78232');
    expect(screen.queryByTestId('set-firm')).not.toBeInTheDocument();
    expect(api.listFirms).not.toHaveBeenCalled();
  });

  it('must-not-fire: a caller who cannot write sees read-only text, nothing requested', async () => {
    const api = mockApi(noFirm({ canDecide: false }));
    renderPage(api);
    expect(await screen.findByTestId('review-title')).toHaveTextContent('Unknown firm · Invoice INV-2025-78232');
    expect(screen.queryByTestId('set-firm')).not.toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Billing firm' })).not.toBeInTheDocument();
    expect(api.listFirms).not.toHaveBeenCalled();
  });

  it('the firm-first banner points at the field in the invoice header', async () => {
    renderPage(mockApi(noFirm()));
    const banner = await screen.findByTestId('firm-first-notice');
    expect(banner).toHaveTextContent(FIRM_FIRST_WORDS);
    expect(FIRM_FIRST_WORDS).toMatch(/set the firm in the invoice header above, then decide/i);
    expect(FIRM_FIRST_WORDS).not.toMatch(/add the firm/i);
  });

  it('uses Fluent tokens only: renders in the dark theme with the field and its button', async () => {
    renderPage(mockApi(noFirm()), { theme: webDarkTheme });
    expect(await screen.findByRole('combobox', { name: 'Billing firm' })).toBeInTheDocument();
    expect(setFirmButton()).toBeInTheDocument();
  });
});

describe('InvoiceReviewPage: Set firm field (095d-3) - the list of firms', () => {
  it('shows the empty-list sentence for the kind of core record, and no Set firm button', async () => {
    const api = mockApi(noFirm());
    api.listFirms.mockResolvedValue(ok([]));
    renderPage(api);
    expect(await screen.findByTestId('set-firm-empty')).toHaveTextContent('No firm is linked to this matter. Add one on the record first.');
    expect(screen.queryByRole('button', { name: 'Set firm' })).not.toBeInTheDocument();
  });

  it('a failed read says so and offers Retry; Retry asks again and shows the firms', async () => {
    const api = mockApi(noFirm());
    api.listFirms.mockResolvedValueOnce(failed(problem(503, null))).mockResolvedValue(ok([FIRM_A]));
    renderPage(api);

    const error = await screen.findByTestId('set-firm-load-error');
    expect(error).toHaveTextContent("couldn't be listed right now");
    expect(error).not.toHaveTextContent('No firm is linked');
    expect(screen.queryByRole('combobox', { name: 'Billing firm' })).not.toBeInTheDocument();

    fireEvent.click(screen.getByTestId('set-firm-retry'));
    expect(await screen.findByRole('combobox', { name: 'Billing firm' })).toBeInTheDocument();
    expect(api.listFirms).toHaveBeenCalledTimes(2);
    expect(screen.queryByTestId('set-firm-load-error')).not.toBeInTheDocument();
  });

  it('one linked firm is still an explicit choice: nothing is selected and nothing is saved until Set firm is pressed', async () => {
    const api = mockApi(noFirm());
    api.listFirms.mockResolvedValue(ok([FIRM_A]));
    renderPage(api);
    const combo = await screen.findByRole('combobox', { name: 'Billing firm' });
    expect(combo).toHaveTextContent('Choose the billing firm');
    expect(setFirmButton()).toBeDisabled();
    expect(api.setFirm).not.toHaveBeenCalled();
    await chooseFirm(FIRM_A.name);
    expect(setFirmButton()).toBeEnabled();
    expect(api.setFirm).not.toHaveBeenCalled();
  });
});

describe('InvoiceReviewPage: Set firm field (095d-3) - saving', () => {
  it('posts the chosen firm, re-reads the review and the approval state, and says the new tracking number', async () => {
    const api = mockApi([noFirm(), withFirm()]);
    api.listFirms.mockResolvedValue(ok([FIRM_A, FIRM_B]));
    api.setFirm.mockResolvedValue(ok({ outcome: 'assigned', decisionId: 'd1', trackingNumber: 'ALPHA-000042', previousTrackingNumber: null, rulesRerun: 'completed' }));
    const refreshApproval = jest.fn(async () => ({ firmNeeded: false }));
    renderPage(api, { refreshApproval });

    await chooseFirm(FIRM_A.name);
    fireEvent.click(setFirmButton());

    const notice = await screen.findByTestId('notice');
    expect(notice).toHaveTextContent('Firm set. The tracking number is now ALPHA-000042.');
    expect(notice).not.toHaveTextContent('Billing rules are being checked again');
    expect(notice).toHaveAttribute('data-intent', 'success');
    expect(api.setFirm).toHaveBeenCalledTimes(1);
    expect(api.setFirm).toHaveBeenCalledWith(INVOICE_ID, FIRM_A.firmId);
    expect(api.getReview).toHaveBeenCalledTimes(2);
    expect(refreshApproval).toHaveBeenCalledTimes(1);
    // The header now shows the firm; the field is gone (an invoice that has a firm is never editable).
    expect(screen.getByTestId('review-title')).toHaveTextContent('Alpha Law LLP · Invoice INV-2025-78232');
    expect(screen.queryByTestId('set-firm')).not.toBeInTheDocument();
  });

  it('without a tracking number the sentence has no number clause; a pending re-run adds the unlock sentence', async () => {
    const api = mockApi([noFirm(), withFirm()]);
    api.listFirms.mockResolvedValue(ok([FIRM_A]));
    api.setFirm.mockResolvedValue(ok({ outcome: 'assigned', decisionId: 'd1', trackingNumber: null, previousTrackingNumber: null, rulesRerun: 'pending' }));
    renderPage(api);

    await chooseFirm(FIRM_A.name);
    fireEvent.click(setFirmButton());

    const notice = await screen.findByTestId('notice');
    expect(notice).toHaveTextContent('Firm set. Billing rules are being checked again; the page unlocks when that finishes.');
    expect(notice).not.toHaveTextContent('tracking number');
  });

  it('while the save is out the field and the button are disabled, and a second press sends nothing', async () => {
    const api = mockApi([noFirm(), withFirm()]);
    api.listFirms.mockResolvedValue(ok([FIRM_A]));
    let release!: (v: unknown) => void;
    api.setFirm.mockImplementation(() => new Promise(res => (release = res)));
    renderPage(api);

    await chooseFirm(FIRM_A.name);
    fireEvent.click(setFirmButton());
    await waitFor(() => expect(setFirmButton()).toBeDisabled());
    expect(screen.getByRole('combobox', { name: 'Billing firm' })).toBeDisabled();
    fireEvent.click(setFirmButton());
    expect(api.setFirm).toHaveBeenCalledTimes(1);

    await act(async () => {
      release(ok({ outcome: 'assigned', decisionId: 'd1', trackingNumber: 'T-1', previousTrackingNumber: null, rulesRerun: 'completed' }));
    });
    expect(await screen.findByTestId('notice')).toHaveTextContent('Firm set.');
  });

  it('a refusal shows the server detail beside the field (never the code), keeps the chosen firm, and 409 re-reads the review', async () => {
    const api = mockApi(noFirm());
    api.listFirms.mockResolvedValue(ok([FIRM_A, FIRM_B]));
    api.setFirm.mockResolvedValue(failed(problem(409, 'ebilling.assign_firm.busy', { detail: 'Another change to this invoice is in progress. Try again in a moment.' })));
    renderPage(api);

    await chooseFirm(FIRM_B.name);
    fireEvent.click(setFirmButton());

    const fault = await screen.findByTestId('set-firm-fault');
    expect(fault).toHaveTextContent('Another change to this invoice is in progress. Try again in a moment.');
    expect(fault).not.toHaveTextContent('ebilling.');
    expect(screen.queryByTestId('notice')).not.toBeInTheDocument();
    // Another user may have set it: the review was re-read (a second GET).
    await waitFor(() => expect(api.getReview).toHaveBeenCalledTimes(2));
    // The choice is kept and can be sent again.
    expect(screen.getByRole('combobox', { name: 'Billing firm' })).toHaveTextContent(FIRM_B.name);
    expect(setFirmButton()).toBeEnabled();

    api.setFirm.mockResolvedValue(ok({ outcome: 'assigned', decisionId: 'd', trackingNumber: 'B-1', previousTrackingNumber: null, rulesRerun: 'completed' }));
    fireEvent.click(setFirmButton());
    await waitFor(() => expect(api.setFirm).toHaveBeenCalledTimes(2));
    expect(api.setFirm).toHaveBeenLastCalledWith(INVOICE_ID, FIRM_B.firmId);
    await waitFor(() => expect(screen.queryByTestId('set-firm-fault')).not.toBeInTheDocument());
  });

  it('a 409 that finds the firm already set re-reads the review: the field is gone and no red bar is left beside the set firm', async () => {
    const api = mockApi([noFirm(), withFirm()]);
    api.listFirms.mockResolvedValue(ok([FIRM_B]));
    api.setFirm.mockResolvedValue(failed(problem(409, 'ebilling.assign_firm.firm_already_set', { detail: 'This invoice already has a firm.' })));
    renderPage(api);

    await chooseFirm(FIRM_B.name);
    fireEvent.click(setFirmButton());

    await waitFor(() => expect(api.getReview).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByTestId('set-firm')).not.toBeInTheDocument());
    expect(screen.getByTestId('review-title')).toHaveTextContent('Alpha Law LLP');
    expect(screen.queryByTestId('set-firm-fault')).not.toBeInTheDocument();
  });

  it('a 400 or 5xx without a detail falls back to the shared sentence; a network failure does not claim nothing changed, keeps the choice and re-reads the review', async () => {
    const api = mockApi(noFirm());
    api.listFirms.mockResolvedValue(ok([FIRM_A]));
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined); // toFailure logs the raw transport text for the console
    api.setFirm.mockResolvedValueOnce(failed(new TypeError('Failed to fetch')));
    warn.mockRestore();
    renderPage(api);

    await chooseFirm(FIRM_A.name);
    fireEvent.click(setFirmButton());

    expect(await screen.findByTestId('set-firm-fault')).toHaveTextContent('The firm could not be confirmed. Check the invoice header; if no firm is shown, try again.');
    expect(screen.getByRole('combobox', { name: 'Billing firm' })).toHaveTextContent(FIRM_A.name);
    // No answer: the firm may have landed, so the review is re-read (a firm that did land then shows).
    expect(screen.getByRole('combobox', { name: 'Billing firm' })).toHaveTextContent(FIRM_A.name);
    await waitFor(() => expect(api.getReview).toHaveBeenCalledTimes(2));
    expect(screen.queryByText('Nothing was changed', { exact: false })).not.toBeInTheDocument();

    api.setFirm.mockResolvedValueOnce(failed(problem(500, null)));
    fireEvent.click(setFirmButton());
    await waitFor(() => expect(screen.getByTestId('set-firm-fault')).toHaveTextContent('The change could not be made. Try again in a moment.'));
  });

  it('choosing another firm drops the last refusal', async () => {
    const api = mockApi(noFirm());
    api.listFirms.mockResolvedValue(ok([FIRM_A, FIRM_B]));
    api.setFirm.mockResolvedValue(failed(problem(400, 'ebilling.assign_firm.firm_required', { detail: 'A firm is required.' })));
    renderPage(api);
    await chooseFirm(FIRM_A.name);
    fireEvent.click(setFirmButton());
    expect(await screen.findByTestId('set-firm-fault')).toBeInTheDocument();
    await chooseFirm(FIRM_B.name);
    expect(screen.queryByTestId('set-firm-fault')).not.toBeInTheDocument();
  });

  it('must-fire: a 403 on Set firm is about the chosen firm - the detail shows in the field, the field and the choice stay, the page is NOT read-only, and another firm can be sent', async () => {
    const api = mockApi(noFirm());
    api.listFirms.mockResolvedValue(ok([FIRM_A, FIRM_B]));
    api.setFirm.mockResolvedValueOnce(failed(problem(403, null, { detail: 'You cannot set the firm.' })));
    renderPage(api);
    await chooseFirm(FIRM_A.name);
    fireEvent.click(setFirmButton());

    expect(await screen.findByTestId('set-firm-fault')).toHaveTextContent('You cannot set the firm.');
    expect(screen.queryByTestId('notice')).not.toBeInTheDocument();
    expect(screen.queryByTestId('readonly-notice')).not.toBeInTheDocument();
    expect(screen.getByTestId('set-firm')).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Billing firm' })).toHaveTextContent(FIRM_A.name);
    expect(screen.getByRole('combobox', { name: 'Billing firm' })).toBeEnabled();
    // The caller can still write: the correction button of the header is still offered (disabled only because the firm is needed).
    expect(screen.getByTestId('correct-header')).toBeInTheDocument();

    await chooseFirm(FIRM_B.name);
    expect(screen.queryByTestId('set-firm-fault')).not.toBeInTheDocument();
    fireEvent.click(setFirmButton());
    await waitFor(() => expect(api.setFirm).toHaveBeenCalledTimes(2));
    expect(api.setFirm).toHaveBeenLastCalledWith(INVOICE_ID, FIRM_B.firmId);
  });

  it('a 403 on Set firm without a detail says the firm is the problem, not the invoice', async () => {
    const api = mockApi(noFirm());
    api.listFirms.mockResolvedValue(ok([FIRM_A]));
    api.setFirm.mockResolvedValueOnce(failed(problem(403, null)));
    renderPage(api);
    await chooseFirm(FIRM_A.name);
    fireEvent.click(setFirmButton());
    expect(await screen.findByTestId('set-firm-fault')).toHaveTextContent('You do not have permission to use this firm. Choose another firm.');
  });

  it('must-not-fire: a 403 from another write still makes the page read-only', async () => {
    const api = mockApi(review(lines())); // firm present, no firm needed
    api.acceptClean.mockResolvedValue(failed(problem(403, null, { detail: 'No write access.' })));
    renderPage(api, { firmNeeded: false });
    fireEvent.click(await screen.findByTestId('accept-clean'));
    expect(await screen.findByTestId('notice')).toHaveTextContent('No write access.');
    expect(await screen.findByTestId('readonly-notice')).toBeInTheDocument();
    expect(screen.queryByTestId('accept-clean')).not.toBeInTheDocument();
  });

  it('an invoice with no core record says no firm can be listed, and asks for nothing', async () => {
    const base = noFirm();
    const api = mockApi({ ...base, header: { ...base.header, coreRecord: null } });
    renderPage(api);
    expect(await screen.findByTestId('set-firm-empty')).toHaveTextContent('This invoice has no matter, project or work assignment, so no firm can be listed.');
    expect(api.listFirms).not.toHaveBeenCalled();
  });
});

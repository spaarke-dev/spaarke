import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { InvoiceReviewPage } from '../components/InvoiceReviewPage';
import { LINE_FINDING_FALLBACK } from '../services/reviewModel';
import { INVOICE_ID, failed, field, finding, flagged, line, lineId, mockApi, ok, problem, review, type MockApi } from './testKit';

function renderPage(api: MockApi, opts: { theme?: typeof webLightTheme; pollIntervalMs?: number; openRecord?: jest.Mock } = {}) {
  return render(
    <FluentProvider theme={opts.theme ?? webLightTheme}>
      <InvoiceReviewPage invoiceId={INVOICE_ID} api={api as never} openRecord={opts.openRecord} pollIntervalMs={opts.pollIntervalMs ?? 60_000} />
    </FluentProvider>
  );
}

const normal = () =>
  review([
    line(1),
    flagged(2),
    line(3, { status: 'Pending', statusLabel: 'Processing' }),
    flagged(4),
    line(5, { status: 'Accepted', statusLabel: 'Accepted', approvedAmount: 1125, reviewedBy: { id: 'u', name: 'Reviewer One' }, reviewedOn: '2026-11-03T09:00:00Z' }),
  ]);

const row = (n: number) => screen.getByTestId(`line-row-${lineId(n)}`);

describe('InvoiceReviewPage: layout and states', () => {
  it('shows a spinner, then the header, totals, count filters and lines from the read model', async () => {
    const api = mockApi(normal());
    const openRecord = jest.fn();
    renderPage(api, { openRecord });
    expect(screen.getByTestId('review-loading')).toBeInTheDocument();

    expect(await screen.findByTestId('review-title')).toHaveTextContent('Example Firm LLP · Invoice INV-2025-78232');
    expect(screen.getByText('INV-2025-78232.3')).toBeInTheDocument();
    expect(screen.getByTestId('total-billed')).toHaveTextContent('5,000.00');
    expect(screen.getByTestId('total-approved')).toHaveTextContent('5,000.00');

    const filters = screen.getByRole('group', { name: 'Line filters' });
    expect(within(filters).getByRole('button', { name: /All lines, 5 lines/ })).toHaveAttribute('aria-pressed', 'true');
    expect(within(filters).getByRole('button', { name: /To decide, 2 lines/ })).toHaveAttribute('aria-pressed', 'false');
    expect(within(filters).getByRole('button', { name: /Processing, 1 line/ })).toBeInTheDocument();

    expect(api.getReview).toHaveBeenCalledWith(INVOICE_ID);
    expect(api.listCorrections).toHaveBeenCalledWith(INVOICE_ID);
    expect(screen.getAllByTestId(/^line-row-/)).toHaveLength(5);

    fireEvent.click(screen.getByTestId('core-link'));
    expect(openRecord).toHaveBeenCalledWith('sprk_matter', expect.any(String));
  });

  it('filters the grid by a count card and returns to All when it is chosen again', async () => {
    renderPage(mockApi(normal()));
    await screen.findByTestId('review-title');
    const filters = screen.getByRole('group', { name: 'Line filters' });
    fireEvent.click(within(filters).getByRole('button', { name: /To decide, 2 lines/ }));
    expect(screen.getAllByTestId(/^line-row-/)).toHaveLength(2);
    fireEvent.click(within(filters).getByRole('button', { name: /To decide, 2 lines/ }));
    expect(screen.getAllByTestId(/^line-row-/)).toHaveLength(5);
  });

  it('shows a Pending line as Processing with no tick and no decision', async () => {
    renderPage(mockApi(normal()));
    await screen.findByTestId('review-title');
    expect(screen.getByTestId(`processing-${lineId(3)}`)).toHaveTextContent('Processing');
    expect(within(row(3)).queryByRole('checkbox')).not.toBeInTheDocument();
    expect(within(row(2)).getByRole('checkbox')).toBeInTheDocument();

    fireEvent.click(row(3));
    const pane = await screen.findByTestId(`pane-line-${lineId(3)}`);
    expect(within(pane).getAllByText('Processing').length).toBeGreaterThan(0);
    expect(within(pane).queryByTestId('decision-confirm')).not.toBeInTheDocument();
    expect(within(pane).queryByTestId('correct-Hours')).not.toBeInTheDocument();
  });

  it('re-reads the review while a line is Processing and stops when none is', async () => {
    jest.useFakeTimers();
    try {
      const ready = review([line(1), flagged(2), line(3)]);
      const api = mockApi([normal(), ready]);
      renderPage(api, { pollIntervalMs: 1000 });
      await act(async () => {
        await Promise.resolve();
      });
      await screen.findByTestId('review-title');
      expect(api.getReview).toHaveBeenCalledTimes(1);

      await act(async () => {
        jest.advanceTimersByTime(1000);
      });
      await waitFor(() => expect(api.getReview).toHaveBeenCalledTimes(2));
      await waitFor(() => expect(screen.queryByTestId(`processing-${lineId(3)}`)).not.toBeInTheDocument());

      await act(async () => {
        jest.advanceTimersByTime(5000);
      });
      expect(api.getReview).toHaveBeenCalledTimes(2);
    } finally {
      jest.useRealTimers();
    }
  });

  it('shows the kill-switch notice when eBilling is disabled, with no retry', async () => {
    const api = mockApi(normal());
    api.getReview.mockResolvedValue(failed(problem(503, 'ebilling.disabled')));
    renderPage(api);
    const err = await screen.findByTestId('review-load-error');
    expect(err).toHaveTextContent('eBilling is not enabled in this environment.');
    expect(within(err).queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
  });

  it('is read-only for a viewer the server says cannot decide: no ticks, no decision, no Correct', async () => {
    const api = mockApi(review([flagged(1), line(2)], { flags: { canRead: true, canDecide: false } }));
    renderPage(api);
    await screen.findByTestId('review-title');
    expect(screen.getByTestId('readonly-notice')).toBeInTheDocument();
    expect(screen.queryAllByRole('checkbox')).toHaveLength(0);
    expect(screen.queryByTestId('accept-clean')).not.toBeInTheDocument();
    expect(screen.queryByTestId('correct-header')).not.toBeInTheDocument();
    fireEvent.click(row(1));
    const pane = await screen.findByTestId(`pane-line-${lineId(1)}`);
    expect(within(pane).queryByTestId('decision-confirm')).not.toBeInTheDocument();
    expect(within(pane).queryByTestId('correct-Hours')).not.toBeInTheDocument();
  });

  it('shows a decided invoice read-only with its status', async () => {
    renderPage(mockApi(review([line(1, { status: 'Accepted', statusLabel: 'Accepted' })], { header: { ...review([]).header, status: 'Approved' } })));
    await screen.findByTestId('review-title');
    expect(screen.getByTestId('readonly-notice')).toHaveTextContent('This invoice is approved; it is shown read-only.');
    expect(screen.getAllByText('Approved').length).toBeGreaterThan(0);
  });

  it('shows the decided state of a decided line through the StatusBar override', async () => {
    renderPage(mockApi(normal()));
    await screen.findByTestId('review-title');
    fireEvent.click(row(5));
    const pane = await screen.findByTestId(`pane-line-${lineId(5)}`);
    const bar = within(pane).getByTestId('status-bar');
    expect(bar).toHaveTextContent('Accepted');
    expect(bar).toHaveTextContent('by Reviewer One');
    expect(within(pane).queryByTestId('decision-confirm')).not.toBeInTheDocument();
  });

  it('shows each line finding as a reason (fixed text when its message is null) and counts it in the badge', async () => {
    const l = line(1, {
      status: 'Flagged',
      statusLabel: 'Flagged',
      findingCount: 0,
      lineFindings: [
        { code: 'coding.not_returned', message: 'The AI returned no codes.' },
        { code: 'line.no_description', message: null },
      ],
    });
    renderPage(mockApi(review([l])));
    await screen.findByTestId('review-title');
    expect(screen.getByTestId(`findings-${lineId(1)}`)).toHaveTextContent('2');
    fireEvent.click(row(1));
    const pane = await screen.findByTestId(`pane-line-${lineId(1)}`);
    const reasons = within(pane).getAllByTestId('line-finding').map(e => e.textContent);
    expect(reasons).toEqual(['The AI returned no codes.', LINE_FINDING_FALLBACK['line.no_description']]);
  });

  it('marks an unmatched timekeeper, a low-confidence value and an AI observation in the grid', async () => {
    const l = flagged(1, {
      timekeeper: null,
      timekeeperAsBilled: 'Unknown Person',
      fields: { ...line(1).fields, Hours: field({ lowConfidence: true, combined: 0.4 }) },
      findings: [finding({ valuesRead: [{ name: 'Block billing', value: 'true', field: 'Observation.BlockBilling', confidence: 0.8, belowReliabilityThreshold: false }] })],
    });
    renderPage(mockApi(review([l])));
    await screen.findByTestId('review-title');
    expect(screen.getByTestId(`unmatched-${lineId(1)}`)).toBeInTheDocument();
    expect(screen.getByTestId(`lowconf-${lineId(1)}-Hours`)).toBeInTheDocument();
    expect(screen.getByTestId(`aiobs-${lineId(1)}`)).toBeInTheDocument();
  });

  it('renders under the dark theme with the same rows as the light theme', async () => {
    // jsdom computes no colours, so what keeps the page theme-correct is the colour-literal source guard below; this checks that the page
    // mounts and renders its content under the dark provider.
    renderPage(mockApi(normal()), { theme: webDarkTheme });
    expect(await screen.findByTestId('invoice-review')).toBeInTheDocument();
    expect(screen.getAllByTestId(/^line-row-/)).toHaveLength(5);
  });
});

describe('InvoiceReviewPage: decisions (task 100)', () => {
  it('decides the first line to decide with the suggested Reduce, shows the server totals and moves to the next line', async () => {
    const after = review([line(1), flagged(2, { status: 'Reduced', statusLabel: 'Reduced', approvedAmount: 1012.5 }), line(3, { status: 'Pending' }), flagged(4), line(5, { status: 'Accepted' })], {
      totals: { billed: 5000, fees: 5000, expenses: 0, lineAdjustments: -112.5, invoiceAdjustments: 0, approved: 4887.5 },
    });
    const api = mockApi([normal(), after]);
    renderPage(api);
    await screen.findByTestId('review-title');

    fireEvent.click(screen.getByTestId('start-first'));
    await screen.findByTestId(`pane-line-${lineId(2)}`);
    // Suggestion first and preselected; nothing is sent until Confirm.
    expect(screen.getByRole('radio', { name: /Suggested: reduce to 1,012.50/ })).toBeChecked();
    expect(api.decideLine).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText(`Reason for line 2`), { target: { value: 'Above the approved rate' } });
    fireEvent.click(screen.getByTestId('decision-confirm'));

    await waitFor(() => expect(api.decideLine).toHaveBeenCalledTimes(1));
    expect(api.decideLine).toHaveBeenCalledWith(INVOICE_ID, lineId(2), {
      decisionId: expect.any(String),
      action: 'reduce',
      reason: 'Above the approved rate',
      approvedAmount: 1012.5,
    });
    await waitFor(() => expect(screen.getByTestId('total-approved')).toHaveTextContent('4,887.50'));
    expect(screen.getByTestId('total-line-adj')).toHaveTextContent('−112.50');
    expect(await screen.findByTestId(`pane-line-${lineId(4)}`)).toBeInTheDocument();
    expect(screen.getByTestId('notice')).toHaveTextContent('Line 2 reduced to 1,012.50');
  });

  it('shows the server refusal of a Reduce without a reason and changes nothing', async () => {
    const api = mockApi(normal());
    api.decideLine.mockResolvedValue(failed(problem(400, 'ebilling.line.reason_required', { detail: 'A reason is required to reduce a line.' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(2));
    await screen.findByTestId(`pane-line-${lineId(2)}`);
    fireEvent.click(screen.getByRole('radio', { name: 'Reduce…' }));
    fireEvent.change(screen.getByLabelText('Approved amount for line 2'), { target: { value: '900' } });
    fireEvent.click(screen.getByTestId('decision-confirm'));

    await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent('A reason is required to reduce a line.'));
    expect(api.decideLine.mock.calls[0][2]).toMatchObject({ action: 'reduce', reason: null, approvedAmount: 900 });
    expect(api.getReview).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('total-approved')).toHaveTextContent('5,000.00');
    expect(screen.getByTestId(`pane-line-${lineId(2)}`)).toBeInTheDocument();
  });

  it('turns the page read-only after a 403 on a write', async () => {
    const api = mockApi(normal());
    api.decideLine.mockResolvedValue(failed(problem(403, 'ebilling.decision.not_authorized', { detail: 'You cannot decide lines on this invoice.' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(2));
    await screen.findByTestId(`pane-line-${lineId(2)}`);
    fireEvent.click(screen.getByTestId('decision-confirm'));
    await waitFor(() => expect(screen.getByTestId('readonly-notice')).toBeInTheDocument());
    expect(screen.queryByTestId('decision-confirm')).not.toBeInTheDocument();
    expect(screen.queryAllByRole('checkbox')).toHaveLength(0);
  });

  it('offers Repeat after a 503 incomplete and repeats with the same decision id', async () => {
    const api = mockApi(normal());
    api.decideLine
      .mockResolvedValueOnce(failed(problem(503, 'ebilling.decision.incomplete', { detail: 'The line was saved; the record was not.', failedStep: 'record' })))
      .mockResolvedValueOnce(ok({ decisionId: 'x', lines: [], totals: null }));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(2));
    await screen.findByTestId(`pane-line-${lineId(2)}`);
    fireEvent.click(screen.getByTestId('decision-confirm'));
    fireEvent.click(await screen.findByTestId('notice-repeat'));
    await waitFor(() => expect(api.decideLine).toHaveBeenCalledTimes(2));
    expect(api.decideLine.mock.calls[1][2].decisionId).toBe(api.decideLine.mock.calls[0][2].decisionId);
  });

  it('accepts all clean lines in one request', async () => {
    const api = mockApi(normal());
    renderPage(api);
    await screen.findByTestId('review-title');
    expect(screen.getByTestId('accept-clean')).toHaveTextContent('Accept all clean lines (1)');
    fireEvent.click(screen.getByTestId('accept-clean'));
    await waitFor(() => expect(api.acceptClean).toHaveBeenCalledTimes(1));
    expect(api.acceptClean).toHaveBeenCalledWith(INVOICE_ID, expect.any(String));
    await waitFor(() => expect(api.getReview).toHaveBeenCalledTimes(2));
  });

  it('disallows ticked rows with one reason in one request', async () => {
    const api = mockApi(normal());
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(within(row(2)).getByRole('checkbox'));
    fireEvent.click(within(row(4)).getByRole('checkbox'));
    expect(screen.queryByTestId(`pane-line-${lineId(2)}`)).not.toBeInTheDocument();
    fireEvent.click(screen.getByTestId('bulk-disallow'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Duplicate entries' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Disallow 2 lines' }));
    await waitFor(() => expect(api.decideLines).toHaveBeenCalledTimes(1));
    expect(api.decideLines).toHaveBeenCalledWith(INVOICE_ID, {
      decisionId: expect.any(String),
      action: 'disallow',
      reason: 'Duplicate entries',
      lines: [
        { lineId: lineId(2), approvedAmount: null },
        { lineId: lineId(4), approvedAmount: null },
      ],
    });
  });

  it('applies a rule suggestion to its lines in one request with a per-line amount (By rule)', async () => {
    const api = mockApi(normal());
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByRole('tab', { name: 'By rule' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Apply suggestion to 2 lines…' }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Approved rate' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Reduce 2 lines' }));
    await waitFor(() => expect(api.decideLines).toHaveBeenCalledTimes(1));
    expect(api.decideLines.mock.calls[0][1]).toMatchObject({
      action: 'reduce',
      reason: 'Approved rate',
      lines: [
        { lineId: lineId(2), approvedAmount: 1012.5 },
        { lineId: lineId(4), approvedAmount: 1012.5 },
      ],
    });
  });

  it('closes the bulk dialog on a 503 incomplete and repeats the same request from the page', async () => {
    const api = mockApi(normal());
    api.decideLines
      .mockResolvedValueOnce(failed(problem(503, 'ebilling.decision.incomplete', { detail: 'The lines were saved; the totals were not.', failedStep: 'totals' })))
      .mockResolvedValueOnce(ok({ decisionId: 'x', lines: [], totals: null }));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(within(row(2)).getByRole('checkbox'));
    fireEvent.click(screen.getByTestId('bulk-accept'));
    const dialog = await screen.findByRole('dialog');
    expect(screen.getByText('Accept 1 selected line')).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Accept 1 line' }));
    // The change was saved: the form closes and the page offers Repeat (text query: it also sees content Fluent hides from roles).
    await waitFor(() => expect(screen.queryByText('Accept 1 selected line')).not.toBeInTheDocument());
    fireEvent.click(await screen.findByTestId('notice-repeat'));
    await waitFor(() => expect(api.decideLines).toHaveBeenCalledTimes(2));
    expect(api.decideLines.mock.calls[1][1]).toEqual(api.decideLines.mock.calls[0][1]);
  });

  it('offers Reload when the server says the line changed (409)', async () => {
    const api = mockApi(normal());
    api.decideLine.mockResolvedValue(failed(problem(409, 'ebilling.line.changed', { detail: 'The line changed. Reload and decide again.' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(2));
    await screen.findByTestId(`pane-line-${lineId(2)}`);
    fireEvent.click(screen.getByTestId('decision-confirm'));
    fireEvent.click(await screen.findByTestId('notice-reload'));
    await waitFor(() => expect(api.getReview).toHaveBeenCalledTimes(2));
  });

  it('keeps a bulk refusal in the dialog', async () => {
    const api = mockApi(normal());
    api.decideLines.mockResolvedValue(failed(problem(400, 'ebilling.line.reason_required', { detail: 'A reason is required to disallow.' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(within(row(2)).getByRole('checkbox'));
    fireEvent.click(screen.getByTestId('bulk-disallow'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Disallow 1 line' }));
    expect(await within(dialog).findByTestId('bulk-error')).toHaveTextContent('A reason is required to disallow.');
  });
});

describe('InvoiceReviewPage: failures and recovery (task 110 fix round 2)', () => {
  it('repeats a failed Accept all clean through the multi-line route with the committed ids and the same decision id', async () => {
    const api = mockApi(normal());
    api.acceptClean.mockResolvedValueOnce(
      failed(problem(503, 'ebilling.decision.incomplete', { detail: 'The lines were saved; the totals were not.', failedStep: 'totals', committedLineIds: [lineId(1)] }))
    );
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByTestId('accept-clean'));
    fireEvent.click(await screen.findByTestId('notice-repeat'));
    await waitFor(() => expect(api.decideLines).toHaveBeenCalledTimes(1));
    // Not accept-clean again (its lines are no longer Clean: the server would answer 200 and apply nothing).
    expect(api.acceptClean).toHaveBeenCalledTimes(1);
    expect(api.decideLines).toHaveBeenCalledWith(INVOICE_ID, {
      decisionId: api.acceptClean.mock.calls[0][1],
      action: 'accept',
      reason: null,
      lines: [{ lineId: lineId(1), approvedAmount: null }],
    });
  });

  it('shows the server state before offering Repeat after a 503 incomplete', async () => {
    const api = mockApi(normal());
    api.decideLine.mockResolvedValueOnce(failed(problem(503, 'ebilling.decision.incomplete', { detail: 'The line was saved; the record was not.' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(2));
    await screen.findByTestId(`pane-line-${lineId(2)}`);
    fireEvent.click(screen.getByTestId('decision-confirm'));
    await screen.findByTestId('notice-repeat');
    // The page re-read the review, so it no longer shows the pre-write state next to a Repeat.
    expect(api.getReview).toHaveBeenCalledTimes(2);
  });

  it('sends no reason for an Accept and offers no reason field for it', async () => {
    const api = mockApi(normal());
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(1));
    await screen.findByTestId(`pane-line-${lineId(1)}`);
    fireEvent.click(screen.getByRole('radio', { name: /Accept at/ }));
    expect(screen.queryByLabelText('Reason for line 1')).not.toBeInTheDocument();
    fireEvent.click(screen.getByTestId('decision-confirm'));
    await waitFor(() => expect(api.decideLine).toHaveBeenCalledTimes(1));
    expect(api.decideLine.mock.calls[0][2]).toEqual({ decisionId: expect.any(String), action: 'accept', reason: null, approvedAmount: null });
  });

  it('asks for no reason in the bulk Accept form and sends null', async () => {
    const api = mockApi(normal());
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(within(row(1)).getByRole('checkbox'));
    fireEvent.click(screen.getByTestId('bulk-accept'));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).queryByLabelText('Reason')).not.toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Accept 1 line' }));
    await waitFor(() => expect(api.decideLines).toHaveBeenCalledTimes(1));
    expect(api.decideLines.mock.calls[0][1]).toMatchObject({ action: 'accept', reason: null });
  });

  it('closes a dialog on a 409 and offers Reload on the page (bulk)', async () => {
    const api = mockApi(normal());
    api.decideLines.mockResolvedValue(failed(problem(409, 'ebilling.line.changed', { detail: 'A line changed. Reload and decide again.' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(within(row(2)).getByRole('checkbox'));
    fireEvent.click(screen.getByTestId('bulk-disallow'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Disallow 1 line' }));
    await waitFor(() => expect(screen.queryByText('Disallow 1 selected line')).not.toBeInTheDocument());
    expect(screen.getByTestId('notice')).toHaveTextContent('A line changed. Reload and decide again.');
    expect(screen.queryByTestId('bulk-error')).not.toBeInTheDocument();
    fireEvent.click(screen.getByTestId('notice-reload'));
    await waitFor(() => expect(api.getReview).toHaveBeenCalledTimes(2));
  });

  it('closes the Correct value form on a 409 and offers Reload on the page', async () => {
    const api = mockApi(normal());
    api.correctLine.mockResolvedValue(failed(problem(409, 'ebilling.correction.line_changed', { detail: 'The line changed. Reload and correct it again.' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(2));
    fireEvent.click(within(await screen.findByTestId(`pane-line-${lineId(2)}`)).getByTestId('correct-Hours'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Correct value'), { target: { value: '2' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction' }));
    await waitFor(() => expect(screen.queryByTestId('correct-error')).not.toBeInTheDocument());
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(await screen.findByTestId('notice-reload')).toBeInTheDocument();
  });

  it('keeps polling after one failed re-read, offers Reload, and clears the notice when a later re-read works', async () => {
    jest.useFakeTimers();
    try {
      const ready = review([line(1), flagged(2), line(3)]);
      const api = mockApi(normal());
      api.getReview
        .mockResolvedValueOnce(ok(normal()))
        .mockResolvedValueOnce(failed(problem(503, null)))
        .mockResolvedValue(ok(ready));
      renderPage(api, { pollIntervalMs: 1000 });
      await act(async () => {
        await Promise.resolve();
      });
      await screen.findByTestId('review-title');

      await act(async () => {
        jest.advanceTimersByTime(1000);
      });
      await waitFor(() => expect(api.getReview).toHaveBeenCalledTimes(2));
      // The re-read failed: what is shown stays, and the notice offers Reload.
      expect(await screen.findByTestId('notice-reload')).toBeInTheDocument();
      expect(screen.getByTestId('notice')).toHaveTextContent('temporarily unavailable');
      expect(screen.getByTestId(`processing-${lineId(3)}`)).toBeInTheDocument();

      // Polling was not abandoned: the next tick re-reads and the page recovers by itself.
      await act(async () => {
        jest.advanceTimersByTime(1000);
      });
      await waitFor(() => expect(api.getReview).toHaveBeenCalledTimes(3));
      await waitFor(() => expect(screen.queryByTestId(`processing-${lineId(3)}`)).not.toBeInTheDocument());
      expect(screen.queryByTestId('notice')).not.toBeInTheDocument();
    } finally {
      jest.useRealTimers();
    }
  });

  it('says so when a write was saved but the page could not refresh, with Reload', async () => {
    const api = mockApi(normal());
    renderPage(api);
    await screen.findByTestId('review-title');
    api.getReview.mockResolvedValue(failed(problem(503, null)));
    fireEvent.click(row(1));
    await screen.findByTestId(`pane-line-${lineId(1)}`);
    fireEvent.click(screen.getByRole('radio', { name: /Accept at/ }));
    fireEvent.click(screen.getByTestId('decision-confirm'));
    await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent('could not refresh'));
    expect(screen.getByTestId('notice')).toHaveAttribute('data-intent', 'warning');
    expect(screen.getByTestId('notice-reload')).toBeInTheDocument();
  });

  it('shows the connection sentence, never the browser text, when a write gets no server answer', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      const api = mockApi(normal());
      api.decideLine.mockResolvedValue(failed(new TypeError('Failed to fetch')));
      renderPage(api);
      await screen.findByTestId('review-title');
      fireEvent.click(row(2));
      await screen.findByTestId(`pane-line-${lineId(2)}`);
      fireEvent.click(screen.getByTestId('decision-confirm'));
      await waitFor(() => expect(screen.getByTestId('notice')).toHaveTextContent('The change could not be sent. Check your connection and retry.'));
      expect(screen.getByTestId('notice')).not.toHaveTextContent('Failed to fetch');
    } finally {
      warn.mockRestore();
    }
  });
});

describe('InvoiceReviewPage: corrections (task 101) and the PDF', () => {
  const lowHours = () =>
    review([
      flagged(7, {
        quantity: 25,
        fields: { ...line(7).fields, Hours: field({ lowConfidence: true, combined: 0.42, box: { page: 1, x: 0.6, y: 0.5, w: 0.05, h: 0.02 } }) },
        findings: [finding({ policyCode: 'HOURS-DAY', valuesRead: [{ name: 'Hours', value: '25', field: 'Hours', confidence: 0.42, belowReliabilityThreshold: true }] })],
      }),
    ]);

  it('boxes the clicked value on the PDF', async () => {
    const api = mockApi(lowHours());
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByRole('button', { name: /Hours, line 7, check this value/ }));
    await screen.findByTestId(`pane-line-${lineId(7)}`);
    const viewer = screen.getByTestId('pdf-viewer');
    const h = JSON.parse(viewer.getAttribute('data-highlight')!);
    expect(h.page).toBe(1);
    expect(h.field).toEqual({ x: 0.6, y: 0.5, w: 0.05, h: 0.02 });
    expect(h.label).toBe('Line 7 · Hours');
    await waitFor(() => expect(api.fetchFile).toHaveBeenCalled());
  });

  it('corrects a misread value through the correction route, never a decision', async () => {
    const corrected = review([line(7, { quantity: 2.5 })]);
    const api = mockApi([lowHours(), corrected]);
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(7));
    const pane = await screen.findByTestId(`pane-line-${lineId(7)}`);
    fireEvent.click(within(pane).getByText(/Compare with the document, then confirm or correct/));
    expect(JSON.parse(screen.getByTestId('pdf-viewer').getAttribute('data-highlight')!).label).toBe('Line 7 · Hours');

    fireEvent.click(within(pane).getByTestId('correct-Hours'));
    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent('As read: 25');
    expect(screen.getByText('Correct hours · line 7')).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText('Correct value'), { target: { value: '2.5' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction' }));

    await waitFor(() => expect(api.correctLine).toHaveBeenCalledWith(INVOICE_ID, lineId(7), { field: 'Hours', value: '2.5', confirm: false }));
    expect(api.decideLine).not.toHaveBeenCalled();
    expect(api.decideLines).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.queryByText('Correct hours · line 7')).not.toBeInTheDocument());
    // The line is Clean after the server re-ran the rules.
    expect(within(row(7)).getByText('Clean')).toBeInTheDocument();
    // The "Suggested: reduce" choice made against the old finding did not survive: nothing is chosen, so Confirm cannot send it.
    const confirm = await screen.findByTestId('decision-confirm');
    expect(confirm).toBeDisabled();
    expect(screen.queryByRole('radio', { name: /Suggested/ })).not.toBeInTheDocument();
    fireEvent.click(confirm);
    expect(api.decideLine).not.toHaveBeenCalled();
  });

  it('confirms a low-confidence value as read', async () => {
    const api = mockApi(lowHours());
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(7));
    const pane = await screen.findByTestId(`pane-line-${lineId(7)}`);
    fireEvent.click(within(pane).getByTestId('confirm-Hours'));
    await waitFor(() => expect(api.correctLine).toHaveBeenCalledWith(INVOICE_ID, lineId(7), { field: 'Hours', value: null, confirm: true }));
  });

  it('keeps a refused correction in the dialog with the server message', async () => {
    const api = mockApi(lowHours());
    api.correctLine.mockResolvedValue(failed(problem(400, 'ebilling.correction.value_invalid', { detail: 'Hours must be a number.' })));
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(row(7));
    fireEvent.click(within(await screen.findByTestId(`pane-line-${lineId(7)}`)).getByTestId('correct-Hours'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Correct value'), { target: { value: 'x' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction' }));
    expect(await within(dialog).findByTestId('correct-error')).toHaveTextContent('Hours must be a number.');
  });

  it('corrects a header value through the header route', async () => {
    const api = mockApi(normal());
    renderPage(api);
    await screen.findByTestId('review-title');
    fireEvent.click(screen.getByTestId('correct-header'));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Correct value'), { target: { value: 'INV-2025-78233' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save correction' }));
    await waitFor(() =>
      expect(api.correctHeader).toHaveBeenCalledWith(INVOICE_ID, { field: 'FirmInvoiceNumber', value: 'INV-2025-78233', confirm: false })
    );
  });

  it('says so when the invoice has no PDF', async () => {
    renderPage(mockApi(review([line(1)], { invoicePdfFileId: null })));
    expect(await screen.findByTestId('no-pdf')).toBeInTheDocument();
  });
});

/** CSS named colours a style value might use. */
const NAMED_COLOURS = new Set([
  'white', 'black', 'red', 'green', 'blue', 'yellow', 'orange', 'purple', 'pink', 'brown', 'gray', 'grey', 'silver', 'gold', 'navy', 'teal',
  'maroon', 'olive', 'lime', 'aqua', 'cyan', 'magenta', 'fuchsia', 'ivory', 'beige', 'tan', 'khaki', 'coral', 'salmon', 'crimson', 'indigo',
  'violet', 'lavender', 'turquoise', 'azure', 'whitesmoke', 'gainsboro', 'lightgray', 'lightgrey', 'darkgray', 'darkgrey', 'dimgray',
  'slategray', 'darkred', 'darkgreen', 'darkblue', 'lightblue', 'lightgreen', 'skyblue', 'royalblue', 'steelblue', 'orangered', 'tomato',
  'firebrick',
]);

/**
 * Colour literals in source text: hex colours, rgb()/rgba()/hsl()/hsla(), and CSS named colours used as a style value. Comments are ignored. A
 * named colour counts only inside the quoted value of a style property (`color: 'white'`, `border: '1px solid red'`), so prose and ids do not
 * trip it. Fluent tokens, `transparent`, `inherit` and `currentColor` are not literals.
 */
function findColourLiterals(source: string): string[] {
  const text = source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:'"`])\/\/.*$/gm, '$1');
  const found: string[] = [];
  for (const m of text.matchAll(/(^|[^\w&#])(#[0-9a-fA-F]{3,8})(?![\w-])/g)) found.push(m[2]);
  for (const m of text.matchAll(/\b(rgba?|hsla?)\(/g)) found.push(`${m[1]}(`);
  const prop =
    /\b(?:color|backgroundColor|background|border\w*|outline\w*|fill|stroke|boxShadow|textDecorationColor|caretColor|accentColor)\s*:\s*(['"`])([^'"`]*)\1/g;
  for (const m of text.matchAll(prop)) {
    for (const word of m[2].split(/[^A-Za-z]+/)) if (NAMED_COLOURS.has(word.toLowerCase())) found.push(word);
  }
  return found;
}

describe('source guards', () => {
  it('uses no colour literal anywhere in the page source (dark mode: Fluent tokens only, ADR-021)', () => {
    const srcRoot = path.resolve(__dirname, '..');
    const walk = (dir: string): string[] =>
      fs.readdirSync(dir, { withFileTypes: true }).flatMap(e => {
        const p = path.join(dir, e.name);
        if (e.isDirectory()) return e.name === '__tests__' ? [] : walk(p);
        return /\.(ts|tsx)$/.test(e.name) ? [p] : [];
      });
    for (const f of walk(srcRoot)) {
      const found = findColourLiterals(fs.readFileSync(f, 'utf8'));
      expect({ file: path.relative(srcRoot, f), found }).toEqual({ file: path.relative(srcRoot, f), found: [] });
    }
  });

  it('the colour guard fires on hex, rgb() / hsl() and named colours in style values', () => {
    expect(findColourLiterals("const a = { color: '#fff' };")).toEqual(['#fff']);
    expect(findColourLiterals("const a = { border: '1px solid #CCCCCC80' };")).toEqual(['#CCCCCC80']);
    expect(findColourLiterals("const a = { backgroundColor: 'rgb(0, 0, 0)' };")).toEqual(['rgb(']);
    expect(findColourLiterals('const a = { fill: `hsla(0,0%,0%,.5)` };')).toEqual(['hsla(']);
    expect(findColourLiterals("const a = { backgroundColor: 'white' };")).toEqual(['white']);
    expect(findColourLiterals('const a = { color: "Black", borderTopColor: "red" };')).toEqual(['Black', 'red']);
    expect(findColourLiterals('<div style={{ background: "navy" }} />')).toEqual(['navy']);
  });

  it('the colour guard stays quiet on tokens, transparent, comments, ids and non-colour words', () => {
    expect(findColourLiterals('const a = { color: tokens.colorNeutralForeground1, backgroundColor: tokens.colorNeutralBackground2 };')).toEqual([]);
    expect(findColourLiterals("const a = { backgroundColor: 'transparent', color: 'inherit', borderColor: 'currentColor' };")).toEqual([]);
    expect(findColourLiterals("// a #fff comment and 'white' in prose\n/* rgb(0,0,0) */ const a = 1;")).toEqual([]);
    expect(findColourLiterals("document.getElementById('root'); const href = '#root'; const t = 'Black Friday sale';")).toEqual([]);
    expect(findColourLiterals("const a = { overflow: 'hidden', display: 'flex', position: 'relative' };")).toEqual([]);
  });

  const srcDir = path.resolve(__dirname, '..');
  const files = (dir: string): string[] =>
    fs.readdirSync(dir, { withFileTypes: true }).flatMap(e => {
      const p = path.join(dir, e.name);
      if (e.isDirectory()) return e.name === '__tests__' ? [] : files(p);
      return /\.(ts|tsx)$/.test(e.name) ? [p] : [];
    });

  it('imports nothing from the prototype repository and never writes Dataverse directly', () => {
    for (const f of files(srcDir)) {
      const text = fs.readFileSync(f, 'utf8');
      expect(text).not.toMatch(/spaarke-prototype|ebilling\.r1/);
      expect(text).not.toMatch(/Xrm\.WebApi|createRecord\(|updateRecord\(/);
    }
  });

  it('registers the Navigator side pane on mount', () => {
    const main = fs.readFileSync(path.join(srcDir, 'main.tsx'), 'utf8');
    expect(main).toMatch(/React\.useEffect\(\(\) => \{\s*ensureNavigatorSidePane\(\);\s*\}, \[\]\);/);
  });
});

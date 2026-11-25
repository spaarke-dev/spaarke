import '@testing-library/jest-dom';
import { act, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { InvoiceReviewWithDecisions } from '../components/InvoiceReviewWithDecisions';
import { INVOICE_ID, approval, failed, flagged, line, mockApi, mockDecisionApi, ok, problem, review } from './testKit';

/**
 * ISS-014: while the invoice's approval state says `firmNeeded`, the page re-reads that state at the poll interval, so a firm set elsewhere
 * (another tab or user, the From-email firm step) opens the line actions without a reload. These run the whole page (provider + page).
 */

const INTERVAL = 1000;
const FIRM_NEEDED = () => approval({ firmNeeded: true, canApprove: false });
const clean = () => review([line(1), flagged(2)]);

/** Let every pending promise settle (the reads are all mocked promises, so no timer is involved). */
async function flush() {
  await act(async () => {
    for (let i = 0; i < 25; i++) await Promise.resolve();
  });
}

async function tick(ms = INTERVAL) {
  await act(async () => {
    jest.advanceTimersByTime(ms);
  });
  await flush();
}

async function renderPage(decision: ReturnType<typeof mockDecisionApi>) {
  const api = mockApi(clean());
  const view = render(
    <FluentProvider theme={webLightTheme}>
      <InvoiceReviewWithDecisions invoiceId={INVOICE_ID} api={api as never} decisionApi={decision as never} pollIntervalMs={INTERVAL} />
    </FluentProvider>
  );
  await flush();
  expect(screen.getByTestId('review-title')).toBeInTheDocument();
  return { api, view };
}

describe('InvoiceReview: the firm gate poll (ISS-014)', () => {
  beforeEach(() => jest.useFakeTimers());
  afterEach(() => jest.useRealTimers());

  it('(a) while firmNeeded, re-reads the approval state on the poll interval and keeps the actions disabled', async () => {
    const decision = mockDecisionApi();
    decision.getApproval.mockImplementation(async () => ok(FIRM_NEEDED()));
    const { api } = await renderPage(decision);
    expect(screen.getByTestId('firm-first-notice')).toBeInTheDocument();
    expect(screen.getByTestId('correct-header')).toBeDisabled();
    const afterLoad = decision.getApproval.mock.calls.length;
    expect(afterLoad).toBe(1);

    await tick(INTERVAL - 1);
    expect(decision.getApproval).toHaveBeenCalledTimes(afterLoad);
    await tick(1);
    expect(decision.getApproval).toHaveBeenCalledTimes(afterLoad + 1);
    await tick();
    expect(decision.getApproval).toHaveBeenCalledTimes(afterLoad + 2);

    // Still firmNeeded: the review is not re-read by the poll, the actions stay disabled.
    expect(api.getReview).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('correct-header')).toBeDisabled();
    expect(screen.getByTestId('add-line')).toBeDisabled();
  });

  it('(b) when the re-read says the firm is no longer needed, the poll stops, the review is re-read once and the actions enable', async () => {
    const decision = mockDecisionApi();
    decision.getApproval.mockResolvedValueOnce(ok(FIRM_NEEDED())).mockResolvedValueOnce(ok(FIRM_NEEDED())).mockResolvedValue(ok(approval()));
    const { api } = await renderPage(decision);
    expect(screen.getByTestId('correct-header')).toBeDisabled();

    await tick(); // second read: still needed
    expect(decision.getApproval).toHaveBeenCalledTimes(2);
    expect(api.getReview).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('correct-header')).toBeDisabled();

    await tick(); // third read: the firm was set elsewhere
    expect(decision.getApproval).toHaveBeenCalledTimes(3);
    expect(api.getReview).toHaveBeenCalledTimes(2);
    expect(screen.queryByTestId('firm-first-notice')).not.toBeInTheDocument();
    expect(screen.getByTestId('correct-header')).toBeEnabled();
    expect(screen.getByTestId('add-line')).toBeEnabled();
    expect(screen.getByTestId('accept-clean')).toBeEnabled();

    // Stopped: no further approval or review reads, however long the page stays open.
    await tick(INTERVAL * 5);
    expect(decision.getApproval).toHaveBeenCalledTimes(3);
    expect(api.getReview).toHaveBeenCalledTimes(2);
  });

  it('(c) does not poll when the invoice does not need its firm', async () => {
    const decision = mockDecisionApi(approval());
    const { api } = await renderPage(decision);
    expect(screen.queryByTestId('firm-first-notice')).not.toBeInTheDocument();
    expect(decision.getApproval).toHaveBeenCalledTimes(1);

    await tick(INTERVAL * 5);
    expect(decision.getApproval).toHaveBeenCalledTimes(1);
    expect(api.getReview).toHaveBeenCalledTimes(1);
  });

  it('(d) stops polling when the page unmounts', async () => {
    const decision = mockDecisionApi();
    decision.getApproval.mockImplementation(async () => ok(FIRM_NEEDED()));
    const { view } = await renderPage(decision);
    await tick();
    expect(decision.getApproval).toHaveBeenCalledTimes(2);

    view.unmount();
    await tick(INTERVAL * 5);
    expect(decision.getApproval).toHaveBeenCalledTimes(2);
  });

  it('never starts a read while the previous one is still out', async () => {
    const decision = mockDecisionApi();
    let release: (() => void) | undefined;
    decision.getApproval.mockImplementation(async () => ok(FIRM_NEEDED()));
    decision.getApproval.mockResolvedValueOnce(ok(FIRM_NEEDED())).mockImplementationOnce(
      () =>
        new Promise(resolve => {
          release = () => resolve(ok(FIRM_NEEDED()));
        })
    );
    await renderPage(decision);

    await tick(); // read 2 starts and hangs
    expect(decision.getApproval).toHaveBeenCalledTimes(2);
    await tick(INTERVAL * 3); // three more intervals while it is out
    expect(decision.getApproval).toHaveBeenCalledTimes(2);

    await act(async () => release?.());
    await flush();
    await tick();
    expect(decision.getApproval).toHaveBeenCalledTimes(3);
  });

  it('a failed re-read keeps polling and shows nothing for it; the next good answer opens the gate', async () => {
    const decision = mockDecisionApi();
    decision.getApproval
      .mockResolvedValueOnce(ok(FIRM_NEEDED()))
      .mockResolvedValueOnce(failed(problem(503, null)))
      .mockResolvedValueOnce(failed(problem(503, null)))
      .mockResolvedValue(ok(approval()));
    const { api } = await renderPage(decision);

    await tick();
    await tick();
    expect(decision.getApproval).toHaveBeenCalledTimes(3);
    expect(screen.queryByTestId('notice')).not.toBeInTheDocument();
    expect(screen.queryByTestId('awaiting-retry')).not.toBeInTheDocument();
    expect(screen.getByTestId('correct-header')).toBeDisabled();
    expect(api.getReview).toHaveBeenCalledTimes(1);

    await tick();
    expect(decision.getApproval).toHaveBeenCalledTimes(4);
    expect(api.getReview).toHaveBeenCalledTimes(2);
    expect(screen.getByTestId('correct-header')).toBeEnabled();
  });
});

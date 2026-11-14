/**
 * The Decide lane after the wizard (task 032): the four fixes the task 031 reviews handed over, and the lane read again in place.
 *
 *  (a) `reloadToken`: a change reads the lane again WITHOUT the loading state, so a recorded decision leaves the lane and the count drops
 *      by one while the rows, the filter, the notice and the review under way stay where they were
 *  (b) a billing link still opening when the `service` changes does not put its failure notice on the new lane
 *  (c) the same notice twice is announced twice (a new alert, not the same one left standing)
 *  (d) a new `service` is a new read, so the card filter goes back to All
 * The real code runs from the host down (the lane, the shared panel, the shared worklist service, the shim); the double is the host.
 */
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { INVOICE_REVIEW_FAILED } from '@spaarke/ui-components/components/Worklist/openInvoiceReview';
import {
  createWorklistService,
  type WorklistData,
  type WorklistService,
} from '@spaarke/ui-components/components/Worklist/worklistService';
import { DecideLane, type DecideLaneProps } from '../lane/DecideLane';
import { createDecideLaneFetch } from '../lane/decideLaneFetch';
import type { CallServerTool } from '../mcp/mcpFetch';
import {
  CONSOLE_BASE,
  INVOICE,
  LINE,
  MATTERS,
  guid,
  installResponseStandIn,
  pagingHost,
  row,
  toolError,
  type SlimRow,
} from './laneFixtures';

installResponseStandIn();

const TODAY = new Date(2026, 9, 9, 10, 30);
const LANE_OPEN_KEY = 'spaarke.worklist.laneOpen';
const A = MATTERS.a.id;

beforeEach(() => {
  window.localStorage.clear();
  window.localStorage.setItem(LANE_OPEN_KEY, JSON.stringify({ Decide: true }));
});

const NOTHING_TO_REVIEW = 'Nothing in this view can be reviewed here. Invoice decisions are decided in Invoice Review.';

/** Items 1-2 ask outside counsel, 3 is a billing item, 4-5 chase a reply. */
const fiveItems = (): SlimRow[] => [
  row(1, { core: A, workType: 'askOutsideCounsel' }),
  row(2, { core: A, workType: 'askOutsideCounsel' }),
  row(3, { core: A, workType: 'approveOrRebudget', invoiceReview: { invoiceId: INVOICE, lineId: LINE } }),
  row(4, { core: A, workType: 'chaseReply' }),
  row(5, { core: A, workType: 'chaseReply' }),
];

function mountLane(rows: SlimRow[], props: Partial<DecideLaneProps> = {}) {
  const host = pagingHost(rows);
  // The host answers until a test says the next reads fail.
  let failing = false;
  const callTool: CallServerTool = async (name, args) =>
    failing ? toolError('x', 'The decisions could not be read.', 500) : host.callTool(name, args);
  const newService = (): WorklistService =>
    createWorklistService({ fetch: createDecideLaneFetch(callTool), baseUrl: '' });
  const service = newService();
  const onData = jest.fn<void, [WorklistData | null]>();
  const openLink = jest.fn<Promise<boolean>, [string]>(async () => true);
  const ui = (s: WorklistService, extra: Partial<DecideLaneProps> = {}) => (
    <FluentProvider theme={webLightTheme}>
      <DecideLane
        service={s}
        consoleBaseUrl={CONSOLE_BASE}
        openLink={openLink}
        onData={onData}
        today={TODAY}
        {...props}
        {...extra}
      />
    </FluentProvider>
  );
  const view = render(ui(service));
  return {
    host,
    service,
    newService,
    onData,
    openLink,
    ui,
    view,
    failReads: () => {
      failing = true;
    },
  };
}

const lane = () => screen.getByTestId('worklist-lane-Decide');
const ready = () => screen.findByTestId('decide-lane');
const lineIds = () =>
  within(lane())
    .getAllByTestId('issue-line')
    .map(l => l.getAttribute('data-signal-id'));
const count = () => screen.getByTestId('lane-count-Decide');
const card = (label: string) => within(lane()).getByRole('button', { name: new RegExp(`^${label},`) });

describe('(a) reloadToken: the lane is read again in place', () => {
  it('a change reads the lane again with no loading state; the recorded row leaves and the count drops by one', async () => {
    const rows = fiveItems();
    const { host, service, ui, view, onData } = mountLane(rows);
    await ready();
    expect(count()).toHaveTextContent('5');
    const readsBefore = host.calls.length;

    rows.splice(1, 1); // item 2 was recorded: the server no longer lists it
    view.rerender(ui(service, { reloadToken: 1 }));

    // At once: the lane is still on screen, with the old rows, while the read is in flight.
    expect(screen.queryByTestId('decide-lane-loading')).toBeNull();
    expect(lineIds()).toHaveLength(4); // the 5th row is the billing item: a Finding row, not an issue line

    await waitFor(() => expect(count()).toHaveTextContent('4'));
    expect(lineIds()).not.toContain(guid('5', 2));
    expect(host.calls.length).toBeGreaterThan(readsBefore);
    // The wizard is told the lane it can open on, and was not told "no lane" during an in-place read.
    expect(onData.mock.calls.map(c => c[0] === null)).toEqual([true, false, false]);
    const last = onData.mock.calls[onData.mock.calls.length - 1][0];
    expect(last?.Decide.flatMap(g => g.items.map(i => i.signalId))).not.toContain(guid('5', 2));
  });

  it('no change of the token reads nothing again', async () => {
    const { host, service, ui, view } = mountLane(fiveItems());
    await ready();
    const reads = host.calls.length;
    view.rerender(ui(service, { reloadToken: 0 }));
    view.rerender(ui(service));
    await act(async () => undefined);
    expect(host.calls.length).toBe(reads);
  });

  it('keeps the card filter and a notice that was showing', async () => {
    const rows = fiveItems();
    const { service, ui, view } = mountLane(rows);
    await ready();

    fireEvent.click(card('Invoice decisions'));
    fireEvent.click(screen.getByTestId('lane-start-Decide'));
    expect(await screen.findByTestId('decide-lane-notice')).toHaveTextContent(NOTHING_TO_REVIEW);

    rows.splice(3, 1); // item 4 (not in this view) is recorded
    view.rerender(ui(service, { reloadToken: 1 }));
    await waitFor(() => expect(screen.getByTestId('lane-count-Decide')).toHaveTextContent('4'));

    expect(card('Invoice decisions')).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByTestId('decide-lane-notice')).toHaveTextContent(NOTHING_TO_REVIEW);
  });

  it('a review under way keeps its place among the items still in the lane', async () => {
    const rows = fiveItems();
    const { service, ui, view } = mountLane(rows);
    await ready();

    fireEvent.click(screen.getByTestId('lane-start-Decide'));
    expect(screen.getByTestId('decide-review-position')).toHaveTextContent('1 of 4');
    fireEvent.click(screen.getByTestId('decide-review-next'));
    expect(screen.getByTestId('decide-review-position')).toHaveTextContent('2 of 4');

    rows.splice(1, 1); // the item being reviewed (item 2) is recorded
    view.rerender(ui(service, { reloadToken: 1 }));

    await waitFor(() => expect(screen.getByTestId('decide-review-position')).toHaveTextContent('2 of 3'));
    expect(lineIds()).not.toContain(guid('5', 2));
  });

  it('the review ends when nothing it was walking is left', async () => {
    const rows = [
      row(1, { core: A, workType: 'chaseReply' }),
      row(2, { core: A, workType: 'approveOrRebudget', invoiceReview: { invoiceId: INVOICE } }),
    ];
    const { service, ui, view } = mountLane(rows);
    await ready();
    fireEvent.click(screen.getByTestId('lane-start-Decide'));
    expect(screen.getByTestId('decide-review-position')).toHaveTextContent('1 of 1');

    rows.splice(0, 1);
    view.rerender(ui(service, { reloadToken: 1 }));
    await waitFor(() => expect(screen.getByTestId('lane-count-Decide')).toHaveTextContent('1'));
    expect(screen.queryByTestId('decide-review-bar')).toBeNull();
  });

  it('a read that fails after a decision is shown as the lane error with Try again, not as a stale lane', async () => {
    const { service, ui, view, onData, failReads } = mountLane(fiveItems());
    await ready();
    onData.mockClear();

    failReads();
    view.rerender(ui(service, { reloadToken: 1 }));

    expect(await screen.findByTestId('decide-lane-error')).toHaveTextContent('The decisions could not be read.');
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
    // The wizard is told there is no lane to open on.
    expect(onData).toHaveBeenLastCalledWith(null);
  });

  it('a new service is a new read (the loading state), not an in-place one', async () => {
    const { newService, ui, view, onData } = mountLane(fiveItems());
    await ready();
    onData.mockClear();
    view.rerender(ui(newService(), { reloadToken: 1 }));
    expect(screen.getByTestId('decide-lane-loading')).toBeInTheDocument();
    expect(onData).toHaveBeenCalledWith(null);
    await ready();
  });
});

describe('(b) a billing link still opening when the service changes', () => {
  /** An `openLink` that answers when the test says so. */
  function deferredLink() {
    let settle!: (opened: boolean) => void;
    const openLink = jest.fn(() => new Promise<boolean>(resolve => (settle = resolve)));
    return { openLink, settle: (opened: boolean) => act(async () => settle(opened)) };
  }

  it('control: the failure notice is shown on the lane the link was clicked in', async () => {
    const link = deferredLink();
    mountLane(fiveItems(), { openLink: link.openLink });
    await ready();
    fireEvent.click(screen.getByTestId('billing-finding-link'));
    await link.settle(false);
    expect(await screen.findByTestId('decide-lane-notice')).toHaveTextContent(INVOICE_REVIEW_FAILED);
  });

  it('a late failure after the service changed puts no notice on the new lane', async () => {
    const link = deferredLink();
    const { newService, ui, view } = mountLane(fiveItems(), { openLink: link.openLink });
    await ready();
    fireEvent.click(screen.getByTestId('billing-finding-link'));

    view.rerender(ui(newService(), { openLink: link.openLink }));
    await ready();
    await link.settle(false);

    expect(screen.queryByTestId('decide-lane-notice')).toBeNull();
  });

  it('a link that opened has no notice either way', async () => {
    const link = deferredLink();
    mountLane(fiveItems(), { openLink: link.openLink });
    await ready();
    fireEvent.click(screen.getByTestId('billing-finding-link'));
    await link.settle(true);
    expect(screen.queryByTestId('decide-lane-notice')).toBeNull();
  });
});

describe('(c) the same notice twice is announced twice', () => {
  it('starting a review with nothing to review, again, shows a new alert rather than the one already standing', async () => {
    mountLane(fiveItems());
    await ready();
    fireEvent.click(card('Invoice decisions'));

    fireEvent.click(screen.getByTestId('lane-start-Decide'));
    const first = await screen.findByTestId('decide-lane-notice');
    expect(first).toHaveTextContent(NOTHING_TO_REVIEW);

    fireEvent.click(screen.getByTestId('lane-start-Decide'));
    const second = await screen.findByTestId('decide-lane-notice');
    expect(second).toHaveTextContent(NOTHING_TO_REVIEW);
    expect(second).not.toBe(first); // a new element: the live region announces it again
    expect(first.isConnected).toBe(false);
  });
});

describe('(d) a new service is a new read: the card filter goes back to All', () => {
  it('a filter chosen on the old lane does not narrow the new one', async () => {
    const { newService, ui, view } = mountLane(fiveItems());
    await ready();
    fireEvent.click(card('Ask outside counsel'));
    expect(lineIds()).toEqual([guid('5', 1), guid('5', 2)]);
    expect(card('Ask outside counsel')).toHaveAttribute('aria-pressed', 'true');

    view.rerender(ui(newService()));
    await ready();

    expect(card('All')).toHaveAttribute('aria-pressed', 'true');
    expect(lineIds()).toHaveLength(4); // every non-billing item; the billing item is a Finding row
  });
});

/**
 * The Decide lane in the widget (task 031; FR-05, FR-12; EB-D-194, D-208). The real code runs from the host down: `DecideLane` ->
 * the shared `WorklistLanePanel` / `buildLaneView` -> `createWorklistService` -> the paged fetch -> the MCP shim. The only doubles are
 * the HOST (`callServerTool`) and its `openLink`; `onOpenWizard` is the event task 032 consumes.
 *
 * Stated contract (acceptance criteria):
 *  - header 'Decide', the count and 'Items that need your decision'; category cards with count and oldest age (a click filters rows);
 *    rows grouped by matter with reason and age; counts equal `spaarke_show_decisions`' per-category counts for the same data
 *  - Start review walks every non-billing item across pages with an 'n of N' label (item 200 of 200 reached); a non-billing row
 *    raises `onOpenWizard`
 *  - a billing row reads 'Finding', links to sprk_invoicereviewpage (invoiceId, lineId only when present, encoded once), has no decide
 *    action, never raises `onOpenWizard`, and is skipped by Start review
 *  - negative: a billing row with no usable invoice id, or with no console base, has no link and no decide action
 *  - loading, empty and error states; an isError result shows the server's message
 */
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { createWorklistService } from '@spaarke/ui-components/components/Worklist/worklistService';
import { DecideLane, type DecideLaneProps, type OpenWizardEvent } from '../lane/DecideLane';
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

interface Mounted {
  onOpenWizard: jest.Mock<void, [OpenWizardEvent]>;
  openLink: jest.Mock<Promise<boolean>, [string]>;
}

function renderLane(callTool: CallServerTool, props: Partial<DecideLaneProps> = {}): Mounted {
  const service = createWorklistService({ fetch: createDecideLaneFetch(callTool), baseUrl: '' });
  const onOpenWizard = jest.fn<void, [OpenWizardEvent]>();
  const openLink = jest.fn<Promise<boolean>, [string]>(async () => true);
  render(
    <FluentProvider theme={webLightTheme}>
      <DecideLane
        service={service}
        consoleBaseUrl={CONSOLE_BASE}
        openLink={openLink}
        onOpenWizard={onOpenWizard}
        today={TODAY}
        {...props}
      />
    </FluentProvider>
  );
  return { onOpenWizard, openLink };
}

const lane = () => screen.getByTestId('worklist-lane-Decide');
const ready = () => screen.findByTestId('decide-lane');
const lines = () => within(lane()).getAllByTestId('issue-line');
const lineIds = () => lines().map(l => l.getAttribute('data-signal-id'));
const card = (label: string) => within(lane()).getByRole('button', { name: new RegExp(`^${label},`) });
const cardLabels = () =>
  within(within(lane()).getByRole('group', { name: 'Decide filters' }))
    .getAllByRole('button')
    .map(b => b.getAttribute('aria-label') ?? '');
const startReview = () => fireEvent.click(screen.getByTestId('lane-start-Decide'));

beforeEach(() => {
  window.localStorage.clear();
  // The list starts collapsed (D-195) and is remembered per device: these tests look at rows, so the lane is open.
  window.localStorage.setItem(LANE_OPEN_KEY, JSON.stringify({ Decide: true }));
});

// ---- the 70-item lane: 3 matters, 2 billing items ------------------------------------------------------------------------------

const A = MATTERS.a.id;
const B = MATTERS.b.id;
const C = MATTERS.c.id;

function seventyItems(): SlimRow[] {
  const rows: SlimRow[] = [];
  for (let n = 1; n <= 30; n += 1) {
    rows.push(
      row(n, { core: A, workType: n <= 18 ? 'askOutsideCounsel' : n <= 26 ? 'approveOrRebudget' : 'chaseReply' })
    );
  }
  for (let n = 31; n <= 55; n += 1) {
    // 51-55 have no work type: they are Other.
    rows.push(row(n, { core: B, workType: n <= 40 ? 'chaseReply' : n <= 50 ? 'askOutsideCounsel' : undefined }));
  }
  for (let n = 56; n <= 70; n += 1) {
    rows.push(row(n, { core: C, workType: n <= 65 ? 'approveOrRebudget' : 'askOutsideCounsel' }));
  }
  // Two billing Work Items inside matter A: an invoice line, and a whole invoice.
  rows[2] = row(3, { core: A, workType: 'approveOrRebudget', invoiceReview: { invoiceId: INVOICE, lineId: LINE } });
  rows[19] = row(20, { core: A, workType: 'approveOrRebudget', invoiceReview: { invoiceId: guid('b', 9) } });
  return rows;
}

/** What `spaarke_show_decisions` reports per category for the same rows (the BFF's `DecideLaneCategories`, restated for the test). */
function showDecisionsCategories(rows: readonly SlimRow[]): Record<string, number> {
  const count = (pick: (r: SlimRow) => boolean) => rows.filter(pick).length;
  const plain = (r: SlimRow) => !r.invoiceReview;
  const types = ['askOutsideCounsel', 'approveOrRebudget', 'chaseReply'];
  return {
    All: rows.length,
    'Invoice decisions': count(r => !plain(r)),
    'Ask outside counsel': count(r => plain(r) && r.workType === 'askOutsideCounsel'),
    'Approve or rebudget': count(r => plain(r) && r.workType === 'approveOrRebudget'),
    'Chase a reply': count(r => plain(r) && r.workType === 'chaseReply'),
    Other: count(r => plain(r) && !types.includes(r.workType ?? '')),
  };
}

describe('the lane renders the Console lane', () => {
  it("shows the header 'Decide', the count and the subtitle", async () => {
    renderLane(pagingHost(seventyItems()).callTool);
    await ready();
    expect(within(lane()).getByRole('heading', { name: 'Decide' })).toBeInTheDocument();
    expect(screen.getByTestId('lane-subtitle-Decide')).toHaveTextContent('Items that need your decision');
    expect(screen.getByTestId('lane-count-Decide')).toHaveTextContent('70');
  });

  it('shows a card per category with the counts spaarke_show_decisions reports for the same data, and the oldest age', async () => {
    const rows = seventyItems();
    renderLane(pagingHost(rows).callTool);
    await ready();

    // Hand-checked: A has 17 ask, 7 approve, 4 chase and the 2 billing items; B 10 chase, 10 ask, 5 other; C 10 approve, 5 ask.
    const expected = showDecisionsCategories(rows);
    expect(expected).toEqual({
      All: 70,
      'Invoice decisions': 2,
      'Ask outside counsel': 32,
      'Approve or rebudget': 17,
      'Chase a reply': 14,
      Other: 5,
    });

    const labels = cardLabels();
    expect(labels).toHaveLength(6);
    for (const [name, count] of Object.entries(expected)) {
      const one = labels.find(l => l.startsWith(`${name},`));
      expect(one).toBeDefined();
      expect(one).toMatch(new RegExp(`^${name}, ${count} Decide items`));
      expect(one).toContain('oldest 3 days'); // every row was raised 2026-10-06; today is 2026-10-09
    }
    // The cards partition the lane.
    const typeSum = Object.entries(expected)
      .filter(([name]) => name !== 'All')
      .reduce((n, [, c]) => n + c, 0);
    expect(typeSum).toBe(70);
  });

  it('groups rows by matter, in the order read, with the reason and the age on each row', async () => {
    renderLane(pagingHost(seventyItems()).callTool);
    await ready();

    const cards = within(lane()).getAllByTestId('matter-card');
    expect(cards.map(c => c.getAttribute('aria-label'))).toEqual([
      `${MATTERS.a.name}, 30 items`,
      `${MATTERS.b.name}, 25 items`,
      `${MATTERS.c.name}, 15 items`,
    ]);
    expect(within(cards[0]).getByTestId('matter-card-detail')).toHaveTextContent(MATTERS.a.number);

    const first = within(cards[0]).getAllByTestId('issue-line')[0];
    expect(first).toHaveTextContent('Subject 1');
    expect(first).toHaveTextContent('Rule 1'); // the reason (the rule's short name)
    expect(within(first).getByTestId('issue-timing')).toHaveTextContent('3d');
  });

  it('clicking a card filters the rows to it and clicking it again returns to All', async () => {
    renderLane(pagingHost(seventyItems()).callTool);
    await ready();

    fireEvent.click(card('Chase a reply'));
    expect(lines()).toHaveLength(14);
    expect(screen.getByTestId('lane-showing-Decide')).toHaveTextContent('Showing 14 of 70');
    expect(screen.getByTestId('lane-count-Decide')).toHaveTextContent('70'); // the count describes the whole lane

    fireEvent.click(card('Chase a reply'));
    expect(lines()).toHaveLength(68); // 70 minus the 2 billing rows, which are not standard lines
  });

  it('reads every page before it is shown, so the counts are the lane’s, not the first page’s', async () => {
    const host = pagingHost(seventyItems(), 50);
    renderLane(host.callTool);
    await ready();
    expect(host.calls.map(c => c.args)).toEqual([{}, { cursor: 'cursor-50' }]);
    expect(screen.getByTestId('lane-count-Decide')).toHaveTextContent('70');
  });

  it('shows no "done today" line unless a count is given (no tool returns it), and shows it when given', async () => {
    renderLane(pagingHost(seventyItems()).callTool);
    await ready();
    expect(within(lane()).queryByText(/done today/)).not.toBeInTheDocument();
  });

  it('shows "n of m done today" on the All card when a count is given', async () => {
    renderLane(pagingHost(seventyItems()).callTool, { doneToday: 5 });
    await ready();
    expect(within(lane()).getByText('5 of 75 done today')).toBeInTheDocument();
  });
});

describe('a non-billing row opens the wizard (task 032 consumes the event)', () => {
  it('raises onOpenWizard with the signal id, its core, and the non-billing visible list in order', async () => {
    const rows = seventyItems();
    const { onOpenWizard } = renderLane(pagingHost(rows).callTool);
    await ready();

    fireEvent.click(lines().find(l => l.getAttribute('data-signal-id') === rows[40].signalId)!);

    expect(onOpenWizard).toHaveBeenCalledTimes(1);
    const event = onOpenWizard.mock.calls[0][0];
    expect(event.signalId).toBe(rows[40].signalId);
    expect(event.core).toMatchObject({ recordId: B });
    const nonBilling = rows.filter(r => !r.invoiceReview).map(r => r.signalId);
    expect(event.visibleList).toEqual(nonBilling);
    expect(event.total).toBe(68);
    expect(event.position).toBe(nonBilling.indexOf(rows[40].signalId) + 1);
  });

  it('opens within the filter: the wizard browses only what the card shows', async () => {
    const rows = seventyItems();
    const { onOpenWizard } = renderLane(pagingHost(rows).callTool);
    await ready();
    fireEvent.click(card('Other'));
    fireEvent.click(lines()[0]);
    expect(onOpenWizard.mock.calls[0][0].visibleList).toEqual(rows.slice(50, 55).map(r => r.signalId));
  });

  it('rows are native buttons, so Enter and Space operate them (NFR-07)', async () => {
    renderLane(pagingHost(seventyItems()).callTool);
    await ready();
    expect(lines().every(l => l.tagName === 'BUTTON')).toBe(true);
  });
});

describe('Start review walks the whole lane across pages', () => {
  it('reaches item 200 of 200 in a 200-item lane, skipping billing items, with an "n of N" label', async () => {
    jest.setTimeout(120000);
    // 200 decidable rows over four matters, plus a billing item on each of two page boundaries (rows 50/51 and 100/101).
    const ordered: SlimRow[] = [];
    const matters = [A, B, C, MATTERS.d.id];
    for (let n = 1; n <= 200; n += 1) {
      ordered.push(row(n, { core: matters[Math.floor((n - 1) / 50)], workType: 'askOutsideCounsel' }));
      if (n === 49)
        ordered.push(
          row(901, { core: matters[0], workType: 'approveOrRebudget', invoiceReview: { invoiceId: INVOICE } })
        );
      if (n === 98)
        ordered.push(
          row(902, { core: matters[1], workType: 'approveOrRebudget', invoiceReview: { invoiceId: guid('b', 2) } })
        );
    }
    const host = pagingHost(ordered, 50);
    const { onOpenWizard } = renderLane(host.callTool);
    await ready();
    expect(host.calls.map(c => c.args.cursor)).toEqual([
      undefined,
      'cursor-50',
      'cursor-100',
      'cursor-150',
      'cursor-200',
    ]);
    expect(screen.getByTestId('lane-count-Decide')).toHaveAttribute('aria-label', '202 Decide items'); // the badge itself caps at 99+, as in the Console

    startReview();
    expect(screen.getByTestId('decide-review-position')).toHaveTextContent('1 of 200');
    expect(onOpenWizard).toHaveBeenCalledTimes(1);
    expect(onOpenWizard.mock.calls[0][0]).toMatchObject({ signalId: ordered[0].signalId, position: 1, total: 200 });

    const seen = [onOpenWizard.mock.calls[0][0].signalId];
    for (let i = 1; i < 200; i += 1) {
      fireEvent.click(screen.getByTestId('decide-review-next'));
      seen.push(onOpenWizard.mock.calls[i][0].signalId);
    }

    expect(screen.getByTestId('decide-review-position')).toHaveTextContent('200 of 200');
    expect(screen.getByTestId('decide-review-next')).toBeDisabled();
    expect(seen).toEqual(ordered.filter(r => !r.invoiceReview).map(r => r.signalId));
    expect(seen[199]).toBe(row(200).signalId);
    expect(seen).not.toContain(row(901).signalId);
    expect(seen).not.toContain(row(902).signalId);
    expect(onOpenWizard.mock.calls.every(([e]) => e.total === 200 && e.visibleList.length === 200)).toBe(true);
  }, 120000);

  it('Previous steps back, Stop review removes the bar, and a filter change ends the walk', async () => {
    const rows = seventyItems();
    const { onOpenWizard } = renderLane(pagingHost(rows).callTool);
    await ready();

    startReview();
    fireEvent.click(screen.getByTestId('decide-review-next'));
    expect(screen.getByTestId('decide-review-position')).toHaveTextContent('2 of 68');
    expect(screen.getByTestId('decide-review-previous')).toBeEnabled();
    fireEvent.click(screen.getByTestId('decide-review-previous'));
    expect(screen.getByTestId('decide-review-position')).toHaveTextContent('1 of 68');
    expect(screen.getByTestId('decide-review-previous')).toBeDisabled();
    expect(onOpenWizard).toHaveBeenCalledTimes(3);

    fireEvent.click(card('Other'));
    expect(screen.queryByTestId('decide-review-bar')).not.toBeInTheDocument();

    startReview();
    expect(screen.getByTestId('decide-review-position')).toHaveTextContent('1 of 5');
    fireEvent.click(screen.getByTestId('decide-review-stop'));
    expect(screen.queryByTestId('decide-review-bar')).not.toBeInTheDocument();
  });

  it('opening another row during a review moves the label to that row', async () => {
    const rows = seventyItems();
    const { onOpenWizard } = renderLane(pagingHost(rows).callTool);
    await ready();
    startReview();
    fireEvent.click(lines().find(l => l.getAttribute('data-signal-id') === rows[40].signalId)!);
    // rows[40] is the 39th decidable item (two billing rows come before it)
    expect(screen.getByTestId('decide-review-position')).toHaveTextContent('39 of 68');
    expect(onOpenWizard.mock.calls.at(-1)?.[0]).toMatchObject({ signalId: rows[40].signalId, position: 39, total: 68 });
  });

  it('starts on the first decidable item when the lane begins with a billing item', async () => {
    const rows = [
      row(1, { core: A, workType: 'approveOrRebudget', invoiceReview: { invoiceId: INVOICE } }),
      row(2, { core: A, workType: 'chaseReply' }),
    ];
    const { onOpenWizard } = renderLane(pagingHost(rows).callTool);
    await ready();
    startReview();
    expect(onOpenWizard).toHaveBeenCalledTimes(1);
    expect(onOpenWizard.mock.calls[0][0]).toMatchObject({ signalId: rows[1].signalId, position: 1, total: 1 });
  });
});

describe('billing Work Items link out only (EB-D-194, D-208)', () => {
  const billingLane = () => [
    row(1, {
      core: A,
      workType: 'approveOrRebudget',
      invoiceReview: { invoiceId: INVOICE.toUpperCase(), lineId: `{${LINE.toUpperCase()}}` },
    }),
    row(2, { core: A, workType: 'chaseReply' }),
    row(3, { core: A, workType: 'approveOrRebudget', invoiceReview: { invoiceId: INVOICE } }),
  ];

  it("reads 'Finding' and has no decide action: it is not a standard line, and clicking it opens no wizard", async () => {
    const rows = billingLane();
    const { onOpenWizard, openLink } = renderLane(pagingHost(rows).callTool);
    await ready();

    const findings = within(lane()).getAllByTestId('billing-finding');
    expect(findings.map(f => f.getAttribute('data-signal-id'))).toEqual([rows[0].signalId, rows[2].signalId]);
    for (const finding of findings) {
      expect(within(finding).getByTestId('billing-finding-label')).toHaveTextContent('Finding');
      expect(within(finding).queryByRole('button')).not.toBeInTheDocument(); // no decide action
      fireEvent.click(finding);
    }
    expect(lineIds()).toEqual([rows[1].signalId]); // only the non-billing row is a line
    expect(onOpenWizard).not.toHaveBeenCalled();
    expect(openLink).not.toHaveBeenCalled();
  });

  it('links to sprk_invoicereviewpage with lower-case guids, lineId only when present, encoded once, opened by the host', async () => {
    const rows = billingLane();
    const { onOpenWizard, openLink } = renderLane(pagingHost(rows).callTool);
    await ready();

    const [withLine, invoiceOnly] = within(lane()).getAllByTestId('billing-finding-link');
    expect(withLine.tagName).toBe('A'); // keyboard-operable: a real link
    expect(withLine.getAttribute('href')).toBe(
      `${CONSOLE_BASE}/main.aspx?pagetype=webresource&webresourceName=sprk_invoicereviewpage&data=invoiceId%3D${INVOICE}%26lineId%3D${LINE}`
    );
    expect(invoiceOnly.getAttribute('href')).toBe(
      `${CONSOLE_BASE}/main.aspx?pagetype=webresource&webresourceName=sprk_invoicereviewpage&data=invoiceId%3D${INVOICE}`
    );
    expect(invoiceOnly.getAttribute('href')).not.toContain('lineId');

    // The widget never navigates itself: the click is cancelled and the host is asked to open the page.
    const notCancelled = fireEvent.click(withLine);
    expect(notCancelled).toBe(false);
    await waitFor(() => expect(openLink).toHaveBeenCalledTimes(1));
    expect(openLink).toHaveBeenCalledWith(withLine.getAttribute('href'));
    expect(onOpenWizard).not.toHaveBeenCalled();
  });

  it('is skipped by Start review and left out of the wizard browse set', async () => {
    const rows = billingLane();
    const { onOpenWizard } = renderLane(pagingHost(rows).callTool);
    await ready();
    startReview();
    expect(onOpenWizard.mock.calls[0][0]).toMatchObject({ signalId: rows[1].signalId, total: 1 });
    expect(onOpenWizard.mock.calls[0][0].visibleList).toEqual([rows[1].signalId]);
  });

  it('under the Invoice decisions card there is nothing to review here, and no wizard opens', async () => {
    const { onOpenWizard } = renderLane(pagingHost(billingLane()).callTool);
    await ready();
    fireEvent.click(card('Invoice decisions'));
    expect(within(lane()).getAllByTestId('billing-finding')).toHaveLength(2);
    startReview();
    expect(screen.getByTestId('decide-lane-notice')).toHaveTextContent(
      'Invoice decisions are decided in Invoice Review'
    );
    expect(onOpenWizard).not.toHaveBeenCalled();
    expect(screen.queryByTestId('decide-review-bar')).not.toBeInTheDocument();
  });

  it('negative: a billing row whose invoice id is empty after cleaning has no link and no decide action', async () => {
    const rows = [row(1, { core: A, workType: 'approveOrRebudget', invoiceReview: { invoiceId: '{}' } })];
    const { onOpenWizard, openLink } = renderLane(pagingHost(rows).callTool);
    await ready();

    const finding = within(lane()).getByTestId('billing-finding');
    expect(within(finding).getByTestId('billing-finding-label')).toHaveTextContent('Finding');
    expect(within(finding).queryByRole('link')).not.toBeInTheDocument();
    expect(within(finding).getByTestId('billing-finding-no-link')).toBeInTheDocument();
    expect(within(finding).queryByRole('button')).not.toBeInTheDocument();
    fireEvent.click(finding);
    expect(onOpenWizard).not.toHaveBeenCalled();
    expect(openLink).not.toHaveBeenCalled();
  });

  it('negative: with no usable console base a billing row has no link (the widget does not guess a host)', async () => {
    for (const consoleBaseUrl of [null, undefined, 'http://org.crm.dynamics.com', 'not a url']) {
      const { unmount } = render(<div />);
      unmount();
      const view = renderLane(pagingHost(billingLane()).callTool, { consoleBaseUrl });
      await ready();
      expect(within(lane()).queryAllByTestId('billing-finding-link')).toHaveLength(0);
      expect(within(lane()).getAllByTestId('billing-finding-no-link')).toHaveLength(2);
      expect(view.openLink).not.toHaveBeenCalled();
      document.body.innerHTML = '';
    }
  });

  it('says Invoice Review could not be opened when the host refuses or throws', async () => {
    const { openLink } = renderLane(pagingHost(billingLane()).callTool);
    await ready();
    const [link] = within(lane()).getAllByTestId('billing-finding-link');

    openLink.mockResolvedValueOnce(false);
    fireEvent.click(link);
    expect(await screen.findByTestId('decide-lane-notice')).toHaveTextContent('Invoice Review could not be opened');

    openLink.mockRejectedValueOnce(new Error('blocked'));
    fireEvent.click(link);
    await waitFor(() => expect(openLink).toHaveBeenCalledTimes(2));
    expect(await screen.findByTestId('decide-lane-notice')).toHaveTextContent('Invoice Review could not be opened');
  });
});

describe('loading, empty and error states', () => {
  it('shows a loading state until the lane is read', async () => {
    let finish!: () => void;
    const gate = new Promise<void>(resolve => (finish = resolve));
    const host = pagingHost(seventyItems());
    renderLane(async (name, args) => {
      await gate;
      return host.callTool(name, args);
    });
    expect(screen.getByTestId('decide-lane-loading')).toBeInTheDocument();
    expect(screen.queryByTestId('worklist-lane-Decide')).not.toBeInTheDocument();
    finish();
    await ready();
    expect(screen.queryByTestId('decide-lane-loading')).not.toBeInTheDocument();
  });

  it('shows the empty state for an empty lane, with the header and a disabled Start review', async () => {
    renderLane(pagingHost([]).callTool);
    await ready();
    expect(screen.getByTestId('lane-empty-Decide')).toHaveTextContent('Nothing to decide.');
    expect(screen.getByTestId('lane-count-Decide')).toHaveTextContent('0');
    expect(screen.getByTestId('lane-start-Decide')).toBeDisabled();
  });

  it("shows the server's message when the tool answers isError, and Try again reads the lane again", async () => {
    const good = pagingHost(seventyItems());
    let calls = 0;
    renderLane(async (name, args) => {
      calls += 1;
      return calls === 1
        ? toolError('ontology.signal.worklist_unavailable', 'The worklist is unavailable right now.', 503)
        : good.callTool(name, args);
    });
    const error = await screen.findByTestId('decide-lane-error');
    expect(error).toHaveTextContent('The worklist is unavailable right now.');
    expect(screen.getByRole('heading', { name: 'Decide' })).toBeInTheDocument();

    fireEvent.click(within(error).getByRole('button', { name: 'Try again' }));
    await ready();
    expect(screen.getByTestId('lane-count-Decide')).toHaveTextContent('70');
  });

  it('shows the text of a failure that has no domain answer (bad arguments, an exception)', async () => {
    renderLane(async () => ({
      isError: true,
      content: [{ type: 'text', text: 'An error occurred invoking the tool.' }],
    }));
    expect(await screen.findByTestId('decide-lane-error')).toHaveTextContent('An error occurred invoking the tool.');
  });

  it('a 403 is the caller’s access: no Try again button', async () => {
    renderLane(async () => toolError('ontology.signal.not_permitted', 'You do not have access.', 403));
    const error = await screen.findByTestId('decide-lane-error');
    expect(error).toHaveTextContent('Contact your administrator');
    expect(within(error).queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument();
  });

  it('a host call that rejects shows a plain error, not an empty lane', async () => {
    renderLane(() => Promise.reject(new Error('host went away')));
    expect(await screen.findByTestId('decide-lane-error')).toHaveTextContent('The worklist could not be read.');
    expect(screen.queryByTestId('lane-empty-Decide')).not.toBeInTheDocument();
  });
});

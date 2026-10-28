/**
 * WorklistWidget - unit tests (task 059). The stated contract:
 *  - Decide renders above Do; each lane shows its count and its start button (W-1, W-2)
 *  - a count filter narrows the lane in place: "Showing n of m", a card with an item outside the filter says so, the narrative and
 *    the aggregate row hide (W-9..W-11); the count cards and the lane count keep describing the full list
 *  - nothing is remembered: a new mount opens with no filter (HANDOFF section 1.4 L123)
 *  - the aggregate row is outside both lanes' counts, at the top of the Do lane
 *  - Closed today / Suppressed (with its until-date) / Closed earlier list their sets and open the wizard read-only (W-12)
 *  - a line, a start button and a disclosure line all open the wizard on the list the user is looking at
 *  - negative: the route's order is the list's order (no re-sort); the widget reads `sprk_signal` only through the route
 *  - one card per core record of any type; the reader's Not filed items only in the Do lane (D-34..D-36)
 */

import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

import { WorklistRequestError, WorklistWidget, createWorklistService } from '..';
import { LANE_OPEN_STORAGE_KEY } from '../useLaneOpen';
import type { WorklistService } from '..';
import type { DecisionWizardLaunchRequest } from '../../DecisionWizard';
import {
  A1,
  A2,
  A3,
  C1,
  C2,
  D1,
  D2,
  D3,
  D4,
  D5,
  E1,
  ID,
  ROUTE_BODIES,
  S1,
  body,
  fakeService,
  group,
  matterCore,
  projectCore,
  routeItem,
  serviceRequestCore,
} from '../__fixtures__/worklistRouteFixtures';

/** The viewer's local calendar day every test renders against (the same day task 051's tests use). */
const TODAY = new Date(2026, 9, 9, 10, 30);

type Launch = jest.Mock<Promise<{ opened: boolean; reason?: 'no-host' | 'busy' }>, [DecisionWizardLaunchRequest]>;
const launcher = (): Launch => jest.fn(async () => ({ opened: true }));

function renderWidget(
  props: Partial<React.ComponentProps<typeof WorklistWidget>> & { service?: WorklistService } = {},
  dark = false
) {
  const service = props.service ?? fakeService();
  const launchWizard = props.launchWizard ?? launcher();
  const utils = render(
    <FluentProvider theme={dark ? webDarkTheme : webLightTheme}>
      <WorklistWidget service={service} today={TODAY} launchWizard={launchWizard} {...props} />
    </FluentProvider>
  );
  return { ...utils, service, launchWizard: launchWizard as Launch };
}

// Each lane's open state is remembered per device (D-195, task 169): start every test from a clean device.
beforeEach(() => window.localStorage.clear());

async function ready() {
  await screen.findByTestId('worklist-widget');
}

const lane = (name: 'Decide' | 'Do') => screen.getByTestId(`worklist-lane-${name}`);
const lineIds = (el: HTMLElement) =>
  within(el)
    .getAllByTestId('issue-line')
    .map(l => l.getAttribute('data-signal-id'));
const cardNames = (el: HTMLElement) =>
  within(el)
    .getAllByTestId('matter-card')
    .map(c => c.getAttribute('aria-label')?.replace(/, \d+ items?$/, ''));
const countCard = (el: HTMLElement, label: RegExp) => within(el).getByRole('button', { name: label });

describe('lanes', () => {
  it('renders Decide above Do, each with its count and its start button', async () => {
    renderWidget();
    await ready();
    const lanes = screen.getAllByTestId(/^worklist-lane-/);
    expect(lanes.map(l => l.getAttribute('data-testid'))).toEqual(['worklist-lane-Decide', 'worklist-lane-Do']);
    expect(within(lane('Decide')).getByTestId('lane-count-Decide')).toHaveTextContent('3');
    expect(within(lane('Do')).getByTestId('lane-count-Do')).toHaveTextContent('5');
    expect(within(lane('Decide')).getByRole('button', { name: 'Start review' })).toBeEnabled();
    expect(within(lane('Do')).getByRole('button', { name: 'Work through these' })).toBeEnabled();
    expect(lineIds(lane('Decide'))).toEqual([A1, A2, A3]);
  });

  it('keeps every Decide line above a long Do lane: 49 overdue Do items never push 2 decisions down or out (task 064, BR-5)', async () => {
    // The production shape that motivated "Decide always above Do": a few judgements and a long run of overdue work.
    const doItems = Array.from({ length: 49 }, (_, i) =>
      routeItem(`20000000-0000-0000-0000-${String(i + 100).padStart(12, '0')}`, 'Do', {
        subjectName: `Overdue item ${i}`,
        ruleShortName: 'Overdue task',
        workType: 'finishOrReschedule',
        dueDate: '2026-10-04',
      })
    );
    renderWidget({
      service: fakeService({
        lanes: body(
          'lanes',
          [
            group(matterCore, [routeItem(A1, 'Decide', { workType: 'approveOrRebudget' })]),
            group(serviceRequestCore, [routeItem(A3, 'Decide', { workType: 'chaseReply' })]),
          ],
          [group(projectCore, doItems)]
        ),
      }),
    });
    await ready();

    // Both decisions are rendered, in the Decide lane, with nothing hidden behind a cap or a collapsed section.
    expect(lineIds(lane('Decide'))).toEqual([A1, A3]);
    expect(within(lane('Decide')).getByTestId('lane-count-Decide')).toHaveTextContent('2');
    expect(within(lane('Do')).getByTestId('lane-count-Do')).toHaveTextContent('49');
    expect(within(lane('Decide')).queryByTestId('lane-showing-Decide')).not.toBeInTheDocument();
    expect(within(lane('Decide')).getByRole('button', { name: 'Start review' })).toBeEnabled();

    // The Decide lane precedes the Do lane in the document (and so on screen), and no Do line sits before a Decide line.
    // compareDocumentPosition returns a bit mask
    expect(lane('Decide').compareDocumentPosition(lane('Do')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    const allLines = screen.getAllByTestId('issue-line').map(l => l.getAttribute('data-signal-id'));
    expect(allLines.slice(0, 2)).toEqual([A1, A3]);
    expect(allLines).toHaveLength(2 + 49);
  });

  it("shows the route's order exactly: the widget never re-sorts (unranked input stays unranked)", async () => {
    renderWidget();
    await ready();
    // The Do groups arrive work assignment, project, matter, Not filed (last, as the route emits them); items inside a group keep
    // their order too.
    expect(cardNames(lane('Do'))).toEqual([
      'Review vendor agreement',
      'Contract refresh 2026',
      'Acme v. Beta',
      'Not filed',
    ]);
    expect(lineIds(lane('Do'))).toEqual([D4, D3, D1, D2, D5]);
  });

  it('never moves the Not filed group, wherever the route puts it (the route is the order authority)', async () => {
    // A body that puts Not filed FIRST and the matter before the project: the widget shows exactly that.
    renderWidget({
      service: fakeService({
        lanes: body(
          'lanes',
          [],
          [
            group(null, [routeItem(D5, 'Do', { workType: 'finishOrReschedule' })]),
            group(matterCore, [routeItem(D1, 'Do', { workType: 'finishOrReschedule' })]),
            group({ recordType: 'sprk_project', recordId: ID.project, typeLabel: 'Project', name: 'P', number: '1' }, [
              routeItem(D3, 'Do', { workType: 'comingDue' }),
            ]),
          ]
        ),
      }),
    });
    await ready();
    expect(cardNames(lane('Do'))).toEqual(['Not filed', 'Acme v. Beta', 'P']);
    expect(lineIds(lane('Do'))).toEqual([D5, D1, D3]);
  });

  it('renders one card per core record of any type, and Not filed only in the Do lane (D-34..D-36)', async () => {
    renderWidget();
    await ready();
    expect(cardNames(lane('Decide'))).toEqual(['Acme v. Beta', 'Onboarding request from Northwind']);
    const doCards = within(lane('Do')).getAllByTestId('matter-card');
    expect(doCards.map(c => c.getAttribute('data-core-type'))).toEqual([
      'sprk_workassignment',
      'sprk_project',
      'sprk_matter',
      'not-filed',
    ]);
    expect(within(lane('Decide')).queryByText('Not filed')).not.toBeInTheDocument();
    // The work assignment and the service request each have their own card; the type label comes from the data.
    expect(screen.getAllByTestId('matter-card-detail').map(d => d.textContent)).toEqual(
      expect.arrayContaining(['Work assignment · WRK-00019', 'Service request · SVCR-00003'])
    );
  });

  it('says so, and offers no Start, for an empty lane', async () => {
    renderWidget({ service: fakeService({ lanes: body('lanes', [], []) }) });
    await ready();
    expect(screen.getByTestId('lane-empty-Decide')).toHaveTextContent('Nothing to decide.');
    expect(screen.getByTestId('lane-empty-Do')).toHaveTextContent('Nothing to do.');
    expect(screen.getByTestId('lane-start-Decide')).toBeDisabled();
    expect(screen.getByTestId('lane-start-Do')).toBeDisabled();
  });

  it('under the email aggregate row an empty Do lane says "No other items to do."', async () => {
    renderWidget({
      service: fakeService({ lanes: body('lanes', [], []) }),
      aggregateRow: <div data-testid="agg">8 emails await a match confirmation</div>,
    });
    await ready();
    expect(screen.getByTestId('agg')).toBeInTheDocument();
    expect(screen.getByTestId('lane-empty-Do')).toHaveTextContent('No other items to do.');
    expect(screen.getByTestId('lane-empty-Do')).not.toHaveTextContent('Nothing to do.');
    // The Decide lane has no aggregate row and keeps its own wording.
    expect(screen.getByTestId('lane-empty-Decide')).toHaveTextContent('Nothing to decide.');
  });

  it('reads sets lazily: lanes and closed today on mount, the other two when their disclosure opens', async () => {
    const { service } = renderWidget();
    await ready();
    await waitFor(() => expect(service.read).toHaveBeenCalledTimes(2));
    expect((service.read as jest.Mock).mock.calls.map(c => c[0]).sort()).toEqual(['closedToday', 'lanes']);
    await userEvent.click(within(screen.getByTestId('disclosure-suppressed')).getByRole('button'));
    await waitFor(() => expect(service.read).toHaveBeenCalledWith('suppressed'));
    expect(service.read).not.toHaveBeenCalledWith('closedEarlier');
  });
});

describe('count filters', () => {
  it('a Do count card filters the lane in place and the honesty lines appear', async () => {
    renderWidget({
      narrative: <span>Seven things to do.</span>,
      aggregateRow: <div data-testid="agg">8 emails await a match confirmation</div>,
    });
    await ready();
    expect(screen.getByTestId('worklist-narrative')).toBeInTheDocument();
    expect(screen.getByTestId('agg')).toBeInTheDocument();

    await userEvent.click(countCard(lane('Do'), /^Coming due/));

    expect(within(lane('Do')).getByTestId('lane-showing-Do')).toHaveTextContent('Showing 2 of 5 - Coming due.');
    expect(lineIds(lane('Do'))).toEqual([D3, D2]);
    expect(screen.queryByTestId('worklist-narrative')).not.toBeInTheDocument();
    expect(screen.queryByTestId('agg')).not.toBeInTheDocument();
    // Counts keep describing the full list.
    expect(within(lane('Do')).getByTestId('lane-count-Do')).toHaveTextContent('5');
    expect(countCard(lane('Do'), /^All/)).toBeInTheDocument();
    // The Decide lane is not touched by a Do filter.
    expect(lineIds(lane('Decide'))).toEqual([A1, A2, A3]);
  });

  it('a card with an item outside the filter says so', async () => {
    renderWidget();
    await ready();
    await userEvent.click(countCard(lane('Do'), /^Coming due/));
    const cards = within(lane('Do')).getAllByTestId('matter-card');
    const matter = cards.find(c => c.getAttribute('data-core-type') === 'sprk_matter')!;
    expect(matter).toHaveTextContent('+1 other open item on this matter, not in this filter');
    // The project has nothing outside the filter: no line.
    const project = cards.find(c => c.getAttribute('data-core-type') === 'sprk_project')!;
    expect(project).not.toHaveTextContent('other open');
  });

  it('names the core type from the data (a project says "this project"), and Not filed says "in Not filed"', async () => {
    const service = fakeService({
      lanes: body(
        'lanes',
        [],
        [
          group(null, [
            routeItem(D5, 'Do', { workType: 'finishOrReschedule' }),
            routeItem(D4, 'Do', { workType: 'comingDue' }),
          ]),
          group({ recordType: 'sprk_project', recordId: ID.project, typeLabel: 'Project', name: 'P', number: '1' }, [
            routeItem(D3, 'Do', { workType: 'comingDue' }),
            routeItem(D2, 'Do', { workType: 'finishOrReschedule' }),
          ]),
        ]
      ),
    });
    renderWidget({ service });
    await ready();
    await userEvent.click(countCard(lane('Do'), /^Coming due/));
    const cards = within(lane('Do')).getAllByTestId('matter-card');
    expect(cards[0]).toHaveTextContent('+1 other open item in Not filed, not in this filter');
    expect(cards[1]).toHaveTextContent('+1 other open item on this project, not in this filter');
  });

  it('Show all returns to the full list and the narrative and aggregate row come back', async () => {
    renderWidget({ narrative: <span>Seven things to do.</span>, aggregateRow: <div data-testid="agg">8 emails</div> });
    await ready();
    await userEvent.click(countCard(lane('Do'), /^Coming due/));
    await userEvent.click(within(lane('Do')).getByRole('button', { name: 'Show all' }));
    expect(within(lane('Do')).queryByTestId('lane-showing-Do')).not.toBeInTheDocument();
    expect(lineIds(lane('Do'))).toEqual([D4, D3, D1, D2, D5]);
    expect(screen.getByTestId('worklist-narrative')).toBeInTheDocument();
    expect(screen.getByTestId('agg')).toBeInTheDocument();
  });

  it('choosing the selected card again also clears the filter', async () => {
    renderWidget();
    await ready();
    await userEvent.click(countCard(lane('Do'), /^Coming due/));
    await userEvent.click(countCard(lane('Do'), /^Coming due/));
    expect(within(lane('Do')).queryByTestId('lane-showing-Do')).not.toBeInTheDocument();
  });

  it('a Decide filter hides the narrative but leaves the Do aggregate row (only a Do filter hides it)', async () => {
    renderWidget({ narrative: <span>Words</span>, aggregateRow: <div data-testid="agg">8 emails</div> });
    await ready();
    await userEvent.click(countCard(lane('Decide'), /^Chase a reply/));
    expect(within(lane('Decide')).getByTestId('lane-showing-Decide')).toHaveTextContent('Showing 1 of 3');
    expect(screen.queryByTestId('worklist-narrative')).not.toBeInTheDocument();
    expect(screen.getByTestId('agg')).toBeInTheDocument();
  });

  it('shows "n of m done today" on the All card from the closed-today set', async () => {
    renderWidget();
    await ready();
    // Closed today holds one Decide and one Do item: Decide 1 done + 3 open, Do 1 done + 5 open.
    await waitFor(() => expect(countCard(lane('Decide'), /^All/)).toHaveTextContent('1 of 4 done today'));
    expect(countCard(lane('Do'), /^All/)).toHaveTextContent('1 of 6 done today');
  });

  it('the filter is not remembered: a new mount opens with no filter and only the lane open state is written to browser storage', async () => {
    const setItem = jest.spyOn(Storage.prototype, 'setItem');
    const first = renderWidget();
    await ready();
    await userEvent.click(countCard(lane('Do'), /^Coming due/));
    expect(within(lane('Do')).getByTestId('lane-showing-Do')).toBeInTheDocument();
    first.unmount();

    renderWidget();
    await ready();
    expect(within(lane('Do')).queryByTestId('lane-showing-Do')).not.toBeInTheDocument();
    expect(lineIds(lane('Do'))).toEqual([D4, D3, D1, D2, D5]);
    // Task 169 (D-195): the lanes' open state is the one thing remembered; no filter is ever written.
    expect(setItem.mock.calls.length).toBeGreaterThan(0);
    expect(setItem.mock.calls.every(([key]) => key === LANE_OPEN_STORAGE_KEY)).toBe(true);
    expect(JSON.parse(window.localStorage.getItem(LANE_OPEN_STORAGE_KEY) ?? '{}')).toEqual({ Do: true });
    setItem.mockRestore();
  });
});

describe('the aggregate row and the narrative slot', () => {
  it('the aggregate row is the first thing in the Do lane list and is in neither lane count', async () => {
    renderWidget({ aggregateRow: <div data-testid="agg">8 emails await a match confirmation</div> });
    await ready();
    const doLane = lane('Do');
    expect(within(doLane).getByTestId('worklist-aggregate-row')).toContainElement(screen.getByTestId('agg'));
    // Lane counts, the All card and the start list all ignore it: 5 Do items, not 6.
    expect(within(doLane).getByTestId('lane-count-Do')).toHaveTextContent('5');
    expect(countCard(doLane, /^All/)).toHaveTextContent('5');
    expect(lineIds(doLane)).toHaveLength(5);
    // It precedes the first card in document order.
    const aggregate = within(doLane).getByTestId('worklist-aggregate-row');
    const firstCard = within(doLane).getAllByTestId('matter-card')[0];
    expect(aggregate.compareDocumentPosition(firstCard) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    // And it is not in the Decide lane.
    expect(within(lane('Decide')).queryByTestId('agg')).not.toBeInTheDocument();
  });

  it('shows the narrative only when the host supplies one, framed as not choosing or ordering the list', async () => {
    const { unmount } = renderWidget();
    await ready();
    expect(screen.queryByTestId('worklist-narrative')).not.toBeInTheDocument();
    unmount();
    renderWidget({ narrative: <span>You also have five things to do.</span> });
    await ready();
    const narrative = screen.getByTestId('worklist-narrative');
    expect(narrative).toHaveTextContent('You also have five things to do.');
    expect(narrative).toHaveTextContent(
      "Written by the Assistant from the lists below. It doesn't choose or order them."
    );
  });
});

describe('opening the wizard (D-26: in-app, on the list the user is looking at)', () => {
  it('a line opens the wizard with the lane list in the route order', async () => {
    const { launchWizard } = renderWidget();
    await ready();
    await userEvent.click(within(lane('Do')).getAllByTestId('issue-line')[1]);
    expect(launchWizard).toHaveBeenCalledTimes(1);
    const request = launchWizard.mock.calls[0][0];
    expect(request.itemId).toBe(D3);
    expect(request.core).toEqual({ recordType: 'sprk_project', recordId: ID.project });
    expect(request.visibleList).toEqual([D4, D3, D1, D2, D5]);
  });

  it('with a filter on, the browse set is the filtered list and nothing else', async () => {
    const { launchWizard } = renderWidget();
    await ready();
    await userEvent.click(countCard(lane('Do'), /^Coming due/));
    await userEvent.click(within(lane('Do')).getAllByTestId('issue-line')[0]);
    expect(launchWizard.mock.calls[0][0].visibleList).toEqual([D3, D2]);
    expect(launchWizard.mock.calls[0][0].itemId).toBe(D3);
  });

  it("a lane's start button walks the visible list from its first item", async () => {
    const { launchWizard } = renderWidget();
    await ready();
    await userEvent.click(within(lane('Decide')).getByRole('button', { name: 'Start review' }));
    expect(launchWizard.mock.calls[0][0]).toMatchObject({
      itemId: A1,
      core: { recordType: 'sprk_matter', recordId: ID.matter },
      visibleList: [A1, A2, A3],
    });

    await userEvent.click(countCard(lane('Do'), /^Coming due/));
    await userEvent.click(within(lane('Do')).getByRole('button', { name: 'Work through these' }));
    expect(launchWizard.mock.calls[1][0]).toMatchObject({
      itemId: D3,
      core: { recordType: 'sprk_project', recordId: ID.project },
      visibleList: [D3, D2],
    });
  });

  it('a Not filed line opens with no core record (D-35)', async () => {
    const { launchWizard } = renderWidget();
    await ready();
    await userEvent.click(within(lane('Do')).getAllByTestId('issue-line')[4]); // Not filed is the last group
    expect(launchWizard.mock.calls[0][0]).toMatchObject({ itemId: D5, core: null });
  });

  it('gives the wizard an item reader built from the rows it already holds (no second read)', async () => {
    const { launchWizard, service } = renderWidget();
    await ready();
    await userEvent.click(within(lane('Decide')).getAllByTestId('issue-line')[0]);
    const callsBefore = (service.read as jest.Mock).mock.calls.length;
    const item = await launchWizard.mock.calls[0][0].loadItem!(A1);
    expect(item.row.signalId).toBe(A1);
    expect(item.core).toMatchObject({ recordType: 'sprk_matter', name: 'Acme v. Beta' });
    expect(item.resolution).toBeNull();
    // Other OPEN items on the same core record in the same lane: A2 only (not itself, not the service request's A3).
    expect(item.otherOpenItems.map(i => i.signalId)).toEqual([A2]);
    // A Signal that stores no sentence: the wizard is told null rather than shown an invented one.
    expect(item.sentence).toBeNull();
    expect((service.read as jest.Mock).mock.calls.length).toBe(callsBefore);
    await expect(launchWizard.mock.calls[0][0].loadItem!('missing')).rejects.toThrow(/no longer on the list/);
  });

  it('a Not filed item has no other open items to show', async () => {
    const { launchWizard } = renderWidget();
    await ready();
    await userEvent.click(within(lane('Do')).getAllByTestId('issue-line')[4]); // Not filed is the last group
    const item = await launchWizard.mock.calls[0][0].loadItem!(D5);
    expect(item.core).toBeNull();
    expect(item.otherOpenItems).toEqual([]);
  });

  it('passes the host lookup to the wizard', async () => {
    const lookup = jest.fn(async () => []);
    const { launchWizard } = renderWidget({ lookup });
    await ready();
    await userEvent.click(within(lane('Do')).getAllByTestId('issue-line')[0]);
    expect(launchWizard.mock.calls[0][0].lookup).toBe(lookup);
  });

  it('reads the lanes again once the wizard closed after recording', async () => {
    const launchWizard: Launch = jest.fn(async request => {
      request.onRecorded?.({ signalId: D5, alsoResolved: [] });
      return { opened: true };
    });
    const { service } = renderWidget({ launchWizard });
    await ready();
    await waitFor(() => expect(service.read).toHaveBeenCalledTimes(2));
    await userEvent.click(within(lane('Do')).getAllByTestId('issue-line')[0]);
    await waitFor(() => expect(service.read).toHaveBeenCalledTimes(4));
    expect(
      (service.read as jest.Mock).mock.calls
        .slice(2)
        .map(c => c[0])
        .sort()
    ).toEqual(['closedToday', 'lanes']);
  });

  it('does not re-read when nothing was recorded', async () => {
    const { service, launchWizard } = renderWidget();
    await ready();
    await waitFor(() => expect(service.read).toHaveBeenCalledTimes(2));
    await userEvent.click(within(lane('Do')).getAllByTestId('issue-line')[0]);
    await waitFor(() => expect(launchWizard).toHaveBeenCalled());
    expect(service.read).toHaveBeenCalledTimes(2);
  });

  it('says so when no wizard host is mounted, and never falls back to another surface', async () => {
    const launchWizard: Launch = jest.fn(async () => ({ opened: false, reason: 'no-host' as const }));
    renderWidget({ launchWizard });
    await ready();
    await userEvent.click(within(lane('Do')).getAllByTestId('issue-line')[0]);
    expect(await screen.findByTestId('worklist-notice')).toHaveTextContent(
      'The decision wizard is not available in this view.'
    );
  });
});

describe('Closed today, Suppressed and Closed earlier', () => {
  async function open(set: string) {
    const item = screen.getByTestId(`disclosure-${set}`);
    await userEvent.click(within(item).getByRole('button'));
    return item;
  }

  it('all three are closed by default and list their sets when opened', async () => {
    renderWidget();
    await ready();
    const today = await open('closedToday');
    await waitFor(() => expect(within(today).getAllByTestId('issue-line').length).toBe(2));
    expect(today).toHaveTextContent('Closed today (2)');
    // Both lanes together; each line says how it closed, not an age or a due state.
    const timings = within(today)
      .getAllByTestId('issue-timing')
      .map(t => t.textContent);
    expect(timings).toEqual(['Decided', 'Cleared on its own']);
    expect(within(today).getByText('Retainer top-up')).toBeInTheDocument();

    const earlier = await open('closedEarlier');
    await waitFor(() => expect(within(earlier).getAllByTestId('issue-line')).toHaveLength(1));
    expect(within(earlier).getByTestId('issue-timing')).toHaveTextContent(/^Dismissed /);
  });

  it('Suppressed lists its items with the until-date', async () => {
    renderWidget();
    await ready();
    const suppressed = await open('suppressed');
    await waitFor(() => expect(within(suppressed).getAllByTestId('issue-line')).toHaveLength(1));
    const expected = new Date('2026-10-28T12:00:00Z').toLocaleDateString(undefined, { day: 'numeric', month: 'short' });
    expect(within(suppressed).getByTestId('issue-timing')).toHaveTextContent(`Not raised until ${expected}`);
  });

  it('a closed line opens the wizard on that set, read-only', async () => {
    const { launchWizard } = renderWidget();
    await ready();
    const today = await open('closedToday');
    await waitFor(() => expect(within(today).getAllByTestId('issue-line').length).toBe(2));
    await userEvent.click(within(today).getAllByTestId('issue-line')[1]);
    const request = launchWizard.mock.calls[0][0];
    expect(request.itemId).toBe(C2);
    expect(request.visibleList).toEqual([C1, C2]);

    const decided = await request.loadItem!(C1);
    expect(decided.resolution).toMatchObject({ resolutionType: 'Acted', decidedOn: '2026-10-09T09:00:00Z' });
    // The route returns no Decision Record: no steps are claimed, so the wizard has nothing to replay.
    expect(decided.resolution?.steps).toBeUndefined();
    expect(decided.otherOpenItems).toEqual([]);
    const cleared = await request.loadItem!(C2);
    expect(cleared.resolution?.resolutionType).toBe('ConditionCleared');
  });

  it('a Closed today line opens the Closed today copy, even when the lanes still hold a stale open copy of the same Signal', async () => {
    // After a decision a refresh can fail (503 keeps the last list) or still be in flight: the lanes then still hold C1 as open.
    const staleLanes = body(
      'lanes',
      [group(matterCore, [routeItem(C1, 'Decide', { workType: 'askOutsideCounsel' })])],
      []
    );
    const { launchWizard } = renderWidget({ service: fakeService({ ...ROUTE_BODIES, lanes: staleLanes }) });
    await ready();
    const today = await open('closedToday');
    await waitFor(() => expect(within(today).getAllByTestId('issue-line').length).toBe(2));
    await userEvent.click(within(today).getAllByTestId('issue-line')[0]);
    const fromClosed = await launchWizard.mock.calls[0][0].loadItem!(C1);
    expect(fromClosed.resolution).toMatchObject({ resolutionType: 'Acted' });
    expect(fromClosed.row.status).toBe('Resolved');

    // The same Signal launched from the lanes is still the open item ("To decide").
    await userEvent.click(within(lane('Decide')).getAllByTestId('issue-line')[0]);
    const fromLanes = await launchWizard.mock.calls[1][0].loadItem!(C1);
    expect(fromLanes.resolution).toBeNull();
  });

  it('a lane line never opens a closed copy, and a disclosure line never opens an open one', async () => {
    const { launchWizard } = renderWidget();
    await ready();
    await userEvent.click(within(lane('Do')).getAllByTestId('issue-line')[0]);
    const lanes = launchWizard.mock.calls[0][0].loadItem!;
    await expect(lanes(C2)).rejects.toThrow(/no longer on the list/);
    const today = await open('closedToday');
    await waitFor(() => expect(within(today).getAllByTestId('issue-line').length).toBe(2));
    await userEvent.click(within(today).getAllByTestId('issue-line')[0]);
    await expect(launchWizard.mock.calls[1][0].loadItem!(A1)).rejects.toThrow(/no longer on the list/);
  });

  it('a suppressed line opens read-only and the status note carries the until-date', async () => {
    const { launchWizard } = renderWidget();
    await ready();
    const suppressed = await open('suppressed');
    await waitFor(() => expect(within(suppressed).getAllByTestId('issue-line')).toHaveLength(1));
    await userEvent.click(within(suppressed).getByTestId('issue-line'));
    const request = launchWizard.mock.calls[0][0];
    expect(request.visibleList).toEqual([S1]);
    const item = await request.loadItem!(S1);
    expect(item.resolution?.leftOpen).toBe(true);
    expect(item.resolution?.note).toMatch(/^Not raised again until /);
  });

  it('a closed earlier line opens read-only on the Closed earlier list', async () => {
    const { launchWizard } = renderWidget();
    await ready();
    const earlier = await open('closedEarlier');
    await waitFor(() => expect(within(earlier).getAllByTestId('issue-line')).toHaveLength(1));
    await userEvent.click(within(earlier).getByTestId('issue-line'));
    const request = launchWizard.mock.calls[0][0];
    expect(request.visibleList).toEqual([E1]);
    expect((await request.loadItem!(E1)).resolution?.resolutionType).toBe('Dismissed');
  });

  it('carries the stored evidence to the wizard, with a withheld line marked (task 038)', async () => {
    const { launchWizard } = renderWidget();
    await ready();
    const today = await open('closedToday');
    await waitFor(() => expect(within(today).getAllByTestId('issue-line').length).toBe(2));
    await userEvent.click(within(today).getAllByTestId('issue-line')[0]);
    const item = await launchWizard.mock.calls[0][0].loadItem!(C1);
    expect(item.evidence).toEqual([
      {
        kind: 'sprk_budget',
        ref: 'b1',
        tier: 'Fact',
        text: '$12,000 spent',
        source: 'Budget',
        asOf: '2026-10-08T00:00:00Z',
        clause: 'all[0]',
      },
      {
        kind: 'sprk_communication',
        ref: 'c1',
        tier: 'Interpretation',
        text: null,
        asOf: '2026-10-08T00:00:00Z',
        clause: 'all[1]',
        withheld: true,
      },
    ]);
  });

  it('an empty set says so, and a failed set offers Try again', async () => {
    const service = fakeService({
      lanes: ROUTE_BODIES.lanes,
      closedToday: body('closedToday', [], []),
      closedEarlier: ROUTE_BODIES.closedEarlier,
    });
    const real = service.read.getMockImplementation()!;
    let failEarlier = true;
    service.read.mockImplementation(async set => {
      if (set === 'closedEarlier' && failEarlier) throw new Error('The worklist could not be read (503).');
      return real(set);
    });
    renderWidget({ service });
    await ready();
    const today = await open('closedToday');
    expect(await within(today).findByTestId('disclosure-empty-closedToday')).toHaveTextContent(
      'Nothing was closed today.'
    );

    const earlier = await open('closedEarlier');
    expect(await within(earlier).findByText('The worklist could not be read (503).')).toBeInTheDocument();
    failEarlier = false;
    await userEvent.click(within(earlier).getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(within(earlier).getAllByTestId('issue-line')).toHaveLength(1));
  });
});

describe('loading and failure', () => {
  it('shows a spinner while the lanes load, then the lists', async () => {
    renderWidget();
    expect(screen.getByTestId('worklist-loading')).toBeInTheDocument();
    await ready();
    expect(screen.queryByTestId('worklist-loading')).not.toBeInTheDocument();
  });

  it('a failed lanes read says so and Try again reads it again', async () => {
    const service = fakeService();
    const real = service.read.getMockImplementation()!;
    let fail = true;
    service.read.mockImplementation(async set => {
      if (set === 'lanes' && fail) throw new Error('The worklist could not be read. Try again.');
      return real(set);
    });
    renderWidget({ service });
    expect(await screen.findByTestId('worklist-error')).toHaveTextContent('The worklist could not be read. Try again.');
    fail = false;
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));
    await ready();
    expect(lineIds(lane('Decide'))).toEqual([A1, A2, A3]);
  });

  it('a 403 says the worklist is not available for the account, with no Try again and no technical text', async () => {
    const service = fakeService();
    service.read.mockImplementation(async () => {
      throw new WorklistRequestError('The worklist could not be read (403).', 403);
    });
    renderWidget({ service });
    const error = await screen.findByTestId('worklist-error');
    expect(error).toHaveTextContent("Your worklist isn't available for your account. Contact your administrator.");
    expect(error).not.toHaveTextContent('403');
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument();
  });

  it('any other failure keeps its message and Try again (a 503 is worth another try)', async () => {
    const service = fakeService();
    service.read.mockImplementation(async () => {
      throw new WorklistRequestError('The worklist could not be read. Try again.', 503);
    });
    renderWidget({ service });
    expect(await screen.findByTestId('worklist-error')).toHaveTextContent('The worklist could not be read. Try again.');
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });

  it('a 200 body that is not a worklist answer shows the error state, not empty lanes', async () => {
    const service = createWorklistService({
      fetch: async () => ({ ok: true, status: 200, json: async () => ({ unexpected: true }) }) as unknown as Response,
      baseUrl: 'https://bff.test',
    });
    renderWidget({ service });
    expect(await screen.findByTestId('worklist-error')).toHaveTextContent('The worklist answer could not be read.');
    expect(screen.queryByTestId('lane-empty-Do')).not.toBeInTheDocument();
    expect(screen.queryByText('Nothing to do.')).not.toBeInTheDocument();
  });

  it('keeps the last list on screen when a refresh fails', async () => {
    const service = fakeService();
    const real = service.read.getMockImplementation()!;
    const launchWizard: Launch = jest.fn(async request => {
      request.onRecorded?.({ signalId: D5, alsoResolved: [] });
      return { opened: true };
    });
    renderWidget({ service, launchWizard });
    await ready();
    service.read.mockImplementation(async set => {
      if (set === 'lanes') throw new Error('The worklist could not be read (503).');
      return real(set);
    });
    await userEvent.click(within(lane('Do')).getAllByTestId('issue-line')[0]);
    await screen.findByText('The worklist could not be refreshed. This is the last list that was read.');
    expect(lineIds(lane('Do'))).toEqual([D4, D3, D1, D2, D5]);
  });

  it('a filter whose cards empty after a refresh falls back to All', async () => {
    const service = fakeService();
    const real = service.read.getMockImplementation()!;
    const launchWizard: Launch = jest.fn(async request => {
      request.onRecorded?.({ signalId: D3, alsoResolved: [D2] });
      return { opened: true };
    });
    renderWidget({ service, launchWizard });
    await ready();
    await userEvent.click(countCard(lane('Do'), /^Coming due/));
    // After the wizard, the route no longer has the two Coming due items.
    service.read.mockImplementation(async set =>
      set === 'lanes'
        ? real('lanes').then(d => ({
            ...d,
            Do: d.Do.map(g => ({ ...g, items: g.items.filter(i => i.workType !== 'comingDue') })).filter(
              g => g.items.length
            ),
          }))
        : real(set)
    );
    await userEvent.click(within(lane('Do')).getAllByTestId('issue-line')[0]);
    await waitFor(() => expect(lineIds(lane('Do'))).toEqual([D4, D1, D5]));
    expect(within(lane('Do')).queryByTestId('lane-showing-Do')).not.toBeInTheDocument();
  });
});

describe('the route is the only source (D-15)', () => {
  it('createWorklistService reads GET /api/v1/signals/worklist?set=... and nothing else', async () => {
    const fetchSpy = jest.fn(
      async (url: string) => ({ ok: true, status: 200, json: async () => ROUTE_BODIES.lanes }) as unknown as Response
    );
    const service = createWorklistService({ fetch: fetchSpy, baseUrl: 'https://bff.test/' });
    const data = await service.read('lanes');
    expect(fetchSpy).toHaveBeenCalledTimes(1);
    expect(fetchSpy.mock.calls[0][0]).toBe('https://bff.test/api/v1/signals/worklist?set=lanes');
    expect(fetchSpy.mock.calls[0][1]).toMatchObject({ method: 'GET' });
    expect(data.Decide.map(g => g.items.map(i => i.signalId))).toEqual([[A1, A2], [A3]]);
  });

  it('component sources read no sprk_signal, use no Xrm and no browser storage, and name no table', () => {
    const dir = path.join(__dirname, '..');
    const own = fs
      .readdirSync(dir)
      .filter(f =>
        /^(WorklistWidget|WorklistLanePanel|WorklistDisclosures|useWorklistData|worklistService|worklistView|worklistWizardItem)\.tsx?$/.test(
          f
        )
      );
    expect(own).toHaveLength(7);
    for (const f of own) {
      const code = fs
        .readFileSync(path.join(dir, f), 'utf8')
        .split('\n')
        .filter(l => !/^\s*(\*|\/\/|\/\*)/.test(l))
        .join('\n');
      expect({
        f,
        hit: /sprk_\w+|Xrm|WebApi|localStorage|sessionStorage|indexedDB|retrieveMultiple/.test(code),
      }).toEqual({
        f,
        hit: false,
      });
      // The route's order is the list's order: no sort, reverse or comparator anywhere.
      expect({ f, hit: /\.(?:sort|toSorted|reverse|toReversed)\s*\(/.test(code) }).toEqual({ f, hit: false });
      // D-36: no branch on a core type.
      expect({ f, hit: /===\s*['"](?:Matter|Project|matter|project)['"]/.test(code) }).toEqual({ f, hit: false });
    }
  });

  it('component sources contain no hex, rgb or named colour literal (ADR-021)', () => {
    const dir = path.join(__dirname, '..');
    const literal =
      /#[0-9a-fA-F]{3,8}\b|\b(?:rgba?|hsla?)\(|(?:color|background\w*|border\w*|outline\w*)\s*:\s*['"](?:white|black|red|green|blue|gray|grey|orange|yellow|purple|transparent)['"]/i;
    for (const f of fs.readdirSync(dir).filter(n => /^Worklist\w+\.tsx$/.test(n))) {
      const code = fs
        .readFileSync(path.join(dir, f), 'utf8')
        .split('\n')
        .filter(l => !/^\s*(\*|\/\/|\/\*)/.test(l))
        .join('\n');
      expect({ f, hit: literal.test(code) }).toEqual({ f, hit: false });
    }
  });
});

describe('themes (ADR-021)', () => {
  it('renders every part in the dark theme, including the disclosures', async () => {
    renderWidget({ narrative: <span>Words</span>, aggregateRow: <div>8 emails</div> }, true);
    await ready();
    for (const set of ['closedToday', 'suppressed', 'closedEarlier']) {
      await userEvent.click(within(screen.getByTestId(`disclosure-${set}`)).getByRole('button'));
    }
    await waitFor(() => expect(screen.getAllByTestId('issue-line').length).toBeGreaterThan(10));
    expect(screen.getAllByTestId('matter-card').length).toBeGreaterThan(8);
  });
});

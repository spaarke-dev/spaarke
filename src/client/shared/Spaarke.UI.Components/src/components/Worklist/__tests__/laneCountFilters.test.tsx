/**
 * Lane count filters — unit tests (task 052, D-24 / FR-34 / W-7..W-9).
 *
 * The count filters are DATA for the existing WorkspaceShell/MetricCardRow, not a new component, and they are lenses on
 * membership: choosing one narrows the list the host already shows. Stated contract:
 *  - each lane splits by work type so the type cards SUM to the lane total (one work type per item); an item with no
 *    valid work type for its lane goes to "Other" rather than being lost
 *  - the cards are MetricCard instances (role=button, aria-pressed, disabled at 0), not StatTiles
 *  - clicking a card filters the existing list in place; clicking the selected card again returns to All; a selection
 *    whose count falls to 0 resolves back to All
 *  - the All card carries "n of m done today" when the host supplies done-today
 */

import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

import { MetricCardRow } from '../../WorkspaceShell';
import {
  bucketLaneItems,
  buildLaneCountCards,
  filterLaneItems,
  resolveLaneFilterKey,
  type LaneFilterKey,
  type WorklistItem,
  type WorkType,
} from '..';

const TODAY = new Date(2026, 9, 9, 12, 0, 0); // 9 Oct 2026, viewer-local

let n = 0;
function item(
  lane: 'Decide' | 'Do',
  workType: WorkType | null | undefined,
  extra: Partial<WorklistItem> = {}
): WorklistItem {
  n += 1;
  return { signalId: `s${n}`, lane, workType, subjectName: `Subject ${n}`, ...extra };
}

const decideItems = (): WorklistItem[] => [
  item('Decide', 'askOutsideCounsel', { raisedOn: '2026-10-05T10:00:00' }), // 4 days
  item('Decide', 'askOutsideCounsel', { raisedOn: '2026-10-08T10:00:00' }),
  item('Decide', 'approveOrRebudget', { raisedOn: '2026-10-09T08:00:00' }), // today
  item('Decide', 'chaseReply', { raisedOn: '2026-10-07T08:00:00' }),
];

const doItems = (): WorklistItem[] => [
  item('Do', 'finishOrReschedule', { dueDate: '2026-10-06' }), // 3 past due
  item('Do', 'finishOrReschedule', { dueDate: '2026-10-08' }), // 1 past due
  item('Do', 'comingDue', { dueDate: '2026-10-12' }),
  item('Do', 'chaseResponse', { dueDate: '2026-10-09' }),
];

const sumTypeCounts = (items: WorklistItem[], lane: 'Decide' | 'Do') => {
  const buckets = bucketLaneItems(items, lane);
  const all = buckets.find(b => b.key === 'all')!;
  const typed = buckets.filter(b => b.key !== 'all');
  return { all: all.items.length, sum: typed.reduce((s, b) => s + b.items.length, 0) };
};

describe('bucketLaneItems — counts add up', () => {
  it('Decide: the three work-type cards sum to the lane total', () => {
    const items = decideItems();
    const buckets = bucketLaneItems(items, 'Decide');
    expect(buckets.map(b => b.label)).toEqual(['All', 'Ask outside counsel', 'Approve or rebudget', 'Chase a reply']);
    expect(buckets.map(b => b.items.length)).toEqual([4, 2, 1, 1]);
    expect(sumTypeCounts(items, 'Decide')).toEqual({ all: 4, sum: 4 });
  });

  it('Do: the three work-type cards sum to the lane total', () => {
    const items = doItems();
    const buckets = bucketLaneItems(items, 'Do');
    expect(buckets.map(b => b.label)).toEqual(['All', 'Finish or reschedule', 'Coming due', 'Chase a response']);
    expect(sumTypeCounts(items, 'Do')).toEqual({ all: 4, sum: 4 });
  });

  it("ignores the other lane's items", () => {
    const items = [...decideItems(), ...doItems()];
    expect(sumTypeCounts(items, 'Decide')).toEqual({ all: 4, sum: 4 });
    expect(sumTypeCounts(items, 'Do')).toEqual({ all: 4, sum: 4 });
  });

  it('negative: an item with no work type, or a work type of the OTHER lane, is counted in Other, never dropped', () => {
    const items = [...decideItems(), item('Decide', null), item('Decide', undefined), item('Decide', 'comingDue')];
    const buckets = bucketLaneItems(items, 'Decide');
    const other = buckets.find(b => b.key === 'other');
    expect(other?.items).toHaveLength(3);
    expect(sumTypeCounts(items, 'Decide')).toEqual({ all: 7, sum: 7 });
  });

  it('there is no Other card when nothing needs it', () => {
    expect(bucketLaneItems(decideItems(), 'Decide').some(b => b.key === 'other')).toBe(false);
  });

  it('an empty lane has All = 0 and every type = 0', () => {
    expect(bucketLaneItems([], 'Do').map(b => b.items.length)).toEqual([0, 0, 0, 0]);
  });
});

describe('filterLaneItems / resolveLaneFilterKey', () => {
  it('returns the matching subset in the given order; All returns the whole lane', () => {
    const items = decideItems();
    expect(filterLaneItems(items, 'Decide', 'all')).toEqual(items);
    expect(filterLaneItems(items, 'Decide', 'askOutsideCounsel').map(i => i.signalId)).toEqual([
      items[0].signalId,
      items[1].signalId,
    ]);
  });

  it('a selection whose count is 0 resolves back to All (no empty lens to get stuck on)', () => {
    const items = decideItems().filter(i => i.workType !== 'chaseReply');
    expect(resolveLaneFilterKey(items, 'Decide', 'chaseReply')).toBe('all');
    expect(filterLaneItems(items, 'Decide', 'chaseReply')).toHaveLength(3);
  });

  it('a key that belongs to the other lane resolves to All', () => {
    expect(resolveLaneFilterKey(decideItems(), 'Decide', 'comingDue')).toBe('all');
  });
});

describe('buildLaneCountCards', () => {
  const build = (lane: 'Decide' | 'Do', items: WorklistItem[], selectedKey: LaneFilterKey = 'all', extra = {}) => {
    const onSelectKey = jest.fn();
    const cards = buildLaneCountCards({ lane, items, selectedKey, onSelectKey, today: TODAY, ...extra });
    return { cards, onSelectKey };
  };

  it('Decide notes read "oldest N days" from the oldest raised date', () => {
    const { cards } = build('Decide', decideItems());
    expect(cards.map(c => [c.label, c.value, c.note])).toEqual([
      ['All', 4, 'oldest 4 days'],
      ['Ask outside counsel', 2, 'oldest 4 days'],
      ['Approve or rebudget', 1, 'oldest today'],
      ['Chase a reply', 1, 'oldest 2 days'],
    ]);
  });

  it('uses the singular for one day', () => {
    const { cards } = build('Decide', [item('Decide', 'chaseReply', { raisedOn: '2026-10-08T09:00:00' })]);
    expect(cards[0].note).toBe('oldest 1 day');
  });

  it('Do notes read "N past due" and are absent when nothing is past due', () => {
    const { cards } = build('Do', doItems());
    expect(cards.map(c => [c.label, c.value, c.note])).toEqual([
      ['All', 4, '2 past due'],
      ['Finish or reschedule', 2, '2 past due'],
      ['Coming due', 1, undefined],
      ['Chase a response', 1, undefined],
    ]);
  });

  it('marks exactly the resolved selection as selected; type cards disable at 0, All never does', () => {
    const items = decideItems().filter(i => i.workType !== 'chaseReply');
    const { cards } = build('Decide', items, 'chaseReply');
    expect(cards.filter(c => c.selected).map(c => c.label)).toEqual(['All']); // resolved back to All
    const chase = cards.find(c => c.label === 'Chase a reply')!;
    expect(chase.value).toBe(0);
    expect(chase.disableWhenEmpty).toBe(true);
    expect(cards[0].disableWhenEmpty).toBe(false);
  });

  it('clicking a type card selects it; clicking the selected card again returns to All; clicking All stays All', () => {
    const items = decideItems();
    const first = build('Decide', items, 'all');
    first.cards[1].onClick!();
    expect(first.onSelectKey).toHaveBeenLastCalledWith('askOutsideCounsel');
    const second = build('Decide', items, 'askOutsideCounsel');
    second.cards[1].onClick!();
    expect(second.onSelectKey).toHaveBeenLastCalledWith('all');
    second.cards[0].onClick!();
    expect(second.onSelectKey).toHaveBeenLastCalledWith('all');
  });

  it('the All card carries "n of m done today" only when done-today is supplied', () => {
    expect(build('Do', doItems()).cards[0].progress).toBeUndefined();
    const { cards } = build('Do', doItems(), 'all', { doneToday: 3 });
    expect(cards[0].progress).toEqual({ done: 3, total: 7 });
    expect(cards[1].progress).toBeUndefined();
  });

  it('every card has a distinct id and a readable aria-label', () => {
    const { cards } = build('Decide', decideItems());
    expect(new Set(cards.map(c => c.id)).size).toBe(cards.length);
    expect(cards[1].ariaLabel).toBe('Ask outside counsel, 2 Decide items, oldest 4 days');
  });
});

/** A host: the lane's existing list plus its count filters. Choosing a filter narrows THIS list. */
function LaneHost({ lane, items, doneToday }: { lane: 'Decide' | 'Do'; items: WorklistItem[]; doneToday?: number }) {
  const [key, setKey] = React.useState<LaneFilterKey>('all');
  const cards = buildLaneCountCards({ lane, items, selectedKey: key, onSelectKey: setKey, today: TODAY, doneToday });
  const shown = filterLaneItems(items, lane, key);
  return (
    <div>
      <MetricCardRow cards={cards} layout="wide" ariaLabel={`${lane} filters`} />
      <ul data-testid="list" aria-label={`${lane} list`}>
        {shown.map(i => (
          <li key={i.signalId}>{i.subjectName}</li>
        ))}
      </ul>
    </div>
  );
}

describe('count filters are lenses on the existing list (no second queue)', () => {
  const renderHost = (ui: React.ReactElement, dark = false) =>
    render(<FluentProvider theme={dark ? webDarkTheme : webLightTheme}>{ui}</FluentProvider>);

  it('renders MetricCard buttons (role=button, aria-pressed) in a labelled MetricCardRow group, not StatTiles', () => {
    renderHost(<LaneHost lane="Decide" items={decideItems()} />);
    const group = screen.getByRole('group', { name: 'Decide filters' });
    const buttons = within(group).getAllByRole('button');
    expect(buttons).toHaveLength(4);
    buttons.forEach(b => expect(b).toHaveAttribute('aria-pressed'));
    expect(buttons[0]).toHaveAttribute('aria-pressed', 'true');
  });

  it('clicking a count filter narrows the SAME list in place and opens no other list', () => {
    const items = decideItems();
    renderHost(<LaneHost lane="Decide" items={items} />);
    expect(within(screen.getByTestId('list')).getAllByRole('listitem')).toHaveLength(4);
    fireEvent.click(screen.getByRole('button', { name: /^Ask outside counsel/ }));
    expect(screen.getAllByTestId('list')).toHaveLength(1);
    expect(within(screen.getByTestId('list')).getAllByRole('listitem')).toHaveLength(2);
    expect(screen.getByRole('button', { name: /^Ask outside counsel/ })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: /^All/ })).toHaveAttribute('aria-pressed', 'false');
  });

  it('clicking the selected filter again restores the whole list', () => {
    renderHost(<LaneHost lane="Do" items={doItems()} />);
    const finish = () => screen.getByRole('button', { name: /^Finish or reschedule/ });
    fireEvent.click(finish());
    expect(within(screen.getByTestId('list')).getAllByRole('listitem')).toHaveLength(2);
    fireEvent.click(finish());
    expect(within(screen.getByTestId('list')).getAllByRole('listitem')).toHaveLength(4);
    expect(screen.getByRole('button', { name: /^All/ })).toHaveAttribute('aria-pressed', 'true');
  });

  it('a type with no items is disabled and cannot be chosen', () => {
    const items = decideItems().filter(i => i.workType !== 'chaseReply');
    renderHost(<LaneHost lane="Decide" items={items} />);
    const chase = screen.getByRole('button', { name: /^Chase a reply/ });
    expect(chase).toHaveAttribute('aria-disabled', 'true');
    fireEvent.click(chase);
    expect(within(screen.getByTestId('list')).getAllByRole('listitem')).toHaveLength(3);
  });

  it('shows the note, and "n of m done today" on the All card', () => {
    renderHost(<LaneHost lane="Do" items={doItems()} doneToday={3} />);
    expect(screen.getAllByText('2 past due').length).toBeGreaterThan(0);
    expect(screen.getByText('3 of 7 done today')).toBeInTheDocument();
  });

  it('renders in dark mode', () => {
    renderHost(<LaneHost lane="Decide" items={decideItems()} doneToday={1} />, true);
    expect(screen.getByRole('group', { name: 'Decide filters' })).toBeInTheDocument();
  });
});

describe('static guards', () => {
  const dir = path.join(__dirname, '..');
  const src = fs.readFileSync(path.join(dir, 'laneCountFilters.ts'), 'utf8');

  it('the filters are built from MetricCardConfig for the existing row; no new card component, no StatTiles', () => {
    expect(src).toMatch(/MetricCardConfig/);
    expect(src).not.toMatch(/import[^;]*StatTiles/);
    const files = fs.readdirSync(dir).filter(f => /\.tsx$/.test(f));
    // the worklist folder still holds only MatterCard and IssueLine as components
    expect(files.sort()).toEqual(['IssueLine.tsx', 'MatterCard.tsx']);
  });

  it('has no colour literal (the colours come from MetricCard tokens)', () => {
    expect(src).not.toMatch(/#[0-9a-fA-F]{3,8}\b|rgba?\(/);
  });
});

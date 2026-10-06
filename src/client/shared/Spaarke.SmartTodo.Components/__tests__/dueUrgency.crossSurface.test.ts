/**
 * Cross-surface due-date tier parity (spaarke-ontology-platform-r1 task 081 / H1).
 *
 * One item, one tier, everywhere: the SmartTodo badge (`computeDueLabel`), the
 * LegalWorkspace Updates Feed card (`feedDueUrgency`) and the VisualHost event
 * due-date card (`mapEventToCardProps`, whose `urgency` drives
 * `@spaarke/visuals`' `EventDueDateCard`) must all return THE shared
 * `dueUrgencyForDays` tier for the same DateOnly due date. Before task 081 each
 * surface had its own boundaries (feed 3/10 over a UTC parse; event card 3/5).
 *
 * Also pins the owner palette (SmartTodo's badges, 2026-10-05) on the two
 * non-SmartTodo surfaces: overdue red · 3d dark orange · 7d yellow · 10d and
 * none neutral.
 *
 * Pinned to a negative-UTC-offset zone at 9pm local, where a UTC-midnight
 * parse or an elapsed-hours day count is off by one.
 */

const ORIGINAL_TZ = process.env.TZ;
process.env.TZ = 'America/New_York';

jest.mock('@spaarke/ui-components', () => ({
  ...jest.requireActual('../../Spaarke.UI.Components/src/utils/dateLocal'),
}));

import { tokens } from '@fluentui/react-components';
import { dueUrgencyForDays, parseDueDate } from '@spaarke/ui-components';
import { computeDueLabel } from '../src/utils/todoScoring';
import {
  FEED_DUE_ACCENT,
  feedDueUrgency,
  isFeedEventOverdue,
} from '../../../../solutions/LegalWorkspace/src/components/ActivityFeed/feedDueAccent';
import { computeCategoryCounts } from '../../../../solutions/LegalWorkspace/src/hooks/useActivityFeedFilters';
import { EventFilterCategory } from '../../../../solutions/LegalWorkspace/src/types/enums';
import { mapEventToCardProps } from '../../../pcf/VisualHost/control/utils/eventDueDate';
import {
  EVENT_DUE_BADGE_COLOR,
  EVENT_DUE_DATE_COLUMN_BACKGROUND,
} from '../../Spaarke.Visuals/src/components/EventDueDateCard';

afterAll(() => {
  if (ORIGINAL_TZ === undefined) {
    delete process.env.TZ;
  } else {
    process.env.TZ = ORIGINAL_TZ;
  }
});

const NOW = new Date(2026, 9, 5, 21, 0, 0);

function dateOnly(daysFromNow: number): string {
  const d = new Date(2026, 9, 5 + daysFromNow);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

describe('due-date tier is the same on every surface (America/New_York, 9pm)', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    jest.setSystemTime(NOW);
  });
  afterEach(() => {
    jest.useRealTimers();
  });

  // Guard (review F11): if the TZ override were not in effect (e.g. a UTC CI
  // runner where local == UTC), a UTC-midnight parse would pass these cases
  // trivially. A date-only string must parse to 8pm the PREVIOUS day here.
  it('the zone override is in effect', () => {
    expect(new Date('2026-10-05').getDate()).toBe(4);
    expect(new Date('2026-10-05').getHours()).toBe(20);
  });

  it.each([
    [-1, 'overdue'],
    [0, '3d'],
    [3, '3d'],
    [4, '7d'],
    [7, '7d'],
    [8, '10d'],
    [10, '10d'],
    [11, 'none'],
  ] as const)('due in %i day(s): every surface says %s', (days, expected) => {
    const due = dateOnly(days);
    const shared = dueUrgencyForDays(days);
    const smartTodo = computeDueLabel(parseDueDate(due)).urgency;
    const feed = feedDueUrgency(due, NOW);
    const eventCard = mapEventToCardProps({ sprk_eventid: 'e1', sprk_duedate: due }, NOW).urgency;

    expect({ shared, smartTodo, feed, eventCard }).toEqual({
      shared: expected,
      smartTodo: expected,
      feed: expected,
      eventCard: expected,
    });
  });
});

describe('owner palette (SmartTodo badges) on the feed and event card', () => {
  it('feed accent: red / dark orange / yellow / neutral', () => {
    expect(FEED_DUE_ACCENT).toEqual({
      overdue: tokens.colorPaletteRedBorder2,
      '3d': tokens.colorPaletteDarkOrangeBorder2,
      '7d': tokens.colorPaletteYellowBorder2,
      '10d': tokens.colorNeutralStroke2,
      none: tokens.colorNeutralStroke2,
    });
  });

  it('event card badge + date column: red / dark orange / yellow / neutral (no green)', () => {
    expect(EVENT_DUE_BADGE_COLOR).toEqual({
      overdue: 'danger',
      '3d': 'severe',
      '7d': 'warning',
      '10d': 'informative',
      none: 'informative',
    });
    expect(EVENT_DUE_DATE_COLUMN_BACKGROUND).toEqual({
      overdue: tokens.colorPaletteRedBackground2,
      '3d': tokens.colorPaletteDarkOrangeBackground2,
      '7d': tokens.colorPaletteYellowBackground2,
      '10d': tokens.colorNeutralBackground3,
      none: tokens.colorNeutralBackground3,
    });
  });
});

describe('LegalWorkspace feed: Overdue filter, Overdue badge and card accent agree (review F5)', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    jest.setSystemTime(NOW);
  });
  afterEach(() => {
    jest.useRealTimers();
  });

  const event = (id: string, daysFromNow: number) =>
    ({ sprk_eventid: id, sprk_duedate: dateOnly(daysFromNow) }) as unknown as Parameters<
      typeof computeCategoryCounts
    >[0][number];

  it('a DateOnly due date of TODAY is not overdue anywhere (the badge and filter read it as UTC midnight before)', () => {
    expect(isFeedEventOverdue(dateOnly(0), NOW)).toBe(false);
    expect(feedDueUrgency(dateOnly(0), NOW)).toBe('3d');
    expect(computeCategoryCounts([event('t', 0)])[EventFilterCategory.Overdue]).toBe(0);
  });

  it('yesterday is overdue on all three', () => {
    expect(isFeedEventOverdue(dateOnly(-1), NOW)).toBe(true);
    expect(feedDueUrgency(dateOnly(-1), NOW)).toBe('overdue');
    expect(computeCategoryCounts([event('y', -1)])[EventFilterCategory.Overdue]).toBe(1);
  });

  it('badge count equals the number of cards with the overdue tier', () => {
    const events = [-3, -1, 0, 1, 5].map((d, i) => event(`e${i}`, d));
    const redCards = events.filter(e => feedDueUrgency(e.sprk_duedate as string, NOW) === 'overdue').length;
    expect(computeCategoryCounts(events)[EventFilterCategory.Overdue]).toBe(redCards);
    expect(redCards).toBe(2);
  });
});

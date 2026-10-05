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
} from '../../../../solutions/LegalWorkspace/src/components/ActivityFeed/feedDueAccent';
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

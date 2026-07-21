/**
 * CommunicationsWorkspaceWidget pure-helper unit tests (messaging-communication-app-r2 task 030).
 *
 * Verifies the two pure helpers that back the widget's filter-chip toolbar +
 * card strip, without needing a React/DOM test environment:
 *   1. `aggregateChannelCounts` — per-channel count aggregation from the
 *      DataGrid's `onRecordsLoaded` payload (feeds the card strip).
 *   2. `buildCommunicationsHostFilters` — applied channel + date-range state
 *      → `HostFilterCondition[]` (feeds `<DataGrid hostFilters>`), mirroring
 *      `CalendarWorkspaceWidget`'s `hostFilters` useMemo logic (single-day →
 *      `on`, range → `between`, one-sided → `on-or-after`/`on-or-before`).
 *
 * Test runner: standard `describe`/`it`/`expect` globals (jest or vitest
 * compatible). `@spaarke/communication-components` does not yet have a test
 * runner configured (mirrors `@spaarke/events-components`'
 * `CalendarFilterPane.test.ts` precedent — checked in for the future test
 * runner setup; live UI verification for this task is deferred per the
 * project's established build-and-defer-live pattern, MCP/Dataverse
 * connectivity unavailable in this environment).
 *
 * @see CommunicationsWorkspaceWidget.tsx
 * @see projects/messaging-communication-app-r2/tasks/030-workspace-widget-upgrade.poml
 */

import {
  aggregateChannelCounts,
  buildCommunicationsHostFilters,
  CommunicationChannel,
} from './CommunicationsWorkspaceWidget';

describe('aggregateChannelCounts', () => {
  it('counts records per sprk_communicationtype value', () => {
    const records = [
      { sprk_communicationtype: CommunicationChannel.Email },
      { sprk_communicationtype: CommunicationChannel.Email },
      { sprk_communicationtype: CommunicationChannel.TeamsMessage },
      { sprk_communicationtype: CommunicationChannel.SMS },
    ];
    const counts = aggregateChannelCounts(records);
    expect(counts.get(CommunicationChannel.Email)).toBe(2);
    expect(counts.get(CommunicationChannel.TeamsMessage)).toBe(1);
    expect(counts.get(CommunicationChannel.SMS)).toBe(1);
    expect(counts.get(CommunicationChannel.Notification)).toBeUndefined();
  });

  it('ignores records with a missing or non-numeric channel value', () => {
    const records = [{ sprk_communicationtype: null }, { sprk_communicationtype: undefined }, { other: 'field' }];
    const counts = aggregateChannelCounts(records);
    expect(counts.size).toBe(0);
  });

  it('returns an empty map for an empty records array', () => {
    expect(aggregateChannelCounts([]).size).toBe(0);
  });
});

describe('buildCommunicationsHostFilters', () => {
  it('returns no conditions when nothing is applied', () => {
    expect(buildCommunicationsHostFilters(new Set(), '', '')).toEqual([]);
  });

  it('emits an "in" condition for selected channels', () => {
    const conditions = buildCommunicationsHostFilters(
      new Set([CommunicationChannel.Email, CommunicationChannel.SMS]),
      '',
      ''
    );
    expect(conditions).toEqual([
      {
        attribute: 'sprk_communicationtype',
        operator: 'in',
        value: [CommunicationChannel.Email, CommunicationChannel.SMS],
      },
    ]);
  });

  it('emits an "on" condition when from === to (single day)', () => {
    const conditions = buildCommunicationsHostFilters(new Set(), '2026-07-01', '2026-07-01');
    expect(conditions).toEqual([{ attribute: 'sprk_sentat', operator: 'on', value: '2026-07-01' }]);
  });

  it('emits a "between" condition for a genuine range', () => {
    const conditions = buildCommunicationsHostFilters(new Set(), '2026-07-01', '2026-07-10');
    expect(conditions).toEqual([
      { attribute: 'sprk_sentat', operator: 'between', value: ['2026-07-01', '2026-07-10'] },
    ]);
  });

  it('emits "on-or-after" for a from-only range', () => {
    const conditions = buildCommunicationsHostFilters(new Set(), '2026-07-01', '');
    expect(conditions).toEqual([{ attribute: 'sprk_sentat', operator: 'on-or-after', value: '2026-07-01' }]);
  });

  it('emits "on-or-before" for a to-only range', () => {
    const conditions = buildCommunicationsHostFilters(new Set(), '', '2026-07-10');
    expect(conditions).toEqual([{ attribute: 'sprk_sentat', operator: 'on-or-before', value: '2026-07-10' }]);
  });

  it('composes channel + date conditions together', () => {
    const conditions = buildCommunicationsHostFilters(
      new Set([CommunicationChannel.Email]),
      '2026-07-01',
      '2026-07-10'
    );
    expect(conditions).toEqual([
      { attribute: 'sprk_communicationtype', operator: 'in', value: [CommunicationChannel.Email] },
      { attribute: 'sprk_sentat', operator: 'between', value: ['2026-07-01', '2026-07-10'] },
    ]);
  });
});

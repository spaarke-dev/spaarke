/**
 * The worklist's route client and the item reader it gives the wizard (task 059): the shape of the Signal read route's answer
 * (task 038), the two ways a failure arrives, and what is shown as missing rather than invented.
 */

import { createWorklistService, parseWorklistResponse } from '..';
import { A1, A2, ID, ROUTE_BODIES, body, group, matterCore, routeItem } from '../__fixtures__/worklistRouteFixtures';
import { buildLaneView, hiddenItemsNote } from '../worklistView';
import {
  buildWizardItem,
  closedLineText,
  formatShortDate,
  parseEvidenceRefs,
  resolutionFor,
} from '../worklistWizardItem';
import type { WorklistGroupData, WorklistRouteItem } from '../worklistService';

const TODAY = new Date(2026, 9, 9, 10, 30);
const ok = (json: unknown) => ({ ok: true, status: 200, json: async () => json }) as unknown as Response;
const refused = (status: number, json: unknown) =>
  ({ ok: false, status, json: async () => json }) as unknown as Response;

describe('parseWorklistResponse', () => {
  it('reads both lanes, the groups and the items in the order the route gave them', () => {
    const data = parseWorklistResponse(ROUTE_BODIES.lanes, 'lanes');
    expect(data.set).toBe('lanes');
    expect(data.Decide.map(g => g.items.map(i => i.signalId))).toEqual([
      [A1, A2],
      ['10000000-0000-0000-0000-000000000003'],
    ]);
    expect(data.Do.map(g => g.core?.recordType ?? 'not-filed')).toEqual([
      'sprk_workassignment',
      'sprk_project',
      'sprk_matter',
      'not-filed',
    ]);
    expect(data.Decide[0].core).toEqual({
      recordType: 'sprk_matter',
      recordId: ID.matter,
      typeLabel: 'Matter',
      name: 'Acme v. Beta',
      number: 'MTR-00042',
    });
  });

  it('maps the route spellings: work type, past-due wording and the resolution option value', () => {
    const data = parseWorklistResponse(
      body(
        'closedToday',
        [],
        [
          group(matterCore, [
            routeItem('a', 'Do', { workType: 'chaseResponse', pastDueWording: 'late', resolutionType: 100000004 }),
            routeItem('b', 'Do', { workType: 'bogus', resolutionType: 100000000 }),
            routeItem('c', 'Do', { resolutionType: 7 }),
          ]),
        ]
      ),
      'closedToday'
    );
    const [a, b, c] = data.Do[0].items;
    expect(a).toMatchObject({ workType: 'chaseResponse', pastDueWording: 'late', resolutionType: 'PolicyRetired' });
    // An unknown work type is not guessed at (the lane filters put it under Other), nor is an unknown resolution.
    expect(b).toMatchObject({ workType: null, pastDueWording: 'overdue', resolutionType: 'Acted' });
    expect(c.resolutionType).toBeNull();
  });

  it('a Not filed group has no core, whatever its core field says; a group with no items is dropped', () => {
    const data = parseWorklistResponse(
      body(
        'lanes',
        [],
        [
          { core: matterCore, notFiled: true, items: [routeItem('a', 'Do')] },
          group(matterCore, []),
          group({ recordType: 'sprk_matter' }, [routeItem('b', 'Do')]),
        ]
      ),
      'lanes'
    );
    expect(data.Do.map(g => g.core)).toEqual([null, null]);
    expect(data.Do.map(g => g.items[0].signalId)).toEqual(['a', 'b']);
  });

  it('a body that is not a worklist answer throws, never an empty list (no false "Nothing to do.")', () => {
    const lanesOnly = (lane: 'Decide' | 'Do') => ({ lanes: [{ lane, groups: [] }] });
    for (const bad of [
      null,
      'x',
      [],
      {},
      { lanes: 'x' },
      { lanes: [null, { lane: 'Other', groups: [] }] },
      lanesOnly('Decide'),
      lanesOnly('Do'),
    ]) {
      expect(() => parseWorklistResponse(bad, 'lanes')).toThrow('The worklist answer could not be read.');
    }
    expect.assertions(8 + 1);
    try {
      parseWorklistResponse({}, 'lanes', 200);
    } catch (err) {
      expect(err).toMatchObject({ name: 'WorklistRequestError', httpStatus: 200 });
    }
  });

  it('inside a readable answer, an unreadable item is skipped, never made up', () => {
    const data = parseWorklistResponse(
      body('lanes', [group(matterCore, [{ subjectName: 'no id' }, routeItem('ok', 'Decide')])], []),
      'lanes'
    );
    expect(data.Decide[0].items.map(i => i.signalId)).toEqual(['ok']);
    expect(data.Do).toEqual([]);
  });
});

describe('createWorklistService failures', () => {
  const apiError = (status: number, detail?: string) =>
    Object.assign(new Error(`HTTP ${status}`), {
      name: 'ApiError',
      status,
      problemDetails: detail ? { detail } : undefined,
    });

  it('a returned 503 carries the route problem detail and the status', async () => {
    const service = createWorklistService({
      fetch: async () => refused(503, { detail: 'The worklist could not be read. Try again.' }),
      baseUrl: 'https://bff.test',
    });
    await expect(service.read('lanes')).rejects.toMatchObject({
      name: 'WorklistRequestError',
      httpStatus: 503,
      message: 'The worklist could not be read. Try again.',
    });
  });

  it('a returned failure with no readable body still says what failed', async () => {
    const service = createWorklistService({
      fetch: async () =>
        ({ ok: false, status: 403, json: async () => Promise.reject(new Error('no body')) }) as unknown as Response,
      baseUrl: 'https://bff.test',
    });
    await expect(service.read('suppressed')).rejects.toMatchObject({
      httpStatus: 403,
      message: 'The worklist could not be read (403).',
    });
  });

  it('a thrown ApiError (the authenticated fetch) and a thrown network error both become one error', async () => {
    const thrown = createWorklistService({
      fetch: async () => {
        throw apiError(403, 'Forbidden');
      },
      baseUrl: 'https://bff.test',
    });
    await expect(thrown.read('lanes')).rejects.toMatchObject({ httpStatus: 403 });
    const offline = createWorklistService({
      fetch: async () => {
        throw new TypeError('Failed to fetch');
      },
      baseUrl: 'https://bff.test',
    });
    await expect(offline.read('lanes')).rejects.toMatchObject({
      httpStatus: null,
      message: 'The worklist could not be read.',
    });
  });

  it('a 200 whose body is not JSON is an error, not an empty list', async () => {
    const service = createWorklistService({
      fetch: async () =>
        ({ ok: true, status: 200, json: async () => Promise.reject(new Error('bad json')) }) as unknown as Response,
      baseUrl: 'https://bff.test',
    });
    await expect(service.read('lanes')).rejects.toMatchObject({ message: 'The worklist answer could not be read.' });
  });

  it('a 200 whose body is not a worklist answer is an error, not an empty list', async () => {
    const service = createWorklistService({ fetch: async () => ok({ unexpected: true }), baseUrl: 'https://bff.test' });
    await expect(service.read('lanes')).rejects.toMatchObject({
      name: 'WorklistRequestError',
      httpStatus: 200,
      message: 'The worklist answer could not be read.',
    });
  });

  it('asks for each set by name and trims the base url', async () => {
    const urls: string[] = [];
    const service = createWorklistService({
      fetch: async url => {
        urls.push(url);
        return ok(ROUTE_BODIES.lanes);
      },
      baseUrl: 'https://bff.test///',
    });
    for (const set of ['lanes', 'closedToday', 'suppressed', 'closedEarlier'] as const) await service.read(set);
    expect(urls).toEqual([
      'https://bff.test/api/v1/signals/worklist?set=lanes',
      'https://bff.test/api/v1/signals/worklist?set=closedToday',
      'https://bff.test/api/v1/signals/worklist?set=suppressed',
      'https://bff.test/api/v1/signals/worklist?set=closedEarlier',
    ]);
  });
});

describe('parseEvidenceRefs', () => {
  it('maps the stored lines and keeps the withheld flag the route sets', () => {
    expect(
      parseEvidenceRefs(
        JSON.stringify([
          {
            kind: 'k',
            ref: 'r',
            tier: 'Observation',
            text: 'T',
            source: 'S',
            asOf: '2026-10-01',
            clause: 'all[0]',
            extra: 1,
          },
          { kind: 'k2', ref: 'r2', tier: 'Fact', asOf: '2026-10-02', withheld: true },
          { tier: 'Nonsense', text: '' },
        ])
      )
    ).toEqual([
      { kind: 'k', ref: 'r', tier: 'Observation', text: 'T', source: 'S', asOf: '2026-10-01', clause: 'all[0]' },
      { kind: 'k2', ref: 'r2', tier: 'Fact', text: null, asOf: '2026-10-02', withheld: true },
      { kind: '', ref: '', tier: 'Missing', text: null },
    ]);
  });

  it('nothing stored, not JSON, not an array, or an array of non-objects: no lines', () => {
    for (const raw of [null, undefined, '', '{not json', '{"a":1}', '"x"', '[1,"x",null]']) {
      expect(parseEvidenceRefs(raw)).toEqual([]);
    }
  });
});

describe('closed and suppressed text', () => {
  const item = (extra: Partial<WorklistRouteItem>): WorklistRouteItem => ({
    signalId: 'x',
    lane: 'Do',
    ...extra,
  });

  it('names how a closed item ended; Closed earlier adds the date', () => {
    expect(closedLineText(item({ resolutionType: 'Acted' }), 'closedToday', TODAY)).toBe('Decided');
    expect(closedLineText(item({ resolutionType: 'Dismissed' }), 'closedToday', TODAY)).toBe('Dismissed');
    expect(closedLineText(item({ resolutionType: 'ConditionCleared' }), 'closedToday', TODAY)).toBe(
      'Cleared on its own'
    );
    expect(closedLineText(item({ resolutionType: 'Superseded' }), 'closedToday', TODAY)).toBe('Superseded');
    expect(closedLineText(item({ resolutionType: 'PolicyRetired' }), 'closedToday', TODAY)).toBe(
      'Closed - rule retired'
    );
    const day = new Date('2026-10-02T12:00:00Z').toLocaleDateString(undefined, { day: 'numeric', month: 'short' });
    expect(
      closedLineText(item({ resolutionType: 'Dismissed', resolvedOn: '2026-10-02T12:00:00Z' }), 'closedEarlier', TODAY)
    ).toBe(`Dismissed ${day}`);
  });

  it('never blank: a closed item with no resolution type, or a suppressed one with no date, still says something', () => {
    expect(closedLineText(item({}), 'closedToday', TODAY)).toBe('Closed');
    expect(closedLineText(item({}), 'closedEarlier', TODAY)).toBe('Closed');
    expect(closedLineText(item({}), 'suppressed', TODAY)).toBe('Suppressed');
    const until = new Date('2026-10-28T12:00:00Z').toLocaleDateString(undefined, { day: 'numeric', month: 'short' });
    expect(closedLineText(item({ suppressedUntil: '2026-10-28T12:00:00Z' }), 'suppressed', TODAY)).toBe(
      `Not raised until ${until}`
    );
  });

  it('adds the year only when it is not the current one', () => {
    expect(formatShortDate('2026-10-28T12:00:00Z', TODAY)).not.toMatch(/2026/);
    expect(formatShortDate('2027-01-15T12:00:00Z', TODAY)).toMatch(/2027/);
    expect(formatShortDate(null, TODAY)).toBeNull();
    expect(formatShortDate('not a date', TODAY)).toBeNull();
  });
});

describe('what the wizard is given', () => {
  const g: WorklistGroupData = {
    core: { recordType: 'sprk_matter', recordId: ID.matter },
    items: [
      { signalId: 'a', lane: 'Decide' },
      { signalId: 'b', lane: 'Decide' },
    ],
  };

  it('an open item: no resolution, its siblings as the other open items, no sentence invented', () => {
    const w = buildWizardItem({ item: g.items[0], group: g, set: 'lanes', today: TODAY });
    expect(w.resolution).toBeNull();
    expect(w.otherOpenItems.map(i => i.signalId)).toEqual(['b']);
    expect(w.sentence).toBeNull();
    expect(w.core).toBe(g.core);
  });

  it('a closed item lists no other open items (they are not open) and carries no record it was never given', () => {
    const closed: WorklistRouteItem = {
      signalId: 'a',
      lane: 'Decide',
      resolutionType: 'Dismissed',
      resolvedOn: '2026-10-09T09:00:00Z',
    };
    const w = buildWizardItem({
      item: closed,
      group: { ...g, items: [closed, g.items[1]] },
      set: 'closedToday',
      today: TODAY,
    });
    expect(w.otherOpenItems).toEqual([]);
    expect(w.resolution).toEqual({ resolutionType: 'Dismissed', decidedOn: '2026-10-09T09:00:00Z' });
    expect(w.resolution?.steps).toBeUndefined();
    expect(w.resolution?.recordNumber).toBeUndefined();
  });

  it('a closed item with no recorded type is shown as open-ended, not given a made-up ending', () => {
    expect(resolutionFor({ signalId: 'a', lane: 'Do' }, 'closedEarlier', TODAY)).toMatchObject({
      leftOpen: true,
      note: 'Closed - how it closed was not recorded',
    });
  });
});

describe('buildLaneView', () => {
  const groups: WorklistGroupData[] = [
    {
      core: { recordType: 't', recordId: '1' },
      items: [
        { signalId: 'z', lane: 'Do', workType: 'comingDue' },
        { signalId: 'y', lane: 'Do', workType: 'chaseResponse' },
      ],
    },
    { core: null, items: [{ signalId: 'x', lane: 'Do', workType: 'comingDue' }] },
  ];

  it('keeps the input order whatever the ids sort like, and counts what a filter hides per group', () => {
    const all = buildLaneView(groups, 'Do', 'all');
    expect(all.visibleIds).toEqual(['z', 'y', 'x']);
    const filtered = buildLaneView(groups, 'Do', 'comingDue');
    expect(filtered.visibleIds).toEqual(['z', 'x']);
    expect(filtered.groups.map(g => [g.key, g.hiddenCount])).toEqual([
      ['t:1', 1],
      ['not-filed', 0],
    ]);
  });

  it('a filter that holds nothing resolves to All', () => {
    expect(buildLaneView(groups, 'Do', 'finishOrReschedule').filterKey).toBe('all');
  });

  it('writes the honesty line from the data (type word, singular and plural)', () => {
    expect(hiddenItemsNote(0, 'Matter', false)).toBeUndefined();
    expect(hiddenItemsNote(1, 'Matter', false)).toBe('+1 other open item on this matter, not in this filter');
    expect(hiddenItemsNote(2, 'Work assignment', false)).toBe(
      '+2 other open items on this work assignment, not in this filter'
    );
    expect(hiddenItemsNote(1, null, false)).toBe('+1 other open item on this record, not in this filter');
    expect(hiddenItemsNote(3, null, true)).toBe('+3 other open items in Not filed, not in this filter');
  });
});

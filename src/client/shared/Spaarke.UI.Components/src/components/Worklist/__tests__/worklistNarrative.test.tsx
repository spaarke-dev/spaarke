/**
 * worklistNarrative - unit tests (task 062; FR-31, FR-34; W-10 as amended). The stated contract:
 *  - the narrative request carries the list's COUNTS and nothing else: no record name, no date, no item. Do volume is summarised,
 *    never listed, and Know is not counted at all (row-contract requirement 4)
 *  - an answer whose entity-name scrub removed a term is dropped whole; so is one with no summary; a legacy `topAction`, `itemRefs`
 *    and channel narratives are ignored (no model-chosen top action)
 *  - the widget shows the Assistant's narrative, tagged as written by it, while no filter is on and hides it under ANY filter
 *    (a filter does not call the model)
 *  - negative: the narrative changes neither the membership nor the order of either lane
 *  - a failed or absent narrate is no narrative, never an error
 *
 * Classification (ADR-038 section 7): MAINTAIN - the narrative's contract (the model never influences order or names an item).
 */

import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { WorklistWidget, createWorklistService } from '..';
import type { WorklistData, WorklistService } from '..';
import {
  DECIDE_CATEGORY,
  DO_CATEGORY,
  DO_PAST_DUE_CATEGORY,
  acceptNarrative,
  buildWorklistNarrativeRequest,
  clearNarrativeCache,
} from '../worklistNarrative';
import type { WorklistNarrativeRequest, WorklistNarrativeText } from '../worklistNarrative';
import { ROUTE_BODIES, fakeService } from '../__fixtures__/worklistRouteFixtures';
import { parseWorklistResponse } from '../worklistService';

/** The viewer's local calendar day every test renders against. */
const TODAY = new Date(2026, 9, 9, 10, 30);

const lanes = (): WorklistData => parseWorklistResponse(ROUTE_BODIES.lanes, 'lanes');
const allItems = (data: WorklistData) => [...data.Decide, ...data.Do].flatMap(g => g.items);

const TEXT: WorklistNarrativeText = {
  summary: 'There are 3 items to decide and 5 items to do.',
  keyTakeaways: ['4 of the items to do are past due.'],
};

type NarrateMock = jest.Mock<Promise<WorklistNarrativeText | null>, [WorklistNarrativeRequest]>;

function serviceWithNarrate(text: WorklistNarrativeText | null = TEXT): WorklistService & { narrate: NarrateMock } {
  return { ...fakeService(), narrate: jest.fn(async (_r: WorklistNarrativeRequest) => text) };
}

function renderWidget(service: WorklistService, props: Partial<React.ComponentProps<typeof WorklistWidget>> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <WorklistWidget
        service={service}
        today={TODAY}
        launchWizard={jest.fn(async () => ({ opened: true }))}
        {...props}
      />
    </FluentProvider>
  );
}

const lane = (name: 'Decide' | 'Do') => screen.getByTestId(`worklist-lane-${name}`);
const lineIds = (el: HTMLElement) =>
  within(el)
    .getAllByTestId('issue-line')
    .map(l => l.getAttribute('data-signal-id'));
const orderOf = () => ({ decide: lineIds(lane('Decide')), do: lineIds(lane('Do')) });

beforeEach(() => clearNarrativeCache());

describe('buildWorklistNarrativeRequest', () => {
  it("carries the lanes' counts and the past-due count, and nothing else", () => {
    const data = lanes();
    const request = buildWorklistNarrativeRequest(data, TODAY);
    expect(request).not.toBeNull();

    const decide = data.Decide.reduce((n, g) => n + g.items.length, 0);
    const doItems = data.Do.flatMap(g => g.items);
    const pastDue = doItems.filter(i => i.dueDate && i.dueDate < '2026-10-09').length;
    expect(pastDue).toBeGreaterThan(0);

    expect(request!.categories).toEqual([
      { name: DECIDE_CATEGORY, count: decide, unreadCount: decide },
      { name: DO_CATEGORY, count: doItems.length, unreadCount: doItems.length },
      { name: DO_PAST_DUE_CATEGORY, count: pastDue, unreadCount: pastDue },
    ]);
    expect(request!.totalNotificationCount).toBe(decide + doItems.length);
  });

  it('names no item: no priority item, no channel, no date and no record name, so the narrative cannot name one absent from the list', () => {
    const data = lanes();
    const request = buildWorklistNarrativeRequest(data, TODAY)!;

    expect(request.priorityItems).toEqual([]);
    expect(request.channels).toEqual([]);
    expect(request.tldrFacts.keyDates).toEqual([]);
    expect(request.tldrFacts.recordNames).toEqual([]);

    const wire = JSON.stringify(request);
    for (const item of allItems(data)) {
      if (item.subjectName) expect(wire).not.toContain(item.subjectName);
      expect(wire).not.toContain(item.signalId);
    }
    for (const group of [...data.Decide, ...data.Do]) {
      if (group.core?.name) expect(wire).not.toContain(group.core.name);
    }
  });

  it('is null for no list and for an empty list (nothing to say, so no call)', () => {
    expect(buildWorklistNarrativeRequest(undefined, TODAY)).toBeNull();
    expect(buildWorklistNarrativeRequest({ set: 'lanes', Decide: [], Do: [] }, TODAY)).toBeNull();
  });

  it('leaves out the past-due category when nothing is past due', () => {
    const data = lanes();
    for (const g of data.Do) for (const i of g.items) i.dueDate = '2026-10-30';
    const names = buildWorklistNarrativeRequest(data, TODAY)!.categories.map(c => c.name);
    expect(names).toEqual([DECIDE_CATEGORY, DO_CATEGORY]);
  });
});

describe('acceptNarrative', () => {
  const answer = (over: Record<string, unknown> = {}) => ({
    tldr: { summary: ' A summary. ', keyTakeaways: ['One.', '  ', 7, 'Two.'] },
    channelNarratives: [{ category: 'matters', bullets: [{ narrative: 'Some matter', itemIds: ['x'] }] }],
    ...over,
  });

  it('takes the trimmed summary and the string takeaways', () => {
    expect(acceptNarrative(answer())).toEqual({ summary: 'A summary.', keyTakeaways: ['One.', 'Two.'] });
  });

  it('fails closed when the entity-name scrub removed a term (the model named something it was not given)', () => {
    expect(
      acceptNarrative(answer({ _validationMetadata: { scrubbedText: 'x', removedTerms: ['Globex'] } }))
    ).toBeNull();
  });

  it('accepts an answer whose scrub removed nothing', () => {
    expect(acceptNarrative(answer({ _validationMetadata: { scrubbedText: 'x', removedTerms: [] } }))).not.toBeNull();
  });

  it('drops an answer with no summary, and anything that is not an object', () => {
    expect(acceptNarrative(answer({ tldr: { summary: '  ', keyTakeaways: ['One.'] } }))).toBeNull();
    expect(acceptNarrative(null)).toBeNull();
    expect(acceptNarrative('text')).toBeNull();
    expect(acceptNarrative({})).toBeNull();
  });

  it('ignores a legacy topAction, itemRefs and channel narratives: only the summary and takeaways come through', () => {
    const accepted = acceptNarrative(
      answer({
        tldr: {
          summary: 'S.',
          keyTakeaways: [],
          topAction: 'Do the invoice first',
          itemRefs: [{ anchorText: 'A', itemId: 'x' }],
        },
      })
    );
    expect(accepted).toEqual({ summary: 'S.', keyTakeaways: [] });
    expect(JSON.stringify(accepted)).not.toMatch(/topAction|first|itemRefs|Some matter/);
  });
});

describe('createWorklistService.narrate', () => {
  const request = buildWorklistNarrativeRequest(lanes(), TODAY)!;
  const service = (fetch: jest.Mock) => createWorklistService({ fetch, baseUrl: 'https://bff.test/' });

  it('POSTs the counts to the narrate route and returns the accepted text', async () => {
    const fetch = jest.fn(async () => ({
      ok: true,
      status: 200,
      json: async () => ({ tldr: { summary: 'S.', keyTakeaways: ['K.'] } }),
    }));
    const text = await service(fetch).narrate!(request);

    expect(text).toEqual({ summary: 'S.', keyTakeaways: ['K.'] });
    const [address, init] = fetch.mock.calls[0] as unknown as [string, RequestInit];
    expect(address).toBe('https://bff.test/api/ai/daily-briefing/narrate');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual(request);
  });

  it('is null (never a throw) for a non-2xx answer, a thrown fetch, an unreadable body and a scrubbed answer', async () => {
    const failed = jest.fn(async () => ({ ok: false, status: 500, json: async () => ({}) }));
    const thrown = jest.fn(async () => {
      throw new Error('offline');
    });
    const unreadable = jest.fn(async () => ({
      ok: true,
      status: 200,
      json: async () => {
        throw new Error('not json');
      },
    }));
    const scrubbed = jest.fn(async () => ({
      ok: true,
      status: 200,
      json: async () => ({
        tldr: { summary: 'S.', keyTakeaways: [] },
        _validationMetadata: { removedTerms: ['Globex'] },
      }),
    }));
    for (const fetch of [failed, thrown, unreadable, scrubbed]) {
      await expect(service(fetch as unknown as jest.Mock).narrate!(request)).resolves.toBeNull();
    }
  });
});

describe('WorklistWidget narrative', () => {
  it("shows the Assistant's narrative above the lanes, tagged as written by it", async () => {
    const service = serviceWithNarrate();
    renderWidget(service);

    const narrative = await screen.findByTestId('worklist-narrative');
    expect(within(narrative).getByTestId('worklist-narrative-summary')).toHaveTextContent(TEXT.summary);
    expect(narrative).toHaveTextContent('4 of the items to do are past due.');
    expect(narrative).toHaveTextContent('Written by the Assistant');
    expect(service.narrate).toHaveBeenCalledTimes(1);
    const sent = service.narrate.mock.calls[0][0];
    expect(sent.categories.map(c => c.name)).toEqual([DECIDE_CATEGORY, DO_CATEGORY, DO_PAST_DUE_CATEGORY]);
  });

  it('hides it under ANY filter and does not call the model for a filter; clearing the filter brings it back', async () => {
    const service = serviceWithNarrate();
    renderWidget(service);
    await screen.findByTestId('worklist-narrative');
    expect(service.narrate).toHaveBeenCalledTimes(1);

    await userEvent.click(within(lane('Do')).getByRole('button', { name: /^Coming due/ }));
    expect(screen.queryByTestId('worklist-narrative')).not.toBeInTheDocument();

    await userEvent.click(within(lane('Do')).getByRole('button', { name: 'Show all' }));
    expect(await screen.findByTestId('worklist-narrative')).toBeInTheDocument();
    expect(service.narrate).toHaveBeenCalledTimes(1);
  });

  it('hides it under a Decide filter too', async () => {
    renderWidget(serviceWithNarrate());
    await screen.findByTestId('worklist-narrative');
    await userEvent.click(within(lane('Decide')).getByRole('button', { name: /^Chase a reply/ }));
    expect(screen.queryByTestId('worklist-narrative')).not.toBeInTheDocument();
  });

  it('does not change the membership or the order of either lane (no model output influences order)', async () => {
    const without = renderWidget(fakeService());
    await screen.findByTestId('worklist-widget');
    await waitFor(() => expect(lineIds(lane('Do')).length).toBeGreaterThan(0));
    const plain = orderOf();
    without.unmount();

    renderWidget(serviceWithNarrate());
    await screen.findByTestId('worklist-narrative');
    expect(orderOf()).toEqual(plain);
  });

  it('shows no narrative, and no error, when the model gives none', async () => {
    const service = serviceWithNarrate(null);
    renderWidget(service);
    await screen.findByTestId('worklist-widget');
    await waitFor(() => expect(service.narrate).toHaveBeenCalled());
    expect(screen.queryByTestId('worklist-narrative')).not.toBeInTheDocument();
    expect(screen.queryByTestId('worklist-notice')).not.toBeInTheDocument();
    expect(lineIds(lane('Do')).length).toBeGreaterThan(0);
  });

  it('shows no narrative, and calls nothing, when the service has no narrate', async () => {
    renderWidget(fakeService());
    await screen.findByTestId('worklist-widget');
    expect(screen.queryByTestId('worklist-narrative')).not.toBeInTheDocument();
  });

  it('a host-supplied narrative wins and the model is not called', async () => {
    const service = serviceWithNarrate();
    renderWidget(service, { narrative: <span>Host words.</span> });
    expect(await screen.findByTestId('worklist-narrative')).toHaveTextContent('Host words.');
    expect(service.narrate).not.toHaveBeenCalled();
  });

  it('asks once for the same counts across mounts (cached)', async () => {
    const service = serviceWithNarrate();
    const first = renderWidget(service);
    await screen.findByTestId('worklist-narrative');
    first.unmount();

    renderWidget(service);
    await screen.findByTestId('worklist-narrative');
    expect(service.narrate).toHaveBeenCalledTimes(1);
  });

  it('shows no narrative for an empty list and makes no call', async () => {
    const empty = {
      ...fakeService({}),
    };
    const service = { ...empty, narrate: jest.fn(async (_r: WorklistNarrativeRequest) => TEXT) };
    renderWidget(service);
    await screen.findByTestId('worklist-widget');
    expect(service.narrate).not.toHaveBeenCalled();
    expect(screen.queryByTestId('worklist-narrative')).not.toBeInTheDocument();
  });
});

describe('no model output influences order or membership (source-scan guard)', () => {
  const dir = path.join(__dirname, '..');
  const code = (f: string) =>
    fs
      .readFileSync(path.join(dir, f), 'utf8')
      .split(/\r?\n/)
      .filter(l => !/^\s*(\*|\/\/|\/\*)/.test(l))
      .join('\n');
  const NARRATIVE = /narrat/i;

  it.each(['worklistView.ts', 'laneCountFilters.ts', 'useWorklistData.ts', 'WorklistLanePanel.tsx', 'MatterCard.tsx'])(
    '%s, which orders and filters the lists, reaches no narrative code',
    file => {
      expect(NARRATIVE.test(code(file))).toBe(false);
    }
  );

  it('the guard fires on code that does reach the narrative (must-fire control)', () => {
    expect(NARRATIVE.test(code('worklistNarrative.ts'))).toBe(true);
    expect(NARRATIVE.test(code('WorklistWidget.tsx'))).toBe(true);
  });

  it('the narrative module sorts nothing and re-groups nothing', () => {
    expect(
      /\.sort\(|\.reverse\(|\.toSorted\(|\.toReversed\(/.test(
        code('worklistNarrative.ts') + code('useWorklistNarrative.ts')
      )
    ).toBe(false);
  });
});

/**
 * @jest-environment node
 *
 * The Decide lane's read over MCP (task 031; FR-05, FR-12): `createWorklistService` reads `GET /api/v1/signals/worklist?set=lanes`; the
 * paged fetch answers it by following `spaarke_list_decision_page`'s cursor chain to the end and building the route's body, so the REAL
 * shared service parses it. The host (`callServerTool`) is the only double.
 */
import {
  createWorklistService,
  WorklistRequestError,
} from '@spaarke/ui-components/components/Worklist/worklistService';
import { createDecideLaneFetch, lanesBodyFromPages, MAX_PAGES, WORKLIST_ROUTE } from '../lane/decideLaneFetch';
import { ShimReason, type CallServerTool } from '../mcp/mcpFetch';
import { MATTERS, PAGE_TOOL, pagingHost, row, toolError } from './laneFixtures';

const serviceOver = (callTool: CallServerTool) =>
  createWorklistService({ fetch: createDecideLaneFetch(callTool), baseUrl: '' });

const ids = (data: Awaited<ReturnType<ReturnType<typeof serviceOver>['read']>>) =>
  data.Decide.flatMap(g => g.items.map(i => i.signalId));

describe('reading the lane over the paging tool', () => {
  it('reads one page into groups with the cores named once per page, and an empty Do lane', async () => {
    const rows = [
      row(1, { core: MATTERS.a.id, workType: 'askOutsideCounsel' }),
      row(2, { core: MATTERS.a.id, workType: 'chaseReply' }),
      row(3, { core: MATTERS.b.id, workType: 'approveOrRebudget' }),
    ];
    const host = pagingHost(rows);
    const data = await serviceOver(host.callTool).read('lanes');

    expect(host.calls).toEqual([{ name: PAGE_TOOL, args: {} }]);
    expect(data.Decide.map(g => [g.core?.name, g.core?.number, g.items.length])).toEqual([
      [MATTERS.a.name, MATTERS.a.number, 2],
      [MATTERS.b.name, MATTERS.b.number, 1],
    ]);
    expect(data.Decide[0].items[0]).toMatchObject({
      lane: 'Decide',
      workType: 'askOutsideCounsel',
      subjectName: 'Subject 1',
    });
    expect(data.Do).toEqual([]);
  });

  it('follows the cursor chain to the end: every item once, in order, one group across a page boundary', async () => {
    // 75 rows of matter A then 75 of matter B: the page boundary at 50 and 100 splits A's rows over two pages.
    const rows = [
      ...Array.from({ length: 75 }, (_, i) => row(i + 1, { core: MATTERS.a.id, workType: 'askOutsideCounsel' })),
      ...Array.from({ length: 75 }, (_, i) => row(i + 76, { core: MATTERS.b.id, workType: 'chaseReply' })),
    ];
    const host = pagingHost(rows);
    const data = await serviceOver(host.callTool).read('lanes');

    expect(host.calls.map(c => c.args)).toEqual([{}, { cursor: 'cursor-50' }, { cursor: 'cursor-100' }]);
    expect(ids(data)).toEqual(rows.map(r => r.signalId));
    expect(data.Decide.map(g => g.items.length)).toEqual([75, 75]);
  });

  it('puts a row with no core record in a Not filed group (never dropped)', async () => {
    const data = await serviceOver(pagingHost([row(1), row(2, { core: MATTERS.a.id })]).callTool).read('lanes');
    expect(data.Decide.map(g => g.core === null)).toEqual([true, false]);
    expect(ids(data)).toHaveLength(2);
  });

  it('keeps privileged rows (the app-only tool carries full detail) and a row it is sent twice only once', async () => {
    const privileged = row(1, { core: MATTERS.a.id, privileged: true });
    const data = await serviceOver(pagingHost([privileged, privileged, row(2, { core: MATTERS.a.id })]).callTool).read(
      'lanes'
    );
    expect(ids(data)).toEqual([privileged.signalId, row(2).signalId]);
  });

  it('reads an empty lane as an empty Decide lane, not an error', async () => {
    const data = await serviceOver(pagingHost([]).callTool).read('lanes');
    expect(data.Decide).toEqual([]);
  });
});

describe('failures keep the REST shape the service reads', () => {
  it('a tool error on any page fails the whole read with the server message and status (no partial lane)', async () => {
    const rows = Array.from({ length: 120 }, (_, i) => row(i + 1, { core: MATTERS.a.id }));
    const good = pagingHost(rows);
    const callTool: CallServerTool = async (name, args) =>
      args.cursor === 'cursor-50'
        ? toolError('ontology.signal.worklist_unavailable', 'The worklist is unavailable right now.', 503)
        : good.callTool(name, args);

    const failure = await serviceOver(callTool)
      .read('lanes')
      .then(
        () => null,
        (err: unknown) => err
      );
    expect(failure).toBeInstanceOf(WorklistRequestError);
    expect((failure as WorklistRequestError).message).toBe('The worklist is unavailable right now.');
    expect((failure as WorklistRequestError).httpStatus).toBe(503);
  });

  it('keeps the server message of a first-page refusal and its status', async () => {
    const callTool: CallServerTool = async () =>
      toolError('ontology.signal.worklist_unavailable', 'Try again in a minute.', 503);
    await expect(serviceOver(callTool).read('lanes')).rejects.toMatchObject({
      message: 'Try again in a minute.',
      httpStatus: 503,
    });
  });

  it('a host call that rejects is "no answer at all" (status null), not an empty lane', async () => {
    const callTool: CallServerTool = () => Promise.reject(new Error('host went away'));
    await expect(serviceOver(callTool).read('lanes')).rejects.toMatchObject({
      message: 'The worklist could not be read.',
      httpStatus: null,
    });
  });

  it('a page with no items array is unreadable (502), never an empty lane', async () => {
    const callTool: CallServerTool = async () => ({ structuredContent: { cores: {} } });
    await expect(serviceOver(callTool).read('lanes')).rejects.toMatchObject({ httpStatus: 502 });
  });

  it('a cursor that comes back again stops the read (502) instead of looping', async () => {
    const callTool: CallServerTool = async () => ({ structuredContent: { items: [row(1)], nextCursor: 'same' } });
    await expect(serviceOver(callTool).read('lanes')).rejects.toMatchObject({ httpStatus: 502 });
  });

  it('a lane longer than MAX_PAGES is refused (502) rather than followed for ever', async () => {
    let n = 0;
    const callTool: CallServerTool = async () => {
      n += 1;
      return { structuredContent: { items: [row(n)], nextCursor: `cursor-${n}` } };
    };
    await expect(serviceOver(callTool).read('lanes')).rejects.toMatchObject({ httpStatus: 502 });
    expect(n).toBe(MAX_PAGES);
  });
});

describe('only the lanes read is mapped', () => {
  it('answers another set, or another address, with 404 widget.route_unmapped and calls no tool', async () => {
    const host = pagingHost([row(1)]);
    const fetchLane = createDecideLaneFetch(host.callTool);
    for (const address of [`${WORKLIST_ROUTE}?set=closedToday`, `${WORKLIST_ROUTE}`, '/api/v1/other']) {
      const response = await fetchLane(address, { method: 'GET' });
      expect(response.status).toBe(404);
      expect(((await response.json()) as { reasonCode: string }).reasonCode).toBe(ShimReason.RouteUnmapped);
    }
    expect(host.calls).toEqual([]);
  });
});

describe('lanesBodyFromPages', () => {
  it('builds the Signal read route body: Decide groups in arrival order, an empty Do lane', () => {
    const body = lanesBodyFromPages([
      { items: [row(1, { core: MATTERS.b.id }), row(2, { core: MATTERS.a.id })] as never, cores: {}, nextCursor: null },
    ]) as { lanes: Array<{ lane: string; groups: Array<{ core: { recordId: string } | null }> }> };
    expect(body.lanes.map(l => l.lane)).toEqual(['Decide', 'Do']);
    expect(body.lanes[0].groups.map(g => g.core?.recordId)).toEqual([MATTERS.b.id, MATTERS.a.id]);
    expect(body.lanes[1].groups).toEqual([]);
  });
});

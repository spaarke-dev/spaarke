/**
 * @jest-environment node
 *
 * The wizard's reads and its one write over MCP (task 032, FR-13, FR-15, NFR-06): the routes the shared service asks for become tool
 * calls, and the item the wizard decides is built from the plan tool's full row plus the lane the user saw. The consumers are the real
 * shared ones (`createDecisionWizardService`, `createWorklistService`, `buildWizardItem`); the only double is the host's `callServerTool`.
 *
 *  - one plan-tool call per open of an item (it runs a whole-lane read on the server and writes the plan-read stamp): the loader, the plan
 *    and the suppression share it; opening the item again calls again
 *  - the commit carries the review untouched, to the Work Item the path names, and a refusal keeps its reason code
 *  - negative: the item loader fails closed (the item left the lane, the row could not be read, the row is another Signal's)
 *  - negative: nothing here reaches the commit tool except the wizard's own commit
 */
import { createDecisionWizardService } from '@spaarke/ui-components/components/DecisionWizard/decisionWizardService';
import {
  DecisionCommitError,
  type DecisionReview,
} from '@spaarke/ui-components/components/DecisionWizard/decisionWizardTypes';
import { createWorklistService, type WorklistData } from '@spaarke/ui-components/components/Worklist/worklistService';
import { createDecideLaneFetch } from '../lane/decideLaneFetch';
import {
  createWizardBridge,
  GET_DECISION_PLAN_TOOL,
  COMPLETE_DECISION_TOOL,
  SEARCH_DECISION_LOOKUP_TOOL,
} from '../wizard/wizardFetch';
import { createWizardItemLoader, findDecideGroup, ITEM_GONE, ITEM_UNREADABLE } from '../wizard/wizardItems';
import { MATTERS, guid, toolError } from './laneFixtures';
import { COMPLETE_TOOL, PLAN_TOOL, PLANS, SENTENCE, restRow, wizardHost, type ServerItem } from './wizardFixtures';

const BASE = 'https://spaarke.invalid';
const ID1 = guid('5', 1);
const ID2 = guid('5', 2);
const SERVER: ServerItem[] = [
  { n: 1, plan: PLANS.reviseBudget, core: MATTERS.a.id },
  { n: 2, plan: PLANS.reassign, core: MATTERS.a.id },
  { n: 3, plan: PLANS.reviseBudget, core: MATTERS.b.id },
];
const REVIEW: DecisionReview = {
  reviewId: 'r-1',
  confirmed: true,
  actions: [{ code: 'revise-budget', taken: false }],
  because: 'Why.',
};

async function readLane(host: ReturnType<typeof wizardHost>): Promise<WorklistData> {
  return createWorklistService({ fetch: createDecideLaneFetch(host.callTool), baseUrl: '' }).read('lanes');
}

describe('the routes', () => {
  it("the plan and the suppression are the members of the plan tool's one answer", async () => {
    const host = wizardHost(SERVER);
    const service = createDecisionWizardService({ fetch: createWizardBridge(host.callTool).fetch, baseUrl: BASE });

    const plan = await service.getPlan(ID1);
    const suppression = await service.getSuppression(ID1);

    expect(plan.signalId).toBe(ID1);
    expect(plan.actions.map(a => a.code)).toEqual(['revise-budget']);
    expect(suppression.signalId).toBe(ID1);
    expect(host.callsTo(PLAN_TOOL).map(c => c.args)).toEqual([{ signalId: ID1 }, { signalId: ID1 }]);
  });

  it('shares ONE plan-tool call between the loader, the plan and the suppression of one open, and calls again on the next open', async () => {
    const host = wizardHost(SERVER);
    const bridge = createWizardBridge(host.callTool);
    const service = createDecisionWizardService({ fetch: bridge.fetch, baseUrl: BASE });

    await bridge.readItemRow(ID1);
    await service.getPlan(ID1);
    await service.getSuppression(ID1);
    expect(host.callsTo(PLAN_TOOL)).toHaveLength(1);

    // A plan read after the two the open expected is a new read (a Try again), not a stale answer.
    await service.getPlan(ID1);
    expect(host.callsTo(PLAN_TOOL)).toHaveLength(2);

    // Opening again makes a new call: the plan-read stamp a commit requires is fresh.
    await bridge.readItemRow(ID1);
    await service.getPlan(ID1);
    await service.getSuppression(ID1);
    expect(host.callsTo(PLAN_TOOL)).toHaveLength(3);
  });

  it('keeps two items apart: the shared answer of one open is never given to another item', async () => {
    const host = wizardHost(SERVER);
    const bridge = createWizardBridge(host.callTool);
    const service = createDecisionWizardService({ fetch: bridge.fetch, baseUrl: BASE });

    await bridge.readItemRow(ID1);
    const other = await service.getPlan(ID2);
    expect(other.signalId).toBe(ID2);
    expect(other.actions.map(a => a.code)).toEqual(['reassign']);
    expect(host.callsTo(PLAN_TOOL)).toHaveLength(2);
  });

  it('a suppression the tool could not read is unavailable, never a count of nothing', async () => {
    const host = wizardHost(SERVER, {
      plan: (signalId, item) => ({
        structuredContent: {
          plan: { ...item.plan, signalId },
          suppression: null,
          suppressionError: { reasonCode: 'x', message: 'no history' },
          row: restRow(item),
        },
      }),
    });
    const service = createDecisionWizardService({ fetch: createWizardBridge(host.callTool).fetch, baseUrl: BASE });
    await expect(service.getSuppression(ID1)).rejects.toMatchObject({ httpStatus: 502 });
    await expect(service.getPlan(ID1)).resolves.toMatchObject({ signalId: ID1 });
  });

  it('the commit sends the review untouched to the Work Item the path names, and nothing else reaches the commit tool', async () => {
    const host = wizardHost(SERVER);
    const bridge = createWizardBridge(host.callTool);
    const service = createDecisionWizardService({ fetch: bridge.fetch, baseUrl: BASE });

    // Reads first: none of them writes.
    await bridge.readItemRow(ID1);
    await service.getPlan(ID1);
    await service.getSuppression(ID1);
    expect(host.callsTo(COMPLETE_TOOL)).toHaveLength(0);

    const result = await service.commit(ID1, REVIEW);
    expect(host.callsTo(COMPLETE_TOOL)).toHaveLength(1);
    expect(host.callsTo(COMPLETE_TOOL)[0].args).toEqual({ signalId: ID1, review: REVIEW });
    expect(result.reviewId).toBe('r-1');
    expect(COMPLETE_DECISION_TOOL).toBe(COMPLETE_TOOL);
  });

  it("a resumed review's retry and send choice (ontology task 191) reach the commit tool inside the review, unchanged", async () => {
    const host = wizardHost(SERVER);
    const service = createDecisionWizardService({ fetch: createWizardBridge(host.callTool).fetch, baseUrl: BASE });
    const resumed: DecisionReview = { ...REVIEW, retry: true, sendChoice: 'record-without-sending' };

    await service.commit(ID1, resumed);

    expect(host.callsTo(COMPLETE_TOOL)).toHaveLength(1);
    expect(host.callsTo(COMPLETE_TOOL)[0].args).toEqual({ signalId: ID1, review: resumed });
  });

  it('a refusal keeps its reason code, status and message for the wizard to word', async () => {
    const host = wizardHost(SERVER, {
      complete: () =>
        toolError(
          'ontology.decision.actions_do_not_match_plan',
          "The matter's budgets changed while you were deciding; reopen the item.",
          422
        ),
    });
    const service = createDecisionWizardService({ fetch: createWizardBridge(host.callTool).fetch, baseUrl: BASE });
    const error = await service.commit(ID1, REVIEW).then(
      () => null,
      (err: unknown) => err
    );
    expect(error).toBeInstanceOf(DecisionCommitError);
    expect((error as DecisionCommitError).failure).toMatchObject({
      httpStatus: 422,
      reasonCode: 'ontology.decision.actions_do_not_match_plan',
      detail: "The matter's budgets changed while you were deciding; reopen the item.",
    });
  });

  it('a host that does not answer the commit is "may not have been recorded" (the review can be sent again), not a refusal', async () => {
    const base = wizardHost(SERVER);
    const bridge = createWizardBridge(async (name, args) => {
      if (name === COMPLETE_TOOL) throw new Error('host closed');
      return base.callTool(name, args);
    });
    const service = createDecisionWizardService({ fetch: bridge.fetch, baseUrl: BASE });
    const error = (await service.commit(ID1, REVIEW).then(
      () => null,
      (err: unknown) => err
    )) as DecisionCommitError;
    expect(error).toBeInstanceOf(DecisionCommitError);
    expect(error.failure.title).toBe('The decision may not have been recorded');
    expect(error.failure.httpStatus).toBe(0);
  });

  it('the lookup address reaches the lookup tool with the Work Item, the parameter and the text', async () => {
    const host = wizardHost(SERVER);
    const { fetch } = createWizardBridge(host.callTool);
    const params = new URLSearchParams({ signalId: ID1, parameterCode: 'budget', query: 'fy26' });
    const body = await (await fetch(`/api/v1/decision-lookup?${params.toString()}`, { method: 'GET' })).json();

    expect(host.calls).toEqual([
      { name: SEARCH_DECISION_LOOKUP_TOOL, args: { signalId: ID1, parameterCode: 'budget', query: 'fy26' } },
    ]);
    expect(body.candidates.map((c: { name: string }) => c.name)).toEqual([
      'FY26 litigation budget',
      'FY26 appeal budget',
    ]);
  });
});

describe('readItemRow', () => {
  it("rejects with the tool's own message when it refused (nothing opens)", async () => {
    const host = wizardHost(SERVER, {
      plan: () => toolError('sdap.access.deny.record_unavailable', 'This item is not available to you.', 404),
    });
    await expect(createWizardBridge(host.callTool).readItemRow(ID1)).rejects.toThrow(
      'This item is not available to you.'
    );
  });

  it('rejects with a plain sentence when the host did not answer, and when the answer has no body', async () => {
    const gone = createWizardBridge(async () => {
      throw new Error('host closed');
    });
    await expect(gone.readItemRow(ID1)).rejects.toThrow('The decision could not be read. Try again.');

    const empty = createWizardBridge(async () => ({ content: [{ type: 'text', text: 'ok' }] }));
    await expect(empty.readItemRow(ID1)).rejects.toThrow('The decision could not be read. Try again.');
  });

  it('a failed open leaves nothing pending: the next read of the item calls the tool', async () => {
    let fail = true;
    const host = wizardHost(SERVER);
    const callTool = jest.fn(async (name: string, args: Record<string, unknown>) => {
      if (name === GET_DECISION_PLAN_TOOL && fail) throw new Error('host closed');
      return host.callTool(name, args);
    });
    const bridge = createWizardBridge(callTool);
    await expect(bridge.readItemRow(ID1)).rejects.toThrow();
    fail = false;
    const service = createDecisionWizardService({ fetch: bridge.fetch, baseUrl: BASE });
    await expect(service.getPlan(ID1)).resolves.toMatchObject({ signalId: ID1 });
    expect(callTool).toHaveBeenCalledTimes(2);
  });
});

describe('the item loader', () => {
  it("builds the item from the plan tool's full row and the lane's group: sentence and evidence from the tool, the core and the siblings from the lane", async () => {
    const host = wizardHost(SERVER);
    const lane = await readLane(host);
    const load = createWizardItemLoader(createWizardBridge(host.callTool), lane, () => new Date(2026, 9, 9));

    const item = await load(ID1);

    expect(item.row.signalId).toBe(ID1);
    expect(item.sentence).toBe(SENTENCE);
    expect(item.evidence).toHaveLength(1);
    expect(item.evidence[0].text).toBe('Invoice 4411 dated 3 Oct 2026');
    expect(item.core?.recordId).toBe(MATTERS.a.id);
    // The other open items on the same matter, for "Also resolve".
    expect(item.otherOpenItems.map(i => i.signalId)).toEqual([ID2]);
    expect(host.callsTo(PLAN_TOOL)).toHaveLength(1);
  });

  it('reads from the lane as it was at launch: an item still to be shown does not vanish when the lane is read again', async () => {
    const host = wizardHost(SERVER);
    const lane = await readLane(host);
    const load = createWizardItemLoader(createWizardBridge(host.callTool), lane);
    // The first item is recorded and the lane is read again; the snapshot the wizard opened on is unchanged.
    await host.callTool(COMPLETE_TOOL, { signalId: ID1, review: REVIEW });
    const later = await readLane(host);
    expect(later.Decide.flatMap(g => g.items.map(i => i.signalId))).not.toContain(ID1);

    await expect(load(ID2)).resolves.toMatchObject({ row: { signalId: ID2 } });
    await expect(load(ID1)).resolves.toMatchObject({ row: { signalId: ID1 } });
  });

  it('fails closed when the Signal is not in the lane the wizard opened on (no tool call)', async () => {
    const host = wizardHost(SERVER);
    const lane = await readLane(host);
    const load = createWizardItemLoader(createWizardBridge(host.callTool), lane);
    await expect(load(guid('5', 99))).rejects.toThrow(ITEM_GONE);
    expect(host.callsTo(PLAN_TOOL)).toHaveLength(0);
    expect(findDecideGroup(lane, guid('5', 99))).toBeNull();
  });

  it("fails closed when the tool says the item has left the caller's lanes (row null, no error)", async () => {
    const host = wizardHost(SERVER, {
      plan: (signalId, item) => ({
        structuredContent: { plan: { ...item.plan, signalId }, suppression: null, row: null },
      }),
    });
    const load = createWizardItemLoader(createWizardBridge(host.callTool), await readLane(host));
    await expect(load(ID1)).rejects.toThrow(ITEM_GONE);
  });

  it('fails closed when the tool could not read the row (it is not decided without its evidence)', async () => {
    const host = wizardHost(SERVER, {
      plan: (signalId, item) => ({
        structuredContent: {
          plan: { ...item.plan, signalId },
          suppression: null,
          row: null,
          rowError: { reasonCode: 'x', message: 'no row' },
        },
      }),
    });
    const load = createWizardItemLoader(createWizardBridge(host.callTool), await readLane(host));
    await expect(load(ID1)).rejects.toThrow(ITEM_UNREADABLE);
  });

  it("never shows another Signal's row under this item", async () => {
    const host = wizardHost(SERVER, {
      plan: (signalId, item) => ({
        structuredContent: { plan: { ...item.plan, signalId }, suppression: null, row: restRow({ ...item, n: 2 }) },
      }),
    });
    const load = createWizardItemLoader(createWizardBridge(host.callTool), await readLane(host));
    await expect(load(ID1)).rejects.toThrow(ITEM_UNREADABLE);
  });

  it('a row that is not a row at all is unreadable', async () => {
    const host = wizardHost(SERVER, {
      plan: (signalId, item) => ({
        structuredContent: { plan: { ...item.plan, signalId }, suppression: null, row: 'garbage' },
      }),
    });
    const load = createWizardItemLoader(createWizardBridge(host.callTool), await readLane(host));
    await expect(load(ID1)).rejects.toThrow(ITEM_UNREADABLE);
  });

  it("carries the tool's refusal message out as the loader's error", async () => {
    const host = wizardHost(SERVER, {
      plan: () => toolError('sdap.access.deny.record_unavailable', 'This item is not available to you.', 404),
    });
    const load = createWizardItemLoader(createWizardBridge(host.callTool), await readLane(host));
    await expect(load(ID1)).rejects.toThrow('This item is not available to you.');
  });
});

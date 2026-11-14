/**
 * @jest-environment node
 *
 * The MCP fetch shim (task 030, FR-12 / FR-09): a tool result becomes a real Response the shared services can read; a tool failure
 * becomes the Response REST would have sent for the same failure; nothing here opens a connection. Node environment because jsdom has
 * no Response.
 *
 * Bodies are shaped exactly as the BFF sends them (McpToolResults.cs, DecisionResultMapper):
 *  - failure: `isError: true`, `structuredContent = { reasonCode, message, status, correlationId?, partial? }`, a text block with the message;
 *  - a failure with no domain answer (bad arguments, an exception): `isError: true` with a text block only;
 *  - `spaarke_get_decision_plan` -> `{ plan, suppression, row }`; `spaarke_list_decision_page` -> `{ items, cores, nextCursor }`;
 *    `spaarke_complete_decision` -> the commit result itself.
 * The consumers are the real shared ones: `createDecisionWizardService`, `failureKeepsReview`, `failureMessage`.
 */
import {
  createWorklistService,
  WorklistRequestError,
} from '@spaarke/ui-components/components/Worklist/worklistService';
import {
  createDecisionWizardService,
  DecisionWizardRequestError,
} from '@spaarke/ui-components/components/DecisionWizard/decisionWizardService';
import { failureHeading, failureMessage } from '@spaarke/ui-components/components/DecisionWizard/decisionDisplay';
import { failureKeepsReview } from '@spaarke/ui-components/components/DecisionWizard/decisionRecording';
import {
  DecisionCommitError,
  type DecisionCommitFailure,
  type DecisionReview,
} from '@spaarke/ui-components/components/DecisionWizard/decisionWizardTypes';
import {
  createMcpFetch,
  DEFAULT_TOOL_ERROR_STATUS,
  NON_DOMAIN_ERROR_STATUS,
  ShimReason,
  type CallServerTool,
  type McpRoute,
  type McpToolResult,
} from '../mcp/mcpFetch';

const asRecord = (value: unknown) => value as Record<string, unknown>;

const ROUTES: McpRoute[] = [
  {
    method: 'GET',
    path: '/api/v1/signals/:signalId/decision-plan',
    tool: 'spaarke_get_decision_plan',
    result: structured => asRecord(structured).plan,
  },
  {
    method: 'GET',
    path: '/api/v1/signals/:signalId/suppression',
    tool: 'spaarke_get_decision_plan',
    result: structured => asRecord(structured).suppression,
  },
  { method: 'POST', path: '/api/v1/signals/:signalId/decision', tool: 'spaarke_complete_decision' },
  {
    method: 'GET',
    path: '/api/v1/signals/worklist',
    tool: 'spaarke_list_decision_page',
    args: r => ({ set: r.query.set }),
  },
];

function harness(result: McpToolResult | Error, routes: McpRoute[] = ROUTES) {
  const calls: Array<{ name: string; args: Record<string, unknown> }> = [];
  const callTool: CallServerTool = async (name, args) => {
    calls.push({ name, args });
    if (result instanceof Error) throw result;
    return result;
  };
  return { calls, fetch: createMcpFetch(callTool, routes) };
}

// ---- BFF-shaped bodies --------------------------------------------------------------------------------------------------

const PLAN = {
  signalId: 'abc',
  lane: 'Decide',
  policyVersionId: 'pv-1',
  actions: [],
  nextSteps: [],
  dismissalReasons: [],
};
const SUPPRESSION = {
  signalId: 'abc',
  dismissalCount: 1,
  dismissalsRecorded: 1,
  threshold: 3,
  windowDays: 30,
  countsTowardSuppression: true,
};

/** `spaarke_get_decision_plan`'s structuredContent. */
const planResult = (): McpToolResult => ({
  content: [{ type: 'text', text: 'Decision plan for 1 item.' }],
  structuredContent: { plan: PLAN, suppression: SUPPRESSION, row: { signalId: 'abc' } },
});

/** `spaarke_list_decision_page`'s structuredContent. */
const pageResult = (): McpToolResult => ({
  content: [{ type: 'text', text: '1 Decide item.' }],
  structuredContent: {
    items: [{ signalId: 'abc', title: 'Approve budget', lane: 'Decide' }],
    cores: { 'core-1': { name: 'Matter 1' } },
    nextCursor: 'cur-2',
  },
});

/** A failed commit, as McpToolResults.FromError sends it (the D-17 facts under `partial`). */
const failedCommit = (): McpToolResult => ({
  isError: true,
  content: [{ type: 'text', text: 'Part of the decision could not be recorded.' }],
  structuredContent: {
    reasonCode: 'ontology.decision.step_failed',
    message: 'Part of the decision could not be recorded.',
    status: 500,
    correlationId: '00-trace-9',
    partial: {
      failedStep: 3,
      landed: [{ entity: 'sprk_budgetrevision', id: 'x-1' }],
      possiblyLanded: [{ entity: 'sprk_communication', id: 'm-1' }],
      steps: [],
      recordMayExist: false,
      sentButNotRecorded: false,
      resumable: true,
      replayed: false,
    },
  },
});

/** A refusal with a mapped code, as McpToolResults.FromError sends it. */
const refusal = (reasonCode: string, message: string, status: number): McpToolResult => ({
  isError: true,
  content: [{ type: 'text', text: message }],
  structuredContent: { reasonCode, message, status, correlationId: '00-trace-1' },
});

const REVIEW: DecisionReview = {
  reviewId: 'r-1',
  confirmed: true,
  actions: [{ code: 'send-budget-inquiry', taken: false }],
};

/** Runs the wizard's one write over the shim and returns the failure it shows (or fails the test when it resolves). */
async function commitFailure(result: McpToolResult | Error): Promise<DecisionCommitFailure> {
  const { fetch } = harness(result);
  const service = createDecisionWizardService({ fetch, baseUrl: 'https://spaarke.invalid' });
  const error = await service.commit('abc', REVIEW).then(
    () => null,
    (err: unknown) => err
  );
  expect(error).toBeInstanceOf(DecisionCommitError);
  return (error as DecisionCommitError).failure;
}

describe('createMcpFetch: success', () => {
  it("returns a 200 Response whose JSON body is the tool's structuredContent", async () => {
    const { fetch, calls } = harness(pageResult());

    const response = await fetch('/api/v1/signals/worklist?set=decide&junk=1');

    expect(response).toBeInstanceOf(Response);
    expect(response.status).toBe(200);
    expect(response.ok).toBe(true);
    expect(response.headers.get('Content-Type')).toBe('application/json');
    const body = await response.json();
    expect(Object.keys(body).sort()).toEqual(['cores', 'items', 'nextCursor']);
    expect(body.nextCursor).toBe('cur-2');
    expect(calls).toEqual([{ name: 'spaarke_list_decision_page', args: { set: 'decide' } }]);
  });

  it('binds URL-decoded path params, the query and the JSON body into the default arguments', async () => {
    const { fetch, calls } = harness({ structuredContent: { ok: true } });

    await fetch('https://anything.invalid/api/v1/signals/a%20b/decision?x=1', {
      method: 'POST',
      body: JSON.stringify({ confirm: true }),
    });

    expect(calls[0]).toEqual({
      name: 'spaarke_complete_decision',
      args: { signalId: 'a b', x: '1', confirm: true },
    });
  });

  it('applies the route\'s `result` selector: the plan route returns structuredContent.plan, the suppression route .suppression', async () => {
    const plan = harness(planResult());
    const suppression = harness(planResult());

    const planBody = await (await plan.fetch('/api/v1/signals/abc/decision-plan')).json();
    const suppressionBody = await (await suppression.fetch('/api/v1/signals/abc/suppression')).json();

    expect(planBody).toEqual(PLAN);
    expect(suppressionBody).toEqual(SUPPRESSION);
    expect(plan.calls).toEqual([{ name: 'spaarke_get_decision_plan', args: { signalId: 'abc' } }]);
  });

  it('is a 502 widget.unreadable_result when the selector finds nothing or throws', async () => {
    const routes: McpRoute[] = [
      { method: 'GET', path: '/a', tool: 't', result: () => undefined },
      {
        method: 'GET',
        path: '/b',
        tool: 't',
        result: () => {
          throw new Error('selector bug');
        },
      },
    ];
    const { fetch } = harness({ structuredContent: { plan: {} } }, routes);

    for (const path of ['/a', '/b']) {
      const response = await fetch(path);
      expect(response.status).toBe(502);
      expect((await response.json()).reasonCode).toBe(ShimReason.UnreadableResult);
    }
  });

  it('reads a JSON text block when the tool sent no structuredContent', async () => {
    const { fetch } = harness({ content: [{ type: 'text', text: '{"a":1}' }] });

    const response = await fetch('/api/v1/signals/worklist');

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ a: 1 });
  });

  it('is a 502 with reasonCode widget.unreadable_result when the result has nothing readable', async () => {
    const { fetch } = harness({ content: [{ type: 'text', text: 'not json' }] });

    const response = await fetch('/api/v1/signals/worklist');

    expect(response.ok).toBe(false);
    expect(response.status).toBe(502);
    expect((await response.json()).reasonCode).toBe(ShimReason.UnreadableResult);
  });
});

describe('createMcpFetch: a failure is the Response REST would send', () => {
  it('a 409 refusal is HTTP 409 with REST ProblemDetails: title is the reason phrase, detail the message, no `message` member', async () => {
    const { fetch } = harness(
      refusal('ontology.decision.review_in_progress', 'Another attempt at this review is running.', 409)
    );

    const response = await fetch('/api/v1/signals/abc/decision', { method: 'POST', body: '{}' });

    expect(response.ok).toBe(false);
    expect(response.status).toBe(409);
    expect(await response.json()).toEqual({
      title: 'Conflict',
      status: 409,
      detail: 'Another attempt at this review is running.',
      reasonCode: 'ontology.decision.review_in_progress',
      correlationId: '00-trace-1',
    });
  });

  it('a failed commit spreads the D-17 partial facts to the TOP level (landed, possiblyLanded, resumable, ...) with no `partial` member', async () => {
    const { fetch } = harness(failedCommit());

    const response = await fetch('/api/v1/signals/abc/decision', { method: 'POST', body: '{}' });
    const body = await response.json();

    expect(response.status).toBe(500);
    expect(body).toMatchObject({
      title: 'The decision was not recorded',
      status: 500,
      detail: 'Part of the decision could not be recorded.',
      reasonCode: 'ontology.decision.step_failed',
      correlationId: '00-trace-9',
      failedStep: 3,
      landed: [{ entity: 'sprk_budgetrevision', id: 'x-1' }],
      possiblyLanded: [{ entity: 'sprk_communication', id: 'm-1' }],
      recordMayExist: false,
      sentButNotRecorded: false,
      resumable: true,
      replayed: false,
    });
    expect(body).not.toHaveProperty('partial');
    expect(body).not.toHaveProperty('message');
  });

  it('partial facts can never override the fixed members (status, title, detail, reasonCode)', async () => {
    const { fetch } = harness({
      isError: true,
      structuredContent: {
        reasonCode: 'x.y',
        message: 'real detail',
        status: 403,
        partial: { status: 200, title: 'spoof', detail: 'spoof', reasonCode: 'spoof' },
      },
    });

    const body = await (await fetch('/api/v1/signals/abc/decision', { method: 'POST', body: '{}' })).json();

    expect(body).toMatchObject({ status: 403, title: 'Forbidden', detail: 'real detail', reasonCode: 'x.y' });
  });

  it('uses 422 only when the structured content names no usable status (absent, or outside 400-599)', async () => {
    const absent = harness({ isError: true, structuredContent: { reasonCode: 'x.y', message: 'm' } });
    const bogus = harness({ isError: true, structuredContent: { reasonCode: 'x.y', message: 'm', status: 200 } });

    const a = await absent.fetch('/api/v1/signals/a/decision', { method: 'POST' });
    const b = await bogus.fetch('/api/v1/signals/a/decision', { method: 'POST' });

    expect(a.status).toBe(DEFAULT_TOOL_ERROR_STATUS);
    expect(b.status).toBe(DEFAULT_TOOL_ERROR_STATUS);
    expect((await a.json()).title).toBe('Unprocessable Entity');
  });

  it('an isError result with only a text block is a 500 ProblemDetails with that text as detail and NO reasonCode', async () => {
    const { fetch } = harness({
      isError: true,
      content: [{ type: 'text', text: 'An error occurred invoking spaarke_complete_decision.' }],
    });

    const response = await fetch('/api/v1/signals/abc/decision', { method: 'POST', body: '{}' });
    const body = await response.json();

    expect(response.status).toBe(NON_DOMAIN_ERROR_STATUS);
    expect(body.detail).toBe('An error occurred invoking spaarke_complete_decision.');
    expect(body).not.toHaveProperty('reasonCode');
    expect(body.title).not.toBe(body.detail);
  });

  it('rethrows a rejected host call, as fetch does on a network failure (it is NOT turned into a Response)', async () => {
    const { fetch } = harness(new Error('The host is not connected.'));

    await expect(fetch('/api/v1/signals/abc/decision-plan')).rejects.toThrow('The host is not connected.');
  });
});

describe('createMcpFetch: requests it cannot serve', () => {
  it('answers 404 widget.route_unmapped for an unmapped path or method, and calls no tool', async () => {
    const { fetch, calls } = harness({ structuredContent: {} });

    const path = await fetch('/api/v1/nothing');
    const method = await fetch('/api/v1/signals/abc/decision-plan', { method: 'DELETE' });

    expect(path.status).toBe(404);
    expect((await path.json()).reasonCode).toBe(ShimReason.RouteUnmapped);
    expect(method.status).toBe(404);
    expect(calls).toHaveLength(0);
  });

  it('answers 400 widget.bad_request for a body that is not JSON, and calls no tool', async () => {
    const { fetch, calls } = harness({ structuredContent: {} });

    const response = await fetch('/api/v1/signals/abc/decision', { method: 'POST', body: '{not json' });

    expect(response.status).toBe(400);
    expect((await response.json()).reasonCode).toBe(ShimReason.BadRequest);
    expect(calls).toHaveLength(0);
  });

  it('answers 400 widget.bad_request for a malformed percent escape in the path, instead of throwing', async () => {
    const { fetch, calls } = harness({ structuredContent: {} });

    const response = await fetch('/api/v1/signals/%E0%A4%A/decision-plan');

    expect(response.status).toBe(400);
    expect((await response.json()).reasonCode).toBe(ShimReason.BadRequest);
    expect(calls).toHaveLength(0);
  });

  it("answers 400 widget.bad_request when a route's argument builder throws, and calls no tool", async () => {
    const calls: string[] = [];
    const routes: McpRoute[] = [
      {
        method: 'GET',
        path: '/api/v1/boom',
        tool: 'demo_boom',
        args: () => {
          throw new Error('cannot build');
        },
      },
    ];
    const shim = createMcpFetch(async name => {
      calls.push(name);
      return { structuredContent: {} };
    }, routes);

    const response = await shim('/api/v1/boom');

    expect(response.status).toBe(400);
    expect((await response.json()).reasonCode).toBe(ShimReason.BadRequest);
    expect(calls).toHaveLength(0);
  });

  it('answers 502 widget.unreadable_result when the host returns no result object', async () => {
    const shim = createMcpFetch(async () => undefined as unknown as McpToolResult, ROUTES);

    const response = await shim('/api/v1/signals/abc/decision-plan');

    expect(response.status).toBe(502);
    expect((await response.json()).reasonCode).toBe(ShimReason.UnreadableResult);
  });

  it('lets the path identifier win over a body or query member of the same name', async () => {
    const { fetch, calls } = harness({ structuredContent: { ok: true } });

    await fetch('/api/v1/signals/real-id/decision?signalId=from-query', {
      method: 'POST',
      body: JSON.stringify({ signalId: 'from-body', confirm: true }),
    });

    expect(calls[0].args).toEqual({ signalId: 'real-id', confirm: true });
  });
});

describe('createMcpFetch: it never touches the network', () => {
  it('does not call the global fetch, and there is no XMLHttpRequest, for any outcome', async () => {
    const globalFetch = jest.spyOn(globalThis, 'fetch');
    const outcomes: Array<McpToolResult | Error> = [
      pageResult(),
      failedCommit(),
      refusal('a.b', 'm', 409),
      new Error('host down'),
    ];

    for (const outcome of outcomes) {
      await harness(outcome)
        .fetch('/api/v1/signals/abc/decision', { method: 'POST', body: '{}' })
        .catch(() => undefined);
    }
    await harness({ structuredContent: {} }).fetch('/unmapped');

    expect(globalFetch).not.toHaveBeenCalled();
    expect(typeof XMLHttpRequest).toBe('undefined');
    globalFetch.mockRestore();
  });
});

describe('createMcpFetch with the shared Decision wizard service (the real consumer of a failed commit)', () => {
  it('getPlan over the shim reads structuredContent.plan, not the { plan, suppression, row } wrapper', async () => {
    const { fetch } = harness(planResult());
    const service = createDecisionWizardService({ fetch, baseUrl: 'https://spaarke.invalid' });

    expect(await service.getPlan('abc')).toEqual(PLAN);
    expect(await service.getSuppression('abc')).toEqual(SUPPRESSION);
  });

  it('a failed commit that landed writes keeps the review, with landed, possiblyLanded, resumable and the reference intact', async () => {
    const failure = await commitFailure(failedCommit());

    expect(failure.httpStatus).toBe(500);
    expect(failure.reasonCode).toBe('ontology.decision.step_failed');
    expect(failure.correlationId).toBe('00-trace-9');
    expect(failure.failedStep).toBe(3);
    expect(failure.landed).toEqual([{ entity: 'sprk_budgetrevision', id: 'x-1' }]);
    expect(failure.possiblyLanded).toEqual([{ entity: 'sprk_communication', id: 'm-1' }]);
    expect(failure.resumable).toBe(true);
    expect(failureKeepsReview(failure)).toBe(true);
  });

  it('a landed-writes failure keeps the review even when it arrives with a 4xx status (the facts, not the status, decide)', async () => {
    const result = failedCommit();
    (result.structuredContent as { status: number }).status = 422;

    const failure = await commitFailure(result);

    expect(failure.httpStatus).toBe(422);
    expect(failure.landed).toHaveLength(1);
    expect(failureKeepsReview(failure)).toBe(true);
  });

  it('a 409 review-in-progress refusal shows its own mapped message and keeps the review', async () => {
    const failure = await commitFailure(
      refusal('ontology.decision.review_in_progress', 'Another attempt at this review is running.', 409)
    );

    expect(failure.httpStatus).toBe(409);
    expect(failure.title).toBe('Conflict');
    expect(failureHeading(failure)).toBe('This decision is already being recorded');
    expect(failureMessage(failure)).toBe('This decision is already being recorded. Wait a moment, then try again.');
    expect(failureMessage(failure)).not.toMatch(/Something went wrong/);
    expect(failureKeepsReview(failure)).toBe(true);
  });

  it("a refusal whose code the wizard does not map shows the tool's own sentence, not 'Something went wrong'", async () => {
    const failure = await commitFailure(
      refusal('ontology.decision.review_invalid', 'Choose an action for every item before confirming.', 422)
    );

    expect(failure.title).toBe('Unprocessable Entity');
    expect(failure.detail).toBe('Choose an action for every item before confirming.');
    expect(failureMessage(failure)).toBe('Choose an action for every item before confirming.');
    expect(failureKeepsReview(failure)).toBe(false); // a plain refusal stored nothing: the review may be edited under a new id
  });

  it('the invoice-review refusal still reads "Decide this in Invoice Review"', async () => {
    const failure = await commitFailure(
      refusal('ontology.decision.decided_in_invoice_review', 'Decide this item in Invoice Review.', 409)
    );

    expect(failureHeading(failure)).toBe('Decide this in Invoice Review');
    expect(failureMessage(failure)).toBe('Invoice items are decided in Invoice Review. Nothing was recorded.');
  });

  it('a rejected host call is the status-0 "may not have been recorded" failure, and keeps the review', async () => {
    const failure = await commitFailure(new Error('The host is not connected.'));

    expect(failure.httpStatus).toBe(0);
    expect(failure.title).toBe('The decision may not have been recorded');
    expect(failureHeading(failure)).toBe('The decision may not have been recorded');
    expect(failureMessage(failure)).toMatch(/not known whether anything was written/);
    expect(failureKeepsReview(failure)).toBe(true);
  });

  it('a non-domain tool error (bad arguments, an exception) is a 500 the wizard shows with the tool text, keeping the review', async () => {
    const failure = await commitFailure({
      isError: true,
      content: [{ type: 'text', text: 'An error occurred invoking the tool.' }],
    });

    expect(failure.httpStatus).toBe(500);
    expect(failure.reasonCode).toBeNull();
    expect(failureMessage(failure)).toBe('An error occurred invoking the tool.');
    expect(failureKeepsReview(failure)).toBe(true);
  });

  it('a failed plan read is a request error carrying the status and the tool message', async () => {
    const { fetch } = harness(refusal('sdap.access.deny.record_unavailable', 'The requested record was not found.', 404));
    const service = createDecisionWizardService({ fetch, baseUrl: 'https://spaarke.invalid' });

    const failure = await service.getPlan('abc').then(
      () => null,
      (err: unknown) => err
    );

    expect(failure).toBeInstanceOf(DecisionWizardRequestError);
    expect((failure as DecisionWizardRequestError).httpStatus).toBe(404);
    expect((failure as DecisionWizardRequestError).message).toBe('The requested record was not found.');
  });
});

describe('createMcpFetch with the shared Worklist service', () => {
  it('lets createWorklistService read a lane answer over the shim', async () => {
    const { fetch } = harness({
      structuredContent: {
        lanes: [
          { lane: 'Decide', groups: [] },
          { lane: 'Do', groups: [] },
        ],
      },
    });

    const data = await createWorklistService({ fetch, baseUrl: 'https://spaarke.invalid' }).read('lanes');

    expect(data).toEqual({ set: 'lanes', Decide: [], Do: [] });
  });

  it("surfaces a tool error as the service's own error, with the tool's message and the Response status", async () => {
    const { fetch } = harness(refusal('ontology.worklist.unavailable', 'The worklist could not be read. Try again.', 503));

    const failure = await createWorklistService({ fetch, baseUrl: '' })
      .read('lanes')
      .then(
        () => null,
        (err: unknown) => err
      );

    expect(failure).toBeInstanceOf(WorklistRequestError);
    expect((failure as WorklistRequestError).message).toBe('The worklist could not be read. Try again.');
    expect((failure as WorklistRequestError).httpStatus).toBe(503);
  });

  it('a rejected host call is the service\'s "no answer" error (no status)', async () => {
    const { fetch } = harness(new Error('host down'));

    const failure = await createWorklistService({ fetch, baseUrl: '' })
      .read('lanes')
      .then(
        () => null,
        (err: unknown) => err
      );

    expect(failure).toBeInstanceOf(WorklistRequestError);
    expect((failure as WorklistRequestError).httpStatus).toBeNull();
  });
});

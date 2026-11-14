/**
 * @jest-environment node
 *
 * The MCP fetch shim (task 030, FR-12 / FR-09): a tool result becomes a real Response the shared services can read; a tool error becomes a
 * non-2xx Response that keeps `reasonCode` and `message`; nothing here opens a connection. Node environment because jsdom has no Response.
 *
 * Illustrative tool names only: the real route table (which request maps to which Spaarke tool) is task 031's and 032's.
 */
import {
  createWorklistService,
  WorklistRequestError,
} from '@spaarke/ui-components/components/Worklist/worklistService';
import {
  createMcpFetch,
  DEFAULT_TOOL_ERROR_STATUS,
  ShimReason,
  type CallServerTool,
  type McpRoute,
  type McpToolResult,
} from '../mcp/mcpFetch';

const ROUTES: McpRoute[] = [
  { method: 'GET', path: '/api/v1/signals/:signalId/decision-plan', tool: 'demo_get_plan' },
  { method: 'POST', path: '/api/v1/signals/:signalId/decision', tool: 'demo_complete' },
  { method: 'GET', path: '/api/v1/signals/worklist', tool: 'demo_list', args: r => ({ set: r.query.set }) },
];

function harness(result: McpToolResult | Error) {
  const calls: Array<{ name: string; args: Record<string, unknown> }> = [];
  const callTool: CallServerTool = async (name, args) => {
    calls.push({ name, args });
    if (result instanceof Error) throw result;
    return result;
  };
  return { calls, fetch: createMcpFetch(callTool, ROUTES) };
}

describe('createMcpFetch: success', () => {
  it("returns a 200 Response whose JSON body is the tool's structuredContent", async () => {
    const { fetch, calls } = harness({ structuredContent: { steps: [1, 2], planned: true } });

    const response = await fetch('/api/v1/signals/abc/decision-plan', { method: 'GET' });

    expect(response).toBeInstanceOf(Response);
    expect(response.status).toBe(200);
    expect(response.ok).toBe(true);
    expect(response.headers.get('Content-Type')).toBe('application/json');
    expect(await response.json()).toEqual({ steps: [1, 2], planned: true });
    expect(calls).toEqual([{ name: 'demo_get_plan', args: { signalId: 'abc' } }]);
  });

  it('binds URL-decoded path params, the query and the JSON body into the default arguments', async () => {
    const { fetch, calls } = harness({ structuredContent: { ok: true } });

    await fetch('https://anything.invalid/api/v1/signals/a%20b/decision?x=1', {
      method: 'POST',
      body: JSON.stringify({ confirm: true }),
    });

    expect(calls[0]).toEqual({ name: 'demo_complete', args: { signalId: 'a b', x: '1', confirm: true } });
  });

  it("uses a route's own argument builder", async () => {
    const { fetch, calls } = harness({ structuredContent: { lanes: [] } });

    await fetch('/api/v1/signals/worklist?set=lanes&junk=1');

    expect(calls[0]).toEqual({ name: 'demo_list', args: { set: 'lanes' } });
  });

  it('reads a JSON text block when the tool sent no structuredContent', async () => {
    const { fetch } = harness({ content: [{ type: 'text', text: '{"a":1}' }] });

    const response = await fetch('/api/v1/signals/abc/decision-plan');

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ a: 1 });
  });

  it('is a 502 with reasonCode widget.unreadable_result when the result has nothing readable', async () => {
    const { fetch } = harness({ content: [{ type: 'text', text: 'not json' }] });

    const response = await fetch('/api/v1/signals/abc/decision-plan');

    expect(response.ok).toBe(false);
    expect(response.status).toBe(502);
    expect((await response.json()).reasonCode).toBe(ShimReason.UnreadableResult);
  });
});

describe('createMcpFetch: an isError result', () => {
  it("is a non-2xx Response whose JSON carries the tool's reasonCode and message", async () => {
    const { fetch } = harness({
      isError: true,
      content: [{ type: 'text', text: 'Decide this in Invoice Review.' }],
      structuredContent: {
        reasonCode: 'ontology.decision.decided_in_invoice_review',
        message: 'Decide this in Invoice Review.',
        partial: { landed: [{ kind: 'signal', id: 's1' }] },
      },
    });

    const response = await fetch('/api/v1/signals/abc/decision', { method: 'POST', body: '{}' });
    const body = await response.json();

    expect(response.ok).toBe(false);
    expect(response.status).toBe(DEFAULT_TOOL_ERROR_STATUS);
    expect(body.reasonCode).toBe('ontology.decision.decided_in_invoice_review');
    expect(body.message).toBe('Decide this in Invoice Review.');
    expect(body.detail).toBe('Decide this in Invoice Review.'); // RFC 7807 readers keep working
    expect(body.partial).toEqual({ landed: [{ kind: 'signal', id: 's1' }] }); // the tool's own members pass through
  });

  it('honours an HTTP status the tool names (400-599) and ignores any other value', async () => {
    const named = harness({ isError: true, structuredContent: { reasonCode: 'x.y', message: 'm', status: 409 } });
    const bogus = harness({ isError: true, structuredContent: { reasonCode: 'x.y', message: 'm', status: 200 } });

    expect((await named.fetch('/api/v1/signals/a/decision-plan')).status).toBe(409);
    expect((await bogus.fetch('/api/v1/signals/a/decision-plan')).status).toBe(DEFAULT_TOOL_ERROR_STATUS);
  });

  it('falls back to widget.tool_error and the text content when the error has no structured body', async () => {
    const { fetch } = harness({ isError: true, content: [{ type: 'text', text: 'Something is wrong.' }] });

    const response = await fetch('/api/v1/signals/abc/decision-plan');
    const body = await response.json();

    expect(response.ok).toBe(false);
    expect(body.reasonCode).toBe(ShimReason.ToolError);
    expect(body.message).toBe('Something is wrong.');
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

  it('turns a rejected host call into a 502 Response instead of throwing', async () => {
    const { fetch } = harness(new Error('The host is not connected.'));

    const response = await fetch('/api/v1/signals/abc/decision-plan');
    const body = await response.json();

    expect(response.status).toBe(502);
    expect(body.reasonCode).toBe(ShimReason.HostCallFailed);
    expect(body.message).toBe('The host is not connected.');
  });
});

describe('createMcpFetch: it never touches the network', () => {
  it('does not call the global fetch, and there is no XMLHttpRequest, for any outcome', async () => {
    const globalFetch = jest.spyOn(globalThis, 'fetch');
    const outcomes: Array<McpToolResult | Error> = [
      { structuredContent: { ok: true } },
      { isError: true, structuredContent: { reasonCode: 'a.b', message: 'm' } },
      new Error('host down'),
    ];

    for (const outcome of outcomes) {
      await harness(outcome).fetch('/api/v1/signals/abc/decision-plan');
    }
    await harness({ structuredContent: {} }).fetch('/unmapped');

    expect(globalFetch).not.toHaveBeenCalled();
    expect(typeof XMLHttpRequest).toBe('undefined');
    globalFetch.mockRestore();
  });
});

describe('createMcpFetch with the shared Worklist service (the real consumer)', () => {
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
    const { fetch } = harness({
      isError: true,
      structuredContent: { reasonCode: 'x.denied', message: 'Not allowed.', status: 403 },
    });

    const failure = await createWorklistService({ fetch, baseUrl: '' })
      .read('lanes')
      .then(
        () => null,
        (err: unknown) => err
      );

    expect(failure).toBeInstanceOf(WorklistRequestError);
    expect((failure as WorklistRequestError).message).toBe('Not allowed.');
    expect((failure as WorklistRequestError).httpStatus).toBe(403);
  });
});

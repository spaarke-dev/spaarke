/**
 * @jest-environment node
 *
 * The shared widget MCP transport (spaarkeai-mcp-bff-api-r2 task 021, FR-16).
 *
 * - `createWidgetMcpTransport(...).callTool` resolves for every outcome: the tool's result on success and on a tool failure (with the
 *   failure's `reasonCode` lifted), and a no-`structuredContent` error result when there is no tool answer (a rejected or throwing
 *   bridge, a bridge that never answers, an answer that is not a result).
 * - It is assignable to task 011's `McpToolCaller` (checked by `tsc --noEmit` in the widget build, which includes this folder), and a
 *   bridge that never answers reaches the email host as an outcome-unknown `NETWORK` error (status 0), the reason the timeout exists.
 * - `createMcpFetch`'s `title` option: without it a failure carries the standard phrase (the Decisions widget's own titles are covered by
 *   its unchanged `mcpFetch.test.ts`).
 * - Source guard: no file under src/client/widgets/shared/ calls fetch, XHR, sockets or beacons, reads a token or MSAL, or uses browser
 *   storage, reads the page's colour scheme or writes a raw colour, with must-fire controls.
 *
 * The host bridge (`callServerTool`) is replaced by a plain function: it is the module boundary.
 */
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';
import type { McpToolCaller, McpToolCallResult } from '@spaarke/ui-components/components/EmailComposer/mcpToolCaller';
import { createMcpEmailComposeHandlers } from '@spaarke/ui-components/components/EmailComposer/createMcpEmailComposeHandlers';
import { SendCommunicationError } from '@spaarke/ui-components/services/communicationApi';
import {
  createWidgetMcpTransport,
  DEFAULT_CALL_TIMEOUT_MS,
  TransportReason,
  type WidgetMcpTransport,
  type WidgetToolResult,
} from '../widgetMcpTransport';
import { createMcpFetch, type CallServerTool, type McpRoute } from '../mcpFetch';

const never = (): Promise<never> => new Promise<never>(() => undefined);

describe('createWidgetMcpTransport.callTool', () => {
  it('returns the structuredContent of a successful call, for the tool and arguments it was given', async () => {
    const bridge = jest.fn<ReturnType<CallServerTool>, Parameters<CallServerTool>>(async () => ({
      structuredContent: { documents: [{ id: 'd1', name: 'Brief.docx' }] },
      content: [{ type: 'text', text: '1 document' }],
    }));
    const result = await createWidgetMcpTransport(bridge).callTool('spaarke_list_documents', { regardingId: 'm1' });

    expect(bridge).toHaveBeenCalledWith('spaarke_list_documents', { regardingId: 'm1' });
    expect(result.isError).toBeUndefined();
    expect(result.reasonCode).toBeUndefined();
    expect(result.structuredContent).toEqual({ documents: [{ id: 'd1', name: 'Brief.docx' }] });
    expect(result.content).toEqual([{ type: 'text', text: '1 document' }]);
  });

  it('an isError result yields isError true with the tool reasonCode, and keeps the failure body', async () => {
    const failure = {
      reasonCode: 'sdap.communication.forbidden',
      message: 'You cannot send from this matter.',
      status: 403,
      correlationId: 'corr-1',
    };
    const result = await createWidgetMcpTransport(async () => ({
      isError: true,
      structuredContent: failure,
      content: [{ type: 'text', text: failure.message }],
    })).callTool('spaarke_send_email', {});

    expect(result.isError).toBe(true);
    expect(result.reasonCode).toBe('sdap.communication.forbidden');
    expect(result.structuredContent).toEqual(failure);
  });

  it('a host bridge rejection surfaces as an error result with no structuredContent, not a rejected promise', async () => {
    const result = await createWidgetMcpTransport(() =>
      Promise.reject(new Error('The host is not connected.'))
    ).callTool('spaarke_send_email', {});

    expect(result).toEqual({
      isError: true,
      reasonCode: TransportReason.HostUnavailable,
      content: [{ type: 'text', text: 'The host is not connected.' }],
    });
  });

  it('a bridge that throws synchronously is a rejection like any other', async () => {
    const throwing: CallServerTool = () => {
      throw new Error('bridge gone');
    };
    const result = await createWidgetMcpTransport(throwing).callTool('x', {});
    expect(result.isError).toBe(true);
    expect(result.reasonCode).toBe(TransportReason.HostUnavailable);
    expect(result.structuredContent).toBeUndefined();
  });

  it('a bridge that never answers ends in a timeout error result, not a hang', async () => {
    const result = await createWidgetMcpTransport(never, { timeoutMs: 20 }).callTool('spaarke_send_email', {});

    expect(result.isError).toBe(true);
    expect(result.reasonCode).toBe(TransportReason.Timeout);
    expect(result.structuredContent).toBeUndefined();
    expect(result.content?.[0].text).toMatch(/outcome is unknown/);
  });

  it('an answer that is not a tool result is an error result with no structuredContent', async () => {
    const bridge = (async () => undefined) as unknown as CallServerTool;
    const result = await createWidgetMcpTransport(bridge).callTool('x', {});
    expect(result).toMatchObject({ isError: true, reasonCode: TransportReason.UnreadableResult });
    expect(result.structuredContent).toBeUndefined();
  });

  it('uses the default timeout for a timeout that is not a positive finite number', async () => {
    jest.useFakeTimers();
    try {
      const pending = createWidgetMcpTransport(never, { timeoutMs: 0 }).callTool('x', {});
      await jest.advanceTimersByTimeAsync(DEFAULT_CALL_TIMEOUT_MS - 1);
      let settled = false;
      void pending.then(() => {
        settled = true;
      });
      await jest.advanceTimersByTimeAsync(0);
      expect(settled).toBe(false);
      await jest.advanceTimersByTimeAsync(1);
      expect((await pending).reasonCode).toBe(TransportReason.Timeout);
    } finally {
      jest.useRealTimers();
    }
  });
});

describe("task 011's McpToolCaller", () => {
  it('the transport is assignable to McpToolCaller (compile-time; tsc --noEmit checks this file)', () => {
    const transport: WidgetMcpTransport = createWidgetMcpTransport(async () => ({ structuredContent: {} }));
    const caller: McpToolCaller = transport;
    const result: McpToolCallResult = {} as WidgetToolResult;

    // Control: the assertion can fail. A caller whose result has a non-object structuredContent is not an McpToolCaller.
    const wrong = { callTool: async () => ({ structuredContent: 'text' }) };
    // @ts-expect-error structuredContent must be an object
    const notACaller: McpToolCaller = wrong;

    expect([caller, result, notACaller]).toHaveLength(3);
  });

  it('a bridge that never answers reaches the email host as an outcome-unknown NETWORK error (status 0)', async () => {
    const host = createMcpEmailComposeHandlers({
      caller: createWidgetMcpTransport(never, { timeoutMs: 20 }),
      regarding: { id: 'm1', name: 'Matter 1' },
    });
    const request = {
      to: ['a@example.com'],
      subject: 's',
      body: 'b',
      bodyFormat: 'HTML',
      sendMode: 'SharedMailbox',
      associations: [{ entityType: 'sprk_matter', entityId: 'm1' }],
    };

    const sent = host.authenticatedFetch('/api/communications/send', { method: 'POST', body: JSON.stringify(request) });

    await expect(sent).rejects.toBeInstanceOf(SendCommunicationError);
    await expect(sent).rejects.toMatchObject({ status: 0, code: 'NETWORK' });
  });
});

describe('createMcpFetch title option', () => {
  const routes: McpRoute[] = [{ method: 'POST', path: '/api/things', tool: 'make_thing' }];
  const serverException: CallServerTool = async () => ({ isError: true, content: [{ type: 'text', text: 'boom' }] });

  it('without a title option a failure carries the standard phrase', async () => {
    const response = await createMcpFetch(serverException, routes)('/api/things', { method: 'POST' });
    expect(response.status).toBe(500);
    expect(await response.json()).toMatchObject({ title: 'Internal Server Error', detail: 'boom' });
  });

  it("the consumer's title replaces the standard phrase when it names one", async () => {
    const response = await createMcpFetch(serverException, routes, {
      title: status => (status === 500 ? 'The thing was not made' : undefined),
    })('/api/things', { method: 'POST' });
    expect((await response.json()).title).toBe('The thing was not made');
  });
});

/** The guard over every source file of the shared widget folder. */
const SHARED = resolve(__dirname, '../..');

function sourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap(name => {
    const full = join(dir, name);
    if (statSync(full).isDirectory()) return name === '__tests__' ? [] : sourceFiles(full);
    return /\.(ts|tsx)$/.test(name) ? [full] : [];
  });
}

/** Source without comments, so prose that names a forbidden API does not trip the guard. */
function code(text: string): string {
  return text.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1');
}

const NETWORK =
  /(?<![\w.])fetch\s*\(|\b(?:window|globalThis|self)\.fetch\b|XMLHttpRequest|\bEventSource\b|\bWebSocket\b|sendBeacon/;
const TOKEN =
  /Authorization|\bBearer\b|access_?token|id_?token|acquireToken|getToken|\bmsal\b|@azure\/msal|document\.cookie/i;
const STORAGE = /localStorage|sessionStorage|indexedDB|document\.cookie/;
// The Decisions widget's own surface guard applied these two to the transport before it moved here (ADR-021: theme from the host only).
const PAGE_THEME = /detectDarkModeFromNavbar|matchMedia|prefers-color-scheme/;
const RAW_COLOUR = /#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(/;

describe('shared widget source guard', () => {
  const files = sourceFiles(SHARED);
  const label = (f: string) => relative(SHARED, f).replace(/\\/g, '/');
  const offending = (pattern: RegExp) => files.filter(f => pattern.test(code(readFileSync(f, 'utf8')))).map(label);

  it('scans the real source files (not an empty set)', () => {
    expect(files.map(label)).toEqual(
      expect.arrayContaining([
        'mcp/widgetMcpTransport.ts',
        'mcp/mcpFetch.ts',
        'mcp/mcpHost.ts',
        'mcp/mcpHostContext.ts',
      ])
    );
  });

  it('calls fetch, XMLHttpRequest, sockets or beacons nowhere', () => {
    expect(offending(NETWORK)).toEqual([]);
  });

  it('reads no token and uses no MSAL', () => {
    expect(offending(TOKEN)).toEqual([]);
  });

  it('uses no browser storage', () => {
    expect(offending(STORAGE)).toEqual([]);
  });

  it('takes the theme from the host context only and writes no raw colour (ADR-021)', () => {
    expect(offending(PAGE_THEME)).toEqual([]);
    expect(offending(RAW_COLOUR)).toEqual([]);
  });

  it.each([
    [NETWORK, 'await fetch(url)'],
    [NETWORK, 'window.fetch'],
    [NETWORK, 'new XMLHttpRequest()'],
    [TOKEN, "import { PublicClientApplication } from '@azure/msal-browser'"],
    [TOKEN, 'msal.acquireTokenSilent(request)'],
    [STORAGE, 'localStorage.setItem("k", v)'],
    [PAGE_THEME, "window.matchMedia('(prefers-color-scheme: dark)')"],
    [RAW_COLOUR, "color: '#1a2b3c'"],
  ])('the guard fires on %s / %s', (pattern, text) => {
    expect(pattern.test(code(text))).toBe(true);
  });

  it.each([
    [NETWORK, 'createMcpFetch(callTool, routes)'],
    [NETWORK, 'app.callServerTool({})'],
    [TOKEN, 'Unauthorized'],
    [NETWORK, '/* fetch( */ const x = 1'],
  ])('the guard does not fire on %s / %s', (pattern, text) => {
    expect(pattern.test(code(text))).toBe(false);
  });
});

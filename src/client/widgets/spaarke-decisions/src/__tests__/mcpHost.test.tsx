/**
 * The widget's connection to its host (`mcp/mcpHost.ts`, task 030; first tested by task 031, wave 6 review): the handshake context is
 * merged with later changes (never replaced), a tool result is read from `structuredContent` only, `callTool` sends the SDK's
 * `{ name, arguments }` shape and rethrows what the host rejects with, and `openLink` answers whether the host opened the link.
 *
 * The MCP Apps SDK (`@modelcontextprotocol/ext-apps/react-with-deps`) is ESM-only and is the boundary of this module: it is replaced here
 * by a double whose `useApp` hands the provider a fake `App` and runs the provider's `onAppCreated`, exactly as the SDK does before the
 * handshake. Everything else is the real provider.
 */
import * as React from 'react';
import { act, render } from '@testing-library/react';
import { McpHostProvider, useMcpHost, type McpHostConnection } from '../mcp/mcpHost';

interface FakeApp {
  ontoolresult?: (result: { structuredContent?: unknown }) => void;
  onhostcontextchanged?: (changed: Record<string, unknown>) => void;
  getHostContext: jest.Mock;
  callServerTool: jest.Mock;
  openLink: jest.Mock;
}

const sdk = {
  app: null as FakeApp | null,
  isConnected: true,
  error: null as Error | null,
  created: null as FakeApp | null,
};

jest.mock(
  '@modelcontextprotocol/ext-apps/react-with-deps',
  () => ({
    useApp: (options: { onAppCreated?: (app: unknown) => void }) => {
      // The SDK creates the App once and calls `onAppCreated` before connecting.
      const [made] = React.useState(() => {
        if (!sdk.created) return null;
        options.onAppCreated?.(sdk.created);
        return sdk.created;
      });
      return { app: sdk.app ?? made, isConnected: sdk.isConnected, error: sdk.error };
    },
  }),
  { virtual: true }
);

function makeApp(hostContext: Record<string, unknown> | undefined): FakeApp {
  return {
    getHostContext: jest.fn(() => hostContext),
    callServerTool: jest.fn(),
    openLink: jest.fn(),
  };
}

/** Renders the provider and keeps the latest connection a child sees. */
function mount() {
  const seen: { current: McpHostConnection | null } = { current: null };
  function Probe() {
    seen.current = useMcpHost();
    return null;
  }
  const view = render(
    <McpHostProvider name="spaarke-decisions-test">
      <Probe />
    </McpHostProvider>
  );
  return { seen, view };
}

beforeEach(() => {
  sdk.app = null;
  sdk.created = null;
  sdk.isConnected = true;
  sdk.error = null;
});

describe('host context', () => {
  it('starts from the handshake context', () => {
    sdk.created = makeApp({ theme: 'dark', locale: 'en-GB' });
    const { seen } = mount();
    expect(seen.current?.hostContext).toEqual({ theme: 'dark', locale: 'en-GB' });
    expect(seen.current?.isConnected).toBe(true);
  });

  it('merges a change into the context, never replaces it', () => {
    sdk.created = makeApp({ theme: 'dark', locale: 'en-GB' });
    const { seen } = mount();

    act(() => sdk.created!.onhostcontextchanged?.({ theme: 'light' }));
    expect(seen.current?.hostContext).toEqual({ theme: 'light', locale: 'en-GB' });

    act(() => sdk.created!.onhostcontextchanged?.({ displayMode: 'fullscreen' }));
    expect(seen.current?.hostContext).toEqual({ theme: 'light', locale: 'en-GB', displayMode: 'fullscreen' });
  });

  it('keeps a change that arrived before the handshake context was read (the change is newer)', () => {
    const app = makeApp(undefined);
    // The host announces a change at the moment the provider reads the handshake context (the change is newer than that snapshot).
    app.getHostContext.mockImplementation(() => {
      app.onhostcontextchanged?.({ theme: 'light' });
      return { theme: 'dark', locale: 'en-GB' };
    });
    sdk.created = app;
    const { seen } = mount();
    expect(seen.current?.hostContext).toEqual({ theme: 'light', locale: 'en-GB' });
  });

  it('has no context when the host sent none', () => {
    sdk.created = makeApp(undefined);
    const { seen } = mount();
    expect(seen.current?.hostContext).toBeUndefined();
  });
});

describe('tool result', () => {
  it('is null until the host delivers one, then the structuredContent of the latest', () => {
    sdk.created = makeApp({});
    const { seen } = mount();
    expect(seen.current?.toolResult).toBeNull();

    act(() => sdk.created!.ontoolresult?.({ structuredContent: { consoleBaseUrl: 'https://org.crm.dynamics.com' } }));
    expect(seen.current?.toolResult).toEqual({ consoleBaseUrl: 'https://org.crm.dynamics.com' });

    act(() => sdk.created!.ontoolresult?.({ structuredContent: { total: 3 } }));
    expect(seen.current?.toolResult).toEqual({ total: 3 });
  });

  it('ignores a result with no structuredContent (the previous one stays)', () => {
    sdk.created = makeApp({});
    const { seen } = mount();
    act(() => sdk.created!.ontoolresult?.({ structuredContent: { total: 3 } }));
    act(() => sdk.created!.ontoolresult?.({}));
    expect(seen.current?.toolResult).toEqual({ total: 3 });
  });
});

describe('callTool', () => {
  it('sends the SDK shape { name, arguments } and returns the host result untouched', async () => {
    const app = makeApp({});
    const result = { structuredContent: { items: [] }, content: [{ type: 'text', text: 'ok' }] };
    app.callServerTool.mockResolvedValue(result);
    sdk.created = app;
    const { seen } = mount();

    await expect(seen.current!.callTool('spaarke_list_decision_page', { cursor: 'c1' })).resolves.toBe(result);
    expect(app.callServerTool).toHaveBeenCalledTimes(1);
    expect(app.callServerTool).toHaveBeenCalledWith({
      name: 'spaarke_list_decision_page',
      arguments: { cursor: 'c1' },
    });
  });

  it('rethrows when the host call rejects (the shim relies on it)', async () => {
    const app = makeApp({});
    app.callServerTool.mockRejectedValue(new Error('host went away'));
    sdk.created = app;
    const { seen } = mount();
    await expect(seen.current!.callTool('t', {})).rejects.toThrow('host went away');
  });

  it('rejects when the host is not connected (no App yet)', async () => {
    sdk.app = null;
    sdk.created = null;
    sdk.isConnected = false;
    const { seen } = mount();
    await expect(seen.current!.callTool('t', {})).rejects.toThrow('The host is not connected.');
  });
});

describe('openLink', () => {
  it('asks the host to open the url and reports it opened', async () => {
    const app = makeApp({});
    app.openLink.mockResolvedValue({});
    sdk.created = app;
    const { seen } = mount();
    await expect(seen.current!.openLink('https://org.crm.dynamics.com/x')).resolves.toBe(true);
    expect(app.openLink).toHaveBeenCalledWith({ url: 'https://org.crm.dynamics.com/x' });
  });

  it('reports false when the host refused (isError) and when there is no connection', async () => {
    const app = makeApp({});
    app.openLink.mockResolvedValue({ isError: true });
    sdk.created = app;
    const connected = mount();
    await expect(connected.seen.current!.openLink('https://x.test/')).resolves.toBe(false);
    connected.view.unmount();

    sdk.created = null;
    sdk.app = null;
    const disconnected = mount();
    await expect(disconnected.seen.current!.openLink('https://x.test/')).resolves.toBe(false);
  });
});

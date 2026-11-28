/**
 * The shared host connection's `toolInput` (`../../shared/mcp/mcpHost.ts`, spaarkeai-mcp-bff-api-r2 task 035): the arguments of the tool call
 * that opened the widget are kept as an object, anything else is not, and the connection's other members are unchanged.
 *
 * The MCP Apps SDK (`@modelcontextprotocol/ext-apps/react-with-deps`) is ESM-only and is the boundary of this module: it is replaced here by
 * a double whose `useApp` hands the provider a fake `App` and runs the provider's `onAppCreated`, exactly as the SDK does before the handshake.
 * Everything else is the real provider (the same double the Decisions widget's own host test uses).
 */
import * as React from 'react';
import { act, render } from '@testing-library/react';
import { McpHostProvider, useMcpHost, type McpHostConnection } from '../../../shared/mcp/mcpHost';

interface FakeApp {
  ontoolresult?: (result: { structuredContent?: unknown }) => void;
  ontoolinput?: (params: { arguments?: unknown }) => void;
  onhostcontextchanged?: (changed: Record<string, unknown>) => void;
  getHostContext: jest.Mock;
  callServerTool: jest.Mock;
  openLink: jest.Mock;
  requestDisplayMode: jest.Mock;
}

const sdk = { created: null as FakeApp | null };

jest.mock(
  '@modelcontextprotocol/ext-apps/react-with-deps',
  () => ({
    useApp: (options: { onAppCreated?: (app: unknown) => void }) => {
      const [made] = React.useState(() => {
        if (!sdk.created) return null;
        options.onAppCreated?.(sdk.created);
        return sdk.created;
      });
      return { app: made, isConnected: true, error: null };
    },
  }),
  { virtual: true }
);

function mount() {
  sdk.created = {
    getHostContext: jest.fn(() => undefined),
    callServerTool: jest.fn(),
    openLink: jest.fn(),
    requestDisplayMode: jest.fn(),
  };
  const seen: { current: McpHostConnection | null } = { current: null };
  function Probe() {
    seen.current = useMcpHost();
    return null;
  }
  render(
    <McpHostProvider name="spaarke-search-results-test">
      <Probe />
    </McpHostProvider>
  );
  return { seen, app: sdk.created };
}

describe('host connection: toolInput', () => {
  it('is undefined until the host sends the call arguments', () => {
    const { seen } = mount();
    expect(seen.current?.toolInput).toBeUndefined();
  });

  it('keeps the arguments the host sent, beside the result', () => {
    const { seen, app } = mount();

    act(() => app.ontoolinput?.({ arguments: { query: 'indemnification', limit: 5 } }));
    act(() => app.ontoolresult?.({ structuredContent: { items: [] } }));

    expect(seen.current?.toolInput).toEqual({ query: 'indemnification', limit: 5 });
    expect(seen.current?.toolResult).toEqual({ items: [] });
  });

  it.each([[undefined], [null], ['text'], [['a']], [7]])('ignores arguments that are %p (not an object)', value => {
    const { seen, app } = mount();
    act(() => app.ontoolinput?.({ arguments: { query: 'first' } }));

    act(() => app.ontoolinput?.({ arguments: value }));

    expect(seen.current?.toolInput).toBeUndefined();
  });
});

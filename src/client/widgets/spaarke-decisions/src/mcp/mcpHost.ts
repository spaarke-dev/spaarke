/**
 * The widget's connection to its MCP Apps host (spaarkeai-mcp-bff-api-r1 task 030, FR-12).
 *
 * Built on `@modelcontextprotocol/ext-apps` 2.0.4. Against the 1.x samples (trey-research `useMcpApp.tsx`) the calls this module uses are
 * unchanged: `useApp({ appInfo, capabilities, onAppCreated })`, `app.ontoolresult`, `app.onhostcontextchanged`, `app.getHostContext()`,
 * `app.callServerTool({ name, arguments })`, `app.openLink({ url })` (risk R13; notes/task-030-notes.md). The 2.x entry used is
 * `react-with-deps`, which bundles the SDK peers (`@modelcontextprotocol/client`, `core`, `zod`) that `--legacy-peer-deps` does not install.
 *
 * The widget holds no token and opens no connection of its own: everything goes through the host's postMessage bridge. The host is also
 * what the theme comes from (`hostContext.theme`, see `theme/hostTheme.ts`). Components read the connection through `useMcpHost`
 * (`mcpHostContext.ts`).
 */
import { createElement, useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useApp, type App } from '@modelcontextprotocol/ext-apps/react-with-deps';
import type { CallServerTool, McpToolResult } from './mcpFetch';
import { McpHostContext, type HostContext, type McpHostConnection } from './mcpHostContext';

export { McpHostContext, useMcpHost, type McpHostConnection } from './mcpHostContext';

export function McpHostProvider({ name, children }: { name: string; children: ReactNode }) {
  const [hostContext, setHostContext] = useState<HostContext | undefined>(undefined);
  const [toolResult, setToolResult] = useState<unknown>(null);

  const { app, isConnected, error } = useApp({
    appInfo: { name, version: '1.0.0' },
    capabilities: {},
    // Handlers are registered before the handshake so the first host context and tool result are not missed.
    onAppCreated: (created: App) => {
      created.ontoolresult = result => {
        if (result?.structuredContent !== undefined) setToolResult(result.structuredContent);
      };
      // A change carries only the fields that changed: merge, never replace.
      created.onhostcontextchanged = changed => setHostContext(previous => ({ ...previous, ...changed }));
    },
  });

  // The context the host sent during the handshake (init), before any change notification.
  useEffect(() => {
    if (!app) return;
    const initial = app.getHostContext();
    if (initial) setHostContext(previous => ({ ...initial, ...previous }));
  }, [app]);

  const appRef = useRef<App | null>(null);
  appRef.current = app;

  const callTool = useCallback<CallServerTool>(async (toolName, args) => {
    const current = appRef.current;
    if (!current) throw new Error('The host is not connected.');
    return (await current.callServerTool({ name: toolName, arguments: args })) as McpToolResult;
  }, []);

  const openLink = useCallback(async (url: string) => {
    const current = appRef.current;
    if (!current) return false;
    const result = await current.openLink({ url });
    return result.isError !== true;
  }, []);

  const value = useMemo<McpHostConnection>(
    () => ({ isConnected, error, hostContext, toolResult, callTool, openLink }),
    [isConnected, error, hostContext, toolResult, callTool, openLink]
  );

  return createElement(McpHostContext.Provider, { value }, children);
}

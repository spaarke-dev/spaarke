/**
 * What the rest of a widget sees of its MCP Apps host (shared, spaarkeai-mcp-bff-api-r2 task 021; written by r1 task 030). Kept apart
 * from `mcpHost.ts` (which imports the ext-apps SDK) so components and their tests depend on this shape only: a test renders a
 * component under `McpHostContext.Provider` with a plain object, no SDK needed.
 */
import { createContext, useContext } from 'react';
import type { CallServerTool } from './mcpFetch';

/** The slice of ext-apps' `McpUiHostContext` the widget reads (the SDK type satisfies it). */
export interface HostContext {
  theme?: string;
  [key: string]: unknown;
}

export interface McpHostConnection {
  /** The handshake with the host completed. */
  isConnected: boolean;
  /** The connection failed (handshake timeout or transport error). */
  error: Error | null;
  /** The latest host context (theme, display mode, ...); `undefined` until the host sends one. */
  hostContext: HostContext | undefined;
  /** `structuredContent` of the latest result the host delivered for the tool call that opened the widget; `null` until one arrives. */
  toolResult: unknown;
  /** Calls a server tool through the host. Rejects when the host is not connected. */
  callTool: CallServerTool;
  /** Asks the host to open a link in the user's browser (the widget declares no network origins and never navigates itself). */
  openLink: (url: string) => Promise<boolean>;
  /**
   * Asks the host to change the display mode (the wizard does not use it: it shows inline, owner decision 2026-11-24). Resolves with the mode the host actually set,
   * which may differ from the one asked for, or `null` when the host is not connected or refused the request outright. Never rejects.
   */
  requestDisplayMode: (mode: DisplayMode) => Promise<DisplayMode | null>;
}

/** The display modes of the MCP Apps spec. */
export type DisplayMode = 'inline' | 'fullscreen' | 'pip';

export const DISCONNECTED: McpHostConnection = {
  isConnected: false,
  error: null,
  hostContext: undefined,
  toolResult: null,
  callTool: () => Promise.reject(new Error('The host is not connected.')),
  openLink: () => Promise.resolve(false),
  requestDisplayMode: () => Promise.resolve(null),
};

export const McpHostContext = createContext<McpHostConnection>(DISCONNECTED);

/** The host connection. Outside a provider it is a disconnected stub, so a component renders (and tests) without a host. */
export function useMcpHost(): McpHostConnection {
  return useContext(McpHostContext);
}

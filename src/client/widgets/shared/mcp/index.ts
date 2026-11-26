/**
 * The shared MCP transport for Spaarke MCP App widgets (spaarkeai-mcp-bff-api-r2 task 021, FR-16). See ../README.md.
 *
 * This barrel includes `McpHostProvider`, which imports the ext-apps SDK (ESM-only). A component, or a test that should not load the
 * SDK, imports the deep paths instead: `mcpHostContext` (the connection shape and `useMcpHost`), `widgetMcpTransport`, `mcpFetch`.
 */
export { McpHostProvider } from './mcpHost';
export {
  DISCONNECTED,
  McpHostContext,
  useMcpHost,
  type DisplayMode,
  type HostContext,
  type McpHostConnection,
} from './mcpHostContext';
export {
  createWidgetMcpTransport,
  useWidgetMcpTransport,
  DEFAULT_CALL_TIMEOUT_MS,
  TransportReason,
  type WidgetMcpTransport,
  type WidgetMcpTransportOptions,
  type WidgetToolResult,
} from './widgetMcpTransport';
export {
  createMcpFetch,
  ShimReason,
  DEFAULT_TOOL_ERROR_STATUS,
  NON_DOMAIN_ERROR_STATUS,
  type CallServerTool,
  type McpFetchOptions,
  type McpRequest,
  type McpRoute,
  type McpToolResult,
} from './mcpFetch';

/**
 * What the widget sees of its MCP Apps host: the shared connection shape (`src/client/widgets/shared/mcp/mcpHostContext.ts`,
 * spaarkeai-mcp-bff-api-r2 task 021, FR-16), re-exported so the widget's imports stay put. It does not load the ext-apps SDK, so a test
 * renders a component under `McpHostContext.Provider` with a plain object.
 */
export {
  DISCONNECTED,
  McpHostContext,
  useMcpHost,
  type DisplayMode,
  type HostContext,
  type McpHostConnection,
} from '../../../shared/mcp/mcpHostContext';

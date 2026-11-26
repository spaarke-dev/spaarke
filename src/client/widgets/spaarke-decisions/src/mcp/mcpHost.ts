/**
 * The widget's connection to its MCP Apps host (spaarkeai-mcp-bff-api-r1 task 030, FR-12): the shared provider
 * (`src/client/widgets/shared/mcp/mcpHost.ts`, spaarkeai-mcp-bff-api-r2 task 021, FR-16), re-exported so the widget's imports stay put.
 */
export { McpHostProvider, McpHostContext, useMcpHost, type McpHostConnection } from '../../../shared/mcp/mcpHost';

/**
 * mcpToolCaller.ts - the injected tool-call transport the MCP email host talks through.
 *
 * A local structural type, not an `@modelcontextprotocol/*` import (ADR-012: the shared component library takes
 * its transport by injection). The Copilot compose widget supplies a real caller over its MCP Apps bridge
 * (spaarkeai-mcp-bff-api-r2 task 021); tests supply a fake.
 */

/** A tool result as the host reads it: the MCP `CallToolResult` fields the email host uses. */
export interface McpToolCallResult {
  /** The tool's structured payload (success). */
  structuredContent?: Record<string, unknown>;
  /** True when the tool refused or failed; the REST-equal code is in {@link reasonCode}. */
  isError?: boolean;
  /** The REST `reasonCode` of a refusal (for example `sdap.communication.*`). */
  reasonCode?: string;
  /** Text content blocks; an error's message is read from the first text block. */
  content?: ReadonlyArray<{ type: string; text?: string }>;
}

/** The transport: call a tool by name with JSON arguments. A rejected promise is a transport failure (no result). */
export interface McpToolCaller {
  callTool(name: string, args: Record<string, unknown>): Promise<McpToolCallResult>;
}

/** Tool names the host calls (implemented server-side by tasks 023 and 040). */
export const MCP_TOOL_LIST_DOCUMENTS = 'spaarke_list_documents';
export const MCP_TOOL_SEND_EMAIL = 'spaarke_send_email';

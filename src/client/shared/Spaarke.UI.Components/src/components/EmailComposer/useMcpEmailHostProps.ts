/**
 * useMcpEmailHostProps - the MCP-backed host wiring for an `EmailComposer` mount in a Copilot widget; the sibling of
 * `useXrmEmailHostProps`. It builds `createMcpEmailComposeHandlers` once per compose session, so the idempotency key
 * is generated once and reused by every Send, including a retry. No Xrm (ADR-012). Spread the result into
 * `EmailComposer`.
 *
 * Left out on purpose (v1): `onSearchRecipients`, `onLookupRecipients`, `onAddRelationship`, template, AI draft and
 * local-upload handlers. The engine hides each control whose handler is undefined.
 */
import * as React from 'react';
import {
  createMcpEmailComposeHandlers,
  type McpEmailComposeHandlers,
  type McpEmailRegardingMatter,
  type McpMatterDocument,
} from './createMcpEmailComposeHandlers';
import type { McpToolCaller } from './mcpToolCaller';

export type McpEmailHostProps = McpEmailComposeHandlers;

export interface UseMcpEmailHostPropsOptions {
  /** The tool-call transport. Pass a stable reference (the handlers are rebuilt, with a NEW key, if it changes). */
  caller: McpToolCaller;
  /** The regarding matter (required, preset by the host). A different matter is a new compose session. */
  regarding: McpEmailRegardingMatter;
  /** The widget's matter-document picker UI. Omitted = no attach control. */
  pickDocument?: (documents: McpMatterDocument[]) => Promise<McpMatterDocument | null>;
}

export function useMcpEmailHostProps({
  caller,
  regarding,
  pickDocument,
}: UseMcpEmailHostPropsOptions): McpEmailHostProps {
  // A ref keeps a re-rendered inline `pickDocument` from rebuilding the handlers (which would mint a new key).
  const pickRef = React.useRef(pickDocument);
  pickRef.current = pickDocument;
  const hasPicker = !!pickDocument;

  return React.useMemo(
    () =>
      createMcpEmailComposeHandlers({
        caller,
        regarding: { id: regarding.id, name: regarding.name },
        pickDocument: hasPicker ? docs => pickRef.current?.(docs) ?? Promise.resolve(null) : undefined,
      }),
    [caller, regarding.id, regarding.name, hasPicker]
  );
}

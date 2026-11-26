/**
 * useMcpEmailHostProps - the MCP-backed host wiring for an `EmailComposer` mount in a Copilot widget; the sibling of
 * `useXrmEmailHostProps`. No Xrm (ADR-012). Spread the result into `EmailComposer` (it carries the engine switches that
 * hide the controls the send tool cannot honour: `sendMode`, `showBcc`, `showInternalOnly`, `associationsRemovable`).
 *
 * Idempotency key lifetime (the key makes a retried Send safe, so it must not move with unrelated things):
 *  - one key per compose session, kept in a ref that resets ONLY when `regarding.id` changes;
 *  - `caller` and `pickDocument` are read through refs, so a new transport object or an inline picker never rebuilds
 *    the key, and a late-arriving matter `name` rebuilds only the "Related to" label;
 *  - a Send that returned a `communicationId` spends the key: the next Send gets a new one. A failed Send keeps it,
 *    so pressing Send again after a timeout is the same request, not a second email.
 *
 * Left out on purpose (v1): `onSearchRecipients`, `onLookupRecipients`, `onAddRelationship`, template, AI draft and
 * local-upload handlers. The engine hides each control whose handler is undefined.
 */
import * as React from 'react';
import {
  createMcpEmailComposeHandlers,
  newIdempotencyKey,
  type McpDocumentListOutcome,
  type McpEmailComposeHandlers,
  type McpEmailRegardingMatter,
  type McpMatterDocument,
} from './createMcpEmailComposeHandlers';
import type { McpToolCaller } from './mcpToolCaller';

export type McpEmailHostProps = McpEmailComposeHandlers;

export interface UseMcpEmailHostPropsOptions {
  /** The tool-call transport. Read through a ref: a new object each render is fine. */
  caller: McpToolCaller;
  /** The regarding matter (required, preset by the host). A different `id` is a new compose session. */
  regarding: McpEmailRegardingMatter;
  /**
   * The widget's matter-document picker UI, handed the documents or an error to show. Omitted = no attach control.
   * Read through a ref; only its presence matters to the handlers.
   */
  pickDocument?: (outcome: McpDocumentListOutcome) => Promise<McpMatterDocument | null>;
}

interface SessionKey {
  regardingId: string;
  key: string;
}

export function useMcpEmailHostProps({
  caller,
  regarding,
  pickDocument,
}: UseMcpEmailHostPropsOptions): McpEmailHostProps {
  const callerRef = React.useRef(caller);
  callerRef.current = caller;
  const pickRef = React.useRef(pickDocument);
  pickRef.current = pickDocument;
  const hasPicker = !!pickDocument;

  // The transport the handlers hold never changes identity; it forwards to the latest caller.
  const stableCaller = React.useMemo<McpToolCaller>(
    () => ({ callTool: (name, args) => callerRef.current.callTool(name, args) }),
    []
  );

  const sessionRef = React.useRef<SessionKey | null>(null);
  if (!sessionRef.current || sessionRef.current.regardingId !== regarding.id) {
    sessionRef.current = { regardingId: regarding.id, key: newIdempotencyKey() };
  }

  return React.useMemo(() => {
    const regardingId = regarding.id;
    return createMcpEmailComposeHandlers({
      caller: stableCaller,
      regarding: { id: regardingId, name: regarding.name },
      pickDocument: hasPicker ? outcome => pickRef.current?.(outcome) ?? Promise.resolve(null) : undefined,
      idempotencyKey: () => sessionRef.current?.key ?? '',
      onSent: () => {
        // Only the session this handler set belongs to: a Send resolving after the matter changed must not rotate the new one.
        if (sessionRef.current?.regardingId === regardingId) {
          sessionRef.current = { regardingId, key: newIdempotencyKey() };
        }
      },
    });
  }, [stableCaller, regarding.id, regarding.name, hasPicker]);
}

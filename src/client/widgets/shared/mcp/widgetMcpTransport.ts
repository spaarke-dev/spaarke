/**
 * The direct tool-call transport every Spaarke MCP App widget shares (spaarkeai-mcp-bff-api-r2 task 021, FR-16).
 *
 * `callTool(name, args)` calls a server tool through the host's `callServerTool` (the postMessage bridge; the widget opens no connection
 * and holds no token) and ALWAYS resolves with a result the caller can read, in the shape of task 011's `McpToolCaller`
 * (`@spaarke/ui-components` EmailComposer `mcpToolCaller.ts`):
 *
 *  - success: `{ structuredContent, content }` as the tool answered (`structuredContent` only when it is a JSON object);
 *  - a tool failure (`isError: true`): passed through, with `structuredContent = { reasonCode, message, status, correlationId? }`
 *    (McpToolResults.cs) and that `reasonCode` lifted to the top level;
 *  - the host call rejecting (host not connected, transport error, the SDK's own request timeout), the host not answering within
 *    `timeoutMs`, or an answer that is not a result: `{ isError: true, reasonCode: TransportReason.*, content: [text] }` with NO
 *    `structuredContent`. `content` is always fixed, user-safe text (a caller may show it); the bridge's own error text, which can read
 *    like "MCP error -32001: Request timed out", is kept in `diagnostic` for logs and is never put in `content`. That is the "no domain answer" shape, so the outcome is unknown: task 011's email host reads it as
 *    `SendCommunicationError(0, 'NETWORK')` ("may not have been sent"), never as a refusal. A late answer after the timeout is dropped.
 *
 * Use it where a widget calls tools itself. A widget that runs a shared `@spaarke/ui-components` service over an injected `fetch` uses
 * `createMcpFetch` (`mcpFetch.ts`), which keeps the REST error shapes and lets a rejection through as a `fetch` network failure; the
 * Decisions widget does that, and its calls are not routed through this file (its error handling stays as it was).
 */
import { useMemo } from 'react';
import type { CallServerTool } from './mcpFetch';
import { useMcpHost } from './mcpHostContext';

/** `reasonCode` values the transport itself produces when there is no tool answer (always with no `structuredContent`). */
export const TransportReason = {
  /** The host call rejected: not connected, a transport error, or the SDK's own request timeout. */
  HostUnavailable: 'widget.host_unavailable',
  /** The host did not answer within `timeoutMs`. */
  Timeout: 'widget.host_timeout',
  /** The host answered with something that is not a tool result. */
  UnreadableResult: 'widget.unreadable_result',
} as const;

/**
 * How long `callTool` waits for the host before it gives up (90 s). The ext-apps SDK's own request timeout (60 s) normally answers
 * first, as a rejection; this is the backstop for a bridge that never answers at all.
 */
export const DEFAULT_CALL_TIMEOUT_MS = 90_000;

/** A tool result as a widget reads it (structurally the same as task 011's `McpToolCallResult`). */
export interface WidgetToolResult {
  /** The tool's structured payload (success), or the failure body `{ reasonCode, message, status, correlationId? }`. */
  structuredContent?: Record<string, unknown>;
  /** True when the tool refused or failed, or when there was no tool answer (then there is no `structuredContent`). */
  isError?: boolean;
  /** The failure's `reasonCode`: the tool's own, or a `TransportReason` value when there was no tool answer. */
  reasonCode?: string;
  /** The tool's content blocks; for a transport failure, one fixed, user-safe text block (never the bridge's own error text). */
  content?: ReadonlyArray<{ type: string; text?: string }>;
  /** Transport failures only: the host call's own error text, for diagnosis. Nothing displays it; it is never copied into `content`. */
  diagnostic?: string;
}

export interface WidgetMcpTransport {
  /** Calls a server tool through the host. Never rejects and never waits longer than the timeout (see the file header). */
  callTool(name: string, args: Record<string, unknown>): Promise<WidgetToolResult>;
}

export interface WidgetMcpTransportOptions {
  /** How long to wait for the host, in milliseconds; a value that is not a positive finite number means the default. */
  timeoutMs?: number;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isContentBlock(value: unknown): value is { type: string; text?: string } {
  return isRecord(value) && typeof value.type === 'string';
}

function noAnswer(reasonCode: string, text: string, diagnostic?: string): WidgetToolResult {
  return { isError: true, reasonCode, content: [{ type: 'text', text }], ...(diagnostic ? { diagnostic } : {}) };
}

/** The wait in whole seconds, rounded up, as a phrase (a timeout under a second is "1 second", never "0 seconds"). */
function secondsPhrase(timeoutMs: number): string {
  const seconds = Math.max(1, Math.ceil(timeoutMs / 1000));
  return `${seconds} ${seconds === 1 ? 'second' : 'seconds'}`;
}

/** The transport result for what the host answered. */
function readResult(answer: unknown): WidgetToolResult {
  if (!isRecord(answer)) {
    return noAnswer(TransportReason.UnreadableResult, 'The tool answered without a readable result.');
  }
  const structuredContent = isRecord(answer.structuredContent) ? answer.structuredContent : undefined;
  const content = Array.isArray(answer.content) ? answer.content.filter(isContentBlock) : undefined;
  const result: WidgetToolResult = {
    ...(structuredContent ? { structuredContent } : {}),
    ...(content ? { content } : {}),
  };
  if (answer.isError === true) {
    result.isError = true;
    const reasonCode = structuredContent?.reasonCode;
    if (typeof reasonCode === 'string' && reasonCode) result.reasonCode = reasonCode;
  }
  return result;
}

const TIMED_OUT = Symbol('timed out');

/** Builds the transport over the host's `callServerTool` (the `callTool` of `useMcpHost()`, or a test double). */
export function createWidgetMcpTransport(
  callServerTool: CallServerTool,
  options: WidgetMcpTransportOptions = {}
): WidgetMcpTransport {
  const requested = options.timeoutMs;
  const timeoutMs =
    typeof requested === 'number' && Number.isFinite(requested) && requested > 0 ? requested : DEFAULT_CALL_TIMEOUT_MS;

  return {
    async callTool(name, args) {
      let timer: ReturnType<typeof setTimeout> | undefined;
      const timedOut = new Promise<typeof TIMED_OUT>(resolve => {
        timer = setTimeout(() => resolve(TIMED_OUT), timeoutMs);
      });
      try {
        // A synchronous throw from the bridge is a rejection like any other.
        const call = Promise.resolve().then(() => callServerTool(name, args));
        // A rejection that arrives after the timeout has nobody waiting for it. `Promise.race` below already subscribes to `call`, so it
        // is never reported as unhandled; this line states that intent and keeps it true if the race is ever restructured.
        call.catch(() => undefined);
        const answer = await Promise.race([call, timedOut]);
        if (answer === TIMED_OUT) {
          return noAnswer(
            TransportReason.Timeout,
            `The host did not answer within ${secondsPhrase(timeoutMs)}, so the outcome is unknown.`
          );
        }
        return readResult(answer);
      } catch (error) {
        // The bridge's own text is for diagnosis only: it is not written for the people who read `content`.
        return noAnswer(
          TransportReason.HostUnavailable,
          'The host did not answer.',
          error instanceof Error && error.message ? error.message : undefined
        );
      } finally {
        clearTimeout(timer);
      }
    },
  };
}

/** The transport over the widget's host connection (`McpHostProvider`), stable while the connection's `callTool` is. */
export function useWidgetMcpTransport(options: WidgetMcpTransportOptions = {}): WidgetMcpTransport {
  const { callTool } = useMcpHost();
  const { timeoutMs } = options;
  return useMemo(() => createWidgetMcpTransport(callTool, { timeoutMs }), [callTool, timeoutMs]);
}

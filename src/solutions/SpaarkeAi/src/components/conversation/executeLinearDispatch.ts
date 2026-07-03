/**
 * executeLinearDispatch.ts — R7 Wave 12.3 Phase 12.3a (2026-07-03).
 *
 * Companion to `executeSummarizeIntent.ts` for the LINEAR-DISPATCH SSE event
 * pathway. Unlike `executeSummarizeIntent`, files are ALREADY promoted
 * server-side (the `linear_dispatch` event carries `sessionAttachmentIds`
 * referring to existing session-file rows) and the request body is pre-composed
 * by the BFF. All the client does is:
 *
 *   1. Emit `workspace.widget_load` with the appropriate output-schema so
 *      the Summary tab is installed for THIS run (per-run tab per FR-06,
 *      B-G9c2 B7+B8).
 *   2. POST to `dispatchUrl` with `requestBody` verbatim (Content-Type
 *      application/json).
 *   3. Read the response as `text/event-stream`, parse `AnalysisChunk` events,
 *      bridge them into `workspace.streaming_*` PaneEventBus events.
 *
 * The consumer-type discrimination lets this handler evolve to route other
 * linear consumers (matter-pre-fill, project-pre-fill, document-profile,
 * summarize-file) into different tab shapes. For Phase 12.3a we only handle
 * `chat-summarize` (routes to the Summary tab).
 *
 * @see ADR-028 — Spaarke Auth v2; never snapshot tokens
 * @see ADR-030 — PaneEventBus channels closed at 4
 * @see ADR-019 — ProblemDetails error contract
 * @see executeSummarizeIntent.ts — sibling for the slash-command path
 * @see LinearDispatchSseEvent.cs — BFF event source
 */

import {
  createSseToPaneEventBridge,
  type AnalysisChunk,
} from './sseToPaneEventBridge';
import type {
  WorkspacePaneEvent,
  PaneChannel,
  PaneChannelEventMap,
  StructuredOutputStreamWidgetData,
} from '@spaarke/ai-widgets';
import {
  STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE,
  SUMMARIZE_SCHEMA,
  SUM_CHAT_OUTPUT_SCHEMA,
} from '@spaarke/ai-widgets';

/**
 * Inputs the ConversationPane host must provide for a linear-dispatch execution.
 */
export interface ExecuteLinearDispatchInputs {
  /** Consumer-type key from the SSE payload (e.g. `"chat-summarize"`). */
  readonly consumerType: string;
  /** Server-relative URL to POST to (from the SSE payload). */
  readonly dispatchUrl: string;
  /** Pre-composed JSON request body string (from the SSE payload). */
  readonly requestBody: string;
  /** Session file IDs correlating this run (from the SSE payload). */
  readonly sessionAttachmentIds: ReadonlyArray<string>;
  /** BFF API base URL. `dispatchUrl` is server-relative and joined against this. */
  readonly bffBaseUrl: string;
  /**
   * Fresh access-token getter (Auth v2 / ADR-028). Called ONCE per stream
   * open immediately before opening the fetch. NEVER snapshot the token.
   */
  readonly getAccessToken: () => Promise<string>;
  /**
   * Publisher for PaneEventBus events. Typically wired from
   * `useDispatchPaneEvent()`. The orchestrator emits:
   *   - `workspace.widget_load` (installs the Summary tab)
   *   - `workspace.streaming_started` / `field_delta` / `streaming_complete`
   *     during the SSE stream
   */
  readonly publishPaneEvent: <C extends PaneChannel>(
    channel: C,
    event: PaneChannelEventMap[C]
  ) => void;
  /** Optional AbortSignal forwarded to the fetch + SSE stream. */
  readonly signal?: AbortSignal;
  /**
   * Optional stream id; defaults to a random one. Subscribers use this to
   * disambiguate concurrent streams.
   */
  readonly streamId?: string;
  /**
   * Optional display title for the tab (used verbatim). When omitted the
   * consumer-type default is used ("Summary" for `chat-summarize`).
   */
  readonly displayName?: string;
}

/**
 * Result of a successful execution.
 */
export interface ExecuteLinearDispatchResult {
  /** Stream id emitted across all `workspace.streaming_*` events. */
  readonly streamId: string;
  /** Consumer-type key that was dispatched. */
  readonly consumerType: string;
}

function generateStreamId(consumerType: string): string {
  return `${consumerType}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
}

/**
 * Resolve the workspace-widget config for a consumer type. Right now only
 * `chat-summarize` is served by this pathway; add more consumers here as they
 * migrate off `playbook_options` onto explicit-intent auto-dispatch.
 */
function resolveWidgetConfig(consumerType: string): {
  widgetType: string;
  schema: unknown;
  outputSchema: unknown;
  defaultDisplayName: string;
} | null {
  if (consumerType === 'chat-summarize') {
    return {
      widgetType: STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE,
      schema: SUMMARIZE_SCHEMA,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
      defaultDisplayName: 'Summary',
    };
  }
  return null;
}

/**
 * Runs the linear-dispatch flow: install workspace tab + POST + stream + bridge.
 *
 * Throws on any /dispatchUrl error, SSE network error, or terminal `error`
 * chunk. Caller surfaces a chat-thread error chip.
 */
export async function executeLinearDispatch(
  inputs: ExecuteLinearDispatchInputs
): Promise<ExecuteLinearDispatchResult> {
  const {
    consumerType,
    dispatchUrl,
    requestBody,
    bffBaseUrl,
    getAccessToken,
    publishPaneEvent,
    signal,
  } = inputs;

  if (!consumerType) {
    throw new Error('executeLinearDispatch: consumerType is required');
  }
  if (!dispatchUrl) {
    throw new Error('executeLinearDispatch: dispatchUrl is required');
  }

  const widgetConfig = resolveWidgetConfig(consumerType);
  if (!widgetConfig) {
    throw new Error(
      `executeLinearDispatch: no workspace widget configured for consumerType=${consumerType}`
    );
  }

  const streamId = inputs.streamId ?? generateStreamId(consumerType);
  const tabDisplayName = inputs.displayName ?? widgetConfig.defaultDisplayName;

  // ───────────────────────────────────────────────────────────────────────
  // Step 1: Emit workspace.widget_load (install Summary tab for THIS run)
  // ───────────────────────────────────────────────────────────────────────
  const widgetData: StructuredOutputStreamWidgetData & {
    fileIds?: string[];
  } = {
    mode: 'streaming',
    schema: widgetConfig.schema as StructuredOutputStreamWidgetData['schema'],
    outputSchema: widgetConfig.outputSchema as StructuredOutputStreamWidgetData['outputSchema'],
    correlationId: streamId,
    title: tabDisplayName,
    fileIds: inputs.sessionAttachmentIds.slice(),
  };

  publishPaneEvent('workspace', {
    type: 'widget_load',
    widgetType: widgetConfig.widgetType,
    widgetData,
    displayName: tabDisplayName,
  });

  // ───────────────────────────────────────────────────────────────────────
  // Step 2: POST to dispatchUrl with the pre-composed request body
  // ───────────────────────────────────────────────────────────────────────
  // Auth v2 §H-4 / D-AUTH-7: fresh token per stream open. NEVER snapshot.
  const token = await getAccessToken();

  // dispatchUrl is server-relative (`/api/ai/chat/sessions/{id}/summarize`);
  // join against bffBaseUrl. The BFF composed the URL, we do not interpret it.
  const fullUrl = dispatchUrl.startsWith('http')
    ? dispatchUrl
    : `${bffBaseUrl.replace(/\/$/, '')}${dispatchUrl.startsWith('/') ? '' : '/'}${dispatchUrl}`;

  const response = await fetch(fullUrl, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    },
    body: requestBody,
    signal,
  });

  if (!response.ok) {
    let errorCode = 'linear_dispatch.failed';
    try {
      const problem = (await response.json()) as { errorCode?: string };
      if (problem && typeof problem.errorCode === 'string') {
        errorCode = problem.errorCode;
      }
    } catch {
      // Non-JSON body — fall through.
    }
    publishPaneEvent('workspace', {
      type: 'streaming_complete',
      streamId,
      completionStatus: 'declined',
    });
    throw new Error(
      `executeLinearDispatch: POST ${fullUrl} failed (status=${response.status}, errorCode=${errorCode})`
    );
  }

  if (!response.body) {
    publishPaneEvent('workspace', {
      type: 'streaming_complete',
      streamId,
      completionStatus: 'declined',
    });
    throw new Error('executeLinearDispatch: response body is empty');
  }

  // ───────────────────────────────────────────────────────────────────────
  // Step 3: Consume SSE stream and bridge to workspace pane events
  // ───────────────────────────────────────────────────────────────────────
  const bridge = createSseToPaneEventBridge(streamId);
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let sawError = false;
  let sawComplete = false;

  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });

      const parts = buffer.split('\n\n');
      buffer = parts.pop() ?? '';

      for (const part of parts) {
        const dataLines: string[] = [];
        for (const line of part.split('\n')) {
          if (line.startsWith('data:')) {
            dataLines.push(line.slice(5).trimStart());
          }
        }
        if (dataLines.length === 0) continue;

        const json = dataLines.join('\n');
        let chunk: AnalysisChunk;
        try {
          chunk = JSON.parse(json) as AnalysisChunk;
        } catch {
          continue;
        }

        const busEvents = bridge.consume(chunk);
        for (const ev of busEvents) {
          publishPaneEvent('workspace', ev);
        }

        if (chunk.type === 'error') sawError = true;
        if (chunk.type === 'complete') sawComplete = true;
      }
    }

    if (!sawError && !sawComplete) {
      publishPaneEvent('workspace', {
        type: 'streaming_complete',
        streamId,
        completionStatus: 'empty',
      });
    }
  } catch (err) {
    publishPaneEvent('workspace', {
      type: 'streaming_complete',
      streamId,
      completionStatus: 'declined',
    } satisfies WorkspacePaneEvent);
    throw err;
  } finally {
    try {
      reader.releaseLock();
    } catch {
      // Cleanup-tail; safe to ignore.
    }
  }

  if (sawError) {
    throw new Error('executeLinearDispatch: stream emitted an error chunk');
  }

  return { streamId, consumerType };
}

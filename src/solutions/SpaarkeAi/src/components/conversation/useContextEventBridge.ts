/**
 * useContextEventBridge — R6 Pillar 6c / task 095 trace bridge, extracted from
 * ConversationPane.tsx by ai-architecture-redesign-r1 task 045 (FR-P3-06).
 *
 * Receives `context_event` SSE payloads from SprkChat and dispatches each one
 * to the `context` PaneEventBus channel where ExecutionTraceWidget renders it.
 * The `consumer_chips` discriminant (task-022 chip SSE contract) is
 * conversation-surface UI — forwarded to the consumer-chips controller, not
 * the bus.
 *
 * ADR-015: log STRUCTURAL signals (event type discriminant) only.
 * ADR-030: additive event types on the existing `context` channel.
 */

import * as React from "react";
import type { ContextPaneEvent, WorkspacePaneEvent, BufferedTraceEvent } from "@spaarke/ai-widgets";
import { recordExecutionTraceEvent } from "@spaarke/ai-widgets";

export interface ContextEventPayload {
  contextEventType?: string;
  contextTimestamp?: string;
  contextToolName?: string;
  contextDecisionId?: string;
  contextOutcome?: string;
  contextDurationMs?: number;
  contextKnowledgeSourceId?: string;
  contextRelevanceScore?: number;
  contextResultCount?: number;
  contextPlaybookId?: string;
  contextNodeId?: string;
  contextNodeType?: string;
  contextLayer?: string;
  contextDecision?: string;
  contextCapabilityName?: string;
  contextChips?: ReadonlyArray<Record<string, unknown>>;
  // tool_chain (task 046 / FR-P3-07): the PERSISTED session-ledger ToolChain
  // segment (ADR-040 storage-precedes-rendering). Identifiers/counts only.
  contextTurn?: number;
  contextToolChainCalls?: ReadonlyArray<{
    toolId?: string;
    argsSummary?: string;
    resultCount?: number;
    citationCount?: number;
    durationMs?: number;
  }>;
  // workspace_open_tab (G-P3 UAT round-2 R2-D, 2026-07-07): server-side
  // SendWorkspaceArtifactHandler asks the client to open a workspace tab LIVE
  // (e.g. the Compose layout). Bridged onto the SAME PaneEventBus
  // `workspace.widget_load` mechanism the Workspaces menu uses.
  contextWidgetType?: string;
  contextDisplayName?: string;
  contextTabId?: string;
  contextWidgetDataJson?: string;
}

export interface ContextEventBridgeDeps {
  /** `context`-channel PaneEventBus publisher. */
  dispatch: (channel: "context", event: ContextPaneEvent) => void;
  /**
   * `workspace`-channel PaneEventBus publisher (R2-D `workspace_open_tab`
   * bridge). Structurally the same dispatcher as `dispatch` — typed separately
   * so the workspace leg is explicit.
   */
  dispatchWorkspace: (channel: "workspace", event: WorkspacePaneEvent) => void;
  /** Consumer-chips controller acceptor (`consumer_chips` carrier). */
  acceptChips: (raw: unknown) => void;
}

export function useContextEventBridge(deps: ContextEventBridgeDeps): {
  handleContextEvent: (data: ContextEventPayload) => void;
} {
  const { dispatch, dispatchWorkspace, acceptChips } = deps;

  const handleContextEvent = React.useCallback(
    (data: ContextEventPayload): void => {
      const eventType = data.contextEventType;
      if (!eventType) return;

      // ADR-015 telemetry: log discriminant only — never typed-field values.
      console.log("[ConversationPane] context_event received — type:%s", eventType);

      // Click-path chips (task 023 / FR-P1-04): conversation-surface UI —
      // replace-only-when-non-empty (G-P1 Defect-1 fix) inside acceptChips.
      if (eventType === "consumer_chips") {
        acceptChips(data.contextChips);
        return;
      }

      // G-P3 UAT round-2 R2-D (2026-07-07): server-initiated workspace tab open
      // (SendWorkspaceArtifactHandler widgetType 'Workspace' — e.g. the Compose
      // layout). Dispatch the SAME `workspace.widget_load` event the Workspaces
      // menu produces; WorkspacePane's existing subscriber adds + auto-activates
      // the tab and the client-side tab persistence makes it survive reload.
      // The event carries NO tabId on purpose — WorkspacePane treats tabId-less
      // widget_load as the server-initiated "open a new tab" contract.
      if (eventType === "workspace_open_tab") {
        const widgetType = data.contextWidgetType;
        if (!widgetType) return;
        let widgetData: unknown = null;
        if (typeof data.contextWidgetDataJson === "string" && data.contextWidgetDataJson.length > 0) {
          try {
            widgetData = JSON.parse(data.contextWidgetDataJson);
          } catch {
            // Malformed payload — open the tab without data rather than dropping
            // the user-visible action on the floor.
            widgetData = null;
          }
        }
        dispatchWorkspace("workspace", {
          type: "widget_load",
          widgetType,
          widgetData,
          displayName: data.contextDisplayName,
          // D-F3 UI-action truthfulness (FR-A1-08 / task AIR2-037): forward the
          // server-issued frame id so WorkspacePane can ack it back once the tab is
          // actually materialized. This is the SAME identifier the server's ack-gated
          // tool call (SendWorkspaceArtifactHandler) is waiting on — dropping it here
          // (as this bridge did before task AIR2-037) meant no client event could ever
          // reference the frame, silently defeating the ack contract.
          frameId: data.contextTabId,
        } as WorkspacePaneEvent);
        return;
      }

      const timestamp = data.contextTimestamp ?? new Date().toISOString();

      // Map the SSE payload to the matching ContextPaneEvent discriminated
      // union (R6 task 059). Unknown discriminants are ignored (ADR-030
      // additive policy).
      switch (eventType) {
        case "tool_call_started":
          dispatch("context", {
            type: "tool_call_started",
            timestamp,
            toolName: data.contextToolName ?? "",
            decisionId: data.contextDecisionId ?? "",
          } as ContextPaneEvent);
          break;
        case "tool_call_completed":
          dispatch("context", {
            type: "tool_call_completed",
            timestamp,
            toolName: data.contextToolName ?? "",
            decisionId: data.contextDecisionId ?? "",
            outcome: data.contextOutcome ?? "",
            durationMs: data.contextDurationMs ?? 0,
          } as ContextPaneEvent);
          break;
        case "knowledge_retrieved":
          dispatch("context", {
            type: "knowledge_retrieved",
            timestamp,
            knowledgeSourceId: data.contextKnowledgeSourceId ?? "",
            relevanceScore: data.contextRelevanceScore ?? 0,
            resultCount: data.contextResultCount ?? 0,
          } as ContextPaneEvent);
          break;
        case "playbook_node_executing":
          dispatch("context", {
            type: "playbook_node_executing",
            timestamp,
            playbookId: data.contextPlaybookId ?? "",
            nodeId: data.contextNodeId ?? "",
            nodeType: data.contextNodeType ?? "",
          } as ContextPaneEvent);
          break;
        case "playbook_node_completed":
          dispatch("context", {
            type: "playbook_node_completed",
            timestamp,
            playbookId: data.contextPlaybookId ?? "",
            nodeId: data.contextNodeId ?? "",
            durationMs: data.contextDurationMs ?? 0,
          } as ContextPaneEvent);
          break;
        case "decision_made":
          dispatch("context", {
            type: "decision_made",
            timestamp,
            layer: data.contextLayer ?? "",
            decision: data.contextDecision ?? "",
            capabilityName: data.contextCapabilityName,
          } as ContextPaneEvent);
          break;
        case "tool_chain": {
          // Task 046 / FR-P3-07 — the ledger ToolChain bridge (ADR-040): the
          // BFF emits this frame only AFTER the segment was appended to the
          // session ledger. Forward the persisted records to the `context`
          // bus where ExecutionTraceWidget renders them. Explicit per-field
          // copy (NFR-07 — identifiers/counts only; never a spread).
          const rawCalls = data.contextToolChainCalls;
          if (!Array.isArray(rawCalls) || rawCalls.length === 0) return;
          const toolChainEvent = {
            type: "tool_chain",
            timestamp,
            turn: typeof data.contextTurn === "number" ? data.contextTurn : 0,
            toolChainCalls: rawCalls
              .filter((c) => typeof c?.toolId === "string" && c.toolId.length > 0)
              .map((c) => ({
                toolId: c.toolId as string,
                argsSummary: typeof c.argsSummary === "string" ? c.argsSummary : undefined,
                resultCount: typeof c.resultCount === "number" ? c.resultCount : undefined,
                citationCount: typeof c.citationCount === "number" ? c.citationCount : undefined,
                durationMs: typeof c.durationMs === "number" ? c.durationMs : undefined,
              })),
          };
          // G-P3 UAT round-5 R5-D (2026-07-07): ALSO record into the replay
          // buffer. The trace widget mounts only when the user picks it from
          // the Context-pane Tools menu — usually AFTER these events fired —
          // and PaneEventBus drops events with no subscriber. The buffer lets
          // the widget backfill on mount (same page session; a hard refresh
          // still loses history — server ToolChain read surface is the named
          // follow-up).
          recordExecutionTraceEvent(toolChainEvent as BufferedTraceEvent);
          dispatch("context", toolChainEvent as ContextPaneEvent);
          break;
        }
        default:
          return;
      }
    },
    [dispatch, dispatchWorkspace, acceptChips]
  );

  // Stable controller identity (Step 9.5 review).
  return React.useMemo(() => ({ handleContextEvent }), [handleContextEvent]);
}


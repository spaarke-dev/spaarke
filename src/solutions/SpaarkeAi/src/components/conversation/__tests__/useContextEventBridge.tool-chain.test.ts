/**
 * useContextEventBridge — `tool_chain` bridge tests
 * (G-P3 UAT round-5 R5-D, 2026-07-07).
 *
 * Round-5 evidence: the ExecutionTraceWidget stayed EMPTY after tool-invoking
 * turns. The wire contract (server ContextSseEventDto → this bridge →
 * PaneEventBus `context` channel → widget) was correct end-to-end; the break
 * was mount lifecycle — the widget mounts only when the user selects
 * "Execution Trace" from the Context-pane Tools menu, AFTER the events fired,
 * and PaneEventBus drops events with no subscriber.
 *
 * These tests pin the bridge half of the fix: every dispatched tool_chain
 * event is ALSO recorded into the executionTraceBuffer the widget replays on
 * mount — plus the previously untested field-mapping contract itself.
 */

import { renderHook } from "@testing-library/react";
import { clearExecutionTraceBuffer, getExecutionTraceBuffer } from "@spaarke/ai-widgets";
import { useContextEventBridge } from "../useContextEventBridge";

describe("useContextEventBridge — tool_chain (task 046 contract + R5-D buffer)", () => {
  beforeEach(() => {
    clearExecutionTraceBuffer();
  });

  function setup() {
    const dispatch = jest.fn();
    const dispatchWorkspace = jest.fn();
    const acceptChips = jest.fn();
    const { result } = renderHook(() =>
      useContextEventBridge({ dispatch, dispatchWorkspace, acceptChips }),
    );
    return { dispatch, dispatchWorkspace, acceptChips, result };
  }

  const frame = {
    contextEventType: "tool_chain",
    contextTimestamp: "2026-07-07T18:40:00.000Z",
    contextTurn: 3,
    contextToolChainCalls: [
      {
        toolId: "dataverse.search_data",
        argsSummary: "query=<redacted:12>; scope=sprk_matter",
        resultCount: 4,
        citationCount: 2,
        durationMs: 350,
      },
      // No toolId — must be filtered out (NFR-07 narrowing).
      { argsSummary: "orphan" },
    ],
  };

  it("dispatches the narrowed tool_chain event on the context channel (widget contract)", () => {
    const { dispatch, result } = setup();

    result.current.handleContextEvent(frame);

    expect(dispatch).toHaveBeenCalledTimes(1);
    const [channel, event] = dispatch.mock.calls[0];
    expect(channel).toBe("context");
    expect(event).toEqual({
      type: "tool_chain",
      timestamp: "2026-07-07T18:40:00.000Z",
      turn: 3,
      toolChainCalls: [
        {
          toolId: "dataverse.search_data",
          argsSummary: "query=<redacted:12>; scope=sprk_matter",
          resultCount: 4,
          citationCount: 2,
          durationMs: 350,
        },
      ],
    });
  });

  it("records the SAME event into the replay buffer for the late-mounting widget (R5-D)", () => {
    const { dispatch, result } = setup();

    result.current.handleContextEvent(frame);

    const buffered = getExecutionTraceBuffer();
    expect(buffered).toHaveLength(1);
    // Buffer content is byte-identical to the dispatched event — the widget
    // replays it through the same handler its live subscription uses.
    expect(buffered[0]).toEqual(dispatch.mock.calls[0][1]);
  });

  it("drops empty-call frames entirely (no dispatch, no buffer entry)", () => {
    const { dispatch, result } = setup();

    result.current.handleContextEvent({
      contextEventType: "tool_chain",
      contextTimestamp: "2026-07-07T18:40:00.000Z",
      contextToolChainCalls: [],
    });

    expect(dispatch).not.toHaveBeenCalled();
    expect(getExecutionTraceBuffer()).toHaveLength(0);
  });
});

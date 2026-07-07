/**
 * useContextEventBridge — `workspace_open_tab` bridge tests
 * (G-P3 UAT round-2 R2-D, 2026-07-07).
 *
 * Round-2 evidence: the model invoked SYS-Send_Workspace_Artifact (server-side
 * UpsertTab succeeded, App Insights 15:26:51Z / 15:27:45Z) but NO tab appeared —
 * the post-046 client has no polling channel, so server-side tab writes were
 * invisible. The fix: the handler emits a `workspace_open_tab` context_event
 * frame and THIS bridge dispatches the SAME PaneEventBus `workspace.widget_load`
 * event the Workspaces menu uses (WorkspacePane adds + auto-activates the tab).
 */

import { renderHook } from "@testing-library/react";
import { useContextEventBridge } from "../useContextEventBridge";

describe("useContextEventBridge — workspace_open_tab (R2-D)", () => {
  function setup() {
    const dispatch = jest.fn();
    const dispatchWorkspace = jest.fn();
    const acceptChips = jest.fn();
    const { result } = renderHook(() =>
      useContextEventBridge({ dispatch, dispatchWorkspace, acceptChips }),
    );
    return { dispatch, dispatchWorkspace, acceptChips, result };
  }

  it("dispatches workspace.widget_load with the parsed widgetData (the Workspaces-menu contract)", () => {
    const { dispatchWorkspace, dispatch, result } = setup();

    result.current.handleContextEvent({
      contextEventType: "workspace_open_tab",
      contextWidgetType: "workspace",
      contextDisplayName: "Compose",
      contextTabId: "11111111111111111111111111111111",
      contextWidgetDataJson: JSON.stringify({
        layoutId: "c09d26be-e173-f111-ab0e-7ced8ddc4a05",
        layoutName: "Compose",
      }),
    });

    expect(dispatchWorkspace).toHaveBeenCalledTimes(1);
    const [channel, event] = dispatchWorkspace.mock.calls[0];
    expect(channel).toBe("workspace");
    // No tabId on the dispatched event — WorkspacePane treats tabId-less
    // widget_load as the "open a new tab" contract (its own re-dispatch
    // confirmation carries the tabId).
    expect(event).toEqual({
      type: "widget_load",
      widgetType: "workspace",
      widgetData: {
        layoutId: "c09d26be-e173-f111-ab0e-7ced8ddc4a05",
        layoutName: "Compose",
      },
      displayName: "Compose",
    });

    // The frame must NOT leak onto the context channel (ExecutionTraceWidget).
    expect(dispatch).not.toHaveBeenCalled();
  });

  it("opens the tab with null widgetData when the JSON payload is malformed", () => {
    const { dispatchWorkspace, result } = setup();

    result.current.handleContextEvent({
      contextEventType: "workspace_open_tab",
      contextWidgetType: "workspace",
      contextDisplayName: "Compose",
      contextWidgetDataJson: "{not json",
    });

    expect(dispatchWorkspace).toHaveBeenCalledTimes(1);
    expect(dispatchWorkspace.mock.calls[0][1].widgetData).toBeNull();
  });

  it("ignores workspace_open_tab frames without a widgetType", () => {
    const { dispatchWorkspace, result } = setup();

    result.current.handleContextEvent({
      contextEventType: "workspace_open_tab",
      contextDisplayName: "Compose",
    });

    expect(dispatchWorkspace).not.toHaveBeenCalled();
  });

  it("keeps routing trace discriminants to the context channel (regression guard)", () => {
    const { dispatch, dispatchWorkspace, result } = setup();

    result.current.handleContextEvent({
      contextEventType: "tool_call_started",
      contextTimestamp: "2026-07-07T15:26:51Z",
      contextToolName: "SYS-Send_Workspace_Artifact",
      contextDecisionId: "d-1",
    });

    expect(dispatch).toHaveBeenCalledTimes(1);
    expect(dispatch.mock.calls[0][0]).toBe("context");
    expect(dispatchWorkspace).not.toHaveBeenCalled();
  });
});

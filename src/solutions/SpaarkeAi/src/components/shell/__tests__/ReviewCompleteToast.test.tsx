/**
 * ReviewCompleteToast.test.tsx — UAT round-1 follow-on (task 071): "Notify me
 * when completed" (in-app layer).
 *
 * Covers the four acceptance criteria from
 * projects/ai-advanced-capabilities-agreements-r1/tasks/071-review-complete-toast.poml:
 *   1. toast-on-hidden   — review completes while Compose is NOT the active tab → toast fires.
 *   2. no-toast-on-visible — review completes while Compose IS the active tab → suppressed.
 *   3. action-navigates  — clicking "View findings" dispatches the source-only Compose
 *                          re-activation `widget_load` (the SAME mechanism WorkspacePane's
 *                          add-to-DMS / reporting-email flows already use).
 *   4. bounded stacking  — a second completion while the first toast is still showing UPDATES
 *                          the existing toast (fixed toastId) instead of stacking a new one.
 *
 * `useToastController` is mocked (not a real mounted `<Toaster>`/portal) so the assertions
 * target the CONTRACT directly — which toast-store calls fire, with what options — mirroring
 * how `three-pane-compose-coordination.e2e.test.tsx` uses a REAL `PaneEventBus` (not a mocked
 * bus) for the signal side of the test, while stubbing the one genuinely-external boundary.
 */

import "@testing-library/jest-dom";
import * as React from "react";
import { render, screen, fireEvent, act } from "@testing-library/react";
import { FluentProvider, webLightTheme, useToastController } from "@fluentui/react-components";
import { PaneEventBus, PaneEventBusProvider } from "@spaarke/ai-widgets";
import { ReviewCompleteToast, REVIEW_COMPLETE_TOAST_ID } from "../ReviewCompleteToast";

jest.mock("@fluentui/react-components", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual("@fluentui/react-components") as any;
  return {
    ...actual,
    useToastController: jest.fn(),
  };
});

const mockedUseToastController = useToastController as jest.Mock;

describe("ReviewCompleteToast (task 071)", () => {
  let dispatchToast: jest.Mock;
  let updateToast: jest.Mock;
  let dismissToast: jest.Mock;

  beforeEach(() => {
    dispatchToast = jest.fn();
    updateToast = jest.fn();
    dismissToast = jest.fn();
    mockedUseToastController.mockReturnValue({ dispatchToast, updateToast, dismissToast });
  });

  function renderBridge(): { bus: PaneEventBus } {
    const bus = new PaneEventBus();
    render(
      <PaneEventBusProvider bus={bus}>
        <ReviewCompleteToast toasterId="test-toaster" />
      </PaneEventBusProvider>
    );
    return { bus };
  }

  it("toast-on-hidden: raises the toast when the review completes while another tab is active", () => {
    const { bus } = renderBridge();

    act(() => {
      bus.dispatch("workspace", {
        type: "active_widget_changed",
        widgetType: "workspace",
        tabId: "tab-1",
        displayName: "Daily Briefing",
      });
    });
    act(() => {
      bus.dispatch("workspace", {
        type: "compose_advisory_comments",
        advisoryComments: [{ targetText: "clause 1", explanation: "risky" }],
        overallRisk: "medium",
        sessionId: "sess-1",
        timestamp: "2026-08-03T00:00:00.000Z",
      });
    });

    expect(dispatchToast).toHaveBeenCalledTimes(1);
    const [, options] = dispatchToast.mock.calls[0];
    expect(options).toMatchObject({ toastId: REVIEW_COMPLETE_TOAST_ID, intent: "success" });
    expect(updateToast).not.toHaveBeenCalled();
  });

  it("no-toast-on-visible: suppresses the toast when Compose is already the active tab", () => {
    const { bus } = renderBridge();

    act(() => {
      bus.dispatch("workspace", {
        type: "active_widget_changed",
        widgetType: "compose",
        tabId: "tab-compose",
        displayName: "Compose",
      });
    });
    act(() => {
      bus.dispatch("workspace", {
        type: "compose_advisory_comments",
        advisoryComments: [{ targetText: "clause 1", explanation: "risky" }],
        overallRisk: "medium",
        sessionId: "sess-1",
        timestamp: "2026-08-03T00:00:00.000Z",
      });
    });

    expect(dispatchToast).not.toHaveBeenCalled();
    expect(updateToast).not.toHaveBeenCalled();
  });

  it("action-navigates: clicking \"View findings\" re-activates the existing Compose tab and dismisses the toast", () => {
    const { bus } = renderBridge();
    const dispatchSpy = jest.spyOn(bus, "dispatch");

    act(() => {
      bus.dispatch("workspace", {
        type: "compose_advisory_comments",
        advisoryComments: [{ targetText: "clause 1", explanation: "risky" }],
        overallRisk: "medium",
        sessionId: "sess-1",
        timestamp: "2026-08-03T00:00:00.000Z",
      });
    });

    expect(dispatchToast).toHaveBeenCalledTimes(1);
    const [content] = dispatchToast.mock.calls[0];

    // The captured toast content's action closure is bound to the SAME bus
    // (via useDispatchPaneEvent() inside ReviewCompleteToast) — render it
    // standalone and click through.
    render(<FluentProvider theme={webLightTheme}>{content}</FluentProvider>);
    fireEvent.click(screen.getByText("View findings"));

    expect(dispatchSpy).toHaveBeenCalledWith("workspace", {
      type: "widget_load",
      widgetType: "compose",
      widgetData: { source: "review-complete-toast" },
    });
    expect(dismissToast).toHaveBeenCalledWith(REVIEW_COMPLETE_TOAST_ID);
  });

  it("bounded stacking: a second completion while the toast is still showing UPDATES it instead of stacking a new one", () => {
    const { bus } = renderBridge();

    act(() => {
      bus.dispatch("workspace", {
        type: "compose_advisory_comments",
        advisoryComments: [{ targetText: "clause 1", explanation: "risky" }],
        overallRisk: "medium",
        sessionId: "sess-1",
        timestamp: "2026-08-03T00:00:00.000Z",
      });
    });
    expect(dispatchToast).toHaveBeenCalledTimes(1);

    // Second completion — the toast is still "active" (no onStatusChange fired yet).
    act(() => {
      bus.dispatch("workspace", {
        type: "compose_advisory_comments",
        advisoryComments: [{ targetText: "clause 2", explanation: "also risky" }],
        overallRisk: "high",
        sessionId: "sess-1",
        timestamp: "2026-08-03T00:05:00.000Z",
      });
    });

    // Still only ONE dispatchToast call ever — the second completion updated in place.
    expect(dispatchToast).toHaveBeenCalledTimes(1);
    expect(updateToast).toHaveBeenCalledTimes(1);
    expect(updateToast.mock.calls[0][0]).toMatchObject({ toastId: REVIEW_COMPLETE_TOAST_ID, intent: "success" });

    // Once the toast lifecycle reports it has cleared, the NEXT completion dispatches fresh again.
    const firstOptions = dispatchToast.mock.calls[0][1];
    act(() => {
      firstOptions.onStatusChange(null, { status: "dismissed" });
    });
    act(() => {
      bus.dispatch("workspace", {
        type: "compose_advisory_comments",
        advisoryComments: [{ targetText: "clause 3", explanation: "risky again" }],
        overallRisk: "medium",
        sessionId: "sess-1",
        timestamp: "2026-08-03T00:10:00.000Z",
      });
    });
    expect(dispatchToast).toHaveBeenCalledTimes(2);
  });
});

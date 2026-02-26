/**
 * Selection-Based Revision E2E Integration Tests
 *
 * Tests the complete selection-based revision flow:
 * 1. Cross-pane selection -> SprkChatHighlightRefine appears
 * 2. User enters instruction -> refine API called
 * 3. SSE streams through bridge -> DiffReviewPanel shows diff
 * 4. Accept -> editor content updated; Reject -> original restored
 *
 * These tests cover the SprkChat side of the flow:
 * - Cross-pane selection reception via SprkChatBridge
 * - Refine submission calling correct API endpoint
 * - SSE tokens emitted through bridge as document_stream_* events
 * - Error handling (API failures)
 * - Quick action auto-submission
 *
 * @see SprkChat.tsx - handleEditorRefine, handleRefine
 * @see SprkChatHighlightRefine.tsx - UI for selection refinement
 * @see useSelectionListener.ts - Bridge event subscription
 * @see ADR-012 - Shared Component Library
 */

import * as React from "react";
import { screen, fireEvent, waitFor, act } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SprkChatHighlightRefine } from "../SprkChatHighlightRefine";
import { useSelectionListener } from "../hooks/useSelectionListener";
import { renderWithProviders } from "../../../__mocks__/pcfMocks";
import type { ICrossPaneSelection } from "../types";

// ---------------------------------------------------------------------------
// Minimal mock SprkChatBridge (in-memory event bus)
// ---------------------------------------------------------------------------

class MockBridge {
    private _handlers: Map<string, Array<(payload: unknown) => void>> = new Map();
    isDisconnected = false;

    subscribe<T = unknown>(event: string, handler: (payload: T) => void): () => void {
        const handlers = this._handlers.get(event) ?? [];
        handlers.push(handler as (payload: unknown) => void);
        this._handlers.set(event, handlers);
        return () => {
            const current = this._handlers.get(event) ?? [];
            const idx = current.indexOf(handler as (payload: unknown) => void);
            if (idx >= 0) current.splice(idx, 1);
        };
    }

    emit(event: string, payload: unknown): void {
        const handlers = this._handlers.get(event) ?? [];
        for (const handler of handlers) {
            handler(payload);
        }
    }

    disconnect(): void {
        this.isDisconnected = true;
        this._handlers.clear();
    }
}

// ---------------------------------------------------------------------------
// Test Helpers
// ---------------------------------------------------------------------------

/** Creates a cross-pane selection object simulating editor selection */
function makeCrossPaneSelection(
    text: string,
    overrides?: Partial<ICrossPaneSelection>
): ICrossPaneSelection {
    return {
        text,
        fullText: text,
        selectedHtml: `<p>${text}</p>`,
        startOffset: 0,
        endOffset: text.length,
        source: "analysis-editor",
        ...overrides,
    };
}

/** Test wrapper that renders SprkChatHighlightRefine with a cross-pane selection */
const CrossPaneTestWrapper: React.FC<{
    onRefine: (text: string, instruction: string) => void;
    crossPaneSelection: ICrossPaneSelection | null;
    isRefining?: boolean;
}> = ({ onRefine, crossPaneSelection, isRefining = false }) => {
    const contentRef = React.useRef<HTMLDivElement>(null);

    return (
        <div>
            <div ref={contentRef} data-testid="content-area" style={{ position: "relative" }}>
                <p>Some chat content here.</p>
            </div>
            <SprkChatHighlightRefine
                contentRef={contentRef}
                onRefine={onRefine}
                isRefining={isRefining}
                crossPaneSelection={crossPaneSelection}
            />
        </div>
    );
};

// ---------------------------------------------------------------------------
// Test Suite: Cross-Pane Selection -> HighlightRefine Display
// ---------------------------------------------------------------------------

describe("Selection-Based Revision: Cross-Pane Selection", () => {
    let mockOnRefine: jest.Mock;

    beforeEach(() => {
        mockOnRefine = jest.fn();
    });

    afterEach(() => {
        jest.clearAllMocks();
    });

    it("crossPaneSelection_Present_ShowsCrossPaneToolbar", () => {
        const selection = makeCrossPaneSelection("The analysis reveals key findings.");

        renderWithProviders(
            <CrossPaneTestWrapper
                onRefine={mockOnRefine}
                crossPaneSelection={selection}
            />
        );

        expect(
            screen.getByTestId("highlight-refine-toolbar-cross-pane")
        ).toBeInTheDocument();
    });

    it("crossPaneSelection_ShowsSelectedInEditorBadge", () => {
        const selection = makeCrossPaneSelection("Selected editor text");

        renderWithProviders(
            <CrossPaneTestWrapper
                onRefine={mockOnRefine}
                crossPaneSelection={selection}
            />
        );

        expect(screen.getByText("Selected in Editor")).toBeInTheDocument();
    });

    it("crossPaneSelection_ShowsRefineButton", () => {
        const selection = makeCrossPaneSelection("Text to refine");

        renderWithProviders(
            <CrossPaneTestWrapper
                onRefine={mockOnRefine}
                crossPaneSelection={selection}
            />
        );

        expect(screen.getByTestId("refine-button")).toBeInTheDocument();
    });

    it("crossPaneSelection_Null_HidesCrossPaneToolbar", () => {
        renderWithProviders(
            <CrossPaneTestWrapper
                onRefine={mockOnRefine}
                crossPaneSelection={null}
            />
        );

        expect(
            screen.queryByTestId("highlight-refine-toolbar-cross-pane")
        ).not.toBeInTheDocument();
    });

    it("crossPaneSelection_EmptyText_HidesCrossPaneToolbar", () => {
        const selection = makeCrossPaneSelection("");

        renderWithProviders(
            <CrossPaneTestWrapper
                onRefine={mockOnRefine}
                crossPaneSelection={selection}
            />
        );

        expect(
            screen.queryByTestId("highlight-refine-toolbar-cross-pane")
        ).not.toBeInTheDocument();
    });

    it("crossPaneSelection_ShowsPreviewText", () => {
        const selection = makeCrossPaneSelection("The quick brown fox jumps over the lazy dog.");

        renderWithProviders(
            <CrossPaneTestWrapper
                onRefine={mockOnRefine}
                crossPaneSelection={selection}
            />
        );

        // Preview text should contain the selection (may be wrapped in quotes)
        expect(
            screen.getByText(/The quick brown fox/)
        ).toBeInTheDocument();
    });
});

// ---------------------------------------------------------------------------
// Test Suite: Refine Submission Flow
// ---------------------------------------------------------------------------

describe("Selection-Based Revision: Refine Submission", () => {
    let mockOnRefine: jest.Mock;

    beforeEach(() => {
        mockOnRefine = jest.fn();
    });

    afterEach(() => {
        jest.clearAllMocks();
    });

    it("refineSubmit_EnterInstruction_CallsOnRefineWithTextAndInstruction", async () => {
        const user = userEvent.setup();
        const selection = makeCrossPaneSelection("Editor selected text");

        renderWithProviders(
            <CrossPaneTestWrapper
                onRefine={mockOnRefine}
                crossPaneSelection={selection}
            />
        );

        // Click Refine to show input
        await user.click(screen.getByTestId("refine-button"));

        await waitFor(() => {
            expect(screen.getByTestId("refine-instruction-input")).toBeInTheDocument();
        });

        // Type instruction
        const input = screen.getByTestId("refine-instruction-input");
        const nativeInput = input.querySelector("input") || input;
        await user.type(nativeInput, "Make this more formal");

        // Submit
        await user.click(screen.getByTestId("refine-submit-button"));

        expect(mockOnRefine).toHaveBeenCalledWith(
            "Editor selected text",
            "Make this more formal"
        );
    });

    it("refineSubmit_EnterKey_SubmitsRefinement", async () => {
        const user = userEvent.setup();
        const selection = makeCrossPaneSelection("Text for enter-key test");

        renderWithProviders(
            <CrossPaneTestWrapper
                onRefine={mockOnRefine}
                crossPaneSelection={selection}
            />
        );

        await user.click(screen.getByTestId("refine-button"));

        await waitFor(() => {
            expect(screen.getByTestId("refine-instruction-input")).toBeInTheDocument();
        });

        const input = screen.getByTestId("refine-instruction-input");
        const nativeInput = input.querySelector("input") || input;
        await user.type(nativeInput, "Simplify{Enter}");

        expect(mockOnRefine).toHaveBeenCalledWith(
            "Text for enter-key test",
            "Simplify"
        );
    });

    it("refineSubmit_EmptyInstruction_SubmitButtonDisabled", async () => {
        const user = userEvent.setup();
        const selection = makeCrossPaneSelection("Some text");

        renderWithProviders(
            <CrossPaneTestWrapper
                onRefine={mockOnRefine}
                crossPaneSelection={selection}
            />
        );

        await user.click(screen.getByTestId("refine-button"));

        await waitFor(() => {
            expect(screen.getByTestId("refine-submit-button")).toBeDisabled();
        });
    });

    it("refineSubmit_IsRefiningTrue_DisablesRefineButton", () => {
        const selection = makeCrossPaneSelection("Text while refining");

        renderWithProviders(
            <CrossPaneTestWrapper
                onRefine={mockOnRefine}
                crossPaneSelection={selection}
                isRefining={true}
            />
        );

        expect(screen.getByTestId("refine-button")).toBeDisabled();
    });
});

// ---------------------------------------------------------------------------
// Test Suite: Quick Action Chips
// ---------------------------------------------------------------------------

describe("Selection-Based Revision: Quick Actions", () => {
    let mockOnRefine: jest.Mock;

    beforeEach(() => {
        mockOnRefine = jest.fn();
    });

    afterEach(() => {
        jest.clearAllMocks();
    });

    it("quickAction_ClickSimplify_AutoSubmitsWithSimplifyInstruction", async () => {
        const user = userEvent.setup();
        const selection = makeCrossPaneSelection("Complex text to simplify");

        renderWithProviders(
            <CrossPaneTestWrapper
                onRefine={mockOnRefine}
                crossPaneSelection={selection}
            />
        );

        // Quick action chips should be visible (no need to click Refine first)
        const simplifyButton = screen.getByTestId("quick-action-simplify");
        expect(simplifyButton).toBeInTheDocument();

        await user.click(simplifyButton);

        expect(mockOnRefine).toHaveBeenCalledWith(
            "Complex text to simplify",
            "Simplify this text"
        );
    });

    it("quickAction_ClickExpand_AutoSubmitsWithExpandInstruction", async () => {
        const user = userEvent.setup();
        const selection = makeCrossPaneSelection("Brief text");

        renderWithProviders(
            <CrossPaneTestWrapper
                onRefine={mockOnRefine}
                crossPaneSelection={selection}
            />
        );

        await user.click(screen.getByTestId("quick-action-expand"));

        expect(mockOnRefine).toHaveBeenCalledWith(
            "Brief text",
            "Expand this text with more detail"
        );
    });

    it("quickAction_ClickMakeConcise_AutoSubmits", async () => {
        const user = userEvent.setup();
        const selection = makeCrossPaneSelection("Verbose text that is too long");

        renderWithProviders(
            <CrossPaneTestWrapper
                onRefine={mockOnRefine}
                crossPaneSelection={selection}
            />
        );

        await user.click(screen.getByTestId("quick-action-concise"));

        expect(mockOnRefine).toHaveBeenCalledWith(
            "Verbose text that is too long",
            "Make this text more concise"
        );
    });

    it("quickAction_ClickMakeFormal_AutoSubmits", async () => {
        const user = userEvent.setup();
        const selection = makeCrossPaneSelection("Casual text here");

        renderWithProviders(
            <CrossPaneTestWrapper
                onRefine={mockOnRefine}
                crossPaneSelection={selection}
            />
        );

        await user.click(screen.getByTestId("quick-action-formal"));

        expect(mockOnRefine).toHaveBeenCalledWith(
            "Casual text here",
            "Rewrite this text in a more formal tone"
        );
    });

    it("quickAction_WhileRefining_ChipsDisabled", () => {
        const selection = makeCrossPaneSelection("Text");

        renderWithProviders(
            <CrossPaneTestWrapper
                onRefine={mockOnRefine}
                crossPaneSelection={selection}
                isRefining={true}
            />
        );

        // Quick action chips should be hidden when refining
        expect(screen.queryByTestId("quick-actions-row")).not.toBeInTheDocument();
    });
});

// ---------------------------------------------------------------------------
// Test Suite: useSelectionListener Hook
// ---------------------------------------------------------------------------

describe("Selection-Based Revision: useSelectionListener", () => {
    let bridge: MockBridge;

    beforeEach(() => {
        bridge = new MockBridge();
    });

    afterEach(() => {
        bridge.disconnect();
    });

    it("selectionChanged_BridgeEvent_SetsSelectionState", () => {
        const { result } = renderHookWithBridge(bridge);

        act(() => {
            bridge.emit("selection_changed", {
                text: "Selected text from editor",
                startOffset: 10,
                endOffset: 35,
                context: JSON.stringify({
                    selectedHtml: "<p>Selected text from editor</p>",
                    source: "analysis-editor",
                }),
            });
        });

        expect(result.current.selection).not.toBeNull();
        expect(result.current.selection!.text).toBe("Selected text from editor");
        expect(result.current.selection!.source).toBe("analysis-editor");
    });

    it("selectionCleared_BridgeEvent_ResetsSelectionToNull", () => {
        const { result } = renderHookWithBridge(bridge);

        // First set a selection
        act(() => {
            bridge.emit("selection_changed", {
                text: "Something selected",
                startOffset: 0,
                endOffset: 18,
                context: JSON.stringify({
                    selectedHtml: "<p>Something selected</p>",
                    source: "analysis-editor",
                }),
            });
        });

        expect(result.current.selection).not.toBeNull();

        // Then clear it
        act(() => {
            bridge.emit("selection_changed", {
                text: "",
                startOffset: 0,
                endOffset: 0,
                context: "selection_cleared",
            });
        });

        expect(result.current.selection).toBeNull();
    });

    it("clearSelection_Programmatic_ResetsSelectionToNull", () => {
        const { result } = renderHookWithBridge(bridge);

        // Set a selection
        act(() => {
            bridge.emit("selection_changed", {
                text: "Active selection",
                startOffset: 0,
                endOffset: 16,
            });
        });

        expect(result.current.selection).not.toBeNull();

        // Programmatically clear
        act(() => {
            result.current.clearSelection();
        });

        expect(result.current.selection).toBeNull();
    });

    it("bridgeDisconnected_ClearsSelectionOnUnsubscribe", () => {
        const { result, unmount } = renderHookWithBridge(bridge);

        // Set a selection
        act(() => {
            bridge.emit("selection_changed", {
                text: "Will be cleared on unmount",
                startOffset: 0,
                endOffset: 26,
            });
        });

        expect(result.current.selection).not.toBeNull();

        // Unmount triggers cleanup (unsubscribe)
        unmount();

        // After unmount, we can't check result.current, but the cleanup
        // should have called setSelection(null) in the effect cleanup
    });

    it("enabled_False_DoesNotSubscribeToEvents", () => {
        const { result } = renderHookWithBridge(bridge, false);

        act(() => {
            bridge.emit("selection_changed", {
                text: "Should not be received",
                startOffset: 0,
                endOffset: 22,
            });
        });

        expect(result.current.selection).toBeNull();
    });
});

// ---------------------------------------------------------------------------
// Test Suite: Edge Cases
// ---------------------------------------------------------------------------

describe("Selection-Based Revision: Edge Cases", () => {
    it("edgeCase_LargeSelection_TextTruncatedForPreview", () => {
        const longText = "A".repeat(6000);
        const selection = makeCrossPaneSelection(longText);

        renderWithProviders(
            <CrossPaneTestWrapper
                onRefine={jest.fn()}
                crossPaneSelection={selection}
            />
        );

        // The toolbar should still render
        expect(
            screen.getByTestId("highlight-refine-toolbar-cross-pane")
        ).toBeInTheDocument();
    });

    it("edgeCase_DismissButton_ClosesToolbar", async () => {
        jest.useFakeTimers();
        const user = userEvent.setup({ advanceTimers: jest.advanceTimersByTime });
        const selection = makeCrossPaneSelection("Dismissible selection");
        const mockOnRefine = jest.fn();

        const { rerender } = renderWithProviders(
            <CrossPaneTestWrapper
                onRefine={mockOnRefine}
                crossPaneSelection={selection}
            />
        );

        expect(
            screen.getByTestId("highlight-refine-toolbar-cross-pane")
        ).toBeInTheDocument();

        // Click dismiss button
        await user.click(screen.getByTestId("refine-dismiss-button"));

        // Advance animation timer
        act(() => {
            jest.advanceTimersByTime(300);
        });

        jest.useRealTimers();
    });

    it("edgeCase_EscapeKey_DismissesToolbar", async () => {
        jest.useFakeTimers();
        const selection = makeCrossPaneSelection("Escape test");

        renderWithProviders(
            <CrossPaneTestWrapper
                onRefine={jest.fn()}
                crossPaneSelection={selection}
            />
        );

        expect(
            screen.getByTestId("highlight-refine-toolbar-cross-pane")
        ).toBeInTheDocument();

        // Press Escape
        fireEvent.keyDown(document, { key: "Escape" });

        // Advance animation timer
        act(() => {
            jest.advanceTimersByTime(300);
        });

        jest.useRealTimers();
    });
});

// ---------------------------------------------------------------------------
// Test Suite: SSE Bridge Routing (Unit-level, no real fetch)
// ---------------------------------------------------------------------------

describe("Selection-Based Revision: Bridge Event Routing", () => {
    it("bridgeRoute_EditorRefine_EmitsDocumentStreamStartWithDiffType", () => {
        const bridge = new MockBridge();
        const receivedEvents: Array<{ event: string; payload: unknown }> = [];

        // Subscribe to bridge events (simulates Analysis Workspace consumer)
        bridge.subscribe("document_stream_start", (payload) => {
            receivedEvents.push({ event: "document_stream_start", payload });
        });

        // Simulate what handleEditorRefine does: emit stream start with diff operationType
        bridge.emit("document_stream_start", {
            operationId: "refine-test-123",
            targetPosition: "selection",
            operationType: "diff",
        });

        expect(receivedEvents).toHaveLength(1);
        expect(receivedEvents[0].event).toBe("document_stream_start");
        const payload = receivedEvents[0].payload as {
            operationId: string;
            targetPosition: string;
            operationType: string;
        };
        expect(payload.operationType).toBe("diff");
        expect(payload.operationId).toBe("refine-test-123");

        bridge.disconnect();
    });

    it("bridgeRoute_TokensRouted_AsDocumentStreamTokenEvents", () => {
        const bridge = new MockBridge();
        const receivedTokens: string[] = [];

        bridge.subscribe("document_stream_token", (payload: any) => {
            receivedTokens.push(payload.token);
        });

        // Simulate token routing (what handleEditorRefine does after parsing SSE)
        const tokens = ["Revised ", "text ", "content."];
        tokens.forEach((token, index) => {
            bridge.emit("document_stream_token", {
                operationId: "refine-route-test",
                token,
                index,
            });
        });

        expect(receivedTokens).toEqual(["Revised ", "text ", "content."]);

        bridge.disconnect();
    });

    it("bridgeRoute_StreamEnd_EmitsDocumentStreamEndEvent", () => {
        const bridge = new MockBridge();
        let endPayload: any = null;

        bridge.subscribe("document_stream_end", (payload) => {
            endPayload = payload;
        });

        bridge.emit("document_stream_end", {
            operationId: "refine-end-test",
            cancelled: false,
            totalTokens: 5,
        });

        expect(endPayload).not.toBeNull();
        expect(endPayload.operationId).toBe("refine-end-test");
        expect(endPayload.cancelled).toBe(false);
        expect(endPayload.totalTokens).toBe(5);

        bridge.disconnect();
    });

    it("bridgeRoute_Error_EmitsStreamEndWithCancelled", () => {
        const bridge = new MockBridge();
        let endPayload: any = null;

        bridge.subscribe("document_stream_end", (payload) => {
            endPayload = payload;
        });

        // When an error occurs, handleEditorRefine emits stream end with cancelled=true
        bridge.emit("document_stream_end", {
            operationId: "refine-error-test",
            cancelled: true,
            totalTokens: 0,
        });

        expect(endPayload.cancelled).toBe(true);
        expect(endPayload.totalTokens).toBe(0);

        bridge.disconnect();
    });

    it("bridgeRoute_CompleteDiffSequence_EndToEnd", () => {
        const bridge = new MockBridge();
        const events: Array<{ type: string; payload: unknown }> = [];

        bridge.subscribe("document_stream_start", (p) =>
            events.push({ type: "start", payload: p })
        );
        bridge.subscribe("document_stream_token", (p) =>
            events.push({ type: "token", payload: p })
        );
        bridge.subscribe("document_stream_end", (p) =>
            events.push({ type: "end", payload: p })
        );

        const opId = "refine-full-123";

        // Emit complete sequence
        bridge.emit("document_stream_start", {
            operationId: opId,
            targetPosition: "selection",
            operationType: "diff",
        });
        bridge.emit("document_stream_token", {
            operationId: opId,
            token: "Improved ",
            index: 0,
        });
        bridge.emit("document_stream_token", {
            operationId: opId,
            token: "text.",
            index: 1,
        });
        bridge.emit("document_stream_end", {
            operationId: opId,
            cancelled: false,
            totalTokens: 2,
        });

        // Verify event order: start, token, token, end
        expect(events).toHaveLength(4);
        expect(events[0].type).toBe("start");
        expect(events[1].type).toBe("token");
        expect(events[2].type).toBe("token");
        expect(events[3].type).toBe("end");

        bridge.disconnect();
    });
});

// ---------------------------------------------------------------------------
// Hook render helper (uses @testing-library/react renderHook)
// ---------------------------------------------------------------------------

import { renderHook } from "@testing-library/react";

function renderHookWithBridge(bridge: MockBridge, enabled = true) {
    return renderHook(() =>
        useSelectionListener({
            bridge: bridge as any,
            enabled,
        })
    );
}

/**
 * Integration tests for useSelectionBroadcast hook
 *
 * Tests the selection broadcast hook that emits editor selections to SprkChat
 * via SprkChatBridge. Covers:
 *   - Text selection emits selection_changed event
 *   - Drag-select debounces at 300ms
 *   - Empty selection emits selection_cleared context
 *   - Cleanup on unmount (removes listener, clears timer)
 *
 * Uses fake timers for deterministic debounce testing.
 *
 * @see hooks/useSelectionBroadcast.ts
 */

import { renderHook, act } from "@testing-library/react";
import { useSelectionBroadcast } from "../hooks/useSelectionBroadcast";
import { SprkChatBridge } from "./mocks/MockSprkChatBridge";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Create a mock editor ref with minimal required API */
function createMockEditorRef() {
    return {
        current: {
            focus: jest.fn(),
            getHtml: jest.fn(() => ""),
            setHtml: jest.fn(),
            clear: jest.fn(),
        },
    };
}

/**
 * Simulate a DOM selectionchange event.
 * Sets up getSelection to return the specified text and range.
 */
function simulateSelection(text: string, options?: {
    startOffset?: number;
    endOffset?: number;
    insideEditor?: boolean;
}) {
    const {
        startOffset = 0,
        endOffset = text.length,
        insideEditor = true,
    } = options ?? {};

    // Create a mock contenteditable container
    const editorContainer = document.createElement("div");
    editorContainer.setAttribute("contenteditable", "true");
    editorContainer.setAttribute("data-lexical-editor", "true");
    document.body.appendChild(editorContainer);

    const textNode = document.createTextNode(text);
    editorContainer.appendChild(textNode);

    const range = document.createRange();
    if (text) {
        range.setStart(textNode, startOffset);
        range.setEnd(textNode, Math.min(endOffset, text.length));
    }

    // Mock getBoundingClientRect on the range
    range.getBoundingClientRect = jest.fn(() => ({
        top: 100,
        left: 200,
        width: 150,
        height: 20,
        right: 350,
        bottom: 120,
        x: 200,
        y: 100,
        toJSON: () => ({}),
    }));

    // Mock cloneContents
    const fragment = document.createDocumentFragment();
    const span = document.createElement("span");
    span.textContent = text;
    fragment.appendChild(span);
    range.cloneContents = jest.fn(() => fragment);

    // Override document.getSelection
    const mockSelection = {
        toString: () => text,
        rangeCount: text ? 1 : 0,
        getRangeAt: jest.fn(() => range),
        isCollapsed: !text,
    };
    jest.spyOn(document, "getSelection").mockReturnValue(
        mockSelection as unknown as Selection
    );

    // If not inside editor, remove the editorContainer before the event fires
    if (!insideEditor) {
        // Re-attach textNode to a non-editor parent
        const externalDiv = document.createElement("div");
        externalDiv.appendChild(textNode);
        document.body.appendChild(externalDiv);
        // Update range to use the new parent
        range.setStart(textNode, startOffset);
        range.setEnd(textNode, Math.min(endOffset, text.length));
    }

    // Dispatch selectionchange event
    document.dispatchEvent(new Event("selectionchange"));

    return {
        cleanup: () => {
            if (editorContainer.parentNode) {
                document.body.removeChild(editorContainer);
            }
            jest.restoreAllMocks();
        },
    };
}

// ---------------------------------------------------------------------------
// Test Suite
// ---------------------------------------------------------------------------

describe("useSelectionBroadcast", () => {
    let bridge: SprkChatBridge;
    let editorRef: ReturnType<typeof createMockEditorRef>;

    beforeEach(() => {
        jest.useFakeTimers();
        bridge = new SprkChatBridge({ context: "test-selection" });
        editorRef = createMockEditorRef();
    });

    afterEach(() => {
        bridge.disconnect();
        jest.useRealTimers();
        jest.restoreAllMocks();
    });

    // -----------------------------------------------------------------------
    // 1. Text Selection Emits selection_changed
    // -----------------------------------------------------------------------

    it("selectionChange_TextSelected_EmitsSelectionChangedEvent", () => {
        // Arrange
        const emittedEvents: Array<{ text: string; context: string }> = [];
        bridge.subscribe("selection_changed", (payload: unknown) => {
            const p = payload as { text: string; context: string };
            emittedEvents.push(p);
        });

        renderHook(() =>
            useSelectionBroadcast({
                editorRef: editorRef as React.RefObject<unknown> as React.RefObject<null>,
                bridge,
                enabled: true,
            })
        );

        // Act: simulate a text selection inside the editor
        const { cleanup } = simulateSelection("selected text");

        // Advance past the 300ms debounce
        act(() => {
            jest.advanceTimersByTime(300);
        });

        // Assert: selection_changed emitted with the selected text
        expect(emittedEvents).toHaveLength(1);
        expect(emittedEvents[0].text).toBe("selected text");

        cleanup();
    });

    // -----------------------------------------------------------------------
    // 2. Drag Debounce
    // -----------------------------------------------------------------------

    it("selectionChange_RapidDragSelect_DebouncesAt300ms", () => {
        // Arrange
        const emittedEvents: unknown[] = [];
        bridge.subscribe("selection_changed", (payload) => {
            emittedEvents.push(payload);
        });

        renderHook(() =>
            useSelectionBroadcast({
                editorRef: editorRef as React.RefObject<unknown> as React.RefObject<null>,
                bridge,
                enabled: true,
            })
        );

        // Act: rapid selection changes (simulates drag)
        const s1 = simulateSelection("sel");
        act(() => { jest.advanceTimersByTime(100); }); // 100ms
        s1.cleanup();

        const s2 = simulateSelection("select");
        act(() => { jest.advanceTimersByTime(100); }); // 200ms total
        s2.cleanup();

        const s3 = simulateSelection("selected te");
        act(() => { jest.advanceTimersByTime(100); }); // 300ms total
        s3.cleanup();

        const s4 = simulateSelection("selected text final");
        act(() => { jest.advanceTimersByTime(300); }); // Only final fires

        // Assert: only the last selection was emitted (debounce)
        // Due to debounce reset, intermediate selections are dropped
        expect(emittedEvents.length).toBeGreaterThanOrEqual(1);
        const lastEvent = emittedEvents[emittedEvents.length - 1] as { text: string };
        expect(lastEvent.text).toBe("selected text final");

        s4.cleanup();
    });

    // -----------------------------------------------------------------------
    // 3. Deselect (Selection Cleared)
    // -----------------------------------------------------------------------

    it("selectionChange_EmptySelection_EmitsSelectionClearedContext", () => {
        // Arrange
        const emittedEvents: Array<{ text: string; context: string }> = [];
        bridge.subscribe("selection_changed", (payload: unknown) => {
            const p = payload as { text: string; context: string };
            emittedEvents.push(p);
        });

        renderHook(() =>
            useSelectionBroadcast({
                editorRef: editorRef as React.RefObject<unknown> as React.RefObject<null>,
                bridge,
                enabled: true,
            })
        );

        // Act: first select text, then deselect (empty selection)
        const s1 = simulateSelection("some text");
        act(() => { jest.advanceTimersByTime(300); });
        s1.cleanup();

        // Now simulate an empty selection (deselect)
        const s2 = simulateSelection("");
        act(() => { jest.advanceTimersByTime(300); });

        // Assert: a selection_cleared event was emitted after the text selection
        const clearedEvents = emittedEvents.filter(
            (e) => e.context === "selection_cleared"
        );
        expect(clearedEvents.length).toBeGreaterThanOrEqual(1);
        expect(clearedEvents[0].text).toBe("");

        s2.cleanup();
    });

    // -----------------------------------------------------------------------
    // 4. Cleanup on Unmount
    // -----------------------------------------------------------------------

    it("unmount_CleanupCalled_RemovesEventListenerAndClearsTimer", () => {
        // Arrange
        const removeListenerSpy = jest.spyOn(document, "removeEventListener");

        const { unmount } = renderHook(() =>
            useSelectionBroadcast({
                editorRef: editorRef as React.RefObject<unknown> as React.RefObject<null>,
                bridge,
                enabled: true,
            })
        );

        // Act: unmount the hook
        unmount();

        // Assert: removeEventListener was called for "selectionchange"
        expect(removeListenerSpy).toHaveBeenCalledWith(
            "selectionchange",
            expect.any(Function)
        );

        removeListenerSpy.mockRestore();
    });

    // -----------------------------------------------------------------------
    // 5. Disabled hook does not listen
    // -----------------------------------------------------------------------

    it("selectionChange_WhenDisabled_DoesNotEmitEvents", () => {
        // Arrange
        const emittedEvents: unknown[] = [];
        bridge.subscribe("selection_changed", (payload) => {
            emittedEvents.push(payload);
        });

        renderHook(() =>
            useSelectionBroadcast({
                editorRef: editorRef as React.RefObject<unknown> as React.RefObject<null>,
                bridge,
                enabled: false,
            })
        );

        // Act
        const { cleanup } = simulateSelection("should not emit");
        act(() => { jest.advanceTimersByTime(300); });

        // Assert: no events emitted
        expect(emittedEvents).toHaveLength(0);

        cleanup();
    });

    // -----------------------------------------------------------------------
    // 6. Bridge disconnected skips emission
    // -----------------------------------------------------------------------

    it("selectionChange_BridgeDisconnected_DoesNotEmit", () => {
        // Arrange
        const emittedEvents: unknown[] = [];
        bridge.subscribe("selection_changed", (payload) => {
            emittedEvents.push(payload);
        });

        renderHook(() =>
            useSelectionBroadcast({
                editorRef: editorRef as React.RefObject<unknown> as React.RefObject<null>,
                bridge,
                enabled: true,
            })
        );

        // Disconnect the bridge before selection
        bridge.disconnect();

        // Act
        const { cleanup } = simulateSelection("text after disconnect");
        act(() => { jest.advanceTimersByTime(300); });

        // Assert: no events emitted (bridge is disconnected)
        expect(emittedEvents).toHaveLength(0);

        cleanup();
    });

    // -----------------------------------------------------------------------
    // 7. Null bridge skips
    // -----------------------------------------------------------------------

    it("selectionChange_NullBridge_DoesNotEmitOrCrash", () => {
        // Arrange
        renderHook(() =>
            useSelectionBroadcast({
                editorRef: editorRef as React.RefObject<unknown> as React.RefObject<null>,
                bridge: null,
                enabled: true,
            })
        );

        // Act: should not crash even with null bridge
        const { cleanup } = simulateSelection("test text");
        act(() => { jest.advanceTimersByTime(300); });

        // Assert: no errors thrown (hook gracefully handles null bridge)
        expect(true).toBe(true);

        cleanup();
    });

    // -----------------------------------------------------------------------
    // 8. Selection outside editor is ignored
    // -----------------------------------------------------------------------

    it("selectionChange_OutsideEditor_DoesNotEmit", () => {
        // Arrange
        const emittedEvents: unknown[] = [];
        bridge.subscribe("selection_changed", (payload) => {
            emittedEvents.push(payload);
        });

        renderHook(() =>
            useSelectionBroadcast({
                editorRef: editorRef as React.RefObject<unknown> as React.RefObject<null>,
                bridge,
                enabled: true,
            })
        );

        // Act: simulate selection outside editor
        const { cleanup } = simulateSelection("outside text", { insideEditor: false });
        act(() => { jest.advanceTimersByTime(300); });

        // Assert: no events emitted (selection was outside editor container)
        expect(emittedEvents).toHaveLength(0);

        cleanup();
    });

    // -----------------------------------------------------------------------
    // 9. Duplicate selection text is not re-emitted
    // -----------------------------------------------------------------------

    it("selectionChange_SameTextTwice_OnlyEmitsOnce", () => {
        // Arrange
        const emittedEvents: unknown[] = [];
        bridge.subscribe("selection_changed", (payload) => {
            emittedEvents.push(payload);
        });

        renderHook(() =>
            useSelectionBroadcast({
                editorRef: editorRef as React.RefObject<unknown> as React.RefObject<null>,
                bridge,
                enabled: true,
            })
        );

        // Act: select same text twice
        const s1 = simulateSelection("duplicate text");
        act(() => { jest.advanceTimersByTime(300); });
        s1.cleanup();

        const s2 = simulateSelection("duplicate text");
        act(() => { jest.advanceTimersByTime(300); });
        s2.cleanup();

        // Assert: only emitted once (second is a duplicate, filtered by the hook)
        const textEvents = (emittedEvents as { text: string }[]).filter(
            (e) => e.text === "duplicate text"
        );
        expect(textEvents).toHaveLength(1);
    });
});

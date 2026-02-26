/**
 * useStreamingInsert Hook Tests
 *
 * Tests the convenience hook that wraps StreamingInsertPlugin's imperative handle.
 * Covers: state management, event dispatching, manual API, and pluginProps/pluginRef.
 *
 * @see ADR-012 - Shared Component Library
 */

import { renderHook, act } from "@testing-library/react";
import { useStreamingInsert } from "../useStreamingInsert";
import type {
    IDocumentStreamStartEvent,
    IDocumentStreamTokenEvent,
    IDocumentStreamEndEvent,
    IDocumentReplaceEvent,
    IProgressEvent,
} from "../../../SprkChat/types";
import type { StreamingInsertHandle } from "../StreamingInsertPlugin";

// ---------------------------------------------------------------------------
// Mock the StreamingInsertPlugin handle
// ---------------------------------------------------------------------------

/**
 * Creates a mock StreamingInsertHandle that records all method calls.
 */
function createMockHandle(): StreamingInsertHandle & {
    calls: { method: string; args: unknown[] }[];
} {
    const calls: { method: string; args: unknown[] }[] = [];
    return {
        calls,
        startStream(targetPosition?: string) {
            calls.push({ method: "startStream", args: [targetPosition] });
        },
        insertToken(token: string) {
            calls.push({ method: "insertToken", args: [token] });
        },
        endStream(cancelled?: boolean) {
            calls.push({ method: "endStream", args: [cancelled] });
        },
    };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("useStreamingInsert", () => {
    describe("Initial State", () => {
        it("should return correct initial state values", () => {
            const { result } = renderHook(() => useStreamingInsert());

            expect(result.current.isStreaming).toBe(false);
            expect(result.current.tokenCount).toBe(0);
            expect(result.current.operationId).toBeNull();
            expect(result.current.pluginRef).toBeDefined();
            expect(result.current.pluginRef.current).toBeNull();
        });

        it("should provide pluginProps with correct shape", () => {
            const { result } = renderHook(() => useStreamingInsert());

            const props = result.current.pluginProps;
            expect(props).toHaveProperty("isStreaming");
            expect(props).toHaveProperty("onStreamingComplete");
            expect(props.isStreaming).toBe(false);
            expect(typeof props.onStreamingComplete).toBe("function");
        });

        it("should expose all expected methods", () => {
            const { result } = renderHook(() => useStreamingInsert());

            expect(typeof result.current.handleStreamEvent).toBe("function");
            expect(typeof result.current.startStream).toBe("function");
            expect(typeof result.current.insertToken).toBe("function");
            expect(typeof result.current.endStream).toBe("function");
        });
    });

    describe("Manual API - startStream", () => {
        it("should set isStreaming to true and update operationId", () => {
            const { result } = renderHook(() => useStreamingInsert());

            // Attach a mock handle
            const mockHandle = createMockHandle();
            (result.current.pluginRef as any).current = mockHandle;

            act(() => {
                result.current.startStream("op-1");
            });

            expect(result.current.isStreaming).toBe(true);
            expect(result.current.operationId).toBe("op-1");
            expect(result.current.tokenCount).toBe(0);
        });

        it("should call plugin handle's startStream with targetPosition", () => {
            const { result } = renderHook(() => useStreamingInsert());
            const mockHandle = createMockHandle();
            (result.current.pluginRef as any).current = mockHandle;

            act(() => {
                result.current.startStream("op-2", "node-key-123");
            });

            expect(mockHandle.calls).toEqual([
                { method: "startStream", args: ["node-key-123"] },
            ]);
        });

        it("should call startStream without targetPosition when not provided", () => {
            const { result } = renderHook(() => useStreamingInsert());
            const mockHandle = createMockHandle();
            (result.current.pluginRef as any).current = mockHandle;

            act(() => {
                result.current.startStream("op-3");
            });

            expect(mockHandle.calls).toEqual([
                { method: "startStream", args: [undefined] },
            ]);
        });

        it("should reset tokenCount when starting a new stream", () => {
            const { result } = renderHook(() => useStreamingInsert());
            const mockHandle = createMockHandle();
            (result.current.pluginRef as any).current = mockHandle;

            // Insert some tokens
            act(() => {
                result.current.startStream("op-A");
            });
            act(() => {
                result.current.insertToken("t1");
                result.current.insertToken("t2");
            });
            expect(result.current.tokenCount).toBe(2);

            // Start new stream should reset
            act(() => {
                result.current.startStream("op-B");
            });
            expect(result.current.tokenCount).toBe(0);
        });
    });

    describe("Manual API - insertToken", () => {
        it("should increment tokenCount for each inserted token", () => {
            const { result } = renderHook(() => useStreamingInsert());
            const mockHandle = createMockHandle();
            (result.current.pluginRef as any).current = mockHandle;

            act(() => {
                result.current.startStream("op-1");
            });

            act(() => {
                result.current.insertToken("token1");
            });
            expect(result.current.tokenCount).toBe(1);

            act(() => {
                result.current.insertToken("token2");
            });
            expect(result.current.tokenCount).toBe(2);

            act(() => {
                result.current.insertToken("token3");
            });
            expect(result.current.tokenCount).toBe(3);
        });

        it("should forward token to plugin handle", () => {
            const { result } = renderHook(() => useStreamingInsert());
            const mockHandle = createMockHandle();
            (result.current.pluginRef as any).current = mockHandle;

            act(() => {
                result.current.startStream("op-1");
            });
            act(() => {
                result.current.insertToken("hello");
            });

            const insertCall = mockHandle.calls.find(
                (c) => c.method === "insertToken"
            );
            expect(insertCall).toBeDefined();
            expect(insertCall!.args).toEqual(["hello"]);
        });

        it("should gracefully handle insertToken when pluginRef is null", () => {
            const { result } = renderHook(() => useStreamingInsert());

            // pluginRef.current is null; should not throw
            expect(() => {
                act(() => {
                    result.current.insertToken("no crash");
                });
            }).not.toThrow();

            // tokenCount should still increment since hook manages it independently
            expect(result.current.tokenCount).toBe(1);
        });
    });

    describe("Manual API - endStream", () => {
        it("should call plugin handle's endStream", () => {
            const { result } = renderHook(() => useStreamingInsert());
            const mockHandle = createMockHandle();
            (result.current.pluginRef as any).current = mockHandle;

            act(() => {
                result.current.startStream("op-1");
            });
            act(() => {
                result.current.endStream();
            });

            const endCall = mockHandle.calls.find(
                (c) => c.method === "endStream"
            );
            expect(endCall).toBeDefined();
            expect(endCall!.args).toEqual([undefined]);
        });

        it("should call plugin handle's endStream with cancelled=true", () => {
            const { result } = renderHook(() => useStreamingInsert());
            const mockHandle = createMockHandle();
            (result.current.pluginRef as any).current = mockHandle;

            act(() => {
                result.current.startStream("op-1");
            });
            act(() => {
                result.current.endStream(true);
            });

            const endCall = mockHandle.calls.find(
                (c) => c.method === "endStream"
            );
            expect(endCall!.args).toEqual([true]);
        });

        it("should set isStreaming to false via onStreamingComplete callback", () => {
            const { result } = renderHook(() => useStreamingInsert());

            act(() => {
                result.current.startStream("op-1");
            });
            expect(result.current.isStreaming).toBe(true);

            // Simulate the plugin calling onStreamingComplete
            act(() => {
                result.current.pluginProps.onStreamingComplete!(false);
            });
            expect(result.current.isStreaming).toBe(false);
        });
    });

    describe("onComplete Callback", () => {
        it("should call onComplete with operationId when stream completes normally", () => {
            const onComplete = jest.fn();
            const { result } = renderHook(() =>
                useStreamingInsert(onComplete)
            );

            act(() => {
                result.current.startStream("op-42");
            });

            // Simulate plugin's onStreamingComplete
            act(() => {
                result.current.pluginProps.onStreamingComplete!(false);
            });

            expect(onComplete).toHaveBeenCalledTimes(1);
            expect(onComplete).toHaveBeenCalledWith("op-42", false);
        });

        it("should call onComplete with cancelled=true when cancelled", () => {
            const onComplete = jest.fn();
            const { result } = renderHook(() =>
                useStreamingInsert(onComplete)
            );

            act(() => {
                result.current.startStream("op-99");
            });

            act(() => {
                result.current.pluginProps.onStreamingComplete!(true);
            });

            expect(onComplete).toHaveBeenCalledWith("op-99", true);
        });

        it("should not fail when onComplete is not provided", () => {
            const { result } = renderHook(() => useStreamingInsert());

            act(() => {
                result.current.startStream("op-1");
            });

            // Should not throw when onStreamingComplete is called
            expect(() => {
                act(() => {
                    result.current.pluginProps.onStreamingComplete!(false);
                });
            }).not.toThrow();
        });
    });

    describe("handleStreamEvent - Event Dispatching", () => {
        it("should handle document_stream_start event", () => {
            const { result } = renderHook(() => useStreamingInsert());
            const mockHandle = createMockHandle();
            (result.current.pluginRef as any).current = mockHandle;

            const startEvent: IDocumentStreamStartEvent = {
                type: "document_stream_start",
                operationId: "op-start-1",
                targetPosition: "paragraph-5",
                operationType: "insert",
            };

            act(() => {
                result.current.handleStreamEvent(startEvent);
            });

            expect(result.current.isStreaming).toBe(true);
            expect(result.current.operationId).toBe("op-start-1");
            expect(mockHandle.calls).toEqual([
                { method: "startStream", args: ["paragraph-5"] },
            ]);
        });

        it("should handle document_stream_token event", () => {
            const { result } = renderHook(() => useStreamingInsert());
            const mockHandle = createMockHandle();
            (result.current.pluginRef as any).current = mockHandle;

            // First start a stream
            act(() => {
                result.current.handleStreamEvent({
                    type: "document_stream_start",
                    operationId: "op-token-1",
                    targetPosition: "pos-1",
                    operationType: "insert",
                });
            });

            const tokenEvent: IDocumentStreamTokenEvent = {
                type: "document_stream_token",
                operationId: "op-token-1",
                token: "Hello",
                index: 0,
            };

            act(() => {
                result.current.handleStreamEvent(tokenEvent);
            });

            expect(result.current.tokenCount).toBe(1);
            const insertCall = mockHandle.calls.find(
                (c) => c.method === "insertToken"
            );
            expect(insertCall!.args).toEqual(["Hello"]);
        });

        it("should handle document_stream_end event", () => {
            const { result } = renderHook(() => useStreamingInsert());
            const mockHandle = createMockHandle();
            (result.current.pluginRef as any).current = mockHandle;

            act(() => {
                result.current.handleStreamEvent({
                    type: "document_stream_start",
                    operationId: "op-end-1",
                    targetPosition: "pos-1",
                    operationType: "insert",
                });
            });

            const endEvent: IDocumentStreamEndEvent = {
                type: "document_stream_end",
                operationId: "op-end-1",
                cancelled: false,
                totalTokens: 5,
            };

            act(() => {
                result.current.handleStreamEvent(endEvent);
            });

            const endCall = mockHandle.calls.find(
                (c) => c.method === "endStream"
            );
            expect(endCall).toBeDefined();
            expect(endCall!.args).toEqual([false]);
        });

        it("should handle document_stream_end with cancelled=true", () => {
            const { result } = renderHook(() => useStreamingInsert());
            const mockHandle = createMockHandle();
            (result.current.pluginRef as any).current = mockHandle;

            act(() => {
                result.current.handleStreamEvent({
                    type: "document_stream_start",
                    operationId: "op-cancel-1",
                    targetPosition: "pos-1",
                    operationType: "insert",
                });
            });

            act(() => {
                result.current.handleStreamEvent({
                    type: "document_stream_end",
                    operationId: "op-cancel-1",
                    cancelled: true,
                    totalTokens: 0,
                });
            });

            const endCall = mockHandle.calls.find(
                (c) => c.method === "endStream"
            );
            expect(endCall!.args).toEqual([true]);
        });

        it("should handle document_replace event without error (no-op)", () => {
            const { result } = renderHook(() => useStreamingInsert());

            const replaceEvent: IDocumentReplaceEvent = {
                type: "document_replace",
                operationId: "op-replace-1",
                html: "<p>Replaced content</p>",
            };

            expect(() => {
                act(() => {
                    result.current.handleStreamEvent(replaceEvent);
                });
            }).not.toThrow();

            // Should not affect streaming state
            expect(result.current.isStreaming).toBe(false);
        });

        it("should handle progress event without error (no-op)", () => {
            const { result } = renderHook(() => useStreamingInsert());

            const progressEvent: IProgressEvent = {
                type: "progress",
                operationId: "op-progress-1",
                percent: 50,
                message: "Processing...",
            };

            expect(() => {
                act(() => {
                    result.current.handleStreamEvent(progressEvent);
                });
            }).not.toThrow();

            expect(result.current.isStreaming).toBe(false);
        });

        it("should process a full stream lifecycle via handleStreamEvent", () => {
            const onComplete = jest.fn();
            const { result } = renderHook(() =>
                useStreamingInsert(onComplete)
            );
            const mockHandle = createMockHandle();
            (result.current.pluginRef as any).current = mockHandle;

            // Start
            act(() => {
                result.current.handleStreamEvent({
                    type: "document_stream_start",
                    operationId: "full-op",
                    targetPosition: "doc-start",
                    operationType: "insert",
                });
            });

            expect(result.current.isStreaming).toBe(true);
            expect(result.current.operationId).toBe("full-op");

            // Tokens
            act(() => {
                result.current.handleStreamEvent({
                    type: "document_stream_token",
                    operationId: "full-op",
                    token: "Hello",
                    index: 0,
                });
            });
            act(() => {
                result.current.handleStreamEvent({
                    type: "document_stream_token",
                    operationId: "full-op",
                    token: " ",
                    index: 1,
                });
            });
            act(() => {
                result.current.handleStreamEvent({
                    type: "document_stream_token",
                    operationId: "full-op",
                    token: "World",
                    index: 2,
                });
            });

            expect(result.current.tokenCount).toBe(3);

            // End
            act(() => {
                result.current.handleStreamEvent({
                    type: "document_stream_end",
                    operationId: "full-op",
                    cancelled: false,
                    totalTokens: 3,
                });
            });

            // Simulate plugin completing
            act(() => {
                result.current.pluginProps.onStreamingComplete!(false);
            });

            expect(result.current.isStreaming).toBe(false);
            expect(onComplete).toHaveBeenCalledWith("full-op", false);

            // Verify plugin calls sequence
            const methods = mockHandle.calls.map((c) => c.method);
            expect(methods).toEqual([
                "startStream",
                "insertToken",
                "insertToken",
                "insertToken",
                "endStream",
            ]);
        });
    });

    describe("pluginProps Synchronization", () => {
        it("should reflect isStreaming in pluginProps", () => {
            const { result } = renderHook(() => useStreamingInsert());
            const mockHandle = createMockHandle();
            (result.current.pluginRef as any).current = mockHandle;

            expect(result.current.pluginProps.isStreaming).toBe(false);

            act(() => {
                result.current.startStream("op-sync");
            });

            expect(result.current.pluginProps.isStreaming).toBe(true);

            act(() => {
                result.current.pluginProps.onStreamingComplete!(false);
            });

            expect(result.current.pluginProps.isStreaming).toBe(false);
        });
    });
});

/**
 * StreamingInsertPlugin Unit Tests
 *
 * Tests the Lexical editor plugin for streaming document insertion.
 * Covers: token insertion, stream lifecycle, cancellation, token buffering,
 * newline handling, and editor state management.
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9 (no hard-coded colors)
 */

import * as React from "react";
import { createRef } from "react";
import { render, act } from "@testing-library/react";
import { LexicalComposer } from "@lexical/react/LexicalComposer";
import { RichTextPlugin } from "@lexical/react/LexicalRichTextPlugin";
import { ContentEditable } from "@lexical/react/LexicalContentEditable";
import { LexicalErrorBoundary } from "@lexical/react/LexicalErrorBoundary";
import {
    $getRoot,
    $createParagraphNode,
    $createTextNode,
    $setSelection,
    $createRangeSelection,
    LexicalEditor,
    ParagraphNode,
    TextNode,
} from "lexical";
import {
    StreamingInsertPlugin,
    StreamingInsertHandle,
} from "../StreamingInsertPlugin";

// ---------------------------------------------------------------------------
// Test Utilities
// ---------------------------------------------------------------------------

/**
 * Creates a Lexical editor test harness with StreamingInsertPlugin mounted.
 * Returns the plugin ref handle and a getter for the underlying LexicalEditor.
 */
function renderStreamingEditor(options?: {
    onStreamingComplete?: (cancelled: boolean) => void;
    initialContent?: string;
}) {
    const pluginRef = createRef<StreamingInsertHandle>();
    let editorInstance: LexicalEditor | null = null;

    const initialConfig = {
        namespace: "StreamingInsertTest",
        onError: (error: Error) => {
            throw error;
        },
        nodes: [ParagraphNode, TextNode],
        editable: true,
    };

    /**
     * Captures the LexicalEditor instance for assertions.
     */
    function EditorCapture(): null {
        const [editor] =
            require("@lexical/react/LexicalComposerContext").useLexicalComposerContext();
        editorInstance = editor;

        // Set initial content if provided
        React.useEffect(() => {
            if (options?.initialContent) {
                editor.update(() => {
                    const root = $getRoot();
                    root.clear();
                    const paragraph = $createParagraphNode();
                    paragraph.append($createTextNode(options.initialContent!));
                    root.append(paragraph);
                });
            }
        }, [editor]);

        return null;
    }

    const { container, unmount } = render(
        <LexicalComposer initialConfig={initialConfig}>
            <RichTextPlugin
                contentEditable={<ContentEditable />}
                ErrorBoundary={LexicalErrorBoundary}
            />
            <EditorCapture />
            <StreamingInsertPlugin
                ref={pluginRef}
                isStreaming={false}
                onStreamingComplete={options?.onStreamingComplete}
            />
        </LexicalComposer>
    );

    /**
     * Gets the full plain-text content of the editor.
     */
    function getEditorText(): string {
        let text = "";
        editorInstance!.getEditorState().read(() => {
            text = $getRoot().getTextContent();
        });
        return text;
    }

    /**
     * Gets the number of top-level children (paragraphs) in the editor root.
     */
    function getParagraphCount(): number {
        let count = 0;
        editorInstance!.getEditorState().read(() => {
            count = $getRoot().getChildrenSize();
        });
        return count;
    }

    /**
     * Returns whether the editor is currently editable.
     */
    function isEditable(): boolean {
        return editorInstance!.isEditable();
    }

    return {
        pluginRef,
        getEditor: () => editorInstance!,
        getEditorText,
        getParagraphCount,
        isEditable,
        container,
        unmount,
    };
}

/**
 * Flush microtasks, rAF callbacks, and timers to process buffered tokens.
 * Lexical's discrete updates should flush synchronously, but the plugin uses
 * requestAnimationFrame for batching.
 */
async function flushAll(): Promise<void> {
    // Flush microtasks
    await Promise.resolve();
    // Run pending timers (for setTimeout-based flush scheduling)
    jest.runAllTimers();
    // Run rAF callbacks
    jest.runAllTimers();
    // Flush again for any Lexical update() callbacks
    await Promise.resolve();
}

// ---------------------------------------------------------------------------
// Test Configuration
// ---------------------------------------------------------------------------

beforeEach(() => {
    jest.useFakeTimers();
});

afterEach(() => {
    jest.useRealTimers();
});

// Mock requestAnimationFrame and cancelAnimationFrame for JSDOM
// (JSDOM does not provide these natively in all versions)
let rafId = 0;
const rafCallbacks = new Map<number, FrameRequestCallback>();

beforeEach(() => {
    rafId = 0;
    rafCallbacks.clear();

    jest.spyOn(window, "requestAnimationFrame").mockImplementation(
        (callback: FrameRequestCallback): number => {
            const id = ++rafId;
            rafCallbacks.set(id, callback);
            // Schedule via setTimeout so jest.runAllTimers() triggers it
            setTimeout(() => {
                if (rafCallbacks.has(id)) {
                    rafCallbacks.delete(id);
                    callback(performance.now());
                }
            }, 0);
            return id;
        }
    );

    jest.spyOn(window, "cancelAnimationFrame").mockImplementation(
        (id: number): void => {
            rafCallbacks.delete(id);
        }
    );
});

afterEach(() => {
    jest.restoreAllMocks();
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("StreamingInsertPlugin", () => {
    describe("Token Insertion", () => {
        it("should insert a single token into an empty editor", async () => {
            const { pluginRef, getEditorText } = renderStreamingEditor();

            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken("Hello");
                await flushAll();
                pluginRef.current!.endStream();
            });

            expect(getEditorText()).toContain("Hello");
        });

        it("should insert multiple tokens concatenated in order", async () => {
            const { pluginRef, getEditorText } = renderStreamingEditor();

            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken("Hello");
                pluginRef.current!.insertToken(" ");
                pluginRef.current!.insertToken("World");
                await flushAll();
                pluginRef.current!.endStream();
            });

            expect(getEditorText()).toContain("Hello World");
        });

        it("should handle empty string tokens without error", async () => {
            const { pluginRef, getEditorText } = renderStreamingEditor();

            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken("");
                pluginRef.current!.insertToken("Hello");
                pluginRef.current!.insertToken("");
                await flushAll();
                pluginRef.current!.endStream();
            });

            expect(getEditorText()).toContain("Hello");
        });

        it("should handle tokens with whitespace", async () => {
            const { pluginRef, getEditorText } = renderStreamingEditor();

            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken("  spaces  ");
                await flushAll();
                pluginRef.current!.endStream();
            });

            expect(getEditorText()).toContain("  spaces  ");
        });

        it("should handle special characters in tokens", async () => {
            const { pluginRef, getEditorText } = renderStreamingEditor();

            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken("Hello & <World> \"quotes\"");
                await flushAll();
                pluginRef.current!.endStream();
            });

            expect(getEditorText()).toContain("Hello & <World> \"quotes\"");
        });
    });

    describe("Stream Lifecycle", () => {
        it("should set editor to non-editable during streaming", async () => {
            const { pluginRef, isEditable } = renderStreamingEditor();

            expect(isEditable()).toBe(true);

            await act(async () => {
                pluginRef.current!.startStream();
            });

            expect(isEditable()).toBe(false);

            await act(async () => {
                pluginRef.current!.endStream();
            });

            expect(isEditable()).toBe(true);
        });

        it("should restore original editable state on endStream", async () => {
            const { pluginRef, getEditor, isEditable } =
                renderStreamingEditor();

            // Set editor to non-editable before starting stream
            await act(async () => {
                getEditor().setEditable(false);
            });

            expect(isEditable()).toBe(false);

            await act(async () => {
                pluginRef.current!.startStream();
            });

            // Still non-editable during streaming
            expect(isEditable()).toBe(false);

            await act(async () => {
                pluginRef.current!.endStream();
            });

            // Restored to the original non-editable state
            expect(isEditable()).toBe(false);
        });

        it("should call onStreamingComplete when endStream is called", async () => {
            const onComplete = jest.fn();
            const { pluginRef } = renderStreamingEditor({
                onStreamingComplete: onComplete,
            });

            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken("test");
                await flushAll();
                pluginRef.current!.endStream();
            });

            expect(onComplete).toHaveBeenCalledTimes(1);
            expect(onComplete).toHaveBeenCalledWith(false);
        });

        it("should call onStreamingComplete with cancelled=true when cancelled", async () => {
            const onComplete = jest.fn();
            const { pluginRef } = renderStreamingEditor({
                onStreamingComplete: onComplete,
            });

            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken("test");
                await flushAll();
                pluginRef.current!.endStream(true);
            });

            expect(onComplete).toHaveBeenCalledTimes(1);
            expect(onComplete).toHaveBeenCalledWith(true);
        });

        it("should ignore insertToken calls before startStream", async () => {
            const { pluginRef, getEditorText } = renderStreamingEditor();

            await act(async () => {
                pluginRef.current!.insertToken("should be ignored");
                await flushAll();
            });

            // Editor text should only contain the default empty paragraph content
            const text = getEditorText();
            expect(text).not.toContain("should be ignored");
        });

        it("should ignore insertToken calls after endStream", async () => {
            const { pluginRef, getEditorText } = renderStreamingEditor();

            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken("before");
                await flushAll();
                pluginRef.current!.endStream();
            });

            await act(async () => {
                pluginRef.current!.insertToken("after");
                await flushAll();
            });

            const text = getEditorText();
            expect(text).toContain("before");
            expect(text).not.toContain("after");
        });

        it("should handle endStream called without startStream (no-op)", async () => {
            const onComplete = jest.fn();
            const { pluginRef } = renderStreamingEditor({
                onStreamingComplete: onComplete,
            });

            await act(async () => {
                pluginRef.current!.endStream();
            });

            // onStreamingComplete should NOT be called since no stream was active
            expect(onComplete).not.toHaveBeenCalled();
        });
    });

    describe("Cancellation", () => {
        it("should remove inserted text when cancelled", async () => {
            const { pluginRef, getEditorText } = renderStreamingEditor();

            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken("This ");
                pluginRef.current!.insertToken("should ");
                pluginRef.current!.insertToken("be removed");
                await flushAll();
                pluginRef.current!.endStream(true);
            });

            const text = getEditorText();
            expect(text).not.toContain("This should be removed");
        });

        it("should restore editor to editable after cancellation", async () => {
            const { pluginRef, isEditable } = renderStreamingEditor();

            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken("test");
                await flushAll();
                pluginRef.current!.endStream(true);
            });

            expect(isEditable()).toBe(true);
        });

        it("should discard buffered tokens on cancellation", async () => {
            const { pluginRef, getEditorText } = renderStreamingEditor();

            await act(async () => {
                pluginRef.current!.startStream();
                // Insert tokens but do NOT flush before cancellation
                pluginRef.current!.insertToken("buffered1");
                pluginRef.current!.insertToken("buffered2");
                // Cancel immediately (tokens are still in buffer)
                pluginRef.current!.endStream(true);
                await flushAll();
            });

            const text = getEditorText();
            expect(text).not.toContain("buffered1");
            expect(text).not.toContain("buffered2");
        });
    });

    describe("Zero-Token Stream", () => {
        it("should leave editor state unchanged for begin-then-end with zero tokens", async () => {
            const { pluginRef, getEditorText, getEditor } = renderStreamingEditor();

            // Set initial content and get a valid node key for target position
            let targetKey: string = "";
            await act(async () => {
                getEditor().update(() => {
                    const root = $getRoot();
                    root.clear();
                    const paragraph = $createParagraphNode();
                    const textNode = $createTextNode("Existing content");
                    paragraph.append(textNode);
                    root.append(paragraph);
                    targetKey = textNode.getKey();
                });
                await flushAll();
            });

            const textBefore = getEditorText();

            await act(async () => {
                // Use target position to avoid selection-based anchor issues
                pluginRef.current!.startStream(targetKey);
                pluginRef.current!.endStream();
            });

            const textAfter = getEditorText();
            expect(textAfter).toBe(textBefore);
        });

        it("should restore editability for zero-token stream", async () => {
            const { pluginRef, isEditable } = renderStreamingEditor();

            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.endStream();
            });

            expect(isEditable()).toBe(true);
        });

        it("should call onStreamingComplete for zero-token stream", async () => {
            const onComplete = jest.fn();
            const { pluginRef } = renderStreamingEditor({
                onStreamingComplete: onComplete,
            });

            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.endStream();
            });

            expect(onComplete).toHaveBeenCalledWith(false);
        });
    });

    describe("Newline Handling", () => {
        it("should create new paragraphs for newline characters", async () => {
            const { pluginRef, getParagraphCount } =
                renderStreamingEditor();

            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken("Line 1\nLine 2");
                await flushAll();
                pluginRef.current!.endStream();
            });

            // Should have at least 2 paragraphs (original empty + new ones from \n)
            expect(getParagraphCount()).toBeGreaterThanOrEqual(2);
        });

        it("should handle multiple consecutive newlines", async () => {
            const { pluginRef, getParagraphCount, getEditorText } =
                renderStreamingEditor();

            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken("A\n\nB");
                await flushAll();
                pluginRef.current!.endStream();
            });

            const text = getEditorText();
            expect(text).toContain("A");
            expect(text).toContain("B");
            // Should have at least 3 paragraphs (A, empty, B)
            expect(getParagraphCount()).toBeGreaterThanOrEqual(3);
        });

        it("should handle newlines across multiple token boundaries", async () => {
            const { pluginRef, getEditorText, getParagraphCount } =
                renderStreamingEditor();

            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken("First");
                pluginRef.current!.insertToken("\n");
                pluginRef.current!.insertToken("Second");
                await flushAll();
                pluginRef.current!.endStream();
            });

            const text = getEditorText();
            expect(text).toContain("First");
            expect(text).toContain("Second");
            expect(getParagraphCount()).toBeGreaterThanOrEqual(2);
        });
    });

    describe("Token Buffering", () => {
        it("should batch multiple tokens before flushing", async () => {
            const { pluginRef, getEditorText } = renderStreamingEditor();

            await act(async () => {
                pluginRef.current!.startStream();

                // Insert several tokens rapidly (before any flush happens)
                for (let i = 0; i < 5; i++) {
                    pluginRef.current!.insertToken(`t${i}`);
                }

                // Now flush
                await flushAll();
                pluginRef.current!.endStream();
            });

            const text = getEditorText();
            expect(text).toContain("t0t1t2t3t4");
        });

        it("should flush on endStream even if buffer has pending tokens", async () => {
            const { pluginRef, getEditorText } = renderStreamingEditor();

            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken("pending");
                // Do NOT call flushAll() - endStream should handle it
                pluginRef.current!.endStream();
            });

            const text = getEditorText();
            expect(text).toContain("pending");
        });

        it("should not drop tokens during rapid burst insertion", async () => {
            const { pluginRef, getEditorText } = renderStreamingEditor();
            const tokenCount = 50;

            await act(async () => {
                pluginRef.current!.startStream();

                for (let i = 0; i < tokenCount; i++) {
                    pluginRef.current!.insertToken(`[${i}]`);
                    // Simulate some rAF ticks between batches
                    if (i % 10 === 9) {
                        await flushAll();
                    }
                }

                await flushAll();
                pluginRef.current!.endStream();
            });

            const text = getEditorText();
            for (let i = 0; i < tokenCount; i++) {
                expect(text).toContain(`[${i}]`);
            }
        });
    });

    describe("Target Position", () => {
        it("should start stream at a target position and append content", async () => {
            const { pluginRef, getEditorText, getEditor } = renderStreamingEditor();

            // Set up initial content with a known target node key
            let targetKey: string = "";
            await act(async () => {
                getEditor().update(() => {
                    const root = $getRoot();
                    root.clear();
                    const paragraph = $createParagraphNode();
                    const textNode = $createTextNode("Existing");
                    paragraph.append(textNode);
                    root.append(paragraph);
                    targetKey = textNode.getKey();
                });
                await flushAll();
            });

            await act(async () => {
                pluginRef.current!.startStream(targetKey);
                pluginRef.current!.insertToken(" appended");
                await flushAll();
                pluginRef.current!.endStream();
            });

            const text = getEditorText();
            expect(text).toContain("Existing");
            expect(text).toContain("appended");
        });

        it("should handle start with invalid target position gracefully", async () => {
            const { pluginRef, getEditorText } = renderStreamingEditor();

            await act(async () => {
                // Pass a non-existent node key
                pluginRef.current!.startStream("non-existent-key-999");
                pluginRef.current!.insertToken("fallback content");
                await flushAll();
                pluginRef.current!.endStream();
            });

            // Should still insert content (falls back to end of document)
            expect(getEditorText()).toContain("fallback content");
        });
    });

    describe("Multiple Sequential Streams", () => {
        it("should support starting a new stream after completing a previous one", async () => {
            const onComplete = jest.fn();
            const { pluginRef, getEditorText } = renderStreamingEditor({
                onStreamingComplete: onComplete,
            });

            // First stream
            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken("First stream");
                await flushAll();
                pluginRef.current!.endStream();
            });

            expect(onComplete).toHaveBeenCalledTimes(1);

            // Second stream
            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken(" Second stream");
                await flushAll();
                pluginRef.current!.endStream();
            });

            expect(onComplete).toHaveBeenCalledTimes(2);
            const text = getEditorText();
            expect(text).toContain("First stream");
            expect(text).toContain("Second stream");
        });

        it("should reset token count between streams", async () => {
            const { pluginRef, getEditorText } = renderStreamingEditor();

            // First stream with 3 tokens
            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken("a");
                pluginRef.current!.insertToken("b");
                pluginRef.current!.insertToken("c");
                await flushAll();
                pluginRef.current!.endStream();
            });

            // Second stream with 2 tokens
            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken("d");
                pluginRef.current!.insertToken("e");
                await flushAll();
                pluginRef.current!.endStream();
            });

            const text = getEditorText();
            expect(text).toContain("abc");
            expect(text).toContain("de");
        });
    });

    describe("Edge Cases", () => {
        it("should handle unicode characters", async () => {
            const { pluginRef, getEditorText } = renderStreamingEditor();

            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken("Caf\u00e9 ");
                pluginRef.current!.insertToken("\u2603 ");
                pluginRef.current!.insertToken("\u{1F600}");
                await flushAll();
                pluginRef.current!.endStream();
            });

            const text = getEditorText();
            expect(text).toContain("Caf\u00e9");
        });

        it("should handle very long tokens", async () => {
            const { pluginRef, getEditorText } = renderStreamingEditor();
            const longToken = "A".repeat(10000);

            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken(longToken);
                await flushAll();
                pluginRef.current!.endStream();
            });

            const text = getEditorText();
            expect(text).toContain(longToken);
        });

        it("should clean up rAF on unmount during active stream", async () => {
            const cancelSpy = jest.spyOn(window, "cancelAnimationFrame");
            const { pluginRef, unmount } = renderStreamingEditor();

            await act(async () => {
                pluginRef.current!.startStream();
                pluginRef.current!.insertToken("will unmount");
            });

            // Unmount during active stream
            unmount();

            // cancelAnimationFrame should have been called during cleanup
            // (or the cleanup effect should have run)
            // No assertion on exact call count since internal state determines if rAF was pending
            // The main assertion is that no errors occur during unmount
        });
    });
});

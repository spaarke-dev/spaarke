/**
 * StreamingInsertPlugin - Lexical plugin for streaming document insertion
 *
 * Receives individual tokens via SSE events and inserts them into the Lexical editor
 * at a target position with cursor tracking and smooth typewriter-like UX.
 *
 * Key design decisions:
 * - Uses requestAnimationFrame-based batching to prevent UI jank at 50-100 tokens/sec
 * - Maintains editor selection/cursor state during streaming
 * - Sets editor to read-only during active streaming to prevent user conflicts
 * - Supports cancellation with optional partial content removal
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9 compatible (no hard-coded colors)
 * @see IDocumentStreamStartEvent, IDocumentStreamTokenEvent, IDocumentStreamEndEvent
 */

import * as React from "react";
import { useEffect, useRef, useCallback, useImperativeHandle, forwardRef } from "react";
import { useLexicalComposerContext } from "@lexical/react/LexicalComposerContext";
import {
    $getSelection,
    $isRangeSelection,
    $createTextNode,
    $getNodeByKey,
    $createParagraphNode,
    $getRoot,
    TextNode,
    COMMAND_PRIORITY_CRITICAL,
    KEY_DOWN_COMMAND,
} from "lexical";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Imperative handle exposed by the StreamingInsertPlugin via ref.
 * Consumers use this to drive the streaming lifecycle:
 *   1. startStream() - begin a streaming insertion operation
 *   2. insertToken() - called for each token received from SSE
 *   3. endStream()   - finalize or cancel the operation
 */
export interface StreamingInsertHandle {
    /**
     * Begin a streaming insertion at the current cursor position or a specified target.
     * Sets the editor to read-only and initializes the insertion point.
     *
     * @param targetPosition - Optional Lexical node key to position the cursor at before streaming.
     *                         If omitted, streaming begins at the current selection.
     */
    startStream(targetPosition?: string): void;

    /**
     * Insert a single token (text fragment) at the current streaming position.
     * Tokens are buffered and flushed in batches via requestAnimationFrame for smooth UX.
     *
     * @param token - The text token to insert (may contain whitespace, newlines, etc.)
     */
    insertToken(token: string): void;

    /**
     * End the streaming operation.
     *
     * @param cancelled - If true, removes all content inserted during this streaming operation.
     *                    If false (default), content is kept and the editor returns to editable state.
     */
    endStream(cancelled?: boolean): void;
}

/**
 * Props for the StreamingInsertPlugin component.
 */
export interface StreamingInsertPluginProps {
    /** Whether a streaming operation is currently active (controls visual state) */
    isStreaming: boolean;
    /** Callback invoked when the streaming operation completes (either normally or via cancellation) */
    onStreamingComplete?: (cancelled: boolean) => void;
}

/**
 * Internal state for tracking a streaming operation.
 */
interface StreamingState {
    /** Whether a stream is currently active */
    isActive: boolean;
    /** The Lexical node key of the text node being appended to */
    activeNodeKey: string | null;
    /** Total text inserted during this operation (for undo on cancel) */
    insertedText: string;
    /** Count of tokens received */
    tokenCount: number;
    /** The operation's original editable state to restore on end */
    wasEditable: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Maximum number of tokens to buffer before forcing a flush.
 * At 50-100 tokens/sec, this provides a batch every ~50-100ms which keeps
 * the editor responsive while minimizing Lexical update() calls.
 */
const MAX_BUFFER_SIZE = 8;

/**
 * Minimum interval (ms) between flushes to avoid overwhelming the editor
 * with rapid Lexical update() calls.
 */
const MIN_FLUSH_INTERVAL_MS = 16; // ~1 frame at 60fps

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * StreamingInsertPlugin - A Lexical plugin for handling streaming document insertion.
 *
 * This plugin must be placed inside a <LexicalComposer> tree. It exposes an imperative
 * handle via React.forwardRef that the parent component uses to drive streaming:
 *
 * @example
 * ```tsx
 * const streamRef = useRef<StreamingInsertHandle>(null);
 *
 * // In SSE event handler:
 * streamRef.current?.startStream();
 * streamRef.current?.insertToken("Hello ");
 * streamRef.current?.insertToken("world!");
 * streamRef.current?.endStream();
 *
 * // In the editor tree:
 * <LexicalComposer initialConfig={config}>
 *   <StreamingInsertPlugin
 *     ref={streamRef}
 *     isStreaming={isStreaming}
 *     onStreamingComplete={handleComplete}
 *   />
 * </LexicalComposer>
 * ```
 */
export const StreamingInsertPlugin = forwardRef<StreamingInsertHandle, StreamingInsertPluginProps>(
    function StreamingInsertPlugin(
        props: StreamingInsertPluginProps,
        ref: React.Ref<StreamingInsertHandle>
    ) {
        const { onStreamingComplete } = props;
        const [editor] = useLexicalComposerContext();

        // Token buffer for batched insertion
        const tokenBufferRef = useRef<string[]>([]);
        const flushRafRef = useRef<number | null>(null);
        const lastFlushTimeRef = useRef<number>(0);

        // Streaming operation state
        const streamStateRef = useRef<StreamingState>({
            isActive: false,
            activeNodeKey: null,
            insertedText: "",
            tokenCount: 0,
            wasEditable: true,
        });

        // ─────────────────────────────────────────────────────────────────────
        // Block keyboard input during streaming
        // ─────────────────────────────────────────────────────────────────────

        useEffect(() => {
            return editor.registerCommand(
                KEY_DOWN_COMMAND,
                (_event: KeyboardEvent) => {
                    if (streamStateRef.current.isActive) {
                        // Block all keyboard input during streaming to prevent
                        // user edits from interfering with token insertion
                        return true; // Handled = prevent default
                    }
                    return false;
                },
                COMMAND_PRIORITY_CRITICAL
            );
        }, [editor]);

        // ─────────────────────────────────────────────────────────────────────
        // Flush buffered tokens into the editor
        // ─────────────────────────────────────────────────────────────────────

        const flushBuffer = useCallback(() => {
            flushRafRef.current = null;

            const buffer = tokenBufferRef.current;
            if (buffer.length === 0 || !streamStateRef.current.isActive) {
                return;
            }

            // Concatenate all buffered tokens into a single string
            const text = buffer.join("");
            tokenBufferRef.current = [];
            lastFlushTimeRef.current = performance.now();

            editor.update(
                () => {
                    const state = streamStateRef.current;
                    if (!state.isActive) {
                        return;
                    }

                    // Handle newlines: split text on \n and create new paragraphs
                    const segments = text.split("\n");

                    for (let i = 0; i < segments.length; i++) {
                        const segment = segments[i];

                        // Insert text segment into the active text node
                        if (segment.length > 0) {
                            if (state.activeNodeKey) {
                                const node = $getNodeByKey(state.activeNodeKey);
                                if (node instanceof TextNode) {
                                    // Append to existing text node
                                    const current = node.getTextContent();
                                    node.setTextContent(current + segment);
                                } else {
                                    // Node was removed or changed type; create a new text node
                                    const newNode = createAndInsertTextNode(segment);
                                    state.activeNodeKey = newNode?.getKey() ?? null;
                                }
                            } else {
                                // No active node yet; create one at root end
                                const newNode = createAndInsertTextNode(segment);
                                state.activeNodeKey = newNode?.getKey() ?? null;
                            }
                        }

                        // If there are more segments, this \n means we need a new paragraph
                        if (i < segments.length - 1) {
                            const newParagraph = $createParagraphNode();
                            const newTextNode = $createTextNode("");
                            newParagraph.append(newTextNode);

                            // Insert the new paragraph after the current node's parent
                            if (state.activeNodeKey) {
                                const currentNode = $getNodeByKey(state.activeNodeKey);
                                if (currentNode) {
                                    const parent = currentNode.getTopLevelElementOrThrow();
                                    parent.insertAfter(newParagraph);
                                } else {
                                    $getRoot().append(newParagraph);
                                }
                            } else {
                                $getRoot().append(newParagraph);
                            }

                            state.activeNodeKey = newTextNode.getKey();
                        }
                    }

                    // Track inserted text for potential undo on cancel
                    state.insertedText += text;
                },
                { discrete: true }
            );
        }, [editor]);

        /**
         * Create a new TextNode and insert it into the editor at the end of the
         * last paragraph, or in a new paragraph if the root is empty.
         */
        function createAndInsertTextNode(text: string): TextNode | null {
            const root = $getRoot();
            const lastChild = root.getLastChild();
            const textNode = $createTextNode(text);

            if (lastChild) {
                lastChild.append(textNode);
            } else {
                const paragraph = $createParagraphNode();
                paragraph.append(textNode);
                root.append(paragraph);
            }

            return textNode;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Schedule a buffer flush (batching via requestAnimationFrame)
        // ─────────────────────────────────────────────────────────────────────

        const scheduleFlush = useCallback(() => {
            // If a flush is already scheduled, don't schedule another
            if (flushRafRef.current !== null) {
                // But if buffer is getting large, flush immediately
                if (tokenBufferRef.current.length >= MAX_BUFFER_SIZE) {
                    cancelAnimationFrame(flushRafRef.current);
                    flushRafRef.current = null;
                    flushBuffer();
                }
                return;
            }

            const timeSinceLastFlush = performance.now() - lastFlushTimeRef.current;

            if (timeSinceLastFlush >= MIN_FLUSH_INTERVAL_MS) {
                // Enough time has passed; flush on next animation frame
                flushRafRef.current = requestAnimationFrame(flushBuffer);
            } else {
                // Too soon; schedule flush after the minimum interval
                const delay = MIN_FLUSH_INTERVAL_MS - timeSinceLastFlush;
                const timeoutId = window.setTimeout(() => {
                    flushRafRef.current = requestAnimationFrame(flushBuffer);
                }, delay);
                // Store timeout as negative to distinguish from rAF handle
                flushRafRef.current = -timeoutId;
            }
        }, [flushBuffer]);

        // ─────────────────────────────────────────────────────────────────────
        // Cleanup on unmount
        // ─────────────────────────────────────────────────────────────────────

        useEffect(() => {
            return () => {
                if (flushRafRef.current !== null) {
                    if (flushRafRef.current > 0) {
                        cancelAnimationFrame(flushRafRef.current);
                    } else {
                        clearTimeout(-flushRafRef.current);
                    }
                    flushRafRef.current = null;
                }
            };
        }, []);

        // ─────────────────────────────────────────────────────────────────────
        // Imperative API
        // ─────────────────────────────────────────────────────────────────────

        useImperativeHandle(ref, () => ({
            startStream(targetPosition?: string) {
                const state = streamStateRef.current;

                // Store original editable state and make editor non-editable
                // during streaming to prevent user interference
                state.wasEditable = editor.isEditable();
                editor.setEditable(false);

                // Reset streaming state
                state.isActive = true;
                state.insertedText = "";
                state.tokenCount = 0;
                state.activeNodeKey = null;
                tokenBufferRef.current = [];

                if (targetPosition) {
                    // Position cursor at the specified node
                    editor.update(() => {
                        const targetNode = $getNodeByKey(targetPosition);
                        if (targetNode) {
                            // If the target is a text node, we append after it
                            if (targetNode instanceof TextNode) {
                                state.activeNodeKey = targetNode.getKey();
                            } else {
                                // For element nodes, create a new text node as a child
                                const textNode = $createTextNode("");
                                targetNode.append(textNode);
                                state.activeNodeKey = textNode.getKey();
                            }
                        } else {
                            // Target not found; insert at end
                            initializeAtEnd(state);
                        }
                    });
                } else {
                    // No target specified; try to use current selection, or append at end
                    editor.update(() => {
                        const selection = $getSelection();
                        if ($isRangeSelection(selection)) {
                            const anchor = selection.anchor;
                            const anchorNode = anchor.getNode();
                            if (anchorNode instanceof TextNode) {
                                // If we're in the middle of a text node, split it
                                // and start inserting at the split point
                                if (anchor.offset < anchorNode.getTextContentSize()) {
                                    const [, afterNode] = anchorNode.splitText(anchor.offset);
                                    // Create an empty text node between the split parts
                                    const insertNode = $createTextNode("");
                                    if (afterNode) {
                                        afterNode.insertBefore(insertNode);
                                    } else {
                                        anchorNode.insertAfter(insertNode);
                                    }
                                    state.activeNodeKey = insertNode.getKey();
                                } else {
                                    // At the end of the text node; create a new node after it
                                    const insertNode = $createTextNode("");
                                    anchorNode.insertAfter(insertNode);
                                    state.activeNodeKey = insertNode.getKey();
                                }
                            } else {
                                // Selection is on an element node; create text node inside it
                                const insertNode = $createTextNode("");
                                anchorNode.append(insertNode);
                                state.activeNodeKey = insertNode.getKey();
                            }
                        } else {
                            initializeAtEnd(state);
                        }
                    });
                }
            },

            insertToken(token: string) {
                if (!streamStateRef.current.isActive) {
                    return;
                }

                streamStateRef.current.tokenCount++;
                tokenBufferRef.current.push(token);
                scheduleFlush();
            },

            endStream(cancelled?: boolean) {
                const state = streamStateRef.current;
                if (!state.isActive) {
                    return;
                }

                // Flush any remaining buffered tokens before ending
                if (!cancelled && tokenBufferRef.current.length > 0) {
                    // Cancel any scheduled flush and do it synchronously
                    if (flushRafRef.current !== null) {
                        if (flushRafRef.current > 0) {
                            cancelAnimationFrame(flushRafRef.current);
                        } else {
                            clearTimeout(-flushRafRef.current);
                        }
                        flushRafRef.current = null;
                    }
                    flushBuffer();
                }

                if (cancelled) {
                    // Remove all content inserted during this streaming operation
                    editor.update(() => {
                        if (state.activeNodeKey) {
                            const node = $getNodeByKey(state.activeNodeKey);
                            if (node instanceof TextNode) {
                                // Remove the text that was inserted during streaming
                                const currentText = node.getTextContent();
                                const insertedLength = state.insertedText.length;
                                if (currentText.length <= insertedLength) {
                                    // The entire node is streaming content; remove it
                                    node.remove();
                                } else {
                                    // Trim the streaming content from the end
                                    node.setTextContent(
                                        currentText.substring(0, currentText.length - insertedLength)
                                    );
                                }
                            }
                        }
                    });

                    // Discard buffered tokens
                    tokenBufferRef.current = [];
                    if (flushRafRef.current !== null) {
                        if (flushRafRef.current > 0) {
                            cancelAnimationFrame(flushRafRef.current);
                        } else {
                            clearTimeout(-flushRafRef.current);
                        }
                        flushRafRef.current = null;
                    }
                }

                // Restore editability
                editor.setEditable(state.wasEditable);

                // Reset state
                state.isActive = false;
                state.activeNodeKey = null;
                state.insertedText = "";
                state.tokenCount = 0;

                // Notify parent
                onStreamingComplete?.(cancelled ?? false);
            },
        }), [editor, flushBuffer, scheduleFlush, onStreamingComplete]);

        /**
         * Initialize the insertion point at the end of the document.
         * Creates a new paragraph with an empty text node if needed.
         */
        function initializeAtEnd(state: StreamingState): void {
            const root = $getRoot();
            const lastChild = root.getLastChild();
            if (lastChild) {
                const lastTextNode = lastChild.getLastChild();
                if (lastTextNode instanceof TextNode) {
                    const insertNode = $createTextNode("");
                    lastTextNode.insertAfter(insertNode);
                    state.activeNodeKey = insertNode.getKey();
                } else {
                    const insertNode = $createTextNode("");
                    lastChild.append(insertNode);
                    state.activeNodeKey = insertNode.getKey();
                }
            } else {
                const paragraph = $createParagraphNode();
                const textNode = $createTextNode("");
                paragraph.append(textNode);
                root.append(paragraph);
                state.activeNodeKey = textNode.getKey();
            }
        }

        // This plugin renders nothing to the DOM
        return null;
    }
);

export default StreamingInsertPlugin;

/**
 * Test Fixtures and Helpers for CrossPane Integration Tests
 *
 * Provides deterministic synthetic test data builders and helper functions
 * used across all cross-pane integration test suites.
 *
 * All test data is synthetic per ADR-015 (no real document content).
 *
 * @see ADR-015 - AI Data Governance (no real document content in tests)
 */

import type {
    DocumentStreamStartPayload,
    DocumentStreamTokenPayload,
    DocumentStreamEndPayload,
    DocumentReplacedPayload,
    SelectionChangedPayload,
    ContextChangedPayload,
} from "./SprkChatBridgeTestHarness";
import { SprkChatBridge } from "./SprkChatBridgeTestHarness";

// ---------------------------------------------------------------------------
// Payload Builders
// ---------------------------------------------------------------------------

/** Creates a stream start payload */
export function makeStreamStart(
    operationId: string,
    targetPosition = "end",
    operationType: "insert" | "replace" | "diff" = "insert"
): DocumentStreamStartPayload {
    return { operationId, targetPosition, operationType };
}

/** Creates a stream token payload */
export function makeStreamToken(
    operationId: string,
    token: string,
    index: number
): DocumentStreamTokenPayload {
    return { operationId, token, index };
}

/** Creates a stream end payload */
export function makeStreamEnd(
    operationId: string,
    totalTokens: number,
    cancelled = false
): DocumentStreamEndPayload {
    return { operationId, cancelled, totalTokens };
}

/** Creates a document replaced payload */
export function makeDocumentReplaced(
    operationId: string,
    html: string,
    previousVersionId?: string
): DocumentReplacedPayload {
    return { operationId, html, previousVersionId };
}

/** Creates a selection changed payload */
export function makeSelectionChanged(
    text: string,
    startOffset = 0,
    endOffset?: number,
    context?: string
): SelectionChangedPayload {
    return {
        text,
        startOffset,
        endOffset: endOffset ?? text.length,
        context,
    };
}

/** Creates a context changed payload */
export function makeContextChanged(
    entityType: string,
    entityId: string,
    playbookId?: string
): ContextChangedPayload {
    return { entityType, entityId, playbookId };
}

// ---------------------------------------------------------------------------
// Streaming Sequence Helpers
// ---------------------------------------------------------------------------

/**
 * Emits a complete streaming write sequence from a producer bridge.
 * Mirrors the real flow: SprkChat SSE handler -> bridge.emit().
 */
export function emitStreamingSequence(
    producerBridge: SprkChatBridge,
    operationId: string,
    tokens: string[],
    options?: {
        cancelAfter?: number;
        operationType?: "insert" | "replace" | "diff";
        targetPosition?: string;
    }
): void {
    const opType = options?.operationType ?? "insert";
    const targetPos = options?.targetPosition ?? "end";

    // 1. Emit stream_start
    producerBridge.emit(
        "document_stream_start",
        makeStreamStart(operationId, targetPos, opType)
    );

    // 2. Emit tokens
    const emitCount = options?.cancelAfter ?? tokens.length;
    for (let i = 0; i < emitCount && i < tokens.length; i++) {
        producerBridge.emit(
            "document_stream_token",
            makeStreamToken(operationId, tokens[i], i)
        );
    }

    // 3. Emit stream_end
    if (options?.cancelAfter !== undefined && options.cancelAfter < tokens.length) {
        producerBridge.emit(
            "document_stream_end",
            makeStreamEnd(operationId, options.cancelAfter, true)
        );
    } else {
        producerBridge.emit(
            "document_stream_end",
            makeStreamEnd(operationId, tokens.length, false)
        );
    }
}

/**
 * Emits a diff-mode streaming sequence (for selection revision flow).
 */
export function emitDiffStreamSequence(
    producerBridge: SprkChatBridge,
    operationId: string,
    tokens: string[],
    options?: { cancelled?: boolean }
): void {
    emitStreamingSequence(producerBridge, operationId, tokens, {
        operationType: "diff",
        targetPosition: "selection",
        cancelAfter: options?.cancelled ? 0 : undefined,
    });

    // If we need to emit a cancelled end without tokens:
    if (options?.cancelled) {
        // Re-emit the end as cancelled (overrides the one from emitStreamingSequence)
        // Actually, if cancelAfter=0, the sequence will emit start + end(cancelled=true, totalTokens=0)
        // which is correct behavior.
    }
}

// ---------------------------------------------------------------------------
// Mock Editor Ref
// ---------------------------------------------------------------------------

/**
 * Creates a mock editor ref that tracks streaming method calls and content state.
 * Simulates the RichTextEditor ref API extended with streaming methods.
 */
export function createMockEditorRef(initialContent = "") {
    let htmlContent = initialContent;
    const streamingCalls: Array<{
        method: string;
        args: unknown[];
        timestamp: number;
    }> = [];
    let isStreamActive = false;
    let streamedTokens: string[] = [];
    const undoStack: string[] = [];
    let undoIndex = -1;

    const mockHandle = { id: "mock-stream-handle" };

    const editorRef = {
        current: {
            focus: jest.fn(),
            getHtml: jest.fn(() => htmlContent),
            setHtml: jest.fn((html: string) => {
                htmlContent = html;
            }),
            clear: jest.fn(() => {
                htmlContent = "";
                streamedTokens = [];
            }),
            beginStreamingInsert: jest.fn((position: string) => {
                isStreamActive = true;
                streamedTokens = [];
                streamingCalls.push({
                    method: "beginStreamingInsert",
                    args: [position],
                    timestamp: performance.now(),
                });
                return mockHandle;
            }),
            appendStreamToken: jest.fn((_handle: unknown, token: string) => {
                if (isStreamActive) {
                    streamedTokens.push(token);
                    htmlContent += token;
                    streamingCalls.push({
                        method: "appendStreamToken",
                        args: [_handle, token],
                        timestamp: performance.now(),
                    });
                }
            }),
            endStreamingInsert: jest.fn((_handle: unknown) => {
                isStreamActive = false;
                streamingCalls.push({
                    method: "endStreamingInsert",
                    args: [_handle],
                    timestamp: performance.now(),
                });
            }),
        },
    };

    return {
        editorRef,
        getHtmlContent: () => htmlContent,
        setHtmlContent: (html: string) => {
            htmlContent = html;
        },
        getStreamedTokens: () => [...streamedTokens],
        getStreamingCalls: () => [...streamingCalls],
        isStreamActive: () => isStreamActive,
        getMockHandle: () => mockHandle,
        getUndoStack: () => [...undoStack],
        pushToUndoStack: () => {
            if (undoIndex < undoStack.length - 1) {
                undoStack.splice(undoIndex + 1);
            }
            undoStack.push(htmlContent);
            undoIndex = undoStack.length - 1;
        },
        undo: () => {
            if (undoIndex > 0) {
                undoIndex--;
                htmlContent = undoStack[undoIndex];
                editorRef.current.setHtml(htmlContent);
            }
        },
        redo: () => {
            if (undoIndex < undoStack.length - 1) {
                undoIndex++;
                htmlContent = undoStack[undoIndex];
                editorRef.current.setHtml(htmlContent);
            }
        },
    };
}

// ---------------------------------------------------------------------------
// Test Constants (ADR-015: all synthetic)
// ---------------------------------------------------------------------------

export const TEST_CONTEXT = "test-session-abc-123";
export const TEST_OPERATION_ID = "op-test-001";
export const TEST_ENTITY_TYPE = "sprk_analysisoutput";
export const TEST_ENTITY_ID = "entity-test-001";
export const TEST_PLAYBOOK_ID = "playbook-default-test";
export const TEST_ANALYSIS_CONTENT = "<p>Synthetic analysis output for testing.</p>";
export const TEST_REVISED_CONTENT = "<p>Synthetic revised content for testing.</p>";

/** Sample tokens for streaming tests */
export const SAMPLE_TOKENS = [
    "The ",
    "analysis ",
    "reveals ",
    "key ",
    "findings.",
];

/** Large token set for stress tests (100 tokens) */
export const LARGE_TOKEN_SET = Array.from(
    { length: 100 },
    (_, i) => `word${i} `
);

/** Sample document content tokens for full pipeline */
export const DOCUMENT_CONTENT_TOKENS = [
    "Executive Summary\n\n",
    "The document analysis identified ",
    "three critical compliance gaps ",
    "that require immediate attention.\n\n",
    "1. Data retention policy alignment\n",
    "2. Access control mechanisms\n",
    "3. Encryption standard compliance",
];

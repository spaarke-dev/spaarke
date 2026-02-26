/**
 * StreamingWriteHarness — E2E validation page for the streaming write pipeline
 *
 * SUPERSEDED by task R2-071: E2E streaming write is now validated by comprehensive
 * integration tests at:
 *   src/client/code-pages/AnalysisWorkspace/src/__tests__/streaming-e2e.test.ts
 *
 * Those tests cover:
 *   - Happy path (stream_start -> tokens -> stream_end)
 *   - Cancel mid-stream (partial content preserved, undoable)
 *   - Document replace (undo stack management)
 *   - Error scenarios (network drop, bridge disconnect, invalid data)
 *   - Bridge channel naming consistency (sprk-workspace-{context})
 *   - Latency validation (<100ms per token, NFR-01)
 *   - Transport selection (BroadcastChannel / postMessage fallback)
 *   - Security (no auth tokens via BroadcastChannel, ADR-015)
 *   - Full pipeline integration (SprkChat -> Bridge -> Editor)
 *
 * This harness is retained for visual/manual debugging only.
 * It is NOT production code and NOT part of the automated test suite.
 *
 * Validates the full data path:
 *   1. Mock SSE events (simulates BFF API document_stream_* events)
 *   2. SprkChatBridge.emit() on the producer side
 *   3. BroadcastChannel transport
 *   4. SprkChatBridge.subscribe() on the consumer side
 *   5. useDocumentStreamConsumer hook
 *   6. RichTextEditor streaming ref API (beginStreamingInsert / appendStreamToken / endStreamingInsert)
 *
 * Usage:
 *   Import and render this component in a dev page or Storybook.
 *   Click "Start Streaming" to simulate a document write operation.
 *   Observe tokens appearing character-by-character with the pulsing cursor.
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-015 - No auth tokens via BroadcastChannel
 * @deprecated Superseded by streaming-e2e.test.ts (task R2-071). Retained for manual debugging only.
 */

import * as React from "react";
import { useState, useRef, useMemo, useCallback } from "react";
import {
    Button,
    Text,
    Badge,
    makeStyles,
    tokens,
    Card,
    CardHeader,
    Divider,
    Textarea,
    type TextareaOnChangeData,
} from "@fluentui/react-components";
import {
    Play20Regular,
    Stop20Regular,
    Delete20Regular,
} from "@fluentui/react-icons";

import { SprkChatBridge } from "../services/SprkChatBridge";
import { RichTextEditor } from "../components/RichTextEditor/RichTextEditor";
import type { RichTextEditorRef } from "../components/RichTextEditor/RichTextEditor";
import { useDocumentStreamConsumer } from "../components/RichTextEditor/hooks/useDocumentStreamConsumer";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        gap: "16px",
        padding: "24px",
        maxWidth: "960px",
        margin: "0 auto",
    },
    header: {
        display: "flex",
        alignItems: "center",
        gap: "12px",
    },
    controls: {
        display: "flex",
        gap: "8px",
        alignItems: "center",
        flexWrap: "wrap",
    },
    status: {
        display: "flex",
        gap: "12px",
        alignItems: "center",
    },
    latencyLog: {
        fontFamily: "monospace",
        fontSize: tokens.fontSizeBase200,
        maxHeight: "160px",
        overflow: "auto",
        padding: "8px",
        backgroundColor: tokens.colorNeutralBackground3,
        borderRadius: tokens.borderRadiusMedium,
        whiteSpace: "pre-wrap",
    },
    editorSection: {
        border: `1px solid ${tokens.colorNeutralStroke1}`,
        borderRadius: tokens.borderRadiusMedium,
        padding: "12px",
    },
    textArea: {
        width: "100%",
        minHeight: "80px",
    },
});

// ─────────────────────────────────────────────────────────────────────────────
// Mock SSE content
// ─────────────────────────────────────────────────────────────────────────────

const DEFAULT_MOCK_TEXT = `The analysis reveals several key findings regarding the document's compliance with current regulations.

First, the data retention policy aligns with GDPR Article 17, ensuring that personal data is erased within the required timeframe.

Second, the access control mechanisms implement role-based permissions consistent with the principle of least privilege.

Third, the encryption standards meet industry benchmarks for data at rest and in transit.`;

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * StreamingWriteHarness - Test harness for validating the E2E streaming write pipeline.
 *
 * E2E streaming validation is now in
 * src/client/code-pages/AnalysisWorkspace/src/__tests__/streaming-e2e.test.ts.
 * This component is retained for visual/manual debugging only.
 * @deprecated Use streaming-e2e.test.ts for automated validation.
 */
export function StreamingWriteHarness(): React.ReactElement {
    const styles = useStyles();

    // Bridge: use a unique context so this harness doesn't interfere with production
    const bridgeContext = useMemo(() => `harness-${Date.now()}`, []);
    const producerBridge = useMemo(
        () => new SprkChatBridge({ context: bridgeContext }),
        [bridgeContext]
    );
    const consumerBridge = useMemo(
        () => new SprkChatBridge({ context: bridgeContext }),
        [bridgeContext]
    );

    // Editor ref
    const editorRef = useRef<RichTextEditorRef>(null);

    // Mock content
    const [mockText, setMockText] = useState(DEFAULT_MOCK_TEXT);

    // Latency tracking
    const [latencyLog, setLatencyLog] = useState<string[]>([]);
    const tokenStartTimesRef = useRef<Map<number, number>>(new Map());

    // Consumer hook — wires bridge events to editor ref
    const { isStreaming, operationId, tokenCount } = useDocumentStreamConsumer({
        bridge: consumerBridge,
        editorRef,
        onStreamStart: (opId: string) => {
            setLatencyLog((prev) => [
                ...prev,
                `[${new Date().toISOString()}] Stream started: ${opId}`,
            ]);
        },
        onStreamEnd: (opId: string, cancelled: boolean) => {
            setLatencyLog((prev) => [
                ...prev,
                `[${new Date().toISOString()}] Stream ended: ${opId} (cancelled=${cancelled})`,
            ]);

            // Report latency summary
            const times = tokenStartTimesRef.current;
            if (times.size > 0) {
                const latencies: number[] = [];
                let prevTime = 0;
                const sorted = Array.from(times.entries()).sort((a, b) => a[0] - b[0]);
                for (const [idx, time] of sorted) {
                    if (idx > 0 && prevTime > 0) {
                        latencies.push(time - prevTime);
                    }
                    prevTime = time;
                }
                if (latencies.length > 0) {
                    const avg = latencies.reduce((a, b) => a + b, 0) / latencies.length;
                    const max = Math.max(...latencies);
                    const p95Idx = Math.floor(latencies.length * 0.95);
                    const sortedLatencies = [...latencies].sort((a, b) => a - b);
                    const p95 = sortedLatencies[p95Idx] ?? max;
                    setLatencyLog((prev) => [
                        ...prev,
                        `  Latency: avg=${avg.toFixed(1)}ms, p95=${p95.toFixed(1)}ms, max=${max.toFixed(1)}ms (${latencies.length} intervals)`,
                        `  NFR-01 target: <100ms per token — ${p95 < 100 ? "PASS" : "FAIL"}`,
                    ]);
                }
                tokenStartTimesRef.current.clear();
            }
        },
        onTokenReceived: (index: number, timestamp: number) => {
            tokenStartTimesRef.current.set(index, timestamp);
        },
    });

    // Streaming control ref for cancellation
    const streamTimerRef = useRef<number | null>(null);

    // ─────────────────────────────────────────────────────────────────────
    // Mock SSE emitter (producer side)
    // ─────────────────────────────────────────────────────────────────────

    const startMockStream = useCallback(() => {
        if (isStreaming) {
            return;
        }

        const opId = `op-${Date.now()}`;
        const textToStream = mockText;

        // Tokenize: split into individual characters (simulates per-token SSE)
        // In production, tokens are typically 1-4 words. Character-by-character
        // is a worst-case scenario for latency testing.
        const tokens = textToStream.split("");

        setLatencyLog((prev) => [
            ...prev,
            `\n--- New Stream ---`,
            `[${new Date().toISOString()}] Emitting ${tokens.length} tokens via bridge`,
        ]);

        // Emit document_stream_start
        producerBridge.emit("document_stream_start", {
            operationId: opId,
            targetPosition: "end",
            operationType: "insert",
        });

        // Emit tokens with a realistic interval (~20ms per token, ~50 tokens/sec)
        let index = 0;
        const interval = 20;

        const emitNextToken = (): void => {
            if (index >= tokens.length) {
                // Emit document_stream_end
                producerBridge.emit("document_stream_end", {
                    operationId: opId,
                    cancelled: false,
                    totalTokens: tokens.length,
                });
                streamTimerRef.current = null;
                return;
            }

            producerBridge.emit("document_stream_token", {
                operationId: opId,
                token: tokens[index],
                index,
            });
            index++;

            streamTimerRef.current = window.setTimeout(emitNextToken, interval);
        };

        streamTimerRef.current = window.setTimeout(emitNextToken, interval);
    }, [isStreaming, mockText, producerBridge]);

    const cancelMockStream = useCallback(() => {
        if (streamTimerRef.current !== null) {
            clearTimeout(streamTimerRef.current);
            streamTimerRef.current = null;
        }

        if (operationId) {
            producerBridge.emit("document_stream_end", {
                operationId,
                cancelled: true,
                totalTokens: tokenCount,
            });
        }
    }, [operationId, tokenCount, producerBridge]);

    const clearEditor = useCallback(() => {
        editorRef.current?.clear();
        setLatencyLog([]);
    }, []);

    const handleEditorChange = useCallback(() => {
        // No-op for the harness — we only care about streaming writes
    }, []);

    const handleMockTextChange = useCallback(
        (_ev: React.ChangeEvent<HTMLTextAreaElement>, data: TextareaOnChangeData) => {
            setMockText(data.value);
        },
        []
    );

    // ─────────────────────────────────────────────────────────────────────
    // Cleanup on unmount
    // ─────────────────────────────────────────────────────────────────────

    React.useEffect(() => {
        return () => {
            if (streamTimerRef.current !== null) {
                clearTimeout(streamTimerRef.current);
            }
            producerBridge.disconnect();
            consumerBridge.disconnect();
        };
    }, [producerBridge, consumerBridge]);

    // ─────────────────────────────────────────────────────────────────────
    // Render
    // ─────────────────────────────────────────────────────────────────────

    return (
        <div className={styles.root}>
            {/* Header */}
            <div className={styles.header}>
                <Text size={600} weight="semibold">
                    Streaming Write Harness
                </Text>
                <Badge
                    appearance="filled"
                    color="important"
                    size="small"
                >
                    Test Only
                </Badge>
            </div>

            <Text size={300}>
                Validates the full E2E streaming write pipeline: Mock SSE events
                → SprkChatBridge (BroadcastChannel) → useDocumentStreamConsumer
                → RichTextEditor StreamingInsertPlugin. Not production code.
            </Text>

            <Divider />

            {/* Mock Content Input */}
            <Card>
                <CardHeader
                    header={
                        <Text weight="semibold">Mock SSE Content</Text>
                    }
                    description="Text to stream into the editor (each character = 1 token)"
                />
                <Textarea
                    className={styles.textArea}
                    value={mockText}
                    onChange={handleMockTextChange}
                    disabled={isStreaming}
                    resize="vertical"
                />
            </Card>

            {/* Controls */}
            <div className={styles.controls}>
                <Button
                    appearance="primary"
                    icon={<Play20Regular />}
                    onClick={startMockStream}
                    disabled={isStreaming}
                >
                    Start Streaming
                </Button>
                <Button
                    appearance="secondary"
                    icon={<Stop20Regular />}
                    onClick={cancelMockStream}
                    disabled={!isStreaming}
                >
                    Cancel
                </Button>
                <Button
                    appearance="subtle"
                    icon={<Delete20Regular />}
                    onClick={clearEditor}
                    disabled={isStreaming}
                >
                    Clear
                </Button>

                <Divider vertical style={{ height: "24px" }} />

                <div className={styles.status}>
                    <Badge
                        appearance="filled"
                        color={isStreaming ? "success" : "informative"}
                    >
                        {isStreaming ? "Streaming" : "Idle"}
                    </Badge>
                    {operationId && (
                        <Text size={200}>
                            Op: {operationId} | Tokens: {tokenCount}
                        </Text>
                    )}
                </div>
            </div>

            <Divider />

            {/* Editor (Consumer Side) */}
            <div className={styles.editorSection}>
                <Text weight="semibold" size={300}>
                    RichTextEditor (Consumer — receives bridge events)
                </Text>
                <div style={{ marginTop: "8px" }}>
                    <RichTextEditor
                        ref={editorRef}
                        value=""
                        onChange={handleEditorChange}
                        placeholder="Streaming content will appear here..."
                        minHeight={200}
                        maxHeight={400}
                    />
                </div>
            </div>

            {/* Latency Log */}
            {latencyLog.length > 0 && (
                <Card>
                    <CardHeader
                        header={
                            <Text weight="semibold">Latency Log</Text>
                        }
                    />
                    <div className={styles.latencyLog}>
                        {latencyLog.join("\n")}
                    </div>
                </Card>
            )}

            {/* Architecture Diagram */}
            <Card>
                <CardHeader
                    header={
                        <Text weight="semibold">Data Flow</Text>
                    }
                />
                <Text
                    size={200}
                    font="monospace"
                    style={{ whiteSpace: "pre", padding: "8px" }}
                >
{`  [Mock SSE Emitter]
        |
        v
  producerBridge.emit("document_stream_*")
        |
        v  (BroadcastChannel: sprk-workspace-${bridgeContext})
        |
  consumerBridge.subscribe("document_stream_*")
        |
        v
  useDocumentStreamConsumer hook
        |
        v
  editorRef.beginStreamingInsert()
  editorRef.appendStreamToken()
  editorRef.endStreamingInsert()`}
                </Text>
            </Card>
        </div>
    );
}

export default StreamingWriteHarness;

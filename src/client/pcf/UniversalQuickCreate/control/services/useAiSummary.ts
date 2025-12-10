/**
 * AI Summary Orchestration Hook (useAiSummary)
 *
 * Manages AI document analysis for multiple documents with concurrent streaming,
 * status tracking, and batch enqueue on close. Updated for the Document Intelligence
 * API which returns structured analysis results (summary, TL;DR, keywords, entities).
 *
 * @version 2.0.0.0
 */

import { useState, useCallback, useRef, useEffect } from 'react';
import { SseStreamStatus } from './useSseStream';

/**
 * Extracted entities from document analysis
 */
export interface ExtractedEntities {
    /** Organizations mentioned in the document */
    organizations: string[];
    /** People mentioned in the document */
    people: string[];
    /** Monetary amounts or quantities */
    amounts: string[];
    /** Dates or time periods */
    dates: string[];
    /** Document type classification */
    documentType: string;
    /** Reference numbers (invoice, PO, case numbers, etc.) */
    references: string[];
}

/**
 * Complete result of AI document analysis
 */
export interface DocumentAnalysisResult {
    /** Multi-sentence summary */
    summary: string;
    /** TL;DR bullet points (1-3 items) */
    tldr: string[];
    /** Comma-separated keywords */
    keywords: string;
    /** Extracted named entities */
    entities: ExtractedEntities;
    /** Raw AI response (for debugging) */
    rawResponse?: string;
    /** Whether parsing was successful */
    parsedSuccessfully: boolean;
}

/**
 * Summary status types
 */
export type SummaryStatus = 'pending' | 'streaming' | 'complete' | 'error' | 'skipped' | 'not-supported';

/**
 * Document summary state for tracking and display
 */
export interface DocumentSummaryState {
    /** Document identifier */
    documentId: string;
    /** File name */
    fileName: string;
    /** Summary text (may be partial during streaming) */
    summary?: string;
    /** Current status */
    status: SummaryStatus;
    /** Error message (when status is 'error') */
    error?: string;
    /** TL;DR bullet points (available after completion) */
    tldr?: string[];
    /** Keywords (available after completion) */
    keywords?: string;
    /** Extracted entities (available after completion) */
    entities?: ExtractedEntities;
    /** Document type classification (available after completion) */
    documentType?: string;
    /** Whether structured parsing was successful */
    parsedSuccessfully?: boolean;
}

/**
 * Document to be summarized
 */
export interface SummaryDocument {
    /** Unique document ID (Dataverse GUID) */
    documentId: string;

    /** SharePoint Embedded drive ID */
    driveId: string;

    /** SharePoint Embedded item ID */
    itemId: string;

    /** File name for display */
    fileName: string;
}

/**
 * Hook configuration options
 */
export interface UseAiSummaryOptions {
    /** Base URL for API endpoints */
    apiBaseUrl: string;

    /** Function to get authorization token (for dynamic token acquisition) */
    getToken?: () => Promise<string>;

    /** Maximum concurrent streams (default: 3) */
    maxConcurrent?: number;

    /** Auto-start streaming when documents added */
    autoStart?: boolean;

    /** Callback when analysis completes successfully */
    onAnalysisComplete?: (documentId: string, result: DocumentAnalysisResult) => void;

    /** @deprecated Use onAnalysisComplete instead */
    onSummaryComplete?: (documentId: string, summary: string) => void;
}

/**
 * Hook return type
 */
export interface UseAiSummaryResult {
    /** Document summary states for carousel display */
    documents: DocumentSummaryState[];

    /** Whether any summaries are in progress */
    isProcessing: boolean;

    /** Count of completed summaries */
    completedCount: number;

    /** Count of documents with errors */
    errorCount: number;

    /** Add documents to be summarized */
    addDocuments: (docs: SummaryDocument[]) => void;

    /** Start streaming for all pending documents */
    startAll: () => void;

    /** Retry a specific document */
    retry: (documentId: string) => void;

    /** Enqueue incomplete summaries for background processing */
    enqueueIncomplete: () => Promise<void>;

    /** Clear all documents */
    clear: () => void;

    /** Check if there are incomplete summaries */
    hasIncomplete: boolean;
}

/**
 * Internal state for tracking active streams
 */
interface StreamState {
    abortController: AbortController;
    documentId: string;
}

/**
 * SSE chunk from Document Intelligence API
 */
interface SseChunk {
    /** Event type: "text" | "complete" | "error" */
    type?: string;
    /** Content for streaming text chunks */
    content?: string;
    /** Whether this is the final chunk */
    done?: boolean;
    /** Summary text (legacy, for backward compatibility) */
    summary?: string;
    /** Structured analysis result (for type="complete") */
    result?: DocumentAnalysisResult;
    /** Error message (for type="error") */
    error?: string;
}

/**
 * Parse SSE line to extract data
 */
const parseSseLine = (line: string): SseChunk | null => {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith(':')) return null;

    if (trimmed.startsWith('data:')) {
        const jsonStr = trimmed.slice(5).trim();
        if (!jsonStr || jsonStr === '[DONE]') {
            return { done: true };
        }
        try {
            return JSON.parse(jsonStr) as SseChunk;
        } catch {
            return { content: jsonStr };
        }
    }
    return null;
};

/**
 * useAiSummary Hook
 *
 * Orchestrates AI document analysis for multiple documents with
 * concurrent streaming and status management.
 */
export const useAiSummary = (options: UseAiSummaryOptions): UseAiSummaryResult => {
    const {
        apiBaseUrl,
        getToken,
        maxConcurrent = 3,
        autoStart = true,
        onAnalysisComplete,
        onSummaryComplete
    } = options;

    const [documents, setDocuments] = useState<DocumentSummaryState[]>([]);
    const activeStreamsRef = useRef<Map<string, StreamState>>(new Map());
    const pendingQueueRef = useRef<SummaryDocument[]>([]);
    const documentMapRef = useRef<Map<string, SummaryDocument>>(new Map());

    /**
     * Update a single document's state
     */
    const updateDocument = useCallback((
        documentId: string,
        updates: Partial<DocumentSummaryState>
    ) => {
        setDocuments(prev => prev.map(doc =>
            doc.documentId === documentId
                ? { ...doc, ...updates }
                : doc
        ));
    }, []);

    /**
     * Stream document analysis for a single document
     */
    const streamDocument = useCallback(async (doc: SummaryDocument) => {
        const { documentId, driveId, itemId } = doc;

        console.log('[useAiSummary] Starting stream for document:', { documentId, driveId, itemId, apiBaseUrl });

        // Check if already streaming
        if (activeStreamsRef.current.has(documentId)) {
            console.log('[useAiSummary] Already streaming, skipping:', documentId);
            return;
        }

        // Create abort controller
        const abortController = new AbortController();
        activeStreamsRef.current.set(documentId, { abortController, documentId });

        // Update status to streaming
        updateDocument(documentId, { status: 'streaming', summary: '', error: undefined });

        let accumulatedSummary = '';
        // Updated endpoint: /api/ai/document-intelligence/analyze
        const streamUrl = `${apiBaseUrl}/ai/document-intelligence/analyze`;
        console.log('[useAiSummary] Fetching:', streamUrl);

        try {
            // Acquire token dynamically before each request
            let token: string | undefined;
            if (getToken) {
                try {
                    token = await getToken();
                    console.log('[useAiSummary] Token acquired successfully');
                } catch (tokenError) {
                    console.error('[useAiSummary] Failed to acquire token:', tokenError);
                    throw new Error('Failed to acquire authentication token');
                }
            }

            const response = await fetch(streamUrl, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'text/event-stream',
                    ...(token ? { 'Authorization': `Bearer ${token}` } : {})
                },
                body: JSON.stringify({ documentId, driveId, itemId }),
                signal: abortController.signal
            });

            if (!response.ok) {
                const errorText = await response.text();
                throw new Error(errorText || `HTTP ${response.status}`);
            }

            if (!response.body) {
                throw new Error('Response body not readable');
            }

            const reader = response.body.getReader();
            const decoder = new TextDecoder();
            let buffer = '';

            while (true) {
                const { done, value } = await reader.read();

                if (done) {
                    // Stream ended without complete event - mark as complete with accumulated data
                    updateDocument(documentId, { status: 'complete' });
                    if (onSummaryComplete && accumulatedSummary) {
                        onSummaryComplete(documentId, accumulatedSummary);
                    }
                    break;
                }

                buffer += decoder.decode(value, { stream: true });
                const events = buffer.split('\n\n');
                buffer = events.pop() || '';

                for (const event of events) {
                    for (const line of event.split('\n')) {
                        const chunk = parseSseLine(line);
                        if (chunk) {
                            // Handle error event
                            if (chunk.type === 'error' || chunk.error) {
                                throw new Error(chunk.error || 'Unknown error');
                            }

                            // Handle complete event with structured result
                            if (chunk.type === 'complete' || chunk.done) {
                                const result = chunk.result;
                                const updates: Partial<DocumentSummaryState> = {
                                    status: 'complete',
                                    summary: result?.summary || chunk.summary || accumulatedSummary
                                };

                                // Add structured data if available
                                if (result) {
                                    updates.tldr = result.tldr;
                                    updates.keywords = result.keywords;
                                    updates.entities = result.entities;
                                    updates.documentType = result.entities?.documentType;
                                    updates.parsedSuccessfully = result.parsedSuccessfully;

                                    // Call new analysis complete callback
                                    if (onAnalysisComplete) {
                                        onAnalysisComplete(documentId, result);
                                    }
                                }

                                // Legacy callback for backward compatibility
                                if (onSummaryComplete && updates.summary) {
                                    onSummaryComplete(documentId, updates.summary);
                                }

                                updateDocument(documentId, updates);
                                activeStreamsRef.current.delete(documentId);
                                return;
                            }

                            // Handle streaming text content
                            if (chunk.type === 'text' || chunk.content) {
                                accumulatedSummary += chunk.content || '';
                                updateDocument(documentId, { summary: accumulatedSummary });
                            }
                        }
                    }
                }
            }
        } catch (err) {
            if (err instanceof Error && err.name === 'AbortError') {
                // Aborted - don't update to error
                return;
            }

            updateDocument(documentId, {
                status: 'error',
                error: err instanceof Error ? err.message : 'Unknown error'
            });
        } finally {
            activeStreamsRef.current.delete(documentId);
            // Process next in queue
            processQueue();
        }
    }, [apiBaseUrl, getToken, updateDocument, onAnalysisComplete, onSummaryComplete]);

    /**
     * Process pending queue respecting concurrent limit
     */
    const processQueue = useCallback(() => {
        const activeCount = activeStreamsRef.current.size;
        const availableSlots = maxConcurrent - activeCount;

        if (availableSlots <= 0 || pendingQueueRef.current.length === 0) {
            return;
        }

        const toProcess = pendingQueueRef.current.splice(0, availableSlots);
        toProcess.forEach(doc => streamDocument(doc));
    }, [maxConcurrent, streamDocument]);

    /**
     * Add documents to be summarized
     */
    const addDocuments = useCallback((docs: SummaryDocument[]) => {
        console.log('[useAiSummary] addDocuments called with:', docs.length, 'documents', docs);

        // Add to document map
        docs.forEach(doc => documentMapRef.current.set(doc.documentId, doc));

        // Create initial states
        const newStates: DocumentSummaryState[] = docs.map(doc => ({
            documentId: doc.documentId,
            fileName: doc.fileName,
            status: 'pending' as SummaryStatus,
            summary: undefined,
            error: undefined,
            tldr: undefined,
            keywords: undefined,
            entities: undefined,
            documentType: undefined,
            parsedSuccessfully: undefined
        }));

        setDocuments(prev => [...prev, ...newStates]);

        // Add to pending queue
        pendingQueueRef.current.push(...docs);

        // Auto-start if enabled
        if (autoStart) {
            // Use setTimeout to ensure state is updated
            setTimeout(() => processQueue(), 0);
        }
    }, [autoStart, processQueue]);

    /**
     * Start streaming for all pending documents
     */
    const startAll = useCallback(() => {
        processQueue();
    }, [processQueue]);

    /**
     * Retry a specific document
     */
    const retry = useCallback((documentId: string) => {
        const doc = documentMapRef.current.get(documentId);
        if (!doc) return;

        // Reset status to pending and clear all fields
        updateDocument(documentId, {
            status: 'pending',
            summary: undefined,
            error: undefined,
            tldr: undefined,
            keywords: undefined,
            entities: undefined,
            documentType: undefined,
            parsedSuccessfully: undefined
        });

        // Add back to queue
        pendingQueueRef.current.push(doc);
        processQueue();
    }, [updateDocument, processQueue]);

    /**
     * Enqueue incomplete summaries for background processing
     */
    const enqueueIncomplete = useCallback(async () => {
        const incompleteIds = documents
            .filter(d => d.status !== 'complete' && d.status !== 'skipped' && d.status !== 'not-supported')
            .map(d => d.documentId);

        if (incompleteIds.length === 0) return;

        // Get document details
        const incompleteDocuments = incompleteIds
            .map(id => documentMapRef.current.get(id))
            .filter((doc): doc is SummaryDocument => doc !== undefined);

        if (incompleteDocuments.length === 0) return;

        try {
            // Acquire token dynamically
            let token: string | undefined;
            if (getToken) {
                try {
                    token = await getToken();
                } catch (tokenError) {
                    console.error('[useAiSummary] Failed to acquire token for enqueue:', tokenError);
                }
            }

            // Updated endpoint: /api/ai/document-intelligence/enqueue-batch
            const response = await fetch(`${apiBaseUrl}/ai/document-intelligence/enqueue-batch`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(token ? { 'Authorization': `Bearer ${token}` } : {})
                },
                body: JSON.stringify({
                    documents: incompleteDocuments.map(doc => ({
                        documentId: doc.documentId,
                        driveId: doc.driveId,
                        itemId: doc.itemId
                    }))
                })
            });

            if (!response.ok) {
                console.error('Failed to enqueue incomplete summaries');
            }
        } catch (error) {
            console.error('Error enqueueing incomplete summaries:', error);
        }
    }, [documents, apiBaseUrl, getToken]);

    /**
     * Clear all documents
     */
    const clear = useCallback(() => {
        // Abort all active streams
        activeStreamsRef.current.forEach(({ abortController }) => {
            abortController.abort();
        });
        activeStreamsRef.current.clear();

        // Clear queues
        pendingQueueRef.current = [];
        documentMapRef.current.clear();

        // Clear state
        setDocuments([]);
    }, []);

    // Cleanup on unmount
    useEffect(() => {
        return () => {
            activeStreamsRef.current.forEach(({ abortController }) => {
                abortController.abort();
            });
        };
    }, []);

    // Computed values
    const isProcessing = documents.some(d => d.status === 'streaming' || d.status === 'pending');
    const completedCount = documents.filter(d => d.status === 'complete').length;
    const errorCount = documents.filter(d => d.status === 'error').length;
    const hasIncomplete = documents.some(
        d => d.status !== 'complete' && d.status !== 'skipped' && d.status !== 'not-supported'
    );

    return {
        documents,
        isProcessing,
        completedCount,
        errorCount,
        addDocuments,
        startAll,
        retry,
        enqueueIncomplete,
        clear,
        hasIncomplete
    };
};

export default useAiSummary;

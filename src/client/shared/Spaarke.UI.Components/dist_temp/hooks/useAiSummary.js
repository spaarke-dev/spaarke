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
/**
 * Parse SSE line to extract data
 */
const parseSseLine = (line) => {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith(':'))
        return null;
    if (trimmed.startsWith('data:')) {
        const jsonStr = trimmed.slice(5).trim();
        if (!jsonStr || jsonStr === '[DONE]') {
            return { done: true };
        }
        try {
            return JSON.parse(jsonStr);
        }
        catch {
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
export const useAiSummary = (options) => {
    const { apiBaseUrl, getToken, maxConcurrent = 3, autoStart = true, onAnalysisComplete, onSummaryComplete } = options;
    const [documents, setDocuments] = useState([]);
    const activeStreamsRef = useRef(new Map());
    const pendingQueueRef = useRef([]);
    const documentMapRef = useRef(new Map());
    /**
     * Update a single document's state
     */
    const updateDocument = useCallback((documentId, updates) => {
        setDocuments(prev => prev.map(doc => (doc.documentId === documentId ? { ...doc, ...updates } : doc)));
    }, []);
    /**
     * Stream document analysis for a single document
     */
    const streamDocument = useCallback(async (doc) => {
        const { documentId, driveId, itemId } = doc;
        console.log('[useAiSummary] Starting stream for document:', {
            documentId,
            driveId,
            itemId,
            apiBaseUrl,
        });
        // Check if already streaming
        if (activeStreamsRef.current.has(documentId)) {
            console.log('[useAiSummary] Already streaming, skipping:', documentId);
            return;
        }
        // Create abort controller
        const abortController = new AbortController();
        activeStreamsRef.current.set(documentId, { abortController, documentId });
        // Update status to streaming
        updateDocument(documentId, {
            status: 'streaming',
            summary: '',
            error: undefined,
        });
        let accumulatedSummary = '';
        // Helper function to get token headers
        const getAuthHeaders = async () => {
            if (!getToken)
                return {};
            try {
                const token = await getToken();
                console.log('[useAiSummary] Token acquired successfully');
                return { Authorization: `Bearer ${token}` };
            }
            catch (tokenError) {
                console.error('[useAiSummary] Failed to acquire token:', tokenError);
                throw new Error('Failed to acquire authentication token');
            }
        };
        try {
            // Step 1: Resolve Document Profile playbook
            let playbookId;
            try {
                const playbookUrl = `${apiBaseUrl}/api/ai/playbooks/by-name/Document%20Profile`;
                const authHeaders = await getAuthHeaders();
                const playbookResponse = await fetch(playbookUrl, {
                    method: 'GET',
                    headers: {
                        Accept: 'application/json',
                        ...authHeaders,
                    },
                    signal: abortController.signal,
                });
                if (!playbookResponse.ok) {
                    throw new Error(`Failed to resolve playbook: HTTP ${playbookResponse.status}`);
                }
                const playbook = await playbookResponse.json();
                playbookId = playbook.playbookId || playbook.id;
                console.log('[useAiSummary] Resolved Document Profile playbook:', playbookId);
            }
            catch (playbookError) {
                console.error('[useAiSummary] Failed to resolve playbook:', playbookError);
                throw new Error('Failed to resolve Document Profile playbook');
            }
            // Step 2: Execute analysis with new unified endpoint
            const streamUrl = `${apiBaseUrl}/api/ai/analysis/execute`;
            console.log('[useAiSummary] Fetching:', streamUrl);
            const authHeaders = await getAuthHeaders();
            const response = await fetch(streamUrl, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    Accept: 'text/event-stream',
                    ...authHeaders,
                },
                body: JSON.stringify({
                    documentIds: [documentId], // Array for multi-document support
                    playbookId: playbookId,
                    actionId: null, // Use playbook's default action
                    additionalContext: null,
                }),
                signal: abortController.signal,
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
                            // Handle metadata event (analysisId, documentName)
                            if (chunk.type === 'metadata') {
                                if (chunk.analysisId) {
                                    updateDocument(documentId, {
                                        analysisId: chunk.analysisId,
                                    });
                                    console.log('[useAiSummary] Analysis ID:', chunk.analysisId);
                                }
                                continue;
                            }
                            // Handle done event (analysis complete)
                            if (chunk.type === 'done' || chunk.done) {
                                const updates = {
                                    status: 'complete',
                                    summary: accumulatedSummary,
                                };
                                // Add analysis metadata
                                if (chunk.analysisId) {
                                    updates.analysisId = chunk.analysisId;
                                }
                                // Add storage result metadata (soft failure handling)
                                if (chunk.partialStorage !== undefined) {
                                    updates.partialStorage = chunk.partialStorage;
                                }
                                if (chunk.storageMessage) {
                                    updates.storageMessage = chunk.storageMessage;
                                }
                                // Legacy callback for backward compatibility
                                if (onSummaryComplete && updates.summary) {
                                    onSummaryComplete(documentId, updates.summary);
                                }
                                updateDocument(documentId, updates);
                                activeStreamsRef.current.delete(documentId);
                                console.log('[useAiSummary] Analysis complete:', {
                                    analysisId: chunk.analysisId,
                                    partialStorage: chunk.partialStorage,
                                    storageMessage: chunk.storageMessage,
                                });
                                return;
                            }
                            // Handle streaming text content (type="chunk")
                            if (chunk.type === 'chunk' || chunk.content) {
                                accumulatedSummary += chunk.content || '';
                                updateDocument(documentId, { summary: accumulatedSummary });
                            }
                        }
                    }
                }
            }
        }
        catch (err) {
            if (err instanceof Error && err.name === 'AbortError') {
                // Aborted - don't update to error
                return;
            }
            updateDocument(documentId, {
                status: 'error',
                error: err instanceof Error ? err.message : 'Unknown error',
            });
        }
        finally {
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
    const addDocuments = useCallback((docs) => {
        console.log('[useAiSummary] addDocuments called with:', docs.length, 'documents', docs);
        // Add to document map
        docs.forEach(doc => documentMapRef.current.set(doc.documentId, doc));
        // Create initial states
        const newStates = docs.map(doc => ({
            documentId: doc.documentId,
            fileName: doc.fileName,
            status: 'pending',
            summary: undefined,
            error: undefined,
            tldr: undefined,
            keywords: undefined,
            entities: undefined,
            documentType: undefined,
            parsedSuccessfully: undefined,
            analysisId: undefined,
            partialStorage: undefined,
            storageMessage: undefined,
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
    const retry = useCallback((documentId) => {
        const doc = documentMapRef.current.get(documentId);
        if (!doc)
            return;
        // Reset status to pending and clear all fields
        updateDocument(documentId, {
            status: 'pending',
            summary: undefined,
            error: undefined,
            tldr: undefined,
            keywords: undefined,
            entities: undefined,
            documentType: undefined,
            parsedSuccessfully: undefined,
            analysisId: undefined,
            partialStorage: undefined,
            storageMessage: undefined,
        });
        // Add back to queue
        pendingQueueRef.current.push(doc);
        processQueue();
    }, [updateDocument, processQueue]);
    /**
     * Enqueue incomplete summaries for background processing
     * @deprecated Background AI analysis has been removed. AI analysis now requires user context (OBO authentication).
     * This method is a no-op and exists only for backward compatibility.
     */
    const enqueueIncomplete = useCallback(async () => {
        console.warn('[useAiSummary] enqueueIncomplete is deprecated. Background AI analysis has been removed. Users must trigger analysis manually.');
        // No-op: Background job processing has been removed per ARCHITECTURE-CHANGES.md
        // The DocumentIntelligenceService and enqueue-batch endpoint no longer exist
        // AI analysis now requires user context (OBO authentication) and cannot run as background job
    }, []);
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
    const hasIncomplete = documents.some(d => d.status !== 'complete' && d.status !== 'skipped' && d.status !== 'not-supported');
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
        hasIncomplete,
    };
};
export default useAiSummary;
//# sourceMappingURL=useAiSummary.js.map
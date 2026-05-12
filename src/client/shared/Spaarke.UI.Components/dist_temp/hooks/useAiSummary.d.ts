/**
 * AI Summary Orchestration Hook (useAiSummary)
 *
 * Manages AI document analysis for multiple documents with concurrent streaming,
 * status tracking, and batch enqueue on close. Updated for the Document Intelligence
 * API which returns structured analysis results (summary, TL;DR, keywords, entities).
 *
 * @version 2.0.0.0
 */
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
    /** Analysis ID from Dataverse (available after completion) */
    analysisId?: string;
    /** Whether storage partially succeeded (outputs saved but field mapping failed) */
    partialStorage?: boolean;
    /** User-friendly message about storage result */
    storageMessage?: string;
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
 * useAiSummary Hook
 *
 * Orchestrates AI document analysis for multiple documents with
 * concurrent streaming and status management.
 */
export declare const useAiSummary: (options: UseAiSummaryOptions) => UseAiSummaryResult;
export default useAiSummary;
//# sourceMappingURL=useAiSummary.d.ts.map
/**
 * Type definitions for Analysis Workspace PCF control
 */

/**
 * Analysis record from Dataverse
 */
export interface IAnalysis {
    sprk_analysisid: string;
    sprk_name: string;
    sprk_documentid: string;
    _sprk_actionid_value?: string;  // Lookup field (OData format: _fieldname_value)
    statuscode: AnalysisStatusCode;  // Standard Power Apps Status Reason field
    sprk_workingdocument?: string;
    sprk_chathistory?: string;
    createdon: string;
    modifiedon: string;
}

/**
 * Analysis Status Reason (statuscode) values
 * Uses standard Power Apps Status Reason field instead of custom sprk_status
 *
 * | Label       | Value       |
 * |-------------|-------------|
 * | Draft       | 1           |
 * | In Progress | 100,000,001 |
 * | In Review   | 100,000,002 |
 * | Closed      | 2           |
 * | Completed   | 100,000,003 |
 */
export enum AnalysisStatusCode {
    Draft = 1,
    InProgress = 100000001,
    InReview = 100000002,
    Closed = 2,
    Completed = 100000003
}

/**
 * Chat message for AI conversation
 */
export interface IChatMessage {
    id: string;
    role: "user" | "assistant" | "system";
    content: string;
    timestamp: string;
    isStreaming?: boolean;
}

/**
 * Props for AnalysisWorkspaceApp component
 */
export interface IAnalysisWorkspaceAppProps {
    analysisId: string;
    documentId: string;
    containerId: string;
    fileId: string;
    apiBaseUrl: string;
    webApi: ComponentFramework.WebApi;
    onWorkingDocumentChange: (content: string) => void;
    onChatHistoryChange: (history: string) => void;
    onStatusChange: (status: string) => void;
}

/**
 * Analysis API execute request
 */
export interface IAnalysisExecuteRequest {
    documentId: string;
    actionId?: string;
    skillIds?: string[];
    knowledgeIds?: string[];
    toolIds?: string[];
    outputFormat?: "markdown" | "structured_json";
    maxTokens?: number;
}

/**
 * Analysis API continue request (chat)
 */
export interface IAnalysisContinueRequest {
    analysisId: string;
    userMessage: string;
    workingDocument?: string;
}

/**
 * SSE chunk from streaming response
 */
export interface ISseChunk {
    type: "content" | "error" | "done";
    content?: string;
    error?: string;
    tokenUsage?: {
        promptTokens: number;
        completionTokens: number;
        totalTokens: number;
    };
}

/**
 * Document preview info
 */
export interface IDocumentPreview {
    fileId: string;
    fileName: string;
    mimeType: string;
    previewUrl?: string;
    textContent?: string;
}

/**
 * Editor panel state
 */
export interface IEditorPanelState {
    content: string;
    isDirty: boolean;
    lastSaved?: string;
    isAutoSaveEnabled: boolean;
}

/**
 * Chat panel state
 */
export interface IChatPanelState {
    messages: IChatMessage[];
    isStreaming: boolean;
    currentStreamContent: string;
    inputValue: string;
}

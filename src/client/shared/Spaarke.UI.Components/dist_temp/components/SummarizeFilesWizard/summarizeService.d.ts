/**
 * summarizeService.ts
 * Service layer for the Summarize New File(s) wizard.
 * Calls the BFF summarize endpoint with uploaded files via SSE stream.
 *
 * Accepts `authenticatedFetch` and `bffBaseUrl` as parameters so the service
 * remains decoupled from any specific auth/config module.
 */
import type { IUploadedFile } from '../FileUpload/fileUploadTypes';
import type { ISummarizeResult } from './summarizeTypes';
/** Type for an authenticated fetch function matching the standard Fetch API signature. */
export type AuthenticatedFetchFn = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;
export interface StreamSummarizeCallbacks {
    /** Called when a progress step event is received (step = "document_loaded" | "extracting_text" | ...) */
    onProgress?: (stepId: string) => void;
}
/**
 * Calls POST /api/workspace/files/summarize via SSE.
 * Fires onProgress callbacks as each pipeline step is announced.
 * Resolves with the structured ISummarizeResult when the stream completes.
 * Throws on error or if the stream ends with no result.
 */
export declare function streamSummarize(files: IUploadedFile[], callbacks?: StreamSummarizeCallbacks, signal?: AbortSignal, authenticatedFetch?: AuthenticatedFetchFn, bffBaseUrl?: string): Promise<ISummarizeResult>;
/**
 * @deprecated Use streamSummarize instead. This REST endpoint no longer exists on the BFF.
 */
export declare function runSummarize(files: IUploadedFile[], signal?: AbortSignal, authenticatedFetch?: AuthenticatedFetchFn, bffBaseUrl?: string): Promise<ISummarizeResult>;
//# sourceMappingURL=summarizeService.d.ts.map
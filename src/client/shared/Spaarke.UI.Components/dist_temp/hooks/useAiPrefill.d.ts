/**
 * useAiPrefill.ts
 * Reusable hook for AI pre-fill in entity creation wizards.
 *
 * Sends uploaded files to a BFF pre-fill endpoint, receives AI-extracted
 * field values, resolves lookup fields via fuzzy matching against Dataverse,
 * and calls onApply with the resolved values.
 *
 * @example Matter wizard usage:
 * ```typescript
 * const prefill = useAiPrefill({
 *   endpoint: '/api/workspace/matters/pre-fill',
 *   uploadedFiles,
 *   authenticatedFetch,
 *   bffBaseUrl: getBffBaseUrl(),
 *   fieldExtractor: (data) => ({
 *     textFields: {
 *       matterName: data.matterName,
 *       summary: data.summary,
 *     },
 *     lookupFields: {
 *       matterTypeName: data.matterTypeName || data.matterType,
 *       practiceAreaName: data.practiceAreaName || data.practiceArea,
 *       assignedAttorneyName: data.assignedAttorneyName,
 *     },
 *   }),
 *   lookupResolvers: {
 *     matterTypeName: (v) => searchMatterTypes(webApi, v),
 *     practiceAreaName: (v) => searchPracticeAreas(webApi, v),
 *     assignedAttorneyName: (v) => searchContactsAsLookup(webApi, v),
 *   },
 *   onApply: (resolved) => dispatch({ type: 'APPLY_AI_PREFILL', fields: resolved }),
 *   skipIfInitialized: hasInitialValues,
 * });
 *
 * // prefill.status: 'idle' | 'loading' | 'success' | 'error'
 * // prefill.prefilledFields: string[] — field names that were pre-filled
 * ```
 */
import type { IUploadedFile } from '../components/FileUpload/fileUploadTypes';
import type { ILookupItem } from '../types/LookupTypes';
export type AiPrefillStatus = 'idle' | 'loading' | 'success' | 'error';
/** Result returned by the useAiPrefill hook. */
export interface IAiPrefillResult {
    /** Current status of the pre-fill operation. */
    status: AiPrefillStatus;
    /** Names of fields that were successfully pre-filled. */
    prefilledFields: string[];
    /** Error message if status is 'error'. */
    error?: string;
}
/**
 * Extracted fields from the AI response, split into text fields (direct values)
 * and lookup fields (need Dataverse resolution).
 */
export interface IExtractedFields {
    /** Fields that map directly to form values (no lookup needed). */
    textFields: Record<string, string | undefined>;
    /** Fields that need Dataverse lookup resolution (display name → GUID). */
    lookupFields: Record<string, string | undefined>;
}
/**
 * Resolved lookup result: the matched Dataverse item (id + name) for a field,
 * or a string for text-only fields.
 */
export interface IResolvedPrefillFields {
    [fieldName: string]: {
        id: string;
        name: string;
    } | string;
}
/** Configuration for the useAiPrefill hook. */
export interface IAiPrefillConfig {
    /** BFF endpoint path, e.g. '/api/workspace/matters/pre-fill'. */
    endpoint: string;
    /** Files from Step 1 to send for analysis. */
    uploadedFiles: IUploadedFile[];
    /** Authenticated fetch function (from @spaarke/auth or solution-specific). */
    authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>;
    /** BFF base URL (e.g. 'https://spe-api-dev-67e2xz.azurewebsites.net/api'). */
    bffBaseUrl: string;
    /**
     * Extract fields from the raw AI response JSON.
     * Split into textFields (direct values) and lookupFields (need resolution).
     */
    fieldExtractor: (data: Record<string, unknown>) => IExtractedFields;
    /**
     * Map of lookup field name → search function.
     * Each search function queries Dataverse and returns candidates for fuzzy matching.
     */
    lookupResolvers: Record<string, (aiValue: string) => Promise<ILookupItem[]>>;
    /**
     * Called with the final resolved fields (text values + lookup {id, name} pairs).
     * The consumer applies these to its form state.
     */
    onApply: (fields: IResolvedPrefillFields, prefilledFieldNames: string[]) => void;
    /** Timeout in ms (default: 60000). BFF has 45s playbook timeout + extraction time. */
    timeout?: number;
    /** Skip pre-fill if true (e.g., form already has initial values from a prior mount). */
    skipIfInitialized?: boolean;
    /** Log prefix for console messages (default: 'AiPrefill'). */
    logPrefix?: string;
}
/**
 * Hook that manages AI pre-fill for entity creation wizard forms.
 *
 * On mount (when uploadedFiles is non-empty and not skipped):
 * 1. POSTs files as multipart/form-data to BFF endpoint
 * 2. Extracts text and lookup fields from AI response
 * 3. Resolves lookup fields via Dataverse search + fuzzy matching
 * 4. Calls onApply with resolved values
 *
 * Handles: timeout, cancellation, abort on unmount, double-execution guard.
 */
export declare function useAiPrefill(config: IAiPrefillConfig): IAiPrefillResult;
//# sourceMappingURL=useAiPrefill.d.ts.map
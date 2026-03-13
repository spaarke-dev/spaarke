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
 *   endpoint: '/workspace/matters/pre-fill',
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

import * as React from 'react';
import type { IUploadedFile } from '../components/FileUpload/fileUploadTypes';
import type { ILookupItem } from '../types/LookupTypes';
import { findBestLookupMatch } from '../utils/lookupMatching';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

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
  [fieldName: string]: { id: string; name: string } | string;
}

/** Configuration for the useAiPrefill hook. */
export interface IAiPrefillConfig {
  /** BFF endpoint path, e.g. '/workspace/matters/pre-fill'. */
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

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

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
export function useAiPrefill(config: IAiPrefillConfig): IAiPrefillResult {
  const {
    endpoint,
    uploadedFiles,
    authenticatedFetch: fetchFn,
    bffBaseUrl,
    fieldExtractor,
    lookupResolvers,
    onApply,
    timeout = 60_000,
    skipIfInitialized = false,
    logPrefix = 'AiPrefill',
  } = config;

  const [status, setStatus] = React.useState<AiPrefillStatus>('idle');
  const [prefilledFields, setPrefilledFields] = React.useState<string[]>([]);
  const [error, setError] = React.useState<string | undefined>();

  // Guard against double-execution (React strict mode or remount)
  const attemptedRef = React.useRef(skipIfInitialized);

  // Stable refs for callbacks to avoid re-triggering useEffect
  const onApplyRef = React.useRef(onApply);
  React.useEffect(() => { onApplyRef.current = onApply; }, [onApply]);
  const fieldExtractorRef = React.useRef(fieldExtractor);
  React.useEffect(() => { fieldExtractorRef.current = fieldExtractor; }, [fieldExtractor]);
  const lookupResolversRef = React.useRef(lookupResolvers);
  React.useEffect(() => { lookupResolversRef.current = lookupResolvers; }, [lookupResolvers]);

  // Stable dependency key: join file names so the effect doesn't re-run on array ref changes
  const prefillKey = uploadedFiles.map((f) => f.name).join('|');

  React.useEffect(() => {
    if (uploadedFiles.length === 0 || attemptedRef.current) {
      return;
    }

    attemptedRef.current = true;
    let cancelled = false;
    const abortController = new AbortController();

    const runPrefill = async (): Promise<void> => {
      setStatus('loading');
      setError(undefined);

      const timeoutId = window.setTimeout(() => abortController.abort(), timeout);

      try {
        // Build multipart/form-data (BFF expects IFormFileCollection)
        const formData = new FormData();
        for (const f of uploadedFiles) {
          formData.append('files', f.file, f.name);
        }

        console.info(`[${logPrefix}] Starting AI pre-fill...`, { fileCount: uploadedFiles.length });

        const response = await fetchFn(`${bffBaseUrl}${endpoint}`, {
          method: 'POST',
          body: formData,
          signal: abortController.signal,
          // Note: do NOT set Content-Type header — browser sets it with boundary
        });

        clearTimeout(timeoutId);
        if (cancelled) return;

        if (!response.ok) {
          console.warn(`[${logPrefix}] Pre-fill returned ${response.status}`);
          setStatus('error');
          setError(`HTTP ${response.status}`);
          return;
        }

        const data = await response.json();
        console.info(`[${logPrefix}] Pre-fill response:`, data);
        if (cancelled) return;

        // Extract text and lookup fields from AI response
        const { textFields, lookupFields } = fieldExtractorRef.current(data);

        // Build resolved fields: start with text fields
        const resolved: IResolvedPrefillFields = {};
        const fieldNames: string[] = [];

        for (const [key, value] of Object.entries(textFields)) {
          if (value) {
            resolved[key] = value;
            fieldNames.push(key);
          }
        }

        // Resolve lookup fields via Dataverse search + fuzzy match
        const resolvers = lookupResolversRef.current;
        const resolvePromises: Promise<void>[] = [];

        for (const [fieldName, aiValue] of Object.entries(lookupFields)) {
          if (!aiValue) continue;
          const resolver = resolvers[fieldName];
          if (!resolver) {
            // No resolver — treat as text field
            resolved[fieldName] = aiValue;
            fieldNames.push(fieldName);
            continue;
          }

          resolvePromises.push(
            resolver(aiValue)
              .then((candidates) => {
                const best = findBestLookupMatch(aiValue, candidates);
                if (best) {
                  resolved[fieldName] = { id: best.id, name: best.name };
                  fieldNames.push(fieldName);
                } else {
                  // No match — still include as text for display
                  resolved[fieldName] = aiValue;
                  fieldNames.push(fieldName);
                }
              })
              .catch(() => {
                // Lookup failed — keep display name only
                resolved[fieldName] = aiValue;
                fieldNames.push(fieldName);
              })
          );
        }

        await Promise.all(resolvePromises);
        if (cancelled) return;

        if (fieldNames.length > 0) {
          onApplyRef.current(resolved, fieldNames);
        }

        setPrefilledFields(fieldNames);
        setStatus('success');
      } catch (err) {
        clearTimeout(timeoutId);
        if (!cancelled) {
          if (abortController.signal.aborted) {
            console.warn(`[${logPrefix}] Pre-fill timed out after ${timeout}ms`);
            setError('Request timed out');
          } else {
            console.warn(`[${logPrefix}] Pre-fill failed:`, err);
            setError(err instanceof Error ? err.message : 'Unknown error');
          }
          setStatus('error');
        }
      }
    };

    void runPrefill();

    return () => {
      cancelled = true;
      abortController.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [prefillKey]);

  return { status, prefilledFields, error };
}

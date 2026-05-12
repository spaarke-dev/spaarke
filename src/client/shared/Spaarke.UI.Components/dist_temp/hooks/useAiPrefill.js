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
import * as React from 'react';
import { findBestLookupMatch } from '../utils/lookupMatching';
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
export function useAiPrefill(config) {
    const { endpoint, uploadedFiles, authenticatedFetch: fetchFn, bffBaseUrl, fieldExtractor, lookupResolvers, onApply, timeout = 60000, skipIfInitialized = false, logPrefix = 'AiPrefill', } = config;
    const [status, setStatus] = React.useState('idle');
    const [prefilledFields, setPrefilledFields] = React.useState([]);
    const [error, setError] = React.useState();
    // Guard against double-execution (React strict mode or remount)
    const attemptedRef = React.useRef(skipIfInitialized);
    // Stable refs for callbacks to avoid re-triggering useEffect
    const onApplyRef = React.useRef(onApply);
    React.useEffect(() => {
        onApplyRef.current = onApply;
    }, [onApply]);
    const fieldExtractorRef = React.useRef(fieldExtractor);
    React.useEffect(() => {
        fieldExtractorRef.current = fieldExtractor;
    }, [fieldExtractor]);
    const lookupResolversRef = React.useRef(lookupResolvers);
    React.useEffect(() => {
        lookupResolversRef.current = lookupResolvers;
    }, [lookupResolvers]);
    // Stable dependency key: join file names so the effect doesn't re-run on array ref changes
    const prefillKey = uploadedFiles.map(f => f.name).join('|');
    React.useEffect(() => {
        if (uploadedFiles.length === 0 || attemptedRef.current) {
            return;
        }
        attemptedRef.current = true;
        let cancelled = false;
        const abortController = new AbortController();
        const runPrefill = async () => {
            setStatus('loading');
            setError(undefined);
            const timeoutId = window.setTimeout(() => abortController.abort(), timeout);
            try {
                // Build multipart/form-data (BFF expects IFormFileCollection)
                const formData = new FormData();
                for (const f of uploadedFiles) {
                    formData.append('files', f.file, f.name);
                }
                console.info(`[${logPrefix}] Starting AI pre-fill...`, {
                    fileCount: uploadedFiles.length,
                });
                const response = await fetchFn(`${bffBaseUrl}${endpoint}`, {
                    method: 'POST',
                    body: formData,
                    signal: abortController.signal,
                    // Note: do NOT set Content-Type header — browser sets it with boundary
                });
                clearTimeout(timeoutId);
                if (cancelled)
                    return;
                if (!response.ok) {
                    console.warn(`[${logPrefix}] Pre-fill returned ${response.status}`);
                    setStatus('error');
                    setError(`HTTP ${response.status}`);
                    return;
                }
                const data = await response.json();
                console.info(`[${logPrefix}] Pre-fill response:`, data);
                if (cancelled)
                    return;
                // Extract text and lookup fields from AI response
                const { textFields, lookupFields } = fieldExtractorRef.current(data);
                // Build resolved fields: start with text fields
                const resolved = {};
                const fieldNames = [];
                for (const [key, value] of Object.entries(textFields)) {
                    if (value) {
                        resolved[key] = value;
                        fieldNames.push(key);
                    }
                }
                // Resolve lookup fields via Dataverse search + fuzzy match
                const resolvers = lookupResolversRef.current;
                const resolvePromises = [];
                for (const [fieldName, aiValue] of Object.entries(lookupFields)) {
                    if (!aiValue)
                        continue;
                    const resolver = resolvers[fieldName];
                    if (!resolver) {
                        // No resolver — treat as text field
                        resolved[fieldName] = aiValue;
                        fieldNames.push(fieldName);
                        continue;
                    }
                    resolvePromises.push(resolver(aiValue)
                        .then(candidates => {
                        const best = findBestLookupMatch(aiValue, candidates);
                        if (best) {
                            resolved[fieldName] = { id: best.id, name: best.name };
                            fieldNames.push(fieldName);
                        }
                        else {
                            // No match — still include as text for display
                            resolved[fieldName] = aiValue;
                            fieldNames.push(fieldName);
                        }
                    })
                        .catch(() => {
                        // Lookup failed — keep display name only
                        resolved[fieldName] = aiValue;
                        fieldNames.push(fieldName);
                    }));
                }
                await Promise.all(resolvePromises);
                if (cancelled)
                    return;
                if (fieldNames.length > 0) {
                    onApplyRef.current(resolved, fieldNames);
                }
                setPrefilledFields(fieldNames);
                setStatus('success');
            }
            catch (err) {
                clearTimeout(timeoutId);
                if (!cancelled) {
                    if (abortController.signal.aborted) {
                        console.warn(`[${logPrefix}] Pre-fill timed out after ${timeout}ms`);
                        setError('Request timed out');
                    }
                    else {
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
//# sourceMappingURL=useAiPrefill.js.map
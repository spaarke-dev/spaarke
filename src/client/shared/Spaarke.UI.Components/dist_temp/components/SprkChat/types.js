/**
 * SprkChat Types
 *
 * Type definitions for the SprkChat component and its sub-components.
 * Aligns with the ChatEndpoints.cs API contract (AIPL-054).
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9
 * @see ADR-022 - React 16 APIs only
 */
/** Default quick action presets for highlight-refine. */
export const DEFAULT_QUICK_ACTIONS = [
    { key: 'simplify', label: 'Simplify', instruction: 'Simplify this text' },
    {
        key: 'expand',
        label: 'Expand',
        instruction: 'Expand this text with more detail',
    },
    {
        key: 'concise',
        label: 'Make Concise',
        instruction: 'Make this text more concise',
    },
    {
        key: 'formal',
        label: 'Make Formal',
        instruction: 'Rewrite this text in a more formal tone',
    },
];
/** Maximum character length for the truncated `text` preview. Selections longer than this are clipped. */
export const CROSS_PANE_SELECTION_MAX_PREVIEW = 5000;
/**
 * Timeout threshold in milliseconds for document processing (NFR-02: 15 seconds).
 * When processing exceeds this threshold, the UI shows an extended wait message.
 */
export const DOCUMENT_PROCESSING_TIMEOUT_MS = 15000;
//# sourceMappingURL=types.js.map
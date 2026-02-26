/**
 * DiffCompareView - AI revision diff comparison component
 * Displays side-by-side and inline diff views with Accept/Reject/Edit actions.
 * Supports both plain-text and HTML diff modes.
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9 Design System
 */

export { DiffCompareView } from "./DiffCompareView";
export { extractTextFromHtml, computeHtmlDiff, detectBlockChanges, escapeHtml } from "./diffUtils";
export type {
    IDiffCompareViewProps,
    IDiffSegment,
    DiffCompareViewMode,
    DiffOptions,
    DiffResult,
    DiffStats,
    BlockChange,
    BlockChangeType,
} from "./DiffCompareView.types";

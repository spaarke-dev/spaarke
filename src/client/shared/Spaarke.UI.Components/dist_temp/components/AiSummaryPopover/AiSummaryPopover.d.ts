/**
 * AiSummaryPopover - Reusable AI Summary popover component.
 *
 * Displays a popover with AI-generated summary content (TLDR + full summary).
 * Fetches summary lazily on first open via callback prop. Includes copy-to-clipboard.
 *
 * Consumer provides a trigger element and an async fetch callback.
 * Zero service dependencies — fully callback-based.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 */
import * as React from 'react';
/**
 * Summary data returned by the fetch callback.
 */
export interface ISummaryData {
    summary: string | null;
    tldr: string | null;
}
/**
 * Props for the AiSummaryPopover component.
 */
export interface IAiSummaryPopoverProps {
    /** The trigger element that opens the popover (typically a Button). */
    trigger: React.ReactElement;
    /** Async callback to fetch summary data. Called once on first open. */
    onFetchSummary: () => Promise<ISummaryData>;
    /** Popover positioning relative to trigger. Default: "after". */
    positioning?: 'above' | 'below' | 'before' | 'after';
    /** Whether to show the arrow. Default: true. */
    withArrow?: boolean;
}
export declare const AiSummaryPopover: React.FC<IAiSummaryPopoverProps>;
export default AiSummaryPopover;
//# sourceMappingURL=AiSummaryPopover.d.ts.map
/**
 * SummaryResultsStep.tsx
 * Step 2 of the Summarize New File(s) wizard — displays AI-generated summary results.
 *
 * Sections rendered (conditionally):
 *   - TL;DR (always)
 *   - Summary (always)
 *   - File-by-File Highlights (multi-file only)
 *   - Related Practice Areas (if detected)
 *   - Who's Mentioned (if parties found)
 *   - Call to Action (if actionable items found)
 */
import * as React from 'react';
import type { ISummarizeResult } from './summarizeTypes';
import type { SummarizeStatus } from './summarizeTypes';
export interface ISummaryResultsStepProps {
    status: SummarizeStatus;
    result: ISummarizeResult | null;
    errorMessage: string | null;
    onRetry: () => void;
    /** Active step ID driven by SSE progress events from the parent. */
    activeStepId: string | null;
    /** Completed step IDs driven by SSE progress events from the parent. */
    completedStepIds: string[];
}
export declare const SummaryResultsStep: React.FC<ISummaryResultsStepProps>;
//# sourceMappingURL=SummaryResultsStep.d.ts.map
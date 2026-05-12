/**
 * PlanPreviewCard - Renders a proposed multi-step plan with action controls.
 *
 * Displays a plan title, ordered steps with status indicators, and three
 * action buttons: Proceed (executes the plan), Edit Plan (opens inline
 * text input to send a modification message), and Cancel (aborts).
 *
 * Steps update in real-time as execution progresses — passing updated
 * `steps` props re-renders the card with current statuses and partial results.
 *
 * This card is the gate for Phase 2F compound intent execution:
 * no plan executes until the user clicks Proceed.
 *
 * @see ADR-012 - Shared Component Library (callback-based, no Xrm)
 * @see ADR-021 - Fluent UI v9 design tokens, dark mode support
 * @see spec-2E / spec-2F - Plan preview and execution gate requirements
 */
import * as React from 'react';
/**
 * Status of an individual plan step during execution.
 * - pending:   Not yet started (shows a numbered circle)
 * - running:   Currently executing (shows a Spinner)
 * - completed: Finished successfully (shows CheckmarkCircle)
 * - failed:    Finished with an error (shows DismissCircle)
 */
export type PlanStepStatus = 'pending' | 'running' | 'completed' | 'failed';
/**
 * A single step within a proposed plan.
 */
export interface PlanStep {
    /** Stable unique identifier for this step. */
    id: string;
    /** Human-readable description of what this step does. */
    description: string;
    /** Current execution status; defaults to 'pending' before execution begins. */
    status: PlanStepStatus;
    /**
     * Optional partial result text streamed in while the step is running or
     * the full result after the step completes. Rendered as muted text below
     * the step description.
     */
    result?: string;
}
/**
 * Props for the PlanPreviewCard component.
 */
export interface PlanPreviewCardProps {
    /** Display title for the plan (e.g., "Analyze Contract Risk and Summarize Findings"). */
    planTitle: string;
    /** Ordered list of plan steps. Update this array to reflect execution progress. */
    steps: PlanStep[];
    /**
     * Whether the plan is currently executing.
     * When true, the Proceed button is disabled to prevent double-submission.
     */
    isExecuting: boolean;
    /**
     * Called when the user confirms they want to execute the plan.
     * The parent should begin streaming execution and update step statuses.
     */
    onProceed: () => void;
    /**
     * Called when the user wants to cancel the plan before execution.
     * The parent should dismiss the card.
     */
    onCancel: () => void;
    /**
     * Called when the user submits an edit message to modify the plan.
     * The parent should send this message to the BFF as a new chat message.
     * @param editMessage - Free-text modification request from the user.
     */
    onEditPlan: (editMessage: string) => void;
    /**
     * Called when the user wants to cancel an in-progress execution.
     * MUST abort the SSE stream via AbortController (spec MUST rule).
     * Only meaningful when isExecuting is true.
     */
    onCancelExecution?: () => void;
}
/**
 * PlanPreviewCard
 *
 * Renders a proposed multi-step AI plan with Proceed/Edit Plan/Cancel controls
 * and per-step progress indicators. Designed to be the execution gate for
 * compound AI intents: the plan does not execute until the user clicks Proceed.
 *
 * @example
 * ```tsx
 * <PlanPreviewCard
 *   planTitle="Analyze Contract Risk and Summarize Findings"
 *   steps={[
 *     { id: 's1', description: 'Extract key clauses', status: 'completed', result: '5 clauses found' },
 *     { id: 's2', description: 'Assess risk level', status: 'running' },
 *     { id: 's3', description: 'Generate summary', status: 'pending' },
 *   ]}
 *   isExecuting={true}
 *   onProceed={handleProceed}
 *   onCancel={handleCancel}
 *   onEditPlan={handleEditPlan}
 * />
 * ```
 */
export declare const PlanPreviewCard: React.FC<PlanPreviewCardProps>;
//# sourceMappingURL=PlanPreviewCard.d.ts.map
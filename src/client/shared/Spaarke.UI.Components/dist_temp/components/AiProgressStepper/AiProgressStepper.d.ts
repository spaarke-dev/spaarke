/**
 * AiProgressStepper
 *
 * Fluent v9 compliant multi-step progress indicator for AI analysis operations.
 * Displays a horizontal step track with all steps visible — active (blue),
 * completed (green), pending (grey). The active step's description and a short
 * indeterminate ProgressBar appear below the track.
 *
 * Variants:
 *   - `card`: absolute-positioned overlay with semi-transparent backdrop
 *   - `inline`: flat layout embedded directly in parent container
 *
 * @see ADR-021 - Fluent UI v9 design system
 * @see ADR-012 - Shared Component Library conventions
 */
import type { AiProgressStepperProps } from "./AiProgressStepper.types";
export declare function AiProgressStepper({ steps, activeStepId, completedStepIds, errorStepId, title, onCancel, variant, }: AiProgressStepperProps): JSX.Element;
//# sourceMappingURL=AiProgressStepper.d.ts.map
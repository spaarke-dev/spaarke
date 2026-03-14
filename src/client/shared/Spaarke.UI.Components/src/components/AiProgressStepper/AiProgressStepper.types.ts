/**
 * AiProgressStepper types
 *
 * Types and step definitions for the AiProgressStepper component.
 * Steps map directly to backend `progress` chunk `step` field values.
 *
 * @see ADR-021 - Fluent UI v9 design system
 * @see ADR-012 - Shared Component Library conventions
 */

export type AiProgressStepStatus = "pending" | "active" | "completed" | "error";

export interface AiProgressStep {
  /** Matches the backend `step` field in progress chunks (e.g. "extracting_text"). */
  id: string;
  /** Display label shown in the step list (e.g. "Reading Content"). */
  label: string;
  /** Optional description shown below the label when the step is active. */
  description?: string;
}

export interface AiProgressStepperProps {
  /** Ordered list of steps to display. */
  steps: AiProgressStep[];
  /** The step currently in progress. Null means no step is active yet. */
  activeStepId: string | null;
  /** IDs of steps that have completed successfully. */
  completedStepIds: string[];
  /** ID of a step that failed (shows error state). */
  errorStepId?: string | null;
  /** When true, the active indicator animates to convey streaming activity. */
  isStreaming?: boolean;
  /** Card header title. Defaults to "Analyzing..." */
  title?: string;
  /** Cancel callback. When provided, a cancel button appears in the header. */
  onCancel?: () => void;
  /**
   * Layout variant:
   * - `card`: floating overlay with semi-transparent backdrop (AnalysisWorkspace, PlaybookBuilder)
   * - `inline`: flat layout embedded directly in parent container (wizard steps, REST surfaces)
   */
  variant?: "card" | "inline";
}

/**
 * Default steps for document analysis.
 * Step IDs match the backend `step` field emitted by AnalysisOrchestrationService.
 */
export const DOCUMENT_ANALYSIS_STEPS: AiProgressStep[] = [
  {
    id: "document_loaded",
    label: "Opening Document",
    description: "Loading document metadata...",
  },
  {
    id: "extracting_text",
    label: "Reading Content",
    description: "Extracting text with Document Intelligence...",
  },
  {
    id: "context_ready",
    label: "Preparing Analysis",
    description: "Loading knowledge and context...",
  },
  {
    id: "analyzing",
    label: "Running Analysis",
    description: "AI is analyzing your document...",
  },
  {
    id: "delivering",
    label: "Delivering Results",
    description: "Streaming results to editor...",
  },
];

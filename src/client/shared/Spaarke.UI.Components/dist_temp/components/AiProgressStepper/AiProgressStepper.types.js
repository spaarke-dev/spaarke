/**
 * AiProgressStepper types
 *
 * Types and step definitions for the AiProgressStepper component.
 * Steps map directly to backend `progress` chunk `step` field values.
 *
 * @see ADR-021 - Fluent UI v9 design system
 * @see ADR-012 - Shared Component Library conventions
 */
/**
 * Default steps for document analysis.
 * Step IDs match the backend `step` field emitted by AnalysisOrchestrationService.
 */
export const DOCUMENT_ANALYSIS_STEPS = [
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
//# sourceMappingURL=AiProgressStepper.types.js.map
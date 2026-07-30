/**
 * analysisFileResolution.ts (SpaarkeAi solution) — RE-EXPORT shim.
 *
 * The Analysis file-hop resolver (`sprk_documentid` → `sprk_document` preview,
 * ADR-007) was HOISTED to `@spaarke/ui-components` (2026-07-29 duplicate-component
 * audit) so the shared-lib `AnalysisHubWidget` consumes it directly instead of
 * restating the same document-hop + preview-url closure locally. This module
 * re-exports it so any solution-side import path keeps working; new code may
 * import directly from `@spaarke/ui-components`.
 */

export { resolveAnalysisDocumentId, resolveAnalysisFilePreview } from '@spaarke/ui-components';
export type {
  AnalysisFilePreviewDeps,
  AnalysisFilePreviewResolution,
  AnalysisFilePreviewResolved,
  AnalysisFilePreviewNoDocument,
} from '@spaarke/ui-components';

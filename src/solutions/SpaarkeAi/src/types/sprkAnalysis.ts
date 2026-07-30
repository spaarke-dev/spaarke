/**
 * sprkAnalysis.ts (SpaarkeAi solution) — RE-EXPORT shim.
 *
 * The canonical `sprk_analysis` record type + option-set enums + regarding
 * catalog were HOISTED to `@spaarke/ui-components` (2026-07-29 P2, duplicate-
 * component audit) so the shared-lib widgets (`@spaarke/ai-widgets` —
 * `AnalysisHubWidget`, `CreateAnalysisWizardWidget`) and this solution consume
 * ONE source instead of the widgets restating the work-type/status magic
 * numbers + regarding catalog locally (ADR-012 dependency direction previously
 * forced those restatements).
 *
 * This module re-exports the hoisted symbols so the solution's existing import
 * path (`../types/sprkAnalysis`) keeps working unchanged. New code may import
 * directly from `@spaarke/ui-components`.
 */

export {
  SprkAnalysisWorkType,
  SprkAnalysisStatus,
  ANALYSIS_WORK_TYPE_IDS,
  ANALYSIS_WORK_TYPE_ID_BY_VALUE,
  ANALYSIS_WORK_TYPE_VALUE_BY_ID,
  ANALYSIS_REGARDING_TARGETS,
  SPRK_ANALYSIS_SELECT,
} from '@spaarke/ui-components';

export type {
  AnalysisWorkTypeId,
  AnalysisRegardingTarget,
  ISprkAnalysisRecord,
} from '@spaarke/ui-components';

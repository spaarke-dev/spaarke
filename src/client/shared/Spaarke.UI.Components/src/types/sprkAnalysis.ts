/**
 * sprkAnalysis.ts — Canonical client `sprk_analysis` record type + option-set
 * enums + regarding-target catalog.
 *
 * HOISTED to `@spaarke/ui-components` (2026-07-29 duplicate-component audit, P2)
 * from its original SpaarkeAi-solution location so that BOTH the shared-lib
 * widgets (`@spaarke/ai-widgets` — `AnalysisHubWidget`, `CreateAnalysisWizardWidget`)
 * AND the SpaarkeAi solution consume ONE source. Before the hoist, ADR-012's
 * dependency direction (solution → lib, never the reverse) forced the widgets to
 * RESTATE the work-type/status magic numbers + the regarding-entity catalog
 * locally. Placing the (context-agnostic, Xrm-free) types here eliminates those
 * restatements. The SpaarkeAi `types/sprkAnalysis.ts` now re-exports from here.
 *
 * Convention: mirrors `ITodoRecord` (`components/TodoDetail/types.ts`) — Lookup
 * columns are modeled as the underscore-prefixed `_field_value` OData annotation
 * key (+ optional formatted-value companion), Choice columns are `number | null`
 * with a paired enum, and system fields (`statecode`/`statuscode`) are included.
 * The `ANALYSIS_REGARDING_TARGETS` catalog follows the `TODO_REGARDING_TARGETS` /
 * `EVENT_REGARDING_TARGETS` preset convention in `AssociateToStep/types.ts`.
 *
 * @see ADR-012 — Shared Component Library (this is the canonical shared home)
 * @see ADR-024 — Polymorphic Resolver Pattern (the regarding field-set)
 * @see ADR-007 — SPE file access via the `sprk_documentid` → `sprk_document` hop
 */

// ---------------------------------------------------------------------------
// sprk_worktype — Choice
// ---------------------------------------------------------------------------

/** `sprk_worktype` option-set VALUES — the integer values Dataverse stores. */
export enum SprkAnalysisWorkType {
  AgreementAnalysis = 100000000,
  LegalResearch = 100000001,
  PatentApplication = 100000002,
}

/**
 * Semantic kebab-id mapping for `sprk_worktype` — the closed union consumers
 * (hub cards, wizard branching) key off of. Agreement Review ships **live**;
 * Legal Research + Patent Application render "coming soon" (spec FR-10).
 */
export type AnalysisWorkTypeId = 'agreement-analysis' | 'legal-research' | 'patent-application';

export const ANALYSIS_WORK_TYPE_IDS: readonly AnalysisWorkTypeId[] = [
  'agreement-analysis',
  'legal-research',
  'patent-application',
] as const;

/** `sprk_worktype` integer value → semantic kebab id. */
export const ANALYSIS_WORK_TYPE_ID_BY_VALUE: Record<SprkAnalysisWorkType, AnalysisWorkTypeId> = {
  [SprkAnalysisWorkType.AgreementAnalysis]: 'agreement-analysis',
  [SprkAnalysisWorkType.LegalResearch]: 'legal-research',
  [SprkAnalysisWorkType.PatentApplication]: 'patent-application',
};

/** Semantic kebab id → `sprk_worktype` integer value (inverse of {@link ANALYSIS_WORK_TYPE_ID_BY_VALUE}). */
export const ANALYSIS_WORK_TYPE_VALUE_BY_ID: Record<AnalysisWorkTypeId, SprkAnalysisWorkType> = {
  'agreement-analysis': SprkAnalysisWorkType.AgreementAnalysis,
  'legal-research': SprkAnalysisWorkType.LegalResearch,
  'patent-application': SprkAnalysisWorkType.PatentApplication,
};

// ---------------------------------------------------------------------------
// sprk_analysisstatus — Choice
// ---------------------------------------------------------------------------

/** `sprk_analysisstatus` option-set VALUES. */
export enum SprkAnalysisStatus {
  Draft = 0,
  InProgress = 1,
  Completed = 2,
  Closed = 3,
  OnHold = 4,
  Cancelled = 5,
  Archived = 6,
}

// ---------------------------------------------------------------------------
// Regarding-target catalog (ADR-024) — Matter / Project / Document
// ---------------------------------------------------------------------------

/**
 * A single Analysis-scoped regarding target. Carries everything the READ side
 * (hub: which entity types can be pre-set) AND the WRITE side (wizard:
 * `@odata.bind` via entity set + nav-prop hint) need — so neither surface has
 * to restate its own catalog. Follows the `RegardingTarget` shape convention
 * in `AssociateToStep/types.ts`, extended with `entitySet` + `navPropHint` for
 * the create-time resolver bind.
 */
export interface AnalysisRegardingTarget {
  /** Dataverse logical name (e.g., "sprk_matter"). */
  entityType: string;
  /** Human-readable label shown in the record-type dropdown. */
  label: string;
  /** Entity SET (plural) name for `@odata.bind` (e.g., "sprk_matters"). */
  entitySet: string;
  /** Nav-prop hint for PolymorphicResolverService.applyResolverFields (e.g., "matter"). */
  navPropHint: string;
}

/**
 * Canonical Analysis regarding catalog (ADR-024): exactly one is populated per
 * Analysis. Single source consumed by `AnalysisHubWidget` (pre-set gating) and
 * `CreateAnalysisWizardWidget` (AssociateToStep + create-time resolver bind).
 */
export const ANALYSIS_REGARDING_TARGETS: readonly AnalysisRegardingTarget[] = [
  { entityType: 'sprk_matter', label: 'Matter', entitySet: 'sprk_matters', navPropHint: 'matter' },
  { entityType: 'sprk_project', label: 'Project', entitySet: 'sprk_projects', navPropHint: 'project' },
  { entityType: 'sprk_document', label: 'Document', entitySet: 'sprk_documents', navPropHint: 'document' },
] as const;

// ---------------------------------------------------------------------------
// sprk_analysis record shape (WebApi retrieve result)
// ---------------------------------------------------------------------------

/**
 * A `sprk_analysis` record as returned by Web API `retrieveRecord` /
 * `retrieveMultipleRecords`. Lookup columns use the underscore-prefixed
 * `_field_value` OData annotation (+ optional formatted-value companion);
 * Choice columns are the raw numeric option value. Context-agnostic — no Xrm,
 * no PCF APIs (ADR-012).
 */
export interface ISprkAnalysisRecord {
  // ---- Primary identity -----------------------------------------------------
  sprk_analysisid: string;
  sprk_name: string;

  // ---- Core detail ------------------------------------------------------------
  sprk_description?: string | null;

  /**
   * SPE subject-pointer lookup → `sprk_document` — the file this analysis was
   * run against. Distinct role from `sprk_regardingdocument` below (ADR-007).
   */
  _sprk_documentid_value?: string | null;
  '_sprk_documentid_value@OData.Community.Display.V1.FormattedValue'?: string;

  /** Session grouping key — Text(50), binds to the live chat session. */
  sprk_sessionid?: string | null;

  /** Choice — drives surface + tool palette + wizard branching (spec FR-10). */
  sprk_worktype?: SprkAnalysisWorkType | null;
  sprk_worktypename?: string | null;

  /** Choice — workflow status (Draft/In Progress/Completed/Closed/On Hold/Cancelled/Archived). */
  sprk_analysisstatus?: SprkAnalysisStatus | null;
  sprk_analysisstatusname?: string | null;

  /** Standard Dataverse state/status-reason fields. */
  statecode?: number | null;
  statuscode?: number | null;

  /** Lookup → `sprk_analysisplaybook` — the playbook driving this analysis. */
  _sprk_playbook_value?: string | null;
  '_sprk_playbook_value@OData.Community.Display.V1.FormattedValue'?: string;

  /** Lookup → `sprk_document` — saved output artifact, when the analysis produced one. */
  _sprk_outputfileid_value?: string | null;
  '_sprk_outputfileid_value@OData.Community.Display.V1.FormattedValue'?: string;

  // ---- Regarding field-set (ADR-024 dual-field pattern) ----------------------
  // Entity-specific lookups — exactly one is populated per Analysis.
  _sprk_regardingmatter_value?: string | null;
  _sprk_regardingproject_value?: string | null;
  /**
   * Regarding **context** lookup → `sprk_document` — distinct from
   * `sprk_documentid` above (the SPE subject-pointer). For a document-only
   * analysis both may point at the same record (owner-accepted intentional dup).
   */
  _sprk_regardingdocument_value?: string | null;
  _sprk_regardingbudget_value?: string | null;
  _sprk_regardingcommunication_value?: string | null;
  _sprk_regardinginvoice_value?: string | null;

  // Denormalized resolver fields (populated by PolymorphicResolverService, ADR-024).
  _sprk_regardingrecordtype_value?: string | null;
  '_sprk_regardingrecordtype_value@OData.Community.Display.V1.FormattedValue'?: string;
  sprk_regardingrecordid?: string | null;
  sprk_regardingrecordname?: string | null;
  sprk_regardingrecordnumber?: string | null;
}

/**
 * OData `$select` fields for a `sprk_analysis` retrieve.
 *
 * Use:
 *   await webApi.retrieveRecord("sprk_analysis", id, `?$select=${SPRK_ANALYSIS_SELECT}`)
 */
export const SPRK_ANALYSIS_SELECT = [
  // Identity & primary name
  'sprk_analysisid',
  'sprk_name',
  // Core detail
  'sprk_description',
  '_sprk_documentid_value',
  'sprk_sessionid',
  'sprk_worktype',
  'sprk_analysisstatus',
  'statecode',
  'statuscode',
  '_sprk_playbook_value',
  '_sprk_outputfileid_value',
  // Regarding — entity-specific lookups
  '_sprk_regardingmatter_value',
  '_sprk_regardingproject_value',
  '_sprk_regardingdocument_value',
  '_sprk_regardingbudget_value',
  '_sprk_regardingcommunication_value',
  '_sprk_regardinginvoice_value',
  // Regarding — resolver fields
  '_sprk_regardingrecordtype_value',
  'sprk_regardingrecordid',
  'sprk_regardingrecordname',
  'sprk_regardingrecordnumber',
].join(',');

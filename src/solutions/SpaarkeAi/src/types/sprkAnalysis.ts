/**
 * sprkAnalysis.ts — Client `sprk_analysis` record type (net-new, task 011).
 *
 * The Analysis platform's hub grid (030), per-type creation wizard (040), and
 * reopen path (031) all need a typed Analysis record. No client `sprk_analysis`
 * type existed before this file (spec §11.1) — this is the first client model
 * of the record.
 *
 * Field shapes verified against `docs/data-model/field-mapping-reference.md`
 * "AI / Analysis Domain > Analysis (`sprk_analysis`)" (authoritative schema
 * doc) + the owner-verified additions in
 * `projects/ai-advanced-capabilities-analysis-hub-r1/notes/schema-prerequisites.md`
 * "VERIFIED PRESENT" section (Dataverse MCP `describe`, 2026-07-28).
 *
 * Convention: mirrors the WebApi-retrieve shape used elsewhere in this
 * client/shared-lib (see `ITodoRecord` in
 * `Spaarke.UI.Components/src/components/TodoDetail/types.ts` and
 * `ICommunicationRecord` in `code-pages/CommunicationPage/src/types/communication.ts`)
 * — Lookup columns are modeled as the underscore-prefixed `_field_value`
 * OData annotation key (+ optional formatted-value companion), Choice columns
 * are modeled as `number | null` with a paired enum, and system fields
 * (`statecode`/`statuscode`) are included per that same convention.
 *
 * Scope (per task 011 constraints):
 *   - Models ONLY the owner-created columns verified in task 010
 *     (`sprk_worktype`, the regarding field-set, `sprk_description`) plus the
 *     pre-existing "Core" columns named in the task brief
 *     (`sprk_analysisid`, `sprk_name`, `sprk_documentid`, `sprk_sessionid`,
 *     `sprk_analysisstatus`, `sprk_playbook`, `sprk_outputfileid`).
 *   - Does NOT model the AI-execution-internal columns (`sprk_workingdocument`,
 *     `sprk_chathistory` [being retired — project CLAUDE.md], `sprk_finaloutput`,
 *     `sprk_errormessage`, `sprk_inputtokens`/`sprk_outputtokens`,
 *     `sprk_startedon`/`sprk_completedon`, `sprk_actionid`) — those belong to a
 *     future task that actually wires analysis execution, not the hub/wizard
 *     data-spine surface this task defines. Extend this file when that need
 *     is concrete (CLAUDE.md §11 — avoid speculative fields).
 *   - Does NOT add SPE pointer fields (`speDriveItemId`/`GraphDriveId`/
 *     `GraphItemId`) — files are reached via `sprk_documentid` → `sprk_document`
 *     (ADR-007). `sprk_documentid` is the SPE *subject-pointer* — a distinct
 *     role from `sprk_regardingdocument` (regarding/context lookup); for a
 *     document-only analysis both may point at the same `sprk_document`
 *     record (owner-accepted intentional duplication, see
 *     schema-prerequisites.md).
 *
 * @see ADR-012 — Shared Component Library (placement: no existing `@spaarke/*`
 *      `sprk_analysis` type to extend, so this lives at the SpaarkeAi client's
 *      own type location — first `types/` folder in this solution, precedented
 *      by the sibling `code-pages/CommunicationPage/src/types/` convention)
 * @see ADR-024 — Polymorphic Resolver Pattern (the regarding field-set below)
 * @see ADR-007 — SPE file access via the `sprk_documentid` → `sprk_document` hop
 */

// ---------------------------------------------------------------------------
// sprk_worktype — Choice (verified via Dataverse MCP describe, 2026-07-28)
// ---------------------------------------------------------------------------

/**
 * `sprk_worktype` option-set VALUES — the integer values Dataverse stores.
 * This is the enum the raw WebApi-retrieved field is typed against.
 */
export enum SprkAnalysisWorkType {
  AgreementAnalysis = 100000000,
  LegalResearch = 100000001,
  PatentApplication = 100000002,
}

/**
 * Semantic kebab-id mapping for `sprk_worktype` — the closed union consumers
 * (hub cards, wizard branching) key off of. Agreement Review ships **live**
 * this project; Legal Research + Patent Application render "coming soon"
 * (spec FR-10).
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
// sprk_analysisstatus — Choice (docs/data-model/field-mapping-reference.md)
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
// sprk_analysis record shape (WebApi retrieve result)
// ---------------------------------------------------------------------------

/**
 * A `sprk_analysis` record as returned by Web API `retrieveRecord` /
 * `retrieveMultipleRecords`. Lookup columns use the underscore-prefixed
 * `_field_value` OData annotation (+ optional formatted-value companion);
 * Choice columns are the raw numeric option value.
 *
 * Context-agnostic — no Xrm, no PCF APIs (ADR-012).
 */
export interface ISprkAnalysisRecord {
  // ---- Primary identity -----------------------------------------------------
  sprk_analysisid: string;
  sprk_name: string;

  // ---- Core detail ------------------------------------------------------------
  /** Multiline text — owner-added 2026-07-28 (schema-prerequisites.md). */
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
  // Entity-specific lookups — exactly one is populated per Analysis. The three
  // owner-added lookups (matter/project/document) plus the pre-existing
  // budget/communication/invoice lookups confirmed present on sprk_analysis
  // (schema-prerequisites.md "VERIFIED PRESENT", 2026-07-28).
  _sprk_regardingmatter_value?: string | null;
  _sprk_regardingproject_value?: string | null;
  /**
   * Regarding **context** lookup → `sprk_document` — distinct from
   * `sprk_documentid` above (the SPE subject-pointer). For a document-only
   * analysis both may point at the same record (owner-accepted intentional
   * duplication — schema-prerequisites.md).
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

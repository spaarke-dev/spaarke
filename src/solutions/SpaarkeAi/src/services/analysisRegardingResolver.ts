/**
 * analysisRegardingResolver.ts — consumes the RegardingResolver field-set (ADR-024 dual-field
 * pattern) already written on `sprk_analysis` records to resolve the single populated regarding
 * parent (Matter / Project / Document) and read `sprk_worktype` (task 012).
 *
 * This module does NOT write the resolver fields — that write path is owned exclusively by the
 * shared `PolymorphicResolverService.applyResolverFields()` (consumed via the RegardingResolver
 * PCF / wizard services elsewhere in the platform, per ADR-024 "MUST use the shared
 * PolymorphicResolverService for all client-side programmatic record creation"). This module is
 * the READ-side consumer: given an already-retrieved `ISprkAnalysisRecord`, it enumerates the
 * three Analysis-scoped entity-specific lookups (`sprk_regardingmatter` / `sprk_regardingproject` /
 * `sprk_regardingdocument`), enforces the single-valued invariant (NFR-05 / ADR-024 "MUST NOT
 * allow multiple entity-specific lookups to be populated simultaneously"), and surfaces the
 * resolved parent (or an unresolved/invalid flag) plus `sprk_worktype` for downstream consumers
 * (the hub grid, the per-type wizard, task 041's `activeWorkType` → `getToolsForSurface` scoping,
 * and task 051's regarding subgrids).
 *
 * Scope note (constraint from task 012 POML): only the three Analysis-scoped regarding lookups
 * participate in the single-valued count. `sprk_analysis` also carries pre-existing
 * `sprk_regardingbudget` / `sprk_regardingcommunication` / `sprk_regardinginvoice` lookups (part
 * of the generic ADR-024 catalog for OTHER entities) — those are out of scope for Analysis and are
 * intentionally excluded here. `sprk_documentid` (the ADR-007 SPE subject-pointer) is a SEPARATE
 * role from `sprk_regardingdocument` and is likewise excluded from the count.
 *
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md — the dual-field pattern this consumes
 * @see src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts — the
 *      shared WRITE-side service (`cleanGuid` is reused here for GUID normalization on read)
 * @see src/client/pcf/RegardingResolver/RegardingResolver/handlers/ResolverWriteHandler.ts —
 *      canonical write-side consumer of the shared service
 * @see ../types/sprkAnalysis.ts — the client `sprk_analysis` record type (task 011)
 */

import { cleanGuid } from '@spaarke/ui-components';

import {
  ANALYSIS_WORK_TYPE_ID_BY_VALUE,
  type AnalysisWorkTypeId,
  type ISprkAnalysisRecord,
  type SprkAnalysisWorkType,
} from '../types/sprkAnalysis';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** The three Analysis-scoped regarding parent entity types (task 012 constraint). */
export type AnalysisRegardingEntityType = 'sprk_matter' | 'sprk_project' | 'sprk_document';

interface IAnalysisRegardingFieldEntry {
  entityType: AnalysisRegardingEntityType;
  /** The `_sprk_regarding{type}_value` OData-annotated lookup key on `ISprkAnalysisRecord`. */
  field: keyof ISprkAnalysisRecord;
}

/**
 * The Analysis-scoped regarding field-set (task 012 SCOPE constraint) — deliberately excludes the
 * pre-existing `sprk_regardingbudget` / `sprk_regardingcommunication` / `sprk_regardinginvoice`
 * lookups (part of the generic ADR-024 catalog for other entities, not Analysis) and
 * `sprk_documentid` (ADR-007 SPE subject-pointer — a separate role).
 */
const ANALYSIS_REGARDING_FIELDS: readonly IAnalysisRegardingFieldEntry[] = [
  { entityType: 'sprk_matter', field: '_sprk_regardingmatter_value' },
  { entityType: 'sprk_project', field: '_sprk_regardingproject_value' },
  { entityType: 'sprk_document', field: '_sprk_regardingdocument_value' },
];

/** `sprk_worktype` read alongside regarding resolution (task 012 step 3). */
export interface IAnalysisWorkTypeInfo {
  /** Raw `sprk_worktype` option-set integer value, or `null` when unset. */
  workType: SprkAnalysisWorkType | null;
  /** Semantic kebab-id mapping of `workType` (see `sprkAnalysis.ts`), or `null` when unset/unknown. */
  workTypeId: AnalysisWorkTypeId | null;
}

/** Exactly one regarding field was populated — resolved to its parent. */
export interface IAnalysisRegardingResolved extends IAnalysisWorkTypeInfo {
  status: 'resolved';
  entityType: AnalysisRegardingEntityType;
  /** Normalized (brace-stripped, lowercased) parent record GUID. */
  recordId: string;
  /** Denormalized `sprk_regardingrecordname`, as written by the resolver. */
  recordName: string | null;
  /** Denormalized `sprk_regardingrecordnumber`, as written by the resolver. */
  recordNumber: string | null;
}

/** Zero regarding fields populated — unresolvable (no fabricated parent). */
export interface IAnalysisRegardingUnresolved extends IAnalysisWorkTypeInfo {
  status: 'unresolved';
  reason: string;
}

/** More than one regarding field populated — invalid (ADR-024 single-valued invariant violated). */
export interface IAnalysisRegardingInvalid extends IAnalysisWorkTypeInfo {
  status: 'invalid';
  reason: string;
  /** Every populated entity type found (always length >= 2). */
  populatedEntityTypes: AnalysisRegardingEntityType[];
}

export type AnalysisRegardingResolution =
  | IAnalysisRegardingResolved
  | IAnalysisRegardingUnresolved
  | IAnalysisRegardingInvalid;

// ---------------------------------------------------------------------------
// Internals
// ---------------------------------------------------------------------------

function isPopulated(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function readWorkTypeInfo(record: ISprkAnalysisRecord): IAnalysisWorkTypeInfo {
  const workType = record.sprk_worktype ?? null;
  const workTypeId = workType != null ? (ANALYSIS_WORK_TYPE_ID_BY_VALUE[workType] ?? null) : null;
  return { workType, workTypeId };
}

// ---------------------------------------------------------------------------
// Public: resolve
// ---------------------------------------------------------------------------

/**
 * Resolve a `sprk_analysis` record's single-valued regarding parent (Matter/Project/Document) and
 * read `sprk_worktype`, per the RegardingResolver field-set pattern (ADR-024).
 *
 * - Exactly ONE of `sprk_regardingmatter` / `sprk_regardingproject` / `sprk_regardingdocument`
 *   populated → `status: 'resolved'` with the parent's entity type, GUID, and the denormalized
 *   `sprk_regardingrecordname` / `sprk_regardingrecordnumber` resolver fields.
 * - ZERO populated → `status: 'unresolved'` (rejected/flagged as unresolvable; never fabricates a
 *   parent).
 * - MORE THAN ONE populated → `status: 'invalid'` (rejected/flagged; never silently picks one).
 *
 * `sprk_worktype` is always read and returned regardless of resolution status, since downstream
 * consumers (task 041 tool scoping) need it independent of whether the regarding chain resolved.
 */
export function resolveAnalysisRegarding(record: ISprkAnalysisRecord): AnalysisRegardingResolution {
  const workTypeInfo = readWorkTypeInfo(record);

  const populated = ANALYSIS_REGARDING_FIELDS.filter(entry => isPopulated(record[entry.field]));

  if (populated.length === 0) {
    return {
      status: 'unresolved',
      reason:
        '[analysisRegardingResolver] No populated regarding field (sprk_regardingmatter / ' +
        'sprk_regardingproject / sprk_regardingdocument) — Analysis has no resolvable parent.',
      ...workTypeInfo,
    };
  }

  if (populated.length > 1) {
    const populatedEntityTypes = populated.map(entry => entry.entityType);
    return {
      status: 'invalid',
      reason:
        `[analysisRegardingResolver] Multiple regarding fields populated (${populatedEntityTypes.join(', ')}) — ` +
        'exactly one is required (ADR-024 single-valued invariant).',
      populatedEntityTypes,
      ...workTypeInfo,
    };
  }

  const only = populated[0];
  const rawId = record[only.field] as string;

  return {
    status: 'resolved',
    entityType: only.entityType,
    recordId: cleanGuid(rawId),
    recordName: record.sprk_regardingrecordname ?? null,
    recordNumber: record.sprk_regardingrecordnumber ?? null,
    ...workTypeInfo,
  };
}

import { apiClient, ApiClientError } from '@shared/services';
import {
  derivePrimaryReview,
  type PrimaryReviewModel,
  type PrimaryCandidate,
  type ProvenanceDoc,
} from '@spaarke/communication-components/logic/connections/provenance';
import type { EntitySearchResult, EntityType } from '../hooks/useEntitySearch';

/**
 * communicationSuggestionsService.ts
 *
 * FR-B2: pre-select the Association Engine's PREDICTED record when the Outlook
 * add-in "Save to Spaarke" picker (and the ribbon quick-save) opens.
 *
 * The candidate model MUST match the code-page review surface exactly, so this
 * service reuses the SHARED `derivePrimaryReview` (ADR-045 — no forked candidate
 * model). It does NOT recompute engine decisions client-side; it reconstructs a
 * `ProvenanceDoc` from the BFF's read-only engine projection and hands it to the
 * same function the reading pane uses.
 *
 * Flow:
 *   1. GET /api/office/communications/by-message-id/{internetMessageId}/suggestions
 *      → 200 { communicationId, subject, suggestions, names, filingAccess } when the email is captured
 *        (`filingAccess`, task 084: per-candidate "can the caller file here", keyed by targetId),
 *      → 404 when it is not yet captured (email just arrived / never filed).
 *   2. On 404 (or no usable candidate) → return null → the caller opens the picker
 *      with NO pre-selection and requires an explicit choice (FR-B2 fallback; never
 *      auto-file a guessed record — spec ADR-015 spirit / task escalation trigger).
 *
 * @see src/server/api/Sprk.Bff.Api/Api/Office/CommunicationsEndpoints.cs (GetSuggestionsByMessageIdAsync)
 * @see src/client/shared/Spaarke.Communication.Components/src/logic/connections/provenance.ts (derivePrimaryReview)
 */

/** One rung's contribution to a suggested candidate (wire shape — camelCase). */
interface SuggestedContributorWire {
  rung: string;
  confidence: number;
  provenance: string;
}

/** A reinforced per-field candidate target from the engine (wire shape — camelCase). */
interface SuggestedCandidateWire {
  field: string;
  targetEntity: string;
  targetId: string;
  reinforcedConfidence: number;
  deterministicConfidence: number;
  written: boolean;
  conflict: boolean;
  contributors: SuggestedContributorWire[];
}

/** The engine's read-only suggestion projection (mirrors BFF SuggestAssociationsResponse). */
interface SuggestAssociationsWire {
  communicationId: string;
  status: string;
  autoFileEligible: boolean;
  candidates: SuggestedCandidateWire[];
  // signals omitted — derivePrimaryReview does not consume them.
}

/** BFF response for the by-message-id/{id}/suggestions endpoint. */
interface CommunicationSuggestionsWire {
  communicationId: string;
  subject: string;
  suggestions: SuggestAssociationsWire;
  /** Server-resolved display names keyed by candidate targetId (the provenance stores only ids). */
  names?: Record<string, string>;
  /**
   * Task 084 (#1037): whether the caller can FILE to each candidate, keyed by candidate targetId (same
   * shape as `names`), decided by the save's own evaluator. `false` → the save would refuse it; `true`
   * or an absent key → selectable. Not part of the candidate model: it is applied AFTER the shared
   * `derivePrimaryReview` (ADR-045 — ranking and the model are untouched).
   */
  filingAccess?: Record<string, boolean>;
}

/** The engine's shared review model, plus the per-candidate filing access the suggestions route returned. */
interface SuggestionsModel {
  model: PrimaryReviewModel;
  filingAccess?: Record<string, boolean>;
}

/**
 * The pre-selection result: the engine's predicted record mapped to the picker's
 * `EntitySearchResult`, plus the alternates (also mapped) for surfacing.
 */
export interface EnginePreSelection {
  /** The predicted record to pre-select in the picker. */
  predicted: EntitySearchResult;
  /** Alternate candidates (highest-confidence first), excluding the predicted record. */
  alternates: EntitySearchResult[];
  /** The underlying shared review model (state + candidates), for callers that need it. */
  model: PrimaryReviewModel;
}

/**
 * An auto-matched "Related to" candidate for the reconciliation-style cards: the
 * picker's `EntitySearchResult` plus the engine's confidence (the "% match") and the
 * human match reason. Ranked highest-confidence first by the shared model.
 */
export interface RelatedCandidate extends EntitySearchResult {
  /** 0-1 reinforced confidence → rendered as "%". */
  confidence: number;
  /** Human match reason (e.g. "sender on matter team"), when the engine provides one. */
  matchReason?: string;
}

/**
 * Dataverse logical name → the add-in picker's EntityType, for the engine candidates the add-in can
 * FILE to. A logical name not listed here maps to null (no card, no ribbon prediction).
 *
 * Exported for task 084's #1075 guard: the ribbon quick-save sends `EntitySearchResult.entityType` (a
 * value from this map) as the save's `targetEntity.entityType`, so every value here must be a name the
 * save accepts (`OfficeEndpoints.ValidateSaveRequest`). `quickSaveHelpers.test.ts` pins that.
 *
 * `account` is deliberately NOT listed (task 084): the save stopped accepting "account" on 2026-09-04
 * (unified-access-control-r2 — `sprk_document` has no account lookup), so an Account prediction could
 * only produce a ribbon quick-save refused with 400 OFFICE_002. Unmapped, it now gets the same "no
 * prediction → open the pane" path as any other type the add-in cannot file to. The Save picker never
 * offered Account cards (its chips are Matter/Project/Invoice), so the pane is unchanged.
 */
export const LOGICAL_TO_ENTITY_TYPE: Readonly<Record<string, EntityType>> = {
  sprk_matter: 'Matter',
  sprk_project: 'Project',
  sprk_invoice: 'Invoice',
  contact: 'Contact',
};

/**
 * Build the endpoint URL. The internetMessageId is URL-encoded to survive the
 * `<` / `>` / `@` characters RFC-5322 message ids commonly contain.
 */
function buildEndpoint(internetMessageId: string): string {
  return `/api/office/communications/by-message-id/${encodeURIComponent(internetMessageId)}/suggestions`;
}

/**
 * Reconstruct a `ProvenanceDoc` from the BFF's engine projection so it can be fed
 * to the SHARED `derivePrimaryReview` verbatim (no client-side recompute; ADR-045).
 * Only the fields `derivePrimaryReview` consumes are populated.
 */
function toProvenanceDoc(suggestions: SuggestAssociationsWire, names?: Record<string, string>): ProvenanceDoc {
  return {
    version: 1,
    direction: '',
    decision: {
      status: suggestions.status,
      autoFiled: suggestions.autoFileEligible,
      killSwitchEnabled: false,
      autoFileThreshold: 0.85,
      topDeterministicConfidence: 0,
      topConfidence: 0,
      aiInvolved: false,
      reason: '',
    },
    rungsFired: [],
    candidates: suggestions.candidates.map(c => ({
      field: c.field,
      targetEntity: c.targetEntity,
      targetId: c.targetId,
      // Server-resolved display name (the provenance stores only ids); when absent, the
      // shared model falls back to the id — identical to the code page's un-resolved case.
      ...(names?.[c.targetId] ? { targetName: names[c.targetId] } : {}),
      reinforcedConfidence: c.reinforcedConfidence,
      deterministicConfidence: c.deterministicConfidence,
      written: c.written,
      conflict: c.conflict,
      contributors: c.contributors.map(k => ({
        rung: k.rung,
        confidence: k.confidence,
        provenance: k.provenance,
      })),
    })),
    signals: [],
  };
}

/**
 * Map a shared `PrimaryCandidate` to the picker's `EntitySearchResult`. Returns
 * `null` when the predicted entity type is not one the add-in picker supports (the
 * engine can predict types — organization, service request, event, account — that
 * `LOGICAL_TO_ENTITY_TYPE` does not map; those simply get no pre-selection rather than a
 * broken chip).
 *
 * Task 084: `canFile` is set from the route's `filingAccess` map ONLY when it has a key for this
 * candidate's targetId — an absent key leaves `canFile` absent (selectable, as before).
 */
function candidateToEntity(
  candidate: PrimaryCandidate,
  filingAccess?: Record<string, boolean>
): EntitySearchResult | null {
  const entityType = LOGICAL_TO_ENTITY_TYPE[candidate.entity];
  if (!entityType) return null;
  // Prefer the record number (e.g. matter number) then the human match reason.
  const displayInfo = candidate.recordNumber ?? candidate.matchReason;
  const canFile = filingAccess?.[candidate.targetId];
  return {
    id: candidate.targetId,
    entityType,
    logicalName: candidate.entity,
    name: candidate.targetName,
    ...(displayInfo ? { displayInfo } : {}),
    ...(canFile !== undefined ? { canFile } : {}),
  };
}

/** Map a shared `PrimaryCandidate` to a `RelatedCandidate` (entity + confidence), or null for unsupported types. */
function candidateToRelated(
  candidate: PrimaryCandidate,
  filingAccess?: Record<string, boolean>
): RelatedCandidate | null {
  const entity = candidateToEntity(candidate, filingAccess);
  if (!entity) return null;
  return {
    ...entity,
    confidence: candidate.confidence,
    ...(candidate.matchReason ? { matchReason: candidate.matchReason } : {}),
  };
}

/**
 * Fetch the engine's suggestion for an email and reduce it — via the SHARED
 * `derivePrimaryReview` — to a pre-selection for the picker.
 *
 * @param internetMessageId The open email's RFC-5322 message id
 *   (`Office.context.mailbox.item.internetMessageId`). Falsy → returns null
 *   without a network call.
 * @returns The predicted record + alternates, or `null` when the email is not
 *   captured (404), the engine has no usable candidate, or the top candidate is a
 *   type the picker can't represent. `null` ⇒ caller opens the picker with no
 *   pre-selection (FR-B2 fallback — never auto-file a guess).
 * @throws {ApiClientError} for any non-404 BFF error (5xx, network, 401). Callers
 *   should treat a throw as "no pre-selection" (best-effort — a failed prediction
 *   must never block the manual save flow).
 */
async function fetchModel(internetMessageId: string | undefined): Promise<SuggestionsModel | null> {
  if (!internetMessageId) return null;
  const trimmed = internetMessageId.trim();
  if (trimmed.length === 0) return null;

  let response: CommunicationSuggestionsWire;
  try {
    response = await apiClient.get<CommunicationSuggestionsWire>(buildEndpoint(trimmed));
  } catch (err) {
    // 404 = "not captured yet" → no suggestions (the FR-B2 fallback path).
    if (err instanceof ApiClientError && err.error.status === 404) {
      return null;
    }
    throw err;
  }
  if (!response?.suggestions) return null;

  // SAME candidate model as the code page (no fork; ADR-045). Server-resolved display
  // names are folded into the model's `targetName` (the field it is designed to receive). Filing access
  // (task 084) is NOT folded in — it rides alongside and is applied when mapping to the picker's shape.
  const model = derivePrimaryReview(JSON.stringify(toProvenanceDoc(response.suggestions, response.names)), null, []);
  return { model, ...(response.filingAccess ? { filingAccess: response.filingAccess } : {}) };
}

export async function fetchEnginePreSelection(
  internetMessageId: string | undefined
): Promise<EnginePreSelection | null> {
  const fetched = await fetchModel(internetMessageId);
  if (!fetched) return null;
  const { model, filingAccess } = fetched;

  const predictedCandidate = model.primary ?? model.candidates[0];
  if (!predictedCandidate) return null;

  // `predicted.canFile === false` tells the ribbon quick-save NOT to auto-file (task 084).
  const predicted = candidateToEntity(predictedCandidate, filingAccess);
  if (!predicted) return null;

  const alternates = model.candidates
    .filter(c => !(c.entity === predictedCandidate.entity && c.targetId === predictedCandidate.targetId))
    .map(c => candidateToEntity(c, filingAccess))
    .filter((e): e is EntitySearchResult => e !== null);

  return { predicted, alternates, model };
}

/**
 * Fetch the engine's ranked "Related to" candidates (highest confidence first),
 * mapped to {@link RelatedCandidate} for the reconciliation-style auto-match cards.
 * Unsupported candidate types are dropped. Returns `[]` when the email is not
 * captured / has no suggestions (the caller falls back to search). Best-effort — a
 * throw should be treated as "no candidates" by the caller.
 */
export async function fetchRelatedCandidates(internetMessageId: string | undefined): Promise<RelatedCandidate[]> {
  const fetched = await fetchModel(internetMessageId);
  if (!fetched) return [];
  const { model, filingAccess } = fetched;
  return model.candidates
    .map(c => candidateToRelated(c, filingAccess))
    .filter((c): c is RelatedCandidate => c !== null);
}

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
 *      → 200 { communicationId, subject, suggestions } when the email is captured,
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

/** Dataverse logical name → the add-in picker's EntityType (only the 5 the picker supports). */
const LOGICAL_TO_ENTITY_TYPE: Record<string, EntityType> = {
  sprk_matter: 'Matter',
  sprk_project: 'Project',
  sprk_invoice: 'Invoice',
  account: 'Account',
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
function toProvenanceDoc(suggestions: SuggestAssociationsWire): ProvenanceDoc {
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
 * engine can predict types — organization, service request, event — the picker's
 * 5-type model can't represent; those simply get no pre-selection rather than a
 * broken chip).
 */
function candidateToEntity(candidate: PrimaryCandidate): EntitySearchResult | null {
  const entityType = LOGICAL_TO_ENTITY_TYPE[candidate.entity];
  if (!entityType) return null;
  // Prefer the record number (e.g. matter number) then the human match reason.
  const displayInfo = candidate.recordNumber ?? candidate.matchReason;
  return {
    id: candidate.targetId,
    entityType,
    logicalName: candidate.entity,
    name: candidate.targetName,
    ...(displayInfo ? { displayInfo } : {}),
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
export async function fetchEnginePreSelection(
  internetMessageId: string | undefined
): Promise<EnginePreSelection | null> {
  if (!internetMessageId) return null;
  const trimmed = internetMessageId.trim();
  if (trimmed.length === 0) return null;

  let response: CommunicationSuggestionsWire;
  try {
    response = await apiClient.get<CommunicationSuggestionsWire>(buildEndpoint(trimmed));
  } catch (err) {
    // 404 = "not captured yet" → no pre-selection (the FR-B2 fallback path).
    if (err instanceof ApiClientError && err.error.status === 404) {
      return null;
    }
    throw err;
  }

  if (!response?.suggestions) return null;

  // SAME candidate model as the code page (no fork; ADR-045).
  const model = derivePrimaryReview(JSON.stringify(toProvenanceDoc(response.suggestions)), null, []);
  const predictedCandidate = model.primary ?? model.candidates[0];
  if (!predictedCandidate) return null;

  const predicted = candidateToEntity(predictedCandidate);
  if (!predicted) return null;

  const alternates = model.candidates
    .filter(c => !(c.entity === predictedCandidate.entity && c.targetId === predictedCandidate.targetId))
    .map(candidateToEntity)
    .filter((e): e is EntitySearchResult => e !== null);

  return { predicted, alternates, model };
}

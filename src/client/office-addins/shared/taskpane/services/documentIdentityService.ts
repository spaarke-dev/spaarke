import { apiClient, ApiClientError } from '@shared/services';
import { cleanGuid } from '../utils/cleanGuid';

/**
 * documentIdentityService.ts
 *
 * Client-side service for FR-01 (spaarkeai-word-add-in-r1 task 013): resolves the open Word
 * document's URL to the `sprk_document` it came from, via task 012's server-side resolver.
 *
 * Underlying BFF endpoint:
 *
 *   POST /api/documents/resolve-identity
 *   { "documentUrl": "<Office.context.document.url, sent exactly as returned>" }
 *
 * Full contract + rationale: `projects/spaarkeai-word-add-in-r1/notes/012-identity-resolver-decisions.md`.
 * `resolved: false` is a SUCCESSFUL answer, not an error — most `reason` values mean "this is a new
 * document, not a Spaarke one." The one exception (`identity_conflict`) and the indeterminate
 * outcomes (503, and a 403 whose `reasonCode` is `sdap.access.error.system_failure`) must NOT be
 * treated as "new" — doing so is how duplicate `sprk_document` rows get minted. This service
 * encodes all of that in {@link DocumentIdentityOutcome} so callers switch on `.kind` instead of
 * re-deriving the rule from HTTP status codes.
 */

/** The record a resolved document is associated with (mirrors the BFF's `RelatedRecordIdentity`). */
export interface ResolvedRelatedRecord {
  /** Dataverse logical name, e.g. `sprk_matter`. */
  entityType: string;
  /** Record id, canonicalized bare-lowercase (ADR-044). */
  id: string;
  /** The record's primary name, when Dataverse supplied it. */
  name: string | null;
}

/**
 * Outcome of a document-identity resolution attempt. Every server response AND every failure mode
 * (network, 5xx, 401, unexpected 400) maps to exactly one member — `resolveDocumentIdentity` never
 * throws, so callers can `switch` on `.kind` without a try/catch.
 */
export type DocumentIdentityOutcome =
  | {
      /** The URL resolved to a `sprk_document` the caller may read. */
      kind: 'resolved';
      documentId: string;
      documentName: string;
      fileName: string;
      relatedRecord: ResolvedRelatedRecord | null;
    }
  | {
      /**
       * Not a Spaarke document — a normal, expected outcome. Covers `not_cloud_document` (a local
       * file), `not_resolvable` (Graph would not resolve the URL for this caller), and
       * `not_spaarke_document` (the file exists but no `sprk_document` tracks it).
       */
      kind: 'new';
      reason: 'not_cloud_document' | 'not_resolvable' | 'not_spaarke_document';
    }
  | {
      /**
       * A row already holds this file's item id under a different drive. NOT "new" — offering
       * save-as-new would collide with `sprk_graphitemid_uk`.
       */
      kind: 'conflict';
    }
  | {
      /**
       * Could not be determined (Graph/Dataverse outage, or an authorization check that failed
       * closed during a Dataverse outage). NEVER treat as "new".
       */
      kind: 'indeterminate';
      reason: 'unavailable' | 'system_failure';
    }
  | {
      /** The caller may reach the file but may not read the `sprk_document` record. */
      kind: 'denied';
    }
  | {
      /** An unexpected failure (network, 5xx, malformed request). */
      kind: 'error';
      message: string;
    };

/** @internal Response shape returned by the BFF resolver. */
interface BffRelatedRecord {
  entityType: string;
  id: string;
  name?: string | null;
}

/** @internal Response shape returned by the BFF resolver. */
interface BffDocumentIdentityResponse {
  resolved: boolean;
  documentId?: string | null;
  documentName?: string | null;
  fileName?: string | null;
  relatedRecord?: BffRelatedRecord | null;
  reason?: string | null;
}

/** @internal The one ProblemDetails extension this service reads off a 403 (not in the shared `ApiError` type). */
interface ForbiddenProblemDetails {
  reasonCode?: string;
}

/** `AuthorizationService.cs` denies with this code when Dataverse is down during the authorization check. */
const SYSTEM_FAILURE_REASON_CODE = 'sdap.access.error.system_failure';

const RESOLVE_IDENTITY_ENDPOINT = '/api/documents/resolve-identity';

function isAbsoluteUrl(url: string): boolean {
  try {
    // Constructing a URL is the standard-library way to validate absoluteness; a relative or
    // malformed string throws.
    new URL(url);
    return true;
  } catch {
    return false;
  }
}

function asNewReason(
  reason: string | null | undefined
): 'not_cloud_document' | 'not_resolvable' | 'not_spaarke_document' {
  if (reason === 'not_cloud_document' || reason === 'not_resolvable' || reason === 'not_spaarke_document') {
    return reason;
  }
  // An unrecognized reason still means "resolved: false" from a server that documents `reason` as
  // the only non-conflict values — fall back to the safest classification (new document) rather
  // than surface an error for a shape this client doesn't yet know about.
  return 'not_resolvable';
}

/**
 * Resolve an open Word document's URL to the `sprk_document` it came from.
 *
 * @param documentUrl `Office.context.document.url`, sent EXACTLY as returned — no client-side
 * reshaping (Spike-1 verified Word web and Word desktop return byte-identical raw-space paths that
 * resolve). When empty, whitespace, or not an absolute URL (an unsaved document, per task 012's
 * notes §2 rule 3), resolves to `{ kind: 'new', reason: 'not_cloud_document' }` WITHOUT a network
 * call — the server would answer 400 `document_url_required` for the same input.
 */
export async function resolveDocumentIdentity(
  documentUrl: string | null | undefined
): Promise<DocumentIdentityOutcome> {
  const trimmed = documentUrl?.trim();
  if (!trimmed || !isAbsoluteUrl(trimmed)) {
    return { kind: 'new', reason: 'not_cloud_document' };
  }

  try {
    const response = await apiClient.post<BffDocumentIdentityResponse>(RESOLVE_IDENTITY_ENDPOINT, {
      documentUrl: trimmed,
    });

    if (response.resolved) {
      return {
        kind: 'resolved',
        documentId: cleanGuid(response.documentId),
        documentName: response.documentName ?? '',
        fileName: response.fileName ?? '',
        relatedRecord: response.relatedRecord
          ? {
              entityType: response.relatedRecord.entityType,
              id: cleanGuid(response.relatedRecord.id),
              name: response.relatedRecord.name ?? null,
            }
          : null,
      };
    }

    if (response.reason === 'identity_conflict') {
      return { kind: 'conflict' };
    }

    return { kind: 'new', reason: asNewReason(response.reason) };
  } catch (err) {
    if (err instanceof ApiClientError) {
      const status = err.error.status;

      if (status === 503) {
        return { kind: 'indeterminate', reason: 'unavailable' };
      }

      if (status === 403) {
        const reasonCode = (err.error as ForbiddenProblemDetails).reasonCode;
        return reasonCode === SYSTEM_FAILURE_REASON_CODE
          ? { kind: 'indeterminate', reason: 'system_failure' }
          : { kind: 'denied' };
      }

      return { kind: 'error', message: err.message };
    }

    return {
      kind: 'error',
      message: err instanceof Error ? err.message : 'Document identity resolution failed.',
    };
  }
}

/**
 * The identity-related subset of `App.savedContext` this service knows how to populate (task 013
 * step 6). `App`'s actual saved-context type is a wider superset (it also carries the Create-To-Do
 * "filed to" fields from `SaveView.onSaved`) — this is the slice `applyDocumentIdentityOutcome`
 * reads and writes.
 */
export interface DocumentIdentityContext {
  /** `sprk_documentid`, bare lowercase (ADR-044). */
  documentId?: string;
  /** `sprk_documentname`. */
  documentName?: string;
  /** `sprk_filename`. */
  fileName?: string;
  /** The record the resolved document belongs to, or `null` when unassociated. */
  relatedRecord?: ResolvedRelatedRecord | null;
  /** Create-To-Do "regarding" fields (see `SavedTodoContext` in `CreateTodoView.tsx`). */
  regardingEntity?: string;
  regardingRecordId?: string;
  regardingName?: string;
}

/**
 * Apply a {@link DocumentIdentityOutcome} to the pane's saved-context state.
 *
 * Merges document identity fields into whatever state already existed — it does NOT replace it
 * (task 013 step 6: "joining the existing save-context shape rather than adding a parallel store").
 * When the resolved document has a related record, it ALSO seeds the same Create-To-Do "regarding"
 * fields `SaveView.onSaved` writes (`toFriendlyRegardingType` mirrors `App.tsx`'s own logical→
 * friendly mapping so a resolved Word document's matter/project/invoice can drive Create To Do too).
 *
 * Every non-`'resolved'` outcome ('new' | 'conflict' | 'indeterminate' | 'denied' | 'error') returns
 * `prev` **by reference, unchanged** — none of them carry a document to display, and returning the
 * same reference lets a React `setState` updater bail out of a re-render for a no-op update.
 *
 * A pure function (no Office.js, no network, no React) so this merge is independently unit-testable
 * — `App.tsx` has no render-based test harness in this codebase (no `App.test.tsx` exists), so this
 * is the only automated proof of the merge behavior acceptance criteria 6-8 depend on.
 */
export function applyDocumentIdentityOutcome(
  prev: DocumentIdentityContext | undefined,
  outcome: DocumentIdentityOutcome,
  toFriendlyRegardingType: (entityType: string) => string
): DocumentIdentityContext | undefined {
  if (outcome.kind !== 'resolved') {
    return prev;
  }

  return {
    ...prev,
    documentId: outcome.documentId,
    documentName: outcome.documentName,
    fileName: outcome.fileName,
    relatedRecord: outcome.relatedRecord,
    ...(outcome.relatedRecord
      ? {
          regardingEntity: toFriendlyRegardingType(outcome.relatedRecord.entityType),
          regardingRecordId: outcome.relatedRecord.id,
          ...(outcome.relatedRecord.name ? { regardingName: outcome.relatedRecord.name } : {}),
        }
      : {}),
  };
}

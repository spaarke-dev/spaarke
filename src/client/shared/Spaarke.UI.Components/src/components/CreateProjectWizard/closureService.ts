/**
 * closureService.ts
 * BFF API client for Secure Project closure.
 *
 * Calls POST /api/v1/external-access/close-project to orchestrate:
 *   - Deactivation of all sprk_externalrecordaccess records for the project
 *   - Removal of all external members from the SPE container
 *   - Invalidation of Redis participation cache entries
 *
 * Dependencies are injected as parameters (no solution-specific imports):
 *   - authenticatedFetch: MSAL-backed fetch function
 *   - bffBaseUrl: BFF API base URL
 *
 * Returns result object — never throws.
 */

import type { AuthenticatedFetchFn } from '../../utils/fetchTypes';
import { problemOf } from '../../utils/thrownFetchError';

/**
 * #2103 follow-up: the closure's NON-retryable leaf. A revoked organization has more active members than one closure
 * may sweep, so their container permissions were not removed and a retry meets the same bound; the server's `detail`
 * says what an administrator must do instead. MUST match `ProjectClosureEndpoint.ClosureOrganizationTooLargeReason`.
 */
export const CLOSURE_ORGANIZATION_TOO_LARGE_REASON_CODE = 'sdap.closure.incomplete.organization_too_large';

/**
 * #2104 round 2: the closure's other NON-retryable leaf. The project has more grant rows than one closure reads (active
 * ones: nothing was revoked; revoked ones: their container permissions could not be confirmed removed), and a retry
 * reads the same rows. MUST match `ProjectClosureEndpoint.ClosureTooManyGrantsReason`.
 */
export const CLOSURE_TOO_MANY_GRANTS_REASON_CODE = 'sdap.closure.incomplete.too_many_grants';

/**
 * #2104 round 3: the closure's third NON-retryable leaf. A revoked grantee matches more than one permission on the
 * project's container, or a permission whose holder cannot be verified, so nothing was removed for them; a retry reads
 * the same permissions and refuses again. The server's `detail` tells an administrator to resolve the container's
 * permissions in the SPE admin app. MUST match `ProjectClosureEndpoint.ClosurePermissionAmbiguousReason`.
 */
export const CLOSURE_PERMISSION_AMBIGUOUS_REASON_CODE = 'sdap.closure.incomplete.permission_ambiguous';

/**
 * #2104 round 2: the grants WERE revoked, but some grantee's file access on the project's container could not be
 * confirmed removed. Retryable. MUST match `ProjectClosureEndpoint.ClosureContainerNotClearedReason`.
 */
export const CLOSURE_CONTAINER_NOT_CLEARED_REASON_CODE = 'sdap.closure.incomplete.container_not_cleared';

/**
 * #2104 round 2: some grants were revoked and some could not be deactivated (they remain active). Retryable. MUST match
 * `ProjectClosureEndpoint.ClosurePartialRevocationReason`.
 */
export const CLOSURE_PARTIAL_REVOCATION_REASON_CODE = 'sdap.closure.incomplete.partial_revocation';

/**
 * #2104 round 2: the active grants could not be enumerated, so none was revoked. Retryable. MUST match
 * `ProjectClosureEndpoint.ClosureEnumerationFailedReason`.
 */
export const CLOSURE_ENUMERATION_FAILED_REASON_CODE = 'sdap.closure.incomplete.enumeration_failed';

/** The reason codes a retry cannot fix: the dialog shows them as "incomplete", with no "Try Again". */
export const NON_RETRYABLE_CLOSURE_REASON_CODES: readonly string[] = [
  CLOSURE_ORGANIZATION_TOO_LARGE_REASON_CODE,
  CLOSURE_TOO_MANY_GRANTS_REASON_CODE,
  CLOSURE_PERMISSION_AMBIGUOUS_REASON_CODE,
];

// ---------------------------------------------------------------------------
// Request / Response types (mirror BFF Dtos)
// ---------------------------------------------------------------------------

export interface ICloseProjectRequest {
  /** The Dataverse Project (sprk_project) ID to close. */
  projectId: string;
  /**
   * Optional SPE container ID. When provided, all external container members
   * will be removed from the SharePoint Embedded container in addition to
   * deactivating Dataverse access records.
   */
  containerId?: string;
}

export interface ICloseProjectResponse {
  /** Number of sprk_externalrecordaccess records deactivated. */
  accessRecordsRevoked: number;
  /** Number of external SPE container permission entries removed. */
  speContainerMembersRemoved: number;
  /** Contact IDs whose Redis participation cache entries were invalidated. */
  affectedContactIds: string[];
}

export interface ICloseProjectResult {
  success: boolean;
  data?: ICloseProjectResponse;
  errorMessage?: string;
  /**
   * The server's machine-readable `reasonCode` (a ProblemDetails extension, e.g.
   * {@link CLOSURE_ORGANIZATION_TOO_LARGE_REASON_CODE}) when the failure carried one — so a caller can tell a
   * retryable failure from one a retry cannot fix.
   */
  reasonCode?: string;
  /** The server's ProblemDetails `detail`, unprefixed, when the failure carried one. */
  detail?: string;
}

// ---------------------------------------------------------------------------
// Service function
// ---------------------------------------------------------------------------

/**
 * Calls the BFF /api/v1/external-access/close-project endpoint.
 *
 * Closes a Secure Project by revoking all external access and removing
 * all external members from the SPE container.
 *
 * Dependencies are injected as parameters to avoid solution-specific imports.
 *
 * @param request - Closure request payload
 * @param authenticatedFetch - MSAL-backed fetch function for BFF API calls
 * @param bffBaseUrl - Base URL for the BFF API (e.g. "https://spe-api-dev.azurewebsites.net/api")
 * @returns ICloseProjectResult — never throws.
 */
export async function closeSecureProject(
  request: ICloseProjectRequest,
  // See CloseProjectDialog's prop of the same name: `@spaarke/auth`'s throwing `authenticatedFetch`
  // (ADR-028) - a non-2xx rejects and the catch below reports it.
  authenticatedFetch: AuthenticatedFetchFn,
  bffBaseUrl: string
): Promise<ICloseProjectResult> {
  const url = `${bffBaseUrl}/api/v1/external-access/close-project`;

  try {
    const response = await authenticatedFetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });

    const data: ICloseProjectResponse = await response.json();

    console.info('[ClosureService] Project closure complete:', {
      accessRecordsRevoked: data.accessRecordsRevoked,
      speContainerMembersRemoved: data.speContainerMembersRemoved,
      affectedContactIds: data.affectedContactIds.length,
    });

    return { success: true, data };
  } catch (err) {
    const message = err instanceof Error ? err.message : 'Network error';
    console.error('[ClosureService] Project closure error:', err);
    const problem = problemOf(err);
    return {
      success: false,
      errorMessage: `Project closure failed: ${message}`,
      reasonCode: typeof problem?.reasonCode === 'string' ? problem.reasonCode : undefined,
      detail: typeof problem?.detail === 'string' ? problem.detail : undefined,
    };
  }
}

/**
 * provisioningService.ts
 * BFF API client for Secure Project infrastructure provisioning.
 *
 * Calls POST /api/v1/external-access/provision-project to orchestrate:
 *   - Assignment of the project to the canonical Secure Project business unit's owner team
 *   - SPE container provisioning
 *   - Recording the container on the project record
 *
 * CONTRACT CHANGED 2026-08-25 (BFF task 021). The backend no longer creates a business unit per
 * project, no longer creates an External Access Account, and no longer supports umbrella BU
 * selection: there is ONE canonical `Secure Project` business unit, resolved by name from server
 * configuration. `umbrellaBuId`, `accountId`, `accountName` and `wasUmbrellaBu` are gone; the owner
 * team the project now belongs to is reported instead.
 *
 * Dependencies are injected as parameters (no solution-specific imports):
 *   - authenticatedFetch: MSAL-backed fetch function
 *   - bffBaseUrl: BFF API base URL
 *
 * Returns result object — never throws.
 */

// ---------------------------------------------------------------------------
// Request / Response types (mirror BFF Dtos)
// ---------------------------------------------------------------------------

export interface IProvisionProjectRequest {
  /** The sprk_project GUID that has just been created with sprk_issecure = true. */
  projectId: string;
  /**
   * Optional. Short project reference code (e.g. "P-2024-0042"), used only as a fallback for the
   * SPE container's display name when the project record has no name. It no longer names a business
   * unit, so it is no longer required.
   */
  projectRef?: string;
}

export interface IProvisionProjectResponse {
  /** The canonical Secure Project business unit — resolved by name, not created. */
  businessUnitId: string;
  businessUnitName: string;
  /** The business unit's default owner team, which now owns the project. */
  ownerTeamId: string;
  ownerTeamName: string;
  /** The project's own SPE container, recorded on sprk_containerid. */
  speContainerId: string;
}

export interface IProvisionProjectResult {
  success: boolean;
  data?: IProvisionProjectResponse;
  errorMessage?: string;
}

// ---------------------------------------------------------------------------
// Provisioning step progress
// ---------------------------------------------------------------------------

/**
 * Ordered steps shown in the provisioning progress UI.
 *
 * Mirrors what the backend actually does, in order. The retired 'bu' and 'account' steps described
 * creating a business unit and an External Access Account per project; neither happens any more.
 * Ownership is listed first because it is done first \u2014 it is the security step, so a container
 * failure must not leave the record owned outside the Secure Project business unit.
 */
export const PROVISIONING_STEPS = [
  { key: 'ownership', label: 'Securing project ownership\u2026' },
  { key: 'container', label: 'Provisioning document container\u2026' },
  { key: 'storing', label: 'Recording the container on the project\u2026' },
] as const;

export type ProvisioningStepKey = (typeof PROVISIONING_STEPS)[number]['key'];

// ---------------------------------------------------------------------------
// Service function
// ---------------------------------------------------------------------------

/**
 * Calls the BFF /api/v1/external-access/provision-project endpoint.
 *
 * Dependencies are injected as parameters to avoid solution-specific imports.
 *
 * @param request - Provisioning request payload
 * @param authenticatedFetch - MSAL-backed fetch function for BFF API calls
 * @param bffBaseUrl - Base URL for the BFF API (e.g. "https://spe-api-dev.azurewebsites.net/api")
 * @returns IProvisionProjectResult — never throws.
 */
export async function provisionSecureProject(
  request: IProvisionProjectRequest,
  authenticatedFetch: typeof fetch,
  bffBaseUrl: string
): Promise<IProvisionProjectResult> {
  const url = `${bffBaseUrl}/api/v1/external-access/provision-project`;

  try {
    const response = await authenticatedFetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      let errorDetail = `HTTP ${response.status}`;
      try {
        // Attempt to extract ProblemDetails detail field
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const problem: any = await response.json();
        errorDetail = problem?.detail ?? problem?.title ?? errorDetail;
      } catch {
        /* ignore JSON parse failure */
      }

      console.error('[ProvisioningService] Provisioning failed:', response.status, errorDetail);
      return {
        success: false,
        errorMessage: `Provisioning failed: ${errorDetail}`,
      };
    }

    const data: IProvisionProjectResponse = await response.json();

    console.info('[ProvisioningService] Provisioning complete:', {
      buId: data.businessUnitId,
      buName: data.businessUnitName,
      ownerTeamId: data.ownerTeamId,
      containerId: data.speContainerId,
    });

    return { success: true, data };
  } catch (err) {
    const message = err instanceof Error ? err.message : 'Network error';
    console.error('[ProvisioningService] Provisioning error:', err);
    return {
      success: false,
      errorMessage: `Provisioning failed: ${message}`,
    };
  }
}

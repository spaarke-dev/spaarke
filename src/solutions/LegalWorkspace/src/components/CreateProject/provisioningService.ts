/**
 * provisioningService.ts
 * BFF API client for Secure Project infrastructure provisioning.
 *
 * Calls POST /api/v1/external-access/provision-project to orchestrate:
 *   - Child Business Unit creation (SP-{ProjectRef})
 *   - SPE container provisioning
 *   - External Access Account creation
 *   - Storage of all references on the project record
 *
 * Supports umbrella BU selection for multi-project organisations.
 *
 * Follows the same pattern as other BFF service calls in the wizard:
 *   - Uses authenticatedFetch (MSAL-backed token acquisition)
 *   - Returns result object — never throws
 */

import { getBffBaseUrl } from '../../config/runtimeConfig';
import { authenticatedFetch } from '../../services/authInit';

// ---------------------------------------------------------------------------
// Request / Response types (mirror BFF Dtos)
// ---------------------------------------------------------------------------

export interface IProvisionProjectRequest {
  /** The sprk_project GUID that has just been created with sprk_issecure = true. */
  projectId: string;
  /**
   * Short project reference code used to name the BU (e.g. "P-2024-0042").
   * Required unless umbrellaBuId is provided.
   */
  projectRef?: string;
  /**
   * Optional. When provided, reuses this existing Business Unit instead of
   * creating a new one (umbrella BU scenario).
   */
  umbrellaBuId?: string;
}

export interface IProvisionProjectResponse {
  businessUnitId: string;
  businessUnitName: string;
  speContainerId: string;
  accountId: string;
  accountName: string;
  wasUmbrellaBu: boolean;
}

export interface IProvisionProjectResult {
  success: boolean;
  data?: IProvisionProjectResponse;
  errorMessage?: string;
}

// ---------------------------------------------------------------------------
// Provisioning step progress
// ---------------------------------------------------------------------------

/** Ordered steps shown in the provisioning progress UI. */
export const PROVISIONING_STEPS = [
  { key: 'bu', label: 'Creating secure Business Unit\u2026' },
  { key: 'container', label: 'Provisioning document container\u2026' },
  { key: 'account', label: 'Creating external access account\u2026' },
  { key: 'storing', label: 'Storing project references\u2026' },
] as const;

export type ProvisioningStepKey = (typeof PROVISIONING_STEPS)[number]['key'];

// ---------------------------------------------------------------------------
// Service function
// ---------------------------------------------------------------------------

/**
 * Calls the BFF /api/v1/external-access/provision-project endpoint.
 *
 * Returns IProvisionProjectResult — never throws.
 */
export async function provisionSecureProject(
  request: IProvisionProjectRequest
): Promise<IProvisionProjectResult> {
  const url = `${getBffBaseUrl()}/v1/external-access/provision-project`;

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
      containerId: data.speContainerId,
      accountId: data.accountId,
      wasUmbrellaBu: data.wasUmbrellaBu,
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

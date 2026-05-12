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
 * Dependencies are injected as parameters (no solution-specific imports):
 *   - authenticatedFetch: MSAL-backed fetch function
 *   - bffBaseUrl: BFF API base URL
 *
 * Returns result object — never throws.
 */
// ---------------------------------------------------------------------------
// Provisioning step progress
// ---------------------------------------------------------------------------
/** Ordered steps shown in the provisioning progress UI. */
export const PROVISIONING_STEPS = [
    { key: 'bu', label: 'Creating secure Business Unit\u2026' },
    { key: 'container', label: 'Provisioning document container\u2026' },
    { key: 'account', label: 'Creating external access account\u2026' },
    { key: 'storing', label: 'Storing project references\u2026' },
];
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
export async function provisionSecureProject(request, authenticatedFetch, bffBaseUrl) {
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
                const problem = await response.json();
                errorDetail = problem?.detail ?? problem?.title ?? errorDetail;
            }
            catch {
                /* ignore JSON parse failure */
            }
            console.error('[ProvisioningService] Provisioning failed:', response.status, errorDetail);
            return {
                success: false,
                errorMessage: `Provisioning failed: ${errorDetail}`,
            };
        }
        const data = await response.json();
        console.info('[ProvisioningService] Provisioning complete:', {
            buId: data.businessUnitId,
            containerId: data.speContainerId,
            accountId: data.accountId,
            wasUmbrellaBu: data.wasUmbrellaBu,
        });
        return { success: true, data };
    }
    catch (err) {
        const message = err instanceof Error ? err.message : 'Network error';
        console.error('[ProvisioningService] Provisioning error:', err);
        return {
            success: false,
            errorMessage: `Provisioning failed: ${message}`,
        };
    }
}
//# sourceMappingURL=provisioningService.js.map
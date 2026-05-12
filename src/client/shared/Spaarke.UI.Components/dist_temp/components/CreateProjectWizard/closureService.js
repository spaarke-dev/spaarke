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
export async function closeSecureProject(request, authenticatedFetch, bffBaseUrl) {
    const url = `${bffBaseUrl}/api/v1/external-access/close-project`;
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
            console.error('[ClosureService] Project closure failed:', response.status, errorDetail);
            return {
                success: false,
                errorMessage: `Project closure failed: ${errorDetail}`,
            };
        }
        const data = await response.json();
        console.info('[ClosureService] Project closure complete:', {
            accessRecordsRevoked: data.accessRecordsRevoked,
            speContainerMembersRemoved: data.speContainerMembersRemoved,
            affectedContactIds: data.affectedContactIds.length,
        });
        return { success: true, data };
    }
    catch (err) {
        const message = err instanceof Error ? err.message : 'Network error';
        console.error('[ClosureService] Project closure error:', err);
        return {
            success: false,
            errorMessage: `Project closure failed: ${message}`,
        };
    }
}
//# sourceMappingURL=closureService.js.map
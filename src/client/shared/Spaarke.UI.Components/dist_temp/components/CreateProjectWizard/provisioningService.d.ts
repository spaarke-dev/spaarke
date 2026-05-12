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
/** Ordered steps shown in the provisioning progress UI. */
export declare const PROVISIONING_STEPS: readonly [{
    readonly key: "bu";
    readonly label: "Creating secure Business Unit…";
}, {
    readonly key: "container";
    readonly label: "Provisioning document container…";
}, {
    readonly key: "account";
    readonly label: "Creating external access account…";
}, {
    readonly key: "storing";
    readonly label: "Storing project references…";
}];
export type ProvisioningStepKey = (typeof PROVISIONING_STEPS)[number]['key'];
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
export declare function provisionSecureProject(request: IProvisionProjectRequest, authenticatedFetch: typeof fetch, bffBaseUrl: string): Promise<IProvisionProjectResult>;
//# sourceMappingURL=provisioningService.d.ts.map
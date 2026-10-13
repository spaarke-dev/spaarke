/**
 * launchParams.ts — the Document Upload Wizard's launch `data` contract.
 *
 * Extracted from `main.tsx` (task 113, ontology-platform-r1 D-26) so the code page and the in-app
 * host resolve the parent record from the SAME envelope with the SAME rules. Pure: no DOM, no
 * auth, no side effects (importing it must never bootstrap the code page).
 *
 * `containerId` is deliberately NOT read (task 076, 2026-09-03): the server derives the upload
 * container from the parent record, so a caller that still appends `&containerId=…` is ignored.
 */

export interface UploadLaunchParams {
    parentEntityType: string;
    parentEntityId: string;
    parentEntityName: string;
}

/**
 * @param dataEnvelope the `data` value exactly as `Xrm.Navigation.navigateTo` received it (the whole
 *   `key=value&…` string, URI-encoded once by the caller), or `null` when absent.
 * @param fallback the page's own query params, used when there is no `data` envelope (dev server).
 */
export function resolveUploadLaunchParams(
    dataEnvelope: string | null,
    fallback: URLSearchParams = new URLSearchParams(),
): UploadLaunchParams {
    const appParams = dataEnvelope ? new URLSearchParams(decodeURIComponent(dataEnvelope)) : fallback;
    return {
        parentEntityType: appParams.get("parentEntityType") ?? "",
        parentEntityId: appParams.get("parentEntityId") ?? "",
        parentEntityName: appParams.get("parentEntityName") ?? "",
    };
}

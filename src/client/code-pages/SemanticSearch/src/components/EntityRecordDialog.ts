/**
 * EntityRecordDialog — Opens Dataverse entity record forms in dialog mode
 *
 * Maps search domains to entity logical names and calls
 * Xrm.Navigation.navigateTo with target: 2 (dialog).
 *
 * @see ADR-006: Xrm.Navigation.navigateTo for record dialogs
 */

import type { SearchDomain } from "../types";

// =============================================
// Domain to entity mapping
// =============================================

const DOMAIN_TO_ENTITY: Record<SearchDomain, string> = {
    documents: "sprk_document",
    matters: "sprk_matter",
    projects: "sprk_project",
    invoices: "sprk_invoice",
};

// =============================================
// Public API
// =============================================

/**
 * Open a Dataverse entity record form in a dialog.
 *
 * @param recordId - The GUID of the record to open
 * @param domain - The search domain (determines entity type)
 */
export function openEntityRecord(recordId: string, domain: SearchDomain): void {
    const entityName = DOMAIN_TO_ENTITY[domain];

    try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = (window as any).Xrm as typeof Xrm | undefined;
        if (!xrm?.Navigation?.navigateTo) {
            console.warn("[EntityRecordDialog] Xrm.Navigation not available — cannot open record dialog.");
            return;
        }

        xrm.Navigation.navigateTo(
            {
                pageType: "entityrecord",
                entityName,
                entityId: recordId,
            },
            {
                target: 2,
                width: { value: 70, unit: "%" },
                height: { value: 80, unit: "%" },
            }
        );
    } catch (err) {
        console.error("[EntityRecordDialog] Failed to open record dialog:", err);
    }
}

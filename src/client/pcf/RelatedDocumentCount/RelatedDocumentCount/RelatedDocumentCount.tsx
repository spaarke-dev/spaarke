/**
 * RelatedDocumentCount - Main component for the PCF control.
 *
 * Fetches the count of semantically related documents via useRelatedDocumentCount
 * and renders the RelationshipCountCard shared component. Clicking "View" opens
 * the FindSimilarDialog with the DocumentRelationshipViewer code page.
 *
 * @see ADR-012 - Deep import from shared component library
 * @see ADR-021 - Fluent UI v9 design tokens
 * @see ADR-022 - React 16 APIs only (useState, useCallback)
 */

import * as React from "react";
import { RelationshipCountCard } from "@spaarke/ui-components/dist/components/RelationshipCountCard";
import { FindSimilarDialog } from "@spaarke/ui-components/dist/components/FindSimilarDialog";
import { useRelatedDocumentCount } from "./hooks/useRelatedDocumentCount";
import { IRelatedDocumentCountProps } from "./types";

/**
 * Build the URL for the DocumentRelationshipViewer code page web resource.
 * Follows the same URL construction pattern as SemanticSearchControl's NavigationService.
 *
 * @param documentId - Source document GUID
 * @param tenantId - Azure AD tenant ID
 * @param isDarkMode - Whether to pass dark theme to the viewer
 * @returns Full URL to the web resource, or null if missing data
 */
function buildViewerUrl(
    documentId: string,
    tenantId: string | undefined,
    isDarkMode: boolean
): string | null {
    if (!documentId || documentId.trim() === "") {
        return null;
    }

    const theme = isDarkMode ? "dark" : "light";
    const params = new URLSearchParams({ documentId, theme });
    if (tenantId) {
        params.set("tenantId", tenantId);
    }

    // Resolve client URL from Xrm context (Dataverse org URL)
    const clientUrl = getClientUrl();
    return `${clientUrl}/WebResources/sprk_documentrelationshipviewer?data=${encodeURIComponent(params.toString())}`;
}

/**
 * Get the Dataverse client URL from the Xrm global.
 * Falls back to window.location.origin if Xrm is not available (e.g., test harness).
 */
function getClientUrl(): string {
    try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = (window as any).Xrm as {
            Utility?: { getGlobalContext?: () => { getClientUrl?: () => string } };
        } | undefined;
        const url = xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.();
        if (url) return url.replace(/\/$/, "");
    } catch {
        // Xrm not available (test harness, dev mode)
    }
    return window.location.origin;
}

/**
 * RelatedDocumentCount component.
 *
 * Displays the count of semantically related documents using RelationshipCountCard.
 * On click, opens the FindSimilarDialog iframe with the full relationship viewer.
 */
export const RelatedDocumentCount: React.FC<IRelatedDocumentCountProps> = ({
    documentId,
    tenantId,
    apiBaseUrl,
    cardTitle = "RELATED DOCUMENTS",
    isDarkMode,
}) => {
    // Fetch related document count from BFF API
    const { count, isLoading, error, lastUpdated } = useRelatedDocumentCount(
        documentId,
        tenantId,
        apiBaseUrl
    );

    // Dialog open/close state
    const [isDialogOpen, setIsDialogOpen] = React.useState(false);

    // Build viewer URL for the dialog
    const viewerUrl = React.useMemo(
        () => buildViewerUrl(documentId, tenantId, isDarkMode),
        [documentId, tenantId, isDarkMode]
    );

    // Open the FindSimilarDialog when user clicks "View" on the count card
    const handleOpen = React.useCallback(() => {
        if (viewerUrl) {
            setIsDialogOpen(true);
        }
    }, [viewerUrl]);

    // Close the FindSimilarDialog
    const handleClose = React.useCallback(() => {
        setIsDialogOpen(false);
    }, []);

    return (
        <div data-pcf-version="1.0.1">
            <RelationshipCountCard
                title={cardTitle}
                count={count}
                isLoading={isLoading}
                error={error}
                onOpen={handleOpen}
                lastUpdated={lastUpdated ?? undefined}
            />
            <FindSimilarDialog
                open={isDialogOpen}
                onClose={handleClose}
                url={isDialogOpen ? viewerUrl : null}
            />
        </div>
    );
};

export default RelatedDocumentCount;

/**
 * NavigationService
 *
 * Service for Dataverse navigation actions.
 * Implements Open File, Open Record (Modal), Open Record (New Tab), and View All.
 *
 * @see spec.md for navigation requirements
 */

import { SearchResult, SearchFilters, SearchScope } from "../types";
import { msalConfig } from "./auth/msalConfig";

/**
 * Navigation target modes
 */
export enum NavigationTarget {
    /** Open in new tab (Xrm target: 1) */
    NewTab = 1,
    /** Open in modal dialog (Xrm target: 2) */
    Modal = 2,
}

/**
 * Modal dialog options
 */
export interface ModalOptions {
    width: number;
    height: number;
    unit: "%" | "px";
}

/**
 * Default modal options (80% width and height per spec)
 */
const DEFAULT_MODAL_OPTIONS: ModalOptions = {
    width: 80,
    height: 80,
    unit: "%",
};

/**
 * Service for navigation operations
 */
export class NavigationService {
    /**
     * Open a file in web browser or desktop application.
     * @param fileUrl - SPE pre-authenticated file URL (may be empty if not available from API)
     * @param recordUrl - Fallback Dataverse record URL
     * @param fileType - File extension (docx, xlsx, pdf, etc.)
     * @param mode - "web" opens in browser; "desktop" launches Office app via ms-office protocol
     */
    openFile(fileUrl: string, recordUrl: string, fileType: string, mode: "web" | "desktop"): void {
        if (mode === "web") {
            // Prefer SPE file URL; fall back to the Dataverse record URL
            const url = fileUrl || recordUrl;
            if (!url) {
                console.warn("NavigationService.openFile: No URL provided for web mode");
                return;
            }
            window.open(url, "_blank", "noopener,noreferrer");
        } else {
            // Desktop mode — use Microsoft Office URI scheme
            if (!fileUrl) {
                console.warn("NavigationService.openFile: No fileUrl for desktop mode, falling back to record URL");
                if (recordUrl) {
                    window.open(recordUrl, "_blank", "noopener,noreferrer");
                }
                return;
            }
            const protocol = this.getOfficeProtocol(fileType);
            if (protocol) {
                // Open using ms-word://, ms-excel://, etc.
                window.open(`${protocol}ofe|u|${encodeURIComponent(fileUrl)}`, "_self");
            } else {
                // No protocol for this type (e.g. PDF) — open in browser
                window.open(fileUrl, "_blank", "noopener,noreferrer");
            }
        }
    }

    /**
     * Map file extension to Microsoft Office URI scheme.
     */
    private getOfficeProtocol(fileType: string): string | null {
        switch (fileType.toLowerCase()) {
            case "doc":
            case "docx": return "ms-word:";
            case "xls":
            case "xlsx": return "ms-excel:";
            case "ppt":
            case "pptx": return "ms-powerpoint:";
            case "one":
            case "onetoc2": return "onenote:";
            default: return null;
        }
    }

    /**
     * Open a Dataverse record in a modal dialog
     * @param result - Search result containing entity and record info
     * @param options - Optional modal sizing options
     */
    async openRecordModal(
        result: SearchResult,
        options: ModalOptions = DEFAULT_MODAL_OPTIONS
    ): Promise<void> {
        const entityName = this.getEntityLogicalName(result);
        const recordId = this.getRecordId(result);

        if (!entityName || !recordId) {
            console.warn(
                "NavigationService.openRecordModal: Missing entity name or record ID",
                { entityName, recordId }
            );
            return;
        }

        await this.navigateToRecord(entityName, recordId, NavigationTarget.Modal, options);
    }

    /**
     * Open a Dataverse record in a new browser tab.
     * Uses window.open() directly because Xrm.Navigation.navigateTo(target:1)
     * navigates inline (replaces current page), not in a new browser tab.
     * @param result - Search result containing entity and record info
     */
    openRecordNewTab(result: SearchResult): void {
        const entityName = this.getEntityLogicalName(result);
        const recordId = this.getRecordId(result);

        if (!entityName || !recordId) {
            console.warn(
                "NavigationService.openRecordNewTab: Missing entity name or record ID",
                { entityName, recordId }
            );
            return;
        }

        const clientUrl = this.getClientUrl();
        const recordUrl = `${clientUrl}/main.aspx?etn=${entityName}&id=${recordId}&pagetype=entityrecord`;
        window.open(recordUrl, "_blank", "noopener,noreferrer");
    }

    /**
     * Open the DocumentRelationshipViewer HTML web resource for a document (Find Similar)
     * @param result - Search result to find similar documents for
     */
    /**
     * Build the URL for the DocumentRelationshipViewer web resource.
     * Returns null if the document ID is missing.
     * Caller is responsible for opening/rendering the URL (e.g. in an iframe Dialog).
     */
    getFindSimilarUrl(result: SearchResult, isDarkMode: boolean = false): string | null {
        const rawId = result.documentId;
        if (!rawId) {
            console.warn("NavigationService.getFindSimilarUrl: Missing document ID");
            return null;
        }

        const authorityParts = msalConfig.auth.authority?.split("/") ?? [];
        const tenantId = authorityParts[authorityParts.length - 1] ?? "";
        const theme = isDarkMode ? "dark" : "light";
        const data = new URLSearchParams({ documentId: rawId, tenantId, theme }).toString();
        const clientUrl = this.getClientUrl();
        return `${clientUrl}/WebResources/sprk_documentrelationshipviewer?data=${encodeURIComponent(data)}`;
    }

    /**
     * Open the document upload dialog custom page (Add Document)
     * @param scopeId - Current record ID to associate the upload with
     * @param entityType - API entity type name (matter, project, etc.)
     */
    async openAddDocument(scopeId: string | null, entityType: string | null): Promise<void> {
        const xrm = this.getXrm();
        if (!xrm?.Navigation?.navigateTo) {
            console.warn("NavigationService.openAddDocument: Xrm.Navigation not available");
            return;
        }

        const entityLogicalNameMap: Record<string, string> = {
            matter: "sprk_matter",
            project: "sprk_project",
            invoice: "sprk_invoice",
            account: "account",
            contact: "contact",
        };
        const entityLogicalName = entityType ? entityLogicalNameMap[entityType] : undefined;

        // Wrap GUID in braces if not already wrapped — Dataverse custom pages require {guid} format
        const formattedRecordId = scopeId
            ? (scopeId.startsWith("{") ? scopeId : `{${scopeId}}`)
            : undefined;

        try {
            await xrm.Navigation.navigateTo(
                {
                    pageType: "custom",
                    name: "sprk_documentuploaddialog_e52db",
                    entityName: entityLogicalName,
                    recordId: formattedRecordId,
                },
                {
                    target: 2,
                    width: { value: 70, unit: "%" },
                    height: { value: 80, unit: "%" },
                }
            );
        } catch (error) {
            console.error("NavigationService.openAddDocument: Navigation failed", error);
        }
    }

    /**
     * Navigate to the Custom Page for viewing all search results
     * @param query - Current search query
     * @param scope - Search scope (all, matter, custom)
     * @param scopeId - Scope ID if applicable
     * @param filters - Current search filters
     * @param customPageName - Logical name of the custom page (default: sprk_semanticsearchpage)
     */
    async viewAllResults(
        query: string,
        scope: SearchScope,
        scopeId: string | null,
        filters: SearchFilters,
        customPageName: string = "sprk_semanticsearchpage"
    ): Promise<void> {
        const xrm = this.getXrm();
        if (!xrm?.Navigation?.navigateTo) {
            console.warn(
                "NavigationService.viewAllResults: Xrm.Navigation not available"
            );
            return;
        }

        try {
            const pageInput: Xrm.Navigation.CustomPage = {
                pageType: "custom",
                name: customPageName,
                entityName: scope === "matter" && scopeId ? "sprk_matter" : undefined,
                recordId: scope === "matter" && scopeId ? scopeId : undefined,
            };

            const navigationOptions: Xrm.Navigation.NavigationOptions = {
                target: 1, // New window
            };

            await xrm.Navigation.navigateTo(pageInput, navigationOptions);
        } catch (error) {
            console.error("NavigationService.viewAllResults: Navigation failed", error);
        }
    }

    /**
     * Navigate to a Dataverse record
     */
    private async navigateToRecord(
        entityName: string,
        recordId: string,
        target: NavigationTarget,
        modalOptions?: ModalOptions
    ): Promise<void> {
        const xrm = this.getXrm();
        if (!xrm?.Navigation?.navigateTo) {
            console.warn(
                "NavigationService: Xrm.Navigation not available, falling back to URL navigation"
            );
            this.fallbackNavigate(entityName, recordId, target);
            return;
        }

        try {
            const pageInput: Xrm.Navigation.PageInputEntityRecord = {
                pageType: "entityrecord",
                entityName,
                entityId: recordId,
            };

            const navigationOptions: Xrm.Navigation.NavigationOptions =
                target === NavigationTarget.Modal
                    ? {
                          target: 2,
                          width: { value: modalOptions?.width ?? 80, unit: modalOptions?.unit ?? "%" },
                          height: { value: modalOptions?.height ?? 80, unit: modalOptions?.unit ?? "%" },
                      }
                    : {
                          target: 1,
                      };

            await xrm.Navigation.navigateTo(pageInput, navigationOptions);
        } catch (error) {
            console.error("NavigationService: Navigation failed", error);
            this.fallbackNavigate(entityName, recordId, target);
        }
    }

    /**
     * Fallback navigation using URL
     */
    private fallbackNavigate(
        entityName: string,
        recordId: string,
        target: NavigationTarget
    ): void {
        const clientUrl = this.getClientUrl();
        const recordUrl = `${clientUrl}/main.aspx?etn=${entityName}&id=${recordId}&pagetype=entityrecord`;

        if (target === NavigationTarget.NewTab) {
            window.open(recordUrl, "_blank", "noopener,noreferrer");
        } else {
            // For modal, we can't replicate Xrm modal behavior without Xrm.Navigation
            // Fall back to new tab
            window.open(recordUrl, "_blank", "noopener,noreferrer");
        }
    }

    /**
     * Extract entity logical name from search result
     */
    private getEntityLogicalName(result: SearchResult): string | undefined {
        // Use explicit entityLogicalName if available
        if (result.entityLogicalName) {
            return result.entityLogicalName;
        }

        // Try to extract from recordUrl
        if (result.recordUrl) {
            const match = result.recordUrl.match(/etn=([^&]+)/);
            if (match) {
                return match[1];
            }
        }

        // Default to sprk_document
        return "sprk_document";
    }

    /**
     * Extract record ID from search result
     */
    private getRecordId(result: SearchResult): string | undefined {
        // Use explicit recordId if available
        if (result.recordId) {
            return result.recordId;
        }

        // Use documentId
        if (result.documentId) {
            return result.documentId;
        }

        // Try to extract from recordUrl
        if (result.recordUrl) {
            const match = result.recordUrl.match(/id=([^&]+)/);
            if (match) {
                return match[1];
            }
        }

        return undefined;
    }

    /**
     * Get the Dataverse client URL
     */
    private getClientUrl(): string {
        const xrm = this.getXrm();
        if (xrm?.Utility?.getGlobalContext) {
            return xrm.Utility.getGlobalContext().getClientUrl();
        }

        // Fallback to current origin
        return window.location.origin;
    }

    /**
     * Resolve the Xrm global in a PCF virtual control context.
     *
     * PCF virtual controls run inside an iframe (or sandboxed context), so the
     * top-level `Xrm` declaration from @types/xrm may not be available as a
     * bare global. We check window.Xrm and window.parent.Xrm as fallbacks.
     */
    private getXrm(): typeof Xrm | undefined {
        // Direct global (works in classic web resource context)
        if (typeof Xrm !== "undefined") {
            return Xrm;
        }
        // window.Xrm (PCF virtual controls in the same origin frame)
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const w = window as any;
        if (w.Xrm) {
            return w.Xrm as typeof Xrm;
        }
        // window.parent.Xrm (PCF controls inside cross-origin iframes fall back to parent)
        try {
            if (w.parent?.Xrm) {
                return w.parent.Xrm as typeof Xrm;
            }
        } catch {
            // Cross-origin parent access blocked — swallow silently
        }
        return undefined;
    }
}

export default NavigationService;

/**
 * NavigationService
 *
 * Service for Dataverse navigation actions.
 * Implements Open File, Open Record (Modal), Open Record (New Tab), and View All.
 *
 * @see spec.md for navigation requirements
 */

import { SearchResult, SearchFilters, SearchScope } from "../types";

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
     * Open a file URL in a new browser tab
     * @param fileUrl - SPE file URL (pre-authenticated)
     */
    openFile(fileUrl: string): void {
        if (!fileUrl) {
            console.warn("NavigationService.openFile: No file URL provided");
            return;
        }

        window.open(fileUrl, "_blank", "noopener,noreferrer");
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
     * Open a Dataverse record in a new browser tab
     * @param result - Search result containing entity and record info
     */
    async openRecordNewTab(result: SearchResult): Promise<void> {
        const entityName = this.getEntityLogicalName(result);
        const recordId = this.getRecordId(result);

        if (!entityName || !recordId) {
            console.warn(
                "NavigationService.openRecordNewTab: Missing entity name or record ID",
                { entityName, recordId }
            );
            return;
        }

        await this.navigateToRecord(entityName, recordId, NavigationTarget.NewTab);
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
        // Check if Xrm.Navigation is available
        if (typeof Xrm === "undefined" || !Xrm.Navigation?.navigateTo) {
            console.warn(
                "NavigationService.viewAllResults: Xrm.Navigation not available"
            );
            return;
        }

        try {
            // Build page parameters to pass context
            const pageInput: Xrm.Navigation.CustomPage = {
                pageType: "custom",
                name: customPageName,
                entityName: scope === "matter" && scopeId ? "sprk_matter" : undefined,
                recordId: scope === "matter" && scopeId ? scopeId : undefined,
            };

            // Encode search context as URL parameters for the custom page to read
            const searchContext = {
                query,
                scope,
                scopeId: scopeId || "",
                documentTypes: filters.documentTypes.join(","),
                matterTypes: filters.matterTypes.join(","),
                fileTypes: filters.fileTypes.join(","),
                dateFrom: filters.dateRange?.from || "",
                dateTo: filters.dateRange?.to || "",
            };

            // Custom pages can receive parameters via the recordId or through URL
            // We'll encode the context in a way the custom page can decode
            const contextParam = encodeURIComponent(JSON.stringify(searchContext));

            // Navigate to custom page in new window for full results view
            const navigationOptions: Xrm.Navigation.NavigationOptions = {
                target: 1, // New window
            };

            await Xrm.Navigation.navigateTo(pageInput, navigationOptions);
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
        // Check if Xrm.Navigation is available
        if (typeof Xrm === "undefined" || !Xrm.Navigation?.navigateTo) {
            console.warn(
                "NavigationService: Xrm.Navigation not available, falling back to URL navigation"
            );
            this.fallbackNavigate(entityName, recordId, target);
            return;
        }

        try {
            // Build navigation parameters
            const pageInput: Xrm.Navigation.PageInputEntityRecord = {
                pageType: "entityrecord",
                entityName,
                entityId: recordId,
            };

            // Build navigation options based on target
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

            await Xrm.Navigation.navigateTo(pageInput, navigationOptions);
        } catch (error) {
            console.error("NavigationService: Navigation failed", error);
            // Fall back to URL navigation on error
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
        if (typeof Xrm !== "undefined" && Xrm.Utility?.getGlobalContext) {
            return Xrm.Utility.getGlobalContext().getClientUrl();
        }

        // Fallback to current origin
        return window.location.origin;
    }
}

export default NavigationService;

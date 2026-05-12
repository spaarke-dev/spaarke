/**
 * PlaybookLibraryShell — Shared playbook browsing + execution shell.
 *
 * Extracted from AnalysisBuilder/App.tsx (UDSS-020). Provides a 2-tab layout:
 *   Tab 1: Select Playbook — card grid with locked scope preview on selection
 *   Tab 2: Custom Scope — manual action/skills/knowledge/tools selection
 *
 * All Dataverse access is routed through the IDataService prop so the shell
 * remains portable across PCF controls, Code Pages, SPAs, and test harnesses.
 *
 * BFF API calls use the injected `authenticatedFetch` + `bffBaseUrl` props
 * instead of importing from solution-specific modules.
 */
import React from 'react';
import type { AuthenticatedFetchFn } from '../Playbook/analysisService';
import type { IDataService } from '../../types/serviceInterfaces';
export interface IPlaybookLibraryShellProps {
    /** Entity type of the source record (e.g., "sprk_document"). */
    entityType: string;
    /** GUID of the source entity record (the active document when documentIds is also provided). */
    entityId: string;
    /**
     * Optional list of document IDs available for selection.
     *
     * When two or more IDs are provided a DocumentSelector bar is rendered at the
     * top of the shell, allowing the user to switch the active document before
     * running an analysis.  The first ID in the array is selected by default
     * (unless `entityId` already matches one of them).
     *
     * When only one ID is provided (or this prop is omitted) the selector is
     * hidden and `entityId` is used directly.
     */
    documentIds?: string[];
    /** Optional allowlist — only show playbooks whose IDs are in this array. */
    allowedPlaybookIds?: string[];
    /** Display mode: 'browse' shows full 2-tab UI, 'intent' pre-selects a playbook. */
    mode?: 'browse' | 'intent';
    /** When true, suppresses the header/footer chrome for embedding inside another shell. */
    embedded?: boolean;
    /** Pre-select a specific playbook by intent string (matched against playbook name). */
    intent?: string;
    /** Called when analysis creation completes successfully. */
    onComplete?: (result: {
        analysisId: string;
    }) => void;
    /** Called when the user cancels or closes the shell. */
    onClose?: () => void;
    /** Data access abstraction (Xrm.WebApi adapter, test mock, etc.). */
    dataService: IDataService;
    /** Authenticated fetch function for BFF API calls. */
    authenticatedFetch?: AuthenticatedFetchFn;
    /** Base URL of the BFF API (e.g., "https://spe-api-dev.azurewebsites.net"). */
    bffBaseUrl?: string;
    /** Display name of the source entity (shown in the header subtitle). */
    entityDisplayName?: string;
    /** Label for the primary action button. Defaults to "Run Analysis". */
    executeButtonLabel?: string;
    /** Title shown in the header. Defaults to "New Analysis". */
    title?: string;
}
export declare const PlaybookLibraryShell: React.FC<IPlaybookLibraryShellProps>;
export default PlaybookLibraryShell;
//# sourceMappingURL=PlaybookLibraryShell.d.ts.map
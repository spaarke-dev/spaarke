/**
 * FindSimilarDialog - Reusable iframe dialog for the DocumentRelationshipViewer.
 *
 * Renders a near-fullscreen Dialog containing an iframe that loads the
 * DocumentRelationshipViewer Code Page web resource.
 *
 * Consumer builds the URL (since URL construction differs between PCF and
 * LegalWorkspace) and passes it in. This component just provides the dialog
 * shell with correct sizing and no scrollbars.
 *
 * Optional `embedded` prop hides the title bar chrome when the dialog is
 * rendered inside a Dataverse form (e.g., as part of a PCF control panel).
 *
 * Optional `authenticatedFetch` and `bffBaseUrl` are accepted for forward
 * compatibility with service-injected patterns but are not currently used
 * by the iframe shell itself.
 *
 * Zero hard service dependencies — fully prop-based.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 * @see ADR-012 for shared component library patterns
 */
import * as React from 'react';
import type { IDataService } from '../../types/serviceInterfaces';
export interface IFindSimilarDialogProps {
    /** Whether the dialog is open. */
    open: boolean;
    /** Called when the dialog requests to close (backdrop click, Escape). */
    onClose: () => void;
    /** The URL to load in the iframe. When null/undefined the iframe is not rendered. */
    url: string | null;
    /**
     * When true, hides the title bar chrome (expand / close buttons).
     * Useful when the dialog is rendered inside a Dataverse form where
     * the parent already provides navigation controls.
     * @default false
     */
    embedded?: boolean;
    /**
     * Optional authenticated fetch function for forward compatibility
     * with service-injection patterns. Not used by the iframe shell itself.
     */
    authenticatedFetch?: (url: string, init?: RequestInit) => Promise<Response>;
    /**
     * Optional BFF API base URL for forward compatibility with
     * service-injection patterns. Not used by the iframe shell itself.
     */
    bffBaseUrl?: string;
    /**
     * Optional data service for forward compatibility with service-injection
     * patterns. Not used by the iframe shell itself, but available for
     * consumers that may extend this component.
     */
    dataService?: IDataService;
}
export declare const FindSimilarDialog: React.FC<IFindSimilarDialogProps>;
export default FindSimilarDialog;
//# sourceMappingURL=FindSimilarDialog.d.ts.map
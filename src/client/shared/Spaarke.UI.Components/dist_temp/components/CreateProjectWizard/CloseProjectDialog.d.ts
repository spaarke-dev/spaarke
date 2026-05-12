/**
 * CloseProjectDialog.tsx
 * Confirmation dialog for closing a Secure Project (internal users only).
 *
 * Displayed when an internal user (attorney, paralegal, admin) wants to close
 * a Secure Project — permanently revoking all external access.
 *
 * Closure consequences clearly communicated to user:
 *   - All external access records deactivated (sprk_externalrecordaccess)
 *   - All external members removed from the SPE document container
 *   - Redis participation cache invalidated for all affected contacts
 *
 * Three states:
 *   1. Confirmation — warning list + "Close Project" (danger) / "Cancel" buttons
 *   2. Closing      — Spinner with progress label
 *   3. Result       — Success summary or error MessageBar
 *
 * Dependencies are injected via props (no solution-specific imports):
 *   - authenticatedFetch: MSAL-backed fetch function
 *   - bffBaseUrl: BFF API base URL
 *
 * Constraints:
 *   - Fluent v9 only: Dialog, DialogSurface, DialogBody, DialogTitle,
 *     DialogContent, DialogActions, Button, Spinner, Text, MessageBar,
 *     MessageBarBody, makeStyles, tokens (ADR-021)
 *   - makeStyles with semantic tokens — ZERO hard-coded colours
 *   - Supports light, dark, and high-contrast modes (ADR-021)
 *   - Default export enables React.lazy() dynamic import
 */
import * as React from 'react';
import { type ICloseProjectResponse } from './closureService';
export interface ICloseProjectDialogProps {
    /** Whether the dialog is open. */
    open: boolean;
    /** Dataverse project GUID. Required to call the closure endpoint. */
    projectId: string;
    /** Human-readable project name shown in the dialog title area. */
    projectName: string;
    /**
     * Optional SPE container ID. When provided, external container members
     * are also removed from SharePoint Embedded.
     */
    containerId?: string;
    /** Called when the dialog is dismissed (cancelled or completed). */
    onClose: () => void;
    /**
     * Optional callback invoked after a successful project closure.
     * Callers can use this to refresh data or navigate away.
     */
    onClosed?: (result: ICloseProjectResponse) => void;
    /** MSAL-backed authenticated fetch function for BFF API calls. */
    authenticatedFetch: typeof fetch;
    /** BFF API base URL. */
    bffBaseUrl: string;
}
declare const CloseProjectDialog: React.FC<ICloseProjectDialogProps>;
export { CloseProjectDialog };
export default CloseProjectDialog;
//# sourceMappingURL=CloseProjectDialog.d.ts.map
/**
 * FilePreviewDialog.tsx
 * Full-screen modal dialog for document preview with toolbar actions.
 *
 * Shared library version — all external service calls are injected via the
 * `services` prop (IFilePreviewServices) so the component has zero
 * environment-specific imports.
 *
 * Features:
 *   - Fluent UI v9 Dialog (85vw x 85vh, max 880px)
 *   - Iframe preview with Spinner during load, error + retry on failure
 *   - Toolbar: Open File, Open Record, Copy Link, Add/Remove Workspace
 *   - Open File: lazy-fetch open links, cascade desktop -> web -> download
 */
import * as React from 'react';
import type { IFilePreviewServices } from './filePreviewTypes';
export interface IFilePreviewDialogProps {
    open: boolean;
    documentId: string;
    documentName: string;
    onClose: () => void;
    /** Service callbacks for BFF / Xrm operations. */
    services: IFilePreviewServices;
    /** Whether this document is currently in the user's workspace. */
    isInWorkspace?: boolean;
    /** Called when the workspace flag changes. */
    onWorkspaceFlagChanged?: (newFlag: boolean) => void;
}
export declare const FilePreviewDialog: React.FC<IFilePreviewDialogProps>;
//# sourceMappingURL=FilePreviewDialog.d.ts.map
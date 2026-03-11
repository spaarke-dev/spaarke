/**
 * App.tsx
 * DocumentUploadWizard Code Page -- thin shell that delegates to DocumentUploadWizardDialog.
 *
 * Receives URL parameters from main.tsx and passes them as props to the
 * domain wizard dialog. Handles the close behavior (window.close).
 *
 * URL parameters (passed from main.tsx):
 *   - parentEntityType: Dataverse entity type of the parent record (e.g., "sprk_document")
 *   - parentEntityId:   ID of the parent record
 *   - parentEntityName: Display name of the parent record
 *   - containerId:      SPE container ID for file uploads
 *
 * @see ADR-006  - Code Pages for standalone dialogs (not PCF)
 * @see ADR-007  - Document access through BFF API (SpeFileStore facade)
 * @see ADR-021  - Fluent UI v9 design system (makeStyles + design tokens)
 */

import { useCallback } from "react";
import {
    makeStyles,
    tokens,
} from "@fluentui/react-components";

import { DocumentUploadWizardDialog } from "./DocumentUploadWizardDialog";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface AppProps {
    /** Dataverse entity type of the parent record (e.g., "sprk_document"). */
    parentEntityType: string;
    /** ID of the parent record. */
    parentEntityId: string;
    /** Display name of the parent record. */
    parentEntityName: string;
    /** SPE container ID for file uploads. */
    containerId: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        width: "100%",
        height: "100%",
        overflow: "hidden",
        backgroundColor: tokens.colorNeutralBackground1,
    },
});

// ---------------------------------------------------------------------------
// App component
// ---------------------------------------------------------------------------

export function App({
    parentEntityType,
    parentEntityId,
    parentEntityName,
    containerId,
}: AppProps): JSX.Element {
    const styles = useStyles();

    // -- Close handler --
    // Dataverse dialogs opened via navigateTo(target:2) render the web resource
    // in an iframe overlay. We try multiple strategies to close reliably:
    //   1. Click the dialog's native close button (X) via DOM traversal
    //   2. window.close() — works if opened as a popup/window
    //   3. parent.close() — works in some iframe contexts
    const handleClose = useCallback(() => {
        /* eslint-disable @typescript-eslint/no-explicit-any */
        // Strategy 1: Find and click the Dataverse dialog close button
        // The dialog chrome renders a close button with data-id="dialogCloseIconButton"
        // or class "ms-Dialog-button--close"
        try {
            const frames = [window, window.parent, window.top].filter(Boolean);
            for (const frame of frames) {
                try {
                    const closeBtn =
                        frame?.document?.querySelector('[data-id="dialogCloseIconButton"]') as HTMLElement
                        ?? frame?.document?.querySelector('.ms-Dialog-button--close') as HTMLElement;
                    if (closeBtn) {
                        closeBtn.click();
                        return;
                    }
                } catch { /* cross-origin frame */ }
            }
        } catch { /* */ }

        // Strategy 2: window.close() — works for standalone popups
        try { window.close(); } catch { /* */ }

        // Strategy 3: parent.close() — works in some iframe contexts
        try { if (window.parent !== window) (window.parent as any).close(); } catch { /* */ }
        /* eslint-enable @typescript-eslint/no-explicit-any */
    }, []);

    return (
        <div className={styles.root}>
            <DocumentUploadWizardDialog
                parentEntityType={parentEntityType}
                parentEntityId={parentEntityId}
                parentEntityName={parentEntityName}
                containerId={containerId}
                onClose={handleClose}
            />
        </div>
    );
}

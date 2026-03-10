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
    const handleClose = useCallback(() => {
        try {
            /* eslint-disable @typescript-eslint/no-explicit-any */
            const xrm = (window as any).Xrm;
            if (xrm?.Navigation?.navigateTo) {
                window.close();
            } else {
                window.close();
            }
            /* eslint-enable @typescript-eslint/no-explicit-any */
        } catch {
            window.close();
        }
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

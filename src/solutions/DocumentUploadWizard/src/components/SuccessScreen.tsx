/**
 * SuccessScreen.tsx
 * Builds the IWizardSuccessConfig for the Document Upload Wizard success screen.
 *
 * Provides:
 *   - buildSuccessConfig(): creates the static IWizardSuccessConfig from upload results
 *   - SuccessActions: stateful component rendered as the config's `actions` field,
 *     handling "Work on Analysis" (with inline DocumentPicker), "Find Similar", and "Close"
 *
 * The IWizardSuccessConfig is rendered by WizardSuccessScreen (shared component)
 * which provides the standard layout: icon -> title -> body -> actions -> warnings.
 *
 * @see ADR-006  - Code Pages for standalone dialogs
 * @see ADR-021  - Fluent UI v9 design system (makeStyles + semantic tokens)
 */

import { useState, useCallback, useMemo } from "react";
import {
    makeStyles,
    tokens,
    Text,
    Button,
} from "@fluentui/react-components";
import {
    CheckmarkCircleFilled,
    WarningFilled,
    BrainCircuitRegular,
    DocumentSearchRegular,
    DismissRegular,
} from "@fluentui/react-icons";

import type { IWizardSuccessConfig } from "@spaarke/ui-components/components/Wizard";
import type { OrchestratorResult, OrchestratorFileResult } from "../services/uploadOrchestrator";
import type { NextStepActionId } from "../types";
import { DocumentPicker } from "./DocumentPicker";

// ---------------------------------------------------------------------------
// SuccessActions props
// ---------------------------------------------------------------------------

export interface ISuccessActionsProps {
    /** Successfully uploaded file results (for document picker). */
    successfulFiles: OrchestratorFileResult[];
    /** Next-step action IDs selected by the user in the NextStepsStep. */
    selectedNextSteps: NextStepActionId[];
    /** Callback to launch the Analysis Builder for a specific document. */
    onLaunchAnalysis: (documentRecordId: string) => void;
    /** Callback to launch FindSimilarDialog. */
    onLaunchFindSimilar: () => void;
    /** Callback to close the wizard dialog. */
    onClose: () => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    actionsContainer: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        gap: tokens.spacingVerticalM,
        width: "100%",
    },
    actionsRow: {
        display: "flex",
        gap: tokens.spacingHorizontalM,
        justifyContent: "center",
        flexWrap: "wrap",
    },
    pickerSection: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
        width: "100%",
        maxWidth: "400px",
        textAlign: "left",
    },
    pickerActions: {
        display: "flex",
        gap: tokens.spacingHorizontalM,
        justifyContent: "center",
        marginTop: tokens.spacingVerticalXS,
    },
});

// ---------------------------------------------------------------------------
// SuccessActions — stateful action buttons with inline document picker
// ---------------------------------------------------------------------------

/**
 * Stateful component rendered as the `actions` field of IWizardSuccessConfig.
 * Handles conditional action buttons and the inline document picker for
 * multi-document "Work on Analysis" selection.
 */
export function SuccessActions({
    successfulFiles,
    selectedNextSteps,
    onLaunchAnalysis,
    onLaunchFindSimilar,
    onClose,
}: ISuccessActionsProps): JSX.Element {
    const styles = useStyles();

    // -- Document picker state --
    const [showDocumentPicker, setShowDocumentPicker] = useState(false);
    const [selectedDocumentId, setSelectedDocumentId] = useState<string | null>(null);

    // -- Action visibility --
    const showAnalysis = selectedNextSteps.includes("work-on-analysis") && successfulFiles.length > 0;
    const showFindSimilar = selectedNextSteps.includes("find-similar") && successfulFiles.length > 0;

    // -- Handlers --
    const handleWorkOnAnalysis = useCallback(() => {
        if (successfulFiles.length === 1) {
            // Single document — launch directly
            const docId = successfulFiles[0].createResult?.documentId;
            if (docId) {
                onLaunchAnalysis(docId);
            }
        } else {
            // Multiple documents — show inline picker
            setShowDocumentPicker(true);
        }
    }, [successfulFiles, onLaunchAnalysis]);

    const handlePickerConfirm = useCallback(() => {
        if (selectedDocumentId) {
            onLaunchAnalysis(selectedDocumentId);
            setShowDocumentPicker(false);
        }
    }, [selectedDocumentId, onLaunchAnalysis]);

    const handlePickerCancel = useCallback(() => {
        setShowDocumentPicker(false);
        setSelectedDocumentId(null);
    }, []);

    // -- Document picker view --
    if (showDocumentPicker) {
        return (
            <div className={styles.actionsContainer}>
                <div className={styles.pickerSection}>
                    <Text size={400} weight="semibold">
                        Select a document to analyze
                    </Text>
                    <DocumentPicker
                        documents={successfulFiles}
                        selectedDocumentId={selectedDocumentId}
                        onSelectionChange={setSelectedDocumentId}
                    />
                    <div className={styles.pickerActions}>
                        <Button
                            appearance="primary"
                            disabled={!selectedDocumentId}
                            onClick={handlePickerConfirm}
                        >
                            Start Analysis
                        </Button>
                        <Button appearance="subtle" onClick={handlePickerCancel}>
                            Cancel
                        </Button>
                    </div>
                </div>
            </div>
        );
    }

    // -- Standard action buttons --
    return (
        <div className={styles.actionsRow}>
            {showAnalysis && (
                <Button
                    appearance="primary"
                    icon={<BrainCircuitRegular />}
                    onClick={handleWorkOnAnalysis}
                >
                    Work on Analysis
                </Button>
            )}
            {showFindSimilar && (
                <Button
                    appearance={showAnalysis ? "secondary" : "primary"}
                    icon={<DocumentSearchRegular />}
                    onClick={onLaunchFindSimilar}
                >
                    Find Similar
                </Button>
            )}
            <Button
                appearance={showAnalysis || showFindSimilar ? "subtle" : "primary"}
                icon={<DismissRegular />}
                onClick={onClose}
            >
                Close
            </Button>
        </div>
    );
}

// ---------------------------------------------------------------------------
// buildSuccessConfig — constructs the IWizardSuccessConfig
// ---------------------------------------------------------------------------

export interface IBuildSuccessConfigParams {
    /** Results from the upload orchestrator pipeline. */
    uploadResults: OrchestratorResult;
    /** Next-step action IDs selected by the user in the NextStepsStep. */
    selectedNextSteps: NextStepActionId[];
    /** Callback to launch the Analysis Builder for a specific document. */
    onLaunchAnalysis: (documentRecordId: string) => void;
    /** Callback to launch FindSimilarDialog. */
    onLaunchFindSimilar: () => void;
    /** Callback to close the wizard dialog. */
    onClose: () => void;
}

/**
 * Builds the IWizardSuccessConfig for display by WizardSuccessScreen.
 *
 * - Icon: green checkmark (all success), yellow warning (partial), red warning (all failed)
 * - Title: document count summary
 * - Body: descriptive text
 * - Actions: SuccessActions component (stateful, handles document picker)
 * - Warnings: per-file failure messages
 */
export function buildSuccessConfig({
    uploadResults,
    selectedNextSteps,
    onLaunchAnalysis,
    onLaunchFindSimilar,
    onClose,
}: IBuildSuccessConfigParams): IWizardSuccessConfig {
    const { successCount, failureCount, totalFiles, fileResults } = uploadResults;
    const hasFailures = failureCount > 0;
    const allFailed = successCount === 0 && totalFiles > 0;

    // -- Icon --
    const icon = allFailed ? (
        <WarningFilled
            style={{ fontSize: "64px", color: tokens.colorPaletteRedForeground1 }}
        />
    ) : hasFailures ? (
        <WarningFilled
            style={{ fontSize: "64px", color: tokens.colorPaletteYellowForeground1 }}
        />
    ) : (
        <CheckmarkCircleFilled
            style={{ fontSize: "64px", color: tokens.colorPaletteGreenForeground1 }}
        />
    );

    // -- Title --
    const title = allFailed
        ? "Upload failed"
        : hasFailures
            ? `${successCount} of ${totalFiles} documents uploaded`
            : `${successCount} document${successCount !== 1 ? "s" : ""} uploaded successfully`;

    // -- Body --
    const bodyText = allFailed
        ? "None of the selected files could be uploaded. Please check the errors below and try again."
        : hasFailures
            ? "Some files could not be uploaded. Successfully uploaded documents are available for further actions."
            : "All files have been uploaded and are ready for use.";

    const body = (
        <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
            {bodyText}
        </Text>
    );

    // -- Successful files for document picker --
    const successfulFiles = fileResults.filter(
        (fr) => fr.success && fr.createResult
    );

    // -- Actions (stateful component) --
    const actions = (
        <SuccessActions
            successfulFiles={successfulFiles}
            selectedNextSteps={selectedNextSteps}
            onLaunchAnalysis={onLaunchAnalysis}
            onLaunchFindSimilar={onLaunchFindSimilar}
            onClose={onClose}
        />
    );

    // -- Warnings --
    const warnings: string[] = [];
    for (const fr of fileResults) {
        if (!fr.success && fr.errorMessage) {
            warnings.push(`${fr.fileName}: ${fr.errorMessage}`);
        } else if (!fr.success) {
            warnings.push(`${fr.fileName}: Upload failed`);
        }
    }

    return {
        icon,
        title,
        body,
        actions,
        warnings: warnings.length > 0 ? warnings : undefined,
    };
}

/**
 * SuccessScreen.tsx
 * Builds the IWizardSuccessConfig for the Document Upload Wizard success screen.
 *
 * Simplified: next-step actions (Work on Analysis, Find Similar) are handled
 * inline as dynamic wizard steps before Finish. The success screen now only
 * shows the upload summary + a Close button.
 *
 * @see ADR-006  - Code Pages for standalone dialogs
 * @see ADR-021  - Fluent UI v9 design system (makeStyles + semantic tokens)
 */

import {
    tokens,
    Text,
    Button,
} from "@fluentui/react-components";
import {
    CheckmarkCircleRegular,
    WarningFilled,
} from "@fluentui/react-icons";

import type { IWizardSuccessConfig } from "@spaarke/ui-components/components/Wizard";
import type { OrchestratorResult } from "../services/uploadOrchestrator";

// ---------------------------------------------------------------------------
// buildSuccessConfig — constructs the IWizardSuccessConfig
// ---------------------------------------------------------------------------

export interface IBuildSuccessConfigParams {
    /** Results from the upload orchestrator pipeline (may be null if upload didn't complete). */
    uploadResults: OrchestratorResult | null;
    /** Callback to close the wizard dialog. */
    onClose: () => void;
}

/**
 * Builds the IWizardSuccessConfig for display by WizardSuccessScreen.
 *
 * - Icon: green checkmark (all success), yellow warning (partial), red warning (all failed)
 * - Title: document count summary
 * - Body: descriptive text
 * - Actions: Close button only (next-step actions handled in wizard dynamic steps)
 * - Warnings: per-file failure messages
 */
export function buildSuccessConfig({
    uploadResults,
    onClose,
}: IBuildSuccessConfigParams): IWizardSuccessConfig {
    // Close handler with fallback — try onClose first, then window.close()
    const handleDone = (): void => {
        try {
            onClose();
        } catch {
            // Fallback if onClose throws
        }
        // Double-ensure: also try window.close() directly for Dataverse dialog contexts
        try {
            window.close();
        } catch {
            // May be blocked in non-popup contexts — ignore
        }
    };

    // Handle case where upload didn't complete (shouldn't happen with Processing step)
    if (!uploadResults) {
        return {
            icon: (
                <WarningFilled
                    style={{ fontSize: "48px", color: tokens.colorPaletteYellowForeground1 }}
                />
            ),
            title: "Upload incomplete",
            body: (
                <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
                    The upload did not complete. Please try again.
                </Text>
            ),
            actions: (
                <Button appearance="primary" icon={<CheckmarkCircleRegular />} onClick={handleDone}>
                    Done
                </Button>
            ),
        };
    }

    const { successCount, failureCount, totalFiles, fileResults } = uploadResults;
    const hasFailures = failureCount > 0;
    const allFailed = successCount === 0 && totalFiles > 0;

    // -- Icon (smaller, only shown for warnings/failures) --
    const icon = allFailed ? (
        <WarningFilled
            style={{ fontSize: "48px", color: tokens.colorPaletteRedForeground1 }}
        />
    ) : hasFailures ? (
        <WarningFilled
            style={{ fontSize: "48px", color: tokens.colorPaletteYellowForeground1 }}
        />
    ) : null;

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
            ? "Some files could not be uploaded. Successfully uploaded documents are available in the document library."
            : "All files have been uploaded and are ready for use.";

    const body = (
        <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
            {bodyText}
        </Text>
    );

    // -- Actions --
    const actions = (
        <Button appearance="primary" icon={<CheckmarkCircleRegular />} onClick={handleDone}>
            Done
        </Button>
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

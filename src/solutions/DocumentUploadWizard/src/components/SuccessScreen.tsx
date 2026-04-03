/**
 * SuccessScreen.tsx
 * Builds the IWizardSuccessConfig for the Document Upload Wizard success screen.
 *
 * Shows created document links that open in new tabs, plus a Done button in the
 * wizard footer. The user can open multiple documents before closing.
 *
 * @see ADR-006  - Code Pages for standalone dialogs
 * @see ADR-021  - Fluent UI v9 design system (makeStyles + semantic tokens)
 */

import * as React from "react";
import {
    tokens,
    Text,
    Link,
    Button,
} from "@fluentui/react-components";
import {
    CheckmarkCircleRegular,
    WarningFilled,
    DocumentRegular,
    OpenRegular,
} from "@fluentui/react-icons";

import type { IWizardSuccessConfig } from "@spaarke/ui-components/components/Wizard";
import type { OrchestratorResult } from "../services/uploadOrchestrator";

// ---------------------------------------------------------------------------
// Xrm helper — resolve client URL for record links
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */
function getClientUrl(): string {
    try {
        const frames = [window, window.parent, window.top].filter(Boolean) as Window[];
        for (const frame of frames) {
            try {
                const url = (frame as any).Xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.();
                if (url) return url;
            } catch { /* cross-origin */ }
        }
    } catch { /* */ }
    return "";
}
/* eslint-enable @typescript-eslint/no-explicit-any */

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
 * - Title: "X documents created successfully"
 * - Body: descriptive text + clickable links to each created document record
 * - Actions: Done button (rendered in wizard footer)
 * - Warnings: per-file failure messages
 */
export function buildSuccessConfig({
    uploadResults,
    onClose,
}: IBuildSuccessConfigParams): IWizardSuccessConfig {
    const handleDone = (): void => {
        try { onClose(); } catch { /* */ }
        try { window.close(); } catch { /* */ }
    };

    // Handle case where upload didn't complete
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

    // -- Icon --
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
            ? `${successCount} of ${totalFiles} documents created`
            : `${successCount} document${successCount !== 1 ? "s" : ""} created successfully`;

    // -- Build document links for successfully created records --
    const clientUrl = getClientUrl();
    const createdDocs = fileResults.filter(
        (fr) => fr.success && fr.createResult?.recordId
    );

    // -- Body --
    const bodyText = allFailed
        ? "None of the selected files could be uploaded. Please check the errors below and try again."
        : hasFailures
            ? "Some files could not be uploaded. Successfully created documents are listed below."
            : "All files have been uploaded and are ready for use.";

    const body = (
        <div style={{ display: "flex", flexDirection: "column", gap: tokens.spacingVerticalS, alignItems: "center" }}>
            <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
                {bodyText}
            </Text>
            {createdDocs.length > 0 && clientUrl && (
                <div style={{
                    display: "flex",
                    flexDirection: "column",
                    gap: tokens.spacingVerticalXS,
                    marginTop: tokens.spacingVerticalM,
                    alignItems: "flex-start",
                    width: "100%",
                    maxWidth: "400px",
                }}>
                    {createdDocs.map((fr) => {
                        const recordUrl = `${clientUrl}/main.aspx?etn=sprk_document&id=${fr.createResult!.recordId}&pagetype=entityrecord`;
                        return (
                            <Link
                                key={fr.createResult!.recordId}
                                href={recordUrl}
                                target="_blank"
                                style={{
                                    display: "flex",
                                    alignItems: "center",
                                    gap: tokens.spacingHorizontalS,
                                }}
                            >
                                <DocumentRegular fontSize={16} />
                                <span>{fr.fileName}</span>
                                <OpenRegular fontSize={12} style={{ color: tokens.colorNeutralForeground3 }} />
                            </Link>
                        );
                    })}
                </div>
            )}
        </div>
    );

    // -- Actions (rendered in wizard footer) --
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

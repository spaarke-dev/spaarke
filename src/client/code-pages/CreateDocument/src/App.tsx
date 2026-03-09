/**
 * App.tsx
 * CreateDocument Code Page — WizardShell orchestrator.
 *
 * Wires three wizard steps:
 *   Step 1 — FileUploadStep:    Drag-and-drop file selection
 *   Step 2 — DocumentDetailsStep: Name, type, description form
 *   Step 3 — NextStepsStep:     Optional follow-on action checklist
 *
 * handleFinish flow:
 *   1. Upload files to SPE via PUT /api/obo/containers/{containerId}/files/{path}
 *   2. Create sprk_document record in Dataverse
 *   3. Show success state
 *
 * URL parameters (passed from index.tsx):
 *   - containerId: SPE container ID for file uploads
 *   - matterId:    Optional related matter record ID
 *
 * @see ADR-006  - Code Pages for standalone dialogs (not PCF)
 * @see ADR-007  - Document access through BFF API (SpeFileStore facade)
 * @see ADR-008  - Endpoint filters for auth (Bearer token from Xrm SDK)
 * @see ADR-021  - Fluent UI v9 design system (makeStyles + design tokens)
 */

import { useState, useCallback, useMemo } from "react";
import {
    makeStyles,
    tokens,
    Text,
    Button,
    Spinner,
    MessageBar,
    MessageBarBody,
} from "@fluentui/react-components";
import { CheckmarkCircleRegular } from "@fluentui/react-icons";

import { FileUploadStep } from "./components/FileUploadStep";
import { DocumentDetailsStep } from "./components/DocumentDetailsStep";
import { CreateDocumentNextStepsStep } from "./components/CreateDocumentNextStepsStep";
import { uploadFiles } from "./services/documentUploadService";
import { createDocumentRecord } from "./services/documentRecordService";
import type {
    IUploadedFile,
    IDocumentFormValues,
    NextStepActionId,
} from "./types";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface AppProps {
    /** SPE container ID for file uploads. */
    containerId: string;
    /** Optional related matter record ID. */
    matterId: string;
}

// ---------------------------------------------------------------------------
// Wizard phase
// ---------------------------------------------------------------------------

type WizardPhase = "wizard" | "processing" | "success" | "error";

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
    // -- Stepper sidebar --
    mainArea: {
        display: "flex",
        flex: 1,
        overflow: "hidden",
    },
    sidebar: {
        display: "flex",
        flexDirection: "column",
        width: "220px",
        flexShrink: 0,
        paddingTop: tokens.spacingVerticalXL,
        paddingBottom: tokens.spacingVerticalL,
        paddingLeft: tokens.spacingHorizontalL,
        paddingRight: tokens.spacingHorizontalM,
        backgroundColor: tokens.colorNeutralBackground2,
        borderRightWidth: "1px",
        borderRightStyle: "solid",
        borderRightColor: tokens.colorNeutralStroke2,
        gap: tokens.spacingVerticalS,
    },
    stepItem: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalS,
        paddingRight: tokens.spacingHorizontalS,
        borderRadius: tokens.borderRadiusMedium,
        cursor: "default",
    },
    stepItemActive: {
        backgroundColor: tokens.colorBrandBackground2,
    },
    stepItemCompleted: {
        color: tokens.colorNeutralForeground3,
    },
    stepNumber: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        width: "24px",
        height: "24px",
        borderRadius: "50%",
        fontSize: "12px",
        fontWeight: 600,
        backgroundColor: tokens.colorNeutralBackground4,
        color: tokens.colorNeutralForeground2,
        flexShrink: 0,
    },
    stepNumberActive: {
        backgroundColor: tokens.colorBrandBackground,
        color: tokens.colorNeutralForegroundOnBrand,
    },
    stepLabel: {
        color: tokens.colorNeutralForeground1,
    },
    stepLabelInactive: {
        color: tokens.colorNeutralForeground3,
    },
    // -- Content area --
    contentArea: {
        flex: 1,
        display: "flex",
        flexDirection: "column",
        overflowY: "auto",
        paddingTop: tokens.spacingVerticalXL,
        paddingBottom: tokens.spacingVerticalL,
        paddingLeft: tokens.spacingHorizontalXXL,
        paddingRight: tokens.spacingHorizontalXXL,
    },
    // -- Footer --
    footer: {
        display: "flex",
        alignItems: "center",
        justifyContent: "flex-end",
        gap: tokens.spacingHorizontalS,
        paddingTop: tokens.spacingVerticalM,
        paddingBottom: tokens.spacingVerticalM,
        paddingLeft: tokens.spacingHorizontalXL,
        paddingRight: tokens.spacingHorizontalXL,
        borderTopWidth: "1px",
        borderTopStyle: "solid",
        borderTopColor: tokens.colorNeutralStroke2,
        backgroundColor: tokens.colorNeutralBackground1,
        flexShrink: 0,
    },
    // -- Processing / Success states --
    centerState: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        flex: 1,
        gap: tokens.spacingVerticalM,
        padding: tokens.spacingVerticalXXL,
        textAlign: "center",
    },
    successIcon: {
        color: tokens.colorPaletteGreenForeground1,
        fontSize: "64px",
    },
    successTitle: {
        color: tokens.colorNeutralForeground1,
    },
    successBody: {
        color: tokens.colorNeutralForeground2,
        maxWidth: "400px",
    },
    successActions: {
        display: "flex",
        gap: tokens.spacingHorizontalS,
        marginTop: tokens.spacingVerticalM,
    },
});

// ---------------------------------------------------------------------------
// Step definitions
// ---------------------------------------------------------------------------

const STEP_DEFS = [
    { id: "upload", label: "Upload Files" },
    { id: "details", label: "Document Details" },
    { id: "next-steps", label: "Next Steps" },
] as const;

// ---------------------------------------------------------------------------
// App component
// ---------------------------------------------------------------------------

export function App({ containerId, matterId }: AppProps): JSX.Element {
    const styles = useStyles();

    // -- Step navigation state --
    const [currentStep, setCurrentStep] = useState(0);

    // -- File state --
    const [files, setFiles] = useState<IUploadedFile[]>([]);

    // -- Form state --
    const [formValues, setFormValues] = useState<IDocumentFormValues>({
        name: "",
        documentType: "",
        description: "",
    });

    // -- Next steps state --
    const [selectedActions, setSelectedActions] = useState<NextStepActionId[]>([]);

    // -- Wizard phase --
    const [phase, setPhase] = useState<WizardPhase>("wizard");
    const [processingMessage, setProcessingMessage] = useState("");
    const [errorMessage, setErrorMessage] = useState("");
    const [createdDocumentId, setCreatedDocumentId] = useState("");

    // -- Step advancement predicates --
    const canAdvance = useMemo(() => {
        switch (currentStep) {
            case 0:
                return files.length > 0;
            case 1:
                return formValues.name.trim().length > 0;
            case 2:
                return true; // Next Steps is always advanceable
            default:
                return false;
        }
    }, [currentStep, files.length, formValues.name]);

    const isLastStep = currentStep === STEP_DEFS.length - 1;

    // -- File handlers --
    const handleFilesAdded = useCallback((newFiles: IUploadedFile[]) => {
        setFiles((prev) => {
            const existing = new Set(prev.map((f) => `${f.name}::${f.sizeBytes}`));
            const unique = newFiles.filter((f) => !existing.has(`${f.name}::${f.sizeBytes}`));
            return [...prev, ...unique];
        });
    }, []);

    const handleFileRemoved = useCallback((fileId: string) => {
        setFiles((prev) => prev.filter((f) => f.id !== fileId));
    }, []);

    // -- Navigation --
    const handleNext = useCallback(() => {
        if (isLastStep) {
            void handleFinish();
        } else {
            setCurrentStep((s) => Math.min(s + 1, STEP_DEFS.length - 1));
        }
    }, [isLastStep]);

    const handleBack = useCallback(() => {
        setCurrentStep((s) => Math.max(s - 1, 0));
    }, []);

    // -- Finish handler --
    const handleFinish = useCallback(async () => {
        setPhase("processing");
        setErrorMessage("");

        try {
            // Step 1: Upload files
            if (containerId && files.length > 0) {
                setProcessingMessage("Uploading files...");

                const uploadResults = await uploadFiles(
                    containerId,
                    files,
                    (fileId, progress) => {
                        setFiles((prev) =>
                            prev.map((f) =>
                                f.id === fileId
                                    ? { ...f, progress, uploadStatus: "uploading" as const }
                                    : f,
                            ),
                        );
                    },
                );

                // Mark completed/errored
                setFiles((prev) =>
                    prev.map((f) => {
                        const result = uploadResults.find((r) => r.fileName === f.name);
                        if (result?.success) {
                            return { ...f, uploadStatus: "complete" as const, progress: 100 };
                        }
                        if (result && !result.success) {
                            return {
                                ...f,
                                uploadStatus: "error" as const,
                                uploadError: result.error,
                            };
                        }
                        return f;
                    }),
                );

                const failedUploads = uploadResults.filter((r) => !r.success);
                if (failedUploads.length > 0 && failedUploads.length === uploadResults.length) {
                    throw new Error("All file uploads failed. Please check your connection and try again.");
                }

                // Step 2: Create document record
                setProcessingMessage("Creating document record...");
                const driveItemIds = uploadResults
                    .filter((r) => r.success && r.driveItemId)
                    .map((r) => r.driveItemId!);

                const createResult = await createDocumentRecord(
                    formValues,
                    matterId || undefined,
                    driveItemIds,
                );

                if (!createResult.success) {
                    throw new Error(createResult.error ?? "Failed to create document record.");
                }

                setCreatedDocumentId(createResult.documentId ?? "");
            } else {
                // No container — just create the Dataverse record
                setProcessingMessage("Creating document record...");
                const createResult = await createDocumentRecord(
                    formValues,
                    matterId || undefined,
                );

                if (!createResult.success) {
                    throw new Error(createResult.error ?? "Failed to create document record.");
                }

                setCreatedDocumentId(createResult.documentId ?? "");
            }

            setPhase("success");
        } catch (err) {
            setErrorMessage(err instanceof Error ? err.message : "An unexpected error occurred.");
            setPhase("error");
        }
    }, [containerId, files, formValues, matterId]);

    // -- Close handler --
    const handleClose = useCallback(() => {
        // Close the dialog by navigating back or closing the window
        try {
            /* eslint-disable @typescript-eslint/no-explicit-any */
            const xrm = (window as any).Xrm;
            if (xrm?.Navigation?.navigateTo) {
                // Attempt to close the dialog by calling the close callback
                window.close();
            } else {
                window.close();
            }
            /* eslint-enable @typescript-eslint/no-explicit-any */
        } catch {
            window.close();
        }
    }, []);

    // -- Open created document --
    const handleOpenDocument = useCallback(() => {
        if (!createdDocumentId) return;
        try {
            /* eslint-disable @typescript-eslint/no-explicit-any */
            const xrm = (window as any).Xrm;
            if (xrm?.Navigation?.openForm) {
                xrm.Navigation.openForm({
                    entityName: "sprk_document",
                    entityId: createdDocumentId,
                });
            }
            /* eslint-enable @typescript-eslint/no-explicit-any */
        } catch {
            // Fallback — no-op outside Dataverse
        }
        handleClose();
    }, [createdDocumentId, handleClose]);

    // -- Render step content --
    const renderStepContent = (): JSX.Element => {
        switch (currentStep) {
            case 0:
                return (
                    <FileUploadStep
                        files={files}
                        onFilesAdded={handleFilesAdded}
                        onFileRemoved={handleFileRemoved}
                    />
                );
            case 1:
                return (
                    <DocumentDetailsStep
                        formValues={formValues}
                        onFormChange={setFormValues}
                    />
                );
            case 2:
                return (
                    <CreateDocumentNextStepsStep
                        selectedActions={selectedActions}
                        onSelectionChange={setSelectedActions}
                    />
                );
            default:
                return <Text>Unknown step</Text>;
        }
    };

    // -- Processing state --
    if (phase === "processing") {
        return (
            <div className={styles.root}>
                <div className={styles.centerState}>
                    <Spinner size="large" label={processingMessage || "Processing..."} />
                    <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                        {processingMessage}
                    </Text>
                </div>
            </div>
        );
    }

    // -- Success state --
    if (phase === "success") {
        return (
            <div className={styles.root}>
                <div className={styles.centerState}>
                    <CheckmarkCircleRegular className={styles.successIcon} />
                    <Text as="h2" size={500} weight="semibold" className={styles.successTitle}>
                        Document created
                    </Text>
                    <Text size={300} className={styles.successBody}>
                        <strong>{formValues.name}</strong> has been created
                        {files.length > 0 ? ` with ${files.length} file(s) uploaded` : ""}
                        {selectedActions.length > 0
                            ? `. Follow-on actions: ${selectedActions.join(", ")}`
                            : ""}
                        .
                    </Text>
                    <div className={styles.successActions}>
                        {createdDocumentId && (
                            <Button appearance="primary" onClick={handleOpenDocument}>
                                Open Document
                            </Button>
                        )}
                        <Button appearance="secondary" onClick={handleClose}>
                            Close
                        </Button>
                    </div>
                </div>
            </div>
        );
    }

    // -- Error state --
    if (phase === "error") {
        return (
            <div className={styles.root}>
                <div className={styles.centerState}>
                    <MessageBar intent="error">
                        <MessageBarBody>{errorMessage}</MessageBarBody>
                    </MessageBar>
                    <div className={styles.successActions}>
                        <Button
                            appearance="primary"
                            onClick={() => {
                                setPhase("wizard");
                                setErrorMessage("");
                            }}
                        >
                            Try Again
                        </Button>
                        <Button appearance="secondary" onClick={handleClose}>
                            Close
                        </Button>
                    </div>
                </div>
            </div>
        );
    }

    // -- Wizard state --
    return (
        <div className={styles.root}>
            <div className={styles.mainArea}>
                {/* Sidebar stepper */}
                <div className={styles.sidebar}>
                    {STEP_DEFS.map((step, idx) => {
                        const isActive = idx === currentStep;
                        const isCompleted = idx < currentStep;
                        return (
                            <div
                                key={step.id}
                                className={`${styles.stepItem}${isActive ? ` ${styles.stepItemActive}` : ""}${isCompleted ? ` ${styles.stepItemCompleted}` : ""}`}
                            >
                                <span
                                    className={`${styles.stepNumber}${isActive ? ` ${styles.stepNumberActive}` : ""}`}
                                >
                                    {isCompleted ? "\u2713" : idx + 1}
                                </span>
                                <Text
                                    size={300}
                                    weight={isActive ? "semibold" : "regular"}
                                    className={isActive ? styles.stepLabel : styles.stepLabelInactive}
                                >
                                    {step.label}
                                </Text>
                            </div>
                        );
                    })}
                </div>

                {/* Content area */}
                <div className={styles.contentArea}>{renderStepContent()}</div>
            </div>

            {/* Footer */}
            <div className={styles.footer}>
                {currentStep > 0 && (
                    <Button appearance="secondary" onClick={handleBack}>
                        Back
                    </Button>
                )}
                <Button
                    appearance="primary"
                    onClick={handleNext}
                    disabled={!canAdvance}
                >
                    {isLastStep ? "Finish" : "Next"}
                </Button>
            </div>
        </div>
    );
}

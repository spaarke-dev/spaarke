/**
 * DocumentUploadWizardDialog.tsx
 * Domain orchestrator for the Document Upload Wizard.
 *
 * Manages all domain state and wires three wizard steps into the generic
 * WizardShell component:
 *   Step 1 — Add Files:   File selection via drag-and-drop / browse (AddFilesStep)
 *   Step 2 — Summary:     Upload progress (FileUploadProgress) then Document Profile (SummaryStep)
 *   Step 3 — Next Steps:  Optional follow-on actions (NextStepsStep)
 *
 * Upload pipeline (auto-triggered when entering Step 2 — Summary):
 *   Phase 1: Upload files to SPE via MultiFileUploadService (parallel)
 *   Phase 2: Create sprk_document records via DocumentRecordService (OData)
 *   Phase 3: Document Profile playbook (fire-and-forget, visible in SummaryStep)
 *   Phase 4: RAG indexing (fire-and-forget)
 *
 * State management:
 *   - fileState via useReducer (selected files, validation errors, upload progress)
 *   - orchestratorProgress via useState (per-file pipeline progress during upload)
 *   - uploadedDocumentMap via useState (maps local file ID -> Dataverse metadata)
 *   - Step 2/3 state via useState (summary results, selected next steps)
 *   - Refs for closure safety in renderContent callbacks (prevents stale closures)
 *
 * @see ADR-006  - Code Pages for standalone dialogs (not PCF)
 * @see ADR-007  - Document access through BFF API (SpeFileStore facade)
 * @see ADR-021  - Fluent UI v9 design system (makeStyles + semantic tokens)
 */

import { useReducer, useState, useCallback, useRef, useMemo, useEffect } from "react";
import {
    makeStyles,
    tokens,
} from "@fluentui/react-components";

import { getAuthProvider, authenticatedFetch } from "@spaarke/auth";

import { WizardShell } from "@spaarke/ui-components/components/Wizard";
import type {
    IWizardStepConfig,
    IWizardShellHandle,
    IWizardSuccessConfig,
} from "@spaarke/ui-components/components/Wizard";

import type {
    IDocumentUploadWizardDialogProps,
    IFileState,
    FileAction,
    IUploadedFile,
    IFileValidationError,
    ISummaryResults,
    NextStepActionId,
    IResolvedParentContext,
} from "./types";

import { AddFilesStep } from "./components/AddFilesStep";
import { AssociateToStep, resolveXrm, resolveBusinessUnitContainerId } from "./components/AssociateToStep";
import { SummaryStep } from "./components/SummaryStep";
import type { UploadedDocumentInfo } from "./components/SummaryStep";
import { NextStepsStep } from "./components/NextStepsStep";
import type { IDocumentEmailStepProps } from "./components/DocumentEmailStep";
// BFF base URL is resolved at runtime via resolveRuntimeConfig() in main.tsx
// and set on window.__SPAARKE_BFF_BASE_URL__ before React renders.
import { createBffTokenProvider } from "./services/codePageTokenProvider";
import { createCodePageDataverseClient } from "./services/codePageDataverseClient";
import {
    orchestrateUpload,
    defaultEntityConfigResolver,
} from "./services/uploadOrchestrator";
import type {
    OrchestratorFileProgress,
    OrchestratorResult,
} from "./services/uploadOrchestrator";
import { FileUploadProgress } from "./components/FileUploadProgress";
import { buildSuccessConfig } from "./components/SuccessScreen";
// nextStepLauncher is no longer used here — inline playbook/find-similar in NextStepsStep

// ---------------------------------------------------------------------------
// AutoUploadTrigger — starts the upload pipeline on mount (step 2 entry)
// ---------------------------------------------------------------------------

function AutoUploadTrigger({ onStart }: { onStart: () => void }): null {
    const triggered = useRef(false);
    useEffect(() => {
        if (!triggered.current) {
            triggered.current = true;
            onStart();
        }
    }, [onStart]);
    return null;
}

// (FindSimilarDialog is no longer inline — opens in new tab via nextStepLauncher)

// ---------------------------------------------------------------------------
// File state reducer
// ---------------------------------------------------------------------------

const INITIAL_FILE_STATE: IFileState = {
    selectedFiles: [],
    validationErrors: [],
    uploadProgress: [],
};

function fileReducer(state: IFileState, action: FileAction): IFileState {
    switch (action.type) {
        case "ADD_FILES": {
            // De-duplicate by name + size (same logic as LegalWorkspace reference)
            const existing = new Set(
                state.selectedFiles.map((f) => `${f.name}::${f.sizeBytes}`)
            );
            const newFiles = action.files.filter(
                (f) => !existing.has(`${f.name}::${f.sizeBytes}`)
            );
            return {
                ...state,
                selectedFiles: [...state.selectedFiles, ...newFiles],
                validationErrors: [], // Clear errors on successful add
            };
        }
        case "REMOVE_FILE":
            return {
                ...state,
                selectedFiles: state.selectedFiles.filter((f) => f.id !== action.fileId),
            };
        case "SET_VALIDATION_ERRORS":
            return { ...state, validationErrors: action.errors };
        case "CLEAR_VALIDATION_ERRORS":
            return { ...state, validationErrors: [] };
        case "START_UPLOAD":
            return {
                ...state,
                uploadProgress: state.selectedFiles.map((f) => ({
                    fileId: f.id,
                    status: "uploading" as const,
                    progressPercent: 0,
                })),
            };
        case "UPDATE_PROGRESS":
            return {
                ...state,
                uploadProgress: state.uploadProgress.map((p) =>
                    p.fileId === action.fileId
                        ? { ...p, progressPercent: action.progressPercent }
                        : p
                ),
            };
        case "UPLOAD_FILE_COMPLETED":
            return {
                ...state,
                uploadProgress: state.uploadProgress.map((p) =>
                    p.fileId === action.fileId
                        ? { ...p, status: "completed" as const, progressPercent: 100 }
                        : p
                ),
            };
        case "UPLOAD_FILE_FAILED":
            return {
                ...state,
                uploadProgress: state.uploadProgress.map((p) =>
                    p.fileId === action.fileId
                        ? { ...p, status: "failed" as const, errorMessage: action.errorMessage }
                        : p
                ),
            };
        case "RESET":
            return INITIAL_FILE_STATE;
        default:
            return state;
    }
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
// DocumentUploadWizardDialog (exported)
// ---------------------------------------------------------------------------

export function DocumentUploadWizardDialog({
    parentEntityType,
    parentEntityId,
    parentEntityName,
    containerId,
    onClose,
}: IDocumentUploadWizardDialogProps): JSX.Element {
    const styles = useStyles();

    // ---------------------------------------------------------------------------
    // BFF config (resolved at render time from window global set by bootstrap())
    // NOTE: Must be inside the component body — module-level code runs synchronously
    // at bundle parse time, before the async bootstrap() in main.tsx can set this.
    // ---------------------------------------------------------------------------
    const bffBaseUrl = window.__SPAARKE_BFF_BASE_URL__ ?? (() => {
        throw new Error(
            '[DocumentUploadWizard] window.__SPAARKE_BFF_BASE_URL__ is not set. ' +
            'resolveRuntimeConfig() must be called in main.tsx before rendering.'
        );
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
    const bffTokenProvider = useMemo(() => createBffTokenProvider(), []);
    const wizardRef = useRef<IWizardShellHandle>(null);

    // ── Standalone mode detection ───────────────────────────────────────────
    const isStandaloneMode = !parentEntityType || !parentEntityId;

    // ── Standalone state (AssociateToStep resolution) ────────────────────────
    const [resolvedParent, setResolvedParent] = useState<IResolvedParentContext | null>(null);
    const [isUnassociated, setIsUnassociated] = useState(false);
    const resolvedParentRef = useRef(resolvedParent);
    resolvedParentRef.current = resolvedParent;

    // Eagerly resolve BU container ID so it's ready if user clicks Skip on AssociateToStep.
    const buContainerIdRef = useRef<string>("");
    useEffect(() => {
        if (!isStandaloneMode) return;
        const xrm = resolveXrm();
        if (!xrm) return;
        resolveBusinessUnitContainerId(xrm)
            .then((id) => { buContainerIdRef.current = id; })
            .catch((err) => console.warn("[DocumentUploadWizard] BU container pre-resolve failed:", err));
    }, [isStandaloneMode]);

    // ── Effective values (bridge raw props vs AssociateToStep resolution) ────
    // When standalone and user Skipped associate-to, resolvedParent is null.
    // Fall back to BU container (pre-resolved at mount) for unassociated uploads.
    const effectiveParentEntityType = isStandaloneMode ? (resolvedParent?.parentEntityType ?? "") : parentEntityType;
    const effectiveParentEntityId = isStandaloneMode ? (resolvedParent?.parentEntityId ?? "") : parentEntityId;
    const effectiveParentEntityName = isStandaloneMode ? (resolvedParent?.parentEntityName ?? "") : parentEntityName;
    const effectiveContainerId = isStandaloneMode ? (resolvedParent?.containerId || buContainerIdRef.current || "") : containerId;
    const effectiveIsUnassociated = isStandaloneMode && (resolvedParent === null || resolvedParent?.isUnassociated === true);

    // ── File state (useReducer) ─────────────────────────────────────────────
    const [fileState, fileDispatch] = useReducer(fileReducer, INITIAL_FILE_STATE);

    // ── Upload orchestrator state ───────────────────────────────────────────
    const [orchestratorProgress, setOrchestratorProgress] = useState<OrchestratorFileProgress[]>([]);
    const [uploadResult, setUploadResult] = useState<OrchestratorResult | null>(null);
    const [isUploading, setIsUploading] = useState(false);

    // ── Step 2 state: uploaded document map + profiling status ─────────────
    // uploadedDocumentMap is populated by the upload pipeline (tasks 012/014)
    // after files are uploaded to SPE. Maps local file ID -> document metadata.
    const [uploadedDocumentMap] = useState<Map<string, UploadedDocumentInfo>>(
        () => new Map()
    );
    const [_summaryResults, _setSummaryResults] = useState<ISummaryResults | null>(null);
    const [isProfileProcessing, setIsProfileProcessing] = useState(false);

    // ── Step 3 state: selected next steps ──────────────────────────────
    const [selectedNextSteps, setSelectedNextSteps] = useState<NextStepActionId[]>([]);

    // (Find Similar now opens in a new tab via nextStepLauncher — no inline state needed)

    // ── Refs for closure safety in renderContent callbacks ───────────────────
    // WizardShell step configs use renderContent callbacks that may capture
    // stale closures. Refs ensure we always read the latest state values.
    const fileStateRef = useRef(fileState);
    fileStateRef.current = fileState;
    const uploadedDocumentMapRef = useRef(uploadedDocumentMap);
    uploadedDocumentMapRef.current = uploadedDocumentMap;
    const isProfileProcessingRef = useRef(isProfileProcessing);
    isProfileProcessingRef.current = isProfileProcessing;
    const selectedNextStepsRef = useRef(selectedNextSteps);
    selectedNextStepsRef.current = selectedNextSteps;
    const uploadResultRef = useRef(uploadResult);
    uploadResultRef.current = uploadResult;
    const isUploadingRef = useRef(isUploading);
    isUploadingRef.current = isUploading;

    // ── File handler callbacks ──────────────────────────────────────────────
    const handleFilesAdded = useCallback(
        (files: IUploadedFile[]) => fileDispatch({ type: "ADD_FILES", files }),
        []
    );

    const handleValidationErrors = useCallback(
        (errors: IFileValidationError[]) =>
            fileDispatch({ type: "SET_VALIDATION_ERRORS", errors }),
        []
    );

    const handleFileRemoved = useCallback(
        (fileId: string) => fileDispatch({ type: "REMOVE_FILE", fileId }),
        []
    );

    const handleClearErrors = useCallback(
        () => fileDispatch({ type: "CLEAR_VALIDATION_ERRORS" }),
        []
    );

    // ── Orchestrator progress handler ───────────────────────────────────────
    const handleOrchestratorProgress = useCallback(
        (progress: OrchestratorFileProgress) => {
            setOrchestratorProgress((prev) => {
                const idx = prev.findIndex((p) => p.fileName === progress.fileName);
                if (idx >= 0) {
                    const updated = [...prev];
                    updated[idx] = progress;
                    return updated;
                }
                return [...prev, progress];
            });
        },
        []
    );

    // ── Run upload pipeline ─────────────────────────────────────────────────
    const runUploadPipeline = useCallback(async (): Promise<OrchestratorResult> => {
        const selectedFiles = fileStateRef.current.selectedFiles;

        // Convert IUploadedFile[] to File[] via the .file property
        const nativeFiles: File[] = selectedFiles
            .map((f) => f.file)
            .filter((f): f is File => f != null);

        if (nativeFiles.length === 0) {
            throw new Error("No files to upload. Please add files in Step 1.");
        }

        setIsUploading(true);
        setOrchestratorProgress([]);
        setUploadResult(null);
        fileDispatch({ type: "START_UPLOAD" });

        try {
            const dataverseClient = createCodePageDataverseClient();

            const result = await orchestrateUpload(
                nativeFiles,
                {
                    bffBaseUrl,
                    bffTokenProvider,
                    dataverseClient,
                    entityConfigResolver: defaultEntityConfigResolver,
                    parentContext: {
                        parentEntityName: effectiveParentEntityType,
                        parentRecordId: effectiveParentEntityId,
                        containerId: effectiveContainerId,
                        parentDisplayName: effectiveParentEntityName,
                    },
                    onUnauthorized: () => {
                        try {
                            getAuthProvider().clearCache();
                        } catch {
                            // Ignore if auth not initialized
                        }
                    },
                },
                handleOrchestratorProgress,
            );

            setUploadResult(result);

            // Populate uploadedDocumentMap for SummaryStep (Document Profile streaming)
            for (const fileResult of result.fileResults) {
                if (fileResult.success && fileResult.createResult?.recordId && fileResult.speMetadata) {
                    const matchingFile = selectedFiles.find((f) => f.name === fileResult.fileName);
                    if (matchingFile) {
                        uploadedDocumentMap.set(matchingFile.id, {
                            documentId: fileResult.createResult.recordId,
                            driveId: fileResult.speMetadata.parentId ?? effectiveContainerId,
                            itemId: fileResult.speMetadata.id,
                        });
                    }
                }
            }

            // Update summary results
            const totalBytes = selectedFiles.reduce((sum, f) => sum + (f.sizeBytes ?? 0), 0);
            _setSummaryResults({
                successCount: result.successCount,
                failureCount: result.failureCount,
                totalBytesUploaded: totalBytes,
            });

            return result;
        } finally {
            setIsUploading(false);
        }
    }, [effectiveParentEntityType, effectiveParentEntityId, effectiveParentEntityName, effectiveContainerId, handleOrchestratorProgress, uploadedDocumentMap]);

    // ── Email step props (memoized for the dynamic Send Email step) ────────
    const emailStepProps: IDocumentEmailStepProps = useMemo(
        () => ({
            uploadedFileNames: fileState.selectedFiles.map((f) => f.name),
            parentEntityName: effectiveParentEntityName,
            parentEntityType: effectiveParentEntityType,
            parentEntityId: effectiveParentEntityId,
        }),
        [fileState.selectedFiles, effectiveParentEntityName, effectiveParentEntityType, effectiveParentEntityId]
    );

    // ── Step configurations ─────────────────────────────────────────────────

    const stepConfigs: IWizardStepConfig[] = useMemo(
        () => {
            const steps: IWizardStepConfig[] = [];

            // Standalone mode: prepend AssociateToStep (skippable — Skip uploads without association)
            if (isStandaloneMode) {
                steps.push({
                    id: "associate-to",
                    label: "Associate To",
                    canAdvance: () => resolvedParentRef.current !== null && !resolvedParentRef.current.isUnassociated && resolvedParentRef.current.containerId !== "",
                    isSkippable: true,
                    renderContent: (handle: IWizardShellHandle) => (
                        <AssociateToStep
                            resolvedParent={resolvedParent}
                            onParentResolved={(ctx) => {
                                setResolvedParent(ctx);
                                // When Skip advances past this step with no selection,
                                // WizardShell sets resolvedParent via the effect below.
                            }}
                        />
                    ),
                });
            }

            steps.push({
                id: "add-files",
                label: "Add Files",
                canAdvance: () => fileStateRef.current.selectedFiles.length > 0,
                renderContent: (_handle: IWizardShellHandle) => (
                    <AddFilesStep
                        files={fileState.selectedFiles}
                        onFilesAdded={handleFilesAdded}
                        onFileRemoved={handleFileRemoved}
                        parentEntityName={effectiveParentEntityName}
                        parentEntityType={effectiveParentEntityType}
                        validationErrors={fileState.validationErrors}
                        onClearErrors={handleClearErrors}
                        isUnassociated={effectiveIsUnassociated}
                    />
                ),
            });
            steps.push({
                id: "processing",
                label: "Processing",
                canAdvance: () => {
                    const result = uploadResultRef.current;
                    return result !== null && !isUploadingRef.current;
                },
                renderContent: (_handle: IWizardShellHandle) => {
                    // After upload completes: show SummaryStep with Document Profile streaming
                    if (uploadResult && uploadedDocumentMap.size > 0) {
                        return (
                            <SummaryStep
                                files={fileState.selectedFiles}
                                apiBaseUrl={bffBaseUrl}
                                getToken={bffTokenProvider}
                                uploadedDocumentMap={uploadedDocumentMap}
                                onProcessingChange={setIsProfileProcessing}
                            />
                        );
                    }

                    // Upload complete but all files failed — show progress with errors
                    if (uploadResult) {
                        return <FileUploadProgress fileProgress={orchestratorProgress} />;
                    }

                    // Auto-trigger upload when entering Processing step
                    return (
                        <>
                            <AutoUploadTrigger onStart={() => void runUploadPipeline()} />
                            <FileUploadProgress fileProgress={orchestratorProgress} />
                        </>
                    );
                },
            });

            steps.push({
                id: "next-steps",
                label: "Next Steps",
                canAdvance: () => true,
                isEarlyFinish: () => selectedNextStepsRef.current.length === 0,
                renderContent: (_handle: IWizardShellHandle) => (
                    <NextStepsStep
                        selectedNextSteps={selectedNextSteps}
                        onNextStepsChanged={setSelectedNextSteps}
                        wizardShellRef={wizardRef}
                        emailStepProps={emailStepProps}
                        uploadedDocumentMap={uploadedDocumentMapRef.current}
                        uploadedFiles={fileStateRef.current.selectedFiles}
                        containerId={effectiveContainerId}
                        bffBaseUrl={bffBaseUrl}
                        bffTokenProvider={bffTokenProvider}
                    />
                ),
            });

            return steps;
        },
        [
            isStandaloneMode,
            resolvedParent,
            isUnassociated,
            fileState.selectedFiles,
            fileState.validationErrors,
            orchestratorProgress,
            uploadResult,
            isUploading,
            uploadedDocumentMap,
            selectedNextSteps,
            emailStepProps,
            effectiveParentEntityName,
            effectiveParentEntityType,
            effectiveIsUnassociated,
            handleFilesAdded,
            handleFileRemoved,
            handleClearErrors,
        ]
    );

    // ── Finish handler ──────────────────────────────────────────────────────

    const handleFinish = useCallback(async (): Promise<IWizardSuccessConfig | void> => {
        // Upload is guaranteed complete by the Processing step.
        const result = uploadResultRef.current;

        return buildSuccessConfig({
            uploadResults: result,
            onClose,
        });
    }, [onClose]);

    // ── Render ──────────────────────────────────────────────────────────────

    return (
        <div className={styles.root}>
            <WizardShell
                ref={wizardRef}
                open={true}
                embedded={true}
                hideTitle={true}
                title={
                    effectiveParentEntityName
                        ? `Upload Files \u2014 ${effectiveParentEntityName}`
                        : effectiveIsUnassociated
                            ? "Upload Files \u2014 General"
                            : "Upload Files"
                }
                steps={stepConfigs}
                onClose={onClose}
                onFinish={handleFinish}
                finishLabel="Finish"
                finishingLabel="Processing..."
            />

            {/* Find Similar now opens in a new tab via nextStepLauncher */}
        </div>
    );
}

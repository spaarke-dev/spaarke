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

import { useReducer, useState, useCallback, useRef, useMemo, useEffect, lazy, Suspense } from "react";
import {
    makeStyles,
    tokens,
    Text,
    Spinner,
} from "@fluentui/react-components";

import { getAuthProvider, authenticatedFetch } from "@spaarke/auth";

import { WizardShell } from "@spaarke/ui-components/components/Wizard";
import type {
    IWizardStepConfig,
    IWizardShellHandle,
    IWizardSuccessConfig,
} from "@spaarke/ui-components/components/Wizard";
import type { IFindSimilarServiceConfig, INavigationMessage } from "@spaarke/ui-components/components/FindSimilar/findSimilarTypes";
import type { IFilePreviewServices } from "@spaarke/ui-components/components/FilePreview/filePreviewTypes";

import type {
    IDocumentUploadWizardDialogProps,
    IFileState,
    FileAction,
    IUploadedFile,
    IFileValidationError,
    ISummaryResults,
    NextStepActionId,
} from "./types";

import { AddFilesStep } from "./components/AddFilesStep";
import { SummaryStep } from "./components/SummaryStep";
import type { UploadedDocumentInfo } from "./components/SummaryStep";
import { NextStepsStep } from "./components/NextStepsStep";
import type { IDocumentEmailStepProps } from "./components/DocumentEmailStep";
import { getBffBaseUrl } from "./config/bffConfig";
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
import { openAnalysisBuilder } from "./services/nextStepLauncher";

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

// ---------------------------------------------------------------------------
// Lazy-loaded FindSimilarDialog (chunk only fetched on user interaction)
// ---------------------------------------------------------------------------

const LazyFindSimilarDialog = lazy(
    () => import("@spaarke/ui-components/components/FindSimilar/FindSimilarDialog")
);

// ---------------------------------------------------------------------------
// FindSimilarDialog service configuration (stable singletons)
// ---------------------------------------------------------------------------

const findSimilarServiceConfig: IFindSimilarServiceConfig = {
    getBffBaseUrl,
    authenticatedFetch,
};

/**
 * Stub file preview services for the FindSimilarDialog.
 * The Document Upload Wizard does not need full file preview capabilities
 * within the Find Similar results, so these are minimal no-op implementations.
 * If richer preview is needed later, wire in real BFF-backed implementations.
 */
const findSimilarFilePreviewServices: IFilePreviewServices = {
    getDocumentPreviewUrl: async () => null,
    getDocumentOpenLinks: async () => null,
    navigateToEntity: (params) => {
        // Open Dataverse record in new tab via Xrm or URL fallback
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = (window as any).Xrm ?? (window as any).parent?.Xrm;
        if (xrm?.Navigation?.navigateTo) {
            xrm.Navigation.navigateTo(
                { pageType: "entityrecord", entityName: params.entityName, entityId: params.entityId },
                { target: params.openInNewWindow ? 1 : 2 },
            );
        }
    },
    copyDocumentLink: async () => false,
    setWorkspaceFlag: async () => false,
};

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
// BFF config (resolved once, reused across renders)
// ---------------------------------------------------------------------------

const bffBaseUrl = getBffBaseUrl();
const bffTokenProvider = createBffTokenProvider();

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
    const wizardRef = useRef<IWizardShellHandle>(null);

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

    // ── Find Similar state (inline rendering) ───────────────────────────
    const [isFindSimilarOpen, setIsFindSimilarOpen] = useState(false);

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
                        parentEntityName: parentEntityType,
                        parentRecordId: parentEntityId,
                        containerId,
                        parentDisplayName: parentEntityName,
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
                            driveId: fileResult.speMetadata.parentId ?? containerId,
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
    }, [parentEntityType, parentEntityId, parentEntityName, containerId, handleOrchestratorProgress, uploadedDocumentMap]);

    // ── Email step props (memoized for the dynamic Send Email step) ────────
    const emailStepProps: IDocumentEmailStepProps = useMemo(
        () => ({
            uploadedFileNames: fileState.selectedFiles.map((f) => f.name),
            parentEntityName,
            parentEntityType,
            parentEntityId,
        }),
        [fileState.selectedFiles, parentEntityName, parentEntityType, parentEntityId]
    );

    // ── Step configurations ─────────────────────────────────────────────────

    const stepConfigs: IWizardStepConfig[] = useMemo(
        () => [
            {
                id: "add-files",
                label: "Add Files",
                canAdvance: () => fileStateRef.current.selectedFiles.length > 0,
                renderContent: (_handle: IWizardShellHandle) => (
                    <AddFilesStep
                        files={fileState.selectedFiles}
                        onFilesAdded={handleFilesAdded}
                        onFileRemoved={handleFileRemoved}
                        parentEntityName={parentEntityName}
                        parentEntityType={parentEntityType}
                        validationErrors={fileState.validationErrors}
                        onClearErrors={handleClearErrors}
                    />
                ),
            },
            {
                id: "summary",
                label: "Summary",
                canAdvance: () => {
                    // Can advance once upload is done (Phases 1-2 complete)
                    // Profiling (Phase 3) is non-blocking -- user can advance anytime
                    const result = uploadResultRef.current;
                    return result !== null && !isUploadingRef.current;
                },
                renderContent: (_handle: IWizardShellHandle) => {
                    // During upload: show per-file upload progress (Phases 1-2)
                    if (isUploading || (!uploadResult && orchestratorProgress.length > 0)) {
                        return <FileUploadProgress fileProgress={orchestratorProgress} />;
                    }

                    // After upload: show SummaryStep with Document Profile streaming
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

                    // Upload complete but all files failed -- show progress with errors
                    if (uploadResult) {
                        return <FileUploadProgress fileProgress={orchestratorProgress} />;
                    }

                    // Auto-trigger upload when entering step 2
                    return (
                        <>
                            <AutoUploadTrigger onStart={() => void runUploadPipeline()} />
                            <FileUploadProgress fileProgress={[]} />
                        </>
                    );
                },
            },
            {
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
                        onLaunchAnalysis={() => {
                            // Launch analysis for the first successfully uploaded document
                            const firstDoc = uploadedDocumentMapRef.current.values().next().value;
                            if (firstDoc) {
                                handleLaunchAnalysis(firstDoc.documentId);
                            }
                        }}
                        onLaunchFindSimilar={handleLaunchFindSimilar}
                    />
                ),
            },
        ],
        [
            fileState.selectedFiles,
            fileState.validationErrors,
            orchestratorProgress,
            uploadResult,
            isUploading,
            uploadedDocumentMap,
            selectedNextSteps,
            emailStepProps,
            parentEntityName,
            parentEntityType,
            handleFilesAdded,
            handleFileRemoved,
            handleClearErrors,
        ]
    );

    // ── Next-step launcher callbacks ────────────────────────────────────

    const handleLaunchAnalysis = useCallback(
        (documentRecordId: string) => {
            console.log("[DocumentUploadWizard] Launching Analysis Builder for:", documentRecordId);
            openAnalysisBuilder(documentRecordId, containerId);
        },
        [containerId],
    );

    const handleLaunchFindSimilar = useCallback(() => {
        console.log("[DocumentUploadWizard] Opening Find Similar dialog (inline).");
        setIsFindSimilarOpen(true);
    }, []);

    const handleCloseFindSimilar = useCallback(() => {
        setIsFindSimilarOpen(false);
    }, []);

    const handleFindSimilarNavigate = useCallback((message: INavigationMessage) => {
        findSimilarFilePreviewServices.navigateToEntity({
            action: message.action === "openRecord" ? "openRecord" : "openRecord",
            entityName: message.entityName,
            entityId: message.entityId ?? "",
            openInNewWindow: message.openInNewWindow,
        });
    }, []);

    // ── Finish handler ──────────────────────────────────────────────────────

    const handleFinish = useCallback(async (): Promise<IWizardSuccessConfig | void> => {
        // Wait for in-progress upload or trigger if not started
        let result = uploadResultRef.current;

        if (!result && !isUploadingRef.current) {
            try {
                result = await runUploadPipeline();
            } catch (err) {
                const message = err instanceof Error ? err.message : "Upload failed";
                throw new Error(message);
            }
        } else if (!result && isUploadingRef.current) {
            // Upload is in progress — wait for it to finish
            await new Promise<void>((resolve) => {
                const interval = setInterval(() => {
                    if (uploadResultRef.current) {
                        clearInterval(interval);
                        result = uploadResultRef.current;
                        resolve();
                    }
                }, 200);
            });
        }

        // Build success/failure screen via shared SuccessScreen component
        return buildSuccessConfig({
            uploadResults: result,
            selectedNextSteps: selectedNextStepsRef.current,
            onLaunchAnalysis: handleLaunchAnalysis,
            onLaunchFindSimilar: handleLaunchFindSimilar,
            onClose,
        });
    }, [onClose, runUploadPipeline, handleLaunchAnalysis, handleLaunchFindSimilar]);

    // ── Render ──────────────────────────────────────────────────────────────

    return (
        <div className={styles.root}>
            <WizardShell
                ref={wizardRef}
                open={true}
                embedded={true}
                title={
                    parentEntityName
                        ? `Upload Files \u2014 ${parentEntityName}`
                        : "Upload Files"
                }
                steps={stepConfigs}
                onClose={onClose}
                onFinish={handleFinish}
                finishLabel="Finish"
                finishingLabel="Processing..."
            />

            {/* Find Similar dialog — rendered inline, lazy-loaded on first open */}
            {isFindSimilarOpen && (
                <Suspense fallback={<Spinner label="Loading Find Similar..." />}>
                    <LazyFindSimilarDialog
                        open={isFindSimilarOpen}
                        onClose={handleCloseFindSimilar}
                        serviceConfig={findSimilarServiceConfig}
                        onNavigateToEntity={handleFindSimilarNavigate}
                        filePreviewServices={findSimilarFilePreviewServices}
                    />
                </Suspense>
            )}
        </div>
    );
}

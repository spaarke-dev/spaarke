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
import { MailRegular, CheckmarkCircleRegular } from "@fluentui/react-icons";
import { ChoiceModal } from "@spaarke/ui-components/components/SprkModal";

import { getAuthProvider, authenticatedFetch, resolveTenantIdSync } from "@spaarke/auth";

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
import { AssociateToStep } from "./components/AssociateToStep";
import { SummaryStep } from "./components/SummaryStep";
import type { UploadedDocumentInfo } from "./components/SummaryStep";
import { NextStepsStep } from "./components/NextStepsStep";
import type { IDocumentEmailStepProps, IDocumentEmailComposeController } from "./components/DocumentEmailStep";
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
import type { ConflictResolution } from "./components/FileUploadProgress";
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
// Result merging (single-file collision retries)
// ---------------------------------------------------------------------------

/**
 * Fold a retry's outcome into the batch result.
 *
 * `next` describes ONLY the retried file(s). Replacing `prev` with it would drop every other file
 * from the counts, from `_summaryResults`, and from the `uploadResults` payload the Next Steps step
 * consumes. Files in `prev` that were not retried are carried through unchanged.
 */
function mergeOrchestratorResults(
    prev: OrchestratorResult,
    next: OrchestratorResult,
): OrchestratorResult {
    const fileResults = prev.fileResults.map(
        (r) => next.fileResults.find((n) => n.fileName === r.fileName) ?? r,
    );

    // Defensive: a retried file that was somehow absent from `prev` still belongs in the result.
    for (const n of next.fileResults) {
        if (!fileResults.some((r) => r.fileName === n.fileName)) {
            fileResults.push(n);
        }
    }

    const successCount = fileResults.filter((r) => r.success).length;
    return {
        success: successCount > 0,
        totalFiles: fileResults.length,
        successCount,
        failureCount: fileResults.length - successCount,
        fileResults,
    };
}

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

    // 🔴 The eager business-unit container pre-resolve that stood here was DELETED 2026-09-03
    // (task 076). It existed so a container was ready the moment the user clicked Skip; under the
    // record-keyed contract the client never names a container at all, and "skip associate" goes to
    // `PUT /api/obo/me/files/{path}`, where the SERVER derives the acting user's BU container. The
    // client was reading Dataverse on mount to compute an answer it is no longer asked for.

    // ── Effective values (bridge raw props vs AssociateToStep resolution) ────
    // When standalone and the user skipped associate-to, resolvedParent is null and both identifiers
    // stay empty — which is exactly what `resolveUploadTarget` reads as "no owning record".
    const effectiveParentEntityType = isStandaloneMode ? (resolvedParent?.parentEntityType ?? "") : parentEntityType;
    const effectiveParentEntityId = isStandaloneMode ? (resolvedParent?.parentEntityId ?? "") : parentEntityId;
    const effectiveParentEntityName = isStandaloneMode ? (resolvedParent?.parentEntityName ?? "") : parentEntityName;
    const effectiveIsUnassociated = isStandaloneMode && (resolvedParent === null || resolvedParent?.isUnassociated === true);

    // ── File state (useReducer) ─────────────────────────────────────────────
    const [fileState, fileDispatch] = useReducer(fileReducer, INITIAL_FILE_STATE);

    // ── Upload orchestrator state ───────────────────────────────────────────
    const [orchestratorProgress, setOrchestratorProgress] = useState<OrchestratorFileProgress[]>([]);
    const [uploadResult, setUploadResult] = useState<OrchestratorResult | null>(null);
    const [isUploading, setIsUploading] = useState(false);
    /** File names whose collision retry is in flight — disables their buttons, shows a spinner. */
    const [resolvingFileNames, setResolvingFileNames] = useState<ReadonlySet<string>>(
        () => new Set<string>()
    );

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

    // ── Send Email Finish-guard ────────────────────────────────────────
    // The Send Email step registers a controller here; on Finish we check for an unsent
    // composed email and prompt (Send / Finish without sending / Keep editing).
    const emailControllerRef = useRef<IDocumentEmailComposeController | null>(null);
    const [unsentPrompt, setUnsentPrompt] = useState<{ resolve: (choice: "send" | "finish" | "cancel") => void } | null>(null);

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
    /**
     * Run the pipeline over the selected files, or over a SUBSET when retrying.
     *
     * A retry (`options.onlyFileNames` set) must not reset the progress list or the result — the
     * other files' outcomes are still on screen and still valid. It merges into them instead.
     */
    const runUploadPipeline = useCallback(async (options?: {
        /** Restrict this run to these file names. Omit to run the whole selection. */
        onlyFileNames?: readonly string[];
        /** Collision resolution to apply to this run. Only meaningful with `onlyFileNames`. */
        conflictBehavior?: ConflictResolution;
    }): Promise<OrchestratorResult> => {
        const selectedFiles = fileStateRef.current.selectedFiles;
        const retryNames = options?.onlyFileNames;
        const isRetry = retryNames != null;

        // Convert IUploadedFile[] to File[] via the .file property
        const nativeFiles: File[] = selectedFiles
            .filter((f) => !isRetry || retryNames!.includes(f.name))
            .map((f) => f.file)
            .filter((f): f is File => f != null);

        if (nativeFiles.length === 0) {
            throw new Error(
                isRetry
                    ? "The file to retry is no longer available. Please add it again in Step 1."
                    : "No files to upload. Please add files in Step 1.",
            );
        }

        setIsUploading(true);
        if (!isRetry) {
            setOrchestratorProgress([]);
            setUploadResult(null);
            fileDispatch({ type: "START_UPLOAD" });
        }

        try {
            const dataverseClient = createCodePageDataverseClient();

            // Resolve tenantId for RAG indexing (Phase 4).
            // resolveTenantIdSync reads from cached JWT tid claim — fast, synchronous.
            let tenantId = '';
            try {
                tenantId = resolveTenantIdSync() || getAuthProvider().getCachedTenantId();
            } catch {
                // Auth may not be initialized; tenantId will be empty and Phase 4 will log a warning
            }

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
                        parentDisplayName: effectiveParentEntityName,
                    },
                    tenantId,
                    onUnauthorized: () => {
                        try {
                            getAuthProvider().clearCache();
                        } catch {
                            // Ignore if auth not initialized
                        }
                    },
                },
                handleOrchestratorProgress,
                options?.conflictBehavior,
            );

            // On a retry, fold this run's per-file outcomes into the existing result rather than
            // replacing it — `result` only describes the retried file, so assigning it directly
            // would drop every other file from the counts and from the Next Steps payload.
            const effectiveResult =
                isRetry && uploadResultRef.current
                    ? mergeOrchestratorResults(uploadResultRef.current, result)
                    : result;

            setUploadResult(effectiveResult);

            // Populate uploadedDocumentMap for SummaryStep (Document Profile streaming)
            for (const fileResult of effectiveResult.fileResults) {
                if (fileResult.success && fileResult.createResult?.recordId && fileResult.speMetadata) {
                    const matchingFile = selectedFiles.find((f) => f.name === fileResult.fileName);
                    if (matchingFile) {
                        uploadedDocumentMap.set(matchingFile.id, {
                            documentId: fileResult.createResult.recordId,
                            // The SERVER's drive for this file. `parentId` is the parent FOLDER,
                            // not the drive — it was only ever a stand-in because the two happened
                            // to coincide at the container root. The `?? effectiveContainerId`
                            // fallback behind it named the client-resolved container and is gone
                            // with the rest of that plumbing (task 076).
                            driveId: fileResult.speMetadata.driveId ?? "",
                            itemId: fileResult.speMetadata.id,
                        });
                    }
                }
            }

            // Update summary results
            const totalBytes = selectedFiles.reduce((sum, f) => sum + (f.sizeBytes ?? 0), 0);
            _setSummaryResults({
                successCount: effectiveResult.successCount,
                failureCount: effectiveResult.failureCount,
                totalBytesUploaded: totalBytes,
            });

            return effectiveResult;
        } finally {
            setIsUploading(false);
        }
    }, [effectiveParentEntityType, effectiveParentEntityId, effectiveParentEntityName, handleOrchestratorProgress, uploadedDocumentMap]);

    // ── Name-collision resolution ───────────────────────────────────────────
    /**
     * Retry ONE file with the collision resolution the user picked.
     *
     * Nothing was written when the collision was reported (the BFF uploads with
     * `conflictBehavior=fail` by default), so this is a clean re-run of the full pipeline for that
     * file — upload, Dataverse record, and indexing — not a patch-up of a partial write.
     */
    const handleResolveConflict = useCallback(
        (fileName: string, resolution: ConflictResolution) => {
            setResolvingFileNames((prev) => new Set(prev).add(fileName));
            void runUploadPipeline({ onlyFileNames: [fileName], conflictBehavior: resolution })
                .catch(() => {
                    // orchestrateUpload reports per-file failures through progress; a throw here is
                    // a pipeline-level fault, already surfaced on the row. Swallow so the finally
                    // below always clears the spinner.
                })
                .finally(() => {
                    setResolvingFileNames((prev) => {
                        const next = new Set(prev);
                        next.delete(fileName);
                        return next;
                    });
                });
        },
        [runUploadPipeline],
    );

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
                    // The `&& resolvedParentRef.current.containerId !== ""` clause was DELETED
                    // 2026-09-03 (task 076). It blocked Next whenever the CLIENT could not resolve
                    // a container for the selected record — a lookup the upload no longer performs
                    // or consults. Keeping it would have gated the wizard on a question the client
                    // is no longer in a position to answer, and refused records the server can
                    // resolve perfectly well. A record that genuinely has no resolvable container
                    // now fails per file, with the SERVER's reason, on the Processing step.
                    canAdvance: () => resolvedParentRef.current !== null && !resolvedParentRef.current.isUnassociated,
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
                    const progressPane = (
                        <FileUploadProgress
                            fileProgress={orchestratorProgress}
                            onResolveConflict={handleResolveConflict}
                            resolvingFileNames={resolvingFileNames}
                        />
                    );

                    // After upload completes: show SummaryStep with Document Profile streaming.
                    // Any file still waiting on a collision decision keeps its row ABOVE the
                    // summary — otherwise one successful file hides the choice entirely and the
                    // pending file is silently dropped from the batch.
                    if (uploadResult && uploadedDocumentMap.size > 0) {
                        const hasPendingConflict = orchestratorProgress.some(
                            (p) => p.phase === "error" && p.nameConflict != null,
                        );
                        return (
                            <>
                                {hasPendingConflict && progressPane}
                                <SummaryStep
                                    files={fileState.selectedFiles}
                                    apiBaseUrl={bffBaseUrl}
                                    getToken={bffTokenProvider}
                                    uploadedDocumentMap={uploadedDocumentMap}
                                    onProcessingChange={setIsProfileProcessing}
                                />
                            </>
                        );
                    }

                    // Upload complete but all files failed — show progress with errors
                    if (uploadResult) {
                        return progressPane;
                    }

                    // Auto-trigger upload when entering Processing step
                    return (
                        <>
                            <AutoUploadTrigger onStart={() => void runUploadPipeline()} />
                            {progressPane}
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
                        bffBaseUrl={bffBaseUrl}
                        bffTokenProvider={bffTokenProvider}
                        onEmailControllerChange={(c) => { emailControllerRef.current = c; }}
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
            // Both are read by the Processing step's progress pane: without them the retry buttons
            // fire a stale closure and the in-flight spinner never appears.
            handleResolveConflict,
            resolvingFileNames,
        ]
    );

    // ── Finish handler ──────────────────────────────────────────────────────

    const handleFinish = useCallback(async (): Promise<IWizardSuccessConfig | void> => {
        // Send Email Finish-guard: if the user composed an email (entered recipients) on the
        // Send Email step but hasn't sent it, prompt before finishing.
        const controller = emailControllerRef.current;
        if (controller?.hasUnsentEmail()) {
            const choice = await new Promise<"send" | "finish" | "cancel">((resolve) => {
                setUnsentPrompt({ resolve });
            });
            setUnsentPrompt(null);
            if (choice === "cancel") {
                // Abort the finish and keep the wizard open on the step. An empty message
                // leaves WizardShell's finishError falsy, so no error bar is shown.
                throw new Error("");
            }
            if (choice === "send") {
                const ok = await controller.send();
                if (!ok) {
                    throw new Error("The email could not be sent. Please check the recipients and try again.");
                }
            }
            // "finish" (or a successful "send") falls through to complete the wizard.
        }

        // Upload is guaranteed complete by the Processing step.
        return buildSuccessConfig({
            uploadResults: uploadResultRef.current,
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

            {unsentPrompt && (
                <ChoiceModal
                    open={true}
                    onClose={() => unsentPrompt.resolve("cancel")}
                    title="Send your email?"
                    message="You've started an email but haven't sent it yet."
                    cancelLabel="Keep editing"
                    choices={[
                        {
                            id: "send",
                            label: "Send email",
                            description:
                                "Send it now from the Spaarke shared mailbox with the uploaded documents attached, then finish.",
                            icon: <MailRegular />,
                        },
                        {
                            id: "finish",
                            label: "Finish without sending",
                            description: "Finish the upload without sending — the email won't be sent.",
                            icon: <CheckmarkCircleRegular />,
                        },
                    ]}
                    onSelect={(id) => unsentPrompt.resolve(id as "send" | "finish")}
                />
            )}

            {/* Find Similar now opens in a new tab via nextStepLauncher */}
        </div>
    );
}

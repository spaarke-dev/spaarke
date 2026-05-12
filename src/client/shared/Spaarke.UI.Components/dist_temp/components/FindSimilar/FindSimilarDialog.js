/**
 * FindSimilarDialog.tsx
 * Two-step wizard dialog for "Find Similar".
 *
 * Uses WizardShell with 2 steps:
 *   0 — Upload file(s)   (FileUploadZone + UploadedFileList — from shared FileUpload)
 *   1 — Results           (FindSimilarResultsStep — tabbed grid of Documents / Matters / Projects)
 *
 * Shared library version — all external dependencies are injected via props:
 *   - IFindSimilarServiceConfig for BFF calls (authenticatedFetch, getBffBaseUrl)
 *   - IFilePreviewServices for the document preview dialog
 *   - onNavigateToEntity for Dataverse record navigation
 */
import * as React from 'react';
import { Button, MessageBar, MessageBarBody, Text, makeStyles, tokens } from '@fluentui/react-components';
import { CheckmarkCircleFilled } from '@fluentui/react-icons';
import { WizardShell } from '../Wizard/WizardShell';
import { FileUploadZone } from '../FileUpload/FileUploadZone';
import { UploadedFileList } from '../FileUpload/UploadedFileList';
import { FindSimilarResultsStep } from './FindSimilarResultsStep';
import { runFindSimilar } from './findSimilarService';
function fileReducer(state, action) {
    switch (action.type) {
        case 'ADD_FILES': {
            const existing = new Set(state.uploadedFiles.map(f => `${f.name}::${f.sizeBytes}`));
            const newFiles = action.files.filter(f => !existing.has(`${f.name}::${f.sizeBytes}`));
            return {
                ...state,
                uploadedFiles: [...state.uploadedFiles, ...newFiles],
                validationErrors: [],
            };
        }
        case 'REMOVE_FILE':
            return {
                ...state,
                uploadedFiles: state.uploadedFiles.filter(f => f.id !== action.fileId),
            };
        case 'SET_VALIDATION_ERRORS':
            return { ...state, validationErrors: action.errors };
        case 'CLEAR_VALIDATION_ERRORS':
            return { ...state, validationErrors: [] };
        case 'RESET':
            return { uploadedFiles: [], validationErrors: [] };
        default:
            return state;
    }
}
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    stepTitle: {
        display: 'block',
        color: tokens.colorNeutralForeground1,
        marginBottom: tokens.spacingVerticalXS,
    },
    stepSubtitle: {
        display: 'block',
        color: tokens.colorNeutralForeground3,
        marginBottom: tokens.spacingVerticalM,
    },
    errorBar: {
        flexShrink: 0,
    },
});
// ---------------------------------------------------------------------------
// FindSimilarDialog
// ---------------------------------------------------------------------------
export const FindSimilarDialog = ({ open, onClose, serviceConfig, onNavigateToEntity, filePreviewServices, }) => {
    const styles = useStyles();
    // -- File state --
    const [fileState, fileDispatch] = React.useReducer(fileReducer, {
        uploadedFiles: [],
        validationErrors: [],
    });
    // -- Search state --
    const [searchStatus, setSearchStatus] = React.useState('idle');
    const [searchResults, setSearchResults] = React.useState(null);
    const [searchError, setSearchError] = React.useState(null);
    const abortControllerRef = React.useRef(null);
    // -- Reset on open --
    React.useEffect(() => {
        if (open) {
            fileDispatch({ type: 'RESET' });
            setSearchStatus('idle');
            setSearchResults(null);
            setSearchError(null);
        }
        return () => {
            abortControllerRef.current?.abort();
        };
    }, [open]);
    // -- File handlers --
    const handleFilesAccepted = React.useCallback((files) => fileDispatch({ type: 'ADD_FILES', files }), []);
    const handleValidationErrors = React.useCallback((errors) => fileDispatch({ type: 'SET_VALIDATION_ERRORS', errors }), []);
    const handleRemoveFile = React.useCallback((fileId) => fileDispatch({ type: 'REMOVE_FILE', fileId }), []);
    const handleClearErrors = React.useCallback(() => fileDispatch({ type: 'CLEAR_VALIDATION_ERRORS' }), []);
    // -- Run search --
    const runSearch = React.useCallback(async () => {
        if (fileState.uploadedFiles.length === 0)
            return;
        abortControllerRef.current?.abort();
        const controller = new AbortController();
        abortControllerRef.current = controller;
        setSearchStatus('loading');
        setSearchError(null);
        try {
            const result = await runFindSimilar(fileState.uploadedFiles, serviceConfig, controller.signal);
            if (!controller.signal.aborted) {
                setSearchResults(result);
                setSearchStatus('success');
            }
        }
        catch (err) {
            if (!controller.signal.aborted) {
                const message = err instanceof Error ? err.message : 'An unknown error occurred.';
                setSearchError(message);
                setSearchStatus('error');
            }
        }
    }, [fileState.uploadedFiles, serviceConfig]);
    // Auto-run search when entering Step 2
    const searchAttemptedRef = React.useRef(false);
    // Reset search flag when files change
    React.useEffect(() => {
        searchAttemptedRef.current = false;
        setSearchStatus('idle');
        setSearchResults(null);
        setSearchError(null);
    }, [fileState.uploadedFiles.length]);
    // -- handleFinish --
    const handleFinish = React.useCallback(async () => {
        const currentResults = searchResults;
        const totalFound = currentResults
            ? currentResults.documentsTotalCount + currentResults.mattersTotalCount + currentResults.projectsTotalCount
            : 0;
        console.info('[FindSimilarDialog] Finish with', totalFound, 'total results');
        return {
            icon: React.createElement(CheckmarkCircleFilled, { fontSize: 64, style: { color: tokens.colorPaletteGreenForeground1 } }),
            title: 'Search Complete',
            body: (React.createElement(Text, { size: 300, style: { color: tokens.colorNeutralForeground2 } },
                "Found ",
                totalFound,
                " similar item",
                totalFound !== 1 ? 's' : '',
                " across documents, matters, and projects.")),
            actions: (React.createElement(Button, { appearance: "secondary", onClick: onClose }, "Close")),
        };
    }, [searchResults, onClose]);
    // -- Step configurations --
    const stepConfigs = React.useMemo(() => [
        // Step 0: Upload file(s)
        {
            id: 'upload-files',
            label: 'Upload file(s)',
            canAdvance: () => fileState.uploadedFiles.length > 0,
            renderContent: () => (React.createElement(React.Fragment, null,
                React.createElement("div", null,
                    React.createElement(Text, { as: "h2", size: 500, weight: "semibold", className: styles.stepTitle }, "Upload file(s)"),
                    React.createElement(Text, { size: 200, className: styles.stepSubtitle }, "Upload one or more documents. The AI will extract text and search for similar documents, matters, and projects.")),
                fileState.validationErrors.length > 0 && (React.createElement(MessageBar, { intent: "error", className: styles.errorBar, onMouseEnter: handleClearErrors },
                    React.createElement(MessageBarBody, null, fileState.validationErrors.map((err, i) => (React.createElement("div", { key: i },
                        React.createElement("strong", null, err.fileName),
                        ": ",
                        err.reason)))))),
                React.createElement(FileUploadZone, { onFilesAccepted: handleFilesAccepted, onValidationErrors: handleValidationErrors }),
                fileState.uploadedFiles.length > 0 && (React.createElement(UploadedFileList, { files: fileState.uploadedFiles, onRemove: handleRemoveFile })))),
        },
        // Step 1: Results
        {
            id: 'results',
            label: 'Results',
            canAdvance: () => searchStatus === 'success' && searchResults !== null,
            isEarlyFinish: () => searchStatus === 'success' && searchResults !== null,
            renderContent: () => {
                // Auto-trigger search on first render of this step
                if (!searchAttemptedRef.current && searchStatus === 'idle') {
                    searchAttemptedRef.current = true;
                    Promise.resolve().then(() => runSearch());
                }
                return (React.createElement(FindSimilarResultsStep, { status: searchStatus, results: searchResults, errorMessage: searchError, onRetry: runSearch, onNavigateToEntity: onNavigateToEntity, filePreviewServices: filePreviewServices }));
            },
        },
    ], [
        fileState.uploadedFiles,
        fileState.validationErrors,
        searchStatus,
        searchResults,
        searchError,
        styles,
        handleFilesAccepted,
        handleValidationErrors,
        handleRemoveFile,
        handleClearErrors,
        runSearch,
        onNavigateToEntity,
        filePreviewServices,
    ]);
    // -- Render --
    return (React.createElement(WizardShell, { open: open, title: "Find Similar Records", ariaLabel: "Find Similar Records", steps: stepConfigs, onClose: onClose, onFinish: handleFinish, finishingLabel: "Processing\u2026", finishLabel: "Done" }));
};
// Default export enables React.lazy() dynamic import for bundle-size optimization.
export default FindSimilarDialog;
//# sourceMappingURL=FindSimilarDialog.js.map
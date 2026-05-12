/**
 * AddFilesStep.tsx
 * Step 2: "Add Files" -- upload new files to include with the work assignment.
 *
 * Documents from the associated record (step 1) are already available --
 * this step only handles NEW file uploads.
 *
 * This step is skippable (canAdvance always true).
 */
import * as React from 'react';
import { Text, makeStyles, tokens, } from '@fluentui/react-components';
import { FileUploadZone } from '../FileUpload/FileUploadZone';
import { UploadedFileList } from '../FileUpload/UploadedFileList';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    root: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalL,
    },
    headerText: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
    },
    stepTitle: {
        color: tokens.colorNeutralForeground1,
    },
    stepSubtitle: {
        color: tokens.colorNeutralForeground3,
    },
});
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
export const AddFilesStep = ({ onUploadedFilesChange, initialUploadedFiles, }) => {
    const styles = useStyles();
    const [uploadedFiles, setUploadedFiles] = React.useState(initialUploadedFiles ?? []);
    // Report uploaded files whenever they change
    React.useEffect(() => {
        onUploadedFilesChange(uploadedFiles);
    }, [uploadedFiles, onUploadedFilesChange]);
    const handleFilesAccepted = React.useCallback((files) => {
        setUploadedFiles((prev) => [...prev, ...files]);
    }, []);
    const handleRemoveFile = React.useCallback((fileId) => {
        setUploadedFiles((prev) => prev.filter((f) => f.id !== fileId));
    }, []);
    return (React.createElement("div", { className: styles.root },
        React.createElement("div", { className: styles.headerText },
            React.createElement(Text, { as: "h2", size: 500, weight: "semibold", className: styles.stepTitle }, "Add Files"),
            React.createElement(Text, { size: 200, className: styles.stepSubtitle }, "Upload files to include with the work assignment. Documents from the associated record are already available.")),
        React.createElement(FileUploadZone, { onFilesAccepted: handleFilesAccepted, onValidationErrors: () => { } }),
        uploadedFiles.length > 0 && (React.createElement(UploadedFileList, { files: uploadedFiles, onRemove: handleRemoveFile }))));
};
//# sourceMappingURL=AddFilesStep.js.map
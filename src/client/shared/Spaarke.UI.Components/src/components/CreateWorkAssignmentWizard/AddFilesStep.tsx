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
import {
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { FileUploadZone } from '../FileUpload/FileUploadZone';
import { UploadedFileList } from '../FileUpload/UploadedFileList';
import type { IUploadedFile } from '../FileUpload/fileUploadTypes';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IAddFilesStepProps {
  /** Called with the list of uploaded files whenever it changes. */
  onUploadedFilesChange: (files: IUploadedFile[]) => void;
  initialUploadedFiles?: IUploadedFile[];
}

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

export const AddFilesStep: React.FC<IAddFilesStepProps> = ({
  onUploadedFilesChange,
  initialUploadedFiles,
}) => {
  const styles = useStyles();

  const [uploadedFiles, setUploadedFiles] = React.useState<IUploadedFile[]>(
    initialUploadedFiles ?? []
  );

  // Report uploaded files whenever they change
  React.useEffect(() => {
    onUploadedFilesChange(uploadedFiles);
  }, [uploadedFiles, onUploadedFilesChange]);

  const handleFilesAccepted = React.useCallback(
    (files: IUploadedFile[]) => {
      setUploadedFiles((prev) => [...prev, ...files]);
    },
    []
  );

  const handleRemoveFile = React.useCallback(
    (fileId: string) => {
      setUploadedFiles((prev) => prev.filter((f) => f.id !== fileId));
    },
    []
  );

  return (
    <div className={styles.root}>
      <div className={styles.headerText}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Add Files
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Upload files to include with the work assignment. Documents from the
          associated record are already available.
        </Text>
      </div>

      <FileUploadZone onFilesAccepted={handleFilesAccepted} onValidationErrors={() => {}} />
      {uploadedFiles.length > 0 && (
        <UploadedFileList files={uploadedFiles} onRemove={handleRemoveFile} />
      )}
    </div>
  );
};

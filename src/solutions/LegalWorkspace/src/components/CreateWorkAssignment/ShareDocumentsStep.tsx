/**
 * ShareDocumentsStep.tsx
 * Step 2: "Share Documents" — link existing docs + upload new files.
 *
 * Section 1: "Link Documents" — LookupField → add to chip list
 * Section 2: "Upload Files" — FileUploadZone + UploadedFileList
 *
 * This step is skippable (canAdvance always true).
 */
import * as React from 'react';
import {
  Text,
  Tag,
  TagGroup,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { LookupField } from '../../../../../client/shared/Spaarke.UI.Components/src/components/LookupField/LookupField';
import { FileUploadZone } from '../../../../../client/shared/Spaarke.UI.Components/src/components/FileUpload/FileUploadZone';
import { UploadedFileList } from '../../../../../client/shared/Spaarke.UI.Components/src/components/FileUpload/UploadedFileList';
import type { ILookupItem } from '../../../../../client/shared/Spaarke.UI.Components/src/types/LookupTypes';
import type { IUploadedFile } from '../CreateMatter/wizardTypes';
import { WorkAssignmentService } from './workAssignmentService';
import type { IWebApi } from '../../types/xrm';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IShareDocumentsStepProps {
  webApi: IWebApi;
  containerId?: string;
  /** Called with the list of linked document IDs whenever it changes. */
  onLinkedDocsChange: (docIds: string[]) => void;
  /** Called with the list of uploaded files whenever it changes. */
  onUploadedFilesChange: (files: IUploadedFile[]) => void;
  initialLinkedDocs?: Array<{ id: string; name: string }>;
  initialUploadedFiles?: IUploadedFile[];
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  stepTitle: {
    color: tokens.colorNeutralForeground1,
    marginBottom: tokens.spacingVerticalXS,
  },
  stepSubtitle: {
    color: tokens.colorNeutralForeground3,
    marginBottom: tokens.spacingVerticalM,
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  sectionLabel: {
    color: tokens.colorNeutralForeground2,
  },
  tagGroup: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalXS,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ShareDocumentsStep: React.FC<IShareDocumentsStepProps> = ({
  webApi,
  containerId,
  onLinkedDocsChange,
  onUploadedFilesChange,
  initialLinkedDocs,
  initialUploadedFiles,
}) => {
  const styles = useStyles();

  const [linkedDocs, setLinkedDocs] = React.useState<Array<{ id: string; name: string }>>(
    initialLinkedDocs ?? []
  );
  const [uploadedFiles, setUploadedFiles] = React.useState<IUploadedFile[]>(
    initialUploadedFiles ?? []
  );

  const serviceRef = React.useRef<WorkAssignmentService | null>(null);
  if (!serviceRef.current) {
    serviceRef.current = new WorkAssignmentService(webApi, containerId);
  }

  // Report linked doc IDs whenever they change
  React.useEffect(() => {
    onLinkedDocsChange(linkedDocs.map((d) => d.id));
  }, [linkedDocs, onLinkedDocsChange]);

  // Report uploaded files whenever they change
  React.useEffect(() => {
    onUploadedFilesChange(uploadedFiles);
  }, [uploadedFiles, onUploadedFilesChange]);

  const handleSearchDocuments = React.useCallback(
    (query: string) => serviceRef.current!.searchDocuments(query),
    []
  );

  const handleDocumentSelected = React.useCallback(
    (item: ILookupItem | null) => {
      if (!item) return;
      // Don't add duplicates
      setLinkedDocs((prev) => {
        if (prev.some((d) => d.id === item.id)) return prev;
        return [...prev, { id: item.id, name: item.name }];
      });
    },
    []
  );

  const handleRemoveLinkedDoc = React.useCallback(
    (_e: unknown, data: { value: string }) => {
      setLinkedDocs((prev) => prev.filter((d) => d.id !== data.value));
    },
    []
  );

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
    <div className={styles.form}>
      <div>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Share Documents
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Link existing documents or upload new files to include with the work assignment.
        </Text>
      </div>

      {/* Section 1: Link existing documents */}
      <div className={styles.section}>
        <Text size={300} weight="semibold" className={styles.sectionLabel}>
          Link Documents
        </Text>
        <LookupField
          label=""
          value={null}
          onChange={handleDocumentSelected}
          onSearch={handleSearchDocuments}
          placeholder="Search documents to link..."
        />
        {linkedDocs.length > 0 && (
          <TagGroup className={styles.tagGroup} onDismiss={handleRemoveLinkedDoc}>
            {linkedDocs.map((doc) => (
              <Tag key={doc.id} value={doc.id} dismissible>
                {doc.name}
              </Tag>
            ))}
          </TagGroup>
        )}
      </div>

      {/* Section 2: Upload new files */}
      <div className={styles.section}>
        <Text size={300} weight="semibold" className={styles.sectionLabel}>
          Upload Files
        </Text>
        <FileUploadZone onFilesAccepted={handleFilesAccepted} onValidationErrors={() => {}} />
        {uploadedFiles.length > 0 && (
          <UploadedFileList files={uploadedFiles} onRemove={handleRemoveFile} />
        )}
      </div>
    </div>
  );
};

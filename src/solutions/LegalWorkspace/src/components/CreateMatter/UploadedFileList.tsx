/**
 * UploadedFileList.tsx
 * Displays the list of files accepted in Step 1 of the Create New Matter wizard.
 *
 * Each row shows:
 *   - Type-appropriate icon (PDF / DOCX / XLSX)
 *   - File name
 *   - Formatted file size (KB / MB)
 *   - Remove button (DismissRegular)
 *
 * All colors via Fluent v9 semantic tokens — zero hardcoded values.
 */
import * as React from 'react';
import {
  makeStyles,
  mergeClasses,
  tokens,
  Text,
  Button,
  Tooltip,
} from '@fluentui/react-components';
import {
  DocumentPdfRegular,
  DocumentRegular,
  TableRegular,
  DismissRegular,
} from '@fluentui/react-icons';
import { IUploadedFileListProps, IUploadedFile, UploadedFileType } from './wizardTypes';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  list: {
    listStyle: 'none',
    margin: '0px',
    padding: '0px',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  row: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    borderTopWidth: '1px',
    borderRightWidth: '1px',
    borderBottomWidth: '1px',
    borderLeftWidth: '1px',
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftColor: tokens.colorNeutralStroke2,
    // Subtle slide-in via keyframe is declared inline to avoid Griffel limitation
    // with @keyframes — we use a simple opacity transition instead.
    transition: 'background-color 0.15s ease',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground3,
    },
  },
  fileIcon: {
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
    fontSize: '20px',
  },
  fileIconPdf: {
    color: tokens.colorPaletteRedForeground1,
  },
  fileIconDocx: {
    color: tokens.colorPaletteBlueForeground2,
  },
  fileIconXlsx: {
    color: tokens.colorPaletteGreenForeground1,
  },
  fileInfo: {
    display: 'flex',
    flexDirection: 'column',
    flex: '1 1 auto',
    minWidth: 0,
    gap: '1px',
  },
  fileName: {
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  fileSize: {
    color: tokens.colorNeutralForeground4,
  },
  removeButton: {
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
  },
  emptyState: {
    // Not rendered — caller only mounts when files.length > 0
    display: 'none',
  },
});

// ---------------------------------------------------------------------------
// Utilities
// ---------------------------------------------------------------------------

/** Format bytes into a human-readable string (KB / MB). */
function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

// ---------------------------------------------------------------------------
// File icon sub-component
// ---------------------------------------------------------------------------

interface IFileIconProps {
  fileType: UploadedFileType;
}

const FileTypeIcon: React.FC<IFileIconProps> = ({ fileType }) => {
  const styles = useStyles();

  if (fileType === 'pdf') {
    return (
      <DocumentPdfRegular
        className={mergeClasses(styles.fileIcon, styles.fileIconPdf)}
        aria-hidden="true"
      />
    );
  }

  if (fileType === 'xlsx') {
    return (
      <TableRegular
        className={mergeClasses(styles.fileIcon, styles.fileIconXlsx)}
        aria-hidden="true"
      />
    );
  }

  // docx
  return (
    <DocumentRegular
      className={mergeClasses(styles.fileIcon, styles.fileIconDocx)}
      aria-hidden="true"
    />
  );
};

// ---------------------------------------------------------------------------
// Single file row
// ---------------------------------------------------------------------------

interface IFileRowProps {
  file: IUploadedFile;
  onRemove: (fileId: string) => void;
}

const FileRow: React.FC<IFileRowProps> = ({ file, onRemove }) => {
  const styles = useStyles();

  const handleRemove = React.useCallback(() => {
    onRemove(file.id);
  }, [file.id, onRemove]);

  return (
    <li className={styles.row} role="listitem">
      <FileTypeIcon fileType={file.fileType} />

      <div className={styles.fileInfo}>
        <Tooltip content={file.name} relationship="label" withArrow>
          <Text size={200} className={styles.fileName}>
            {file.name}
          </Text>
        </Tooltip>
        <Text size={100} className={styles.fileSize}>
          {formatBytes(file.sizeBytes)}
        </Text>
      </div>

      <Tooltip content={`Remove ${file.name}`} relationship="label" withArrow>
        <Button
          appearance="subtle"
          size="small"
          icon={<DismissRegular />}
          className={styles.removeButton}
          onClick={handleRemove}
          aria-label={`Remove ${file.name}`}
        />
      </Tooltip>
    </li>
  );
};

// ---------------------------------------------------------------------------
// UploadedFileList (exported)
// ---------------------------------------------------------------------------

export const UploadedFileList: React.FC<IUploadedFileListProps> = ({
  files,
  onRemove,
}) => {
  const styles = useStyles();

  if (files.length === 0) {
    return null;
  }

  return (
    <ol
      className={styles.list}
      aria-label={`Uploaded files (${files.length})`}
    >
      {files.map((file) => (
        <FileRow key={file.id} file={file} onRemove={onRemove} />
      ))}
    </ol>
  );
};

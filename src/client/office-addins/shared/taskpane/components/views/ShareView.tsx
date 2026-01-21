import React, { useState } from 'react';
import {
  makeStyles,
  tokens,
  Button,
  Card,
  CardHeader,
  Text,
  Body1,
  Input,
  Dropdown,
  Option,
  MessageBar,
  MessageBarBody,
  Spinner,
  Field,
} from '@fluentui/react-components';
import {
  ShareRegular,
  CopyRegular,
  SearchRegular,
  DocumentRegular,
} from '@fluentui/react-icons';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  searchSection: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  searchRow: {
    display: 'flex',
    flexDirection: 'row',
    gap: tokens.spacingHorizontalS,
  },
  searchInput: {
    flex: 1,
  },
  documentList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    maxHeight: '200px',
    overflow: 'auto',
  },
  documentItem: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: tokens.spacingVerticalXS,
    borderRadius: tokens.borderRadiusMedium,
    cursor: 'pointer',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground3,
    },
  },
  documentItemSelected: {
    backgroundColor: tokens.colorBrandBackground2,
  },
  linkSection: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  linkRow: {
    display: 'flex',
    flexDirection: 'row',
    gap: tokens.spacingHorizontalS,
    alignItems: 'flex-end',
  },
  linkInput: {
    flex: 1,
  },
  actions: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    marginTop: tokens.spacingVerticalM,
  },
});

export interface ShareViewProps {
  /** Callback to search for documents */
  onSearch?: (query: string) => Promise<DocumentSearchResult[]>;
  /** Callback to generate sharing link */
  onGenerateLink?: (documentId: string, permissions: SharePermissions) => Promise<string>;
  /** Callback to insert link into email/document */
  onInsertLink?: (link: string) => Promise<void>;
  /** Whether an operation is in progress */
  isLoading?: boolean;
  /** Error message */
  error?: string | null;
}

export interface DocumentSearchResult {
  id: string;
  name: string;
  path: string;
  modifiedDate?: string;
}

export interface SharePermissions {
  type: 'view' | 'edit';
  expiration?: Date;
}

export const ShareView: React.FC<ShareViewProps> = ({
  onSearch,
  onGenerateLink,
  onInsertLink,
  isLoading = false,
  error,
}) => {
  const styles = useStyles();
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<DocumentSearchResult[]>([]);
  const [selectedDocument, setSelectedDocument] = useState<DocumentSearchResult | null>(null);
  const [permissionType, setPermissionType] = useState<'view' | 'edit'>('view');
  const [generatedLink, setGeneratedLink] = useState<string | null>(null);
  const [linkCopied, setLinkCopied] = useState(false);

  const handleSearch = async () => {
    if (onSearch && searchQuery.trim()) {
      const results = await onSearch(searchQuery);
      setSearchResults(results);
      setSelectedDocument(null);
      setGeneratedLink(null);
    }
  };

  const handleSelectDocument = (doc: DocumentSearchResult) => {
    setSelectedDocument(doc);
    setGeneratedLink(null);
  };

  const handleGenerateLink = async () => {
    if (onGenerateLink && selectedDocument) {
      const link = await onGenerateLink(selectedDocument.id, { type: permissionType });
      setGeneratedLink(link);
    }
  };

  const handleCopyLink = async () => {
    if (generatedLink) {
      await navigator.clipboard.writeText(generatedLink);
      setLinkCopied(true);
      setTimeout(() => setLinkCopied(false), 2000);
    }
  };

  const handleInsertLink = async () => {
    if (onInsertLink && generatedLink) {
      await onInsertLink(generatedLink);
    }
  };

  return (
    <div className={styles.container}>
      {/* Search Section */}
      <Card>
        <CardHeader
          image={<SearchRegular />}
          header={<Text weight="semibold">Find Document</Text>}
        />
        <div className={styles.searchSection}>
          <div className={styles.searchRow}>
            <Input
              className={styles.searchInput}
              placeholder="Search by name or path..."
              value={searchQuery}
              onChange={(_, data) => setSearchQuery(data.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
            />
            <Button
              icon={isLoading ? <Spinner size="tiny" /> : <SearchRegular />}
              onClick={handleSearch}
              disabled={isLoading || !searchQuery.trim()}
            >
              Search
            </Button>
          </div>

          {/* Search Results */}
          {searchResults.length > 0 && (
            <div className={styles.documentList}>
              {searchResults.map((doc) => (
                <div
                  key={doc.id}
                  className={`${styles.documentItem} ${selectedDocument?.id === doc.id ? styles.documentItemSelected : ''}`}
                  onClick={() => handleSelectDocument(doc)}
                >
                  <DocumentRegular />
                  <div>
                    <Body1>{doc.name}</Body1>
                    <Text size={200}>{doc.path}</Text>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </Card>

      {/* Link Generation */}
      {selectedDocument && (
        <Card>
          <CardHeader
            image={<ShareRegular />}
            header={<Text weight="semibold">Generate Sharing Link</Text>}
          />
          <div className={styles.linkSection}>
            <Body1>Selected: {selectedDocument.name}</Body1>

            <Field label="Permission">
              <Dropdown
                value={permissionType === 'view' ? 'View only' : 'Can edit'}
                onOptionSelect={(_, data) => setPermissionType(data.optionValue as 'view' | 'edit')}
              >
                <Option value="view">View only</Option>
                <Option value="edit">Can edit</Option>
              </Dropdown>
            </Field>

            <Button
              appearance="primary"
              icon={isLoading ? <Spinner size="tiny" /> : <ShareRegular />}
              onClick={handleGenerateLink}
              disabled={isLoading}
            >
              Generate Link
            </Button>

            {/* Generated Link */}
            {generatedLink && (
              <div className={styles.linkRow}>
                <Input
                  className={styles.linkInput}
                  value={generatedLink}
                  readOnly
                />
                <Button
                  icon={<CopyRegular />}
                  onClick={handleCopyLink}
                >
                  {linkCopied ? 'Copied!' : 'Copy'}
                </Button>
              </div>
            )}
          </div>
        </Card>
      )}

      {/* Error */}
      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {/* Insert Action */}
      {generatedLink && (
        <div className={styles.actions}>
          <Button
            appearance="primary"
            onClick={handleInsertLink}
            disabled={isLoading}
          >
            Insert Link
          </Button>
        </div>
      )}
    </div>
  );
};

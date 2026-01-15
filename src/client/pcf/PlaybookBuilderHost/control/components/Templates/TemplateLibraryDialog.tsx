/**
 * Template Library Dialog - Browse and Clone Playbook Templates
 *
 * Displays available playbook templates in a grid layout.
 * Users can clone templates to create their own playbooks.
 *
 * @version 2.7.0
 */

import * as React from 'react';
import { useCallback, useEffect, useState } from 'react';
import {
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  Button,
  Card,
  CardHeader,
  CardFooter,
  Text,
  Spinner,
  Input,
  makeStyles,
  tokens,
  shorthands,
  MessageBar,
  MessageBarBody,
  Badge,
} from '@fluentui/react-components';
import {
  Search20Regular,
  Copy20Regular,
  DocumentMultiple20Regular,
  Dismiss20Regular,
} from '@fluentui/react-icons';
import { useTemplateStore, type PlaybookTemplate } from '../../stores/templateStore';

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  dialogSurface: {
    maxWidth: '900px',
    width: '90vw',
    maxHeight: '80vh',
  },
  searchContainer: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalM),
    marginBottom: tokens.spacingVerticalL,
  },
  searchInput: {
    flex: 1,
    maxWidth: '400px',
  },
  grid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))',
    ...shorthands.gap(tokens.spacingHorizontalL),
    ...shorthands.padding(tokens.spacingVerticalS, '0'),
    maxHeight: '50vh',
    overflowY: 'auto',
  },
  card: {
    height: '100%',
    display: 'flex',
    flexDirection: 'column',
  },
  cardContent: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
  },
  description: {
    color: tokens.colorNeutralForeground2,
    display: '-webkit-box',
    WebkitLineClamp: 3,
    WebkitBoxOrient: 'vertical',
    ...shorthands.overflow('hidden'),
    marginTop: tokens.spacingVerticalXS,
    flex: 1,
  },
  cardFooter: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    ...shorthands.padding(tokens.spacingVerticalS, '0', '0'),
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    ...shorthands.padding(tokens.spacingVerticalXXL),
    textAlign: 'center',
    color: tokens.colorNeutralForeground2,
  },
  emptyIcon: {
    fontSize: '48px',
    marginBottom: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },
  loadingContainer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    ...shorthands.padding(tokens.spacingVerticalXXL),
  },
  titleContainer: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
  },
  templateBadge: {
    backgroundColor: tokens.colorBrandBackground,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface TemplateLibraryDialogProps {
  open: boolean;
  onClose: () => void;
  onCloneSuccess: (playbookId: string, playbookName: string) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const TemplateLibraryDialog: React.FC<TemplateLibraryDialogProps> = ({
  open,
  onClose,
  onCloneSuccess,
}) => {
  const styles = useStyles();
  const [searchTerm, setSearchTerm] = useState('');
  const [cloningId, setCloningId] = useState<string | null>(null);

  // Store state
  const {
    templates,
    isLoading,
    error,
    isCloning,
    cloneError,
    fetchTemplates,
    clonePlaybook,
    clearError,
  } = useTemplateStore();

  // Fetch templates when dialog opens
  useEffect(() => {
    if (open) {
      fetchTemplates(1);
    }
  }, [open, fetchTemplates]);

  // Handle search
  const handleSearch = useCallback(() => {
    fetchTemplates(1, searchTerm || undefined);
  }, [fetchTemplates, searchTerm]);

  // Handle search on Enter
  const handleSearchKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === 'Enter') {
        handleSearch();
      }
    },
    [handleSearch]
  );

  // Handle clone
  const handleClone = useCallback(
    async (template: PlaybookTemplate) => {
      setCloningId(template.id);
      try {
        const cloned = await clonePlaybook(template.id);
        onCloneSuccess(cloned.id, cloned.name);
        onClose();
      } catch {
        // Error is already set in store
      } finally {
        setCloningId(null);
      }
    },
    [clonePlaybook, onCloneSuccess, onClose]
  );

  // Handle close
  const handleClose = useCallback(() => {
    clearError();
    setSearchTerm('');
    onClose();
  }, [clearError, onClose]);

  return (
    <Dialog open={open} onOpenChange={(_, data) => !data.open && handleClose()}>
      <DialogSurface className={styles.dialogSurface}>
        <DialogTitle>
          <div className={styles.titleContainer}>
            <DocumentMultiple20Regular />
            <Text>Template Library</Text>
          </div>
        </DialogTitle>

        <DialogBody>
          <DialogContent>
            {/* Error message */}
            {(error || cloneError) && (
              <MessageBar intent="error" style={{ marginBottom: tokens.spacingVerticalM }}>
                <MessageBarBody>{error || cloneError}</MessageBarBody>
              </MessageBar>
            )}

            {/* Search bar */}
            <div className={styles.searchContainer}>
              <Input
                className={styles.searchInput}
                placeholder="Search templates..."
                value={searchTerm}
                onChange={(_, data) => setSearchTerm(data.value)}
                onKeyDown={handleSearchKeyDown}
                contentBefore={<Search20Regular />}
              />
              <Button appearance="primary" onClick={handleSearch} disabled={isLoading}>
                Search
              </Button>
            </div>

            {/* Loading state */}
            {isLoading && (
              <div className={styles.loadingContainer}>
                <Spinner size="medium" label="Loading templates..." />
              </div>
            )}

            {/* Empty state */}
            {!isLoading && templates.length === 0 && (
              <div className={styles.emptyState}>
                <div className={styles.emptyIcon}>
                  <DocumentMultiple20Regular />
                </div>
                <Text size={400} weight="semibold">
                  No templates found
                </Text>
                <Text size={300}>
                  {searchTerm
                    ? 'Try adjusting your search terms'
                    : 'No playbook templates are available yet'}
                </Text>
              </div>
            )}

            {/* Template grid */}
            {!isLoading && templates.length > 0 && (
              <div className={styles.grid}>
                {templates.map((template) => (
                  <Card key={template.id} className={styles.card}>
                    <CardHeader
                      header={
                        <div className={styles.titleContainer}>
                          <Text weight="semibold">{template.name}</Text>
                          <Badge
                            appearance="filled"
                            size="small"
                            className={styles.templateBadge}
                          >
                            Template
                          </Badge>
                        </div>
                      }
                    />
                    <div className={styles.cardContent}>
                      <Text className={styles.description} size={200}>
                        {template.description || 'No description available'}
                      </Text>
                    </div>
                    <CardFooter className={styles.cardFooter}>
                      <Text size={100} style={{ color: tokens.colorNeutralForeground3 }}>
                        Updated: {new Date(template.modifiedOn).toLocaleDateString()}
                      </Text>
                      <Button
                        appearance="primary"
                        size="small"
                        icon={<Copy20Regular />}
                        disabled={isCloning}
                        onClick={() => handleClone(template)}
                      >
                        {cloningId === template.id ? 'Cloning...' : 'Clone'}
                      </Button>
                    </CardFooter>
                  </Card>
                ))}
              </div>
            )}
          </DialogContent>
        </DialogBody>

        <DialogActions>
          <Button appearance="secondary" icon={<Dismiss20Regular />} onClick={handleClose}>
            Close
          </Button>
        </DialogActions>
      </DialogSurface>
    </Dialog>
  );
};

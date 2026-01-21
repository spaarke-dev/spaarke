/**
 * Save As Dialog - Create a copy of an existing scope with a new name
 *
 * Provides a dialog for creating customer copies of system or customer scopes.
 * New copies always get CUST- prefix and are editable.
 *
 * @version 1.0.0
 */

import * as React from 'react';
import { useState, useCallback, useEffect, useMemo } from 'react';
import {
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  Button,
  Input,
  Field,
  Text,
  Badge,
  MessageBar,
  MessageBarBody,
  Spinner,
  makeStyles,
  tokens,
  shorthands,
} from '@fluentui/react-components';
import {
  Copy20Regular,
  Dismiss20Regular,
  Save20Regular,
  LockClosed16Regular,
  Person16Regular,
  Info16Regular,
} from '@fluentui/react-icons';
import type { ScopeItem, ScopeType, OwnershipType } from '../ScopeBrowser/ScopeBrowser';

// -----------------------------------------------------------------------------
// Types
// -----------------------------------------------------------------------------

export interface SaveAsDialogProps {
  /** Whether the dialog is open */
  open: boolean;
  /** The source scope being copied */
  sourceScope: ScopeItem;
  /** The type of scope (actions, skills, tools, knowledge) */
  scopeType: ScopeType;
  /** Callback when save is confirmed with new name */
  onSave: (newName: string) => Promise<void>;
  /** Callback when dialog is cancelled */
  onCancel: () => void;
  /** Optional function to validate name uniqueness */
  validateName?: (name: string) => Promise<boolean>;
}

// -----------------------------------------------------------------------------
// Styles (Fluent UI v9 tokens for dark mode support - ADR-021)
// -----------------------------------------------------------------------------

const useStyles = makeStyles({
  dialogSurface: {
    maxWidth: '500px',
    width: '90vw',
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
  },
  content: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalM),
  },
  sourceSection: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalS),
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
  },
  sourceLabel: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    textTransform: 'uppercase',
    letterSpacing: '0.05em',
  },
  sourceInfo: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
  },
  sourceName: {
    fontWeight: tokens.fontWeightSemibold,
  },
  ownershipBadge: {
    flexShrink: 0,
  },
  systemBadge: {
    backgroundColor: tokens.colorPaletteBlueBorderActive,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  customerBadge: {
    backgroundColor: tokens.colorPaletteGreenBackground3,
    color: tokens.colorPaletteGreenForeground1,
  },
  newCustBadge: {
    backgroundColor: tokens.colorPaletteGreenBackground3,
    color: tokens.colorPaletteGreenForeground1,
  },
  newSection: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalS),
  },
  ownershipInfo: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
  },
  ownershipText: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  infoIcon: {
    color: tokens.colorBrandForeground1,
  },
  inputPrefix: {
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightSemibold,
  },
  validationError: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
  },
});

// -----------------------------------------------------------------------------
// Configuration
// -----------------------------------------------------------------------------

const SCOPE_TYPE_LABELS: Record<ScopeType, string> = {
  actions: 'Action',
  skills: 'Skill',
  tools: 'Tool',
  knowledge: 'Knowledge',
};

// -----------------------------------------------------------------------------
// Helper Components
// -----------------------------------------------------------------------------

interface OwnershipBadgeProps {
  ownershipType: OwnershipType;
  className?: string;
}

const OwnershipBadge: React.FC<OwnershipBadgeProps> = ({ ownershipType, className }) => {
  const styles = useStyles();

  return (
    <Badge
      appearance="filled"
      size="small"
      className={`${className || ''} ${ownershipType === 'system' ? styles.systemBadge : styles.customerBadge}`}
      icon={ownershipType === 'system' ? <LockClosed16Regular /> : <Person16Regular />}
    >
      {ownershipType === 'system' ? 'SYS' : 'CUST'}
    </Badge>
  );
};

// -----------------------------------------------------------------------------
// Component
// -----------------------------------------------------------------------------

export const SaveAsDialog: React.FC<SaveAsDialogProps> = ({
  open,
  sourceScope,
  scopeType,
  onSave,
  onCancel,
  validateName,
}) => {
  const styles = useStyles();

  // State
  const [newName, setNewName] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [validationError, setValidationError] = useState<string | null>(null);

  // Reset state when dialog opens
  useEffect(() => {
    if (open) {
      // Pre-fill with source name + " (Copy)"
      setNewName(`${sourceScope.displayName} (Copy)`);
      setError(null);
      setValidationError(null);
    }
  }, [open, sourceScope.displayName]);

  // Validate name
  const validateNameInput = useCallback(
    async (name: string): Promise<boolean> => {
      if (!name.trim()) {
        setValidationError('Name is required');
        return false;
      }

      if (name.trim().length < 3) {
        setValidationError('Name must be at least 3 characters');
        return false;
      }

      if (name.trim().length > 100) {
        setValidationError('Name must be less than 100 characters');
        return false;
      }

      // Check for uniqueness if validation function provided
      if (validateName) {
        const isUnique = await validateName(name.trim());
        if (!isUnique) {
          setValidationError('A scope with this name already exists');
          return false;
        }
      }

      setValidationError(null);
      return true;
    },
    [validateName]
  );

  // Handle name change
  const handleNameChange = useCallback(
    (_: React.ChangeEvent<HTMLInputElement>, data: { value: string }) => {
      setNewName(data.value);
      // Clear validation error on change
      if (validationError) {
        setValidationError(null);
      }
    },
    [validationError]
  );

  // Handle save
  const handleSave = useCallback(async () => {
    const isValid = await validateNameInput(newName);
    if (!isValid) {
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      await onSave(newName.trim());
      // Dialog will be closed by parent after successful save
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save copy');
    } finally {
      setIsSubmitting(false);
    }
  }, [newName, onSave, validateNameInput]);

  // Handle cancel
  const handleCancel = useCallback(() => {
    if (!isSubmitting) {
      onCancel();
    }
  }, [isSubmitting, onCancel]);

  // Handle keyboard
  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === 'Enter' && !isSubmitting) {
        handleSave();
      } else if (e.key === 'Escape' && !isSubmitting) {
        handleCancel();
      }
    },
    [handleSave, handleCancel, isSubmitting]
  );

  // Dialog title
  const dialogTitle = useMemo(() => {
    return `Save ${SCOPE_TYPE_LABELS[scopeType]} As`;
  }, [scopeType]);

  return (
    <Dialog open={open} onOpenChange={(_, data) => !data.open && handleCancel()}>
      <DialogSurface className={styles.dialogSurface} onKeyDown={handleKeyDown}>
        <DialogTitle>
          <div className={styles.header}>
            <Copy20Regular />
            <span>{dialogTitle}</span>
          </div>
        </DialogTitle>

        <DialogBody>
          <DialogContent className={styles.content}>
            {/* Error message */}
            {error && (
              <MessageBar intent="error">
                <MessageBarBody>{error}</MessageBarBody>
              </MessageBar>
            )}

            {/* Source scope info */}
            <div className={styles.sourceSection}>
              <Text className={styles.sourceLabel}>Copying from</Text>
              <div className={styles.sourceInfo}>
                <OwnershipBadge
                  ownershipType={sourceScope.ownershipType}
                  className={styles.ownershipBadge}
                />
                <Text className={styles.sourceName}>{sourceScope.displayName}</Text>
              </div>
              {sourceScope.description && (
                <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                  {sourceScope.description}
                </Text>
              )}
            </div>

            {/* New scope name input */}
            <div className={styles.newSection}>
              <Field
                label="New Name"
                required
                validationMessage={validationError}
                validationState={validationError ? 'error' : undefined}
              >
                <Input
                  value={newName}
                  onChange={handleNameChange}
                  disabled={isSubmitting}
                  placeholder="Enter a name for the copy"
                  autoFocus
                />
              </Field>
            </div>

            {/* Ownership info */}
            <div className={styles.ownershipInfo}>
              <Info16Regular className={styles.infoIcon} />
              <Text className={styles.ownershipText}>
                The copy will be created as a <strong>Customer</strong> scope (CUST-) and will be fully editable.
              </Text>
            </div>
          </DialogContent>
        </DialogBody>

        <DialogActions>
          <Button
            appearance="secondary"
            icon={<Dismiss20Regular />}
            onClick={handleCancel}
            disabled={isSubmitting}
          >
            Cancel
          </Button>
          <Button
            appearance="primary"
            icon={isSubmitting ? <Spinner size="tiny" /> : <Save20Regular />}
            onClick={handleSave}
            disabled={isSubmitting || !newName.trim()}
          >
            {isSubmitting ? 'Saving...' : 'Save Copy'}
          </Button>
        </DialogActions>
      </DialogSurface>
    </Dialog>
  );
};

export default SaveAsDialog;

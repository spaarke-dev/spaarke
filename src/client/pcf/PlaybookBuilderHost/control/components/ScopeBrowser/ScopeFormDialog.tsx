/**
 * Scope Form Dialog - Create/Edit/View scope records
 *
 * Provides forms for creating and editing customer scopes,
 * with read-only mode for system scopes.
 *
 * @version 1.0.0
 */

import * as React from 'react';
import { useState, useCallback, useMemo, useEffect } from 'react';
import {
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  Button,
  Input,
  Textarea,
  Label,
  Field,
  Dropdown,
  Option,
  MessageBar,
  MessageBarBody,
  Spinner,
  Badge,
  makeStyles,
  tokens,
  shorthands,
} from '@fluentui/react-components';
import {
  Dismiss20Regular,
  Save20Regular,
  Eye20Regular,
  LockClosed16Regular,
  Person16Regular,
} from '@fluentui/react-icons';
import type { ScopeItem, ScopeType, OwnershipType } from './ScopeBrowser';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export type DialogMode = 'create' | 'edit' | 'view';

export interface ScopeFormData {
  name: string;
  displayName: string;
  description: string;
  // Action-specific fields
  systemPrompt?: string;
  actionType?: string;
  // Skill-specific fields
  promptFragment?: string;
  category?: string;
  // Tool-specific fields
  toolType?: string;
  handlerClass?: string;
  configuration?: string;
  // Knowledge-specific fields
  knowledgeType?: string;
  content?: string;
  documentId?: string;
}

export interface ScopeFormDialogProps {
  open: boolean;
  mode: DialogMode;
  scopeType: ScopeType;
  scope?: ScopeItem;
  initialData?: Partial<ScopeFormData>;
  onClose: () => void;
  onSave?: (data: ScopeFormData) => Promise<void>;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  dialogSurface: {
    maxWidth: '600px',
    width: '90vw',
    maxHeight: '80vh',
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
    ...shorthands.overflow('auto'),
    maxHeight: '50vh',
  },
  field: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalXS),
  },
  textarea: {
    minHeight: '100px',
  },
  largeTextarea: {
    minHeight: '200px',
  },
  readOnlyBanner: {
    marginBottom: tokens.spacingVerticalM,
  },
  ownershipBadge: {
    marginLeft: 'auto',
  },
  systemBadge: {
    backgroundColor: tokens.colorPaletteBlueBorderActive,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  customerBadge: {
    backgroundColor: tokens.colorPaletteGreenBackground3,
    color: tokens.colorPaletteGreenForeground1,
  },
  validationError: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Configuration
// ─────────────────────────────────────────────────────────────────────────────

const SCOPE_TYPE_LABELS: Record<ScopeType, string> = {
  actions: 'Action',
  skills: 'Skill',
  tools: 'Tool',
  knowledge: 'Knowledge',
};

const ACTION_TYPES = [
  { value: 'ai-analysis', label: 'AI Analysis' },
  { value: 'condition', label: 'Condition' },
  { value: 'create-task', label: 'Create Task' },
  { value: 'send-email', label: 'Send Email' },
  { value: 'update-record', label: 'Update Record' },
];

const TOOL_TYPES = [
  { value: 'entity-extractor', label: 'Entity Extractor' },
  { value: 'clause-analyzer', label: 'Clause Analyzer' },
  { value: 'document-classifier', label: 'Document Classifier' },
  { value: 'summary', label: 'Summary' },
  { value: 'risk-detector', label: 'Risk Detector' },
  { value: 'custom', label: 'Custom' },
];

const KNOWLEDGE_TYPES = [
  { value: 'inline', label: 'Inline Content' },
  { value: 'document', label: 'Document Reference' },
  { value: 'rag-index', label: 'RAG Index' },
];

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const ScopeFormDialog: React.FC<ScopeFormDialogProps> = ({
  open,
  mode,
  scopeType,
  scope,
  initialData,
  onClose,
  onSave,
}) => {
  const styles = useStyles();

  // State
  const [formData, setFormData] = useState<ScopeFormData>({
    name: '',
    displayName: '',
    description: '',
  });
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [validationErrors, setValidationErrors] = useState<Record<string, string>>({});

  // Determine if read-only
  const isReadOnly = mode === 'view' || (scope?.ownershipType === 'system');
  const isSystemScope = scope?.ownershipType === 'system';

  // Initialize form data when dialog opens
  useEffect(() => {
    if (open) {
      if (scope && (mode === 'edit' || mode === 'view')) {
        setFormData({
          name: scope.name,
          displayName: scope.displayName,
          description: scope.description,
          ...initialData,
        });
      } else if (initialData) {
        setFormData({
          name: '',
          displayName: '',
          description: '',
          ...initialData,
        });
      } else {
        setFormData({
          name: '',
          displayName: '',
          description: '',
        });
      }
      setError(null);
      setValidationErrors({});
    }
  }, [open, mode, scope, initialData]);

  // Update field
  const updateField = useCallback((field: keyof ScopeFormData, value: string) => {
    setFormData((prev) => ({ ...prev, [field]: value }));
    // Clear validation error when field changes
    if (validationErrors[field]) {
      setValidationErrors((prev) => {
        const next = { ...prev };
        delete next[field];
        return next;
      });
    }
  }, [validationErrors]);

  // Validate form
  const validateForm = useCallback((): boolean => {
    const errors: Record<string, string> = {};

    if (!formData.displayName.trim()) {
      errors.displayName = 'Name is required';
    }

    // Scope-specific validation
    if (scopeType === 'actions') {
      if (!formData.systemPrompt?.trim()) {
        errors.systemPrompt = 'System prompt is required for actions';
      }
    }

    if (scopeType === 'skills') {
      if (!formData.promptFragment?.trim()) {
        errors.promptFragment = 'Prompt fragment is required for skills';
      }
    }

    if (scopeType === 'knowledge' && formData.knowledgeType === 'inline') {
      if (!formData.content?.trim()) {
        errors.content = 'Content is required for inline knowledge';
      }
    }

    setValidationErrors(errors);
    return Object.keys(errors).length === 0;
  }, [formData, scopeType]);

  // Handle save
  const handleSave = useCallback(async () => {
    if (!onSave || isReadOnly) return;

    if (!validateForm()) {
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      await onSave(formData);
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save scope');
    } finally {
      setIsSubmitting(false);
    }
  }, [formData, onSave, onClose, isReadOnly, validateForm]);

  // Dialog title
  const dialogTitle = useMemo(() => {
    const typeLabel = SCOPE_TYPE_LABELS[scopeType];
    switch (mode) {
      case 'create':
        return `Create ${typeLabel}`;
      case 'edit':
        return `Edit ${typeLabel}`;
      case 'view':
        return `View ${typeLabel}`;
      default:
        return typeLabel;
    }
  }, [mode, scopeType]);

  // Ownership badge
  const ownershipBadge = scope ? (
    <Badge
      appearance="filled"
      size="small"
      className={isSystemScope ? styles.systemBadge : styles.customerBadge}
      icon={isSystemScope ? <LockClosed16Regular /> : <Person16Regular />}
    >
      {isSystemScope ? 'System' : 'Customer'}
    </Badge>
  ) : null;

  return (
    <Dialog open={open} onOpenChange={(_, data) => !data.open && onClose()}>
      <DialogSurface className={styles.dialogSurface}>
        <DialogTitle>
          <div className={styles.header}>
            <Eye20Regular />
            <span>{dialogTitle}</span>
            <span className={styles.ownershipBadge}>{ownershipBadge}</span>
          </div>
        </DialogTitle>

        <DialogBody>
          <DialogContent className={styles.content}>
            {/* Read-only banner for system scopes */}
            {isSystemScope && mode !== 'view' && (
              <MessageBar intent="info" className={styles.readOnlyBanner}>
                <MessageBarBody>
                  System scopes are read-only. Use &quot;Save As&quot; to create an editable copy.
                </MessageBarBody>
              </MessageBar>
            )}

            {/* Error message */}
            {error && (
              <MessageBar intent="error">
                <MessageBarBody>{error}</MessageBarBody>
              </MessageBar>
            )}

            {/* Common fields */}
            <Field
              className={styles.field}
              label="Name"
              required
              validationMessage={validationErrors.displayName}
              validationState={validationErrors.displayName ? 'error' : undefined}
            >
              <Input
                value={formData.displayName}
                onChange={(_, data) => updateField('displayName', data.value)}
                disabled={isReadOnly}
                placeholder="Enter a descriptive name"
              />
            </Field>

            <Field className={styles.field} label="Description">
              <Textarea
                value={formData.description}
                onChange={(_, data) => updateField('description', data.value)}
                disabled={isReadOnly}
                placeholder="Describe what this scope does"
                className={styles.textarea}
              />
            </Field>

            {/* Action-specific fields */}
            {scopeType === 'actions' && (
              <>
                <Field className={styles.field} label="Action Type">
                  <Dropdown
                    value={formData.actionType || 'ai-analysis'}
                    onOptionSelect={(_, data) =>
                      updateField('actionType', data.optionValue as string)
                    }
                    disabled={isReadOnly}
                  >
                    {ACTION_TYPES.map((type) => (
                      <Option key={type.value} value={type.value}>
                        {type.label}
                      </Option>
                    ))}
                  </Dropdown>
                </Field>

                <Field
                  className={styles.field}
                  label="System Prompt"
                  required
                  validationMessage={validationErrors.systemPrompt}
                  validationState={validationErrors.systemPrompt ? 'error' : undefined}
                >
                  <Textarea
                    value={formData.systemPrompt || ''}
                    onChange={(_, data) => updateField('systemPrompt', data.value)}
                    disabled={isReadOnly}
                    placeholder="Enter the system prompt for AI processing"
                    className={styles.largeTextarea}
                  />
                </Field>
              </>
            )}

            {/* Skill-specific fields */}
            {scopeType === 'skills' && (
              <>
                <Field className={styles.field} label="Category">
                  <Input
                    value={formData.category || ''}
                    onChange={(_, data) => updateField('category', data.value)}
                    disabled={isReadOnly}
                    placeholder="e.g., Analysis, Formatting, Extraction"
                  />
                </Field>

                <Field
                  className={styles.field}
                  label="Prompt Fragment"
                  required
                  validationMessage={validationErrors.promptFragment}
                  validationState={validationErrors.promptFragment ? 'error' : undefined}
                >
                  <Textarea
                    value={formData.promptFragment || ''}
                    onChange={(_, data) => updateField('promptFragment', data.value)}
                    disabled={isReadOnly}
                    placeholder="Enter the prompt fragment that modifies AI behavior"
                    className={styles.largeTextarea}
                  />
                </Field>
              </>
            )}

            {/* Tool-specific fields */}
            {scopeType === 'tools' && (
              <>
                <Field className={styles.field} label="Tool Type">
                  <Dropdown
                    value={formData.toolType || 'custom'}
                    onOptionSelect={(_, data) =>
                      updateField('toolType', data.optionValue as string)
                    }
                    disabled={isReadOnly}
                  >
                    {TOOL_TYPES.map((type) => (
                      <Option key={type.value} value={type.value}>
                        {type.label}
                      </Option>
                    ))}
                  </Dropdown>
                </Field>

                <Field className={styles.field} label="Handler Class">
                  <Input
                    value={formData.handlerClass || ''}
                    onChange={(_, data) => updateField('handlerClass', data.value)}
                    disabled={isReadOnly}
                    placeholder="e.g., GenericAnalysisHandler"
                  />
                </Field>

                <Field className={styles.field} label="Configuration (JSON)">
                  <Textarea
                    value={formData.configuration || ''}
                    onChange={(_, data) => updateField('configuration', data.value)}
                    disabled={isReadOnly}
                    placeholder='{"operation": "extract", "parameters": {}}'
                    className={styles.largeTextarea}
                    style={{ fontFamily: 'monospace' }}
                  />
                </Field>
              </>
            )}

            {/* Knowledge-specific fields */}
            {scopeType === 'knowledge' && (
              <>
                <Field className={styles.field} label="Knowledge Type">
                  <Dropdown
                    value={formData.knowledgeType || 'inline'}
                    onOptionSelect={(_, data) =>
                      updateField('knowledgeType', data.optionValue as string)
                    }
                    disabled={isReadOnly}
                  >
                    {KNOWLEDGE_TYPES.map((type) => (
                      <Option key={type.value} value={type.value}>
                        {type.label}
                      </Option>
                    ))}
                  </Dropdown>
                </Field>

                {formData.knowledgeType === 'inline' && (
                  <Field
                    className={styles.field}
                    label="Content"
                    required
                    validationMessage={validationErrors.content}
                    validationState={validationErrors.content ? 'error' : undefined}
                  >
                    <Textarea
                      value={formData.content || ''}
                      onChange={(_, data) => updateField('content', data.value)}
                      disabled={isReadOnly}
                      placeholder="Enter the knowledge content"
                      className={styles.largeTextarea}
                    />
                  </Field>
                )}

                {formData.knowledgeType === 'document' && (
                  <Field className={styles.field} label="Document ID">
                    <Input
                      value={formData.documentId || ''}
                      onChange={(_, data) => updateField('documentId', data.value)}
                      disabled={isReadOnly}
                      placeholder="Enter the document GUID"
                    />
                  </Field>
                )}
              </>
            )}
          </DialogContent>
        </DialogBody>

        <DialogActions>
          <Button appearance="secondary" icon={<Dismiss20Regular />} onClick={onClose}>
            {isReadOnly ? 'Close' : 'Cancel'}
          </Button>
          {!isReadOnly && (
            <Button
              appearance="primary"
              icon={isSubmitting ? <Spinner size="tiny" /> : <Save20Regular />}
              onClick={handleSave}
              disabled={isSubmitting}
            >
              {isSubmitting ? 'Saving...' : 'Save'}
            </Button>
          )}
        </DialogActions>
      </DialogSurface>
    </Dialog>
  );
};

export default ScopeFormDialog;

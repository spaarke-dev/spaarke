/**
 * SelectWorkStep.tsx
 * Step 1: "Work to Assign" -- select the entity record this work relates to.
 *
 * Follows the AssociateToStep pattern from DocumentUploadWizard:
 *   - Record Type dropdown + "Select Record" button (Xrm.Utility.lookupObjects)
 *   - Selected record display with checkmark + clear
 *   - Divider with "or"
 *   - Checkbox: "Assign work without a specific record"
 */
import * as React from 'react';
import {
  Text,
  Dropdown,
  Option,
  Button,
  Checkbox,
  Divider,
  Spinner,
  MessageBar,
  MessageBarBody,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  SearchRegular,
  DismissRegular,
  CheckmarkCircleRegular,
} from '@fluentui/react-icons';
import type { ICreateWorkAssignmentFormState } from './formTypes';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISelectWorkStepProps {
  onValidChange: (isValid: boolean) => void;
  onFormValues: (values: Pick<ICreateWorkAssignmentFormState, 'recordType' | 'recordId' | 'recordName' | 'assignWithoutRecord'>) => void;
  initialValues?: Pick<ICreateWorkAssignmentFormState, 'recordType' | 'recordId' | 'recordName' | 'assignWithoutRecord'>;
}

// ---------------------------------------------------------------------------
// Record Type options
// ---------------------------------------------------------------------------

const RECORD_TYPE_OPTIONS: { key: string; text: string; entityLogicalName: string }[] = [
  { key: 'matter', text: 'Matter', entityLogicalName: 'sprk_matter' },
  { key: 'project', text: 'Project', entityLogicalName: 'sprk_project' },
  { key: 'invoice', text: 'Invoice', entityLogicalName: 'sprk_invoice' },
  { key: 'event', text: 'Event', entityLogicalName: 'sprk_event' },
];

// ---------------------------------------------------------------------------
// Xrm helpers (frame-walking pattern)
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */
interface XrmUtility {
  lookupObjects: (options: Record<string, unknown>) => Promise<Array<{ id: string; name: string; entityType: string }>>;
}

interface XrmHandle {
  Utility: XrmUtility;
}

function resolveXrm(): XrmHandle | null {
  const frames: Window[] = [window];
  try { if (window.parent !== window) frames.push(window.parent); } catch { /* cross-origin */ }
  try { if (window.top && window.top !== window) frames.push(window.top); } catch { /* cross-origin */ }

  for (const frame of frames) {
    try {
      const xrm = (frame as any).Xrm;
      if (xrm?.Utility?.lookupObjects) {
        return xrm as XrmHandle;
      }
    } catch {
      // Cross-origin frame -- skip
    }
  }
  return null;
}
/* eslint-enable @typescript-eslint/no-explicit-any */

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
  formRow: {
    display: 'flex',
    alignItems: 'flex-end',
    gap: tokens.spacingHorizontalM,
  },
  dropdownWrapper: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    flex: 1,
    maxWidth: '300px',
  },
  fieldLabel: {
    color: tokens.colorNeutralForeground2,
  },
  selectedRecord: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
  },
  selectedIcon: {
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
  },
  selectedText: {
    flex: 1,
    color: tokens.colorNeutralForeground1,
  },
  checkboxSection: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  checkboxHint: {
    color: tokens.colorNeutralForeground3,
    paddingLeft: '30px',
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SelectWorkStep: React.FC<ISelectWorkStepProps> = ({
  onValidChange,
  onFormValues,
  initialValues,
}) => {
  const styles = useStyles();

  const [recordType, setRecordType] = React.useState<'' | 'matter' | 'project' | 'invoice' | 'event'>(initialValues?.recordType ?? '');
  const [recordId, setRecordId] = React.useState(initialValues?.recordId ?? '');
  const [recordName, setRecordName] = React.useState(initialValues?.recordName ?? '');
  const [assignWithoutRecord, setAssignWithoutRecord] = React.useState(initialValues?.assignWithoutRecord ?? false);
  const [isSelecting, setIsSelecting] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // Report validity + values
  React.useEffect(() => {
    const isValid = assignWithoutRecord || recordId !== '';
    onValidChange(isValid);
    onFormValues({ recordType, recordId, recordName, assignWithoutRecord });
  }, [recordType, recordId, recordName, assignWithoutRecord, onValidChange, onFormValues]);

  const handleRecordTypeChange = React.useCallback(
    (_e: unknown, data: { optionValue?: string }) => {
      const val = (data.optionValue ?? '') as '' | 'matter' | 'project' | 'invoice' | 'event';
      setRecordType(val);
      // Clear previous selection when entity type changes
      if (recordId) {
        setRecordId('');
        setRecordName('');
      }
    },
    [recordId]
  );

  // Handle record selection via Xrm.Utility.lookupObjects
  const handleSelectRecord = React.useCallback(async () => {
    if (!recordType) return;

    const option = RECORD_TYPE_OPTIONS.find((o) => o.key === recordType);
    if (!option) return;

    const xrm = resolveXrm();
    if (!xrm) {
      setError('Xrm not available -- cannot open record picker.');
      return;
    }

    try {
      setError(null);
      setIsSelecting(true);
      const results = await xrm.Utility.lookupObjects({
        defaultEntityType: option.entityLogicalName,
        entityTypes: [option.entityLogicalName],
        allowMultiSelect: false,
      });

      if (!results || results.length === 0) return; // User cancelled

      const selected = results[0];
      const cleanId = selected.id.replace(/[{}]/g, '').toLowerCase();
      setRecordId(cleanId);
      setRecordName(selected.name);
    } catch (err) {
      console.error('[SelectWorkStep] Record selection failed:', err);
      setError(err instanceof Error ? err.message : 'Failed to select record.');
    } finally {
      setIsSelecting(false);
    }
  }, [recordType]);

  const handleClear = React.useCallback(() => {
    setRecordId('');
    setRecordName('');
  }, []);

  const handleAssignWithoutChange = React.useCallback(
    (_e: unknown, data: { checked: boolean | 'mixed' }) => {
      const checked = data.checked === true;
      setAssignWithoutRecord(checked);
      if (checked) {
        setRecordId('');
        setRecordName('');
      }
    },
    []
  );

  const selectedTypeText = RECORD_TYPE_OPTIONS.find((o) => o.key === recordType)?.text ?? '';
  const hasSelection = recordId !== '';

  return (
    <div className={styles.root}>
      {/* Step header */}
      <div className={styles.headerText}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Work to Assign
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Select the subject matter that is to be assigned for work responsibility.
        </Text>
      </div>

      {/* Error banner */}
      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {/* Record Type dropdown + Select Record button */}
      <div className={styles.formRow}>
        <div className={styles.dropdownWrapper}>
          <Text size={200} weight="semibold" className={styles.fieldLabel}>
            Record Type
          </Text>
          <Dropdown
            value={selectedTypeText}
            selectedOptions={recordType ? [recordType] : []}
            onOptionSelect={handleRecordTypeChange}
            placeholder="Select record type..."
            disabled={assignWithoutRecord || isSelecting}
          >
            {RECORD_TYPE_OPTIONS.map((opt) => (
              <Option key={opt.key} value={opt.key}>
                {opt.text}
              </Option>
            ))}
          </Dropdown>
        </div>
        <Button
          appearance="primary"
          icon={<SearchRegular />}
          onClick={handleSelectRecord}
          disabled={!recordType || assignWithoutRecord || isSelecting}
        >
          Select Record
        </Button>
      </div>

      {/* Selected record display */}
      {hasSelection && (
        <div className={styles.selectedRecord}>
          <CheckmarkCircleRegular fontSize={20} className={styles.selectedIcon} />
          <Text size={300} weight="semibold" className={styles.selectedText}>
            {recordName}
          </Text>
          <Text size={200} className={styles.fieldLabel}>
            ({selectedTypeText})
          </Text>
          <Button
            appearance="subtle"
            icon={<DismissRegular />}
            size="small"
            onClick={handleClear}
            aria-label="Clear selection"
          />
        </div>
      )}

      {/* Resolving spinner */}
      {isSelecting && (
        <Spinner size="tiny" label="Opening record picker..." />
      )}

      {/* Divider */}
      <Divider>or</Divider>

      {/* Assign without record checkbox */}
      <div className={styles.checkboxSection}>
        <Checkbox
          label="Assign work without a specific record"
          checked={assignWithoutRecord}
          onChange={handleAssignWithoutChange}
          disabled={isSelecting}
        />
        <Text size={200} className={styles.checkboxHint}>
          The work assignment will be created without a parent record link.
          You can associate it later.
        </Text>
      </div>
    </div>
  );
};

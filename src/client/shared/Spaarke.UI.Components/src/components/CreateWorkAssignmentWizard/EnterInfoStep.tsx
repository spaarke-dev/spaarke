/**
 * EnterInfoStep.tsx
 * Step 3: "Enter Info" -- core work assignment fields.
 *
 * Fields: Name (required), Description, Matter Type, Practice Area,
 *         Priority (required), Response Due Date (required).
 *
 * Pre-fill:
 *   - From the record selected in Step 1 (matching fields)
 *   - From AI processing output when no record is associated
 *   The orchestrator passes pre-filled values via initialValues.
 *
 * Dependencies are injected via props -- no solution-specific imports.
 */
import * as React from 'react';
import {
  Text,
  Input,
  Textarea,
  Dropdown,
  Option,
  Field,
  Spinner,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { LookupField } from '../LookupField/LookupField';
import type { ILookupItem } from '../../types/LookupTypes';
import { useAiPrefill, type IResolvedPrefillFields } from '../../hooks/useAiPrefill';
import type { IUploadedFile } from '../FileUpload/fileUploadTypes';
import { searchMatterTypes, searchPracticeAreas } from './workAssignmentService';
import type { ICreateWorkAssignmentFormState } from './formTypes';
import type { IDataService } from '../../types/serviceInterfaces';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IEnterInfoStepProps {
  dataService: IDataService;
  onValidChange: (isValid: boolean) => void;
  onFormValues: (values: Pick<
    ICreateWorkAssignmentFormState,
    'name' | 'description' | 'matterTypeId' | 'matterTypeName' | 'practiceAreaId' | 'practiceAreaName' | 'priority' | 'responseDueDate'
  >) => void;
  initialValues?: Partial<ICreateWorkAssignmentFormState>;
  /** Files uploaded in the Add Files step -- used for AI pre-fill when no record is selected. */
  uploadedFiles?: IUploadedFile[];
  /** True when initialValues came from a selected record (skip AI pre-fill). */
  hasInitialValues?: boolean;
  /** Authenticated fetch function for BFF API calls. */
  authenticatedFetch: AuthenticatedFetchFn;
  /** BFF API base URL. */
  bffBaseUrl: string;
}

// ---------------------------------------------------------------------------
// Priority options
// ---------------------------------------------------------------------------

const PRIORITY_OPTIONS = [
  { key: 100000000, text: 'Low' },
  { key: 100000001, text: 'Normal' },
  { key: 100000002, text: 'High' },
  { key: 100000003, text: 'Urgent' },
];

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: '10px',
  },
  headerText: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    marginBottom: tokens.spacingVerticalS,
  },
  stepTitle: {
    color: tokens.colorNeutralForeground1,
  },
  stepSubtitle: {
    color: tokens.colorNeutralForeground3,
  },
  row: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: tokens.spacingHorizontalM,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const EnterInfoStep: React.FC<IEnterInfoStepProps> = ({
  dataService,
  onValidChange,
  onFormValues,
  initialValues,
  uploadedFiles = [],
  hasInitialValues = false,
  authenticatedFetch,
  bffBaseUrl,
}) => {
  const styles = useStyles();

  const [name, setName] = React.useState(initialValues?.name ?? '');
  const [description, setDescription] = React.useState(initialValues?.description ?? '');
  const [matterTypeId, setMatterTypeId] = React.useState(initialValues?.matterTypeId ?? '');
  const [matterTypeName, setMatterTypeName] = React.useState(initialValues?.matterTypeName ?? '');
  const [practiceAreaId, setPracticeAreaId] = React.useState(initialValues?.practiceAreaId ?? '');
  const [practiceAreaName, setPracticeAreaName] = React.useState(initialValues?.practiceAreaName ?? '');
  const [priority, setPriority] = React.useState(initialValues?.priority ?? 100000001);
  const [responseDueDate, setResponseDueDate] = React.useState(initialValues?.responseDueDate ?? '');

  // Track whether initial values have been applied (for pre-fill)
  const appliedPrefillRef = React.useRef(false);

  // Apply pre-fill values when initialValues change (e.g., record loaded)
  React.useEffect(() => {
    if (!initialValues || appliedPrefillRef.current) return;
    if (initialValues.name || initialValues.description || initialValues.matterTypeId || initialValues.practiceAreaId) {
      if (initialValues.name) setName(initialValues.name);
      if (initialValues.description) setDescription(initialValues.description);
      if (initialValues.matterTypeId) {
        setMatterTypeId(initialValues.matterTypeId);
        setMatterTypeName(initialValues.matterTypeName ?? '');
      }
      if (initialValues.practiceAreaId) {
        setPracticeAreaId(initialValues.practiceAreaId);
        setPracticeAreaName(initialValues.practiceAreaName ?? '');
      }
      if (initialValues.priority) setPriority(initialValues.priority);
      if (initialValues.responseDueDate) setResponseDueDate(initialValues.responseDueDate);
      appliedPrefillRef.current = true;
    }
  }, [initialValues]);

  // -- AI Pre-fill via shared hook --------------------------------------------
  const handlePrefillApply = React.useCallback(
    (resolved: IResolvedPrefillFields) => {
      for (const [key, value] of Object.entries(resolved)) {
        if (typeof value === 'string') {
          if (key === 'name') setName(value);
          else if (key === 'description') setDescription(value);
        } else {
          // Lookup resolved: { id, name }
          if (key === 'matterTypeName') {
            setMatterTypeId(value.id);
            setMatterTypeName(value.name);
          } else if (key === 'practiceAreaName') {
            setPracticeAreaId(value.id);
            setPracticeAreaName(value.name);
          }
        }
      }
    },
    []
  );

  const prefill = useAiPrefill({
    endpoint: '/workspace/matters/pre-fill',
    uploadedFiles,
    authenticatedFetch,
    bffBaseUrl,
    fieldExtractor: (data) => ({
      textFields: {
        name: data.matterName as string | undefined,
        description: data.summary as string | undefined,
      },
      lookupFields: {
        matterTypeName: (data.matterTypeName || data.matterType) as string | undefined,
        practiceAreaName: (data.practiceAreaName || data.practiceArea) as string | undefined,
      },
    }),
    lookupResolvers: {
      matterTypeName: (v) => searchMatterTypes(dataService, v),
      practiceAreaName: (v) => searchPracticeAreas(dataService, v),
    },
    onApply: handlePrefillApply,
    skipIfInitialized: hasInitialValues,
    logPrefix: 'WorkAssignment',
  });

  // Report validity + values -- Priority and Response Due Date are mandatory
  React.useEffect(() => {
    const isValid =
      name.trim().length > 0 &&
      priority > 0 &&
      responseDueDate.trim().length > 0;
    onValidChange(isValid);
    onFormValues({ name, description, matterTypeId, matterTypeName, practiceAreaId, practiceAreaName, priority, responseDueDate });
  }, [name, description, matterTypeId, matterTypeName, practiceAreaId, practiceAreaName, priority, responseDueDate, onValidChange, onFormValues]);

  const handleNameChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => setName(e.target.value),
    []
  );

  const handleDescriptionChange = React.useCallback(
    (e: React.ChangeEvent<HTMLTextAreaElement>) => setDescription(e.target.value),
    []
  );

  const handleMatterTypeChange = React.useCallback(
    (item: ILookupItem | null) => {
      setMatterTypeId(item?.id ?? '');
      setMatterTypeName(item?.name ?? '');
    },
    []
  );

  const handleSearchMatterTypes = React.useCallback(
    (query: string) => searchMatterTypes(dataService, query),
    [dataService]
  );

  const handlePracticeAreaChange = React.useCallback(
    (item: ILookupItem | null) => {
      setPracticeAreaId(item?.id ?? '');
      setPracticeAreaName(item?.name ?? '');
    },
    []
  );

  const handleSearchPracticeAreas = React.useCallback(
    (query: string) => searchPracticeAreas(dataService, query),
    [dataService]
  );

  const handlePriorityChange = React.useCallback(
    (_e: unknown, data: { optionValue?: string }) => {
      setPriority(parseInt(data.optionValue ?? '100000001', 10));
    },
    []
  );

  const handleDueDateChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => setResponseDueDate(e.target.value),
    []
  );

  const matterTypeValue: ILookupItem | null = matterTypeId ? { id: matterTypeId, name: matterTypeName } : null;
  const practiceAreaValue: ILookupItem | null = practiceAreaId ? { id: practiceAreaId, name: practiceAreaName } : null;
  const selectedPriorityText = PRIORITY_OPTIONS.find((o) => o.key === priority)?.text ?? 'Normal';

  if (prefill.status === 'loading') {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: tokens.spacingVerticalL, padding: tokens.spacingVerticalXXL }}>
        <Spinner size="medium" label="Analyzing uploaded files..." />
      </div>
    );
  }

  return (
    <div className={styles.root}>
      {/* Step header -- title and description on separate lines */}
      <div className={styles.headerText}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Enter Info
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Provide the details for the work assignment.
        </Text>
      </div>

      <Field label="Name" required>
        <Input
          value={name}
          onChange={handleNameChange}
          placeholder="Work assignment name"
          autoComplete="off"
        />
      </Field>

      <Field label="Description">
        <Textarea
          value={description}
          onChange={handleDescriptionChange}
          placeholder="Describe the work assignment..."
          rows={6}
          resize="vertical"
        />
      </Field>

      <div className={styles.row}>
        <LookupField
          label="Matter Type"
          value={matterTypeValue}
          onChange={handleMatterTypeChange}
          onSearch={handleSearchMatterTypes}
          placeholder="Search matter types..."
        />
        <LookupField
          label="Practice Area"
          value={practiceAreaValue}
          onChange={handlePracticeAreaChange}
          onSearch={handleSearchPracticeAreas}
          placeholder="Search practice areas..."
        />
      </div>

      <div className={styles.row}>
        <Field label="Priority" required>
          <Dropdown
            value={selectedPriorityText}
            selectedOptions={[String(priority)]}
            onOptionSelect={handlePriorityChange}
          >
            {PRIORITY_OPTIONS.map((opt) => (
              <Option key={opt.key} value={String(opt.key)}>
                {opt.text}
              </Option>
            ))}
          </Dropdown>
        </Field>
        <Field label="Response Due Date" required>
          <Input
            type="date"
            value={responseDueDate}
            onChange={handleDueDateChange}
          />
        </Field>
      </div>
    </div>
  );
};

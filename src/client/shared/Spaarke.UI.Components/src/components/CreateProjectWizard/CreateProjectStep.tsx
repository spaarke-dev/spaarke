/**
 * CreateProjectStep.tsx
 * Form step for the "Create New Project" wizard — 2-column grid with lookup fields.
 *
 * Mirrors CreateMatter/CreateRecordStep with AI pre-fill support:
 *   - On mount, sends uploaded files to BFF /workspace/projects/pre-fill
 *   - AI-extracted display names are fuzzy-matched against Dataverse lookups
 *   - Pre-filled fields show "AI" badge via AiFieldTag
 *   - Skeleton loading state while AI is processing
 *
 * Layout (CSS Grid):
 *   +---------------------------+------------------------------+
 *   |  Project Type (lookup)    |  Practice Area (lookup)       |
 *   +---------------------------+------------------------------+
 *   |  Project Name (Input, full-width) *                       |
 *   +---------------------------+------------------------------+
 *   |  Project Description (Textarea, full-width, optional)     |
 *   +----------------------------------------------------------+
 *
 * Form validation:
 *   Required: Project Name (non-empty after trim)
 *   -> `onValidChange(true)` emitted when projectName has a value
 *
 * Constraints:
 *   - Fluent v9 only: Input, Textarea, Field, Text, Skeleton, Badge
 *   - makeStyles with semantic tokens — ZERO hardcoded colours
 *   - Supports light, dark, and high-contrast modes
 */

import * as React from 'react';
import {
  Badge,
  Field,
  Input,
  Skeleton,
  SkeletonItem,
  Text,
  Textarea,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  ICreateProjectFormState,
  EMPTY_PROJECT_FORM,
} from './projectFormTypes';
import { ProjectService } from './projectService';
import { LookupField } from '../LookupField';
import { AiFieldTag } from '../AiFieldTag';
import { SecureProjectSection } from './SecureProjectSection';
import type { ILookupItem } from '../../types/LookupTypes';
import type { IDataService } from '../../types/serviceInterfaces';
import type { IUploadedFile } from '../FileUpload/fileUploadTypes';
import { useAiPrefill, type IResolvedPrefillFields } from '../../hooks/useAiPrefill';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const PREFILL_PATH = '/workspace/projects/pre-fill';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICreateProjectStepProps {
  /** IDataService reference for Dataverse lookup queries. */
  dataService: IDataService;
  /** Called when form validity changes. Parent uses this to enable/disable Next. */
  onValidChange: (isValid: boolean) => void;
  /** Called on every form change with the latest form values. */
  onFormValues: (values: ICreateProjectFormState) => void;
  /**
   * Actual uploaded file objects from Step 1. Needed for multipart/form-data
   * upload to the BFF AI pre-fill endpoint. Empty array if no files uploaded.
   */
  uploadedFiles?: IUploadedFile[];
  /**
   * Initial form values from the parent. When provided with non-empty values
   * (e.g. on remount after navigating back), the step initialises from these
   * instead of starting empty. This preserves user edits and Assign Resources
   * overrides that were written to the parent's form state.
   */
  initialFormValues?: ICreateProjectFormState;
  /** MSAL-backed authenticated fetch function for BFF API calls. */
  authenticatedFetch?: typeof fetch;
  /** BFF API base URL. */
  bffBaseUrl?: string;
}

// ---------------------------------------------------------------------------
// AI pre-fill types (local)
// ---------------------------------------------------------------------------

type AiPrefillStatus = 'idle' | 'loading' | 'success' | 'error';

interface IAiPrefillState {
  status: AiPrefillStatus;
  prefilledFields: Set<keyof ICreateProjectFormState>;
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

  // ── Step header ──────────────────────────────────────────────────────────
  headerRow: {
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
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

  // ── AI Pre-filled badge ───────────────────────────────────────────────────
  aiBadge: {
    flexShrink: 0,
    marginTop: tokens.spacingVerticalXS,
  },

  // ── 2-column grid ─────────────────────────────────────────────────────────
  formGrid: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: `${tokens.spacingVerticalL} ${tokens.spacingHorizontalL}`,
  },

  // Fields that should span both columns
  fullWidth: {
    gridColumn: '1 / -1',
  },

  // ── Skeleton items ────────────────────────────────────────────────────────
  skeletonField: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  skeletonLabel: {
    width: '120px',
    height: '16px',
  },
  skeletonInput: {
    width: '100%',
    height: '32px',
  },
  skeletonTextareaLg: {
    width: '100%',
    height: '100px',
  },
});

// ---------------------------------------------------------------------------
// Sub-components
// ---------------------------------------------------------------------------

/** Skeleton placeholder for a single-line input field during AI loading. */
const FieldSkeleton: React.FC<{ large?: boolean }> = ({ large }) => {
  const styles = useStyles();
  return (
    <Skeleton className={styles.skeletonField}>
      <SkeletonItem className={styles.skeletonLabel} />
      <SkeletonItem className={large ? styles.skeletonTextareaLg : styles.skeletonInput} />
    </Skeleton>
  );
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function isFormValid(form: ICreateProjectFormState): boolean {
  return form.projectName.trim() !== '';
}

// ---------------------------------------------------------------------------
// CreateProjectStep (exported)
// ---------------------------------------------------------------------------

export const CreateProjectStep: React.FC<ICreateProjectStepProps> = ({
  dataService,
  onValidChange,
  onFormValues,
  uploadedFiles = [],
  initialFormValues,
  authenticatedFetch: authFetch,
  bffBaseUrl,
}) => {
  const styles = useStyles();

  // ── Form state ──────────────────────────────────────────────────────────
  // When initialFormValues is provided with non-empty values (remount after
  // user navigated back), start from those to preserve user edits and
  // Assign Resources overrides. Otherwise start empty for first mount.
  const hasInitialValues = initialFormValues && initialFormValues.projectName.trim() !== '';
  const [formState, setFormState] = React.useState<ICreateProjectFormState>(
    () => hasInitialValues ? { ...initialFormValues } : { ...EMPTY_PROJECT_FORM }
  );

  // ── AI pre-fill state ───────────────────────────────────────────────────
  const [aiState, setAiState] = React.useState<IAiPrefillState>({
    status: 'idle',
    prefilledFields: new Set(),
  });

  // ── Service ref (stable across re-renders) ─────────────────────────────
  const serviceRef = React.useRef<ProjectService>(new ProjectService(dataService));
  React.useEffect(() => {
    serviceRef.current = new ProjectService(dataService);
  }, [dataService]);

  // ── Notify parent of validity changes ──────────────────────────────────
  const valid = isFormValid(formState);
  React.useEffect(() => {
    onValidChange(valid);
  }, [valid, onValidChange]);

  // ── Emit latest form values to parent on every change ──────────────────
  const onFormValuesRef = React.useRef(onFormValues);
  React.useEffect(() => {
    onFormValuesRef.current = onFormValues;
  }, [onFormValues]);

  React.useEffect(() => {
    onFormValuesRef.current(formState);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formState]);

  // ── AI Pre-fill via shared hook ────────────────────────────────────────
  const handlePrefillApply = React.useCallback(
    (resolved: IResolvedPrefillFields, _prefilledFieldNames: string[]) => {
      const updates: Partial<ICreateProjectFormState> = {};
      const pfFields = new Set<keyof ICreateProjectFormState>();

      for (const [key, value] of Object.entries(resolved)) {
        if (typeof value === 'string') {
          (updates as Record<string, string>)[key] = value;
          pfFields.add(key as keyof ICreateProjectFormState);
        } else {
          // Lookup resolved: set both id and name fields
          const idKey = key.replace(/Name$/, 'Id');
          (updates as Record<string, string>)[idKey] = value.id;
          (updates as Record<string, string>)[key] = value.name;
          pfFields.add(idKey as keyof ICreateProjectFormState);
        }
      }

      if (Object.keys(updates).length > 0) {
        setFormState((prev) => ({ ...prev, ...updates }));
      }
      setAiState({ status: 'success', prefilledFields: pfFields });
    },
    []
  );

  // Only run AI pre-fill if authenticatedFetch and bffBaseUrl are provided
  const prefill = useAiPrefill({
    endpoint: PREFILL_PATH,
    uploadedFiles,
    authenticatedFetch: authFetch ?? ((...args: Parameters<typeof fetch>) => fetch(...args)),
    bffBaseUrl: bffBaseUrl ?? '',
    fieldExtractor: (data) => ({
      textFields: {
        projectName: data.projectName as string | undefined,
        description: data.description as string | undefined,
      },
      lookupFields: {
        projectTypeName: data.projectTypeName as string | undefined,
        practiceAreaName: data.practiceAreaName as string | undefined,
        assignedAttorneyName: data.assignedAttorneyName as string | undefined,
        assignedParalegalName: data.assignedParalegalName as string | undefined,
        assignedOutsideCounselName: data.assignedOutsideCounselName as string | undefined,
      },
    }),
    lookupResolvers: {
      projectTypeName: (v) => serviceRef.current.searchProjectTypes(v),
      practiceAreaName: (v) => serviceRef.current.searchPracticeAreas(v),
      assignedAttorneyName: (v) => serviceRef.current.searchContacts(v),
      assignedParalegalName: (v) => serviceRef.current.searchContacts(v),
      assignedOutsideCounselName: (v) => serviceRef.current.searchOrganizations(v),
    },
    onApply: handlePrefillApply,
    skipIfInitialized: !!hasInitialValues,
    logPrefix: 'CreateProject',
  });

  // Sync hook status with local AI state for loading/error display
  React.useEffect(() => {
    if (prefill.status === 'loading') {
      setAiState({ status: 'loading', prefilledFields: new Set() });
    } else if (prefill.status === 'error') {
      setAiState({ status: 'error', prefilledFields: new Set() });
    }
  }, [prefill.status]);

  // ── Lookup search callbacks (stable refs) ──────────────────────────────

  const handleSearchProjectTypes = React.useCallback(
    (query: string) => serviceRef.current.searchProjectTypes(query),
    []
  );

  const handleSearchPracticeAreas = React.useCallback(
    (query: string) => serviceRef.current.searchPracticeAreas(query),
    []
  );

  // ── Lookup change handlers ─────────────────────────────────────────────

  const handleProjectTypeChange = React.useCallback(
    (item: ILookupItem | null) => {
      setFormState((prev) => ({
        ...prev,
        projectTypeId: item?.id ?? '',
        projectTypeName: item?.name ?? '',
      }));
    },
    []
  );

  const handlePracticeAreaChange = React.useCallback(
    (item: ILookupItem | null) => {
      setFormState((prev) => ({
        ...prev,
        practiceAreaId: item?.id ?? '',
        practiceAreaName: item?.name ?? '',
      }));
    },
    []
  );

  // ── Text field change handlers ─────────────────────────────────────────

  const handleProjectNameChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setFormState((prev) => ({ ...prev, projectName: e.target.value }));
    },
    []
  );

  const handleDescriptionChange = React.useCallback(
    (e: React.ChangeEvent<HTMLTextAreaElement>) => {
      setFormState((prev) => ({ ...prev, description: e.target.value }));
    },
    []
  );

  const handleSecureChange = React.useCallback(
    (value: boolean) => {
      setFormState((prev) => ({ ...prev, isSecure: value }));
    },
    []
  );

  // ── Derived ───────────────────────────────────────────────────────────
  const isLoading = aiState.status === 'loading';
  const hasAnyPrefill = aiState.prefilledFields.size > 0;

  const isAiField = (field: keyof ICreateProjectFormState): boolean =>
    aiState.prefilledFields.has(field);

  // Build lookup value objects from form state
  const projectTypeValue: ILookupItem | null = formState.projectTypeId
    ? { id: formState.projectTypeId, name: formState.projectTypeName }
    : null;

  const practiceAreaValue: ILookupItem | null = formState.practiceAreaId
    ? { id: formState.practiceAreaId, name: formState.practiceAreaName }
    : null;

  // ── Render ─────────────────────────────────────────────────────────────
  return (
    <div className={styles.root}>
      {/* Step header */}
      <div className={styles.headerRow}>
        <div className={styles.headerText}>
          <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
            Enter Info
          </Text>
          <Text size={200} className={styles.stepSubtitle}>
            {isLoading
              ? 'Analysing uploaded documents\u2026'
              : 'Fill in the project details. Required fields are marked with *.'}
          </Text>
        </div>

        {hasAnyPrefill && (
          <Badge
            className={styles.aiBadge}
            appearance="tint"
            color="brand"
            icon={<span aria-hidden="true" />}
          >
            AI Pre-filled
          </Badge>
        )}
      </div>

      {/* ── Secure Project toggle ── */}
      {!isLoading && (
        <SecureProjectSection
          isSecure={formState.isSecure}
          onSecureChange={handleSecureChange}
        />
      )}

      {/* 2-column form grid */}
      {isLoading ? (
        <div className={styles.formGrid}>
          <FieldSkeleton />
          <FieldSkeleton />
          <div className={styles.fullWidth}>
            <FieldSkeleton />
          </div>
          <div className={styles.fullWidth}>
            <FieldSkeleton large />
          </div>
        </div>
      ) : (
        <div className={styles.formGrid}>
          {/* ── Row 1: Project Type + Practice Area ── */}

          <LookupField
            label="Project Type"
            placeholder="Search project types..."
            value={projectTypeValue}
            onChange={handleProjectTypeChange}
            onSearch={handleSearchProjectTypes}
            labelExtra={isAiField('projectTypeId') ? <AiFieldTag /> : undefined}
            minSearchLength={1}
          />

          <LookupField
            label="Practice Area"
            placeholder="Search practice areas..."
            value={practiceAreaValue}
            onChange={handlePracticeAreaChange}
            onSearch={handleSearchPracticeAreas}
            labelExtra={isAiField('practiceAreaId') ? <AiFieldTag /> : undefined}
            minSearchLength={1}
          />

          {/* ── Row 2: Project Name (full width, required) ── */}

          <Field
            className={styles.fullWidth}
            label={
              <span>
                Project Name
                <span aria-hidden="true" style={{ color: tokens.colorPaletteRedForeground1 }}>
                  {' *'}
                </span>
                {isAiField('projectName') && <AiFieldTag />}
              </span>
            }
            required
          >
            <Input
              value={formState.projectName}
              onChange={handleProjectNameChange}
              placeholder="Enter project name"
              aria-label="Project Name"
            />
          </Field>

          {/* ── Row 3: Project Description (full width, optional) ── */}

          <Field
            className={styles.fullWidth}
            label={
              <span>
                Project Description
                {isAiField('description') && <AiFieldTag />}
              </span>
            }
          >
            <Textarea
              value={formState.description}
              onChange={handleDescriptionChange}
              placeholder="Brief description of the project, its objectives, and scope"
              rows={11}
              resize="vertical"
              aria-label="Project Description"
              style={{ width: '100%' }}
            />
          </Field>
        </div>
      )}
    </div>
  );
};

export { CreateProjectStep as default };

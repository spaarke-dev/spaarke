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
 *   ┌───────────────────────────┬──────────────────────────────┐
 *   │  Project Type (lookup)    │  Practice Area (lookup)       │
 *   ├───────────────────────────┴──────────────────────────────┤
 *   │  Project Name (Input, full-width) *                       │
 *   ├───────────────────────────┬──────────────────────────────┤
 *   │  Assigned Attorney (lookup)│  Assigned Paralegal (lookup) │
 *   ├───────────────────────────┴──────────────────────────────┤
 *   │  Assigned Outside Counsel (lookup)                        │
 *   ├───────────────────────────┴──────────────────────────────┤
 *   │  Description (Textarea, full-width, optional)             │
 *   └──────────────────────────────────────────────────────────┘
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
import { LookupField } from '../CreateMatter/LookupField';
import { AiFieldTag } from '../CreateMatter/AiFieldTag';
import type { ILookupItem } from '../../types/entities';
import type { IWebApi } from '../../types/xrm';
import type { IUploadedFile } from '../CreateMatter/wizardTypes';
import { getBffBaseUrl } from '../../config/bffConfig';
import { authenticatedFetch } from '../../services/bffAuthProvider';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const PREFILL_PATH = '/workspace/projects/pre-fill';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICreateProjectStepProps {
  /** Xrm.WebApi reference for Dataverse lookup queries. */
  webApi: IWebApi;
  /** Called when form validity changes. Parent uses this to enable/disable Next. */
  onValidChange: (isValid: boolean) => void;
  /** Called on every form change with the latest form values. */
  onFormValues: (values: ICreateProjectFormState) => void;
  /**
   * Actual uploaded file objects from Step 1. Needed for multipart/form-data
   * upload to the BFF AI pre-fill endpoint. Empty array if no files uploaded.
   */
  uploadedFiles?: IUploadedFile[];
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

/**
 * Fuzzy-match an AI-generated display name against Dataverse lookup results.
 * Same scoring as CreateRecordStep: 1.0 exact, 0.8 prefix, 0.7 contains, 0.5 single, 0.4 threshold.
 */
function findBestLookupMatch(
  aiValue: string,
  candidates: ILookupItem[]
): ILookupItem | null {
  if (candidates.length === 0) return null;

  const aiLower = aiValue.toLowerCase().trim();
  let bestScore = 0;
  let bestItem: ILookupItem | null = null;

  for (const item of candidates) {
    const dbLower = item.name.toLowerCase().trim();
    let score = 0;

    if (dbLower === aiLower) {
      score = 1.0;
    } else if (dbLower.startsWith(aiLower) || aiLower.startsWith(dbLower)) {
      score = 0.8;
    } else if (dbLower.includes(aiLower) || aiLower.includes(dbLower)) {
      score = 0.7;
    }

    if (score > bestScore) {
      bestScore = score;
      bestItem = item;
    }
  }

  if (bestScore < 0.4 && candidates.length === 1) {
    bestScore = 0.5;
    bestItem = candidates[0];
  }

  return bestScore >= 0.4 ? bestItem : null;
}

// ---------------------------------------------------------------------------
// CreateProjectStep (exported)
// ---------------------------------------------------------------------------

export const CreateProjectStep: React.FC<ICreateProjectStepProps> = ({
  webApi,
  onValidChange,
  onFormValues,
  uploadedFiles = [],
}) => {
  const styles = useStyles();

  // ── Form state ──────────────────────────────────────────────────────────
  const [formState, setFormState] = React.useState<ICreateProjectFormState>(
    () => ({ ...EMPTY_PROJECT_FORM })
  );

  // ── AI pre-fill state ───────────────────────────────────────────────────
  const [aiState, setAiState] = React.useState<IAiPrefillState>({
    status: 'idle',
    prefilledFields: new Set(),
  });

  // ── Service ref (stable across re-renders) ─────────────────────────────
  const serviceRef = React.useRef<ProjectService>(new ProjectService(webApi));
  React.useEffect(() => {
    serviceRef.current = new ProjectService(webApi);
  }, [webApi]);

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

  // ── AI Pre-fill on mount ───────────────────────────────────────────────
  const prefillAttemptedRef = React.useRef(false);

  React.useEffect(() => {
    if (uploadedFiles.length === 0 || prefillAttemptedRef.current) {
      return;
    }

    prefillAttemptedRef.current = true;
    let cancelled = false;
    const abortController = new AbortController();

    const runPrefill = async (): Promise<void> => {
      setAiState({ status: 'loading', prefilledFields: new Set() });

      const timeoutId = window.setTimeout(() => abortController.abort(), 60_000);

      try {
        const bffBaseUrl = getBffBaseUrl();
        const formData = new FormData();
        for (const f of uploadedFiles) {
          formData.append('files', f.file, f.name);
        }

        console.info('[CreateProject] Starting AI pre-fill...', { fileCount: uploadedFiles.length });

        const response = await authenticatedFetch(`${bffBaseUrl}${PREFILL_PATH}`, {
          method: 'POST',
          body: formData,
          signal: abortController.signal,
        });

        clearTimeout(timeoutId);
        if (cancelled) return;

        if (!response.ok) {
          console.warn(`[CreateProject] Pre-fill returned ${response.status}`);
          setAiState({ status: 'error', prefilledFields: new Set() });
          return;
        }

        const data = await response.json();
        console.info('[CreateProject] Pre-fill response:', data);
        if (cancelled) return;

        // Map BFF response fields to form state updates
        const updates: Partial<ICreateProjectFormState> = {};
        const prefilledFields = new Set<keyof ICreateProjectFormState>();

        const aiProjectType = data.projectTypeName;
        const aiPracticeArea = data.practiceAreaName;
        const aiAttorney = data.assignedAttorneyName;
        const aiParalegal = data.assignedParalegalName;
        const aiOutsideCounsel = data.assignedOutsideCounselName;

        if (data.projectName) {
          updates.projectName = data.projectName;
          prefilledFields.add('projectName');
        }
        if (data.description) {
          updates.description = data.description;
          prefilledFields.add('description');
        }

        // Fuzzy-resolve lookup fields against Dataverse
        const resolvePromises: Promise<void>[] = [];

        if (aiProjectType && webApi) {
          resolvePromises.push(
            serviceRef.current.searchProjectTypes(aiProjectType).then((results) => {
              const best = findBestLookupMatch(aiProjectType, results);
              if (best) {
                updates.projectTypeId = best.id;
                updates.projectTypeName = best.name;
                prefilledFields.add('projectTypeId');
              }
            }).catch(() => { /* keep display name only */ })
          );
        }

        if (aiPracticeArea && webApi) {
          resolvePromises.push(
            serviceRef.current.searchPracticeAreas(aiPracticeArea).then((results) => {
              const best = findBestLookupMatch(aiPracticeArea, results);
              if (best) {
                updates.practiceAreaId = best.id;
                updates.practiceAreaName = best.name;
                prefilledFields.add('practiceAreaId');
              }
            }).catch(() => { /* keep display name only */ })
          );
        }

        if (aiAttorney && webApi) {
          resolvePromises.push(
            serviceRef.current.searchContacts(aiAttorney).then((results) => {
              const best = findBestLookupMatch(aiAttorney, results);
              if (best) {
                updates.assignedAttorneyId = best.id;
                updates.assignedAttorneyName = best.name;
                prefilledFields.add('assignedAttorneyId');
              }
            }).catch(() => { /* keep display name only */ })
          );
        }

        if (aiParalegal && webApi) {
          resolvePromises.push(
            serviceRef.current.searchContacts(aiParalegal).then((results) => {
              const best = findBestLookupMatch(aiParalegal, results);
              if (best) {
                updates.assignedParalegalId = best.id;
                updates.assignedParalegalName = best.name;
                prefilledFields.add('assignedParalegalId');
              }
            }).catch(() => { /* keep display name only */ })
          );
        }

        if (aiOutsideCounsel && webApi) {
          resolvePromises.push(
            serviceRef.current.searchOrganizations(aiOutsideCounsel).then((results) => {
              const best = findBestLookupMatch(aiOutsideCounsel, results);
              if (best) {
                updates.assignedOutsideCounselId = best.id;
                updates.assignedOutsideCounselName = best.name;
                prefilledFields.add('assignedOutsideCounselId');
              }
            }).catch(() => { /* keep display name only */ })
          );
        }

        await Promise.all(resolvePromises);
        if (cancelled) return;

        if (Object.keys(updates).length > 0) {
          setFormState((prev) => ({ ...prev, ...updates }));
        }

        setAiState({ status: 'success', prefilledFields });
      } catch (err) {
        clearTimeout(timeoutId);
        if (!cancelled) {
          if (abortController.signal.aborted) {
            console.warn('[CreateProject] Pre-fill timed out after 60s');
          } else {
            console.warn('[CreateProject] Pre-fill failed:', err);
          }
          setAiState({ status: 'error', prefilledFields: new Set() });
        }
      }
    };

    void runPrefill();

    return () => {
      cancelled = true;
      abortController.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [uploadedFiles.length]);

  // ── Lookup search callbacks (stable refs) ──────────────────────────────

  const handleSearchProjectTypes = React.useCallback(
    (query: string) => serviceRef.current.searchProjectTypes(query),
    []
  );

  const handleSearchPracticeAreas = React.useCallback(
    (query: string) => serviceRef.current.searchPracticeAreas(query),
    []
  );

  const handleSearchAttorneys = React.useCallback(
    (query: string) => serviceRef.current.searchContacts(query),
    []
  );

  const handleSearchParalegals = React.useCallback(
    (query: string) => serviceRef.current.searchContacts(query),
    []
  );

  const handleSearchOutsideCounsel = React.useCallback(
    (query: string) => serviceRef.current.searchOrganizations(query),
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

  const handleAttorneyChange = React.useCallback(
    (item: ILookupItem | null) => {
      setFormState((prev) => ({
        ...prev,
        assignedAttorneyId: item?.id ?? '',
        assignedAttorneyName: item?.name ?? '',
      }));
    },
    []
  );

  const handleParalegalChange = React.useCallback(
    (item: ILookupItem | null) => {
      setFormState((prev) => ({
        ...prev,
        assignedParalegalId: item?.id ?? '',
        assignedParalegalName: item?.name ?? '',
      }));
    },
    []
  );

  const handleOutsideCounselChange = React.useCallback(
    (item: ILookupItem | null) => {
      setFormState((prev) => ({
        ...prev,
        assignedOutsideCounselId: item?.id ?? '',
        assignedOutsideCounselName: item?.name ?? '',
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

  const attorneyValue: ILookupItem | null = formState.assignedAttorneyId
    ? { id: formState.assignedAttorneyId, name: formState.assignedAttorneyName }
    : null;

  const paralegalValue: ILookupItem | null = formState.assignedParalegalId
    ? { id: formState.assignedParalegalId, name: formState.assignedParalegalName }
    : null;

  const outsideCounselValue: ILookupItem | null = formState.assignedOutsideCounselId
    ? { id: formState.assignedOutsideCounselId, name: formState.assignedOutsideCounselName }
    : null;

  // ── Render ─────────────────────────────────────────────────────────────
  return (
    <div className={styles.root}>
      {/* Step header */}
      <div className={styles.headerRow}>
        <div className={styles.headerText}>
          <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
            Create record
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

      {/* 2-column form grid */}
      {isLoading ? (
        <div className={styles.formGrid}>
          <FieldSkeleton />
          <FieldSkeleton />
          <div className={styles.fullWidth}>
            <FieldSkeleton />
          </div>
          <FieldSkeleton />
          <FieldSkeleton />
          <FieldSkeleton />
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
            isAiPrefilled={isAiField('projectTypeId')}
            minSearchLength={1}
          />

          <LookupField
            label="Practice Area"
            placeholder="Search practice areas..."
            value={practiceAreaValue}
            onChange={handlePracticeAreaChange}
            onSearch={handleSearchPracticeAreas}
            isAiPrefilled={isAiField('practiceAreaId')}
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

          {/* ── Row 3: Assigned Attorney + Assigned Paralegal ── */}

          <LookupField
            label="Assigned Attorney"
            placeholder="Search contacts..."
            value={attorneyValue}
            onChange={handleAttorneyChange}
            onSearch={handleSearchAttorneys}
            isAiPrefilled={isAiField('assignedAttorneyId')}
            minSearchLength={2}
          />

          <LookupField
            label="Assigned Paralegal"
            placeholder="Search contacts..."
            value={paralegalValue}
            onChange={handleParalegalChange}
            onSearch={handleSearchParalegals}
            isAiPrefilled={isAiField('assignedParalegalId')}
            minSearchLength={2}
          />

          {/* ── Row 3b: Assigned Outside Counsel ── */}

          <LookupField
            label="Assigned Outside Counsel"
            placeholder="Search organizations..."
            value={outsideCounselValue}
            onChange={handleOutsideCounselChange}
            onSearch={handleSearchOutsideCounsel}
            isAiPrefilled={isAiField('assignedOutsideCounselId')}
            minSearchLength={2}
          />

          {/* ── Row 4: Description (full width, optional) ── */}

          <Field
            className={styles.fullWidth}
            label={
              <span>
                Description
                {isAiField('description') && <AiFieldTag />}
              </span>
            }
          >
            <Textarea
              value={formState.description}
              onChange={handleDescriptionChange}
              placeholder="Brief description of the project, its objectives, and scope"
              rows={5}
              resize="vertical"
              aria-label="Description"
            />
          </Field>
        </div>
      )}
    </div>
  );
};

export { CreateProjectStep as default };

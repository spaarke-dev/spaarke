/**
 * CreateProjectStep.tsx
 * Form step for the "Create New Project" wizard — 2-column grid with lookup fields.
 *
 * Simplified version of CreateMatter/CreateRecordStep — no AI pre-fill, no file
 * upload context. Just a clean form for entering project details.
 *
 * Layout (CSS Grid):
 *   ┌───────────────────────────┬──────────────────────────────┐
 *   │  Project Type (lookup)    │  Practice Area (lookup)       │
 *   ├───────────────────────────┴──────────────────────────────┤
 *   │  Project Name (Input, full-width) *                       │
 *   ├───────────────────────────┬──────────────────────────────┤
 *   │  Assigned Attorney (lookup)│  Assigned Paralegal (lookup) │
 *   ├───────────────────────────┴──────────────────────────────┤
 *   │  Description (Textarea, full-width, optional)             │
 *   └──────────────────────────────────────────────────────────┘
 *
 * Lookup fields use the reusable LookupField component with debounced
 * Dataverse search via ProjectService.
 *
 * Form validation:
 *   Required: Project Name (non-empty after trim)
 *   -> `onValidChange(true)` emitted when projectName has a value
 *
 * Constraints:
 *   - Fluent v9 only: Input, Textarea, Field, Text
 *   - makeStyles with semantic tokens — ZERO hardcoded colours
 *   - Supports light, dark, and high-contrast modes
 */

import * as React from 'react';
import {
  Field,
  Input,
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
import type { ILookupItem } from '../../types/entities';
import type { IWebApi } from '../../types/xrm';

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
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Derives whether all required fields have values (for Next-button enablement).
 * Only projectName is required for this form.
 */
function isFormValid(form: ICreateProjectFormState): boolean {
  return form.projectName.trim() !== '';
}

// ---------------------------------------------------------------------------
// CreateProjectStep (exported)
// ---------------------------------------------------------------------------

export const CreateProjectStep: React.FC<ICreateProjectStepProps> = ({
  webApi,
  onValidChange,
  onFormValues,
}) => {
  const styles = useStyles();

  // ── Form state ──────────────────────────────────────────────────────────
  const [formState, setFormState] = React.useState<ICreateProjectFormState>(
    () => ({ ...EMPTY_PROJECT_FORM })
  );

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

  // ── Build lookup value objects from form state ─────────────────────────

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

  // ── Render ─────────────────────────────────────────────────────────────
  return (
    <div className={styles.root}>
      {/* Step header */}
      <div className={styles.headerText}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Create record
        </Text>
        <Text size={300} className={styles.stepSubtitle}>
          Fill in the project details below.
        </Text>
      </div>

      {/* 2-column form grid */}
      <div className={styles.formGrid}>
        {/* ── Row 1: Project Type + Practice Area ── */}

        <LookupField
          label="Project Type"
          placeholder="Search project types..."
          value={projectTypeValue}
          onChange={handleProjectTypeChange}
          onSearch={handleSearchProjectTypes}
          minSearchLength={1}
        />

        <LookupField
          label="Practice Area"
          placeholder="Search practice areas..."
          value={practiceAreaValue}
          onChange={handlePracticeAreaChange}
          onSearch={handleSearchPracticeAreas}
          minSearchLength={1}
        />

        {/* ── Row 2: Project Name (full width, required) ── */}

        <Field
          className={styles.fullWidth}
          label="Project Name"
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
          minSearchLength={2}
        />

        <LookupField
          label="Assigned Paralegal"
          placeholder="Search contacts..."
          value={paralegalValue}
          onChange={handleParalegalChange}
          onSearch={handleSearchParalegals}
          minSearchLength={2}
        />

        {/* ── Row 4: Description (full width, optional) ── */}

        <Field
          className={styles.fullWidth}
          label="Description"
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
    </div>
  );
};

export { CreateProjectStep as default };

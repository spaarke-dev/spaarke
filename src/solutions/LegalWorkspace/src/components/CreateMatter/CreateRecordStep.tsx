/**
 * CreateRecordStep.tsx
 * Step 2 of the "Create New Matter" wizard — 2-column form with lookup fields.
 *
 * Layout (CSS Grid):
 *   ┌───────────────────────────┬──────────────────────────────┐
 *   │  Matter Type (lookup)     │  Practice Area (lookup)       │
 *   ├───────────────────────────┴──────────────────────────────┤
 *   │  Matter Name (Input, full-width) *                       │
 *   ├───────────────────────────┬──────────────────────────────┤
 *   │  Assigned Attorney (lookup)│  Assigned Paralegal (lookup) │
 *   ├───────────────────────────┴──────────────────────────────┤
 *   │  Summary (Textarea, full-width) + "Generate with AI"     │
 *   └──────────────────────────────────────────────────────────┘
 *
 * Lookup fields use LookupField component with debounced Dataverse search.
 * Summary has an AI generate button that calls BFF endpoint.
 *
 * Form validation:
 *   Required: Matter Type, Practice Area, Matter Name
 *   → `onValidChange(true)` emitted when all three have values
 *
 * Constraints:
 *   - Fluent v9 only: Input, Textarea, Field, Label, Skeleton, Button, Spinner
 *   - makeStyles with semantic tokens — ZERO hardcoded colours
 *   - Supports light, dark, and high-contrast modes
 */

import * as React from 'react';
import {
  Field,
  Input,
  Skeleton,
  SkeletonItem,
  Text,
  Badge,
  Textarea,
  Button,
  Spinner,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { SparkleRegular } from '@fluentui/react-icons';
import {
  ICreateRecordStepProps,
  ICreateMatterFormState,
  IAiPrefillState,
  IAiPrefillFields,
  FormAction,
} from './formTypes';
import { AiFieldTag } from './AiFieldTag';
import { LookupField } from './LookupField';
import {
  searchMatterTypes,
  searchPracticeAreas,
  searchContactsAsLookup,
  searchOrganizationsAsLookup,
  fetchAiDraftSummary,
} from './matterService';
import type { ILookupItem } from '../../types/entities';
import { getBffBaseUrl } from '../../config/runtimeConfig';
import { authenticatedFetch } from '../../services/authInit';
import { useAiPrefill, type IResolvedPrefillFields } from '../../../../../client/shared/Spaarke.UI.Components/src/hooks/useAiPrefill';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const PREFILL_PATH = '/workspace/matters/pre-fill';

// ---------------------------------------------------------------------------
// Initial state factories
// ---------------------------------------------------------------------------

function buildInitialFormState(): ICreateMatterFormState {
  return {
    matterTypeId: '',
    matterTypeName: '',
    practiceAreaId: '',
    practiceAreaName: '',
    matterName: '',
    assignedAttorneyId: '',
    assignedAttorneyName: '',
    assignedParalegalId: '',
    assignedParalegalName: '',
    assignedOutsideCounselId: '',
    assignedOutsideCounselName: '',
    summary: '',
  };
}

function buildInitialAiState(): IAiPrefillState {
  return {
    status: 'idle',
    prefilledFields: new Set(),
  };
}

// ---------------------------------------------------------------------------
// Reducer
// ---------------------------------------------------------------------------

interface ICombinedState {
  form: ICreateMatterFormState;
  ai: IAiPrefillState;
}

function combinedReducer(
  state: ICombinedState,
  action: FormAction
): ICombinedState {
  switch (action.type) {
    case 'SET_FIELD': {
      return {
        ...state,
        form: { ...state.form, [action.field]: action.value },
      };
    }

    case 'SET_LOOKUP': {
      return {
        ...state,
        form: {
          ...state.form,
          [action.idField]: action.id,
          [action.nameField]: action.name,
        },
      };
    }

    case 'APPLY_AI_PREFILL': {
      const fields = action.fields;
      const nextForm = { ...state.form };
      const prefilledFields = new Set<keyof ICreateMatterFormState>();

      (Object.keys(fields) as (keyof IAiPrefillFields)[]).forEach((key) => {
        const val = fields[key];
        if (val !== undefined && val !== '') {
          (nextForm as Record<string, string>)[key] = val as string;
          prefilledFields.add(key as keyof ICreateMatterFormState);
        }
      });

      return {
        form: nextForm,
        ai: {
          ...state.ai,
          prefilledFields,
        },
      };
    }

    case 'AI_PREFILL_LOADING': {
      return {
        ...state,
        ai: { status: 'loading', prefilledFields: new Set() },
      };
    }

    case 'AI_PREFILL_SUCCESS': {
      return {
        ...state,
        ai: { ...state.ai, status: 'success' },
      };
    }

    case 'AI_PREFILL_ERROR': {
      return {
        ...state,
        ai: { status: 'error', prefilledFields: new Set() },
      };
    }

    case 'CLEAR_ERRORS': {
      return state;
    }

    default:
      return state;
  }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Derives whether all required fields have values (for Next-button enablement).
 */
function isFormValid(form: ICreateMatterFormState): boolean {
  return (
    form.matterTypeId !== '' &&
    form.practiceAreaId !== '' &&
    form.matterName.trim() !== ''
  );
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

  // ── Field label row (label + optional AI tag) ─────────────────────────────
  labelRow: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },

  // ── Summary section ────────────────────────────────────────────────────────
  summaryHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
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
// CreateRecordStep (exported)
// ---------------------------------------------------------------------------

export const CreateRecordStep: React.FC<ICreateRecordStepProps> = ({
  webApi,
  uploadedFileNames,
  uploadedFiles,
  onValidChange,
  onSubmit,
  initialFormValues,
}) => {
  const styles = useStyles();

  // ── Reducer ────────────────────────────────────────────────────────────────
  // When initialFormValues is provided with non-empty values (remount after
  // user navigated back), start from those values to preserve user edits and
  // Assign Resources overrides. Otherwise start empty for first mount.
  const hasInitialValues = initialFormValues && initialFormValues.matterName.trim() !== '';
  const [state, dispatch] = React.useReducer(combinedReducer, undefined, () => ({
    form: hasInitialValues ? { ...initialFormValues } : buildInitialFormState(),
    ai: buildInitialAiState(),
  }));

  const { form, ai } = state;

  // ── AI summary generation state ────────────────────────────────────────────
  const [isGeneratingSummary, setIsGeneratingSummary] = React.useState(false);

  // ── Notify parent of validity changes ─────────────────────────────────────
  const valid = isFormValid(form);
  React.useEffect(() => {
    onValidChange(valid);
  }, [valid, onValidChange]);

  // ── Emit latest form values to parent on every change ─────────────────────
  const onSubmitRef = React.useRef(onSubmit);
  React.useEffect(() => {
    onSubmitRef.current = onSubmit;
  }, [onSubmit]);

  React.useEffect(() => {
    onSubmitRef.current(form);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [form]);

  // ── AI Pre-fill via shared hook ────────────────────────────────────────────
  const handlePrefillApply = React.useCallback(
    (resolved: IResolvedPrefillFields, prefilledFieldNames: string[]) => {
      const fields: IAiPrefillFields = {};
      for (const [key, value] of Object.entries(resolved)) {
        if (typeof value === 'string') {
          (fields as Record<string, string>)[key] = value;
        } else {
          // Lookup resolved: set both id and name fields
          // e.g., matterTypeName → { id, name } → set matterTypeId + matterTypeName
          const idKey = key.replace(/Name$/, 'Id');
          (fields as Record<string, string>)[idKey] = value.id;
          (fields as Record<string, string>)[key] = value.name;
        }
      }
      if (Object.keys(fields).length > 0) {
        dispatch({ type: 'APPLY_AI_PREFILL', fields });
      }
      dispatch({ type: 'AI_PREFILL_SUCCESS' });
    },
    []
  );

  const prefill = useAiPrefill({
    endpoint: PREFILL_PATH,
    uploadedFiles,
    authenticatedFetch,
    bffBaseUrl: getBffBaseUrl(),
    fieldExtractor: (data) => ({
      textFields: {
        matterName: data.matterName as string | undefined,
        summary: data.summary as string | undefined,
      },
      lookupFields: {
        // BFF may return old or new field names — handle both
        matterTypeName: (data.matterTypeName || data.matterType) as string | undefined,
        practiceAreaName: (data.practiceAreaName || data.practiceArea) as string | undefined,
        assignedAttorneyName: data.assignedAttorneyName as string | undefined,
        assignedParalegalName: data.assignedParalegalName as string | undefined,
        assignedOutsideCounselName: data.assignedOutsideCounselName as string | undefined,
      },
    }),
    lookupResolvers: {
      matterTypeName: (v) => searchMatterTypes(webApi, v),
      practiceAreaName: (v) => searchPracticeAreas(webApi, v),
      assignedAttorneyName: (v) => searchContactsAsLookup(webApi, v),
      assignedParalegalName: (v) => searchContactsAsLookup(webApi, v),
      assignedOutsideCounselName: (v) => searchOrganizationsAsLookup(webApi, v),
    },
    onApply: handlePrefillApply,
    skipIfInitialized: !!hasInitialValues,
    logPrefix: 'CreateMatter',
  });

  // Sync hook status with reducer for loading/error states
  React.useEffect(() => {
    if (prefill.status === 'loading') {
      dispatch({ type: 'AI_PREFILL_LOADING' });
    } else if (prefill.status === 'error') {
      dispatch({ type: 'AI_PREFILL_ERROR' });
    }
  }, [prefill.status]);

  // ── Lookup search callbacks (stable refs) ──────────────────────────────────

  const handleSearchMatterTypes = React.useCallback(
    (query: string) => searchMatterTypes(webApi, query),
    [webApi]
  );

  const handleSearchPracticeAreas = React.useCallback(
    (query: string) => searchPracticeAreas(webApi, query),
    [webApi]
  );

  const handleSearchAttorneys = React.useCallback(
    (query: string) => searchContactsAsLookup(webApi, query),
    [webApi]
  );

  const handleSearchParalegals = React.useCallback(
    (query: string) => searchContactsAsLookup(webApi, query),
    [webApi]
  );

  const handleSearchOutsideCounsel = React.useCallback(
    (query: string) => searchOrganizationsAsLookup(webApi, query),
    [webApi]
  );

  // ── Lookup change handlers ─────────────────────────────────────────────────

  const handleMatterTypeChange = React.useCallback(
    (item: ILookupItem | null) => {
      dispatch({
        type: 'SET_LOOKUP',
        idField: 'matterTypeId',
        nameField: 'matterTypeName',
        id: item?.id ?? '',
        name: item?.name ?? '',
      });
    },
    []
  );

  const handlePracticeAreaChange = React.useCallback(
    (item: ILookupItem | null) => {
      dispatch({
        type: 'SET_LOOKUP',
        idField: 'practiceAreaId',
        nameField: 'practiceAreaName',
        id: item?.id ?? '',
        name: item?.name ?? '',
      });
    },
    []
  );

  const handleAttorneyChange = React.useCallback(
    (item: ILookupItem | null) => {
      dispatch({
        type: 'SET_LOOKUP',
        idField: 'assignedAttorneyId',
        nameField: 'assignedAttorneyName',
        id: item?.id ?? '',
        name: item?.name ?? '',
      });
    },
    []
  );

  const handleParalegalChange = React.useCallback(
    (item: ILookupItem | null) => {
      dispatch({
        type: 'SET_LOOKUP',
        idField: 'assignedParalegalId',
        nameField: 'assignedParalegalName',
        id: item?.id ?? '',
        name: item?.name ?? '',
      });
    },
    []
  );

  const handleOutsideCounselChange = React.useCallback(
    (item: ILookupItem | null) => {
      dispatch({
        type: 'SET_LOOKUP',
        idField: 'assignedOutsideCounselId',
        nameField: 'assignedOutsideCounselName',
        id: item?.id ?? '',
        name: item?.name ?? '',
      });
    },
    []
  );

  // ── Text field change handler ──────────────────────────────────────────────

  const handleFieldChange = React.useCallback(
    (field: keyof ICreateMatterFormState) =>
      (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
        dispatch({ type: 'SET_FIELD', field, value: e.target.value });
      },
    []
  );

  // ── AI Summary generation ──────────────────────────────────────────────────

  const handleGenerateSummary = React.useCallback(async () => {
    setIsGeneratingSummary(true);
    try {
      const result = await fetchAiDraftSummary(
        form.matterName,
        form.matterTypeName,
        form.practiceAreaName
      );
      dispatch({ type: 'SET_FIELD', field: 'summary', value: result.summary });
    } finally {
      setIsGeneratingSummary(false);
    }
  }, [form.matterName, form.matterTypeName, form.practiceAreaName]);

  // ── Derived ───────────────────────────────────────────────────────────────
  const isLoading = ai.status === 'loading';
  const hasAnyPrefill = ai.prefilledFields.size > 0;

  const isAiField = (field: keyof ICreateMatterFormState): boolean =>
    ai.prefilledFields.has(field);

  // Build lookup value objects from form state
  const matterTypeValue: ILookupItem | null = form.matterTypeId
    ? { id: form.matterTypeId, name: form.matterTypeName }
    : null;

  const practiceAreaValue: ILookupItem | null = form.practiceAreaId
    ? { id: form.practiceAreaId, name: form.practiceAreaName }
    : null;

  const attorneyValue: ILookupItem | null = form.assignedAttorneyId
    ? { id: form.assignedAttorneyId, name: form.assignedAttorneyName }
    : null;

  const paralegalValue: ILookupItem | null = form.assignedParalegalId
    ? { id: form.assignedParalegalId, name: form.assignedParalegalName }
    : null;

  const outsideCounselValue: ILookupItem | null = form.assignedOutsideCounselId
    ? { id: form.assignedOutsideCounselId, name: form.assignedOutsideCounselName }
    : null;

  /**
   * Renders the label for a text field with optional required mark and AI tag.
   */
  const renderLabel = (
    text: string,
    field: keyof ICreateMatterFormState,
    required?: boolean
  ): React.ReactElement => (
    <span className={styles.labelRow}>
      {text}
      {required && (
        <span aria-hidden="true" style={{ color: tokens.colorPaletteRedForeground1 }}>
          {' *'}
        </span>
      )}
      {isAiField(field) && <AiFieldTag />}
    </span>
  );

  // ── Render ────────────────────────────────────────────────────────────────
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
              : 'Fill in the matter details. Required fields are marked with *.'}
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
          <div className={styles.fullWidth}>
            <FieldSkeleton large />
          </div>
        </div>
      ) : (
        <div className={styles.formGrid}>
          {/* ── Row 1: Matter Type + Practice Area ── */}

          <LookupField
            label="Matter Type"
            required
            placeholder="Search matter types..."
            value={matterTypeValue}
            onChange={handleMatterTypeChange}
            onSearch={handleSearchMatterTypes}
            isAiPrefilled={isAiField('matterTypeId')}
            minSearchLength={1}
          />

          <LookupField
            label="Practice Area"
            required
            placeholder="Search practice areas..."
            value={practiceAreaValue}
            onChange={handlePracticeAreaChange}
            onSearch={handleSearchPracticeAreas}
            isAiPrefilled={isAiField('practiceAreaId')}
            minSearchLength={1}
          />

          {/* ── Row 2: Matter Name (full width) ── */}

          <Field
            className={styles.fullWidth}
            label={renderLabel('Matter Name', 'matterName', true)}
            required
          >
            <Input
              value={form.matterName}
              onChange={handleFieldChange('matterName')}
              placeholder="Enter matter name"
              aria-label="Matter Name"
            />
          </Field>

          {/* ── Row 3: Matter Description — full width with AI generate ── */}

          <div className={styles.fullWidth}>
            <div className={styles.summaryHeader}>
              {renderLabel('Matter Description', 'summary')}
              <Button
                appearance="subtle"
                size="small"
                icon={isGeneratingSummary ? <Spinner size="extra-tiny" /> : <SparkleRegular />}
                onClick={handleGenerateSummary}
                disabled={isGeneratingSummary || !form.matterName.trim()}
                aria-label="Generate description with AI"
              >
                {isGeneratingSummary ? 'Generating\u2026' : 'Generate with AI'}
              </Button>
            </div>
            <Textarea
              value={form.summary}
              onChange={handleFieldChange('summary')}
              placeholder="Brief description of the matter, its background, and objectives"
              rows={11}
              resize="vertical"
              aria-label="Matter Description"
              style={{ marginTop: tokens.spacingVerticalXS, width: '100%' }}
            />
          </div>
        </div>
      )}
    </div>
  );
};

export { CreateRecordStep as default };

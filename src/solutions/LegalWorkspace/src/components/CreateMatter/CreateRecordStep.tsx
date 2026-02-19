/**
 * CreateRecordStep.tsx
 * Step 2 of the "Create New Matter" wizard — 2-column form with AI pre-fill.
 *
 * Layout (CSS Grid):
 *   ┌───────────────────────────┬──────────────────────────────┐
 *   │  Matter Type (Dropdown)   │  Practice Area (Dropdown)    │
 *   │  Matter Name (Input) *    │  Organization (Input) *      │
 *   │  Estimated Budget (Input) │  Key Parties (Textarea)      │
 *   ├───────────────────────────┴──────────────────────────────┤
 *   │  Summary (Textarea, full-width, 5 rows)                  │
 *   └──────────────────────────────────────────────────────────┘
 *
 * AI Pre-fill lifecycle:
 *   1. On mount, if uploadedFileNames.length > 0 → POST /api/workspace/matters/pre-fill
 *   2. While call is in-flight → each field shows a Skeleton placeholder
 *   3. On success → fields are populated; AI-populated fields get an AiFieldTag
 *      next to their label + an "AI Pre-filled" badge at top-right
 *   4. On BFF error → fields stay empty (graceful fallback, no error modal)
 *   5. User may edit any field regardless of AI pre-fill state
 *
 * Form validation:
 *   Required: Matter Type, Practice Area, Matter Name, Organization
 *   → `onValidChange(true)` emitted when all four have values
 *
 * Constraints:
 *   - Fluent v9 only: Dropdown (Select), Input, Textarea, Field, Label, Skeleton
 *   - makeStyles with semantic tokens — ZERO hardcoded colours
 *   - SparkleRegular from @fluentui/react-icons for AI indicator
 *   - Supports light, dark, and high-contrast modes
 */

import * as React from 'react';
import {
  Field,
  Input,
  Label,
  Select,
  Skeleton,
  SkeletonItem,
  Text,
  Badge,
  Textarea,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  ICreateRecordStepProps,
  ICreateMatterFormState,
  IAiPrefillState,
  IAiPrefillFields,
  FormAction,
  IAiPrefillResponse,
  MatterType,
  PracticeArea,
} from './formTypes';
import { AiFieldTag } from './AiFieldTag';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const MATTER_TYPE_OPTIONS: MatterType[] = [
  'Litigation',
  'Transaction',
  'Advisory',
  'Regulatory',
  'IP',
  'Employment',
];

const PRACTICE_AREA_OPTIONS: PracticeArea[] = [
  'Corporate',
  'Real Estate',
  'IP',
  'Employment',
  'Litigation',
  'Tax',
  'Environmental',
];

const PREFILL_ENDPOINT = '/api/workspace/matters/pre-fill';

// ---------------------------------------------------------------------------
// Initial state factories
// ---------------------------------------------------------------------------

function buildInitialFormState(): ICreateMatterFormState {
  return {
    matterType: '',
    matterName: '',
    estimatedBudget: '',
    practiceArea: '',
    organization: '',
    keyParties: '',
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

    case 'APPLY_AI_PREFILL': {
      const fields = action.fields;
      const nextForm = { ...state.form };
      const prefilledFields = new Set<keyof ICreateMatterFormState>();

      (Object.keys(fields) as (keyof IAiPrefillFields)[]).forEach((key) => {
        const val = fields[key];
        if (val !== undefined && val !== '') {
          // Type-safe assignment — both IAiPrefillFields and ICreateMatterFormState
          // share the same field names and compatible value types.
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
      // No errors are stored in state; validation is derived — no-op
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
    form.matterType !== '' &&
    form.matterName.trim() !== '' &&
    form.practiceArea !== '' &&
    form.organization.trim() !== ''
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

  // Fields that should span both columns (Summary)
  fullWidth: {
    gridColumn: '1 / -1',
  },

  // ── Field label row (label + optional AI tag) ─────────────────────────────
  labelRow: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
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
  skeletonTextarea: {
    width: '100%',
    height: '72px',
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
const FieldSkeleton: React.FC<{ textareaRows?: number }> = ({
  textareaRows,
}) => {
  const styles = useStyles();
  const inputClass =
    textareaRows && textareaRows >= 5
      ? styles.skeletonTextareaLg
      : textareaRows
      ? styles.skeletonTextarea
      : styles.skeletonInput;

  return (
    <Skeleton className={styles.skeletonField}>
      <SkeletonItem className={styles.skeletonLabel} />
      <SkeletonItem className={inputClass} />
    </Skeleton>
  );
};

// ---------------------------------------------------------------------------
// CreateRecordStep (exported)
// ---------------------------------------------------------------------------

export const CreateRecordStep: React.FC<ICreateRecordStepProps> = ({
  uploadedFileNames,
  onValidChange,
  onSubmit,
}) => {
  const styles = useStyles();

  // ── Reducer ────────────────────────────────────────────────────────────────
  const [state, dispatch] = React.useReducer(combinedReducer, undefined, () => ({
    form: buildInitialFormState(),
    ai: buildInitialAiState(),
  }));

  const { form, ai } = state;

  // ── Notify parent of validity changes ─────────────────────────────────────
  const valid = isFormValid(form);
  React.useEffect(() => {
    onValidChange(valid);
  }, [valid, onValidChange]);

  // ── Expose submit values to parent via ref-based callback ──────────────────
  // The parent wizard calls onSubmit when advancing to Step 3.
  // We use a stable ref so the latest form values are always captured.
  const latestFormRef = React.useRef<ICreateMatterFormState>(form);
  React.useEffect(() => {
    latestFormRef.current = form;
  }, [form]);

  // Store onSubmit in ref to keep the effect below stable.
  const onSubmitRef = React.useRef(onSubmit);
  React.useEffect(() => {
    onSubmitRef.current = onSubmit;
  }, [onSubmit]);

  // ── Emit latest form values to parent on every change ─────────────────────
  // This allows WizardDialog (task 024) to always have current form values
  // without requiring an imperative "submit" call on step transition.
  React.useEffect(() => {
    onSubmitRef.current(form);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [form]);

  // ── AI Pre-fill on mount ───────────────────────────────────────────────────
  React.useEffect(() => {
    if (uploadedFileNames.length === 0) {
      // No files — stay idle, show empty form
      return;
    }

    let cancelled = false;

    const runPrefill = async (): Promise<void> => {
      dispatch({ type: 'AI_PREFILL_LOADING' });

      try {
        const response = await fetch(PREFILL_ENDPOINT, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ fileNames: uploadedFileNames }),
        });

        if (cancelled) return;

        if (!response.ok) {
          // Graceful fallback: BFF error → empty form, no error modal
          dispatch({ type: 'AI_PREFILL_ERROR' });
          return;
        }

        const data: IAiPrefillResponse = await response.json();

        if (cancelled) return;

        if (data.fields && Object.keys(data.fields).length > 0) {
          dispatch({ type: 'APPLY_AI_PREFILL', fields: data.fields });
        }

        dispatch({ type: 'AI_PREFILL_SUCCESS' });
      } catch {
        if (!cancelled) {
          // Network error or JSON parse failure → graceful fallback
          dispatch({ type: 'AI_PREFILL_ERROR' });
        }
      }
    };

    void runPrefill();

    return () => {
      cancelled = true;
    };
    // Only re-run when the files list reference changes (i.e. on step entry).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [uploadedFileNames]);

  // ── Field change handlers ──────────────────────────────────────────────────
  const handleFieldChange = React.useCallback(
    (field: keyof ICreateMatterFormState) =>
      (
        e: React.ChangeEvent<
          HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement
        >
      ) => {
        dispatch({ type: 'SET_FIELD', field, value: e.target.value });
      },
    []
  );

  // ── Derived ───────────────────────────────────────────────────────────────
  const isLoading = ai.status === 'loading';
  const hasAnyPrefill = ai.prefilledFields.size > 0;

  /** Returns true if the given field was pre-filled by AI. */
  const isAiField = (field: keyof ICreateMatterFormState): boolean =>
    ai.prefilledFields.has(field);

  /**
   * Renders the label for a field.  If the field was AI-pre-filled, appends
   * an AiFieldTag next to the label text.
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
            Create record
          </Text>
          <Text size={200} className={styles.stepSubtitle}>
            {isLoading
              ? 'Analysing uploaded documents\u2026'
              : 'Fill in the matter details. Required fields are marked with *.'}
          </Text>
        </div>

        {/* AI Pre-filled badge — shown once pre-fill completes with ≥1 field */}
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
        /* Skeleton placeholders while BFF call is in-flight */
        <div className={styles.formGrid}>
          <FieldSkeleton />
          <FieldSkeleton />
          <FieldSkeleton />
          <FieldSkeleton />
          <FieldSkeleton />
          <FieldSkeleton textareaRows={3} />
          <div className={styles.fullWidth}>
            <FieldSkeleton textareaRows={5} />
          </div>
        </div>
      ) : (
        <div className={styles.formGrid}>
          {/* ── Left column ── */}

          {/* Matter Type */}
          <Field
            label={renderLabel('Matter Type', 'matterType', true)}
            required
          >
            <Select
              value={form.matterType}
              onChange={handleFieldChange('matterType')}
              aria-label="Matter Type"
            >
              <option value="">Select matter type</option>
              {MATTER_TYPE_OPTIONS.map((opt) => (
                <option key={opt} value={opt}>
                  {opt}
                </option>
              ))}
            </Select>
          </Field>

          {/* Practice Area */}
          <Field
            label={renderLabel('Practice Area', 'practiceArea', true)}
            required
          >
            <Select
              value={form.practiceArea}
              onChange={handleFieldChange('practiceArea')}
              aria-label="Practice Area"
            >
              <option value="">Select practice area</option>
              {PRACTICE_AREA_OPTIONS.map((opt) => (
                <option key={opt} value={opt}>
                  {opt}
                </option>
              ))}
            </Select>
          </Field>

          {/* Matter Name */}
          <Field
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

          {/* Organization */}
          <Field
            label={renderLabel('Organization', 'organization', true)}
            required
          >
            <Input
              value={form.organization}
              onChange={handleFieldChange('organization')}
              placeholder="Enter organization"
              aria-label="Organization"
            />
          </Field>

          {/* Estimated Budget */}
          <Field label={renderLabel('Estimated Budget', 'estimatedBudget')}>
            <Input
              type="number"
              value={form.estimatedBudget}
              onChange={handleFieldChange('estimatedBudget')}
              placeholder="0.00"
              contentBefore={
                <Label aria-hidden="true">$</Label>
              }
              aria-label="Estimated Budget"
            />
          </Field>

          {/* Key Parties */}
          <Field label={renderLabel('Key Parties', 'keyParties')}>
            <Textarea
              value={form.keyParties}
              onChange={handleFieldChange('keyParties')}
              placeholder="List key individuals, entities, or counterparties"
              rows={3}
              resize="vertical"
              aria-label="Key Parties"
            />
          </Field>

          {/* Summary — full width */}
          <Field
            className={styles.fullWidth}
            label={renderLabel('Summary', 'summary')}
          >
            <Textarea
              value={form.summary}
              onChange={handleFieldChange('summary')}
              placeholder="Brief description of the matter, its background, and objectives"
              rows={5}
              resize="vertical"
              aria-label="Summary"
            />
          </Field>
        </div>
      )}
    </div>
  );
};

// ---------------------------------------------------------------------------
// Imperative handle for parent wizard to request form values
// ---------------------------------------------------------------------------

/**
 * The parent wizard (WizardDialog) needs to read the current form state when
 * the user clicks Next.  Rather than lifting all form state into the wizard,
 * we expose a stable callback via a custom hook approach.
 *
 * WizardDialog passes `onSubmit` — this component calls it automatically
 * when `onValidChange(true)` was the last emission AND the Next button was
 * pressed.  The actual invocation contract is:
 *
 *   1. WizardDialog calls `handleNext()`.
 *   2. WizardDialog notices currentStepIndex === 1 → calls `stepRef.current?.submit()`.
 *   3. CreateRecordStep calls `onSubmit(latestFormRef.current)`.
 *
 * However, to keep WizardDialog simple for this task, we expose the submit
 * callback through the `onSubmit` prop pattern.  The parent calls it via
 * a forwarded ref if it needs to, or we emit it inline via an effect when the
 * parent triggers a step transition.
 *
 * For task 023 scope: the values are emitted via `onSubmit` on every
 * keystroke so the parent always has the latest values without needing a ref.
 * Task 024 will read these values from wizard state.
 */
export { CreateRecordStep as default };

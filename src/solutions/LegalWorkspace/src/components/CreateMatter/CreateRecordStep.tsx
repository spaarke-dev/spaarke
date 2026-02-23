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
  fetchAiDraftSummary,
} from './matterService';
import type { ILookupItem } from '../../types/entities';
import { getBffBaseUrl } from '../../config/bffConfig';
import { authenticatedFetch } from '../../services/bffAuthProvider';

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

/**
 * Fuzzy-match an AI-generated display name against Dataverse lookup results.
 *
 * Scoring (highest wins, minimum 0.4 to accept):
 *   1.0  — exact match (case-insensitive)
 *   0.8  — one string starts with the other ("Corporate" ↔ "Corporate Law")
 *   0.7  — one string is contained in the other ("Trans" in "Transactional")
 *   0.5  — single result from Dataverse contains() filter (already relevant)
 *
 * Returns null if no candidate scores above threshold.
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

  // If no strong match but Dataverse contains() returned exactly one result,
  // trust it — the server-side filter already validated relevance.
  if (bestScore < 0.4 && candidates.length === 1) {
    bestScore = 0.5;
    bestItem = candidates[0];
  }

  return bestScore >= 0.4 ? bestItem : null;
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
}) => {
  const styles = useStyles();

  // ── Reducer ────────────────────────────────────────────────────────────────
  const [state, dispatch] = React.useReducer(combinedReducer, undefined, () => ({
    form: buildInitialFormState(),
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

  // ── AI Pre-fill on mount ───────────────────────────────────────────────────
  // Stable dependency key: join file names into a single string so the effect
  // doesn't re-run on every render (uploadedFileNames is a new array ref each time).
  const prefillKey = uploadedFileNames.join('|');
  const prefillAttemptedRef = React.useRef(false);

  React.useEffect(() => {
    if (uploadedFiles.length === 0 || prefillAttemptedRef.current) {
      return;
    }

    prefillAttemptedRef.current = true;
    let cancelled = false;
    const abortController = new AbortController();

    const runPrefill = async (): Promise<void> => {
      dispatch({ type: 'AI_PREFILL_LOADING' });

      // Client-side timeout: 60s (BFF has 45s playbook timeout + text extraction time)
      const timeoutId = window.setTimeout(() => abortController.abort(), 60_000);

      try {
        // Send actual files as multipart/form-data (BFF expects IFormFileCollection)
        const bffBaseUrl = getBffBaseUrl();
        const formData = new FormData();
        for (const f of uploadedFiles) {
          formData.append('files', f.file, f.name);
        }

        console.info('[CreateMatter] Starting AI pre-fill...', { fileCount: uploadedFiles.length });

        const response = await authenticatedFetch(`${bffBaseUrl}${PREFILL_PATH}`, {
          method: 'POST',
          body: formData,
          signal: abortController.signal,
          // Note: do NOT set Content-Type header — browser sets it with boundary
        });

        clearTimeout(timeoutId);

        if (cancelled) return;

        if (!response.ok) {
          console.warn(`[CreateMatter] Pre-fill returned ${response.status}`);
          dispatch({ type: 'AI_PREFILL_ERROR' });
          return;
        }

        // BFF returns flat PreFillResponse; map to IAiPrefillFields
        const data = await response.json();
        console.info('[CreateMatter] Pre-fill response:', data);

        if (cancelled) return;

        const fields: IAiPrefillFields = {};
        // BFF may return either old field names (matterType/practiceArea) or
        // new names (matterTypeName/practiceAreaName) — handle both
        const aiMatterType = data.matterTypeName || data.matterType;
        const aiPracticeArea = data.practiceAreaName || data.practiceArea;
        if (aiMatterType) fields.matterTypeName = aiMatterType;
        if (aiPracticeArea) fields.practiceAreaName = aiPracticeArea;
        if (data.matterName) fields.matterName = data.matterName;
        if (data.summary) fields.summary = data.summary;

        // Resolve AI display names to Dataverse lookup IDs so LookupField
        // renders them as selected chips (LookupField needs both id + name).
        // Uses fuzzy matching since AI output won't always exactly match
        // Dataverse values (e.g. AI says "Transactional", DB has "Transactional Law").
        const resolvePromises: Promise<void>[] = [];

        if (aiMatterType && webApi) {
          resolvePromises.push(
            searchMatterTypes(webApi, aiMatterType).then((results) => {
              const best = findBestLookupMatch(aiMatterType, results);
              if (best) {
                fields.matterTypeId = best.id;
                fields.matterTypeName = best.name;
              }
            }).catch(() => { /* keep display name only */ })
          );
        }

        if (aiPracticeArea && webApi) {
          resolvePromises.push(
            searchPracticeAreas(webApi, aiPracticeArea).then((results) => {
              const best = findBestLookupMatch(aiPracticeArea, results);
              if (best) {
                fields.practiceAreaId = best.id;
                fields.practiceAreaName = best.name;
              }
            }).catch(() => { /* keep display name only */ })
          );
        }

        await Promise.all(resolvePromises);

        if (cancelled) return;

        if (Object.keys(fields).length > 0) {
          dispatch({ type: 'APPLY_AI_PREFILL', fields });
        }

        dispatch({ type: 'AI_PREFILL_SUCCESS' });
      } catch (err) {
        clearTimeout(timeoutId);
        if (!cancelled) {
          if (abortController.signal.aborted) {
            console.warn('[CreateMatter] Pre-fill timed out after 60s');
          } else {
            console.warn('[CreateMatter] Pre-fill failed:', err);
          }
          dispatch({ type: 'AI_PREFILL_ERROR' });
        }
      }
    };

    void runPrefill();

    return () => {
      cancelled = true;
      abortController.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [prefillKey]);

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
            Create record
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
          <FieldSkeleton />
          <FieldSkeleton />
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

          {/* ── Row 4: Summary — full width with AI generate ── */}

          <div className={styles.fullWidth}>
            <div className={styles.summaryHeader}>
              {renderLabel('Summary', 'summary')}
              <Button
                appearance="subtle"
                size="small"
                icon={isGeneratingSummary ? <Spinner size="extra-tiny" /> : <SparkleRegular />}
                onClick={handleGenerateSummary}
                disabled={isGeneratingSummary || !form.matterName.trim()}
                aria-label="Generate summary with AI"
              >
                {isGeneratingSummary ? 'Generating\u2026' : 'Generate with AI'}
              </Button>
            </div>
            <Textarea
              value={form.summary}
              onChange={handleFieldChange('summary')}
              placeholder="Brief description of the matter, its background, and objectives"
              rows={5}
              resize="vertical"
              aria-label="Summary"
              style={{ marginTop: tokens.spacingVerticalXS }}
            />
          </div>
        </div>
      )}
    </div>
  );
};

export { CreateRecordStep as default };

/**
 * DraftSummaryStep.tsx
 * Follow-on step for "Draft Summary" in the Create New Matter wizard.
 *
 * On mount: calls streamAiDraftSummary from matterService (stub / BFF).
 * Uses RecipientField for "Distribute to" and "CC" with contact lookup
 * and freeform email entry.
 *
 * Constraints:
 *   - Fluent v9: Card, Textarea, Text, Spinner, Badge
 *   - SparkleRegular for AI indicator
 *   - makeStyles with semantic tokens -- ZERO hardcoded colors
 */

import * as React from 'react';
import {
  Card,
  CardHeader,
  Textarea,
  Text,
  Badge,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  SparkleRegular,
  WarningRegular,
} from '@fluentui/react-icons';
import { AiProgressStepper, DOCUMENT_ANALYSIS_STEPS } from '../AiProgressStepper';
import { streamAiDraftSummary } from './matterService';
import { RecipientField, IRecipientItem } from './RecipientField';
import type { ICreateMatterFormState } from './formTypes';
import type { ILookupItem } from '../../types/LookupTypes';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

// Step IDs match DOCUMENT_ANALYSIS_STEPS from the shared library
const ALL_STEP_IDS = DOCUMENT_ANALYSIS_STEPS.map((s) => s.id);

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IDraftSummaryStepProps {
  /** Form values from Step 2 -- used to personalise the AI prompt. */
  formValues: ICreateMatterFormState;
  /** Current AI-generated summary text (controlled). */
  summaryText: string;
  /** Called when summary text changes. */
  onSummaryChange: (text: string) => void;
  /** Current "Distribute to" recipients (controlled). */
  recipients: IRecipientItem[];
  /** Called when "Distribute to" recipients change. */
  onRecipientsChange: (recipients: IRecipientItem[]) => void;
  /** Current CC recipients (controlled). */
  ccRecipients: IRecipientItem[];
  /** Called when CC recipients change. */
  onCcRecipientsChange: (recipients: IRecipientItem[]) => void;
  /** Search function for contact lookup. */
  onSearchContacts: (query: string) => Promise<ILookupItem[]>;
  /**
   * Authenticated fetch function for BFF API calls.
   * Required for AI summary streaming. Injected by the host application.
   */
  authenticatedFetch?: (url: string, init?: RequestInit) => Promise<Response>;
  /**
   * BFF API base URL (e.g. "https://spe-api-dev-67e2xz.azurewebsites.net/api").
   * Required for AI summary streaming. Injected by the host application.
   */
  bffBaseUrl?: string;
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
  aiBadge: {
    flexShrink: 0,
    marginTop: tokens.spacingVerticalXS,
  },

  // -- AI summary card --
  summaryCard: {
    borderTopWidth: '1px',
    borderRightWidth: '1px',
    borderBottomWidth: '1px',
    borderLeftWidth: '1px',
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorBrandStroke2,
    borderRightColor: tokens.colorBrandStroke2,
    borderBottomColor: tokens.colorBrandStroke2,
    borderLeftColor: tokens.colorBrandStroke2,
    backgroundColor: tokens.colorBrandBackground2,
  },
  summaryCardHeader: {
    paddingBottom: tokens.spacingVerticalXS,
  },
  summaryHeaderInner: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorBrandForeground2,
  },
  summaryHeaderText: {
    color: tokens.colorBrandForeground2,
  },

  summaryLoading: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    minHeight: '80px',
    gap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground3,
  },

  summaryUnavailable: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground3,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },

  summaryTextarea: {
    width: '100%',
  },
});

// ---------------------------------------------------------------------------
// DraftSummaryStep (exported)
// ---------------------------------------------------------------------------

export const DraftSummaryStep: React.FC<IDraftSummaryStepProps> = ({
  formValues,
  summaryText,
  onSummaryChange,
  recipients,
  onRecipientsChange,
  ccRecipients,
  onCcRecipientsChange,
  onSearchContacts,
  authenticatedFetch,
  bffBaseUrl,
}) => {
  const styles = useStyles();

  const [summaryStatus, setSummaryStatus] = React.useState<
    'idle' | 'loading' | 'loaded' | 'error'
  >('idle');
  const hasFetchedRef = React.useRef(false);

  const [activeStepId, setActiveStepId] = React.useState<string | null>(null);
  const [completedStepIds, setCompletedStepIds] = React.useState<string[]>([]);

  // -- Stream AI summary on mount (once) -- SSE-driven step state --
  React.useEffect(() => {
    if (hasFetchedRef.current) return;
    hasFetchedRef.current = true;

    // Only fetch if summary not already set (e.g. from parent state on re-render)
    if (summaryText !== '') {
      setSummaryStatus('loaded');
      return;
    }

    const abortController = new AbortController();
    setSummaryStatus('loading');
    setActiveStepId('document_loaded');
    setCompletedStepIds([]);

    streamAiDraftSummary(
      formValues.matterName,
      formValues.matterTypeName,
      formValues.practiceAreaName,
      {
        onProgress: (stepId: string) => {
          const idx = ALL_STEP_IDS.indexOf(stepId);
          setActiveStepId(stepId);
          setCompletedStepIds(ALL_STEP_IDS.slice(0, Math.max(0, idx)));
        },
      },
      abortController.signal,
      authenticatedFetch,
      bffBaseUrl,
    )
      .then((result) => {
        if (abortController.signal.aborted) return;
        onSummaryChange(result.summary);
        setCompletedStepIds(ALL_STEP_IDS);
        setActiveStepId(null);
        setSummaryStatus('loaded');
      })
      .catch(() => {
        if (abortController.signal.aborted) return;
        setSummaryStatus('error');
      });

    return () => {
      abortController.abort();
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // -- Render --
  return (
    <div className={styles.root}>
      {/* Header */}
      <div className={styles.headerRow}>
        <div className={styles.headerText}>
          <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
            Draft Summary
          </Text>
          <Text size={200} className={styles.stepSubtitle}>
            Review and edit the AI-generated summary below, then add recipient
            email addresses for distribution.
          </Text>
        </div>
        {summaryStatus === 'loaded' && (
          <Badge
            className={styles.aiBadge}
            appearance="tint"
            color="brand"
            icon={<SparkleRegular />}
          >
            AI Generated
          </Badge>
        )}
      </div>

      {/* AI summary card */}
      <Card className={styles.summaryCard}>
        <CardHeader
          className={styles.summaryCardHeader}
          header={
            <div className={styles.summaryHeaderInner}>
              <SparkleRegular aria-hidden="true" fontSize={16} />
              <Text size={200} weight="semibold" className={styles.summaryHeaderText}>
                AI Draft Summary
              </Text>
            </div>
          }
        />

        {summaryStatus === 'loading' && (
          <AiProgressStepper
            variant="inline"
            steps={DOCUMENT_ANALYSIS_STEPS}
            activeStepId={activeStepId}
            completedStepIds={completedStepIds}
            title="Generating Summary"
            isStreaming
          />
        )}

        {summaryStatus === 'error' && (
          <div className={styles.summaryUnavailable}>
            <WarningRegular aria-hidden="true" fontSize={16} />
            <Text size={200}>
              Summary unavailable. You can type a summary manually below.
            </Text>
          </div>
        )}

        {(summaryStatus === 'loaded' || summaryStatus === 'error') && (
          <Textarea
            className={styles.summaryTextarea}
            value={summaryText}
            onChange={(e) => onSummaryChange(e.target.value)}
            placeholder="Enter or edit the matter summary here&hellip;"
            rows={10}
            resize="vertical"
            aria-label="Matter summary"
          />
        )}
      </Card>

      {/* Distribute to -- contact lookup + freeform email */}
      <RecipientField
        label="Distribute to"
        placeholder="Search contacts or type email..."
        recipients={recipients}
        onRecipientsChange={onRecipientsChange}
        onSearch={onSearchContacts}
      />

      {/* CC -- same pattern */}
      <RecipientField
        label="CC"
        placeholder="Search contacts or type email..."
        recipients={ccRecipients}
        onRecipientsChange={onCcRecipientsChange}
        onSearch={onSearchContacts}
      />
    </div>
  );
};

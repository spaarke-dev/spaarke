/**
 * DraftSummaryStep.tsx
 * Follow-on step for AI-generated summary with recipient distribution.
 *
 * Moved from LegalWorkspace's CreateMatter to the shared library.
 * The AI fetch function is provided via props (no entity-specific imports).
 *
 * @see CreateRecordWizard — wires the fetchAiSummary callback from config
 */
import * as React from 'react';
import { Card, CardHeader, Textarea, Spinner, Text, Badge, makeStyles, tokens } from '@fluentui/react-components';
import { SparkleRegular, WarningRegular } from '@fluentui/react-icons';
import { RecipientField } from './RecipientField';
import type { IRecipientItem } from '../types';
import type { ILookupItem } from '../../../types/LookupTypes';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IDraftSummaryStepProps {
  /** Current AI-generated or user-edited summary text (controlled). */
  summaryText: string;
  /** Called when summary text changes. */
  onSummaryChange: (text: string) => void;
  /** "Distribute to" recipients (controlled). */
  recipients: IRecipientItem[];
  /** Called when "Distribute to" recipients change. */
  onRecipientsChange: (recipients: IRecipientItem[]) => void;
  /** CC recipients (controlled). */
  ccRecipients: IRecipientItem[];
  /** Called when CC recipients change. */
  onCcRecipientsChange: (recipients: IRecipientItem[]) => void;
  /** Search function for contact lookup. */
  onSearchContacts: (query: string) => Promise<ILookupItem[]>;
  /**
   * Optional async function to fetch AI draft summary.
   * If not provided, the step shows a manual-entry textarea immediately.
   */
  fetchAiSummary?: () => Promise<{ summary: string }>;
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
  stepTitle: { color: tokens.colorNeutralForeground1 },
  stepSubtitle: { color: tokens.colorNeutralForeground3 },
  aiBadge: {
    flexShrink: 0,
    marginTop: tokens.spacingVerticalXS,
  },
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
  summaryHeaderText: { color: tokens.colorBrandForeground2 },
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
  summaryTextarea: { width: '100%' },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const DraftSummaryStep: React.FC<IDraftSummaryStepProps> = ({
  summaryText,
  onSummaryChange,
  recipients,
  onRecipientsChange,
  ccRecipients,
  onCcRecipientsChange,
  onSearchContacts,
  fetchAiSummary,
}) => {
  const styles = useStyles();

  const [summaryStatus, setSummaryStatus] = React.useState<'idle' | 'loading' | 'loaded' | 'error'>('idle');
  const hasFetchedRef = React.useRef(false);

  React.useEffect(() => {
    if (hasFetchedRef.current) return;
    hasFetchedRef.current = true;

    if (summaryText !== '') {
      setSummaryStatus('loaded');
      return;
    }

    if (!fetchAiSummary) {
      setSummaryStatus('loaded');
      return;
    }

    let cancelled = false;
    setSummaryStatus('loading');

    fetchAiSummary()
      .then(result => {
        if (cancelled) return;
        onSummaryChange(result.summary);
        setSummaryStatus('loaded');
      })
      .catch(() => {
        if (cancelled) return;
        setSummaryStatus('error');
      });

    return () => {
      cancelled = true;
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <div className={styles.root}>
      <div className={styles.headerRow}>
        <div className={styles.headerText}>
          <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
            Draft Summary
          </Text>
          <Text size={200} className={styles.stepSubtitle}>
            Review and edit the AI-generated summary below, then add recipient email addresses for distribution.
          </Text>
        </div>
        {summaryStatus === 'loaded' && fetchAiSummary && (
          <Badge className={styles.aiBadge} appearance="tint" color="brand" icon={<SparkleRegular />}>
            AI Generated
          </Badge>
        )}
      </div>

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
          <div className={styles.summaryLoading}>
            <Spinner size="tiny" />
            <Text size={200}>Generating summary&hellip;</Text>
          </div>
        )}

        {summaryStatus === 'error' && (
          <div className={styles.summaryUnavailable}>
            <WarningRegular aria-hidden="true" fontSize={16} />
            <Text size={200}>Summary unavailable. You can type a summary manually below.</Text>
          </div>
        )}

        {(summaryStatus === 'loaded' || summaryStatus === 'error') && (
          <Textarea
            className={styles.summaryTextarea}
            value={summaryText}
            onChange={e => onSummaryChange(e.target.value)}
            placeholder="Enter or edit the summary here&hellip;"
            rows={10}
            resize="vertical"
            aria-label="Summary"
          />
        )}
      </Card>

      <RecipientField
        label="Distribute to"
        placeholder="Search contacts or type email..."
        recipients={recipients}
        onRecipientsChange={onRecipientsChange}
        onSearch={onSearchContacts}
      />

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

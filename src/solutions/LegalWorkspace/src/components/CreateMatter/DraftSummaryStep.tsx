/**
 * DraftSummaryStep.tsx
 * Follow-on step for "Draft Matter Summary" in the Create New Matter wizard.
 *
 * Layout:
 *   ┌──────────────────────────────────────────────────────────────────────┐
 *   │  Draft Matter Summary                             ✦ AI Generated     │
 *   │  Review and edit the AI-generated summary, then add recipients.      │
 *   │                                                                       │
 *   │  ┌─ AI Summary ─────────────────────────────────────────────────┐   │
 *   │  │  ✦  This litigation matter, "Smith v. Jones", has been...    │   │
 *   │  └─────────────────────────────────────────────────────────────┘   │
 *   │                                                                       │
 *   │  Recipient emails (one per line)                                     │
 *   │  [                                               ]                   │
 *   │                                                                       │
 *   │  [+ Add recipient]                                                   │
 *   └──────────────────────────────────────────────────────────────────────┘
 *
 * On mount: calls fetchAiDraftSummary from matterService (stub / BFF).
 * While in-flight: spinner in the summary card area.
 * On error: "Summary unavailable" fallback message.
 *
 * Constraints:
 *   - Fluent v9: Card, Textarea, Input, Button, Spinner, Text
 *   - SparkleRegular for AI indicator
 *   - makeStyles with semantic tokens — ZERO hardcoded colors
 */

import * as React from 'react';
import {
  Card,
  CardHeader,
  Textarea,
  Input,
  Button,
  Spinner,
  Text,
  Badge,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  SparkleRegular,
  AddRegular,
  DismissRegular,
  WarningRegular,
} from '@fluentui/react-icons';
import { fetchAiDraftSummary } from './matterService';
import type { ICreateMatterFormState } from './formTypes';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IDraftSummaryStepProps {
  /** Form values from Step 2 — used to personalise the AI prompt. */
  formValues: ICreateMatterFormState;
  /** Current AI-generated summary text (controlled). */
  summaryText: string;
  /** Called when summary text changes. */
  onSummaryChange: (text: string) => void;
  /** Current recipient email list (controlled). */
  recipientEmails: string[];
  /** Called when recipient list changes. */
  onRecipientsChange: (emails: string[]) => void;
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

  // ── AI summary card ───────────────────────────────────────────────────
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

  // ── Recipient emails section ──────────────────────────────────────────
  recipientSection: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  recipientLabel: {
    color: tokens.colorNeutralForeground1,
  },
  recipientRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  recipientInput: {
    flex: '1 1 auto',
  },
  addRecipientRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  addEmailInput: {
    flex: '1 1 auto',
  },
});

// ---------------------------------------------------------------------------
// DraftSummaryStep (exported)
// ---------------------------------------------------------------------------

export const DraftSummaryStep: React.FC<IDraftSummaryStepProps> = ({
  formValues,
  summaryText,
  onSummaryChange,
  recipientEmails,
  onRecipientsChange,
}) => {
  const styles = useStyles();

  const [summaryStatus, setSummaryStatus] = React.useState<
    'idle' | 'loading' | 'loaded' | 'error'
  >('idle');
  const [newEmailInput, setNewEmailInput] = React.useState('');
  const hasFetchedRef = React.useRef(false);

  // ── Fetch AI summary on mount (once) ────────────────────────────────────
  React.useEffect(() => {
    if (hasFetchedRef.current) return;
    hasFetchedRef.current = true;

    // Only fetch if summary not already set (e.g. from parent state on re-render)
    if (summaryText !== '') {
      setSummaryStatus('loaded');
      return;
    }

    let cancelled = false;
    setSummaryStatus('loading');

    fetchAiDraftSummary(
      formValues.matterName,
      formValues.matterType,
      formValues.practiceArea
    )
      .then((result) => {
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

  // ── Recipient handlers ──────────────────────────────────────────────────
  const handleAddEmail = React.useCallback(() => {
    const email = newEmailInput.trim();
    if (!email) return;
    if (recipientEmails.includes(email)) {
      setNewEmailInput('');
      return;
    }
    onRecipientsChange([...recipientEmails, email]);
    setNewEmailInput('');
  }, [newEmailInput, recipientEmails, onRecipientsChange]);

  const handleRemoveEmail = React.useCallback(
    (email: string) => {
      onRecipientsChange(recipientEmails.filter((e) => e !== email));
    },
    [recipientEmails, onRecipientsChange]
  );

  const handleAddEmailKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        handleAddEmail();
      }
    },
    [handleAddEmail]
  );

  // ── Render ─────────────────────────────────────────────────────────────
  return (
    <div className={styles.root}>
      {/* Header */}
      <div className={styles.headerRow}>
        <div className={styles.headerText}>
          <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
            Draft Matter Summary
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
          <div className={styles.summaryLoading}>
            <Spinner size="tiny" />
            <Text size={200}>Generating summary\u2026</Text>
          </div>
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
            placeholder="Enter or edit the matter summary here\u2026"
            rows={5}
            resize="vertical"
            aria-label="Matter summary"
          />
        )}
      </Card>

      {/* Recipient emails */}
      <div className={styles.recipientSection}>
        <Text size={300} weight="semibold" className={styles.recipientLabel}>
          Distribute to
        </Text>
        <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
          Add email addresses to receive the matter summary.
        </Text>

        {/* Existing recipients */}
        {recipientEmails.map((email) => (
          <div key={email} className={styles.recipientRow}>
            <Input
              className={styles.recipientInput}
              value={email}
              readOnly
              aria-label={`Recipient: ${email}`}
            />
            <Button
              appearance="subtle"
              size="small"
              icon={<DismissRegular fontSize={14} />}
              onClick={() => handleRemoveEmail(email)}
              aria-label={`Remove ${email}`}
            />
          </div>
        ))}

        {/* Add new recipient row */}
        <div className={styles.addRecipientRow}>
          <Input
            className={styles.addEmailInput}
            type="email"
            value={newEmailInput}
            onChange={(e) => setNewEmailInput(e.target.value)}
            onKeyDown={handleAddEmailKeyDown}
            placeholder="Enter email address"
            aria-label="New recipient email"
          />
          <Button
            appearance="secondary"
            size="small"
            icon={<AddRegular fontSize={14} />}
            onClick={handleAddEmail}
            disabled={!newEmailInput.trim()}
            aria-label="Add recipient"
          >
            Add
          </Button>
        </div>
      </div>
    </div>
  );
};

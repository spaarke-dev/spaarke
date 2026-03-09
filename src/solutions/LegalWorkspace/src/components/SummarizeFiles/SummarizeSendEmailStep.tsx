/**
 * SummarizeSendEmailStep.tsx
 * Follow-on step for "Send Email" in the Summarize Files wizard.
 *
 * Adapts the CreateMatter/SendEmailStep pattern:
 *   - To (LookupField → searchUsersAsLookup)
 *   - Subject (Input)
 *   - Body (Textarea, 15 rows, pre-filled with summary)
 *   - "Include only short summary" checkbox at top
 */
import * as React from 'react';
import {
  Checkbox,
  Field,
  Input,
  Textarea,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { LookupField } from '../CreateMatter/LookupField';
import type { ILookupItem } from '../../types/entities';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISummarizeSendEmailStepProps {
  /** Controlled "To" value (email address string). */
  emailTo: string;
  /** Called when "To" changes. */
  onEmailToChange: (value: string) => void;
  /** Controlled subject value. */
  emailSubject: string;
  /** Called when subject changes. */
  onEmailSubjectChange: (value: string) => void;
  /** Controlled body value. */
  emailBody: string;
  /** Called when body changes. */
  onEmailBodyChange: (value: string) => void;
  /** Search function for user lookup. */
  onSearchUsers: (query: string) => Promise<ILookupItem[]>;
  /** Whether to include only the short summary. */
  includeShortSummary: boolean;
  /** Toggle for short summary. */
  onIncludeShortSummaryChange: (checked: boolean) => void;
}

// ---------------------------------------------------------------------------
// Template builders
// ---------------------------------------------------------------------------

export function buildSummaryEmailSubject(): string {
  return 'Document Summary';
}

export function buildSummaryEmailBody(
  summary: string,
  shortSummary: string,
  useShort: boolean,
): string {
  const summaryContent = useShort ? shortSummary : summary;

  return (
    `Dear Colleague,\n\n` +
    `Please find the AI-generated summary of the uploaded documents below.\n\n` +
    `Kind regards,\n` +
    `[Your Name]\n\n` +
    `────────────────────────────────────\n\n` +
    summaryContent
  );
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function extractEmailFromUserName(name: string): string {
  const match = name.match(/\(([^)]+@[^)]+)\)/);
  return match ? match[1] : '';
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

  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },

  labelRow: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },
  requiredMark: {
    color: tokens.colorPaletteRedForeground1,
  },

  infoNote: {
    color: tokens.colorNeutralForeground3,
    paddingTop: tokens.spacingVerticalXS,
  },
});

// ---------------------------------------------------------------------------
// SummarizeSendEmailStep (exported)
// ---------------------------------------------------------------------------

export const SummarizeSendEmailStep: React.FC<ISummarizeSendEmailStepProps> = ({
  emailTo,
  onEmailToChange,
  emailSubject,
  onEmailSubjectChange,
  emailBody,
  onEmailBodyChange,
  onSearchUsers,
  includeShortSummary,
  onIncludeShortSummaryChange,
}) => {
  const styles = useStyles();

  const [selectedUser, setSelectedUser] = React.useState<ILookupItem | null>(null);

  const handleUserSelect = React.useCallback(
    (item: ILookupItem | null) => {
      setSelectedUser(item);
      if (item) {
        const email = extractEmailFromUserName(item.name);
        onEmailToChange(email || item.name);
      } else {
        onEmailToChange('');
      }
    },
    [onEmailToChange]
  );

  const renderLabel = (text: string, required?: boolean): React.ReactElement => (
    <span className={styles.labelRow}>
      {text}
      {required && (
        <span aria-hidden="true" className={styles.requiredMark}>
          {' *'}
        </span>
      )}
    </span>
  );

  return (
    <div className={styles.root}>
      {/* Header */}
      <div className={styles.headerText}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Send Email
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Compose an email with the file summary. It will be sent via the system.
        </Text>
      </div>

      {/* Short summary toggle */}
      <Checkbox
        checked={includeShortSummary}
        onChange={(_e, data) => onIncludeShortSummaryChange(!!data.checked)}
        label="Include only short summary"
      />

      {/* Email form */}
      <div className={styles.form}>
        {/* To — user lookup */}
        <LookupField
          label="To"
          required
          placeholder="Search users..."
          value={selectedUser}
          onChange={handleUserSelect}
          onSearch={onSearchUsers}
          minSearchLength={2}
        />

        {/* Subject */}
        <Field
          label={renderLabel('Subject', true)}
          required
        >
          <Input
            value={emailSubject}
            onChange={(e) => onEmailSubjectChange(e.target.value)}
            placeholder="Email subject"
            aria-label="Subject"
          />
        </Field>

        {/* Body */}
        <Field
          label={renderLabel('Message', true)}
          required
        >
          <Textarea
            value={emailBody}
            onChange={(e) => onEmailBodyChange(e.target.value)}
            placeholder="Compose your message&hellip;"
            rows={15}
            resize="vertical"
            aria-label="Message body"
          />
        </Field>

        {/* Info note */}
        <Text size={100} className={styles.infoNote}>
          This email will be sent via the BFF communication endpoint.
        </Text>
      </div>
    </div>
  );
};

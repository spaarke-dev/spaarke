/**
 * SendEmailStep.tsx
 * Follow-on step for composing an email to client.
 *
 * Moved from LegalWorkspace's CreateMatter to the shared library.
 * Entity-specific form values are no longer referenced — email pre-fill
 * is handled by the CreateRecordWizard via config callbacks.
 *
 * @see CreateRecordWizard — pre-fills emailSubject/emailBody from config
 */
import * as React from 'react';
import { Field, Input, Textarea, Text, makeStyles, tokens } from '@fluentui/react-components';
import { LookupField } from '../../LookupField/LookupField';
import type { ILookupItem } from '../../../types/LookupTypes';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISendEmailStepProps {
  /** Optional step title override (default: "Send Email to Client"). */
  title?: string;
  /** Controlled "To" value (email address string). */
  emailTo: string;
  /** Called when "To" changes. */
  onEmailToChange: (value: string) => void;
  /** Controlled "CC" value (email address string). */
  emailCc?: string;
  /** Called when "CC" changes. */
  onEmailCcChange?: (value: string) => void;
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
  stepTitle: { color: tokens.colorNeutralForeground1 },
  stepSubtitle: { color: tokens.colorNeutralForeground3 },
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
  requiredMark: { color: tokens.colorPaletteRedForeground1 },
  infoNote: {
    color: tokens.colorNeutralForeground3,
    paddingTop: tokens.spacingVerticalXS,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SendEmailStep: React.FC<ISendEmailStepProps> = ({
  title = 'Send Email to Client',
  emailTo: _emailTo,
  onEmailToChange,
  emailCc,
  onEmailCcChange,
  emailSubject,
  onEmailSubjectChange,
  emailBody,
  onEmailBodyChange,
  onSearchUsers,
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
      <div className={styles.headerText}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          {title}
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Compose an introductory email. It will be created as an email activity in Dataverse, linked to this record.
        </Text>
      </div>

      <div className={styles.form}>
        <LookupField
          label="To"
          required
          placeholder="Search users..."
          value={selectedUser}
          onChange={handleUserSelect}
          onSearch={onSearchUsers}
          minSearchLength={2}
        />

        {onEmailCcChange && (
          <Field label="CC">
            <Input
              value={emailCc ?? ''}
              onChange={e => onEmailCcChange(e.target.value)}
              placeholder="CC email addresses (separate with ;)"
              aria-label="CC"
            />
          </Field>
        )}

        <Field label={renderLabel('Subject', true)} required>
          <Input
            value={emailSubject}
            onChange={e => onEmailSubjectChange(e.target.value)}
            placeholder="Email subject"
            aria-label="Subject"
          />
        </Field>

        <Field label={renderLabel('Message', true)} required>
          <Textarea
            value={emailBody}
            onChange={e => onEmailBodyChange(e.target.value)}
            placeholder="Compose your message&hellip;"
            rows={15}
            resize="vertical"
            aria-label="Message body"
          />
        </Field>

        <Text size={100} className={styles.infoNote}>
          This email will be saved as a draft activity on the record. You can review and send it from there.
        </Text>
      </div>
    </div>
  );
};

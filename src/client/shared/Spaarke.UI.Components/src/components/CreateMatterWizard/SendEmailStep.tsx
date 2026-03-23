/**
 * SendEmailStep.tsx
 * Follow-on step for "Send Notification Email" in the Create New Matter wizard.
 *
 * "To" field uses LookupField searching the systemuser table.
 * Subject is pre-filled: "New Matter: {matterName}"
 * Body uses a default template including matter type + practice area.
 *
 * Constraints:
 *   - Fluent v9: Input, Textarea, Field, Text
 *   - makeStyles with semantic tokens -- ZERO hardcoded colors
 */

import * as React from 'react';
import {
  Field,
  Input,
  Textarea,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { LookupField } from './LookupField';
import type { ICreateMatterFormState } from './formTypes';
import type { ILookupItem } from '../../types/LookupTypes';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISendEmailStepProps {
  /** Form values from Step 2 -- used to pre-fill subject/body. */
  formValues: ICreateMatterFormState;
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
}

// ---------------------------------------------------------------------------
// Template builder
// ---------------------------------------------------------------------------

export function buildDefaultEmailSubject(matterName: string): string {
  return `New Matter: ${matterName}`;
}

export function buildDefaultEmailBody(form: ICreateMatterFormState): string {
  const typeStr = form.matterTypeName ? ` ${form.matterTypeName.toLowerCase()}` : '';
  const areaStr = form.practiceAreaName ? ` (${form.practiceAreaName})` : '';

  return (
    `Dear Client,\n\n` +
    `We are pleased to confirm that your${typeStr} matter, "${form.matterName}"${areaStr}, ` +
    `has been created in our legal management system.\n\n` +
    `Our team will be in touch shortly to discuss next steps and any actions required from you.\n\n` +
    `Please do not hesitate to reach out if you have any questions.\n\n` +
    `Kind regards,\n` +
    `[Your Name]\n` +
    `[Firm Name]`
  );
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Extract email from a lookup item name like "John Doe (john@example.com)".
 */
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

  // -- Form --
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
// SendEmailStep (exported)
// ---------------------------------------------------------------------------

export const SendEmailStep: React.FC<ISendEmailStepProps> = ({
  emailTo,
  onEmailToChange,
  emailSubject,
  onEmailSubjectChange,
  emailBody,
  onEmailBodyChange,
  onSearchUsers,
}) => {
  const styles = useStyles();

  // Track the selected user lookup item for the LookupField display
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
          Send Notification Email
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Compose an introductory email. It will be created as an email activity
          in Dataverse, linked to this matter.
        </Text>
      </div>

      {/* Email form */}
      <div className={styles.form}>
        {/* To -- user lookup */}
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
          This email will be saved as a draft activity on the matter record. You
          can review and send it from there.
        </Text>
      </div>
    </div>
  );
};

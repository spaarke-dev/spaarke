/**
 * SendEmailStep.tsx
 * Follow-on step for "Send Email to Client" in the Create New Matter wizard.
 *
 * Layout:
 *   ┌──────────────────────────────────────────────────────────────────────┐
 *   │  Send Email to Client                                                 │
 *   │  Compose an introductory email. It will be created as a Dataverse   │
 *   │  email activity linked to this matter.                               │
 *   │                                                                       │
 *   │  To *                                                                │
 *   │  [client@example.com                                     ]           │
 *   │                                                                       │
 *   │  Subject *                                                           │
 *   │  [New Matter: Smith v. Jones                              ]           │
 *   │                                                                       │
 *   │  Message *                                                           │
 *   │  [Dear Client,                                            ]           │
 *   │  [We are pleased to confirm that your matter...           ]           │
 *   └──────────────────────────────────────────────────────────────────────┘
 *
 * Subject is pre-filled: "New Matter: {matterName}"
 * Body uses a default template including matter type + practice area.
 * All fields are editable.
 *
 * Constraints:
 *   - Fluent v9: Input, Textarea, Field, Text
 *   - makeStyles with semantic tokens — ZERO hardcoded colors
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
import type { ICreateMatterFormState } from './formTypes';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISendEmailStepProps {
  /** Form values from Step 2 — used to pre-fill subject/body. */
  formValues: ICreateMatterFormState;
  /** Controlled "To" value. */
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

  // ── Form ─────────────────────────────────────────────────────────────────
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
}) => {
  const styles = useStyles();

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
          Send Email to Client
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Compose an introductory email. It will be created as an email activity
          in Dataverse, linked to this matter.
        </Text>
      </div>

      {/* Email form */}
      <div className={styles.form}>
        {/* To */}
        <Field
          label={renderLabel('To', true)}
          required
        >
          <Input
            type="email"
            value={emailTo}
            onChange={(e) => onEmailToChange(e.target.value)}
            placeholder="client@example.com"
            aria-label="To"
          />
        </Field>

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
            placeholder="Compose your message\u2026"
            rows={10}
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

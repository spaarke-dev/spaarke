/**
 * SendEmailStep.tsx
 * Generic email composition step for use in any wizard or multi-step form.
 *
 * Accepts configurable title, subtitle, default subject/body, and an optional
 * regarding entity reference. All domain-specific values are passed via props
 * rather than hardcoded.
 *
 * Layout:
 *   +----------------------------------------------------------------------+
 *   |  {title}                                                              |
 *   |  {subtitle}                                                           |
 *   |                                                                       |
 *   |  {headerContent}  (optional slot for extra controls above the form)   |
 *   |                                                                       |
 *   |  To *      [Search users...                             ]             |
 *   |                                                                       |
 *   |  Subject * [Pre-filled subject                          ]             |
 *   |                                                                       |
 *   |  Message * [Pre-filled body                             ]             |
 *   |                                                                       |
 *   |  {infoNote}                                                           |
 *   +----------------------------------------------------------------------+
 *
 * Constraints:
 *   - Fluent v9: Input, Textarea, Field, Text
 *   - makeStyles with semantic tokens -- ZERO hardcoded colors
 */

import * as React from "react";
import {
  Field,
  Input,
  Textarea,
  Text,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import { LookupField } from "./LookupField";
import { extractEmailFromUserName } from "./emailHelpers";
import type { ILookupItem } from "./LookupField";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISendEmailStepProps {
  /** Title displayed at the top of the step. */
  title: string;
  /** Subtitle / description displayed below the title. */
  subtitle: string;

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

  /** Search function for user lookup (searches systemuser table). */
  onSearchUsers: (query: string) => Promise<ILookupItem[]>;

  /**
   * Logical name of the Dataverse entity this email relates to
   * (e.g. "sprk_matter", "sprk_document"). Stored for reference by the caller.
   */
  regardingEntityType?: string;
  /**
   * GUID of the regarding record. Stored for reference by the caller.
   */
  regardingId?: string;

  /**
   * Optional content rendered between the header and the email form.
   * Useful for domain-specific controls like a "short summary" checkbox.
   */
  headerContent?: React.ReactNode;

  /**
   * Info note displayed below the message field.
   * Defaults to: "This email will be saved as a draft activity."
   */
  infoNote?: string;

  /** Number of rows for the message textarea. Default: 15. */
  messageRows?: number;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalL,
  },

  headerText: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
  },
  stepTitle: {
    color: tokens.colorNeutralForeground1,
  },
  stepSubtitle: {
    color: tokens.colorNeutralForeground3,
  },

  form: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
  },

  labelRow: {
    display: "inline-flex",
    alignItems: "center",
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
  title,
  subtitle,
  emailTo,
  onEmailToChange,
  emailSubject,
  onEmailSubjectChange,
  emailBody,
  onEmailBodyChange,
  onSearchUsers,
  headerContent,
  infoNote = "This email will be saved as a draft activity.",
  messageRows = 15,
}) => {
  const styles = useStyles();

  // Track the selected user lookup item for the LookupField display
  const [selectedUser, setSelectedUser] = React.useState<ILookupItem | null>(
    null,
  );

  const handleUserSelect = React.useCallback(
    (item: ILookupItem | null) => {
      setSelectedUser(item);
      if (item) {
        const email = extractEmailFromUserName(item.name);
        onEmailToChange(email || item.name);
      } else {
        onEmailToChange("");
      }
    },
    [onEmailToChange],
  );

  const renderLabel = (
    text: string,
    required?: boolean,
  ): React.ReactElement => (
    <span className={styles.labelRow}>
      {text}
      {required && (
        <span aria-hidden="true" className={styles.requiredMark}>
          {" *"}
        </span>
      )}
    </span>
  );

  return (
    <div className={styles.root}>
      {/* Header */}
      <div className={styles.headerText}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          {title}
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          {subtitle}
        </Text>
      </div>

      {/* Optional header content (e.g. checkboxes, toggles) */}
      {headerContent}

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
        <Field label={renderLabel("Subject", true)} required>
          <Input
            value={emailSubject}
            onChange={(e) => onEmailSubjectChange(e.target.value)}
            placeholder="Email subject"
            aria-label="Subject"
          />
        </Field>

        {/* Body */}
        <Field label={renderLabel("Message", true)} required>
          <Textarea
            value={emailBody}
            onChange={(e) => onEmailBodyChange(e.target.value)}
            placeholder="Compose your message&hellip;"
            rows={messageRows}
            resize="vertical"
            aria-label="Message body"
          />
        </Field>

        {/* Info note */}
        <Text size={100} className={styles.infoNote}>
          {infoNote}
        </Text>
      </div>
    </div>
  );
};

SendEmailStep.displayName = "SendEmailStep";

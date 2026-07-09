/**
 * SendEmailFollowOnStep.tsx
 * Follow-on step for composing a notification email, shared across all Spaarke
 * create wizards.
 *
 * Thin wrapper over the already-shared, generic `EmailStep` (`components/
 * EmailStep/SendEmailStep`) — it does NOT re-implement the email form. It only
 * supplies follow-on-appropriate defaults (title / subtitle / info note) and
 * composes two optional affordances that the divergent wizard card sets need:
 *
 *   - `headerContent` — extra controls above the form. SummarizeFiles uses this
 *     for its "include only short summary" toggle (matching the current
 *     SummarizeSendEmailStep placement).
 *   - CC field — CreateWorkAssignment's Send Email offers a CC. When
 *     `onEmailCcChange` is supplied, a CC input is rendered in the header slot
 *     (the generic EmailStep has no CC of its own, and this wrapper does not
 *     modify it). Absent → no CC field, identical to the new wizards' Send
 *     Email.
 *
 * All email state is controlled by the consuming wizard. The actual email
 * activity is created at finish time by the wizard's service (e.g.
 * `EntityCreationService.sendEmail`) — this step only collects.
 *
 * @see components/EmailStep/SendEmailStep — the wrapped generic step
 * @see followOnTypes.ts — FollowOnCardConfig.renderStep supplies this component
 */
import * as React from 'react';
import { Field, Input, makeStyles, tokens } from '@fluentui/react-components';
import { SendEmailStep as EmailStep } from '../../EmailStep';
import type { ILookupItem } from '../../../types/LookupTypes';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISendEmailFollowOnStepProps {
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
  /** Search function for the user lookup (searches systemuser). */
  onSearchUsers: (query: string) => Promise<ILookupItem[]>;

  /** Optional CC value. When `onEmailCcChange` is supplied a CC input renders. */
  emailCc?: string;
  /** Called when CC changes. Presence enables the CC field. */
  onEmailCcChange?: (value: string) => void;

  /** Step title. Defaults to "Send Notification Email". */
  title?: string;
  /** Step subtitle. A follow-on-appropriate default is used when omitted. */
  subtitle?: string;
  /** Info note under the message field. A default is used when omitted. */
  infoNote?: string;
  /** Number of rows for the message textarea. Default: 15. */
  messageRows?: number;
  /**
   * Extra content rendered above the email form (composed with the optional CC
   * field). Use for domain-specific controls like a "short summary" toggle.
   */
  headerContent?: React.ReactNode;
  /** Logical name of the entity this email relates to (reference only). */
  regardingEntityType?: string;
  /** GUID of the regarding record (reference only). */
  regardingId?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  headerSlot: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

const DEFAULT_SUBTITLE =
  'Compose an introductory email. It will be created as an email activity in Dataverse, linked to this record.';
const DEFAULT_INFO_NOTE = 'This email will be saved as a draft activity on the record.';

export const SendEmailFollowOnStep: React.FC<ISendEmailFollowOnStepProps> = ({
  emailTo,
  onEmailToChange,
  emailSubject,
  onEmailSubjectChange,
  emailBody,
  onEmailBodyChange,
  onSearchUsers,
  emailCc,
  onEmailCcChange,
  title = 'Send Notification Email',
  subtitle = DEFAULT_SUBTITLE,
  infoNote = DEFAULT_INFO_NOTE,
  messageRows = 15,
  headerContent,
  regardingEntityType,
  regardingId,
}) => {
  const styles = useStyles();

  // Compose the optional CC field with any consumer-provided headerContent so
  // the generic EmailStep (which has no CC) stays unmodified.
  const composedHeader =
    headerContent || onEmailCcChange ? (
      <div className={styles.headerSlot}>
        {headerContent}
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
      </div>
    ) : undefined;

  return (
    <EmailStep
      title={title}
      subtitle={subtitle}
      emailTo={emailTo}
      onEmailToChange={onEmailToChange}
      emailSubject={emailSubject}
      onEmailSubjectChange={onEmailSubjectChange}
      emailBody={emailBody}
      onEmailBodyChange={onEmailBodyChange}
      onSearchUsers={onSearchUsers}
      infoNote={infoNote}
      messageRows={messageRows}
      headerContent={composedHeader}
      regardingEntityType={regardingEntityType}
      regardingId={regardingId}
    />
  );
};

SendEmailFollowOnStep.displayName = 'SendEmailFollowOnStep';

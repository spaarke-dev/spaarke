/**
 * INTERACTIVE EMAIL SLOT — the email branch of the channel-aware shell.
 *
 * This is the shell seam for the interactive email surface. It is reached ONLY
 * via the type-driven layout switch (`renderByCommunicationType`) for
 * `CommunicationType.Email`, plus `compose` mode (which is email at launch).
 *
 * ─────────────────────────────────────────────────────────────────────────────
 * ⛳ TASK 041 EXTENSION SEAM — mount `<EmailComposer />` / `<SendEmailPage />`
 * ─────────────────────────────────────────────────────────────────────────────
 * Task 040 lands the SHELL ONLY. Task 041 replaces the placeholder below with
 * the canonical composer from `@spaarke/ui-components` (reference §5 / §7.6):
 *
 *   import { SendEmailPage } from '@spaarke/ui-components';
 *   ...
 *   return (
 *     <SendEmailPage
 *       mode={props.mode}
 *       communicationId={props.record?.sprk_communicationid}
 *       authenticatedFetch={props.authenticatedFetch}   // ADR-028 function-based
 *       bffBaseUrl={props.bffBaseUrl}                    // host only, NO /api
 *       initialTo={props.initialTo}
 *       initialCc={props.initialCc}
 *       initialSubject={props.initialSubject}
 *       initialBody={props.initialBody}
 *       associations={props.associations}
 *       onSent={(id) => props.nav.onSent(id)}
 *       onClose={props.nav.onClose}
 *     />
 *   );
 *
 * DO NOT pass an `accessToken` prop — only `authenticatedFetch` crosses this
 * boundary (ADR-028). Keep the prop shape below as the composer's input contract.
 */

import * as React from 'react';
import { makeStyles, tokens, Title2, Body1, Caption1, Badge } from '@fluentui/react-components';
import type { AuthenticatedFetchFn } from '@spaarke/auth';
import type {
  CommunicationMode,
  ICommunicationAssociation,
  ICommunicationNavCallbacks,
  ICommunicationRecord,
} from '../types/communication';

export interface IEmailComposerSlotProps {
  mode: CommunicationMode;
  /** Present for view/reply/forward/draft; null for compose. */
  record: ICommunicationRecord | null;
  initialTo: string[];
  initialCc: string[];
  initialSubject?: string;
  initialBody?: string;
  associations: ICommunicationAssociation[];
  /** ADR-028 function-based transport — the ONLY auth object handed down. */
  authenticatedFetch: AuthenticatedFetchFn;
  /** BFF base URL (host only, NO /api). */
  bffBaseUrl: string;
  nav: ICommunicationNavCallbacks;
}

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    padding: tokens.spacingHorizontalXXL,
    height: '100%',
    overflowY: 'auto',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  seam: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalL,
    borderRadius: tokens.borderRadiusLarge,
    border: `${tokens.strokeWidthThin} dashed ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  meta: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: tokens.spacingHorizontalS,
  },
});

export function EmailComposerSlot(props: IEmailComposerSlotProps): React.JSX.Element {
  const styles = useStyles();

  return (
    <div className={styles.root}>
      <div>
        <Title2>Email</Title2>
        <Caption1>
          Interactive email surface · mode: <strong>{props.mode}</strong>
        </Caption1>
      </div>

      {/*
        ⛳ TASK 042 EXTENSION SEAM — RegardingResolver review surface.
        Task 042 embeds `RegardingResolver` here (reads sprk_associationprovenance
        + the regarding lookups from task 001) so the user can review/adjust the
        resolved associations before send. It does NOT introduce a new regarding
        mechanism — it renders the existing RegardingResolver over this record.
        Until 042 lands, the parsed associations are shown read-only below.
      */}

      <section className={styles.seam} aria-label="Email composer placeholder">
        <Body1>
          <strong>Shell placeholder (task 040).</strong> The canonical
          <code> &lt;SendEmailPage /&gt; </code> composer mounts here in task 041.
        </Body1>
        <Caption1>Pre-fill / record context wired and ready to hand to the composer:</Caption1>
        <div className={styles.meta}>
          {props.record && <Badge appearance="tint">record: {props.record.sprk_communicationid.slice(0, 8)}…</Badge>}
          {props.initialTo.length > 0 && <Badge appearance="tint">to: {props.initialTo.length}</Badge>}
          {props.initialCc.length > 0 && <Badge appearance="tint">cc: {props.initialCc.length}</Badge>}
          {props.initialSubject && <Badge appearance="tint">subject ✓</Badge>}
          {props.associations.map(a => (
            <Badge key={`${a.entityType}:${a.entityId}`} appearance="outline">
              {a.entityType}: {a.entityId.slice(0, 8)}…
            </Badge>
          ))}
        </div>
      </section>
    </div>
  );
}

/**
 * INTERACTIVE EMAIL SLOT — the email branch of the channel-aware shell.
 *
 * Reached ONLY via the type-driven layout switch (`renderByCommunicationType`)
 * for `CommunicationType.Email`, plus `compose` mode (which is email at launch).
 * Non-email `sprk_communicationtype` channels never route here — they render
 * read-only via `ReadOnlyCommunicationView` (task 040). The composer is the
 * channel-specific renderer mounted ONLY for Email.
 *
 * ─────────────────────────────────────────────────────────────────────────────
 * TASK 041 — mounts the canonical `<SendEmailPage />` wrapper (task 021,
 * `@spaarke/ui-components`). This is the ONLY email-composer surface; the engine
 * is CONSUMED from the shared lib, never forked or reimplemented (ADR-012).
 * ─────────────────────────────────────────────────────────────────────────────
 *
 * URL contract → wrapper props (reference §7.5 / §7.6):
 *   - `mode`            ← parsed `?mode=compose|view|reply|forward|draft`
 *   - `communicationId` ← the loaded `sprk_communication` record's id
 *                         (present for view/reply/forward/draft; undefined for compose)
 *   - `initialTo` / `initialSubject` / `initialBody` ← URL pre-fill (or record fallback,
 *                         resolved upstream in `CommunicationLayout`)
 *   - `associations`    ← parsed `?associatedTo=<entityType>:<guid>` pairs
 *   - `authenticatedFetch` + `bffBaseUrl` ← 040 auth bootstrap (ADR-028 —
 *                         function-based transport + host base URL only, NO /api,
 *                         NO accessToken prop)
 *   - `onSent`  → `nav.onSent`  (Xrm.Navigation.openForm on the created/updated record)
 *   - `onClose` → `nav.onClose` (leave the page)
 *
 * The wrapper locks `mount='page'` (first-class entity-form chrome, ADR-021 /
 * reference §5.10) and renders its own per-mode header + linked-record chips —
 * the slot adds no duplicate chrome.
 */

import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import { SendEmailPage } from '@spaarke/ui-components';
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
  // Fill the iframe height and own the scroll; the `page`-mount composer
  // supplies its own centered max-width layout + padding (reference §5.10).
  root: {
    height: '100%',
    overflowY: 'auto',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
});

export function EmailComposerSlot(props: IEmailComposerSlotProps): React.JSX.Element {
  const styles = useStyles();

  return (
    <div className={styles.root}>
      {/*
        ⛳ TASK 042 EXTENSION SEAM — RegardingResolver review surface.
        Task 042 embeds `RegardingResolver` here (reads sprk_associationprovenance
        + the regarding lookups from task 001) so the user can review/adjust the
        resolved associations before send. It does NOT introduce a new regarding
        mechanism — it renders the existing RegardingResolver over this record.
        The composer already shows the parsed associations read-only (via its
        AssociationChips); 042 makes them reviewable/adjustable.
      */}

      <SendEmailPage
        mode={props.mode}
        communicationId={props.record?.sprk_communicationid}
        authenticatedFetch={props.authenticatedFetch}
        bffBaseUrl={props.bffBaseUrl}
        initialTo={props.initialTo}
        initialSubject={props.initialSubject}
        initialBody={props.initialBody}
        associations={props.associations}
        onSent={communicationId => props.nav.onSent(communicationId)}
        onClose={props.nav.onClose}
      />
    </div>
  );
}

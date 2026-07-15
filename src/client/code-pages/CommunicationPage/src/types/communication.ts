/**
 * Types for the channel-aware Communication Code Page.
 *
 * The page is generalized by `sprk_communicationtype` (FR-16 / NFR-04): the
 * layout is chosen by the communication *channel*, never by the string literal
 * "email". Email is the only interactive channel at launch; Teams / SMS /
 * Notification render read-only through the same shell. Adding a future channel
 * = a new read-only renderer entry, with NO change to this contract.
 */

/**
 * `sprk_communicationtype` option-set VALUES (source of truth for the layout
 * switch — see `renderByCommunicationType`).
 *
 * Verified against `docs/data-model/sprk_communication.md` (2026-07-15):
 *   100000000 Email · 100000001 Teams Message · 100000002 SMS · 100000003 Notification
 *
 * The layout switch MUST key off these numeric values — NOT `typename` strings
 * and NOT the literal "email".
 */
export enum CommunicationType {
  Email = 100000000,
  TeamsMessage = 100000001,
  Sms = 100000002,
  Notification = 100000003,
}

/** URL `mode=` values (reference §7.3). */
export type CommunicationMode = 'compose' | 'view' | 'reply' | 'forward' | 'draft';

export const COMMUNICATION_MODES: readonly CommunicationMode[] = [
  'compose',
  'view',
  'reply',
  'forward',
  'draft',
] as const;

/** A parsed `associatedTo=<entityType>:<guid>` pair — stamped onto the record. */
export interface ICommunicationAssociation {
  /** Dataverse entity logical name (e.g. `sprk_matter`). */
  entityType: string;
  /** Target record GUID. */
  entityId: string;
}

/** Optional auth-env overrides carried on the `data=` string (reference §7.3). */
export interface ICommunicationAuthParams {
  bffBaseUrl?: string;
  tenantId?: string;
  /** BFF OAuth scope (`api://<app-id>/user_impersonation`). */
  scope?: string;
  /** MSAL client ID. */
  clientId?: string;
}

/** Fully-parsed `data=` URL contract for the Communication Code Page. */
export interface ICommunicationPageParams {
  mode: CommunicationMode;
  /** `sprk_communication` GUID — required for view/reply/forward/draft. */
  id?: string;
  to: string[];
  cc: string[];
  subject?: string;
  body?: string;
  associations: ICommunicationAssociation[];
  auth: ICommunicationAuthParams;
  /** Validation problems found while parsing (e.g. missing `id` for `view`). */
  warnings: string[];
}

/**
 * Subset of a `sprk_communication` record read via `Xrm.WebApi`. Column logical
 * names verified against `docs/data-model/sprk_communication.md`. Includes the
 * task-001 reply-thread columns (`sprk_internetmessageid`, `sprk_inreplyto`).
 */
export interface ICommunicationRecord {
  sprk_communicationid: string;
  /** Channel — drives the layout switch. */
  sprk_communicationtype?: number | null;
  sprk_communicationtypename?: string | null;
  sprk_direction?: number | null;
  sprk_directionname?: string | null;
  sprk_subject?: string | null;
  sprk_from?: string | null;
  sprk_to?: string | null;
  sprk_cc?: string | null;
  sprk_bcc?: string | null;
  sprk_body?: string | null;
  sprk_bodyformat?: number | null;
  sprk_bodyformatname?: string | null;
  statuscode?: number | null;
  statecode?: number | null;
  sprk_sentat?: string | null;
  sprk_receiveddate?: string | null;
  sprk_associationstatus?: number | null;
  // task-001 reply-thread columns
  sprk_internetmessageid?: string | null;
  sprk_inreplyto?: string | null;
}

/**
 * Navigation callbacks the shell passes down to whichever renderer it mounts.
 * Task 041 (`<EmailComposer />`) consumes these; the read-only renderers use
 * only `onClose`. These are plain function props — NO token/`accessToken`
 * props ever cross this boundary (ADR-028).
 */
export interface ICommunicationNavCallbacks {
  /** Fired on successful send — navigate to the created/updated record. */
  onSent: (communicationId: string) => void;
  /** Fired on cancel/close — leave the page. */
  onClose: () => void;
}

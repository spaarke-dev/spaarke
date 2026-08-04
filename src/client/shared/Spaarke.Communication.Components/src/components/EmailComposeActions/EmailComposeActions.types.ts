/**
 * EmailComposeActions.types.ts
 *
 * Dependency-injection contract for `useEmailComposeActions` (email-
 * communication-solution-r5 task 036, FR-09/FR-10/FR-15). This module wires
 * the task-032 reading-pane toolbar's `EmailToolbarActionHandlers` dispatch
 * seam to the CANONICAL `EmailComposer`/`SendEmailDialog` — it does NOT fork
 * a new composer, does NOT add a new BFF endpoint, and does NOT import Xrm
 * directly (ADR-012 context-agnostic): the host (task 040 assembly / the
 * Email code page) injects `dataService` / `navigationService` via
 * `createXrmDataService()` / `createXrmNavigationService()`
 * (`@spaarke/ui-components`), mirroring `sprk_communicationconversationpage`'s
 * `App.tsx` pattern.
 */
import type * as React from 'react';
import type {
  AuthenticatedFetchFn,
  IDataService,
  INavigationService,
  ILookupItem,
  ICommunicationAssociation,
  SendCommunicationError,
  ISendEmailDialogProps,
  IRecordLookupTarget,
  IPickedRecord,
  IRecipient,
  IEmailTemplateSummary,
  IEmailTemplateRenderResult,
  IEmailAiDraftAction,
  IEmailAiDraftRequest,
  IEmailAiDraftResult,
} from '@spaarke/ui-components';
import type { EmailToolbarActionHandlers } from '../EmailReadingPaneShell';
import type { ComposerFields } from '../../logic/actions';

/**
 * `ISendEmailDialogProps` (the wrapper's DECLARED type) omits `initialCc` —
 * the wrapper forwards any extra props straight through to the underlying
 * `<EmailComposer/>` engine via `...composerProps`, and the engine's own
 * `IEmailComposerProps` DOES accept `initialCc`. This intersection reflects
 * the TRUE forwarded prop surface without editing `SendEmailDialog.tsx`
 * (`@spaarke/ui-components` source is out of this task's edit scope) — it
 * only widens the TYPE this package uses for the element it builds.
 */
export type SendEmailDialogElementProps = ISendEmailDialogProps & Pick<ComposerFields, 'initialCc'>;

/**
 * Injected dependencies. All context-agnostic (ADR-012) — the host resolves
 * the concrete Xrm/BFF bridge and hands typed services/callbacks in.
 */
export interface EmailComposeActionsDeps {
  /** Auth-aware fetch (ADR-028) — forwarded verbatim to the canonical composer's EXISTING send path. */
  authenticatedFetch: AuthenticatedFetchFn;
  /** BFF host base URL (no `/api` suffix) — forwarded verbatim to the composer. */
  bffBaseUrl: string;
  /**
   * Reads the `sprk_communication` fields (`sprk_from`/`sprk_to`/`sprk_cc`/
   * `sprk_subject`/`sprk_body`/`createdon`) needed to derive Reply/Reply
   * All/Forward pre-fill via the task-022 `deriveComposerFields`. The host
   * supplies `createXrmDataService()` — this module never touches
   * `Xrm.WebApi` itself.
   */
  dataService: IDataService;
  /**
   * Resolves "Open full form" (FR-15) as an 85% `navigateTo` modal
   * (`openRecordModal`) per `docs/standards/MODAL-DECISION-CRITERIA.md`. The
   * host supplies `createXrmNavigationService()`.
   */
  navigationService: INavigationService;
  /** Recipient directory typeahead — forwarded to the composer's `RecipientField`. */
  onSearchRecipients?: (query: string) => Promise<ILookupItem[]>;
  /**
   * Advanced OOB recipient lookup (To/Cc/Bcc label click → people picker).
   * Forwarded to the composer for ALL modes. Optional — the host mount supplies
   * the Xrm-backed handler (`createXrmEmailComposeHandlers`); omitted → the
   * recipient fields stay free-text/typeahead only.
   */
  onLookupRecipients?: (field: 'to' | 'cc' | 'bcc') => Promise<IRecipient[] | null>;
  /**
   * Record-lookup catalog + host lookup for the body-toolbar "link a document" /
   * "insert a link to a record" tools. Forwarded for ALL modes. Optional
   * (host-supplied Xrm handlers); omitted → those toolbar tools are hidden.
   */
  recordLookupCatalog?: IRecordLookupTarget[];
  onLookupRecord?: (entityType: string) => Promise<IPickedRecord | null>;
  /**
   * Add-a-relationship (connector toolbar icon). Forwarded for ALL modes.
   * Optional (host-supplied Xrm handler); omitted → the connector tool is hidden.
   */
  onAddRelationship?: () => Promise<IPickedRecord | null>;
  /**
   * New-file upload (item 9b): resolve a locally-picked file to a governed
   * `sprk_document` so it becomes send-eligible. Forwarded verbatim to the
   * composer. Optional (host-supplied Xrm handler via `createXrmEmailComposeHandlers`);
   * omitted → local picks stay display-only.
   */
  onUploadLocalAttachment?: (file: File) => Promise<{ documentId: string; driveItemId?: string; linkUrl?: string }>;
  /**
   * Resolve a recipient-openable SPE sharing link for a linked document (R2 item 12). Forwarded to
   * the composer; omitted → links keep their original internal URL.
   */
  onResolveShareLink?: (documentId: string) => Promise<string | null>;
  /**
   * Compose template picker (Wave E). `onListEmailTemplates` lists selectable OOB `template`
   * records; `onRenderEmailTemplate` renders the chosen one (merging field codes from the
   * primary regarding). Forwarded verbatim to the composer for ALL modes. Optional
   * (host-supplied Xrm/BFF handlers via `createXrmEmailComposeHandlers`); omit either → the
   * toolbar template button is hidden.
   */
  onListEmailTemplates?: () => Promise<IEmailTemplateSummary[]>;
  onRenderEmailTemplate?: (args: {
    templateId: string;
    regardingEntityType?: string;
    regardingRecordId?: string;
  }) => Promise<IEmailTemplateRenderResult>;
  /**
   * AI "sparkle" draft (Wave E). `onDraftWithAi` generates/refines the body via the BFF; optional
   * `aiDraftActions` overrides the preset quick-actions. Forwarded to the composer for ALL modes.
   * Optional (host-supplied Xrm/BFF handler); omit `onDraftWithAi` → the sparkle button is hidden.
   */
  onDraftWithAi?: (req: IEmailAiDraftRequest) => Promise<IEmailAiDraftResult>;
  aiDraftActions?: IEmailAiDraftAction[];
  /**
   * The signed-in user's mailbox address (item 3). The email surface defaults the composer's
   * From to send-as the current user; passing this shows the real address in the "From:" row
   * instead of a generic "My mailbox". Host-resolved (Xrm). Optional — omitted → generic label.
   */
  fromMailbox?: string;
  /**
   * Dataverse client URL (no trailing slash) used to build attachment
   * deep-links when carrying a parent email's attachments onto reply/forward.
   * Optional — absent → a relative document link is omitted (attachments still
   * carry as files).
   */
  dataverseUrl?: string;
  /**
   * Associations to carry onto Reply/Reply All/Forward (e.g. the parent's
   * inherited regarding records). Ignored for New (blank compose). Optional —
   * the host may supply an already-computed list (e.g. the reading-pane's
   * `filedAssociations` mapped to `ICommunicationAssociation`).
   */
  associations?: ICommunicationAssociation[];
  /**
   * Fired after a successful send with the new `sprk_communication` id — the
   * host's responsibility to refresh the reading pane / re-select the record
   * (this module owns compose dispatch only, not list/record refresh).
   */
  onSent?: (communicationId: string) => void;
  /** Fired on a send error (surfacing — e.g. a MessageBar — is the host's responsibility). */
  onError?: (err: SendCommunicationError) => void;
  /**
   * Fired whenever the composer dialog CLOSES — on Cancel OR after a successful Send.
   * The reading-pane host omits this (the dialog simply unmounts in place); a STANDALONE
   * compose launcher (e.g. the `sprk_emailpage` compose mode opened via `openEmailCompose`)
   * uses it to close its own host window once the compose is done.
   */
  onClose?: () => void;
  /**
   * UAT 2026-08-03: render the composer at the maximized geometry (fills the
   * host viewport, maximize toggle hidden). Pass true when the host is itself
   * a dedicated/overlaid modal surface (record-single mode inside the OOB
   * email-record modal; the standalone compose window). Default false — the
   * standard floating mid-size rectangle (list-mode reading page).
   */
  composerFullBleed?: boolean;
}

export interface UseEmailComposeActionsResult {
  /**
   * Pass straight through to `<EmailReadingPaneShell actions={...} />`
   * (task 032 seam). Implements `onReply`/`onReplyAll`/`onForward`/`onNew`
   * ONLY — this task's scope (FR-09/FR-10). `onArchive`/`onCreate` are
   * deliberately left `undefined`; wiring them is a different task's concern
   * and the shell already degrades an unwired handler to a console-warned
   * no-op.
   */
  actions: EmailToolbarActionHandlers;
  /**
   * Render this once, anywhere below the host's `FluentProvider` — owns the
   * canonical `SendEmailDialog`'s open/close lifecycle. Always the SAME
   * element type (`SendEmailDialog`) regardless of mode — no forked/second
   * composer is ever introduced.
   */
  composerDialog: React.ReactElement<SendEmailDialogElementProps>;
  /**
   * FR-15 — opens the OOB `sprk_communication` Email main form for
   * `communicationId` as an 85% `navigateTo` modal
   * (`INavigationService.openRecordModal`). Wire this to whichever "Open
   * full form" trigger the assembling host renders (e.g. the task-034 header
   * slot, via the exported `OpenFullFormButton`) — `EmailToolbar.tsx`
   * (task 032) is out of this task's edit scope.
   */
  openFullForm: (communicationId: string) => Promise<void>;
}

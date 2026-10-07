/**
 * Ambient TYPE shim for `@spaarke/ui-components/send-email-pane` (spaarkeai-word-add-in-r1 task 096).
 *
 * RUNTIME: the specifier resolves to the REAL shared source,
 * `src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/wrappers/SendEmailPane.tsx` — the thin
 * pane wrapper (ADR-045) over the one compose engine, `EmailComposer` — through exact aliases in
 * `webpack.config.js` (bundle) and `jest.config.js` (tests). Nothing here is bundled or rendered — the Email tab
 * renders the shared wrapper and engine, not a copy of them.
 *
 * TYPES ONLY: this package's `tsc` cannot type-check the shared source directly. Its compiler options are
 * stricter than the shared library's own (`exactOptionalPropertyTypes`, `noUncheckedIndexedAccess`: 56 errors
 * across the engine's files, none a runtime defect), and the engine's type graph reaches
 * `services/EntityCreationService.ts`, which imports `@spaarke/sdap-client` — a package this add-in does not
 * install. CI's production-typecheck job counts every non-test error, so pulling the shared source into this
 * program would read as production debt. Same mechanism and reason as
 * `src/client/external-spa/src/types/sdap-client.d.ts`.
 *
 * SCOPE: declares ONLY the props the Email tab passes, with the shared library's own names and shapes
 * (`EmailComposer.types.ts`, `wrappers/SendEmailPane.tsx`, `services/communicationApi.ts`,
 * `types/LookupTypes.ts`). Keep it a strict SUBSET of the real contract: when the Email tab needs another prop,
 * copy its declaration from `EmailComposer.types.ts` verbatim. `EmailView.test.tsx` renders the REAL wrapper +
 * engine with exactly these props and asserts the request it sends, so a prop this shim names that the engine
 * no longer honours fails a test.
 */
declare module '@spaarke/ui-components/send-email-pane' {
  import type * as React from 'react';

  /** `EmailComposer.types.ts` — `EmailAttachmentSourceKind`. */
  export type EmailAttachmentSourceKind = 'local' | 'spe' | 'related' | 'wizard';

  /** `EmailComposer.types.ts` — `IComposerAttachmentSource`. */
  export interface IComposerAttachmentSource {
    kind: EmailAttachmentSourceKind;
    config?: Record<string, unknown>;
  }

  /** `EmailComposer.types.ts` — `IAttachmentItem` (the fields the pane sets). */
  export interface IAttachmentItem {
    id: string;
    source: EmailAttachmentSourceKind;
    fileName: string;
    sizeBytes: number;
    mimeType?: string;
    /** `sprk_document` GUID — what the engine sends as `attachmentDocumentIds`. */
    documentId?: string;
    driveItemId?: string;
    /** Attach-file inclusion. Compose mode defaults it OFF unless set — the pane sets `true`. */
    selected?: boolean;
    linkSelected?: boolean;
    linkUrl?: string;
  }

  /** `services/communicationApi.ts` — `ICommunicationAssociation`. */
  export interface ICommunicationAssociation {
    /** Dataverse logical name (e.g. "sprk_matter"). */
    entityType: string;
    /** Dataverse record GUID (no braces). */
    entityId: string;
    entityName?: string;
    entityUrl?: string;
  }

  /** `types/LookupTypes.ts` — `ILookupItem`. */
  export interface ILookupItem {
    id: string;
    /** Display name, conventionally "Full Name (email)". */
    name: string;
    /** The first-class email the recipient field commits. */
    email?: string;
    entityType?: 'contact' | 'systemuser';
  }

  /** `services/communicationApi.ts` — `SendCommunicationError` (the BFF's ProblemDetails, parsed). */
  export interface SendCommunicationError extends Error {
    readonly status: number;
    readonly code: string;
    readonly detail: string;
    readonly correlationId?: string;
  }

  /** `services/EntityCreationService.ts` — `AuthenticatedFetchFn` (ADR-028: a fetch, never a token). */
  export type AuthenticatedFetchFn = (url: string, init?: RequestInit) => Promise<Response>;

  /** `services/communicationApi.ts` — `CommunicationSendMode`. */
  export type CommunicationSendMode = 'sharedMailbox' | 'user';

  /** The subset of `EmailComposerState` the pane reads in `onStateChange`. */
  export interface EmailComposerStateSubset {
    isSending: boolean;
  }

  /**
   * `wrappers/SendEmailPane.tsx` — `ISendEmailPaneProps` (`IEmailComposerProps` minus `mount`, which the wrapper
   * locks to `inline`; `mode` defaults to `compose`), the subset the Email tab passes.
   */
  export interface ISendEmailPaneProps {
    mode?: 'compose' | 'view' | 'reply' | 'forward' | 'draft';
    authenticatedFetch: AuthenticatedFetchFn;
    /** Host only, no `/api`. */
    bffBaseUrl?: string;
    initialTo?: string[];
    initialCc?: string[];
    initialSubject?: string;
    initialBody?: string;
    initialBodyFormat?: 'HTML' | 'PlainText';
    initialAttachments?: IAttachmentItem[];
    associations?: ICommunicationAssociation[];
    showAssociations?: boolean;
    attachmentSources?: IComposerAttachmentSource[];
    onSearchRecipients?: (query: string) => Promise<ILookupItem[]>;
    /** FIXES the send mode and hides the From switcher. */
    sendMode?: CommunicationSendMode;
    fromMailbox?: string;
    archiveToSpe?: boolean;
    onStateChange?: (state: EmailComposerStateSubset) => void;
    onSent?: (result: { communicationId: string }) => void;
    onError?: (err: SendCommunicationError) => void;
    className?: string;
  }

  /** The shared pane wrapper over the send engine (`forwardRef` component). */
  export const SendEmailPane: React.ForwardRefExoticComponent<ISendEmailPaneProps & React.RefAttributes<unknown>>;
}

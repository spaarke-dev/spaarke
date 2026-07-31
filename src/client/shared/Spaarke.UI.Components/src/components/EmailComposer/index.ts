/**
 * EmailComposer - Canonical email-composer engine (task 020, FR-12)
 *
 * The single React component that knows email-send mechanics. Semantic
 * wrappers (`SendEmailStep` / `SendEmailDialog` / `SendEmailPage`, task 021)
 * are the typical caller-facing surface — see `reference/r3-send-side-design.md`
 * §5.1.2 for when to use the engine directly vs. a wrapper.
 *
 * Standards: ADR-012 (shared component library), ADR-021 (Fluent v9),
 * ADR-028 (auth decoupling), ADR-045 (Communication architecture).
 */

// Engine
export { EmailComposer } from './EmailComposer';

// Semantic wrappers (task 021, FR-12) — the caller-facing API. Callers import a
// wrapper, NOT the engine directly (design §5.1.2 — direct engine use requires a
// code-review-justified comment). Each locks one `mount` value.
export { SendEmailStep } from './wrappers/SendEmailStep';
export type { ISendEmailStepProps } from './wrappers/SendEmailStep';

export { SendEmailDialog } from './wrappers/SendEmailDialog';
export type { ISendEmailDialogProps } from './wrappers/SendEmailDialog';

export { SendEmailPage } from './wrappers/SendEmailPage';
export type { ISendEmailPageProps } from './wrappers/SendEmailPage';

// Xrm-backed compose-lookup handler factory (shared by the Email code page +
// the SpaarkeAi `email` widget mounts — see file docblock). The engine stays
// context-agnostic; hosts inject these callbacks.
export {
  createXrmEmailComposeHandlers,
  resolveCurrentUserEmail,
  EMAIL_RECORD_LOOKUP_CATALOG,
} from './createXrmEmailComposeHandlers';
export type { XrmEmailComposeHandlers } from './createXrmEmailComposeHandlers';

// Pattern B launcher — opens the standalone Email code page (`sprk_emailpage`)
// as a centered modal dialog for a specific `sprk_communication` record.
// Callers (e.g. a Messages "open email" icon) invoke this instead of
// `Xrm.Navigation.openForm`.
export { openEmailRecord, EMAIL_PAGE_WEBRESOURCE_NAME } from './openEmailRecord';
export type { OpenEmailRecordOptions } from './openEmailRecord';
export { openEmailCompose } from './openEmailCompose';
export type { OpenEmailComposeOptions, OpenEmailComposeMode } from './openEmailCompose';

// Sub-components (exported for advanced/direct composition + task 023 unit tests)
export { RecipientField } from './subcomponents/RecipientField';
export type { IRecipientFieldProps } from './subcomponents/RecipientField';

export { BodyEditor } from './subcomponents/BodyEditor';
export type { IBodyEditorProps } from './subcomponents/BodyEditor';

export { AttachmentList } from './subcomponents/AttachmentList';
export type { IAttachmentListProps } from './subcomponents/AttachmentList';

export { SendModeRadio } from './subcomponents/SendModeRadio';
export type { ISendModeRadioProps } from './subcomponents/SendModeRadio';

export { AssociationChips } from './subcomponents/AssociationChips';
export type { IAssociationChipsProps } from './subcomponents/AssociationChips';

export { ComposerActionBar } from './subcomponents/ComposerActionBar';
export type { IComposerActionBarProps } from './subcomponents/ComposerActionBar';

// Reducer + pure helpers (task 023 unit-tests these directly)
export {
  emailComposerReducer,
  initialState,
  validateState,
  dedupSubjectPrefix,
  deriveReplyState,
  deriveForwardState,
  deriveDraftState,
  deriveViewState,
  mapStateToDraftUpdate,
  mapStateToSendRequest,
  buildBodyWithAttachmentLinks,
  ATTACHMENT_MAX_COUNT,
  ATTACHMENT_MAX_TOTAL_BYTES,
  ATTACHMENT_WARN_TOTAL_BYTES,
  DEFAULT_MAX_RECIPIENTS,
  validateLocalAttachmentFile,
} from './EmailComposer.reducer';
export type { IValidateOptions } from './EmailComposer.reducer';

// Public types
export type {
  EmailComposerMode,
  EmailComposerMount,
  EmailComposerBodyFormat,
  EmailAttachmentSourceKind,
  IComposerAttachmentSource,
  IAttachmentItem,
  IRecordLookupTarget,
  IPickedRecord,
  IEmailTemplateSummary,
  IEmailTemplateRenderResult,
  IEmailAiDraftAction,
  IEmailAiDraftRequest,
  IEmailAiDraftResult,
  IWizardContext,
  IRecipient,
  ValidationErrorCode,
  IValidationError,
  IValidationResult,
  ISourceCommunicationRecord,
  EmailComposerState,
  EmailComposerAction,
  IEmailComposerProps,
  IEmailComposerHandle,
  EmailComposerRef,
} from './EmailComposer.types';

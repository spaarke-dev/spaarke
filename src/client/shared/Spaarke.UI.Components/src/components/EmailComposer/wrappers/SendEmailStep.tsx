/**
 * SendEmailStep.tsx — semantic wrapper for the wizard-step caller (task 021, FR-12).
 *
 * Locks `mount='inline'` (no internal action bar — the wizard frame owns the
 * Finish/Send button) and forwards the composer handle so the host wizard can
 * drive `validate()` / `send()` from its own navigation. Thin by contract
 * (ADR-045, design §5.1.1): NO business logic — it only fixes `mount`, defaults
 * `mode` to `'compose'`, and delegates to `<EmailComposer />`.
 *
 * The forwarded ref IS the `IEmailComposerHandle` (`composerRef`): a host does
 *   const composerRef = useRef<IEmailComposerHandle>(null);
 *   <SendEmailStep ref={composerRef} ... />
 *   // on Finish: await composerRef.current?.send();
 *
 * No `@spaarke/auth` import (ADR-028) — `authenticatedFetch` is injected via props.
 */
import * as React from 'react';
import { forwardRef } from 'react';

import { EmailComposer } from '../EmailComposer';
import type {
  EmailComposerMode,
  EmailComposerBodyFormat,
  EmailComposerState,
  IEmailComposerHandle,
  IComposerAttachmentSource,
  IWizardContext,
} from '../EmailComposer.types';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';
import type { ICommunicationAssociation } from '../../../services/communicationApi';

export interface ISendEmailStepProps {
  /** Defaults to `'compose'`. */
  mode?: EmailComposerMode;
  initialTo?: string[];
  initialSubject?: string;
  initialBody?: string;
  initialBodyFormat?: EmailComposerBodyFormat;
  associations?: ICommunicationAssociation[];
  attachmentSources?: IComposerAttachmentSource[];
  wizardContext?: IWizardContext;
  authenticatedFetch: AuthenticatedFetchFn;
  bffBaseUrl: string;
  /** Wizard polls composer state to gate its Next/Finish button. */
  onStateChange?: (state: EmailComposerState) => void;
}

export const SendEmailStep = forwardRef<IEmailComposerHandle, ISendEmailStepProps>((props, ref) => (
  <EmailComposer {...props} ref={ref} mount="inline" mode={props.mode ?? 'compose'} />
));

SendEmailStep.displayName = 'SendEmailStep';

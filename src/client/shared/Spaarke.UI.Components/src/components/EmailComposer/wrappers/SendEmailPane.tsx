/**
 * SendEmailPane.tsx — semantic wrapper for a side-PANE caller (spaarkeai-word-add-in-r1 task 096).
 *
 * The fourth mount shape ADR-045 anticipates ("if a new mount emerges, add a new thin wrapper over the one
 * engine"): a narrow, chromeless pane that owns its own send — the Word add-in's Email tab, which emails the
 * open document from the task pane. It differs from the other three wrappers only in what it exposes:
 *   - `SendEmailStep`   locks `inline` but is a WIZARD step — the frame drives send/validation through the
 *                       handle, and it does not expose `onSent` / `onError` / `sendMode`.
 *   - `SendEmailDialog` is a modal (SprkModal chrome).
 *   - `SendEmailPage`   locks `page` — window chrome with a close ×, meaningless inside a task pane — and does
 *                       not expose `sendMode` / `onError`.
 * A pane needs the chromeless `inline` mount with the engine's own Send button (rendered in the "From:" row for
 * every editable mount), a host-locked send mode, and the result callbacks — so it forwards the engine's props
 * as they are.
 *
 * Thin by contract (ADR-045, design §5.1.1): NO business logic — it only fixes `mount='inline'`, defaults `mode`
 * to `'compose'`, and delegates to `<EmailComposer />`. No `@spaarke/auth` import (ADR-028) — `authenticatedFetch`
 * is injected via props.
 */
import * as React from 'react';
import { forwardRef } from 'react';

import { EmailComposer } from '../EmailComposer';
import type { EmailComposerMode, IEmailComposerHandle, IEmailComposerProps } from '../EmailComposer.types';

export type ISendEmailPaneProps = Omit<IEmailComposerProps, 'mount' | 'mode'> & {
  /** Defaults to `'compose'`. */
  mode?: EmailComposerMode;
};

export const SendEmailPane = forwardRef<IEmailComposerHandle, ISendEmailPaneProps>((props, ref) => (
  <EmailComposer {...props} ref={ref} mount="inline" mode={props.mode ?? 'compose'} />
));

SendEmailPane.displayName = 'SendEmailPane';

// The engine's failed-send wording, for a pane that shows send failures itself (`sendFailureDisplay="host"`).
// Re-exported HERE because a pane host consumes this file by exact alias (the Word add-in's
// `@spaarke/ui-components/send-email-pane`), never the barrel — and the function is pure (no host dependency).
export { describeSendFailure } from '../describeSendFailure';
export type { ISendFailureDescription } from '../describeSendFailure';

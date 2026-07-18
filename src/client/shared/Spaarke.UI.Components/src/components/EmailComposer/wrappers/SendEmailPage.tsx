/**
 * SendEmailPage.tsx — semantic wrapper for the Code Page caller (task 021, FR-12).
 *
 * Locks `mount='page'` (full-page entity-form chrome). `mode` is REQUIRED here —
 * the Communication Code Page (W4, task 041) drives it from the URL parameter
 * (`?mode=view|reply|forward|draft|compose`). Maps `onClose` → engine `onCancel`
 * and `onSent(result)` → `props.onSent?.(communicationId)`.
 *
 * Thin by contract (ADR-045, design §5.1.1): no business logic — the host Code
 * Page reads the `sprk_communication` record (view/reply/forward/draft) and
 * passes `communicationId` in; the engine never fetches (ADR-012).
 *
 * No `@spaarke/auth` import (ADR-028) — `authenticatedFetch` is injected via props.
 */
import * as React from 'react';

import { EmailComposer } from '../EmailComposer';
import type { EmailComposerMode } from '../EmailComposer.types';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';
import type { ICommunicationAssociation } from '../../../services/communicationApi';

export interface ISendEmailPageProps {
  /** REQUIRED — driven by the Code Page URL parameter. */
  mode: EmailComposerMode;
  /** Required for view/reply/forward/draft modes. */
  communicationId?: string;
  initialTo?: string[];
  initialCc?: string[];
  initialSubject?: string;
  initialBody?: string;
  associations?: ICommunicationAssociation[];
  authenticatedFetch: AuthenticatedFetchFn;
  bffBaseUrl: string;
  onSent?: (communicationId: string) => void;
  /** Close the Code Page (e.g. navigate away) — mapped to engine `onCancel`. */
  onClose?: () => void;
}

export function SendEmailPage(props: ISendEmailPageProps) {
  const { onClose, onSent, ...composerProps } = props;

  return (
    <EmailComposer
      {...composerProps}
      mount="page"
      onCancel={onClose}
      onSent={onSent ? result => onSent(result.communicationId) : undefined}
    />
  );
}

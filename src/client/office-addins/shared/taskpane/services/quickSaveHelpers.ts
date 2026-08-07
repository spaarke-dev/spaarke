import type { EntitySearchResult } from '../hooks/useEntitySearch';

/**
 * quickSaveHelpers.ts
 *
 * Pure, testable helpers for the Outlook ribbon one-click quick-save (FR-B2 / GitHub
 * #234). The Office.js glue lives in `outlook/commands/index.ts`; the request-shaping
 * and the file-vs-fallback decision live here so they are unit-testable without an
 * Office runtime.
 *
 * The save request mirrors the Email branch that `useSaveFlow` builds for
 * `POST /api/office/save` (same server contract — contentType/email/targetEntity/
 * aiOptions/documentMetadata). The idempotency key is carried in the body
 * (`idempotencyKey`) — the server accepts it there OR via the X-Idempotency-Key header
 * (OfficeEndpoints.SaveAsync), so the header-less `apiClient.post` works.
 */

/** A recipient as read from the Outlook item. */
export interface QuickSaveRecipient {
  email: string;
  displayName?: string;
  type: 'to' | 'cc' | 'bcc';
}

/** The email context the ribbon command reads from Office.js before quick-saving. */
export interface QuickSaveEmailContext {
  internetMessageId: string;
  subject: string;
  senderEmail?: string;
  senderName?: string;
  recipients?: QuickSaveRecipient[];
  sentDate?: Date;
}

/** The server request body for POST /api/office/save (Email content, quick-save path). */
export interface OfficeSaveRequestBody {
  contentType: 'Email';
  triggerAiProcessing: boolean;
  aiOptions: { profileSummary: boolean; ragIndex: boolean; deepAnalysis: boolean };
  documentMetadata: { name: string; description?: string };
  targetEntity: { entityType: string; entityId: string; displayName: string };
  email: {
    subject: string;
    senderEmail: string;
    senderName?: string;
    recipients: Array<{ type: 'To' | 'Cc' | 'Bcc'; email: string; name?: string }>;
    sentDate?: string;
    body: undefined;
    isBodyHtml: true;
    internetMessageId: string;
    selectedAttachmentFileNames: undefined;
  };
  idempotencyKey: string;
}

/** Default AI processing for the quick-save path (mirrors useSaveFlow's defaults). */
const DEFAULT_AI_OPTIONS = { profileSummary: true, ragIndex: true, deepAnalysis: false };

function mapRecipientType(type: 'to' | 'cc' | 'bcc'): 'To' | 'Cc' | 'Bcc' {
  return type === 'to' ? 'To' : type === 'cc' ? 'Cc' : 'Bcc';
}

/**
 * Build the `POST /api/office/save` body that files an email to the engine-predicted
 * record. The email body + attachment content are fetched server-side via Graph (OBO),
 * so the client sends only the internetMessageId + metadata — identical to useSaveFlow.
 */
export function buildEmailSaveRequest(
  context: QuickSaveEmailContext,
  target: EntitySearchResult,
  idempotencyKey: string
): OfficeSaveRequestBody {
  return {
    contentType: 'Email',
    triggerAiProcessing:
      DEFAULT_AI_OPTIONS.profileSummary || DEFAULT_AI_OPTIONS.ragIndex || DEFAULT_AI_OPTIONS.deepAnalysis,
    aiOptions: { ...DEFAULT_AI_OPTIONS },
    documentMetadata: { name: context.subject || 'Untitled Email' },
    targetEntity: {
      entityType: target.logicalName,
      entityId: target.id,
      displayName: target.name,
    },
    email: {
      subject: context.subject || 'Untitled Email',
      senderEmail: context.senderEmail || 'unknown@placeholder.com',
      ...(context.senderName ? { senderName: context.senderName } : {}),
      recipients: (context.recipients ?? []).map(r => ({
        type: mapRecipientType(r.type),
        email: r.email,
        ...(r.displayName ? { name: r.displayName } : {}),
      })),
      ...(context.sentDate ? { sentDate: context.sentDate.toISOString() } : {}),
      body: undefined,
      isBodyHtml: true,
      internetMessageId: context.internetMessageId,
      selectedAttachmentFileNames: undefined,
    },
    idempotencyKey,
  };
}

/**
 * Compute a stable idempotency key for a quick-save (SHA-256 of the message id + target).
 * Falls back to a plain concatenation when Web Crypto is unavailable (older hosts) —
 * the server also structurally dedups, so this is a best-effort de-dup hint.
 */
export async function computeQuickSaveIdempotencyKey(
  internetMessageId: string,
  target: EntitySearchResult
): Promise<string> {
  const canonical = `email:${internetMessageId}|${target.logicalName}:${target.id}`;
  try {
    if (typeof crypto !== 'undefined' && crypto.subtle) {
      const data = new TextEncoder().encode(canonical);
      const hashBuffer = await crypto.subtle.digest('SHA-256', data);
      return Array.from(new Uint8Array(hashBuffer))
        .map(b => b.toString(16).padStart(2, '0'))
        .join('');
    }
  } catch {
    // fall through to the plain canonical key
  }
  return canonical;
}

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
    /** Task 046 (b): always true here; the ribbon files under the email's own subject and never takes a typed name. */
    isNameSystemDerived: true;
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
      // Task 046 (b): the subject is the email's own, so the server stores it with a short unique suffix.
      isNameSystemDerived: true,
      body: undefined,
      isBodyHtml: true,
      internetMessageId: context.internetMessageId,
      selectedAttachmentFileNames: undefined,
    },
    idempotencyKey,
  };
}

/**
 * What a quick-save idempotency key is computed over. Outlook's ribbon quick-save (FR-B2) always
 * files to a PREDICTED record, so its canonical string names the message + target. Word's ribbon
 * quick-save (spaarkeai-word-add-in-r1 task 037 / FR-17) has no association-engine prediction to
 * key on — a Word quick-save is always a CREATE of an unfiled document (see `buildDocumentSaveRequest`)
 * — so its canonical string is over the document's own content instead: two rapid clicks on the SAME
 * unsaved bytes collapse to one key (the double-click acceptance criterion), while a legitimate
 * second quick-save after an edit produces a genuinely different key rather than being deduped
 * forever. `kind` is a real discriminant (not a label) so each source's canonical shape stays honest.
 */
export type QuickSaveIdempotencySource =
  | { readonly kind: 'email'; readonly internetMessageId: string; readonly target: EntitySearchResult }
  | { readonly kind: 'document'; readonly title: string; readonly contentBase64: string };

function canonicalQuickSaveKey(source: QuickSaveIdempotencySource): string {
  return source.kind === 'email'
    ? `email:${source.internetMessageId}|${source.target.logicalName}:${source.target.id}`
    : `document:${source.title}|${source.contentBase64}`;
}

/**
 * Compute a stable idempotency key for a quick-save (SHA-256 of the canonical source description).
 * Falls back to the plain canonical string when Web Crypto is unavailable (older hosts) — the
 * server also structurally dedups, so this is a best-effort de-dup hint.
 */
export async function computeQuickSaveIdempotencyKey(source: QuickSaveIdempotencySource): Promise<string> {
  const canonical = canonicalQuickSaveKey(source);
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

/** The document side of a Word quick-save context (the value `WordAdapter.getSubject()` returns). */
export interface QuickSaveDocumentContext {
  /** The document's title/subject — used as both the server-facing `title` and the base file name. */
  title: string;
}

/** The server request body for POST /api/office/save (Document content, Word quick-save path). */
export interface OfficeDocumentSaveRequestBody {
  contentType: 'Document';
  triggerAiProcessing: boolean;
  aiOptions: { profileSummary: boolean; ragIndex: boolean; deepAnalysis: boolean };
  document: {
    fileName: string;
    title: string;
    contentType: string;
    contentBase64: string;
  };
  idempotencyKey: string;
}

/** MIME type the server expects for a `.docx` upload (matches `useSaveFlow.ts`'s Document branch). */
const DOCX_CONTENT_TYPE = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';

/**
 * Build the `POST /api/office/save` body for a Word ribbon quick-save. Always a CREATE of a new,
 * unfiled `sprk_document` — no `targetEntity` (association is optional for a Document save, matching
 * the pane's own `useSaveFlow` behavior) and no `existingDocumentId`/`isNewVersion` (a one-click quick
 * save never attempts the pane's identity-resolved version-save path; the user can associate/file the
 * document later from the pane). Mirrors `buildEmailSaveRequest`'s shape for the Document branch of
 * the same server contract (`useSaveFlow.ts`'s `startSave`, `contentType === 'Document'`).
 */
export function buildDocumentSaveRequest(
  context: QuickSaveDocumentContext,
  contentBase64: string,
  idempotencyKey: string
): OfficeDocumentSaveRequestBody {
  const rawName = (context.title || 'Document').trim() || 'Document';
  const fileName = /\.docx$/i.test(rawName) ? rawName : `${rawName}.docx`;

  return {
    contentType: 'Document',
    triggerAiProcessing:
      DEFAULT_AI_OPTIONS.profileSummary || DEFAULT_AI_OPTIONS.ragIndex || DEFAULT_AI_OPTIONS.deepAnalysis,
    aiOptions: { ...DEFAULT_AI_OPTIONS },
    document: {
      fileName,
      title: context.title || 'Document',
      contentType: DOCX_CONTENT_TYPE,
      contentBase64,
    },
    idempotencyKey,
  };
}

/**
 * Convert a document's raw bytes to a base64 string — the exact conversion `SaveView.tsx`'s
 * `captureDocumentContent` already performs (byte-by-byte `String.fromCharCode` + `btoa`, not
 * `Buffer`, which does not exist in the add-in's browser runtime). Extracted here so the Word ribbon
 * command (a thin entry point, NFR-10) can reuse it without duplicating the loop, and so it is
 * independently unit-testable without an Office.js mock.
 */
export function arrayBufferToBase64(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = '';
  for (let i = 0; i < bytes.length; i++) {
    binary += String.fromCharCode(bytes[i] ?? 0);
  }
  return btoa(binary);
}

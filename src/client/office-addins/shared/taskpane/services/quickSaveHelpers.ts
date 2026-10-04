import type { EntitySearchResult } from '../hooks/useEntitySearch';
import { stripDocumentExtension, toDocxFileName } from '../utils/documentFileName';
import { mapProblemDetailsToMessage, type ProblemDetails } from '../utils/errorMessages';
import type { DocumentIdentityOutcome } from './documentIdentityService';

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
      // The FRIENDLY type name ("Matter"), never the logical name ("sprk_matter"): the save accepts only
      // friendly names (`OfficeEndpoints.ValidateSaveRequest` → 400 OFFICE_002 otherwise), and the
      // finalization worker matches on them. Sending `logicalName` refused every quick-save (#1075).
      entityType: target.entityType,
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
  | {
      readonly kind: 'document';
      readonly title: string;
      readonly contentBase64: string;
      /**
       * Task 089: set on a VERSION Quick Save (the open document resolved to this `sprk_document`). It is part of
       * the key so a version save and a create of the same bytes are never one operation; a create's key is
       * byte-for-byte what it was before task 089.
       */
      readonly existingDocumentId?: string;
    };

function canonicalQuickSaveKey(source: QuickSaveIdempotencySource): string {
  if (source.kind === 'email') {
    return `email:${source.internetMessageId}|${source.target.logicalName}:${source.target.id}`;
  }
  return source.existingDocumentId
    ? `document-version:${source.existingDocumentId}|${source.title}|${source.contentBase64}`
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

/**
 * Where a Word Quick Save goes (task 089, owner 2026-10-03):
 * - `create` — the open document is not a Spaarke document (identity `new`): a new, unfiled `sprk_document`, with
 *   `allowRename` so a name that already belongs to a DIFFERENT document is kept-both by the server instead of
 *   refused (409 `OFFICE_020`, the UAT-9 failure);
 * - `version` — the open document resolved (by URL or by its identity stamp) to `existingDocumentId`: a new version
 *   of it, the FR-11 path (`existingDocumentId` + `isNewVersion`), which the server's
 *   `OfficeVersionSaveAuthorizationFilter` gates on write access.
 *
 * A file NAME is never identity (#1005): nothing here turns a name match into a version save.
 */
export type QuickSaveTarget =
  | { readonly mode: 'create' }
  | { readonly mode: 'version'; readonly existingDocumentId: string };

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
    /** `create` only: keep both on a name collision (task 025's `AllowRename`). */
    allowRename?: true;
    /** `version` only, always with `isNewVersion` — the server refuses one without the other (OFFICE_018). */
    existingDocumentId?: string;
    isNewVersion?: true;
  };
  idempotencyKey: string;
}

/** MIME type the server expects for a `.docx` upload (matches `useSaveFlow.ts`'s Document branch). */
const DOCX_CONTENT_TYPE = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';

/**
 * The name a Quick Save gives the document — the pane's own rule (task 089 calls it rather than keeping a second
 * copy): the Document Name the pane would default to (`stripDocumentExtension` of the document's title, task 020),
 * uploaded under `toDocxFileName` of that name (`useSaveFlow`'s rule).
 */
export function quickSaveDocumentNames(title: string): { documentName: string; fileName: string } {
  const documentName = stripDocumentExtension((title || '').trim());
  return { documentName: documentName || 'document', fileName: toDocxFileName(documentName) };
}

/**
 * Build the `POST /api/office/save` body for a Word ribbon Quick Save (task 037; task 089 adds `target`). Never
 * filed to a record — no `targetEntity`; association is optional for a Document save, and the user files it from
 * the pane. Mirrors the Document branch `useSaveFlow.startSave` builds for the same server contract.
 */
export function buildDocumentSaveRequest(
  context: QuickSaveDocumentContext,
  contentBase64: string,
  idempotencyKey: string,
  target: QuickSaveTarget = { mode: 'create' }
): OfficeDocumentSaveRequestBody {
  const { documentName, fileName } = quickSaveDocumentNames(context.title);

  return {
    contentType: 'Document',
    triggerAiProcessing:
      DEFAULT_AI_OPTIONS.profileSummary || DEFAULT_AI_OPTIONS.ragIndex || DEFAULT_AI_OPTIONS.deepAnalysis,
    aiOptions: { ...DEFAULT_AI_OPTIONS },
    document: {
      fileName,
      title: documentName,
      contentType: DOCX_CONTENT_TYPE,
      contentBase64,
      ...(target.mode === 'version'
        ? { existingDocumentId: target.existingDocumentId, isNewVersion: true as const }
        : { allowRename: true as const }),
    },
    idempotencyKey,
  };
}

/**
 * The file name a stored document's SPE `webUrl` names, or `null` when it names none. SharePoint Embedded returns
 * an Office web URL of the documented shape `…/_layouts/15/doc2.aspx?sourcedoc={guid}&file=Name.docx&…` (Microsoft
 * Learn, "Open Office files"); a direct file URL (`…/Document%20Library/Name.docx`) names it in its last segment.
 * Used to tell the user the name the server ACTUALLY stored, which differs from the requested one when it kept both.
 */
export function fileNameFromWebUrl(webUrl: string | null | undefined): string | null {
  if (!webUrl) return null;
  let url: URL;
  try {
    url = new URL(webUrl);
  } catch {
    return null;
  }

  const fromQuery = url.searchParams.get('file')?.trim();
  if (fromQuery) return fromQuery;

  const lastSegment = url.pathname.split('/').pop() ?? '';
  let decoded: string;
  try {
    decoded = decodeURIComponent(lastSegment).trim();
  } catch {
    return null;
  }
  // A viewer page (`Doc.aspx`) without a `file` parameter names no document.
  return /\.docx?$/i.test(decoded) ? decoded : null;
}

/** The job outcome a Quick Save reads after the save: the document it landed on and where it is stored. */
export interface QuickSaveSavedDocument {
  documentId: string;
  webUrl?: string | null;
}

/**
 * The notification for a successful Quick Save — always says what happened, in words (task 089, UAT-9):
 * - version → "Saved a new version of '{name}'."
 * - create → "Saved to Spaarke as '{stored file name}'." — and when the server kept both under another name, says so.
 * - duplicate (the server had already saved exactly these bytes) → says nothing changed.
 * When the stored name cannot be read from the job, the create message names no file rather than a guessed one.
 */
export function describeQuickSaveSuccess(input: {
  target: QuickSaveTarget;
  requestedFileName: string;
  /** For a version: the resolved document's label (its name or file name), when identity resolution gave one. */
  documentLabel?: string | null;
  saved: QuickSaveSavedDocument | null;
  duplicate: boolean;
}): string {
  const storedFileName = fileNameFromWebUrl(input.saved?.webUrl);

  if (input.target.mode === 'version') {
    const name = input.documentLabel || storedFileName || input.requestedFileName;
    return input.duplicate
      ? `'${name}' has not changed since it was last saved to Spaarke. Nothing new was saved.`
      : `Saved a new version of '${name}'.`;
  }

  if (input.duplicate) {
    return storedFileName
      ? `This document is already saved in Spaarke as '${storedFileName}'. Nothing new was saved.`
      : 'This document is already saved in Spaarke. Nothing new was saved.';
  }
  if (!storedFileName) {
    return 'Saved to Spaarke.';
  }
  if (storedFileName.toLowerCase() !== input.requestedFileName.toLowerCase()) {
    return `Saved to Spaarke as '${storedFileName}'. A different document is already named '${input.requestedFileName}', so both were kept.`;
  }
  return `Saved to Spaarke as '${storedFileName}'.`;
}

/**
 * Why a Quick Save did NOT save when the open document's identity is neither "new" nor resolved — the same outcomes
 * on which the pane refuses to save without the user's explicit choice (task 024: conflict / indeterminate /
 * denied / error never create silently). A one-click command has no way to ask, so it says why and saves nothing.
 * Returns `null` for `new` and `resolved`, which Quick Save handles itself.
 */
export function describeUnsavableIdentity(identity: DocumentIdentityOutcome): string | null {
  switch (identity.kind) {
    case 'conflict':
      return 'Quick Save did not save this document: Spaarke has conflicting records for it. Open Save to Spaarke to choose what to do.';
    case 'indeterminate':
      return 'Quick Save did not save this document: Spaarke could not check whether it is already saved. Try again in a moment.';
    case 'denied':
      return "Quick Save did not save this document: you can't add a version to its Spaarke document. Open Save to Spaarke to save your copy as a new document.";
    case 'error':
      return `Quick Save did not save this document: Spaarke could not check whether it is already saved (${identity.message}).`;
    default:
      return null;
  }
}

/** The step of a Quick Save that failed — decides how a failure WITHOUT a server reason is worded. */
export type QuickSaveStage = 'connect' | 'read' | 'save';

/** A thrown `ApiClientError` carries the server's ProblemDetails on `.error` (duck-typed: no runtime import here). */
function serverProblemOf(error: unknown): (ProblemDetails & { status: number }) | null {
  const candidate = (error as { error?: unknown } | null)?.error as Partial<ProblemDetails> | undefined;
  return candidate && typeof candidate === 'object' && typeof candidate.status === 'number'
    ? (candidate as ProblemDetails & { status: number })
    : null;
}

/**
 * The notification for a Quick Save that failed — never the fixed "Failed to save" text (UAT-9, task 089):
 * - the server refused (403, 409, 413, 429, …) → the server's own message (its `detail`, else the catalog message
 *   for its `errorCode`, else its title), with the HTTP status;
 * - no server reason → what failed, in words: reading the document from Word, or reaching Spaarke.
 */
export function describeQuickSaveFailure(error: unknown, stage: QuickSaveStage): string {
  const problem = serverProblemOf(error);
  if (problem) {
    const known = problem.errorCode ? mapProblemDetailsToMessage(problem).message : '';
    const reason = (problem.detail || known || problem.title || '').trim();
    return reason
      ? `Spaarke did not save this document: ${reason} (${problem.status})`
      : `Spaarke did not save this document (HTTP ${problem.status}).`;
  }

  const raw = (error as { message?: unknown } | null)?.message;
  const detail = typeof raw === 'string' && raw.trim() ? ` (${raw.trim()})` : '';
  switch (stage) {
    case 'read':
      return `Couldn't read this document from Word, so nothing was saved${detail}.`;
    case 'connect':
      return `Couldn't connect to Spaarke, so nothing was saved${detail}.`;
    default:
      return `Couldn't reach Spaarke, so the document may not have been saved${detail}. Check your connection and try again.`;
  }
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

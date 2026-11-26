/**
 * createMcpEmailComposeHandlers.ts
 *
 * The MCP sibling of `createXrmEmailComposeHandlers`: the props an `EmailComposer` mount needs when it runs inside a
 * Copilot widget, where there is no `window.Xrm`. Everything goes through an injected {@link McpToolCaller}
 * (`callTool(name, args)`), so the engine stays context-agnostic (ADR-012) and this file imports no Xrm global and no
 * `@modelcontextprotocol/*` package.
 *
 * v1 scope (owner): To/Cc typed entry, subject, rich body, a regarding matter the HOST sets (the user does not pick
 * or remove it), attachments chosen from that matter's documents, Send. Every callback outside that scope is left
 * undefined, so the engine hides its control: no recipient lookup dialog, record lookup menu, relationship picker,
 * template picker, AI draft button or local upload. The host also returns the engine's own switches for the controls
 * the send tool has no field for (`sendMode` fixed = no From switcher, `showBcc: false`, `showInternalOnly: false`,
 * `associationsRemovable: false`), so a control is never shown whose value would be dropped on Send. Spread them with the
 * rest of the host props.
 *
 * How the two tools are reached:
 *  - Attachment picker: `onLookupRecord('sprk_document')` calls `spaarke_list_documents` for the regarding matter
 *    and hands the host's `pickDocument` UI a {@link McpDocumentListOutcome}: the rows, or an error to show. No other
 *    source feeds the picker.
 *  - Send: the engine sends through `sendCommunication()` over an injected `authenticatedFetch` (it has no send
 *    callback, and its contract is not changed here). `authenticatedFetch` below is a fetch adapter for that one
 *    route: it turns `POST /api/communications/send` into `spaarke_send_email`, adds the idempotency key, and turns
 *    a tool failure into a `SendCommunicationError` carrying the tool's own status, `reasonCode` and `correlationId`
 *    (`structuredContent` of McpToolResults.cs), so the engine's failure path (`describeSendFailure`) shows it.
 *    Nothing retries automatically: a retry is the user pressing Send again, with the same key.
 *  - Backstop: a request that still carries something the tool cannot send (Bcc, Internal Only, the user's own
 *    mailbox, or a "Related to" that no longer includes the matter) is refused before any tool call, with a
 *    message. It is never dropped silently.
 */
import {
  SendCommunicationError,
  type CommunicationSendMode,
  type ICommunicationAssociation,
} from '../../services/communicationApi';
import { SEND_FAILURE_NETWORK_CODE } from './describeSendFailure';
import {
  MCP_TOOL_LIST_DOCUMENTS,
  MCP_TOOL_SEND_EMAIL,
  type McpToolCaller,
  type McpToolCallResult,
} from './mcpToolCaller';
import type { IComposerAttachmentSource, IPickedRecord, IRecordLookupTarget } from './EmailComposer.types';

const DOCUMENT_ENTITY = 'sprk_document';
const MATTER_ENTITY = 'sprk_matter';
const SEND_ROUTE = '/api/communications/send';

/** Status of a tool refusal whose `structuredContent` names none: the request was understood and refused. */
const DEFAULT_TOOL_ERROR_STATUS = 422;

/** Reason codes of the host's own refusals (client side, before any tool call). */
export const McpHostRefusal = {
  BccNotSupported: 'mcp_host.bcc_not_supported',
  InternalOnlyNotSupported: 'mcp_host.internal_only_not_supported',
  OwnMailboxNotSupported: 'mcp_host.own_mailbox_not_supported',
  MatterRemoved: 'mcp_host.matter_association_removed',
} as const;

/** The matter the email is filed under. Set by the host (the compose tool's draft), not picked by the user. */
export interface McpEmailRegardingMatter {
  /** Matter GUID (no braces). */
  id: string;
  /** Display name, for the "Related to" chip. */
  name?: string;
}

/** A matter document as `spaarke_list_documents` returns it (only the fields the picker needs). */
export interface McpMatterDocument {
  id: string;
  name: string;
  sizeBytes?: number;
  fileType?: string;
  /** Spaarke record link, when the tool returns one. */
  url?: string;
}

/**
 * What the picker UI is handed: the matter's documents, or an error to show the user (the list call was refused or
 * never answered). Exactly one of the two is present.
 */
export type McpDocumentListOutcome =
  | { documents: McpMatterDocument[]; error?: undefined; reference?: undefined }
  | { documents?: undefined; error: string; reference?: string };

export interface CreateMcpEmailComposeHandlersOptions {
  /** The tool-call transport. */
  caller: McpToolCaller;
  /** The regarding matter (required). */
  regarding: McpEmailRegardingMatter;
  /**
   * The host's document picker UI: shown the list outcome (documents, or an error it must display), resolves the
   * chosen document (or `null` if cancelled or on an error). Omitted = the host has no picker, and the engine hides
   * the attach control.
   */
  pickDocument?: (outcome: McpDocumentListOutcome) => Promise<McpMatterDocument | null>;
  /**
   * Idempotency key of this compose session. A string seeds the key this handler set generates and rotates itself;
   * a function is read at every Send (the caller owns the key's lifetime, see `useMcpEmailHostProps`). Omitted =
   * generated. Every Send, including a retry after a failure, carries the current key; a Send that returned a
   * `communicationId` ends the session's key, so the next deliberate Send gets a new one.
   */
  idempotencyKey?: string | (() => string);
  /** Called after a Send that returned a `communicationId` (the session's key is spent). */
  onSent?: (communicationId: string) => void;
}

export interface McpEmailComposeHandlers {
  recordLookupCatalog: IRecordLookupTarget[];
  /** Present only when a `pickDocument` UI was supplied; answers `sprk_document` only. */
  onLookupRecord?: (entityType: string) => Promise<IPickedRecord | null>;
  /** Attachments come from the matter's documents only: no local-file source, so the engine hides "Add files". */
  attachmentSources: IComposerAttachmentSource[];
  /** The "Related to" association the host presets: the regarding matter. */
  associations: ICommunicationAssociation[];
  /** Fetch adapter for the engine's one request, `POST /api/communications/send`, served by `spaarke_send_email`. */
  authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>;
  /**
   * The idempotency key the NEXT Send carries. A live getter: it changes after a successful Send, so read it, do not
   * copy it. (`EmailComposer` has no such prop; the adapter reads the key itself.)
   */
  readonly idempotencyKey: string;
  /** EmailComposer prop: the send route has one sender, so the From switcher is hidden. */
  sendMode: CommunicationSendMode;
  /** EmailComposer prop: the send route has no Bcc field. */
  showBcc: false;
  /** EmailComposer prop: the send route has no `isInternalOnly` field. */
  showInternalOnly: false;
  /** EmailComposer prop: the matter is the host's, so its chip has no remove button. */
  associationsRemovable: false;
}

/** A new idempotency key. `crypto.randomUUID` when present, else a time + random fallback. */
export function newIdempotencyKey(): string {
  const c = (globalThis as { crypto?: { randomUUID?: () => string } }).crypto;
  if (c?.randomUUID) return c.randomUUID();
  return `idem-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`;
}

function textOf(result: McpToolCallResult): string | undefined {
  return result.content?.find(b => b.type === 'text' && b.text)?.text;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function stringOrUndefined(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

/** Read the documents array out of a list result; skips rows with no id or name. */
function readDocuments(result: McpToolCallResult): McpMatterDocument[] {
  const rows = result.structuredContent?.['documents'];
  if (!Array.isArray(rows)) return [];
  const docs: McpMatterDocument[] = [];
  for (const row of rows) {
    if (typeof row !== 'object' || row === null) continue;
    const r = row as Record<string, unknown>;
    if (typeof r['id'] !== 'string' || typeof r['name'] !== 'string') continue;
    docs.push({
      id: r['id'],
      name: r['name'],
      sizeBytes: typeof r['sizeBytes'] === 'number' ? r['sizeBytes'] : undefined,
      fileType: typeof r['fileType'] === 'string' ? r['fileType'] : undefined,
      url: typeof r['url'] === 'string' ? r['url'] : undefined,
    });
  }
  return docs;
}

/**
 * A tool failure as the error the engine's failure path already understands. The real server shape (McpToolResults.cs):
 * `structuredContent = { reasonCode, message, status, correlationId? }`, so the error keeps the tool's own `status`
 * (403 stays 403, 500 stays 500: a 5xx may have been sent, see `mayHaveBeenSent`), `reasonCode` and support reference.
 * An `isError` with no `structuredContent` is not a domain answer (bad arguments, a server exception): whether the
 * email went out is unknown, so it is `NETWORK` / status 0, which the composer treats as "may not have been sent".
 */
function toolError(result: McpToolCallResult): SendCommunicationError {
  const structured = isRecord(result.structuredContent) ? result.structuredContent : undefined;
  const text = textOf(result);
  if (!structured) {
    return new SendCommunicationError(
      0,
      SEND_FAILURE_NETWORK_CODE,
      text ?? 'The email service did not return a result.'
    );
  }
  const status = typeof structured['status'] === 'number' ? structured['status'] : DEFAULT_TOOL_ERROR_STATUS;
  return new SendCommunicationError(
    status,
    result.reasonCode ?? stringOrUndefined(structured['reasonCode']) ?? 'MCP_TOOL_ERROR',
    stringOrUndefined(structured['message']) ?? text ?? 'The email service refused the send.',
    stringOrUndefined(structured['correlationId'])
  );
}

/** What the picker shows for a list call that was refused or never answered. */
function listError(result: McpToolCallResult): { error: string; reference?: string } {
  const structured = isRecord(result.structuredContent) ? result.structuredContent : undefined;
  return {
    error:
      stringOrUndefined(structured?.['message']) ??
      textOf(result) ??
      "The matter's documents could not be loaded. Try again.",
    reference: stringOrUndefined(structured?.['correlationId']),
  };
}

/** A 200 JSON response. Uses the platform `Response` when there is one; else the structural subset `sendCommunication` reads. */
function jsonOk(payload: Record<string, unknown>): Response {
  const text = JSON.stringify(payload);
  if (typeof Response !== 'undefined') {
    return new Response(text, { status: 200, headers: { 'Content-Type': 'application/json' } });
  }
  return { ok: true, status: 200, json: async () => JSON.parse(text) } as unknown as Response;
}

function normalizeId(id: unknown): string {
  return typeof id === 'string' ? id.replace(/[{}]/g, '').toLowerCase() : '';
}

/** A refusal made by the host itself, before any tool call: a 4xx, so nothing is treated as possibly sent. */
function hostRefusal(code: string, message: string): SendCommunicationError {
  return new SendCommunicationError(422, code, message);
}

/**
 * Backstop for the controls the host hides: if a request still carries what `spaarke_send_email` has no field for,
 * refuse it with a message instead of sending it without that part.
 */
function assertRequestIsSendable(body: Record<string, unknown>, matterId: string): void {
  if (Array.isArray(body['bcc']) && body['bcc'].length > 0) {
    throw hostRefusal(
      McpHostRefusal.BccNotSupported,
      'Bcc recipients cannot be sent from here. Remove the Bcc recipients, or send this email from Spaarke.'
    );
  }
  if (body['isInternalOnly'] === true) {
    throw hostRefusal(
      McpHostRefusal.InternalOnlyNotSupported,
      'Internal Only emails cannot be sent from here. Send this email from Spaarke.'
    );
  }
  if (body['sendMode'] === 'User') {
    throw hostRefusal(
      McpHostRefusal.OwnMailboxNotSupported,
      'Emails are sent from the Spaarke shared mailbox here; sending from your own mailbox is not available.'
    );
  }
  const associations = body['associations'];
  const hasMatter =
    Array.isArray(associations) &&
    associations.some(
      a => isRecord(a) && a['entityType'] === MATTER_ENTITY && normalizeId(a['entityId']) === normalizeId(matterId)
    );
  if (!hasMatter) {
    throw hostRefusal(
      McpHostRefusal.MatterRemoved,
      'This email is filed under its matter and the matter can no longer be removed. Reopen the email to send it.'
    );
  }
}

export function createMcpEmailComposeHandlers(options: CreateMcpEmailComposeHandlersOptions): McpEmailComposeHandlers {
  const { caller, regarding, pickDocument } = options;
  let ownKey = typeof options.idempotencyKey === 'string' ? options.idempotencyKey : newIdempotencyKey();
  const currentKey = typeof options.idempotencyKey === 'function' ? options.idempotencyKey : () => ownKey;

  const onLookupRecord = pickDocument
    ? async (entityType: string): Promise<IPickedRecord | null> => {
        if (entityType !== DOCUMENT_ENTITY) return null;
        let outcome: McpDocumentListOutcome;
        try {
          const result = await caller.callTool(MCP_TOOL_LIST_DOCUMENTS, {
            regardingEntityType: MATTER_ENTITY,
            regardingId: regarding.id,
          });
          outcome = result.isError ? listError(result) : { documents: readDocuments(result) };
        } catch {
          outcome = { error: "The matter's documents could not be loaded: the server did not answer. Try again." };
        }
        const chosen = await pickDocument(outcome);
        if (!chosen) return null;
        return {
          entityType: DOCUMENT_ENTITY,
          id: chosen.id,
          name: chosen.name,
          url: chosen.url,
          sizeBytes: chosen.sizeBytes,
        };
      }
    : undefined;

  const authenticatedFetch = async (url: string, init?: RequestInit): Promise<Response> => {
    if (!url.split('?')[0].endsWith(SEND_ROUTE) || typeof init?.body !== 'string') {
      throw new Error(`MCP email host serves only POST ${SEND_ROUTE}; got ${url}`);
    }
    const body = JSON.parse(init.body) as Record<string, unknown>;
    assertRequestIsSendable(body, regarding.id);
    // The regarding matter is the host's, never the request's: a tampered association cannot redirect the archive.
    let result: McpToolCallResult;
    try {
      result = await caller.callTool(MCP_TOOL_SEND_EMAIL, {
        to: body['to'],
        cc: body['cc'],
        subject: body['subject'],
        body: body['body'],
        bodyFormat: body['bodyFormat'],
        regardingEntityType: MATTER_ENTITY,
        regardingId: regarding.id,
        attachmentDocumentIds: body['attachmentDocumentIds'] ?? [],
        idempotencyKey: currentKey(),
      });
    } catch (err) {
      // No result at all: the email may or may not have gone out. NETWORK keeps the retry (same key) safe.
      throw new SendCommunicationError(
        0,
        SEND_FAILURE_NETWORK_CODE,
        err instanceof Error && err.message ? err.message : 'Network request failed'
      );
    }
    if (result.isError) throw toolError(result);
    const communicationId = result.structuredContent?.['communicationId'];
    if (typeof communicationId === 'string' && communicationId) {
      // The key is spent: a replay of it would return this send, so the next deliberate send needs a new one.
      if (typeof options.idempotencyKey !== 'function') ownKey = newIdempotencyKey();
      options.onSent?.(communicationId);
      return jsonOk({ communicationId });
    }
    // No id on a success result is reported by sendCommunication as "sent, unconfirmed" (the user is not told to resend).
    // The key is kept: a retry replays this send.
    return jsonOk({});
  };

  return {
    recordLookupCatalog: [{ logicalName: DOCUMENT_ENTITY, displayName: 'Document' }],
    onLookupRecord,
    attachmentSources: [{ kind: 'related' }],
    associations: [{ entityType: MATTER_ENTITY, entityId: regarding.id, entityName: regarding.name }],
    authenticatedFetch,
    get idempotencyKey() {
      return currentKey();
    },
    sendMode: 'sharedMailbox',
    showBcc: false,
    showInternalOnly: false,
    associationsRemovable: false,
  };
}

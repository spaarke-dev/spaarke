/**
 * createMcpEmailComposeHandlers.ts
 *
 * The MCP sibling of `createXrmEmailComposeHandlers`: the props an `EmailComposer` mount needs when it runs inside a
 * Copilot widget, where there is no `window.Xrm`. Everything goes through an injected {@link McpToolCaller}
 * (`callTool(name, args)`), so the engine stays context-agnostic (ADR-012) and this file imports no Xrm global and no
 * `@modelcontextprotocol/*` package.
 *
 * v1 scope (owner): To/Cc typed entry, subject, rich body, a regarding matter the HOST sets (the user does not pick
 * it), attachments chosen from that matter's documents, Send. Every callback outside that scope is left undefined, so
 * the engine hides its control: no recipient lookup dialog, record lookup menu, relationship picker, template picker,
 * AI draft button or local upload.
 *
 * How the two tools are reached:
 *  - Attachment picker: `onLookupRecord('sprk_document')` calls `spaarke_list_documents` for the regarding matter
 *    and hands the rows to the host's `pickDocument` UI. No other source feeds the picker.
 *  - Send: the engine sends through `sendCommunication()` over an injected `authenticatedFetch` (it has no send
 *    callback, and its contract is not changed here). `authenticatedFetch` below is a fetch adapter for that one
 *    route: it turns `POST /api/communications/send` into `spaarke_send_email`, adds the idempotency key, and turns
 *    an `isError` result into a `SendCommunicationError` carrying the tool's `reasonCode`, so the engine's own
 *    failure path (`describeSendFailure`) shows it. Nothing retries automatically: a retry is the user pressing Send
 *    again, with the same key.
 */
import { SendCommunicationError, type ICommunicationAssociation } from '../../services/communicationApi';
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

export interface CreateMcpEmailComposeHandlersOptions {
  /** The tool-call transport. */
  caller: McpToolCaller;
  /** The regarding matter (required). */
  regarding: McpEmailRegardingMatter;
  /**
   * The host's document picker UI: shown the matter's documents, resolves the chosen one (or `null` if cancelled).
   * Omitted = the host has no picker, and the engine hides the attach control.
   */
  pickDocument?: (documents: McpMatterDocument[]) => Promise<McpMatterDocument | null>;
  /**
   * Idempotency key for this compose session. Generated once when omitted; every Send of this handler set,
   * including a retry after a failure, carries it.
   */
  idempotencyKey?: string;
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
  /** The idempotency key every Send of this compose session carries. */
  idempotencyKey: string;
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
 * A tool refusal as the error the engine's failure path already understands: the tool's `reasonCode` is the code.
 * 422 (a 4xx): the tool refused before sending, so the engine treats nothing as possibly sent.
 */
function toolError(result: McpToolCallResult): SendCommunicationError {
  return new SendCommunicationError(
    422,
    result.reasonCode ?? 'MCP_TOOL_ERROR',
    textOf(result) ?? 'The email service refused the send.'
  );
}

/** A 200 JSON response. Uses the platform `Response` when there is one; else the structural subset `sendCommunication` reads. */
function jsonOk(payload: Record<string, unknown>): Response {
  const text = JSON.stringify(payload);
  if (typeof Response !== 'undefined') {
    return new Response(text, { status: 200, headers: { 'Content-Type': 'application/json' } });
  }
  return { ok: true, status: 200, json: async () => JSON.parse(text) } as unknown as Response;
}

export function createMcpEmailComposeHandlers(options: CreateMcpEmailComposeHandlersOptions): McpEmailComposeHandlers {
  const { caller, regarding, pickDocument } = options;
  const idempotencyKey = options.idempotencyKey ?? newIdempotencyKey();

  const onLookupRecord = pickDocument
    ? async (entityType: string): Promise<IPickedRecord | null> => {
        if (entityType !== DOCUMENT_ENTITY) return null;
        const result = await caller.callTool(MCP_TOOL_LIST_DOCUMENTS, {
          regardingEntityType: MATTER_ENTITY,
          regardingId: regarding.id,
        });
        if (result.isError) return null;
        const chosen = await pickDocument(readDocuments(result));
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
    // The regarding matter is the host's, never the request's: a tampered association cannot redirect the archive.
    const result = await caller.callTool(MCP_TOOL_SEND_EMAIL, {
      to: body['to'],
      cc: body['cc'],
      subject: body['subject'],
      body: body['body'],
      bodyFormat: body['bodyFormat'],
      regardingEntityType: MATTER_ENTITY,
      regardingId: regarding.id,
      attachmentDocumentIds: body['attachmentDocumentIds'] ?? [],
      idempotencyKey,
    });
    if (result.isError) throw toolError(result);
    const communicationId = result.structuredContent?.['communicationId'];
    // No id on a success result is reported by sendCommunication as "sent, unconfirmed" (the user is not told to resend).
    return jsonOk(typeof communicationId === 'string' ? { communicationId } : {});
  };

  return {
    recordLookupCatalog: [{ logicalName: DOCUMENT_ENTITY, displayName: 'Document' }],
    onLookupRecord,
    attachmentSources: [{ kind: 'related' }],
    associations: [{ entityType: MATTER_ENTITY, entityId: regarding.id, entityName: regarding.name }],
    authenticatedFetch,
    idempotencyKey,
  };
}

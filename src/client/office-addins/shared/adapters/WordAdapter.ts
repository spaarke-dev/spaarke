/**
 * Word-specific host adapter implementation.
 *
 * Provides access to Word documents for saving to Spaarke DMS.
 * Implements the IHostAdapter interface for host-agnostic task pane usage.
 * Requires Office.js WordApi requirement set 1.3 or higher.
 *
 * @example
 * ```typescript
 * import { WordAdapter } from './WordAdapter';
 *
 * const adapter = new WordAdapter();
 * await adapter.initialize();
 *
 * // Get document content as OOXML
 * const content = await adapter.getDocumentContent({ format: 'ooxml' });
 *
 * // Insert a link at cursor position
 * await adapter.insertLink('https://spaarke.com/doc/123', 'View Document');
 * ```
 */

import type { IHostAdapter } from './IHostAdapter';
import type {
  HostType,
  ItemType,
  BodyContent,
  AttachmentInfo,
  Recipient,
  HostCapabilities,
  InsertLinkResult,
  AttachFileResult,
  GetDocumentContentOptions,
  HostAdapterError,
  HostAdapterErrorCode,
  EmailComposeContent,
  ComposeEmailResult,
} from './types';

/**
 * Minimum required WordApi version for this adapter.
 */
const MIN_WORD_API_VERSION = '1.3';

/**
 * The FR-02 stamp contract (spaarkeai-word-add-in-r1 task 051, reading what task 014 writes).
 * Mirrors `OfficeDocumentStamp.StampNamespace` / `.StampRootElement` / `.StampIdElement`
 * (`src/server/api/Sprk.Bff.Api/Services/Office/OfficeDocumentStamp.cs`). TypeScript cannot
 * reference a C# constant, so this is a byte-for-byte VALUE mirror, not a shared reference — a
 * silent mismatch here means `getByNamespaceAsync` simply never finds the part the server wrote,
 * with no compiler or runtime signal. Re-verify against that file (or
 * `projects/spaarkeai-word-add-in-r1/notes/014-xml-part-stamp-decisions.md` §13.3) before changing
 * any of the three strings below.
 */
const STAMP_NAMESPACE = 'urn:spaarke:office:document-identity:1';
const STAMP_ROOT_ELEMENT = 'documentIdentity';
const STAMP_ID_ELEMENT = 'documentId';

/**
 * A bare, optionally-braced GUID's shape — what `Guid.ToString("D")` writes server-side, plus the
 * braced form defensively (`cleanGuid`, applied by this method's caller per ADR-044, strips braces).
 */
const STAMP_GUID_SHAPE = /^\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}?$/;
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

/**
 * Parse one custom XML part's raw XML text and return its stamp id, or `null` for anything that
 * isn't exactly task 014's shape: a namespace/root mismatch (any other custom XML part — a
 * bibliography, Google Docs metadata, SharePoint columns — 019 condition 2/3), malformed XML, a
 * missing `documentId` child, or text that isn't GUID-shaped. Mirrors the server's
 * `TryReadStampId` (`OfficeDocumentStamp.cs`) — same rejection list.
 *
 * Returns a lowercase, brace-stripped candidate purely so THIS function can correctly decide "is
 * this GUID-shaped" — `shared/adapters` sits below `shared/taskpane` in this package's layering, so
 * it cannot import `shared/taskpane/utils/cleanGuid` without inverting that boundary (no
 * `shared/adapters/**` file imports from `shared/taskpane/**` today). `readDocumentStamp()`'s
 * caller (`documentIdentityService.ts`, which already imports the canonical `cleanGuid`) still runs
 * the result through it before the id crosses into saved state — that is the ADR-044 boundary; the
 * normalization here is an implementation detail of shape validation, not a second one.
 */
function extractStampId(xml: string): string | null {
  let doc: Document;
  try {
    doc = new DOMParser().parseFromString(xml, 'application/xml');
  } catch {
    return null;
  }

  // A well-formedness failure surfaces as a <parsererror> document, not a thrown exception (both
  // jsdom and real browsers do this) — must check explicitly rather than trust the try/catch above.
  if (doc.getElementsByTagName('parsererror').length > 0) {
    return null;
  }

  // lib.dom.d.ts declares `documentElement: HTMLElement` (non-nullable), but the WHATWG spec itself
  // types it `Element?` — a degenerate parse (e.g. an empty string, which a hostile or buggy
  // getXmlAsync result could produce) can genuinely yield no root element. This guards real
  // untrusted-input behavior the lib's type declaration under-states, not a redundant check.
  const root = doc.documentElement;
  if (!root || root.localName !== STAMP_ROOT_ELEMENT || root.namespaceURI !== STAMP_NAMESPACE) {
    return null;
  }

  const idNode = root.getElementsByTagNameNS(STAMP_NAMESPACE, STAMP_ID_ELEMENT)[0];
  const raw = idNode?.textContent?.trim();
  if (!raw || !STAMP_GUID_SHAPE.test(raw)) {
    return null;
  }

  const bare = raw.replace(/[{}]/g, '').toLowerCase();
  return bare === EMPTY_GUID ? null : bare;
}

/**
 * Word-specific host adapter implementation.
 *
 * Handles document content extraction, metadata retrieval, and link insertion
 * for Word documents. Does not support attachments or recipients (email-only features).
 */
export class WordAdapter implements IHostAdapter {
  private _initialized = false;
  private _documentUrl: string | null = null;

  /**
   * Get the Office host type.
   * @returns 'word'
   */
  getHostType(): HostType {
    return 'word';
  }

  /**
   * Get the type of the current item.
   * @returns 'document' for Word
   */
  getItemType(): ItemType {
    return 'document';
  }

  /**
   * Get the unique identifier of the current document.
   *
   * For Word, this returns the document URL if available, or generates
   * a unique identifier based on document properties and timestamp.
   *
   * @returns Promise resolving to the document identifier
   * @throws When adapter is not initialized
   */
  async getItemId(): Promise<string> {
    this.ensureInitialized();

    // Return cached URL if available
    if (this._documentUrl) {
      return this._documentUrl;
    }

    // Try to get document URL from Office.js
    return Word.run(async context => {
      const document = context.document;
      const properties = document.properties;

      // Load properties to help generate a unique ID
      properties.load(['title', 'author', 'creationDate']);
      await context.sync();

      // Try to construct a meaningful identifier
      // Note: Office.js doesn't directly expose the file URL in all scenarios
      const title = properties.title || 'untitled';
      const author = properties.author || 'unknown';
      const creationDate = properties.creationDate?.toISOString() || Date.now().toString();

      // Generate a consistent ID based on document properties
      const id = `word-doc-${this.hashString(`${title}-${author}-${creationDate}`)}`;
      this._documentUrl = id;
      return id;
    });
  }

  /**
   * Get the title of the current document.
   *
   * Returns the document title from document properties, or 'Untitled Document'
   * if no title is set.
   *
   * @returns Promise resolving to the document title
   */
  async getSubject(): Promise<string> {
    this.ensureInitialized();

    return Word.run(async context => {
      const properties = context.document.properties;
      properties.load('title');
      await context.sync();
      return properties.title || 'Untitled Document';
    });
  }

  /**
   * Get the body content of the document.
   *
   * Retrieves the document body as HTML or plain text depending on
   * the preferred type specified.
   *
   * @param preferredType - 'html' or 'text'. Defaults to 'html'.
   * @returns Promise resolving to the body content with type indicator
   */
  async getBody(preferredType: 'html' | 'text' = 'html'): Promise<BodyContent> {
    this.ensureInitialized();

    return Word.run(async context => {
      const body = context.document.body;

      if (preferredType === 'html') {
        const html = body.getHtml();
        await context.sync();
        return {
          content: html.value,
          type: 'html',
        };
      } else {
        body.load('text');
        await context.sync();
        return {
          content: body.text,
          type: 'text',
        };
      }
    });
  }

  /**
   * Get attachments from the current item.
   *
   * Word documents do not have attachments in the same sense as emails.
   * This method always returns an empty array.
   *
   * @returns Promise resolving to an empty array
   */
  async getAttachments(): Promise<AttachmentInfo[]> {
    // Word documents don't have attachments like Outlook emails
    return [];
  }

  /**
   * Get the content of a specific attachment.
   *
   * Not supported for Word documents. Always throws an error.
   *
   * @param _attachmentId - Ignored
   * @throws Always throws CAPABILITY_NOT_SUPPORTED error
   */
  async getAttachmentContent(_attachmentId: string): Promise<AttachmentInfo> {
    throw this.createError(
      'CAPABILITY_NOT_SUPPORTED',
      'Word documents do not support attachments. Use getDocumentContent() instead.'
    );
  }

  /**
   * Get the sender's email address.
   *
   * Not applicable for Word documents. Returns an empty string.
   *
   * @returns Promise resolving to an empty string
   */
  async getSenderEmail(): Promise<string> {
    // Not applicable for Word documents
    return '';
  }

  /**
   * Get the recipients of the current item.
   *
   * Not applicable for Word documents. Returns an empty array.
   *
   * @returns Promise resolving to an empty array
   */
  async getRecipients(): Promise<Recipient[]> {
    // Not applicable for Word documents
    return [];
  }

  /**
   * Get the open document's URL (spaarkeai-word-add-in-r1 FR-01 / task 013).
   *
   * Reads `Office.context.document.url` — a Common API property, available without `Word.run` —
   * EXACTLY as Office reports it. No reshaping, re-encoding, or trimming: Spike-1 verified Word web
   * and Word desktop return byte-identical raw-space paths that the BFF's identity resolver
   * (`POST /api/documents/resolve-identity`) consumes as-is.
   *
   * An unsaved document (never saved to a cloud location) has no URL. Office reports this as an
   * empty string, not `undefined` — treated here as a defined, expected `null` result rather than a
   * throw, per the interface contract.
   *
   * @returns Promise resolving to the document's absolute URL, or `null` when the document has none.
   */
  async getDocumentUrl(): Promise<string | null> {
    this.ensureInitialized();

    const url = Office.context.document.url;
    return url ? url : null;
  }

  /**
   * Read the client-side custom XML identity stamp task 014 (FR-02) writes into every saved
   * document (spaarkeai-word-add-in-r1 task 051 — the client half that completes FR-02).
   *
   * Common API only (`Office.context.document.customXmlParts.getByNamespaceAsync` → `getXmlAsync`)
   * — 019 condition 1 forbids `Word.Document.customXmlParts` (WordApi 1.4; the manifest deliberately
   * stays at 1.3 to keep Office 2019/2021 LTSC installable). Promisified the same way
   * {@link getCompressedFile} wraps `getFileAsync`. Guarded by `isSetSupported('CustomXmlParts')`
   * (019 condition 4) BEFORE any Common API call is made, extending {@link checkRequirementSet}
   * rather than adding a second helper (root CLAUDE.md §11).
   *
   * A missing, unreadable, or internally-disagreeing stamp is a NORMAL state (019 condition 3) —
   * every failure mode resolves to `null`, never a throw, so this can never block the save flow.
   * **A stamp is a hint, never an authorization** (014 §3): this method makes no network call and
   * asserts nothing beyond "the bytes carry this GUID" — resolving the id through an authorized
   * server path is the caller's job (`documentIdentityService.applyStampPrecedence`).
   *
   * @returns The single distinct Spaarke id found, or `null` for every failure mode (unsupported
   * requirement set, no matching part, malformed XML, or two disagreeing ids).
   */
  async readDocumentStamp(): Promise<string | null> {
    this.ensureInitialized();

    if (!this.checkRequirementSet('CustomXmlParts')) {
      return null;
    }

    try {
      const parts = await this.getCustomXmlPartsByNamespace(STAMP_NAMESPACE);

      // Mirrors the server's TryReadStamp loop (OfficeDocumentStamp.cs): collect the one id every
      // matching part agrees on; two DISTINCT ids is "no answer is better than the wrong one".
      let found: string | null = null;
      for (const part of parts) {
        const xml = await this.getPartXml(part);
        const id = extractStampId(xml);
        if (!id) {
          continue;
        }
        if (found !== null && found !== id) {
          return null;
        }
        found = id;
      }

      return found;
    } catch {
      return null;
    }
  }

  /**
   * Get the document content as an ArrayBuffer.
   *
   * The default (and `ooxml`) path returns the **real .docx binary** (the compressed OOXML package)
   * via `Office.context.document.getFileAsync(Office.FileType.Compressed)`. This replaces the previous
   * `body.getOoxml()` approach, which returned FLAT OOXML XML text — not a valid .docx — so SPE could
   * not preview it, Word could not open it, and AI profiling reported "unsupported file type"
   * (email-communication-intelligence-r2 UAT 2026-09-03). `getFileAsync(Compressed)` returns the same
   * bytes Word would write to disk, which every downstream consumer (SPE preview, Compose mount, AI
   * text extraction) expects. `html`/`text` remain body-scoped extractions for non-save callers.
   *
   * @param options - Options specifying the desired format
   * @returns Promise resolving to the document content as ArrayBuffer
   */
  async getDocumentContent(options?: GetDocumentContentOptions): Promise<ArrayBuffer> {
    this.ensureInitialized();

    const format = options?.format ?? 'ooxml';

    // The save path wants the actual .docx — Compressed is the file Word writes to disk.
    if (format === 'ooxml') {
      return this.getCompressedFile();
    }

    return Word.run(async context => {
      const body = context.document.body;
      let content: string;

      switch (format) {
        case 'html': {
          const html = body.getHtml();
          await context.sync();
          content = html.value;
          break;
        }
        case 'text': {
          body.load('text');
          await context.sync();
          content = body.text;
          break;
        }
        case 'pdf': {
          // PDF export is not directly available via Word.js API
          // Server-side conversion is required
          throw this.createError(
            'CAPABILITY_NOT_SUPPORTED',
            'PDF export requires server-side conversion. Retrieve as OOXML and convert on server.'
          );
        }
        default: {
          throw this.createError(
            'CAPABILITY_NOT_SUPPORTED',
            `Unsupported format: ${format}. Use 'ooxml', 'html', or 'text'.`
          );
        }
      }

      // Convert string to ArrayBuffer
      const encoder = new TextEncoder();
      const uint8Array = encoder.encode(content);
      return uint8Array.buffer;
    });
  }

  /**
   * Read the current document as a compressed .docx via the Common API `getFileAsync`, assembling the
   * 4 MB-max slices into a single ArrayBuffer. `getFileAsync` returns the document's SAVED state; Office
   * auto-persists a temporary copy for an unsaved/dirty document, so this works before an explicit save.
   * Slices are read sequentially (simpler + avoids the parallel-callback edge cases) and the file handle
   * is always closed.
   *
   * Ported verbatim from `word/WordHostAdapter.getCompressedFile()` (deleted in task 010 / FR-04). The
   * ONLY intentional divergence is the rejection value: this adapter surfaces a typed
   * `CONTENT_RETRIEVAL_FAILED` `HostAdapterError` instead of a bare `Error`, surfacing the underlying
   * Office error message as that typed error's `message`. The byte-producing statements (slice size,
   * read order, concatenation) are character-for-character identical — changing them regresses the
   * 2026-09-03 UAT defect.
   */
  private getCompressedFile(): Promise<ArrayBuffer> {
    return new Promise<ArrayBuffer>((resolve, reject) => {
      Office.context.document.getFileAsync(Office.FileType.Compressed, { sliceSize: 65536 }, result => {
        if (result.status !== Office.AsyncResultStatus.Succeeded) {
          reject(
            this.createError(
              'CONTENT_RETRIEVAL_FAILED',
              result.error?.message || 'Failed to read the Word document (getFileAsync).'
            )
          );
          return;
        }

        const file = result.value;
        const sliceCount = file.sliceCount;
        const slices: Uint8Array[] = new Array(sliceCount);

        const finish = (err?: HostAdapterError) => {
          file.closeAsync(() => {
            /* best-effort close */
          });
          if (err) {
            reject(err);
            return;
          }
          const total = slices.reduce((n, s) => n + s.length, 0);
          const out = new Uint8Array(total);
          let offset = 0;
          for (const s of slices) {
            out.set(s, offset);
            offset += s.length;
          }
          resolve(out.buffer);
        };

        const readSlice = (index: number): void => {
          if (index >= sliceCount) {
            finish();
            return;
          }
          file.getSliceAsync(index, sliceResult => {
            if (sliceResult.status !== Office.AsyncResultStatus.Succeeded) {
              finish(
                this.createError(
                  'CONTENT_RETRIEVAL_FAILED',
                  sliceResult.error?.message || `Failed to read document slice ${index}.`
                )
              );
              return;
            }
            const data = sliceResult.value.data as unknown;
            slices[index] = data instanceof Uint8Array ? data : Uint8Array.from(data as number[]);
            readSlice(index + 1);
          });
        };

        readSlice(0);
      });
    });
  }

  /**
   * Promisify `CustomXmlParts.getByNamespaceAsync` (Common API — see {@link readDocumentStamp}).
   * A synchronous throw inside the executor (e.g. `Office.context.document` itself being absent on a
   * misbehaving host) becomes a promise rejection, so callers only need one `try`/`catch`.
   */
  private getCustomXmlPartsByNamespace(namespace: string): Promise<Office.CustomXmlPart[]> {
    return new Promise((resolve, reject) => {
      Office.context.document.customXmlParts.getByNamespaceAsync(namespace, result => {
        if (result.status !== Office.AsyncResultStatus.Succeeded) {
          reject(result.error);
          return;
        }
        resolve(result.value);
      });
    });
  }

  /** Promisify `CustomXmlPart.getXmlAsync` (Common API — see {@link readDocumentStamp}). */
  private getPartXml(part: Office.CustomXmlPart): Promise<string> {
    return new Promise((resolve, reject) => {
      part.getXmlAsync(result => {
        if (result.status !== Office.AsyncResultStatus.Succeeded) {
          reject(result.error);
          return;
        }
        resolve(result.value);
      });
    });
  }

  /**
   * Get the capabilities of this adapter.
   *
   * Word adapter supports document content retrieval and link insertion,
   * but not email-specific features like attachments or recipients.
   *
   * @returns The capabilities object
   */
  getCapabilities(): HostCapabilities {
    const isApiSupported = this.checkRequirementSet('WordApi', MIN_WORD_API_VERSION);
    // task 027 / FR-10 (NFR-10): decided at runtime, never a manifest requirement — see the
    // HostCapabilities.canOpenBrowserWindow doc comment.
    const canOpenBrowserWindow = this.checkRequirementSet('OpenBrowserWindowApi', '1.1');

    return {
      canGetAttachments: false,
      canGetRecipients: false,
      canGetSender: false,
      canGetDocumentContent: isApiSupported,
      canGetDocumentUrl: true,
      // task 051 / FR-02 (client half): unlike canGetDocumentUrl, genuinely conditional — a host can
      // satisfy this adapter's WordApi 1.3 floor and still lack the separate Common `CustomXmlParts`
      // requirement set (019 condition 4). Bare one-arg call: the set is unversioned (019 §6 cond. 4).
      canReadDocumentStamp: this.checkRequirementSet('CustomXmlParts'),
      canSaveAsPdf: true, // Server-side conversion
      canSaveAsEml: false,
      canInsertLink: isApiSupported,
      canAttachFile: false,
      canOpenBrowserWindow,
      // task 036 / FR-15: `Office.context.mailbox` does not exist in Word — always false. Never
      // reachable via a `hostType` conditional in a view; the view reads this flag.
      canComposeEmail: false,
      // task 040 / FR-19: linked-todos is spec'd Outlook-only (spec.md Assumptions) — Word has no
      // `sprk_communication` counterpart for the banner's query to key off.
      canShowLinkedTodos: false,
      // task 040 / FR-19: triage/auto-match suggestions is spec'd Outlook-only — the engine keys off
      // a captured email's sender/recipients/thread signals, which a Word document has none of.
      canSuggestRelatedRecords: false,
      // task 020 / FR-06: unconditionally true — `getSubject()` (WordApi 1.1, well below this
      // adapter's own WordApi 1.3 init-time floor) always returns a usable value (the document's
      // Title, or "Untitled Document"), so no separate requirement-set check is needed here, matching
      // `canGetDocumentUrl`'s unconditional-true pattern above.
      canProvideDocumentName: true,
      minApiVersion: MIN_WORD_API_VERSION,
      supportedRequirementSet: `WordApi ${MIN_WORD_API_VERSION}`,
    };
  }

  /**
   * Initialize the adapter.
   *
   * Must be called after Office.js is ready. Validates that we're running
   * in Word and that the required API version is supported.
   *
   * @throws When not running in Word or required API is not available
   */
  async initialize(): Promise<void> {
    return new Promise((resolve, reject) => {
      Office.onReady(info => {
        if (info.host !== Office.HostType.Word) {
          reject(this.createError('INVALID_HOST', `Expected Word host but running in: ${info.host ?? 'unknown'}`));
          return;
        }

        // Check for minimum required API version
        if (!this.checkRequirementSet('WordApi', MIN_WORD_API_VERSION)) {
          reject(
            this.createError(
              'API_NOT_AVAILABLE',
              `WordApi ${MIN_WORD_API_VERSION} or higher is required. ` + 'Please update your Office client.'
            )
          );
          return;
        }

        this._initialized = true;
        resolve();
      });
    });
  }

  /**
   * Check if the adapter has been initialized.
   * @returns True if initialize() has been called successfully
   */
  isInitialized(): boolean {
    return this._initialized;
  }

  /**
   * Insert a link into the document at the current cursor position.
   *
   * Uses Word's range.insertHtml to insert a hyperlink at the current
   * selection point.
   *
   * @param url - The URL to insert
   * @param displayText - Optional display text for the link
   * @returns Promise resolving to the result of the insertion
   */
  async insertLink(url: string, displayText?: string): Promise<InsertLinkResult> {
    this.ensureInitialized();

    try {
      await Word.run(async context => {
        const selection = context.document.getSelection();

        // Create HTML anchor element
        const linkText = displayText ?? url;
        const html = `<a href="${this.escapeHtml(url)}">${this.escapeHtml(linkText)}</a>`;

        // Insert HTML at selection
        selection.insertHtml(html, Word.InsertLocation.replace);
        await context.sync();
      });

      return { success: true };
    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : 'Unknown error inserting link';
      return {
        success: false,
        errorMessage,
      };
    }
  }

  /**
   * Attach a file to the current item.
   *
   * Not supported for Word documents. Always returns an error result.
   *
   * @param _content - Ignored
   * @param _fileName - Ignored
   * @param _contentType - Ignored
   * @returns Promise resolving to an error result
   */
  async attachFile(_content: string, _fileName: string, _contentType: string): Promise<AttachFileResult> {
    return {
      success: false,
      errorMessage: 'Attaching files is not supported in Word. This feature is only available in Outlook compose mode.',
    };
  }

  /**
   * Open a new-message compose window. Not supported for Word documents.
   *
   * `Office.context.mailbox` does not exist in Word — always returns a defined error result (never
   * throws), mirroring {@link attachFile}'s convention for an Outlook-only capability called on the
   * wrong host. Callers should already be gated on {@link HostCapabilities.canComposeEmail}, which
   * is always `false` here, so this is a defensive fallback rather than the expected call path.
   *
   * @param _content - Ignored
   * @returns Promise resolving to an error result
   */
  async composeNewEmail(_content: EmailComposeContent): Promise<ComposeEmailResult> {
    return {
      success: false,
      errorMessage: 'Composing email is not supported in Word. This feature is only available in Outlook.',
    };
  }

  // ========================
  // Private Helper Methods
  // ========================

  /**
   * Ensure the adapter has been initialized.
   * @throws When adapter is not initialized
   */
  private ensureInitialized(): void {
    if (!this._initialized) {
      throw this.createError('NOT_INITIALIZED', 'WordAdapter has not been initialized. Call initialize() first.');
    }
  }

  /**
   * Check if a requirement set is supported.
   *
   * `version` is optional (task 051 / FR-02 client half): an UNVERSIONED Common set — e.g.
   * `CustomXmlParts` (019 §6 condition 4) — is correctly checked with the bare one-argument call;
   * omitting `version` here, rather than adding a second helper, follows root CLAUDE.md §11.
   *
   * @param set - The requirement set name (e.g., 'WordApi')
   * @param version - The minimum version required. Omit for an unversioned Common requirement set.
   * @returns True if the requirement set is supported
   */
  private checkRequirementSet(set: string, version?: string): boolean {
    try {
      return version === undefined
        ? Office.context.requirements.isSetSupported(set)
        : Office.context.requirements.isSetSupported(set, version);
    } catch {
      return false;
    }
  }

  /**
   * Create a HostAdapterError.
   * @param code - The error code
   * @param message - The error message
   * @returns The error object
   */
  private createError(code: HostAdapterErrorCode, message: string): HostAdapterError {
    return { code, message };
  }

  /**
   * Escape HTML special characters to prevent XSS.
   * @param text - The text to escape
   * @returns The escaped text
   */
  private escapeHtml(text: string): string {
    const htmlEntities: Record<string, string> = {
      '&': '&amp;',
      '<': '&lt;',
      '>': '&gt;',
      '"': '&quot;',
      "'": '&#39;',
    };
    return text.replace(/[&<>"']/g, char => htmlEntities[char] || char);
  }

  /**
   * Generate a simple hash string for creating unique IDs.
   * @param input - The input string to hash
   * @returns A hexadecimal hash string
   */
  private hashString(input: string): string {
    let hash = 0;
    for (let i = 0; i < input.length; i++) {
      const char = input.charCodeAt(i);
      hash = (hash << 5) - hash + char;
      hash = hash & hash; // Convert to 32-bit integer
    }
    return Math.abs(hash).toString(16);
  }
}

/**
 * Default export for convenience.
 */
export default WordAdapter;

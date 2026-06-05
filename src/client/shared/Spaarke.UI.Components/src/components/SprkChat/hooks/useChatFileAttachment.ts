/**
 * useChatFileAttachment - Multi-file chat attachment hook (FR-07)
 *
 * Drives the Assistant `+` button multi-file attach UX in SprkChat. Validates
 * count / size / MIME and performs client-side text extraction via lazy-loaded
 * `pdfjs-dist` (PDF) and `mammoth` (DOCX). Plain text / Markdown go through the
 * browser's native `File.text()`.
 *
 * Design contract (consumed by tasks 025 + 026):
 *   - `files`         — array of chips for the toolbar strip (status: extracting/ready/error)
 *   - `attachments`   — derived array of ready `{ filename, contentType, textContent }`
 *                       entries; this is what gets wired into the outbound chat-send payload
 *   - `errors`        — rejection list with stable `id` + `reason` (UI-actionable)
 *   - `addFiles`      — validate + extract incoming FileList / File[]
 *   - `removeFile`    — splice by index (chip + attachment)
 *   - `clearAll`      — empty both arrays (used post-send)
 *
 * Constraints (binding):
 *   - ADR-012: lives in `@spaarke/ui-components`, context-agnostic. NO imports
 *     from SpaarkeAi / LegalWorkspace. Telemetry is provided via an optional
 *     injected `onExtractionError` callback (consumers pass the
 *     `logTelemetryError`-bound function).
 *   - ADR-022: React hooks only (useState + useCallback + useRef). No
 *     React 18/19-only APIs (useTransition, useDeferredValue) so the hook
 *     remains PCF-safe per ADR-012.
 *   - NFR-12: `pdfjs-dist` and `mammoth` MUST be dynamic `import()`'d inside
 *     `addFiles`, NEVER at module top-level. Each lib is loaded at most once
 *     per hook lifetime and memoized in a ref.
 *   - NFR-04 / FR-07 validation: max 5 files, max 25 MB per file, allowlist of
 *     4 MIME types, PDF max 200 pages. (R4 task 050 / A-4: raised 10 → 25 MB
 *     to align with DocumentUploadWizard + OfficeService standards. See
 *     `docs/standards/CHAT-ATTACHMENT-POLICY.md` for rationale and upgrade path.)
 *   - FR-24 / OC-09: extraction failures emit an `Attachment.ExtractionFailure`
 *     error event with `mimeType`, `sizeBytes`, and `errorMessage`. No
 *     happy-path events.
 *   - OC-02: extracted text is in-memory only — the hook MUST NOT create
 *     Dataverse Document entities or call any SPE/Dataverse endpoint.
 *
 * @see ADR-012 - Shared component library
 * @see ADR-022 - React hook patterns
 * @see Task 024 (this file), Task 025 (consumer toolbar), Task 026 (payload wiring)
 */

import { useCallback, useRef, useState } from 'react';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * The shape consumed by the outbound chat-send payload (task 026).
 * Mirrors the BFF ChatEndpoints attachments contract verified in spike 001.
 */
export interface ChatAttachment {
  filename: string;
  contentType: string;
  textContent: string;
  /**
   * Original File reference, retained for binary-upload paths (R5 task 036 —
   * POST /documents requires multipart binary, not extracted text).
   *
   * OPTIONAL + ADDITIVE: existing consumers that only read filename /
   * contentType / textContent are unaffected. Hosts implementing binary
   * promotion (e.g. ConversationPane's `executeSummarizeIntent`) should
   * prefer this field over reconstructing a synthetic File from textContent
   * because PDF/DOCX bytes do NOT round-trip through extracted text.
   */
  file?: File;
}

/**
 * A chip rendered in the SprkChat toolbar strip while attachments are being
 * extracted / are ready / failed. `id` is a stable identifier suitable for
 * React `key` props.
 */
export interface AttachmentChip {
  id: string;
  filename: string;
  sizeBytes: number;
  mimeType: string;
  status: AttachmentChipStatus;
  textContent?: string;
  error?: string;
  /**
   * Original File reference, retained for binary-upload paths (R5 task 036 —
   * POST /documents requires multipart binary, not extracted text).
   *
   * Populated during `addFiles` when the chip is created. The File reference
   * remains readable even after `pdfjs` / `mammoth` consume the
   * `arrayBuffer()` — browser File objects are reference-counted Blobs and
   * the underlying bytes stay available for re-reads (e.g. multipart upload).
   */
  file?: File;
}

export type AttachmentChipStatus = 'extracting' | 'ready' | 'error';

/**
 * A validation / extraction rejection. The `reason` discriminator makes it
 * trivial for the UI to map to a localized message (task 025).
 */
export interface AttachmentError {
  id: string;
  filename: string;
  reason: AttachmentErrorReason;
  message: string;
}

export type AttachmentErrorReason =
  | 'too-many'
  | 'too-large'
  | 'unsupported-mime'
  | 'extraction-failed'
  | 'pdf-too-many-pages';

/**
 * Optional callback for FR-24 telemetry. Consumers (e.g., SprkChat in
 * SpaarkeAi) pass `(filename, mimeType, sizeBytes, error) => logTelemetryError(
 * TELEMETRY_FILE_EXTRACTION_FAILURE, { filename, mimeType, sizeBytes,
 * errorMessage: error.message })`. The hook itself stays context-agnostic.
 */
export type AttachmentExtractionErrorCallback = (
  filename: string,
  mimeType: string,
  sizeBytes: number,
  error: Error
) => void;

export interface UseChatFileAttachmentOptions {
  /**
   * Optional FR-24 telemetry callback invoked when extraction throws or
   * exceeds the PDF page cap. The hook calls it exactly once per failure.
   * If omitted, failures are still surfaced via the `errors` array but no
   * telemetry is emitted.
   */
  onExtractionError?: AttachmentExtractionErrorCallback;
}

export interface IUseChatFileAttachmentResult {
  /** Chips for the toolbar strip — all states (extracting / ready / error). */
  files: AttachmentChip[];
  /** Derived: ready chips mapped to outbound payload shape (task 026). */
  attachments: ChatAttachment[];
  /** Rejected files + extraction failures. Each entry is UI-actionable. */
  errors: AttachmentError[];
  /** Validate + extract incoming files. Async because extraction is async. */
  addFiles: (files: FileList | File[]) => Promise<void>;
  /** Splice both chip and corresponding attachment by index. */
  removeFile: (index: number) => void;
  /** Empty all state (used by task 026 after successful send). */
  clearAll: () => void;
}

// ---------------------------------------------------------------------------
// Constants — NFR-04 limits
// ---------------------------------------------------------------------------

/** FR-07: max 5 files per chat message. */
export const MAX_ATTACHMENTS = 5;

/**
 * NFR-04: max 25 MB per file.
 *
 * Raised from 10 MB to 25 MB in R4 task 050 (A-4) to align with
 * `DocumentUploadWizard` and `OfficeService` (also 25 MB) — 25 MB is the
 * established Spaarke binary-attachment standard. See
 * `docs/standards/CHAT-ATTACHMENT-POLICY.md` for rationale, MIME allow-list,
 * total-text cap policy, PDF page cap, and upgrade path.
 *
 * Note: server-side `MaxAttachmentTextCharsPerFile` (2.5M chars) and
 * `MaxAttachmentTextCharsTotal` (5M chars) operate on EXTRACTED TEXT, not the
 * raw binary — a 25 MB PDF typically extracts to <1M chars. They are not
 * scaled with this change; see the policy doc for the rationale.
 */
export const MAX_FILE_BYTES = 25 * 1024 * 1024;

/** NFR-04: PDF page cap (additional constraint beyond size). */
export const MAX_PDF_PAGES = 200;

/** NFR-04: MIME allowlist. */
export const ALLOWED_MIME_TYPES: ReadonlySet<string> = new Set([
  'text/plain',
  'text/markdown',
  'application/pdf',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
]);

const MIME_PDF = 'application/pdf';
const MIME_DOCX = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';
const MIME_TEXT = 'text/plain';
const MIME_MARKDOWN = 'text/markdown';

// ---------------------------------------------------------------------------
// Lazy-loaded library types (minimal — we only touch what we need).
// Keeping these inline avoids pulling in @types/pdfjs-dist statically.
// ---------------------------------------------------------------------------

interface PdfTextItem {
  str: string;
}

interface PdfTextContent {
  items: Array<PdfTextItem | { str?: string }>;
}

interface PdfPage {
  getTextContent: () => Promise<PdfTextContent>;
}

interface PdfDocument {
  numPages: number;
  getPage: (pageNumber: number) => Promise<PdfPage>;
}

interface PdfJsModule {
  getDocument: (params: { data: ArrayBuffer } | { url: string } | ArrayBuffer | Uint8Array) => {
    promise: Promise<PdfDocument>;
  };
  GlobalWorkerOptions?: { workerSrc?: string };
}

interface MammothModule {
  extractRawText: (input: { arrayBuffer: ArrayBuffer }) => Promise<{ value: string; messages?: unknown[] }>;
}

// ---------------------------------------------------------------------------
// ID helper — stable, unique-per-hook-instance.
// Uses a closure counter rather than crypto.randomUUID() to stay
// runtime-agnostic (jsdom + browser + ssr-friendly).
// ---------------------------------------------------------------------------

function createIdFactory(): () => string {
  let counter = 0;
  return () => {
    counter += 1;
    return `att-${Date.now().toString(36)}-${counter}`;
  };
}

// ---------------------------------------------------------------------------
// Extraction helpers — kept module-scope (not inside the hook) so they remain
// pure + testable. Each receives an `ensure*` lazy-loader so we don't double-
// load the libs across multiple addFiles invocations.
// ---------------------------------------------------------------------------

async function extractText(file: File): Promise<string> {
  // Browser File.text() is React 19 / modern-browser safe and avoids the
  // older FileReader event-based API.
  return file.text();
}

async function extractPdf(
  file: File,
  ensurePdfJs: () => Promise<PdfJsModule>
): Promise<{ text: string; numPages: number }> {
  const pdfjs = await ensurePdfJs();
  const buffer = await file.arrayBuffer();

  // pdfjs `getDocument` accepts the ArrayBuffer wrapped in { data: ... } or
  // raw — the wrapped form is the documented API in v3/v4/v5.
  const loadingTask = pdfjs.getDocument({ data: buffer });
  const pdf = await loadingTask.promise;

  if (pdf.numPages > MAX_PDF_PAGES) {
    // Surface the cap as a structured exception so the caller can map it to
    // the `pdf-too-many-pages` reason rather than the generic
    // `extraction-failed` bucket.
    const error = new Error(`PDF has ${pdf.numPages} pages; max is ${MAX_PDF_PAGES}`);
    (error as Error & { code?: string }).code = 'pdf-too-many-pages';
    throw error;
  }

  const pageTexts: string[] = [];
  for (let pageNumber = 1; pageNumber <= pdf.numPages; pageNumber += 1) {
    const page = await pdf.getPage(pageNumber);
    const content = await page.getTextContent();
    const pageText = content.items.map(item => (item as PdfTextItem).str ?? '').join(' ');
    pageTexts.push(pageText);
  }
  return { text: pageTexts.join('\n\n'), numPages: pdf.numPages };
}

async function extractDocx(file: File, ensureMammoth: () => Promise<MammothModule>): Promise<string> {
  const mammoth = await ensureMammoth();
  const buffer = await file.arrayBuffer();
  const result = await mammoth.extractRawText({ arrayBuffer: buffer });
  return result.value ?? '';
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * useChatFileAttachment — multi-file attachment lifecycle for SprkChat.
 *
 * @example
 * ```tsx
 * // Inside SpaarkeAi's SprkChat host
 * const onExtractionError = useCallback(
 *   (filename, mimeType, sizeBytes, error) => {
 *     logTelemetryError(TELEMETRY_FILE_EXTRACTION_FAILURE, {
 *       filename, mimeType, sizeBytes, errorMessage: error.message,
 *     });
 *   },
 *   []
 * );
 *
 * const {
 *   files, attachments, errors,
 *   addFiles, removeFile, clearAll,
 * } = useChatFileAttachment({ onExtractionError });
 * ```
 */
export function useChatFileAttachment(options: UseChatFileAttachmentOptions = {}): IUseChatFileAttachmentResult {
  const { onExtractionError } = options;

  const [files, setFiles] = useState<AttachmentChip[]>([]);
  const [errors, setErrors] = useState<AttachmentError[]>([]);

  // Lazy-loaded library cache. Refs (NOT state) because (a) we don't want
  // re-renders when the lib loads, (b) loading is per-hook-instance side
  // effect, not derived data.
  const pdfJsRef = useRef<Promise<PdfJsModule> | null>(null);
  const mammothRef = useRef<Promise<MammothModule> | null>(null);

  // Stable id generator per hook instance.
  const idFactoryRef = useRef<() => string>(createIdFactory());

  const ensurePdfJs = useCallback((): Promise<PdfJsModule> => {
    if (!pdfJsRef.current) {
      // NFR-12: this is the load-bearing dynamic import. MUST NOT become a
      // static `import` at the top of the file. Webpack/Vite emit a separate
      // chunk for `pdfjs-dist`, which is what task 061 / the bundle audit
      // verifies.
      pdfJsRef.current = import('pdfjs-dist').then(mod => {
        // pdfjs-dist exports a `GlobalWorkerOptions` object in modern
        // versions; if absent the worker is best-effort and may fall back to
        // a same-thread mode in test environments. We leave the workerSrc
        // unset here — consumers that need real worker isolation should set
        // it during their bootstrap rather than coupling the hook to a
        // particular bundler.
        return mod as unknown as PdfJsModule;
      });
    }
    return pdfJsRef.current;
  }, []);

  const ensureMammoth = useCallback((): Promise<MammothModule> => {
    if (!mammothRef.current) {
      // Lazy import — same NFR-12 rationale as pdfjs above.
      mammothRef.current = import('mammoth').then(mod => {
        // mammoth browser build exports default + extractRawText. Both shapes
        // are observable in practice depending on the bundler interop mode.
        const cast = mod as unknown as MammothModule & { default?: MammothModule };
        return cast.default ?? cast;
      });
    }
    return mammothRef.current;
  }, []);

  const addFiles = useCallback(
    async (incoming: FileList | File[]): Promise<void> => {
      const inputArray: File[] = Array.from(incoming as Iterable<File>);
      if (inputArray.length === 0) {
        return;
      }

      // Snapshot the current state once so all validation gates within this
      // addFiles call agree on the count baseline. State updates from this
      // call are batched at the end.
      const newChips: AttachmentChip[] = [];
      const newErrors: AttachmentError[] = [];
      // Track how many chips have already been accepted (existing + newly
      // queued in this call) so the `too-many` gate is honored within the
      // same invocation, not just across invocations.
      let acceptedCount = files.length;

      // ---- Gate A: validation ----
      const acceptedForExtraction: AttachmentChip[] = [];

      for (const file of inputArray) {
        // (a) too-many — global cap of 5 across the message
        if (acceptedCount >= MAX_ATTACHMENTS) {
          newErrors.push({
            id: idFactoryRef.current(),
            filename: file.name,
            reason: 'too-many',
            message: `Maximum ${MAX_ATTACHMENTS} files per message.`,
          });
          continue;
        }

        // (b) too-large — 25 MB per file (raised from 10 MB in R4 A-4)
        if (file.size > MAX_FILE_BYTES) {
          newErrors.push({
            id: idFactoryRef.current(),
            filename: file.name,
            reason: 'too-large',
            message: `File exceeds ${(MAX_FILE_BYTES / (1024 * 1024)).toFixed(0)} MB limit.`,
          });
          continue;
        }

        // (c) unsupported MIME
        const mimeType = file.type;
        if (!ALLOWED_MIME_TYPES.has(mimeType)) {
          newErrors.push({
            id: idFactoryRef.current(),
            filename: file.name,
            reason: 'unsupported-mime',
            message: `Unsupported file type: ${mimeType || 'unknown'}.`,
          });
          continue;
        }

        // Accept — create chip in `extracting` state.
        //
        // R5 task 036: retain the original File on the chip so downstream
        // binary-upload paths (e.g. ConversationPane `executeSummarizeIntent`
        // → POST /documents) can re-read the bytes. Extraction below also
        // reads `file.arrayBuffer()` / `file.text()` but those calls do NOT
        // invalidate the File — browser File objects are reference-counted
        // Blobs and remain readable across calls.
        const chip: AttachmentChip = {
          id: idFactoryRef.current(),
          filename: file.name,
          sizeBytes: file.size,
          mimeType,
          status: 'extracting',
          file,
        };
        newChips.push(chip);
        acceptedForExtraction.push(chip);
        acceptedCount += 1;
      }

      // Optimistic insert: push the new chips + errors immediately so the
      // toolbar strip can render `extracting` chips. Extraction results will
      // patch them in the next setFiles below.
      if (newChips.length > 0) {
        setFiles(prev => [...prev, ...newChips]);
      }
      if (newErrors.length > 0) {
        setErrors(prev => [...prev, ...newErrors]);
      }

      // ---- Gate B: extraction ----
      // Pair each accepted chip back to its source File via index alignment.
      // We rebuild the alignment by filtering the same input array against
      // the validation gates above — order is preserved.
      const filesForExtraction: File[] = [];
      let chipIndex = 0;
      let acceptedRemaining = acceptedForExtraction.length;
      for (const file of inputArray) {
        if (acceptedRemaining === 0) {
          break;
        }
        const acceptedChip = acceptedForExtraction[chipIndex];
        if (
          acceptedChip &&
          acceptedChip.filename === file.name &&
          acceptedChip.sizeBytes === file.size &&
          acceptedChip.mimeType === file.type
        ) {
          filesForExtraction.push(file);
          chipIndex += 1;
          acceptedRemaining -= 1;
        }
      }

      // Extract sequentially-but-async; sequential is fine because per-file
      // extraction is short and avoids spiking memory on PDF/DOCX parsing.
      const patches: Array<{ id: string; patch: Partial<AttachmentChip> }> = [];
      const extractionErrors: AttachmentError[] = [];

      for (let i = 0; i < acceptedForExtraction.length; i += 1) {
        const chip = acceptedForExtraction[i];
        const file = filesForExtraction[i];
        if (!file) {
          // Defensive: should not happen given the alignment above.
          continue;
        }

        try {
          let textContent = '';
          if (chip.mimeType === MIME_PDF) {
            const result = await extractPdf(file, ensurePdfJs);
            textContent = result.text;
          } else if (chip.mimeType === MIME_DOCX) {
            textContent = await extractDocx(file, ensureMammoth);
          } else if (chip.mimeType === MIME_TEXT || chip.mimeType === MIME_MARKDOWN) {
            textContent = await extractText(file);
          } else {
            // Should not reach here — MIME gate above filtered.
            throw new Error(`Unsupported MIME type for extraction: ${chip.mimeType}`);
          }

          patches.push({
            id: chip.id,
            patch: { status: 'ready', textContent },
          });
        } catch (rawError: unknown) {
          const error = rawError instanceof Error ? rawError : new Error(String(rawError));
          const codedReason = (error as Error & { code?: string }).code;
          const reason: AttachmentErrorReason =
            codedReason === 'pdf-too-many-pages' ? 'pdf-too-many-pages' : 'extraction-failed';
          const message = reason === 'pdf-too-many-pages' ? error.message : `Failed to extract text: ${error.message}`;

          patches.push({
            id: chip.id,
            patch: { status: 'error', error: message },
          });
          extractionErrors.push({
            id: idFactoryRef.current(),
            filename: chip.filename,
            reason,
            message,
          });

          // FR-24 / OC-09 telemetry — emit exactly once per failed file.
          if (onExtractionError) {
            try {
              onExtractionError(chip.filename, chip.mimeType, chip.sizeBytes, error);
            } catch {
              // Never propagate telemetry failures.
            }
          }
        }
      }

      // Apply all extraction patches in a single state update.
      if (patches.length > 0) {
        setFiles(prev =>
          prev.map(chip => {
            const patch = patches.find(p => p.id === chip.id);
            return patch ? { ...chip, ...patch.patch } : chip;
          })
        );
      }
      if (extractionErrors.length > 0) {
        setErrors(prev => [...prev, ...extractionErrors]);
      }
    },
    [files.length, ensurePdfJs, ensureMammoth, onExtractionError]
  );

  const removeFile = useCallback((index: number): void => {
    setFiles(prev => {
      if (index < 0 || index >= prev.length) {
        return prev;
      }
      const next = prev.slice();
      next.splice(index, 1);
      return next;
    });
  }, []);

  const clearAll = useCallback((): void => {
    setFiles([]);
    setErrors([]);
  }, []);

  // Derive `attachments` from chips with `status === 'ready'`. Cheap O(n)
  // filter; the alternative (separate state) would invite drift.
  //
  // R5 task 036: forward the retained File reference so downstream binary-
  // upload paths (e.g. `executeSummarizeIntent` → POST /documents) can post
  // the original bytes rather than a synthetic File reconstructed from
  // extracted text (which fails for PDF/DOCX BFF Document Intelligence).
  const attachments: ChatAttachment[] = files
    .filter(chip => chip.status === 'ready' && chip.textContent !== undefined)
    .map(chip => ({
      filename: chip.filename,
      contentType: chip.mimeType,
      textContent: chip.textContent ?? '',
      file: chip.file,
    }));

  return {
    files,
    attachments,
    errors,
    addFiles,
    removeFile,
    clearAll,
  };
}

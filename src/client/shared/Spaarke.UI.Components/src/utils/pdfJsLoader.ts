/**
 * pdfJsLoader — lazy `pdfjs-dist` loader for components that render PDFs.
 *
 * NFR-12: `pdfjs-dist` MUST stay a dynamic `import()` so consumers that never render a PDF do not load it
 * (webpack/Vite emit a separate chunk). Do not turn the `import('pdfjs-dist')` below into a static import.
 *
 * Worker source: pdfjs-dist v4+ throws "No GlobalWorkerOptions.workerSrc specified" unless it is set. A host
 * in a CSP-restricted context (for example a Dataverse code page that blocks the CDN) should preset
 * `GlobalWorkerOptions.workerSrc` to a same-origin worker before the first load; this loader only fills it in
 * when it is empty, pointing at a versioned jsdelivr URL that matches the installed pdfjs-dist.
 *
 * NOTE (ebilling-r1 task 108): `components/SprkChat/hooks/useChatFileAttachment.ts` carries an equivalent
 * loader inline and is intentionally NOT switched to this one. Its test suite asserts the literal text
 * `import('pdfjs-dist')` in that file's source (NFR-12 source-level guard), so extracting would change the
 * outcome of an existing test. Behaviour here is deliberately identical to that loader; consolidate both when
 * that guard is updated to follow the loader.
 *
 * Types are declared inline (the minimal surface we touch) rather than imported from pdfjs-dist, which keeps
 * the module type-free of the library at build time, as in the SprkChat hook.
 *
 * @see ADR-012 - Shared component library boundary
 */

// ---------------------------------------------------------------------------
// Minimal pdfjs surface
// ---------------------------------------------------------------------------

export interface IPdfJsViewport {
  width: number;
  height: number;
}

export interface IPdfJsRenderTask {
  promise: Promise<unknown>;
  cancel: () => void;
}

export interface IPdfJsPage {
  getViewport: (params: { scale: number }) => IPdfJsViewport;
  render: (params: { canvas: HTMLCanvasElement; viewport: IPdfJsViewport }) => IPdfJsRenderTask;
  cleanup?: () => void;
}

export interface IPdfJsDocument {
  numPages: number;
  getPage: (pageNumber: number) => Promise<IPdfJsPage>;
}

export interface IPdfJsLoadingTask {
  promise: Promise<IPdfJsDocument>;
  /** Aborts requests and releases the worker. The document proxy has no destroy() of its own in pdfjs 6. */
  destroy: () => Promise<void>;
}

export interface IPdfJsModule {
  getDocument: (params: { data: Uint8Array }) => IPdfJsLoadingTask;
  GlobalWorkerOptions?: { workerSrc?: string };
  version?: string;
}

/** Fallback when pdfjs-dist does not report its own version. Keep in step with package.json. */
const FALLBACK_PDFJS_VERSION = '6.2.108';

let pdfJsPromise: Promise<IPdfJsModule> | null = null;

/**
 * Load pdfjs-dist once and make sure a worker source is set. A failed load is not cached, so a later call retries.
 */
export function loadPdfJs(): Promise<IPdfJsModule> {
  if (!pdfJsPromise) {
    pdfJsPromise = import('pdfjs-dist').then(mod => {
      const runtime = mod as unknown as IPdfJsModule;
      if (runtime.GlobalWorkerOptions && !runtime.GlobalWorkerOptions.workerSrc) {
        const version = runtime.version ?? FALLBACK_PDFJS_VERSION;
        runtime.GlobalWorkerOptions.workerSrc = `https://cdn.jsdelivr.net/npm/pdfjs-dist@${version}/build/pdf.worker.min.mjs`;
      }
      return runtime;
    });
    pdfJsPromise.catch(() => {
      pdfJsPromise = null;
    });
  }
  return pdfJsPromise;
}

/** Test seam: forget the cached module so a test can re-run the load. */
export function resetPdfJsLoaderForTests(): void {
  pdfJsPromise = null;
}

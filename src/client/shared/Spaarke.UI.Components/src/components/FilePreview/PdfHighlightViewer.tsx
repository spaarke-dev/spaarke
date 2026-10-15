/**
 * PdfHighlightViewer — renders the pages of a host-supplied PDF and highlights an extracted line and field.
 *
 * Why this exists: `RichFilePreview` shows an SPE preview URL in a cross-origin iframe, so it has no page
 * geometry and cannot carry an overlay. This viewer renders real PDF pages (lazy-loaded `pdfjs-dist`) to
 * canvases it owns, so a review screen can show where a value was read (ebilling-r1 FR-31, prototype F-4).
 *
 * Highlight contract: every box is `{x, y, w, h}` normalised 0-1 from the TOP-LEFT of its page
 * (the extraction bounding-box shape; an optional `page` on a box is ignored, `IPdfHighlight.page` places it).
 *   - row box   — outlined and tinted with a multiply blend, so the printed text stays legible;
 *   - field box — outlined, with a label tag;
 *   - on a highlight change the scroller moves so the row sits 30 % down the viewport.
 * Out-of-range coordinates are clamped to the page; a highlight on a page the document does not have is dropped.
 *
 * Dark mode: the surrounding chrome follows the host theme; the pages (and their overlays) render inside a nested
 * light `FluentProvider` so a page stays white as a real PDF page does (F-4). The provider is styled with an inline
 * transparent background, never a className (F-10).
 *
 * Constraints (binding):
 *   - ADR-012: context-agnostic. The host supplies the bytes (`data`) or a loader (`loadData`); no fetch, Xrm or
 *     auth inside this component.
 *   - ADR-021: Fluent v9 semantic tokens only; no hard-coded colours.
 *   - NFR-12: `pdfjs-dist` is only reached through `utils/pdfJsLoader` (dynamic import).
 *   - NFR-02: only the visible pages and their neighbours are rendered to canvas.
 *   - React-16-safe: classic hooks only (no React 18+ APIs).
 *   - Accessibility: the viewer is a labelled region; the highlighted label is announced through a polite live
 *     region; overlays are aria-hidden and pointer-events none.
 *
 * @see ADR-012, ADR-021
 * @see projects/ebilling-r1/tasks/108-bounding-box-document-viewer.poml
 */

import * as React from 'react';
import { Button, Caption1, FluentProvider, Spinner, Text, makeStyles, mergeClasses, tokens, webLightTheme } from '@fluentui/react-components';
import { DocumentPdf16Regular } from '@fluentui/react-icons';
import { loadPdfJs, type IPdfJsDocument, type IPdfJsLoadingTask } from '../../utils/pdfJsLoader';
import { boxToCss, clampBox, computeScrollTop, isValidPage, pagesToRender, type IPdfNormalisedBox } from './pdfHighlightGeometry';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/** PDF bytes supplied by the host. */
export type PdfBytes = ArrayBuffer | Uint8Array;

/** What to highlight. Boxes are normalised 0-1 from the top-left of `page` (1-based). */
export interface IPdfHighlight {
  /** 1-based page the boxes sit on. */
  page: number;
  /** The whole line's region on the page. */
  row?: IPdfNormalisedBox | null;
  /** The single value's region; drawn with `label`. */
  field?: IPdfNormalisedBox | null;
  /** Field name shown on the tag and announced to screen readers. */
  label?: string;
}

export interface IPdfHighlightViewerProps {
  /** The PDF bytes. Takes precedence over `loadData`. The viewer copies them, so the host keeps ownership. */
  data?: PdfBytes | null;
  /**
   * Returns the PDF bytes. Memoise it (useCallback): a new function identity starts a new load.
   * Retry calls it again.
   */
  loadData?: () => Promise<PdfBytes>;
  /** Name shown in the toolbar line. */
  documentName?: string;
  /** The line/field to box and scroll to. Null or omitted: no overlay, the scroller returns to the top. */
  highlight?: IPdfHighlight | null;
  /** Optional cap on page width in CSS px (pages are centred). Default: fill the container. */
  maxPageWidth?: number;
  /** Accessible name of the viewer region. Default "Document viewer". */
  ariaLabel?: string;
  /** Called once the document has been parsed. */
  onLoad?: (info: { pageCount: number }) => void;
  /** Called when loading or parsing fails (the error state is shown regardless). */
  onError?: (error: Error) => void;
  className?: string;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Placeholder page shape (US Letter, height / width) until a page reports its real size. */
const DEFAULT_PAGE_ASPECT = 11 / 8.5;
/** Cap the canvas backing store at 2x so a 4K screen does not render 4x the pixels. */
const MAX_DEVICE_PIXEL_RATIO = 2;
/** Neighbour pages kept rendered either side of a visible page (NFR-02). */
const RENDER_NEIGHBOURS = 1;

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    minHeight: 0,
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  bar: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    flexShrink: 0,
  },
  barName: { flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' },
  meta: { color: tokens.colorNeutralForeground3, flexShrink: 0 },
  srOnly: {
    position: 'absolute',
    width: '1px',
    height: '1px',
    overflow: 'hidden',
    clipPath: 'inset(50%)',
    whiteSpace: 'nowrap',
  },
  scroller: {
    // Positioned so page.offsetTop is measured against the scroller's content (scroll maths).
    position: 'relative',
    flex: 1,
    minHeight: 0,
    overflowY: 'auto',
    padding: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground4,
    display: 'flex',
    flexDirection: 'column',
  },
  centerContent: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    textAlign: 'center',
  },
  subtle: { color: tokens.colorNeutralForeground3 },
  page: {
    position: 'relative',
    width: '100%',
    // A real PDF page is white in both themes; the nested light provider makes these tokens light.
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    boxShadow: tokens.shadow4,
    overflow: 'hidden',
    flexShrink: 0,
  },
  canvas: { display: 'block', width: '100%', height: '100%' },
  pageError: {
    position: 'absolute',
    inset: 0,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    color: tokens.colorNeutralForeground3,
  },
  rowBox: {
    position: 'absolute',
    outline: `${tokens.strokeWidthThick} solid ${tokens.colorBrandStroke1}`,
    borderRadius: tokens.borderRadiusSmall,
    pointerEvents: 'none',
  },
  // multiply keeps the printed text fully legible under the tint
  rowFill: {
    position: 'absolute',
    inset: 0,
    backgroundColor: tokens.colorBrandBackground2,
    mixBlendMode: 'multiply',
    pointerEvents: 'none',
  },
  fieldBox: {
    position: 'absolute',
    outline: `${tokens.strokeWidthThicker} solid ${tokens.colorPaletteMarigoldBorderActive}`,
    borderRadius: tokens.borderRadiusSmall,
    pointerEvents: 'none',
  },
  fieldTag: {
    position: 'absolute',
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    backgroundColor: tokens.colorPaletteMarigoldBackground3,
    color: tokens.colorNeutralForeground1,
    borderRadius: tokens.borderRadiusSmall,
    whiteSpace: 'nowrap',
    pointerEvents: 'none',
  },
  tagAbove: { transform: 'translateY(-100%)' },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** A private copy of the host's bytes. pdfjs transfers the buffer it is given to its worker, which would detach the host's copy and break Retry. */
function toUint8Copy(bytes: PdfBytes): Uint8Array {
  if (ArrayBuffer.isView(bytes)) {
    return new Uint8Array(bytes.buffer, bytes.byteOffset, bytes.byteLength).slice();
  }
  return new Uint8Array(bytes as ArrayBuffer).slice();
}

function toError(e: unknown): Error {
  return e instanceof Error ? e : new Error(typeof e === 'string' ? e : 'Failed to load the document');
}

function isRenderCancelled(e: unknown): boolean {
  return typeof e === 'object' && e !== null && (e as { name?: string }).name === 'RenderingCancelledException';
}

function prefersReducedMotion(): boolean {
  try {
    return typeof window !== 'undefined' && typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  } catch {
    // matchMedia can throw in unusual embeds; animating is the safe default.
    return false;
  }
}

function scrollScrollerTo(el: HTMLElement, top: number): void {
  const behavior: ScrollBehavior = prefersReducedMotion() ? 'auto' : 'smooth';
  if (typeof el.scrollTo === 'function') {
    el.scrollTo({ top, behavior });
  } else {
    el.scrollTop = top;
  }
}

// ---------------------------------------------------------------------------
// One page: placeholder box, canvas (only inside the render window), overlay
// ---------------------------------------------------------------------------

interface IPdfPageProps {
  doc: IPdfJsDocument;
  pageNumber: number;
  /** Height / width of the page box. */
  aspect: number;
  /** True while the page is inside the render window; false releases the canvas. */
  render: boolean;
  maxPageWidth?: number;
  /** Highlight for this page, or null. */
  highlight: IPdfHighlight | null;
  registerElement: (pageNumber: number, el: HTMLDivElement | null) => void;
  onAspect: (pageNumber: number, aspect: number) => void;
}

const PdfPage: React.FC<IPdfPageProps> = ({ doc, pageNumber, aspect, render, maxPageWidth, highlight, registerElement, onAspect }) => {
  const styles = useStyles();
  const elRef = React.useRef<HTMLDivElement | null>(null);
  const canvasRef = React.useRef<HTMLCanvasElement | null>(null);
  const [cssWidth, setCssWidth] = React.useState(0);
  const [renderFailed, setRenderFailed] = React.useState(false);

  const setEl = React.useCallback(
    (el: HTMLDivElement | null) => {
      elRef.current = el;
      registerElement(pageNumber, el);
    },
    [pageNumber, registerElement]
  );

  // Track the page's rendered width so the canvas is rendered at the size it is shown.
  React.useEffect(() => {
    const el = elRef.current;
    if (!el) return undefined;
    setCssWidth(el.clientWidth);
    if (typeof ResizeObserver === 'undefined') return undefined;
    const ro = new ResizeObserver(entries => {
      const w = entries[0]?.contentRect.width ?? el.clientWidth;
      setCssWidth(Math.round(w));
    });
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  // Render the page into the canvas while it is inside the window.
  React.useEffect(() => {
    if (!render || cssWidth <= 0) return undefined;
    const canvas = canvasRef.current;
    if (!canvas) return undefined;
    const run = { cancelled: false };
    const inFlight: { task: { promise: Promise<unknown>; cancel: () => void } | null } = { task: null };
    setRenderFailed(false);
    (async () => {
      try {
        const page = await doc.getPage(pageNumber);
        if (run.cancelled) return;
        const base = page.getViewport({ scale: 1 });
        if (base.width > 0) onAspect(pageNumber, base.height / base.width);
        const dpr = Math.min(typeof window !== 'undefined' ? window.devicePixelRatio || 1 : 1, MAX_DEVICE_PIXEL_RATIO);
        const viewport = page.getViewport({ scale: (cssWidth / base.width) * dpr });
        canvas.width = Math.floor(viewport.width);
        canvas.height = Math.floor(viewport.height);
        inFlight.task = page.render({ canvas, viewport });
        await inFlight.task.promise;
      } catch (e) {
        if (run.cancelled || isRenderCancelled(e)) return;
        setRenderFailed(true);
      }
    })();
    return () => {
      run.cancelled = true;
      if (inFlight.task) inFlight.task.cancel();
    };
  }, [doc, pageNumber, render, cssWidth, onAspect]);

  const rowCss = highlight ? boxToCss(highlight.row) : null;
  const fieldCss = highlight ? boxToCss(highlight.field) : null;
  const fieldClamped = highlight ? clampBox(highlight.field) : null;
  // A field at the very top of the page has no room for the tag above it: put the tag under the box instead.
  const tagBelow = fieldClamped ? fieldClamped.y < 0.03 : false;
  const tagTop = fieldClamped ? `${(tagBelow ? fieldClamped.y + fieldClamped.h : fieldClamped.y) * 100}%` : '0%';

  return (
    <div
      ref={setEl}
      className={styles.page}
      data-page={pageNumber}
      style={{ aspectRatio: `1 / ${aspect}`, ...(maxPageWidth ? { maxWidth: maxPageWidth, alignSelf: 'center' } : {}) }}
      role="img"
      aria-label={`Page ${pageNumber} of ${doc.numPages}`}
    >
      {render && <canvas ref={canvasRef} className={styles.canvas} aria-hidden />}
      {renderFailed && (
        <div className={styles.pageError}>
          <Caption1>This page could not be rendered.</Caption1>
        </div>
      )}
      {rowCss && (
        <div className={styles.rowBox} style={rowCss} data-testid="pdf-highlight-row" aria-hidden>
          <div className={styles.rowFill} />
        </div>
      )}
      {fieldCss && highlight && (
        <>
          <div className={styles.fieldBox} style={fieldCss} data-testid="pdf-highlight-field" aria-hidden />
          {highlight.label && (
            <div
              className={mergeClasses(styles.fieldTag, !tagBelow && styles.tagAbove)}
              style={{ left: fieldCss.left, top: tagTop }}
              data-testid="pdf-highlight-label"
              aria-hidden
            >
              {highlight.label}
            </div>
          )}
        </>
      )}
    </div>
  );
};

// ---------------------------------------------------------------------------
// Viewer
// ---------------------------------------------------------------------------

type ViewerStatus = 'empty' | 'loading' | 'ready' | 'error';

export const PdfHighlightViewer: React.FC<IPdfHighlightViewerProps> = ({
  data,
  loadData,
  documentName,
  highlight,
  maxPageWidth,
  ariaLabel,
  onLoad,
  onError,
  className,
}) => {
  const styles = useStyles();
  const hasSource = !!data || !!loadData;
  const [status, setStatus] = React.useState<ViewerStatus>(hasSource ? 'loading' : 'empty');
  const [errorMessage, setErrorMessage] = React.useState('');
  const [attempt, setAttempt] = React.useState(0);
  const [doc, setDoc] = React.useState<IPdfJsDocument | null>(null);
  const [aspects, setAspects] = React.useState<Record<number, number | undefined>>({});
  const [visible, setVisible] = React.useState<number[]>([1]);

  const scrollerRef = React.useRef<HTMLDivElement | null>(null);
  const pageEls = React.useRef<Map<number, HTMLDivElement>>(new Map());
  const hadHighlightRef = React.useRef(false);

  // Latest callbacks without re-triggering the load effect.
  const onLoadRef = React.useRef(onLoad);
  const onErrorRef = React.useRef(onError);
  onLoadRef.current = onLoad;
  onErrorRef.current = onError;

  // ---- Load the document ---------------------------------------------------
  React.useEffect(() => {
    if (!data && !loadData) {
      setDoc(null);
      setStatus('empty');
      return undefined;
    }
    const run = { cancelled: false };
    // A function call, so TypeScript does not narrow `run.cancelled` across the awaits below.
    const isCancelled = (): boolean => run.cancelled;
    const holder: { task: IPdfJsLoadingTask | null } = { task: null };
    setStatus('loading');
    setDoc(null);
    setAspects({});
    setVisible([1]);
    (async () => {
      try {
        const bytes = data ?? (await (loadData as () => Promise<PdfBytes>)());
        const pdfjs = await loadPdfJs();
        if (isCancelled()) return;
        holder.task = pdfjs.getDocument({ data: toUint8Copy(bytes) });
        const parsed = await holder.task.promise;
        if (isCancelled()) return;
        if (parsed.numPages < 1) {
          setStatus('empty');
          return;
        }
        setDoc(parsed);
        setStatus('ready');
        if (onLoadRef.current) onLoadRef.current({ pageCount: parsed.numPages });
      } catch (e) {
        if (isCancelled()) return;
        const err = toError(e);
        setErrorMessage(err.message);
        setStatus('error');
        if (onErrorRef.current) onErrorRef.current(err);
      }
    })();
    return () => {
      run.cancelled = true;
      // Releases the document and its worker resources (pdfjs 6 has no destroy() on the document itself).
      if (holder.task) {
        holder.task.destroy().catch(() => undefined);
      }
    };
  }, [data, loadData, attempt]);

  const handleRetry = React.useCallback(() => setAttempt(a => a + 1), []);

  // ---- Page registry + visibility -----------------------------------------
  const registerElement = React.useCallback((pageNumber: number, el: HTMLDivElement | null) => {
    if (el) pageEls.current.set(pageNumber, el);
    else pageEls.current.delete(pageNumber);
  }, []);

  const handleAspect = React.useCallback((pageNumber: number, aspect: number) => {
    setAspects(prev => (prev[pageNumber] === aspect ? prev : { ...prev, [pageNumber]: aspect }));
  }, []);

  const pageCount = doc ? doc.numPages : 0;
  // Without IntersectionObserver there is no way to know what is visible: render every page rather than only the first.
  const canObserve = typeof IntersectionObserver !== 'undefined';

  React.useEffect(() => {
    if (status !== 'ready' || !canObserve) return undefined;
    const inView = new Set<number>();
    const observer = new IntersectionObserver(
      entries => {
        for (const entry of entries) {
          const n = Number((entry.target as HTMLElement).dataset.page);
          if (!Number.isFinite(n)) continue;
          if (entry.isIntersecting) inView.add(n);
          else inView.delete(n);
        }
        const next = Array.from(inView).sort((a, b) => a - b);
        setVisible(prev => (prev.length === next.length && prev.every((v, i) => v === next[i]) ? prev : next));
      },
      // Half a viewport of margin so a page is rendering before it scrolls into view.
      { root: scrollerRef.current, rootMargin: '50% 0px' }
    );
    pageEls.current.forEach(el => observer.observe(el));
    return () => observer.disconnect();
  }, [status, pageCount, canObserve]);

  const activeHighlight = highlight && isValidPage(highlight.page, pageCount) ? highlight : null;
  const highlightPage = activeHighlight ? activeHighlight.page : null;

  const renderWindow = React.useMemo(() => {
    if (!canObserve) return new Set(pagesToRender(Array.from({ length: pageCount }, (_, i) => i + 1), pageCount, 0));
    const seeds = highlightPage ? [...visible, highlightPage] : visible;
    return new Set(pagesToRender(seeds, pageCount, RENDER_NEIGHBOURS));
  }, [visible, highlightPage, pageCount, canObserve]);

  // ---- Scroll to the highlighted row --------------------------------------
  const targetY = activeHighlight ? activeHighlight.row?.y ?? activeHighlight.field?.y ?? 0 : null;
  const knownAspect = highlightPage ? aspects[highlightPage] : undefined;
  React.useEffect(() => {
    const scroller = scrollerRef.current;
    if (status !== 'ready' || !scroller) return;
    if (highlightPage === null || targetY === null) {
      if (hadHighlightRef.current && scroller.scrollTop > 0) scrollScrollerTo(scroller, 0);
      hadHighlightRef.current = false;
      return;
    }
    const pageEl = pageEls.current.get(highlightPage);
    if (!pageEl) return;
    hadHighlightRef.current = true;
    scrollScrollerTo(scroller, computeScrollTop(pageEl.offsetTop, pageEl.offsetHeight, targetY, scroller.clientHeight));
    // knownAspect: re-aim once the highlighted page reports its real size (its height may differ from the placeholder).
  }, [status, highlightPage, targetY, knownAspect]);

  // ---- Render --------------------------------------------------------------
  const defaultAspect = aspects[1] ?? DEFAULT_PAGE_ASPECT;
  const pages = React.useMemo(() => Array.from({ length: pageCount }, (_, i) => i + 1), [pageCount]);

  const announcement = activeHighlight
    ? `Highlighted ${activeHighlight.label ?? 'row'} on page ${activeHighlight.page} of ${pageCount}`
    : '';

  const renderBody = (): React.ReactNode => {
    if (status === 'loading') {
      return (
        <div className={styles.centerContent}>
          <Spinner size="large" label="Loading preview..." labelPosition="below" />
        </div>
      );
    }
    if (status === 'error') {
      return (
        <div className={styles.centerContent} role="alert">
          <Text size={400} weight="semibold">
            Preview not available
          </Text>
          <Text size={200} className={styles.subtle}>
            Unable to load the document. The file may be unsupported or temporarily unavailable.
          </Text>
          {errorMessage && (
            <Caption1 className={styles.subtle}>{errorMessage}</Caption1>
          )}
          <Button appearance="primary" onClick={handleRetry}>
            Retry
          </Button>
        </div>
      );
    }
    if (status === 'empty' || !doc) {
      return (
        <div className={styles.centerContent}>
          <Text size={300} className={styles.subtle}>
            No document to display
          </Text>
        </div>
      );
    }
    return (
      // Nested light provider: pages stay light in dark mode. Inline transparent background, never a className (F-10).
      <FluentProvider
        theme={webLightTheme}
        style={{ display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM, backgroundColor: 'transparent' }}
      >
        {pages.map(p => (
          <PdfPage
            key={p}
            doc={doc}
            pageNumber={p}
            aspect={aspects[p] ?? defaultAspect}
            render={renderWindow.has(p)}
            maxPageWidth={maxPageWidth}
            highlight={activeHighlight && activeHighlight.page === p ? activeHighlight : null}
            registerElement={registerElement}
            onAspect={handleAspect}
          />
        ))}
      </FluentProvider>
    );
  };

  let meta = '';
  if (status === 'ready') {
    meta = activeHighlight ? `Page ${activeHighlight.page} of ${pageCount}` : `${pageCount} ${pageCount === 1 ? 'page' : 'pages'}`;
  }

  return (
    <div className={mergeClasses(styles.root, className)} role="region" aria-label={ariaLabel ?? 'Document viewer'}>
      <div className={styles.bar}>
        <DocumentPdf16Regular />
        <Text size={200} className={styles.barName} title={documentName}>
          {documentName ?? ''}
        </Text>
        {meta && <Caption1 className={styles.meta}>{meta}</Caption1>}
        <span className={styles.srOnly} role="status" aria-live="polite">
          {announcement}
        </span>
      </div>
      <div className={styles.scroller} ref={scrollerRef} tabIndex={0} aria-label="Document pages">
        {renderBody()}
      </div>
    </div>
  );
};

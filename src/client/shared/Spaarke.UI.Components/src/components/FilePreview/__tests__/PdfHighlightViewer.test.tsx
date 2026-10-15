/**
 * PdfHighlightViewer tests (ebilling-r1 task 108).
 *
 * pdfjs is replaced by a fake behind `utils/pdfJsLoader` (no canvas rendering in jsdom). Layout metrics that
 * jsdom reports as 0 (clientWidth, offsetTop, offsetHeight, clientHeight) are stubbed so the scroll maths and the
 * "render only when the page has a width" rule can run. IntersectionObserver is a controllable fake.
 *
 * Covers: geometry and scroll maths (including edges), highlight boxes on the right page, no highlight, lazy
 * rendering, loader rejection + retry, empty states, light FluentProvider around the pages, host bytes not detached.
 */

import * as React from 'react';
import { render, screen, waitFor, act, fireEvent } from '@testing-library/react';
import { FluentProvider, webDarkTheme } from '@fluentui/react-components';
import { PdfHighlightViewer, type IPdfHighlight, type IPdfHighlightViewerProps } from '../PdfHighlightViewer';
import {
  boxToCss,
  clamp01,
  clampBox,
  computeScrollTop,
  isValidPage,
  pagesToRender,
  PDF_HIGHLIGHT_SCROLL_VIEWPORT_FRACTION,
} from '../pdfHighlightGeometry';

// ---------------------------------------------------------------------------
// pdfjs fake (behind the lazy loader)
// ---------------------------------------------------------------------------

const mockGetDocument = jest.fn();
let mockNumPages = 3;
const mockGetPage = jest.fn();
const mockRender = jest.fn();
const mockDestroy = jest.fn();

jest.mock('../../../utils/pdfJsLoader', () => ({
  loadPdfJs: jest.fn(() => Promise.resolve({ getDocument: (...a: unknown[]) => mockGetDocument(...a) })),
}));

function installFakeDocument(): void {
  mockGetPage.mockImplementation((n: number) =>
    Promise.resolve({
      getViewport: ({ scale }: { scale: number }) => ({ width: 600 * scale, height: 800 * scale }),
      render: (...a: unknown[]) => {
        mockRender(n, ...a);
        return { promise: Promise.resolve(), cancel: jest.fn() };
      },
    })
  );
  mockDestroy.mockResolvedValue(undefined);
  mockGetDocument.mockImplementation(() => ({
    promise: Promise.resolve({ numPages: mockNumPages, getPage: mockGetPage }),
    destroy: mockDestroy,
  }));
}

// ---------------------------------------------------------------------------
// jsdom layout + observer stubs
// ---------------------------------------------------------------------------

const PAGE_HEIGHT = 1000;
const VIEWPORT_HEIGHT = 500;
const mockScrollTo = jest.fn();

class FakeIntersectionObserver {
  static instances: FakeIntersectionObserver[] = [];
  observed = new Set<Element>();
  constructor(public cb: (entries: Array<{ target: Element; isIntersecting: boolean }>) => void) {
    FakeIntersectionObserver.instances.push(this);
  }
  observe(el: Element): void {
    this.observed.add(el);
  }
  unobserve(el: Element): void {
    this.observed.delete(el);
  }
  disconnect(): void {
    this.observed.clear();
  }
  /** Report exactly these 1-based pages as intersecting and every other observed page as not. */
  setVisible(pages: number[]): void {
    const entries = Array.from(this.observed).map(target => ({
      target,
      isIntersecting: pages.includes(Number((target as HTMLElement).dataset.page)),
    }));
    this.cb(entries);
  }
}

const proto = HTMLElement.prototype as unknown as Record<string, unknown>;
const saved: Record<string, PropertyDescriptor | undefined> = {};
const stub = (name: string, getter: (this: HTMLElement) => unknown) => {
  saved[name] = Object.getOwnPropertyDescriptor(HTMLElement.prototype, name);
  Object.defineProperty(HTMLElement.prototype, name, { configurable: true, get: getter });
};

beforeAll(() => {
  stub('clientWidth', function () {
    return 600;
  });
  stub('clientHeight', function () {
    return VIEWPORT_HEIGHT;
  });
  stub('offsetHeight', function () {
    return this.dataset.page ? PAGE_HEIGHT : 0;
  });
  stub('offsetTop', function () {
    return this.dataset.page ? (Number(this.dataset.page) - 1) * PAGE_HEIGHT : 0;
  });
  proto.scrollTo = mockScrollTo;
});

afterAll(() => {
  Object.keys(saved).forEach(name => {
    const d = saved[name];
    if (d) Object.defineProperty(HTMLElement.prototype, name, d);
    else delete proto[name];
  });
  delete proto.scrollTo;
});

const realIO = (globalThis as { IntersectionObserver?: unknown }).IntersectionObserver;

beforeEach(() => {
  jest.clearAllMocks();
  mockNumPages = 3;
  installFakeDocument();
  FakeIntersectionObserver.instances = [];
  (globalThis as { IntersectionObserver?: unknown }).IntersectionObserver = FakeIntersectionObserver;
});

afterEach(() => {
  (globalThis as { IntersectionObserver?: unknown }).IntersectionObserver = realIO;
});

const bytes = (): ArrayBuffer => new Uint8Array([37, 80, 68, 70]).buffer;

function renderViewer(props: Partial<IPdfHighlightViewerProps> = {}) {
  const loadData = props.loadData ?? jest.fn().mockResolvedValue(bytes());
  const utils = render(
    <FluentProvider theme={webDarkTheme}>
      <PdfHighlightViewer documentName="Invoice 1042.pdf" loadData={loadData} {...props} />
    </FluentProvider>
  );
  return { ...utils, loadData };
}

const waitReady = () => screen.findByLabelText('Page 1 of 3');
const pageEl = (n: number, total = 3) => screen.getByLabelText(`Page ${n} of ${total}`);

const HL: IPdfHighlight = {
  page: 2,
  row: { x: 0.05, y: 0.4, w: 0.9, h: 0.03 },
  field: { x: 0.7, y: 0.4, w: 0.2, h: 0.03 },
  label: 'Amount',
};

// ---------------------------------------------------------------------------
// Geometry
// ---------------------------------------------------------------------------

describe('pdfHighlightGeometry', () => {
  it('clamp01 bounds values and zeroes non-finite input', () => {
    expect(clamp01(0.5)).toBe(0.5);
    expect(clamp01(-0.2)).toBe(0);
    expect(clamp01(1.7)).toBe(1);
    expect(clamp01(NaN)).toBe(0);
    expect(clamp01(Infinity)).toBe(0);
  });

  it('clampBox keeps an in-range box unchanged', () => {
    expect(clampBox({ x: 0.1, y: 0.2, w: 0.3, h: 0.4 })).toEqual({ x: 0.1, y: 0.2, w: 0.3, h: 0.4 });
  });

  it('clampBox trims a box that spills over the right and bottom edges', () => {
    const c = clampBox({ x: 0.8, y: 0.9, w: 0.5, h: 0.5 });
    expect(c?.x).toBeCloseTo(0.8);
    expect(c?.y).toBeCloseTo(0.9);
    expect(c?.w).toBeCloseTo(0.2);
    expect(c?.h).toBeCloseTo(0.1);
  });

  it('clampBox moves a negative origin to 0 and drops a box with no area left', () => {
    expect(clampBox({ x: -0.2, y: -0.1, w: 0.3, h: 0.3 })).toEqual({ x: 0, y: 0, w: 0.3, h: 0.3 });
    expect(clampBox({ x: 1, y: 0.5, w: 0.2, h: 0.1 })).toBeNull();
    expect(clampBox({ x: 0.2, y: 0.2, w: 0, h: 0.1 })).toBeNull();
    expect(clampBox(null)).toBeNull();
    expect(clampBox(undefined)).toBeNull();
  });

  it('boxToCss converts to percentage strings and returns null for an unusable box', () => {
    expect(boxToCss({ x: 0.25, y: 0.5, w: 0.5, h: 0.125 })).toEqual({
      left: '25%',
      top: '50%',
      width: '50%',
      height: '12.5%',
    });
    expect(boxToCss({ x: 2, y: 2, w: 1, h: 1 })).toBeNull();
  });

  it('computeScrollTop = page offset + y x page height - 30 % of the viewport', () => {
    expect(PDF_HIGHLIGHT_SCROLL_VIEWPORT_FRACTION).toBe(0.3);
    // page 2 starts at 1000; row at 50 % of a 1000px page; viewport 500 -> 1000 + 500 - 150
    expect(computeScrollTop(1000, 1000, 0.5, 500)).toBe(1350);
  });

  it('computeScrollTop never goes below 0 and clamps y to the page', () => {
    expect(computeScrollTop(0, 1000, 0.05, 500)).toBe(0);
    expect(computeScrollTop(0, 1000, -3, 500)).toBe(0);
    // y above 1 behaves as 1 (bottom of the page): 1000 + 1000 - 150
    expect(computeScrollTop(1000, 1000, 9, 500)).toBe(1850);
    expect(computeScrollTop(0, 1000, NaN, 500)).toBe(0);
  });

  it('pagesToRender returns visible pages plus neighbours, clamped and de-duplicated', () => {
    expect(pagesToRender([1], 5)).toEqual([1, 2]);
    expect(pagesToRender([3], 5)).toEqual([2, 3, 4]);
    expect(pagesToRender([4, 5], 5)).toEqual([3, 4, 5]);
    expect(pagesToRender([2, 3], 10)).toEqual([1, 2, 3, 4]);
    expect(pagesToRender([], 5)).toEqual([]);
    expect(pagesToRender([2], 5, 0)).toEqual([2]);
  });

  it('isValidPage accepts whole numbers inside the document only', () => {
    expect(isValidPage(1, 3)).toBe(true);
    expect(isValidPage(3, 3)).toBe(true);
    expect(isValidPage(0, 3)).toBe(false);
    expect(isValidPage(4, 3)).toBe(false);
    expect(isValidPage(1.5, 3)).toBe(false);
    expect(isValidPage(1, 0)).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

describe('PdfHighlightViewer', () => {
  describe('states', () => {
    it('shows the empty state when no source is supplied', () => {
      render(<PdfHighlightViewer />);
      expect(screen.getByText('No document to display')).toBeInTheDocument();
      expect(mockGetDocument).not.toHaveBeenCalled();
    });

    it('shows the empty state for a document with no pages', async () => {
      mockNumPages = 0;
      renderViewer();
      expect(await screen.findByText('No document to display')).toBeInTheDocument();
    });

    it('shows a spinner while loading, then the pages, and reports the page count', async () => {
      const onLoad = jest.fn();
      renderViewer({ onLoad });
      expect(screen.getByText('Loading preview...')).toBeInTheDocument();
      await waitReady();
      expect(screen.queryByText('Loading preview...')).not.toBeInTheDocument();
      expect(pageEl(2)).toBeInTheDocument();
      expect(pageEl(3)).toBeInTheDocument();
      expect(onLoad).toHaveBeenCalledWith({ pageCount: 3 });
      expect(screen.getByText('Invoice 1042.pdf')).toBeInTheDocument();
      expect(screen.getByText('3 pages')).toBeInTheDocument();
    });

    it('has an accessible region name', async () => {
      renderViewer({ ariaLabel: 'Invoice document' });
      await waitReady();
      expect(screen.getByRole('region', { name: 'Invoice document' })).toBeInTheDocument();
    });

    it('shows the error state when the loader rejects, and Retry calls the loader again', async () => {
      const onError = jest.fn();
      const loadData = jest.fn().mockRejectedValueOnce(new Error('network down')).mockResolvedValue(bytes());
      renderViewer({ loadData, onError });

      expect(await screen.findByText('Preview not available')).toBeInTheDocument();
      expect(screen.getByText('network down')).toBeInTheDocument();
      expect(onError).toHaveBeenCalledTimes(1);
      expect(loadData).toHaveBeenCalledTimes(1);

      fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
      await waitReady();
      expect(loadData).toHaveBeenCalledTimes(2);
      expect(screen.queryByText('Preview not available')).not.toBeInTheDocument();
    });

    it('shows the error state when the PDF cannot be parsed', async () => {
      mockGetDocument.mockImplementation(() => ({ promise: Promise.reject(new Error('Invalid PDF structure')), destroy: mockDestroy }));
      renderViewer();
      expect(await screen.findByText('Preview not available')).toBeInTheDocument();
      expect(screen.getByText('Invalid PDF structure')).toBeInTheDocument();
    });

    it('releases the pdfjs loading task (document and worker) on unmount', async () => {
      const { unmount } = renderViewer();
      await waitReady();
      expect(mockDestroy).not.toHaveBeenCalled();
      unmount();
      expect(mockDestroy).toHaveBeenCalledTimes(1);
    });

    it('gives pdfjs a copy so the host bytes are not detached (Retry stays possible)', async () => {
      const host = new Uint8Array([37, 80, 68, 70]);
      renderViewer({ data: host, loadData: undefined });
      await waitReady();
      const passed = mockGetDocument.mock.calls[0][0].data as Uint8Array;
      expect(passed).not.toBe(host);
      expect(passed.buffer).not.toBe(host.buffer);
      expect(Array.from(passed)).toEqual([37, 80, 68, 70]);
    });
  });

  describe('highlight', () => {
    it('draws the row box and the labelled field box on the highlighted page only, at the stated positions', async () => {
      renderViewer({ highlight: HL });
      await waitReady();

      const row = screen.getByTestId('pdf-highlight-row');
      const field = screen.getByTestId('pdf-highlight-field');
      const label = screen.getByTestId('pdf-highlight-label');

      expect(pageEl(2)).toContainElement(row);
      expect(pageEl(2)).toContainElement(field);
      expect(pageEl(2)).toContainElement(label);
      expect(pageEl(1)).not.toContainElement(row);
      expect(pageEl(3)).not.toContainElement(row);

      expect(row).toHaveStyle({ left: '5%', top: '40%', width: '90%' });
      expect(field).toHaveStyle({ left: '70%', top: '40%', width: '20%' });
      expect(label).toHaveTextContent('Amount');
      expect(label).toHaveStyle({ left: '70%' });
    });

    it('uses token classes, a multiply tint under the row, and aria-hidden overlays', async () => {
      renderViewer({ highlight: HL });
      await waitReady();
      const row = screen.getByTestId('pdf-highlight-row');
      expect(row).toHaveAttribute('aria-hidden', 'true');
      expect(screen.getByTestId('pdf-highlight-field')).toHaveAttribute('aria-hidden', 'true');
      expect(screen.getByTestId('pdf-highlight-label')).toHaveAttribute('aria-hidden', 'true');
      // Styles are Griffel atomic classes from tokens; the row box wraps the tint fill.
      expect(row.className).not.toBe('');
      expect(row.firstElementChild).not.toBeNull();
      // No hard-coded colour reaches an inline style on any overlay.
      [row, row.firstElementChild as HTMLElement, screen.getByTestId('pdf-highlight-field'), screen.getByTestId('pdf-highlight-label')].forEach(el => {
        expect(el.getAttribute('style') ?? '').not.toMatch(/#[0-9a-f]{3,8}|rgb\(|hsl\(/i);
      });
    });

    it('announces the highlighted label to screen readers', async () => {
      renderViewer({ highlight: HL });
      await waitReady();
      expect(screen.getByRole('status')).toHaveTextContent('Highlighted Amount on page 2 of 3');
      expect(screen.getByText('Page 2 of 3', { selector: 'span' })).toBeInTheDocument();
    });

    it('draws nothing when there is no highlight', async () => {
      renderViewer({ highlight: null });
      await waitReady();
      expect(screen.queryByTestId('pdf-highlight-row')).not.toBeInTheDocument();
      expect(screen.queryByTestId('pdf-highlight-field')).not.toBeInTheDocument();
      expect(screen.queryByTestId('pdf-highlight-label')).not.toBeInTheDocument();
      expect(screen.getByRole('status')).toHaveTextContent('');
    });

    it('drops a highlight on a page the document does not have', async () => {
      renderViewer({ highlight: { ...HL, page: 9 } });
      await waitReady();
      expect(screen.queryByTestId('pdf-highlight-row')).not.toBeInTheDocument();
      expect(mockScrollTo).not.toHaveBeenCalled();
    });

    it('clamps out-of-range coordinates to the page', async () => {
      renderViewer({
        highlight: { page: 1, row: { x: -0.1, y: 0.9, w: 2, h: 0.5 }, field: { x: 0.9, y: 0.5, w: 0.5, h: 0.1 }, label: 'Hours' },
      });
      await waitReady();
      const row = screen.getByTestId('pdf-highlight-row');
      expect(row).toHaveStyle({ left: '0%', width: '100%' });
      expect(row.style.top).toBe('90%');
      expect(parseFloat(row.style.height)).toBeCloseTo(10);
      const field = screen.getByTestId('pdf-highlight-field');
      expect(field.style.left).toBe('90%');
      expect(parseFloat(field.style.width)).toBeCloseTo(10);
    });

    it('omits the field box and tag when only a row is given', async () => {
      renderViewer({ highlight: { page: 2, row: HL.row } });
      await waitReady();
      expect(screen.getByTestId('pdf-highlight-row')).toBeInTheDocument();
      expect(screen.queryByTestId('pdf-highlight-field')).not.toBeInTheDocument();
      expect(screen.queryByTestId('pdf-highlight-label')).not.toBeInTheDocument();
    });

    it('puts the tag under a field that sits at the very top of the page', async () => {
      renderViewer({ highlight: { page: 1, field: { x: 0.1, y: 0.01, w: 0.2, h: 0.02 }, label: 'Date' } });
      await waitReady();
      const label = screen.getByTestId('pdf-highlight-label');
      // below the box: y 0.01 + h 0.02 = 3 %
      expect(parseFloat(label.style.top)).toBeCloseTo(3);
      // a field further down keeps its tag above (top = field y)
    });

    it('keeps the tag above a field that has room', async () => {
      renderViewer({ highlight: { page: 1, field: { x: 0.1, y: 0.5, w: 0.2, h: 0.02 }, label: 'Date' } });
      await waitReady();
      expect(parseFloat(screen.getByTestId('pdf-highlight-label').style.top)).toBeCloseTo(50);
    });
  });

  describe('scrolling', () => {
    it('scrolls so the row sits 30 % down the viewport on load', async () => {
      renderViewer({ highlight: HL });
      await waitReady();
      await waitFor(() => expect(mockScrollTo).toHaveBeenCalled());
      // page 2 offsetTop 1000 + 0.4 x 1000 - 0.3 x 500
      expect(mockScrollTo).toHaveBeenLastCalledWith(expect.objectContaining({ top: 1250 }));
    });

    it('moves the scroller when the highlight changes, and returns to the top when it is cleared', async () => {
      const loadData = jest.fn().mockResolvedValue(bytes());
      const { rerender } = renderViewer({ loadData, highlight: HL });
      await waitReady();
      await waitFor(() => expect(mockScrollTo).toHaveBeenCalledTimes(1));

      const next: IPdfHighlight = { page: 3, row: { x: 0, y: 0.8, w: 1, h: 0.03 }, label: 'Rate' };
      rerender(
        <FluentProvider theme={webDarkTheme}>
          <PdfHighlightViewer documentName="Invoice 1042.pdf" loadData={loadData} highlight={next} />
        </FluentProvider>
      );
      // page 3 offsetTop 2000 + 0.8 x 1000 - 150
      await waitFor(() => expect(mockScrollTo).toHaveBeenLastCalledWith(expect.objectContaining({ top: 2650 })));
      expect(screen.getByTestId('pdf-highlight-row').closest('[data-page]')).toHaveAttribute('data-page', '3');

      // Clearing the highlight scrolls back to the top once the scroller has moved.
      const scroller = screen.getByLabelText('Document pages');
      scroller.scrollTop = 2650;
      mockScrollTo.mockClear();
      rerender(
        <FluentProvider theme={webDarkTheme}>
          <PdfHighlightViewer documentName="Invoice 1042.pdf" loadData={loadData} highlight={null} />
        </FluentProvider>
      );
      await waitFor(() => expect(mockScrollTo).toHaveBeenCalledWith(expect.objectContaining({ top: 0 })));
    });

    it('scrolls to the field when the highlight has no row', async () => {
      renderViewer({ highlight: { page: 1, field: { x: 0.1, y: 0.6, w: 0.2, h: 0.02 }, label: 'Date' } });
      await waitReady();
      await waitFor(() => expect(mockScrollTo).toHaveBeenCalled());
      // page 1 offsetTop 0 + 0.6 x 1000 - 150
      expect(mockScrollTo).toHaveBeenLastCalledWith(expect.objectContaining({ top: 450 }));
    });
  });

  describe('lazy page rendering (NFR-02)', () => {
    const renderedPages = () => Array.from(new Set(mockRender.mock.calls.map(c => c[0] as number))).sort((a, b) => a - b);

    it('renders only the visible page and its neighbours, and more as the user scrolls', async () => {
      mockNumPages = 20;
      renderViewer();
      await screen.findByLabelText('Page 1 of 20');

      const io = FakeIntersectionObserver.instances[FakeIntersectionObserver.instances.length - 1];
      act(() => io.setVisible([1]));
      await waitFor(() => expect(renderedPages()).toEqual([1, 2]));
      expect(document.querySelectorAll('canvas')).toHaveLength(2);

      act(() => io.setVisible([10]));
      await waitFor(() => expect(renderedPages()).toEqual([1, 2, 9, 10, 11]));
      // Pages that left the window release their canvas.
      expect(document.querySelectorAll('canvas')).toHaveLength(3);
    });

    it('also renders the highlighted page and its neighbours', async () => {
      mockNumPages = 20;
      renderViewer({ highlight: { page: 15, row: { x: 0, y: 0.1, w: 1, h: 0.02 } } });
      await screen.findByLabelText('Page 1 of 20');
      const io = FakeIntersectionObserver.instances[FakeIntersectionObserver.instances.length - 1];
      act(() => io.setVisible([1]));
      await waitFor(() => expect(renderedPages()).toEqual([1, 2, 14, 15, 16]));
    });

    it('renders the page into its canvas at the shown width', async () => {
      renderViewer();
      await waitReady();
      const io = FakeIntersectionObserver.instances[FakeIntersectionObserver.instances.length - 1];
      act(() => io.setVisible([1]));
      await waitFor(() => expect(mockRender).toHaveBeenCalled());
      const call = mockRender.mock.calls.find(c => c[0] === 1);
      const params = call?.[1] as { canvas: HTMLCanvasElement; viewport: { width: number } };
      // clientWidth 600 on a 600-wide page at devicePixelRatio 1 -> 600 px canvas
      expect(params.canvas.width).toBe(600);
      expect(params.viewport.width).toBe(600);
    });

    it('shows a per-page notice, not a crash, when one page fails to render', async () => {
      mockGetPage.mockImplementation((n: number) =>
        n === 1
          ? Promise.reject(new Error('bad page'))
          : Promise.resolve({
              getViewport: ({ scale }: { scale: number }) => ({ width: 600 * scale, height: 800 * scale }),
              render: () => ({ promise: Promise.resolve(), cancel: jest.fn() }),
            })
      );
      renderViewer();
      await waitReady();
      const io = FakeIntersectionObserver.instances[FakeIntersectionObserver.instances.length - 1];
      act(() => io.setVisible([1]));
      expect(await screen.findByText('This page could not be rendered.')).toBeInTheDocument();
      expect(pageEl(2)).toBeInTheDocument();
    });
  });

  describe('dark mode (F-4)', () => {
    it('wraps the pages in a light FluentProvider with an inline transparent background', async () => {
      renderViewer({ highlight: HL });
      await waitReady();
      const provider = pageEl(1).parentElement as HTMLElement;
      expect(provider.className).toContain('fui-FluentProvider');
      expect(provider.style.backgroundColor).toBe('transparent');
      // All pages, and the highlight boxes, live under that single light provider.
      [1, 2, 3].forEach(n => expect(pageEl(n).parentElement).toBe(provider));
      expect(provider).toContainElement(screen.getByTestId('pdf-highlight-row'));
      // The toolbar is outside the light provider, so it follows the host (dark) theme.
      expect(provider).not.toContainElement(screen.getByText('Invoice 1042.pdf'));
    });

    it('does not apply the light provider to the loading/error chrome', async () => {
      const loadData = jest.fn().mockRejectedValue(new Error('x'));
      renderViewer({ loadData });
      const heading = await screen.findByText('Preview not available');
      expect(heading.closest('.fui-FluentProvider')).toBe(document.querySelector('.fui-FluentProvider'));
    });
  });
});

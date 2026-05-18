/**
 * ContextWidgetAdapter — unit tests
 *
 * Covers:
 * - createContextWidgetAdapter renders the inner R1 widget correctly.
 * - The adapter passes data, isLoading, error, and className to the inner widget.
 * - DocumentViewerContextWidget.onHighlight scrolls to a [data-citation-id] element.
 * - DocumentViewerContextWidget.onHighlight is a safe no-op when no element matches.
 * - WebSourceContextWidget.onHighlight is a no-op (no scroll, no error).
 * - CitationContextWidget.onHighlight scrolls the matching <li> into view.
 * - CitationContextWidget.onHighlight is a no-op when citation is not found.
 *
 * Task: AIPU2-081
 */

import '@testing-library/jest-dom';
import React, { createRef } from 'react';
import { render, screen, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import type { ContextWidgetHighlightHandle } from '../ContextWidgetAdapter';
import DocumentViewerContextWidget from '../DocumentViewerContextWidget';
import WebSourceContextWidget from '../WebSourceContextWidget';
import CitationContextWidget from '../CitationContextWidget';
import type { DocumentViewerData } from '../DocumentViewerContextWidget';
import type { WebSourceData } from '../WebSourceContextWidget';
import type { CitationData } from '../CitationContextWidget';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function wrap(ui: React.ReactElement) {
  return render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
}

// ---------------------------------------------------------------------------
// DocumentViewerContextWidget — rendering
// ---------------------------------------------------------------------------

describe('DocumentViewerContextWidget — rendering', () => {
  const data: DocumentViewerData = {
    documentUrl: 'https://example.com/doc.pdf',
    fileName: 'annual-report.pdf',
    mimeType: 'application/pdf',
    canDownload: false,
  };

  it('renders the document file name', () => {
    const ref = createRef<ContextWidgetHighlightHandle>();
    wrap(
      <DocumentViewerContextWidget
        ref={ref}
        data={data}
        widgetType="DocumentViewer"
      />
    );

    expect(screen.getByText('annual-report.pdf')).toBeInTheDocument();
  });

  it('renders loading state when isLoading is true', () => {
    const ref = createRef<ContextWidgetHighlightHandle>();
    wrap(
      <DocumentViewerContextWidget
        ref={ref}
        data={data}
        widgetType="DocumentViewer"
        isLoading={true}
      />
    );

    expect(screen.getByText(/loading document/i)).toBeInTheDocument();
  });

  it('renders error message when error prop is set', () => {
    const ref = createRef<ContextWidgetHighlightHandle>();
    wrap(
      <DocumentViewerContextWidget
        ref={ref}
        data={data}
        widgetType="DocumentViewer"
        error="Document could not be loaded"
      />
    );

    expect(screen.getByText('Document could not be loaded')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// DocumentViewerContextWidget — onHighlight
// ---------------------------------------------------------------------------

describe('DocumentViewerContextWidget — onHighlight', () => {
  const data: DocumentViewerData = {
    documentUrl: 'https://example.com/doc.pdf',
    fileName: 'contract.pdf',
    mimeType: 'application/pdf',
  };

  it('exposes an onHighlight handle via ref', () => {
    const ref = createRef<ContextWidgetHighlightHandle>();
    wrap(
      <DocumentViewerContextWidget
        ref={ref}
        data={data}
        widgetType="DocumentViewer"
      />
    );

    expect(ref.current).not.toBeNull();
    expect(typeof ref.current?.onHighlight).toBe('function');
  });

  it('calls onHighlight without throwing when no matching element exists', () => {
    const ref = createRef<ContextWidgetHighlightHandle>();
    wrap(
      <DocumentViewerContextWidget
        ref={ref}
        data={data}
        widgetType="DocumentViewer"
      />
    );

    // Should not throw — no [data-citation-id] element in DOM
    expect(() => {
      act(() => {
        ref.current?.onHighlight('citation-99', 'char:100-200');
      });
    }).not.toThrow();
  });

  it('scrolls to and highlights a matching [data-citation-id] element', () => {
    const ref = createRef<ContextWidgetHighlightHandle>();
    wrap(
      <DocumentViewerContextWidget
        ref={ref}
        data={data}
        widgetType="DocumentViewer"
      />
    );

    // The adapter wraps the inner widget in a <div> owned by containerRef.
    // We inject a mock overlay element directly into that container div so
    // the highlighter's querySelector can find it.
    const adapterRoot = document.querySelector('div[style]') as HTMLElement | null;
    const overlay = document.createElement('span');
    overlay.setAttribute('data-citation-id', 'cit-42');
    overlay.scrollIntoView = jest.fn();
    adapterRoot?.appendChild(overlay);

    act(() => {
      ref.current?.onHighlight('cit-42');
    });

    expect(overlay.scrollIntoView).toHaveBeenCalledWith({ behavior: 'smooth', block: 'center' });
    expect(overlay.classList.contains('context-citation-highlight')).toBe(true);
  });
});

// ---------------------------------------------------------------------------
// WebSourceContextWidget — rendering
// ---------------------------------------------------------------------------

describe('WebSourceContextWidget — rendering', () => {
  const data: WebSourceData = {
    url: 'https://example.com/source',
    title: 'Example Source',
  };

  it('renders the URL in the URL bar input', () => {
    const ref = createRef<ContextWidgetHighlightHandle>();
    wrap(
      <WebSourceContextWidget
        ref={ref}
        data={data}
        widgetType="WebSource"
      />
    );

    // URL appears in a read-only input
    expect(screen.getByDisplayValue('https://example.com/source')).toBeInTheDocument();
  });

  it('renders loading state when isLoading is true', () => {
    const ref = createRef<ContextWidgetHighlightHandle>();
    wrap(
      <WebSourceContextWidget
        ref={ref}
        data={data}
        widgetType="WebSource"
        isLoading={true}
      />
    );

    expect(screen.getByText(/loading web source/i)).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// WebSourceContextWidget — onHighlight (no-op)
// ---------------------------------------------------------------------------

describe('WebSourceContextWidget — onHighlight (no-op)', () => {
  const data: WebSourceData = { url: 'https://example.com' };

  it('exposes an onHighlight handle via ref', () => {
    const ref = createRef<ContextWidgetHighlightHandle>();
    wrap(
      <WebSourceContextWidget
        ref={ref}
        data={data}
        widgetType="WebSource"
      />
    );

    expect(ref.current).not.toBeNull();
    expect(typeof ref.current?.onHighlight).toBe('function');
  });

  it('onHighlight is a safe no-op — does not throw', () => {
    const ref = createRef<ContextWidgetHighlightHandle>();
    wrap(
      <WebSourceContextWidget
        ref={ref}
        data={data}
        widgetType="WebSource"
      />
    );

    expect(() => {
      act(() => {
        ref.current?.onHighlight('cit-any', 'char:0-100');
      });
    }).not.toThrow();
  });
});

// ---------------------------------------------------------------------------
// CitationContextWidget — rendering
// ---------------------------------------------------------------------------

describe('CitationContextWidget — rendering', () => {
  const data: CitationData = {
    citations: [
      { id: 'c1', index: 1, text: 'Brown v. Board of Education', sourceType: 'legal', url: 'https://law.com/c1' },
      { id: 'c2', index: 2, text: 'Roe v. Wade', sourceType: 'legal' },
    ],
  };

  it('renders all citation texts', () => {
    const ref = createRef<ContextWidgetHighlightHandle>();
    wrap(
      <CitationContextWidget
        ref={ref}
        data={data}
        widgetType="Citation"
      />
    );

    expect(screen.getByText('Brown v. Board of Education')).toBeInTheDocument();
    expect(screen.getByText('Roe v. Wade')).toBeInTheDocument();
  });

  it('renders loading state when isLoading is true', () => {
    const ref = createRef<ContextWidgetHighlightHandle>();
    wrap(
      <CitationContextWidget
        ref={ref}
        data={data}
        widgetType="Citation"
        isLoading={true}
      />
    );

    expect(screen.getByText(/loading citations/i)).toBeInTheDocument();
  });

  it('renders "No citations available" when citation list is empty', () => {
    const ref = createRef<ContextWidgetHighlightHandle>();
    wrap(
      <CitationContextWidget
        ref={ref}
        data={{ citations: [] }}
        widgetType="Citation"
      />
    );

    expect(screen.getByText(/no citations available/i)).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// CitationContextWidget — onHighlight (scroll to matching li)
// ---------------------------------------------------------------------------

describe('CitationContextWidget — onHighlight', () => {
  const data: CitationData = {
    citations: [
      { id: 'cit-a', index: 1, text: 'First citation', sourceType: 'document' },
      { id: 'cit-b', index: 2, text: 'Second citation', sourceType: 'web' },
    ],
  };

  it('exposes an onHighlight handle via ref', () => {
    const ref = createRef<ContextWidgetHighlightHandle>();
    wrap(
      <CitationContextWidget
        ref={ref}
        data={data}
        widgetType="Citation"
      />
    );

    expect(ref.current).not.toBeNull();
    expect(typeof ref.current?.onHighlight).toBe('function');
  });

  it('scrolls the matching list item into view on onHighlight', () => {
    const ref = createRef<ContextWidgetHighlightHandle>();
    const { container } = wrap(
      <CitationContextWidget
        ref={ref}
        data={data}
        widgetType="Citation"
      />
    );

    // After render the CitationContextWidget adds data-citation-id to <li> elements.
    const listItems = container.querySelectorAll('li');
    expect(listItems.length).toBeGreaterThanOrEqual(2);

    // Mock scrollIntoView on the first list item (cit-a).
    const firstItem = listItems[0];
    firstItem.scrollIntoView = jest.fn();

    act(() => {
      ref.current?.onHighlight('cit-a');
    });

    expect(firstItem.scrollIntoView).toHaveBeenCalledWith({ behavior: 'smooth', block: 'nearest' });
    expect(firstItem.classList.contains('context-citation-highlight')).toBe(true);
  });

  it('does not throw when onHighlight is called with an unknown citationId', () => {
    const ref = createRef<ContextWidgetHighlightHandle>();
    wrap(
      <CitationContextWidget
        ref={ref}
        data={data}
        widgetType="Citation"
      />
    );

    expect(() => {
      act(() => {
        ref.current?.onHighlight('nonexistent-cit');
      });
    }).not.toThrow();
  });
});

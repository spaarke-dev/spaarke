/**
 * DocumentViewerWidget — unit tests (R4 task 042 / W-4)
 *
 * Covers:
 *  (a) Renders filename + MIME badge + size in header
 *  (b) Renders extracted text content as a preview
 *  (c) Renders empty-state message when textContent is empty
 *  (d) Truncates very large textContent payloads
 *  (e) Defensive narrowing: invalid payload shape renders without crash
 *
 * Registry-resolution test lives in
 * `__tests__/document-viewer-widget-registration.test.ts` so it can verify
 * the side-effect import path without depending on the React tree.
 */

import '@testing-library/jest-dom';
import React from 'react';
import { render, screen } from '@testing-library/react';
import DocumentViewerWidget, { type DocumentViewerWidgetData } from '../DocumentViewerWidget';
import type { WorkspaceWidgetProps } from '../../../types/widget-types';

function renderWidget(
  data: WorkspaceWidgetProps<DocumentViewerWidgetData>['data'],
  overrides: Partial<WorkspaceWidgetProps<DocumentViewerWidgetData>> = {}
) {
  return render(<DocumentViewerWidget data={data} widgetType="document-viewer" {...overrides} />);
}

describe('DocumentViewerWidget — header rendering', () => {
  it('renders filename in the header', () => {
    renderWidget({
      filename: 'Contract.pdf',
      contentType: 'application/pdf',
      sizeBytes: 1024 * 1024,
      textContent: 'Hello world',
    });

    expect(screen.getByText('Contract.pdf')).toBeInTheDocument();
  });

  it('renders MIME type as a badge', () => {
    renderWidget({
      filename: 'memo.docx',
      contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      textContent: 'Some text',
    });

    expect(
      screen.getByText('application/vnd.openxmlformats-officedocument.wordprocessingml.document')
    ).toBeInTheDocument();
  });

  it('renders human-readable file size', () => {
    renderWidget({
      filename: 'big.pdf',
      contentType: 'application/pdf',
      sizeBytes: 5 * 1024 * 1024,
      textContent: 'x',
    });

    expect(screen.getByText('5.0 MB')).toBeInTheDocument();
  });

  it('omits size when sizeBytes is not provided', () => {
    renderWidget({
      filename: 'unknown.txt',
      contentType: 'text/plain',
      textContent: 'x',
    });

    // No MB / KB / B suffix should appear when sizeBytes is undefined.
    expect(screen.queryByText(/\d+(\.\d+)? (B|KB|MB|GB)/)).not.toBeInTheDocument();
  });
});

describe('DocumentViewerWidget — content rendering', () => {
  it('renders extracted text content as a preview', () => {
    renderWidget({
      filename: 'simple.txt',
      contentType: 'text/plain',
      textContent: 'Hello\nworld',
    });

    const preview = screen.getByTestId('document-viewer-preview');
    expect(preview).toBeInTheDocument();
    expect(preview).toHaveTextContent('Hello world');
  });

  it('renders the empty-state message when textContent is empty', () => {
    renderWidget({
      filename: 'empty.pdf',
      contentType: 'application/pdf',
      textContent: '',
    });

    expect(screen.getByText(/No preview available/i)).toBeInTheDocument();
    expect(screen.queryByTestId('document-viewer-preview')).not.toBeInTheDocument();
  });

  it('truncates the inline preview for very large text payloads', () => {
    const huge = 'A'.repeat(60_000); // > 50,000 char cap
    renderWidget({
      filename: 'huge.pdf',
      contentType: 'application/pdf',
      textContent: huge,
    });

    const preview = screen.getByTestId('document-viewer-preview');
    expect(preview.textContent).toContain('Preview truncated');
    expect(preview.textContent!.length).toBeLessThanOrEqual(
      huge.length + 200 // overhead for the truncation banner
    );
  });
});

describe('DocumentViewerWidget — error + loading states', () => {
  it('renders the loading state when isLoading is true', () => {
    renderWidget(
      {
        filename: 'pending.pdf',
        contentType: 'application/pdf',
        textContent: 'will not render',
      },
      { isLoading: true }
    );

    expect(screen.getByText(/Loading preview/i)).toBeInTheDocument();
    expect(screen.queryByTestId('document-viewer-preview')).not.toBeInTheDocument();
  });

  it('renders the error state when error is set', () => {
    renderWidget(
      {
        filename: 'broken.pdf',
        contentType: 'application/pdf',
        textContent: 'will not render',
      },
      { error: 'Extraction failed' }
    );

    expect(screen.getByText('Extraction failed')).toBeInTheDocument();
    expect(screen.queryByTestId('document-viewer-preview')).not.toBeInTheDocument();
  });
});

describe('DocumentViewerWidget — defensive narrowing', () => {
  it('renders without crashing for non-conforming payload shapes', () => {
    // Cast through unknown to bypass the prop type and simulate an upstream
    // dispatcher sending a wrong-shaped payload (e.g. legacy widget_load
    // signal that hasn't been migrated). The widget should still render
    // its empty state without throwing.
    const bad = renderWidget({ foo: 'bar' } as unknown as DocumentViewerWidgetData);
    expect(bad.container).toBeTruthy();
    // Empty-state copy is used as the fallback when filename/textContent
    // are missing.
    expect(screen.getByText(/No preview available/i)).toBeInTheDocument();
  });
});

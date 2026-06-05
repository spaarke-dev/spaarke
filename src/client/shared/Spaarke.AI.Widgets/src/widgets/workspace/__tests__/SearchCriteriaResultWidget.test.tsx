/**
 * SearchCriteriaResultWidget — unit tests (R4 task 043 / W-5)
 *
 * Covers:
 *  (a) Renders domain title, query body, and per-domain filter rows
 *  (b) Documents domain shows Document Type + File Type + Matter Type rows
 *  (c) Matters domain shows Matter Type only (no Document/File Type)
 *  (d) Projects/Invoices domains show no per-domain filters
 *  (e) Empty query falls back to "No query text" placeholder
 *  (f) Defensive narrowing: invalid payload shape renders empty-state safely
 *
 * Registry-resolution test lives in
 * register-search-criteria-result-widget.test.ts so it can verify the
 * side-effect import path without depending on the React tree.
 *
 * Mirrors DocumentViewerWidget.test.tsx (R4 task 042 sibling).
 */

import '@testing-library/jest-dom';
import React from 'react';
import { render, screen } from '@testing-library/react';
import SearchCriteriaResultWidget, { type SearchCriteriaResultWidgetData } from '../SearchCriteriaResultWidget';
import type { WorkspaceWidgetProps } from '../../../types/widget-types';

function renderWidget(
  data: WorkspaceWidgetProps<SearchCriteriaResultWidgetData>['data'],
  overrides: Partial<WorkspaceWidgetProps<SearchCriteriaResultWidgetData>> = {}
) {
  return render(<SearchCriteriaResultWidget data={data} widgetType="search-criteria-result" {...overrides} />);
}

describe('SearchCriteriaResultWidget — header rendering', () => {
  it('renders the domain in the header title', () => {
    renderWidget({
      query: 'IP indemnity clauses',
      domain: 'documents',
    });

    expect(screen.getByText(/Search criteria — Documents/)).toBeInTheDocument();
  });

  it('renders "Captured at" subtitle when capturedAt is provided', () => {
    renderWidget({
      query: 'q',
      domain: 'documents',
      capturedAt: '2026-05-26T18:00:00.000Z',
    });

    expect(screen.getByText(/Captured at /)).toBeInTheDocument();
  });
});

describe('SearchCriteriaResultWidget — body rendering by domain', () => {
  it('renders the query value when non-empty', () => {
    renderWidget({
      query: 'find clauses about indemnity',
      domain: 'documents',
    });

    expect(screen.getByText('find clauses about indemnity')).toBeInTheDocument();
  });

  it('renders "No query text" placeholder when query is empty', () => {
    renderWidget({
      query: '',
      domain: 'matters',
    });

    expect(screen.getByText('No query text')).toBeInTheDocument();
  });

  it('shows Document Type + File Type + Matter Type rows for documents domain', () => {
    renderWidget({
      query: 'q',
      domain: 'documents',
      documentType: 'contract',
      fileType: 'pdf',
      matterType: 'litigation',
    });

    expect(screen.getByText('Document Type')).toBeInTheDocument();
    expect(screen.getByText('File Type')).toBeInTheDocument();
    expect(screen.getByText('Matter Type')).toBeInTheDocument();
    expect(screen.getByText('Contract')).toBeInTheDocument();
    expect(screen.getByText('Pdf')).toBeInTheDocument();
    expect(screen.getByText('Litigation')).toBeInTheDocument();
  });

  it('shows only Matter Type row for matters domain', () => {
    renderWidget({
      query: 'q',
      domain: 'matters',
      matterType: 'advisory',
    });

    expect(screen.queryByText('Document Type')).not.toBeInTheDocument();
    expect(screen.queryByText('File Type')).not.toBeInTheDocument();
    expect(screen.getByText('Matter Type')).toBeInTheDocument();
    expect(screen.getByText('Advisory')).toBeInTheDocument();
  });

  it('shows no per-domain filters for projects domain', () => {
    renderWidget({
      query: 'q',
      domain: 'projects',
    });

    expect(screen.queryByText('Document Type')).not.toBeInTheDocument();
    expect(screen.queryByText('File Type')).not.toBeInTheDocument();
    expect(screen.queryByText('Matter Type')).not.toBeInTheDocument();
    // Date Range is always rendered for any valid payload.
    expect(screen.getByText('Date Range')).toBeInTheDocument();
  });

  it('renders dateRange preset labels correctly', () => {
    renderWidget({
      query: 'q',
      domain: 'invoices',
      dateRange: 'last30',
    });

    expect(screen.getByText('Last 30 days')).toBeInTheDocument();
  });

  it('renders "All" placeholder for filter values that equal "all"', () => {
    renderWidget({
      query: 'q',
      domain: 'documents',
      documentType: 'all',
      fileType: 'all',
      matterType: 'all',
    });

    // Three "All" placeholder values, one per filter row.
    const allLabels = screen.getAllByText('All');
    expect(allLabels.length).toBeGreaterThanOrEqual(3);
  });

  it('renders a notice banner explaining the demo scope', () => {
    renderWidget({
      query: 'q',
      domain: 'documents',
    });

    expect(screen.getByTestId('search-criteria-result-notice')).toBeInTheDocument();
  });
});

describe('SearchCriteriaResultWidget — loading + error states', () => {
  it('renders the loading state when isLoading is true', () => {
    renderWidget({ query: 'q', domain: 'documents' }, { isLoading: true });

    expect(screen.getByText('Loading criteria…')).toBeInTheDocument();
    expect(screen.queryByTestId('search-criteria-result-body')).not.toBeInTheDocument();
  });

  it('renders the error state when error is set', () => {
    renderWidget({ query: 'q', domain: 'documents' }, { error: 'Failed to load criteria' });

    expect(screen.getByText('Failed to load criteria')).toBeInTheDocument();
    expect(screen.queryByTestId('search-criteria-result-body')).not.toBeInTheDocument();
  });
});

describe('SearchCriteriaResultWidget — defensive narrowing', () => {
  it('renders empty state for non-conforming payload shapes', () => {
    // Cast through unknown to simulate an upstream dispatcher sending a
    // wrong-shaped payload. The widget MUST not crash and SHOULD render a
    // safe empty state instead of falling through to the body section.
    const bad = renderWidget({ foo: 'bar' } as unknown as SearchCriteriaResultWidgetData);
    expect(bad.container).toBeTruthy();
    expect(screen.getByText('No criteria captured.')).toBeInTheDocument();
    expect(screen.queryByTestId('search-criteria-result-body')).not.toBeInTheDocument();
  });
});

/**
 * Shared test utilities for Spaarke AI output and source widget tests.
 *
 * Provides:
 *   - renderWithTheme: wraps render() in FluentProvider with a given theme.
 *   - Mock prop factories for each of the 11 output widgets and 6 source widgets.
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * Constraint: No production widget source files are modified here.
 */

import React from 'react';
import { render, type RenderResult } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';

// Re-export themes so test files import from one place
export { webLightTheme, webDarkTheme };

// ---------------------------------------------------------------------------
// renderWithTheme helper
// ---------------------------------------------------------------------------

/**
 * Renders the given element wrapped in a FluentProvider with the specified
 * theme. Defaults to webLightTheme.
 */
export function renderWithTheme(
  ui: React.ReactElement,
  theme: typeof webLightTheme | typeof webDarkTheme = webLightTheme
): RenderResult {
  return render(<FluentProvider theme={theme}>{ui}</FluentProvider>);
}

// ---------------------------------------------------------------------------
// Output widget mock prop factories
// ---------------------------------------------------------------------------

/** Minimal valid props for BudgetDashboardWidget */
export function mockBudgetDashboardProps() {
  return {
    data: {
      title: 'Q3 Matter Budget',
      items: [
        { label: 'Legal Fees', spent: 50000, budget: 80000, currency: 'USD' },
        { label: 'Disbursements', spent: 12000, budget: 10000, currency: 'USD' },
      ],
    },
    isLoading: false,
  };
}

/** Minimal valid props for SearchResultsWidget */
export function mockSearchResultsProps() {
  return {
    data: {
      query: 'force majeure clause',
      results: [
        {
          id: 'r1',
          title: 'Contract Analysis 2024',
          excerpt: 'The force majeure clause is triggered by...',
          score: 0.92,
          url: 'https://example.com/doc1',
        },
      ],
    },
    isLoading: false,
  };
}

/** Minimal valid props for AnalysisEditorWidget */
export function mockAnalysisEditorProps() {
  return {
    data: {
      sections: [
        {
          heading: 'Executive Summary',
          body: 'This agreement outlines the obligations of both parties.',
        },
        {
          heading: 'Key Risks',
          body: 'The indemnification clause is broader than standard.',
        },
      ],
      editable: false,
    },
    isLoading: false,
  };
}

/** Minimal valid props for ContractComparisonWidget */
export function mockContractComparisonProps() {
  return {
    data: {
      leftLabel: 'Original',
      rightLabel: 'Revised',
      clauses: [
        {
          id: 'clause-1',
          left: 'Termination requires 30-day notice.',
          right: 'Termination requires 60-day notice.',
          hasDelta: true,
        },
        {
          id: 'clause-2',
          left: 'Governing law: New York.',
          right: 'Governing law: New York.',
          hasDelta: false,
        },
      ],
    },
    isLoading: false,
  };
}

/** Minimal valid props for TimelineWidget */
export function mockTimelineProps() {
  return {
    data: {
      events: [
        {
          id: 'e1',
          date: '2024-01-15',
          label: 'Contract Signed',
          isMilestone: true,
        },
        {
          id: 'e2',
          date: '2024-03-01',
          label: 'First Review',
          description: 'Initial review by legal team',
        },
      ],
    },
    isLoading: false,
  };
}

/** Minimal valid props for DocumentCompareWidget */
export function mockDocumentCompareProps() {
  return {
    data: {
      leftLabel: 'Version 1',
      rightLabel: 'Version 2',
      lines: [
        {
          id: 'l1',
          leftText: 'The Buyer agrees to pay...',
          rightText: 'The Purchaser agrees to pay...',
          changeType: 'changed' as const,
        },
        {
          id: 'l2',
          leftText: 'on or before the due date.',
          rightText: 'on or before the due date.',
          changeType: 'unchanged' as const,
        },
      ],
      viewMode: 'side-by-side' as const,
    },
    isLoading: false,
  };
}

/** Minimal valid props for StatusSummaryWidget */
export function mockStatusSummaryProps() {
  return {
    data: {
      title: 'Contract Health',
      categories: [
        {
          id: 'cat-1',
          label: 'Compliance',
          status: 'success' as const,
          summary: 'All required clauses present.',
        },
        {
          id: 'cat-2',
          label: 'Risk',
          status: 'warning' as const,
          summary: 'Broad indemnification clause.',
        },
      ],
    },
    isLoading: false,
  };
}

/** Minimal valid props for RecommendationWidget */
export function mockRecommendationProps() {
  return {
    data: {
      recommendations: [
        {
          id: 'rec-1',
          priority: 'high' as const,
          text: 'Narrow the indemnification scope.',
          rationale: 'Current language creates unlimited liability.',
        },
        {
          id: 'rec-2',
          priority: 'medium' as const,
          text: 'Add a dispute resolution clause.',
        },
      ],
    },
    isLoading: false,
  };
}

/** Minimal valid props for ActionPlanWidget */
export function mockActionPlanProps() {
  return {
    data: {
      title: 'Action Plan',
      steps: [
        { id: 'step-1', label: 'Review redlines', completed: false },
        { id: 'step-2', label: 'Send to client', completed: false },
      ],
    },
    isLoading: false,
  };
}

/** Minimal valid props for ChartWidget */
export function mockChartProps() {
  return {
    data: {
      chartType: 'bar' as const,
      title: 'Matter Costs',
      xLabel: 'Month',
      yLabel: 'USD',
      series: [
        {
          name: 'Legal Fees',
          points: [
            { x: 'Jan', y: 5000 },
            { x: 'Feb', y: 7200 },
          ],
        },
      ],
    },
    isLoading: false,
  };
}

/** Minimal valid props for DataTableWidget */
export function mockDataTableProps() {
  return {
    data: {
      columns: [
        { key: 'name', label: 'Name', sortable: true },
        { key: 'amount', label: 'Amount', sortable: true },
      ],
      rows: [
        { name: 'Legal Fees', amount: 50000 },
        { name: 'Disbursements', amount: 12000 },
      ],
    },
    isLoading: false,
  };
}

// ---------------------------------------------------------------------------
// Source widget mock prop factories
// ---------------------------------------------------------------------------

/** Minimal valid props for DocumentViewerWidget */
export function mockDocumentViewerProps() {
  return {
    data: {
      documentUrl: 'https://example.com/doc.pdf',
      fileName: 'Agreement.pdf',
      mimeType: 'application/pdf',
      canDownload: false,
    },
    isLoading: false,
  };
}

/** Minimal valid props for WebSourceWidget */
export function mockWebSourceProps() {
  return {
    data: {
      url: 'https://example.com',
      title: 'Example Web Source',
    },
    isLoading: false,
  };
}

/** Minimal valid props for LegalLibraryWidget */
export function mockLegalLibraryProps() {
  return {
    data: {
      citation: 'Brown v. Board of Education, 347 U.S. 483 (1954)',
      court: 'U.S. Supreme Court',
      date: 'May 17, 1954',
      excerpt: 'We conclude that in the field of public education the doctrine of separate but equal has no place.',
      url: 'https://example.com/brown-v-board',
    },
    isLoading: false,
  };
}

/** Minimal valid props for CitationWidget */
export function mockCitationProps() {
  return {
    data: {
      citations: [
        {
          id: 'cit-1',
          index: 1,
          text: 'Smith v. Jones, 123 F.3d 456 (9th Cir. 2000)',
          sourceType: 'legal' as const,
          url: 'https://example.com/smith-v-jones',
        },
        {
          id: 'cit-2',
          index: 2,
          text: 'Contract Law Principles, 4th Ed.',
          sourceType: 'document' as const,
        },
      ],
    },
    isLoading: false,
  };
}

/** Minimal valid props for ImageViewerWidget */
export function mockImageViewerProps() {
  return {
    data: {
      src: 'https://example.com/image.png',
      alt: 'Contract diagram',
      caption: 'Figure 1: Agreement structure',
    },
    isLoading: false,
  };
}

/** Minimal valid props for CodeViewerWidget */
export function mockCodeViewerProps() {
  return {
    data: {
      code: 'const x = 1;\nconsole.log(x);\n',
      language: 'typescript',
      showLineNumbers: true,
    },
    isLoading: false,
  };
}

/**
 * triageColumnRenderers.test.tsx — Pillar E triage cell renderers (task 051, FR-E1).
 *
 * Covers the closed acceptance set: each triage field (category / priority /
 * summary / RI-confidence / review-outcome) renders in its column; priority drives
 * the DEFAULT grid sort via the shipped `sprk_gridconfiguration` `<order>` (the
 * framework sort, NOT a bespoke client re-sort); a missing/null value degrades to
 * a neutral placeholder (no crash, no "undefined"); and every cell renders under
 * the dark Fluent theme (ADR-021).
 */
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { TRIAGE_COLUMN_RENDERERS, TRIAGE_PRIORITY, REVIEW_OUTCOME } from '../triageColumnRenderers';
import needsReviewConfig from '../needs-review.gridconfiguration.json';

const EMPTY_RECORD: Record<string, unknown> = {};

function renderCell(field: string, value: unknown, theme = webLightTheme) {
  const node = TRIAGE_COLUMN_RENDERERS[field](value, EMPTY_RECORD) as React.ReactElement;
  return render(<FluentProvider theme={theme}>{node}</FluentProvider>);
}

describe('triageColumnRenderers', () => {
  it('renders each triage field in its column', () => {
    renderCell('sprk_triagecategory', 'Court / Filing');
    expect(screen.getByText('Court / Filing')).toBeInTheDocument();

    renderCell('sprk_triagepriority', TRIAGE_PRIORITY.URGENT);
    expect(screen.getByText('Urgent')).toBeInTheDocument();

    renderCell('sprk_triagesummary', 'Opposing counsel requests a two-week extension on the discovery deadline.');
    expect(screen.getByText(/Opposing counsel requests a two-week extension/)).toBeInTheDocument();

    renderCell('sprk_riconfidence', 0.92);
    expect(screen.getByText('92%')).toBeInTheDocument();

    renderCell('sprk_reviewoutcome', REVIEW_OUTCOME.ROUTE);
    expect(screen.getByText('Route')).toBeInTheDocument();
  });

  it('maps each priority + outcome choice value to its status label', () => {
    for (const [val, label] of [
      [TRIAGE_PRIORITY.URGENT, 'Urgent'],
      [TRIAGE_PRIORITY.HIGH, 'High'],
      [TRIAGE_PRIORITY.MEDIUM, 'Medium'],
      [TRIAGE_PRIORITY.LOW, 'Low'],
    ] as const) {
      const { unmount } = renderCell('sprk_triagepriority', val);
      expect(screen.getByText(label)).toBeInTheDocument();
      unmount();
    }
    for (const [val, label] of [
      [REVIEW_OUTCOME.FILE, 'File'],
      [REVIEW_OUTCOME.UPDATE, 'Update'],
      [REVIEW_OUTCOME.ROUTE, 'Route'],
      [REVIEW_OUTCOME.DISMISS, 'Dismiss'],
      [REVIEW_OUTCOME.PENDING, 'Pending'],
    ] as const) {
      const { unmount } = renderCell('sprk_reviewoutcome', val);
      expect(screen.getByText(label)).toBeInTheDocument();
      unmount();
    }
  });

  it('accepts an option-set value arriving as a numeric string', () => {
    renderCell('sprk_triagepriority', '100000000');
    expect(screen.getByText('Urgent')).toBeInTheDocument();
  });

  it('NEGATIVE — missing/null triage renders a neutral placeholder (no crash, no "undefined")', () => {
    for (const field of [
      'sprk_triagecategory',
      'sprk_triagepriority',
      'sprk_triagesummary',
      'sprk_riconfidence',
      'sprk_reviewoutcome',
    ]) {
      const { unmount } = renderCell(field, null);
      expect(screen.getByText('—')).toBeInTheDocument();
      expect(screen.queryByText(/undefined/)).not.toBeInTheDocument();
      unmount();
    }
    // An unrecognized choice integer also degrades to the placeholder.
    renderCell('sprk_reviewoutcome', 99999999);
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('drives the default grid sort by priority via the shipped config <order> (framework, not client re-sort)', () => {
    const fetchXml = needsReviewConfig.source.fetchXml;
    const priorityIdx = fetchXml.indexOf('<order attribute="sprk_triagepriority"');
    const receivedIdx = fetchXml.indexOf('<order attribute="sprk_receiveddate"');
    // Priority is the FIRST order key (ascending → Urgent=100000000 first), date secondary.
    expect(priorityIdx).toBeGreaterThanOrEqual(0);
    expect(fetchXml).toContain('<order attribute="sprk_triagepriority" descending="false" />');
    expect(priorityIdx).toBeLessThan(receivedIdx);
    // The triage fields are all selected + laid out as columns.
    for (const f of [
      'sprk_triagepriority',
      'sprk_reviewoutcome',
      'sprk_riconfidence',
      'sprk_triagecategory',
      'sprk_triagesummary',
    ]) {
      expect(fetchXml).toContain(`<attribute name="${f}" />`);
      expect(needsReviewConfig.source.layoutXml).toContain(`<cell name="${f}"`);
      expect(needsReviewConfig.columns).toHaveProperty(f);
    }
  });

  it('renders triage cells under the dark Fluent theme (ADR-021) with no console errors', () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    renderCell('sprk_triagepriority', TRIAGE_PRIORITY.URGENT, webDarkTheme);
    expect(screen.getByText('Urgent')).toBeInTheDocument();
    renderCell('sprk_riconfidence', 0.45, webDarkTheme);
    expect(screen.getByText('45%')).toBeInTheDocument();
    expect(errorSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
  });
});

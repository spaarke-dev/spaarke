/**
 * AnalysisHubWidget — 2b regarding pre-set unit tests (task 050, spec §12 / FR-14)
 *
 * Entry case 2b (new-in-record): when the hub is opened in a record context (a
 * Matter/Project modal launched via openSpaarkeAi — task 050 host routing + the
 * 052 ribbon), the Agreement Review card's `create-analysis-wizard` dispatch
 * pre-sets the wizard's regarding lookup to that parent record (`initialAssociation`).
 *
 * Negative (2a / cross-wire guard): with NO record context (entityContext null)
 * OR an unsupported host entity type, NO `initialAssociation` is included — only
 * 2b forces the parent regarding (project constraint: do not cross-wire the cases).
 *
 * `entityContext` is read from `useAiSession()`; the mock is a mutable ref so each
 * test can set the host record context before rendering.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

const mockDispatch = jest.fn();
jest.mock('../../../events/useDispatchPaneEvent', () => ({
  useDispatchPaneEvent: () => mockDispatch,
}));

// Mutable entityContext so each test controls the host record context.
let mockEntityContext: { entityType?: string; entityId?: string } | null = null;
jest.mock('../../../providers/useAiSession', () => ({
  useAiSession: () => ({
    bffBaseUrl: 'https://bff.example.com',
    authenticatedFetch: jest.fn(),
    entityContext: mockEntityContext,
  }),
}));

jest.mock('../DataverseEntityViewWidget', () => ({
  DataverseEntityViewWidget: () => <div data-testid="mock-dataverse-entity-view-widget" />,
}));

import { AnalysisHubWidget } from '../AnalysisHubWidget';

function renderHub(theme: typeof webLightTheme = webLightTheme) {
  return render(
    <FluentProvider theme={theme}>
      <AnalysisHubWidget data={{}} widgetType="analysis-hub" />
    </FluentProvider>
  );
}

function clickAgreementReview(): void {
  fireEvent.click(screen.getByRole('button', { name: /Agreement Review/i }));
}

beforeEach(() => {
  mockDispatch.mockClear();
  mockEntityContext = null;
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('AnalysisHubWidget — 2b regarding pre-set (task 050, FR-14)', () => {
  it('2b: with a Matter record context, the Agreement Review dispatch pre-sets regarding=parent (initialAssociation)', () => {
    mockEntityContext = { entityType: 'sprk_matter', entityId: 'matter-guid-1' };
    renderHub();
    clickAgreementReview();

    expect(mockDispatch).toHaveBeenCalledWith('workspace', {
      type: 'widget_load',
      widgetType: 'create-analysis-wizard',
      widgetData: {
        workTypeValue: 100000000,
        workTypeLabel: 'Agreement Review',
        initialAssociation: {
          entityType: 'sprk_matter',
          recordId: 'matter-guid-1',
          recordName: 'matter-guid-1',
        },
      },
      displayName: 'Create Agreement Review Analysis',
    });
  });

  it('2b: a Project record context is also pre-set as the regarding parent', () => {
    mockEntityContext = { entityType: 'sprk_project', entityId: 'project-guid-9' };
    renderHub();
    clickAgreementReview();

    const call = mockDispatch.mock.calls.find(([ch]) => ch === 'workspace');
    expect(call?.[1]?.widgetData?.initialAssociation).toEqual({
      entityType: 'sprk_project',
      recordId: 'project-guid-9',
      recordName: 'project-guid-9',
    });
  });

  it('2a: with NO record context, the dispatch omits initialAssociation (no forced regarding)', () => {
    mockEntityContext = null;
    renderHub();
    clickAgreementReview();

    expect(mockDispatch).toHaveBeenCalledWith('workspace', {
      type: 'widget_load',
      widgetType: 'create-analysis-wizard',
      widgetData: {
        workTypeValue: 100000000,
        workTypeLabel: 'Agreement Review',
      },
      displayName: 'Create Agreement Review Analysis',
    });
  });

  it('negative: an unsupported host entity type does NOT pre-set regarding (only Matter/Project/Document)', () => {
    mockEntityContext = { entityType: 'account', entityId: 'account-guid-1' };
    renderHub();
    clickAgreementReview();

    const call = mockDispatch.mock.calls.find(([ch]) => ch === 'workspace');
    expect(call?.[1]?.widgetData?.initialAssociation).toBeUndefined();
  });

  it('ADR-021: the 2b pre-set dispatch works under the dark theme', () => {
    mockEntityContext = { entityType: 'sprk_matter', entityId: 'matter-guid-dark' };
    renderHub(webDarkTheme);
    clickAgreementReview();

    const call = mockDispatch.mock.calls.find(([ch]) => ch === 'workspace');
    expect(call?.[1]?.widgetData?.initialAssociation?.recordId).toBe('matter-guid-dark');
  });
});

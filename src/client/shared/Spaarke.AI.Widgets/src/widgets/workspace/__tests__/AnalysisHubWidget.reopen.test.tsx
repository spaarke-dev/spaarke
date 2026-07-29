/**
 * AnalysisHubWidget — row-reopen unit tests (task 031, spec FR-11)
 *
 * Exercises the grid row-open flow per the task's ui-tests + acceptance criteria:
 *
 *   1. Selecting a row for an Analysis with a bound session dispatches
 *      `conversation.session_switch` (the additive ADR-030 discriminant ConversationPane's
 *      existing History-select handler consumes) — the "reopen restores session history"
 *      contract.
 *   2. When the Analysis has a linked file (`_sprk_documentid_value`), a `document-viewer`
 *      `widget_load` is ALSO dispatched on the `workspace` channel (ADR-007 hop) — the
 *      "files restore" contract. No SPE pointer is read off the row beyond the
 *      `sprk_documentid` lookup field.
 *   3. Negative — stale/no-session case: a 404 from the by-analysis lookup degrades to an
 *      inline notice and dispatches NOTHING (no empty session is silently created, per the
 *      task's escalation trigger).
 *   4. A network/lookup error degrades gracefully (no throw, no dispatch).
 *   5. ADR-021: the reopen notice renders correctly under both light and dark Fluent themes.
 *
 * `DataverseEntityViewWidget` is mocked at the module boundary (same convention as
 * `AnalysisHubWidget.test.tsx`) but this mock ALSO exposes a button that invokes the
 * `onRecordOpen` callback passed through `data` — simulating a DataGrid row-open without
 * needing a full Xrm.WebApi + DataGrid harness.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

const mockDispatch = jest.fn();
jest.mock('../../../events/useDispatchPaneEvent', () => ({
  useDispatchPaneEvent: () => mockDispatch,
}));

const mockAuthenticatedFetch = jest.fn();
jest.mock('../../../providers/useAiSession', () => ({
  useAiSession: () => ({
    bffBaseUrl: 'https://bff.example.com',
    authenticatedFetch: (...args: unknown[]) => mockAuthenticatedFetch(...args),
  }),
}));

const ANALYSIS_ROW_ID = '11111111-1111-1111-1111-111111111111';
const SESSION_ID = 'session-abc-123';

interface MockDataverseEntityViewWidgetData {
  configId?: string;
  onRecordOpen?: (recordId: string, record: Record<string, unknown>) => void;
}

let currentRecord: Record<string, unknown> = { sprk_analysisid: ANALYSIS_ROW_ID };

jest.mock('../DataverseEntityViewWidget', () => ({
  DataverseEntityViewWidget: (props: { data?: MockDataverseEntityViewWidgetData; widgetType: string }) => (
    <button
      type="button"
      data-testid="mock-grid-row"
      onClick={() => props.data?.onRecordOpen?.(ANALYSIS_ROW_ID, currentRecord)}
    >
      Open row
    </button>
  ),
}));

import { AnalysisHubWidget } from '../AnalysisHubWidget';

// ---------------------------------------------------------------------------
// Test harness
// ---------------------------------------------------------------------------

function renderHub(theme: typeof webLightTheme = webLightTheme) {
  return render(
    <FluentProvider theme={theme}>
      <AnalysisHubWidget data={{}} widgetType="analysis-hub" />
    </FluentProvider>
  );
}

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  } as unknown as Response;
}

beforeEach(() => {
  mockDispatch.mockClear();
  mockAuthenticatedFetch.mockReset();
  currentRecord = { sprk_analysisid: ANALYSIS_ROW_ID };
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('AnalysisHubWidget — row reopen (task 031, FR-11)', () => {
  it('reopens a bound session: dispatches conversation.session_switch with the resolved sessionId', async () => {
    mockAuthenticatedFetch.mockResolvedValueOnce(
      jsonResponse({ sessionId: SESSION_ID, messageCount: 4, isArchived: false, createdOn: '2026-07-01T00:00:00Z' })
    );

    renderHub();
    fireEvent.click(screen.getByTestId('mock-grid-row'));

    await waitFor(() => {
      expect(mockDispatch).toHaveBeenCalledWith('conversation', {
        type: 'session_switch',
        sessionId: SESSION_ID,
      });
    });

    // Resolves via the new BFF lookup — the task-020 FK, not the retired legacy path.
    // (buildBffApiUrl is jest-mocked in this package to a plain concat — see
    // src/__mocks__/@spaarke/auth.ts — so the assertion checks the path segment only;
    // the real implementation additionally normalizes in the `/api/` prefix at runtime.)
    expect(mockAuthenticatedFetch).toHaveBeenCalledWith(
      expect.stringContaining('/ai/chat/sessions/by-analysis/'),
      expect.any(Object)
    );
    expect(mockAuthenticatedFetch.mock.calls[0][0]).toContain(ANALYSIS_ROW_ID);

    // No reopen notice on the success path.
    expect(screen.queryByTestId('analysis-hub-reopen-notice')).not.toBeInTheDocument();
  });

  it('reopens a bound session with a linked file: ALSO dispatches a document-viewer widget_load (ADR-007 hop)', async () => {
    currentRecord = {
      sprk_analysisid: ANALYSIS_ROW_ID,
      _sprk_documentid_value: 'doc-guid-1',
      '_sprk_documentid_value@OData.Community.Display.V1.FormattedValue': 'Acme MSA.docx',
      sprk_name: 'Acme Agreement Review',
    };
    mockAuthenticatedFetch.mockResolvedValueOnce(
      jsonResponse({ sessionId: SESSION_ID, messageCount: 4, isArchived: false, createdOn: '2026-07-01T00:00:00Z' })
    );

    renderHub();
    fireEvent.click(screen.getByTestId('mock-grid-row'));

    await waitFor(() => {
      expect(mockDispatch).toHaveBeenCalledWith(
        'workspace',
        expect.objectContaining({
          type: 'widget_load',
          widgetType: 'document-viewer',
          displayName: 'Acme MSA.docx',
        })
      );
    });

    const workspaceCall = mockDispatch.mock.calls.find(([channel]) => channel === 'workspace');
    expect(workspaceCall).toBeDefined();
    const widgetData = workspaceCall?.[1]?.widgetData as { documentId?: string; fetchPreviewUrl?: () => unknown };
    expect(widgetData.documentId).toBe('doc-guid-1');
    expect(typeof widgetData.fetchPreviewUrl).toBe('function');

    // The session_switch dispatch still fires alongside the file restore.
    expect(mockDispatch).toHaveBeenCalledWith('conversation', {
      type: 'session_switch',
      sessionId: SESSION_ID,
    });
  });

  it('reopens a bound session with NO linked file: does not dispatch a document-viewer widget_load', async () => {
    currentRecord = { sprk_analysisid: ANALYSIS_ROW_ID }; // no _sprk_documentid_value
    mockAuthenticatedFetch.mockResolvedValueOnce(
      jsonResponse({ sessionId: SESSION_ID, messageCount: 0, isArchived: false, createdOn: null })
    );

    renderHub();
    fireEvent.click(screen.getByTestId('mock-grid-row'));

    await waitFor(() => {
      expect(mockDispatch).toHaveBeenCalledWith('conversation', {
        type: 'session_switch',
        sessionId: SESSION_ID,
      });
    });

    expect(mockDispatch).not.toHaveBeenCalledWith('workspace', expect.anything());
  });

  it('negative — stale/no-session (404): degrades to an inline notice, dispatches NOTHING (no empty session is minted)', async () => {
    mockAuthenticatedFetch.mockResolvedValueOnce(jsonResponse({ error: 'not found' }, 404));

    renderHub();
    fireEvent.click(screen.getByTestId('mock-grid-row'));

    await waitFor(() => {
      expect(screen.getByTestId('analysis-hub-reopen-notice')).toBeInTheDocument();
    });

    expect(mockDispatch).not.toHaveBeenCalled();
  });

  it('negative — lookup error: degrades gracefully (no throw, no dispatch, notice shown)', async () => {
    mockAuthenticatedFetch.mockRejectedValueOnce(new Error('network down'));

    renderHub();
    expect(() => fireEvent.click(screen.getByTestId('mock-grid-row'))).not.toThrow();

    await waitFor(() => {
      expect(screen.getByTestId('analysis-hub-reopen-notice')).toBeInTheDocument();
    });

    expect(mockDispatch).not.toHaveBeenCalled();
  });

  it('ADR-021: the reopen notice renders correctly under both light and dark Fluent themes', async () => {
    mockAuthenticatedFetch.mockResolvedValueOnce(jsonResponse({ error: 'not found' }, 404));
    const { unmount } = renderHub(webLightTheme);
    fireEvent.click(screen.getByTestId('mock-grid-row'));
    await waitFor(() => {
      expect(screen.getByTestId('analysis-hub-reopen-notice')).toBeInTheDocument();
    });
    unmount();

    mockAuthenticatedFetch.mockResolvedValueOnce(jsonResponse({ error: 'not found' }, 404));
    renderHub(webDarkTheme);
    fireEvent.click(screen.getByTestId('mock-grid-row'));
    await waitFor(() => {
      expect(screen.getByTestId('analysis-hub-reopen-notice')).toBeInTheDocument();
    });
  });
});

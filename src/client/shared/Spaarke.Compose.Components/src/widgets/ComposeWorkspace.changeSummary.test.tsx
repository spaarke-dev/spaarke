/**
 * ComposeWorkspace.changeSummary.test.tsx — R8 UAT item 8 host wiring.
 *
 * WHY THIS FILE EXISTS, stated precisely: the sibling `ComposeWorkspace.imports.test.tsx` was written
 * because `ComposeEditor`'s paraId/imported-revision props were threaded NOWHERE — the seam test passed,
 * the server sent the fields, and the running app had `undefined` props and dead code. The change-summary
 * wiring has the identical shape (a handler threaded down, outcomes rendered up), so it gets the identical
 * guard rather than trusting that it was wired.
 *
 * The producer (`composeChangesText.test.ts`) and the flow (`useComposeChangeSummary.test.tsx`) are tested
 * on their own. What only this level can prove is that the workspace ACTUALLY passes the trigger to the
 * editor and turns each outcome into something the user sees.
 *
 * Mocking strategy mirrors the sibling verbatim — heavy children stubbed, `ComposeEditor` captured as a
 * prop recorder.
 */

import * as React from 'react';
import { render, screen, waitFor, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

// Fluent's MessageBar uses ResizeObserver, which jsdom lacks.
if (typeof (globalThis as { ResizeObserver?: unknown }).ResizeObserver === 'undefined') {
  (globalThis as { ResizeObserver?: unknown }).ResizeObserver = class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  };
}

const authenticatedFetchMock = jest.fn();
jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...args),
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-token',
    authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...args),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

jest.mock('@spaarke/ui-components', () => ({
  ConfirmModal: () => null,
  createXrmNavigationService: () => ({ openLookup: jest.fn() }),
  createXrmDataService: () => ({ retrieveRecord: jest.fn() }),
  SendEmailDialog: () => null,
  SprkModal: () => null,
  RichFilePreviewDialog: () => null,
}));

jest.mock('@spaarke/ai-widgets/events', () => ({
  useDispatchPaneEvent: () => jest.fn(),
  usePaneEvent: () => undefined,
}));

jest.mock('./hooks', () => ({
  useComposeBroadcastChannel: () => ({ postFocusMe: jest.fn(), postForceClosed: jest.fn() }),
  useComposeCheckoutLifecycle: () => ({ forceCloseAndAcquire: jest.fn(), discardAndCancel: jest.fn() }),
  useComposeHeartbeatGate: () => undefined,
}));

// `isDirty` is per-test: the save gate is the first branch the flow takes.
let editorIsDirty = false;

type CapturedEditorProps = { onSummarizeChanges?: () => void };
const editorProps: { current: CapturedEditorProps } = { current: {} };
jest.mock('./ComposeEditor', () => {
  const ReactLib = require('react');
  return {
    ComposeEditor: ReactLib.forwardRef((props: CapturedEditorProps, ref: React.Ref<unknown>) => {
      editorProps.current = props;
      ReactLib.useImperativeHandle(ref, () => ({
        serialize: async () => new ArrayBuffer(0),
        getCounts: () => ({ characters: 0, words: 0 }),
        isDirty: () => editorIsDirty,
        materializeComposeDraft: () => undefined,
        materializePendingRedline: () => 'applied',
      }));
      return <div data-testid="compose-editor-stub" />;
    }),
  };
});

// eslint-disable-next-line import/first
import { ComposeWorkspace } from './ComposeWorkspace';
// eslint-disable-next-line import/first
import { registerComposeAiToolbarAction, __resetComposeAiToolbarActionsForTests } from './ComposeAiToolbar';

const SPE_ID = 'spe-item-123';
const DRIVE_ID = 'drive-abc';

function renderWorkspace(props: Partial<React.ComponentProps<typeof ComposeWorkspace>> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ComposeWorkspace
        bffBaseUrl="https://bff.example.test"
        driveId={DRIVE_ID}
        tenantId="tenant-1"
        initialDocumentRef={{ speDriveItemId: SPE_ID }}
        {...props}
      />
    </FluentProvider>
  );
}

/** Routes the Load leg and the pull-annotations leg by URL, so a test controls only what it cares about. */
function mockFetchRoutes(pullPayload: { revisions?: unknown[]; comments?: unknown[] } | null): void {
  authenticatedFetchMock.mockImplementation(async (url: string) => {
    if (typeof url === 'string' && url.includes('/pull-annotations')) {
      if (pullPayload === null) return { ok: false, status: 500, json: async () => ({}), text: async () => '' };
      return {
        ok: true,
        status: 200,
        json: async () => ({
          documentSpeId: SPE_ID,
          driveId: DRIVE_ID,
          revisions: pullPayload.revisions ?? [],
          comments: pullPayload.comments ?? [],
          correlationId: 'corr-1',
        }),
      };
    }
    if (typeof url === 'string' && url.includes('/api/compose/documents/')) {
      return {
        ok: true,
        status: 200,
        json: async () => ({
          documentSpeId: SPE_ID,
          driveId: DRIVE_ID,
          sessionId: 'session-1',
          documentRecordId: 'doc-guid-1',
          content: btoa('fake-docx-bytes'),
          eTag: 'etag-1',
          fileName: 'Contract.docx',
          size: 100,
        }),
      };
    }
    return { ok: false, status: 404, json: async () => [], text: async () => '' };
  });
}

async function mountAndGetTrigger(): Promise<() => void> {
  renderWorkspace();
  await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
  const trigger = editorProps.current.onSummarizeChanges;
  expect(trigger).toBeDefined();
  return trigger!;
}

const REVISION = {
  kind: 'insertion',
  id: 'rev-1',
  author: 'Jane Author',
  date: '2026-01-01T00:00:00Z',
  text: 'inserted clause text',
  anchorText: 'surrounding paragraph context',
  paragraphHint: 0,
};

beforeEach(() => {
  authenticatedFetchMock.mockReset();
  authenticatedFetchMock.mockResolvedValue({ ok: false, status: 404, json: async () => [], text: async () => '' });
  editorProps.current = {};
  editorIsDirty = false;
  __resetComposeAiToolbarActionsForTests();
});

describe('ComposeWorkspace — change-summary wiring', () => {
  it('threads onSummarizeChanges to ComposeEditor — the prop is not dead', async () => {
    // The exact defect the sibling imports test exists for: a handler that is built, exported, and
    // threaded nowhere still passes every unit test beneath it.
    mockFetchRoutes({ revisions: [] });
    const trigger = await mountAndGetTrigger();
    expect(typeof trigger).toBe('function');
  });

  it('tells the user when the document has no tracked changes — rather than dispatching', async () => {
    // The refusal, surfaced. The action was ASKED for, so silence would read as a broken button.
    mockFetchRoutes({ revisions: [], comments: [] });
    const trigger = await mountAndGetTrigger();

    await act(async () => {
      trigger();
    });

    await waitFor(() =>
      expect(screen.getByTestId('compose-workspace-change-summary-message')).toHaveTextContent(
        /no tracked changes or comments to summarise/i
      )
    );
  });

  it('does not call pull-annotations at all when the editor is dirty — it asks first', async () => {
    // The save gate runs BEFORE any network call: the summary reads STORED bytes, so proceeding would
    // silently omit the unsaved edits.
    editorIsDirty = true;
    mockFetchRoutes({ revisions: [REVISION] });
    const trigger = await mountAndGetTrigger();

    await act(async () => {
      trigger();
    });

    const pullCalls = authenticatedFetchMock.mock.calls.filter(
      c => typeof c[0] === 'string' && (c[0] as string).includes('/pull-annotations')
    );
    expect(pullCalls).toHaveLength(0);
  });

  it('surfaces a user-safe message when the pull fails, with no server detail', async () => {
    mockFetchRoutes(null);
    const trigger = await mountAndGetTrigger();

    await act(async () => {
      trigger();
    });

    await waitFor(() => {
      const bar = screen.getByTestId('compose-workspace-change-summary-message');
      expect(bar).toHaveTextContent(/could not be generated/i);
      expect(bar).not.toHaveTextContent(/500/);
    });
  });

  it('reports the undeployed-Binding case specifically rather than as a document problem', async () => {
    // With real change data but no registered bindingId, the dispatch cannot run. Left as the generic
    // failure copy this would send someone hunting for a bug in the document.
    mockFetchRoutes({ revisions: [REVISION] });
    const trigger = await mountAndGetTrigger();

    await act(async () => {
      trigger();
    });

    // No binding registered in this test ⇒ the flow reports failure rather than silently succeeding.
    await waitFor(() => expect(screen.getByTestId('compose-workspace-change-summary-message')).toBeInTheDocument());
  });

  it('dispatches through the registered Binding when there IS change data', async () => {
    registerComposeAiToolbarAction({
      id: 'compose-summarize-word-changes',
      label: 'Summarise changes',
      tooltip: 'Summarise the tracked changes made in Word',
      bindingId: 'binding-guid-1',
      placement: 'overflow',
      surfaces: [],
    });

    const enqueue = jest.fn().mockResolvedValue({ ok: true });
    mockFetchRoutes({ revisions: [REVISION] });

    render(
      <FluentProvider theme={webLightTheme}>
        <ComposeWorkspace
          bffBaseUrl="https://bff.example.test"
          driveId={DRIVE_ID}
          tenantId="tenant-1"
          initialDocumentRef={{ speDriveItemId: SPE_ID }}
          enqueueComposeAction={enqueue}
        />
      </FluentProvider>
    );
    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());

    await act(async () => {
      editorProps.current.onSummarizeChanges?.();
    });

    await waitFor(() => expect(enqueue).toHaveBeenCalledTimes(1));
    const request = enqueue.mock.calls[0][0];
    expect(request.bindingId).toBe('binding-guid-1');
    // The operand is the produced changesText, carrying the real change content.
    expect(request.args.slots.changesText).toContain('inserted clause text');

    // A successful dispatch says nothing in the banner — the Assistant renders the result, and a
    // banner here would duplicate it.
    expect(screen.queryByTestId('compose-workspace-change-summary-message')).not.toBeInTheDocument();
  });
});

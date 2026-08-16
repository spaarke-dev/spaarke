/**
 * ComposeWorkspace.draftAutosave.test.tsx — FR-03 (spaarkeai-compose-r7 task 040)
 *
 * Verifies the CLIENT-ONLY draft-safe autosave seam end-to-end at the workspace level:
 *   1. A dirty born-in-editor doc auto-drafts to localStorage on the ~15s tick — keyed by the
 *      task-010 logical id — and does NOT hit the BFF (NFR-03: no create-on-save / save / persist).
 *   2. On reopen with NO real mount door, a persisted draft is recovered into the editor (populated,
 *      not the blank empty-state), reusing the recovered logical id.
 *   3. A real mount door (stored-document) WINS — recovery never clobbers a loaded server doc.
 *
 * CI-only, same seam-isolation as ComposeWorkspace.draftSeed.test.tsx (`@spaarke/auth` is
 * workspace-resolved). Pure store mechanics (save/get/clear/match-gating/corruption) are covered
 * standalone by composeDraftStore.test.ts.
 */

import * as React from 'react';
import { render, screen, act, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import {
  COMPOSE_DRAFT_CONTENT_KEY,
  saveComposeDraft,
  getComposeDraft,
} from './composeDraftStore';
import { COMPOSE_ACTIVE_DRAFT_ID_KEY, persistActiveComposeLogicalId } from './composeIdentity';

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
  ApiError: class ApiError extends Error {
    status: number;
    constructor(message: string, status = 0) {
      super(message);
      this.status = status;
    }
  },
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-token',
    authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...args),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
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

jest.mock('./ComposeToolbar', () => ({
  ComposeToolbar: () => <div data-testid="compose-toolbar-stub" />,
}));

// ComposeEditor stub: dirty + a distinctive getDraftHtml() the autosave tick snapshots; captures the
// initialHtml it is handed (the recovery-populated signal).
const editorProps: { initialHtml: string | null | undefined } = { initialHtml: undefined };
const DRAFT_HTML = '<p>autosaved working draft body</p>';
jest.mock('./ComposeEditor', () => {
  const ReactLib = require('react');
  return {
    ComposeEditor: ReactLib.forwardRef(
      (props: { initialHtml?: string | null; onDirtyChange?: (d: boolean) => void }, ref: React.Ref<unknown>) => {
        editorProps.initialHtml = props.initialHtml;
        ReactLib.useEffect(() => {
          props.onDirtyChange?.(true);
        }, []);
        ReactLib.useImperativeHandle(ref, () => ({
          isDirty: () => true,
          getDraftHtml: () => DRAFT_HTML,
          serializeOperationLog: () => ({ orderedOps: [], baseVersion: null }),
          commitSaved: jest.fn(),
          getBaselineParaIdMap: () => [],
          getAnchoredComments: () => [],
          getRedlineAnnotations: () => [],
          hasPendingRedlines: () => false,
          buildContentModel: () => ({ blocks: [], comments: [] }),
          getCounts: () => ({ characters: 0, words: 0 }),
          materializeComposeDraft: () => undefined,
        }));
        return <div data-testid="compose-editor-stub" dangerouslySetInnerHTML={{ __html: props.initialHtml ?? '' }} />;
      }
    ),
  };
});

// eslint-disable-next-line import/first
import { ComposeWorkspace } from './ComposeWorkspace';

function renderWorkspace(props: Partial<React.ComponentProps<typeof ComposeWorkspace>> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ComposeWorkspace bffBaseUrl="https://bff.example.test" driveId="" tenantId="tenant-1" {...props} />
    </FluentProvider>
  );
}

/** BFF calls that would mean the draft touched the server (NFR-03 forbids this on autosave). */
const persistenceCalls = () =>
  authenticatedFetchMock.mock.calls.filter(([u]) =>
    ['create-on-save', '/save', '/upload', '/persist'].some(frag => String(u).includes(frag))
  );

beforeEach(() => {
  window.localStorage.clear();
  authenticatedFetchMock.mockReset();
  // Born-in-editor mounts arm a harmless read-only compose-outputs GET probe — resolve it benignly.
  authenticatedFetchMock.mockResolvedValue({ ok: false, status: 404, json: async () => [] });
  editorProps.initialHtml = undefined;
});

afterEach(() => {
  jest.useRealTimers();
});

describe('ComposeWorkspace — FR-03 draft-safe autosave (client-only)', () => {
  it('auto-drafts a dirty doc to localStorage on the ~15s tick and NEVER writes to the BFF', () => {
    jest.useFakeTimers();
    act(() => {
      renderWorkspace({ initialDraftRef: { html: '<p>seed</p>' } });
    });
    // Born-in-editor inline mount is synchronous → editor is present.
    expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument();
    // Nothing persisted before the first tick.
    expect(window.localStorage.getItem(COMPOSE_DRAFT_CONTENT_KEY)).toBeNull();

    act(() => {
      jest.advanceTimersByTime(15000);
    });

    // A draft was written, keyed by the mount's minted logical id (the active-draft slot).
    const activeId = window.localStorage.getItem(COMPOSE_ACTIVE_DRAFT_ID_KEY);
    expect(activeId).toBeTruthy();
    const draft = getComposeDraft(activeId!);
    expect(draft).not.toBeNull();
    expect(draft?.html).toBe(DRAFT_HTML);

    // NFR-03: the autosave tick never created an SPE version / hit a persistence endpoint.
    expect(persistenceCalls()).toHaveLength(0);
  });

  it('recovers a persisted draft into the editor on reopen with no real mount door', () => {
    // Simulate a prior session's crash: an active draft id + its content are in storage.
    persistActiveComposeLogicalId('lid-recover-1');
    saveComposeDraft('lid-recover-1', '<p>recovered from a prior crash</p>', 'My Draft.docx');

    act(() => {
      renderWorkspace(); // NO initial doc/upload/draft ref → recovery owns the mount
    });

    // Editor mounts POPULATED with the recovered draft body (not the blank empty-state).
    expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument();
    expect(editorProps.initialHtml).toBe('<p>recovered from a prior crash</p>');
  });

  it('does NOT recover when a real mount door (stored document) is present', async () => {
    persistActiveComposeLogicalId('lid-recover-2');
    saveComposeDraft('lid-recover-2', '<p>should be ignored</p>');

    act(() => {
      renderWorkspace({ initialDocumentRef: { speDriveItemId: 'spe-1', fileName: 'stored.docx' }, driveId: 'drive-1' });
    });

    // The stored-document Load door owns the mount — it fetches; recovery deferred (non-destructive).
    await waitFor(() => expect(authenticatedFetchMock).toHaveBeenCalled());
    expect(editorProps.initialHtml).not.toBe('<p>should be ignored</p>');
  });
});

/**
 * ComposeWorkspace — a failed Open in Word is shown in the banner stack.
 *
 * The workspace binds Open in Word (web / desktop) through the REAL `useDocumentActions`
 * (@spaarke/document-operations source), whose fetch is `@spaarke/auth`'s authenticatedFetch. That fetch
 * THROWS ApiError (status + ProblemDetails) for a non-2xx; the hook turns it into "Couldn't open the
 * document: <reason>" as `actionError`. Before the fix the workspace never read `actionError`, so a failed
 * Open in Word showed nothing — the "Opening in Word…" indicator just stopped. Errors here are the REAL
 * `ApiError` class (Spaarke.Auth source), thrown the way authenticatedFetch throws them.
 */

import * as React from 'react';
import { render, screen, waitFor, act, fireEvent } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

// Fluent's MessageBar uses ResizeObserver, which jsdom lacks.
if (typeof (globalThis as { ResizeObserver?: unknown }).ResizeObserver === 'undefined') {
  (globalThis as { ResizeObserver?: unknown }).ResizeObserver = class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  };
}

const SPE_ID = 'drive-item-doc-1';
const DRIVE_ID = 'drive-1';
const DOC_SESSION = '00000000-0000-0000-0000-0000000d0c08';

/** What the open-links request does this test: throw this error, or answer with links. */
const config: { openLinksError: Error | null } = { openLinksError: null };

type RealAuth = typeof import('../../../Spaarke.Auth/src/index');
const realAuth = (): RealAuth => jest.requireActual('../../../Spaarke.Auth/src/index') as RealAuth;

const authenticatedFetchMock = jest.fn(async (url: string): Promise<Response> => {
  if (url.includes('/open-links')) {
    if (config.openLinksError) throw config.openLinksError;
    return {
      ok: true,
      status: 200,
      json: async () => ({ webUrl: 'https://web/doc', mimeType: 'application/msword', fileName: 'contract.docx' }),
    } as unknown as Response;
  }
  if (url.includes('/api/compose/documents/')) {
    return {
      ok: true,
      status: 200,
      json: async () => ({
        documentSpeId: SPE_ID,
        driveId: DRIVE_ID,
        sessionId: DOC_SESSION,
        documentRecordId: 'sprk-doc-1',
        content: 'UEsDBA==',
        eTag: 'etag-1',
        versionId: 'v-load',
        fileName: 'contract.docx',
        size: 500,
        anchoredAnnotations: [],
        definedTermsTracking: [],
        actionHistory: [],
      }),
    } as unknown as Response;
  }
  // Everything else (ledger probes etc.) → a benign 404, as authenticatedFetch expresses one.
  throw new (realAuth().ApiError)('Not found', 404);
});

// NO `virtual: true` on the sibling-lib mocks (see jest.config.js "Sibling `@spaarke/*` resolution").
// `@spaarke/auth`: the REAL module from source (error classes + guards), with only the fetch replaced.
jest.mock('@spaarke/auth', () => {
  const actual = jest.requireActual('../../../Spaarke.Auth/src/index');
  return {
    ...actual,
    authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...(args as [string])),
    useAuth: () => ({
      isAuthenticated: true,
      getAccessToken: async () => 'test-token',
      authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...(args as [string])),
      tenantId: 'test-tenant',
      logout: jest.fn(),
    }),
  };
});
// The REAL hook, from source (its dist is ESM).
jest.mock('@spaarke/document-operations', () => jest.requireActual('../../../Spaarke.DocumentOperations/src/index'));
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
jest.mock('./useComposeWordShuttle', () => ({
  useComposePullAnnotations: () => ({ pull: jest.fn() }),
  useComposeCheckChanges: () => ({ checkChanges: jest.fn() }),
  anchoredAnnotationsToPriorAnchors: () => [],
}));
jest.mock('./useComposeReanchor', () => ({
  useComposeReanchor: () => ({ summary: null, reanchor: jest.fn(), reset: jest.fn() }),
}));
jest.mock('./ComposeToolbar', () => ({
  ComposeToolbar: () => <div data-testid="compose-toolbar-stub" />,
}));

// ComposeEditor stub — a CLEAN document (no unsaved edits, no redlines), so Open in Word does not flush a
// save first. Captures the Word handlers the workspace threads in.
const editorProps: {
  current: { onOpenInWord?: () => void; onOpenInWordDesktop?: () => void; wordActionsDisabled?: boolean };
} = { current: {} };
jest.mock('./ComposeEditor', () => {
  const ReactLib = require('react');
  return {
    ComposeEditor: ReactLib.forwardRef(
      (
        props: { onOpenInWord?: () => void; onOpenInWordDesktop?: () => void; wordActionsDisabled?: boolean },
        ref: React.Ref<unknown>
      ) => {
        editorProps.current = props;
        ReactLib.useImperativeHandle(ref, () => ({
          isDirty: () => false,
          serializeOperationLog: () => ({ orderedOps: [], baseVersion: 'v-load' }),
          commitSaved: jest.fn(),
          getBaselineParaIdMap: () => [],
          getAnchoredComments: () => [],
          getRedlineAnnotations: () => [],
          hasPendingRedlines: () => false,
          buildContentModel: () => ({ paragraphs: [] }),
          getCounts: () => ({ characters: 0, words: 0 }),
        }));
        return <div data-testid="compose-editor-stub" />;
      }
    ),
  };
});

// Import AFTER mocks.
// eslint-disable-next-line import/first
import { ComposeWorkspace } from './ComposeWorkspace';

function renderWorkspace(theme = webLightTheme) {
  return render(
    <FluentProvider theme={theme}>
      <ComposeWorkspace
        initialDocumentRef={{ speDriveItemId: SPE_ID, sprkDocumentId: 'sprk-doc-1', fileName: 'contract.docx' }}
        initialSessionId={DOC_SESSION}
        bffBaseUrl="https://bff.example.test"
        driveId={DRIVE_ID}
        tenantId="tenant-1"
      />
    </FluentProvider>
  );
}

async function mountLoaded(theme = webLightTheme): Promise<void> {
  renderWorkspace(theme);
  await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
  await waitFor(() => expect(editorProps.current.wordActionsDisabled).toBe(false));
}

async function click(handler: (() => void) | undefined): Promise<void> {
  await act(async () => {
    handler?.();
    await Promise.resolve();
    await Promise.resolve();
  });
}

beforeEach(() => {
  authenticatedFetchMock.mockClear();
  config.openLinksError = null;
  editorProps.current = {};
  jest.spyOn(window, 'open').mockImplementation(() => null);
});

afterEach(() => {
  jest.restoreAllMocks();
});

describe('ComposeWorkspace — a failed Open in Word is shown', () => {
  it('Open in Word (web): a thrown 404 shows the server\'s detail under an "Open in Word" banner', async () => {
    const detail = 'The document has no file in SharePoint Embedded.';
    config.openLinksError = new (realAuth().ApiError)(detail, 404, { title: 'Not Found', status: 404, detail });
    await mountLoaded();

    await click(editorProps.current.onOpenInWord);

    const banner = await screen.findByTestId('compose-workspace-word-action-error');
    expect(banner).toHaveTextContent('Open in Word');
    expect(banner).toHaveTextContent(`Couldn't open the document: ${detail}`);
  });

  it('Open in Word (desktop): a thrown bare 500 reads "temporarily unavailable", in dark mode too', async () => {
    config.openLinksError = new (realAuth().ApiError)('HTTP 500', 500);
    await mountLoaded(webDarkTheme);

    await click(editorProps.current.onOpenInWordDesktop);

    const banner = await screen.findByTestId('compose-workspace-word-action-error');
    expect(banner).toHaveTextContent(
      "Couldn't open the document in the desktop app: The document service is temporarily unavailable. Try again in a few minutes."
    );
  });

  it('the banner dismisses, and the next successful Open in Word clears it', async () => {
    config.openLinksError = new (realAuth().ApiError)('HTTP 403', 403);
    await mountLoaded();

    await click(editorProps.current.onOpenInWord);
    expect(await screen.findByTestId('compose-workspace-word-action-error')).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('compose-workspace-word-action-error-dismiss'));
    expect(screen.queryByTestId('compose-workspace-word-action-error')).not.toBeInTheDocument();

    // A new failure re-shows (a different reason, so the message itself changes even though the test's
    // act() batches the hook's start-of-action clear with the failure); a following success clears it.
    config.openLinksError = new (realAuth().ApiError)('HTTP 500', 500);
    await click(editorProps.current.onOpenInWord);
    expect(await screen.findByTestId('compose-workspace-word-action-error')).toBeInTheDocument();
    config.openLinksError = null;
    await click(editorProps.current.onOpenInWord);
    await waitFor(() => expect(screen.queryByTestId('compose-workspace-word-action-error')).not.toBeInTheDocument());
    expect(window.open).toHaveBeenCalledWith('https://web/doc', '_blank');
  });
});

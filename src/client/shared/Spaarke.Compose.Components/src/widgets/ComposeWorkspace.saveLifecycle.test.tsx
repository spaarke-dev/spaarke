/**
 * ComposeWorkspace.saveLifecycle.test.tsx — spaarkeai-compose-r8 task 012 (FR-S05).
 *
 * THE CONTRACT: a save that never comes back cannot strand the editor, and two saves of the same
 * document cannot run at once.
 *
 * Why this file exists: the save request had no timeout, no `AbortSignal` and no in-flight guard. A
 * hung request left `status === 'saving'` forever — Save disabled, no error, no recovery — and the
 * only escape was a page reload, which discards the document. That is a data-loss path reached by
 * doing nothing wrong. Separately, `triggerSave` closes over `state`, so two calls dispatched in the
 * same tick both read the pre-dispatch `'loaded'` status: a held Ctrl+S, or an unmount flush landing
 * on a manual save, could POST twice and let each `commitSaved()` acknowledge work the other carried.
 *
 * The 423 lock banner and its working Retry (FR-S04) are covered end-to-end by
 * `ComposeWorkspace.saveErrorRouting.test.tsx` ("423 renders the Word-lock banner with a working
 * Retry"), which asserts the retry re-issues the save and succeeds once the lock clears. Not
 * duplicated here.
 *
 * ADR-028: `authenticatedFetch` stays the transport; the mock replaces the network at that boundary
 * and honors the `signal` the production code passes through its `RequestInit` — so the abort under
 * test is the real mechanism, not a simulated rejection.
 * ADR-038: behavior at the save-orchestration seam with a stubbed heavy child.
 */

import * as React from 'react';
import { render, screen, waitFor, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

// Fluent's MessageBar (the banners asserted here) uses ResizeObserver, which jsdom lacks.
if (typeof (globalThis as { ResizeObserver?: unknown }).ResizeObserver === 'undefined') {
  (globalThis as { ResizeObserver?: unknown }).ResizeObserver = class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  };
}

const SPE_ID = 'drive-item-doc-1';
const DRIVE_ID = 'drive-1';
const DOC_SESSION = '00000000-0000-0000-0000-0000000d0c07';
const CONTENT_B64 = 'UEsDBA==';

/** Must match `COMPOSE_SAVE_TIMEOUT_MS` in ComposeWorkspace.tsx. */
const SAVE_TIMEOUT_MS = 120000;

/**
 * How the next `/save` behaves. `'ok'` resolves a normal persisted 200; `'hang'` never settles unless
 * the signal aborts (the production timeout is the only thing that can end it); `'deferred'` settles
 * when the test releases it, which is what makes the in-flight window observable.
 */
type SaveBehavior = 'ok' | 'hang' | 'deferred';

/**
 * FR-S08 (task 015): the size limit the mocked server advertises on its Load response. `null` = the
 * server advertises nothing (an older BFF), which must switch the client's numeric pre-flight OFF
 * rather than make it guess.
 */
const config: { behavior: SaveBehavior; advertisedLimit: number | null } = {
  behavior: 'ok',
  advertisedLimit: null,
};

let saveCallCount = 0;
let releaseDeferredSave: (() => void) | null = null;

const authModule = (): { ApiError: new (message: string, status: number) => Error } =>
  jest.requireMock('@spaarke/auth') as { ApiError: new (m: string, s: number) => Error };

function okSaveResponse(): Response {
  return {
    ok: true,
    status: 200,
    json: async () => ({
      documentSpeId: SPE_ID,
      documentRecordId: 'sprk-doc-1',
      driveId: DRIVE_ID,
      versionId: 'v-after-save',
      eTag: 'etag-2',
      size: 500,
      wasPromotedThisSave: false,
      outcome: 'persisted',
    }),
  } as unknown as Response;
}

/** An `AbortError` shaped exactly as a real aborted `fetch` rejection: a DOMException-alike whose
 *  `name` is what the production classifier reads (structurally, not via `instanceof`). */
function abortError(): Error {
  const err = new Error('The operation was aborted.');
  err.name = 'AbortError';
  return err;
}

const authenticatedFetchMock = jest.fn(async (url: string, init?: RequestInit): Promise<Response> => {
  if (url.includes('/api/compose/documents/') && url.includes('/save')) {
    saveCallCount += 1;
    if (config.behavior === 'hang') {
      // Never settles on its own. The ONLY way out is the signal the workspace attached — which is
      // the point: before FR-S05 there was no signal and this state was permanent.
      return new Promise<Response>((_resolve, reject) => {
        init?.signal?.addEventListener('abort', () => reject(abortError()));
      });
    }
    if (config.behavior === 'deferred') {
      return new Promise<Response>(resolve => {
        releaseDeferredSave = () => resolve(okSaveResponse());
      });
    }
    return okSaveResponse();
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
        content: CONTENT_B64,
        eTag: 'etag-1',
        versionId: 'v-load',
        fileName: 'contract.docx',
        size: 500,
        // FR-S08: the server-advertised document-size limit the client pre-flights against.
        maxDocumentBytes: config.advertisedLimit,
        anchoredAnnotations: [],
        definedTermsTracking: [],
        actionHistory: [],
      }),
    } as unknown as Response;
  }
  throw new (authModule().ApiError)('Not found', 404);
});

// NO `virtual: true` on the sibling-lib mocks below — deliberately, and it is load-bearing. The flag
// registers the specifier in jest's RESOLVER, which is shared by every suite a worker runs, so one
// suite's virtual registration changes how a LATER suite resolves the same specifier. See the
// "Sibling `@spaarke/*` resolution" note in jest.config.js for the measurement and the contract.
jest.mock('@spaarke/auth', () => {
  class ApiError extends Error {
    public readonly status: number;
    constructor(message: string, status: number) {
      super(message);
      this.name = 'ApiError';
      this.status = status;
    }
  }
  class AuthError extends Error {
    public readonly code: string;
    constructor(message: string, code = 'auth_failed') {
      super(message);
      this.name = 'AuthError';
      this.code = code;
    }
  }
  return {
    ApiError,
    AuthError,
    authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...(args as [string, RequestInit?])),
    useAuth: () => ({
      isAuthenticated: true,
      getAccessToken: async () => 'test-token',
      authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...(args as [string, RequestInit?])),
      tenantId: 'test-tenant',
      logout: jest.fn(),
    }),
  };
});

jest.mock('@spaarke/ui-components', () => ({
  createXrmNavigationService: () => ({ openLookup: jest.fn() }),
  createXrmDataService: () => ({ retrieveRecord: jest.fn() }),
  SendEmailDialog: () => null,
  SprkModal: () => null,
  RichFilePreviewDialog: () => null,
}));
jest.mock('@spaarke/document-operations', () => ({
  useDocumentActions: () => ({ openInWeb: jest.fn(), openInDesktop: jest.fn(), isActing: false }),
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
  anchoredAnnotationsToDocxAnnotations: () => [],
}));
jest.mock('./useComposeReanchor', () => ({
  useComposeReanchor: () => ({ summary: null, reanchor: jest.fn(), reset: jest.fn() }),
}));
jest.mock('./ComposeToolbar', () => ({
  ComposeToolbar: () => <div data-testid="compose-toolbar-stub" />,
}));

// ── ComposeEditor stub — a dirty document with one captured op (mirrors the saveErrorRouting stub). ──
const commitSavedMock = jest.fn();
const editorProps: { current: { onSave?: () => void; canSave?: boolean } } = { current: {} };
jest.mock('./ComposeEditor', () => {
  const ReactLib = require('react');
  return {
    ComposeEditor: ReactLib.forwardRef(
      (
        props: { onSave?: () => void; canSave?: boolean; onDirtyChange?: (d: boolean) => void },
        ref: React.Ref<unknown>
      ) => {
        editorProps.current = props;
        ReactLib.useEffect(() => {
          props.onDirtyChange?.(true);
        }, []);
        ReactLib.useImperativeHandle(ref, () => ({
          isDirty: () => true,
          serializeOperationLog: () => ({
            orderedOps: [
              {
                operation: { type: 'insertText', paraId: 'AAAA0001', at: { runIndex: 0, offset: 3 }, text: 'x' },
                deletedContentFlag: false,
              },
            ],
            baseVersion: 'v-load',
          }),
          commitSaved: commitSavedMock,
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

function renderWorkspace() {
  return render(
    <FluentProvider theme={webLightTheme}>
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

beforeEach(() => {
  authenticatedFetchMock.mockClear();
  commitSavedMock.mockClear();
  saveCallCount = 0;
  releaseDeferredSave = null;
  config.behavior = 'ok';
  config.advertisedLimit = null;
  editorProps.current = {};
});

afterEach(() => {
  jest.useRealTimers();
});

/** Mount and wait for the Load to settle, leaving the workspace ready to save. */
async function mountLoaded(): Promise<void> {
  renderWorkspace();
  await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
  await waitFor(() => expect(editorProps.current.canSave).toBe(true));
}

/** Fire the workspace Save and let its synchronous + microtask work run. */
async function clickSave(): Promise<void> {
  await act(async () => {
    editorProps.current.onSave?.();
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
  });
}

/** Run out the save deadline under fake timers and flush the abort rejection through React. */
async function advanceToSaveTimeout(): Promise<void> {
  await act(async () => {
    jest.advanceTimersByTime(SAVE_TIMEOUT_MS);
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
  });
}

async function errorBannerText(): Promise<string> {
  const banner = await screen.findByTestId('compose-workspace-error-banner');
  return banner.textContent ?? '';
}

describe("ComposeWorkspace — FR-S08: the size pre-flight uses the SERVER's number, or none", () => {
  it('a document over the advertised limit is refused BEFORE any request is sent', async () => {
    // 1 byte: the retained mount bytes (a few bytes of base64-decoded ZIP magic) exceed it, so the
    // pre-flight fires without needing a 25 MB fixture. The mechanism under test is the comparison
    // and where it happens, not the magnitude.
    config.advertisedLimit = 1;
    await mountLoaded();

    await clickSave();

    expect(saveCallCount).toBe(0);
    const text = await errorBannerText();
    expect(text).toContain('Not saved');
    expect(text).toContain('the limit is');
    expect(text).toContain('still here');

    // Refused, not broken: the editor is out of `saving` and the work is untouched.
    await waitFor(() => expect(editorProps.current.canSave).toBe(true));
    expect(commitSavedMock).not.toHaveBeenCalled();
  });

  it('NEGATIVE: a document UNDER the advertised limit saves normally', async () => {
    config.advertisedLimit = 25 * 1024 * 1024;
    await mountLoaded();

    await clickSave();

    await waitFor(() => expect(saveCallCount).toBe(1));
    await waitFor(() => expect(commitSavedMock).toHaveBeenCalledTimes(1));
    expect(screen.queryByTestId('compose-workspace-error-banner')).not.toBeInTheDocument();
  });

  it('NEGATIVE: when the server advertises NO limit, the client does not invent one', async () => {
    // An older BFF, or a mount door that never called the server. The client must not fall back to a
    // compiled-in number — the server still enforces its own and refuses honestly if need be. A guess
    // here is the "two constants" divergence the requirement exists to remove.
    config.advertisedLimit = null;
    await mountLoaded();

    await clickSave();

    await waitFor(() => expect(saveCallCount).toBe(1));
    expect(screen.queryByTestId('compose-workspace-error-banner')).not.toBeInTheDocument();
  });
});

describe('ComposeWorkspace — FR-S05: a save cannot hang forever and cannot double-run', () => {
  it('a hung save times out, reports honestly, and returns the editor to a usable state without a reload', async () => {
    await mountLoaded();
    config.behavior = 'hang';
    // Fake timers must be installed BEFORE the save, because the deadline is scheduled inside it —
    // a timer created under real timers cannot be advanced by switching afterwards.
    jest.useFakeTimers();

    await clickSave();
    expect(saveCallCount).toBe(1);

    // Mid-flight: the workspace is in `saving`, so Save is unavailable. This is the state that used
    // to be terminal — no timeout, no signal, no way out but a reload.
    expect(editorProps.current.canSave).toBe(false);
    expect(screen.queryByTestId('compose-workspace-error-banner')).not.toBeInTheDocument();

    // Nothing here resolves the request; only the production timeout can end it.
    await advanceToSaveTimeout();
    jest.useRealTimers();

    const text = await errorBannerText();
    expect(text).toContain('the save took too long and was stopped');
    expect(text).toContain('Your changes are still here');

    // The editor is out of `saving` and usable again, with no page reload involved. (Save itself
    // stays gated on dirtiness — the stub reports dirty, so it is live for the retry.)
    await waitFor(() => expect(editorProps.current.canSave).toBe(true));

    // An aborted save is a FAILED save: nothing was confirmed, so the op-log batch and the dirty
    // flag must survive. `commitSaved()` is what would drop them.
    expect(commitSavedMock).not.toHaveBeenCalled();
  });

  it('a timed-out save can be retried and succeeds — the timeout does not poison the document', async () => {
    await mountLoaded();
    config.behavior = 'hang';
    jest.useFakeTimers();
    await clickSave();
    expect(saveCallCount).toBe(1);

    await advanceToSaveTimeout();
    jest.useRealTimers();
    await waitFor(() => expect(editorProps.current.canSave).toBe(true));

    config.behavior = 'ok';
    await clickSave();
    await waitFor(() => expect(saveCallCount).toBe(2));
    await waitFor(() => expect(commitSavedMock).toHaveBeenCalledTimes(1));
    expect(screen.queryByTestId('compose-workspace-error-banner')).not.toBeInTheDocument();
  });

  it('a second save cannot start while one is in flight — the request is issued once', async () => {
    await mountLoaded();
    config.behavior = 'deferred';

    // Both calls land in the SAME tick, so both read the pre-dispatch `'loaded'` status. Only the
    // ref-based guard can tell them apart; the reducer status cannot (it has not re-rendered yet).
    await act(async () => {
      editorProps.current.onSave?.();
      editorProps.current.onSave?.();
      editorProps.current.onSave?.();
      await Promise.resolve();
      await Promise.resolve();
    });

    expect(saveCallCount).toBe(1);

    await act(async () => {
      releaseDeferredSave?.();
      await Promise.resolve();
      await Promise.resolve();
      await Promise.resolve();
    });

    // Exactly one save, and therefore exactly one commit — two concurrent saves would each commit,
    // and the second would acknowledge a batch the first had already dropped.
    await waitFor(() => expect(commitSavedMock).toHaveBeenCalledTimes(1));
    expect(saveCallCount).toBe(1);
  });

  it('NEGATIVE: the guard releases — a save after a completed save runs normally', async () => {
    // The failure this guards against is a one-way latch: a guard that is set and never cleared
    // silently disables saving for the rest of the session, which is worse than the double-POST it
    // was added to prevent. (The Save BUTTON is correctly disabled after a successful save because
    // the document is clean — that is dirtiness, not the guard, so the save entry point is driven
    // directly here, exactly as Ctrl+S and the cross-pane bridge chip do.)
    await mountLoaded();

    await clickSave();
    await waitFor(() => expect(saveCallCount).toBe(1));
    await waitFor(() => expect(commitSavedMock).toHaveBeenCalledTimes(1));

    await clickSave();
    await waitFor(() => expect(saveCallCount).toBe(2));
    await waitFor(() => expect(commitSavedMock).toHaveBeenCalledTimes(2));
  });

  it('NEGATIVE: an ordinary successful save is unaffected — no timeout fires, no banner, one commit', async () => {
    await mountLoaded();

    await clickSave();
    await waitFor(() => expect(saveCallCount).toBe(1));
    await waitFor(() => expect(commitSavedMock).toHaveBeenCalledTimes(1));
    expect(screen.queryByTestId('compose-workspace-error-banner')).not.toBeInTheDocument();

    // The request carried the deadline: an `AbortSignal` reached `authenticatedFetch` through its
    // RequestInit (ADR-028 — not a raw `fetch`), and the completed save left no pending abort.
    const saveCall = authenticatedFetchMock.mock.calls.find(
      ([url]) => typeof url === 'string' && url.includes('/save')
    );
    expect(saveCall).toBeDefined();
    const init = saveCall?.[1] as RequestInit | undefined;
    expect(init?.signal).toBeInstanceOf(AbortSignal);
    expect(init?.signal?.aborted).toBe(false);
  });
});

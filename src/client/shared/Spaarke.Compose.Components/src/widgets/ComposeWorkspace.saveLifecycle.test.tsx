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
type SaveBehavior = 'ok' | 'hang' | 'deferred' | 'throttled';

/**
 * FR-S08 (task 015): the size limit the mocked server advertises on its Load response. `null` = the
 * server advertises nothing (an older BFF), which must switch the client's numeric pre-flight OFF
 * rather than make it guess.
 */
const config: {
  behavior: SaveBehavior;
  advertisedLimit: number | null;
  /**
   * FR-S09 item 1 (task 016): whether the ComposeEditor stub attaches its imperative handle.
   * `'detached'` reproduces the real render race the workspace guards against — the editor is mounted
   * and the Save button is enabled, but `editorRef.current` is still null.
   */
  editorHandle: 'attached' | 'detached';
} = {
  behavior: 'ok',
  advertisedLimit: null,
  editorHandle: 'attached',
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
    if (config.behavior === 'throttled') {
      // What a Graph throttle now looks like on the wire: 429 + the server's own detail naming the wait.
      throw new (authModule().ApiError)(
        'The document service is busy right now, so nothing was saved and nothing was overwritten. ' +
          'Your changes are still here — try again in about 17 seconds.',
        429
      );
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
  // FR-S09 item 2 (task 016): ComposeSaveNameDialog renders a FormModal. The stub exposes the two
  // things the test drives — that the modal is OPEN, and the dismiss the user actually performs.
  FormModal: (props: { open?: boolean; children?: unknown; onClose?: () => void }) => {
    const ReactLib = require('react');
    if (!props.open) return null;
    return ReactLib.createElement(
      'div',
      { 'data-testid': 'form-modal-stub' },
      props.children,
      ReactLib.createElement('button', { 'data-testid': 'form-modal-dismiss', onClick: props.onClose }, 'Cancel')
    );
  },
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
const editorProps: {
  current: { onSave?: (mode?: 'version' | 'new') => void; canSave?: boolean; saveDisabledReason?: string };
} = {
  current: {},
};
jest.mock('./ComposeEditor', () => {
  const ReactLib = require('react');
  return {
    ComposeEditor: ReactLib.forwardRef(
      (
        props: {
          onSave?: (mode?: 'version' | 'new') => void;
          canSave?: boolean;
          saveDisabledReason?: string;
          onDirtyChange?: (d: boolean) => void;
        },
        ref: React.Ref<unknown>
      ) => {
        editorProps.current = props;
        ReactLib.useEffect(() => {
          props.onDirtyChange?.(true);
        }, []);
        // FR-S09 item 1: `null` leaves `editorRef.current` unset while the editor is still MOUNTED and
        // the Save button still enabled — the exact shape of the render race the guard exists for.
        ReactLib.useImperativeHandle(ref, () =>
          config.editorHandle === 'detached'
            ? null
            : {
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
              }
        );
        return <div data-testid="compose-editor-stub" />;
      }
    ),
  };
});

// Import AFTER mocks.
// eslint-disable-next-line import/first
import { ComposeWorkspace } from './ComposeWorkspace';

function renderWorkspace(overrides?: {
  tenantId?: string;
  initialDocumentRef?: { speDriveItemId: string; sprkDocumentId?: string; fileName?: string; transientKey?: string };
}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <ComposeWorkspace
        initialDocumentRef={
          overrides?.initialDocumentRef ?? {
            speDriveItemId: SPE_ID,
            sprkDocumentId: 'sprk-doc-1',
            fileName: 'contract.docx',
          }
        }
        initialSessionId={DOC_SESSION}
        bffBaseUrl="https://bff.example.test"
        driveId={DRIVE_ID}
        tenantId={overrides?.tenantId ?? 'tenant-1'}
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
  config.editorHandle = 'attached';
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

describe('ComposeWorkspace — FR-S09: no save-path guard drops in silence', () => {
  it('item 1 — Save with the editor handle not attached SAYS SO instead of doing nothing', async () => {
    // The defect: `if (!state.documentRef || !editorRef.current) return;` — a bare return, with the
    // editor on screen and the Save button enabled. Pressing Save did nothing at all, repeatedly, and
    // was indistinguishable from a dead button.
    config.editorHandle = 'detached';
    renderWorkspace();
    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
    await waitFor(() => expect(editorProps.current.canSave).toBe(true));

    await clickSave();

    expect(saveCallCount).toBe(0);
    const text = await errorBannerText();
    expect(text).toMatch(/did not run/i);
    expect(text).toMatch(/editor was not ready/i);
    // And it must promise what is true: the work survives.
    expect(text).toMatch(/still here/i);
  });

  it('item 3 — losing tenantId disables Save AND states why, instead of failing after the press', async () => {
    // `tenantId` was already required by `triggerSave` and by every save request body — but not by the
    // GATE, so the button stayed enabled on a workspace that could not possibly save and only said so
    // after the user pressed it. Observed by re-rendering a LOADED workspace without the tenant (the
    // host's config going away mid-session); mounting without it never reaches the editor at all,
    // because the load path refuses first — which is the same precondition, stated one layer earlier.
    const view = renderWorkspace();
    await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
    await waitFor(() => expect(editorProps.current.canSave).toBe(true));

    view.rerender(
      <FluentProvider theme={webLightTheme}>
        <ComposeWorkspace
          initialDocumentRef={{ speDriveItemId: SPE_ID, sprkDocumentId: 'sprk-doc-1', fileName: 'contract.docx' }}
          initialSessionId={DOC_SESSION}
          bffBaseUrl="https://bff.example.test"
          driveId={DRIVE_ID}
          tenantId=""
        />
      </FluentProvider>
    );

    await waitFor(() => expect(editorProps.current.canSave).toBe(false));
    expect(editorProps.current.saveDisabledReason).toMatch(/connection settings/i);
    expect(saveCallCount).toBe(0);
  });

  it('item 3 NEGATIVE — a configured workspace enables Save and offers no reason', async () => {
    await mountLoaded();
    expect(editorProps.current.canSave).toBe(true);
    expect(editorProps.current.saveDisabledReason).toBeUndefined();
  });

  it('item 2 — dismissing the name modal reports that the save did not happen', async () => {
    // Save As ALWAYS routes through the name modal (`saveNeedsName(mode)` returns true for 'new'), so it
    // is the reachable form of the same gate a first create-on-save hits. Dismissing it used to abandon
    // the requested save in silence — press Save As, press Esc, believe you saved.
    await mountLoaded();

    await act(async () => {
      editorProps.current.onSave?.('new');
      await Promise.resolve();
    });

    // The gate is visible...
    const modal = await screen.findByTestId('compose-save-name-dialog');
    expect(modal).toBeInTheDocument();
    expect(saveCallCount).toBe(0);

    // ...and dismissing it is now an accounted-for refusal, not silence.
    await act(async () => {
      screen.getByTestId('form-modal-dismiss').click();
      await Promise.resolve();
    });

    expect(saveCallCount).toBe(0);
    const text = await errorBannerText();
    expect(text).toMatch(/not saved/i);
    expect(text).toMatch(/needs a name/i);
  });

  it('item 6 — a 429 reads as "busy, try again", never as a server error', async () => {
    await mountLoaded();
    config.behavior = 'throttled';

    await clickSave();

    const text = await errorBannerText();
    // The server's own detail carries the wait; the client must surface it rather than overwrite it.
    expect(text).toMatch(/busy/i);
    expect(text).toMatch(/17 seconds/);
    expect(text).toMatch(/still here/i);
    // THE discriminator. Before task 016 a 429 fell through to `saveFailureMessage`'s default arm and
    // was announced as "Not saved — the server rejected this save (HTTP 429): ...". Nothing rejected
    // anything: the request was fine and will succeed shortly. Asserting the ABSENCE of that framing is
    // what makes this test fail against the unfixed code rather than passing on the server's detail.
    expect(text).not.toMatch(/rejected this save/i);
    expect(text).not.toMatch(/HTTP 429/);
    expect(text).not.toMatch(/server hit an error/i);
    expect(text).not.toMatch(/InvalidOperationException/);
    // Nothing was written, so the editor must NOT have been told the save committed.
    expect(commitSavedMock).not.toHaveBeenCalled();
  });
});

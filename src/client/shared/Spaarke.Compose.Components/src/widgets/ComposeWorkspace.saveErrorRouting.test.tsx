/**
 * ComposeWorkspace.saveErrorRouting.test.tsx — spaarkeai-compose-r8 task 010 (FR-S01).
 *
 * THE CONTRACT: every non-2xx save produces its OWN user-visible outcome, routed on the status of the
 * `ApiError` that `authenticatedFetch` throws.
 *
 * Why this file exists: `authenticatedFetch` returns ONLY when `response.ok` and THROWS an `ApiError`
 * (status + RFC-7807 ProblemDetails) for every non-2xx — see `Spaarke.Auth/src/authenticatedFetch.ts`.
 * The workspace's status-specific handling nevertheless lived inside an `if (!response.ok)` block, a
 * shape the real function cannot produce, so it was unreachable from R5 onward: 423 lock, 412
 * stale-base and 403 permission all collapsed into one dead-end "Save failed: …" with no recovery.
 * That single undifferentiated message is why "can't save" persisted across R5–R7 as a queue of
 * different bugs wearing one error string.
 *
 * Every case here drives the REAL thrown-ApiError path. No test in this file mocks
 * `authenticatedFetch` to RESOLVE `{ ok: false }` — that shape is what made the old tests
 * self-confirming (they exercised the dead block and passed while the live contract was broken).
 *
 * ADR-028: `authenticatedFetch` stays the transport; the mock replaces the network at that boundary.
 * ADR-038: a client behavior test at the save-orchestration seam with a stubbed heavy child — NOT a
 * banned `Mock<HttpMessageHandler>` / DI-registration / ctor-null test.
 */

import * as React from 'react';
import { render, screen, waitFor, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

// Fluent's MessageBar (every banner asserted here) uses ResizeObserver, which jsdom lacks.
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
/** Minimal valid base64 of the ZIP magic `PK\x03\x04` — the STUB editor ignores the decoded bytes. */
const CONTENT_B64 = 'UEsDBA==';

/**
 * How the next `/save` should fail. `null` = succeed. Set per test; reset in `beforeEach`.
 * `kind: 'api'` throws the mocked module's own `ApiError`; `'auth'` throws an `AuthError` (what
 * `authenticatedFetch` throws when the 401 retry budget is exhausted — it carries `code`, not
 * `status`); `'transport'` throws the raw `TypeError` a rejected `fetch` produces when no HTTP
 * exchange happened at all.
 */
type SaveOutcome =
  | { kind: 'api'; status: number; detail: string }
  | { kind: 'auth' }
  | { kind: 'transport' }
  | { kind: 'post-save-throw' }
  | { kind: 'concurrent-warning' }
  | { kind: 'outcome-200'; outcome: string }
  | null;

const config: { saveOutcome: SaveOutcome } = { saveOutcome: null };

let saveCallCount = 0;

/**
 * The error classes the mocked `@spaarke/auth` exports — resolved LAZILY (inside the call) because
 * the factory runs when `ComposeWorkspace` requires the module, which babel hoists above this file's
 * top-level statements. Throwing the module's OWN classes keeps the failure identical to production's.
 */
const authModule = (): {
  ApiError: new (message: string, status: number) => Error;
  AuthError: new (message: string, code?: string) => Error;
} =>
  jest.requireMock('@spaarke/auth') as {
    ApiError: new (m: string, s: number) => Error;
    AuthError: new (m: string, c?: string) => Error;
  };

const authenticatedFetchMock = jest.fn(async (url: string): Promise<Response> => {
  // Replace-path save (checked BEFORE the generic documents Load route).
  if (url.includes('/api/compose/documents/') && url.includes('/save')) {
    saveCallCount += 1;
    const outcome = config.saveOutcome;
    if (outcome?.kind === 'api') {
      throw new (authModule().ApiError)(outcome.detail, outcome.status);
    }
    if (outcome?.kind === 'auth') {
      throw new (authModule().AuthError)('Authentication failed after all retry attempts', 'auth_exhausted');
    }
    if (outcome?.kind === 'transport') {
      throw new TypeError('Failed to fetch');
    }
    if (outcome?.kind === 'post-save-throw') {
      // A 2xx whose body cannot be read. Since FR-S06 the status alone does NOT say whether anything
      // was written (`storage-failed` also arrives on a 200), so this case is genuinely indeterminate —
      // the throw happens inside the same try/catch as the request, before the outcome can be read.
      return {
        ok: true,
        status: 200,
        json: async () => {
          throw new SyntaxError('Unexpected end of JSON input');
        },
      } as unknown as Response;
    }
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
        // FR-S02: the concurrent-writer outcome is a 200 carrying a warning, not a refusal.
        degradationWarnings:
          outcome?.kind === 'concurrent-warning' ? [{ code: 'concurrent-external-change', count: 1 }] : undefined,
        // FR-S06: the terminal outcome. A 200 does NOT imply anything was written.
        outcome:
          outcome?.kind === 'outcome-200'
            ? outcome.outcome
            : outcome?.kind === 'concurrent-warning'
              ? 'persisted-with-warnings'
              : 'persisted',
      }),
    } as unknown as Response;
  }
  // Stored-document Load.
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
        anchoredAnnotations: [],
        definedTermsTracking: [],
        actionHistory: [],
      }),
    } as unknown as Response;
  }
  // Session-ledger compose-outputs probe + everything else → benign 404, expressed the way
  // `authenticatedFetch` actually expresses one (a thrown ApiError, not a resolved non-ok Response).
  throw new (authModule().ApiError)('Not found', 404);
});

// NO `virtual: true` on the sibling-lib mocks below — deliberately, and it is load-bearing. The flag
// registers the specifier in jest's RESOLVER, which is shared by every suite a worker runs, so one
// suite's virtual registration changes how a LATER suite resolves the same specifier. See the
// "Sibling `@spaarke/*` resolution" note in jest.config.js for the measurement and the contract.
jest.mock('@spaarke/auth', () => {
  // Mirror the real `@spaarke/auth` error classes. Declared INSIDE the factory so the classes the
  // workspace imports and the classes the fetch mock throws are the same objects.
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

// Mocked to the tiny surface ComposeWorkspace actually consumes so this suite is self-contained
// (same set + rationale as ComposeWorkspace.saveOpLogPreservation.test.tsx).
jest.mock('@spaarke/ui-components', () => ({
  // r8 task 052 (FR-C05) — ComposeWorkspace also mounts <ConfirmModal/> unconditionally for the
  // stale-target "apply anyway?" question (controlled via its own `open` prop, same pattern as
  // SprkModal/SendEmailDialog above). A no-op stub keeps this mock complete.
  ConfirmModal: () => null,
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

// ── ComposeEditor stub — a dirty document with a single captured op, mirroring the op-log harness. ──
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
  config.saveOutcome = null;
  editorProps.current = {};
});

/** Mount, wait for the Load to settle, then fire the workspace Save and let its async flow finish. */
async function mountAndSave(): Promise<void> {
  renderWorkspace();
  await waitFor(() => expect(screen.getByTestId('compose-editor-stub')).toBeInTheDocument());
  await waitFor(() => expect(editorProps.current.canSave).toBe(true));
  await clickSave();
  await waitFor(() => expect(saveCallCount).toBe(1));
}

async function clickSave(): Promise<void> {
  await act(async () => {
    editorProps.current.onSave?.();
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
  });
}

/** The generic save-error banner's rendered text (the `Save error` MessageBar). */
async function errorBannerText(): Promise<string> {
  const banner = await screen.findByTestId('compose-workspace-error-banner');
  return banner.textContent ?? '';
}

describe('ComposeWorkspace — save errors route on ApiError.status (FR-S01, r8 task 010)', () => {
  it('423 renders the Word-lock banner with a working Retry — not the generic error banner', async () => {
    config.saveOutcome = { kind: 'api', status: 423, detail: 'The document is locked for co-authoring.' };
    await mountAndSave();

    const lockBanner = await screen.findByTestId('compose-workspace-word-lock-banner');
    expect(lockBanner).toHaveTextContent('The document is locked for co-authoring.');
    // The lock bar REPLACES the generic error banner (ComposeBannerStack suppresses it when isLock).
    expect(screen.queryByTestId('compose-workspace-error-banner')).not.toBeInTheDocument();

    // The Retry affordance re-runs the save. It succeeds once the lock clears.
    config.saveOutcome = null;
    const retry = screen.getByTestId('compose-word-lock-retry');
    await act(async () => {
      retry.click();
      await Promise.resolve();
      await Promise.resolve();
      await Promise.resolve();
    });
    await waitFor(() => expect(saveCallCount).toBe(2));
    await waitFor(() => expect(commitSavedMock).toHaveBeenCalledTimes(1));
  });

  it('a concurrent external writer is a SUCCESSFUL save with a warning, never a refusal (FR-S02)', async () => {
    // Task 011 made concurrency last-writer-wins: the server no longer refuses on a moved base, so the
    // concurrent-writer case arrives on the SUCCESS path carrying `concurrent-external-change`. This is
    // the paired client-recovery assertion NFR-08 requires for that outcome.
    config.saveOutcome = { kind: 'concurrent-warning' };
    await mountAndSave();

    const banner = await screen.findByTestId('compose-workspace-concurrency-banner');
    expect(banner).toHaveTextContent('Someone else saved this document while you had it open');
    expect(banner).toHaveTextContent('version history');
    // The save SUCCEEDED — no error banner, and the op-log was committed.
    expect(screen.queryByTestId('compose-workspace-error-banner')).not.toBeInTheDocument();
    await waitFor(() => expect(commitSavedMock).toHaveBeenCalledTimes(1));
  });

  it('NEGATIVE: a 412 is no longer special-cased — the refusal loop is gone (FR-S02)', async () => {
    // Defence in depth: if a stale BFF ever returns 412 on the save route, it must render as an ordinary
    // honest rejection, NOT resurrect the reload-and-reapply refusal flow task 011 deleted.
    config.saveOutcome = { kind: 'api', status: 412, detail: 'stale base' };
    await mountAndSave();

    const text = await errorBannerText();
    expect(text).toContain('the server rejected this save (HTTP 412)');
    expect(screen.queryByTestId('compose-external-change-banner')).not.toBeInTheDocument();
  });

  it('403 renders the permission message, carrying the server detail', async () => {
    config.saveOutcome = { kind: 'api', status: 403, detail: 'Write access is required on this container.' };
    await mountAndSave();

    const text = await errorBannerText();
    expect(text).toContain('You do not have permission to save this document');
    expect(text).toContain('Write access is required on this container');
  });

  it('404 says the document no longer exists and points at Save As', async () => {
    config.saveOutcome = { kind: 'api', status: 404, detail: 'Item not found.' };
    await mountAndSave();

    const text = await errorBannerText();
    expect(text).toContain('no longer exists at its saved location');
    expect(text).toContain('Save As');
  });

  it('another 4xx surfaces the honest status + server detail (rejected, not "failed")', async () => {
    config.saveOutcome = { kind: 'api', status: 422, detail: 'A tracked edit could not be applied.' };
    await mountAndSave();

    const text = await errorBannerText();
    expect(text).toContain('the server rejected this save (HTTP 422)');
    expect(text).toContain('A tracked edit could not be applied');
    expect(text).toContain('Your changes are still here');
  });

  it('5xx is presented as a retryable server error, distinct from a 4xx rejection', async () => {
    config.saveOutcome = { kind: 'api', status: 500, detail: 'NullReferenceException (TraceId: abc123)' };
    await mountAndSave();

    const text = await errorBannerText();
    expect(text).toContain('the server hit an error (HTTP 500)');
    expect(text).toContain('TraceId: abc123');
    expect(text).toContain('try again');
  });

  it('a 5xx WITHOUT ProblemDetails does not echo a redundant "HTTP 500" detail', async () => {
    // `ApiError.message` falls back to the literal `HTTP {status}` when the body carries no
    // ProblemDetails — appending that after our own status text would read "(HTTP 500): HTTP 500".
    config.saveOutcome = { kind: 'api', status: 500, detail: 'HTTP 500' };
    await mountAndSave();

    const text = await errorBannerText();
    expect(text).toContain('the server hit an error (HTTP 500).');
    expect(text).not.toContain('HTTP 500: HTTP 500');
  });

  it('an AuthError (401 budget exhausted — no status at all) says the sign-in expired', async () => {
    config.saveOutcome = { kind: 'auth' };
    await mountAndSave();

    const text = await errorBannerText();
    expect(text).toContain('your sign-in expired');
    expect(text).toContain('Your changes are still here');
  });

  it('a transport rejection (no HTTP exchange) is reported as a connection problem, not a server error', async () => {
    config.saveOutcome = { kind: 'transport' };
    await mountAndSave();

    const text = await errorBannerText();
    expect(text).toContain("couldn't complete the request");
    expect(text).not.toContain('HTTP');
  });

  it('NEGATIVE: a SUCCESSFUL save renders no error banner and no lock banner', async () => {
    config.saveOutcome = null;
    await mountAndSave();

    await waitFor(() => expect(commitSavedMock).toHaveBeenCalledTimes(1));
    expect(screen.queryByTestId('compose-workspace-error-banner')).not.toBeInTheDocument();
    expect(screen.queryByTestId('compose-workspace-word-lock-banner')).not.toBeInTheDocument();
    expect(screen.queryByTestId('compose-external-change-banner')).not.toBeInTheDocument();
  });

  it('a 2xx whose body cannot be read is reported as INDETERMINATE, never as saved or not-saved', async () => {
    // Task 010 originally asserted "was saved" here, on the assumption that a 2xx meant the write
    // landed. FR-S06 (task 013) disproved that assumption — `storage-failed` also arrives on a 200 —
    // so when the body cannot be read we genuinely do not know. Claiming "Not saved" risks a duplicate
    // save; claiming "Saved" risks silent data loss. The honest report is that it is unconfirmed.
    config.saveOutcome = { kind: 'post-save-throw' };
    await mountAndSave();

    const text = await errorBannerText();
    expect(text).toContain('could not confirm');
    expect(text).toContain('Reload the document to check');
    expect(text).not.toContain('Not saved —');
    // Unconfirmed is NOT success: the op-log must survive so a retry re-sends the same work.
    expect(commitSavedMock).not.toHaveBeenCalled();
  });

  // ── FR-S06 (task 013): the outcome field, not the HTTP status, decides success ────────────────────
  //
  // THE defect this contract removes: `ComposeService`'s container-failure path RETURNS a result rather
  // than throwing, and the endpoint wraps every returned result in `Results.Ok` — so a save that wrote
  // nothing at all arrived as HTTP 200 and rendered as "Saved ✓".

  it('a 200 carrying storage-failed does NOT render Saved — nothing was written (FR-S06)', async () => {
    config.saveOutcome = { kind: 'outcome-200', outcome: 'storage-failed' };
    await mountAndSave();

    const text = await errorBannerText();
    expect(text).toContain('Not saved');
    expect(text).toContain('could not store the document');
    // The success side effects must NOT have fired: no op-log commit (so a retry re-sends the work).
    expect(commitSavedMock).not.toHaveBeenCalled();
    await waitFor(() => expect(editorProps.current.canSave).toBe(true));
  });

  it('a 200 carrying partially-recorded says PARTLY saved — stored, but not everything (FR-S06)', async () => {
    config.saveOutcome = { kind: 'outcome-200', outcome: 'partially-recorded' };
    await mountAndSave();

    const text = await errorBannerText();
    expect(text).toContain('Partly saved');
    expect(text).toContain('redo anything missing');
    // Deliberately not a success: the document is stored but incomplete, so the user has work to redo
    // and must not be told an unqualified "Saved".
    expect(commitSavedMock).not.toHaveBeenCalled();
  });

  it('an UNRECOGNIZED outcome is treated as not-a-success — the safe direction is to under-claim', async () => {
    // A newer BFF adding a member this client does not know must never silently render as "Saved".
    config.saveOutcome = { kind: 'outcome-200', outcome: 'some-future-member' };
    await mountAndSave();

    await screen.findByTestId('compose-workspace-error-banner');
    expect(commitSavedMock).not.toHaveBeenCalled();
  });

  it('NEGATIVE: persisted and persisted-with-warnings ARE successes (no false failures)', async () => {
    config.saveOutcome = { kind: 'outcome-200', outcome: 'persisted' };
    await mountAndSave();
    await waitFor(() => expect(commitSavedMock).toHaveBeenCalledTimes(1));
    expect(screen.queryByTestId('compose-workspace-error-banner')).not.toBeInTheDocument();
  });

  it("NEGATIVE: an ABSENT outcome is a success — an older BFF's 200 always meant a completed write", async () => {
    config.saveOutcome = { kind: 'outcome-200', outcome: undefined as unknown as string };
    await mountAndSave();
    await waitFor(() => expect(commitSavedMock).toHaveBeenCalledTimes(1));
    expect(screen.queryByTestId('compose-workspace-error-banner')).not.toBeInTheDocument();
  });

  it('NEGATIVE: a failed save never commits the op-log — the edits survive for a retry', async () => {
    config.saveOutcome = { kind: 'api', status: 500, detail: 'boom' };
    await mountAndSave();

    await screen.findByTestId('compose-workspace-error-banner');
    expect(commitSavedMock).not.toHaveBeenCalled();
    // The document stays saveable — a retry re-sends the same batch.
    await waitFor(() => expect(editorProps.current.canSave).toBe(true));
  });
});

/**
 * ComposeWorkspace.redline-from-ledger.test.tsx — DEF-09 READ→RENDER half.
 *
 * Companion to the SpaarkeAi write-routing forcing test
 * (`ConversationPane.compose-draft-alternative-session-routing.e2e.test.tsx`). That
 * file proves the compose EDIT dispatch WRITES its `compose` SessionOutput into the
 * DOCUMENT session (not the chat session). THIS file proves the other half of the
 * slice: `ComposeWorkspace` READS `compose-outputs` from its DOCUMENT session and the
 * inline redline MARK materializes from that stored ledger entry — which is exactly
 * what the owner-reported defect was missing.
 *
 * DEF-12 UPDATE: the cramped fixed `compose-redline-controls` bar was REMOVED as the
 * primary control (that role moved to the Assistant confirmation message). This test now
 * asserts the two things the new design KEEPS in the document: (1) the visual redline
 * MARK (insertion/deletion span carrying the ledgerRef) still materializes, and (2)
 * per-change granularity survives — clicking the redline span opens the on-click
 * accept/reject popover (`compose-redline-onclick`) for THAT change. It also asserts the
 * removed primary bar is gone.
 *
 * Split rationale (honest, not a false-green shortcut): TipTap mounts only in this
 * `@spaarke/compose-components` jest env, and `ConversationPane` (which owns the
 * write-routing fix) lives in the SpaarkeAi solution and cannot be imported here
 * (unidirectional dependency). So the write→read→render slice is proven across the two
 * environments; neither test shares one session id or mocks the ledger away.
 *
 * Authenticity: the REAL `ComposeWorkspace` + REAL `ComposeEditor` (real TipTap, real
 * `usePendingRedline`) are mounted; the network is mocked at the `@spaarke/auth`
 * boundary and backed by a per-session ledger. The `compose-outputs` read is served
 * ONLY for the DOCUMENT session — a read against any other session returns empty, so a
 * regression that read the wrong session would render NO redline and fail this test.
 */

import * as React from 'react';
import * as fs from 'fs';
import * as path from 'path';
import { render, screen, waitFor, act, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets/events';

// Fluent MessageBar uses ResizeObserver, which jsdom lacks.
if (typeof (globalThis as { ResizeObserver?: unknown }).ResizeObserver === 'undefined') {
  (globalThis as { ResizeObserver?: unknown }).ResizeObserver = class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  };
}

// ---------------------------------------------------------------------------
// Two DISTINCT session ids. Only the DOCUMENT session has compose outputs.
// ---------------------------------------------------------------------------
const DOC_SESSION = '00000000-0000-0000-0000-0000000d0c07';
const CHAT_SESSION = '00000000-0000-0000-0000-00000000c8a7';
const DRAFT_BINDING = 'binding-draft-alternative-guid';
const LEDGER_REF = `${DRAFT_BINDING}@t1`;
const NEW_TEXT = 'The Supplier shall indemnify the Customer without any liability cap.';
const SPE_ID = 'drive-item-doc-1';
const DRIVE_ID = 'drive-1';

// A minimal real .docx so mammoth import succeeds cleanly (falls back to empty bytes
// if the fixture path can't be resolved — the no-target insertion redline renders
// either way).
function loadSampleDocxBase64(): string {
  try {
    const p = path.resolve(__dirname, '../../../../../../tests/integration/Spe.Integration.Tests/fixtures/sample.docx');
    return fs.readFileSync(p).toString('base64');
  } catch {
    return '';
  }
}
const SAMPLE_DOCX_B64 = loadSampleDocxBase64();

// Per-session compose-outputs ledger: only DOC_SESSION carries the drafted alternative.
const composeOutputsBySession: Record<string, unknown[]> = {
  [DOC_SESSION]: [
    {
      key: LEDGER_REF,
      bindingId: DRAFT_BINDING,
      turn: 1,
      disposition: 'compose',
      // No `target_text` ⇒ a pure INSERTION redline at the cursor (renders regardless
      // of the loaded document's text — the render+controls are what this test asserts).
      payload: { new_text: NEW_TEXT },
    },
  ],
};

/** Every `compose-outputs` GET url, so we can assert it read the DOCUMENT session. */
const composeOutputsReadUrls: string[] = [];

function sessionFromUrl(url: string): string {
  return decodeURIComponent(url.match(/\/sessions\/([^/]+)\//)?.[1] ?? '');
}

const authenticatedFetchMock = jest.fn(async (url: string, _init?: RequestInit): Promise<Response> => {
  // Compose Load (stored document).
  if (url.includes('/api/compose/documents/') && !url.includes('/save')) {
    return {
      ok: true,
      status: 200,
      json: async () => ({
        documentSpeId: SPE_ID,
        driveId: DRIVE_ID,
        sessionId: DOC_SESSION,
        documentRecordId: 'sprk-doc-1',
        content: SAMPLE_DOCX_B64,
        eTag: 'etag-1',
        fileName: 'contract.docx',
        size: 952,
        anchoredAnnotations: [],
        definedTermsTracking: [],
        actionHistory: [],
      }),
    } as unknown as Response;
  }
  // Session-ledger compose-outputs read (the DEF-09 read side).
  if (url.includes('/compose-outputs')) {
    composeOutputsReadUrls.push(url);
    const session = sessionFromUrl(url);
    return { ok: true, status: 200, json: async () => composeOutputsBySession[session] ?? [] } as unknown as Response;
  }
  return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
});

jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...(args as [string, RequestInit?])),
  useAuth: () => ({
    isAuthenticated: true,
    getAccessToken: async () => 'test-token',
    authenticatedFetch: (...args: unknown[]) => authenticatedFetchMock(...(args as [string, RequestInit?])),
    tenantId: 'test-tenant',
    logout: jest.fn(),
  }),
}));

// Heavy workspace side-effect hooks — inert doubles (checkout/broadcast/heartbeat +
// Word-shuttle + reanchor). None are on the READ→materialize→redline path under test.
jest.mock('./hooks', () => ({
  useComposeBroadcastChannel: () => ({ postFocusMe: jest.fn(), postForceClosed: jest.fn() }),
  useComposeCheckoutLifecycle: () => ({ forceCloseAndAcquire: jest.fn(), discardAndCancel: jest.fn() }),
  useComposeHeartbeatGate: () => undefined,
}));
jest.mock('./useComposeWordShuttle', () => ({
  useComposePushAnnotations: () => ({ push: jest.fn(), pushing: false }),
  useComposePullAnnotations: () => ({ pull: jest.fn() }),
  useComposeCheckChanges: () => ({ checkChanges: jest.fn() }),
  anchoredAnnotationsToPriorAnchors: () => [],
  anchoredAnnotationsToDocxAnnotations: () => [],
}));
jest.mock('./useComposeReanchor', () => ({
  useComposeReanchor: () => ({ summary: null, reanchor: jest.fn(), reset: jest.fn() }),
}));

// Import AFTER mocks.
// eslint-disable-next-line import/first
import { ComposeWorkspace } from './ComposeWorkspace';

function renderWorkspace(bus: PaneEventBus) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <ComposeWorkspace
          initialDocumentRef={{ speDriveItemId: SPE_ID, sprkDocumentId: 'sprk-doc-1', fileName: 'contract.docx' }}
          initialSessionId={DOC_SESSION}
          bffBaseUrl="https://bff.example.test"
          driveId={DRIVE_ID}
          tenantId="tenant-1"
        />
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

beforeEach(() => {
  authenticatedFetchMock.mockClear();
  composeOutputsReadUrls.length = 0;
});

describe('DEF-09/DEF-12: ComposeWorkspace materializes the redline mark + per-change on-click from the DOCUMENT session ledger', () => {
  it('reads compose-outputs from the document session, materializes the redline MARK, keeps per-change on-click, and drops the primary bar', async () => {
    const bus = new PaneEventBus();
    renderWorkspace(bus);

    // Document loads (session id = DOC_SESSION) and the editor mounts.
    await screen.findByRole('textbox', undefined, { timeout: 5000 });

    // Flow 5 — the Assistant-applied signal referencing the stored ledger entry (exactly
    // what ConversationPane emits after a Draft-alternative dispatch). ComposeWorkspace's
    // shipped receiver re-reads the DOCUMENT session's compose-outputs and materializes.
    act(() => {
      bus.dispatch('workspace', {
        type: 'compose_assistant_insert',
        documentRef: { speDriveItemId: SPE_ID },
        sourceNodeId: DRAFT_BINDING,
        sourcePlaybookId: '',
        contentHtml: '',
        format: 'html',
        insertMode: 'insert-at-cursor',
        requireUserConfirm: false,
        ledgerRef: LEDGER_REF,
        sessionId: DOC_SESSION,
        timestamp: '2026-07-10T00:00:00.000Z',
      });
    });

    // (1) The visual redline MARK materializes in the document (the defect: it never appeared).
    // The pure insertion renders a `<span data-compose-mark="insertion" data-ledger-ref>` carrying
    // the ledgerRef provenance + the drafted text. This is what STAYS under DEF-12.
    const markSpan = await waitFor(
      () => {
        const el = document.querySelector<HTMLElement>(
          `[data-compose-mark="insertion"][data-ledger-ref="${LEDGER_REF}"]`
        );
        if (!el) throw new Error('insertion redline mark not yet materialized');
        return el;
      },
      { timeout: 5000 }
    );
    expect(markSpan.textContent).toContain(NEW_TEXT);

    // (2) DEF-12 — the cramped fixed primary bar is GONE.
    expect(screen.queryByTestId('compose-redline-controls')).toBeNull();

    // (3) DEF-12 — per-change granularity survives: clicking the redline span opens the on-click
    // accept/reject popover for THAT change (routes to the same usePendingRedline.accept/reject).
    act(() => {
      fireEvent.click(markSpan);
    });
    const onClickPopover = await screen.findByTestId('compose-redline-onclick', undefined, { timeout: 5000 });
    expect(onClickPopover).toBeInTheDocument();
    expect(onClickPopover.getAttribute('data-ledger-ref')).toBe(LEDGER_REF);
    expect(screen.getByTestId(`compose-redline-accept-${LEDGER_REF}`)).toBeInTheDocument();
    expect(screen.getByTestId(`compose-redline-reject-${LEDGER_REF}`)).toBeInTheDocument();

    // It read compose-outputs from the DOCUMENT session — never the chat session.
    expect(composeOutputsReadUrls.length).toBeGreaterThan(0);
    expect(composeOutputsReadUrls.some(u => u.includes(`/sessions/${DOC_SESSION}/compose-outputs`))).toBe(true);
    expect(composeOutputsReadUrls.every(u => !u.includes(`/sessions/${CHAT_SESSION}/compose-outputs`))).toBe(true);
  });
});

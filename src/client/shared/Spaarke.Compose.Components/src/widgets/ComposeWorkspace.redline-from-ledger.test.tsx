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
// DEF-11 Accept-all harness — the SAME cross-pane bridge the Assistant's confirmation-message
// Accept control routes through (DEF-12); mirrors ConversationPane.compose-draft-alternative-
// session-routing.e2e.test.tsx's `BridgeCapture` pattern so Accept-all is exercised the same way
// the real integration invokes it (`acceptRedline(baseLedgerRef)`), not a shortcut UI click.
import {
  ComposeActionBridgeProvider,
  useComposeActionBridge,
  type ComposeActionBridgeValue,
} from '../context/composeActionBridge';

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

/** The default single-edit ledger fixture (DEF-09/DEF-12 forcing test) — restored in `beforeEach`. */
function defaultComposeOutputs(): unknown[] {
  return [
    {
      key: LEDGER_REF,
      bindingId: DRAFT_BINDING,
      turn: 1,
      disposition: 'compose',
      // No `target_text` ⇒ a pure INSERTION redline at the cursor (renders regardless
      // of the loaded document's text — the render+controls are what this test asserts).
      payload: { new_text: NEW_TEXT },
    },
  ];
}

// Per-session compose-outputs ledger: only DOC_SESSION carries stored output(s). Mutable — each
// `it` may override `composeOutputsBySession[DOC_SESSION]` with its own fixture; `beforeEach`
// restores the default single-edit shape so tests stay order-independent.
const composeOutputsBySession: Record<string, unknown[]> = {
  [DOC_SESSION]: defaultComposeOutputs(),
};

// ---------------------------------------------------------------------------
// DEF-11 — whole-document revision fixtures (multi-change edits[] + comments[]).
// ---------------------------------------------------------------------------
const REVISE_BINDING = 'binding-revise-document-guid';
const REVISE_LEDGER_REF = `${REVISE_BINDING}@t1`;
const REVISE_EDIT_TEXTS = ['Alternative clause one.', 'Alternative clause two.', 'Alternative clause three.'];

const FLAG_BINDING = 'binding-flag-risks-guid';
const FLAG_LEDGER_REF = `${FLAG_BINDING}@t1`;

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

// DEF-11 Accept-all harness — same topology as `renderWorkspace` plus the cross-pane
// `ComposeActionBridgeProvider` + a capture component, so a test can call
// `bridgeRef.current.acceptRedline(baseLedgerRef)` exactly as `ConversationPane.handleAcceptComposeEdit`
// does (DEF-12), rather than driving Accept-all through a UI shortcut that doesn't exist for a BASE key
// (the on-click popover only ever addresses the clicked change's own sub-key).
const bridgeRef: { current: ComposeActionBridgeValue | null } = { current: null };
function BridgeCapture(): null {
  bridgeRef.current = useComposeActionBridge();
  return null;
}

function renderWorkspaceWithBridge(bus: PaneEventBus) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <ComposeActionBridgeProvider>
          <ComposeWorkspace
            initialDocumentRef={{ speDriveItemId: SPE_ID, sprkDocumentId: 'sprk-doc-1', fileName: 'contract.docx' }}
            initialSessionId={DOC_SESSION}
            bffBaseUrl="https://bff.example.test"
            driveId={DRIVE_ID}
            tenantId="tenant-1"
          />
          <BridgeCapture />
        </ComposeActionBridgeProvider>
      </PaneEventBusProvider>
    </FluentProvider>
  );
}

beforeEach(() => {
  authenticatedFetchMock.mockClear();
  composeOutputsReadUrls.length = 0;
  composeOutputsBySession[DOC_SESSION] = defaultComposeOutputs();
  bridgeRef.current = null;
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

// ---------------------------------------------------------------------------
// DEF-11 — whole-document revision: multi-change redline + Accept-all (edits[])
// ---------------------------------------------------------------------------
describe('DEF-11: ComposeWorkspace materializes a whole-document edits[] payload as a MULTI-change redline, Accept-all commits all', () => {
  it('renders >=3 distinct-sub-key marks from one stored compose output and Accept-all leaves 0 pending', async () => {
    composeOutputsBySession[DOC_SESSION] = [
      {
        key: REVISE_LEDGER_REF,
        bindingId: REVISE_BINDING,
        turn: 1,
        disposition: 'compose',
        // Insertion-style edits (no `target_text`) — renders regardless of the loaded document's
        // text, same reliability rationale as the DEF-09 fixture above. Each becomes its own
        // insertion mark keyed by the `{REVISE_LEDGER_REF}#{i}` sub-key (usePendingRedline.materializeMany).
        payload: {
          edits: [
            { new_text: REVISE_EDIT_TEXTS[0] },
            { new_text: REVISE_EDIT_TEXTS[1] },
            { new_text: REVISE_EDIT_TEXTS[2] },
          ],
          rationale: 'Improved clarity across three clauses.',
        },
      },
    ];

    const bus = new PaneEventBus();
    renderWorkspaceWithBridge(bus);
    expect(bridgeRef.current?.hasRedlineAcceptHandler).toBe(true);

    await screen.findByRole('textbox', undefined, { timeout: 5000 });

    // Flow 5 — exactly what ConversationPane emits after a whole-document revise dispatch
    // (same signal DEF-09 uses; only the stored payload SHAPE differs).
    act(() => {
      bus.dispatch('workspace', {
        type: 'compose_assistant_insert',
        documentRef: { speDriveItemId: SPE_ID },
        sourceNodeId: REVISE_BINDING,
        sourcePlaybookId: '',
        contentHtml: '',
        format: 'html',
        insertMode: 'insert-at-cursor',
        requireUserConfirm: false,
        ledgerRef: REVISE_LEDGER_REF,
        sessionId: DOC_SESSION,
        timestamp: '2026-07-11T00:00:00.000Z',
      });
    });

    // (1) MULTIPLE marks materialize from the ONE stored output — NOT one (the whole point of
    // the multi-change redline; a regression to the single-edit path would render only ONE mark
    // carrying the drafted text and none of the others).
    const markSpans = await waitFor(
      () => {
        const els = Array.from(
          document.querySelectorAll<HTMLElement>(
            `[data-compose-mark="insertion"][data-ledger-ref^="${REVISE_LEDGER_REF}#"]`
          )
        );
        if (els.length < 3) throw new Error(`expected >=3 insertion marks, found ${els.length}`);
        return els;
      },
      { timeout: 5000 }
    );
    expect(markSpans.length).toBeGreaterThanOrEqual(3);
    const subKeys = new Set(markSpans.map(el => el.getAttribute('data-ledger-ref')));
    expect(subKeys.size).toBeGreaterThanOrEqual(3); // distinct #{i} sub-keys, not the same key repeated
    expect(subKeys.has(`${REVISE_LEDGER_REF}#0`)).toBe(true);
    expect(subKeys.has(`${REVISE_LEDGER_REF}#1`)).toBe(true);
    expect(subKeys.has(`${REVISE_LEDGER_REF}#2`)).toBe(true);
    const text = markSpans.map(el => el.textContent).join(' ');
    for (const t of REVISE_EDIT_TEXTS) expect(text).toContain(t);

    // (2) Accept-ALL via the BASE ledgerRef — the SAME conduit the Assistant confirmation
    // message's Accept control uses (DEF-12 `acceptRedlineViaBridge(ledgerRef)` with the base key,
    // never a sub-key). Every sub-change commits; none stay pending.
    act(() => {
      bridgeRef.current!.acceptRedline(REVISE_LEDGER_REF);
    });

    await waitFor(() => {
      const remaining = document.querySelectorAll(`[data-compose-mark][data-ledger-ref^="${REVISE_LEDGER_REF}"]`);
      expect(remaining.length).toBe(0);
    });
    const finalText = document.body.textContent ?? '';
    for (const t of REVISE_EDIT_TEXTS) expect(finalText).toContain(t);
  });
});

// ---------------------------------------------------------------------------
// DEF-11 — whole-document revision: review flags (comments[]) → anchored comment annotations
// ---------------------------------------------------------------------------
describe('DEF-11: ComposeWorkspace materializes a flag-risks comments[] payload as anchored comment annotations', () => {
  it('registers >=2 anchored comment AnchoredAnnotations (no accept/reject affordance — flags carry no edit)', async () => {
    composeOutputsBySession[DOC_SESSION] = [
      {
        key: FLAG_LEDGER_REF,
        bindingId: FLAG_BINDING,
        turn: 1,
        disposition: 'compose',
        payload: {
          comments: [
            { target_text: 'liability cap', comment: 'This limits recovery — confirm with client.' },
            { target_text: 'termination clause', comment: 'Consider adding a cure period.' },
          ],
        },
      },
    ];

    const bus = new PaneEventBus();
    renderWorkspace(bus);
    await screen.findByRole('textbox', undefined, { timeout: 5000 });

    // NOTE: ComposeWorkspace's refresh-durability effect (task 016 FR-04) re-materializes the
    // CURRENT stored compose output as soon as the session loads (page-refresh recovery) — so the
    // 2 review flags are already registered by the time the editor mounts, without waiting for an
    // explicit Flow-5 signal. The workspace's rehydrated-collection-count signal (FR-29/FR-33)
    // observably carries the count.
    const workspaceRoot = await screen.findByTestId('compose-workspace');
    await waitFor(() => {
      expect(workspaceRoot.getAttribute('data-compose-anchored-annotation-count')).toBe('2');
    });
    expect(document.querySelectorAll(`[data-compose-mark][data-ledger-ref^="${FLAG_LEDGER_REF}"]`).length).toBe(0); // no redline mark for a comment (flags carry no edit)

    // Idempotency: an explicit Flow-5 duplicate signal for the SAME stored output (exactly what
    // ConversationPane emits after dispatch) must NOT double-append the flags.
    act(() => {
      bus.dispatch('workspace', {
        type: 'compose_assistant_insert',
        documentRef: { speDriveItemId: SPE_ID },
        sourceNodeId: FLAG_BINDING,
        sourcePlaybookId: '',
        contentHtml: '',
        format: 'html',
        insertMode: 'insert-at-cursor',
        requireUserConfirm: false,
        ledgerRef: FLAG_LEDGER_REF,
        sessionId: DOC_SESSION,
        timestamp: '2026-07-11T00:05:00.000Z',
      });
    });
    await waitFor(() => {
      expect(workspaceRoot.getAttribute('data-compose-anchored-annotation-count')).toBe('2');
    });
  });
});

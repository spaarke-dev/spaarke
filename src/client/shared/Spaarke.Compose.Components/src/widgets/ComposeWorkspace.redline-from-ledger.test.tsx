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

// A minimal real .docx retained bytes fixture — kept for `content`/save-baseline fidelity
// even though task 013 (F-2 "one reader") means the EDITOR no longer decodes these bytes
// client-side; the mocked Load response below supplies `projection` directly instead (falls
// back to empty bytes if the fixture path can't be resolved — the no-target insertion
// redline renders either way, since it does not depend on the loaded document's own text).
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

// ---------------------------------------------------------------------------
// r8 task 055 — the review-flag ANCHOR fixtures.
//
// `loadParaIdMap` overrides the Load response's reference map for the task-055 block only; every
// other test in this file leaves it `[]`, which is exactly what the response carried before (the
// field was absent, and `hydratedParaIdMap` defaults to `[]`) — so nothing else moves.
//
// `annotationPosts` captures the FR-29 session-annotations write bodies. That POST is the only
// place a review flag's resolved `anchor.paraId` becomes observable: the annotation store is what
// the return-from-Word re-anchor (`PriorAnchorInput` -> `AnnotationReanchorService`) later reads.
// ---------------------------------------------------------------------------
let loadParaIdMap: unknown[] = [];
const annotationPosts: Array<{ anchoredAnnotations: Array<Record<string, unknown>> }> = [];

// R8 §GAPS-5 Phase 3 — every POST body sent to the Review Summary write endpoint. Before Phase 3 this
// array could only ever stay empty: nothing in the repo called that endpoint.
const reviewMemoPosts: Array<{ overallRisk: string; sections: Array<Record<string, unknown>> }> = [];

// Every SAVE body the client sent. Added 2026-09-07 with the Summary Page wiring: nothing previously
// asserted that an appendix field actually rides the save request, which is precisely how `summaryPage`
// stayed dead at the transport boundary for so long.
const saveBodies: Array<Record<string, unknown>> = [];

/** The review-flag annotations from the most recent session-annotations write. */
function latestReviewFlagAnnotations(): Array<Record<string, unknown>> {
  const last = annotationPosts[annotationPosts.length - 1];
  return (last?.anchoredAnnotations ?? []).filter(a => String(a.id ?? '').startsWith('ai-review:'));
}

function anchorOf(annotation: Record<string, unknown> | undefined): Record<string, unknown> {
  return (annotation?.anchor ?? {}) as Record<string, unknown>;
}

function sessionFromUrl(url: string): string {
  return decodeURIComponent(url.match(/\/sessions\/([^/]+)\//)?.[1] ?? '');
}

const authenticatedFetchMock = jest.fn(async (url: string, _init?: RequestInit): Promise<Response> => {
  // Compose Load (stored document). Task 013 (F-2 "one reader"): the client-side mammoth reader is
  // DELETED, so the mocked response MUST carry `projection` — a real BFF always does (tasks
  // 010/011/012) — otherwise the editor renders the error/unavailable state, not an editable
  // ProseMirror surface, and every assertion below that waits on `role="textbox"` times out.
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
        paraIdMap: loadParaIdMap,
        projection: {
          status: 'success',
          canEdit: true,
          html: '<p data-paraid="AB12CD34">Sample document body.</p>',
          warnings: [],
          schemaVersion: 'compose-html-v1',
        },
      }),
    } as unknown as Response;
  }
  // FR-29 session-annotations write (r8 task 055 observes the resolved anchors here). Checked BEFORE
  // the generic 404 and AFTER `/compose-outputs` so the two session routes never shadow each other.
  if (url.includes('/annotations')) {
    try {
      annotationPosts.push(JSON.parse(String(_init?.body ?? '{}')));
    } catch {
      /* a malformed body is a test-harness bug, not a product path — ignore */
    }
    return { ok: true, status: 200, json: async () => ({}) } as unknown as Response;
  }
  // Session-ledger compose-outputs read (the DEF-09 read side).
  if (url.includes('/compose-outputs')) {
    composeOutputsReadUrls.push(url);
    const session = sessionFromUrl(url);
    return { ok: true, status: 200, json: async () => composeOutputsBySession[session] ?? [] } as unknown as Response;
  }
  // Task 032 (dedupe-guard test) — a minimal Save response so `triggerSave` completes the
  // 'loaded'→'saving'→'loaded' status cycle without unmounting ComposeEditor (`showEditor` covers
  // BOTH statuses), the SAME-editor-instance re-materialize race notes/031-execution-notes.md
  // escalated.
  if (url.includes('/api/compose/documents/') && url.includes('/save')) {
    try {
      saveBodies.push(JSON.parse(String(_init?.body ?? '{}')));
    } catch {
      /* a malformed body is a harness bug, not a product path */
    }
    return {
      ok: true,
      status: 200,
      json: async () => ({
        documentSpeId: SPE_ID,
        documentRecordId: 'sprk-doc-1',
        size: 1024,
        wasPromotedThisSave: false,
      }),
    } as unknown as Response;
  }
  // R8 §GAPS-5 Phase 3 — Review Summary write + read-back. The GET deliberately 404s until a POST has
  // landed, mirroring production: the row the READ actions look for is the row the POST writes, which
  // is exactly the loop that had no entry point before Phase 3.
  if (url.includes('/review-memo')) {
    const method = (_init?.method ?? 'GET').toUpperCase();
    if (method === 'POST') {
      const body = JSON.parse(String(_init?.body ?? '{}'));
      reviewMemoPosts.push(body);
      return {
        ok: true,
        status: 201,
        json: async () => ({
          analysisId: '00000000-0000-0000-0000-0000000a0a01',
          outputId: '00000000-0000-0000-0000-0000000a0a02',
          sectionCount: body.sections?.length ?? 0,
        }),
      } as unknown as Response;
    }
    const latest = reviewMemoPosts[reviewMemoPosts.length - 1];
    if (!latest) {
      return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
    }
    return {
      ok: true,
      status: 200,
      json: async () => ({
        analysisId: '00000000-0000-0000-0000-0000000a0a01',
        analysisName: 'Agreement Review',
        documentName: 'contract.docx',
        memo: {
          schemaVersion: 'review-memo-v1',
          overallRisk: latest.overallRisk,
          sectionCount: latest.sections.length,
          sections: [],
        },
      }),
    } as unknown as Response;
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
import { ComposeWorkspace, projectLedgerFindingsToAdvisoryComments } from './ComposeWorkspace';

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
  loadParaIdMap = [];
  annotationPosts.length = 0;
  reviewMemoPosts.length = 0;
  saveBodies.length = 0;
  // Task 032 — the 128KB-budget degraded-restore marker rides `window.sessionStorage`, keyed by
  // session id. DOC_SESSION is a shared constant across this whole file's tests, so clear it every
  // test to prevent cross-test leakage of a marker one test wrote.
  window.sessionStorage.clear();
});

describe('DEF-09/DEF-12: ComposeWorkspace materializes the redline mark + per-change on-click from the DOCUMENT session ledger', () => {
  it('reads compose-outputs from the document session, materializes the redline MARK, keeps per-change on-click, and drops the primary bar', async () => {
    const bus = new PaneEventBus();
    renderWorkspace(bus);

    // Document loads (session id = DOC_SESSION) and the editor mounts.
    await screen.findByRole('textbox');

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

    await screen.findByRole('textbox');

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
    await screen.findByRole('textbox');

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

// ---------------------------------------------------------------------------
// r8 task 055 (FR-C03) — whole-document review flags carry a DETERMINISTIC anchor.
//
// `AnchoredAnnotationAnchor.paraId` shipped in R3 FR-11 documented as the PRIMARY anchor
// ("paraId-FIRST, then the textPattern/paragraphHint fuzzy fallback"), and its CONSUMER is live: the
// return-from-Word re-anchor sends it to `AnnotationReanchorService`, which "resolves by this FIRST
// and only falls back to the fuzzy scorer when it is absent". The PRODUCER was dark —
// `registerAiReviewComments` wrote `{ textPattern, paragraphHint: -1, spanId }` and no paraId, so
// EVERY `flag-risks` flag went through the fuzzy scorer even when the model named its paragraph
// exactly. These tests close that, and fix the filter defect the same change surfaces.
// ---------------------------------------------------------------------------
const ANCHORED_FLAG_BINDING = 'binding-flag-risks-anchored-guid';
const ANCHORED_FLAG_LEDGER_REF = `${ANCHORED_FLAG_BINDING}@t1`;

/** The Load response's reference map for this block: clauses 4.1 / 4.2 under section 4. */
const FLAG_PARA_ID_MAP = [
  { index: 0, paraId: 'AAAA0041', isMinted: false, computedNumber: '4.1', listPath: [4, 1] },
  { index: 1, paraId: 'AAAA0042', isMinted: false, computedNumber: '4.2', listPath: [4, 2] },
];

function seedFlags(comments: unknown[]): void {
  loadParaIdMap = FLAG_PARA_ID_MAP;
  composeOutputsBySession[DOC_SESSION] = [
    {
      key: ANCHORED_FLAG_LEDGER_REF,
      bindingId: ANCHORED_FLAG_BINDING,
      turn: 1,
      disposition: 'compose',
      payload: { comments },
    },
  ];
}

async function renderAndWaitForFlags(expectedCount: number): Promise<void> {
  renderWorkspace(new PaneEventBus());
  await screen.findByRole('textbox');
  const workspaceRoot = await screen.findByTestId('compose-workspace');
  await waitFor(() => {
    expect(workspaceRoot.getAttribute('data-compose-anchored-annotation-count')).toBe(String(expectedCount));
  });
  if (expectedCount > 0) {
    await waitFor(() => expect(latestReviewFlagAnnotations()).toHaveLength(expectedCount));
  }
}

describe('r8 task 055: a whole-document review flag resolves its anchor deterministically', () => {
  it('a target_para_id flag carries that paraId onto the annotation anchor', async () => {
    seedFlags([
      { target_para_id: 'AAAA0042', target_text: 'liability cap', comment: 'Confirm the cap with the client.' },
    ]);
    await renderAndWaitForFlags(1);

    const anchor = anchorOf(latestReviewFlagAnnotations()[0]);
    expect(anchor.paraId).toBe('AAAA0042');
    // The prose anchor is RETAINED, not replaced — it is the documented fuzzy fallback for a
    // document Word has re-saved (which regenerates paraIds).
    expect(anchor.textPattern).toBe('liability cap');
  });

  it('a target_ref flag resolves the citation through the reference map onto the anchor', async () => {
    seedFlags([{ target_ref: 'clause 4.1', target_text: 'twelve months', comment: 'Cap is unusually short.' }]);
    await renderAndWaitForFlags(1);

    expect(anchorOf(latestReviewFlagAnnotations()[0]).paraId).toBe('AAAA0041');
  });

  it('DEFECT (task 055 §5): a flag with a deterministic anchor and NO target_text is KEPT, not dropped', async () => {
    // Task 054 lets the model return `target_para_id` with weak or absent prose — L-1 (hard breaks
    // collapse in `collectBlocks().text`) means a quoted excerpt may not even exist verbatim. The
    // shipped `c.target.length > 0 && c.body.length > 0` gate would silently drop exactly the
    // BEST-anchored flags. The gate must be "a resolvable anchor OR non-empty prose".
    seedFlags([
      { target_para_id: 'AAAA0042', comment: 'Indemnity is one-way; flag for negotiation.' },
      { target_ref: '4.1', comment: 'Cap should be mutual.' },
    ]);
    await renderAndWaitForFlags(2);

    const flags = latestReviewFlagAnnotations();
    expect(flags.map(f => anchorOf(f).paraId)).toEqual(['AAAA0042', 'AAAA0041']);
    expect(flags.map(f => f.body)).toEqual(['Indemnity is one-way; flag for negotiation.', 'Cap should be mutual.']);
  });

  it('a text-only flag still registers, with no fabricated paraId (the shipped path is unchanged)', async () => {
    seedFlags([{ target_text: 'termination clause', comment: 'Consider adding a cure period.' }]);
    await renderAndWaitForFlags(1);

    const anchor = anchorOf(latestReviewFlagAnnotations()[0]);
    expect(anchor.paraId).toBeUndefined();
    expect(anchor.textPattern).toBe('termination clause');
  });

  it('two anchors that DISAGREE never fabricate a paraId — the flag survives on its prose alone', async () => {
    seedFlags([
      {
        target_para_id: 'AAAA0041',
        target_ref: '4.2',
        target_text: 'liability cap',
        comment: 'Ambiguously anchored.',
      },
    ]);
    await renderAndWaitForFlags(1);

    const anchor = anchorOf(latestReviewFlagAnnotations()[0]);
    expect(anchor.paraId).toBeUndefined();
    expect(anchor.textPattern).toBe('liability cap');
  });

  it('a flag with an unresolvable citation and no prose has nothing to anchor to and is skipped', async () => {
    seedFlags([
      { target_ref: 'clause 99.9', comment: 'Nothing to anchor this to.' },
      { target_para_id: 'AAAA0042', comment: 'This one is anchorable.' },
    ]);
    await renderAndWaitForFlags(1);

    expect(latestReviewFlagAnnotations().map(f => f.body)).toEqual(['This one is anchorable.']);
  });

  it('a flag with no BODY is still skipped whatever its anchor says (a comment with no text is not a flag)', async () => {
    seedFlags([
      { target_para_id: 'AAAA0041', comment: '   ' },
      { target_para_id: 'AAAA0042', comment: 'Real finding.' },
    ]);
    await renderAndWaitForFlags(1);

    expect(latestReviewFlagAnnotations().map(f => f.body)).toEqual(['Real finding.']);
  });

  it('the ledger-key dedup still holds: a duplicate Flow-5 signal never double-appends anchored flags', async () => {
    seedFlags([
      { target_para_id: 'AAAA0041', comment: 'One.' },
      { target_para_id: 'AAAA0042', comment: 'Two.' },
    ]);
    const bus = new PaneEventBus();
    renderWorkspace(bus);
    await screen.findByRole('textbox');
    const workspaceRoot = await screen.findByTestId('compose-workspace');
    await waitFor(() => {
      expect(workspaceRoot.getAttribute('data-compose-anchored-annotation-count')).toBe('2');
    });

    act(() => {
      bus.dispatch('workspace', {
        type: 'compose_assistant_insert',
        documentRef: { speDriveItemId: SPE_ID },
        sourceNodeId: ANCHORED_FLAG_BINDING,
        sourcePlaybookId: '',
        contentHtml: '',
        format: 'html',
        insertMode: 'insert-at-cursor',
        requireUserConfirm: false,
        ledgerRef: ANCHORED_FLAG_LEDGER_REF,
        sessionId: DOC_SESSION,
        timestamp: '2026-08-25T00:05:00.000Z',
      });
    });
    await waitFor(() => {
      expect(workspaceRoot.getAttribute('data-compose-anchored-annotation-count')).toBe('2');
    });
  });
});

// ---------------------------------------------------------------------------
// FR-16 (agreements-r1 task 030) — DURABLE AGREEMENT-REVIEW findings.
// A compose-disposition output carrying `flaggedSections[]` re-materializes as PERSISTENT advisory
// comment threads (placeAdvisoryComments — metadata-preserving), NOT a redline, deterministically,
// with ZERO LLM re-run on reopen.
// ---------------------------------------------------------------------------
const REVIEW_BINDING = 'binding-agreement-review-guid';
const REVIEW_LEDGER_REF = `${REVIEW_BINDING}@t1`;

// The pure projection is the ONLY place a durable-review payload's per-clause metadata could be
// dropped on the reopen path, so it carries the authoritative metadata + both-vintages proof
// (the integration tests below then prove the branch WIRES it to placeAdvisoryComments on reopen).
describe('FR-16 task 030: projectLedgerFindingsToAdvisoryComments — flaggedSections[] → AdvisoryCommentInput (both vintages, metadata intact)', () => {
  it('LEGACY vintage (explanation) — carries explanation verbatim + sectionRef/riskLevel/standardRef', () => {
    const items = projectLedgerFindingsToAdvisoryComments([
      {
        quotedText: 'The receiving party shall retain information indefinitely.',
        explanation: 'Indefinite retention deviates from the standard 3-year term.',
        sectionRef: '3.2',
        riskLevel: 'High',
        standardRef: 'B5 - Retention',
      },
    ]);
    expect(items).toEqual([
      {
        targetText: 'The receiving party shall retain information indefinitely.',
        explanation: 'Indefinite retention deviates from the standard 3-year term.',
        sectionRef: '3.2',
        riskLevel: 'High',
        standardRef: 'B5 - Retention',
        flaggedClause: undefined,
        assessment: undefined,
      },
    ]);
  });

  it('POST-SPLIT vintage (flaggedClause + assessment, no explanation) — composes explanation AND carries the discrete fields through', () => {
    const items = projectLedgerFindingsToAdvisoryComments([
      {
        quotedText: 'Either party may terminate on 5 days notice.',
        flaggedClause: 'The termination notice period is 5 days.',
        assessment: 'The firm standard requires at least 30 days; 5 days is materially short.',
        sectionRef: '7.1',
        riskLevel: 'Medium',
        standardRef: 'B9 - Termination',
      },
    ]);
    expect(items).toHaveLength(1);
    // Discrete fields carried through unchanged (task 052 structured render/export — no string-parsing).
    expect(items[0].flaggedClause).toBe('The termination notice period is 5 days.');
    expect(items[0].assessment).toBe('The firm standard requires at least 30 days; 5 days is materially short.');
    // Composed explanation (thread text / legacy-degrade source) = the two discrete fields joined.
    expect(items[0].explanation).toBe(
      'The termination notice period is 5 days.\n\nThe firm standard requires at least 30 days; 5 days is materially short.'
    );
    expect(items[0].sectionRef).toBe('7.1');
    expect(items[0].riskLevel).toBe('Medium');
    expect(items[0].standardRef).toBe('B9 - Termination');
  });

  it('legacy explanation WINS as the text source when both vintages coexist (deterministic precedence); discrete fields still carried', () => {
    const items = projectLedgerFindingsToAdvisoryComments([
      {
        quotedText: 'Clause X.',
        explanation: 'LEGACY EXPLANATION',
        flaggedClause: 'discrete clause',
        assessment: 'discrete assessment',
      },
    ]);
    expect(items[0].explanation).toBe('LEGACY EXPLANATION');
    expect(items[0].flaggedClause).toBe('discrete clause');
    expect(items[0].assessment).toBe('discrete assessment');
  });

  it('skips malformed entries (missing quotedText, blank anchor, no body, non-object, null) without throwing — never a partial crash', () => {
    const items = projectLedgerFindingsToAdvisoryComments([
      { explanation: 'no quotedText — skipped' },
      { quotedText: '   ', explanation: 'blank anchor — skipped' },
      { quotedText: 'has anchor but no body — skipped' },
      'not-an-object',
      null,
      { quotedText: 'Valid clause survives.', assessment: 'Only-assessment vintage still yields a body.' },
    ]);
    expect(items).toHaveLength(1);
    expect(items[0].targetText).toBe('Valid clause survives.');
    expect(items[0].explanation).toBe('Only-assessment vintage still yields a body.');
  });

  it('empty input → empty output (no throw)', () => {
    expect(projectLedgerFindingsToAdvisoryComments([])).toEqual([]);
  });
});

describe('FR-16 task 030: ComposeWorkspace re-materializes a durable review flaggedSections[] payload as advisory comments (NOT a redline), idempotently, with zero dispatch', () => {
  it('reopening a reviewed document restores the advisory comment anchor from the ledger — right clause, no redline mark, no re-dispatch, idempotent', async () => {
    composeOutputsBySession[DOC_SESSION] = [
      {
        key: REVIEW_LEDGER_REF,
        bindingId: REVIEW_BINDING,
        turn: 1,
        disposition: 'compose',
        // POST-split vintage (flaggedClause + assessment, no explanation). `quotedText` equals the
        // loaded document's only paragraph text, so strict resolution anchors it deterministically.
        payload: {
          overallRisk: 'High',
          flaggedSections: [
            {
              quotedText: 'Sample document body.',
              flaggedClause: 'The body imposes an unqualified obligation.',
              assessment: 'This deviates from the firm standard, which requires a materiality qualifier.',
              sectionRef: '1.1',
              riskLevel: 'High',
              standardRef: 'B5 - Obligations',
            },
          ],
        },
      },
    ];

    const bus = new PaneEventBus();
    renderWorkspace(bus);
    await screen.findByRole('textbox');

    // The FR-04 refresh-durability effect re-materializes the CURRENT stored compose output on load.
    // The findings branch resolves the flagged clause and places a PERSISTENT advisory comment thread —
    // observable as a `span[data-comment-id]` mark carrying the quoted clause (the same signal the
    // ComposeEditor.advisoryComments unit test asserts).
    const anchor = await waitFor(
      () => {
        const spans = document.querySelectorAll<HTMLElement>('span[data-comment-id]');
        if (spans.length !== 1) throw new Error(`expected exactly 1 advisory anchor, found ${spans.length}`);
        return spans[0];
      },
      { timeout: 5000 }
    );
    expect(anchor.textContent).toBe('Sample document body.'); // anchored to the RIGHT clause

    // A findings payload is NOT an edit — it must NOT fall into the edits / single-edit redline branch.
    // (The placed comment anchor is `data-compose-mark="comment-anchor"`; a REDLINE is insertion/deletion.)
    expect(document.querySelectorAll('[data-compose-mark="insertion"], [data-compose-mark="deletion"]').length).toBe(0);

    // ZERO dispatch / ZERO LLM re-run: reopening only READ the stored ledger (compose-outputs) + the
    // document Load. Every network call is a GET — there is NO POST/dispatch of any kind (the point of FR-16).
    expect(composeOutputsReadUrls.some(u => u.includes(`/sessions/${DOC_SESSION}/compose-outputs`))).toBe(true);
    const nonReadCalls = authenticatedFetchMock.mock.calls.filter(
      ([, init]) => ((init?.method ?? 'GET') as string).toUpperCase() !== 'GET'
    );
    expect(nonReadCalls).toEqual([]);

    // Idempotency: a duplicate Flow-5 signal for the SAME stored output (exactly what ConversationPane
    // emits after a dispatch) must NOT place a second thread — the lastMaterializedKey guard.
    act(() => {
      bus.dispatch('workspace', {
        type: 'compose_assistant_insert',
        documentRef: { speDriveItemId: SPE_ID },
        sourceNodeId: REVIEW_BINDING,
        sourcePlaybookId: '',
        contentHtml: '',
        format: 'html',
        insertMode: 'insert-at-cursor',
        requireUserConfirm: false,
        ledgerRef: REVIEW_LEDGER_REF,
        sessionId: DOC_SESSION,
        timestamp: '2026-07-31T00:00:00.000Z',
      });
    });
    await waitFor(() => {
      expect(document.querySelectorAll('span[data-comment-id]').length).toBe(1);
    });
  });

  it('negative: a malformed findings payload (no usable flagged sections) logs + skips gracefully — no crash, no placement, no redline', async () => {
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
    try {
      composeOutputsBySession[DOC_SESSION] = [
        {
          key: REVIEW_LEDGER_REF,
          bindingId: REVIEW_BINDING,
          turn: 1,
          disposition: 'compose',
          // A findings-SHAPED payload (flaggedSections present) whose entries are all unusable:
          // missing quotedText, blank anchor, wrong type. The projection yields [].
          payload: {
            overallRisk: 'Low',
            flaggedSections: [{ explanation: 'no quotedText' }, { quotedText: '   ' }, 'junk'],
          },
        },
      ];

      const bus = new PaneEventBus();
      renderWorkspace(bus);
      // The editor still mounts (no crash); the FR-04 effect logs + skips.
      await screen.findByRole('textbox');
      await waitFor(() =>
        expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('no usable flagged sections'), REVIEW_LEDGER_REF)
      );
      // Nothing placed, no redline — a malformed payload is a graceful skip, not a partial placement.
      expect(document.querySelectorAll('span[data-comment-id]').length).toBe(0);
      expect(document.querySelectorAll('[data-compose-mark]').length).toBe(0);
    } finally {
      warnSpy.mockRestore();
    }
  });
});

// ---------------------------------------------------------------------------
// Task 032 — FR-16 completion: summary-panel restore, 128KB payload budget (Leg B — visible
// notice), findings/edit coexistence + supersede protection, 031-residual dedupe guard.
// ---------------------------------------------------------------------------

describe('FR-16 task 032: summary-panel restore (gutter + panel, zero dispatch)', () => {
  it('reopening a reviewed document restores gutter notes AND the summary-panel row (with risk data) — zero LLM calls', async () => {
    composeOutputsBySession[DOC_SESSION] = [
      {
        key: REVIEW_LEDGER_REF,
        bindingId: REVIEW_BINDING,
        turn: 1,
        disposition: 'compose',
        payload: {
          overallRisk: 'High',
          flaggedSections: [
            {
              quotedText: 'Sample document body.',
              flaggedClause: 'The body imposes an unqualified obligation.',
              assessment: 'This deviates from the firm standard, which requires a materiality qualifier.',
              sectionRef: '1.1',
              riskLevel: 'High',
              standardRef: 'B5 - Obligations',
            },
          ],
        },
      },
    ];

    const bus = new PaneEventBus();
    renderWorkspace(bus);
    await screen.findByRole('textbox');

    // (1) Gutter restore — the pre-existing task 030 guarantee, re-proven here alongside the panel
    // for the FULL closed-guarantee assertion (acceptance criterion 1).
    await waitFor(() => {
      expect(document.querySelectorAll('span[data-comment-id]').length).toBe(1);
    });

    // (2) Summary-panel restore (task 032 gap #1 — the NEW behavior). The panel defaults collapsed
    // (UAT round-7 #4); open it via the toolbar toggle, then assert the row carries the RIGHT
    // content + risk data — previously this stayed EMPTY on reopen (only a LIVE review populated it).
    const toggle = await screen.findByTestId('compose-format-review-summary-toggle', undefined, { timeout: 5000 });
    act(() => {
      fireEvent.click(toggle);
    });
    const row = await screen.findByTestId('nda-review-summary-finding-0', undefined, { timeout: 5000 });
    // R8 §GAPS-5 Phase 1 — this assertion CHANGED, because it had pinned the defect. It previously
    // expected the flaggedClause ("The body imposes an unqualified obligation"), which is the grounded
    // FACT. That was never the intended row content: the row renders a TAKEAWAY, and the takeaway is
    // the judgment. It read the fact only because this mapping dropped `assessment`, leaving the panel
    // to marker-parse a composed blob that carries no markers — so deriveTakeaway returned the blob's
    // first sentence. Restoring the discrete fields makes the row render the judgment, which is what
    // "the row carries the RIGHT content" always meant. (Trailing period stripped by deriveTakeaway.)
    expect(row.textContent).toContain('Deviates from the firm standard, which requires a materiality qualifier');
    // The grounded fact belongs on the in-document gutter note, NOT in the summary's scan strip.
    expect(row.textContent).not.toContain('The body imposes an unqualified obligation');
    expect(row.textContent).toContain('High'); // the per-finding risk badge

    // (3) Zero LLM calls / zero dispatch — every network call is a GET (the point of FR-16).
    const nonReadCalls = authenticatedFetchMock.mock.calls.filter(
      ([, init]) => ((init?.method ?? 'GET') as string).toUpperCase() !== 'GET'
    );
    expect(nonReadCalls).toEqual([]);
  });
});

// ---------------------------------------------------------------------------
// R8 §GAPS-5 Phase 1 — the FR-05 discrete fields (flaggedClause / assessment) must survive into the
// summary panel on BOTH population paths. The ledger-restore leg is asserted in the describe above;
// this is the LIVE leg. Both mappings hand-copied five fields and dropped these two, so the panel
// fell back to marker-parsing a blob that post-split payloads compose WITHOUT markers.
// ---------------------------------------------------------------------------

describe('R8 §GAPS-5 Phase 1: discrete FR-05 fields survive the LIVE advisory path into the summary', () => {
  it('a live post-split review renders the ASSESSMENT (judgment) as the summary takeaway', async () => {
    composeOutputsBySession[DOC_SESSION] = [];

    const bus = new PaneEventBus();
    renderWorkspace(bus);
    await screen.findByRole('textbox');

    // Mirrors useNdaReviewAdvisoryCommentsBridge's post-split projection: discrete fields carried
    // through, with `explanation` COMPOSED as [flaggedClause, assessment].join('\n\n') — no
    // "Grounded fact —"/"Judgment —" markers, which is precisely why parsing it cannot work.
    const flaggedClause = 'The body imposes an unqualified obligation.';
    const assessment = 'This deviates from the firm standard, which requires a materiality qualifier.';
    act(() => {
      bus.dispatch('workspace', {
        type: 'compose_advisory_comments',
        advisoryComments: [
          {
            targetText: 'Sample document body.',
            explanation: `${flaggedClause}\n\n${assessment}`,
            flaggedClause,
            assessment,
            sectionRef: '1.1',
            riskLevel: 'High',
            standardRef: 'B5 - Obligations',
          },
        ],
        overallRisk: 'High',
        sessionId: DOC_SESSION,
        timestamp: '2026-09-07T00:00:00.000Z',
      });
    });

    const toggle = await screen.findByTestId('compose-format-review-summary-toggle', undefined, { timeout: 5000 });
    act(() => {
      fireEvent.click(toggle);
    });

    const row = await screen.findByTestId('nda-review-summary-finding-0', undefined, { timeout: 5000 });
    expect(row.textContent).toContain('Deviates from the firm standard, which requires a materiality qualifier');
    // The grounded fact is the OTHER half — it belongs on the gutter note, not the scan strip. Before
    // Phase 1 this is what the row showed, because `assessment` never reached the panel.
    expect(row.textContent).not.toContain('The body imposes an unqualified obligation');
  });
});

describe('FR-16 task 032: 128KB payload budget (Leg B — explicit degraded-restore notice, never silent absence)', () => {
  it('a findings-shaped output present but yielding ZERO usable items (corrupted/partial) surfaces a visible notice, not a crash', async () => {
    composeOutputsBySession[DOC_SESSION] = [
      {
        key: REVIEW_LEDGER_REF,
        bindingId: REVIEW_BINDING,
        turn: 1,
        disposition: 'compose',
        payload: { overallRisk: 'Low', flaggedSections: [{ explanation: 'no quotedText' }, { quotedText: '   ' }] },
      },
    ];

    const bus = new PaneEventBus();
    renderWorkspace(bus);
    await screen.findByRole('textbox');

    const banner = await screen.findByTestId('compose-workspace-review-findings-degraded-banner', undefined, {
      timeout: 5000,
    });
    expect(banner.textContent).toMatch(/couldn.t be fully restored/i);
    // Negative half of acceptance criterion 5: no crash, no placement, no redline.
    expect(document.querySelectorAll('span[data-comment-id]').length).toBe(0);
    expect(document.querySelectorAll('[data-compose-mark]').length).toBe(0);
  });

  it('a truncated/skipped findings entry (server-side ADR-040 cap, invisible to the client GET) surfaces a visible notice via the same-tab durability marker — never silent absence', async () => {
    // First mount: a normal review restores cleanly and (as a side effect of the successful
    // restore) records the same-tab durability marker (task 032, Leg B).
    composeOutputsBySession[DOC_SESSION] = [
      {
        key: REVIEW_LEDGER_REF,
        bindingId: REVIEW_BINDING,
        turn: 1,
        disposition: 'compose',
        payload: {
          overallRisk: 'Medium',
          flaggedSections: [
            { quotedText: 'Sample document body.', explanation: 'A finding.', sectionRef: '1.1', riskLevel: 'Medium' },
          ],
        },
      },
    ];
    const bus1 = new PaneEventBus();
    const { unmount } = renderWorkspace(bus1);
    await screen.findByRole('textbox');
    await waitFor(() => {
      expect(document.querySelectorAll('span[data-comment-id]').length).toBe(1);
    });
    unmount();

    // Simulate the ADR-040 128KB cap having since truncated this entry — `ChatEndpoints.
    // ProjectComposeOutputs` SKIPS a truncation-marker entry entirely, so a LATER read shows
    // COMPLETELY EMPTY compose-outputs for this session — indistinguishable from "no review ran"
    // on the response alone. The sessionStorage marker from the first mount survives (same tab).
    composeOutputsBySession[DOC_SESSION] = [];

    const bus2 = new PaneEventBus();
    renderWorkspace(bus2);
    await screen.findByRole('textbox');

    const banner = await screen.findByTestId('compose-workspace-review-findings-degraded-banner', undefined, {
      timeout: 5000,
    });
    expect(banner.textContent).toMatch(/couldn.t be fully restored/i);
    expect(banner.textContent).toContain('1'); // the marker's expectedCount
    // Honestly nothing placed this time — the entry is genuinely gone from the read-projection.
    expect(document.querySelectorAll('span[data-comment-id]').length).toBe(0);
  });
});

describe("FR-16 task 032: findings + edit coexistence — a later edit no longer evicts an earlier review's durability", () => {
  it('a findings output (turn 1) and a LATER edit output (turn 2, higher turn) BOTH restore on reopen', async () => {
    composeOutputsBySession[DOC_SESSION] = [
      {
        key: REVIEW_LEDGER_REF,
        bindingId: REVIEW_BINDING,
        turn: 1,
        disposition: 'compose',
        payload: {
          overallRisk: 'High',
          flaggedSections: [
            { quotedText: 'Sample document body.', explanation: 'A finding.', sectionRef: '1.1', riskLevel: 'High' },
          ],
        },
      },
      {
        // A LATER draft-alternative — strictly HIGHER turn than the review. Pre-032, the untargeted
        // `composeOutputs.reduce((a,b) => b.turn>a.turn?b:a)` picked ONLY this one, silently
        // dropping the review's findings durability (the exact bug this task closes).
        key: LEDGER_REF,
        bindingId: DRAFT_BINDING,
        turn: 2,
        disposition: 'compose',
        payload: { new_text: NEW_TEXT },
      },
    ];

    const bus = new PaneEventBus();
    renderWorkspace(bus);
    await screen.findByRole('textbox');

    // BOTH restore — the findings anchor AND the edit redline mark, simultaneously, with zero dispatch.
    await waitFor(() => {
      expect(document.querySelectorAll('span[data-comment-id]').length).toBe(1);
    });
    await waitFor(() => {
      expect(document.querySelector(`[data-compose-mark="insertion"][data-ledger-ref="${LEDGER_REF}"]`)).not.toBeNull();
    });

    const nonReadCalls = authenticatedFetchMock.mock.calls.filter(
      ([, init]) => ((init?.method ?? 'GET') as string).toUpperCase() !== 'GET'
    );
    expect(nonReadCalls).toEqual([]);
  });
});

describe("FR-16 task 032: supersede protection — an edit bindingId's own turn progression never retracts a findings output", () => {
  it('an edit output that has been superseded (a later same-bindingId turn) leaves a DIFFERENT-bindingId findings output fully intact', async () => {
    // Server-side supersede (ChatEndpoints.SupersedeComposeOutput) is scoped to ONE bindingId
    // (`ComposeDisposition.ResolveCurrent(outputs, prior.BindingId)` — verified by reading the
    // implementation) and appends a NEW higher-turn entry for that SAME bindingId. The findings
    // output below carries a DIFFERENT bindingId (the review Binding is never the edit Binding —
    // task 031's own finding), so it is structurally UNREACHABLE by any edit-binding's supersede
    // call. This proves the CLIENT half of that guarantee: regardless of how many turns the edit
    // bindingId accumulates (a "Try another" / supersede cycle), the findings output restores.
    composeOutputsBySession[DOC_SESSION] = [
      {
        key: REVIEW_LEDGER_REF,
        bindingId: REVIEW_BINDING,
        turn: 1,
        disposition: 'compose',
        payload: {
          overallRisk: 'Medium',
          flaggedSections: [
            { quotedText: 'Sample document body.', explanation: 'A finding.', sectionRef: '1.1', riskLevel: 'Medium' },
          ],
        },
      },
      // v1 of the edit (the ORIGINAL suggestion) — now superseded.
      {
        key: `${DRAFT_BINDING}@t2`,
        bindingId: DRAFT_BINDING,
        turn: 2,
        disposition: 'compose',
        payload: { new_text: 'draft v1 — superseded' },
      },
      // v2 (the CURRENT head after "Try another" / supersede) — the SAME bindingId, a later turn.
      {
        key: `${DRAFT_BINDING}@t3`,
        bindingId: DRAFT_BINDING,
        turn: 3,
        disposition: 'compose',
        payload: { new_text: 'draft v2 — current' },
      },
    ];

    const bus = new PaneEventBus();
    renderWorkspace(bus);
    await screen.findByRole('textbox');

    // Findings unaffected — the gutter anchor restores.
    await waitFor(() => {
      expect(document.querySelectorAll('span[data-comment-id]').length).toBe(1);
    });
    // Only the CURRENT (highest-turn) edit materializes — the superseded v1 text never renders.
    await waitFor(() => {
      expect(
        document.querySelector(`[data-compose-mark="insertion"][data-ledger-ref="${DRAFT_BINDING}@t3"]`)
      ).not.toBeNull();
    });
    expect(document.body.textContent).not.toContain('draft v1 — superseded');
    expect(document.body.textContent).toContain('draft v2 — current');
  });
});

describe('FR-16 task 032: 031-residual dedupe guard — a same-mount status-cycle never double-places a live-materialized review', () => {
  it('a live review placement survives a Save status-cycle (loaded→saving→loaded, SAME editor instance) without duplicating the advisory comment', async () => {
    // No findings in the ledger at MOUNT time — the review happens LIVE, in-session (the realistic
    // production sequence: dispatch → live `compose_advisory_comments` event → THEN the write lands
    // in the ledger, per ADR-040 store-before-render).
    composeOutputsBySession[DOC_SESSION] = [];

    const bus = new PaneEventBus();
    renderWorkspace(bus);
    await screen.findByRole('textbox');

    // LIVE placement (mirrors useNdaReviewAdvisoryCommentsBridge's emitFromResult — no `ledgerRef`
    // on the wire today, verified by reading the bridge).
    act(() => {
      bus.dispatch('workspace', {
        type: 'compose_advisory_comments',
        advisoryComments: [
          { targetText: 'Sample document body.', explanation: 'A finding.', sectionRef: '1.1', riskLevel: 'Medium' },
        ],
        overallRisk: 'Medium',
        sessionId: DOC_SESSION,
        timestamp: '2026-07-31T00:00:00.000Z',
      });
    });
    await waitFor(() => {
      expect(document.querySelectorAll('span[data-comment-id]').length).toBe(1);
    });

    // The write has since landed in the ledger (same clause, same session) — exactly what a
    // subsequent GET would now return.
    composeOutputsBySession[DOC_SESSION] = [
      {
        key: REVIEW_LEDGER_REF,
        bindingId: REVIEW_BINDING,
        turn: 1,
        disposition: 'compose',
        payload: {
          overallRisk: 'Medium',
          flaggedSections: [
            { quotedText: 'Sample document body.', explanation: 'A finding.', sectionRef: '1.1', riskLevel: 'Medium' },
          ],
        },
      },
    ];

    // Trigger a Save — `status` cycles 'loaded'→'saving'→'loaded' WITHOUT `sessionId` changing and
    // WITHOUT unmounting ComposeEditor (`showEditor` covers both statuses) — the SAME residual
    // notes/031-execution-notes.md escalated. The FR-04 effect's `[state.status, state.sessionId]`
    // deps re-fire on BOTH transitions, re-running the untargeted materialize pass in the SAME
    // (still-alive) editor instance that already holds the live-placed comment.
    // `data-testid="compose-format-save"` is on the SplitButton's OUTER wrapper (Fluent v9); the
    // clickable primary-action element is the nested `<button>` — click THAT, not the wrapper div.
    const saveWrapper = await screen.findByTestId('compose-format-save', undefined, { timeout: 5000 });
    const saveButton = saveWrapper.querySelector('button');
    if (!saveButton) throw new Error('Save split-button primary action <button> not found inside the wrapper');
    act(() => {
      fireEvent.click(saveButton);
    });
    await screen.findByTestId('compose-workspace-save-success-banner', undefined, { timeout: 5000 });

    // The dedupe guard: still exactly ONE advisory anchor — the content-signature check recognized
    // the SAME clause set the live path already placed and skipped the ledger-driven re-placement
    // (`placeAdvisoryComments` has no idempotency of its own).
    expect(document.querySelectorAll('span[data-comment-id]').length).toBe(1);
  });
});

// ---------------------------------------------------------------------------
// R8 §GAPS-5 Phase 3 — the WRITE half. `POST .../review-memo` had NO production caller anywhere in
// the repo: the read half (Download / Email) was built against it as though it were already being
// called, so both actions always hit the 404 "generate first" banner. The feature could not succeed
// for anyone. These tests are the forcing function for that call existing.
// ---------------------------------------------------------------------------

describe('R8 §GAPS-5 Phase 3: the Review Summary write call', () => {
  /** Opens the toolbar's Review Summary document dropdown and returns its menu. */
  async function openMemoMenu(): Promise<void> {
    const trigger = await screen.findByTestId('compose-format-memo-menu', undefined, { timeout: 5000 });
    act(() => {
      fireEvent.click(trigger);
    });
  }

  function seedReview(): void {
    composeOutputsBySession[DOC_SESSION] = [
      {
        key: REVIEW_LEDGER_REF,
        bindingId: REVIEW_BINDING,
        turn: 1,
        disposition: 'compose',
        payload: {
          overallRisk: 'High',
          flaggedSections: [
            {
              quotedText: 'Sample document body.',
              flaggedClause: 'The body imposes an unqualified obligation.',
              assessment: 'This deviates from the firm standard, which requires a materiality qualifier.',
              sectionRef: '1.1',
              riskLevel: 'High',
              standardRef: 'B5 - Obligations',
            },
          ],
        },
      },
    ];
  }

  it('Generate POSTs the panel findings, then reports success from the READ-BACK count', async () => {
    seedReview();
    const bus = new PaneEventBus();
    renderWorkspace(bus);
    await screen.findByRole('textbox');
    // Wait for the ledger restore to populate the findings the payload is built from.
    await waitFor(() => {
      expect(document.querySelectorAll('span[data-comment-id]').length).toBe(1);
    });

    await openMemoMenu();
    const create = await screen.findByTestId('compose-format-memo-create', undefined, { timeout: 5000 });
    act(() => {
      fireEvent.click(create);
    });

    // (1) The POST actually happened — the whole point of Phase 3.
    await waitFor(() => expect(reviewMemoPosts).toHaveLength(1));

    // (2) It carried the findings, with the FR-05 discrete fields Phase 1 stopped dropping.
    const body = reviewMemoPosts[0];
    expect(body.overallRisk).toBe('High');
    expect(body.sections).toHaveLength(1);
    expect(body.sections[0]).toMatchObject({
      sectionRef: '1.1',
      quotedText: 'Sample document body.',
      flaggedClause: 'The body imposes an unqualified obligation.',
      assessment: 'This deviates from the firm standard, which requires a materiality qualifier.',
      standardRef: 'B5 - Obligations',
      riskLevel: 'High',
    });

    // (3) Decision D2 — afterText is never invented.
    expect(body.sections[0].afterText).toBeUndefined();

    // (4) Success is reported from the server's read-back, not assumed from the POST returning 201.
    const banner = await screen.findByTestId('compose-workspace-memo-action-message', undefined, { timeout: 5000 });
    expect(banner.textContent).toContain('Review Summary created (1 finding)');
  });

  it('refuses to generate a summary of nothing — no POST, an actionable message instead', async () => {
    // A review that produced NO findings. A summary of nothing is itself the defect (the same rule R8
    // item 8 applies to the change summary), so this must refuse rather than persist an empty artifact.
    composeOutputsBySession[DOC_SESSION] = [];
    const bus = new PaneEventBus();
    renderWorkspace(bus);
    await screen.findByRole('textbox');

    // With no findings the dropdown is gated off entirely (`hasReview` is false), so the refusal is
    // structural: there is no control to press. Assert THAT, rather than a message that cannot appear.
    expect(screen.queryByTestId('compose-format-memo-menu')).not.toBeInTheDocument();
    expect(reviewMemoPosts).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// nda-r1 task 041 — the NDA-REVIEW "Summary Page" appendix, client wiring (2026-09-07).
//
// The generator, the SaveComposeDocumentRequest field, the SaveAsync call site and a corpus seam test
// had all existed and been green since task 041. There was no HTTP body property and no client sender,
// so the appendix had never been produced in the running app — the last open instance of the
// "server-ready, client-unwired" defect class. These tests are the client half of the forcing function
// (ComposeSaveBodyMappingGuardTests is the server half).
// ---------------------------------------------------------------------------

describe('nda-r1 t041: the Summary Page appendix rides the save body', () => {
  function seedReview(): void {
    composeOutputsBySession[DOC_SESSION] = [
      {
        key: REVIEW_LEDGER_REF,
        bindingId: REVIEW_BINDING,
        turn: 1,
        disposition: 'compose',
        payload: {
          overallRisk: 'High',
          flaggedSections: [
            {
              quotedText: 'Sample document body.',
              flaggedClause: 'The body imposes an unqualified obligation.',
              assessment: 'This deviates from the firm standard, which requires a materiality qualifier.',
              sectionRef: '1.1',
              riskLevel: 'High',
              standardRef: 'B5 - Obligations',
            },
          ],
        },
      },
    ];
  }

  async function saveDocument(): Promise<void> {
    const saveWrapper = await screen.findByTestId('compose-format-save', undefined, { timeout: 5000 });
    const saveButton = saveWrapper.querySelector('button');
    if (!saveButton) throw new Error('Save split-button primary action <button> not found');
    act(() => {
      fireEvent.click(saveButton);
    });
    await screen.findByTestId('compose-workspace-save-success-banner', undefined, { timeout: 5000 });
  }

  it('sends `summaryPage` — carrying BOTH finding vintages — once the user opts in', async () => {
    seedReview();
    const bus = new PaneEventBus();
    renderWorkspace(bus);
    await screen.findByRole('textbox');
    await waitFor(() => {
      expect(document.querySelectorAll('span[data-comment-id]').length).toBe(1);
    });

    // Open the Save split-button's menu and tick the appendix toggle.
    const saveWrapper = await screen.findByTestId('compose-format-save', undefined, { timeout: 5000 });
    const menuButton = saveWrapper.querySelectorAll('button')[1];
    if (!menuButton) throw new Error('Save split-button menu trigger not found');
    act(() => {
      fireEvent.click(menuButton);
    });
    const toggle = await screen.findByTestId('compose-format-summary-page-toggle', undefined, { timeout: 5000 });
    act(() => {
      fireEvent.click(toggle);
    });

    await saveDocument();

    expect(saveBodies.length).toBeGreaterThan(0);
    const summaryPage = saveBodies[saveBodies.length - 1].summaryPage as
      | { overallRisk: string; flaggedSections: Array<Record<string, unknown>> }
      | undefined;
    expect(summaryPage).toBeDefined();
    expect(summaryPage!.overallRisk).toBe('High');
    expect(summaryPage!.flaggedSections).toHaveLength(1);
    // BOTH vintages ride along: the server's overview line prefers `assessment` and falls back to
    // `explanation` only for legacy payloads, so sending both means neither vintage loses its "why".
    expect(summaryPage!.flaggedSections[0]).toMatchObject({
      sectionRef: '1.1',
      riskLevel: 'High',
      standardRef: 'B5 - Obligations',
      flaggedClause: 'The body imposes an unqualified obligation.',
      assessment: 'This deviates from the firm standard, which requires a materiality qualifier.',
    });
  });

  it("sends NOTHING when the user has not opted in — an appendix is never added on a save's own initiative", async () => {
    seedReview();
    const bus = new PaneEventBus();
    renderWorkspace(bus);
    await screen.findByRole('textbox');
    await waitFor(() => {
      expect(document.querySelectorAll('span[data-comment-id]').length).toBe(1);
    });

    await saveDocument();

    expect(saveBodies.length).toBeGreaterThan(0);
    expect(saveBodies[saveBodies.length - 1].summaryPage).toBeUndefined();
  });

  it('offers no toggle at all when no review has run — it cannot promise an appendix with no findings', async () => {
    composeOutputsBySession[DOC_SESSION] = [];
    const bus = new PaneEventBus();
    renderWorkspace(bus);
    await screen.findByRole('textbox');

    const saveWrapper = await screen.findByTestId('compose-format-save', undefined, { timeout: 5000 });
    const menuButton = saveWrapper.querySelectorAll('button')[1];
    if (!menuButton) throw new Error('Save split-button menu trigger not found');
    act(() => {
      fireEvent.click(menuButton);
    });

    expect(screen.queryByTestId('compose-format-summary-page-toggle')).not.toBeInTheDocument();
  });
});

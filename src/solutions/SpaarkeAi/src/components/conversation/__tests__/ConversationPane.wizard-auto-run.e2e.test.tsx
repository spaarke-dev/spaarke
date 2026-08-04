/**
 * ConversationPane — task 033 (spec FR-17) wizard→review AUTO-RUN bridge e2e.
 *
 * THE FLOW UNDER TEST (the owner's Phase-2 must-have leg): the Create-Analysis wizard's finish hook
 * dispatches its SHIPPED `widget_load{widgetType:'compose'}` seed, now additionally carrying the
 * task-033 hand-off fields `{ composeSessionId, analysisId, subDomain, autoRunReview }`
 * (`ComposeWidgetSeed`). ConversationPane's workspace-channel listener must:
 *   (1) ADOPT the wizard-minted ANALYSIS-OWNED session as the pane's active chat session
 *       (`handleSelectHistorySession` mechanism + the synchronous `chatSessionIdRef` update), and
 *   (2) ARM the shipped explicit review door (task 023's `pendingExplicitAgreementReview` buffer →
 *       `useAgreementReviewGate.runExplicit`), which fires once the DEF-10 register lands the
 *       document as a session file (the register's new `sourceDocReadyToken` bump) and capability
 *       discovery resolves the review bindingId.
 *
 * SESSION-COINCIDENCE INVARIANT (why ONE session id appears everywhere): the wizard seed's
 * `composeSessionId` is BOTH the pane's adopted chat session AND (via `ComposeDirectWidget` →
 * `<ComposeWorkspace initialSessionId>` → BFF Load resume) the document session ComposeWorkspace
 * registers back — mirroring the upload-mount door's by-construction coincidence. So the register's
 * `/documents` upload, the review's `sessionIdOverride` dispatch, and the compose-outputs ledger
 * read must ALL target `WIZARD_SESSION` — asserted literally below (a routing regression shows up
 * as a different session id in the dispatch URL or an empty ledger).
 *
 * Harness mirrors `ConversationPane.agreement-review-session-routing.e2e.test.tsx` (031): real
 * `ConversationPane` over a real `PaneEventBus`, `createConsumerDispatcher` NOT mocked, network
 * mocked at the wire boundary with ONE session-keyed in-memory ledger, and a
 * `ComposeRegistrationCapture` stub standing in for the real `ComposeWorkspace` mount. Differences:
 * the pane starts COLD (`chatSessionId: null` — the wizard flow's canonical case) with a STATEFUL
 * `setChatSessionId` mock so the adoption is observable, and the flow is driven by the BUS EVENT
 * (the wizard's hand-off), never by chat text.
 *
 * @see ../ConversationPane.tsx (the task-033 workspace listener + watchdog + register bump)
 * @see ../../workspace/composeWidgetData.ts (the typed hand-off fields)
 * @see ./ConversationPane.agreement-review-session-routing.e2e.test.tsx (harness precedent, 031)
 * @see ./ConversationPane.agreement-review-explicit-door.e2e.test.tsx (the explicit door, 023)
 */
import '@testing-library/jest-dom';
import { TextEncoder as NodeTextEncoder, TextDecoder as NodeTextDecoder } from 'util';
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextEncoder === 'undefined') (global as any).TextEncoder = NodeTextEncoder;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (global as any).TextDecoder === 'undefined') (global as any).TextDecoder = NodeTextDecoder;
import React, { act } from 'react';
import { render } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type { WorkspacePaneEvent } from '@spaarke/ai-widgets';
import type { ISprkChatProps, IChatMessage } from '@spaarke/ui-components';
import {
  ComposeActionBridgeProvider,
  useComposeActiveDocumentRegistration,
  type ComposeActiveDocumentRegistration,
} from '@spaarke/compose-components/context/composeActionBridge';

// ---------------------------------------------------------------------------
// Ids — ONE wizard session (the coincidence invariant), a distinct analysis id.
// ---------------------------------------------------------------------------
const WIZARD_SESSION = '00000000-0000-0000-0000-000000w17a4d';
const ANALYSIS_ID = 'aaaaaaaa-0000-0000-0000-000000000033';
const DOCUMENT_ID = 'dddddddd-0000-0000-0000-000000000033';
const SESSION_FILE_ID = 'session-file-wizard-auto-run-1';
const CLASSIFY_BINDING = 'binding-agreement-classify-guid';
const REVIEW_BINDING = 'binding-nda-review-guid';
const BFF = 'https://test-bff.example.com';
const FILE_NAME = 'acme-employment-agreement.docx';
const SUB_DOMAIN = 'employment';

// ---------------------------------------------------------------------------
// ONE in-memory ledger, KEYED BY SESSION (mirrors the 031 forcing harness).
// ---------------------------------------------------------------------------
interface LedgerOutput {
  key: string;
  bindingId: string;
  turn: number;
  disposition: string;
  payload: Record<string, unknown>;
}
const ledger = new Map<string, LedgerOutput[]>();

const dispatchPostUrls: string[] = [];
const dispatchPostBodies: Array<Record<string, unknown>> = [];

function sessionFromUrl(url: string): string {
  return decodeURIComponent(url.match(/\/sessions\/([^/]+)\//)?.[1] ?? '');
}

function sseCompleteResponse(result: Record<string, unknown>): Response {
  const line = `data: ${JSON.stringify({ type: 'complete', done: true, result })}\n\n`;
  const chunk = new TextEncoder().encode(line);
  let sent = false;
  const body = {
    getReader() {
      return {
        read: async () => (sent ? { done: true, value: undefined } : ((sent = true), { done: false, value: chunk })),
        releaseLock() {},
      };
    },
  };
  return { ok: true, status: 200, body, json: async () => ({}), text: async () => '' } as unknown as Response;
}

const originalFetch = global.fetch;
beforeAll(() => {
  global.fetch = jest.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (url.includes('/dispatch')) {
      dispatchPostUrls.push(url);
      const session = sessionFromUrl(url);
      const parsedBody = init?.body ? (JSON.parse(String(init.body)) as Record<string, unknown>) : {};
      dispatchPostBodies.push(parsedBody);
      const bindingId = parsedBody.bindingId as string | undefined;

      if (bindingId === CLASSIFY_BINDING) {
        // The runExplicit NON-BLOCKING sanity check — deliberately DISAGREES with the explicit
        // sub-domain (nda vs employment) to prove the explicit bind is never re-routed.
        return sseCompleteResponse({
          isAgreement: true,
          composite: false,
          candidates: [{ subDomainKey: 'nda', confidence: 0.95 }],
        });
      }
      const payload = {
        overallRisk: 'medium',
        flaggedSections: [{ sectionRef: '7.1', quotedText: 'Non-compete clause', explanation: 'Overbroad duration.' }],
      };
      const entry: LedgerOutput = {
        key: `${REVIEW_BINDING}@t1`,
        bindingId: REVIEW_BINDING,
        turn: 1,
        disposition: 'compose',
        payload,
      };
      ledger.set(session, [...(ledger.get(session) ?? []), entry]);
      return sseCompleteResponse(payload);
    }
    return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
  }) as unknown as typeof fetch;
});
afterAll(() => {
  global.fetch = originalFetch;
});

// Bridge-failure switch: when true, the /documents upload (the DEF-10 register's landing POST)
// fails — the sessionFileId never mints and the auto-run watchdog must surface the failure.
let failDocumentsUpload = false;

const authenticatedFetchMock = jest.fn(async (url: string) => {
  if (url.includes('/api/ai/capabilities')) {
    return {
      ok: true,
      status: 200,
      json: async () => ({
        capabilities: [
          {
            bindingId: CLASSIFY_BINDING,
            consumerType: 'agreement-classify',
            consumerCode: 'default',
            displayLabel: 'Classifies an uploaded document into an agreement sub-domain.',
            surfaces: ['assistant'],
            launchArgsSchemaJson: '{"type":"object"}',
          },
          {
            bindingId: REVIEW_BINDING,
            consumerType: 'nda-review',
            consumerCode: 'default',
            displayLabel: 'Review an uploaded agreement.',
            surfaces: ['assistant', 'compose'],
            launchArgsSchemaJson: '{"type":"object"}',
          },
        ],
      }),
    } as unknown as Response;
  }
  if (url.includes('/documents')) {
    if (failDocumentsUpload) {
      return { ok: false, status: 500, json: async () => ({}), text: async () => '' } as unknown as Response;
    }
    return { ok: true, status: 200, json: async () => ({ documentId: SESSION_FILE_ID }) } as unknown as Response;
  }
  if (url.includes('/compose/active-document')) {
    return { ok: true, status: 200, json: async () => ({}) } as unknown as Response;
  }
  if (url.includes('/compose-outputs')) {
    const session = sessionFromUrl(url);
    return { ok: true, status: 200, json: async () => ledger.get(session) ?? [] } as unknown as Response;
  }
  return { ok: false, status: 404, json: async () => ({}), text: async () => '' } as unknown as Response;
});

const captured: {
  onDecorateOutboundBody?: (body: Record<string, unknown>) => Promise<Record<string, unknown> | null>;
} = {};
const injectedMessages: IChatMessage[] = [];

// STATEFUL session mock — the pane starts COLD (null, the wizard flow's canonical case); the
// task-033 listener's adoption goes through setChatSessionId, which must be observable AND must
// feed back into `useAiSession()` so the pane's render-tracked `chatSessionIdRef` follows.
let mockChatSessionId: string | null = null;
const setChatSessionIdMock = jest.fn((id: string) => {
  mockChatSessionId = id;
});

jest.mock('@spaarke/ai-widgets', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const actual = jest.requireActual('@spaarke/ai-widgets') as any;
  return {
    ...actual,
    useAiSession: () => ({
      isAuthenticated: true,
      authenticatedFetch: authenticatedFetchMock,
      getAccessToken: jest.fn(async () => 'token'),
      bffBaseUrl: BFF,
      tenantId: 'test-tenant',
      chatSessionId: mockChatSessionId,
      setChatSessionId: setChatSessionIdMock,
      clearChatSession: jest.fn(),
      playbookId: undefined,
      setPlaybookId: jest.fn(),
      entityContext: null,
      contextMapping: null,
      isLoadingContextMapping: false,
      streaming: { onPaneEvent: null },
      streamingState: { isStreaming: false, tokenCount: 0 },
      turnCount: 0,
      isLoading: false,
    }),
  };
});

jest.mock('../../shell/ThreePaneShell', () => ({
  useShellStage: () => ({
    stage: 'active-chat' as const,
    toLoading: jest.fn(),
    toActiveChat: jest.fn(),
    toReview: jest.fn(),
    reset: jest.fn(),
  }),
  useRestoreContext: () => null,
  usePaneCollapseContext: () => null,
}));

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  const ReactActual = jest.requireActual('react') as typeof import('react');
  return {
    ...actual,
    createXrmDataService: () => ({
      createRecord: jest.fn(),
      retrieveRecord: jest.fn(),
      retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
      updateRecord: jest.fn(),
      deleteRecord: jest.fn(),
    }),
    SprkChat: (props: ISprkChatProps) => {
      const p = props as ISprkChatProps & {
        onDecorateOutboundBody?: (body: Record<string, unknown>) => Promise<Record<string, unknown> | null>;
      };
      captured.onDecorateOutboundBody = p.onDecorateOutboundBody;
      const lastInjectedRef = ReactActual.useRef<IChatMessage | null>(null);
      ReactActual.useEffect(() => {
        const message = props.injectLocalMessage ?? null;
        if (!message) {
          lastInjectedRef.current = null;
          return;
        }
        if (lastInjectedRef.current === message) return;
        lastInjectedRef.current = message;
        injectedMessages.push(message);
        props.onLocalMessageInjected?.();
      });
      return <div data-testid="sprkchat-stub">{props.transcriptFooterSlot}</div>;
    },
  };
});

// Import AFTER mocks.
import { ConversationPane, WIZARD_AUTO_RUN_BRIDGE_FAILURE_MESSAGE, WIZARD_AUTO_RUN_WATCHDOG_MS } from '../ConversationPane';

const registerActiveDocumentRef: { current: ComposeActiveDocumentRegistration | null } = { current: null };

/** Stub standing in for the REAL ComposeWorkspace mount (the SAME bridge conduit — see 031's harness). */
function ComposeRegistrationCapture(): null {
  registerActiveDocumentRef.current = useComposeActiveDocumentRegistration();
  return null;
}

function renderPane(): PaneEventBus {
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>
        <ComposeActionBridgeProvider>
          <ConversationPane />
          <ComposeRegistrationCapture />
        </ComposeActionBridgeProvider>
      </PaneEventBusProvider>
    </FluentProvider>
  );
  return bus;
}

/** The wizard finish hook's hand-off seed (CreateAnalysisWizardWidget onFinish, task 033 shape). */
function wizardSeedEvent(overrides: Record<string, unknown> = {}): WorkspacePaneEvent {
  return {
    type: 'widget_load',
    widgetType: 'compose',
    widgetData: {
      compose: {
        speDriveItemId: 'spe-item-wizard-1',
        speDriveId: 'spe-drive-wizard-1',
        sprkDocumentId: DOCUMENT_ID,
        fileName: FILE_NAME,
        activeWorkType: 'agreement-analysis',
        subDomain: SUB_DOMAIN,
        composeSessionId: WIZARD_SESSION,
        analysisId: ANALYSIS_ID,
        autoRunReview: true,
        ...overrides,
      },
    },
    displayName: FILE_NAME,
  } as unknown as WorkspacePaneEvent;
}

async function publishSeed(bus: PaneEventBus, overrides: Record<string, unknown> = {}): Promise<void> {
  await act(async () => {
    bus.dispatch('workspace', wizardSeedEvent(overrides));
    for (let i = 0; i < 20; i++) await Promise.resolve();
  });
}

/** Simulate ComposeWorkspace's DEF-10 register landing (the resumed wizard session IS the doc session). */
async function driveComposeRegister(): Promise<void> {
  await act(async () => {
    await registerActiveDocumentRef.current!({
      docxBytes: new ArrayBuffer(8),
      fileName: FILE_NAME,
      documentSessionId: WIZARD_SESSION,
    });
    for (let i = 0; i < 60; i++) await Promise.resolve();
  });
}

function reviewDispatches(): number[] {
  return dispatchPostBodies
    .map((b, i) => (b.bindingId === REVIEW_BINDING ? i : -1))
    .filter((i) => i >= 0);
}

beforeEach(() => {
  dispatchPostUrls.length = 0;
  dispatchPostBodies.length = 0;
  injectedMessages.length = 0;
  ledger.clear();
  authenticatedFetchMock.mockClear();
  (global.fetch as jest.Mock).mockClear();
  setChatSessionIdMock.mockClear();
  mockChatSessionId = null;
  failDocumentsUpload = false;
  captured.onDecorateOutboundBody = undefined;
  registerActiveDocumentRef.current = null;
});

describe('task 033 FR-17: wizard hand-off → adopt Analysis-owned session + auto-run the review (ONE session everywhere)', () => {
  it('adopts the wizard session, and once the DEF-10 register lands the file, auto-dispatches the review on the picked pack — dispatch, upload, and ledger ALL on the wizard session', async () => {
    const bus = renderPane();
    // UAT round-6 (item #15a): capture the workspace channel to prove the wizard/auto-run entry path
    // also stamps the cross-navigation resume flag at the ONE `runBindingDispatch` chokepoint.
    const workspaceEvents: WorkspacePaneEvent[] = [];
    bus.subscribe('workspace', (e) => workspaceEvents.push(e as WorkspacePaneEvent));

    await publishSeed(bus);

    // (1) ADOPTION: the pane adopted the wizard-minted Analysis-owned session.
    expect(setChatSessionIdMock).toHaveBeenCalledWith(WIZARD_SESSION);
    // No auto-dispatch yet — the file has not landed (race-safe buffer, never inline).
    expect(reviewDispatches()).toHaveLength(0);

    await driveComposeRegister();

    // (2) BRIDGE: the register's upload landed the file in the ADOPTED (wizard) session — the
    // session-coincidence invariant's first leg (no re-upload by any other actor, ONE upload).
    const uploadCalls = authenticatedFetchMock.mock.calls.filter(([u]: [string]) =>
      String(u).includes(`/api/ai/chat/sessions/${WIZARD_SESSION}/documents`)
    );
    expect(uploadCalls).toHaveLength(1);

    // (3) AUTO-DISPATCH: exactly ONE review dispatch, deterministically bound to the WIZARD-PICKED
    // sub-domain (the classifier sanity check disagreed at 0.95 toward 'nda' — never re-routes),
    // carrying the registered file, targeting the WIZARD session (sessionIdOverride → URL).
    const reviews = reviewDispatches();
    expect(reviews).toHaveLength(1);
    const reviewUrl = dispatchPostUrls[reviews[0]];
    expect(reviewUrl).toContain(`/sessions/${WIZARD_SESSION}/dispatch`);
    const reviewArgs = dispatchPostBodies[reviews[0]].args as Record<string, unknown>;
    expect(reviewArgs.subDomain).toBe(SUB_DOMAIN);
    expect(reviewArgs.fileIds).toEqual([SESSION_FILE_ID]);

    // (4) DURABLE READ LEG: the compose-disposition output landed in the WIZARD session's ledger —
    // the SAME session ComposeWorkspace reads compose-outputs from (030/031/032's durable path).
    expect(ledger.get(WIZARD_SESSION)).toHaveLength(1);
    expect(ledger.get(WIZARD_SESSION)![0].disposition).toBe('compose');

    // (5) NO bridge-failure surface on the happy path.
    expect(
      injectedMessages.some((m) => String(m.content).includes("couldn't start the agreement review automatically"))
    ).toBe(false);

    // (6) UAT round-6 (item #15a): the review dispatch stamped the dispatch-time resume flag.
    expect(
      workspaceEvents.some(
        (e) =>
          e.type === 'nda_review_dispatch_active' &&
          (e as WorkspacePaneEvent & { dispatchActive?: boolean }).dispatchActive === true
      )
    ).toBe(true);
  });

  // task 070 (UAT2 review-depth selector): the wizard's "Analysis Details" step now carries an
  // additive `reviewDepth` field on the SAME hand-off seed. Auto-run dispatches IMMEDIATELY at
  // that depth — no post-open depth-choice ask (would defeat FR-17's "no manual re-upload, review
  // auto-runs"). These two tests prove BOTH the explicit-value and default-absent cases.
  it('task 070: reviewDepth:"quick" on the hand-off seed threads to the review dispatch — no ask, no chips', async () => {
    const bus = renderPane();
    await publishSeed(bus, { reviewDepth: 'quick' });
    await driveComposeRegister();

    const reviews = reviewDispatches();
    expect(reviews).toHaveLength(1);
    const reviewArgs = dispatchPostBodies[reviews[0]].args as Record<string, unknown>;
    expect(reviewArgs.subDomain).toBe(SUB_DOMAIN);
    expect(reviewArgs.reviewDepth).toBe('quick');
  });

  it('task 070: an absent reviewDepth on the hand-off seed normalizes to Thorough (the legal-quality default)', async () => {
    const bus = renderPane();
    // No reviewDepth override — mirrors every seed authored before task 070.
    await publishSeed(bus);
    await driveComposeRegister();

    const reviews = reviewDispatches();
    expect(reviews).toHaveLength(1);
    const reviewArgs = dispatchPostBodies[reviews[0]].args as Record<string, unknown>;
    expect(reviewArgs.reviewDepth).toBe('thorough');
  });

  it('a DUPLICATE hand-off for the SAME analysis never re-adopts or re-arms (once per Analysis)', async () => {
    const bus = renderPane();

    await publishSeed(bus);
    await driveComposeRegister();
    expect(reviewDispatches()).toHaveLength(1);

    // The same wizard seed again (re-dispatched event / tab re-open replay).
    await publishSeed(bus);
    await act(async () => {
      for (let i = 0; i < 30; i++) await Promise.resolve();
    });

    expect(reviewDispatches()).toHaveLength(1); // still exactly one
  });

  it('NEGATIVE — a normal compose open (no autoRunReview) adopts NOTHING extra and never dispatches a review', async () => {
    const bus = renderPane();

    // A plain stored-document open (every pre-033 seed shape): no hand-off fields at all.
    await publishSeed(bus, {
      composeSessionId: undefined,
      analysisId: undefined,
      autoRunReview: undefined,
      subDomain: undefined,
    });
    // ComposeWorkspace still registers its document (the shipped DEF-10 behavior) — on a cold pane
    // the register lazily creates its OWN session; here the mock has no /sessions create route, so
    // the register no-ops. The point: NO adoption, NO arming, NO dispatch.
    expect(setChatSessionIdMock).not.toHaveBeenCalled();
    expect(reviewDispatches()).toHaveLength(0);
    expect(injectedMessages).toHaveLength(0);
  });

  it('NEGATIVE — hand-off WITHOUT autoRunReview (no sub-domain picked): adopts the session (durable bind) but never dispatches a review', async () => {
    const bus = renderPane();

    await publishSeed(bus, { autoRunReview: undefined, subDomain: undefined });
    expect(setChatSessionIdMock).toHaveBeenCalledWith(WIZARD_SESSION);

    await driveComposeRegister();

    // File landed in the adopted session (chat-visible to the Assistant)…
    const uploadCalls = authenticatedFetchMock.mock.calls.filter(([u]: [string]) =>
      String(u).includes(`/api/ai/chat/sessions/${WIZARD_SESSION}/documents`)
    );
    expect(uploadCalls).toHaveLength(1);
    // …but NO auto-run (a deterministic explicit run needs a picked subDomainKey).
    expect(reviewDispatches()).toHaveLength(0);
  });
});

describe('task 033 FR-17: bridge-failure watchdog (ADR-019 distinct surfacing)', () => {
  beforeEach(() => {
    jest.useFakeTimers();
  });
  afterEach(() => {
    jest.useRealTimers();
  });

  it('when the register upload fails (no sessionFileId ever lands), the watchdog surfaces the DISTINCT bridge-failure message and no review ever dispatches', async () => {
    failDocumentsUpload = true;
    const bus = renderPane();

    await publishSeed(bus);
    await driveComposeRegister(); // upload 500s — no sessionFileId, no waiter notify, buffer stays full

    expect(reviewDispatches()).toHaveLength(0);

    await act(async () => {
      jest.advanceTimersByTime(WIZARD_AUTO_RUN_WATCHDOG_MS + 1);
      for (let i = 0; i < 20; i++) await Promise.resolve();
    });

    // The DISTINCT bridge-failure surface (not the wizard's bind-failure warning, not a dispatch
    // error): the buffered intent was cleared and the user got the recovery instruction.
    expect(
      injectedMessages.some((m) => String(m.content) === WIZARD_AUTO_RUN_BRIDGE_FAILURE_MESSAGE)
    ).toBe(true);
    expect(reviewDispatches()).toHaveLength(0);

    // And a LATE register (e.g. a retry that would now succeed) must not fire a stale auto-run:
    failDocumentsUpload = false;
    await driveComposeRegister();
    expect(reviewDispatches()).toHaveLength(0);
  });

  it('happy path never false-alarms: once the review dispatches, the watchdog stands down silently', async () => {
    const bus = renderPane();

    await publishSeed(bus);
    await driveComposeRegister();
    expect(reviewDispatches()).toHaveLength(1);

    await act(async () => {
      jest.advanceTimersByTime(WIZARD_AUTO_RUN_WATCHDOG_MS + 1);
      for (let i = 0; i < 10; i++) await Promise.resolve();
    });

    expect(
      injectedMessages.some((m) => String(m.content) === WIZARD_AUTO_RUN_BRIDGE_FAILURE_MESSAGE)
    ).toBe(false);
  });
});

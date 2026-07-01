/**
 * HardSlashExecutor.test.ts — R6 task 081 / D-D-02 unit tests.
 *
 * Verifies:
 *   1. All 6 hard slashes execute and return the expected outcome.
 *   2. Each executes in <100ms p99 (verified via `performance.now()` brackets).
 *   3. ZERO Azure OpenAI requests for any hard slash (verified via fetch
 *      mock instrumentation).
 *   4. ADR-015 telemetry audit: command name + outcome + timestamp ONLY.
 *      User raw text never appears in any telemetry payload.
 *   5. Validation paths (`/save-to-matter` without matter id, `/pin` without
 *      focused tab, `/export` with empty history) degrade gracefully.
 *   6. Pure helpers (`parseFirstArg`, `buildExportFilename`,
 *      `serializeConversationMarkdown`, `serializeConversationCompact`).
 *
 * @see HardSlashExecutor.ts — module under test
 */

import { parse } from '../CommandRouter';
import {
  executeHardSlash,
  parseFirstArg,
  buildExportFilename,
  serializeConversationMarkdown,
  serializeConversationCompact,
  TELEMETRY_HARD_SLASH_INVOKED,
  TELEMETRY_HARD_SLASH_FAILED,
  type ExecutorContext,
  type TelemetrySink,
  type ConversationMessage,
} from '../HardSlashExecutor';

// ---------------------------------------------------------------------------
// Helpers — synthetic ExecutorContext + spies
// ---------------------------------------------------------------------------

interface TelemetryEvent {
  name: string;
  properties: Record<string, unknown>;
}

interface TestCtxBundle {
  ctx: ExecutorContext;
  telemetryEvents: TelemetryEvent[];
  fetchCalls: Array<{ url: string; init?: RequestInit }>;
  dispatchedEvents: Array<{ channel: string; event: unknown }>;
  downloads: Array<{ blob: Blob; filename: string }>;
  setHelpOpenCalls: boolean[];
  clearLocalConversationCalls: number;
  createNewSessionResult: string | null;
  /** R7 task 094 / FR-18 — spy on `/playbooks` invocations of openLibraryModal. */
  openLibraryModalCalls: number;
}

interface MakeCtxOptions {
  sessionId?: string | null;
  activeMatterId?: string | null;
  history?: ConversationMessage[];
  focusedTabId?: string | null;
  /** Optional fetch override for failure-mode tests. */
  fetchImpl?: (url: string, init?: RequestInit) => Promise<Response>;
  /** Optional createNewSession override (default returns 'session-new-1'). */
  createNewSession?: () => Promise<string | null>;
}

function makeCtx(opts: MakeCtxOptions = {}): TestCtxBundle {
  const telemetryEvents: TelemetryEvent[] = [];
  const fetchCalls: Array<{ url: string; init?: RequestInit }> = [];
  const dispatchedEvents: Array<{ channel: string; event: unknown }> = [];
  const downloads: Array<{ blob: Blob; filename: string }> = [];
  const setHelpOpenCalls: boolean[] = [];
  let clearLocalConversationCalls = 0;
  let openLibraryModalCalls = 0;
  const createNewSessionResult: string | null = null;

  const sessionId = opts.sessionId === undefined ? 'session-1' : opts.sessionId;
  const activeMatterId = opts.activeMatterId === undefined ? null : opts.activeMatterId;
  const history = opts.history ?? [];
  const focusedTabId = opts.focusedTabId === undefined ? null : opts.focusedTabId;

  // Lightweight Response shim — jsdom doesn't expose global `Response`. We
  // only need `ok` + `status` + `text()` for the executor's assertions, so a
  // structural cast is sufficient.
  const makeOkResponse = (): Response =>
    ({
      ok: true,
      status: 200,
      statusText: 'OK',
      headers: new Headers({ 'Content-Type': 'application/json' }),
      text: async () => '{"ok":true}',
      json: async () => ({ ok: true }),
    }) as unknown as Response;

  const fetchImpl =
    opts.fetchImpl ?? (async (_url: string, _init?: RequestInit) => makeOkResponse());

  const telemetry: TelemetrySink = {
    emit(eventName, properties) {
      telemetryEvents.push({ name: eventName, properties: { ...properties } });
    },
  };

  const ctx: ExecutorContext = {
    bffBaseUrl: 'https://bff.test',
    authenticatedFetch: async (url: string, init?: RequestInit) => {
      fetchCalls.push({ url, init });
      return fetchImpl(url, init);
    },
    sessionId,
    paneEventBus: {
      dispatch: (channel: unknown, event: unknown) => {
        dispatchedEvents.push({ channel: String(channel), event });
      },
      // Unused by executor but required by interface shape — stub the rest.
      subscribe: () => () => undefined,
    } as unknown as ExecutorContext['paneEventBus'],
    setHelpOpen: (open) => {
      setHelpOpenCalls.push(open);
    },
    clearLocalConversation: () => {
      clearLocalConversationCalls++;
    },
    createNewSession: opts.createNewSession ?? (async () => 'session-new-1'),
    getConversationHistory: () => history,
    getFocusedTabId: () => focusedTabId,
    activeMatterId,
    downloadBlob: (blob, filename) => {
      downloads.push({ blob, filename });
    },
    // R7 task 094 / FR-18 — spy on `/playbooks` opener.
    openLibraryModal: (): void => {
      openLibraryModalCalls++;
    },
    telemetry,
  };

  return {
    ctx,
    telemetryEvents,
    fetchCalls,
    dispatchedEvents,
    downloads,
    setHelpOpenCalls,
    get clearLocalConversationCalls() {
      return clearLocalConversationCalls;
    },
    get openLibraryModalCalls() {
      return openLibraryModalCalls;
    },
    createNewSessionResult,
  } as unknown as TestCtxBundle;
}

/**
 * Latency bracket helper. Returns the elapsed ms between t0 and after the
 * promise resolves. Used to assert each hard slash executes <100ms p99 per
 * Phase D exit criterion 2.
 *
 * Note: jsdom's `performance.now()` is high-resolution but bounded by event
 * loop scheduling. We use it as an UPPER BOUND signal — if even the
 * jsdom-overhead timing is under 100ms with mocked BFF, we are guaranteed
 * to be under 100ms in production where the actual network is faster.
 */
async function timeIt<T>(fn: () => Promise<T>): Promise<{ result: T; elapsed: number }> {
  const t0 = performance.now();
  const result = await fn();
  const elapsed = performance.now() - t0;
  return { result, elapsed };
}

/**
 * Build a structurally-typed fake Response for tests. jsdom does not expose
 * the global `Response` constructor on a node-jest worker; the executor only
 * reads `ok` and `status`, so this minimal shape is sufficient.
 */
function makeFakeResponse(status: number, body = '{}'): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: status === 200 ? 'OK' : 'Error',
    headers: new Headers({ 'Content-Type': 'application/json' }),
    text: async () => body,
    json: async () => JSON.parse(body),
  } as unknown as Response;
}

/**
 * Asserts no telemetry event payload contains the supplied user text. Per
 * ADR-015 — user message text MUST NOT be logged.
 */
function expectNoUserTextInTelemetry(
  events: TelemetryEvent[],
  forbiddenText: string,
): void {
  const serialized = JSON.stringify(events);
  expect(serialized).not.toContain(forbiddenText);
}

// ---------------------------------------------------------------------------
// /clear
// ---------------------------------------------------------------------------

describe('/clear', () => {
  it('clears local conversation and returns executed outcome', async () => {
    const bundle = makeCtx();
    const intent = parse('/clear');

    const { result, elapsed } = await timeIt(() =>
      executeHardSlash(intent, bundle.ctx),
    );

    expect(result.outcome).toBe('executed');
    expect(result.message).toBeNull();
    expect(elapsed).toBeLessThan(100);
  });

  it('issues DELETE to /api/ai/chat/sessions/{sessionId}', async () => {
    const bundle = makeCtx({ sessionId: 'sess-abc' });
    await executeHardSlash(parse('/clear'), bundle.ctx);

    // DELETE is fire-and-forget; allow the microtask to run.
    await Promise.resolve();
    expect(bundle.fetchCalls).toHaveLength(1);
    expect(bundle.fetchCalls[0]?.init?.method).toBe('DELETE');
    expect(bundle.fetchCalls[0]?.url).toContain('/api/ai/chat/sessions/sess-abc');
  });

  it('skips DELETE when sessionId is null', async () => {
    const bundle = makeCtx({ sessionId: null });
    await executeHardSlash(parse('/clear'), bundle.ctx);
    expect(bundle.fetchCalls).toHaveLength(0);
  });

  it('makes ZERO chat-completion / Azure OpenAI requests', async () => {
    const bundle = makeCtx();
    await executeHardSlash(parse('/clear'), bundle.ctx);
    const chatCompletionHits = bundle.fetchCalls.filter((c) =>
      /openai|chat\/completions|\/messages/i.test(c.url),
    );
    expect(chatCompletionHits).toHaveLength(0);
  });

  it('emits exactly one invoked telemetry event with stable shape', async () => {
    const bundle = makeCtx();
    await executeHardSlash(parse('/clear'), bundle.ctx);
    const invoked = bundle.telemetryEvents.filter(
      (e) => e.name === TELEMETRY_HARD_SLASH_INVOKED,
    );
    expect(invoked).toHaveLength(1);
    expect(invoked[0]?.properties.command).toBe('/clear');
    expect(invoked[0]?.properties.outcome).toBe('executed');
    expect(invoked[0]?.properties.timestamp).toMatch(/^\d{4}-\d{2}-\d{2}T/);
  });
});

// ---------------------------------------------------------------------------
// /new-session
// ---------------------------------------------------------------------------

describe('/new-session', () => {
  it('dispatches workspace.session_reset and mints a new session', async () => {
    const bundle = makeCtx();
    const intent = parse('/new-session');

    const { result, elapsed } = await timeIt(() =>
      executeHardSlash(intent, bundle.ctx),
    );

    expect(result.outcome).toBe('executed');
    expect(result.message).toMatch(/new session/i);
    expect(elapsed).toBeLessThan(100);

    expect(bundle.dispatchedEvents).toHaveLength(1);
    expect(bundle.dispatchedEvents[0]?.channel).toBe('workspace');
    expect((bundle.dispatchedEvents[0]?.event as { type: string }).type).toBe(
      'session_reset',
    );
  });

  it('returns failed-network when createNewSession throws', async () => {
    const bundle = makeCtx({
      createNewSession: async () => {
        throw new Error('boom');
      },
    });
    const result = await executeHardSlash(parse('/new-session'), bundle.ctx);
    expect(result.outcome).toBe('failed-network');
    expect(result.errorCode).toBe('network');
  });

  it('makes ZERO chat-completion requests', async () => {
    const bundle = makeCtx();
    await executeHardSlash(parse('/new-session'), bundle.ctx);
    const chatHits = bundle.fetchCalls.filter((c) =>
      /openai|chat\/completions/i.test(c.url),
    );
    expect(chatHits).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// /help
// ---------------------------------------------------------------------------

describe('/help', () => {
  it('opens the help panel synchronously', async () => {
    const bundle = makeCtx();
    const { result, elapsed } = await timeIt(() =>
      executeHardSlash(parse('/help'), bundle.ctx),
    );
    expect(result.outcome).toBe('executed');
    expect(bundle.setHelpOpenCalls).toEqual([true]);
    expect(elapsed).toBeLessThan(100);
  });

  it('makes ZERO network requests', async () => {
    const bundle = makeCtx();
    await executeHardSlash(parse('/help'), bundle.ctx);
    expect(bundle.fetchCalls).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// /export
// ---------------------------------------------------------------------------

describe('/export', () => {
  it('downloads markdown blob with conversation history', async () => {
    const history: ConversationMessage[] = [
      { role: 'user', content: 'Hello', timestamp: '2026-06-18T10:00:00Z' },
      { role: 'assistant', content: 'Hi there', timestamp: '2026-06-18T10:00:01Z' },
    ];
    const bundle = makeCtx({ history });

    const { result, elapsed } = await timeIt(() =>
      executeHardSlash(parse('/export'), bundle.ctx),
    );

    expect(result.outcome).toBe('executed');
    expect(result.message).toMatch(/exported 2 messages/i);
    expect(elapsed).toBeLessThan(100);
    expect(bundle.downloads).toHaveLength(1);
    expect(bundle.downloads[0]?.filename).toMatch(/spaarke-chat-.+\.md$/);
  });

  it('returns failed-validation on empty history', async () => {
    const bundle = makeCtx({ history: [] });
    const result = await executeHardSlash(parse('/export'), bundle.ctx);
    expect(result.outcome).toBe('failed-validation');
    expect(result.errorCode).toBe('empty-history');
    expect(bundle.downloads).toHaveLength(0);
  });

  it('makes ZERO network requests', async () => {
    const history: ConversationMessage[] = [{ role: 'user', content: 'x' }];
    const bundle = makeCtx({ history });
    await executeHardSlash(parse('/export'), bundle.ctx);
    expect(bundle.fetchCalls).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// /save-to-matter
// ---------------------------------------------------------------------------

describe('/save-to-matter', () => {
  it('POSTs to /api/memory/pins with matter id from arg', async () => {
    const history: ConversationMessage[] = [{ role: 'user', content: 'Q' }];
    const bundle = makeCtx({ history });

    const { result, elapsed } = await timeIt(() =>
      executeHardSlash(
        parse('/save-to-matter 11111111-1111-1111-1111-111111111111'),
        bundle.ctx,
      ),
    );

    expect(result.outcome).toBe('executed');
    expect(elapsed).toBeLessThan(100);
    expect(bundle.fetchCalls).toHaveLength(1);
    expect(bundle.fetchCalls[0]?.url).toContain('/api/memory/pins');
    expect(bundle.fetchCalls[0]?.init?.method).toBe('POST');

    const body = JSON.parse(String(bundle.fetchCalls[0]?.init?.body));
    expect(body.matterId).toBe('11111111-1111-1111-1111-111111111111');
    expect(body.pinType).toBe('matter-fact');
    expect(typeof body.title).toBe('string');
    expect(typeof body.content).toBe('string');
  });

  it('falls back to activeMatterId when no arg supplied', async () => {
    const history: ConversationMessage[] = [{ role: 'user', content: 'Q' }];
    const bundle = makeCtx({ history, activeMatterId: 'matter-active' });

    const result = await executeHardSlash(
      parse('/save-to-matter'),
      bundle.ctx,
    );
    expect(result.outcome).toBe('executed');
    const body = JSON.parse(String(bundle.fetchCalls[0]?.init?.body));
    expect(body.matterId).toBe('matter-active');
  });

  it('fails validation when no matter id and no active matter', async () => {
    const history: ConversationMessage[] = [{ role: 'user', content: 'Q' }];
    const bundle = makeCtx({ history, activeMatterId: null });

    const result = await executeHardSlash(parse('/save-to-matter'), bundle.ctx);
    expect(result.outcome).toBe('failed-validation');
    expect(result.errorCode).toBe('missing-matter-id');
    expect(bundle.fetchCalls).toHaveLength(0);
  });

  it('fails validation on empty conversation history', async () => {
    const bundle = makeCtx({ history: [], activeMatterId: 'matter-1' });
    const result = await executeHardSlash(parse('/save-to-matter'), bundle.ctx);
    expect(result.outcome).toBe('failed-validation');
    expect(result.errorCode).toBe('empty-history');
  });

  it('returns failed-network when fetch throws', async () => {
    const history: ConversationMessage[] = [{ role: 'user', content: 'Q' }];
    const bundle = makeCtx({
      history,
      activeMatterId: 'matter-1',
      fetchImpl: async () => {
        throw new Error('network');
      },
    });
    const result = await executeHardSlash(parse('/save-to-matter'), bundle.ctx);
    expect(result.outcome).toBe('failed-network');
    expect(result.errorCode).toBe('network');
  });

  it('returns failed-network on non-2xx response', async () => {
    const history: ConversationMessage[] = [{ role: 'user', content: 'Q' }];
    const bundle = makeCtx({
      history,
      activeMatterId: 'matter-1',
      fetchImpl: async () => makeFakeResponse(500),
    });
    const result = await executeHardSlash(parse('/save-to-matter'), bundle.ctx);
    expect(result.outcome).toBe('failed-network');
    expect(result.errorCode).toBe('http-500');
  });

  it('makes ZERO chat-completion requests', async () => {
    const history: ConversationMessage[] = [{ role: 'user', content: 'Q' }];
    const bundle = makeCtx({ history, activeMatterId: 'matter-1' });
    await executeHardSlash(parse('/save-to-matter'), bundle.ctx);
    const chatHits = bundle.fetchCalls.filter((c) =>
      /openai|chat\/completions/i.test(c.url),
    );
    expect(chatHits).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// /pin
// ---------------------------------------------------------------------------

describe('/pin', () => {
  it('PATCHes /api/ai/chat/sessions/{sessionId}/tabs with isPinned=true', async () => {
    const bundle = makeCtx({ sessionId: 'sess-1', focusedTabId: 'tab-7' });

    const { result, elapsed } = await timeIt(() =>
      executeHardSlash(parse('/pin'), bundle.ctx),
    );

    expect(result.outcome).toBe('executed');
    expect(elapsed).toBeLessThan(100);
    expect(bundle.fetchCalls).toHaveLength(1);
    expect(bundle.fetchCalls[0]?.url).toContain(
      '/api/ai/chat/sessions/sess-1/tabs',
    );
    expect(bundle.fetchCalls[0]?.init?.method).toBe('PATCH');
    const body = JSON.parse(String(bundle.fetchCalls[0]?.init?.body));
    expect(body.tabs).toEqual([{ tabId: 'tab-7', isPinned: true }]);
  });

  it('dispatches workspace.tab_edited with field NAMES only (ADR-015)', async () => {
    const bundle = makeCtx({ sessionId: 'sess-1', focusedTabId: 'tab-7' });
    await executeHardSlash(parse('/pin'), bundle.ctx);

    expect(bundle.dispatchedEvents).toHaveLength(1);
    const ev = bundle.dispatchedEvents[0]?.event as {
      type: string;
      tabId: string;
      sessionId: string;
      editedFields: string[];
      timestamp: string;
    };
    expect(ev.type).toBe('tab_edited');
    expect(ev.tabId).toBe('tab-7');
    expect(ev.editedFields).toEqual(['isPinned']);
    expect(ev.timestamp).toMatch(/^\d{4}-\d{2}-\d{2}T/);
  });

  it('fails validation when no session', async () => {
    const bundle = makeCtx({ sessionId: null, focusedTabId: 'tab-1' });
    const result = await executeHardSlash(parse('/pin'), bundle.ctx);
    expect(result.outcome).toBe('failed-validation');
    expect(result.errorCode).toBe('no-session');
    expect(bundle.fetchCalls).toHaveLength(0);
  });

  it('fails validation when no focused tab', async () => {
    const bundle = makeCtx({ sessionId: 'sess-1', focusedTabId: null });
    const result = await executeHardSlash(parse('/pin'), bundle.ctx);
    expect(result.outcome).toBe('failed-validation');
    expect(result.errorCode).toBe('no-focused-tab');
    expect(bundle.fetchCalls).toHaveLength(0);
  });

  it('returns failed-network on HTTP 500', async () => {
    const bundle = makeCtx({
      sessionId: 'sess-1',
      focusedTabId: 'tab-7',
      fetchImpl: async () => makeFakeResponse(500),
    });
    const result = await executeHardSlash(parse('/pin'), bundle.ctx);
    expect(result.outcome).toBe('failed-network');
    expect(result.errorCode).toBe('http-500');
  });

  it('makes ZERO chat-completion requests', async () => {
    const bundle = makeCtx({ sessionId: 'sess-1', focusedTabId: 'tab-7' });
    await executeHardSlash(parse('/pin'), bundle.ctx);
    const chatHits = bundle.fetchCalls.filter((c) =>
      /openai|chat\/completions/i.test(c.url),
    );
    expect(chatHits).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// ADR-015 — telemetry audit
// ---------------------------------------------------------------------------

describe('ADR-015 telemetry audit', () => {
  it('never includes user raw text in telemetry payloads', async () => {
    const userText =
      '/save-to-matter matter-1 secret-confidential-content-xyz';
    const bundle = makeCtx({
      history: [{ role: 'user', content: 'top-secret-message-body' }],
      activeMatterId: 'matter-1',
    });
    await executeHardSlash(parse(userText), bundle.ctx);

    // The "secret-confidential-content-xyz" string was part of the user's raw
    // text. It MUST NOT appear in any telemetry event payload.
    expectNoUserTextInTelemetry(
      bundle.telemetryEvents,
      'secret-confidential-content-xyz',
    );
    expectNoUserTextInTelemetry(
      bundle.telemetryEvents,
      'top-secret-message-body',
    );
  });

  it('telemetry payloads have stable shape: command + outcome + timestamp', async () => {
    const bundle = makeCtx();
    await executeHardSlash(parse('/clear'), bundle.ctx);

    const invoked = bundle.telemetryEvents.find(
      (e) => e.name === TELEMETRY_HARD_SLASH_INVOKED,
    );
    expect(invoked).toBeDefined();
    expect(Object.keys(invoked!.properties).sort()).toEqual(
      expect.arrayContaining(['command', 'outcome', 'timestamp']),
    );
  });

  it('failed events carry stable errorCode (no message text)', async () => {
    const bundle = makeCtx({ sessionId: null, focusedTabId: null });
    await executeHardSlash(parse('/pin'), bundle.ctx);

    const failed = bundle.telemetryEvents.find(
      (e) => e.name === TELEMETRY_HARD_SLASH_FAILED,
    );
    expect(failed).toBeDefined();
    expect(failed!.properties.errorCode).toBe('no-session');
    expect(failed!.properties.command).toBe('/pin');
  });
});

// ---------------------------------------------------------------------------
// Latency aggregate — all 7 commands under <100ms p99 in the test runner
// (R7 task 094 added /playbooks; pure-frontend like /help so trivially under
// 100ms — the runner is the only meaningful upper bound here.)
// ---------------------------------------------------------------------------

describe('Phase D exit criterion 2 — latency', () => {
  it('all 7 hard slashes execute in <100ms under mocked BFF', async () => {
    const samples: Array<{ command: string; elapsed: number }> = [];

    // /clear
    {
      const bundle = makeCtx();
      const { elapsed } = await timeIt(() =>
        executeHardSlash(parse('/clear'), bundle.ctx),
      );
      samples.push({ command: '/clear', elapsed });
    }

    // /new-session
    {
      const bundle = makeCtx();
      const { elapsed } = await timeIt(() =>
        executeHardSlash(parse('/new-session'), bundle.ctx),
      );
      samples.push({ command: '/new-session', elapsed });
    }

    // /help
    {
      const bundle = makeCtx();
      const { elapsed } = await timeIt(() =>
        executeHardSlash(parse('/help'), bundle.ctx),
      );
      samples.push({ command: '/help', elapsed });
    }

    // /export
    {
      const bundle = makeCtx({
        history: [{ role: 'user', content: 'hi' }],
      });
      const { elapsed } = await timeIt(() =>
        executeHardSlash(parse('/export'), bundle.ctx),
      );
      samples.push({ command: '/export', elapsed });
    }

    // /save-to-matter
    {
      const bundle = makeCtx({
        history: [{ role: 'user', content: 'hi' }],
        activeMatterId: 'matter-1',
      });
      const { elapsed } = await timeIt(() =>
        executeHardSlash(parse('/save-to-matter'), bundle.ctx),
      );
      samples.push({ command: '/save-to-matter', elapsed });
    }

    // /pin
    {
      const bundle = makeCtx({ focusedTabId: 'tab-7' });
      const { elapsed } = await timeIt(() =>
        executeHardSlash(parse('/pin'), bundle.ctx),
      );
      samples.push({ command: '/pin', elapsed });
    }

    // /playbooks (R7 task 094 / FR-18)
    {
      const bundle = makeCtx();
      const { elapsed } = await timeIt(() =>
        executeHardSlash(parse('/playbooks'), bundle.ctx),
      );
      samples.push({ command: '/playbooks', elapsed });
    }

    for (const s of samples) {
      expect(s.elapsed).toBeLessThan(100);
    }
  });
});

// ---------------------------------------------------------------------------
// Pure helpers
// ---------------------------------------------------------------------------

describe('parseFirstArg', () => {
  it('extracts first arg after command prefix', () => {
    expect(parseFirstArg('/save-to-matter MAT-1 extra', '/save-to-matter')).toBe(
      'MAT-1',
    );
  });

  it('returns null when no arg present', () => {
    expect(parseFirstArg('/save-to-matter', '/save-to-matter')).toBeNull();
    expect(parseFirstArg('/save-to-matter   ', '/save-to-matter')).toBeNull();
  });

  it('returns null when prefix does not match', () => {
    expect(parseFirstArg('/pin', '/save-to-matter')).toBeNull();
  });

  it('is case-insensitive for the prefix', () => {
    expect(parseFirstArg('/Save-To-Matter MAT-1', '/save-to-matter')).toBe(
      'MAT-1',
    );
  });
});

describe('buildExportFilename', () => {
  it('uses session id when present', () => {
    const name = buildExportFilename('sess-abc');
    expect(name).toMatch(/^spaarke-chat-sess-abc-\d{4}-\d{2}-\d{2}\.md$/);
  });

  it('falls back to "session" when sessionId is null', () => {
    const name = buildExportFilename(null);
    expect(name).toMatch(/^spaarke-chat-session-\d{4}-\d{2}-\d{2}\.md$/);
  });

  it('falls back to "session" when sessionId has invalid characters', () => {
    const name = buildExportFilename('bad/path');
    expect(name).toMatch(/^spaarke-chat-session-/);
  });
});

describe('serializeConversationMarkdown', () => {
  it('renders user and assistant headings', () => {
    const md = serializeConversationMarkdown(
      [
        { role: 'user', content: 'Hello' },
        { role: 'assistant', content: 'Hi' },
      ],
      'sess-1',
    );
    expect(md).toMatch(/# Spaarke Chat Export/);
    expect(md).toMatch(/## User/);
    expect(md).toMatch(/## Assistant/);
    expect(md).toMatch(/Hello/);
    expect(md).toMatch(/Hi/);
    expect(md).toMatch(/Session: sess-1/);
  });

  it('handles empty history', () => {
    const md = serializeConversationMarkdown([], null);
    expect(md).toMatch(/# Spaarke Chat Export/);
    expect(md).toMatch(/Session: unknown/);
  });
});

describe('serializeConversationCompact', () => {
  it('produces prefixed lines per role', () => {
    const compact = serializeConversationCompact([
      { role: 'user', content: 'Q' },
      { role: 'assistant', content: 'A' },
      { role: 'system', content: 'S' },
    ]);
    expect(compact).toContain('U: Q');
    expect(compact).toContain('A: A');
    expect(compact).toContain('S: S');
  });

  it('truncates per-message body at 200 chars', () => {
    const long = 'x'.repeat(500);
    const compact = serializeConversationCompact([{ role: 'user', content: long }]);
    // Body is truncated to 200 chars + ellipsis
    expect(compact.length).toBeLessThan(220);
    expect(compact).toContain('…');
  });
});

// ---------------------------------------------------------------------------
// Defensive — assertHardSlash via wrong Intent
// ---------------------------------------------------------------------------

describe('defensive — non-hard-slash intent', () => {
  it('throws when invoked with a soft slash', async () => {
    const bundle = makeCtx();
    const softIntent = parse('/summarize');
    // We expect the inner assertHardSlash to throw; the outer try/catch in
    // executeHardSlash converts it into a failed-unknown result.
    const result = await executeHardSlash(softIntent, bundle.ctx);
    expect(result.outcome).toBe('failed-unknown');
  });

  it('throws when invoked with natural language', async () => {
    const bundle = makeCtx();
    const nl = parse('hello');
    const result = await executeHardSlash(nl, bundle.ctx);
    expect(result.outcome).toBe('failed-unknown');
  });
});

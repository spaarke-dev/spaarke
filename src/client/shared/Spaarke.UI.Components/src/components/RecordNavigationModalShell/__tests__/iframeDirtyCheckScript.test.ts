/**
 * Tests for the iframe-side dirty-check JS Web Resource — smart-todo-r4 task
 * R4-041 (C). Validates the contract counterpart to
 * `RecordNavigationModalShell` (FR-14):
 *
 *   Parent → iframe: `request-dirty-check` { correlationId }
 *   Iframe → parent: `dirty-check-result` { correlationId, dirty }
 *
 * The web resource itself is `src/client/webresources/js/sprk_todo_dirty_check.js`
 * and is loaded here via CommonJS `require`. The file uses defensive `var` +
 * IIFE patterns (no ES modules, no TypeScript) so it runs unchanged in the
 * Dataverse JS Web Resource sandbox.
 *
 * @see ../RecordNavigationModalShell.tsx — parent-side counterpart
 * @see ../types.ts — DIRTY_CHECK_REQUEST_TYPE / DIRTY_CHECK_RESULT_TYPE
 * @see src/client/webresources/js/sprk_todo_dirty_check.js — script under test
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

import * as path from 'path';
import { DIRTY_CHECK_REQUEST_TYPE, DIRTY_CHECK_RESULT_TYPE } from '../index';

// ---------------------------------------------------------------------------
// Locate the script under test. The shared-lib jest runs from
// `src/client/shared/Spaarke.UI.Components/`; the web resource lives 5 levels
// up at `src/client/webresources/js/sprk_todo_dirty_check.js`.
// ---------------------------------------------------------------------------

const SCRIPT_PATH = path.resolve(
  __dirname,
  '../../../../../../webresources/js/sprk_todo_dirty_check.js'
);

// Loader helper that resets `window` namespaces + listener sentinels between
// suites so each test starts from a clean slate. The web resource installs
// itself onto `window.Spaarke.SmartTodo.DirtyCheck`; we need to wipe that
// global and re-require to exercise the OnLoad install path repeatedly.
function loadScript(): {
  onLoad: (executionContext: any) => void;
  _handleMessage: (event: MessageEvent) => void;
  _internals: {
    isOriginAllowed: (origin: string) => boolean;
    computeDirtyState: () => boolean;
    REQUEST_TYPE: string;
    RESULT_TYPE: string;
    LISTENER_INSTALLED: string;
    FORM_CONTEXT_HOLDER: string;
  };
  VERSION: string;
} {
  // Clear the cached module + namespace state so each load starts clean.
  delete require.cache[SCRIPT_PATH];
  delete (window as any).Spaarke;
  delete (window as any).__sprk_todo_dirty_check_listener__;
  delete (window as any).__sprk_todo_dirty_check_formctx__;
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  return require(SCRIPT_PATH);
}

function buildExecutionContext(getIsDirty: () => boolean): any {
  return {
    getFormContext: () => ({
      data: {
        entity: {
          getIsDirty,
        },
      },
    }),
  };
}

// ---------------------------------------------------------------------------
// Constants-shape contract — message types must match the shell's
// ---------------------------------------------------------------------------

describe('sprk_todo_dirty_check.js — contract with RecordNavigationModalShell', () => {
  it('exports the same REQUEST/RESULT type discriminators as the shell', () => {
    const mod = loadScript();
    expect(mod._internals.REQUEST_TYPE).toBe(DIRTY_CHECK_REQUEST_TYPE);
    expect(mod._internals.RESULT_TYPE).toBe(DIRTY_CHECK_RESULT_TYPE);
  });

  it('exposes a VERSION string for diagnostic logging', () => {
    const mod = loadScript();
    expect(typeof mod.VERSION).toBe('string');
    expect(mod.VERSION.length).toBeGreaterThan(0);
  });
});

// ---------------------------------------------------------------------------
// Origin allow-list — security gate
// ---------------------------------------------------------------------------

describe('isOriginAllowed — origin allow-list', () => {
  it('allows wildcard *.dynamics.com customer subdomains', () => {
    const mod = loadScript();
    expect(mod._internals.isOriginAllowed('https://contoso.crm.dynamics.com')).toBe(true);
    expect(mod._internals.isOriginAllowed('https://spaarkedev1.crm.dynamics.com')).toBe(true);
  });

  it('rejects bare dynamics.com (wildcard requires non-empty subdomain label)', () => {
    const mod = loadScript();
    expect(mod._internals.isOriginAllowed('https://dynamics.com')).toBe(false);
  });

  it('rejects untrusted origins', () => {
    const mod = loadScript();
    expect(mod._internals.isOriginAllowed('https://evil.example.com')).toBe(false);
    expect(mod._internals.isOriginAllowed('http://contoso.crm.dynamics.com')).toBe(false); // http
  });

  it('rejects empty / non-string origins', () => {
    const mod = loadScript();
    expect(mod._internals.isOriginAllowed('')).toBe(false);
    expect(mod._internals.isOriginAllowed(null as unknown as string)).toBe(false);
    expect(mod._internals.isOriginAllowed(undefined as unknown as string)).toBe(false);
  });

  it('allows same-origin (window.location.origin) at runtime', () => {
    const mod = loadScript();
    // jsdom sets window.location.origin to "http://localhost" by default.
    expect(mod._internals.isOriginAllowed(window.location.origin)).toBe(true);
  });
});

// ---------------------------------------------------------------------------
// OnLoad — listener registration + idempotency
// ---------------------------------------------------------------------------

describe('onLoad — listener installation', () => {
  it('installs the message listener exactly once across multiple OnLoad invocations', () => {
    const mod = loadScript();
    const addSpy = jest.spyOn(window, 'addEventListener');
    try {
      mod.onLoad(buildExecutionContext(() => false));
      mod.onLoad(buildExecutionContext(() => false));
      mod.onLoad(buildExecutionContext(() => false));

      const messageCalls = addSpy.mock.calls.filter(([type]) => type === 'message');
      expect(messageCalls.length).toBe(1);
      expect((window as any)[mod._internals.LISTENER_INSTALLED]).toBe(true);
    } finally {
      addSpy.mockRestore();
    }
  });

  it('caches the formContext on the window holder so the listener can read it', () => {
    const mod = loadScript();
    const ctx = buildExecutionContext(() => true);
    mod.onLoad(ctx);
    expect((window as any)[mod._internals.FORM_CONTEXT_HOLDER]).toBeTruthy();
    expect(typeof (window as any)[mod._internals.FORM_CONTEXT_HOLDER].data.entity.getIsDirty).toBe(
      'function'
    );
  });

  it('refreshes the cached formContext on subsequent OnLoad (new record in same iframe)', () => {
    const mod = loadScript();
    const ctxA = buildExecutionContext(() => false);
    const ctxB = buildExecutionContext(() => true);
    mod.onLoad(ctxA);
    const firstCtx = (window as any)[mod._internals.FORM_CONTEXT_HOLDER];
    mod.onLoad(ctxB);
    const secondCtx = (window as any)[mod._internals.FORM_CONTEXT_HOLDER];
    expect(secondCtx).not.toBe(firstCtx);
    expect(secondCtx.data.entity.getIsDirty()).toBe(true);
  });

  it('does not throw and does not install the listener when formContext is unavailable', () => {
    const mod = loadScript();
    expect(() =>
      mod.onLoad({
        getFormContext: () => null,
      } as any)
    ).not.toThrow();
    expect((window as any)[mod._internals.LISTENER_INSTALLED]).toBeFalsy();
  });
});

// ---------------------------------------------------------------------------
// _handleMessage — round-trip semantics
// ---------------------------------------------------------------------------

describe('_handleMessage — postMessage round-trip', () => {
  /**
   * Builds a MessageEvent with a mocked `source.postMessage` so we can
   * intercept the iframe's response without setting up a real cross-window
   * channel. Returns the `postMessageSpy` for assertion.
   */
  function buildRequestEvent(
    payload: any,
    origin: string = 'https://contoso.crm.dynamics.com'
  ): { event: MessageEvent; postMessageSpy: jest.Mock } {
    const postMessageSpy = jest.fn();
    const sourceWindow = { postMessage: postMessageSpy };
    const event = new MessageEvent('message', {
      data: payload,
      origin,
      source: sourceWindow as unknown as Window,
    });
    return { event, postMessageSpy };
  }

  it('responds with dirty=true when formContext getIsDirty returns true', () => {
    const mod = loadScript();
    mod.onLoad(buildExecutionContext(() => true));

    const { event, postMessageSpy } = buildRequestEvent({
      type: DIRTY_CHECK_REQUEST_TYPE,
      correlationId: 'corr-1',
    });
    mod._handleMessage(event);

    expect(postMessageSpy).toHaveBeenCalledTimes(1);
    const [response, responseOrigin] = postMessageSpy.mock.calls[0];
    expect(response).toEqual({
      type: DIRTY_CHECK_RESULT_TYPE,
      correlationId: 'corr-1',
      dirty: true,
    });
    // Echo origin — never "*" for responses per MDN security guidance.
    expect(responseOrigin).toBe('https://contoso.crm.dynamics.com');
  });

  it('responds with dirty=false when formContext getIsDirty returns false', () => {
    const mod = loadScript();
    mod.onLoad(buildExecutionContext(() => false));

    const { event, postMessageSpy } = buildRequestEvent({
      type: DIRTY_CHECK_REQUEST_TYPE,
      correlationId: 'corr-clean',
    });
    mod._handleMessage(event);

    expect(postMessageSpy).toHaveBeenCalledWith(
      { type: DIRTY_CHECK_RESULT_TYPE, correlationId: 'corr-clean', dirty: false },
      'https://contoso.crm.dynamics.com'
    );
  });

  it('echoes the parent-supplied correlationId so responses are matchable', () => {
    const mod = loadScript();
    mod.onLoad(buildExecutionContext(() => false));

    const { event: ev1, postMessageSpy: spy1 } = buildRequestEvent({
      type: DIRTY_CHECK_REQUEST_TYPE,
      correlationId: 'aaa',
    });
    const { event: ev2, postMessageSpy: spy2 } = buildRequestEvent({
      type: DIRTY_CHECK_REQUEST_TYPE,
      correlationId: 'bbb',
    });
    mod._handleMessage(ev1);
    mod._handleMessage(ev2);

    expect(spy1.mock.calls[0][0].correlationId).toBe('aaa');
    expect(spy2.mock.calls[0][0].correlationId).toBe('bbb');
  });

  it('drops messages from untrusted origins silently (no response posted)', () => {
    const mod = loadScript();
    mod.onLoad(buildExecutionContext(() => true));

    const { event, postMessageSpy } = buildRequestEvent(
      { type: DIRTY_CHECK_REQUEST_TYPE, correlationId: 'evil-corr' },
      'https://evil.example.com'
    );
    mod._handleMessage(event);

    expect(postMessageSpy).not.toHaveBeenCalled();
  });

  it('drops messages with mismatched type', () => {
    const mod = loadScript();
    mod.onLoad(buildExecutionContext(() => true));

    const { event, postMessageSpy } = buildRequestEvent({
      type: 'some-other-message',
      correlationId: 'corr-x',
    });
    mod._handleMessage(event);
    expect(postMessageSpy).not.toHaveBeenCalled();
  });

  it('drops malformed payloads (missing correlationId)', () => {
    const mod = loadScript();
    mod.onLoad(buildExecutionContext(() => false));

    const { event, postMessageSpy } = buildRequestEvent({
      type: DIRTY_CHECK_REQUEST_TYPE,
    });
    mod._handleMessage(event);
    expect(postMessageSpy).not.toHaveBeenCalled();
  });

  it('drops malformed payloads (null data)', () => {
    const mod = loadScript();
    mod.onLoad(buildExecutionContext(() => false));

    const { event, postMessageSpy } = buildRequestEvent(null);
    mod._handleMessage(event);
    expect(postMessageSpy).not.toHaveBeenCalled();
  });

  it('does not throw if event.source is unavailable (logs only)', () => {
    const mod = loadScript();
    mod.onLoad(buildExecutionContext(() => false));

    const event = new MessageEvent('message', {
      data: { type: DIRTY_CHECK_REQUEST_TYPE, correlationId: 'no-source' },
      origin: 'https://contoso.crm.dynamics.com',
      // source intentionally omitted (jsdom defaults to null)
    });
    expect(() => mod._handleMessage(event)).not.toThrow();
  });

  it('responds dirty=false when no formContext has been cached (graceful fallback)', () => {
    const mod = loadScript();
    // NOTE: intentionally NOT calling onLoad — formContext cache is empty.
    const { event, postMessageSpy } = buildRequestEvent({
      type: DIRTY_CHECK_REQUEST_TYPE,
      correlationId: 'no-ctx',
    });
    mod._handleMessage(event);
    // No formContext → computeDirtyState returns false → respond clean.
    expect(postMessageSpy).toHaveBeenCalledTimes(1);
    expect(postMessageSpy.mock.calls[0][0].dirty).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// End-to-end via real `window.dispatchEvent` (smoke check that the registered
// handler is hooked up correctly, not just `_handleMessage` in isolation).
// ---------------------------------------------------------------------------

describe('end-to-end — window.dispatchEvent flow', () => {
  it('handler installed via onLoad responds to a dispatched MessageEvent', () => {
    const mod = loadScript();
    mod.onLoad(buildExecutionContext(() => true));

    const postMessageSpy = jest.fn();
    const sourceWindow = { postMessage: postMessageSpy };

    const event = new MessageEvent('message', {
      data: { type: DIRTY_CHECK_REQUEST_TYPE, correlationId: 'e2e-1' },
      origin: 'https://contoso.crm.dynamics.com',
      source: sourceWindow as unknown as Window,
    });
    window.dispatchEvent(event);

    expect(postMessageSpy).toHaveBeenCalledTimes(1);
    expect(postMessageSpy.mock.calls[0][0]).toEqual({
      type: DIRTY_CHECK_RESULT_TYPE,
      correlationId: 'e2e-1',
      dirty: true,
    });
  });
});

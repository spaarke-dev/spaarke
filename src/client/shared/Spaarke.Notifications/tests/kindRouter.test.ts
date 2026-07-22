import { KindRouter } from '../src/kindRouter';
import type { NotificationEvent } from '../src/types';

function makeEvent(kind: string, overrides: Partial<NotificationEvent> = {}): { outboxRowId: string; kind: string; envelope?: unknown; source: 'live' | 'poll' } {
  return {
    outboxRowId: overrides.outboxRowId ?? 'row-1',
    kind,
    envelope: overrides.envelope,
    source: overrides.source ?? 'live',
  };
}

describe('KindRouter', () => {
  it('dispatches to the handler registered for the matching kind only', () => {
    const router = new KindRouter();
    const arrivedHandler = jest.fn();
    const assessedHandler = jest.fn();

    router.registerHandler('communication-arrived', arrivedHandler);
    router.registerHandler('communication-assessed', assessedHandler);

    router.dispatch(makeEvent('communication-arrived'));

    expect(arrivedHandler).toHaveBeenCalledTimes(1);
    expect(assessedHandler).not.toHaveBeenCalled();
  });

  it('fires all handlers registered for the same kind', () => {
    const router = new KindRouter();
    const handlerA = jest.fn();
    const handlerB = jest.fn();

    router.registerHandler('suggestion', handlerA);
    router.registerHandler('suggestion', handlerB);

    router.dispatch(makeEvent('suggestion'));

    expect(handlerA).toHaveBeenCalledTimes(1);
    expect(handlerB).toHaveBeenCalledTimes(1);
  });

  it('passes the normalized event (outboxRowId, kind, envelope, source) to the handler', () => {
    const router = new KindRouter();
    const handler = jest.fn();
    router.registerHandler('suggestion', handler);

    const envelope = { suggestionId: 'sug-1' };
    router.dispatch(makeEvent('suggestion', { outboxRowId: 'row-42', envelope, source: 'poll' }));

    expect(handler).toHaveBeenCalledWith({
      outboxRowId: 'row-42',
      kind: 'suggestion',
      envelope,
      source: 'poll',
    });
  });

  it('logs and skips an unrecognized kind — never throws', () => {
    const router = new KindRouter();
    const handler = jest.fn();
    router.registerHandler('suggestion', handler);
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);

    expect(() => router.dispatch(makeEvent('a-totally-novel-kind'))).not.toThrow();

    expect(handler).not.toHaveBeenCalled();
    expect(warnSpy).toHaveBeenCalledTimes(1);
    expect(warnSpy.mock.calls[0][0]).toContain('a-totally-novel-kind');

    warnSpy.mockRestore();
  });

  it('does not throw and does not warn for a RESERVED kind with no registered handler (known but unwired)', () => {
    const router = new KindRouter();
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);

    expect(() => router.dispatch(makeEvent('job-complete'))).not.toThrow();

    expect(warnSpy).not.toHaveBeenCalled();
    warnSpy.mockRestore();
  });

  it('fires a RESERVED kind handler once one is registered (forward-compat: reserved → active)', () => {
    const router = new KindRouter();
    const handler = jest.fn();
    router.registerHandler('share', handler);

    router.dispatch(makeEvent('share'));

    expect(handler).toHaveBeenCalledTimes(1);
  });

  it('does not misfire a handler registered for a different kind when an unrecognized kind arrives', () => {
    const router = new KindRouter();
    const handler = jest.fn();
    router.registerHandler('communication-arrived', handler);
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);

    router.dispatch(makeEvent('system-alert-v2-typo'));

    expect(handler).not.toHaveBeenCalled();
  });

  it('catches a throwing handler and still invokes remaining handlers for the same kind', () => {
    const router = new KindRouter();
    const throwing = jest.fn(() => {
      throw new Error('boom');
    });
    const ok = jest.fn();
    router.registerHandler('suggestion', throwing);
    router.registerHandler('suggestion', ok);
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => undefined);

    expect(() => router.dispatch(makeEvent('suggestion'))).not.toThrow();

    expect(throwing).toHaveBeenCalledTimes(1);
    expect(ok).toHaveBeenCalledTimes(1);
    expect(errorSpy).toHaveBeenCalledTimes(1);

    errorSpy.mockRestore();
  });

  it('unregister function removes only that handler', () => {
    const router = new KindRouter();
    const handlerA = jest.fn();
    const handlerB = jest.fn();
    const unregisterA = router.registerHandler('suggestion', handlerA);
    router.registerHandler('suggestion', handlerB);

    unregisterA();
    router.dispatch(makeEvent('suggestion'));

    expect(handlerA).not.toHaveBeenCalled();
    expect(handlerB).toHaveBeenCalledTimes(1);
  });

  it('clear() removes all handlers for all kinds', () => {
    const router = new KindRouter();
    const handler = jest.fn();
    router.registerHandler('suggestion', handler);

    router.clear();
    router.dispatch(makeEvent('suggestion'));

    expect(handler).not.toHaveBeenCalled();
  });
});

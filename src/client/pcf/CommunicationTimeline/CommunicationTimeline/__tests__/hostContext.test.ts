import { resolveThreadId, resolveThreadIsPrivate, THREAD_PRIVACY_PRIVATE } from '../hostContext';

describe('resolveThreadId', () => {
  it('placed on sprk_communicationthread → returns the host record id', () => {
    const result = resolveThreadId('sprk_communicationthread', 'thread-guid-1', null);
    expect(result).toBe('thread-guid-1');
  });

  it('placed on sprk_communicationthread with no host record id → undefined', () => {
    const result = resolveThreadId('sprk_communicationthread', undefined, null);
    expect(result).toBeUndefined();
  });

  it('placed on sprk_communication with a thread lookup → returns the thread lookup id', () => {
    const result = resolveThreadId('sprk_communication', 'comm-guid-1', 'thread-guid-2');
    expect(result).toBe('thread-guid-2');
  });

  it('placed on sprk_communication with no thread lookup yet → undefined (not null)', () => {
    const result = resolveThreadId('sprk_communication', 'comm-guid-1', null);
    expect(result).toBeUndefined();
  });

  it('placed on an unrecognized entity → undefined', () => {
    const result = resolveThreadId('sprk_matter', 'matter-guid-1', null);
    expect(result).toBeUndefined();
  });

  it('host entity undefined (Xrm frame-walk failed) → undefined', () => {
    const result = resolveThreadId(undefined, undefined, null);
    expect(result).toBeUndefined();
  });
});

describe('resolveThreadIsPrivate (task 068 - "Internal Only" lock)', () => {
  it('thread form that says Private -> true (the compose switch shows on and disabled)', () => {
    expect(resolveThreadIsPrivate('sprk_communicationthread', THREAD_PRIVACY_PRIVATE)).toBe(true);
  });

  it('thread form that says Open, or does not carry the field -> false', () => {
    expect(resolveThreadIsPrivate('sprk_communicationthread', 100000000)).toBe(false);
    expect(resolveThreadIsPrivate('sprk_communicationthread', undefined)).toBe(false);
    expect(resolveThreadIsPrivate('sprk_communicationthread', null)).toBe(false);
  });

  it('sprk_communication placement never locks (the thread privacy is not known there)', () => {
    expect(resolveThreadIsPrivate('sprk_communication', THREAD_PRIVACY_PRIVATE)).toBe(false);
  });

  it('unknown host entity -> false', () => {
    expect(resolveThreadIsPrivate(undefined, THREAD_PRIVACY_PRIVATE)).toBe(false);
  });
});

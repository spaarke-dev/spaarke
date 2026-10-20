import {
  resolveMessageTarget,
  resolveInternalOnlySeed,
  COMMUNICATION_TYPE_MESSAGE,
  THREAD_PRIVACY_PRIVATE,
} from '../hostContext';

describe('resolveMessageTarget', () => {
  it('placed on sprk_communicationthread → mode "thread", threadId = the host record id', () => {
    const result = resolveMessageTarget('sprk_communicationthread', 'thread-guid-1', null, null);
    expect(result).toEqual({ mode: 'thread', threadId: 'thread-guid-1' });
  });

  it('placed on a Message-typed sprk_communication → mode "respond" with threadId + inReplyToMessageId', () => {
    const result = resolveMessageTarget(
      'sprk_communication',
      'comm-guid-1',
      COMMUNICATION_TYPE_MESSAGE,
      'thread-guid-2'
    );
    expect(result).toEqual({ mode: 'respond', threadId: 'thread-guid-2', inReplyToMessageId: 'comm-guid-1' });
  });

  it('placed on a Message-typed sprk_communication with no thread lookup yet → threadId undefined (not null)', () => {
    const result = resolveMessageTarget('sprk_communication', 'comm-guid-1', COMMUNICATION_TYPE_MESSAGE, null);
    expect(result.mode).toBe('respond');
    expect(result.threadId).toBeUndefined();
  });

  it('placed on an Email-typed sprk_communication → mode "unsupported"', () => {
    const result = resolveMessageTarget('sprk_communication', 'comm-guid-1', 100000000, 'thread-guid-3');
    expect(result.mode).toBe('unsupported');
  });

  it('placed on an unrecognized entity → mode "unsupported"', () => {
    const result = resolveMessageTarget('sprk_matter', 'matter-guid-1', null, null);
    expect(result.mode).toBe('unsupported');
  });

  it('host entity undefined (Xrm frame-walk failed) → mode "unsupported"', () => {
    const result = resolveMessageTarget(undefined, undefined, null, null);
    expect(result.mode).toBe('unsupported');
  });
});

describe('resolveInternalOnlySeed (task 068 - "Internal Only" switch seed)', () => {
  it('thread placement on a Private thread form -> threadIsPrivate (switch on + disabled), not started by source', () => {
    expect(resolveInternalOnlySeed('thread', false, THREAD_PRIVACY_PRIVATE)).toEqual({
      threadIsPrivate: true,
      startInternalOnly: false,
    });
  });

  it('thread placement on an Open thread, or a form without the privacy field -> off and unlocked', () => {
    expect(resolveInternalOnlySeed('thread', false, 100000000)).toEqual({
      threadIsPrivate: false,
      startInternalOnly: false,
    });
    expect(resolveInternalOnlySeed('thread', false, undefined)).toEqual({
      threadIsPrivate: false,
      startInternalOnly: false,
    });
  });

  it('respond placement to an internal-only source -> the switch starts on, never locked', () => {
    expect(resolveInternalOnlySeed('respond', true, undefined)).toEqual({
      threadIsPrivate: false,
      startInternalOnly: true,
    });
  });

  it('respond placement to a normal source (or one not loaded yet) -> off', () => {
    expect(resolveInternalOnlySeed('respond', false, undefined).startInternalOnly).toBe(false);
    expect(resolveInternalOnlySeed('respond', null, undefined).startInternalOnly).toBe(false);
  });

  it('respond placement ignores a stray privacy value (the thread is not known there)', () => {
    expect(resolveInternalOnlySeed('respond', false, THREAD_PRIVACY_PRIVATE).threadIsPrivate).toBe(false);
  });

  it('an unsupported placement seeds nothing', () => {
    expect(resolveInternalOnlySeed('unsupported', true, THREAD_PRIVACY_PRIVATE)).toEqual({
      threadIsPrivate: false,
      startInternalOnly: false,
    });
  });
});

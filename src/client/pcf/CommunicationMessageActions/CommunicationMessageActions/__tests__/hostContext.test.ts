import { resolveMessageTarget, COMMUNICATION_TYPE_MESSAGE } from '../hostContext';

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

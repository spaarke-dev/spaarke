import { resolveThreadId } from '../hostContext';

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

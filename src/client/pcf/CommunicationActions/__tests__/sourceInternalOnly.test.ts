/**
 * Task 068 fix round 1 (F1): the source communication's `sprk_isinternalonly` is read three ways. A null / absent
 * flag is UNKNOWN (undefined), never false, so the composer omits `isInternalOnly` and the BFF raises it.
 */
import { readSourceIsInternalOnly } from '../CommunicationActions/sourceInternalOnly';

describe('readSourceIsInternalOnly', () => {
  it('true is known internal', () => expect(readSourceIsInternalOnly(true)).toBe(true));
  it('false is known not internal', () => expect(readSourceIsInternalOnly(false)).toBe(false));
  it('a null flag is unknown (undefined, so the field is omitted)', () => {
    expect(readSourceIsInternalOnly(null)).toBeUndefined();
  });
  it('an absent or non-boolean flag is unknown', () => {
    expect(readSourceIsInternalOnly(undefined)).toBeUndefined();
    expect(readSourceIsInternalOnly('true')).toBeUndefined();
    expect(readSourceIsInternalOnly(1)).toBeUndefined();
  });
});

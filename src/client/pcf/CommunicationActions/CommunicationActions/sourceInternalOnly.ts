/**
 * Three-way read of the source communication's `sprk_isinternalonly` (task 068, fix round 1 F1).
 *
 * `true` and `false` are known. Anything else (null, absent, unreadable) is UNKNOWN and maps to `undefined`,
 * so the composer omits `isInternalOnly` on an untouched reply/forward and the BFF raises it (an explicit
 * `false` is the sender's choice and would win over that raise).
 */
export function readSourceIsInternalOnly(value: unknown): boolean | undefined {
  return value === false ? false : value === true ? true : undefined;
}

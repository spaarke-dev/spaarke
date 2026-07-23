/**
 * Title-resolution tests for the configurable section/modal title (UAT R3 B11-2).
 *
 * The `titleText` manifest property drives both the on-form section header and the
 * review-modal title; when unset it falls back to "RELATED RECORDS". These lock the
 * pure resolver so the default + trim behavior is guaranteed without rendering.
 */

import { resolveTitle, DEFAULT_CONNECTIONS_TITLE } from '../CommunicationConnections/title';

describe('resolveTitle', () => {
  it('defaults to "RELATED RECORDS" when the property is null/undefined', () => {
    expect(resolveTitle(null)).toBe('RELATED RECORDS');
    expect(resolveTitle(undefined)).toBe('RELATED RECORDS');
    expect(DEFAULT_CONNECTIONS_TITLE).toBe('RELATED RECORDS');
  });

  it('defaults when the property is blank or whitespace-only', () => {
    expect(resolveTitle('')).toBe('RELATED RECORDS');
    expect(resolveTitle('   ')).toBe('RELATED RECORDS');
  });

  it('uses the provided title (trimmed) when set', () => {
    expect(resolveTitle('Connections')).toBe('Connections');
    expect(resolveTitle('  Related Matters  ')).toBe('Related Matters');
  });
});

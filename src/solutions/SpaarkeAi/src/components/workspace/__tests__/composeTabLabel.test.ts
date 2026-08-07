/**
 * composeTabLabel.test.ts — spaarkeai-assistant-enhancements-r2 Phase 0 Fix 2.
 *
 * Pure-function unit tests for the Compose tab label derivation: extension
 * stripping, short-prefix truncation with ellipsis, and the full-filename
 * tooltip / no-filename fallback.
 */

import { COMPOSE_TAB_LABEL_MAX_LEN, deriveComposeTabLabel, truncateComposeFileName } from '../composeTabLabel';

describe('truncateComposeFileName', () => {
  it('truncates a long name to the max length + ellipsis, trimming a trailing separator', () => {
    // "Corteva-NDA-August 2022_Signed" — first 8 chars are "Corteva-"; the
    // trailing "-" is trimmed before the ellipsis is appended.
    expect(truncateComposeFileName('Corteva-NDA-August 2022_Signed')).toBe('Corteva…');
  });

  it('returns short names unchanged (no ellipsis when nothing was cut)', () => {
    expect(truncateComposeFileName('NDA')).toBe('NDA');
  });

  it('returns a name exactly at the max length unchanged', () => {
    const exact = 'A'.repeat(COMPOSE_TAB_LABEL_MAX_LEN);
    expect(truncateComposeFileName(exact)).toBe(exact);
  });

  it('respects a custom maxLen', () => {
    expect(truncateComposeFileName('Employment Agreement', 4)).toBe('Empl…');
  });

  it('falls back to a raw slice when the prefix is entirely separator characters', () => {
    expect(truncateComposeFileName('-------- trailing', 4)).toBe('----…');
  });
});

describe('deriveComposeTabLabel', () => {
  it('derives a truncated displayName + full-filename tooltip from a real filename', () => {
    const result = deriveComposeTabLabel('Corteva-NDA-August 2022_Signed.docx');
    expect(result.displayName).toBe('Corteva…');
    expect(result.tooltip).toBe('Corteva-NDA-August 2022_Signed.docx');
  });

  it('strips the extension before truncating', () => {
    const result = deriveComposeTabLabel('NDA.docx');
    // "NDA" (extension-stripped) is under the max length — no ellipsis.
    expect(result.displayName).toBe('NDA');
    expect(result.tooltip).toBe('NDA.docx');
  });

  it('falls back to the default "Compose" label with NO tooltip when there is no filename', () => {
    expect(deriveComposeTabLabel(undefined)).toEqual({ displayName: 'Compose' });
    expect(deriveComposeTabLabel(null)).toEqual({ displayName: 'Compose' });
    expect(deriveComposeTabLabel('   ')).toEqual({ displayName: 'Compose' });
  });

  it('honors a custom fallback label', () => {
    expect(deriveComposeTabLabel(null, 'Workspace')).toEqual({ displayName: 'Workspace' });
  });

  it('does not strip a "." that is not a plausible extension (long / spaced suffix)', () => {
    const result = deriveComposeTabLabel('Smith v. Jones Complaint');
    // No plausible extension ("Jones Complaint" has a space) — the whole
    // string is the truncation input.
    expect(result.tooltip).toBe('Smith v. Jones Complaint');
    expect(result.displayName).toBe(truncateComposeFileName('Smith v. Jones Complaint'));
  });
});

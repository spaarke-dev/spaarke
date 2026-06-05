/**
 * intentMatcher.test.ts — R5 task 036 / P2-CLOSEOUT-05.
 *
 * Table-driven coverage of the pure intent matcher.
 *
 * Acceptance criteria from `tasks/036-...poml`:
 *   - intentMatcher.ts is a pure module with 100% test coverage (table-driven)
 *   - Covers: slash exact, slash with trailing args, keyword patterns
 *     (case-insensitive), button-id match, requires-files satisfied vs not,
 *     no-match cases.
 */

import { matchIntent, IntentMatchers } from '../intentMatcher';

describe('IntentMatchers registry', () => {
  test('contains exactly one matcher entry (summarize-session)', () => {
    expect(IntentMatchers).toHaveLength(1);
    expect(IntentMatchers[0].id).toBe('summarize-session');
    expect(IntentMatchers[0].executor).toBe('summarize-session');
    expect(IntentMatchers[0].requiresFiles).toBe(true);
  });

  test('exposes the slash trigger as canonical lowercase /summarize', () => {
    expect(IntentMatchers[0].slash).toBe('/summarize');
  });

  test('exposes action:summarize as button id', () => {
    expect(IntentMatchers[0].buttonIds).toEqual(['action:summarize']);
  });
});

describe('matchIntent — slash command (with files)', () => {
  const cases: Array<{ name: string; text: string }> = [
    { name: 'exact lowercase', text: '/summarize' },
    { name: 'mixed case', text: '/Summarize' },
    { name: 'upper case', text: '/SUMMARIZE' },
    { name: 'slash + trailing arg', text: '/summarize this' },
    { name: 'slash + multi-word arg', text: '/summarize this document please' },
    { name: 'slash + arg different case', text: '/Summarize THIS' },
    { name: 'slash + tab + arg', text: '/summarize\tthis' },
    { name: 'slash with surrounding whitespace', text: '   /summarize   ' },
  ];

  test.each(cases)('matches "$text" ($name)', ({ text }) => {
    const result = matchIntent(text, /* hasFiles */ true);
    expect(result).not.toBeNull();
    expect(result?.id).toBe('summarize-session');
    expect(result?.via).toBe('slash');
  });
});

describe('matchIntent — slash command rejected (without files)', () => {
  // Per design §3.5 + intentMatcher requiresFiles=true gate: slash WITHOUT
  // files is suppressed; upstream prompt-first interjection handles it.
  test.each([
    '/summarize',
    '/Summarize this',
    '/SUMMARIZE',
  ])('returns null for "%s" when hasFiles=false', (text) => {
    expect(matchIntent(text, false)).toBeNull();
  });
});

describe('matchIntent — slash command negatives', () => {
  // Slash command with random prefix or wrong slash should NOT match.
  test.each([
    'the /summarize',
    'foo/summarize',
    '/summarizer', // /summarize + "r" — does NOT have whitespace separator
    '/summarize-this', // hyphen is NOT whitespace
    '/sum',
    '/summary',
    '/createMatter',
  ])('does not match "%s" via slash', (text) => {
    const result = matchIntent(text, true);
    // Either no match at all, OR match via pattern (which we allow for the
    // "summarizer" → "summari[sz]e\b" word-boundary case below)
    if (result !== null) {
      expect(result.via).not.toBe('slash');
    }
  });
});

describe('matchIntent — keyword patterns (with files)', () => {
  const cases: Array<{ name: string; text: string }> = [
    { name: 'lowercase summarize', text: 'summarize' },
    { name: 'uppercase summarize', text: 'SUMMARIZE this' },
    { name: 'mixed-case summarize', text: 'Summarize my files' },
    { name: 'british summarise', text: 'Summarise this please' },
    { name: 'british lowercase', text: 'summarise these files' },
    { name: 'please summarize', text: 'please summarize' },
    { name: 'Please Summarize', text: 'Please Summarize' },
    { name: 'PLEASE SUMMARISE', text: 'PLEASE SUMMARISE' },
    { name: 'please   summarize (extra space)', text: 'please   summarize' },
  ];

  test.each(cases)('matches "$text" ($name)', ({ text }) => {
    const result = matchIntent(text, true);
    expect(result).not.toBeNull();
    expect(result?.id).toBe('summarize-session');
    expect(result?.via).toBe('pattern');
  });
});

describe('matchIntent — keyword patterns rejected (without files)', () => {
  test.each([
    'summarize',
    'please summarize',
    'Summarise',
  ])('returns null for "%s" when hasFiles=false', (text) => {
    expect(matchIntent(text, false)).toBeNull();
  });
});

describe('matchIntent — keyword pattern negatives', () => {
  // Should NOT match common conversational phrases.
  test.each([
    "Tell me what's in this",
    "What's in the document",
    "Here's a file",
    "Can you read this",
    "tldr",
    "create matter",
    "the summary tab",
    "I want to summarize", // does NOT match — no /summarize prefix, no "please summarize", no start-of-string summarize
  ])('does not match "%s"', (text) => {
    expect(matchIntent(text, true)).toBeNull();
  });
});

describe('matchIntent — button id', () => {
  test('matches action:summarize button id (with files)', () => {
    const result = matchIntent('any text', true, 'action:summarize');
    expect(result).not.toBeNull();
    expect(result?.id).toBe('summarize-session');
    expect(result?.via).toBe('button');
  });

  test('matches action:summarize even with empty message text (button-driven)', () => {
    const result = matchIntent('', true, 'action:summarize');
    expect(result).not.toBeNull();
    expect(result?.via).toBe('button');
  });

  test('does not match unknown button id', () => {
    expect(matchIntent('any text', true, 'action:unknown')).toBeNull();
  });

  test('returns null for action:summarize when hasFiles=false', () => {
    expect(matchIntent('', false, 'action:summarize')).toBeNull();
  });

  test('button-id takes precedence over text — even if text would not match', () => {
    const result = matchIntent('something unrelated', true, 'action:summarize');
    expect(result?.via).toBe('button');
  });
});

describe('matchIntent — precedence rules', () => {
  test('button-id wins over slash text', () => {
    const result = matchIntent('/summarize this', true, 'action:summarize');
    expect(result?.via).toBe('button');
  });

  test('slash wins over plain pattern when slash present', () => {
    const result = matchIntent('/summarize', true);
    expect(result?.via).toBe('slash');
  });

  test('pattern fallback when no slash and no button', () => {
    const result = matchIntent('please summarize', true);
    expect(result?.via).toBe('pattern');
  });
});

describe('matchIntent — empty / whitespace inputs', () => {
  test('empty string returns null', () => {
    expect(matchIntent('', true)).toBeNull();
  });

  test('whitespace-only returns null', () => {
    expect(matchIntent('   \t  ', true)).toBeNull();
  });

  test('empty with button-id still matches', () => {
    expect(matchIntent('', true, 'action:summarize')).not.toBeNull();
  });
});

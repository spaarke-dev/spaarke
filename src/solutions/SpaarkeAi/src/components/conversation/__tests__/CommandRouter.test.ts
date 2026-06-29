/**
 * CommandRouter.test.ts — R6 task 080 / D-D-01.
 *
 * Table-driven coverage of the Pillar 8 closed vocabulary:
 *   - 6 hard slashes  (FR-49)
 *   - 4 soft slashes  (FR-50)
 *   - 3 reference shapes (FR-51)
 *   - composition (FR-52)
 *   - unrecognized-slash passthrough (NFR-11)
 *   - natural-language passthrough  (NFR-11)
 *   - empty input passthrough
 *
 * Purity invariants enforced via shape assertions — the parser MUST be
 * synchronous + pure + side-effect-free.
 *
 * @see CommandRouter.ts
 */

import {
  parse,
  HardSlashes,
  SoftSlashes,
  type Intent,
  type HardSlashCommand,
  type SoftSlashCommand,
} from '../CommandRouter';

// ---------------------------------------------------------------------------
// Vocabulary registry sanity
// ---------------------------------------------------------------------------

describe('CommandRouter vocabulary (Q6 closed)', () => {
  test('exposes exactly 7 hard slashes (Q6 + R7 task 094 /playbooks)', () => {
    expect(HardSlashes).toHaveLength(7);
    expect([...HardSlashes].sort()).toEqual(
      [
        '/clear',
        '/export',
        '/help',
        '/new-session',
        '/pin',
        '/playbooks',
        '/save-to-matter',
      ].sort()
    );
  });

  test('exposes exactly 4 soft slashes', () => {
    expect(SoftSlashes).toHaveLength(4);
    expect([...SoftSlashes].sort()).toEqual(
      ['/analyze', '/draft', '/extract-entities', '/summarize'].sort()
    );
  });
});

// ---------------------------------------------------------------------------
// FR-49: Hard slashes — one test per command (6 tests)
// ---------------------------------------------------------------------------

describe('parse — hard slashes (FR-49)', () => {
  const hardCases: Array<{ command: HardSlashCommand; input: string }> = [
    { command: '/clear', input: '/clear' },
    { command: '/new-session', input: '/new-session' },
    { command: '/help', input: '/help' },
    { command: '/export', input: '/export' },
    { command: '/save-to-matter', input: '/save-to-matter ABC-123' },
    { command: '/pin', input: '/pin' },
    // R7 task 094 / FR-18 — `/playbooks` opens the Playbook Library modal (browse mode).
    { command: '/playbooks', input: '/playbooks' },
  ];

  test.each(hardCases)('classifies "$input" as hard slash $command', ({ command, input }) => {
    const intent = parse(input);
    expect(intent.command).toBe(command);
    expect(intent.isHardSlash).toBe(true);
    expect(intent.isSoftSlash).toBe(false);
    expect(intent.rawText).toBe(input);
  });
});

// ---------------------------------------------------------------------------
// FR-50: Soft slashes — one test per command (4 tests)
// ---------------------------------------------------------------------------

describe('parse — soft slashes (FR-50)', () => {
  const softCases: Array<{ command: SoftSlashCommand; input: string }> = [
    { command: '/summarize', input: '/summarize' },
    { command: '/draft', input: '/draft a response' },
    { command: '/extract-entities', input: '/extract-entities' },
    { command: '/analyze', input: '/analyze the contract' },
  ];

  test.each(softCases)('classifies "$input" as soft slash $command', ({ command, input }) => {
    const intent = parse(input);
    expect(intent.command).toBe(command);
    expect(intent.isSoftSlash).toBe(true);
    expect(intent.isHardSlash).toBe(false);
    expect(intent.rawText).toBe(input);
  });
});

// ---------------------------------------------------------------------------
// Case insensitivity (matches intentMatcher convention)
// ---------------------------------------------------------------------------

describe('parse — case insensitivity', () => {
  test.each([
    ['/Summarize', '/summarize'],
    ['/SUMMARIZE', '/summarize'],
    ['/Clear', '/clear'],
    ['/Save-To-Matter MAT-1', '/save-to-matter'],
    ['/New-Session', '/new-session'],
  ])('"%s" → %s', (input, expected) => {
    const intent = parse(input);
    expect(intent.command).toBe(expected);
  });
});

// ---------------------------------------------------------------------------
// Leading whitespace tolerance
// ---------------------------------------------------------------------------

describe('parse — leading whitespace', () => {
  test('"   /summarize   " is recognized', () => {
    const intent = parse('   /summarize   ');
    expect(intent.command).toBe('/summarize');
    expect(intent.isSoftSlash).toBe(true);
  });
});

// ---------------------------------------------------------------------------
// FR-51: Reference shapes — one test per kind (3 tests)
// ---------------------------------------------------------------------------

describe('parse — references (FR-51)', () => {
  test('extracts #scope as kind=scope', () => {
    const intent = parse('What does #scope cover?');
    expect(intent.references).toHaveLength(1);
    expect(intent.references[0]).toEqual({
      kind: 'scope',
      value: 'scope',
      raw: '#scope',
    });
    // No slash in input → command stays null (NFR-11 passthrough still applies
    // even when references are present).
    expect(intent.command).toBeNull();
  });

  test('extracts @<entity> as kind=entity', () => {
    const intent = parse('Tell me about @opposing-counsel');
    expect(intent.references).toHaveLength(1);
    expect(intent.references[0]).toEqual({
      kind: 'entity',
      value: 'opposing-counsel',
      raw: '@opposing-counsel',
    });
    expect(intent.command).toBeNull();
  });

  test('extracts #<filename> as kind=filename', () => {
    const intent = parse('Look at #engagement-letter.docx');
    expect(intent.references).toHaveLength(1);
    expect(intent.references[0]).toEqual({
      kind: 'filename',
      value: 'engagement-letter.docx',
      raw: '#engagement-letter.docx',
    });
    expect(intent.command).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// FR-52: Composition — command + references
// ---------------------------------------------------------------------------

describe('parse — composition (FR-52)', () => {
  test('"/summarize #engagement-letter.docx" — soft slash + filename ref', () => {
    const intent = parse('/summarize #engagement-letter.docx');
    expect(intent.command).toBe('/summarize');
    expect(intent.isSoftSlash).toBe(true);
    expect(intent.references).toHaveLength(1);
    expect(intent.references[0]).toEqual({
      kind: 'filename',
      value: 'engagement-letter.docx',
      raw: '#engagement-letter.docx',
    });
  });

  test('"/draft response to @opposing-counsel about #motion-to-dismiss" — multi-ref', () => {
    const intent = parse('/draft response to @opposing-counsel about #motion-to-dismiss');
    expect(intent.command).toBe('/draft');
    expect(intent.isSoftSlash).toBe(true);
    expect(intent.references).toHaveLength(2);
    expect(intent.references).toEqual([
      { kind: 'entity', value: 'opposing-counsel', raw: '@opposing-counsel' },
      { kind: 'filename', value: 'motion-to-dismiss', raw: '#motion-to-dismiss' },
    ]);
  });

  test('"/analyze #scope risk on #contract.pdf with @client" — scope + filename + entity', () => {
    const intent = parse('/analyze #scope risk on #contract.pdf with @client');
    expect(intent.command).toBe('/analyze');
    expect(intent.isSoftSlash).toBe(true);
    expect(intent.references).toEqual([
      { kind: 'scope', value: 'scope', raw: '#scope' },
      { kind: 'filename', value: 'contract.pdf', raw: '#contract.pdf' },
      { kind: 'entity', value: 'client', raw: '@client' },
    ]);
  });

  test('"/save-to-matter MAT-123 #notes.md" — hard slash + filename ref', () => {
    const intent = parse('/save-to-matter MAT-123 #notes.md');
    expect(intent.command).toBe('/save-to-matter');
    expect(intent.isHardSlash).toBe(true);
    expect(intent.references).toHaveLength(1);
    expect(intent.references[0].kind).toBe('filename');
    expect(intent.references[0].value).toBe('notes.md');
  });
});

// ---------------------------------------------------------------------------
// NFR-11: Passthrough cases (regression-locking)
// ---------------------------------------------------------------------------

describe('parse — NFR-11 passthrough (binding regression)', () => {
  /**
   * NFR-11 regression test: the explicit `{ command: null, ... }` shape is
   * THE contract that lets the downstream `handleBeforeSendMessage` in
   * ConversationPane.tsx fall through to the existing `CapabilityRouter`
   * natural-language path unchanged.
   *
   * If this assertion ever changes, EVERY existing natural-language send
   * regresses. This test is binding per spec NFR-11.
   */
  test('natural language → command: null (full Intent shape)', () => {
    const intent = parse('summarize this for me');
    expect(intent).toEqual<Intent>({
      command: null,
      references: [],
      rawText: 'summarize this for me',
      isHardSlash: false,
      isSoftSlash: false,
    });
  });

  test('unrecognized slash "/foobar" → command: null (passthrough)', () => {
    const intent = parse('/foobar do something');
    expect(intent.command).toBeNull();
    expect(intent.isHardSlash).toBe(false);
    expect(intent.isSoftSlash).toBe(false);
    expect(intent.references).toEqual([]);
    expect(intent.rawText).toBe('/foobar do something');
  });

  test('unrecognized slash "/unknownCommand" → command: null', () => {
    const intent = parse('/unknownCommand');
    expect(intent.command).toBeNull();
    expect(intent.isHardSlash).toBe(false);
    expect(intent.isSoftSlash).toBe(false);
  });

  test('empty string → command: null, empty references', () => {
    const intent = parse('');
    expect(intent).toEqual<Intent>({
      command: null,
      references: [],
      rawText: '',
      isHardSlash: false,
      isSoftSlash: false,
    });
  });

  test('whitespace-only → command: null, empty references', () => {
    const intent = parse('   \t\n   ');
    expect(intent.command).toBeNull();
    expect(intent.references).toEqual([]);
    expect(intent.isHardSlash).toBe(false);
    expect(intent.isSoftSlash).toBe(false);
  });

  test('natural language WITH reference still has command: null (passthrough preserved)', () => {
    // NFR-11 regression: even when references surface from NL text, the
    // downstream router still falls through because command is null.
    const intent = parse('Tell me what @client thinks about #motion.pdf');
    expect(intent.command).toBeNull();
    expect(intent.isHardSlash).toBe(false);
    expect(intent.isSoftSlash).toBe(false);
    expect(intent.references).toEqual([
      { kind: 'entity', value: 'client', raw: '@client' },
      { kind: 'filename', value: 'motion.pdf', raw: '#motion.pdf' },
    ]);
  });
});

// ---------------------------------------------------------------------------
// Purity invariants (parser must be pure / synchronous)
// ---------------------------------------------------------------------------

describe('parse — purity invariants', () => {
  test('returns synchronously (no Promise)', () => {
    const result = parse('/summarize');
    // If parse() were async this would be a Promise, not an Intent.
    expect(typeof (result as unknown as Promise<unknown>).then).not.toBe('function');
  });

  test('repeated calls yield identical results (deterministic)', () => {
    const input = '/summarize #engagement-letter.docx';
    const a = parse(input);
    const b = parse(input);
    expect(a).toEqual(b);
  });

  test('regex state is reset between calls (no cross-call leakage)', () => {
    // Reference scanning uses a /g regex with shared lastIndex; the parser
    // MUST reset state. This test exercises sequential calls with the same
    // input twice to ensure both calls produce the same reference count.
    const input = 'See #scope and @entity and #file.pdf';
    expect(parse(input).references).toHaveLength(3);
    expect(parse(input).references).toHaveLength(3);
    expect(parse(input).references).toHaveLength(3);
  });

  test('mutually exclusive: command !== null ⇒ exactly one of {isHardSlash, isSoftSlash}', () => {
    for (const s of HardSlashes) {
      const intent = parse(s);
      expect(intent.isHardSlash).toBe(true);
      expect(intent.isSoftSlash).toBe(false);
    }
    for (const s of SoftSlashes) {
      const intent = parse(s);
      expect(intent.isSoftSlash).toBe(true);
      expect(intent.isHardSlash).toBe(false);
    }
  });

  test('command === null ⇒ both isHardSlash and isSoftSlash are false', () => {
    const samples = ['', '   ', 'hello', '/notreal', '#scope alone', '@entity alone'];
    for (const s of samples) {
      const intent = parse(s);
      expect(intent.command).toBeNull();
      expect(intent.isHardSlash).toBe(false);
      expect(intent.isSoftSlash).toBe(false);
    }
  });
});

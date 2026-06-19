/**
 * SoftSlashRouter.test.ts — R6 task 082 / D-D-03.
 *
 * Table-driven coverage of the 4-soft-slash decorate contract:
 *   - mapping table is correct (1:1 with CommandRouter Q6 vocabulary)
 *   - decorateBody adds `commandIntent` ONLY when intent.isSoftSlash === true
 *   - hard slashes, no-slash, unrecognized slash → body UNCHANGED (NFR-11)
 *   - composition with references: payload carries BOTH soft-slash intent
 *     AND reference metadata (FR-52)
 *   - purity: returns NEW object; input body is NEVER mutated
 *
 * @see SoftSlashRouter.ts
 * @see CommandRouter.ts (task 080)
 */

import { parse, type Intent } from '../CommandRouter';
import {
  decorateBody,
  toCommandIntent,
  SoftSlashIntents,
  type CommandIntent,
  type DecoratedChatBody,
} from '../SoftSlashRouter';

// ---------------------------------------------------------------------------
// Vocabulary integrity
// ---------------------------------------------------------------------------

describe('SoftSlashRouter vocabulary (Q6 closed)', () => {
  test('exposes exactly 4 soft slash intents', () => {
    expect(SoftSlashIntents).toHaveLength(4);
    expect([...SoftSlashIntents].sort()).toEqual(
      ['analyze', 'draft', 'extract-entities', 'summarize']
    );
  });
});

// ---------------------------------------------------------------------------
// toCommandIntent — closed-vocabulary mapping
// ---------------------------------------------------------------------------

describe('toCommandIntent — closed vocabulary mapping (FR-50)', () => {
  const cases: Array<{ input: string; expected: CommandIntent }> = [
    { input: '/summarize', expected: 'summarize' },
    { input: '/draft a response', expected: 'draft' },
    { input: '/extract-entities', expected: 'extract-entities' },
    { input: '/analyze the contract', expected: 'analyze' },
  ];

  test.each(cases)(
    '"$input" → commandIntent="$expected"',
    ({ input, expected }) => {
      const intent = parse(input);
      expect(intent.isSoftSlash).toBe(true);
      expect(toCommandIntent(intent)).toBe(expected);
    }
  );

  test('case-insensitive: /SUMMARIZE → summarize', () => {
    const intent = parse('/SUMMARIZE');
    expect(toCommandIntent(intent)).toBe('summarize');
  });
});

describe('toCommandIntent — non-soft-slash inputs return null (NFR-11)', () => {
  test.each([
    ['/clear', 'hard slash'],
    ['/new-session', 'hard slash'],
    ['/help', 'hard slash'],
    ['/export', 'hard slash'],
    ['/save-to-matter MAT-1', 'hard slash'],
    ['/pin', 'hard slash'],
    ['hello world', 'natural language'],
    ['summarize this document', 'natural language equivalent'],
    ['/foobar', 'unrecognized slash'],
    ['', 'empty'],
    ['   ', 'whitespace-only'],
  ])('"%s" (%s) → null', (input) => {
    const intent = parse(input);
    expect(toCommandIntent(intent)).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// decorateBody — adds commandIntent on soft slash
// ---------------------------------------------------------------------------

describe('decorateBody — soft slash adds commandIntent', () => {
  const cases: Array<{
    input: string;
    expectedIntent: CommandIntent;
  }> = [
    { input: '/summarize', expectedIntent: 'summarize' },
    { input: '/draft an email to opposing counsel', expectedIntent: 'draft' },
    { input: '/extract-entities', expectedIntent: 'extract-entities' },
    { input: '/analyze the financial terms', expectedIntent: 'analyze' },
  ];

  test.each(cases)(
    '"$input" → body.commandIntent = "$expectedIntent"',
    ({ input, expectedIntent }) => {
      const intent = parse(input);
      const body: DecoratedChatBody = { message: input, documentId: 'doc-123' };
      const decorated = decorateBody(intent, body);

      expect(decorated.commandIntent).toBe(expectedIntent);
      // Other fields preserved verbatim
      expect(decorated.message).toBe(input);
      expect(decorated.documentId).toBe('doc-123');
    }
  );
});

// ---------------------------------------------------------------------------
// decorateBody — NFR-11 passthrough: hard slash + NL + unrecognized
// ---------------------------------------------------------------------------

describe('decorateBody — non-soft-slash returns body UNCHANGED (NFR-11)', () => {
  test.each([
    ['/clear', 'hard slash /clear'],
    ['/new-session', 'hard slash /new-session'],
    ['/help', 'hard slash /help'],
    ['/save-to-matter ABC-123', 'hard slash with arg'],
    ['hello there', 'natural language'],
    ['Please summarize this document', 'natural language equivalent of /summarize'],
    ['/foobar arg', 'unrecognized slash'],
  ])('"%s" (%s) leaves commandIntent unset', (input) => {
    const intent = parse(input);
    const body: DecoratedChatBody = { message: input, documentId: 'doc-123' };
    const decorated = decorateBody(intent, body);

    expect(decorated.commandIntent).toBeUndefined();
    expect('commandIntent' in decorated).toBe(false);
    expect(decorated.message).toBe(input);
    expect(decorated.documentId).toBe('doc-123');
  });

  test('empty input → body unchanged', () => {
    const intent = parse('');
    const body: DecoratedChatBody = { message: '' };
    const decorated = decorateBody(intent, body);
    expect(decorated.commandIntent).toBeUndefined();
  });
});

// ---------------------------------------------------------------------------
// FR-52 composition — soft slash + references coexist on payload
// ---------------------------------------------------------------------------

describe('decorateBody — composition with references (FR-52)', () => {
  test('"/summarize #engagement-letter.docx" — commandIntent + references both present in intent', () => {
    const input = '/summarize #engagement-letter.docx';
    const intent = parse(input);

    // Sanity: the parsed intent has BOTH the soft-slash command and the file reference
    expect(intent.isSoftSlash).toBe(true);
    expect(intent.command).toBe('/summarize');
    expect(intent.references).toHaveLength(1);
    expect(intent.references[0]).toEqual({
      kind: 'filename',
      value: 'engagement-letter.docx',
      raw: '#engagement-letter.docx',
    });

    // SoftSlashRouter decorates ONLY the commandIntent field. References are
    // resolved separately by task 083 (ReferenceResolver); both decorations
    // compose on the same body without conflict because they target distinct
    // fields.
    const body: DecoratedChatBody = { message: input };
    const decorated = decorateBody(intent, body);
    expect(decorated.commandIntent).toBe('summarize');
    expect(decorated.message).toBe(input);
  });

  test('"/draft response to @opposing-counsel about #motion-to-dismiss" — composition', () => {
    const input = '/draft response to @opposing-counsel about #motion-to-dismiss';
    const intent = parse(input);

    expect(intent.isSoftSlash).toBe(true);
    expect(intent.command).toBe('/draft');
    expect(intent.references).toHaveLength(2);
    expect(intent.references.map(r => r.kind).sort()).toEqual(['entity', 'filename']);

    const body: DecoratedChatBody = { message: input };
    const decorated = decorateBody(intent, body);
    expect(decorated.commandIntent).toBe('draft');
  });

  test('decorated body preserves caller-supplied attachments array', () => {
    const input = '/extract-entities';
    const intent = parse(input);
    const attachments = [{ filename: 'pre-staged.docx', contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', textContent: '...' }];
    const body: DecoratedChatBody = { message: input, attachments };
    const decorated = decorateBody(intent, body);

    expect(decorated.commandIntent).toBe('extract-entities');
    expect(decorated.attachments).toBe(attachments); // reference preserved
  });
});

// ---------------------------------------------------------------------------
// Purity invariants
// ---------------------------------------------------------------------------

describe('decorateBody — purity (immutability of input)', () => {
  test('input body is NOT mutated', () => {
    const intent = parse('/summarize');
    const body: DecoratedChatBody = { message: '/summarize', documentId: 'doc-1' };
    const snapshot = { ...body };

    const decorated = decorateBody(intent, body);

    expect(body).toEqual(snapshot);
    expect('commandIntent' in body).toBe(false);
    expect(decorated).not.toBe(body); // different object reference
    expect(decorated.commandIntent).toBe('summarize');
  });

  test('passthrough path also returns a new object (consistent identity contract)', () => {
    const intent = parse('hello world');
    const body: DecoratedChatBody = { message: 'hello world' };
    const decorated = decorateBody(intent, body);

    expect(decorated).not.toBe(body); // new object even on passthrough
    expect(decorated).toEqual(body); // but contents identical
  });

  test('toCommandIntent is synchronous (returns string | null, not Promise)', () => {
    const intent = parse('/summarize');
    const result = toCommandIntent(intent);
    expect(result).not.toBeInstanceOf(Promise);
    expect(typeof result === 'string' || result === null).toBe(true);
  });

  test('decorateBody is synchronous (returns object, not Promise)', () => {
    const intent = parse('/summarize');
    const result = decorateBody(intent, { message: '/summarize' });
    expect(result).not.toBeInstanceOf(Promise);
    expect(typeof result).toBe('object');
  });
});

// ---------------------------------------------------------------------------
// Defensive: invariant guards
// ---------------------------------------------------------------------------

describe('SoftSlashRouter — defensive invariants', () => {
  test('Intent { command: null } → null commandIntent (NFR-11)', () => {
    const intent: Intent = {
      command: null,
      references: [],
      rawText: '',
      isHardSlash: false,
      isSoftSlash: false,
    };
    expect(toCommandIntent(intent)).toBeNull();
  });

  test('Intent { isSoftSlash: false, command: "/summarize" } (impossible per parse invariants) → null', () => {
    // Defensive: even if a caller constructs a malformed intent, the function
    // honors `isSoftSlash` as the source of truth and refuses to decorate.
    const intent: Intent = {
      command: '/summarize',
      references: [],
      rawText: '/summarize',
      isHardSlash: false,
      isSoftSlash: false, // deliberately mismatched
    };
    expect(toCommandIntent(intent)).toBeNull();
  });
});

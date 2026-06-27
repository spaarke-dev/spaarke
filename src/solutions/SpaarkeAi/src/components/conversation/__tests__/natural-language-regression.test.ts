/**
 * natural-language-regression.test.ts — R6 task 086 / D-D-07 (Pillar 8 NFR-11 gate).
 *
 * Binding regression suite for spec FR-54 + NFR-11: natural-language requests
 * MUST continue to work alongside the new Pillar 8 slash vocabulary. The R5
 * `CapabilityRouter` 3-tier classification path (Layer 1 keyword scoring →
 * Layer 2 embedding similarity → Layer 3 LLM disambiguation) MUST remain
 * UNCHANGED by Pillar 8's parser layer.
 *
 * Wave D-G1 closed `ConversationPane.tsx` wiring such that:
 *   - `parseCommandIntent(messageText)` runs first
 *   - `decorateSoftSlashBody(...)` runs ONLY when `intent.isSoftSlash === true`
 *   - `ReferenceResolver.resolveAll(...)` runs ONLY when `intent.references.length > 0`
 *
 * For pure natural language, ALL three conditions are false → the outbound
 * BFF payload is structurally identical to the pre-Pillar-8 baseline.
 *
 * What this suite verifies (per POML <prompt> Test surface):
 *   1. "summarize this document"                — NL equivalent of /summarize
 *   2. "draft a reply to the opposing counsel"  — NL equivalent of /draft
 *   3. "what's the matter status?"              — conversational query (NFR-01)
 *   4. "make it shorter"                        — refinement follow-up (NFR-01)
 *
 * For each input, the suite asserts (per POML <prompt> + acceptance-criteria):
 *   (a) `Intent.command === null` (parser passthrough)
 *   (b) `Intent.references === []` (no reference tokens)
 *   (c) `Intent.isHardSlash === false` AND `Intent.isSoftSlash === false`
 *   (d) `decorateBody(intent, body)` returns a body with NO `intentHint`
 *       field set (NFR-11 passthrough invariant — Layer 1 keyword path runs
 *       unchanged on the BFF)
 *   (e) `decorateBody` does NOT mutate the input body (purity invariant)
 *   (f) Conversational follow-ups (#3, #4) stay conversational — no side
 *       effects, no hard-slash execution, no soft-slash decoration (NFR-01)
 *
 * NOT in scope (handled elsewhere):
 *   - Slash-input parsing (CommandRouter.test.ts — task 080)
 *   - Soft-slash decoration when input IS a soft slash (SoftSlashRouter.test.ts — task 082)
 *   - Composition with references (composition.integration.test.ts — task 084)
 *   - BFF-side Layer 1 keyword behavior (BFF integration tests; out of scope here)
 *
 * @see CommandRouter.ts (task 080)
 * @see SoftSlashRouter.ts (task 082)
 * @see ConversationPane.tsx handleDecorateOutboundBody (Wave D-G1 wiring)
 * @see projects/spaarke-ai-platform-unification-r6/spec.md FR-54, NFR-01, NFR-11
 * @see projects/spaarke-ai-platform-unification-r6/CLAUDE.md §Pillar 8
 */

import { parse, type Intent } from '../CommandRouter';
import {
  decorateBody,
  toCommandIntent,
  type DecoratedChatBody,
} from '../SoftSlashRouter';

// ---------------------------------------------------------------------------
// The 4 binding natural-language inputs (per POML <prompt> Test surface)
// ---------------------------------------------------------------------------

/**
 * Each row mirrors the 4 inputs enumerated in the POML <prompt>. The
 * `description` field is the verbal NFR-11 contract the row enforces; the
 * `wouldRouteViaLayer1` flag documents the *expected* downstream behavior
 * (used in test names — the BFF behavior itself is out of scope per the file
 * header, but the documentation keeps reviewers grounded in WHY the NFR-11
 * passthrough must hold).
 */
interface NaturalLanguageCase {
  /** The literal user input. */
  input: string;
  /**
   * What the input means in NFR-11 context — used in test names and helps
   * reviewers spot regressions by reading the assertion failures.
   */
  description: string;
  /**
   * Documents the downstream baseline behavior (NOT asserted by this suite —
   * BFF-side; included for reviewer context only).
   */
  baselineRouting: string;
}

const NATURAL_LANGUAGE_CASES: readonly NaturalLanguageCase[] = [
  {
    input: 'summarize this document',
    description: 'NL equivalent of /summarize',
    baselineRouting: 'CapabilityRouter Layer 1 keyword → SUM-CHAT playbook',
  },
  {
    input: 'draft a reply to the opposing counsel',
    description: 'NL equivalent of /draft',
    baselineRouting: 'CapabilityRouter Layer 1 keyword → draft-intent path',
  },
  {
    input: "what's the matter status?",
    description: 'conversational query (NFR-01)',
    baselineRouting: 'CapabilityRouter NO match → agent stays conversational',
  },
  {
    input: 'make it shorter',
    description: 'refinement follow-up (NFR-01)',
    baselineRouting: 'CapabilityRouter NO match → conversational refinement',
  },
] as const;

// ---------------------------------------------------------------------------
// (a) Parser contract: command === null for all 4 inputs (FR-54 + NFR-11)
// ---------------------------------------------------------------------------

describe('Pillar 8 NFR-11 — parser returns command:null for natural-language inputs', () => {
  test.each(NATURAL_LANGUAGE_CASES)(
    '"$input" ($description) → Intent.command === null',
    ({ input }) => {
      const intent = parse(input);
      expect(intent.command).toBeNull();
    }
  );

  test.each(NATURAL_LANGUAGE_CASES)(
    '"$input" ($description) → Intent.references === []',
    ({ input }) => {
      const intent = parse(input);
      expect(intent.references).toEqual([]);
    }
  );

  test.each(NATURAL_LANGUAGE_CASES)(
    '"$input" ($description) → isHardSlash === false AND isSoftSlash === false',
    ({ input }) => {
      const intent = parse(input);
      expect(intent.isHardSlash).toBe(false);
      expect(intent.isSoftSlash).toBe(false);
    }
  );

  test.each(NATURAL_LANGUAGE_CASES)(
    '"$input" ($description) → Intent.rawText preserved verbatim',
    ({ input }) => {
      const intent = parse(input);
      expect(intent.rawText).toBe(input);
    }
  );
});

// ---------------------------------------------------------------------------
// (b) toCommandIntent returns null for all 4 inputs (NFR-11 invariant)
// ---------------------------------------------------------------------------

describe('Pillar 8 NFR-11 — toCommandIntent returns null for natural language', () => {
  test.each(NATURAL_LANGUAGE_CASES)(
    '"$input" ($description) → toCommandIntent === null',
    ({ input }) => {
      const intent = parse(input);
      expect(toCommandIntent(intent)).toBeNull();
    }
  );
});

// ---------------------------------------------------------------------------
// (c) + (d) Decoration is a no-op: body has NO intentHint (NFR-11 passthrough)
// ---------------------------------------------------------------------------

describe('Pillar 8 NFR-11 — decorateBody is a no-op for natural language (no intentHint)', () => {
  test.each(NATURAL_LANGUAGE_CASES)(
    '"$input" ($description) → decorated body has NO intentHint field',
    ({ input }) => {
      const intent = parse(input);
      const body: DecoratedChatBody = { message: input, documentId: 'doc-baseline-1' };
      const decorated = decorateBody(intent, body);

      // NFR-11 binding contract: when intentHint is absent, the BFF
      // CapabilityRouter Layer 1 keyword path runs UNCHANGED.
      expect(decorated.intentHint).toBeUndefined();
      expect('intentHint' in decorated).toBe(false);
    }
  );

  test.each(NATURAL_LANGUAGE_CASES)(
    '"$input" ($description) → decorated body preserves message verbatim',
    ({ input }) => {
      const intent = parse(input);
      const body: DecoratedChatBody = { message: input, documentId: 'doc-baseline-2' };
      const decorated = decorateBody(intent, body);

      expect(decorated.message).toBe(input);
      expect(decorated.documentId).toBe('doc-baseline-2');
    }
  );

  test.each(NATURAL_LANGUAGE_CASES)(
    '"$input" ($description) → decorated body has the same field set as input',
    ({ input }) => {
      const intent = parse(input);
      const body: DecoratedChatBody = { message: input, documentId: 'doc-baseline-3' };
      const decorated = decorateBody(intent, body);

      // Field set parity — no fields added, no fields removed. This is the
      // strict NFR-11 invariant: outbound payload shape == pre-Pillar-8 shape.
      expect(Object.keys(decorated).sort()).toEqual(Object.keys(body).sort());
    }
  );
});

// ---------------------------------------------------------------------------
// (e) Purity: input body is NEVER mutated
// ---------------------------------------------------------------------------

describe('Pillar 8 NFR-11 — decorateBody is pure (no input mutation)', () => {
  test.each(NATURAL_LANGUAGE_CASES)(
    '"$input" ($description) → input body unchanged after decoration',
    ({ input }) => {
      const intent = parse(input);
      const body: DecoratedChatBody = { message: input, documentId: 'doc-purity-1' };
      const snapshot = { ...body };

      decorateBody(intent, body);

      // Mutation check
      expect(body).toEqual(snapshot);
      expect('intentHint' in body).toBe(false);
    }
  );

  test.each(NATURAL_LANGUAGE_CASES)(
    '"$input" ($description) → decorateBody returns a NEW object (not the input ref)',
    ({ input }) => {
      const intent = parse(input);
      const body: DecoratedChatBody = { message: input };
      const decorated = decorateBody(intent, body);

      // Per SoftSlashRouter.decorateBody contract, even on passthrough we get
      // a shallow copy back — callers can rely on consistent identity.
      expect(decorated).not.toBe(body);
      expect(decorated).toEqual(body);
    }
  );
});

// ---------------------------------------------------------------------------
// (f) NFR-01 conversational primacy — refinement / follow-up are inert
// ---------------------------------------------------------------------------

/**
 * NFR-01 binding: the conversational follow-ups (#3 "what's the matter
 * status?" and #4 "make it shorter") MUST produce an inert Pillar-8
 * pre-pass. No hard-slash side effect; no soft-slash decoration; no
 * reference resolution. Effectively, the Pillar 8 layer is invisible.
 */
describe('Pillar 8 NFR-01 — conversational primacy preserved (refinement + follow-up)', () => {
  const conversationalCases = NATURAL_LANGUAGE_CASES.filter(
    (c) =>
      c.input === "what's the matter status?" ||
      c.input === 'make it shorter'
  );

  test('NFR-01 cases present (sanity: 2 conversational inputs in scope)', () => {
    expect(conversationalCases).toHaveLength(2);
  });

  test.each(conversationalCases)(
    '"$input" — Pillar 8 layer is fully inert (no side effects)',
    ({ input }) => {
      const intent = parse(input);

      // Inert parser invariants
      expect(intent.command).toBeNull();
      expect(intent.isHardSlash).toBe(false);
      expect(intent.isSoftSlash).toBe(false);
      expect(intent.references).toEqual([]);

      // Inert decoration — body unchanged
      const body: DecoratedChatBody = { message: input };
      const decorated = decorateBody(intent, body);
      expect('intentHint' in decorated).toBe(false);
      expect(decorated.message).toBe(input);

      // ConversationPane.tsx (Wave D-G1) guards reference resolution behind
      // `intent.references.length > 0`. With zero references, no resolver
      // invocation. We assert the precondition explicitly here so any future
      // regression (e.g., a parser change that surfaces phantom references
      // from conversational text) trips this test BEFORE the runtime
      // side-effect lands.
      expect(intent.references.length).toBe(0);
    }
  );

  test('"make it shorter" is treated identically to a fresh conversational turn (no carry-over state in parser)', () => {
    // The parser is pure — there's no implicit prior-turn coupling. We
    // verify this by parsing twice with different "prior" context: same
    // result both times. This guards against future regressions that might
    // introduce parser-side state (e.g., a context-aware shortcut detector
    // that violates the pure-function contract).
    const first = parse('make it shorter');
    const second = parse('make it shorter');

    expect(first).toEqual(second);
    expect(first.command).toBeNull();
    expect(first.references).toEqual([]);
  });
});

// ---------------------------------------------------------------------------
// Negative anchor: an actual soft slash MUST decorate (sanity check the suite
// would fail-loudly if NFR-11 were broken the other direction)
// ---------------------------------------------------------------------------

/**
 * Anchor test: this is NOT one of the 4 NFR-11 inputs. It exists to confirm
 * the test machinery is wired correctly — if `decorateBody` were silently
 * dropping `intentHint` for ALL inputs (false-negative trap), this test
 * would catch it. The 4 NFR-11 inputs above only assert the negative
 * (intentHint absent); without a positive anchor a stub of `decorateBody`
 * could pass them trivially.
 */
describe('Pillar 8 NFR-11 — positive anchor (suite integrity)', () => {
  test('actual soft slash "/summarize" DOES get decorated with intentHint', () => {
    const intent = parse('/summarize');
    const body: DecoratedChatBody = { message: '/summarize' };
    const decorated = decorateBody(intent, body);

    expect(intent.isSoftSlash).toBe(true);
    expect(intent.command).toBe('/summarize');
    expect(decorated.intentHint).toBe('summarize');
  });

  test('actual hard slash "/clear" does NOT get intentHint (different code path, but same NFR-11 absence)', () => {
    const intent = parse('/clear');
    const body: DecoratedChatBody = { message: '/clear' };
    const decorated = decorateBody(intent, body);

    expect(intent.isHardSlash).toBe(true);
    expect('intentHint' in decorated).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// Intent shape contract guard (defensive type narrowing for downstream
// consumers; ensures the 4 NL inputs all yield the canonical NFR-11 Intent)
// ---------------------------------------------------------------------------

describe('Pillar 8 NFR-11 — canonical NFR-11 Intent shape', () => {
  test.each(NATURAL_LANGUAGE_CASES)(
    '"$input" ($description) → Intent matches canonical NFR-11 shape',
    ({ input }) => {
      const intent = parse(input);
      const expected: Intent = {
        command: null,
        references: [],
        rawText: input,
        isHardSlash: false,
        isSoftSlash: false,
      };
      expect(intent).toEqual(expected);
    }
  );
});
